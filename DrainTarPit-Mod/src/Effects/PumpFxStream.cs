using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DrainTarPit.Building;
using DrainTarPit.Logging;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Networking;
using UnityEngine.Rendering;


namespace DrainTarPit.Effects
{
    /// The particle effects of the pump (PumpFx): the spurt out of the nozzle with its splashes and puddle, and the droplets at the pump.
    internal static class PumpFxStream
    {
        static Material _mat;

        internal static readonly Color TarDark = new(0.04f, 0.03f, 0.02f, 1f), TarLight = new(0.2f, 0.14f, 0.07f, 1f);
        internal static readonly Color WaterDark = new(0.30f, 0.52f, 0.68f, 0.9f), WaterLight = new(0.78f, 0.92f, 1f, 0.95f);
        internal static readonly Color TarPuddle = new(0.05f, 0.04f, 0.03f, 0.75f), WaterPuddle = new(0.32f, 0.55f, 0.7f, 0.55f);

        // Height of the ground under the nozzle. The ray skips our own hoses / pump (the nozzle sits right above its own collider, which made the
        // puddle float at pipe height) and the player; terrain is the fallback.
        // Bug #7: TarPool.Landing already used RaycastNonAlloc; this was the last RaycastAll in the stroke path (a new array on every call, up to 4 per
        // stroke: garbage while pumping continuously). One shared buffer
        // (main thread only). 64 hits is far more than a 12 m ray down through a base meets; if it is ever full the debug log says so.
        static readonly RaycastHit[] GroundHits = new RaycastHit[64];

        internal static float GroundBelow(Vector3 pos)
        {
            float best = float.MaxValue; bool found = false; int n = 0;
            try
            {
                n = Physics.RaycastNonAlloc(pos + Vector3.up * 0.1f, Vector3.down, GroundHits, 12f, ~0, QueryTriggerInteraction.Ignore);
                if (n >= GroundHits.Length) ModLog.VerboseWarn("GroundBelow: " + n + " hits fill the buffer at " + pos.ToString("0.0") + ", lower hits may be missed");
                for (int i = 0; i < n; i++)
                {
                    var h = GroundHits[i];
                    if (h.collider == null || h.distance < 0.05f) continue;
                    if (h.collider.GetComponentInParent<DtpPiece>() != null || h.collider.GetComponentInParent<Character>() != null) continue;
                    if (h.normal.y < 0.3f) continue;                       // walls / undersides are not ground
                    if (h.distance < best) { best = h.distance; found = true; }
                }
            }
            catch (System.Exception e) { ModLog.VerboseWarn("GroundBelow: ray failed at " + pos.ToString("0.0") + ": " + e.Message + " (terrain height used)"); found = false; }
            if (found) return pos.y + 0.1f - best;
            if (ZoneSystem.instance != null)
            {
                float terrain = ZoneSystem.instance.GetGroundHeight(pos);
                ModLog.Verbose("GroundBelow: no ground among " + n + " hits under " + pos.ToString("0.0") + ", terrain height " + terrain.ToString("0.00") + " used");
                return terrain;
            }
            ModLog.Verbose("GroundBelow: no ground and no ZoneSystem at " + pos.ToString("0.0") + ", 0.6 m below used");
            return pos.y - 0.6f;
        }

