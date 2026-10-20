using UnityEngine;

namespace DrainTarPit.Drain
{
    /// <summary>Which existing volume may receive a stroke (TarPool.FindVolume).</summary>
    internal static class PoolTarget
    {
        // User, 2026-10-20: tar poured into a hole dug ~28 m from a vanilla tar pit, walls built up from the hole's edge, stopped at the edge of the
        // ground. The vanilla pit's grid is 64 m wide, so it covered the hole and received the tar - and walls only count for this mod's own pools,
        // so the tar could not rise against them (it then ran off the pit's grid). A vanilla pit now receives tar only where the landing point (or a
        // cell next to it) already holds tar = pouring back into the pit itself; anywhere else a pump tar pool takes it (made if needed), with walls.
        internal static bool HasTarNear(LiquidVolume v, Vector3 p)
        {
            var g = TarPits.ToGrid(v, p);
            int w = v.m_width, stride = w + 1, cx = Mathf.RoundToInt(g.x), cy = Mathf.RoundToInt(g.y);
            using (TarPits.Hold(v))
            {
                var d = TarPits.Depths(v);
                if (d == null || d.Count < stride * stride) return false;
                for (int y = cy - 1; y <= cy + 1; y++)
                    for (int x = cx - 1; x <= cx + 1; x++)
                        if (x >= 0 && y >= 0 && x <= w && y <= w && d[y * stride + x] >= 0.05f) return true;
            }
            return false;
        }

    }
}
