using UnityEngine;

namespace DrainTarPit.Debug
{
    /// <summary>
    /// Heights of the synthetic test pools (dtp ptest tank / shape / walls, steps 8 and 14), relative to the pool origin, by offset (dx, dy)
    /// from the grid centre. Every shape is plain height (user spec W-4: an obstacle is not a special wall). H = Top - Floor.
    ///   closed   square tank, ring 3 cells wide at Top, outside ground 0.5 m below the floor.
    ///   gap      closed, with a notch 3 cells wide on the +x side down to Floor + 0.4 H: the liquid leaves there before the tank is full.
    ///   mixed    closed ring whose four sides have different heights (Top, 0.85 H, 0.7 H, 0.5 H): the lowest side (-y) is the rim.
    ///   pillars  the ring is a row of 2-cell pillars with 1-cell gaps down to the floor: not a basin (only height decides, Q16).
    ///   island   no ring; open ground sloping down to +x, one 3x3 pillar 5 cells downhill of the landing point: the liquid goes around it.
    ///   inside   closed, with a tall 3x3 obstacle (Top) at +4 and a submerged 5x5 step (Floor + 0.2) at -4 inside the tank.
    /// </summary>
    internal static class PoolShapes
    {
        internal static readonly string[] All = { "closed", "gap", "mixed", "pillars", "island", "inside" };

        internal static bool Known(string s) { return System.Array.IndexOf(All, s) >= 0; }

        internal static float Height(PoolTankTests.Tank t, int dx, int dy)
        {
            float H = t.Top - t.Floor, outside = t.Floor - 0.5f;
            int ax = Mathf.Abs(dx), ay = Mathf.Abs(dy), cheb = Mathf.Max(ax, ay);
            bool inTank = cheb <= t.R, inRing = !inTank && cheb <= t.R + t.Ring;
            switch (t.Shape)
            {
                case "gap":
                    if (inRing && dx > t.R && ay <= 1) return t.Floor + 0.4f * H;
                    break;
                case "mixed":
                    if (inRing)
                    {
                        // which side: the larger offset decides; a corner belongs to the lower of its two sides
                        float sx = dx > 0 ? t.Top : t.Floor + 0.85f * H, sy = dy > 0 ? t.Floor + 0.7f * H : t.Floor + 0.5f * H;
                        if (ax > t.R && ay > t.R) return Mathf.Min(sx, sy);
                        return ax > t.R ? sx : sy;
                    }
                    break;
                case "pillars":
                    if (inRing)
                    {
                        int along = ax >= ay ? dy + 1000 : dx + 1000;      // position along the side
                        return along % 3 == 2 ? t.Floor : t.Top;          // two cells of pillar, one cell of gap at floor height
                    }
                    break;
                case "island":
                    if (dx >= 4 && dx <= 6 && ay <= 1) return t.Top;
                    return t.Floor - 0.02f * dx;                            // 2 cm per cell downhill to +x
                case "inside":
                    if (inTank && dx >= 3 && dx <= 5 && ay <= 1) return t.Top;
                    if (inTank && dx >= -6 && dx <= -2 && ay <= 2) return t.Floor + 0.2f;
                    break;
            }
            return inTank ? t.Floor : inRing ? t.Top : outside;
        }

        /// The level the liquid should stand at in the tank when it is full (relative to the pool origin), by construction. NaN = no basin.
        internal static float ExpectedRim(PoolTankTests.Tank t)
        {
            float H = t.Top - t.Floor;
            switch (t.Shape)
            {
                case "gap": return t.Floor + 0.4f * H;
                case "mixed": return t.Floor + 0.5f * H;
                case "pillars": case "island": return float.NaN;
                default: return t.Top;
            }
        }

        /// Capacity (m3) of the tank up to ExpectedRim, from the shape's heights; 0 when there is no basin.
        internal static float Capacity(PoolTankTests.Tank t, float cellArea)
        {
            float rim = ExpectedRim(t);
            if (float.IsNaN(rim)) return 0f;
            double sum = 0d;
            for (int dy = -t.R; dy <= t.R; dy++)
                for (int dx = -t.R; dx <= t.R; dx++) sum += Mathf.Max(0f, rim - Height(t, dx, dy));
            return (float)sum * cellArea;
        }

        /// Cells of the tall obstacle (island pillar / inside obstacle): no liquid may stand on them (W-4: it goes around them).
        internal static bool IsObstacle(PoolTankTests.Tank t, int dx, int dy)
        {
            int ay = Mathf.Abs(dy);
            if (t.Shape == "island") return dx >= 4 && dx <= 6 && ay <= 1;
            if (t.Shape == "inside") return dx >= 3 && dx <= 5 && ay <= 1;
            return false;
        }
    }
}
