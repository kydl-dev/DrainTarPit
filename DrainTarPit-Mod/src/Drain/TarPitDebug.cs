using System.Collections.Generic;
using System.Reflection;
using DrainTarPit.Logging;
using HarmonyLib;
using UnityEngine;
using static DrainTarPit.Drain.TarPits;
using static DrainTarPit.Drain.TarPitTracking;
using static DrainTarPit.Drain.TarPitStats;

namespace DrainTarPit.Drain
{
    /// <summary>
    /// Debug and test doors: the `dtp pits` text, junk pool removal, empty / mesh-dirty buttons, the narrow doors for `dtp ptest` and the save / reload numbers.
    /// </summary>
    internal static class TarPitDebug
    {
        /// Debug text (dtp pits): what this volume says about world point p - inside its grid?, tar depth there, tar surface height, kind, owner.
        public static string DescribeAt(LiquidVolume v, Vector3 p)
        {
            var g = ToGrid(v, p);
            var nv = Nview(v); var zdo = nv != null && nv.IsValid() ? nv.GetZDO() : null;
            string s = (zdo != null && zdo.GetBool(TarPool.ZPool, false) ? "[pump pool] " : "[vanilla] ") + "grid " + v.m_width + "x" + v.m_width + "@" + v.m_scale + "m, owner=" + (nv != null && nv.IsValid() && nv.IsOwner());
            if (!InGrid(v, g)) return s + ", tracks you: " + Tracks(v, Player.m_localPlayer) + ", you are outside its grid";
            float dep; using (Hold(v)) { var d = Depths(v); dep = d != null && d.Count >= (v.m_width + 1) * (v.m_width + 1) ? DepthAt(v, d, g.x, g.y) : -1f; }
            return s + ", tracks you: " + Tracks(v, Player.m_localPlayer) + ", under you: depth " + dep.ToString("0.00") + " m, surface y " + v.GetSurface(p).ToString("0.00") + " (you y " + p.y.ToString("0.00") + ")";
        }

