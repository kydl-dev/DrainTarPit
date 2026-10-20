using System.Collections;
using System.Collections.Generic;
using DrainTarPit.Drain;
using UnityEngine;
using static DrainTarPit.Debug.PoolTestOut;
using static DrainTarPit.Debug.PoolSnapshot;
using static DrainTarPit.Debug.PoolObserver;
using static DrainTarPit.Debug.PoolTests;
using static DrainTarPit.Debug.PoolTankTests;

namespace DrainTarPit.Debug
{
    /// <summary>
    /// Synthetic tests of the liquid rules (steps 8, 9, 13, 14, 15 of _agent/DrainTarPit_Water_and_Tar_First_15_Steps.md). Each test spawns a fresh pump
    /// pool, forces known heights (PoolShapes), pours through the real deposit path and checks the result against numbers known by construction:
    ///   shape <name> [water|tar] [rim0|rim1] [strokes]   one shape (PoolShapes.All) with [Drain] PoolRimDisappear off / on.
    ///   walls <open|thick|submerged|full|real>            the wall pass against synthetic pieces put into standing water (real = real colliders).
    ///   suite                                             all of them in a fixed order, then a PASS / FAIL table.
    /// PoolRimDisappear and PoolRunoff are set for the test and put back afterwards (runoff off: the tests must not spawn pools beyond the grid);
    /// the walls tests also turn PoolShedFilm off (only the wall pass may change the total).
    /// </summary>
    internal static class PoolShapeTests
    {
        const float ShapeHold = 15f, WallsWatch = 6f;

        internal static IEnumerator ShapeTest(string shape, PumpLiquid liquid, bool rim, int strokes)
        {
            var res = new Result();
            var run = ShapeRun(shape, liquid, rim, strokes, res);
            while (run.MoveNext()) yield return run.Current;
            Say(res.Summary);
        }

        internal static IEnumerator WallsTest(string kind)
        {
            var res = new Result();
            var run = WallsRun(kind, res);
            while (run.MoveNext()) yield return run.Current;
            Say(res.Summary);
        }

        internal static IEnumerator Suite()
        {
            var runs = new List<string>();
            Out("=== SUITE (step 15): shapes for water with the rim switch off and on, tar parity, wall-correction controls. About 9 minutes; dtp ptest stop cancels.");
            foreach (bool rim in new[] { false, true })
                foreach (var s in PoolShapes.All) runs.Add("shape " + s + " water " + (rim ? "rim1" : "rim0"));
            foreach (var s in new[] { "closed", "gap", "inside" }) runs.Add("shape " + s + " tar rim0");
            runs.Add("shape closed tar rim1");
            foreach (var k in new[] { "open", "thick", "submerged", "full" }) runs.Add("walls " + k);
            var results = new List<string>();
            int pass = 0, fail = 0;
            foreach (var spec in runs)
            {
                if (Cancel) break;
                var a = spec.Split(' ');
                var res = new Result();
                var run = a[0] == "walls" ? WallsRun(a[1], res) : ShapeRun(a[1], a[2] == "tar" ? PumpLiquid.Tar : PumpLiquid.Water, a[3] == "rim1", 6, res);
                while (run.MoveNext()) yield return run.Current;
                yield return new WaitForFixedUpdate();
                Out("after " + spec + ": " + TarPitTracking.PlayerLiquidLine());
                results.Add(res.Summary);
                if (res.Summary.Contains(": PASS")) pass++; else fail++;
                yield return new WaitForSeconds(1f);
            }
            Out("=== SUITE RESULTS: " + pass + " PASS, " + fail + " FAIL/INCOMPLETE of " + runs.Count + (Cancel ? " (cancelled)" : ""));
            foreach (var s in results) Say(s);
        }

