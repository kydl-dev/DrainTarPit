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

        /// Console `dtp cleanpools`: removes the empty water volumes that are NOT pump pools. `dtp spawn DTP_WaterPool2` makes such a volume (no walls, never flagged), and
        /// the stroke picked the nearest grid centre, so a stack of them hijacked the pour and cost a height pass (one ray per cell) per second each. Pools the pump made
        /// (flagged) and anything that holds water are never touched. Returns how many were removed.
        internal static int RemoveJunkPools()
        {
            int removed = 0;
            foreach (var v in new List<LiquidVolume>(All))
            {
                try
                {
                    if (v == null || v.m_liquidType != LiquidType.Water || IsPumpPool(v)) continue;
                    var nv = Nview(v);
                    if (nv == null || !nv.IsValid() || ZNetScene.instance == null) continue;
                    var d = Depths(v);
                    if (d == null || d.Count < (v.m_width + 1) * (v.m_width + 1) || TotalVolume(v) > 0.05f) continue;
                    nv.ClaimOwnership();
                    ZNetScene.instance.Destroy(v.gameObject);
                    removed++;
                }
                catch (System.Exception e) { ModLog.VerboseWarn("RemoveJunkPools: " + e.Message); }
            }
            return removed;
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
