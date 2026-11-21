// Mod de zombies para Survivalcraft 2.4 normal.
//  - 4 zombis (plantillas del Zombies.xdb): ZombiNormal, ZombiRapido, ZombiTanque, ZombiExplosivo.
//  - Aparecen solos de noche (aparicion natural) y en hordas nocturnas alrededor de los jugadores.
//  - El zombi explosivo explota al morir (en el lugar donde muere).
//  - Las hordas desaparecen al amanecer.
//  - Campeones estilo TBOI: algunos zombis nacen grandes y con color (rojo, amarillo, verde, azul, morado, dorado,
//    negro: drena stamina | blanco: congela | naranja: quema). Cada color suelta su propio botin.
//  - Luna roja cada 7 dias (hordas x2, mas campeones) y jefe de horda cada 3 dias (barra arriba a la izquierda).
//  - El tanque y el jefe rompen puertas y bloques de madera; el zombi rapido salta obstaculos.
//  - Correr hace ruido y atrae zombis cercanos. Panel de dia y zombis eliminados.
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
        const int CNegro = 6, CBlanco = 7, CNaranja = 8, CJefe = 9;   // negro: drena stamina | blanco: congela | naranja: quema | jefe: jefe de horda
        // tipos que pueden tocar al azar (el dorado tiene su propia tirada)
        static readonly int[] TiposAzar = { CRojo, CAmarillo, CVerde, CAzul, CMorado, CNegro, CBlanco, CNaranja };
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

        // tipo de campeon por criatura (-1 = no es campeon). Lo lee el panel de vida de HealthBars por reflexion.
        static readonly Dictionary<Entity, int> tiposCampeon = new Dictionary<Entity, int>();

        public static int TipoCampeon(Entity e)
        {
            int t;
            if (e == null || !tiposCampeon.TryGetValue(e, out t)) return -1;
            return t;
        }

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
            ModsManager.RegisterHook("GuiDraw", this);
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
            horario = proyecto.FindSubsystem<SubsystemTimeOfDay>(false);
            explosiones = proyecto.FindSubsystem<SubsystemExplosions>(true);
            pickables = proyecto.FindSubsystem<SubsystemPickables>(false);
            horda.Clear();
            ultimaPos.Clear();
            colorOriginal.Clear();
            gritaron.Clear();
            curando.Clear();
            vistos.Clear();
            campeones.Clear();
            tiposCampeon.Clear();
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
            jefes.Clear(); zombisVivos.Clear(); esperaRuptura.Clear(); posSalto.Clear(); huds.Clear();
            muertes = 0; LunaRoja = false; jefeEstaNoche = false; proximoJefe = 0; proximoRuido = 0;

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
        static readonly string[] NombresColor = { "Rojo", "Amarillo", "Verde", "Azul", "Morado", "Dorado", "Negro", "Blanco", "Naranja" };
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
            if (tinteDesactivado) return;
            for (int j = 0; j < jefes.Count; j++) if (EstaViva(jefes[j])) Tenir(jefes[j], CNegro);
            if (campeones.Count == 0) return;
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
            tiposCampeon[c.Entity] = tipo;
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
                    if (tirada < ProbCampeon * (LunaRoja ? MultLunaRoja : 1.0))
                    {
                        // los primeros ProbDorado de la tirada son dorado; el resto reparte entre los otros 5
                        int tipo = tirada < ProbDorado ? CDorado : TiposAzar[azar.Next(TiposAzar.Length)];
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
                tiposCampeon.Remove(c.Entity);
                campeonPos.Remove(c);
                bool murio = c.ComponentHealth != null && c.ComponentHealth.Health <= 0f;
                if (!murio) continue; // desaparecio (amanecer): sin efecto
                if (tipo == CMorado) SoltarHijos(p);
                else if (tipo == CDorado) SoltarBotin(p);
                if (tipo != CDorado) SoltarBotinColor(p, tipo);
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
                        if (!(par.Value == CVerde || par.Value == CAzul || par.Value == CNegro || par.Value == CBlanco || par.Value == CNaranja)) continue;
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
                        else if (par.Value == CAzul)
                        {
                            Ralentizar(pl, ahora);
                        }
                        else if (par.Value == CBlanco)
                        {
                            Ralentizar(pl, ahora, FactorCongelado, CongeladoSegundos);
                            Mensaje(pl, "¡Congelado!");
                        }
                        else if (par.Value == CNegro)
                        {
                            DrenarStamina(pl);
                        }
                        else if (par.Value == CNaranja)
                        {
                            Quemar(pl, k);
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

        // ======================================================================
        //  NOCHES ESPECIALES: luna roja, jefe de horda, rompe-bloques, saltarin,
        //  ruido, botin por color y panel de oleadas
        // ======================================================================

        // ---- ajustes de las novedades ----
        const int DiasLunaRoja = 7;            // cada cuantos dias hay luna roja
        const double MultLunaRoja = 2.5;       // luna roja: probabilidad de campeon x2.5 y hordas x2
        const int DiasJefe = 3;                // cada cuantos dias sale un jefe de horda
        const double RetrasoJefe = 25.0;       // segundos tras anochecer hasta que sale el jefe
        const float EscalaJefe = 2.2f;         // tamano del jefe
        const float VidaJefe = 10f;            // el jefe aguanta x10
        const float DanoJefe = 2f;             // el jefe pega x2
        const int BotinJefe = 8;               // objetos que suelta el jefe
        const float FactorCongelado = 0.25f;   // blanco: el jugador camina a un cuarto
        const double CongeladoSegundos = 4.0;
        const float DrenajeStamina = 0.25f;    // negro: stamina que quita cada golpe (0.25 = 25%)
        const double FuegoSegundos = 4.0;      // naranja: segundos en llamas
        const float RupturaDistancia = 12f;    // el tanque rompe bloques si su objetivo esta a menos de esto
        const double RupturaEspera = 2.0;      // segundos entre cada bloque roto (el jefe: la mitad)
        const float VelocidadRuido = 3.0f;     // velocidad del jugador a partir de la cual hace ruido (correr)
        const float RadioRuido = 20f;          // zombis a esta distancia se alertan
        const int MaxAlertados = 5;            // zombis alertados por cada pulso de ruido
        const double ProbBotinRaro = 0.10;     // probabilidad de que un campeon suelte tambien un diamante

        public static bool LunaRoja;           // la lee el resto del mod (campeones y hordas)

        SubsystemTimeOfDay horario;
        int diaNoche;
        bool jefeEstaNoche;
        double proximoJefe;
        double proximoRuido;
        int muertes;
        readonly List<ComponentCreature> jefes = new List<ComponentCreature>();
        readonly HashSet<ComponentCreature> zombisVivos = new HashSet<ComponentCreature>();
        readonly Dictionary<ComponentCreature, double> esperaRuptura = new Dictionary<ComponentCreature, double>();
        readonly Dictionary<ComponentCreature, Vector3> posSalto = new Dictionary<ComponentCreature, Vector3>();
        static bool rupturaDesactivada;
        static MethodInfo metodoDestruir;
        static bool destruirBuscado;
        static MethodInfo metodoFuego;
        static bool fuegoBuscado;

        // botin por color: nombres de clases de bloque del juego (los que no existan se ignoran)
        static readonly string[][] BotinColor =
        {
            new string[] { "RottenMeatBlock" },                       // rojo
            new string[] { "BoneBlock", "BonePieceBlock" },           // amarillo
            new string[] { "RottenMeatBlock", "SaltpeterChunkBlock" },// verde
            new string[] { "SaltpeterChunkBlock" },                   // azul
            new string[] { "CoalChunkBlock" },                        // morado
            new string[] { "SulphurChunkBlock" },                     // dorado (ya tiene su propio botin)
            new string[] { "CoalChunkBlock" },                        // negro
            new string[] { "BoneBlock", "BonePieceBlock" },           // blanco
            new string[] { "SulphurChunkBlock" }                      // naranja
        };
        static readonly string[] BotinJefeNombres =
            { "DiamondChunkBlock", "SulphurChunkBlock", "CopperOreChunkBlock", "IronOreChunkBlock", "CoalChunkBlock", "GermaniumOreChunkBlock" };
        static readonly Dictionary<string, int> valoresPorNombre = new Dictionary<string, int>();

        static int ValorBloque(string nombre)
        {
            int valor;
            if (valoresPorNombre.TryGetValue(nombre, out valor)) return valor;
            valor = -1;
            foreach (Block b in BlocksManager.Blocks)
            {
                if (b != null && b.GetType().Name == nombre) { valor = Terrain.MakeBlockValue(b.BlockIndex); break; }
            }
            valoresPorNombre[nombre] = valor;
            return valor;
        }

        int DiaActual()
        {
            object d = LeerMiembro(horario, "Day");
            return d is int ? (int)d : 0;
        }

        void MensajeATodos(string texto, Color color)
        {
            if (jugadores == null) return;
            foreach (ComponentPlayer pl in jugadores.ComponentPlayers)
            {
                try
                {
                    if (pl.ComponentGui != null) pl.ComponentGui.DisplayLargeMessage(texto, "", 4f, 0f);
                }
                catch (Exception)
                {
                    try { pl.ComponentGui.DisplaySmallMessage(texto, color, true, false); } catch (Exception) { }
                }
            }
        }

        // ---------- calendario: luna roja y jefe ----------
        void NocheEmpieza(double ahora)
        {
            diaNoche = DiaActual();
            LunaRoja = diaNoche > 0 && diaNoche % DiasLunaRoja == 0;
            if (LunaRoja)
            {
                hordaEstaNoche = true; // la luna roja siempre trae hordas
                proximaHorda = ahora + 8.0;
                MensajeATodos("¡LUNA ROJA!", new Color((byte)255, (byte)60, (byte)60));
            }
            jefeEstaNoche = diaNoche > 0 && diaNoche % DiasJefe == 0;
            proximoJefe = ahora + RetrasoJefe;
        }

        void NocheTermina()
        {
            LunaRoja = false;
            jefeEstaNoche = false;
        }

        void RevisarJefe(double ahora)
        {
            if (jefeEstaNoche && ahora >= proximoJefe && EsNoche())
            {
                jefeEstaNoche = false;
                LanzarJefe();
            }

            // jefes que murieron (botin) o desaparecieron (amanecer)
            if (jefes.Count == 0) return;
            HashSet<ComponentCreature> presentes = new HashSet<ComponentCreature>();
            foreach (ComponentCreature c in creaturas.Creatures) presentes.Add(c);
            for (int i = jefes.Count - 1; i >= 0; i--)
            {
                ComponentCreature j = jefes[i];
                bool presente = presentes.Contains(j);
                bool murio = j.ComponentHealth != null && j.ComponentHealth.Health <= 0f;
                if (presente && !murio) continue;
                jefes.RemoveAt(i);
                tiposCampeon.Remove(j.Entity);
                if (presente && murio && j.ComponentBody != null)
                {
                    MensajeATodos("¡Jefe de horda derrotado!", new Color((byte)120, (byte)255, (byte)120));
                    SoltarBotinJefe(j.ComponentBody.Position);
                }
            }
        }

        void LanzarJefe()
        {
            if (jugadores == null || terreno == null) return;
            List<ComponentPlayer> vivos = new List<ComponentPlayer>();
            foreach (ComponentPlayer p in jugadores.ComponentPlayers) if (EstaViva(p)) vivos.Add(p);
            if (vivos.Count == 0) return;
            ComponentPlayer objetivo = vivos[azar.Next(vivos.Count)];
            Vector3 centro = objetivo.ComponentBody.Position;

            for (int intento = 0; intento < 12; intento++)
            {
                double angulo = azar.NextDouble() * 6.2831853;
                int x = (int)Math.Floor(centro.X + Math.Cos(angulo) * 26.0);
                int z = (int)Math.Floor(centro.Z + Math.Sin(angulo) * 26.0);
                int y = terreno.Terrain.GetTopHeight(x, z) + 1;
                int suelo = Terrain.ExtractContents(terreno.Terrain.GetCellValueFast(x, y - 1, z));
                int aqui = Terrain.ExtractContents(terreno.Terrain.GetCellValueFast(x, y, z));
                if (suelo == WaterBlock.Index || aqui == WaterBlock.Index) continue;
                if (!BlocksManager.Blocks[suelo].IsCollidable) continue;

                Entity entidad = creaturas.SpawnCreature(Tanque, new Vector3(x + 0.5f, y, z + 0.5f), false);
                if (entidad == null) continue;
                ComponentCreature jefe = entidad.FindComponent<ComponentCreature>();
                if (jefe == null) continue;

                vistos.Add(jefe);          // el jefe no puede ser ademas campeon
                horda.Add(jefe);           // desaparece al amanecer
                jefes.Add(jefe);
                tiposCampeon[jefe.Entity] = CJefe;
                Escalar(jefe, EscalaJefe);
                Tenir(jefe, CNegro);

                object r = LeerMiembro(jefe.ComponentHealth, "AttackResilience");
                if (r is float) EscribirMiembro(jefe.ComponentHealth, "AttackResilience", (float)r * VidaJefe);
                ComponentMiner minero = jefe.Entity.FindComponent<ComponentMiner>();
                object d = LeerMiembro(minero, "AttackPower");
                if (d is float) EscribirMiembro(minero, "AttackPower", (float)d * DanoJefe);

                ComponentChaseBehavior persecucion = entidad.FindComponent<ComponentChaseBehavior>();
                if (persecucion != null) persecucion.Attack(objetivo, AlcanceHorda, 240f, true);
                MensajeATodos("¡Un jefe de horda se acerca!", new Color((byte)255, (byte)160, (byte)60));
                return;
            }
        }

        void SoltarBotinJefe(Vector3 centro)
        {
            if (pickables == null) return;
            List<int> valores = new List<int>();
            foreach (string n in BotinJefeNombres) { int v = ValorBloque(n); if (v >= 0) valores.Add(v); }
            if (valores.Count == 0) return;
            for (int i = 0; i < BotinJefe; i++)
            {
                Vector3 pos = new Vector3(centro.X + (float)(azar.NextDouble() * 2.0 - 1.0), centro.Y + 1f,
                    centro.Z + (float)(azar.NextDouble() * 2.0 - 1.0));
                SoltarObjeto(valores[azar.Next(valores.Count)], 1, pos);
            }
        }

        // botin segun el color del campeon (+ un diamante raro de vez en cuando)
        void SoltarBotinColor(Vector3 centro, int tipo)
        {
            if (pickables == null || tipo < 0 || tipo >= BotinColor.Length) return;
            foreach (string nombre in BotinColor[tipo])
            {
                int valor = ValorBloque(nombre);
                if (valor < 0) continue;
                int cantidad = 1 + azar.Next(2);
                for (int i = 0; i < cantidad; i++)
                    SoltarObjeto(valor, 1, new Vector3(centro.X + (float)(azar.NextDouble() - 0.5), centro.Y + 0.8f,
                        centro.Z + (float)(azar.NextDouble() - 0.5)));
                break; // con un tipo de objeto por campeon basta
            }
            if (azar.NextDouble() < ProbBotinRaro)
            {
                int diamante = ValorBloque("DiamondChunkBlock");
                if (diamante >= 0) SoltarObjeto(diamante, 1, new Vector3(centro.X, centro.Y + 1f, centro.Z));
            }
        }

        // ---------- efectos de los campeones nuevos ----------
        void DrenarStamina(ComponentPlayer pl)
        {
            ComponentVitalStats vital = pl.ComponentVitalStats;
            if (vital == null) return;
            vital.Stamina = MathUtils.Max(0f, vital.Stamina - DrenajeStamina);
            Mensaje(pl, "¡Te drenaron la stamina!");
        }

        void Quemar(ComponentCreature victima, ComponentCreature atacante)
        {
            try
            {
                ComponentOnFire fuego = victima.Entity.FindComponent<ComponentOnFire>();
                if (fuego != null)
                {
                    if (!fuegoBuscado)
                    {
                        fuegoBuscado = true;
                        foreach (MethodInfo m in typeof(ComponentOnFire).GetMethods())
                        {
                            if (m.Name != "SetOnFire") continue;
                            metodoFuego = m;
                            break;
                        }
                    }
                    if (metodoFuego != null)
                    {
                        ParameterInfo[] ps = metodoFuego.GetParameters();
                        object[] args = new object[ps.Length];
                        for (int i = 0; i < ps.Length; i++)
                        {
                            Type t = ps[i].ParameterType;
                            if (t == typeof(float)) args[i] = (float)FuegoSegundos;
                            else if (t.IsAssignableFrom(typeof(ComponentCreature))) args[i] = atacante;
                            else if (t.IsValueType) args[i] = Activator.CreateInstance(t);
                            else args[i] = null;
                        }
                        metodoFuego.Invoke(fuego, args);
                        Mensaje(victima, "¡Te prendieron fuego!");
                        return;
                    }
                }
                Danar(victima, atacante, 1f); // sin fuego en esta version: dano directo
            }
            catch (Exception e)
            {
                Log.Warning("Zombies: fallo el fuego del campeon naranja: " + e.Message);
            }
        }

        void Ralentizar(ComponentCreature victima, double ahora, float factor, double segundos)
        {
            if (!lentoHasta.ContainsKey(victima))
            {
                ComponentLocomotion loc = victima.Entity.FindComponent<ComponentLocomotion>();
                object v = LeerMiembro(loc, "WalkSpeed");
                if (!(v is float)) return;
                velocidadOriginal[victima] = (float)v;
                EscribirMiembro(loc, "WalkSpeed", (float)v * factor);
            }
            lentoHasta[victima] = ahora + segundos;
        }

        // ---------- tanque y jefe: rompen puertas y bloques de madera ----------
        static bool Rompible(Block b)
        {
            if (b == null) return false;
            string n = b.GetType().Name.ToLowerInvariant();
            if (n.Contains("iron") || n.Contains("steel")) return false;
            return n.Contains("door") || n.Contains("plank") || n.Contains("fence") || n.Contains("wood") || n.Contains("hatch");
        }

        bool DestruirBloque(int x, int y, int z)
        {
            if (!destruirBuscado)
            {
                destruirBuscado = true;
                foreach (MethodInfo m in typeof(SubsystemTerrain).GetMethods())
                {
                    if (m.Name != "DestroyCell") continue;
                    int enteros = 0;
                    foreach (ParameterInfo p in m.GetParameters()) if (p.ParameterType == typeof(int)) enteros++;
                    if (enteros >= 5) { metodoDestruir = m; break; }
                }
                if (metodoDestruir == null) Log.Warning("Zombies: DestroyCell no encontrado; el tanque no rompera bloques");
            }
            if (metodoDestruir == null) return false;
            ParameterInfo[] ps = metodoDestruir.GetParameters();
            object[] args = new object[ps.Length];
            int k = 0;
            for (int i = 0; i < ps.Length; i++)
            {
                Type t = ps[i].ParameterType;
                if (t == typeof(int))
                {
                    // orden de los enteros: nivel de herramienta, x, y, z, valor nuevo
                    int[] orden = { 0, x, y, z, 0 };
                    args[i] = k < orden.Length ? orden[k] : 0;
                    k++;
                }
                else if (t == typeof(bool)) args[i] = false;
                else if (t.IsValueType) args[i] = Activator.CreateInstance(t);
                else args[i] = null;
            }
            metodoDestruir.Invoke(terreno, args);
            return true;
        }

        void RevisarRompedores(double ahora)
        {
            if (rupturaDesactivada || terreno == null) return;
            try
            {
                foreach (ComponentCreature c in creaturas.Creatures)
                {
                    if (!EstaViva(c)) continue;
                    bool esJefe = jefes.Contains(c);
                    if (!esJefe && Plantilla(c) != Tanque) continue;

                    ComponentChaseBehavior persecucion = c.Entity.FindComponent<ComponentChaseBehavior>();
                    ComponentCreature objetivo = LeerMiembro(persecucion, "Target") as ComponentCreature;
                    if (objetivo == null || !EstaViva(objetivo)) continue;

                    double espera;
                    if (esperaRuptura.TryGetValue(c, out espera) && ahora < espera) continue;

                    Vector3 p = c.ComponentBody.Position;
                    Vector3 d = objetivo.ComponentBody.Position - p;
                    d.Y = 0f;
                    float dist = d.Length();
                    if (dist < 1.2f || dist > RupturaDistancia) continue;
                    d = d / dist;

                    float alcance = c.ComponentBody.BoxSize.X * 0.5f + 0.6f;
                    int x = (int)Math.Floor(p.X + d.X * alcance);
                    int z = (int)Math.Floor(p.Z + d.Z * alcance);
                    int y0 = (int)Math.Floor(p.Y + 0.1f);
                    for (int h = 0; h < 2; h++)
                    {
                        int contenido = Terrain.ExtractContents(terreno.Terrain.GetCellValueFast(x, y0 + h, z));
                        if (contenido == 0) continue;
                        if (!Rompible(BlocksManager.Blocks[contenido])) continue;
                        if (DestruirBloque(x, y0 + h, z))
                            esperaRuptura[c] = ahora + (esJefe ? RupturaEspera * 0.5 : RupturaEspera);
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                rupturaDesactivada = true;
                Log.Warning("Zombies: rompe-bloques desactivado por error: " + e);
            }
        }

        // ---------- zombi rapido: salta los obstaculos cuando se atasca ----------
        void RevisarSaltadores(double ahora)
        {
            foreach (ComponentCreature c in creaturas.Creatures)
            {
                if (!EstaViva(c) || Plantilla(c) != Rapido) continue;
                Vector3 p = c.ComponentBody.Position;
                Vector3 antes;
                bool tenia = posSalto.TryGetValue(c, out antes);
                posSalto[c] = p;
                if (!tenia) continue;

                ComponentChaseBehavior persecucion = c.Entity.FindComponent<ComponentChaseBehavior>();
                ComponentCreature objetivo = LeerMiembro(persecucion, "Target") as ComponentCreature;
                if (objetivo == null || !EstaViva(objetivo)) continue;

                Vector3 hacia = objetivo.ComponentBody.Position - p;
                hacia.Y = 0f;
                float dist = hacia.Length();
                if (dist < 2.5f) continue;
                Vector3 mov = p - antes;
                mov.Y = 0f;
                if (mov.Length() > 0.15f) continue;             // avanza bien: no hace falta saltar
                if (!c.ComponentBody.StandingOnValue.HasValue) continue; // ya esta en el aire

                hacia = hacia / dist;
                ComponentLocomotion loc = c.Entity.FindComponent<ComponentLocomotion>();
                EscribirMiembro(loc, "JumpOrder", 1f);
                object vel = LeerMiembro(c.ComponentBody, "Velocity");
                if (vel is Vector3)
                    EscribirMiembro(c.ComponentBody, "Velocity", (Vector3)vel + new Vector3(hacia.X * 2.5f, 0f, hacia.Z * 2.5f));
            }
            if (posSalto.Count > 200)
            {
                List<ComponentCreature> quitar = new List<ComponentCreature>();
                foreach (ComponentCreature k in posSalto.Keys) if (!EstaViva(k)) quitar.Add(k);
                foreach (ComponentCreature k in quitar) posSalto.Remove(k);
            }
        }

        // ---------- ruido: correr atrae a los zombis cercanos ----------
        void RevisarRuido(double ahora)
        {
            if (jugadores == null || ahora < proximoRuido) return;
            proximoRuido = ahora + 1.0;
            foreach (ComponentPlayer pl in jugadores.ComponentPlayers)
            {
                if (!EstaViva(pl)) continue;
                object v = LeerMiembro(pl.ComponentBody, "Velocity");
                if (!(v is Vector3)) continue;
                Vector3 vel = (Vector3)v;
                float horizontal = (float)Math.Sqrt(vel.X * vel.X + vel.Z * vel.Z);
                if (horizontal < VelocidadRuido) continue;

                int alertados = 0;
                foreach (ComponentCreature c in creaturas.Creatures)
                {
                    if (alertados >= MaxAlertados) break;
                    if (!EstaViva(c) || !EsZombiCampeonable(Plantilla(c))) continue;
                    if (Vector3.Distance(c.ComponentBody.Position, pl.ComponentBody.Position) > RadioRuido) continue;
                    ComponentChaseBehavior persecucion = c.Entity.FindComponent<ComponentChaseBehavior>();
                    if (persecucion == null || LeerMiembro(persecucion, "Target") != null) continue; // ya persigue a alguien
                    persecucion.Attack(pl, RadioRuido + 12f, 30f, false);
                    alertados++;
                }
            }
        }

        // ---------- zombis eliminados ----------
        void RevisarMuertes()
        {
            HashSet<ComponentCreature> presentes = new HashSet<ComponentCreature>();
            foreach (ComponentCreature c in creaturas.Creatures)
            {
                presentes.Add(c);
                if (EstaViva(c) && EsZombiCampeonable(Plantilla(c))) zombisVivos.Add(c);
            }
            if (zombisVivos.Count == 0) return;
            List<ComponentCreature> quitar = null;
            foreach (ComponentCreature z in zombisVivos)
            {
                bool murio = z.ComponentHealth != null && z.ComponentHealth.Health <= 0f;
                if (!murio && presentes.Contains(z)) continue;
                if (murio) muertes++;
                if (quitar == null) quitar = new List<ComponentCreature>();
                quitar.Add(z);
            }
            if (quitar != null) foreach (ComponentCreature z in quitar) zombisVivos.Remove(z);
        }

        // ---------- panel de oleadas (arriba a la izquierda) + barra del jefe ----------
        class Hud
        {
            public StackPanelWidget Caja;
            public LabelWidget Info;
            public LabelWidget Luna;
            public LabelWidget NombreJefe;
            public CanvasWidget CajaBarra;
            public RectangleWidget Relleno;
            public string UltimoInfo;
            public string UltimoJefe;
        }

        readonly Dictionary<ComponentGui, Hud> huds = new Dictionary<ComponentGui, Hud>();
        static bool hudDesactivado;
        const float AnchoBarraJefe = 230f;

        public override void GuiDraw(ComponentGui componentGui, Camera camera, int drawOrder)
        {
            if (hudDesactivado || componentGui == null) return;
            try
            {
                ContainerWidget contenedor = componentGui.ControlsContainerWidget;
                if (contenedor == null || creaturas == null) return;

                Hud h;
                if (!huds.TryGetValue(componentGui, out h))
                {
                    List<ComponentGui> viejos = new List<ComponentGui>();
                    foreach (KeyValuePair<ComponentGui, Hud> par in huds)
                    {
                        if (par.Value.Caja != null && par.Value.Caja.ParentWidget == contenedor)
                        {
                            contenedor.RemoveChildren(par.Value.Caja);
                            viejos.Add(par.Key);
                        }
                    }
                    for (int i = 0; i < viejos.Count; i++) huds.Remove(viejos[i]);

                    h = new Hud();
                    h.Caja = new StackPanelWidget();
                    h.Caja.Direction = LayoutDirection.Vertical;
                    h.Caja.HorizontalAlignment = WidgetAlignment.Near;
                    h.Caja.VerticalAlignment = WidgetAlignment.Near;
                    h.Caja.Margin = new Vector2(150f, 6f);
                    h.Caja.IsHitTestVisible = false;

                    h.Info = new LabelWidget();
                    h.Info.FontScale = 0.8f;
                    h.Info.Color = Color.White;
                    h.Info.HorizontalAlignment = WidgetAlignment.Near;
                    h.Caja.AddChildren(h.Info);

                    h.Luna = new LabelWidget();
                    h.Luna.FontScale = 0.9f;
                    h.Luna.Color = new Color((byte)255, (byte)70, (byte)70);
                    h.Luna.Text = "¡LUNA ROJA!";
                    h.Luna.HorizontalAlignment = WidgetAlignment.Near;
                    h.Luna.IsVisible = false;
                    h.Caja.AddChildren(h.Luna);

                    h.NombreJefe = new LabelWidget();
                    h.NombreJefe.FontScale = 0.8f;
                    h.NombreJefe.Color = new Color((byte)255, (byte)170, (byte)70);
                    h.NombreJefe.HorizontalAlignment = WidgetAlignment.Near;
                    h.NombreJefe.IsVisible = false;
                    h.Caja.AddChildren(h.NombreJefe);

                    h.CajaBarra = new CanvasWidget();
                    h.CajaBarra.Size = new Vector2(AnchoBarraJefe, 8f);
                    h.CajaBarra.HorizontalAlignment = WidgetAlignment.Near;
                    h.CajaBarra.IsVisible = false;
                    RectangleWidget fondo = new RectangleWidget();
                    fondo.Size = new Vector2(AnchoBarraJefe, 8f);
                    fondo.HorizontalAlignment = WidgetAlignment.Near;
                    fondo.VerticalAlignment = WidgetAlignment.Near;
                    fondo.FillColor = new Color((byte)30, (byte)30, (byte)30, (byte)200);
                    fondo.OutlineColor = new Color((byte)255, (byte)255, (byte)255, (byte)120);
                    fondo.OutlineThickness = 1f;
                    h.CajaBarra.AddChildren(fondo);
                    h.Relleno = new RectangleWidget();
                    h.Relleno.Size = new Vector2(AnchoBarraJefe, 8f);
                    h.Relleno.HorizontalAlignment = WidgetAlignment.Near;
                    h.Relleno.VerticalAlignment = WidgetAlignment.Near;
                    h.Relleno.FillColor = new Color((byte)200, (byte)40, (byte)40, (byte)255);
                    h.Relleno.OutlineColor = Color.Transparent;
                    h.CajaBarra.AddChildren(h.Relleno);
                    h.Caja.AddChildren(h.CajaBarra);

                    contenedor.AddChildren(h.Caja);
                    huds[componentGui] = h;
                }

                string info = "Dia " + DiaActual() + "  |  Zombis: " + muertes;
                if (info != h.UltimoInfo) { h.UltimoInfo = info; h.Info.Text = info; }
                h.Luna.IsVisible = LunaRoja;

                ComponentCreature jefe = null;
                for (int i = 0; i < jefes.Count; i++) if (EstaViva(jefes[i])) { jefe = jefes[i]; break; }
                h.NombreJefe.IsVisible = jefe != null;
                h.CajaBarra.IsVisible = jefe != null;
                if (jefe != null)
                {
                    float v = MathUtils.Clamp(jefe.ComponentHealth.Health, 0f, 1f);
                    float max = jefe.ComponentHealth.AttackResilience;
                    string txt = "JEFE DE HORDA  " + (max > 0f
                        ? (v * max).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " / " + max.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                        : (v * 100f).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%");
                    if (txt != h.UltimoJefe)
                    {
                        h.UltimoJefe = txt;
                        h.NombreJefe.Text = txt;
                        h.Relleno.Size = new Vector2(MathUtils.Max(AnchoBarraJefe * v, 1f), 8f);
                    }
                }
            }
            catch (Exception ex)
            {
                hudDesactivado = true;
                Log.Warning("Zombies: panel de oleadas desactivado por error: " + ex);
            }
        }

        int LimiteHorda()
        {
            return LunaRoja ? MaxZombisHorda * 2 : MaxZombisHorda;
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
                NocheEmpieza(ahora);
            }
            else if (!noche && eraNoche)
            {
                hordaEstaNoche = false;
                NocheTermina();
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
            RevisarJefe(ahora);
            RevisarRompedores(ahora);
            RevisarSaltadores(ahora);
            RevisarRuido(ahora);
            RevisarMuertes();
        }

        void LanzarHorda()
        {
            if (jugadores == null || terreno == null) return;
            if (horda.Count >= LimiteHorda()) return;

            List<ComponentPlayer> vivos = new List<ComponentPlayer>();
            foreach (ComponentPlayer p in jugadores.ComponentPlayers)
            {
                if (EstaViva(p)) vivos.Add(p);
            }
            if (vivos.Count == 0) return;

            ComponentPlayer objetivo = vivos[azar.Next(vivos.Count)];
            Vector3 centro = objetivo.ComponentBody.Position;
            int cantidad = azar.Next(ZombisHordaMin, ZombisHordaMax + 1);
            if (LunaRoja) cantidad *= 2;

            for (int i = 0; i < cantidad && horda.Count < LimiteHorda(); i++)
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