        // --- one shape ---------------------------------------------------------------------------------------------------------------
        internal static IEnumerator ShapeRun(string shape, PumpLiquid liquid, bool rim, int strokes, Result res)
        {
            string label = "shape " + shape + " " + liquid.ToString().ToLowerInvariant() + " rim" + (rim ? "1" : "0");
            if (!PoolShapes.Known(shape)) { res.Summary = label + ": FAILED - unknown shape (" + string.Join(", ", PoolShapes.All) + ")"; yield break; }
            var tank = new Tank { R = 8, Floor = -1f, Top = 0f, Shape = shape };
            var cfg = Plugin.Instance.Config;
            bool oldSave = cfg.SaveOnConfigSet, oRim = ModSettings.PoolRimDisappear.Value, oRun = ModSettings.PoolRunoff.Value;
            cfg.SaveOnConfigSet = false;
            ModSettings.PoolRimDisappear.Value = rim; ModSettings.PoolRunoff.Value = false;
            LiquidVolume v = null;
            try
            {
                var spawn = SpawnSynthetic(tank, liquid, label, res);
                while (spawn.MoveNext()) yield return spawn.Current;
                v = _spawned;
                if (v == null) yield break;
                var o = v.transform.position;
                float c2 = v.m_scale * v.m_scale, vol = ModSettings.VolumePerStroke.Value, cap = PoolShapes.Capacity(tank, c2), rimY = PoolShapes.ExpectedRim(tank);
                Out("=== " + label.ToUpperInvariant() + ": R " + tank.R + ", H " + F2(tank.Top - tank.Floor) + " m, " + strokes + " strokes of " + F2(vol) + " m3, capacity by construction " + F2(cap) +
                    " m3 up to y " + (float.IsNaN(rimY) ? "n/a (no basin)" : F2(o.y + rimY)) + "; PoolRimDisappear=" + rim + " PoolRunoff=False (test); " + TarPoolDiagnostics.DescribeVolume(v));
                Out(EnvLine(o)); Out(ConfigLine());
                var centre = new Vector3(o.x, o.y + tank.Floor, o.z);
                var r = new Obs { Label = "S", V = v, Tank = tank, T0 = Time.time };
                var b0 = Copy(TarPitDebug.BudgetFor(v));
                Sample(r);
                float next = Time.time; int done = 0;
                while (done < strokes && !Cancel)
                {
                    if (Time.time >= next) { TarPitDeposit.DepositLocal(v, centre, vol, 0); r.Poured += vol; done++; next = Time.time + DefaultPourInterval; }
                    Sample(r);
                    yield return new WaitForSeconds(SampleSeconds);
                }
                var endPour = Sample(r);
                float tEnd = Time.time;
                while (Time.time < tEnd + ShapeHold && !Cancel) { Sample(r); yield return new WaitForSeconds(SampleSeconds); }
                var fin = r.Last;
                if (fin == null || !fin.Valid || !endPour.Valid) { res.Summary = label + ": FAILED - invalid snapshot; " + TarPoolDiagnostics.DescribeVolume(v); Out("SHAPE RESULT " + res.Summary); yield break; }
                var b = Diff(TarPitDebug.BudgetFor(v), b0);
                float onObstacle, level; TankNumbers(v, tank, out onObstacle, out level);
                foreach (var l in TarPitLedger.LinesSince(v, r.T0)) Out("LEDGER " + l);
                var book = TarPitLedger.Of(v);
                float P = r.Poured, inTank = fin.TankIn, over = Mathf.Max(0f, P - cap);
                var why = new List<string>();
                bool basin = !float.IsNaN(rimY);
                if (book.Unbalanced > 0) why.Add(book.Unbalanced + " stroke(s) did not balance");
                if (basin)
                {
                    float want = Mathf.Min(P, cap);
                    if (Mathf.Abs(inTank - want) > Mathf.Max(0.5f, 0.05f * cap)) why.Add("in tank " + F2(inTank) + " m3, want " + F2(want));
                    if (P > cap && Mathf.Abs(level - (o.y + rimY)) > 0.05f) why.Add("level " + F2(level) + ", want the rim " + F2(o.y + rimY));
                    if (P > cap && rim && b.RimRemoved < 0.8f * over) why.Add("rim switch on: only " + F2(b.RimRemoved) + " m3 removed at the rim, want about " + F2(over));
                    if (P > cap && rim && b.Thin > 0.1f * over) why.Add("rim switch on: " + F2(b.Thin) + " m3 still ran over the rim");
                    if (!rim && b.RimRemoved > 0.01f) why.Add("rim switch off: " + F2(b.RimRemoved) + " m3 removed at the rim");
                }
                else
                {
                    if (inTank > 0.15f * P) why.Add("no basin, yet " + F2(inTank) + " m3 stayed around the landing point");
                    if (b.RimRemoved > 0.01f) why.Add("not enclosed, yet " + F2(b.RimRemoved) + " m3 removed at a 'rim'");
                }
                if (onObstacle > 0.01f) why.Add(F2(onObstacle) + " m3 stands on the obstacle (it should flow around it)");
                res.Summary = label + ": " + (why.Count == 0 ? "PASS" : "FAIL (" + string.Join("; ", why.ToArray()) + ")") + " - poured " + F2(P) + ", capacity " + F2(cap) + ", in tank " + F2(inTank) +
                    " (after the pour " + F2(endPour.TankIn) + "), on the ring " + F2(fin.Ring) + ", elsewhere in the grid " + F2(fin.Total - fin.TankIn - fin.Ring) + ", level " + F2(level) +
                    (basin ? " (rim " + F2(o.y + rimY) + ")" : "") + ", on the obstacle " + F2(onObstacle) + "; budget: stayed " + F2(b.Stayed) + " (inner " + F2(b.Inner) + ", outside rim " + F2(b.OutsideRim) +
                    "), rimRemoved " + F2(b.RimRemoved) + ", overflow thin " + F2(b.Thin) + ", edge " + F2(b.Edge) + ", unplaced " + F2(b.Lost) + ", shedEdge " + F2(b.ShedEdge) + ", evap " + F2(b.Evap) + "; " + TarPitLedger.Summary(v);
                Out("SHAPE RESULT " + res.Summary);
            }
            finally
            {
                ModSettings.PoolRimDisappear.Value = oRim; ModSettings.PoolRunoff.Value = oRun; cfg.SaveOnConfigSet = oldSave;
                if (v != null) { TarPool.DestroyForTest(v); Synth.Remove(v); }
            }
        }

