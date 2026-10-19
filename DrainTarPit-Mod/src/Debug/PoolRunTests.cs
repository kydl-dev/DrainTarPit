using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using DrainTarPit.Drain;
using DrainTarPit.Logging;
using UnityEngine;
using static DrainTarPit.Debug.PoolTestOut;
using static DrainTarPit.Debug.PoolSnapshot;
using static DrainTarPit.Debug.PoolObserver;
using static DrainTarPit.Debug.PoolTests;

namespace DrainTarPit.Debug
{
    /// The tests that run on a real pool: watch it, pour into it (PoolTests).
    internal static class PoolRunTests
    {
        const float PoolHoldAfterPour = 60f;        // pour test in a real pool

        // --- watch ------------------------------------------------------------------------------------------------------
        internal static IEnumerator Watch(float seconds)
        {
            var p = Player.m_localPlayer.transform.position;
            var v = NearestPool(p);
            if (v == null) { Say("ptest watch: no water pool is loaded."); yield break; }
            Out("=== WATCH " + F2(seconds) + " s of the selected water pool (" + F2(Vector3.Distance(v.transform.position, p)) + " m from player): " + TarPoolDiagnostics.DescribeVolume(v));
            Out(EnvLine(p)); Out(ConfigLine());
            var r = new Obs { Label = "W", V = v, T0 = Time.time };
            float end = Time.time + seconds;
            Snap first = null;
            while (Time.time < end && !Cancel && v != null)
            {
                var s = Sample(r);
                if (first == null && s.Valid) first = s;
                yield return new WaitForSeconds(SampleSeconds);
            }
            if (v == null) yield break;
            var last = r.Last;
            if (first == null || last == null || !last.Valid)
                Say("WATCH INVALID: no valid baseline or the final snapshot has an invalid grid; first=" + (first != null ? "valid" : "no valid sample") + ", last=" + (last != null ? last.InvalidReason : "missing") + "; invalid samples=" + r.InvalidSnapshots + ". See " + Path.Combine(OutDir(), ReportFile));
            else
                Say("WATCH done: pool " + TarPoolDiagnostics.DescribeVolume(v) + "; total " + F2(first.Total) + " -> " + F2(last.Total) + " m3, level " + F(first.Level) + " -> " + F(last.Level) + ", " + r.Reloads + " reload(s) of the game revision, " +
                    r.Saves + " save(s), " + r.Drops + " drop(s) without a pour, " + r.InvalidSnapshots + " invalid snapshot(s). Details: " + Path.Combine(OutDir(), ReportFile));
        }

