using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DrainTarPit.Building;
using Kydl.Common;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;


namespace DrainTarPit.Effects
{
    /// The sounds of the pump effects (PumpFx): the recordings (asset bundles), one voice per clip with tail fade, the synthesised stroke fallback.
    internal static class PumpFxSound
    {
        internal const string PumpUp = "pump_up", OilPumped = "oil_pumped";
        // user, 2026-10-20 (audio/source/pump-scratching.ogg cut at 0.98 s: first half = piston going down, second half = going up; water-splash.mp3)
        internal const string ScratchDown = "pump_scratch_down", ScratchUp = "pump_scratch_up", WaterSplash = "water_splash";
        static AudioMixerGroup _group;
        static bool _groupResolved;

        // --- recorded clips ---------------------------------------------------------------------------------------------
        internal static readonly Dictionary<string, AudioClip> Clips = new();
        internal static readonly HashSet<string> NotInBundle = new(), Pending = new(), Failed = new();

        internal static bool Loading(string key) { return Pending.Contains(key); }

        /// The recording `key`: from DrainTarPit's asset bundle, or one another mod handed in (PoolHooks.Clips: WaterAndTarPools' water splash).
        /// null when it does not exist anywhere (logged once). T-073: nothing is written to disk any more - the .ogg files used to be embedded in the
        /// DLL, written to the temp folder and read back with UnityWebRequest, which looks like a "dropper" to virus scanners; they are in the bundles now.
        internal static AudioClip Clip(string key)
        {
            AudioClip c;
            if (Clips.TryGetValue(key, out c)) return c;
            if (Plugin.Bundle != null && !NotInBundle.Contains(key))
            {
                c = Plugin.Bundle.LoadAsset<AudioClip>("assets/audio/" + key + ".ogg");
                if (c != null) { Clips[key] = c; ModLog.Verbose("Sound '" + key + "' loaded from bundle (" + c.length.ToString("0.00") + " s)"); return c; }
                NotInBundle.Add(key);
            }
            if (PoolHooks.Clips.TryGetValue(key, out c) && c != null) { Clips[key] = c; ModLog.Verbose("Sound '" + key + "' from WaterAndTarPools (" + c.length.ToString("0.00") + " s)"); return c; }
            if (Failed.Add(key)) ModLog.Verbose("Sound '" + key + "' is not available (not in the bundle" + (key == WaterSplash ? ", WaterAndTarPools not installed" : "") + ")");
            return null;
        }

        internal static void Fail(string key, string why)
        {
            Pending.Remove(key); Failed.Add(key);
            ModLog.Warn("Sound '" + key + "' unavailable (" + why + ") - using the synthesised sound.");
        }

        // --- sound ------------------------------------------------------------------------------------------------------
        // Routed through the same mixer group as the game's own effects (found on a vanilla sfx prefab) so the player's
        // sound-effect volume applies; without one the clip is simply played at EffectsVolume.
        internal static AudioMixerGroup Group()
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
            ModLog.Verbose("Sound: SFX mixer group " + (_group != null ? "'" + _group.name + "' of mixer '" + (_group.audioMixer != null ? _group.audioMixer.name : "?") + "'" : "not found (sounds play without a group)"));
            return _group;
        }

        // --- dry routing ([Effects] DrySound, default on) --------------------------------------------------------------------
        // The game's MasterMixer has an "SFX Reverb" on the SFX group; in its Default (outdoor) snapshot that reverb is loud (Reverb +293 mB, decay
        // 0.5 s; read from the AssetRipper copy of MasterMixer.mixer, 2026-10-20). Indoors / in the menu it is off. A recording of 5 strokes showed an
        // attenuated copy of every lever hit ~0.28 s and ~0.35 s after it: that was the echo. The parent group "Effects" (Master/Effects) has only an
        // attenuation, so the pump sounds go there; the player's sound-effects volume (exposed mixer parameter "SfxVol", set by the game's settings,
        // it belongs to the SFX group we skip) is applied to the AudioSource instead.
        const string SfxVolParam = "SfxVol";
        static AudioMixerGroup _dry;
        static bool _dryResolved;