        // --- wall correction (steps 8 / 9) ---------------------------------------------------------------------------------------------
        internal static IEnumerator WallsRun(string kind, Result res)
        {
            string label = "walls " + kind;
            var tank = new Tank { R = 10, Floor = -1f, Top = 1f, Shape = "closed", RealWalls = kind == "real" };
            if (kind != "open" && kind != "thick" && kind != "submerged" && kind != "full" && kind != "real") { res.Summary = label + ": FAILED - use open | thick | submerged | full | real"; yield break; }
            var cfg = Plugin.Instance.Config;                                 // shedding / runoff off: the wall pass alone must keep the total (T-16 'walls full' lost
            bool oldSave = cfg.SaveOnConfigSet, oShed = ModSettings.PoolShedFilm.Value, oRun = ModSettings.PoolRunoff.Value;   // 0.30 m3 to border films)
            cfg.SaveOnConfigSet = false;
            ModSettings.PoolShedFilm.Value = false; ModSettings.PoolRunoff.Value = false;
            LiquidVolume v = null;
            try
            {
                var spawn = SpawnSynthetic(tank, PumpLiquid.Water, label, res);
                while (spawn.MoveNext()) yield return spawn.Current;
                v = _spawned;
                if (v == null) yield break;
                int w = v.m_width, stride = w + 1, n = stride * stride, c = w / 2;
                Out("=== " + label.ToUpperInvariant() + ": closed tank R " + tank.R + ", walls 2 m, 4 strokes injected evenly (about 0.87 m deep), then a synthetic piece is put into the water; " + TarPoolDiagnostics.DescribeVolume(v));
                Out(EnvLine(v.transform.position)); Out(ConfigLine());
                TarPitLedger.Of(v).LastDeposit = Time.time;                   // book the wall passes of this pool
                for (int k = 0; k < 4; k++) Inject(v, tank, ModSettings.VolumePerStroke.Value);
                yield return new WaitForSeconds(3f);
                float before = TarPits.TotalVolume(v);
                var b0 = Copy(TarPitDebug.BudgetFor(v));
                double wall0 = TarPitLedger.StageDelta(v, "wall");
                var block = new Dictionary<int, float>();
                int half = kind == "open" ? 1 : 4;
                float top = kind == "submerged" ? tank.Floor + 0.3f : kind == "full" ? tank.Top + 1f : tank.Floor + 1.5f;
                if (kind == "full") for (int i = 0; i < n; i++) block[i] = top;
                else if (kind != "real")
                    for (int y = c - half; y <= c + half; y++) for (int x = c - half; x <= c + half; x++) block[y * stride + x] = top;
                tank.Block = block;
                Out("piece: " + (kind == "real" ? "none (real colliders around the tank are walls)" : block.Count + " cells, top " + F2(v.transform.position.y + top)) + "; total before " + F(before) + " m3");
                float t0 = Time.time;
                while (TarPitDebug.BudgetFor(v).WallPasses == b0.WallPasses && Time.time < t0 + 4f && !Cancel) yield return null;
                float firstPushed = TarPitDebug.BudgetFor(v).WallPushed - b0.WallPushed;
                yield return new WaitForSeconds(WallsWatch);
                float after = TarPits.TotalVolume(v);
                var b = Diff(TarPitDebug.BudgetFor(v), b0);
                float later = b.WallPushed - firstPushed, onBlock = 0f;
                using (TarPitDebug.Lock(v)) { var d = TarPitDebug.DepthList(v); foreach (var kv in block) if (kv.Key < n) onBlock += d[kv.Key]; }
                onBlock *= v.m_scale * v.m_scale;
                foreach (var l in TarPitLedger.LinesSince(v, t0)) Out("LEDGER " + l);
                float wallDelta = (float)(TarPitLedger.StageDelta(v, "wall") - wall0);
                var why = new List<string>();
                if (Mathf.Abs(wallDelta) > 0.02f) why.Add("the wall pass changed the total by " + F(wallDelta) + " m3");
                // full: the whole grid is a piece and a 4 cm film lies on it - no basin anywhere. The game's own simulation of that film drifts after the pass
                // (T-17R: -0.21 m3 in 6 s, the only DRIFT in a log with a 60 s real-pool hold and 20 enclosed runs), so only the wall pass is judged (user, D-026).
                if (kind != "full" && Mathf.Abs(after - before) > 0.02f) why.Add("total changed " + F(after - before) + " m3");
                if (later > 0.02f) why.Add(F2(later) + " m3 moved again after the first pass (should settle)");
                if (b.WallRepeat > 0.02f) why.Add(F2(b.WallRepeat) + " m3 moved again from the same cells");
                if (kind == "full" && b.WallLost <= 0f) why.Add("no open cell anywhere, yet nothing was reported as staying");
                if (kind == "submerged" && onBlock <= 0.01f) why.Add("the water above the submerged piece was pushed away");
                if ((kind == "open" || kind == "thick") && firstPushed <= 0.01f) why.Add("the piece displaced nothing");
                res.Summary = label + ": " + (why.Count == 0 ? "PASS" : "FAIL (" + string.Join("; ", why.ToArray()) + ")") + " - total " + F(before) + " -> " + F(after) + " m3 (wall pass " + F(wallDelta) + (kind == "full" ? "; the rest is the game's drift of a film with no basin, info only" : "") + "), displaced at the first pass " + F2(firstPushed) +
                    ", later " + F2(later) + ", repeat " + F2(b.WallRepeat) + ", no open cell (stayed) " + F2(b.WallLost) + ", standing on the piece " + F2(onBlock) + ", wall passes " + b.WallPasses;
                Out("WALLS RESULT " + res.Summary);
            }
            finally
            {
                ModSettings.PoolShedFilm.Value = oShed; ModSettings.PoolRunoff.Value = oRun; cfg.SaveOnConfigSet = oldSave;
                if (v != null) { TarPool.DestroyForTest(v); Synth.Remove(v); }
            }
        }

