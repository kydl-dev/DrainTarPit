using UnityEngine;

namespace DrainTarPit.Pump
{
    /// Which player clip belongs to a pump phase (the lower-case names PlayerPumpAnimations knows) and how long one stroke takes.
    internal static class PumpClips
    {
        public static string ClipOf(PumpPhase p)
        {
            switch (p)
            {
                case PumpPhase.Enter: return "pumpenter";
                case PumpPhase.Idle: return "pumpidle";
                case PumpPhase.Push: return "pumppush";
                case PumpPhase.Pull: return "pumppull";
                case PumpPhase.Cycle: return "pumpcycle";
                case PumpPhase.Exit: return "pumpexit";
            }
            return null;
        }

        public static float StrokeLength() { return Mathf.Max(0.1f, PlayerPumpAnimations.Length("pumppush") + PlayerPumpAnimations.Length("pumppull")); }

        // When the piston moves (for the stroke sounds, PumpFx.StrokeTiming). Read from the pump's clips in Unity (PistonRod local y, 2026-10-20):
        // Push 0.5 s = piston from its lowest to its highest point, Pull 0.633 s = back down; in the 1.2 s Cycle loop the top is reached at about
        // 42 % of the loop, the bottom at its end. The player's clips have the same lengths; those lengths are used when the clips are found.
        const float FallbackPush = 0.5f, FallbackPull = 0.633f, CycleTopAt = 0.42f;

        /// A single stroke (tap): Push then Pull, starting now.
        public static Effects.PumpFx.StrokeTiming TapTiming()
        {
            float push = PlayerPumpAnimations.Length("pumppush"), pull = PlayerPumpAnimations.Length("pumppull");
            if (push <= 0.05f || pull <= 0.05f) Kydl.Common.ModLog.Verbose("Pump timing: player clips not found (push " + push.ToString("0.00") + " s, pull " + pull.ToString("0.00") + " s), the pump clip lengths are used");
            return new Effects.PumpFx.StrokeTiming { StartIn = 0f, Up = push > 0.05f ? push : FallbackPush, Down = pull > 0.05f ? pull : FallbackPull };
        }

        /// One Cycle loop that starts `startIn` seconds from now (the decision to go on is taken shortly before the running loop ends).
        public static Effects.PumpFx.StrokeTiming CycleTiming(float startIn)
        {
            float len = PlayerPumpAnimations.Length("pumpcycle");
            if (len < 0.2f) { Kydl.Common.ModLog.Verbose("Pump timing: player cycle clip not found, [Pump] CycleStrokeSeconds " + ModSettings.CycleStrokeSeconds.Value.ToString("0.00") + " s used"); len = ModSettings.CycleStrokeSeconds.Value; }
            return new Effects.PumpFx.StrokeTiming { StartIn = Mathf.Max(0f, startIn), Up = len * CycleTopAt, Down = len * (1f - CycleTopAt) };
        }
    }
}
