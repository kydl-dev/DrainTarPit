using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using DrainTarPit.Drain;
using DrainTarPit.Logging;
using UnityEngine;


namespace DrainTarPit.Debug
{
    /// Report file, console output and the small environment helpers (config line, crosshair, nearest pool) shared by the pool tests (PoolTests).
    internal static class PoolTestOut
    {
        internal const string ReportFile = "DrainTarPit.PoolTests.txt";
        internal static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // --- output ------------------------------------------------------------------------------------------------------
        internal static string OutDir()
        {
            var d = ModSettings.TestOutDir.Value;
            try
            {
                if (!string.IsNullOrEmpty(d)) { Directory.CreateDirectory(d); return d; }
            }
            catch (System.Exception e) { ModLog.Warn("ptest: cannot use TestOutDir '" + d + "': " + e.Message); }
            return BepInEx.Paths.ConfigPath;
        }

        internal static void Out(string line)
        {
            ModLog.Info("[ptest] " + line);
            try { File.AppendAllText(Path.Combine(OutDir(), ReportFile), System.DateTime.Now.ToString("HH:mm:ss.fff", Inv) + " " + line + "\n"); }
            catch (System.Exception e) { ModLog.VerboseWarn("ptest: report not written: " + e.Message); }
        }

        /// Short text for the console as well as the report.
        internal static void Say(string line)
        {
            Out(line);
            if (global::Console.instance != null) global::Console.instance.Print(line);
        }

        internal static string F(float f) { return f.ToString("0.000", Inv); }
        internal static string F2(float f) { return f.ToString("0.00", Inv); }

        internal static string ConfigLine()
        {
            return "cfg: WaterFlow=" + F2(ModSettings.WaterFlow.Value) + " PitFlowBoost=" + F2(ModSettings.PitFlowBoost.Value) + "/" + F2(ModSettings.PitFlowBoostSeconds.Value) + "s" +
                   " PoolWalls=" + ModSettings.PoolWalls.Value + " PoolOverflow=" + ModSettings.PoolOverflow.Value + " PoolShedFilm=" + ModSettings.PoolShedFilm.Value +
                   " PoolFilmDepth=" + F2(ModSettings.PoolFilmDepth.Value) + " PoolFilmEvaporates=" + ModSettings.PoolFilmEvaporates.Value + " PoolRunoff=" + ModSettings.PoolRunoff.Value +
                   " VolumePerStroke=" + F2(ModSettings.VolumePerStroke.Value) + " cell=" + F2(ModSettings.WaterCellSize.Value) + " size=" + F2(ModSettings.WaterPoolSize.Value);
        }

        internal static string EnvLine(Vector3 at)
        {
            float sea = 30f;
            var f = HarmonyLib.AccessTools.Field(typeof(ZoneSystem), "m_waterLevel");
            if (f != null && ZoneSystem.instance != null) sea = (float)f.GetValue(ZoneSystem.instance);
            float ground = ZoneSystem.instance != null ? ZoneSystem.instance.GetGroundHeight(at) : at.y;
            return "env: at " + at.ToString("0.0") + " terrain y " + F2(ground) + " sea level " + F2(sea) + " (terrain " + F2(ground - sea) + " m above the sea) dll built " +
                   File.GetLastWriteTime(Assembly.GetExecutingAssembly().Location).ToString("yyyy-MM-dd HH:mm:ss", Inv);
        }

        internal static bool Crosshair(out Vector3 point)
        {
            point = Vector3.zero;
            var cam = Camera.main;
            if (cam == null) return false;
            RaycastHit hit;
            if (!Physics.Raycast(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out hit, 80f, ~0, QueryTriggerInteraction.Ignore)) return false;
            point = hit.point;
            return true;
        }

        internal static LiquidVolume NearestPool(Vector3 p)
        {
            return TarPitStats.Nearest(p, LiquidType.Water);
        }
    }
}
