using System.Collections.Generic;
using UnityEngine;
using static DrainTarPit.Drain.TarPits;

namespace DrainTarPit.Drain
{
    /// <summary>
    /// The basin fill behind a deposit: FillBasin (landing cell, passes over rims) and FloodPass (one basin: run downhill, then a priority flood).
    /// </summary>
    internal static class TarPitFlood
    {
        // Called with the pit's mutex held. Returns the volume (m3) that really stayed in the basin. runoff = volume (m3) that left through the border of the
        // grid with the ground still going down there (a moat / ditch that goes on past the edge), runoffAt = that border cell in the world.
        internal static float FillBasin(LiquidVolume v, Vector3 at, float volume, out float runoff, out Vector3 runoffAt)
        {
            runoff = 0f; runoffAt = at;
            var d = Depths(v); var h = Heights(v);
            int w = v.m_width, stride = w + 1, n = stride * stride;
            if (d == null || h == null || d.Count < n || h.Count < n) return 0f;
            float cell2 = v.m_scale * v.m_scale;
            float maxDepth = FMaxDepth != null ? (float)FMaxDepth.GetValue(v) : 4f;
            // pump pool: a cell at the floor value (-m_maxDepth) is ground DEEPER than the grid's rays reach (FixMissedHeights set it), not a solid column - tar may fill it.
            // vanilla pit: -m_maxDepth means a ray that started inside the ground / hit nothing, treated as solid.
            float buried = IsPumpPool(v) ? float.NegativeInfinity : -maxDepth + 0.03f;
            var g = ToGrid(v, at);
            int cur = Mathf.Clamp(Mathf.RoundToInt(g.y), 1, w - 1) * stride + Mathf.Clamp(Mathf.RoundToInt(g.x), 1, w - 1);
            if (h[cur] <= buried)
            {
                int best = -1, bd = int.MaxValue, cx0 = cur % stride, cy0 = cur / stride;
                for (int dy = -4; dy <= 4; dy++)
                    for (int dx = -4; dx <= 4; dx++)
                    {
                        int x = cx0 + dx, y = cy0 + dy;
                        if (x < 1 || y < 1 || x > w - 1 || y > w - 1) continue;
                        int i = y * stride + x;
                        if (h[i] > buried && dx * dx + dy * dy < bd) { bd = dx * dx + dy * dy; best = i; }
                    }
                if (best < 0) return 0f;
                cur = best;
            }

            // Basin after basin: the first pass fills the basin at the landing point. When the level reaches a point of its rim that is lower than the level
            // (the lowest wall top, a dip in the wall) the rest runs over it and the next pass fills what lies behind it (up to 4 basins in all). Tar that finds
            // no basin runs off.
            float left = volume / cell2, added = 0f, run = 0f; int runCell = -1;
            // where the liquid went, for the editor / the log (LastDiag): the landing cell, then every basin it ran through
            var dOrigin = v.transform.position; float dHalf = w * v.m_scale * -0.5f, dScale = v.m_scale;
            System.Func<int, string> cellPos = c => "(" + (dOrigin.x + dHalf + (c % stride) * dScale).ToString("0.0") + ", " + (dOrigin.y + h[c]).ToString("0.00") + ", " + (dOrigin.z + dHalf + (c / stride) * dScale).ToString("0.0") + ")";
            var diag = new System.Text.StringBuilder();
            diag.Append("landing cell: ground y ").Append((dOrigin.y + h[cur]).ToString("0.00")).Append(", water ").Append(d[cur].ToString("0.00")).Append(" m deep");
            int passes = 0;
            bool overflow = ModSettings.PoolOverflow.Value;
            for (int pass = 0; pass < MaxSpillPasses && left > 1e-5f; pass++)
            {
                int spill, edgeCell, regionCells; float edgeLeft, passLevel;
                added += FloodPass(d, h, w, stride, n, cur, buried, maxDepth, ref left, pass > 0, overflow, out spill, out edgeCell, out edgeLeft, out passLevel, out regionCells);
                passes++;
                if (pass < 6)
                {
                    diag.Append(" | basin ").Append(pass + 1).Append(": ").Append(regionCells).Append(" cells, level y ").Append((dOrigin.y + passLevel).ToString("0.00"));
                    if (spill >= 0) diag.Append(", runs over the rim at ").Append(cellPos(spill));
                    else if (edgeCell >= 0) diag.Append(", leaves the grid at ").Append(cellPos(edgeCell));
                }
                if (edgeCell >= 0) { run = edgeLeft; runCell = edgeCell; }
                if (spill < 0) break;
                cur = spill;
            }
            if (left > 1e-5f) diag.Append(" | ").Append((left * cell2).ToString("0.00")).Append(" m3 left after ").Append(passes).Append(" basins");
            else if (passes > 6) diag.Append(" | ").Append(passes).Append(" basins in all");
            Diag = diag.ToString();
            if (runCell >= 0 && run > 1e-5f)
            {
                var o = v.transform.position; float half = w * v.m_scale * -0.5f;
                runoff = run * cell2;
                runoffAt = new Vector3(o.x + half + (runCell % stride) * v.m_scale, o.y + h[runCell], o.z + half + (runCell / stride) * v.m_scale);
            }
            return added * cell2;
        }

        internal static string Diag = "";