        // --- helpers -----------------------------------------------------------------------------------------------------------------------
        static LiquidVolume _spawned;

        /// Spawns a pump pool of `liquid` at the crosshair (or 12 m ahead), makes it synthetic and waits until every height is the shape's.
        static IEnumerator SpawnSynthetic(Tank tank, PumpLiquid liquid, string label, Result res)
        {
            _spawned = null;
            Vector3 land;
            if (!Crosshair(out land)) land = Player.m_localPlayer.transform.position + Player.m_localPlayer.transform.forward * 12f;
            var v = TarPool.SpawnForTest(land, liquid);
            if (v == null) { res.Summary = label + ": FAILED - no pool prefab for " + liquid + " (see the log)"; yield break; }
            Synth[v] = tank;
            float t0 = Time.time;
            while (!TarPitDebug.Ready(v) && Time.time < t0 + 15f && !Cancel) yield return null;
            int w = v.m_width, stride = w + 1, n = stride * stride, c = w / 2;
            bool ok = false;
            t0 = Time.time;
            while (!ok && Time.time < t0 + 6f && !Cancel)
            {
                yield return new WaitForSeconds(0.5f);
                if (!TarPitDebug.Ready(v)) continue;
                ok = true;
                using (TarPitDebug.Lock(v))
                {
                    var h = TarPitDebug.HeightList(v);
                    if (h == null || h.Count < n) { ok = false; continue; }
                    for (int i = 0; i < n && ok; i++) if (Mathf.Abs(h[i] - PoolShapes.Height(tank, i % stride - c, i / stride - c)) > 0.01f) ok = false;
                }
            }
            if (!ok) { res.Summary = label + ": FAILED - the synthetic heights were not applied; " + TarPoolDiagnostics.DescribeVolume(v); Out("RESULT " + res.Summary); TarPool.DestroyForTest(v); Synth.Remove(v); yield break; }
            _spawned = v;
        }

