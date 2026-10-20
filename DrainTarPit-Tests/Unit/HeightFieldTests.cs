using System;
using System.Collections.Generic;
using DrainTarPit.Drain;

namespace DrainTarPit.Tests.Unit
{
    /// <summary>
    /// The liquid rules of the pump pools (HeightField, user spec W-1 - W-5, steps 6 / 8 / 9 / 11 / 12 / 13 / 14) on synthetic grids. These run
    /// the same source file the mod runs; they do not replace the in-game tests (the game's own simulation, saving and colliders are not here).
    /// </summary>
    [TestFixture]
    public class HeightFieldTests
    {
        const int W = 40;                       // 41 x 41 cells
        const int Stride = W + 1, N = Stride * Stride, C = W / 2;
        const float Eps = 1e-3f;

        static int Cell(int dx, int dy) { return (C + dy) * Stride + C + dx; }

        /// The synthetic tank of the in-game tests (PoolShapes): floor 0, ring 3 cells wide, outside 0.5 m lower.
        static float[] Tank(int r, float top, Func<int, int, float?> shape = null)
        {
            var h = new float[N];
            for (int y = 0; y <= W; y++)
                for (int x = 0; x <= W; x++)
                {
                    int dx = x - C, dy = y - C, cheb = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    float v = cheb <= r ? 0f : cheb <= r + 3 ? top : -0.5f;
                    var s = shape != null ? shape(dx, dy) : null;
                    h[y * Stride + x] = s ?? v;
                }
            return h;
        }

        static float Capacity(float[] h, int r, float rim)
        {
            double s = 0;
            for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++) s += Math.Max(0f, rim - h[Cell(dx, dy)]);
            return (float)s;
        }