        internal static AudioMixerGroup DryGroup()
        {
            if (_dryResolved) return _dry;
            var sfx = Group();
            if (sfx == null || sfx.audioMixer == null) return null;          // try again later (no world yet)
            _dryResolved = true;
            try
            {
                foreach (var g in sfx.audioMixer.FindMatchingGroups("Effects"))
                    if (g != null && g.name == "Effects") { _dry = g; break; }
            }
            catch (System.ArgumentException e) { ModLog.VerboseWarn("Sound: Effects group lookup refused: " + e.Message); }
            catch (System.Exception e) { ModLog.Warn("Sound: Effects group lookup failed, pump sounds keep the SFX reverb: " + e.Message); }
            ModLog.Verbose("Sound: dry group " + (_dry != null ? "'Effects' found, pump sounds play without the SFX reverb" : "NOT found, pump sounds use the SFX group (with its reverb)"));
            return _dry;
        }

        /// The game's sound-effects volume as a factor 0..1 (mixer parameter SfxVol, in dB). 1 when it cannot be read.
        internal static float SfxGain(AudioMixerGroup anyGroup)
        {
            float db;
            if (anyGroup == null || anyGroup.audioMixer == null || !anyGroup.audioMixer.GetFloat(SfxVolParam, out db))
            { ModLog.Verbose("Sound: '" + SfxVolParam + "' not readable, sound-effects volume taken as 100 %"); return 1f; }
            float gain = db <= -79.9f ? 0f : Mathf.Clamp01(Mathf.Pow(10f, db / 20f));
            ModLog.Verbose("Sound: sound-effects volume " + db.ToString("0.0") + " dB = " + (gain * 100f).ToString("0") + " %");
            return gain;
        }

        // One voice per clip: a new stroke fades the previous one out (0.08 s) instead of letting two copies ring over each other
        // (that overlap - the long reverb-like tail of pump_up on top of the next stroke - sounded like an echo).
        // tailSeconds > 0: the clip is faded out that long after its loudest moment (cuts the baked-in reverb tail); 0 = play it whole.
        internal static readonly Dictionary<AudioClip, SfxVoice> Voices = new();
        internal static readonly Dictionary<AudioClip, Vector2> Analysis = new();       // x = loudest moment (s, -1 = unknown), y = first audible moment (s)

        // AudioClip.GetData has a Span<float> overload in newer Unity; net48 has no Span<>, so the float[] overload is called via reflection (CS7069 otherwise).
        internal static readonly MethodInfo MGetData = typeof(AudioClip).GetMethod("GetData", new[] { typeof(float[]), typeof(int) });

        // One pass over the clip: where the loudest sample is (the main hit) and where the first sample above 5 % of that is (the recording may start with
        // silence / a very quiet lead-in; the pour sound is started from there, so what you HEAR starts when the stream starts).
        internal static Vector2 Analyze(AudioClip clip)
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

        internal static float PeakTime(AudioClip clip) { return Analyze(clip).x; }

        /// The pieces of one played sound beyond the basics: where in the clip to start, how fast, and where (clip time) it must be faded out by.
        internal struct Span
        {
            public float StartAt;       // clip time (s) to start from; < 0 = from 0 (or the first audible sample with skipLead)
            public float Pitch;         // playback speed (1 = as recorded); also shifts the pitch, keep it near 1
            public float EndAt;         // clip time (s) by which the sound is faded out; <= 0 = no limit
            public float Fade;          // real seconds of that fade
            public static Span Whole { get { return new Span { StartAt = -1f, Pitch = 1f, EndAt = -1f, Fade = 0.06f }; } }
        }

        // skipLead: start the clip at its first audible sample (see Analyze) instead of at 0
        internal static void Sound(Vector3 pos, AudioClip clip, float gain, float tailSeconds, bool skipLead = false)
        {
            Sound(pos, clip, gain, tailSeconds, skipLead, Span.Whole);
        }

        // T-069: the water splash has its own volume (WaterAndTarPools [Effects] EffectsVolume); -1 = [Effects] EffectsVolume of DrainTarPit
        static float _volumeOverride = -1f;

        /// Like Sound, with `volume` instead of [Effects] EffectsVolume. Returns the volume used.
        internal static float SoundWithVolume(Vector3 pos, AudioClip clip, float volume, float gain, float tailSeconds, bool skipLead)
        {
            _volumeOverride = Mathf.Clamp01(volume);
            try { Sound(pos, clip, gain, tailSeconds, skipLead, Span.Whole); }
            finally { _volumeOverride = -1f; }
            ModLog.Verbose("Sound: '" + (clip != null ? clip.name : "?") + "' with its own volume " + volume.ToString("0.00"));
            return volume;
        }