        static void TankNumbers(LiquidVolume v, Tank t, out float onObstacle, out float level)
        {
            onObstacle = 0f; level = float.NaN;
            int w = v.m_width, stride = w + 1, c = w / 2;
            double sum = 0d; int wet = 0;
            using (TarPitDebug.Lock(v))
            {
                var d = TarPitDebug.DepthList(v); var h = TarPitDebug.HeightList(v);
                for (int dy = -t.R; dy <= t.R; dy++)
                    for (int dx = -t.R; dx <= t.R; dx++)
                    {
                        int i = (c + dy) * stride + c + dx;
                        if (PoolShapes.IsObstacle(t, dx, dy)) { onObstacle += d[i]; continue; }
                        if (d[i] > 0.02f) { sum += h[i] + d[i]; wet++; }
                    }
            }
            onObstacle *= v.m_scale * v.m_scale;
            if (wet > 0) level = v.transform.position.y + (float)(sum / wet);
        }

        static TarPitStats.Budget Copy(TarPitStats.Budget b) { return b.Clone(); }

        static TarPitStats.Budget Diff(TarPitStats.Budget a, TarPitStats.Budget b0)
        {
            return new TarPitStats.Budget
            {
                Poured = a.Poured - b0.Poured, Stayed = a.Stayed - b0.Stayed, Inner = a.Inner - b0.Inner, OutsideRim = a.OutsideRim - b0.OutsideRim, Edge = a.Edge - b0.Edge,
                Thin = a.Thin - b0.Thin, RimRemoved = a.RimRemoved - b0.RimRemoved, Lost = a.Lost - b0.Lost, ShedEdge = a.ShedEdge - b0.ShedEdge, Evap = a.Evap - b0.Evap,
                WallPushed = a.WallPushed - b0.WallPushed, WallLost = a.WallLost - b0.WallLost, WallKept = a.WallKept, WallRepeat = a.WallRepeat - b0.WallRepeat, WallPasses = a.WallPasses - b0.WallPasses
            };
        }
    }
}
