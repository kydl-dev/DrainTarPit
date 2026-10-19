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
using static DrainTarPit.Effects.PumpFxSound;
using static DrainTarPit.Effects.PumpFxStream;

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
    ///
    /// Split by responsibility (T-016): this class = the public entry points (Stroke, Pour, PourWhenArrives, Preload); PumpFxSound = recordings,
    /// voices and the synthesised fallback; PumpFxStream = the spurt / splashes / puddle / droplets particle systems.
    /// </summary>
    internal static class PumpFx
    {
        static AudioClip _stroke;                // synthesised fallback of the lever sound

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

        /// Loads the sounds at startup so the first stroke is not silent (no-op on a dedicated server / effects off).
        public static void Preload()
        {
            if (!Enabled) return;
            Clip(PumpUp); Clip(OilPumped);
        }

        static bool _failed;
        static void Fail(System.Exception e) { if (_failed) return; _failed = true; ModLog.Warn("Effects disabled after an error: " + e.Message); _disabled = true; }
    }
}