        static float InTank(float[] d, int r)
        {
            double s = 0;
            for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++) s += d[Cell(dx, dy)];
            return (float)s;
        }

        static float Total(float[] d) { return (float)HeightField.Sum(d, N); }

        static HeightField.FillResult Pour(float[] d, float[] h, float vol, bool rim = false, int at = -1)
        {
            var r = HeightField.Fill(d, h, W, at < 0 ? Cell(0, 0) : at, vol, new HeightField.FillOptions { RimDisappear = rim, MaxDepth = 12f });
            Assert.That(Math.Abs(r.Residual), Is.LessThan(Eps * Math.Max(1f, vol)), "accounting must balance: " + r.Path);
            foreach (var x in d) Assert.That(x, Is.GreaterThanOrEqualTo(0f), "negative depth");
            return r;
        }

        // ---------------------------------------------------------------- basins and the rim switch (W-1 - W-3, steps 11 / 13)

        [Test]
        public void ClosedTank_RimOff_FillsToTheRimThenRunsOverIt()
        {
            var h = Tank(8, 1f); var d = new float[N];
            float cap = Capacity(h, 8, 1f), before = Total(d);
            var r = Pour(d, h, cap * 2f);
            Assert.That(r.Pool.Enclosed, Is.True);
            Assert.That(r.Pool.Rim, Is.EqualTo(1f).Within(1e-4));
            Assert.That(InTank(d, 8), Is.EqualTo(cap).Within(cap * 0.01f), r.Path);
            Assert.That(d[Cell(0, 0)], Is.EqualTo(1f).Within(0.01f), "level = rim");
            Assert.That(r.RimRemoved, Is.EqualTo(0f));
            Assert.That(r.RimCrossings, Is.EqualTo(1));
            Assert.That(r.Outside + r.ToEdge + r.OverflowRemoved, Is.EqualTo(cap).Within(cap * 0.01f), "the rest went over the rim");
            Assert.That(Total(d) - before, Is.EqualTo(r.Kept).Within(Eps * cap), "measured = kept");
        }

        // ---------------------------------------------------------------- a FULL pool, rim switch off (user, 2026-10-20: 100 m3 strokes into the
        // real pool at its rim were all booked 'ran over the rim (thin, not tracked)' and disappeared; the water should run over the rim and away)

        /// Ground outside the ring falls away towards the grid border (a pool on a shore), 0.1 m per cell.
        static float[] TankOnSlope(int r, float top)
        {
            return Tank(r, top, (dx, dy) =>
            {
                int cheb = Math.Max(Math.Abs(dx), Math.Abs(dy));
                return cheb > r + 3 ? -0.5f - 0.1f * (cheb - r - 3) : (float?)null;
            });
        }

        [Test]
        public void FullTank_RimOff_OverflowRunsDownhillToTheGridEdge_NotDeleted()
        {
            var h = TankOnSlope(8, 1f); var d = new float[N];
            float cap = Capacity(h, 8, 1f);
            Pour(d, h, cap);                                                          // full to the rim
            var r = Pour(d, h, 100f);
            Assert.That(r.Retained + r.OtherBasins, Is.EqualTo(0f).Within(0.5f), "the pool is full: " + r.Path);
            Assert.That(InTank(d, 8), Is.EqualTo(cap).Within(cap * 0.01f), "the pool stays full");
            Assert.That(r.RimCrossings, Is.EqualTo(1), r.Path);
            Assert.That(r.OverflowRemoved, Is.LessThan(1f), "a 100-unit stream is not a thin film: " + r.Path);
            Assert.That(r.ToEdge + r.Outside, Is.EqualTo(100f).Within(1f), "it ran over the rim and on downhill: " + r.Path);
            Assert.That(r.ToEdge, Is.GreaterThan(50f), "most of it leaves at the grid edge (next pool / runoff): " + r.Path);
            Assert.That(r.RimRemoved, Is.EqualTo(0f));
        }

        [Test]
        public void FullTank_RimOff_OverflowFillsABasinOutsideTheRim()
        {
            // ring 1 m, a moat 1 m deep around it (cheb 12 - 14), beyond the moat the ground is 0.8 m above the moat floor, flat to the border
            var h = Tank(8, 1f, (dx, dy) =>
            {
                int cheb = Math.Max(Math.Abs(dx), Math.Abs(dy));
                return cheb >= 12 && cheb <= 14 ? -1f : cheb > 14 ? -0.2f : (float?)null;
            });
            var d = new float[N];
            float cap = Capacity(h, 8, 1f);
            Pour(d, h, cap);
            var r = Pour(d, h, 40f);
            Assert.That(r.Outside, Is.EqualTo(40f).Within(0.5f), "the moat outside the rim catches it: " + r.Path);
            Assert.That(r.OverflowRemoved + r.ToEdge, Is.EqualTo(0f).Within(0.5f), r.Path);
            Assert.That(d[Cell(13, 0)], Is.GreaterThan(0.05f), "water stands in the moat");
            Assert.That(InTank(d, 8), Is.EqualTo(cap).Within(cap * 0.01f));
        }

        [Test]
        public void FullTank_RimOff_SmallSpillOnFlatGround_IsAThinFilmNotTracked()
        {
            var h = Tank(8, 1f); var d = new float[N];                               // outside: flat, 0.5 m below the tank floor
            float cap = Capacity(h, 8, 1f);
            Pour(d, h, cap);
            float outsideBefore = Total(d) - InTank(d, 8);
            var r = Pour(d, h, 2f);
            Assert.That(r.OverflowRemoved, Is.EqualTo(2f).Within(0.05f), "2 units over ~1000 flat cells = a film far under 4 cm: " + r.Path);
            Assert.That(Total(d) - InTank(d, 8) - outsideBefore, Is.EqualTo(0f).Within(Eps), "nothing is written outside");
        }

        [Test]
        public void FullTank_RimOn_EverythingIsRemovedAtTheRim()
        {
            var h = TankOnSlope(8, 1f); var d = new float[N];
            float cap = Capacity(h, 8, 1f);
            Pour(d, h, cap, rim: true);
            var r = Pour(d, h, 100f, rim: true);
            Assert.That(r.RimRemoved, Is.EqualTo(100f).Within(0.5f), r.Path);
            Assert.That(r.ToEdge + r.Outside + r.OverflowRemoved, Is.EqualTo(0f).Within(Eps));
        }

        [Test]
        public void ClosedTank_RimOn_FillsToTheRimAndRemovesTheRestThere()
        {
            var h = Tank(8, 1f); var d = new float[N];
            float cap = Capacity(h, 8, 1f);
            var r = Pour(d, h, cap * 2f, rim: true);
            Assert.That(InTank(d, 8), Is.EqualTo(cap).Within(cap * 0.01f));
            Assert.That(r.RimRemoved, Is.EqualTo(cap).Within(cap * 0.01f));
            Assert.That(r.OverflowRemoved + r.ToEdge + r.Outside, Is.EqualTo(0f).Within(Eps), "nothing goes over the rim");
            Assert.That(Total(d), Is.EqualTo(InTank(d, 8)).Within(Eps), "no liquid outside the tank");
        }

        [Test]
        public void BumpyFloor_StrokesBelowCapacity_AreKeptCompletely()
        {
            // T-09: 72.5 % of each stroke was dropped as a "thin film behind a rim" inside the pool. Centimetre bumps make internal lips.
            var rnd = new Random(7);
            var h = Tank(12, 2f, (dx, dy) => Math.Max(Math.Abs(dx), Math.Abs(dy)) <= 12 ? (float?)(float)(rnd.NextDouble() * 0.03) : null);
            var d = new float[N];
            float cap = Capacity(h, 12, 2f), poured = 0f;
            for (int k = 0; k < 20; k++)
            {
                float vol = cap * 0.04f; poured += vol;
                var r = Pour(d, h, vol, at: Cell(k % 5 - 2, k % 3 - 1));
                Assert.That(r.Kept, Is.EqualTo(vol).Within(vol * 1e-3f), "stroke " + k + ": " + r.Path);
                Assert.That(r.OverflowRemoved + r.Unplaced + r.ToEdge + r.RimRemoved, Is.EqualTo(0f).Within(Eps), "stroke " + k);
            }
            Assert.That(InTank(d, 12), Is.EqualTo(poured).Within(poured * 1e-3f));
        }

        [Test]
        public void ThinFilmBehindAnInnerLip_StaysInThePool()
        {
            // a flat floor split by a 3 cm lip: the second half gets a film thinner than MinSpillDepth, it must not be dropped
            var h = Tank(12, 1f, (dx, dy) => Math.Max(Math.Abs(dx), Math.Abs(dy)) <= 12 ? (float?)(dx == 0 ? 0.03f : dx > 0 ? -0.01f : 0f) : null);
            var d = new float[N];
            float leftCap = 12 * 25 * 0.03f;            // the left half up to the lip; the 30 % over it spreads 9 mm thin over the right half
            var r = Pour(d, h, leftCap * 1.3f, at: Cell(-6, 0));
            Assert.That(r.OverflowRemoved, Is.EqualTo(0f), r.Path);
            Assert.That(r.Kept, Is.EqualTo(leftCap * 1.3f).Within(Eps));
            Assert.That(r.OtherBasins, Is.GreaterThan(0f), "the part behind the lip is booked as an inner basin");
        }

        [Test]
        public void GapBelowTheRim_LiquidLeavesThroughItBeforeTheTankIsFull()
        {
            var h = Tank(8, 1f, (dx, dy) => dx > 8 && dx <= 11 && Math.Abs(dy) <= 1 ? (float?)0.4f : null);
            var d = new float[N];
            float capGap = Capacity(h, 8, 0.4f);
            var small = Pour(d, h, capGap * 0.5f);
            Assert.That(small.Kept, Is.EqualTo(capGap * 0.5f).Within(Eps), "below the gap nothing leaves");
            var r = Pour(d, h, capGap * 2f);
            Assert.That(r.Pool.Rim, Is.EqualTo(0.4f).Within(1e-3), "the gap is the rim");
            Assert.That(InTank(d, 8), Is.EqualTo(capGap).Within(capGap * 0.01f));
            Assert.That(d[Cell(0, 0)], Is.EqualTo(0.4f).Within(0.01f), "level at the gap bottom, not the wall top");
        }

        [Test]
        public void MixedRing_TheLowestSideIsTheRim()
        {
            var h = Tank(8, 1f, (dx, dy) =>
            {
                int ax = Math.Abs(dx), ay = Math.Abs(dy);
                if (Math.Max(ax, ay) <= 8 || Math.Max(ax, ay) > 11) return null;
                float sx = dx > 0 ? 1f : 0.85f, sy = dy > 0 ? 0.7f : 0.5f;
                return ax > 8 && ay > 8 ? Math.Min(sx, sy) : ax > 8 ? sx : sy;
            });
            var d = new float[N];
            var r = Pour(d, h, Capacity(h, 8, 1f));
            Assert.That(r.Pool.Rim, Is.EqualTo(0.5f).Within(1e-3));
            Assert.That(d[Cell(0, 0)], Is.EqualTo(0.5f).Within(0.01f));
        }

        // ---------------------------------------------------------------- obstacles are only height (W-4, Q16, step 12)

        [Test]
        public void RingOfPillarsWithFloorLevelGaps_IsNotAPool()
        {
            var h = Tank(8, 1f, (dx, dy) =>
            {
                int ax = Math.Abs(dx), ay = Math.Abs(dy), cheb = Math.Max(ax, ay);
                if (cheb <= 8 || cheb > 11) return null;
                int along = ax >= ay ? dy + 1000 : dx + 1000;
                return along % 3 == 2 ? 0f : 1f;
            });
            var d = new float[N];
            var r = Pour(d, h, 100f, rim: true);
            Assert.That(r.Pool.Enclosed, Is.False, r.Path);
            Assert.That(r.RimRemoved, Is.EqualTo(0f), "not enclosed: there is no rim to remove liquid at");
            Assert.That(InTank(d, 8), Is.LessThan(100f * 0.15f));
        }

        [Test]
        public void TightRingOfPillars_IsAPool()
        {
            var h = Tank(8, 1f);                            // pillars that touch = a closed ring of the same height
            var d = new float[N];
            var r = Pour(d, h, 50f);
            Assert.That(r.Pool.Enclosed, Is.True);
            Assert.That(r.Kept, Is.EqualTo(50f).Within(Eps));
        }

        [Test]
        public void LonePillarOnASlope_LiquidGoesAroundIt()
        {
            var h = new float[N];
            for (int y = 0; y <= W; y++) for (int x = 0; x <= W; x++) h[y * Stride + x] = -0.02f * (x - C);
            for (int dy = -1; dy <= 1; dy++) for (int dx = 4; dx <= 6; dx++) h[Cell(dx, dy)] = 1f;
            var d = new float[N];
            var r = Pour(d, h, 40f, rim: true);
            Assert.That(r.Pool.Enclosed, Is.False);
            Assert.That(r.RimRemoved, Is.EqualTo(0f));
            for (int dy = -1; dy <= 1; dy++) for (int dx = 4; dx <= 6; dx++) Assert.That(d[Cell(dx, dy)], Is.EqualTo(0f), "no liquid on the pillar");
            Assert.That(r.ToEdge + r.OverflowRemoved, Is.GreaterThan(30f), "it runs downhill past the pillar: " + r.Path);
        }

        [Test]
        public void ObstacleInsideAPool_TakesNoLiquidAndReducesTheCapacity()
        {
            Func<int, int, float?> shape = (dx, dy) => dx >= 3 && dx <= 5 && Math.Abs(dy) <= 1 ? (float?)1f : null;
            var h = Tank(8, 1f, shape); var d = new float[N];
            float cap = Capacity(h, 8, 1f);
            Assert.That(cap, Is.EqualTo(17 * 17 - 9).Within(1e-3));
            var r = Pour(d, h, cap * 1.5f, rim: true);
            Assert.That(InTank(d, 8), Is.EqualTo(cap).Within(cap * 0.01f));
            for (int dy = -1; dy <= 1; dy++) for (int dx = 3; dx <= 5; dx++) Assert.That(d[Cell(dx, dy)], Is.EqualTo(0f));
        }

        [Test]
        public void DiagonalWallCorner_DoesNotLeak()
        {
            // a one-cell staircase wall: 8-neighbour descent used to slip through its corners
            var h = new float[N];
            for (int y = 0; y <= W; y++)
                for (int x = 0; x <= W; x++)
                {
                    int s = (x - C) + (y - C);
                    h[y * Stride + x] = s == 0 ? 1f : s < 0 ? 0f : -0.3f;
                }
            var d = new float[N];
            var r = Pour(d, h, 20f, at: Cell(-3, -3));
            double outside = 0; for (int y = 0; y <= W; y++) for (int x = 0; x <= W; x++) if ((x - C) + (y - C) > 0) outside += d[y * Stride + x];
            Assert.That(outside, Is.EqualTo(0d).Within(1e-6), r.Path);
        }

        [Test]
        public void Accounting_BalancesOnRandomTerrain()
        {
            var rnd = new Random(42);
            for (int run = 0; run < 40; run++)
            {
                var h = new float[N];
                for (int i = 0; i < N; i++) h[i] = (float)(rnd.NextDouble() * 0.6 - 0.3);
                var d = new float[N];
                bool rim = run % 2 == 1;
                for (int k = 0; k < 5; k++)
                {
                    float before = Total(d), vol = (float)(rnd.NextDouble() * 60 + 1);
                    var r = Pour(d, h, vol, rim, Cell(rnd.Next(-15, 15), rnd.Next(-15, 15)));
                    Assert.That(Total(d) - before, Is.EqualTo(r.Kept).Within(Eps * vol), "run " + run + " stroke " + k);
                }
            }
        }

        // ---------------------------------------------------------------- the wall pass (W-5, steps 8 / 9 / 12)

        static float[] Flat(float depth, out float[] h)
        {
            h = new float[N]; var d = new float[N];
            for (int i = 0; i < N; i++) d[i] = depth;
            return d;
        }

        static Dictionary<int, float> Block(int half, float top)
        {
            var b = new Dictionary<int, float>();
            for (int dy = -half; dy <= half; dy++) for (int dx = -half; dx <= half; dx++) b[Cell(dx, dy)] = top;
            return b;
        }

        [Test]
        public void Wall_PieceAboveTheWater_DisplacesItsCellsLiquidWithoutLoss()
        {
            float[] h; var d = Flat(0.8f, out h); float before = Total(d);
            var r = HeightField.RaiseCells(h, d, W, Block(1, 1.5f), null, null);
            Assert.That(Total(d), Is.EqualTo(before).Within(Eps));
            Assert.That(r.Displaced, Is.EqualTo(9 * 0.8f).Within(Eps));
            Assert.That(d[Cell(0, 0)], Is.EqualTo(0f));
        }

        [Test]
        public void Wall_ThickPiece_InnerCellsGoToTheNearestOpenCell()
        {
            float[] h; var d = Flat(0.8f, out h); float before = Total(d);
            var r = HeightField.RaiseCells(h, d, W, Block(4, 1.5f), null, null);
            Assert.That(Total(d), Is.EqualTo(before).Within(Eps));
            Assert.That(r.NoOpenNeighbour, Is.GreaterThan(0));
            Assert.That(r.Far, Is.GreaterThan(0f));
            Assert.That(r.Unplaced, Is.EqualTo(0f));
        }

        [Test]
        public void Wall_SubmergedPiece_KeepsTheLiquidAboveIt()
        {
            float[] h; var d = Flat(0.8f, out h); float before = Total(d);
            var r = HeightField.RaiseCells(h, d, W, Block(4, 0.3f), null, null);
            Assert.That(Total(d), Is.EqualTo(before).Within(Eps));
            Assert.That(d[Cell(0, 0)], Is.EqualTo(0.5f).Within(1e-4), "surface stays at 0.8");
            Assert.That(r.Displaced, Is.EqualTo(81 * 0.3f).Within(Eps), "only the piece's own volume moves");
        }

        [Test]
        public void Wall_NoOpenCellAnywhere_LiquidStaysAndIsReported()
        {
            float[] h; var d = Flat(0.8f, out h); float before = Total(d);
            var all = new Dictionary<int, float>(); for (int i = 0; i < N; i++) all[i] = 2f;
            var r = HeightField.RaiseCells(h, d, W, all, null, null);
            Assert.That(Total(d), Is.EqualTo(before).Within(Eps), "never deleted (W-5)");
            Assert.That(r.Unplaced, Is.EqualTo(before).Within(before * 1e-4f), "float sum of 1681 cells");
        }

        [Test]
        public void Wall_RepeatedPassesOverTheSameGeometry_MoveNothingAgain()
        {
            float[] h; var d = Flat(0.8f, out h);
            var block = Block(4, 1.5f);
            var r1 = HeightField.RaiseCells(h, d, W, block, null, null);
            Assert.That(r1.Displaced, Is.GreaterThan(0f));
            var moved = new HashSet<int>(r1.Moved);
            for (int pass = 0; pass < 5; pass++)
            {
                // the game rewrites every height to the terrain once a second; the depths stay what the simulation stepped with
                foreach (var kv in block) h[kv.Key] = 0f;
                float before = Total(d);
                var r = HeightField.RaiseCells(h, d, W, block, block, moved);
                Assert.That(r.Displaced, Is.EqualTo(0f), "pass " + pass);
                Assert.That(r.RepeatCells, Is.EqualTo(0));
                Assert.That(Total(d), Is.EqualTo(before).Within(Eps));
                moved = new HashSet<int>(r.Moved);
            }
        }

        [Test]
        public void Wall_PieceThatGrows_DisplacesOnlyTheNewPart()
        {
            float[] h; var d = Flat(0.8f, out h);
            var low = Block(4, 0.3f);
            HeightField.RaiseCells(h, d, W, low, null, null);
            foreach (var kv in low) h[kv.Key] = 0f;
            var r = HeightField.RaiseCells(h, d, W, Block(4, 0.5f), low, null);
            Assert.That(r.Displaced, Is.EqualTo(81 * 0.2f).Within(0.05f));
        }
    }
}