        // A pressurised spurt (stretched droplets shot out of the nozzle along its axis, then falling in an arc), the splashes where it hits
        // the ground and a puddle that spreads and fades. One spurt per press: it starts hard, fades out within NozzleFlowSeconds and the
        // landing point is worked out from the exit speed, so the splashes / puddle appear where the stream really comes down.
        /// `landAt` / `fallSeconds`: where and how long after the start the first droplets come down (the water splash sound plays there, then).
        internal static void Stream(Vector3 pos, Vector3 dir, float frac, float amount, Drain.PumpLiquid liquid, out Vector3 landAt, out float fallSeconds)
        {
            landAt = pos; fallSeconds = 0f;
            bool water = liquid == Drain.PumpLiquid.Water;
            Color dark = water ? WaterDark : TarDark, light = water ? WaterLight : TarLight, puddle = water ? WaterPuddle : TarPuddle;
            var mat = Mat(); if (mat == null) { ModLog.Verbose("Pour stream: no particle material, nothing shown"); return; }
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.down;
            dir.Normalize();
            const float g = 9.81f;
            float speed = Mathf.Max(0.5f, ModSettings.NozzlePressure.Value);
            Vector3 v = dir * speed;

            // where does it come down? the ground at the landing point can be higher / lower than under the nozzle, so refine a few times
            float groundY = GroundBelow(pos), fall = 0.15f;
            Vector3 land = new Vector3(pos.x, groundY, pos.z);
            bool sideways = v.x * v.x + v.z * v.z > 0.04f;
            for (int i = 0; i < 3; i++)
            {
                float h = Mathf.Max(0.15f, pos.y - groundY);
                fall = Mathf.Clamp((v.y + Mathf.Sqrt(v.y * v.y + 2f * g * h)) / g, 0.1f, 3f);
                land = new Vector3(pos.x + v.x * fall, groundY, pos.z + v.z * fall);
                if (!sideways) break;
                groundY = GroundBelow(new Vector3(land.x, Mathf.Max(pos.y, groundY + 1.5f), land.z));
            }
            landAt = new Vector3(land.x, groundY, land.z); fallSeconds = fall;
            float dur = Mathf.Max(0.1f, ModSettings.NozzleFlowSeconds.Value * (0.75f + 0.5f * frac));   // how long this press keeps spurting

            // 1) the spurt: fast start, then fades out (not a constant flow)
            var go = new GameObject("DTP_pour");
            go.transform.position = pos; go.transform.rotation = Quaternion.LookRotation(dir);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = dur; main.loop = false; main.playOnAwake = false;
            main.startLifetime = fall + 0.05f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.94f, speed * 1.06f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.14f);
            main.startColor = new ParticleSystem.MinMaxGradient(dark, light);
            main.gravityModifier = 1f; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600; main.stopAction = ParticleSystemStopAction.Destroy;
            var em = ps.emission; em.enabled = true;
            var burst = new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.12f, 1f), new Keyframe(0.55f, 0.55f), new Keyframe(1f, 0f));
            em.rateOverTime = new ParticleSystem.MinMaxCurve((190f + 330f * frac) * amount, burst);
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 3.5f; shape.radius = 0.03f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat; r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.02f; r.lengthScale = 1.3f;
            ps.Play();

            // 2) splashes where it lands, starting when the first droplets arrive
            var go2 = new GameObject("DTP_pourSplash");
            go2.transform.position = new Vector3(land.x, groundY + 0.03f, land.z); go2.transform.rotation = Quaternion.LookRotation(Vector3.up);
            var ps2 = go2.AddComponent<ParticleSystem>();
            ps2.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m2 = ps2.main;
            m2.duration = dur; m2.startDelay = fall; m2.loop = false; m2.playOnAwake = false;
            m2.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
            m2.startSpeed = new ParticleSystem.MinMaxCurve(0.7f, 1.9f);
            m2.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
            m2.startColor = new ParticleSystem.MinMaxGradient(dark, light);
            m2.gravityModifier = 1.6f; m2.simulationSpace = ParticleSystemSimulationSpace.World;
            m2.maxParticles = 300; m2.stopAction = ParticleSystemStopAction.Destroy;
            var e2 = ps2.emission; e2.enabled = true; e2.rateOverTime = new ParticleSystem.MinMaxCurve((70f + 160f * frac) * amount, burst);
            var s2 = ps2.shape; s2.enabled = true; s2.shapeType = ParticleSystemShapeType.Cone; s2.angle = 40f; s2.radius = 0.08f;
            var r2 = go2.GetComponent<ParticleSystemRenderer>(); r2.sharedMaterial = mat; r2.renderMode = ParticleSystemRenderMode.Billboard;
            ps2.Play();

            if (!ModSettings.NozzlePuddle.Value) return;
            // 3) puddle: a flat disc on the ground that grows quickly, stays a moment and fades out
            var go3 = new GameObject("DTP_puddle");
            go3.transform.position = new Vector3(land.x, groundY + 0.025f, land.z); go3.transform.rotation = Quaternion.LookRotation(Vector3.up);
            var ps3 = go3.AddComponent<ParticleSystem>();
            ps3.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m3 = ps3.main;
            float size = 0.22f + 0.2f * frac;
            m3.duration = dur; m3.startDelay = fall; m3.loop = false; m3.playOnAwake = false;
            m3.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3f);
            m3.startSpeed = 0f;
            m3.startSize = new ParticleSystem.MinMaxCurve(size, size * 1.6f);
            m3.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m3.startColor = puddle;
            m3.gravityModifier = 0f; m3.simulationSpace = ParticleSystemSimulationSpace.World;
            m3.maxParticles = 8; m3.stopAction = ParticleSystemStopAction.Destroy;
            var e3 = ps3.emission; e3.enabled = true; e3.rateOverTime = 0f;
            e3.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1, (short)2) });
            var s3 = ps3.shape; s3.enabled = true; s3.shapeType = ParticleSystemShapeType.Circle; s3.radius = 0.1f;
            var sz = ps3.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.25f), new Keyframe(0.15f, 0.9f), new Keyframe(1f, 1.2f)));
            var col3 = ps3.colorOverLifetime; col3.enabled = true;
            var gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.05f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col3.color = gr;
            var r3 = go3.GetComponent<ParticleSystemRenderer>(); r3.sharedMaterial = mat; r3.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            ps3.Play();
        }

        // --- droplets ---------------------------------------------------------------------------------------------------
        internal static Material Mat()
        {
            if (_mat != null) return _mat;
            Shader sh = null;
            foreach (var n in new[] { "Sprites/Default", "UI/Default", "Legacy Shaders/Particles/Alpha Blended", "Particles/Standard Unlit" })
            { sh = Shader.Find(n); if (sh != null) break; }
            if (sh == null) return null;
            var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(8f, 8f)) / 8f;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((1f - d) * 3f)));
                }
            tex.Apply();
            _mat = new Material(sh) { name = "DTP_Droplet", mainTexture = tex };
            return _mat;
        }

        internal static void Droplets(Vector3 pos, Vector3 up, int count, float power)
        {
            var mat = Mat(); if (mat == null) return;
            var go = new GameObject("DTP_fx");
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(up);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.5f; main.loop = false; main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f * power, 2.4f * power);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.05f, 0.04f, 0.03f, 1f), new Color(0.2f, 0.14f, 0.07f, 1f));
            main.gravityModifier = 1.4f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 64;
            main.stopAction = ParticleSystemStopAction.Destroy;

            var em = ps.emission; em.enabled = true; em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 28f; shape.radius = 0.04f;

            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat; r.renderMode = ParticleSystemRenderMode.Billboard;
            ps.Play();
        }
    }
}
