// Campeones (estilo TBOI) para: Esqueleto, ZombiIndigena, Chaman, Indigena, JefeIndigena, Cazador y los gigantes
// (pequeno, canibal, muerto, hielo y volador).
//  - 10% de las criaturas de estas plantillas nacen campeonas: mas grandes y con un brillo de color.
//  - rojo: aguanta x1.5 | amarillo: x1.6 de velocidad | verde: envenena al jugador
//  - azul: ralentiza al jugador | morado: al morir suelta hijos | dorado (2%): vida, dano y velocidad x2
//    y suelta azufre y cobre al morir.
// El color es un brillo (EmissionColor): no hacen falta texturas nuevas.
// Archivo nuevo: sube este .cs junto a los demas; se compila todo junto en GiganteCode.dll.
// Esqueleto, ZombiIndigena y GigantePequenoMuerto recalculan sus estadisticas cada medio segundo en
// EsqueletoSpawn.cs / GiganteSpawn.cs: ahi se aplican los factores de esta clase (FactorVida, FactorDano, FactorVelocidad).
using System;
using System.Collections.Generic;
using System.Reflection;
using Engine;
using Game;
using GameEntitySystem;

namespace GiganteCode
{
    public class CampeonesLoader : ModLoader
    {
        // ---- plantillas que pueden ser campeonas (Name en el .xdb) ----
        static readonly string[] Plantillas =
        {
            "Esqueleto", "ZombiIndigena", "Chaman", "Indigena", "JefeIndigena", "Cazador",
            "GigantePequeno", "GigantePequenoCanibal", "GigantePequenoMuerto", "GiganteHielo", "GiganteVolador"
        };
        // las que tienen hijos al morir como morado: los gigantes sueltan 1, el resto 2
        const int HijosMorado = 2;
        const int HijosMoradoGigante = 1;

        // ---- ajustes: cambia estos numeros a tu gusto ----
        const double ProbCampeon = 0.10;       // probabilidad total de campeon (incluye al dorado)
        const double ProbDorado = 0.02;        // parte de esa probabilidad que es el dorado
        const float EscalaCampeon = 1.25f;     // tamano: 1.25 = 125% del tamano normal
        const float BrilloCampeon = 0.55f;     // fuerza del color (0 = sin color, 1 = muy fuerte)
        const float VidaRojo = 1.5f;           // rojo: aguanta x1.5
        const float VelocidadAmarillo = 1.6f;  // amarillo: x1.6 de velocidad
        const float VenenoPorSegundo = 0.5f;   // verde: dano por segundo
        const double VenenoSegundos = 6.0;
        const float FactorLento = 0.5f;        // azul: el jugador camina a la mitad
        const double LentoSegundos = 5.0;
        const float EstadisticasDorado = 2f;   // dorado: vida, dano y velocidad x2
        const int BotinDorado = 4;
        const float AlcanceGolpe = 5.0f;       // distancia a la que un golpe se atribuye al campeon verde/azul
        const float AlcanceHijos = 45f;

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
        static readonly string[] NombresBotin = { "SulphurChunkBlock", "CopperOreChunkBlock" };

        // tipo de campeon de cada criatura viva (lo leen los otros archivos para sus factores)
        static readonly Dictionary<Entity, int> tipos = new Dictionary<Entity, int>();

        public static float FactorVida(Entity e)
        {
            int t;
            if (e == null || !tipos.TryGetValue(e, out t)) return 1f;
            return t == CRojo ? VidaRojo : (t == CDorado ? EstadisticasDorado : 1f);
        }

        public static float FactorDano(Entity e)
        {
            int t;
            if (e == null || !tipos.TryGetValue(e, out t)) return 1f;
            return t == CDorado ? EstadisticasDorado : 1f;
        }

        public static float FactorVelocidad(Entity e)
        {
            int t;
            if (e == null || !tipos.TryGetValue(e, out t)) return 1f;
            return t == CAmarillo ? VelocidadAmarillo : (t == CDorado ? EstadisticasDorado : 1f);
        }

        // estas plantillas recalculan sus estadisticas en otro archivo (alli se aplican los factores)
        static bool RecalculaEstadisticas(string plantilla)
        {
            return plantilla == "Esqueleto" || plantilla == "ZombiIndigena" || plantilla == "GigantePequenoMuerto";
        }

        SubsystemCreatureSpawn creaturas;
        SubsystemTime tiempo;
        SubsystemPlayers jugadores;
        SubsystemPickables pickables;

