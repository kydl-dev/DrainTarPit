using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace DrainTarPit.Drain
{
    /// <summary>
    /// Diagnostic explanation of water receiver selection. Kept separate from TarPool so the production deposit
    /// class stays under the project's class-size limit and does not format debug-only candidate lists on strokes.
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
                bool inside = geometry && TarPool.GridCoversPoint(v, p);
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
    }
}
