using System.Collections.Generic;
using System.Reflection;
using DrainTarPit.Building;
using DrainTarPit.Logging;
using HarmonyLib;
using UnityEngine;
using static DrainTarPit.Drain.TarPits;
using static DrainTarPit.Drain.TarPitStats;

namespace DrainTarPit.Drain
{
    /// <summary>
    /// Ground heights of our own pools: stale heights corrected after a height pass, built pieces (walls) raised into the heights and remembered in the ZDO.
    /// </summary>
    internal static class TarPitWalls
    {
        /// The wall cells (grid index) this pool remembers (owner or not, from the ZDO after a load).
        internal static int WallCellCount(LiquidVolume v)
        {
            WallMemo m;
            return Memos.TryGetValue(v, out m) ? m.Cells.Count : 0;
        }

        // --- stale ground heights: tar floating over a hole that was dug deeper -------------------------------------------------
        // LiquidVolume.UpdateHeights (about once a second) fires one ray per grid cell from m_maxDepth above the volume's origin, m_maxDepth * 2 long,
        // and writes hit.y - origin.y into m_heights. A ray that hits nothing (ground deeper than origin - m_maxDepth, or a ray that starts inside a
        // hillside) leaves the OLD height in place (IL: the write is skipped when RaycastHit.distance == 0). Dig a pump pool deeper than that and those
        // cells keep their old floor: the tar surface (height + depth) stays up there, floating over the new hole, and its liquid trigger still makes
        // you swim in the air. Fix: after UpdateHeights every cell whose ray missed gets the real terrain height; where even that is below the reach
        // of the grid (-m_maxDepth) the cell keeps the floor value -m_maxDepth (FillBasin lets tar fill it); its tar is NOT removed (0.6.7 removed it,
        // which drained the deep part of a deep hole).
        static readonly FieldInfo FHits = AccessTools.Field(typeof(LiquidVolume), "m_raycastHitsArray");

        internal static void FixMissedHeights(LiquidVolume v)
        {
            if (!IsLiquid(v) || ZoneSystem.instance == null || FHits == null || FMaxDepth == null || !IsPumpPool(v)) return;
            var hits = FHits.GetValue(v) as RaycastHit[];
            var h = Heights(v); var d = Depths(v);
            int w = v.m_width, stride = w + 1, n = stride * stride;
            if (hits == null || h == null || d == null || hits.Length < n || h.Count < n || d.Count < n)
            { ModLog.Verbose("Pit " + v.name + ": missed heights not checked, grid data not ready (hits " + (hits == null ? "none" : hits.Length.ToString()) + ", heights " + (h == null ? "none" : h.Count.ToString()) + ", need " + n + ")"); return; }
            float maxDepth = (float)FMaxDepth.GetValue(v);
            var o = v.transform.position;
            float half = w * v.m_scale * -0.5f;
            // Bug #6: the terrain is sampled FIRST (GetGroundHeight is a ray per missed cell) and the lock is taken only for the write, so this method
            // no longer makes the pool's worker thread wait for the samples. Note: with [Drain] PoolWalls on, Patch_UpdateHeights already holds the
            // pool's lock from before the game's height write until the wall pass is done (on purpose, see there), so then the samples still run
            // inside that outer lock; this change only shortens our own hold.
            Missed.Clear();
            for (int y = 0; y <= w; y++)
                for (int x = 0; x <= w; x++)
                {
                    int i = y * stride + x;
                    if (hits[i].distance > 0f) continue;                       // the ray hit ground: vanilla already wrote the right height
                    float gh = ZoneSystem.instance.GetGroundHeight(new Vector3(o.x + half + x * v.m_scale, 0f, o.z + half + y * v.m_scale)) - o.y;
                    Missed.Add(new KeyValuePair<int, float>(i, Mathf.Clamp(gh, -maxDepth, maxDepth)));
                }
            int moved = 0, floored = 0;
            if (Missed.Count > 0)
                using (Hold(v))
                {
                    foreach (var m in Missed)
                    {
                        if (Mathf.Abs(m.Value - h[m.Key]) <= 0.01f) continue;
                        h[m.Key] = m.Value; moved++;                                // below the reach of the grid this is -m_maxDepth: the tar column stands on that floor, its surface stays right
                        if (m.Value <= -maxDepth + 0.001f) floored++;
                    }
                    if (moved > 0) { FDirty.SetValue(v, true); FNeedsSaving.SetValue(v, true); }
                }
            if (moved > 0)
                ModLog.Verbose("Pit " + v.name + ": " + moved + " of " + Missed.Count + " missed-ray ground heights corrected" + (floored > 0 ? " (" + floored + " below the grid's reach, set to the floor -" + maxDepth.ToString("0.0") + " m)" : ""));
        }

