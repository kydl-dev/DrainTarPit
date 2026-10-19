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
using static DrainTarPit.Debug.PoolRunTests;
using static DrainTarPit.Debug.PoolTankTests;
using static DrainTarPit.Debug.PoolProbe;

namespace DrainTarPit.Debug
{
    /// <summary>
    /// In-game test harness for "the water does not stay in the pool" (console: <c>dtp ptest ...</c>). Everything is measured on the pool's own
    /// depth / height lists, the same numbers the game's simulation works on, and written as plain lines to <c>DrainTarPit.PoolTests.txt</c> (folder:
    /// [General] TestOutDir, default the BepInEx config folder) so that the next agent can read the result without the user copying anything.
    ///
    /// Tests (see _agent/TESTLOG.md for what has been run and what each result means):
    ///   watch [sec]                  samples the nearest valid pump water pool 4x a second: volume, level, where the water is (basin / elsewhere), the level at which the
    ///                                basin would overflow (spill4 / spill8), save + reload events of the pool's ZDO.
    ///   pour [n] [interval]          pours n strokes at the crosshair into an existing flagged pump pool if its grid safely covers the point, otherwise makes a new pool (unless in sea/lake water); then watches 60 s more.
    ///   tank [R] [wallH] [n] [mode]  a synthetic closed tank (square of 2R+1 cells, walls wallH m high) in a fresh pool: geometry is known exactly, no built pieces
    ///                                and no terrain involved. mode = flood (the real FloodPass) or inject (water spread evenly over the tank, no FloodPass).
    ///   matrix                       the tank test under several settings (flood / inject / no film shedding / no flow boost / slow flow): where the loss comes from.
    ///   rim                          the level at which the basin around the crosshair overflows its walls (and where the lowest gap is).
    ///   fields                       every field and method name of LiquidVolume / LiquidSurface with the values of the nearest pool.
    ///   out [folder] | stop | help
    /// Target selection skips unflagged/non-pump water volumes; use `dtp cleanpools` to request removal of empty stale ones before a test.
    ///
    /// Split by responsibility (T-016): this class = command entry; PoolTestOut = report + environment helpers; PoolSnapshot = pool snapshot + spill
    /// level; PoolObserver = one observed pool; PoolRunTests = watch / pour; PoolTankTests = tank / matrix; PoolProbe = rim / fields.
    /// </summary>
    internal static class PoolTests
    {
        internal const float SampleSeconds = 0.25f;
        internal const float DefaultPourInterval = 0.5f;

        // --- command entry -----------------------------------------------------------------------------------------------
        internal static bool _busy;
        internal static bool Cancel;

        internal static void Run(string[] args)
        {
            var sub = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
            if (sub == "stop") { Cancel = true; Say("ptest: stopping the running test."); return; }
            if (sub == "out")
            {
                if (args.Length > 1) { ModSettings.TestOutDir.Value = CleanPath(string.Join(" ", args, 1, args.Length - 1)); }
                Say("ptest: report file = " + Path.Combine(OutDir(), ReportFile));
                return;
            }
            if (sub == "help") { Say(Help); return; }
            if (_busy) { Say("ptest: a test is already running (dtp ptest stop)."); return; }
            if (Player.m_localPlayer == null || ZNetScene.instance == null) { Say("ptest: no world loaded."); return; }
            switch (sub)
            {
                case "fields": Fields(); break;
                case "rim": Rim(); break;
                case "watch": Start(Watch(Arg(args, 1, 60f))); break;
                case "pour": Start(Pour((int)Arg(args, 1, 10f), Arg(args, 2, DefaultPourInterval))); break;
                case "tank": Start(TankTest((int)Arg(args, 1, 10f), Arg(args, 2, 6f), (int)Arg(args, 3, 8f), args.Length > 4 && args[4].ToLowerInvariant() == "inject")); break;
                case "matrix": Start(Matrix()); break;
                default: Say(Help); break;
            }
        }

        const string Help = "dtp ptest watch [sec] | pour [n] [interval] | tank [R] [wallH] [n] [flood|inject] | matrix | rim | fields | out [folder] | stop   (report: DrainTarPit.PoolTests.txt; use dtp cleanpools for empty stale water pools)";

        // The game console turns a typed backslash sequence into a control character ("\testruns" arrives with a TAB in it). Put the backslash back,
        // so a Windows path can be typed either way; forward slashes work too.
        internal static string CleanPath(string p)
        {
            return p.Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\b", "\\b").Replace("\f", "\\f").Trim().Trim('"');
        }

        internal static float Arg(string[] a, int i, float def)
        {
            float f;
            return a.Length > i && float.TryParse(a[i], NumberStyles.Float, Inv, out f) ? f : def;
        }

        internal static void Start(IEnumerator routine)
        {
            _busy = true; Cancel = false;
            Plugin.Instance.RunCoroutine(Guarded(routine));
        }

        // runs the test; a failure inside is logged and always ends the busy state
        internal static IEnumerator Guarded(IEnumerator inner)
        {
            while (true)
            {
                bool more;
                try { more = inner.MoveNext(); }
                catch (System.Exception e) { Say("ptest: FAILED: " + e); more = false; }
                if (!more) break;
                yield return inner.Current;
            }
            _busy = false;
        }
    }
}
