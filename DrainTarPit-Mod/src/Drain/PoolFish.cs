using System.Runtime.CompilerServices;
using DrainTarPit.Logging;
using HarmonyLib;
using UnityEngine;

namespace DrainTarPit.Drain
{
    /// <summary>
    /// Fish in pump water pools (user, 2026-10-20: "fish don't swim in the water in my pool ... they just jump on the floor").
    /// Why (read from the game's Fish code): a fish only knows it is in water through SetLiquidLevel, which the liquid's trigger calls. A pump pool is a
    /// LiquidVolume whose LiquidSurface trigger is a thin sheet at the surface, so a fish UNDER the surface is never inside it: m_inWater stays -10000,
    /// IsOutOfWater() is true, gravity is switched on and the fish flops on the pool floor. And for swimming the fish asks GetWaterLevel(), which without
    /// a WaterVolume (sea / lake) returns the fixed 30 m sea level, so in a pool above the sea every waypoint looks like dry land.
    /// Fix: while a fish is inside a pump WATER pool (its x/z on the grid with some water, and not far above the surface) its water level is the
    /// pool surface, every physics step before the fish's own update; GetWaterLevel answers the pool surface for points over the pool. Fish in the
    /// sea / lakes (a WaterVolume) are never touched. Leaving the pool hands the fish back to the game (water level -10000 = out of water).
    /// </summary>
    internal static class PoolFish
    {
        const float MinWater = 0.05f;           // m of water at the fish's x/z that counts as "in the pool"
        const float AboveSurface = 2f;          // a fish this far above the surface (a jump) still belongs to the pool
        const int RecheckEvery = 5;             // physics steps between two pool look-ups of one fish

        sealed class State { public bool InPool; public float Surface = -10000f; public int Countdown; public string Pool = ""; }
        static readonly ConditionalWeakTable<Fish, State> States = new ConditionalWeakTable<Fish, State>();

        static readonly AccessTools.FieldRef<Fish, float> InWater = AccessTools.FieldRefAccess<Fish, float>("m_inWater");
        static readonly AccessTools.FieldRef<Fish, WaterVolume> WaterVol = AccessTools.FieldRefAccess<Fish, WaterVolume>("m_waterVolume");

        /// The pool surface over world point p, if p is over a pump water pool with water. Expected outcome: true exactly when a surface was found.
        internal static bool PoolSurface(Vector3 p, out float surface, out string pool)
        {
            surface = -10000f; pool = "";
            LiquidVolume v; float depth, s;
            if (!TarPits.TrySample(p, out v, out depth, out s, LiquidType.Water)) return false;
            bool found = v != null && TarPits.IsPumpPool(v) && depth >= MinWater && s > -9999f && p.y <= s + AboveSurface;
            if (found) { surface = s; pool = v.name; }
            return found;
        }

        [HarmonyPatch(typeof(Fish), nameof(Fish.CustomFixedUpdate))]
        static class Patch_FixedUpdate
        {
            static void Prefix(Fish __instance)
            {
                try
                {
                    if (__instance == null || WaterVol(__instance) != null) return;          // sea / lake fish: the game handles them
                    var st = States.GetOrCreateValue(__instance);
                    if (--st.Countdown <= 0)
                    {
                        st.Countdown = RecheckEvery;
                        float surface; string pool;
                        bool inPool = PoolSurface(__instance.transform.position, out surface, out pool);
                        if (inPool != st.InPool)
                            ModLog.Verbose("Fish " + __instance.name + (inPool ? " is in pump pool " + pool + ", water level " + surface.ToString("0.00") : " left pump pool " + st.Pool + ", handed back to the game") +
                                " at " + __instance.transform.position.ToString("0.0"));
                        if (!inPool && st.InPool && Mathf.Approximately(InWater(__instance), st.Surface)) InWater(__instance) = -10000f;   // our level, not the game's: out of water again
                        st.InPool = inPool; st.Surface = surface; st.Pool = pool;
                    }
                    if (st.InPool) InWater(__instance) = st.Surface;
                }
                catch (System.MissingFieldException e) { ModLog.Warn("PoolFish: a Fish field is missing in this game version (" + e.Message + "), fish in pools are not helped"); }
                catch (System.Exception e) { ModLog.VerboseWarn("PoolFish: " + e.Message); }
            }
        }

        [HarmonyPatch(typeof(Fish), "GetWaterLevel")]
        static class Patch_GetWaterLevel
        {
            static void Postfix(Fish __instance, Vector3 point, ref float __result)
            {
                try
                {
                    if (__instance == null || WaterVol(__instance) != null) return;
                    State st;
                    if (!States.TryGetValue(__instance, out st) || !st.InPool) return;     // only fish we know are in a pool ask the pools
                    float surface; string pool;
                    var probe = new Vector3(point.x, st.Surface, point.z);                  // waypoints carry a made-up y: test at the surface height
                    __result = PoolSurface(probe, out surface, out pool) ? surface : -10000f; // outside the pool's water = dry (the fish does not swim there)
                }
                catch (System.Exception e) { ModLog.VerboseWarn("PoolFish water level: " + e.Message); }
            }
        }
    }
}
