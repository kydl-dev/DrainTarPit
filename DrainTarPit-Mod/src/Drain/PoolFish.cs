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

        const int LeaveAfter = 4;               // pool look-ups in a row that must miss before a fish counts as out of the pool
        const float ReportEvery = 3f;           // s between two debug state lines of one fish in a pool

        sealed class State
        {
            public bool InPool; public float Surface = -10000f; public int Countdown, Misses; public string Pool = "";
            public float NextReport, NextTry; public int LevelAsks, DryAsks, WaypointTries, WaypointOk, Flees, GameCalls, Foreign, SeaOverrides; public Vector3 OurTarget = new Vector3(float.NaN, 0f, 0f); public string LastTry = "";
        }

        static readonly AccessTools.FieldRef<Fish, bool> HaveWaypoint = AccessTools.FieldRefAccess<Fish, bool>("m_haveWaypoint");
        static readonly AccessTools.FieldRef<Fish, bool> IsJumping = AccessTools.FieldRefAccess<Fish, bool>("m_isJumping");
        static readonly AccessTools.FieldRef<Fish, Vector3> Waypoint = AccessTools.FieldRefAccess<Fish, Vector3>("m_waypoint");
        static readonly AccessTools.FieldRef<Fish, Vector3> SpawnPoint = AccessTools.FieldRefAccess<Fish, Vector3>("m_spawnPoint");
        static readonly AccessTools.FieldRef<Fish, Rigidbody> Body = AccessTools.FieldRefAccess<Fish, Rigidbody>("m_body");
        static readonly AccessTools.FieldRef<Fish, ZNetView> Nview = AccessTools.FieldRefAccess<Fish, ZNetView>("m_nview");
        static readonly AccessTools.FieldRef<Fish, int> Colliding = AccessTools.FieldRefAccess<Fish, int>("m_isColliding");

        // T-43 debugger (user standard: track everything when DebugLog is on): what the fish is doing, every ReportEvery s while it is in a pool.
        // LevelAsks = how often the game asked GetWaterLevel through our patch (0 = the call never reaches the patch), WaypointTries / Ok = swim targets.
        static void Report(Fish f, State st)
        {
            var b = Body(f);
            ModLog.Verbose("Fish " + f.name + " @" + f.GetInstanceID() + " in " + st.Pool + ": y " + f.transform.position.y.ToString("0.00") + ", water " + st.Surface.ToString("0.00") +
                ", gravity " + (b != null && b.useGravity) + ", speed " + (b != null ? b.linearVelocity.magnitude.ToString("0.00") : "?") + ", jumping " + IsJumping(f) +
                ", waypoint " + (HaveWaypoint(f) ? Waypoint(f).ToString("0.0") : "none") + ", spawn " + SpawnPoint(f).ToString("0.0") + ", minDepth " + f.m_minDepth.ToString("0.0") +
                ", swimRange " + f.m_swimRange.ToString("0") + " | water level asks " + st.LevelAsks + " (" + st.DryAsks + " answered dry), swim targets " + st.WaypointOk + " of " + st.WaypointTries + " tries" +
                (st.LastTry.Length > 0 ? ", last refused: " + st.LastTry : "") + (st.Flees > 0 ? ", fled from a player " + st.Flees + " steps (land check skipped)" : "") +
                " | owner " + (Nview(f) != null && Nview(f).IsOwner()) + ", water level now " + InWater(f).ToString("0.00") + ", out of water " + f.IsOutOfWater() + ", swim timer " + SwimTimer(f).ToString("0.00") +
                ", touching " + Colliding(f) + ", game asked for a target " + st.GameCalls + "x, foreign targets replaced " + st.Foreign + ", sea water level overridden " + st.SeaOverrides + "x");
            st.LevelAsks = 0; st.DryAsks = 0; st.WaypointTries = 0; st.WaypointOk = 0; st.Flees = 0; st.GameCalls = 0; st.Foreign = 0; st.SeaOverrides = 0; st.LastTry = "";
        }

        // T-44 (debugger, log 19:51): "water level asks 0" while "swim targets 0 of 60 tries" - inside RandomizeWaypoint the game's GetPointDepth /
        // GetWaterLevel calls never reached the GetWaterLevel patch (most likely inlined by the JIT, so a patch on that small method is skipped there);
        // every target looked like dry land (sea level 30 m under the pool) and was refused. So for a fish in a pump pool RandomizeWaypoint itself is
        // replaced: the same rules as the game (a point within m_swimRange of the spawn point, at least m_minDepth of water there AND halfway there,
        // swim depth between m_minDepth and m_maxDepth under the surface), but the depth is the pool's. Up to TargetTries points per call (the pool is
        // small compared to the swim range). Fish in the sea / lakes run the game's own code. Fishing floats are not looked for in pools (yet).
        const int TargetTries = 8;
        static readonly AccessTools.FieldRef<Fish, FishingFloat> WaypointFF = AccessTools.FieldRefAccess<Fish, FishingFloat>("m_waypointFF");
        static readonly AccessTools.FieldRef<Fish, float> SwimTimer = AccessTools.FieldRefAccess<Fish, float>("m_swimTimer");
        static readonly AccessTools.FieldRef<Fish, float> BlockChange = AccessTools.FieldRefAccess<Fish, float>("m_blockChange");

        /// Water depth of the pool at x/z (surface - floor under it), or -1 when there is no pool water there.
        static float PoolDepth(float x, float z, float surfaceHint, out float surface)
        {
            string pool; float floor;
            if (!PoolSurface(new Vector3(x, surfaceHint, z), out surface, out pool)) return -1f;
            if (ZoneSystem.instance == null || !ZoneSystem.instance.GetSolidHeight(new Vector3(x, surface, z), out floor, 1)) return -1f;  // ray from 1 m over the surface: hoses / roofs above do not count
            return surface - floor;
        }

        /// A swim target in the pool for this fish (the game's rules, the pool's depth). Expected outcome: true exactly when the fish has a target
        /// that this code set (st.OurTarget); false = none found this time (the fish holds still and it is tried again next step).
        static bool PickTarget(Fish f, State st, float timeNow)
        {
            st.WaypointTries++;
            if (IsJumping(f)) { st.LastTry = "jumping"; return HaveWaypoint(f) && Waypoint(f) == st.OurTarget; }
            var pos = f.transform.position; var spawn = SpawnPoint(f);
            for (int i = 0; i < TargetTries; i++)
            {
                var c = Random.insideUnitCircle * f.m_swimRange;
                float sx = spawn.x + c.x, sz = spawn.z + c.y, surface, mid;
                float depth = PoolDepth(sx, sz, st.Surface, out surface);
                if (depth < f.m_minDepth) { st.LastTry = "target (" + sx.ToString("0.0") + ", " + sz.ToString("0.0") + ") " + (depth < 0f ? "outside the pool's water" : "only " + depth.ToString("0.00") + " m deep"); continue; }
                float midDepth = PoolDepth((sx + pos.x) * 0.5f, (sz + pos.z) * 0.5f, st.Surface, out mid);
                if (midDepth < f.m_minDepth) { st.LastTry = "the way to (" + sx.ToString("0.0") + ", " + sz.ToString("0.0") + ") is only " + midDepth.ToString("0.00") + " m deep halfway"; continue; }
                float deepest = Mathf.Min(f.m_maxDepth, depth - f.m_height);
                var wp = new Vector3(sx, surface - Random.Range(f.m_minDepth, Mathf.Max(f.m_minDepth, deepest)), sz);
                Waypoint(f) = wp; WaypointFF(f) = null; HaveWaypoint(f) = true;
                SwimTimer(f) = Random.Range(f.m_wpDurationMin, f.m_wpDurationMax);
                BlockChange(f) = timeNow + Random.Range(f.m_blockChangeDurationMin, f.m_blockChangeDurationMax);
                st.OurTarget = wp; st.WaypointOk++;
                return HaveWaypoint(f) && Waypoint(f) == st.OurTarget;
            }
            HaveWaypoint(f) = false;                                      // an old / foreign target would lead out of the water: hold still instead
            return HaveWaypoint(f);
        }

        // T-45 (log 20:15): even with RandomizeWaypoint patched, the fish's own update kept calling the game's original (its targets had the spawn
        // height 39.9 and our counter stayed 0), so the call from the fish's update does not reach the patch either. The swim target is therefore
        // also driven from the fixed-update prefix, which does run (every log line comes from it): a new target when the fish has none, when its
        // target is not one this code set (the game's own, possibly in the air / on land), or just before the game would pick one (swim timer ends).
        static void DriveTarget(Fish f, State st)
        {
            var nv = Nview(f);
            if (nv == null || !nv.IsValid() || !nv.IsOwner()) return;           // only the owner moves the fish
            bool have = HaveWaypoint(f), ours = have && Waypoint(f) == st.OurTarget;
            if (have && !ours) st.Foreign++;
            if (have && ours && SwimTimer(f) > Time.fixedDeltaTime * 2f) return;
            if (!ours && have) HaveWaypoint(f) = false;                          // never follow a target that was not checked against the pool
            if (Time.time < st.NextTry) return;                                  // no target found a moment ago: up to 16 rays per try, so not every step
            if (!PickTarget(f, st, Time.time)) st.NextTry = Time.time + RetryAfter;
        }

        const float RetryAfter = 0.25f;          // s between two target searches that found nothing

        [HarmonyPatch(typeof(Fish), "RandomizeWaypoint")]
        static class Patch_RandomizeWaypoint
        {
            static bool Prefix(Fish __instance, float timeNow, ref bool __result)
            {
                try
                {
                    State st;
                    if (__instance == null || WaterVol(__instance) != null || !States.TryGetValue(__instance, out st) || !st.InPool) return true;   // the game's code
                    st.GameCalls++;
                    __result = PickTarget(__instance, st, timeNow);
                    return false;
                }
                catch (System.MissingFieldException e) { ModLog.Warn("PoolFish: a Fish field is missing in this game version (" + e.Message + "), the game's own swim targets are used"); return true; }
                catch (System.Exception e) { ModLog.VerboseWarn("PoolFish waypoint: " + e.Message + " (the game's own code runs)"); return true; }
            }
        }
        static readonly ConditionalWeakTable<Fish, State> States = new ConditionalWeakTable<Fish, State>();

        static readonly AccessTools.FieldRef<Fish, float> InWater = AccessTools.FieldRefAccess<Fish, float>("m_inWater");
        static readonly AccessTools.FieldRef<Fish, WaterVolume> WaterVol = AccessTools.FieldRefAccess<Fish, WaterVolume>("m_waterVolume");

        /// The pool surface over world point p, if p is over a pump water pool with water. Expected outcome: true exactly when a surface was found.
        /// T-43: TarPits.TrySample picks the pool with the MOST water at that x/z, which near two overlapping pools (here: a pool and the lower runoff
        /// pool next to it, log 19:42) can be a pool far below the fish. Now only pools whose surface is not under the fish (by more than the fish's
        /// own height) count, and of those the lowest surface wins (the water the fish is really in).
        internal static bool PoolSurface(Vector3 p, out float surface, out string pool)
        {
            surface = -10000f; pool = "";
            LiquidVolume best = null, jump = null; float bestS = float.MaxValue, jumpS = float.MinValue;
            foreach (var v in TarPits.All)
            {
                if (v == null || v.m_liquidType != LiquidType.Water || !TarPits.IsPumpPool(v)) continue;
                var g = TarPits.ToGrid(v, p);
                if (!TarPits.InGrid(v, g)) continue;
                float depth;
                using (TarPits.Hold(v))
                {
                    var d = TarPits.Depths(v);
                    if (d == null || d.Count < (v.m_width + 1) * (v.m_width + 1)) continue;
                    depth = TarPits.DepthAt(v, d, g.x, g.y);
                }
                if (depth < MinWater) continue;
                float s = v.GetSurface(p);
                if (s < -9999f || s > p.y + 50f) continue;                            // no surface here, or far above (a pool on a higher floor)
                if (s >= p.y - 0.4f) { if (s < bestS) { bestS = s; best = v; } }       // the fish is in this water: the lowest such surface wins
                else if (s >= p.y - AboveSurface && s > jumpS) { jumpS = s; jump = v; } // the fish is a little above this surface (a jump)
            }
            if (best == null && jump != null) { best = jump; bestS = jumpS; }
            if (best == null) return best != null;
            surface = bestS; pool = best.name;
            return best != null;
        }

        [HarmonyPatch(typeof(Fish), nameof(Fish.CustomFixedUpdate))]
        static class Patch_FixedUpdate
        {
            static void Prefix(Fish __instance)
            {
                try
                {
                    if (__instance == null) return;
                    // T-46: NOT skipped when the fish has a WaterVolume: a pool on the coast lies inside the sea's water trigger, which kept setting the
                    // fish's water level to the sea (~30 m, under the pool floor). A fish over pump-pool water whose surface is higher belongs to the pool.
                    var st = States.GetOrCreateValue(__instance);
                    if (--st.Countdown <= 0)
                    {
                        st.Countdown = RecheckEvery;
                        float surface; string pool;
                        bool inPool = PoolSurface(__instance.transform.position, out surface, out pool);
                        // T-43: a fish touching a wall / the floor edge is briefly over a dry grid point; it only leaves after LeaveAfter misses in a row
                        if (!inPool && st.InPool && ++st.Misses < LeaveAfter) { inPool = true; surface = st.Surface; pool = st.Pool; }
                        else if (inPool) st.Misses = 0;
                        if (inPool != st.InPool)
                            ModLog.Verbose("Fish " + __instance.name + (inPool ? " is in pump pool " + pool + ", water level " + surface.ToString("0.00") : " left pump pool " + st.Pool + ", handed back to the game") +
                                " at " + __instance.transform.position.ToString("0.0"));
                        if (!inPool && st.InPool && Mathf.Approximately(InWater(__instance), st.Surface)) InWater(__instance) = -10000f;   // our level, not the game's: out of water again
                        st.InPool = inPool; st.Surface = surface; st.Pool = pool;
                    }
                    if (st.InPool && WaterVol(__instance) != null) { WaterVol(__instance) = null; st.SeaOverrides++; }   // the pool, not the sea, decides
                    if (st.InPool) InWater(__instance) = st.Surface;
                    if (st.InPool) DriveTarget(__instance, st);
                }
                catch (System.MissingFieldException e) { ModLog.Warn("PoolFish: a Fish field is missing in this game version (" + e.Message + "), fish in pools are not helped"); }
                catch (System.Exception e) { ModLog.VerboseWarn("PoolFish: " + e.Message); }
            }

            // T-44: the state line is written AFTER the fish's own update (the T-43 line was written before it and always showed gravity on: the game's
            // ZSyncTransform turns gravity back on every frame and the fish turns it off again in its update).
            static void Postfix(Fish __instance)
            {
                try
                {
                    State st;
                    if (__instance == null || !States.TryGetValue(__instance, out st) || !st.InPool || Time.time < st.NextReport) return;
                    st.NextReport = Time.time + ReportEvery;
                    Report(__instance, st);
                }
                catch (System.Exception e) { ModLog.VerboseWarn("PoolFish report: " + e.Message); }
            }
        }

        // T-46 (log 20:21, fish report "water level now 29.8, out of water True" right after the fish's update): the sea's WaterVolume trigger reaches
        // up over the coastal pool and calls SetLiquidLevel(sea surface) on the fish every step, so it thought the water was under it and flopped.
        // While the fish is in a pump pool, a lower water level from the sea (or a lake) is replaced by the pool surface and the sea is not taken.
        [HarmonyPatch(typeof(Fish), nameof(Fish.SetLiquidLevel))]
        static class Patch_SetLiquidLevel
        {
            static void Prefix(Fish __instance, ref float level, LiquidType type, ref Component liquidObj)
            {
                try
                {
                    State st;
                    if (__instance == null || type != LiquidType.Water || !States.TryGetValue(__instance, out st) || !st.InPool || level >= st.Surface) return;
                    if (!(liquidObj is WaterVolume) && liquidObj != null) return;          // a LiquidSurface (our pool's own trigger): the game's value is right
                    level = st.Surface; liquidObj = null; st.SeaOverrides++;
                }
                catch (System.Exception e) { ModLog.VerboseWarn("PoolFish liquid level: " + e.Message); }
            }
        }

        // T-44: when a player is near, the fish swims away with "avoid land", which asks the same depth as above (GetPointDepth) and, getting the
        // sea level, stops the fish dead in a pool. For a fish in a pump pool that check is skipped: the swim targets already keep it in deep water.
        [HarmonyPatch(typeof(Fish), "SwimDirection")]
        static class Patch_SwimDirection
        {
            static void Prefix(Fish __instance, ref bool avoidLand)
            {
                if (!avoidLand || __instance == null) return;
                State st;
                if (States.TryGetValue(__instance, out st) && st.InPool && WaterVol(__instance) == null) { avoidLand = false; st.Flees++; }
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
                    st.LevelAsks++;
                    float surface; string pool;
                    var probe = new Vector3(point.x, st.Surface, point.z);                  // waypoints carry a made-up y: test at the surface height
                    bool wet = PoolSurface(probe, out surface, out pool);
                    if (!wet) st.DryAsks++;
                    __result = wet ? surface : -10000f;                                   // outside the pool's water = dry (the fish does not swim there)
                }
                catch (System.Exception e) { ModLog.VerboseWarn("PoolFish water level: " + e.Message); }
            }
        }
    }
}