        internal static float DropThin;      // depth sum (cell units) FloodPass dropped as a thin film behind a rim during the current FillBasin

        const int MaxSpillPasses = 48;          // basins in a row one stroke may run through (was 4: the liquid that was still left after the 4th basin was lost, see CHANGELOG)
        const float MinSpillDepth = 0.04f;      // m: a film thinner than this, behind a rim, is not kept (it runs off)

        // One basin: runs downhill from `start` to the lowest cell, then raises the level over the connected low cells until `remaining` (depth sum in cell units)
        // is used up. Returns the depth sum added. spill >= 0 = the level reached the rim and a cell behind it is lower than the level: the tar runs over it,
        // `remaining` is what still has to go somewhere.
        // `spill` >= 0: the level reached a lower cell behind the rim (see above). edgeCell >= 0: the level reached the border of the grid (4 cm over its lowest
        // cell) and edgeLeft (depth sum) is what leaves the grid there.
        static float FloodPass(List<float> d, List<float> h, int w, int stride, int n, int start, float buried, float maxDepth, ref float remaining, bool isSpill, bool overflow, out int spill, out int edgeCell, out float edgeLeft, out float levelOut, out int regionOut)
        {
            spill = -1; edgeCell = -1; edgeLeft = 0f; levelOut = 0f; regionOut = 0;
            int cur = start;

            // 1) run downhill to the lowest cell around the start point
            for (int step = 0; step < 60; step++)
            {
                int cx = cur % stride, cy = cur / stride, next = cur; float sBest = h[cur] + d[cur];
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int x = cx + dx, y = cy + dy;
                        if (x < 1 || y < 1 || x > w - 1 || y > w - 1) continue;
                        int i = y * stride + x;
                        if (h[i] <= buried) continue;
                        float s = h[i] + d[i];
                        if (s < sBest - 0.0005f) { sBest = s; next = i; }
                    }
                if (next == cur) break;
                cur = next;
            }

            // 2) priority flood: raise the level over the connected low cells until the volume is used up
            var inRegion = new bool[n]; var inFrontier = new bool[n];
            var region = new List<int> { cur }; inRegion[cur] = true;
            var frontier = new List<int>();
            int[] ox = { 1, -1, 0, 0 }, oy = { 0, 0, 1, -1 };
            System.Action<int> addNeighbours = c =>
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
            bool edge = false;
            int edgeF = -1; float edgeFs = float.MaxValue;           // lowest cell of the grid's border zone seen so far = the way out of this basin
            int partial = -1; float partialAmt = 0f;                 // a lower cell behind a lip that could only be filled partly
            while (rem > 1e-5f && (frontier.Count > 0 || edgeF >= 0))
            {
                float cap = edgeF >= 0 ? edgeFs + MinSpillDepth : float.MaxValue;     // the level at which the liquid runs out over the border of the grid
                int fk = -1; float fs = float.MaxValue;
                for (int k = 0; k < frontier.Count; k++) { int f0 = frontier[k]; float s = h[f0] + d[f0]; if (s < fs) { fs = s; fk = k; } }
                float target = Mathf.Min(fs, cap);
                float need = region.Count * Mathf.Max(0f, target - level);
                if (rem < need) { level += rem / region.Count; rem = 0f; break; }
                rem -= need; level = Mathf.Max(level, target);
                if (cap <= fs) { edge = true; break; }                // the level reached the way out: the rest leaves the grid there
                int f = frontier[fk]; frontier[fk] = frontier[frontier.Count - 1]; frontier.RemoveAt(frontier.Count - 1);
                int fx = f % stride, fy = f / stride;
                if (fx <= 1 || fy <= 1 || fx >= w - 1 || fy >= w - 1)  // the border of the grid is not part of the basin; it is where the liquid leaves once it stands 4 cm over it
                {
                    if (fs < edgeFs) { edgeFs = fs; edgeF = f; }
                    continue;
                }
                // a cell lower than the level: the level is at the lowest point of the rim and the liquid runs over it
                float join = Mathf.Max(0f, level - fs);
                if (overflow && join > 0.02f) { spill = f; break; }
                // (overflow off) a deeper basin behind the lip has to be filled up to the level too, and that costs liquid
                if (join > rem) { partial = f; partialAmt = rem; rem = 0f; break; }
                rem -= join;
                region.Add(f); inRegion[f] = true;
                addNeighbours(f);
            }
            if (edge) { if (edgeF >= 0 && rem > 1e-5f) { edgeCell = edgeF; edgeLeft = rem; } rem = 0f; }
            else if (rem > 1e-5f && spill < 0) { level += rem / region.Count; rem = 0f; }   // closed basin (nothing left on the frontier, no way out): the level just rises
            levelOut = level; regionOut = region.Count;
            if (isSpill && level - h[cur] < MinSpillDepth) { DropThin += remaining; remaining = 0f; spill = -1; return 0f; }     // a thin film behind the rim: not kept, runs off (counted for the editor)

            float added = 0f;
            foreach (int i in region)
            {
                float nd = Mathf.Min(level - h[i], maxDepth * 2f);
                if (nd > d[i]) { added += nd - d[i]; d[i] = nd; }
            }
            if (partial >= 0 && partialAmt > 0f) { d[partial] += partialAmt; added += partialAmt; }
            remaining = rem;
            return added;
        }
    }
}
