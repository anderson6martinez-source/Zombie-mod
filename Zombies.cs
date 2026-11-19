// Mod de zombies para Survivalcraft 2.4 normal.
//  - 4 zombis (plantillas del Zombies.xdb): ZombiNormal, ZombiRapido, ZombiTanque, ZombiExplosivo.
//  - Aparecen solos de noche (aparicion natural) y en hordas nocturnas alrededor de los jugadores.
//  - El zombi explosivo explota al llegar a su objetivo (con una mecha corta).
//  - Las hordas desaparecen al amanecer.
// Un solo archivo: se compila en Zombies.dll. No usa OnProjectLoaded: el estado se reinicia en
// InitializeCreatureTypes, que el juego llama una vez por cada mundo que se carga.
using System;
using System.Collections.Generic;
using System.Reflection;
using Engine;
using GameEntitySystem;

namespace Game
{
    public class ZombiesLoader : ModLoader
    {
        // ---- plantillas (Name en el .xdb) ----
        const string Normal = "ZombiNormal";
        const string Rapido = "ZombiRapido";
        const string Tanque = "ZombiTanque";
        const string Explosivo = "ZombiExplosivo";

        // ---- ajustes: cambia estos numeros a tu gusto ----
        const float LuzNoche = 0.30f;          // por debajo de esta luz del cielo es de noche
        const float LuzAmanecer = 0.60f;       // por encima de esta luz las hordas desaparecen
        const double ProbHorda = 0.7;          // probabilidad de que una noche tenga hordas
        const double PrimeraHorda = 20.0;      // segundos tras anochecer hasta la primera horda
        const double HordaMin = 70.0;          // segundos minimos entre hordas
        const double HordaMax = 130.0;         // segundos maximos entre hordas
        const int ZombisHordaMin = 5;
        const int ZombisHordaMax = 10;
        const int MaxZombisHorda = 40;         // tope de zombis de horda vivos a la vez
        const float RadioMin = 22f;            // distancia al jugador a la que aparece la horda
        const float RadioMax = 32f;
        const float AlcanceHorda = 45f;        // desde donde la horda va por el jugador
        const float PresionExplosion = 20f;    // fuerza de la explosion (dinamita ~ 39)
        const float DistanciaMecha = 2.0f;     // a que distancia enciende la mecha
        const double TiempoMecha = 2.0;        // segundos de mecha
        const double PeriodoParpadeo = 0.10;   // segundos entre cada cambio de color (parpadeo de la mecha)

        // pesos de variantes en la horda: normal, rapido, tanque, explosivo
        static readonly string[] Variantes = { Normal, Rapido, Tanque, Explosivo };
        static readonly int[] Pesos = { 50, 25, 15, 10 };

        // aparicion natural (peso, tamano del grupo)
        static readonly float[] PesoNatural = { 0.5f, 0.3f, 0.15f, 0.2f };
        static readonly int[] GrupoNatural = { 3, 2, 1, 1 };

        SubsystemCreatureSpawn creaturas;
        SubsystemTime tiempo;
        SubsystemSky cielo;
        SubsystemPlayers jugadores;
        SubsystemTerrain terreno;
        SubsystemExplosions explosiones;

        readonly System.Random azar = new System.Random();
        readonly HashSet<ComponentCreature> horda = new HashSet<ComponentCreature>();
        readonly Dictionary<ComponentCreature, double> mecha = new Dictionary<ComponentCreature, double>();
        bool eraNoche;
        bool hordaEstaNoche;
        double proximaHorda;
        double proximaRevision;

        static MethodInfo metodoExplosion;
        static bool explosionBuscada;

        // parpadeo: color original de cada zombi con la mecha encendida y el miembro del modelo que se usa
        readonly Dictionary<ComponentCreature, object> colorOriginal = new Dictionary<ComponentCreature, object>();
        static PropertyInfo colorPropiedad;
        static FieldInfo colorCampo;
        static bool colorBuscado;
        static bool parpadeoDesactivado;

        public override void __ModInitialize()
        {
            ModsManager.RegisterHook("InitializeCreatureTypes", this);
            ModsManager.RegisterHook("SubsystemUpdate", this);
        }