        /// Console `dtp cleanpools`: remove only empty, valid, unflagged water pools. Pump pools are always retained;
        /// populated or unverifiable non-pump water volumes are retained as a safety measure. Each water candidate gets
        /// an explicit KEEP / REMOVE_REQUESTED decision in the console and log.
        internal static List<string> RemoveJunkPools()
        {
            var report = new List<string>();
            int deadRefs = 0, waterCandidates = 0, requested = 0, retainedPump = 0, retainedPopulated = 0, retainedUnsafe = 0;

            // An OnDestroy hook normally removes these; prune any destroyed Unity objects that remain in the registry.
            for (int i = All.Count - 1; i >= 0; i--)
            {
                if (All[i] == null)
                {
                    All.RemoveAt(i);
                    deadRefs++;
                }
            }
            if (deadRefs > 0) report.Add("dtp cleanpools: pruned " + deadRefs + " destroyed/null registry reference(s)");
            if (ZNetScene.instance == null)
            {
                report.Add("dtp cleanpools: ABORTED — no ZNetScene; no pool instances were destroyed");
                return report;
            }

            foreach (var v in new List<LiquidVolume>(All))
            {
                if (v == null || v.m_liquidType != LiquidType.Water) continue;
                waterCandidates++;
                string id = "poolId=" + v.GetInstanceID() + " name=\"" + v.name + "\" pos=" + v.transform.position.ToString("0.0");
                try
                {
                    if (IsPumpPool(v))
                    {
                        retainedPump++;
                        report.Add("dtp cleanpools: KEEP " + id + " reason=valid pump-pool flag; cleanup never removes pump pools");
                        continue;
                    }

                    var nv = Nview(v);
                    if (nv == null || !nv.IsValid() || nv.GetZDO() == null)
                    {
                        retainedUnsafe++;
                        report.Add("dtp cleanpools: KEEP " + id + " reason=missing/invalid ZNetView or ZDO; cannot destroy safely");
                        continue;
                    }
                    if (!TarPoolDiagnostics.GridIsValid(v))
                    {
                        retainedUnsafe++;
                        report.Add("dtp cleanpools: KEEP " + id + " reason=grid is invalid/uninitialized; emptiness cannot be verified");
                        continue;
                    }

                    float volume = 0f;
                    bool volumeValid = true;
                    using (Hold(v))
                    {
                        var d = Depths(v);
                        long expected = ((long)v.m_width + 1L) * ((long)v.m_width + 1L);
                        if (d == null || expected <= 0 || expected > int.MaxValue || d.Count < expected)
                            volumeValid = false;
                        else
                        {
                            double sum = 0d;
                            for (int i = 0; i < expected; i++) sum += d[i];
                            double total = sum * v.m_scale * v.m_scale;
                            if (double.IsNaN(total) || double.IsInfinity(total) || total < -0.001d || total > float.MaxValue)
                                volumeValid = false;
                            else volume = (float)total;
                        }
                    }
                    if (!volumeValid)
                    {
                        retainedUnsafe++;
                        report.Add("dtp cleanpools: KEEP " + id + " reason=volume could not be safely measured");
                        continue;
                    }
                    if (volume > 0.05f)
                    {
                        retainedPopulated++;
                        report.Add("dtp cleanpools: KEEP " + id + " reason=contains " + volume.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " m3 (threshold 0.050 m3)");
                        continue;
                    }

                    nv.ClaimOwnership();
                    ZNetScene.instance.Destroy(v.gameObject);
                    requested++;
                    report.Add("dtp cleanpools: REMOVE_REQUESTED " + id + " reason=unflagged water pool, valid grid, measured " + volume.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " m3");
                }
                catch (System.Exception e)
                {
                    retainedUnsafe++;
                    report.Add("dtp cleanpools: KEEP " + id + " reason=exception before safe removal: " + e.GetType().Name + ": " + e.Message);
                    ModLog.VerboseWarn("RemoveJunkPools: " + e);
                }
            }

            report.Add("dtp cleanpools summary: waterCandidates=" + waterCandidates + " removeRequests=" + requested + " retainedPumpPools=" + retainedPump +
                " retainedPopulated=" + retainedPopulated + " retainedUnsafe=" + retainedUnsafe + " prunedDeadRegistryRefs=" + deadRefs +
                ". REMOVE_REQUESTED means destruction was submitted to ZNetScene; confirm disappearance with dtp pits.");
            return report;
        }

        /// Console `dtp cleanpools small [m3]` (user, 2026-10-20): removes PUMP pools (water or tar) that hold less than `maxM3` (default 1 m3), e.g. the
        /// leftover runoff pools the old hop chains spawned along a shore. A pool with a valid grid and more liquid is never touched; characters it
        /// tracks are released first (TarPitTracking.ReleaseAll), so nobody is left swimming in a removed pool.
        internal static List<string> RemoveSmallPumpPools(float maxM3)
        {
            var report = new List<string>();
            if (ZNetScene.instance == null) { report.Add("dtp cleanpools small: ABORTED - no ZNetScene"); return report; }
            int removed = 0, kept = 0;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var v in new List<LiquidVolume>(All))
            {
                if (v == null || !IsPumpPool(v)) continue;
                string id = "poolId=" + v.GetInstanceID() + " name=\"" + v.name + "\" pos=" + v.transform.position.ToString("0.0");
                try
                {
                    var nv = Nview(v);
                    if (nv == null || !nv.IsValid() || !TarPoolDiagnostics.GridIsValid(v)) { kept++; report.Add("dtp cleanpools small: KEEP " + id + " reason=no valid ZNetView / grid, volume not verifiable"); continue; }
                    float volume = TotalVolume(v);
                    if (float.IsNaN(volume) || volume >= maxM3) { kept++; report.Add("dtp cleanpools small: KEEP " + id + " holds " + volume.ToString("0.000", inv) + " m3"); continue; }
                    foreach (var l in TarPitTracking.ReleaseAll(v)) ModLog.Info("Pool " + v.name + ": " + l);
                    nv.ClaimOwnership();
                    ZNetScene.instance.Destroy(v.gameObject);
                    removed++;
                    report.Add("dtp cleanpools small: REMOVE_REQUESTED " + id + " held " + volume.ToString("0.000", inv) + " m3 (< " + maxM3.ToString("0.###", inv) + ")");
                }
                catch (System.Exception e) { kept++; report.Add("dtp cleanpools small: KEEP " + id + " reason=" + e.GetType().Name + ": " + e.Message); }
            }
            report.Add("dtp cleanpools small summary: removed " + removed + ", kept " + kept + " pump pool(s) (limit " + maxM3.ToString("0.###", inv) + " m3). Confirm with dtp pits.");
            return report;
        }

