using System.Collections.Generic;
using System.IO;
using BepInEx;
using DrainTarPit.Drain;
using DrainTarPit.Logging;
using DrainTarPit.Visuals;
using UnityEngine;

namespace DrainTarPit.Signs
{
    /// <summary>
    /// T-023 (d) (e): which water style a pool shows ON THIS PC. Order: the player's own choice for that pool (pool menu, F9; only for pools
    /// with a named sign, saved per world on this PC: BepInEx/config/DrainTarPit.PoolStyles/&lt;world&gt;.txt) - the world style an admin set
    /// (world-saved global key, PoolRegistry) - this PC's [Visuals] WaterStyle. Three players can see three styles on the same pool.
    /// </summary>
    internal static class PoolStyles
    {
        static readonly Dictionary<string, WaterStyleOption> Mine = new();
        static string _loadedFor;
        static string _lastWorldKey = "";
        static float _nextWorldCheck;
        static readonly Dictionary<LiquidVolume, PoolSign> SignOf = new();
        static int _signGen = -1;

        // --- world style (admin) --------------------------------------------------------------------------------------------
        /// The world style an admin set, or null when none was set in this world.
        internal static WaterStyleOption? World()
        {
            string v;
            WaterStyleOption s;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(PoolRegistry.WorldKey, out v) && System.Enum.TryParse(v, true, out s)) return s;
            return null;
        }

        /// What a pool without a personal choice shows: the world style, else this PC's config.
        internal static WaterStyleOption Default
        {
            get
            {
                var w = World();
                return w ?? (ModSettings.WaterStyle != null ? ModSettings.WaterStyle.Value : WaterStyleOption.Game);
            }
        }

        /// Called every frame (Plugin.Update): notices a changed world style (an admin changed it) once a second and redraws the pools.
        internal static void Tick()
        {
            if (Time.unscaledTime < _nextWorldCheck) return;
            _nextWorldCheck = Time.unscaledTime + 1f;
            var w = World();
            string key = w.HasValue ? w.Value.ToString() : "";
            if (key == _lastWorldKey) return;
            ModLog.Verbose("PoolStyles: world water style " + (key.Length == 0 ? "not set (each PC's config)" : key) + " (was " + (_lastWorldKey.Length == 0 ? "not set" : _lastWorldKey) + ")");
            _lastWorldKey = key;
            PoolWater.RequestRefresh();
        }

        // --- personal choice per pool -------------------------------------------------------------------------------------------
        static string FileFor()
        {
            var w = ZNet.World;
            string world = w != null ? w.m_name + "_" + w.m_uid : "unknown";
            foreach (char c in Path.GetInvalidFileNameChars()) world = world.Replace(c, '_');
            return Path.Combine(Path.Combine(Paths.ConfigPath, "DrainTarPit.PoolStyles"), world + ".txt");
        }

        static void Load()
        {
            string file = FileFor();
            if (_loadedFor == file) return;
            _loadedFor = file;
            Mine.Clear();
            try
            {
                if (File.Exists(file))
                    foreach (var line in File.ReadAllLines(file))
                    {
                        int eq = line.IndexOf('=');
                        WaterStyleOption s;
                        if (eq > 0 && System.Enum.TryParse(line.Substring(eq + 1).Trim(), true, out s)) Mine[line.Substring(0, eq).Trim()] = s;
                    }
                ModLog.Verbose("PoolStyles: " + Mine.Count + " personal pool style(s) loaded from " + file);
            }
            catch (IOException e) { ModLog.Warn("PoolStyles: could not read " + file + ": " + e.Message); }
            catch (System.UnauthorizedAccessException e) { ModLog.Warn("PoolStyles: no permission to read " + file + ": " + e.Message); }
        }

        /// This player's choice for the pool of sign `signId`, or null (= Default).
        internal static WaterStyleOption? MineFor(string signId)
        {
            Load();
            WaterStyleOption s;
            return signId != null && Mine.TryGetValue(signId, out s) ? s : (WaterStyleOption?)null;
        }

        /// Sets (or with null clears) this player's choice for a pool and saves the file. Returns the number of personal choices now.
        internal static int SetMine(string signId, WaterStyleOption? style)
        {
            Load();
            if (style.HasValue) Mine[signId] = style.Value; else Mine.Remove(signId);
            string file = FileFor();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                var lines = new List<string>();
                foreach (var kv in Mine) lines.Add(kv.Key + "=" + kv.Value);
                File.WriteAllLines(file, lines.ToArray());
                ModLog.Verbose("PoolStyles: pool " + signId + " -> " + (style.HasValue ? style.Value.ToString() : "world / default") + ", saved to " + file);
            }
            catch (IOException e) { ModLog.Warn("PoolStyles: could not save " + file + ": " + e.Message); }
            catch (System.UnauthorizedAccessException e) { ModLog.Warn("PoolStyles: no permission to save " + file + ": " + e.Message); }
            PoolWater.RequestRefresh();
            return Mine.Count;
        }

        // --- what a pool shows ----------------------------------------------------------------------------------------------------
        /// The style the pool `v` shows on this PC (called when the pool redraws, PoolWater).
        internal static WaterStyleOption For(LiquidVolume v)
        {
            var sign = SignFor(v);
            var mine = sign != null ? MineFor(sign.Id) : null;
            return mine ?? Default;
        }

        /// The loaded named sign standing on / next to the pool (cached until a sign loads, unloads or is renamed).
        static PoolSign SignFor(LiquidVolume v)
        {
            if (_signGen != PoolSign.Generation) { SignOf.Clear(); _signGen = PoolSign.Generation; }
            PoolSign found;
            if (SignOf.TryGetValue(v, out found)) return found;
            found = null;
            foreach (var s in PoolSign.Loaded)
                if (s != null && !string.IsNullOrEmpty(s.SignName) && s.Pool() == v) { found = s; break; }
            SignOf[v] = found;
            ModLog.Verbose("PoolStyles: pool " + v.name + " at " + v.transform.position.ToString("0") + " has " + (found != null ? "sign '" + found.SignName + "' (" + found.Id + ")" : "no named sign"));
            return found;
        }
    }
}
