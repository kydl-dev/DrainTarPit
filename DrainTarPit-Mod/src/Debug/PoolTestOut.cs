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
            return "env: at " + at.ToString("0.0") + " terrain y " + F2(ground) + " sea level " + F2(sea) + " (terrain " + F2(ground - sea) + " m above the sea); " + RuntimeDllLine();
        }

        /// Fingerprint of the assembly actually loaded by this game process, not the DLL merely present in the source tree.
        internal static string RuntimeDllLine()
        {
            var assembly = Assembly.GetExecutingAssembly();
            string path = assembly.Location;
            string version = assembly.GetName().Version != null ? assembly.GetName().Version.ToString() : "unknown";
            try
            {
                var info = new FileInfo(path);
                string hash;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (var stream = File.OpenRead(path))
                    hash = System.BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                return "runtimeDll path=\"" + path + "\" version=" + version + " bytes=" + info.Length + " modified=" + info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", Inv) + " sha256=" + hash;
            }
            catch (System.Exception e)
            {
                return "runtimeDll path=\"" + path + "\" version=" + version + " fingerprintError=" + e.GetType().Name + ": " + e.Message;
            }
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
            // Watch / fields may inspect the nearest pump pool even when the player stands outside its grid, but never an unflagged water volume.
            LiquidVolume best = null;
            float bestDistance = float.MaxValue;
            foreach (var v in TarPits.All)
            {
                if (v == null || v.m_liquidType != LiquidType.Water || !TarPits.IsPumpPool(v)) continue;
                if (v.m_width <= 0 || v.m_width > 512 || v.m_scale <= 0f || float.IsNaN(v.m_scale) || float.IsInfinity(v.m_scale)) continue;
                float distance = (v.transform.position - p).sqrMagnitude;
                if (distance < bestDistance) { bestDistance = distance; best = v; }
            }
            return best;
        }
    }
}
