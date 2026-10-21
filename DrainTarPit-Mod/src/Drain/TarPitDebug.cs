using System.Collections.Generic;
using System.Reflection;
using DrainTarPit.Logging;
using HarmonyLib;
using UnityEngine;
using static DrainTarPit.Drain.TarPits;
using static DrainTarPit.Drain.TarPitTracking;

namespace DrainTarPit.Drain
{
    /// <summary>
    /// Debug text for `dtp pits` (the pool cleanup / test doors are in WaterAndTarPools).
    /// </summary>
    internal static class TarPitDebug
    {
        /// Debug text (dtp pits): what this volume says about world point p - inside its grid?, tar depth there, tar surface height, kind, owner.
        public static string DescribeAt(LiquidVolume v, Vector3 p)
        {
            var g = ToGrid(v, p);
            var nv = Nview(v); var zdo = nv != null && nv.IsValid() ? nv.GetZDO() : null;
            string s = (zdo != null && ZdoKeys.Bool(zdo, TarPits.ZPool, TarPits.ZPoolOld) ? "[pump pool] " : "[vanilla] ") + "grid " + v.m_width + "x" + v.m_width + "@" + v.m_scale + "m, owner=" + (nv != null && nv.IsValid() && nv.IsOwner());
            if (!InGrid(v, g)) return s + ", tracks you: " + Tracks(v, Player.m_localPlayer) + ", you are outside its grid";
            float dep; using (Hold(v)) { var d = Depths(v); dep = d != null && d.Count >= (v.m_width + 1) * (v.m_width + 1) ? DepthAt(v, d, g.x, g.y) : -1f; }
            return s + ", tracks you: " + Tracks(v, Player.m_localPlayer) + ", under you: depth " + dep.ToString("0.00") + " m, surface y " + v.GetSurface(p).ToString("0.00") + " (you y " + p.y.ToString("0.00") + ")";
        }
    }
}