        static readonly List<KeyValuePair<int, float>> Missed = new();      // main thread only (UpdateHeights runs there)

        // --- walls: built pieces hold the tar like terrain ----------------------------------------------------------------------------
        // LiquidVolume.UpdateHeights measures the ground with one ray per cell using the volume's m_groundLayer (the vanilla pit's mask: the terrain), so a stone
        // wall ring was not ground for the pool: its cells stayed at the terrain height, the tar level could not rise above it and the tar never stood
        // against the walls. For our own pools every height pass is followed by this one: where a built piece stands on the ground (or on another piece) the
        // cell takes the top of the piece (a sphere cast 0.2 m wide, so thin walls between two grid points are not missed). Cheap when nothing is built there:
        // one overlap query per pass. Pieces of this mod (the hoses over the pool), players, items and anything floating above the pool (a roof) do not count.
        const float WallReach = 6f;                                   // m above the pool origin that a wall may reach
        static readonly Collider[] WallCols = new Collider[4096];      // was 512: a base around the pool easily has more colliders than that, the rest was silently ignored = a hole in the wall
        static readonly RaycastHit[] WallHits = new RaycastHit[64];
        static readonly Dictionary<Collider, bool> WallOk = new();
        static bool[] _wallCand = new bool[0];
        static int _wallMask = -1;
        static float _nextWallWarn;

        sealed class HitHeight : IComparer<RaycastHit>
        {
            public int Compare(RaycastHit a, RaycastHit b) { return a.point.y.CompareTo(b.point.y); }
        }
        static readonly HitHeight ByHeight = new();

        // Where the walls are, remembered: when you come back to a pool the volume loads with the zone, but the walls are separate objects that appear
        // frame after frame - for a while the pool has NO walls, its simulation sees plain terrain at the rim and the liquid that stands above the ground
        // runs out over it and spreads thin across the grid ("the level is back at ground height"). So the raised wall cells (cell -> height above the pool
        // origin) are saved in the pool's ZDO and put back at once on load. For WallGraceSeconds after the load they are kept even if no wall collider has
        // been found there yet; after that only what is really there counts (a wall removed while you were away frees its cells again).
        internal sealed class WallMemo { public Dictionary<int, float> Cells = new(); public float GraceUntil; public int Logged = -1; public HashSet<int> LastMoved = new(); }
        internal static readonly Dictionary<LiquidVolume, WallMemo> Memos = new();
        const string ZWalls = "DTP_walls";
        const float WallGraceSeconds = 30f;

        static WallMemo MemoOf(LiquidVolume v)
        {
            WallMemo m;
            if (Memos.TryGetValue(v, out m)) return m;
            m = new WallMemo { GraceUntil = Time.time + WallGraceSeconds };
            Memos[v] = m;
            try
            {
                var nv = Nview(v); var z = nv != null && nv.IsValid() ? nv.GetZDO() : null;
                string s = z != null ? z.GetString(ZWalls, "") : "";
                if (!string.IsNullOrEmpty(s))
                {
                    var pkg = new ZPackage(s);
                    int width = pkg.ReadInt(), cnt = pkg.ReadInt();
                    if (width == v.m_width)
                        for (int k = 0; k < cnt; k++) { int i = pkg.ReadInt(); float y = pkg.ReadSingle(); m.Cells[i] = y; }
                    ModLog.Verbose("Pit " + v.name + ": " + m.Cells.Count + " remembered wall cells restored");
                }
            }
            catch (System.Exception e) { m.Cells.Clear(); ModLog.VerboseWarn("Wall memory not readable: " + e.Message); }
            return m;
        }

        // the owner writes the wall cells to the ZDO when they changed (SyncRevision: the write must not make the pool reload its depths)
        static void UpdateMemo(LiquidVolume v, WallMemo memo, Dictionary<int, float> now)
        {
            bool same = memo.Cells.Count == now.Count;
            if (same)
                foreach (var kv in now)
                {
                    float old;
                    if (!memo.Cells.TryGetValue(kv.Key, out old) || Mathf.Abs(old - kv.Value) > 0.01f) { same = false; break; }
                }
            memo.Cells = now;
            if (same) return;
            var nv = Nview(v);
            if (nv == null || !nv.IsValid() || !nv.IsOwner() || nv.GetZDO() == null) return;
            var pkg = new ZPackage();
            pkg.Write(v.m_width); pkg.Write(now.Count);
            foreach (var kv in now) { pkg.Write(kv.Key); pkg.Write(kv.Value); }
            nv.GetZDO().Set(ZWalls, pkg.GetBase64());
            SyncRevision(v);
        }