        // ---------- mundo nuevo + aparicion natural ----------
        public override void InitializeCreatureTypes(
            SubsystemCreatureSpawn subsystemCreatureSpawn,
            List<SubsystemCreatureSpawn.CreatureType> creatureTypes)
        {
            Project proyecto = subsystemCreatureSpawn.Project;
            creaturas = subsystemCreatureSpawn;
            tiempo = proyecto.FindSubsystem<SubsystemTime>(true);
            cielo = proyecto.FindSubsystem<SubsystemSky>(true);
            jugadores = proyecto.FindSubsystem<SubsystemPlayers>(true);
            terreno = proyecto.FindSubsystem<SubsystemTerrain>(true);
            explosiones = proyecto.FindSubsystem<SubsystemExplosions>(true);
            horda.Clear();
            mecha.Clear();
            colorOriginal.Clear();
            eraNoche = false;
            hordaEstaNoche = false;
            proximaHorda = 0;
            proximaRevision = 0;

            SubsystemTerrain subsystemTerrain = terreno;
            for (int i = 0; i < Variantes.Length; i++)
            {
                string plantilla = Variantes[i];
                float peso = PesoNatural[i];
                int grupo = GrupoNatural[i];

                SubsystemCreatureSpawn.CreatureType type =
                    new SubsystemCreatureSpawn.CreatureType(
                        plantilla, SpawnLocationType.Surface, true, false);

                // solo de noche; suelo solido debajo, 2 bloques de aire y sin agua
                type.SpawnSuitabilityFunction = (SubsystemCreatureSpawn.CreatureType creatureType, Point3 point) =>
                {
                    if (!EsNoche()) return 0f;
                    int below = Terrain.ExtractContents(
                        subsystemTerrain.Terrain.GetCellValueFast(point.X, point.Y - 1, point.Z));
                    int here = Terrain.ExtractContents(
                        subsystemTerrain.Terrain.GetCellValueFast(point.X, point.Y, point.Z));
                    int above = Terrain.ExtractContents(
                        subsystemTerrain.Terrain.GetCellValueFast(point.X, point.Y + 1, point.Z));
                    if (below == WaterBlock.Index || here == WaterBlock.Index || above == WaterBlock.Index) return 0f;
                    if (!BlocksManager.Blocks[below].IsCollidable) return 0f;
                    if (BlocksManager.Blocks[here].IsCollidable || BlocksManager.Blocks[above].IsCollidable) return 0f;
                    return peso;
                };

                type.SpawnFunction = (SubsystemCreatureSpawn.CreatureType creatureType, Point3 point) =>
                {
                    return subsystemCreatureSpawn.SpawnCreatures(creatureType, plantilla, point, grupo).Count;
                };

                creatureTypes.Add(type);
            }
        }

        // ---------- utilidades ----------
        bool EsNoche()
        {
            return cielo != null && cielo.SkyLightIntensity < LuzNoche;
        }

        static bool EstaViva(ComponentCreature c)
        {
            return c != null && c.ComponentHealth != null && c.ComponentHealth.Health > 0f && c.ComponentBody != null;
        }

        static string Plantilla(ComponentCreature c)
        {
            return c.Entity.ValuesDictionary.DatabaseObject.Name;
        }

        string ElegirVariante()
        {
            int total = 0;
            for (int i = 0; i < Pesos.Length; i++) total += Pesos[i];
            int tirada = azar.Next(total);
            for (int i = 0; i < Pesos.Length; i++)
            {
                if (tirada < Pesos[i]) return Variantes[i];
                tirada -= Pesos[i];
            }
            return Normal;
        }

        // la firma de AddExplosion cambia entre versiones (6 o 7 argumentos): se llama por reflexion
        static void Explotar(SubsystemExplosions subsistema, int x, int y, int z, float presion)
        {
            if (!explosionBuscada)
            {
                explosionBuscada = true;
                foreach (MethodInfo m in typeof(SubsystemExplosions).GetMethods())
                {
                    if (m.Name == "AddExplosion" && m.GetParameters().Length >= 6)
                    {
                        metodoExplosion = m;
                        break;
                    }
                }
                if (metodoExplosion == null)
                    Log.Warning("Zombies: SubsystemExplosions.AddExplosion no encontrado en esta version del juego");
            }
            if (metodoExplosion == null) return;
            ParameterInfo[] ps = metodoExplosion.GetParameters();
            object[] args = new object[ps.Length];
            args[0] = x; args[1] = y; args[2] = z; args[3] = presion;
            args[4] = false; // incendiaria
            args[5] = false; // sin sonido
            metodoExplosion.Invoke(subsistema, args);
        }

        // ---------- hordas y explosivos ----------
        public override void SubsystemUpdate(float dt)
        {
            if (creaturas == null || tiempo == null || cielo == null) return;
            double ahora = tiempo.GameTime;
            Parpadear(ahora); // cada frame, para que el parpadeo sea fluido
            if (ahora < proximaRevision) return;
            proximaRevision = ahora + 0.25;

            bool noche = EsNoche();
            if (noche && !eraNoche)
            {
                // empieza la noche: se decide si habra hordas y cuando llega la primera
                hordaEstaNoche = azar.NextDouble() < ProbHorda;
                proximaHorda = ahora + PrimeraHorda;
            }
            else if (!noche && eraNoche)
            {
                hordaEstaNoche = false;
            }
            eraNoche = noche;

            if (noche && hordaEstaNoche && ahora >= proximaHorda)
            {
                proximaHorda = ahora + HordaMin + azar.NextDouble() * (HordaMax - HordaMin);
                LanzarHorda();
            }

            LimpiarHorda();
            RevisarExplosivos(ahora);
        }

