using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using static DrainTarPit.Drain.TarPits;

namespace DrainTarPit.Drain
{
    /// <summary>
    /// Flow boost: raises the viscosity of a pool while it is poured into so the liquid spreads quickly, and the live base-viscosity setting.
    /// </summary>
    internal static class TarPitFlow
    {
        /// Live tuning (editor): sets the base viscosity (the pool's own flow value without the pouring boost). A boost that is running keeps its factor.
        internal static void SetBaseViscosity(LiquidVolume v, float value)
        {
            if (v == null || FVisc == null || value <= 0f) return;
            float[] b;
            if (Boosted.TryGetValue(v, out b)) { b[0] = value; FVisc.SetValue(v, value * Mathf.Max(1f, ModSettings.PitFlowBoost.Value)); }
            else FVisc.SetValue(v, value);
        }

        // --- flow boost ---------------------------------------------------------------------------------------------------
        // The pit simulation moves tar between neighbouring cells with min(sqrt(height difference * m_viscocity), difference / 2) per step,
        // so a bigger m_viscocity makes the rest of the pit run towards the hole faster. While a pit is being pumped (and for
        // PitFlowBoostSeconds after) the value is multiplied by [Drain] PitFlowBoost; afterwards the original is put back.
        static readonly FieldInfo FVisc = AccessTools.Field(typeof(LiquidVolume), "m_viscocity");
        internal static readonly Dictionary<LiquidVolume, float[]> Boosted = new();     // pit -> { original viscosity, boost ends at (Time.time) }

        internal static void BoostFlow(LiquidVolume v)
        {
            float k = ModSettings.PitFlowBoost.Value, secs = ModSettings.PitFlowBoostSeconds.Value;
            if (FVisc == null || k <= 1.001f || secs <= 0f) return;
            float[] b;
            if (!Boosted.TryGetValue(v, out b))
            {
                float orig = (float)FVisc.GetValue(v);
                if (orig <= 0f) return;
                Boosted[v] = b = new[] { orig, 0f };
                FVisc.SetValue(v, orig * k);
            }
            b[1] = Time.time + secs;
        }

        internal static void UnboostIfDone(LiquidVolume v, bool force)
        {
            float[] b;
            if (!Boosted.TryGetValue(v, out b) || (!force && Time.time < b[1])) return;
            if (v != null && FVisc != null) FVisc.SetValue(v, b[0]);
            Boosted.Remove(v);
        }
    }
}
