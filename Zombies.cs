// Mod de zombies para Survivalcraft 2.4 normal.
//  - 4 zombis (plantillas del Zombies.xdb): ZombiNormal, ZombiRapido, ZombiTanque, ZombiExplosivo.
//  - Aparecen solos de noche (aparicion natural) y en hordas nocturnas alrededor de los jugadores.
//  - El zombi explosivo explota al morir (en el lugar donde muere).
//  - Las hordas desaparecen al amanecer.
//  - Campeones estilo TBOI: algunos zombis nacen grandes y con color (rojo, amarillo, verde, azul, morado, dorado).
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
        const string Griton = "ZombiGriton";
        const string Sombra = "ZombiSombra";
        const string Regenerador = "ZombiRegenerador";

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
        const float PresionExplosion = 7f;     // fuerza de la explosion sobre los bloques (dinamita ~ 39)
        const float DanoExplosion = 7.5f;      // dano fijo del explosivo a cada criatura cercana (jugador incluido)
        const float RadioDano = 5.5f;          // radio del dano en bloques

        // ---- campeones (estilo TBOI) ----
        const double ProbCampeon = 0.10;       // probabilidad total de que un zombi nazca campeon (incluye al dorado)
        const double ProbDorado = 0.02;        // parte de esa probabilidad que es el dorado: 2% de todos los zombis
        const float EscalaCampeon = 1.25f;     // tamano: 1.25 = 125% del tamano normal (2.25 seria 125% mas grande)
        const float VidaRojo = 1.5f;           // rojo: aguanta x1.5 de dano
        const float VelocidadAmarillo = 1.6f;  // amarillo: camina x1.6 mas rapido
        const float VenenoPorSegundo = 0.5f;   // verde: dano de veneno cada segundo
        const double VenenoSegundos = 6.0;
        const float FactorLento = 0.5f;        // azul: el jugador camina a la mitad
        const double LentoSegundos = 5.0;
        const int HijosMorado = 2;             // morado: zombis que suelta al morir
        const float EstadisticasDorado = 2f;   // dorado: vida, dano y velocidad x2
        const int BotinDorado = 4;             // dorado: objetos que suelta al morir (azufre y cobre)
        const float AlcanceGolpe = 4.0f;       // distancia a la que un golpe se atribuye al campeon verde/azul

        const int CRojo = 0, CAmarillo = 1, CVerde = 2, CAzul = 3, CMorado = 4, CDorado = 5;
        static readonly float[][] ColoresCampeon =
        {
            new float[] { 0.75f, 0.10f, 0.10f }, // rojo
            new float[] { 1.00f, 0.90f, 0.20f }, // amarillo
            new float[] { 0.30f, 1.00f, 0.30f }, // verde
            new float[] { 0.30f, 0.50f, 1.00f }, // azul
            new float[] { 0.70f, 0.30f, 0.90f }, // morado
            new float[] { 1.00f, 0.80f, 0.25f }  // dorado
        };
        // objetos del botin dorado: azufre y cobre (nombres de las clases de bloque del juego)
        static readonly string[] NombresBotin = { "SulphurChunkBlock", "CopperOreChunkBlock" };

        // ---- griton: al detectar a su objetivo llama a otros zombis ----
        const int AyudaMin = 3;                // zombis que llama el griton
        const int AyudaMax = 4;
        const float RadioGritoMin = 4f;        // a que distancia del griton aparecen
        const float RadioGritoMax = 8f;

        // ---- sombra: solo aparece en las noches mas oscuras ----
        const float LuzSombra = 0.12f;         // por debajo de esta luz del cielo puede aparecer (menor que LuzNoche)

        // ---- regenerador: se cura solo ----
        const float RegeneracionPorSegundo = 0.03f; // fraccion de vida por segundo (0.03 = llena en ~33 s)
        const double PeriodoCuracion = 0.4;    // segundos entre cada cambio de color rosa mientras se cura

        // pesos de variantes en la horda: normal, rapido, tanque, explosivo, griton, sombra, regenerador
        static readonly string[] Variantes = { Normal, Rapido, Tanque, Explosivo, Griton, Sombra, Regenerador };
        static readonly int[] Pesos = { 42, 22, 12, 8, 6, 5, 5 };

        // aparicion natural (peso, tamano del grupo)
        static readonly float[] PesoNatural = { 0.5f, 0.3f, 0.15f, 0.2f, 0.15f, 0.15f, 0.15f };
        static readonly int[] GrupoNatural = { 3, 2, 1, 1, 1, 1, 1 };

        // zombis que el griton puede llamar (nunca otro griton ni explosivos)
        static readonly string[] Ayudantes = { Normal, Normal, Rapido, Tanque };

        SubsystemCreatureSpawn creaturas;
        SubsystemTime tiempo;
        SubsystemSky cielo;
        SubsystemPlayers jugadores;
        SubsystemTerrain terreno;
        SubsystemExplosions explosiones;
        SubsystemPickables pickables;

        readonly System.Random azar = new System.Random();
        readonly HashSet<ComponentCreature> horda = new HashSet<ComponentCreature>();
        // zombis explosivos vivos y su ultima posicion conocida (para explotar donde mueren)
        readonly Dictionary<ComponentCreature, Vector3> ultimaPos = new Dictionary<ComponentCreature, Vector3>();
        bool eraNoche;
        bool hordaEstaNoche;
        double proximaHorda;
        double proximaRevision;

        static MethodInfo metodoExplosion;
        static bool explosionBuscada;
        static MethodInfo metodoHerir;
        static bool herirBuscado;
        static MethodInfo metodoPickable;
        static bool pickableBuscado;
        static List<int> valoresBotin;

        // campeones
        readonly HashSet<ComponentCreature> vistos = new HashSet<ComponentCreature>();
        readonly Dictionary<ComponentCreature, int> campeones = new Dictionary<ComponentCreature, int>();
        readonly Dictionary<ComponentCreature, Vector3> campeonPos = new Dictionary<ComponentCreature, Vector3>();
        readonly Dictionary<ComponentCreature, float> saludPrevia = new Dictionary<ComponentCreature, float>();
        readonly Dictionary<ComponentCreature, double> venenoHasta = new Dictionary<ComponentCreature, double>();
        readonly Dictionary<ComponentCreature, double> venenoSiguiente = new Dictionary<ComponentCreature, double>();
        readonly Dictionary<ComponentCreature, double> lentoHasta = new Dictionary<ComponentCreature, double>();
        readonly Dictionary<ComponentCreature, float> velocidadOriginal = new Dictionary<ComponentCreature, float>();

        // parpadeo: color original de cada regenerador mientras se cura
        readonly Dictionary<ComponentCreature, object> colorOriginal = new Dictionary<ComponentCreature, object>();

        // gritones que ya gritaron
        readonly HashSet<ComponentCreature> gritaron = new HashSet<ComponentCreature>();
        // regeneradores que se estan curando ahora (parpadean en rosa)
        readonly HashSet<ComponentCreature> curando = new HashSet<ComponentCreature>();
        static PropertyInfo colorPropiedad;
        static FieldInfo colorCampo;
        static bool colorBuscado;
        static bool parpadeoDesactivado;
        static bool tinteDesactivado;

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
            pickables = proyecto.FindSubsystem<SubsystemPickables>(false);
            horda.Clear();
            ultimaPos.Clear();
            colorOriginal.Clear();
            gritaron.Clear();
            curando.Clear();
            vistos.Clear();
            campeones.Clear();
            campeonPos.Clear();
            saludPrevia.Clear();
            venenoHasta.Clear();
            venenoSiguiente.Clear();
            lentoHasta.Clear();
            velocidadOriginal.Clear();
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
                    if (plantilla == Sombra && cielo.SkyLightIntensity >= LuzSombra) return 0f;
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

        // la sombra solo se permite en las noches mas oscuras; si no, sale un zombi normal
        string Permitida(string variante)
        {
            if (variante == Sombra && !(cielo != null && cielo.SkyLightIntensity < LuzSombra)) return Normal;
            return variante;
        }

        string ElegirVariante()
        {
            int total = 0;
            for (int i = 0; i < Pesos.Length; i++) total += Pesos[i];
            int tirada = azar.Next(total);
            for (int i = 0; i < Pesos.Length; i++)
            {
                if (tirada < Pesos[i]) return Permitida(Variantes[i]);
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

        // la firma de ComponentHealth.Injure tambien cambia entre versiones: se llama por reflexion
        void Danar(ComponentCreature objetivo, ComponentCreature atacante, float cantidad)
        {
            if (!herirBuscado)
            {
                herirBuscado = true;
                foreach (MethodInfo m in typeof(ComponentHealth).GetMethods())
                {
                    if (m.Name != "Injure") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length >= 1 && ps[0].ParameterType == typeof(float))
                    {
                        metodoHerir = m;
                        break;
                    }
                }
                if (metodoHerir == null)
                    Log.Warning("Zombies: ComponentHealth.Injure no encontrado; el explosivo no hara dano fijo");
            }
            if (metodoHerir == null) return;
            ParameterInfo[] pa = metodoHerir.GetParameters();
            object[] args = new object[pa.Length];
            for (int i = 0; i < pa.Length; i++)
            {
                Type t = pa[i].ParameterType;
                if (i == 0) args[i] = cantidad;
                else if (t == typeof(string)) args[i] = "Explosion";
                else if (t == typeof(bool)) args[i] = false;
                else if (t.IsAssignableFrom(typeof(ComponentCreature))) args[i] = atacante;
                else if (t.IsValueType) args[i] = Activator.CreateInstance(t);
                else args[i] = null;
            }
            metodoHerir.Invoke(objetivo.ComponentHealth, args);
        }

        // dano fijo a todas las criaturas dentro del radio (menos el propio explosivo)
        void DanarCercanos(Vector3 centro, ComponentCreature explosivo)
        {
            HashSet<ComponentCreature> objetivos = new HashSet<ComponentCreature>();
            foreach (ComponentCreature c in creaturas.Creatures) objetivos.Add(c);
            if (jugadores != null)
            {
                foreach (ComponentPlayer pl in jugadores.ComponentPlayers) objetivos.Add(pl);
            }
            foreach (ComponentCreature c in objetivos)
            {
                if (c == explosivo || !EstaViva(c)) continue;
                Vector3 cuerpo = c.ComponentBody.Position + new Vector3(0f, 0.9f, 0f);
                if (Vector3.Distance(cuerpo, centro) > RadioDano) continue;
                try { Danar(c, explosivo, DanoExplosion); }
                catch (Exception e) { Log.Warning("Zombies: fallo el dano de la explosion: " + e.Message); }
            }
        }

        // ---------- campeones ----------
        static object LeerMiembro(object obj, string nombre)
        {
            if (obj == null) return null;
            try
            {
                Type t = obj.GetType();
                BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                PropertyInfo p = t.GetProperty(nombre, f);
                if (p != null && p.CanRead) return p.GetValue(obj, null);
                FieldInfo fi = t.GetField(nombre, f);
                if (fi != null) return fi.GetValue(obj);
            }
            catch (Exception) { }
            return null;
        }

        static bool EscribirMiembro(object obj, string nombre, object valor)
        {
            if (obj == null) return false;
            try
            {
                Type t = obj.GetType();
                BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                PropertyInfo p = t.GetProperty(nombre, f);
                if (p != null && p.CanWrite) { p.SetValue(obj, valor, null); return true; }
                FieldInfo fi = t.GetField(nombre, f);
                if (fi != null) { fi.SetValue(obj, valor); return true; }
            }
            catch (Exception) { }
            return false;
        }

        static bool EsZombiCampeonable(string plantilla)
        {
            // cualquier zombi del mod (el regenerador tambien: al curarse parpadea en rosa
            // y luego vuelve al color de su campeon)
            for (int i = 0; i < Variantes.Length; i++)
                if (Variantes[i] == plantilla) return true;
            return false;
        }

        void Escalar(ComponentCreature c, float k)
        {
            object caja = LeerMiembro(c.ComponentBody, "BoxSize");
            if (caja is Vector3) EscribirMiembro(c.ComponentBody, "BoxSize", (Vector3)caja * k);

            // el modelo: el nombre del campo de escala cambia entre versiones
            ComponentCreatureModel modelo = c.Entity.FindComponent<ComponentCreatureModel>();
            bool hecho = false;
            if (modelo != null)
            {
                foreach (string nombre in new string[] { "Scale", "ModelScale" })
                {
                    object v = LeerMiembro(modelo, nombre);
                    if (v is Vector3) hecho = EscribirMiembro(modelo, nombre, (Vector3)v * k);
                    else if (v is float) hecho = EscribirMiembro(modelo, nombre, (float)v * k);
                    if (hecho) break;
                }
            }
            if (!hecho) Log.Warning("Zombies: no se encontro la escala del modelo; el campeon solo crece de hitbox");
        }

        static object ConvertirColor(float r, float g, float b)
        {
            Type mt = colorPropiedad != null ? colorPropiedad.PropertyType : colorCampo.FieldType;
            Type t = Nullable.GetUnderlyingType(mt) ?? mt;
            if (t == typeof(Vector3)) return new Vector3(r, g, b);
            if (t == typeof(Vector4)) return new Vector4(r, g, b, 1f);
            return new Color((byte)(r * 255f), (byte)(g * 255f), (byte)(b * 255f));
        }

        // El juego reescribe DiffuseColor en cada cuadro (ComponentCreatureModel.Animate), asi que
        // tenir con colores no se ve. Los campeones usan su textura propia: Textures/Creatures/<Zombi>Campeon<Color>.
        static readonly string[] NombresColor = { "Rojo", "Amarillo", "Verde", "Azul", "Morado", "Dorado" };
        static readonly Dictionary<string, Engine.Graphics.Texture2D> texturasCampeon =
            new Dictionary<string, Engine.Graphics.Texture2D>();

        static Engine.Graphics.Texture2D TexturaCampeon(string plantilla, int tipo)
        {
            string ruta = "Textures/Creatures/" + plantilla + "Campeon" + NombresColor[tipo];
            Engine.Graphics.Texture2D tex;
            if (texturasCampeon.TryGetValue(ruta, out tex)) return tex;
            try { tex = ContentManager.Get<Engine.Graphics.Texture2D>(ruta); }
            catch (Exception e)
            {
                tex = null;
                Log.Warning("Zombies: no se pudo cargar la textura " + ruta + ": " + e.Message);
            }
            texturasCampeon[ruta] = tex;
            return tex;
        }

        void Tenir(ComponentCreature c, int tipo)
        {
            try
            {
                ComponentCreatureModel modelo = c.Entity.FindComponent<ComponentCreatureModel>();
                if (modelo == null) return;
                Engine.Graphics.Texture2D tex = TexturaCampeon(Plantilla(c), tipo);
                if (tex != null && !object.ReferenceEquals(modelo.TextureOverride, tex))
                    modelo.TextureOverride = tex;
            }
            catch (Exception e)
            {
                tinteDesactivado = true;
                Log.Warning("Zombies: no se pudo pintar al campeon: " + e.Message);
            }
        }

        // repinta a los campeones vivos en cada frame (menos los regeneradores que parpadean al curarse)
        void PintarCampeones()
        {
            if (tinteDesactivado || campeones.Count == 0) return;
            foreach (KeyValuePair<ComponentCreature, int> par in campeones)
            {
                ComponentCreature c = par.Key;
                if (!EstaViva(c) || curando.Contains(c)) continue;
                Tenir(c, par.Value);
            }
        }

        void HacerCampeon(ComponentCreature c, int tipo)
        {
            campeones[c] = tipo;
            campeonPos[c] = c.ComponentBody.Position;
            Escalar(c, EscalaCampeon);
            Tenir(c, tipo);

            if (tipo == CRojo)
            {
                object r = LeerMiembro(c.ComponentHealth, "AttackResilience");
                if (r is float) EscribirMiembro(c.ComponentHealth, "AttackResilience", (float)r * VidaRojo);
            }
            else if (tipo == CAmarillo)
            {
                ComponentLocomotion loc = c.Entity.FindComponent<ComponentLocomotion>();
                object v = LeerMiembro(loc, "WalkSpeed");
                if (v is float) EscribirMiembro(loc, "WalkSpeed", (float)v * VelocidadAmarillo);
            }
            else if (tipo == CDorado)
            {
                // estadisticas x2: vida, dano y velocidad
                object r = LeerMiembro(c.ComponentHealth, "AttackResilience");
                if (r is float) EscribirMiembro(c.ComponentHealth, "AttackResilience", (float)r * EstadisticasDorado);

                ComponentMiner minero = c.Entity.FindComponent<ComponentMiner>();
                object d = LeerMiembro(minero, "AttackPower");
                if (d is float) EscribirMiembro(minero, "AttackPower", (float)d * EstadisticasDorado);

                ComponentLocomotion loc = c.Entity.FindComponent<ComponentLocomotion>();
                object v = LeerMiembro(loc, "WalkSpeed");
                if (v is float) EscribirMiembro(loc, "WalkSpeed", (float)v * EstadisticasDorado);
            }
        }

        ComponentPlayer JugadorCercano(Vector3 pos)
        {
            if (jugadores == null) return null;
            ComponentPlayer mejor = null;
            float mejorDist = float.MaxValue;
            foreach (ComponentPlayer pl in jugadores.ComponentPlayers)
            {
                if (!EstaViva(pl)) continue;
                float d = Vector3.Distance(pl.ComponentBody.Position, pos);
                if (d < mejorDist) { mejorDist = d; mejor = pl; }
            }
            return mejor;
        }

        // decide quien es campeon, y cuando uno muere aplica su efecto (morado: hijos, dorado: botin)
        void RevisarCampeones(double ahora)
        {
            HashSet<ComponentCreature> presentes = new HashSet<ComponentCreature>();
            List<ComponentCreature> nuevos = null;
            List<int> tiposNuevos = null;
            foreach (ComponentCreature c in creaturas.Creatures)
            {
                presentes.Add(c);
                if (!EstaViva(c)) continue;
                if (vistos.Add(c) && EsZombiCampeonable(Plantilla(c)))
                {
                    double tirada = azar.NextDouble();
                    if (tirada < ProbCampeon)
                    {
                        // los primeros ProbDorado de la tirada son dorado; el resto reparte entre los otros 5
                        int tipo = tirada < ProbDorado ? CDorado : azar.Next(5);
                        if (nuevos == null) { nuevos = new List<ComponentCreature>(); tiposNuevos = new List<int>(); }
                        nuevos.Add(c);
                        tiposNuevos.Add(tipo);
                    }
                }
                if (campeones.ContainsKey(c)) campeonPos[c] = c.ComponentBody.Position;
            }
            if (nuevos != null)
            {
                for (int i = 0; i < nuevos.Count; i++) HacerCampeon(nuevos[i], tiposNuevos[i]);
            }
            vistos.RemoveWhere(x => !presentes.Contains(x));

            List<ComponentCreature> fin = null;
            foreach (KeyValuePair<ComponentCreature, int> par in campeones)
            {
                ComponentCreature c = par.Key;
                bool murio = c.ComponentHealth != null && c.ComponentHealth.Health <= 0f;
                if (murio || !presentes.Contains(c))
                {
                    if (fin == null) fin = new List<ComponentCreature>();
                    fin.Add(c);
                }
            }
            if (fin == null) return;

            foreach (ComponentCreature c in fin)
            {
                int tipo = campeones[c];
                Vector3 p = campeonPos[c];
                campeones.Remove(c);
                campeonPos.Remove(c);
                bool murio = c.ComponentHealth != null && c.ComponentHealth.Health <= 0f;
                if (!murio) continue; // desaparecio (amanecer): sin efecto
                if (tipo == CMorado) SoltarHijos(p);
                else if (tipo == CDorado) SoltarBotin(p);
            }
        }

        void SoltarHijos(Vector3 centro)
        {
            for (int i = 0; i < HijosMorado; i++)
            {
                Vector3 pos = new Vector3(
                    centro.X + (float)(azar.NextDouble() * 3.0 - 1.5),
                    centro.Y + 0.5f,
                    centro.Z + (float)(azar.NextDouble() * 3.0 - 1.5));
                Entity entidad = creaturas.SpawnCreature(Ayudantes[azar.Next(Ayudantes.Length)], pos, false);
                if (entidad == null) continue;
                ComponentCreature hijo = entidad.FindComponent<ComponentCreature>();
                if (hijo == null) continue;
                vistos.Add(hijo); // los hijos no pueden ser campeones
                horda.Add(hijo);  // desaparecen al amanecer
                ComponentPlayer objetivo = JugadorCercano(pos);
                ComponentChaseBehavior persecucion = entidad.FindComponent<ComponentChaseBehavior>();
                if (objetivo != null && persecucion != null)
                    persecucion.Attack(objetivo, AlcanceHorda, 90f, false);
            }
        }

        static List<int> ValoresBotin()
        {
            if (valoresBotin != null) return valoresBotin;
            valoresBotin = new List<int>();
            foreach (string nombre in NombresBotin)
            {
                foreach (Block b in BlocksManager.Blocks)
                {
                    if (b != null && b.GetType().Name == nombre)
                    {
                        valoresBotin.Add(Terrain.MakeBlockValue(b.BlockIndex));
                        break;
                    }
                }
            }
            foreach (string nombre in NombresBotin)
            {
                bool hay = false;
                foreach (Block b in BlocksManager.Blocks)
                    if (b != null && b.GetType().Name == nombre) { hay = true; break; }
                if (!hay) Log.Warning("Zombies: no existe el bloque " + nombre + " para el botin dorado");
            }
            return valoresBotin;
        }

        void SoltarBotin(Vector3 centro)
        {
            if (pickables == null) return;
            List<int> valores = ValoresBotin();
            if (valores.Count == 0) return;
            for (int i = 0; i < BotinDorado; i++)
            {
                Vector3 pos = new Vector3(
                    centro.X + (float)(azar.NextDouble() - 0.5),
                    centro.Y + 0.8f,
                    centro.Z + (float)(azar.NextDouble() - 0.5));
                SoltarObjeto(valores[azar.Next(valores.Count)], 1, pos);
            }
        }

        // la firma de SubsystemPickables.AddPickable cambia entre versiones: por reflexion
        void SoltarObjeto(int valor, int cantidad, Vector3 pos)
        {
            try
            {
                if (!pickableBuscado)
                {
                    pickableBuscado = true;
                    foreach (MethodInfo m in typeof(SubsystemPickables).GetMethods())
                    {
                        ParameterInfo[] pp = m.GetParameters();
                        if (m.Name == "AddPickable" && pp.Length >= 3 && pp[0].ParameterType == typeof(int))
                        {
                            metodoPickable = m;
                            break;
                        }
                    }
                    if (metodoPickable == null) Log.Warning("Zombies: AddPickable no encontrado; el botin dorado no se suelta");
                }
                if (metodoPickable == null) return;
                ParameterInfo[] ps = metodoPickable.GetParameters();
                object[] args = new object[ps.Length];
                int enteros = 0;
                bool posPuesta = false;
                for (int i = 0; i < ps.Length; i++)
                {
                    Type t = ps[i].ParameterType;
                    if (t == typeof(int)) args[i] = (enteros++ == 0) ? valor : cantidad;
                    else if (t == typeof(Vector3) && !posPuesta) { args[i] = pos; posPuesta = true; }
                    else if (t.IsValueType) args[i] = Activator.CreateInstance(t);
                    else args[i] = null;
                }
                metodoPickable.Invoke(pickables, args);
            }
            catch (Exception e)
            {
                Log.Warning("Zombies: fallo al soltar el botin: " + e.Message);
            }
        }

        // verde y azul: si el jugador pierde vida con uno de ellos cerca, se le aplica el efecto
        void RevisarGolpes(double ahora)
        {
            if (jugadores == null) return;
            foreach (ComponentPlayer pl in jugadores.ComponentPlayers)
            {
                if (!EstaViva(pl)) { saludPrevia.Remove(pl); continue; }
                float vida = pl.ComponentHealth.Health;
                float previa;
                if (saludPrevia.TryGetValue(pl, out previa) && vida < previa - 0.001f && campeones.Count > 0)
                {
                    foreach (KeyValuePair<ComponentCreature, int> par in campeones)
                    {
                        ComponentCreature k = par.Key;
                        if (par.Value != CVerde && par.Value != CAzul) continue;
                        if (!EstaViva(k)) continue;
                        if (Vector3.Distance(k.ComponentBody.Position, pl.ComponentBody.Position) > AlcanceGolpe) continue;

                        if (par.Value == CVerde)
                        {
                            if (!venenoHasta.ContainsKey(pl))
                            {
                                venenoSiguiente[pl] = ahora + 1.0;
                                Mensaje(pl, "¡Te envenenaron! " + (int)VenenoSegundos);
                            }
                            venenoHasta[pl] = ahora + VenenoSegundos;
                        }
                        else
                        {
                            Ralentizar(pl, ahora);
                        }
                    }
                }
                saludPrevia[pl] = vida;
            }
        }

        void Ralentizar(ComponentCreature victima, double ahora)
        {
            if (!lentoHasta.ContainsKey(victima))
            {
                ComponentLocomotion loc = victima.Entity.FindComponent<ComponentLocomotion>();
                object v = LeerMiembro(loc, "WalkSpeed");
                if (!(v is float)) return;
                velocidadOriginal[victima] = (float)v;
                EscribirMiembro(loc, "WalkSpeed", (float)v * FactorLento);
            }
            lentoHasta[victima] = ahora + LentoSegundos;
        }

        void RestaurarVelocidad(ComponentCreature victima)
        {
            float original;
            if (!velocidadOriginal.TryGetValue(victima, out original)) return;
            velocidadOriginal.Remove(victima);
            ComponentLocomotion loc = victima.Entity.FindComponent<ComponentLocomotion>();
            EscribirMiembro(loc, "WalkSpeed", original);
        }

        // mensaje verde en pantalla para un jugador
        void Mensaje(ComponentCreature victima, string texto)
        {
            ComponentPlayer jugador = victima as ComponentPlayer;
            if (jugador == null || jugador.ComponentGui == null) return;
            try
            {
                jugador.ComponentGui.DisplaySmallMessage(texto, new Color((byte)120, (byte)255, (byte)120), false, false);
            }
            catch (Exception e)
            {
                Log.Warning("Zombies: no se pudo mostrar el mensaje: " + e.Message);
            }
        }

        // veneno (dano cada segundo) y fin de la ralentizacion
        void AplicarEfectos(double ahora)
        {
            if (venenoHasta.Count > 0)
            {
                List<ComponentCreature> lista = new List<ComponentCreature>(venenoHasta.Keys);
                foreach (ComponentCreature v in lista)
                {
                    if (!EstaViva(v) || ahora >= venenoHasta[v])
                    {
                        venenoHasta.Remove(v);
                        venenoSiguiente.Remove(v);
                        continue;
                    }
                    if (ahora >= venenoSiguiente[v])
                    {
                        venenoSiguiente[v] = ahora + 1.0;
                        try { Danar(v, null, VenenoPorSegundo); }
                        catch (Exception e) { Log.Warning("Zombies: fallo el veneno: " + e.Message); }
                        saludPrevia[v] = v.ComponentHealth.Health; // el veneno no cuenta como un golpe nuevo
                        if (EstaViva(v))
                            Mensaje(v, "Envenenado: " + (int)Math.Ceiling(venenoHasta[v] - ahora) + " s");
                    }
                }
            }
            if (lentoHasta.Count > 0)
            {
                List<ComponentCreature> lista = new List<ComponentCreature>(lentoHasta.Keys);
                foreach (ComponentCreature v in lista)
                {
                    if (!EstaViva(v) || ahora >= lentoHasta[v])
                    {
                        RestaurarVelocidad(v);
                        lentoHasta.Remove(v);
                    }
                }
            }
        }

        // ---------- hordas y explosivos ----------
        public override void SubsystemUpdate(float dt)
        {
            if (creaturas == null || tiempo == null || cielo == null) return;
            double ahora = tiempo.GameTime;
            Parpadear(ahora); // cada frame, para que el parpadeo sea fluido
            PintarCampeones(); // cada frame: si el juego reinicia el color del modelo, se vuelve a poner
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
            RevisarEspeciales(ahora);
            RevisarCampeones(ahora);
            RevisarGolpes(ahora);
            AplicarEfectos(ahora);
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
                if (amanecer && EstaViva(c))
                {
                    ultimaPos.Remove(c); // desaparece al amanecer: no explota
                    creaturas.Project.RemoveEntity(c.Entity, true);
                }
            }
        }

        // ---------- griton y regenerador ----------
        void RevisarEspeciales(double ahora)
        {
            List<ComponentCreature> gritones = null;
            List<ComponentCreature> objetivos = null;

            foreach (ComponentCreature c in creaturas.Creatures)
            {
                if (!EstaViva(c)) continue;
                string plantilla = Plantilla(c);

                if (plantilla == Regenerador)
                {
                    // se cura un poco en cada revision (cada 0.25 s)
                    float vida = c.ComponentHealth.Health;
                    if (vida < 1f)
                    {
                        c.ComponentHealth.Health = Math.Min(1f, vida + RegeneracionPorSegundo * 0.25f);
                        curando.Add(c);
                    }
                    else if (curando.Remove(c))
                    {
                        RestaurarColor(c); // ya esta llena: vuelve a su color normal
                    }
                    continue;
                }

                if (plantilla != Griton) continue;

                ComponentChaseBehavior persecucion = c.Entity.FindComponent<ComponentChaseBehavior>();
                if (persecucion == null || persecucion.Target == null || !EstaViva(persecucion.Target)) continue;

                // grita una sola vez, cuando detecta a su objetivo
                if (gritaron.Add(c))
                {
                    if (gritones == null) { gritones = new List<ComponentCreature>(); objetivos = new List<ComponentCreature>(); }
                    gritones.Add(c);
                    objetivos.Add(persecucion.Target);
                }
            }

            // se invoca fuera del bucle: aparecer zombis cambia la lista de criaturas
            if (gritones != null)
            {
                for (int i = 0; i < gritones.Count; i++) Gritar(gritones[i], objetivos[i]);
            }

            gritaron.RemoveWhere(g => !EstaViva(g));
            curando.RemoveWhere(g => !EstaViva(g));
        }

        // el griton llama zombis alrededor suyo y los manda contra su objetivo
        void Gritar(ComponentCreature griton, ComponentCreature objetivo)
        {
            if (terreno == null || !EstaViva(griton) || !EstaViva(objetivo)) return;
            Vector3 centro = griton.ComponentBody.Position;
            int cantidad = azar.Next(AyudaMin, AyudaMax + 1);

            for (int i = 0; i < cantidad && horda.Count < MaxZombisHorda; i++)
            {
                double angulo = azar.NextDouble() * 6.2831853;
                double radio = RadioGritoMin + azar.NextDouble() * (RadioGritoMax - RadioGritoMin);
                int x = (int)Math.Floor(centro.X + Math.Cos(angulo) * radio);
                int z = (int)Math.Floor(centro.Z + Math.Sin(angulo) * radio);
                int y = terreno.Terrain.GetTopHeight(x, z) + 1;

                int suelo = Terrain.ExtractContents(terreno.Terrain.GetCellValueFast(x, y - 1, z));
                int aqui = Terrain.ExtractContents(terreno.Terrain.GetCellValueFast(x, y, z));
                if (suelo == WaterBlock.Index || aqui == WaterBlock.Index) continue;
                if (!BlocksManager.Blocks[suelo].IsCollidable) continue;

                string plantilla = Ayudantes[azar.Next(Ayudantes.Length)];
                Entity entidad = creaturas.SpawnCreature(plantilla, new Vector3(x + 0.5f, y, z + 0.5f), false);
                if (entidad == null) continue;

                ComponentCreature criatura = entidad.FindComponent<ComponentCreature>();
                if (criatura == null) continue;
                horda.Add(criatura); // cuenta para el tope y desaparece al amanecer como las hordas

                ComponentChaseBehavior persecucion = entidad.FindComponent<ComponentChaseBehavior>();
                if (persecucion != null)
                    persecucion.Attack(objetivo, AlcanceHorda, 90f, false);
            }
        }

        // ---------- parpadeo del regenerador ----------
        // El nombre exacto del color del modelo cambia entre versiones: se busca por reflexion
        // (DiffuseColor o Color en ComponentCreatureModel). Si no existe, solo se avisa en el log
        // y el regenerador se cura igual, sin parpadear.
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

        static object ColorDeParpadeo(int tipo)
        {
            Type mt = colorPropiedad != null ? colorPropiedad.PropertyType : colorCampo.FieldType;
            Type t = Nullable.GetUnderlyingType(mt) ?? mt;
            if (t == typeof(Vector3)) return tipo == 1 ? new Vector3(1f, 0.45f, 0.7f) : new Vector3(1f, 0.2f, 0.2f);
            if (t == typeof(Vector4)) return tipo == 1 ? new Vector4(1f, 0.45f, 0.7f, 1f) : new Vector4(1f, 0.2f, 0.2f, 1f);
            return tipo == 1 ? new Color((byte)255, (byte)115, (byte)180) : new Color((byte)255, (byte)50, (byte)50);
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

        // El parpadeo usa la textura rosa del regenerador (el color del modelo lo reescribe el juego en cada cuadro)
        static Engine.Graphics.Texture2D texturaRosa;
        static bool rosaBuscada;

        static Engine.Graphics.Texture2D TexturaRosa()
        {
            if (rosaBuscada) return texturaRosa;
            rosaBuscada = true;
            try { texturaRosa = ContentManager.Get<Engine.Graphics.Texture2D>("Textures/Creatures/ZombiRegeneradorRosa"); }
            catch (Exception e)
            {
                texturaRosa = null;
                Log.Warning("Zombies: no se pudo cargar ZombiRegeneradorRosa: " + e.Message);
            }
            return texturaRosa;
        }

        void PonerColor(ComponentCreature c, bool encendido, int tipo)
        {
            try
            {
                Engine.Graphics.Texture2D rosa = TexturaRosa();
                if (rosa == null) { parpadeoDesactivado = true; return; }
                ComponentCreatureModel modelo = c.Entity.FindComponent<ComponentCreatureModel>();
                if (modelo == null) return;
                // se guarda la textura de antes de curarse (la normal o la de su campeon)
                if (!colorOriginal.ContainsKey(c)) colorOriginal[c] = modelo.TextureOverride;
                Engine.Graphics.Texture2D normal = colorOriginal[c] as Engine.Graphics.Texture2D;
                modelo.TextureOverride = encendido ? rosa : normal;
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
                if (modelo != null) modelo.TextureOverride = original as Engine.Graphics.Texture2D;
            }
            catch (Exception) { }
        }

        void Parpadear(double ahora)
        {
            if (parpadeoDesactivado || curando.Count == 0) return;

            // regenerador curandose: parpadeo rosa
            bool rosa = ((int)Math.Floor(ahora / PeriodoCuracion)) % 2 == 0;
            foreach (ComponentCreature c in curando)
            {
                if (EstaViva(c)) PonerColor(c, rosa, 1);
            }
        }

        // El explosivo explota solo al morir. Cada revision se guarda la posicion de los vivos;
        // los que ya no estan vivos y tenian vida 0 explotan en su ultima posicion conocida.
        void RevisarExplosivos(double ahora)
        {
            HashSet<ComponentCreature> presentes = new HashSet<ComponentCreature>();
            foreach (ComponentCreature c in creaturas.Creatures)
            {
                presentes.Add(c);
                if (!EstaViva(c) || Plantilla(c) != Explosivo) continue;
                ultimaPos[c] = c.ComponentBody.Position;
            }

            List<ComponentCreature> muertos = null;
            foreach (KeyValuePair<ComponentCreature, Vector3> par in ultimaPos)
            {
                ComponentCreature c = par.Key;
                bool murio = c.ComponentHealth != null && c.ComponentHealth.Health <= 0f;
                // vivo pero ya no esta en el mundo (desaparecio): se descarta sin explotar
                if (murio || !presentes.Contains(c))
                {
                    if (muertos == null) muertos = new List<ComponentCreature>();
                    muertos.Add(c);
                }
            }
            if (muertos == null) return;

            foreach (ComponentCreature c in muertos)
            {
                Vector3 p = ultimaPos[c];
                ultimaPos.Remove(c);
                bool murio = c.ComponentHealth != null && c.ComponentHealth.Health <= 0f;
                if (!murio) continue;
                DanarCercanos(p + new Vector3(0f, 0.9f, 0f), c);
                Explotar(explosiones, (int)Math.Floor(p.X), (int)Math.Floor(p.Y + 0.5f), (int)Math.Floor(p.Z), PresionExplosion);
            }
        }
    }
}