        // --- pour into a real pool --------------------------------------------------------------------------------------
        internal static IEnumerator Pour(int strokes, float interval)
        {
            Vector3 land;
            if (!Crosshair(out land)) { Say("ptest pour: the crosshair points at nothing within 80 m."); yield break; }
            var p = Player.m_localPlayer.transform.position;
            float vol = ModSettings.VolumePerStroke.Value;
            Out("=== POUR " + strokes + " strokes of " + F2(vol) + " m3 every " + F2(interval) + " s at the crosshair " + land.ToString("0.00") + " (the real deposit path), then " + F2(PoolHoldAfterPour) + " s of watching");
            Out(EnvLine(land)); Out(ConfigLine());
            // Record all overlapping pools, then observe the exact instance returned by DebugPour. Do not re-select a nearby pool after the pour.
            Out("pools whose grid covers the landing point / target-selection decisions (before): " + PoolsAt(land));
            var expectedTarget = TarPool.FindVolume(land, PumpLiquid.Water);
            float before = expectedTarget == null ? 0f : TarPoolDiagnostics.GridIsValid(expectedTarget) ? TarPits.TotalVolume(expectedTarget) : float.NaN;
            LiquidVolume v;
            string first = TarPool.DebugPour(land, vol, PumpLiquid.Water, out v);
            Out("first stroke result: " + first);
            Out("first stroke receiver: " + TarPoolDiagnostics.DescribeVolume(v));
            bool initialTargetMismatch = expectedTarget != null && expectedTarget != v;
            if (initialTargetMismatch)
            {
                before = float.NaN;
                Out("ERROR: pre-selection was " + TarPoolDiagnostics.DescribeVolume(expectedTarget) + "; actual first-stroke receiver was " + TarPoolDiagnostics.DescribeVolume(v) + "; baseline invalidated");
            }
            yield return null;
            if (v == null) { Say("ptest pour: no valid receiving pool after the first stroke; result=" + first); yield break; }
            var r = new Obs { Label = "P", V = v, T0 = Time.time, Poured = vol, HasLand = true, Land = land };
            float ledgerFrom = Time.time - 5f;
            Out("observer pinned to receiver: " + TarPoolDiagnostics.DescribeVolume(v) + "; volume before=" + (float.IsNaN(before) ? "INVALID (grid not ready)" : F2(before)) + " m3");
            float next = Time.time + interval; int done = 1; float requestedTotal = vol; bool targetChanged = initialTargetMismatch;
            float levelStart = float.NaN;
            while (done < strokes && !Cancel)
            {
                var s = Sample(r);
                if (s.Valid && float.IsNaN(levelStart) && s.Wet > 0) levelStart = s.Level;
                if (Time.time >= next)
                {
                    LiquidVolume strokeTarget;
                    string strokeResult = TarPool.DebugPour(land, vol, PumpLiquid.Water, out strokeTarget);
                    requestedTotal += vol;
                    Out("stroke " + (done + 1) + " result: " + strokeResult + "; receiver=" + TarPoolDiagnostics.DescribeVolume(strokeTarget));
                    if (strokeTarget != v)
                    {
                        targetChanged = true;
                        Out("ERROR: pour receiver changed from observed " + TarPoolDiagnostics.DescribeVolume(v) + " to " + TarPoolDiagnostics.DescribeVolume(strokeTarget) + "; stopping additional strokes so volumes cannot be misattributed");
                        break;
                    }
                    r.Poured += vol; done++; next = Time.time + interval;
                }
                yield return new WaitForSeconds(SampleSeconds);
            }
            var endPour = Sample(r);
            float tPourEnd = Time.time;
            while (Time.time < tPourEnd + PoolHoldAfterPour && !Cancel) { Sample(r); yield return new WaitForSeconds(SampleSeconds); }
            var fin = r.Last;
            float area = endPour.Valid ? endPour.Region * v.m_scale * v.m_scale : 0f;
            string endVolume = endPour.Valid ? F2(endPour.Total) : "INVALID(" + endPour.InvalidReason + ")";
            string finalVolume = fin != null && fin.Valid ? F2(fin.Total) : "INVALID(" + (fin != null ? fin.InvalidReason : "missing snapshot") + ")";
            string beforeText = float.IsNaN(before) ? "INVALID" : F2(before);
            string kept = endPour.Valid && !float.IsNaN(before) ? F2((endPour.Total - before)) + " m3 / " + F2((endPour.Total - before) / Mathf.Max(0.01f, r.Poured) * 100f) + " %" : "not calculable";
            Say("POUR " + (targetChanged ? "INVALID (receiver changed)" : "done") + ": requested overall " + F2(requestedTotal) + " m3; " + F2(r.Poured) + " m3 requested in " + done + " stroke(s) whose receiver matched the observed pool; receiver " + TarPoolDiagnostics.DescribeVolume(v) +
                "; pool total " + beforeText + " -> " + endVolume + " right after the last observed stroke (retained " + kept + "), " + finalVolume + " after " + F2(PoolHoldAfterPour) + " s more; level " +
                (float.IsNaN(levelStart) ? "n/a" : F(levelStart)) + " -> " + (endPour.Valid ? F(endPour.Level) : "n/a") + " -> " + (fin != null && fin.Valid ? F(fin.Level) : "n/a") +
                " (max " + (r.MaxLevel == float.MinValue ? "n/a" : F(r.MaxLevel)) + "), basin " + (endPour.Valid ? endPour.Region.ToString(Inv) : "n/a") + " cells = " + (endPour.Valid ? F2(area) : "n/a") + " m2, spill level " + (endPour.Valid ? F(endPour.Spill4) : "n/a") +
                "; " + r.Reloads + " reload(s), " + r.Saves + " save(s), " + r.Drops + " drop(s), " + r.InvalidSnapshots + " invalid snapshot(s)");
            Out("pools whose grid covers the landing point / target-selection decisions (after " + F2(PoolHoldAfterPour) + " s): " + PoolsAt(land));
            if (v == null) { Out("mod budget unavailable: observed pool was destroyed during the test"); yield break; }
            var bud = TarPitStats.BudgetOf(v);
            Out("mod budget of the measured pool: poured " + F2(bud.Poured) + " stayed " + F2(bud.Stayed) + " edge " + F2(bud.Edge) + " thin " + F2(bud.Thin) + " lost " + F2(bud.Lost) + " shedEdge " + F2(bud.ShedEdge) +
                " wallPushed " + F2(bud.WallPushed) + " wallStayed(no room) " + F2(bud.WallLost) + " wallKeptOnPieces(last pass) " + F2(bud.WallKept) + " wallRepeat " + F2(bud.WallRepeat) + " wallPasses " + bud.WallPasses +
                " innerDips " + F2(bud.Inner) + " outsideRim " + F2(bud.OutsideRim) + " rimRemoved " + F2(bud.RimRemoved));
            // steps 5 / 6 / 10: every checkpoint (deposit, wall pass, film shed), stroke accounting line and volume-series sample of the measured pool
            foreach (var l in TarPitLedger.LinesSince(v, ledgerFrom)) Out("LEDGER " + l);
            Out("LEDGER SUMMARY " + TarPitLedger.Summary(v));
        }

        /// Reports each loaded water-volume candidate, whether its grid actually covers the landing point, whether it passes the 1.5 m selection margin,
        /// whether it is a flagged pump pool, and why it was selected or rejected. This makes stale unflagged pools visible in the raw test report.
        static string PoolsAt(Vector3 p)
        {
            var target = TarPool.FindVolume(p, PumpLiquid.Water);
            foreach (var line in TarPoolDiagnostics.DescribeWaterSelection(p, target)) Out(line);
            var sb = new StringBuilder();
            int n = 0;
            foreach (var pv in TarPits.All)
            {
                if (pv == null || TarPits.LiquidOf(pv) != PumpLiquid.Water || !TarPoolDiagnostics.GridCoversPoint(pv, p)) continue;
                n++;
                sb.Append("[").Append(TarPoolDiagnostics.DescribeVolume(pv)).Append(" vol=").Append(TarPoolDiagnostics.GridIsValid(pv) ? F2(TarPits.TotalVolume(pv)) : "INVALID")
                  .Append(" walls=").Append(TarPitWalls.WallCellCount(pv)).Append("] ");
            }
            return "actual-grid overlaps=" + n + "; overlapping-pool details=" + sb + "; deposit target = " + DescribeTarget(p);
        }

        static string DescribeTarget(Vector3 p)
        {
            var t = TarPool.FindVolume(p, PumpLiquid.Water);
            return t == null ? "none (a new pool would be spawned)" : TarPoolDiagnostics.DescribeVolume(t);
        }
    }
}