        void LanzarHorda()
        {
            if (jugadores == null || terreno == null) return;
            if (horda.Count >= MaxZombisHorda) return;

            List<ComponentPlayer> vivos = new List<ComponentPlayer>();
            foreach (ComponentPlayer p in jugadores.ComponentPlayers)
            {
                if (EstaViva(p)) vivos.Add(p);
            }
            if (vivos.Count == 0) return;

            ComponentPlayer objetivo = vivos[azar.Next(vivos.Count)];
            Vector3 centro = objetivo.ComponentBody.Position;
            int cantidad = azar.Next(ZombisHordaMin, ZombisHordaMax + 1);

            for (int i = 0; i < cantidad && horda.Count < MaxZombisHorda; i++)
            {
                double angulo = azar.NextDouble() * 6.2831853;
                double radio = RadioMin + azar.NextDouble() * (RadioMax - RadioMin);
                int x = (int)Math.Floor(centro.X + Math.Cos(angulo) * radio);
                int z = (int)Math.Floor(centro.Z + Math.Sin(angulo) * radio);
                int y = terreno.Terrain.GetTopHeight(x, z) + 1;

                int suelo = Terrain.ExtractContents(terreno.Terrain.GetCellValueFast(x, y - 1, z));
                int aqui = Terrain.ExtractContents(terreno.Terrain.GetCellValueFast(x, y, z));
                if (suelo == WaterBlock.Index || aqui == WaterBlock.Index) continue;
                if (!BlocksManager.Blocks[suelo].IsCollidable) continue;

                Entity entidad = creaturas.SpawnCreature(ElegirVariante(), new Vector3(x + 0.5f, y, z + 0.5f), false);
                if (entidad == null) continue;

                ComponentCreature criatura = entidad.FindComponent<ComponentCreature>();
                if (criatura == null) continue;
                horda.Add(criatura);

                ComponentChaseBehavior persecucion = entidad.FindComponent<ComponentChaseBehavior>();
                if (persecucion != null)
                    persecucion.Attack(objetivo, AlcanceHorda, 90f, false);
            }
        }

        // quita los zombis de horda muertos y, al amanecer, hace desaparecer a los que queden
        void LimpiarHorda()
        {
            if (horda.Count == 0) return;
            bool amanecer = cielo.SkyLightIntensity > LuzAmanecer;
            List<ComponentCreature> quitar = null;
            foreach (ComponentCreature c in horda)
            {
                if (!EstaViva(c) || amanecer)
                {
                    if (quitar == null) quitar = new List<ComponentCreature>();
                    quitar.Add(c);
                }
            }
            if (quitar == null) return;
            foreach (ComponentCreature c in quitar)
            {
                horda.Remove(c);
                QuitarMecha(c);
                if (amanecer && EstaViva(c))
                    creaturas.Project.RemoveEntity(c.Entity, true);
            }
        }

        // ---------- parpadeo de la mecha ----------
        // El nombre exacto del color del modelo cambia entre versiones: se busca por reflexion
        // (DiffuseColor o Color en ComponentCreatureModel). Si no existe, solo se avisa en el log
        // y el zombi explota igual, sin parpadear.
        static bool TipoColorSoportado(Type tipo)
        {
            Type t = Nullable.GetUnderlyingType(tipo) ?? tipo;
            return t == typeof(Vector3) || t == typeof(Vector4) || t == typeof(Color);
        }

        static void BuscarColor()
        {
            if (colorBuscado) return;
            colorBuscado = true;
            BindingFlags f = BindingFlags.Public | BindingFlags.Instance;
            foreach (string nombre in new string[] { "DiffuseColor", "Color" })
            {
                PropertyInfo p = typeof(ComponentCreatureModel).GetProperty(nombre, f);
                if (p != null && p.CanRead && p.CanWrite && TipoColorSoportado(p.PropertyType))
                {
                    colorPropiedad = p;
                    return;
                }
                FieldInfo fi = typeof(ComponentCreatureModel).GetField(nombre, f);
                if (fi != null && TipoColorSoportado(fi.FieldType))
                {
                    colorCampo = fi;
                    return;
                }
            }
            Log.Warning("Zombies: no se encontro DiffuseColor/Color en ComponentCreatureModel; el parpadeo queda desactivado");
        }

