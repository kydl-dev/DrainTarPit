using System.Collections.Generic;
using DrainTarPit.Drain;
using DrainTarPit.Logging;
using UnityEngine;

namespace DrainTarPit.Signs
{
    /// <summary>
    /// T-60 (user: "allow crafting pool signs only when close to a pool, preferably on top of it, but being within 5m of a pool is accepted" and
    /// "allow placing signs on tar pools"): the pool a pool sign belongs to. A pool = a pool the pump made (water or tar) or one of the game's
    /// tar pits. The pool under the post wins (distance 0), else the closest one whose liquid is within Reach (horizontal distance), deeper first
    /// on a tie. Used by the build ghost (PoolSignPlacement) and by every placed sign (PoolSign.Pool).
    /// </summary>
    internal static class PoolFinder
    {
        internal const float Reach = 5f;
        const float Step = 0.5f;          // ring spacing of the search around the post
        const int Dirs = 16;              // points per ring
        const float Wet = 0.02f;          // m of liquid that counts as "pool here"

        /// A pool a sign can name: a pump pool (water or tar) or a tar pit of the game.
        internal static bool Accepts(LiquidVolume v) { return v != null && (TarPits.IsTar(v) || TarPits.IsPumpPool(v)); }

        /// The pool under `p` or the closest one within `reach`; `dist` = horizontal distance to its liquid (0 = on top, -1 = none found).
        internal static LiquidVolume Find(Vector3 p, float reach, out float dist)
        {
            dist = -1f;
            LiquidVolume best = null;
            float bestD = float.MaxValue, bestDepth = 0f;
            foreach (var v in TarPits.All)
            {
                if (!Accepts(v)) continue;
                float depth;
                float d = Distance(v, p, reach, out depth);
                if (d < 0f) continue;
                if (d < bestD - 0.01f || (Mathf.Abs(d - bestD) <= 0.01f && depth > bestDepth)) { best = v; bestD = d; bestDepth = depth; }
            }
            if (best != null) dist = bestD;
            ModLog.Verbose("PoolFinder: at " + p.ToString("0.0") + (best != null ? " pool " + best.name + " (" + LiquidName(best) + ") " + (bestD <= 0f ? "under it" : bestD.ToString("0.0") + " m away") : " no pool within " + reach + " m"));
            return best;
        }

        /// Horizontal distance from `p` to the liquid of `v` (0 = `p` is over it), -1 = none within `reach` or its depths are not loaded yet.
        static float Distance(LiquidVolume v, Vector3 p, float reach, out float depth)
        {
            depth = 0f;
            var g = TarPits.ToGrid(v, p);
            float m = reach / Mathf.Max(0.01f, v.m_scale);
            if (g.x < -m || g.y < -m || g.x > v.m_width + m || g.y > v.m_width + m) return -1f;      // too far away: not sampled at all
            using (TarPits.Hold(v))
            {
                var d = TarPits.Depths(v);
                if (d == null || d.Count < (v.m_width + 1) * (v.m_width + 1)) return -1f;
                depth = DepthAt(v, d, p);
                if (depth > Wet) return 0f;
                for (float r = Step; r <= reach + 0.001f; r += Step)
                    for (int i = 0; i < Dirs; i++)
                    {
                        float a = i * Mathf.PI * 2f / Dirs;
                        float dep = DepthAt(v, d, p + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
                        if (dep > Wet) { depth = dep; return r; }
                    }
            }
            return -1f;
        }

        static float DepthAt(LiquidVolume v, List<float> d, Vector3 w)
        {
            var g = TarPits.ToGrid(v, w);
            return TarPits.InGrid(v, g) ? TarPits.DepthAt(v, d, g.x, g.y) : 0f;
        }

        /// "tar" / "water" for the sign's ZDO (the pool menu greys out the water style of tar pools), "" = no pool.
        internal static string LiquidName(LiquidVolume v) { return v == null ? "" : TarPits.IsTar(v) ? "tar" : "water"; }
    }
}
