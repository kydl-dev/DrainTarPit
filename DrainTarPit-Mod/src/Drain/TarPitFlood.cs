using UnityEngine;
using static DrainTarPit.Drain.TarPits;

namespace DrainTarPit.Drain
{
    /// <summary>
    /// The basin fill behind a deposit. The rules live in HeightField (pure, unit tested); this class connects them to a LiquidVolume:
    /// grid, landing cell, config switches ([Drain] PoolOverflow, PoolRimDisappear) and the diagnostic text for the editor / log.
    /// </summary>
    internal static class TarPitFlood
    {
        internal static string Diag = "";

        /// Called with the pool's lock held. Pours `volume` m3 at world point `at` into the basin around it and returns where every part of it
        /// went (HeightField.FillResult, cell units), or null when the grid is not usable (then nothing was changed).
        internal static HeightField.FillResult FillBasin(LiquidVolume v, Vector3 at, float volume)
        {
            var d = Depths(v); var h = Heights(v);
            int w = v.m_width, stride = w + 1, n = stride * stride;
            if (d == null || h == null || d.Count < n || h.Count < n || w < 3) return null;
            float cell2 = v.m_scale * v.m_scale;
            float maxDepth = FMaxDepth != null ? (float)FMaxDepth.GetValue(v) : 4f;
            // pump pool: a cell at the floor value (-m_maxDepth) is ground DEEPER than the grid's rays reach (FixMissedHeights set it), not a solid column.
            // vanilla pit: -m_maxDepth means a ray that started inside the ground / hit nothing, treated as solid.
            var opt = new HeightField.FillOptions
            {
                Overflow = ModSettings.PoolOverflow.Value,
                RimDisappear = ModSettings.PoolRimDisappear != null && ModSettings.PoolRimDisappear.Value,
                Buried = IsPumpPool(v) ? float.NegativeInfinity : -maxDepth + 0.03f,
                MaxDepth = maxDepth
            };
            var g = ToGrid(v, at);
            int cur = Mathf.Clamp(Mathf.RoundToInt(g.y), 1, w - 1) * stride + Mathf.Clamp(Mathf.RoundToInt(g.x), 1, w - 1);
            float landDepth = d[cur], landGround = h[cur];
            var r = HeightField.Fill(d, h, w, cur, volume / cell2, opt);
            Diag = "landing cell: ground y " + (v.transform.position.y + landGround).ToString("0.00") + ", liquid " + landDepth.ToString("0.00") + " m deep; heights below are relative to the pool origin y " +
                   v.transform.position.y.ToString("0.00") + "; rim switch " + (opt.RimDisappear ? "ON (excess disappears at the rim)" : "OFF (fill, then over the rim)") + " | " + r.Path;
            return r;
        }

        /// World position of a grid cell (ground height).
        internal static Vector3 CellWorld(LiquidVolume v, int cell)
        {
            int stride = v.m_width + 1;
            var o = v.transform.position; float half = v.m_width * v.m_scale * -0.5f;
            var h = Heights(v);
            float y = h != null && cell >= 0 && cell < h.Count ? h[cell] : 0f;
            return new Vector3(o.x + half + (cell % stride) * v.m_scale, o.y + y, o.z + half + (cell / stride) * v.m_scale);
        }
    }
}