        static object ColorDeParpadeo()
        {
            Type mt = colorPropiedad != null ? colorPropiedad.PropertyType : colorCampo.FieldType;
            Type t = Nullable.GetUnderlyingType(mt) ?? mt;
            if (t == typeof(Vector3)) return new Vector3(1f, 0.2f, 0.2f);
            if (t == typeof(Vector4)) return new Vector4(1f, 0.2f, 0.2f, 1f);
            return new Color((byte)255, (byte)50, (byte)50);
        }

        static object LeerColor(ComponentCreatureModel modelo)
        {
            return colorPropiedad != null ? colorPropiedad.GetValue(modelo, null) : colorCampo.GetValue(modelo);
        }

        static void EscribirColor(ComponentCreatureModel modelo, object valor)
        {
            if (colorPropiedad != null) colorPropiedad.SetValue(modelo, valor, null);
            else colorCampo.SetValue(modelo, valor);
        }

        void PonerColor(ComponentCreature c, bool encendido)
        {
            try
            {
                BuscarColor();
                if (colorPropiedad == null && colorCampo == null) { parpadeoDesactivado = true; return; }
                ComponentCreatureModel modelo = c.Entity.FindComponent<ComponentCreatureModel>();
                if (modelo == null) return;
                if (!colorOriginal.ContainsKey(c)) colorOriginal[c] = LeerColor(modelo);
                EscribirColor(modelo, encendido ? ColorDeParpadeo() : colorOriginal[c]);
            }
            catch (Exception e)
            {
                parpadeoDesactivado = true;
                Log.Warning("Zombies: el parpadeo fallo y se desactiva: " + e.Message);
            }
        }

        void RestaurarColor(ComponentCreature c)
        {
            object original;
            if (!colorOriginal.TryGetValue(c, out original)) return;
            colorOriginal.Remove(c);
            try
            {
                ComponentCreatureModel modelo = c.Entity.FindComponent<ComponentCreatureModel>();
                if (modelo != null) EscribirColor(modelo, original);
            }
            catch (Exception) { }
        }

        void Parpadear(double ahora)
        {
            if (mecha.Count == 0 || parpadeoDesactivado) return;
            foreach (KeyValuePair<ComponentCreature, double> par in mecha)
            {
                if (!EstaViva(par.Key)) continue;
                double transcurrido = TiempoMecha - (par.Value - ahora);
                bool encendido = ((int)Math.Floor(transcurrido / PeriodoParpadeo)) % 2 == 0;
                PonerColor(par.Key, encendido);
            }
        }

        // saca al zombi de la lista de mechas y le devuelve su color normal
        void QuitarMecha(ComponentCreature c)
        {
            mecha.Remove(c);
            RestaurarColor(c);
        }

        void RevisarExplosivos(double ahora)
        {
            List<ComponentCreature> aExplotar = null;
            foreach (ComponentCreature c in creaturas.Creatures)
            {
                if (!EstaViva(c) || Plantilla(c) != Explosivo) continue;

                if (mecha.ContainsKey(c))
                {
                    if (ahora >= mecha[c])
                    {
                        if (aExplotar == null) aExplotar = new List<ComponentCreature>();
                        aExplotar.Add(c);
                    }
                    continue;
                }

                ComponentChaseBehavior persecucion = c.Entity.FindComponent<ComponentChaseBehavior>();
                if (persecucion == null || persecucion.Target == null || !EstaViva(persecucion.Target)) continue;
                float distancia = Vector3.Distance(c.ComponentBody.Position, persecucion.Target.ComponentBody.Position);
                if (distancia <= DistanciaMecha) mecha[c] = ahora + TiempoMecha;
            }

            // los que ya murieron (o desaparecieron) antes de explotar se descartan sin explotar
            if (mecha.Count > 0)
            {
                List<ComponentCreature> huerfanos = null;
                foreach (ComponentCreature c in mecha.Keys)
                {
                    if (!EstaViva(c))
                    {
                        if (huerfanos == null) huerfanos = new List<ComponentCreature>();
                        huerfanos.Add(c);
                    }
                }
                if (huerfanos != null) foreach (ComponentCreature c in huerfanos) QuitarMecha(c);
            }

            if (aExplotar == null) return;
            foreach (ComponentCreature c in aExplotar)
            {
                QuitarMecha(c);
                Vector3 p = c.ComponentBody.Position;
                int x = (int)Math.Floor(p.X);
                int y = (int)Math.Floor(p.Y + 0.5f);
                int z = (int)Math.Floor(p.Z);
                c.ComponentHealth.Health = 0f; // el zombi muere en la explosion
                Explotar(explosiones, x, y, z, PresionExplosion);
            }
        }
    }
}
