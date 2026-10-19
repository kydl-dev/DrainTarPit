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
    /// The synthetic tank tests (tank, matrix): a fresh pool whose heights are forced to a closed tank, so the geometry is known exactly (PoolTests).
    internal static class PoolTankTests
    {
        const float HoldAfterPour = 25f;            // tank test: watch time after the last stroke

        // --- synthetic tanks ---------------------------------------------------------------------------------------------
        internal sealed class Tank { public int R; public int Ring = 3; public float Floor = -1f; public float Top; }
        internal static readonly Dictionary<LiquidVolume, Tank> Synth = new Dictionary<LiquidVolume, Tank>();

        /// Called from the UpdateHeights postfix (TarPitHooks.Patch_UpdateHeights) with the pool's lock held: forces the heights of a test pool to the tank.
        internal static void ApplySynthetic(LiquidVolume v)
        {
            Tank t;
            if (Synth.Count == 0 || !Synth.TryGetValue(v, out t)) return;
            var h = TarPitDebug.HeightList(v);
            int w = v.m_width, stride = w + 1, n = stride * stride, c = w / 2;
            if (h == null || h.Count < n) return;
            using (TarPitDebug.Lock(v))
            {
                for (int i = 0; i < n; i++)
                {
                    int cheb = Mathf.Max(Mathf.Abs(i % stride - c), Mathf.Abs(i / stride - c));
                    h[i] = cheb <= t.R ? t.Floor : cheb <= t.R + t.Ring ? t.Top : t.Floor - 0.5f;
                }
                TarPitDebug.MarkChanged(v);
            }
        }

        // --- the synthetic tank ----------------------------------------------------------------------------------------------
        internal sealed class Variant { public string Name; public bool Inject; public bool? Shed, Overflow, Runoff; public float? Boost, Flow; }

        internal static IEnumerator Matrix()
        {
            var variants = new[]
            {
                new Variant { Name = "A-default-flood" },
                new Variant { Name = "B-default-inject", Inject = true },
                new Variant { Name = "C-noshed-flood", Shed = false },
                new Variant { Name = "D-noshed-noboost-flood", Shed = false, Boost = 1f },
                new Variant { Name = "E-noshed-noboost-slowflow-inject", Shed = false, Boost = 1f, Flow = 1f, Inject = true },
                new Variant { Name = "F-noshed-nooverflow-norunoff-flood", Shed = false, Overflow = false, Runoff = false },
            };
            Out("=== MATRIX of " + variants.Length + " tank tests (R 10, walls 6 m, 8 strokes). Settings are put back afterwards.");
            var results = new List<string>();
            var cfg = Plugin.Instance.Config;
            bool oldSave = cfg.SaveOnConfigSet;
            float oShed = ModSettings.PoolShedFilm.Value ? 1f : 0f, oBoost = ModSettings.PitFlowBoost.Value, oFlow = ModSettings.WaterFlow.Value;
            bool oOver = ModSettings.PoolOverflow.Value, oRun = ModSettings.PoolRunoff.Value;
            cfg.SaveOnConfigSet = false;
            try
            {
                foreach (var vr in variants)
                {
                    if (Cancel) break;
                    if (vr.Shed.HasValue) ModSettings.PoolShedFilm.Value = vr.Shed.Value;
                    if (vr.Boost.HasValue) ModSettings.PitFlowBoost.Value = vr.Boost.Value;
                    if (vr.Flow.HasValue) ModSettings.WaterFlow.Value = vr.Flow.Value;
                    if (vr.Overflow.HasValue) ModSettings.PoolOverflow.Value = vr.Overflow.Value;
                    if (vr.Runoff.HasValue) ModSettings.PoolRunoff.Value = vr.Runoff.Value;
                    var res = new Result();
                    var run = TankRun(vr.Name, 10, 6f, 8, vr.Inject, res);
                    while (run.MoveNext()) yield return run.Current;
                    results.Add(res.Summary);
                    ModSettings.PoolShedFilm.Value = oShed > 0.5f; ModSettings.PitFlowBoost.Value = oBoost; ModSettings.WaterFlow.Value = oFlow;
                    ModSettings.PoolOverflow.Value = oOver; ModSettings.PoolRunoff.Value = oRun;
                    yield return new WaitForSeconds(1f);
                }
            }
            finally
            {
                ModSettings.PoolShedFilm.Value = oShed > 0.5f; ModSettings.PitFlowBoost.Value = oBoost; ModSettings.WaterFlow.Value = oFlow;
                ModSettings.PoolOverflow.Value = oOver; ModSettings.PoolRunoff.Value = oRun;
                cfg.SaveOnConfigSet = oldSave;
            }
            Out("=== MATRIX RESULTS (kept = pool volume right after the last stroke / poured; final = after " + F2(HoldAfterPour) + " s more)");
            foreach (var s in results) Say(s);
        }

        internal sealed class Result { public string Summary = "(no result)"; }

        internal static IEnumerator TankTest(int radius, float wallH, int strokes, bool inject)
        {
            var res = new Result();
            var run = TankRun(inject ? "tank-inject" : "tank-flood", radius, wallH, strokes, inject, res);
            while (run.MoveNext()) yield return run.Current;
            Say(res.Summary);
        }

        internal static IEnumerator TankRun(string label, int radius, float wallH, int strokes, bool inject, Result res)
        {
            Vector3 land;
            if (!Crosshair(out land)) land = Player.m_localPlayer.transform.position + Player.m_localPlayer.transform.forward * 12f;
            radius = Mathf.Clamp(radius, 3, 30);
            var tank = new Tank { R = radius, Floor = -1f };
            tank.Top = tank.Floor + Mathf.Clamp(wallH, 1f, 10f);
            var v = TarPool.SpawnForTest(land);
            if (v == null) { res.Summary = label + ": FAILED - no pool prefab (see the log)"; yield break; }
            Synth[v] = tank;
            Out("=== TANK " + label + ": R " + radius + " cells (" + F2((2 * radius + 1) * v.m_scale) + " m square), walls " + F2(tank.Top - tank.Floor) + " m high, " + strokes + " strokes of " + F2(ModSettings.VolumePerStroke.Value) +
                " m3, mode " + (inject ? "inject (even spread, no FloodPass)" : "flood (the real FloodPass)") + ", pool at " + v.transform.position.ToString("0.0"));
            Out(EnvLine(land)); Out(ConfigLine());
            float t0 = Time.time;
            while (!TarPitDebug.Ready(v) && Time.time < t0 + 15f && !Cancel) yield return null;
            if (!TarPitDebug.Ready(v)) { res.Summary = label + ": FAILED - the pool never got its ground heights"; TarPool.DestroyForTest(v); Synth.Remove(v); yield break; }
            t0 = Time.time;
            Snap st = null;
            while (Time.time < t0 + 6f && !Cancel)
            {
                yield return new WaitForSeconds(0.5f);
                st = Take(v, tank);
                if (Mathf.Abs(st.CentreH - tank.Floor) < 0.01f && Mathf.Abs(st.RingH - tank.Top) < 0.01f) break;
            }
            Out("tank heights in the pool: centre " + F(st != null ? st.CentreH : float.NaN) + " (want " + F(tank.Floor) + "), wall " + F(st != null ? st.RingH : float.NaN) + " (want " + F(tank.Top) + ")");

            var o = v.transform.position;
            var centre = new Vector3(o.x, o.y + tank.Floor, o.z);
            int cellsIn = (2 * radius + 1) * (2 * radius + 1);
            float c2 = v.m_scale * v.m_scale, areaIn = cellsIn * c2, vol = ModSettings.VolumePerStroke.Value;
            var r = new Obs { Label = "T", V = v, Tank = tank, T0 = Time.time };
            Sample(r);
            float next = Time.time; int done = 0;
            while (done < strokes && !Cancel)
            {
                if (Time.time >= next)
                {
                    if (inject) Inject(v, tank, vol); else TarPitDeposit.DepositLocal(v, centre, vol, 0);
                    r.Poured += vol; done++; next = Time.time + DefaultPourInterval;
                    r.Expected = o.y + tank.Floor + r.Poured / areaIn;
                }
                Sample(r);
                yield return new WaitForSeconds(SampleSeconds);
            }
            var endPour = Sample(r);
            float kept = endPour.Total / Mathf.Max(0.01f, r.Poured), levelRise = endPour.Level - (o.y + tank.Floor);
            float tEnd = Time.time;
            while (Time.time < tEnd + HoldAfterPour && !Cancel) { Sample(r); yield return new WaitForSeconds(SampleSeconds); }
            var fin = r.Last;
            var b = TarPitDebug.BudgetFor(v);
            res.Summary = label + ": poured " + F2(r.Poured) + " m3 -> after the last stroke " + F2(endPour.Total) + " in the pool (" + F2(kept * 100f) + " %), final " + F2(fin.Total) + " (" + F2(fin.Total / Mathf.Max(0.01f, r.Poured) * 100f) + " %); " +
                          "level rise " + F2(levelRise) + " m (expected " + F2(r.Poured / areaIn) + ", " + F2(levelRise / Mathf.Max(0.001f, r.Poured / areaIn) * 100f) + " %), final level rise " + F2(fin.Level - (o.y + tank.Floor)) + ", max level rise " + F2(r.MaxLevel - (o.y + tank.Floor)) +
                          "; final: in tank " + F2(fin.TankIn) + ", on wall cells " + F2(fin.Ring) + ", elsewhere " + F2(fin.Total - fin.TankIn - fin.Ring) + ", grid edge " + F2(fin.Border) + "; reloads " + r.Reloads + ", saves " + r.Saves + ", drops " + r.Drops +
                          "; mod budget: stayed " + F2(b.Stayed) + " edge " + F2(b.Edge) + " thin " + F2(b.Thin) + " lost " + F2(b.Lost) + " shedEdge " + F2(b.ShedEdge) + " evap " + F2(b.Evap) + " wallPushed " + F2(b.WallPushed) + " wallDestroyed " + F2(b.WallLost) + " wallPasses " + b.WallPasses;
            Out("TANK RESULT " + res.Summary);
            TarPool.DestroyForTest(v); Synth.Remove(v);
        }

        // water spread evenly over the tank cells: bypasses FloodPass entirely
        internal static void Inject(LiquidVolume v, Tank t, float volume)
        {
            int w = v.m_width, stride = w + 1, c = w / 2, cells = (2 * t.R + 1) * (2 * t.R + 1);
            float per = volume / (cells * v.m_scale * v.m_scale);
            var d = TarPitDebug.DepthList(v);
            using (TarPitDebug.Lock(v))
            {
                for (int y = c - t.R; y <= c + t.R; y++)
                    for (int x = c - t.R; x <= c + t.R; x++) d[y * stride + x] += per;
                TarPitDebug.MarkChanged(v);
            }
        }
    }
}