        readonly System.Random azar = new System.Random();
        readonly HashSet<ComponentCreature> vistos = new HashSet<ComponentCreature>();
        readonly Dictionary<ComponentCreature, int> campeones = new Dictionary<ComponentCreature, int>();
        readonly Dictionary<ComponentCreature, Vector3> campeonPos = new Dictionary<ComponentCreature, Vector3>();
        readonly Dictionary<ComponentCreature, float> saludPrevia = new Dictionary<ComponentCreature, float>();
        readonly Dictionary<ComponentCreature, double> venenoHasta = new Dictionary<ComponentCreature, double>();
        readonly Dictionary<ComponentCreature, double> venenoSiguiente = new Dictionary<ComponentCreature, double>();
        readonly Dictionary<ComponentCreature, double> lentoHasta = new Dictionary<ComponentCreature, double>();
        readonly Dictionary<ComponentCreature, float> velocidadOriginal = new Dictionary<ComponentCreature, float>();
        double proximaRevision;

        static MethodInfo metodoHerir;
        static bool herirBuscado;
        static MethodInfo metodoPickable;
        static bool pickableBuscado;
        static List<int> valoresBotin;

        public override void __ModInitialize()
        {
            ModsManager.RegisterHook("OnProjectLoaded", this);
            ModsManager.RegisterHook("OnProjectDisposed", this);
            ModsManager.RegisterHook("SubsystemUpdate", this);
        }

        public override void OnProjectLoaded(Project proyecto)
        {
            creaturas = proyecto.FindSubsystem<SubsystemCreatureSpawn>(true);
            tiempo = proyecto.FindSubsystem<SubsystemTime>(true);
            jugadores = proyecto.FindSubsystem<SubsystemPlayers>(true);
            pickables = proyecto.FindSubsystem<SubsystemPickables>(false);
            Reiniciar();
        }

        public override void OnProjectDisposed()
        {
            creaturas = null; tiempo = null; jugadores = null; pickables = null;
            Reiniciar();
        }

        void Reiniciar()
        {
            tipos.Clear();
            vistos.Clear();
            campeones.Clear();
            campeonPos.Clear();
            saludPrevia.Clear();
            venenoHasta.Clear();
            venenoSiguiente.Clear();
            lentoHasta.Clear();
            velocidadOriginal.Clear();
            proximaRevision = 0;
        }

        // ---------- utilidades ----------
        static bool EstaViva(ComponentCreature c)
        {
            return c != null && c.ComponentHealth != null && c.ComponentHealth.Health > 0f && c.ComponentBody != null;
        }

        static string Plantilla(ComponentCreature c)
        {
            return c.Entity.ValuesDictionary.DatabaseObject.Name;
        }

        static bool PuedeSerCampeon(string plantilla)
        {
            for (int i = 0; i < Plantillas.Length; i++)
                if (Plantillas[i] == plantilla) return true;
            return false;
        }

        static bool EsGigante(string plantilla)
        {
            return plantilla.StartsWith("Gigante", StringComparison.Ordinal);
        }

        // la firma de ComponentHealth.Injure cambia entre versiones: se llama por reflexion
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
                    Log.Warning("Campeones: ComponentHealth.Injure no encontrado; el veneno no hara dano");
            }
            if (metodoHerir == null) return;
            ParameterInfo[] pa = metodoHerir.GetParameters();
            object[] args = new object[pa.Length];
            for (int i = 0; i < pa.Length; i++)
            {
                Type t = pa[i].ParameterType;
                if (i == 0) args[i] = cantidad;
                else if (t == typeof(string)) args[i] = "Veneno";
                else if (t == typeof(bool)) args[i] = false;
                else if (t.IsAssignableFrom(typeof(ComponentCreature))) args[i] = atacante;
                else if (t.IsValueType) args[i] = Activator.CreateInstance(t);
                else args[i] = null;
            }
            metodoHerir.Invoke(objetivo.ComponentHealth, args);
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

        // ---------- campeones ----------
        void Brillar(ComponentCreature c, int tipo)
        {
            try
            {
                ComponentCreatureModel modelo = c.ComponentCreatureModel;
                if (modelo == null) return;
                float[] rgb = ColoresCampeon[tipo];
                // EmissionColor se suma al color del modelo (el juego no lo reescribe, a diferencia de DiffuseColor)
                modelo.EmissionColor = new Vector4(
                    rgb[0] * BrilloCampeon, rgb[1] * BrilloCampeon, rgb[2] * BrilloCampeon, 0f);
            }
            catch (Exception e)
            {
                Log.Warning("Campeones: no se pudo dar color al campeon: " + e.Message);
            }
        }

