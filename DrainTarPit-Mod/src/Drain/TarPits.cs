using System.Collections.Generic;
using System.Reflection;
using DrainTarPit.Building;
using HarmonyLib;
using UnityEngine;

namespace DrainTarPit.Drain
{
    /// <summary>
    /// How vanilla tar pits work (from assembly_valheim.dll): a pit is a LiquidVolume with m_liquidType == Tar. It is a
    /// height field - m_depths holds (m_width+1)^2 floats (metres of tar per cell, index = z * (m_width+1) + x),
    /// m_heights the ground height per cell, cell size m_scale, centred on the object's position. A worker thread
    /// evens neighbouring surfaces (m_viscocity) and keeps the total volume constant, under the m_meshDataLock lock.
    /// The ZDO owner saves the depths to the ZDO (s_liquidData) and everybody else reloads them when the revision
    /// changes.
    ///
    /// Draining = subtracting depth from cells near the strainer on the OWNER (we RPC the owner), flagging the volume
    /// dirty + needsSaving. The simulation then pulls the rest of the pit towards the hole, so the level drops
    /// gradually and a concave pit empties from its deepest point.
    ///
    /// The code is split over several static classes (PROJECT_RULES rule 8: no class over 500 lines). This class keeps the shared basics
    /// (reflection fields, the lock, geometry, sampling, constants); TarPitTracking, TarPitDrain, TarPitDeposit, TarPitFlood, TarPitStats,
    /// TarPitFlow, TarPitSettle, TarPitWalls, TarPitHooks and TarPitDebug hold the rest.
    /// </summary>
    internal static class TarPits
    {
        public const string RpcDrain = "DTP_Drain";        // client -> pit owner: remove tar
        public const string RpcDrained = "DTP_Drained";    // pit owner -> requesting client: how much was really removed
        public const string RpcDeposit = "DTP_Deposit";    // client -> pit owner: pour tar INTO a tar volume (fills the basin around the point)
        // WORLD KEYS: VarBaseline, VarRefillTime (in TarPitDrain), TarPool.ZPool, DtpPiece.ZLiquid and RunoffCleanup.ZRunoff use the "DTP_" prefix; worlds saved
        // before the rename hold "TPD_" keys, which are still read (ZdoKeys: read new, fall back to old; write new).
        internal const string VarBaseline = "DTP_baseline", VarBaselineOld = "TPD_baseline";   // total volume (m3) before the first drain; used by refill

        public static readonly List<LiquidVolume> All = new();

        static readonly FieldInfo FDepths = AccessTools.Field(typeof(LiquidVolume), "m_depths");
        static readonly FieldInfo FHeights = AccessTools.Field(typeof(LiquidVolume), "m_heights");
        internal static readonly FieldInfo FLock = AccessTools.Field(typeof(LiquidVolume), "m_meshDataLock");
        internal static readonly FieldInfo FDirty = AccessTools.Field(typeof(LiquidVolume), "m_dirty");
        internal static readonly FieldInfo FNeedsSaving = AccessTools.Field(typeof(LiquidVolume), "m_needsSaving");
        static readonly FieldInfo FNview = AccessTools.Field(typeof(LiquidVolume), "m_nview");

        // The pit's worker thread rewrites m_depths under m_meshDataLock; every read / write here takes it too.
        // IL of LiquidVolume.UpdateThread / Update (assembly_valheim.dll): the game does Monitor.Enter / Monitor.TryEnter on this field, i.e. it is a plain
        // object used as a Monitor (NOT a System.Threading.Mutex - the old code cast it to a Mutex, got null and so took no lock at all, which let the
        // sim thread overwrite drained / deposited depths). Monitor is re-entrant, so callers that already hold it (DrainLocal / RefillTick) are fine.
        // A Mutex is still handled in case a future game version changes the field type.
        internal sealed class Held : System.IDisposable
        {
            readonly object _o; bool _taken;
            public Held(object o)
            {
                _o = o;
                if (_o == null) return;
                var mx = _o as System.Threading.Mutex;
                if (mx != null) { mx.WaitOne(); _taken = true; }
                else System.Threading.Monitor.Enter(_o, ref _taken);
            }
            public void Dispose()
            {
                if (!_taken) return;
                var mx = _o as System.Threading.Mutex;
                if (mx != null) mx.ReleaseMutex(); else System.Threading.Monitor.Exit(_o);
            }
        }
        internal static Held Hold(LiquidVolume v) { return new Held(FLock.GetValue(v)); }

        // Every ZDO.Set on the pit's ZDO bumps its DataRevision, and LiquidVolume.Update -> CheckLoad() reloads the depths from the LAST SAVED data
        // whenever m_lastDataRevision differs. Our own bookkeeping writes (baseline / refill time) therefore threw every drain away. Tell the pit that
        // the revision is ours; the changed depths are written out by its normal CheckSave (m_needsSaving).
        internal static readonly FieldInfo FLastRev = AccessTools.Field(typeof(LiquidVolume), "m_lastDataRevision");
        internal static void SyncRevision(LiquidVolume v)
        {
            var nv = Nview(v);
            if (FLastRev != null && nv != null && nv.IsValid()) FLastRev.SetValue(v, nv.GetZDO().DataRevision);
        }

