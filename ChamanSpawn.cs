// Chaman indigena (plantilla "Chaman" del .xdb):
//  - Maldicion: prende fuego SOLO a quien lo ataco a el o a los suyos (jugador, animal o gigante) por 3 segundos.
//    Nunca maldice por iniciativa propia. Enfriamiento de 6 segundos.
//  - Cura: cada 5 segundos recupera 3 puntos de vida a los indigenas normales y a los cazadores, y 5 puntos al jefe indigena, a menos de 10 bloques,
//    y sobre cada indigena curado sube un corazon (senal de que lo curo).
// No aparece solo por el mundo: lo agrega RefugioIndigenaSpawn en algunos refugios (o sale de su huevo).
// Archivo nuevo: sube este .cs junto a los demas; se compila todo junto en GiganteCode.dll.
using System.Collections.Generic;
using Engine;
using Engine.Graphics;
using GameEntitySystem;
using GiganteCode;

namespace Game
{
    public class ChamanLoader : ModLoader
    {
        const string PlantillaChaman = "Chaman";
        const string PlantillaNativo = "Indigena";
        const string PlantillaJefe = "JefeIndigena";   // el chaman tambien cura al jefe
        const string PlantillaCazador = "Cazador";     // y al cazador

        const float AlcanceMaldicion = 14f;          // bloques
        const float DuracionFuego = 3f;              // segundos ardiendo
        const double EnfriamientoMaldicion = 6.0;    // segundos entre maldiciones

        const double PausaCura = 5.0;                // segundos entre curas
        const float RadioCura = 10f;                 // bloques
        const float VidaCura = 3f;                   // puntos de vida que recupera cada indigena
        const float VidaCuraJefe = 5f;               // puntos de vida que recupera el jefe
        const float VidaCuraCazador = 3f;            // puntos de vida que recupera el cazador

        const double PausaRevision = 0.4;

        SubsystemCreatureSpawn creaturas;
        SubsystemTerrain terreno;
        SubsystemTime tiempo;
        SubsystemParticles particulas;

        readonly Dictionary<ComponentCreature, double> proximaMaldicion = new Dictionary<ComponentCreature, double>();
        readonly Dictionary<ComponentCreature, double> proximaCura = new Dictionary<ComponentCreature, double>();
        double proximaRevision;
        double proximaLimpieza;

        public override void __ModInitialize()
        {
            ModsManager.RegisterHook("OnProjectLoaded", this);
            ModsManager.RegisterHook("OnProjectDisposed", this);
            ModsManager.RegisterHook("SubsystemUpdate", this);
        }

        public override void OnProjectLoaded(Project proyecto)
        {
            creaturas = proyecto.FindSubsystem<SubsystemCreatureSpawn>(true);
            terreno = proyecto.FindSubsystem<SubsystemTerrain>(true);
            tiempo = proyecto.FindSubsystem<SubsystemTime>(true);
            particulas = proyecto.FindSubsystem<SubsystemParticles>(true);
            proximaMaldicion.Clear();
            proximaCura.Clear();
            proximaRevision = 0;
            proximaLimpieza = 0;
        }

        public override void OnProjectDisposed()
        {
            creaturas = null; terreno = null; tiempo = null; particulas = null;
            proximaMaldicion.Clear();
            proximaCura.Clear();
        }

        static bool EstaViva(ComponentCreature c)
        {
            return c != null && c.ComponentHealth != null && c.ComponentBody != null && c.ComponentHealth.Health > 0f;
        }

        static string Plantilla(ComponentCreature c)
        {
            return c.Entity.ValuesDictionary.DatabaseObject.Name;
        }

