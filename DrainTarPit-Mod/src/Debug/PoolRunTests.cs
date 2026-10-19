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
            Out("=== WATCH " + F2(seconds) + " s of the nearest water pool " + v.transform.position.ToString("0.0") + " (" + F2(Vector3.Distance(v.transform.position, p)) + " m away)");
            Out(EnvLine(p)); Out(ConfigLine());
            var r = new Obs { Label = "W", V = v, T0 = Time.time };
            float end = Time.time + seconds;
            Snap first = null;
            while (Time.time < end && !Cancel && v != null)
            {
                var s = Sample(r);
                if (first == null) first = s;
                yield return new WaitForSeconds(SampleSeconds);
            }
            if (v == null) yield break;
            var last = r.Last;
            Say("WATCH done: total " + F2(first.Total) + " -> " + F2(last.Total) + " m3, level " + F(first.Level) + " -> " + F(last.Level) + ", " + r.Reloads + " reload(s) of the game revision, " +
                r.Saves + " save(s), " + r.Drops + " drop(s) without a pour. Details: " + Path.Combine(OutDir(), ReportFile));
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
            var v = TarPitStats.Nearest(land, LiquidType.Water);
            float before = v != null ? TarPits.TotalVolume(v) : 0f;
            string first = TarPool.DebugPour(land, vol, PumpLiquid.Water);
            Out("first stroke: " + first);
            yield return null;
            v = TarPitStats.Nearest(land, LiquidType.Water);
            if (v == null) { Say("ptest pour: no pool exists after the first stroke: " + first); yield break; }
            var r = new Obs { Label = "P", V = v, T0 = Time.time, Poured = vol };
            Out("pool: " + v.transform.position.ToString("0.0") + " pump pool=" + TarPits.IsPumpPool(v) + " volume before=" + F2(before) + " m3");
            float next = Time.time + interval; int done = 1;
            float levelStart = float.NaN;
            while (done < strokes && !Cancel)
            {
                var s = Sample(r);
                if (float.IsNaN(levelStart) && s.Wet > 0) levelStart = s.Level;
                if (Time.time >= next) { TarPool.DebugPour(land, vol, PumpLiquid.Water); r.Poured += vol; done++; next = Time.time + interval; }
                yield return new WaitForSeconds(SampleSeconds);
            }
            var endPour = Sample(r);
            float tPourEnd = Time.time;
            while (Time.time < tPourEnd + PoolHoldAfterPour && !Cancel) { Sample(r); yield return new WaitForSeconds(SampleSeconds); }
            var fin = r.Last;
            float area = endPour.Region * v.m_scale * v.m_scale;
            Say("POUR done: poured " + F2(r.Poured) + " m3, pool total " + F2(before) + " -> " + F2(endPour.Total) + " right after the last stroke (kept " + F2(endPour.Total - before) + " = " +
                F2((endPour.Total - before) / Mathf.Max(0.01f, r.Poured) * 100f) + " %), " + F2(fin.Total) + " after " + F2(PoolHoldAfterPour) + " s more; level " + F(levelStart) + " -> " + F(endPour.Level) +
                " -> " + F(fin.Level) + " (max " + F(r.MaxLevel) + "), basin " + endPour.Region + " cells = " + F2(area) + " m2, spill level " + F(endPour.Spill4) + "; " + r.Reloads + " reload(s), " + r.Saves + " save(s), " + r.Drops + " drop(s)");
        }
    }
}