        public static bool IsTar(LiquidVolume v) { return v != null && v.m_liquidType == LiquidType.Tar; }
        /// A volume this mod handles: vanilla tar pits and tar pools, and water pools made from pumped water (same height-field simulation, other LiquidType).
        public static bool IsLiquid(LiquidVolume v) { return v != null && (v.m_liquidType == LiquidType.Tar || v.m_liquidType == LiquidType.Water); }
        public static PumpLiquid LiquidOf(LiquidVolume v) { return v != null && v.m_liquidType == LiquidType.Water ? PumpLiquid.Water : PumpLiquid.Tar; }
        internal static List<float> Depths(LiquidVolume v) { return (List<float>)FDepths.GetValue(v); }
        internal static List<float> Heights(LiquidVolume v) { return (List<float>)FHeights.GetValue(v); }
        public static ZNetView Nview(LiquidVolume v) { return FNview.GetValue(v) as ZNetView; }

        // --- geometry (mirrors LiquidVolume.WorldToLocal) ---------------------------------------------------------
        internal static Vector2 ToGrid(LiquidVolume v, Vector3 w)
        {
            var o = v.transform.position;
            float half = v.m_width * v.m_scale * -0.5f;
            return new Vector2((w.x - (o.x + half)) / v.m_scale, (w.z - (o.z + half)) / v.m_scale);
        }

        internal static bool InGrid(LiquidVolume v, Vector2 g) { return g.x >= 0f && g.y >= 0f && g.x <= v.m_width && g.y <= v.m_width; }

        internal static float DepthAt(LiquidVolume v, List<float> d, float gx, float gy)
        {
            int stride = v.m_width + 1;
            gx = Mathf.Clamp(gx, 0f, v.m_width); gy = Mathf.Clamp(gy, 0f, v.m_width);
            int x = Mathf.Min((int)gx, v.m_width - 1), y = Mathf.Min((int)gy, v.m_width - 1);
            float fx = gx - x, fy = gy - y;
            float a = Mathf.Lerp(d[y * stride + x], d[y * stride + x + 1], fx);
            float b = Mathf.Lerp(d[(y + 1) * stride + x], d[(y + 1) * stride + x + 1], fx);
            return Mathf.Lerp(a, b, fy);
        }

        /// Finds the pit under world point p. depth = tar depth there, surfaceY = world height of the tar surface.
        public static bool TrySample(Vector3 p, out LiquidVolume vol, out float depth, out float surfaceY, LiquidType type = LiquidType.Tar)
        {
            vol = null; depth = 0f; surfaceY = 0f;
            foreach (var v in All)
            {
                if (v == null || (type != LiquidType.All && v.m_liquidType != type)) continue;      // only volumes of the asked liquid: a tar strainer never sees a water pool
                var g = ToGrid(v, p);
                if (!InGrid(v, g)) continue;
                float dep;
                using (Hold(v))
                {
                    var d = Depths(v);
                    if (d == null || d.Count < (v.m_width + 1) * (v.m_width + 1)) continue;
                    dep = DepthAt(v, d, g.x, g.y);
                }
                if (vol == null || dep > depth) { vol = v; depth = dep; surfaceY = v.GetSurface(p); }
            }
            return vol != null;
        }

        /// Like TrySample, but also looks around p (two rings of 8 points up to `radius` away) and returns the deepest tar
        /// it finds, so the strainer does not have to be dipped in exactly the right spot. `at` = the sampled point.
        public static bool TrySampleNear(Vector3 p, float radius, out LiquidVolume vol, out float depth, out float surfaceY, out Vector3 at, LiquidType type = LiquidType.Tar)
        {
            TrySample(p, out vol, out depth, out surfaceY, type); at = p;
            if (radius < 0.01f) return vol != null;
            for (int ring = 1; ring <= 2; ring++)
                for (int i = 0; i < 8; i++)
                {
                    float ang = i * Mathf.PI * 0.25f, r = radius * ring * 0.5f;
                    var q = p + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                    LiquidVolume v2; float d2, s2;
                    if (TrySample(q, out v2, out d2, out s2, type) && (vol == null || d2 > depth + 0.001f)) { vol = v2; depth = d2; surfaceY = s2; at = q; }
                }
            return vol != null;
        }

        public static float TotalVolume(LiquidVolume v)
        {
            float sum = 0f;
            using (Hold(v)) { var d = Depths(v); for (int i = 0; i < d.Count; i++) sum += d[i]; }
            return sum * v.m_scale * v.m_scale;
        }

        internal static readonly FieldInfo FHaveHeights = AccessTools.Field(typeof(LiquidVolume), "m_haveHeights");
        internal static readonly FieldInfo FMaxDepth = AccessTools.Field(typeof(LiquidVolume), "m_maxDepth");

        /// Flags one of our own pools in its ZDO (no self-refill) without making the volume reload its (empty) saved data.
        public static void MarkPool(LiquidVolume v)
        {
            var nv = Nview(v);
            if (nv == null || !nv.IsValid()) return;
            nv.GetZDO().Set(TarPool.ZPool, true);
            SyncRevision(v);
        }

        public static bool Contains(LiquidVolume v, Vector3 p, float margin)
        {
            var g = ToGrid(v, p); float m = margin / v.m_scale;
            return g.x >= m && g.y >= m && g.x <= v.m_width - m && g.y <= v.m_width - m;
        }

        internal static bool HeightsReady(LiquidVolume v) { return FHaveHeights == null || (bool)FHaveHeights.GetValue(v); }

        internal static bool IsPumpPool(LiquidVolume v)
        {
            var nv = Nview(v);
            return nv != null && nv.IsValid() && nv.GetZDO() != null && ZdoKeys.Bool(nv.GetZDO(), TarPool.ZPool, TarPool.ZPoolOld);
        }
    }
}