        static bool IsWallCollider(Collider c)
        {
            bool ok;
            if (WallOk.TryGetValue(c, out ok)) return ok;
            ok = !c.isTrigger && c.GetComponentInParent<DtpPiece>() == null && c.GetComponentInParent<Character>() == null &&
                 c.GetComponentInParent<ItemDrop>() == null && c.GetComponentInParent<LiquidVolume>() == null;
            WallOk[c] = ok;
            return ok;
        }

        internal static void ApplyWalls(LiquidVolume v)
        {
            if (!ModSettings.PoolWalls.Value || !IsLiquid(v) || !IsPumpPool(v) || FMaxDepth == null) return;
            var h = Heights(v); var d = Depths(v);
            int w = v.m_width, stride = w + 1, n = stride * stride;
            if (h == null || d == null || h.Count < n || d.Count < n) return;
            if (_wallMask == -1) _wallMask = LayerMask.GetMask("piece", "static_solid", "Default", "Default_small");
            float maxDepth = (float)FMaxDepth.GetValue(v), sc = v.m_scale;
            var o = v.transform.position;
            float x0 = o.x + w * sc * -0.5f, z0 = o.z + w * sc * -0.5f, span = w * sc;
            var centre = new Vector3(o.x, o.y + (WallReach - maxDepth) * 0.5f, o.z);
            var ext = new Vector3(span * 0.5f, (WallReach + maxDepth) * 0.5f, span * 0.5f);
            int cnt = Physics.OverlapBoxNonAlloc(centre, ext, WallCols, Quaternion.identity, _wallMask, QueryTriggerInteraction.Ignore);
            var live = new Dictionary<int, float>();                  // cell -> wall height (above the pool origin) found by the casts
            if (cnt >= WallCols.Length && Time.time >= _nextWallWarn) { _nextWallWarn = Time.time + 60f; ModLog.Warn("Pit " + v.name + ": more than " + WallCols.Length + " colliders around the pool - some walls may not be seen"); }
            if (cnt > 0) FindWalls(h, w, stride, n, cnt, o, sc, maxDepth, x0, z0, live);
            ApplyWallHeights(v, h, d, w, stride, n, live);
        }

        static void FindWalls(List<float> h, int w, int stride, int n, int cnt, Vector3 o, float sc, float maxDepth, float x0, float z0, Dictionary<int, float> live)
        {
            WallOk.Clear();
            if (_wallCand.Length < n) _wallCand = new bool[n]; else System.Array.Clear(_wallCand, 0, _wallCand.Length);
            bool any = false;
            for (int k = 0; k < cnt; k++)
            {
                var c = WallCols[k];
                if (c == null || !IsWallCollider(c)) continue;
                var b = c.bounds;
                int ax = Mathf.Max(0, Mathf.FloorToInt((b.min.x - x0) / sc) - 1), bx = Mathf.Min(w, Mathf.CeilToInt((b.max.x - x0) / sc) + 1);
                int az = Mathf.Max(0, Mathf.FloorToInt((b.min.z - z0) / sc) - 1), bz = Mathf.Min(w, Mathf.CeilToInt((b.max.z - z0) / sc) + 1);
                for (int z = az; z <= bz; z++)
                    for (int x = ax; x <= bx; x++) { _wallCand[z * stride + x] = true; any = true; }
            }
            if (!any) return;

            float r = Mathf.Min(0.2f, sc * 0.6f), startY = o.y + WallReach;
            {
                for (int i = 0; i < n; i++)
                {
                    if (!_wallCand[i]) continue;
                    var p = new Vector3(x0 + (i % stride) * sc, startY, z0 + (i / stride) * sc);
                    int hc = Physics.SphereCastNonAlloc(p, r, Vector3.down, WallHits, WallReach + maxDepth, _wallMask, QueryTriggerInteraction.Ignore);
                    if (hc <= 0) continue;
                    System.Array.Sort(WallHits, 0, hc, ByHeight);                        // lowest first: a piece counts when it stands on the ground / on a piece below
                    float support = o.y + h[i], top = float.NegativeInfinity;
                    for (int j = 0; j < hc; j++)
                    {
                        var col = WallHits[j].collider;
                        if (col == null || !IsWallCollider(col)) continue;
                        if (col.bounds.min.y > support + 0.6f) continue;                 // floats above (a roof): not a wall
                        float y = WallHits[j].point.y;
                        if (y > support) { support = y; top = y; }
                    }
                    if (float.IsNegativeInfinity(top)) continue;
                    float nh = Mathf.Clamp(top - o.y, -maxDepth, maxDepth);
                    if (nh <= h[i] + 0.02f) continue;
                    live[i] = nh;
                }
            }
        }

