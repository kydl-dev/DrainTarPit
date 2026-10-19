using System.Collections.Generic;
using UnityEngine;
using static DrainTarPit.Drain.TarPits;
using static DrainTarPit.Drain.TarPitWalls;

namespace DrainTarPit.Drain
{
    /// <summary>
    /// Numbers about a pool for the live editor and the test harness: the water budget (where the poured liquid went), pool statistics, the last deposit.
    /// </summary>
    internal static class TarPitStats
    {
        // --- numbers for the live editor (WaterEditor) --------------------------------------------------------------------
        internal static string LastDiag = "-";          // the path of the last deposit: landing cell and every basin it ran through (owner only)

        internal static string LastDeposit = "-";      // what the last DepositLocal on this machine did (owner only)

        // Where did the poured liquid go? Counted per pool (owner only, since the world was loaded), shown by the editor:
        // Stayed = kept in this grid (of it: Inner = in further dips inside the pool, OutsideRim = in a basin outside the rim), Edge = reached the border of the grid
        // (continues into the next pool or runs off), Thin = ran over the RIM and lay as a thin film outside (W-3, not tracked), RimRemoved = removed at the rim
        // ([Drain] PoolRimDisappear on, W-2), Lost = unplaced (no reachable cell / too many basins), ShedEdge = films that ShedFilms let run off the grid border,
        // Evap = films that ShedFilms evaporated. If 'Stayed' is a small part of 'Poured' the water is not staying in the pool, and the other numbers say why.
        internal sealed class Budget { public float Poured, Stayed, Inner, OutsideRim, Edge, Thin, RimRemoved, Lost, ShedEdge, Evap, WallPushed, WallLost, WallKept, WallRepeat; public int WallPasses; internal Budget Clone() { return (Budget)MemberwiseClone(); } }
        internal static readonly Dictionary<LiquidVolume, Budget> Budgets = new();

        internal static Budget BudgetOf(LiquidVolume v)
        {
            Budget b;
            if (!Budgets.TryGetValue(v, out b)) Budgets[v] = b = new Budget();
            return b;
        }

        internal static string BudgetText(LiquidVolume v)
        {
            Budget b;
            if (v == null || !Budgets.TryGetValue(v, out b)) return "nothing poured into this pool on this machine yet";
            return "poured " + b.Poured.ToString("0.0") + " m3 -> stayed " + b.Stayed.ToString("0.0") + " (inner dips " + b.Inner.ToString("0.0") + ", outside the rim " + b.OutsideRim.ToString("0.0") + "), reached the grid edge " + b.Edge.ToString("0.0") +
                   ", removed at the rim " + b.RimRemoved.ToString("0.0") + ", ran over the rim (thin, not tracked) " + b.Thin.ToString("0.0") + ", unplaced " + b.Lost.ToString("0.0") +
                   "   |   later: films run off the grid border " + b.ShedEdge.ToString("0.0") + ", evaporated " + b.Evap.ToString("0.0") +
                   "   |   wall pass: displaced to open cells " + b.WallPushed.ToString("0.0") + ", no open cell (stayed, not deleted) " + b.WallLost.ToString("0.0") + ", moved again from the same cells " + b.WallRepeat.ToString("0.0") + " in " + b.WallPasses + " passes";
        }

        internal struct PoolStats { public int Width; public float Scale, Total, Max; public int Wet, Thin, WallCells; public bool Owner, Pump; }

        internal static bool Stats(LiquidVolume v, out PoolStats s)
        {
            s = default(PoolStats);
            if (v == null) return false;
            var d = Depths(v);
            int w = v.m_width, n = (w + 1) * (w + 1);
            if (d == null || d.Count < n) return false;
            float film = ModSettings.PoolFilmDepth.Value, sum = 0f;
            using (Hold(v))
            {
                for (int i = 0; i < n; i++)
                {
                    float di = d[i];
                    if (di <= 0.001f) continue;
                    s.Wet++; sum += di;
                    if (di > s.Max) s.Max = di;
                    if (di < film) s.Thin++;
                }
            }
            s.Width = w; s.Scale = v.m_scale; s.Total = sum * v.m_scale * v.m_scale;
            var nv = Nview(v);
            s.Owner = nv != null && nv.IsValid() && nv.IsOwner();
            s.Pump = IsPumpPool(v);
            s.WallCells = WallCellCount(v);
            return true;
        }

        /// The loaded volume of this type whose grid centre is closest (in x / z) to p.
        internal static LiquidVolume Nearest(Vector3 p, LiquidType type)
        {
            LiquidVolume best = null; float bd = float.MaxValue;
            foreach (var v in All)
            {
                if (v == null || v.m_liquidType != type) continue;
                var o = v.transform.position; float dx = o.x - p.x, dz = o.z - p.z, dd = dx * dx + dz * dz;
                if (dd < bd) { bd = dd; best = v; }
            }
            return best;
        }
    }
}