        internal static void Sound(Vector3 pos, AudioClip clip, float gain, float tailSeconds, bool skipLead, Span span)
        {
            if (clip == null) { ModLog.Verbose("Sound: nothing played, the clip is not loaded (yet)"); return; }
            // T-068 (user: "if set to off, then it should disable any audio effects from this mod"): every sound of the mod goes through here
            if (ModSettings.SoundEffects != null && !ModSettings.SoundEffects.Value) { ModLog.Verbose("Sound: '" + clip.name + "' not played ([Effects] SoundEffects off)"); return; }
            SfxVoice old;
            if (Voices.TryGetValue(clip, out old)) { if (old != null && old.Src != null && (old.PeakT < 0f || old.Src.time >= old.PeakT)) old.FadeOut(0.08f); Voices.Remove(clip); }   // only a copy that is already past its main hit is faded; one still building up to it plays on
            var go = new GameObject("DTP_sfx");
            go.transform.position = pos;
            var a = go.AddComponent<AudioSource>();
            float vol = Mathf.Clamp01((_volumeOverride >= 0f ? _volumeOverride : ModSettings.EffectsVolume.Value) * gain);
            a.clip = clip;
            a.spatialBlend = 1f; a.minDistance = 2f; a.maxDistance = 30f; a.rolloffMode = AudioRolloffMode.Linear;
            a.dopplerLevel = 0f; a.playOnAwake = false;
            var dry = ModSettings.DrySound.Value ? DryGroup() : null;
            var g = dry ?? Group();
            if (g != null) a.outputAudioMixerGroup = g;
            if (dry != null)
            {
                vol *= SfxGain(dry);                                   // the SFX group's volume (the player's slider) is skipped with it: apply it here
                a.bypassReverbZones = true; a.reverbZoneMix = 0f;     // no world reverb zones either
            }
            a.volume = vol;
            ModLog.Verbose("Sound '" + clip.name + "' at " + pos.ToString("0.0") + ": volume " + vol.ToString("0.00") + ", group " + (g != null ? g.name : "none") + (dry != null ? " (dry)" : "") +
                (tailSeconds > 0.01f ? ", tail fade " + tailSeconds.ToString("0.00") + " s" : ""));
            var voice = go.AddComponent<SfxVoice>();
            voice.Src = a; voice.Volume = vol;
            float peak = PeakTime(clip); voice.PeakT = peak;
            if (tailSeconds > 0.01f && peak >= 0f) { voice.FadeEnd = peak + tailSeconds; voice.FadeStart = peak + tailSeconds * 0.35f; }
            float pitch = Mathf.Clamp(span.Pitch <= 0f ? 1f : span.Pitch, 0.5f, 2f);
            a.pitch = pitch;
            if (span.EndAt > 0f && (voice.FadeEnd <= 0f || span.EndAt < voice.FadeEnd))
            {   // the sound must be gone by EndAt (clip time): the next part of the stroke starts there
                voice.FadeEnd = span.EndAt;
                voice.FadeStart = Mathf.Max(0f, span.EndAt - Mathf.Max(0.01f, span.Fade) * pitch);
            }
            float from = span.StartAt >= 0f ? span.StartAt : skipLead ? Analyze(clip).y : 0f;
            if (from > 0.005f)
            {
                try { a.timeSamples = Mathf.Clamp((int)(from * clip.frequency), 0, Mathf.Max(0, clip.samples - 1)); }
                catch (System.ArgumentException e) { ModLog.VerboseWarn("Sound '" + clip.name + "': start at " + from.ToString("0.00") + " s refused (" + e.Message + "), plays from 0"); from = 0f; }
            }
            if (span.StartAt >= 0f || span.EndAt > 0f || pitch != 1f)
                ModLog.Verbose("Sound '" + clip.name + "': clip time " + from.ToString("0.00") + " - " + (voice.FadeEnd > 0f ? voice.FadeEnd.ToString("0.00") : clip.length.ToString("0.00")) + " s at speed " + pitch.ToString("0.00"));
            Voices[clip] = voice;
            a.Play();
            Object.Destroy(go, (clip.length - from) / pitch + 0.2f);
        }

        /// Drives the volume of one sound: tail fade-out and the quick fade when a newer copy takes over.
        internal sealed class SfxVoice : MonoBehaviour
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

        // --- synthesised clips ---------------------------------------------------------------------------------------------
        const int Rate = 22050;

        internal static AudioClip MakeStroke()
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

        internal static AudioClip Make(string name, float[] d)
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