        /// Test harness (synthetic wall-correction tests, Debug/PoolTankTests): one wall pass with a given set of raised cells
        /// (cell -> top above the pool origin) instead of the colliders found around the pool. Called with the pool's lock held.
        internal static void ApplyWallCells(LiquidVolume v, Dictionary<int, float> live)
        {
            var h = Heights(v); var d = Depths(v);
            int w = v.m_width, stride = w + 1, n = stride * stride;
            if (h == null || d == null || h.Count < n || d.Count < n) return;
            ApplyWallHeights(v, h, d, w, stride, n, new Dictionary<int, float>(live));
        }

        // Raises the cells of the walls (found now + remembered ones while the grace time runs) in the ground heights and saves the set.
        // The liquid rules are HeightField.RaiseCells (unit tested): only the liquid a piece really displaces moves, a submerged piece keeps the
        // liquid above it, a cell that was already raised to the same top at the last pass moves nothing again (step 9), and nothing is deleted (W-5).
        static void ApplyWallHeights(LiquidVolume v, List<float> h, List<float> d, int w, int stride, int n, Dictionary<int, float> live)
        {
            var memo = MemoOf(v);
            var prev = memo.Cells;                                   // the wall cells of the last pass (UpdateMemo below replaces memo.Cells)
            var eff = live;
            if (Time.time < memo.GraceUntil)
                foreach (var kv in memo.Cells)
                {
                    float c;
                    if (!eff.TryGetValue(kv.Key, out c) || kv.Value > c) eff[kv.Key] = kv.Value;
                }
            UpdateMemo(v, memo, eff);
            if (memo.Logged != eff.Count) { memo.Logged = eff.Count; ModLog.Verbose("Pit " + v.name + ": " + eff.Count + " cells raised by built pieces"); }       // only when the number changes (it was one line per pool per second)
            if (eff.Count == 0) { memo.LastMoved.Clear(); return; }
            bool book = TarPitLedger.Active(v);
            using (Hold(v))
            {
                float c2 = v.m_scale * v.m_scale;
                double before = book ? HeightField.Sum(d, n) : 0d;
                var r = HeightField.RaiseCells(h, d, w, eff, prev, memo.LastMoved);
                memo.LastMoved = new HashSet<int>(r.Moved);
                if (r.Raised > 0) FDirty.SetValue(v, true);
                if (r.KeptCells > 0 || BudgetOf(v).WallKept > 0f) BudgetOf(v).WallKept = r.KeptDepth * c2;       // m3 standing on pieces (not moved), of the last pass
                if (r.DisplacedCells == 0) return;
                FNeedsSaving.SetValue(v, true);
                var wb = BudgetOf(v); wb.WallPasses++; wb.WallPushed += r.Displaced * c2; wb.WallLost += r.Unplaced * c2; wb.WallRepeat += r.RepeatDepth * c2;
                string line = "wall pass: " + r.DisplacedCells + " of " + r.Raised + " raised cells displaced liquid, moved " + (r.Displaced * c2).ToString("0.000") + " m3 to open cells (of it " +
                    (r.Far * c2).ToString("0.000") + " m3 from " + r.NoOpenNeighbour + " cells with no open neighbour, moved outwards), no open cell anywhere (stayed) " + (r.Unplaced * c2).ToString("0.000") +
                    " m3, liquid left standing on submerged pieces " + (r.KeptDepth * c2).ToString("0.000") + " m3 on " + r.KeptCells + " cells, cells that also moved liquid at the last pass " + r.RepeatCells +
                    " (" + (r.RepeatDepth * c2).ToString("0.000") + " m3) (DESTROYED: 0, nothing is deleted)";
                ModLog.Verbose("Pit " + v.name + ": " + line);
                if (book)
                {
                    double after = HeightField.Sum(d, n);
                    TarPitLedger.Checkpoint(v, "wall", (float)(before * c2), (float)(after * c2), line);
                }
            }
        }
    }
}
