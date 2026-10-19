using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace DrainTarPit.Drain
{
    /// <summary>
    /// Grid checks, pool identity text and the explanation of water receiver selection. Kept separate from TarPool so the production deposit
    /// class stays under the project's class-size limit (PROJECT_RULES rule 8) and does not format debug-only candidate lists on strokes.
    /// </summary>
    internal static class TarPoolDiagnostics
    {
        /// Test-log explanation of every loaded water-volume candidate at a landing point. Each candidate is a separate log line.
        internal static List<string> DescribeWaterSelection(Vector3 p, LiquidVolume selected)
        {
            var lines = new List<string>();
            int water = 0;
            int other = 0;
            foreach (var v in TarPits.All)
            {
                if (v == null) continue;
                if (v.m_liquidType != LiquidType.Water) { other++; continue; }
                water++;
                bool pump = TarPits.IsPumpPool(v);
                bool geometry = v.m_width > 0 && v.m_width <= 512 && v.m_scale > 0f && !float.IsNaN(v.m_scale) && !float.IsInfinity(v.m_scale);
                bool inside = geometry && GridCoversPoint(v, p);
                bool margin = inside && TarPits.Contains(v, p, 1.5f);
                string decision;
                if (!geometry) decision = "REJECT invalid grid geometry";
                else if (!inside) decision = "REJECT landing point outside grid";
                else if (!margin) decision = "REJECT landing point within 1.5m edge safety margin";
                else if (!pump) decision = "REJECT non-pump water pool";
                else if (object.ReferenceEquals(v, selected)) decision = "SELECTED";
                else decision = "ELIGIBLE but not selected (another eligible pool has a nearer centre)";
                lines.Add("pool selection candidate: poolId=" + v.GetInstanceID() + " name=\"" + v.name + "\" pos=" + v.transform.position.ToString("0.0") +
                    " pumpPool=" + pump + " grid=" + v.m_width + "x" + v.m_width + " cell=" + v.m_scale.ToString("0.000", CultureInfo.InvariantCulture) +
                    " coversPoint=" + inside + " selectionMargin=" + margin + " decision=\"" + decision + "\"");
            }
            lines.Insert(0, "pool selection scan: landing=" + p.ToString("0.00") + " waterCandidates=" + water + " nonWaterVolumesIgnored=" + other +
                " selected=" + (selected != null ? "poolId=" + selected.GetInstanceID() : "none (a fresh pump pool would be spawned unless the point is sea/lake water)"));
            return lines;
        }

        internal static bool HasUsableGridGeometry(LiquidVolume v)
        {
            return v != null && v.m_width > 0 && v.m_width <= 512 &&
                   v.m_scale > 0f && !float.IsNaN(v.m_scale) && !float.IsInfinity(v.m_scale);
        }

        internal static bool GridCoversPoint(LiquidVolume v, Vector3 p)
        {
            if (!HasUsableGridGeometry(v)) return false;
            return TarPits.InGrid(v, TarPits.ToGrid(v, p));
        }

        /// Diagnostic identity for test and verbose deposit logs. Instance ID is stable for the lifetime of this Unity object (not across game sessions).
        internal static string DescribeVolume(LiquidVolume v)
        {
            if (v == null) return "pool=<none/destroyed>";
            int w = v.m_width;
            long expected = w >= 0 ? ((long)w + 1L) * ((long)w + 1L) : -1L;
            int depths = -1, heights = -1;
            bool ready = false;
            string gridError = null;
            try
            {
                ready = TarPitDebug.Ready(v);
                using (TarPitDebug.Lock(v))
                {
                    var d = TarPitDebug.DepthList(v);
                    var h = TarPitDebug.HeightList(v);
                    depths = d != null ? d.Count : -1;
                    heights = h != null ? h.Count : -1;
                }
            }
            catch (System.Exception e) { gridError = e.GetType().Name; }
            bool scaleValid = v.m_scale > 0f && !float.IsNaN(v.m_scale) && !float.IsInfinity(v.m_scale);
            bool valid = ready && w > 0 && scaleValid && expected > 0 && depths >= expected && heights >= expected && gridError == null;
            return "poolId=" + v.GetInstanceID() + " name=\"" + v.name + "\" pos=" + v.transform.position.ToString("0.00") +
                " liquid=" + TarPits.LiquidOf(v) + " pumpPool=" + TarPits.IsPumpPool(v) + " grid=" + w + "x" + w + " cell=" + v.m_scale.ToString("0.000", CultureInfo.InvariantCulture) +
                "m heightsReady=" + ready + " depthCells=" + (depths < 0 ? "null" : depths.ToString(CultureInfo.InvariantCulture)) + " heightCells=" + (heights < 0 ? "null" : heights.ToString(CultureInfo.InvariantCulture)) +
                " expectedCells=" + expected.ToString(CultureInfo.InvariantCulture) + " gridValid=" + valid + (gridError != null ? " gridError=" + gridError : "");
        }

        internal static bool GridIsValid(LiquidVolume v)
        {
            if (v == null || v.m_width <= 0 || v.m_width > 512 || v.m_scale <= 0f || float.IsNaN(v.m_scale) || float.IsInfinity(v.m_scale)) return false;
            long expected = ((long)v.m_width + 1L) * ((long)v.m_width + 1L);
            if (expected <= 0 || expected > int.MaxValue) return false;
            try
            {
                if (!TarPitDebug.Ready(v)) return false;
                using (TarPitDebug.Lock(v))
                {
                    var d = TarPitDebug.DepthList(v); var h = TarPitDebug.HeightList(v);
                    return d != null && h != null && d.Count >= expected && h.Count >= expected;
                }
            }
            catch { return false; }
        }
    }
}
