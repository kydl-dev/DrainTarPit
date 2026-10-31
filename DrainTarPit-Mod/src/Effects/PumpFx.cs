using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DrainTarPit.Building;
using Kydl.Common;
using DrainTarPit.Visuals;
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
    /// The recordings come from the asset bundle (assets/audio/*.ogg; the water splash from WaterAndTarPools' bundle). Without pump_up a
    /// synthesised sound is used. Droplets are a code-built ParticleSystem. Everything is skipped on a
    /// dedicated server / -nographics (nothing to show or hear there; connected players get them on their own PCs) and while [General] EnableAll is off; sounds also need [Effects] SoundEffects.
    ///
    /// Split by responsibility (T-016): this class = the public entry points (Stroke, Pour, PourWhenArrives, Preload); PumpFxSound = recordings,
    /// voices and the synthesised fallback; PumpFxStream = the spurt / splashes / puddle / droplets particle systems.
    /// </summary>
    internal static class PumpFx
    {
        static AudioClip _stroke;                // synthesised fallback of the lever sound

        static bool _disabled;      // set after an error; runtime only, the config file is left alone
        static bool Enabled { get { return !_disabled && ModSettings.EffectsOn && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null; } }   // T-068: sounds have their own switch (PumpFxSound.Sound)

        /// When the piston moves during one stroke, from the moment PumpFx.Stroke is called: it starts rising after StartIn seconds, rises for Up
        /// seconds (lever pushed down: Push clip / first part of a Cycle loop), then falls for Down seconds (Pull / rest of the loop) and is at its
        /// lowest (lever at its highest) at StartIn + Up + Down. Measured from the pump's own clips (DTP_Pump_Push / Pull / Cycle, PistonRod y).
        internal struct StrokeTiming
        {
            public float StartIn, Up, Down;
            public float BottomAt { get { return StartIn + Up + Down; } }
            public override string ToString() { return "start in " + StartIn.ToString("0.00") + " s, up " + Up.ToString("0.00") + " s, down " + Down.ToString("0.00") + " s"; }
        }

        const float HitLead = 0.08f;            // the pump_up hit is started this long before its loudest moment, so that moment lands on the bottom
        const float MinSpeed = 0.85f, MaxSpeed = 1.25f;   // how far a scratch may be sped up / slowed down to fit its half of the stroke

        /// One stroke. With [Effects] ScratchSounds (default on): the 'up' scratch while the piston rises, the 'down' scratch while it falls, and the
        /// pump_up hit exactly when it reaches the bottom (user, 2026-10-20). Off: the old single pump_up recording from the start of the stroke.
        public static void Stroke(Transform at, StrokeTiming timing)
        {
            if (!Enabled || at == null) { ModLog.Verbose("Pump sound: stroke not played (" + (at == null ? "no fx point" : "effects off") + ")"); return; }
            try
            {
                if (ModSettings.PumpDroplets.Value) Droplets(at.position, Vector3.up, 8, 0.6f);   // off by default: it looked like oil dripping at the pump outlet / first hose joint
                var clip = Clip(PumpUp);               // pump_up: the lever being pressed
                if (clip == null && !Loading(PumpUp)) clip = _stroke != null ? _stroke : (_stroke = MakeStroke());   // fallback: synthesised
                var pos = at.position;
                if (!ModSettings.ScratchSounds.Value)
                {
                    ModLog.Verbose("Pump sound: single pump_up recording (ScratchSounds off)");
                    RunAfter(timing.StartIn, () => Sound(pos, clip, 1f, ModSettings.SoundTailSeconds.Value));
                    return;
                }
                ModLog.Verbose("Pump sound: stroke " + timing);
                Scratch(pos, ScratchUp, timing.StartIn, timing.Up);
                Scratch(pos, ScratchDown, timing.StartIn + timing.Up, timing.Down);
                float peak = clip != null ? PeakTime(clip) : -1f;
                if (clip == null) { ModLog.Verbose("Pump sound: pump_up not loaded yet, no hit this stroke"); return; }
                if (peak < 0f)
                {   // loudest moment unknown (synthesised fallback / unreadable clip): start it at the bottom
                    RunAfter(timing.BottomAt, () => Sound(pos, clip, 1f, ModSettings.SoundTailSeconds.Value));
                    return;
                }
                float from = Mathf.Max(0f, peak - HitLead);
                var span = Span.Whole; span.StartAt = from;
                RunAfter(Mathf.Max(0f, timing.BottomAt - (peak - from)), () => Sound(pos, clip, 1f, ModSettings.SoundTailSeconds.Value, false, span));
            }
            catch (System.Exception e) { Fail(e); }
        }

        /// One half of the scratching recording, fitted into `seconds`: sped up / slowed down within MinSpeed..MaxSpeed, the rest faded out at the end.
        static void Scratch(Vector3 pos, string key, float startIn, float seconds)
        {
            var clip = Clip(key);
            if (clip == null) { ModLog.Verbose("Pump sound: '" + key + "' " + (Loading(key) ? "still loading" : "not available") + ", skipped"); return; }
            if (seconds < 0.05f) { ModLog.Verbose("Pump sound: '" + key + "' skipped, its part of the stroke is only " + seconds.ToString("0.00") + " s"); return; }
            var span = Span.Whole;
            span.StartAt = 0f;
            span.Pitch = Mathf.Clamp(clip.length / seconds, MinSpeed, MaxSpeed);
            span.EndAt = seconds * span.Pitch;                // clip time reached when this part of the stroke is over
            span.Fade = Mathf.Min(0.06f, seconds * 0.3f);
            RunAfter(startIn, () => Sound(pos, clip, 1f, 0f, false, span));
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
            WaterColour? style = liquid == Drain.PumpLiquid.Water ? PoolHooks.ColourFor(pump, null, pos) : (WaterColour?)null;     // T-60 / T-069: the pump's pool style, as this player sees it (WaterAndTarPools)
            RunAfter(tVisual, () => { PourVisual(pos, dir, volume, liquid, style); if (atNozzle != null) atNozzle(); });
            RunAfter(tSound, () => PourSound(pos, liquid));
            ModLog.Verbose("Pour: " + (delay >= 0f ? "follows the tar slug, reaches the nozzle in " + delay.ToString("0.00") + " s" : "no slug to follow, right away"));
        }

        static void RunAfter(float seconds, System.Action what)
        {
            if (seconds <= 0.001f) what(); else TarFlow.Schedule(seconds, what);
        }

        static void PourVisual(Vector3 pos, Vector3 dir, float volume, Drain.PumpLiquid liquid, WaterColour? style = null)
        {
            if (!Enabled) return;
            try
            {
                float frac = Mathf.Clamp01(volume / Mathf.Max(0.01f, ModSettings.VolumePerStroke.Value));
                float amount = ModSettings.NozzleFlowAmount.Value;
                if (amount <= 0.001f) { ModLog.Verbose("Pour: NozzleFlowAmount is 0, no stream (and no splash)"); return; }
                Vector3 land; float fall;
                Stream(pos, dir, frac, amount, liquid, out land, out fall, style);
                if (liquid == Drain.PumpLiquid.Water) WaterSplashAt(land, fall, frac);
            }
            catch (System.Exception e) { Fail(e); }
        }

        // User, 2026-10-20: the recorded water splash when the water out of the nozzle hits something (the pool, the ground, a wall), played where
        // and when the first droplets come down. It replaces the silence of water pours; the synthesised splash stays removed. Tar keeps oil_pumped.
        static void WaterSplashAt(Vector3 land, float fall, float frac)
        {
            if (!ModSettings.WaterSplashSound.Value) { ModLog.Verbose("Pour: water splash off ([Effects] WaterSplashSound)"); return; }
            var clip = Clip(WaterSplash);
            if (clip == null) { ModLog.Verbose("Pour: water_splash " + (Loading(WaterSplash) ? "still loading" : "not available") + ", no splash sound"); return; }
            float gain = Mathf.Lerp(0.6f, 1f, frac);                    // a stroke that brought up less water splashes a bit softer
            float at = Mathf.Max(0f, fall + ModSettings.WaterSplashOffset.Value);   // fine tuning by ear (T-40)
            ModLog.Verbose("Pour: water splash at " + land.ToString("0.0") + " in " + at.ToString("0.00") + " s (fall " + fall.ToString("0.00") + " s, offset " +
                ModSettings.WaterSplashOffset.Value.ToString("0.00") + " s), gain " + gain.ToString("0.00"));
            RunAfter(at, () => SoundWithVolume(land, clip, ModSettings.WaterSplashVolume.Value, gain, 0f, true));   // T-069: its own volume (WaterAndTarPools)
        }

        static void PourSound(Vector3 pos, Drain.PumpLiquid liquid)
        {
            if (!Enabled) return;
            try
            {
                // No sound AT THE NOZZLE for water: its sound is the recorded water_splash where the stream lands (WaterSplashAt, user 2026-10-20).
                // The synthesised splash that used to play here (and as the oil fallback) was removed on purpose - never bring it back.
                if (liquid == Drain.PumpLiquid.Water) { ModLog.Verbose("Pour: no nozzle sound for water (the splash plays where it lands)"); return; }
                if (!ModSettings.TarSplashSound.Value) { ModLog.Verbose("Pour: tar pour sound off ([Effects] TarSplashSound)"); return; }
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
            Clip(PumpUp); Clip(OilPumped); Clip(ScratchUp); Clip(ScratchDown); Clip(WaterSplash);
        }

        static bool _failed;
        static void Fail(System.Exception e) { if (_failed) return; _failed = true; ModLog.Warn("Effects disabled after an error: " + e.Message); _disabled = true; }
    }
}
