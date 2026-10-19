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
using static DrainTarPit.Debug.PoolTests;

namespace DrainTarPit.Debug
{
    /// One-shot probes: where would the water escape (rim), and what the game's LiquidVolume / LiquidSurface are made of (fields) (PoolTests).
    internal static class PoolProbe
    {
        // --- rim: where would the water escape? ---------------------------------------------------------------------------------
        internal static void Rim()
        {
            var p = Player.m_localPlayer.transform.position;
            Vector3 at;
            if (!Crosshair(out at)) at = p;
            var v = TarPool.FindVolume(at, PumpLiquid.Water);
            if (v == null) { Say("ptest rim: the crosshair is not inside a valid pump water pool; aim inside the intended pool and try again."); return; }
            if (!TarPoolDiagnostics.GridIsValid(v)) { Say("ptest rim: invalid or not-yet-initialized pool grid: " + TarPoolDiagnostics.DescribeVolume(v)); return; }
            int w = v.m_width, stride = w + 1, n = stride * stride;
            var hl = TarPitDebug.HeightList(v);
            if (hl == null || hl.Count < n) { Say("ptest rim: invalid height grid for " + TarPoolDiagnostics.DescribeVolume(v)); return; }
            if (_d.Length < n) { _d = new float[n]; _h = new float[n]; _seen = new bool[n]; _q = new int[n]; }
            using (TarPitDebug.Lock(v)) { hl.CopyTo(0, _h, 0, n); }
            var o = v.transform.position;
            int gx = Mathf.Clamp(Mathf.RoundToInt((at.x - (o.x - w * v.m_scale * 0.5f)) / v.m_scale), 1, w - 1);
            int gy = Mathf.Clamp(Mathf.RoundToInt((at.z - (o.z - w * v.m_scale * 0.5f)) / v.m_scale), 1, w - 1);
            int seed = gy * stride + gx;
            // go downhill (ground only) to the lowest cell near the crosshair, like the first step of FloodPass
            for (int step = 0; step < 80; step++)
            {
                int cx = seed % stride, cy = seed / stride, best = seed;
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + Ox[k], ny = cy + Oy[k];
                    if (nx < 1 || ny < 1 || nx > w - 1 || ny > w - 1) continue;
                    int j = ny * stride + nx;
                    if (_h[j] < _h[best] - 0.0005f) best = j;
                }
                if (best == seed) break;
                seed = best;
            }
            int pass4, pass8;
            float s4 = SpillLevel(seed, w, stride, false, out pass4), s8 = SpillLevel(seed, w, stride, true, out pass8);
            string P(int c) { return c >= 0 ? "(" + F2(o.x + (c % stride - w / 2) * v.m_scale) + ", " + F2(o.y + _h[c]) + ", " + F2(o.z + (c / stride - w / 2) * v.m_scale) + ")" : "none"; }
            Say("RIM: basin around the crosshair has its floor at y " + F2(o.y + _h[seed]) + ". Water overflows at y " + F2(o.y + s4) + " (4-neighbour) / y " + F2(o.y + s8) + " (8-neighbour, diagonal) - that is " +
                F2(s4 - _h[seed]) + " m / " + F2(s8 - _h[seed]) + " m of water. Lowest wall cell near " + P(pass4) + " (diagonal: " + P(pass8) + "). Pool: " + TarPoolDiagnostics.DescribeVolume(v) + ", wall cells remembered: " + TarPitWalls.WallCellCount(v) +
                (s4 >= MaxLevelProbe - 0.01f ? ". The basin is closed up to the top of the grid's height range." : ""));
            // step 12: the rule the deposit uses now (HeightField.FindBasin: heights + the liquid already standing there, no object types)
            HeightField.Basin hb;
            using (TarPitDebug.Lock(v)) hb = HeightField.FindBasin(TarPitDebug.DepthList(v), TarPitDebug.HeightList(v), w, seed, float.NegativeInfinity);
            Say("RIM (deposit rule): " + (hb.Enclosed ? "ENCLOSED pool" : "NOT a pool (a dent / slope: liquid just runs downhill)") + ", floor y " + F2(o.y + hb.Floor) + ", rim y " +
                (float.IsInfinity(hb.Rim) ? "closed" : F2(o.y + hb.Rim)) + " (" + (float.IsInfinity(hb.Rim) ? "-" : F2(hb.Depth)) + " m), " + hb.Cells + " cells inside = " + F2(hb.Cells * v.m_scale * v.m_scale) +
                " m2; lowest rim point " + P(hb.RimCell) + "; [Drain] PoolRimDisappear=" + ModSettings.PoolRimDisappear.Value);
        }

        // --- fields: what the game's LiquidVolume is made of -------------------------------------------------------------------------
        internal static void Fields()
        {
            var p = Player.m_localPlayer.transform.position;
            var v = NearestPool(p);
            if (v == null) { Say("ptest fields: no valid pump water pool is loaded; run dtp pits to inspect loaded pools."); return; }
            Out("=== FIELDS of LiquidVolume / LiquidSurface (names are what the game has; instance being inspected: " + TarPoolDiagnostics.DescribeVolume(v) + ")");
            Dump(typeof(LiquidVolume), v);
            LiquidSurface ls = v != null ? v.GetComponentInChildren<LiquidSurface>(true) : null;
            Dump(typeof(LiquidSurface), ls);
            Say("FIELDS written to " + Path.Combine(OutDir(), ReportFile));
        }

        internal static void Dump(System.Type t, object inst)
        {
            const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (var f in t.GetFields(All))
            {
                string val = "-";
                try
                {
                    var o = f.IsStatic ? f.GetValue(null) : inst != null ? f.GetValue(inst) : null;
                    if (inst == null && !f.IsStatic) val = "(no instance)";
                    else if (o == null) val = "null";
                    else if (o is System.Array) val = "Array[" + ((System.Array)o).Length + "]";
                    else if (o is ICollection) val = "Count=" + ((ICollection)o).Count;
                    else val = o.ToString();
                    if (val.Length > 90) val = val.Substring(0, 90);
                }
                catch (System.Exception e) { val = "(" + e.GetType().Name + ")"; }
                Out(t.Name + " field " + (f.IsStatic ? "static " : "") + f.FieldType.Name + " " + f.Name + " = " + val);
            }
            var names = new List<string>();
            foreach (var m in t.GetMethods(All)) names.Add(m.Name + "(" + m.GetParameters().Length + ")");
            Out(t.Name + " methods: " + string.Join(", ", names.ToArray()));
        }
    }
}
