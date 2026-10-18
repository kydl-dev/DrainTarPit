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
    /// <summary>
    /// Small visual + sound effects that do not depend on vanilla prefab names or extra bundle assets:
    ///   Stroke(at)  - 'pump_up' recording (the lever being pressed) and a puff of tar droplets at the pump (every stroke, every client that sees it)
    ///   Pour(pos, m3) - 'oil_pumped' recording + a stream of tar falling from the outlet pipe / nozzle, splashes where it lands and a puddle that
    ///                   spreads and fades; shown to the pumping player and (via the pump's ZDO) to every client near the pump
    /// The recordings come from the asset bundle (assets/audio/pump_up.ogg, oil_pumped.ogg) or, when the bundle does not have
    /// them, from the .ogg files embedded in the DLL (written to the temp cache and loaded with UnityWebRequest). If neither is
    /// available a synthesised sound is used. Droplets are a code-built ParticleSystem. Everything is skipped on a
    /// dedicated server / -nographics and when [Effects] Effects = false.
    /// </summary>
    internal static partial class PumpFx
    {
        static AudioClip _stroke;                // synthesised fallback of the lever sound
        const string PumpUp = "pump_up", OilPumped = "oil_pumped";
        static Material _mat;
        static AudioMixerGroup _group;
        static bool _groupResolved;

        static bool _disabled;      // set after an error; runtime only, the config file is left alone
        static bool Enabled { get { return !_disabled && ModSettings.Effects.Value && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null; } }

        public static void Stroke(Transform at)
        {
            if (!Enabled || at == null) return;
            try
            {
                if (ModSettings.PumpDroplets.Value) Droplets(at.position, Vector3.up, 8, 0.6f);   // off by default: it looked like oil dripping at the pump outlet / first hose joint
                var clip = Clip(PumpUp);               // pump_up: the lever being pressed
                if (clip == null && !Loading(PumpUp)) clip = _stroke != null ? _stroke : (_stroke = MakeStroke());   // fallback: synthesised
                Sound(at.position, clip, 1f, ModSettings.SoundTailSeconds.Value);
            }
            catch (System.Exception e) { Fail(e); }
        }

        /// The liquid coming out at the nozzle. `volume` = m3 that really left the pit with this stroke (scales length and thickness).
        /// Plays right now; PourWhenArrives is the version that waits for the tar to get through the hoses.
        public static void Pour(Vector3 pos, Vector3 dir, float volume, Drain.PumpLiquid liquid = Drain.PumpLiquid.Tar)
        {
            PourVisual(pos, dir, volume, liquid);
            PourSound(pos, liquid);
        }

        /// The pour at the moment the tar slug of this stroke (TarFlow) reaches the nozzle: stream + splashes + puddle at that moment, the
        /// oil_pumped sound starting with its first audible sample on the same moment (+ [Effects] PourSoundOffset), and `atNozzle` (the tar
        /// deposit, which then adds its own flight time). Without a slug to follow (TarFlow off, [Effects] SyncPour off) it happens right away.
        /// Pours and strokes are matched in order, see TarFlow.ClaimArrival.
        public static void PourWhenArrives(DtpPiece pump, Vector3 pos, Vector3 dir, float volume, System.Action atNozzle = null, Drain.PumpLiquid liquid = Drain.PumpLiquid.Tar)
        {
            float delay = ModSettings.SyncPour.Value ? TarFlow.ClaimArrival(pump) : -1f;
            float tVisual = Mathf.Max(0f, delay);
            float tSound = Mathf.Max(0f, tVisual + ModSettings.PourSoundOffset.Value);
            RunAfter(tVisual, () => { PourVisual(pos, dir, volume, liquid); if (atNozzle != null) atNozzle(); });
            RunAfter(tSound, () => PourSound(pos, liquid));
            ModLog.Verbose("Pour: " + (delay >= 0f ? "follows the tar slug, reaches the nozzle in " + delay.ToString("0.00") + " s" : "no slug to follow, right away"));
        }

        static void RunAfter(float seconds, System.Action what)
        {
            if (seconds <= 0.001f) what(); else TarFlow.Schedule(seconds, what);
        }

        static void PourVisual(Vector3 pos, Vector3 dir, float volume, Drain.PumpLiquid liquid)
        {
            if (!Enabled) return;
            try
            {
                float frac = Mathf.Clamp01(volume / Mathf.Max(0.01f, ModSettings.VolumePerStroke.Value));
                float amount = ModSettings.NozzleFlowAmount.Value;
                if (amount > 0.001f) Stream(pos, dir, frac, amount, liquid);
            }
            catch (System.Exception e) { Fail(e); }
        }

        static void PourSound(Vector3 pos, Drain.PumpLiquid liquid)
        {
            if (!Enabled) return;
            try
            {
                // Water pours SILENTLY. The synthesised splash that used to play here (and as the oil fallback) was removed on purpose - never bring it back.
                if (liquid == Drain.PumpLiquid.Water) return;
                var clip = Clip(OilPumped);            // oil_pumped: the liquid coming out of the outlet pipe
                if (clip == null) return;              // no recording available: no sound (there is no synthesised fallback)
                Sound(pos, clip, 1f, 0f, true);
            }
            catch (System.Exception e) { Fail(e); }
        }

        static readonly Color TarDark = new(0.04f, 0.03f, 0.02f, 1f), TarLight = new(0.2f, 0.14f, 0.07f, 1f);
        static readonly Color WaterDark = new(0.30f, 0.52f, 0.68f, 0.9f), WaterLight = new(0.78f, 0.92f, 1f, 0.95f);
        static readonly Color TarPuddle = new(0.05f, 0.04f, 0.03f, 0.75f), WaterPuddle = new(0.32f, 0.55f, 0.7f, 0.55f);

        // Height of the ground under the nozzle. The ray skips our own hoses / pump (the nozzle sits right above its own collider, which made the
        // puddle float at pipe height) and the player; terrain is the fallback.
        static float GroundBelow(Vector3 pos)
        {
            float best = float.MaxValue; bool found = false;
            try
            {
                var hits = Physics.RaycastAll(pos + Vector3.up * 0.1f, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore);
                foreach (var h in hits)
                {
                    if (h.collider == null || h.distance < 0.05f) continue;
                    if (h.collider.GetComponentInParent<DtpPiece>() != null || h.collider.GetComponentInParent<Character>() != null) continue;
                    if (h.normal.y < 0.3f) continue;                       // walls / undersides are not ground
                    if (h.distance < best) { best = h.distance; found = true; }
                }
            }
            catch { }
            if (found) return pos.y + 0.1f - best;
            if (ZoneSystem.instance != null) return ZoneSystem.instance.GetGroundHeight(pos);
            return pos.y - 0.6f;
        }

        // A pressurised spurt (stretched droplets shot out of the nozzle along its axis, then falling in an arc), the splashes where it hits
        // the ground and a puddle that spreads and fades. One spurt per press: it starts hard, fades out within NozzleFlowSeconds and the
        // landing point is worked out from the exit speed, so the splashes / puddle appear where the stream really comes down.
        static void Stream(Vector3 pos, Vector3 dir, float frac, float amount, Drain.PumpLiquid liquid)
        {
            bool water = liquid == Drain.PumpLiquid.Water;
            Color dark = water ? WaterDark : TarDark, light = water ? WaterLight : TarLight, puddle = water ? WaterPuddle : TarPuddle;
            var mat = Mat(); if (mat == null) return;
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

        // --- recorded clips ---------------------------------------------------------------------------------------------
        static readonly Dictionary<string, AudioClip> Clips = new();
        static readonly HashSet<string> NotInBundle = new(), Pending = new(), Failed = new();

        /// Loads the sounds at startup so the first stroke is not silent (no-op on a dedicated server / effects off).
        public static void Preload()
        {
            if (!Enabled) return;
            Clip(PumpUp); Clip(OilPumped);
        }

        static bool Loading(string key) { return Pending.Contains(key); }

        /// The recording `key` (pump_up / oil_pumped). Bundle first, then the embedded .ogg. null while it is still loading or
        /// when it does not exist anywhere (Loading(key) tells these apart).
        static AudioClip Clip(string key)
        {
            AudioClip c;
            if (Clips.TryGetValue(key, out c)) return c;
            if (Plugin.Bundle != null && !NotInBundle.Contains(key))
            {
                c = Plugin.Bundle.LoadAsset<AudioClip>("assets/audio/" + key + ".ogg");
                if (c != null) { Clips[key] = c; ModLog.Verbose("Sound '" + key + "' loaded from bundle (" + c.length.ToString("0.00") + " s)"); return c; }
                NotInBundle.Add(key);
            }
            if (!Pending.Contains(key) && !Failed.Contains(key) && Plugin.Instance != null)
            {
                Pending.Add(key);
                Plugin.Instance.RunCoroutine(LoadEmbedded(key));
            }
            return null;
        }

        // The .ogg is embedded in the DLL (EmbeddedResource audio\<key>.ogg). Unity can only decode it from a file, so it is
        // written to the temp cache once and read back with UnityWebRequestMultimedia.
        static IEnumerator LoadEmbedded(string key)
        {
            string file = null;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                string res = null;
                foreach (var n in asm.GetManifestResourceNames()) if (n.EndsWith(key + ".ogg", System.StringComparison.OrdinalIgnoreCase)) { res = n; break; }
                if (res == null) { Fail(key, "not in the bundle and no embedded " + key + ".ogg"); yield break; }
                byte[] data;
                using (var st = asm.GetManifestResourceStream(res))
                using (var ms = new MemoryStream()) { st.CopyTo(ms); data = ms.ToArray(); }
                file = Path.Combine(Application.temporaryCachePath, "DTP_" + key + ".ogg");
                if (!File.Exists(file) || new FileInfo(file).Length != data.Length) File.WriteAllBytes(file, data);
            }
            catch (System.Exception e) { Fail(key, e.Message); yield break; }

            using (var req = UnityWebRequestMultimedia.GetAudioClip(new System.Uri(file).AbsoluteUri, AudioType.OGGVORBIS))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { Fail(key, req.error); yield break; }
                var clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip == null) { Fail(key, "decoder returned no clip"); yield break; }
                clip.name = "DTP_" + key;
                Clips[key] = clip; Pending.Remove(key);
                ModLog.Verbose("Sound '" + key + "' loaded from the embedded .ogg (" + clip.length.ToString("0.00") + " s)");
            }
        }

        static void Fail(string key, string why)
        {
            Pending.Remove(key); Failed.Add(key);
            ModLog.Warn("Sound '" + key + "' unavailable (" + why + ") - using the synthesised sound.");
        }

        static bool _failed;
        static void Fail(System.Exception e) { if (_failed) return; _failed = true; ModLog.Warn("Effects disabled after an error: " + e.Message); _disabled = true; }

        // --- sound ------------------------------------------------------------------------------------------------------
        // Routed through the same mixer group as the game's own effects (found on a vanilla sfx prefab) so the player's
        // sound-effect volume applies; without one the clip is simply played at EffectsVolume.
        static AudioMixerGroup Group()
        {
            if (_groupResolved) return _group;
            if (ZNetScene.instance == null) return null;
            _groupResolved = true;
            foreach (var n in new[] { "sfx_build_hammer_wood", "sfx_wood_break", "sfx_build_hammer_default", "sfx_build_hammer_metal" })
            {
                var p = ZNetScene.instance.GetPrefab(n);
                var src = p != null ? p.GetComponentInChildren<AudioSource>(true) : null;
                if (src != null && src.outputAudioMixerGroup != null) { _group = src.outputAudioMixerGroup; break; }
            }
            return _group;
        }

        // One voice per clip: a new stroke fades the previous one out (0.08 s) instead of letting two copies ring over each other
        // (that overlap - the long reverb-like tail of pump_up on top of the next stroke - sounded like an echo).
        // tailSeconds > 0: the clip is faded out that long after its loudest moment (cuts the baked-in reverb tail); 0 = play it whole.
        static readonly Dictionary<AudioClip, SfxVoice> Voices = new();
        static readonly Dictionary<AudioClip, Vector2> Analysis = new();       // x = loudest moment (s, -1 = unknown), y = first audible moment (s)

        // AudioClip.GetData has a Span<float> overload in newer Unity; net48 has no Span<>, so the float[] overload is called via reflection (CS7069 otherwise).
        static readonly MethodInfo MGetData = typeof(AudioClip).GetMethod("GetData", new[] { typeof(float[]), typeof(int) });

        // One pass over the clip: where the loudest sample is (the main hit) and where the first sample above 5 % of that is (the recording may start with
        // silence / a very quiet lead-in; the pour sound is started from there, so what you HEAR starts when the stream starts).
        static Vector2 Analyze(AudioClip clip)
        {
            Vector2 r;
            if (Analysis.TryGetValue(clip, out r)) return r;
            r = new Vector2(-1f, 0f);
            try
            {
                var d = new float[clip.samples * clip.channels];
                if (MGetData != null && (bool)MGetData.Invoke(clip, new object[] { d, 0 }))
                {
                    int best = 0; float peak = 0f;
                    for (int i = 0; i < d.Length; i++) { float v = Mathf.Abs(d[i]); if (v > peak) { peak = v; best = i; } }
                    r.x = (best / clip.channels) / (float)clip.frequency;
                    float thr = peak * 0.05f;
                    for (int i = 0; i < d.Length; i++)
                        if (Mathf.Abs(d[i]) >= thr) { r.y = Mathf.Max(0f, (i / clip.channels) / (float)clip.frequency - 0.01f); break; }
                }
            }
            catch { }
            Analysis[clip] = r;
            return r;
        }

        static float PeakTime(AudioClip clip) { return Analyze(clip).x; }

        // skipLead: start the clip at its first audible sample (see Analyze) instead of at 0
        static void Sound(Vector3 pos, AudioClip clip, float gain, float tailSeconds, bool skipLead = false)
        {
            if (clip == null) return;
            SfxVoice old;
            if (Voices.TryGetValue(clip, out old)) { if (old != null && old.Src != null && (old.PeakT < 0f || old.Src.time >= old.PeakT)) old.FadeOut(0.08f); Voices.Remove(clip); }   // only a copy that is already past its main hit is faded; one still building up to it plays on
            var go = new GameObject("DTP_sfx");
            go.transform.position = pos;
            var a = go.AddComponent<AudioSource>();
            float vol = Mathf.Clamp01(ModSettings.EffectsVolume.Value * gain);
            a.clip = clip; a.volume = vol;
            a.spatialBlend = 1f; a.minDistance = 2f; a.maxDistance = 30f; a.rolloffMode = AudioRolloffMode.Linear;
            a.dopplerLevel = 0f; a.playOnAwake = false;
            var g = Group(); if (g != null) a.outputAudioMixerGroup = g;
            var voice = go.AddComponent<SfxVoice>();
            voice.Src = a; voice.Volume = vol;
            float peak = PeakTime(clip); voice.PeakT = peak;
            if (tailSeconds > 0.01f && peak >= 0f) { voice.FadeEnd = peak + tailSeconds; voice.FadeStart = peak + tailSeconds * 0.35f; }
            if (skipLead)
            {
                float on = Analyze(clip).y;
                if (on > 0.005f) { try { a.timeSamples = Mathf.Clamp((int)(on * clip.frequency), 0, Mathf.Max(0, clip.samples - 1)); } catch { } }
            }
            Voices[clip] = voice;
            a.Play();
            Object.Destroy(go, clip.length + 0.2f);
        }

        /// Drives the volume of one sound: tail fade-out and the quick fade when a newer copy takes over.
        sealed class SfxVoice : MonoBehaviour
        {
            public AudioSource Src; public float Volume, PeakT = -1f, FadeStart = -1f, FadeEnd = -1f;
            float _outAt = -1f, _outLen = 0.08f;
            public void FadeOut(float seconds) { if (_outAt < 0f) { _outAt = Time.unscaledTime; _outLen = Mathf.Max(0.01f, seconds); } }
            void Update()
            {
                if (Src == null) return;
                float v = Volume;
                if (FadeEnd > 0f)
                {
                    float t = Src.time;
                    if (t >= FadeEnd) { Src.Stop(); Destroy(gameObject); return; }
                    if (t > FadeStart) v *= 1f - (t - FadeStart) / (FadeEnd - FadeStart);
                }
                if (_outAt >= 0f)
                {
                    float k = (Time.unscaledTime - _outAt) / _outLen;
                    if (k >= 1f) { Src.Stop(); Destroy(gameObject); return; }
                    v *= 1f - k;
                }
                Src.volume = v;
            }
        }

        // --- droplets ---------------------------------------------------------------------------------------------------
        static Material Mat()
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

        static void Droplets(Vector3 pos, Vector3 up, int count, float power)
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

        // --- synthesised clips ---------------------------------------------------------------------------------------------
        const int Rate = 22050;

        static AudioClip MakeStroke()
        {
            int n = (int)(0.7f * Rate); var d = new float[n]; var rng = new System.Random(7); float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float cutoff = Mathf.Lerp(1400f, 250f, Mathf.Clamp01(t / 0.5f));
                float a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate);
                lp += a * ((float)(rng.NextDouble() * 2.0 - 1.0) - lp);
                float env = (1f - Mathf.Exp(-t * 60f)) * Mathf.Exp(-t * 4.5f);
                float thump = Mathf.Sin(2f * Mathf.PI * (70f - 30f * t) * t) * Mathf.Exp(-t * 16f);
                float gurgle = Mathf.Sin(2f * Mathf.PI * (160f + 60f * Mathf.Sin(2f * Mathf.PI * 14f * t)) * t) * Mathf.Exp(-t * 7f) * 0.25f;
                d[i] = lp * env * 2.2f + thump * 0.7f + gurgle;
            }
            return Make("DTP_Stroke", d);
        }

        static AudioClip Make(string name, float[] d)
        {
            float peak = 0.0001f;
            for (int i = 0; i < d.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(d[i]));
            float k = 0.85f / peak;
            for (int i = 0; i < d.Length; i++) d[i] *= k;
            int fade = Mathf.Min(64, d.Length);                         // no click at the end
            for (int i = 0; i < fade; i++) d[d.Length - 1 - i] *= i / (float)fade;
            // non-streaming clip filled by a reader callback (called once with the whole buffer)
            return AudioClip.Create(name, d.Length, 1, Rate, false, buf => { System.Array.Copy(d, 0, buf, 0, System.Math.Min(buf.Length, d.Length)); });
        }
    }
}