        void HacerCampeon(ComponentCreature c, int tipo)
        {
            string plantilla = Plantilla(c);
            campeones[c] = tipo;
            campeonPos[c] = c.ComponentBody.Position;
            tipos[c.Entity] = tipo;

            // tamano: hitbox y modelo
            c.ComponentBody.BoxSize = c.ComponentBody.BoxSize * EscalaCampeon;
            ComponentCreatureModel modelo = c.ComponentCreatureModel;
            if (modelo != null) modelo.ModelScale = modelo.ModelScale * EscalaCampeon;
            Brillar(c, tipo);

            // las plantillas que se recalculan en otro archivo toman sus factores de alli
            if (RecalculaEstadisticas(plantilla)) return;

            ComponentHealth vida = c.ComponentHealth;
            ComponentMiner mano = c.Entity.FindComponent<ComponentMiner>();
            ComponentLocomotion mov = c.ComponentLocomotion;
            if (vida != null) vida.AttackResilience = vida.AttackResilience * FactorVida(c.Entity);
            if (mano != null) mano.AttackPower = mano.AttackPower * FactorDano(c.Entity);
            if (mov != null) mov.WalkSpeed = mov.WalkSpeed * FactorVelocidad(c.Entity);
        }

        public override void SubsystemUpdate(float dt)
        {
            if (creaturas == null || tiempo == null) return;
            double ahora = tiempo.GameTime;
            if (ahora < proximaRevision) return;
            proximaRevision = ahora + 0.25;

            RevisarCampeones(ahora);
            RevisarGolpes(ahora);
            AplicarEfectos(ahora);
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
                if (vistos.Add(c) && PuedeSerCampeon(Plantilla(c)))
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
                if (campeones.ContainsKey(c))
                {
                    campeonPos[c] = c.ComponentBody.Position;
                    // por si algo le quita el brillo (otro mod, cambio de modelo)
                    if (c.ComponentCreatureModel != null && !c.ComponentCreatureModel.EmissionColor.HasValue)
                        Brillar(c, campeones[c]);
                }
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
                string plantilla = Plantilla(c);
                campeones.Remove(c);
                campeonPos.Remove(c);
                tipos.Remove(c.Entity);
                bool murio = c.ComponentHealth != null && c.ComponentHealth.Health <= 0f;
                if (!murio) continue; // desaparecio sin morir: sin efecto
                if (tipo == CMorado) SoltarHijos(p, plantilla);
                else if (tipo == CDorado) SoltarBotin(p);
            }
        }

        void SoltarHijos(Vector3 centro, string plantilla)
        {
            int cantidad = EsGigante(plantilla) ? HijosMoradoGigante : HijosMorado;
            for (int i = 0; i < cantidad; i++)
            {
                Vector3 pos = new Vector3(
                    centro.X + (float)(azar.NextDouble() * 3.0 - 1.5),
                    centro.Y + 0.5f,
                    centro.Z + (float)(azar.NextDouble() * 3.0 - 1.5));
                Entity entidad = creaturas.SpawnCreature(plantilla, pos, false);
                if (entidad == null) continue;
                ComponentCreature hijo = entidad.FindComponent<ComponentCreature>();
                if (hijo == null) continue;
                vistos.Add(hijo); // los hijos no pueden ser campeones
                ComponentPlayer objetivo = JugadorCercano(pos);
                ComponentChaseBehavior persecucion = entidad.FindComponent<ComponentChaseBehavior>();
                if (objetivo != null && persecucion != null)
                    persecucion.Attack(objetivo, AlcanceHijos, 90f, false);
            }
        }

        static List<int> ValoresBotin()
        {
            if (valoresBotin != null) return valoresBotin;
            valoresBotin = new List<int>();
            foreach (string nombre in NombresBotin)
            {
                bool hay = false;
                foreach (Block b in BlocksManager.Blocks)
                {
                    if (b != null && b.GetType().Name == nombre)
                    {
                        valoresBotin.Add(Terrain.MakeBlockValue(b.BlockIndex));
                        hay = true;
                        break;
                    }
                }
                if (!hay) Log.Warning("Campeones: no existe el bloque " + nombre + " para el botin dorado");
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
                    if (metodoPickable == null) Log.Warning("Campeones: AddPickable no encontrado; el botin dorado no se suelta");
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
                Log.Warning("Campeones: fallo al soltar el botin: " + e.Message);
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
                ComponentLocomotion loc = victima.ComponentLocomotion;
                if (loc == null) return;
                velocidadOriginal[victima] = loc.WalkSpeed;
                loc.WalkSpeed = loc.WalkSpeed * FactorLento;
            }
            lentoHasta[victima] = ahora + LentoSegundos;
        }

        void RestaurarVelocidad(ComponentCreature victima)
        {
            float original;
            if (!velocidadOriginal.TryGetValue(victima, out original)) return;
            velocidadOriginal.Remove(victima);
            ComponentLocomotion loc = victima.ComponentLocomotion;
            if (loc != null) loc.WalkSpeed = original;
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
                Log.Warning("Campeones: no se pudo mostrar el mensaje: " + e.Message);
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
                        catch (Exception e) { Log.Warning("Campeones: fallo el veneno: " + e.Message); }
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
    }
}