        /// Test button: removes all the liquid from a pool (owner only).
        internal static bool DebugEmpty(LiquidVolume v)
        {
            var nv = Nview(v);
            if (v == null || nv == null || !nv.IsValid() || !nv.IsOwner()) return false;
            var d = Depths(v);
            using (Hold(v))
            {
                for (int i = 0; i < d.Count; i++) d[i] = 0f;
                FDirty.SetValue(v, true); FNeedsSaving.SetValue(v, true);
            }
            return true;
        }

        /// Makes every loaded volume of this liquid rebuild its mesh (PoolMesh settings changed). Same flag the drain / deposit code sets after a depth change.
        internal static void MarkMeshDirty(LiquidType type)
        {
            foreach (var v in All)
                if (v != null && v.m_liquidType == type) FDirty.SetValue(v, true);
        }

        // --- test support (Debug/PoolTests.cs) --------------------------------------------------------------------------
        // Narrow doors for the in-game test harness (console `dtp ptest`); nothing in the normal game path uses them.
        internal static System.IDisposable Lock(LiquidVolume v) { return Hold(v); }
        internal static List<float> DepthList(LiquidVolume v) { return Depths(v); }
        internal static List<float> HeightList(LiquidVolume v) { return Heights(v); }
        internal static bool Ready(LiquidVolume v) { return HeightsReady(v); }
        internal static void MarkChanged(LiquidVolume v) { FDirty.SetValue(v, true); FNeedsSaving.SetValue(v, true); }
        internal static Budget BudgetFor(LiquidVolume v) { return BudgetOf(v); }

        static readonly System.Type TZdoVars = AccessTools.TypeByName("ZDOVars");
        static readonly FieldInfo FLiquidDataHash = TZdoVars != null ? AccessTools.Field(TZdoVars, "s_liquidData") : null;
        static readonly MethodInfo MGetBytes = AccessTools.Method(typeof(ZDO), "GetByteArray", new[] { typeof(int), typeof(byte[]) });

        /// The numbers that show a save / reload cycle of the pool: the revision the volume believes it has loaded (m_lastDataRevision), the ZDO's own revision, and
        /// size + checksum of the depth data the game saved in the ZDO (-1 when it cannot be read).
        internal static void Revisions(LiquidVolume v, out long own, out long zdo, out int savedBytes, out int savedHash)
        {
            own = -1; zdo = -1; savedBytes = -1; savedHash = 0;
            try
            {
                if (FLastRev != null) own = System.Convert.ToInt64(FLastRev.GetValue(v));
                var nv = Nview(v);
                if (nv == null || !nv.IsValid()) return;
                var z = nv.GetZDO();
                zdo = System.Convert.ToInt64(z.DataRevision);
                if (FLiquidDataHash == null || MGetBytes == null) return;
                var bytes = MGetBytes.Invoke(z, new object[] { (int)FLiquidDataHash.GetValue(null), null }) as byte[];
                if (bytes == null) { savedBytes = 0; return; }
                savedBytes = bytes.Length;
                int h = 17;
                for (int i = 0; i < bytes.Length; i++) h = h * 31 + bytes[i];
                savedHash = h;
            }
            catch (System.Exception e) { ModLog.VerboseWarn("Revisions: " + e.Message); }
        }
    }
}
