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
    }
}
