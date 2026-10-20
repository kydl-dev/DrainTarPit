using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DrainTarPit.Drain
{
    /// <summary>
    /// The liquid rules of the pump pools as a plain height field (user spec W-1 - W-5, PLAN T-017; steps 6, 9, 11, 12, 13 of
    /// _agent/DrainTarPit_Water_and_Tar_First_15_Steps.md). No Unity or game types: the same file is compiled into the unit tests
    /// (DrainTarPit-Tests/Unit), so the rules are checked without the game.
    ///
    /// Grid: (w+1)^2 cells, index = y * (w+1) + x; h = ground height, d = liquid depth (metres). Every object (wall, pillar, tree,
    /// rock, bush) is only ground height here: a closed ring that is high enough makes a basin, a lone pillar is an island (W-4, Q16).
    /// Volumes are "cell units" (depth sum); multiply by the cell area for m3.
    /// </summary>
    internal static class HeightField
    {
        internal const float MinSpillDepth = 0.04f;     // m: a film thinner than this, OUTSIDE the pool's rim, is not tracked (W-3); also the border lip
        internal const float SpillJoin = 0.02f;         // m: a cell this much lower than the level behind a lip = the liquid runs over the lip
        internal const int MaxSpillPasses = 48;         // basins one stroke may run through (guard against loops)
        internal const float EnclosureMinDepth = 0.15f; // m: a depression shallower than this (rim - floor) is a dent in the ground, not a pool (decision D-024)
        internal const float WallMinRise = 0.02f;       // m: a piece lower than this over the ground is not raised into the heights

        static bool IsBorder(int x, int y, int w) { return x <= 1 || y <= 1 || x >= w - 1 || y >= w - 1; }

        // ------------------------------------------------------------------------------------------------ the pool around a point
        /// The depression around a low cell, found by heights only: the lowest level at which liquid standing there could reach the
        /// border of the grid (minimax path over the surfaces h + d, 4-neighbour). Rim = that level; Inside = every cell the liquid
        /// reaches below the rim (the pool, including its internal dips); Enclosed = the rim is at least EnclosureMinDepth over the floor.
        internal sealed class Basin
        {
            public int Low = -1, RimCell = -1, Cells;
            public float Floor, Rim = float.PositiveInfinity;
            public bool Enclosed;
            public bool[] Inside;
            public float Depth { get { return Rim - Floor; } }
        }

        internal static Basin FindBasin(IList<float> d, IList<float> h, int w, int low, float buried)
        {
            int stride = w + 1, n = stride * stride;
            var b = new Basin { Low = low, Floor = h[low], Inside = new bool[n] };
            var key = new float[n];
            for (int i = 0; i < n; i++) key[i] = float.PositiveInfinity;
            var done = new bool[n];
            var heap = new MinHeap(256);
            key[low] = h[low] + d[low];
            heap.Push(key[low], low);
            int[] ox = { 1, -1, 0, 0 }, oy = { 0, 0, 1, -1 };
            var popped = new List<int>();
            while (heap.Count > 0)
            {
                float k; int c = heap.Pop(out k);
                if (done[c] || k > key[c]) continue;
                done[c] = true;
                int cx = c % stride, cy = c / stride;
                if (c != low && IsBorder(cx, cy, w)) { b.Rim = k; b.RimCell = c; break; }
                popped.Add(c);
                for (int j = 0; j < 4; j++)
                {
                    int x = cx + ox[j], y = cy + oy[j];
                    if (x < 0 || y < 0 || x > w || y > w) continue;
                    int i = y * stride + x;
                    if (done[i] || h[i] <= buried) continue;
                    float s = h[i] + d[i] + (IsBorder(x, y, w) ? MinSpillDepth : 0f);
                    float nk = Math.Max(k, s);
                    if (nk < key[i]) { key[i] = nk; heap.Push(nk, i); }
                }
            }
            foreach (int c in popped)
                if (key[c] < b.Rim - 1e-4f) { b.Inside[c] = true; b.Cells++; }
            b.Enclosed = b.Rim - b.Floor >= EnclosureMinDepth;
            return b;
        }

        /// Steepest descent over the surfaces, 4-neighbour like the flood (8-neighbour slipped through the corner of a diagonal wall).
        internal static int Descend(IList<float> d, IList<float> h, int w, int start, float buried)
        {
            int stride = w + 1, cur = start;
            int[] ox = { 1, -1, 0, 0 }, oy = { 0, 0, 1, -1 };
            for (int step = 0; step < 4 * stride; step++)
            {
                int cx = cur % stride, cy = cur / stride, next = cur; float best = h[cur] + d[cur];
                for (int k = 0; k < 4; k++)
                {
                    int x = cx + ox[k], y = cy + oy[k];
                    if (x < 1 || y < 1 || x > w - 1 || y > w - 1) continue;
                    int i = y * stride + x;
                    if (h[i] <= buried) continue;
                    float s = h[i] + d[i];
                    if (s < best - 0.0005f) { best = s; next = i; }
                }
                if (next == cur) break;
                cur = next;
            }
            return cur;
        }

        // ------------------------------------------------------------------------------------------------ pouring
        internal sealed class FillOptions
        {
            public bool Overflow = true;                      // [Drain] PoolOverflow
            public bool RimDisappear;                         // [Drain] PoolRimDisappear (W-1 / W-2)
            public float Buried = float.NegativeInfinity;     // a cell at or below this is solid (vanilla pits); pump pools: -inf
            public float MaxDepth = 4f;                       // the volume's m_maxDepth; a column deeper than 2x this is not written
        }

        /// Where one pour went. Every amount is exclusive (step 6): Poured = Retained + OtherBasins + Outside + ToEdge + RimRemoved
        /// + OverflowRemoved + Unplaced + Numeric. Cell units (multiply by the cell area for m3).
        internal sealed class FillResult
        {
            public float Poured;
            public float Retained;          // raised in the first basin (the landing basin)
            public float OtherBasins;       // raised in further basins INSIDE the same pool (internal dips behind a lip; thin films there are kept)
            public float Outside;           // ran over the pool's rim (W-3) and stands in a basin outside it, still in this grid
            public float ToEdge;            // reached the border of the grid (continues into the next pool or runs off, decided by the caller)
            public float RimRemoved;        // PoolRimDisappear = true: what would have gone over the rim (W-2)
            public float OverflowRemoved;   // PoolRimDisappear = false: ran over the rim and lies as a thin film outside: not tracked (W-3)
            public float Unplaced;          // no reachable cell, more than MaxSpillPasses basins, or deeper than 2x MaxDepth
            public float Numeric;           // float rounding (signed)
            public int EdgeCell = -1, Passes, RimCrossings, Start = -1;
            public Basin Pool;
            public string Path = "";
            public float Accounted { get { return Retained + OtherBasins + Outside + ToEdge + RimRemoved + OverflowRemoved + Unplaced + Numeric; } }
            public float Residual { get { return Poured - Accounted; } }
            public float Kept { get { return Retained + OtherBasins + Outside; } }
        }

        /// Pours `volume` (cell units) at cell `start`. W-3 (rim switch off): the pool fills first - liquid that runs over a lip into another
        /// dip of the same pool stays (even as a thin film); only liquid that runs over the RIM leaves, and a thin film of it outside is not
        /// tracked. A gap in the ring is the lowest point of the rim, so the liquid leaves through it before the pool is full. W-2 (switch on):
        /// an enclosed pool fills to its rim and the rest is removed there. Not enclosed (a dent, a slope): the liquid just runs downhill.
        internal static FillResult Fill(IList<float> d, IList<float> h, int w, int start, float volume, FillOptions o)
        {
            var r = new FillResult { Poured = volume };
            int stride = w + 1;
            var path = new StringBuilder();
            int cur = Math.Max(1, Math.Min(w - 1, start % stride)) + Math.Max(1, Math.Min(w - 1, start / stride)) * stride;
            if (h[cur] <= o.Buried)
            {
                int best = -1, bd = int.MaxValue, cx0 = cur % stride, cy0 = cur / stride;
                for (int dy = -4; dy <= 4; dy++)
                    for (int dx = -4; dx <= 4; dx++)
                    {
                        int x = cx0 + dx, y = cy0 + dy;
                        if (x < 1 || y < 1 || x > w - 1 || y > w - 1) continue;
                        int i = y * stride + x;
                        if (h[i] > o.Buried && dx * dx + dy * dy < bd) { bd = dx * dx + dy * dy; best = i; }
                    }
                if (best < 0) { r.Unplaced = volume; r.Path = "no reachable cell near the landing point"; return r; }
                cur = best;
            }
            r.Start = cur;
            int low = Descend(d, h, w, cur, o.Buried);
            var pool = FindBasin(d, h, w, low, o.Buried);
            r.Pool = pool;
            bool capAtRim = o.RimDisappear && pool.Enclosed;
            path.Append("pool: floor ").Append(F(pool.Floor)).Append(", rim ").Append(float.IsInfinity(pool.Rim) ? "closed" : F(pool.Rim))
                .Append(", ").Append(pool.Cells).Append(" cells, ").Append(pool.Enclosed ? "enclosed" : "not enclosed (just runs downhill)");

            float left = volume; bool outside = false;
            for (int pass = 0; pass < MaxSpillPasses && left > 1e-6f; pass++)
            {
                float input = left;
                var p = FloodPass(d, h, w, cur, o.Buried, o.MaxDepth, ref left, pass > 0, outside, o.Overflow, capAtRim && !outside ? pool.Rim : float.PositiveInfinity);
                r.Passes++;
                if (p.Thin > 0f) r.OverflowRemoved += p.Thin;
                if (outside) r.Outside += p.Added; else if (pass == 0) r.Retained += p.Added; else r.OtherBasins += p.Added;
                r.Unplaced += p.Clipped;
                r.Numeric += p.Numeric;
                r.RimRemoved += p.Capped;
                if (p.EdgeLeft > 0f)
                {
                    if (capAtRim && !outside) r.RimRemoved += p.EdgeLeft;
                    else { r.ToEdge += p.EdgeLeft; r.EdgeCell = p.EdgeCell; }
                }
                if (pass < 8)
                {
                    path.Append(" | basin ").Append(pass + 1).Append(outside ? " (outside the rim)" : "").Append(": ").Append(p.Region).Append(" cells, level ").Append(F(p.Level));
                    if (p.Thin > 0f) path.Append(", thin film outside the rim, not tracked");
                    if (p.Capped > 0f) path.Append(", full to the rim (excess removed)");
                    if (p.EdgeLeft > 0f) path.Append(", leaves the grid at cell ").Append(p.EdgeCell);
                }
                if (p.Spill < 0) break;
                bool external = outside || pool.Inside == null || !pool.Inside[p.Spill];
                if (pass < 8) path.Append(external ? ", runs over the RIM" : ", runs over an inner lip");
                if (external && !outside)
                {
                    r.RimCrossings++;
                    if (capAtRim) { r.RimRemoved += left; left = 0f; break; }
                    outside = true;
                }
                cur = p.Spill;
            }
            if (left > 1e-6f) { r.Unplaced += left; path.Append(" | ").Append(F(left)).Append(" left after ").Append(r.Passes).Append(" basins"); }
            r.Path = path.ToString();
            return r;
        }

        internal struct Pass
        {
            public float Added, Thin, Capped, EdgeLeft, Clipped, Numeric, Level;
            public int Spill, EdgeCell, Region;
        }

        // One basin: runs downhill from `start`, then a priority flood raises the level over the connected low cells until `remaining` is
        // used up. Spill >= 0: the level reached a lip and a cell behind it is lower; `remaining` is what still has to go somewhere.
        // EdgeLeft > 0: the level reached the border of the grid (MinSpillDepth over its lowest border cell). levelCap: the level never
        // goes above it, the rest is Capped (W-2). dropThin: this pass is outside the rim, a film thinner than MinSpillDepth is not kept.
        internal static Pass FloodPass(IList<float> d, IList<float> h, int w, int start, float buried, float maxDepth, ref float remaining,
            bool isSpill, bool dropThin, bool overflow, float levelCap)
        {
            var p = new Pass { Spill = -1, EdgeCell = -1 };
            int stride = w + 1, n = stride * stride;
            int cur = Descend(d, h, w, start, buried);
            var inRegion = new bool[n]; var inFrontier = new bool[n];
            var region = new List<int> { cur }; inRegion[cur] = true;
            var frontier = new List<int>();
            int[] ox = { 1, -1, 0, 0 }, oy = { 0, 0, 1, -1 };
            Action<int> addNeighbours = c =>
            {
                int cx = c % stride, cy = c / stride;
                for (int k = 0; k < 4; k++)
                {
                    int x = cx + ox[k], y = cy + oy[k];
                    if (x < 0 || y < 0 || x > w || y > w) continue;
                    int i = y * stride + x;
                    if (inRegion[i] || inFrontier[i] || h[i] <= buried) continue;
                    inFrontier[i] = true; frontier.Add(i);
                }
            };
            addNeighbours(cur);
            float level = h[cur] + d[cur], rem = remaining;
            bool edge = false, capped = false;
            int edgeF = -1; float edgeFs = float.MaxValue;
            int partial = -1; float partialAmt = 0f;
            while (rem > 1e-7f && (frontier.Count > 0 || edgeF >= 0))
            {
                float cap = edgeF >= 0 ? edgeFs + MinSpillDepth : float.MaxValue;
                int fk = -1; float fs = float.MaxValue;
                for (int k = 0; k < frontier.Count; k++) { int f0 = frontier[k]; float s = h[f0] + d[f0]; if (s < fs) { fs = s; fk = k; } }
                float target = Math.Min(Math.Min(fs, cap), levelCap);
                float need = region.Count * Math.Max(0f, target - level);
                if (rem < need) { level += rem / region.Count; rem = 0f; break; }
                rem -= need; level = Math.Max(level, target);
                if (levelCap <= fs && levelCap <= cap) { capped = true; break; }
                if (cap <= fs) { edge = true; break; }
                int f = frontier[fk]; frontier[fk] = frontier[frontier.Count - 1]; frontier.RemoveAt(frontier.Count - 1);
                int fx = f % stride, fy = f / stride;
                if (IsBorder(fx, fy, w)) { if (fs < edgeFs) { edgeFs = fs; edgeF = f; } continue; }
                float join = Math.Max(0f, level - fs);
                if (overflow && join > SpillJoin) { p.Spill = f; break; }
                if (join > rem) { partial = f; partialAmt = rem; rem = 0f; break; }
                rem -= join;
                region.Add(f); inRegion[f] = true;
                addNeighbours(f);
            }
            if (edge) { if (edgeF >= 0 && rem > 1e-7f) { p.EdgeCell = edgeF; p.EdgeLeft = rem; } rem = 0f; }
            else if (capped) { p.Capped = rem; rem = 0f; }
            else if (rem > 1e-7f && p.Spill < 0)
            {
                // closed basin (nothing left on the frontier, no way out): the level just rises - unless it is capped at the rim
                float up = rem / region.Count;
                if (level + up > levelCap) { float fit = Math.Max(0f, levelCap - level) * region.Count; p.Capped = rem - fit; level = levelCap; }
                else level += up;
                rem = 0f;
            }
            p.Level = level; p.Region = region.Count;
            // A thin film outside the rim is not tracked (W-3) - but only a film that comes to REST here. Liquid that is still on its way (it
            // reached the grid border or spills on over a further lip) is a stream, however thin it lies where it starts: it goes on to the edge
            // (next pool / runoff) or the next basin. Before 2026-10-20 the whole rest of a stroke was dropped here as soon as the first cell
            // outside the rim was thin (in game: 100 of 100 m3 'ran over the rim (thin, not tracked)' at a full pool on a slope).
            if (isSpill && dropThin && !edge && p.Spill < 0 && level - h[cur] < MinSpillDepth)
            {
                // Nothing else of this pass counts, so nothing is counted twice.
                p.Thin = remaining; p.EdgeLeft = 0f; p.EdgeCell = -1; p.Capped = 0f; p.Spill = -1;
                remaining = 0f;
                return p;
            }
            float consumed = remaining - rem, added = 0f, clipped = 0f;
            foreach (int i in region)
            {
                float want = level - h[i], nd = Math.Min(want, maxDepth * 2f);
                if (want > nd && want > d[i]) clipped += want - Math.Max(nd, d[i]);
                if (nd > d[i]) { added += nd - d[i]; d[i] = nd; }
            }
            if (partial >= 0 && partialAmt > 0f) { d[partial] += partialAmt; added += partialAmt; }
            p.Added = added; p.Clipped = clipped;
            p.Numeric = consumed - p.EdgeLeft - p.Capped - added - clipped;
            remaining = rem;
            return p;
        }

        // ------------------------------------------------------------------------------------------------ built pieces (wall pass)
        /// One wall pass, in numbers. Depth sums are cell units.
        internal sealed class RaiseResult
        {
            public int Raised, KeptCells, DisplacedCells, NoOpenNeighbour, UnplacedCells, RepeatCells;
            public float KeptDepth, Displaced, Far, Unplaced, RepeatDepth;
            public readonly List<int> Moved = new List<int>();
        }

        /// Raises the cells under built pieces (and trees / rocks / bushes: every object is just height, W-4) into the ground heights and
        /// moves only the liquid the piece really displaces (step 12): a cell's surface was (old ground + depth); after the ground rises to
        /// `top` the liquid above `top` stays where it is (a submerged piece), only the part below `top` is pushed to the open 4-neighbours,
        /// or to the nearest open cell found over the wall cells. It is never deleted (W-5): with no open cell anywhere it stays.
        /// `prevTop`: the top each cell had at the last pass. The game rewrites every height to the terrain once a second, but the liquid
        /// on a cell that was already raised to the same top stood on that top, so nothing is displaced again (step 9).
        /// `lastMoved`: cells that displaced liquid at the last pass, to count repeats (RepeatCells should be 0 for a pool at rest).
        internal static RaiseResult RaiseCells(IList<float> h, IList<float> d, int w, IDictionary<int, float> raise, IDictionary<int, float> prevTop,
            ICollection<int> lastMoved)
        {
            var r = new RaiseResult();
            int stride = w + 1, n = stride * stride;
            var isWall = new bool[n];
            var walls = new List<int>();
            var push = new List<KeyValuePair<int, float>>();
            foreach (var kv in raise)
            {
                int i = kv.Key; float top = kv.Value;
                if (i < 0 || i >= n || top <= h[i] + WallMinRise) continue;
                float baseH = h[i], pt;
                if (prevTop != null && prevTop.TryGetValue(i, out pt) && pt > baseH) baseH = pt;
                h[i] = top; r.Raised++;
                isWall[i] = true; walls.Add(i);
                if (d[i] <= 0f) continue;
                if (top - baseH <= WallMinRise) { r.KeptCells++; r.KeptDepth += d[i]; continue; }
                float stay = Math.Max(0f, baseH + d[i] - top), move = d[i] - stay;
                if (stay > 0f) { r.KeptCells++; r.KeptDepth += stay; }
                if (move <= 0f) continue;
                d[i] = stay;
                push.Add(new KeyValuePair<int, float>(i, move));
            }
            if (push.Count == 0) return r;
            int[] ox = { 1, -1, 0, 0 }, oy = { 0, 0, 1, -1 };
            int[] target = null;
            foreach (var kv in push)
            {
                int i = kv.Key; float amt = kv.Value;
                int cx = i % stride, cy = i / stride, free = 0;
                for (int k = 0; k < 4; k++)
                {
                    int x = cx + ox[k], y = cy + oy[k];
                    if (x >= 0 && y >= 0 && x <= w && y <= w && !isWall[y * stride + x]) free++;
                }
                r.DisplacedCells++;
                r.Moved.Add(i);
                if (lastMoved != null && lastMoved.Contains(i)) { r.RepeatCells++; r.RepeatDepth += amt; }
                if (free > 0)
                {
                    float share = amt / free;
                    for (int k = 0; k < 4; k++)
                    {
                        int x = cx + ox[k], y = cy + oy[k];
                        if (x >= 0 && y >= 0 && x <= w && y <= w && !isWall[y * stride + x]) d[y * stride + x] += share;
                    }
                    r.Displaced += amt;
                    continue;
                }
                r.NoOpenNeighbour++;
                if (target == null) target = NearestOpenCells(isWall, walls, w);
                int t = target[i];
                if (t < 0) { d[i] += amt; r.UnplacedCells++; r.Unplaced += amt; continue; }    // no open cell in the whole grid: it stays, it is NOT deleted
                d[t] += amt; r.Displaced += amt; r.Far += amt;
            }
            return r;
        }

        /// For every wall cell the nearest open (non-wall) cell, walking over the wall cells in 4-neighbour steps; -1 when the grid has none.
        internal static int[] NearestOpenCells(bool[] isWall, List<int> walls, int w)
        {
            int stride = w + 1, n = stride * stride;
            var target = new int[n];
            for (int i = 0; i < n; i++) target[i] = -1;
            var queue = new List<int>();
            int head = 0;
            int[] ox = { 1, -1, 0, 0 }, oy = { 0, 0, 1, -1 };
            foreach (int i in walls)
            {
                int cx = i % stride, cy = i / stride;
                for (int k = 0; k < 4; k++)
                {
                    int x = cx + ox[k], y = cy + oy[k];
                    if (x < 0 || y < 0 || x > w || y > w || isWall[y * stride + x]) continue;
                    target[i] = y * stride + x; queue.Add(i); break;
                }
            }
            while (head < queue.Count)
            {
                int i = queue[head++], cx = i % stride, cy = i / stride;
                for (int k = 0; k < 4; k++)
                {
                    int x = cx + ox[k], y = cy + oy[k];
                    if (x < 0 || y < 0 || x > w || y > w) continue;
                    int j = y * stride + x;
                    if (!isWall[j] || target[j] >= 0) continue;
                    target[j] = target[i]; queue.Add(j);
                }
            }
            return target;
        }

        internal static double Sum(IList<float> d, int n)
        {
            double s = 0d;
            for (int i = 0; i < n; i++) s += d[i];
            return s;
        }

        static string F(float v) { return v.ToString("0.000", CultureInfo.InvariantCulture); }

        /// Binary min-heap of (key, cell); System.Collections.Generic.PriorityQueue does not exist in net48.
        sealed class MinHeap
        {
            float[] _k; int[] _v; int _n;
            public MinHeap(int cap) { _k = new float[cap]; _v = new int[cap]; }
            public int Count { get { return _n; } }
            public void Push(float k, int v)
            {
                if (_n == _k.Length) { Array.Resize(ref _k, _n * 2); Array.Resize(ref _v, _n * 2); }
                int i = _n++;
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (_k[p] <= k) break;
                    _k[i] = _k[p]; _v[i] = _v[p]; i = p;
                }
                _k[i] = k; _v[i] = v;
            }
            public int Pop(out float key)
            {
                key = _k[0]; int top = _v[0];
                float k = _k[--_n]; int v = _v[_n];
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1;
                    if (l >= _n) break;
                    int c = l + 1 < _n && _k[l + 1] < _k[l] ? l + 1 : l;
                    if (_k[c] >= k) break;
                    _k[i] = _k[c]; _v[i] = _v[c]; i = c;
                }
                if (_n > 0) { _k[i] = k; _v[i] = v; }
                return top;
            }
        }
    }
}
