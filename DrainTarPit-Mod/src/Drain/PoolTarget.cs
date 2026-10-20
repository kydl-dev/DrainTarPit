using UnityEngine;

namespace DrainTarPit.Drain
{
    /// <summary>Which existing volume may receive a stroke (TarPool.FindVolume).</summary>
    internal static class PoolTarget
    {
        // An existing volume of this liquid with the point well inside its grid; the closest centre wins.
        // Water is special: only pools marked with this mod's ZDO flag may receive pumped water. Older test runs
        // left many empty, unflagged DTP_WaterPool2 instances overlapping the real pool; picking one silently made
        // a pour disappear from the pool the user expected to measure. Tar may still be deposited into vanilla tar pits.
        internal static LiquidVolume Find(Vector3 p, PumpLiquid liquid)
        {
            LiquidVolume best = null; float bd = float.MaxValue;
            foreach (var v in TarPits.All)
            {
                if (v == null || TarPits.LiquidOf(v) != liquid) continue;
                if (!TarPoolDiagnostics.HasUsableGridGeometry(v)) continue;
                var g = TarPits.ToGrid(v, p);
                if (!TarPits.InGrid(v, g) || !TarPits.Contains(v, p, 1.5f)) continue;
                if (liquid == PumpLiquid.Water && !TarPits.IsPumpPool(v)) continue;
                if (!TarPits.IsPumpPool(v) && !VanillaTakes(v, p)) continue;      // a vanilla pit only when poured INTO its tar (see HasTarNear)
                float d = (v.transform.position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = v; }
            }
            return best;
        }

        // User, 2026-10-20: tar poured into a hole dug ~28 m from a vanilla tar pit, walls built up from the hole's edge, stopped at the edge of the
        // ground. The vanilla pit's grid is 64 m wide, so it covered the hole and received the tar - and walls only count for this mod's own pools,
        // so the tar could not rise against them (it then ran off the pit's grid). A vanilla pit now receives tar only where the landing point (or a
        // cell next to it) already holds tar = pouring back into the pit itself; anywhere else a pump tar pool takes it (made if needed), with walls.
        // Second user test (2026-10-20): the first strokes had already put tar into the hole as the PIT's tar, so 'has tar here' kept choosing
        // the pit. A vanilla pit also has to be close: its natural tar lies within about VanillaReach of its centre.
        const float VanillaReach = 16f;

        internal static bool VanillaTakes(LiquidVolume v, Vector3 p)
        {
            var c = v.transform.position;
            return new Vector2(p.x - c.x, p.z - c.z).magnitude <= VanillaReach && HasTarNear(v, p);
        }

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