        // el jefe se reconoce por su plantilla (exacta o cualquier nombre que contenga "Jefe")
        static bool EsJefe(string plantilla)
        {
            return plantilla == PlantillaJefe || plantilla.IndexOf("Jefe", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public override void SubsystemUpdate(float dt)
        {
            if (creaturas == null || tiempo == null) return;
            if (tiempo.GameTime < proximaRevision) return;
            proximaRevision = tiempo.GameTime + PausaRevision;

            foreach (ComponentCreature chaman in creaturas.Creatures)
            {
                if (!EstaViva(chaman) || Plantilla(chaman) != PlantillaChaman) continue;
                Maldecir(chaman);
                Curar(chaman);
            }

            // limpieza de chamanes que ya no existen (cada ~10 s)
            if (tiempo.GameTime >= proximaLimpieza)
            {
                proximaLimpieza = tiempo.GameTime + 10.0;
                var vivos = new HashSet<ComponentCreature>(creaturas.Creatures);
                var viejos = new List<ComponentCreature>();
                foreach (ComponentCreature c in proximaMaldicion.Keys) if (!vivos.Contains(c)) viejos.Add(c);
                foreach (ComponentCreature c in proximaCura.Keys) if (!vivos.Contains(c)) viejos.Add(c);
                foreach (ComponentCreature c in viejos)
                {
                    proximaMaldicion.Remove(c);
                    proximaCura.Remove(c);
                }
            }
        }

        // Maldicion de fuego: va contra el objetivo de su persecucion (el jugador o quien lo ataco)
        void Maldecir(ComponentCreature chaman)
        {
            double ahora = tiempo.GameTime;
            proximaMaldicion.TryGetValue(chaman, out double listo);
            if (ahora < listo) return;

            ComponentChaseBehavior persecucion = chaman.Entity.FindComponent<ComponentChaseBehavior>();
            ComponentCreature objetivo = persecucion != null ? persecucion.Target : null;
            if (!EstaViva(objetivo)) return;

            // el chaman no maldice por iniciativa propia (a nadie): solo a quien lo ataco a el o a los suyos
            // (PazIndigena registra a cualquier agresor, no solo al jugador)
            if (!PazIndigena.PuedeAtacar(chaman, objetivo, ahora)) return;

            Vector3 origen = chaman.ComponentBody.Position + new Vector3(0f, chaman.ComponentBody.BoxSize.Y * 0.8f, 0f);
            Vector3 destino = objetivo.ComponentBody.Position + new Vector3(0f, objetivo.ComponentBody.BoxSize.Y * 0.5f, 0f);
            if ((destino - origen).Length() > AlcanceMaldicion) return;

            // sin linea de vista (un bloque en medio): no lanza todavia
            var choque = terreno.Raycast(origen, destino, false, true,
                (v, d) => BlocksManager.Blocks[Terrain.ExtractContents(v)].IsCollidable);
            if (choque.HasValue) return;

            ComponentOnFire fuego = objetivo.Entity.FindComponent<ComponentOnFire>();
            if (fuego == null) return;

            fuego.SetOnFire(chaman, DuracionFuego);
            proximaMaldicion[chaman] = ahora + EnfriamientoMaldicion;

            if (chaman.ComponentCreatureModel != null) chaman.ComponentCreatureModel.AttackOrder = true; // mueve los brazos
        }

        // Cura a los indigenas normales y al jefe heridos que tiene cerca
        void Curar(ComponentCreature chaman)
        {
            double ahora = tiempo.GameTime;
            proximaCura.TryGetValue(chaman, out double listo);
            if (ahora < listo) return;
            proximaCura[chaman] = ahora + PausaCura;

            foreach (ComponentCreature otro in creaturas.Creatures)
            {
                if (otro == chaman || !EstaViva(otro)) continue;
                string plantillaOtro = Plantilla(otro);
                bool esJefe = EsJefe(plantillaOtro);
                bool esCazador = plantillaOtro == PlantillaCazador;
                if (plantillaOtro != PlantillaNativo && !esJefe && !esCazador) continue;
                if (otro.ComponentHealth.Health >= 1f) continue;
                if (Vector3.Distance(otro.ComponentBody.Position, chaman.ComponentBody.Position) > RadioCura) continue;

                // la vida va de 0 a 1: puntos / resistencia (indigena normal 3, cazador 3, jefe 5)
                float resistencia = MathUtils.Max(1f, otro.ComponentHealth.AttackResilience);
                float puntos = esJefe ? VidaCuraJefe : (esCazador ? VidaCuraCazador : VidaCura);
                otro.ComponentHealth.Heal(puntos / resistencia);

                // corazon sobre la cabeza del indigena curado
                if (particulas != null)
                {
                    Vector3 sobreLaCabeza = otro.ComponentBody.Position + new Vector3(0f, otro.ComponentBody.BoxSize.Y + 0.3f, 0f);
                    particulas.AddParticleSystem(new CorazonParticleSystem(sobreLaCabeza), false);
                }
            }
        }
    }
    // Corazon rojo que sube y se desvanece (textura: Assets/Textures/Particles/Corazon.png)
    public class CorazonParticleSystem : ParticleSystem<CorazonParticleSystem.Corazon>
    {
        public class Corazon : Particle
        {
            public float Edad;
        }

        const float Duracion = 1.4f;        // segundos que dura el corazon
        const float VelocidadSubida = 0.9f; // bloques por segundo
        const int CantidadCorazones = 2;

        public CorazonParticleSystem(Vector3 posicion) : base(CantidadCorazones)
        {
            Texture = ContentManager.Get<Texture2D>("Textures/Particles/Corazon");
            TextureSlotsCount = 1;

            Engine.Random azar = new Engine.Random();
            for (int i = 0; i < Particles.Length; i++)
            {
                Corazon p = Particles[i];
                p.IsActive = true;
                p.Position = posicion + new Vector3(azar.Float(-0.2f, 0.2f), i * 0.3f, azar.Float(-0.2f, 0.2f));
                p.Size = new Vector2(0.4f);
                p.Color = Color.White;
                p.TextureSlot = 0;
                p.Edad = -i * 0.15f; // el segundo corazon sale un poco despues
            }
        }

        // devuelve true cuando ya no queda ningun corazon (el juego quita el sistema)
        public override bool Simulate(float dt)
        {
            if (dt > 0.1f) dt = 0.1f;
            bool alguno = false;
            for (int i = 0; i < Particles.Length; i++)
            {
                Corazon p = Particles[i];
                if (!p.IsActive) continue;
                p.Edad += dt;
                if (p.Edad >= Duracion)
                {
                    p.IsActive = false;
                    continue;
                }
                alguno = true;
                if (p.Edad < 0f)
                {
                    p.Color = Color.Transparent; // todavia no aparece
                    continue;
                }
                p.Position += new Vector3(0f, VelocidadSubida * dt, 0f);
                float t = p.Edad / Duracion;
                float opacidad = t < 0.6f ? 1f : (1f - t) / 0.4f;
                p.Color = Color.White * opacidad;
            }
            return !alguno;
        }
    }
}
