using UnityEngine;

namespace DrainTarPit.Drain
{
    /// <summary>
    /// Where runoff (liquid that reached the border of a pool's grid) may still go on into the next pool (TarPool.Continue). User, 2026-10-20 (D-029):
    /// overflow runs down to the low point and collects there, but only within MaxRunoffDistance of where the stroke was poured; further on it vanishes
    /// at the ground. It never flows back towards where it was poured (two pools on a slope used to hand it back and forth until the hop limit, T-21),
    /// and reaching the sea or a river (ground under the world's water level) ends it there.
    /// </summary>
    internal static class RunoffRules
    {
        public const float MaxRunoffDistance = 20f;             // m, horizontal, from the landing point of the player's stroke

        static readonly System.Reflection.FieldInfo FWaterLevel = HarmonyLib.AccessTools.Field(typeof(ZoneSystem), "m_waterLevel");

        static float Flat(Vector3 v) { return new Vector2(v.x, v.z).magnitude; }

        static float SeaLevel()
        {
            var o = FWaterLevel != null && ZoneSystem.instance != null ? FWaterLevel.GetValue(ZoneSystem.instance) : null;
            return o is float f ? f : 30f;
        }

        /// `into` = where the runoff would land in the next pool, `origin` = where the stroke was poured, `prevAt` = where it landed in the pool it leaves.
        internal static bool Allowed(Vector3 into, Vector3 origin, Vector3 prevAt, out string why)
        {
            float far = Flat(into - origin);
            if (far > MaxRunoffDistance) { why = "stops " + far.ToString("0.0") + " m from where it was poured (limit " + MaxRunoffDistance + " m) - vanishes at the ground"; return false; }
            if (far <= Flat(prevAt - origin) + 0.25f) { why = "would flow back towards where it was poured - vanishes here"; return false; }
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(into) < SeaLevel()) { why = "reached the sea / a river - joins it"; return false; }
            why = "";
            return true;
        }
    }
}
