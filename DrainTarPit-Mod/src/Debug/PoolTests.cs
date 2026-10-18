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
    /// <summary>
    /// In-game test harness for "the water does not stay in the pool" (console: <c>dtp ptest ...</c>). Everything is measured on the pool's own
    /// depth / height lists, the same numbers the game's simulation works on, and written as plain lines to <c>DrainTarPit.PoolTests.txt</c> (folder:
    /// [General] TestOutDir, default the BepInEx config folder) so that the next agent can read the result without the user copying anything.
    ///
    /// Tests (see _agent/TESTLOG.md for what has been run and what each result means):
    ///   watch [sec]                  samples the nearest water pool 4x a second: volume, level, where the water is (basin / elsewhere), the level at which the
    ///                                basin would overflow (spill4 / spill8), save + reload events of the pool's ZDO.
    ///   pour [n] [interval]          pours n strokes at the crosshair into the nearest pool (the real deposit path) while watching, then watches 60 s more.
    ///   tank [R] [wallH] [n] [mode]  a synthetic closed tank (square of 2R+1 cells, walls wallH m high) in a fresh pool: geometry is known exactly, no built pieces
    ///                                and no terrain involved. mode = flood (the real FloodPass) or inject (water spread evenly over the tank, no FloodPass).
    ///   matrix                       the tank test under several settings (flood / inject / no film shedding / no flow boost / slow flow): where the loss comes from.
    ///   rim                          the level at which the basin around the crosshair overflows its walls (and where the lowest gap is).
    ///   fields                       every field and method name of LiquidVolume / LiquidSurface with the values of the nearest pool.
    ///   out [folder] | stop | help
    /// </summary>
    internal static class PoolTests
    {
        const string ReportFile = "DrainTarPit.PoolTests.txt";
        const float SampleSeconds = 0.25f;
        const float DefaultPourInterval = 0.5f;
        const float HoldAfterPour = 25f;            // tank test: watch time after the last stroke
        const float PoolHoldAfterPour = 60f;        // pour test in a real pool
        const float DropEvent = 0.3f;               // m3 lost between two samples without a pour = an event worth a line
        const float MaxLevelProbe = 12.5f;          // top of the height range a pool grid can hold (m_maxDepth 12 + margin)
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // --- synthetic tanks ---------------------------------------------------------------------------------------------
        internal sealed class Tank { public int R; public int Ring = 3; public float Floor = -1f; public float Top; }
        static readonly Dictionary<LiquidVolume, Tank> Synth = new Dictionary<LiquidVolume, Tank>();

        /// Called from the UpdateHeights postfix (TarPits.Patch_UpdateHeights) with the pool's lock held: forces the heights of a test pool to the tank.
        internal static void ApplySynthetic(LiquidVolume v)
        {
            Tank t;
            if (Synth.Count == 0 || !Synth.TryGetValue(v, out t)) return;
            var h = TarPits.HeightList(v);
            int w = v.m_width, stride = w + 1, n = stride * stride, c = w / 2;
            if (h == null || h.Count < n) return;
            using (TarPits.Lock(v))
            {
                for (int i = 0; i < n; i++)
                {
                    int cheb = Mathf.Max(Mathf.Abs(i % stride - c), Mathf.Abs(i / stride - c));
                    h[i] = cheb <= t.R ? t.Floor : cheb <= t.R + t.Ring ? t.Top : t.Floor - 0.5f;
                }
                TarPits.MarkChanged(v);
            }
        }

        // --- output ------------------------------------------------------------------------------------------------------
        static string OutDir()
        {
            var d = ModSettings.TestOutDir.Value;
            try
            {
                if (!string.IsNullOrEmpty(d)) { Directory.CreateDirectory(d); return d; }
            }
            catch (System.Exception e) { ModLog.Warn("ptest: cannot use TestOutDir '" + d + "': " + e.Message); }
            return BepInEx.Paths.ConfigPath;
        }

        static void Out(string line)
        {
            ModLog.Info("[ptest] " + line);
            try { File.AppendAllText(Path.Combine(OutDir(), ReportFile), System.DateTime.Now.ToString("HH:mm:ss.fff", Inv) + " " + line + "\n"); }
            catch (System.Exception e) { ModLog.VerboseWarn("ptest: report not written: " + e.Message); }
        }

        /// Short text for the console as well as the report.
        static void Say(string line)
        {
            Out(line);
            if (global::Console.instance != null) global::Console.instance.Print(line);
        }

        static string F(float f) { return f.ToString("0.000", Inv); }
        static string F2(float f) { return f.ToString("0.00", Inv); }

        // --- command entry -----------------------------------------------------------------------------------------------
        static bool _busy, _cancel;

        internal static void Run(string[] args)
        {
            var sub = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
            if (sub == "stop") { _cancel = true; Say("ptest: stopping the running test."); return; }
            if (sub == "out")
            {
                if (args.Length > 1) { ModSettings.TestOutDir.Value = string.Join(" ", args, 1, args.Length - 1); }
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

        const string Help = "dtp ptest watch [sec] | pour [n] [interval] | tank [R] [wallH] [n] [flood|inject] | matrix | rim | fields | out [folder] | stop   (report: DrainTarPit.PoolTests.txt)";

        static float Arg(string[] a, int i, float def)
        {
            float f;
            return a.Length > i && float.TryParse(a[i], NumberStyles.Float, Inv, out f) ? f : def;
        }

        static void Start(IEnumerator routine)
        {
            _busy = true; _cancel = false;
            Plugin.Instance.RunCoroutine(Guarded(routine));
        }

        // runs the test; a failure inside is logged and always ends the busy state
        static IEnumerator Guarded(IEnumerator inner)
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

        // --- snapshot of a pool ------------------------------------------------------------------------------------------
        sealed class Snap
        {
            public float Total, Level, Border, Spread, Spill4 = float.NaN, Spill8 = float.NaN;
            public float Inside, Outside, TankIn, Ring, CentreH, RingH;
            public int Wet, Region, Walls;
            public string Pass = "-";
        }

        static float[] _d = new float[0], _h = new float[0];
        static bool[] _seen = new bool[0];
        static int[] _q = new int[0];
        static readonly int[] Ox = { 1, -1, 0, 0, 1, 1, -1, -1 }, Oy = { 0, 0, 1, -1, 1, -1, 1, -1 };

        static Snap Take(LiquidVolume v, Tank tank)
        {
            var s = new Snap();
            int w = v.m_width, stride = w + 1, n = stride * stride;
            if (_d.Length < n) { _d = new float[n]; _h = new float[n]; _seen = new bool[n]; _q = new int[n]; }
            using (TarPits.Lock(v))
            {
                var dl = TarPits.DepthList(v); var hl = TarPits.HeightList(v);
                if (dl == null || hl == null || dl.Count < n || hl.Count < n) return s;
                dl.CopyTo(0, _d, 0, n); hl.CopyTo(0, _h, 0, n);
            }
            float c2 = v.m_scale * v.m_scale; var o = v.transform.position; int cc = w / 2;
            int deep = -1; float dm = 0.002f; double sum = 0, bor = 0, tin = 0, ring = 0;
            for (int i = 0; i < n; i++)
            {
                float d = _d[i];
                if (d <= 0.001f) continue;
                int x = i % stride, y = i / stride;
                s.Wet++; sum += d;
                if (x <= 1 || y <= 1 || x >= w - 1 || y >= w - 1) bor += d;
                if (tank != null)
                {
                    int cheb = Mathf.Max(Mathf.Abs(x - cc), Mathf.Abs(y - cc));
                    if (cheb <= tank.R) tin += d; else if (cheb <= tank.R + tank.Ring) ring += d;
                }
                if (d > dm) { dm = d; deep = i; }
            }
            s.Total = (float)sum * c2; s.Border = (float)bor * c2; s.TankIn = (float)tin * c2; s.Ring = (float)ring * c2;
            s.CentreH = _h[cc * stride + cc];
            if (tank != null) s.RingH = _h[cc * stride + cc + tank.R + 1];
            s.Walls = TarPits.WallCellCount(v);
            if (deep < 0) return s;

            float surf = _h[deep] + _d[deep];
            s.Level = o.y + surf;
            System.Array.Clear(_seen, 0, n);
            int head = 0, tail = 0; _q[tail++] = deep; _seen[deep] = true;
            double inside = 0; float smin = float.MaxValue, smax = float.MinValue;
            while (head < tail)
            {
                int i = _q[head++]; s.Region++; inside += _d[i];
                if (_d[i] > 0.02f) { float sf = _h[i] + _d[i]; if (sf < smin) smin = sf; if (sf > smax) smax = sf; }
                int x = i % stride, y = i / stride;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Ox[k], ny = y + Oy[k];
                    if (nx < 0 || ny < 0 || nx > w || ny > w) continue;
                    int j = ny * stride + nx;
                    if (_seen[j] || _h[j] >= surf - 0.01f) continue;
                    _seen[j] = true; _q[tail++] = j;
                }
            }
            s.Inside = (float)inside * c2; s.Outside = s.Total - s.Inside; s.Spread = smax > smin ? smax - smin : 0f;
            int pass;
            s.Spill4 = o.y + SpillLevel(deep, w, stride, false, out pass);
            s.Spill8 = o.y + SpillLevel(deep, w, stride, true, out pass);
            s.Pass = pass >= 0 ? "(" + F2(o.x + (pass % stride - w / 2) * v.m_scale) + ", " + F2(o.y + _h[pass]) + ", " + F2(o.z + (pass / stride - w / 2) * v.m_scale) + ")" : "-";
            return s;
        }

        // The lowest level at which water standing at `seed` would run out over the border of the grid: the smallest L for which cells lower than L connect the seed
        // to the border. = the lowest point of the walls around it. MaxLevelProbe when the basin is closed up to the top of what the grid can hold.
        // pass = a cell of the saddle (a wall top / gap at that level), -1 if none was found.
        static float SpillLevel(int seed, int w, int stride, bool diag, out int pass)
        {
            pass = -1;
            float lo = _h[seed], hi = MaxLevelProbe;
            if (!Escapes(seed, w, stride, hi, diag, null)) return hi;
            for (int it = 0; it < 24; it++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Escapes(seed, w, stride, mid, diag, null)) hi = mid; else lo = mid;
            }
            // the region just below the saddle: a cell next to it whose height is within 3 cm above `lo` is part of the lowest rim
            Escapes(seed, w, stride, lo, diag, _q);
            int kk = diag ? 8 : 4, n = stride * stride;
            for (int i = 0; i < n && pass < 0; i++)
            {
                if (!_seen[i]) continue;
                int x = i % stride, y = i / stride;
                for (int k = 0; k < kk; k++)
                {
                    int nx = x + Ox[k], ny = y + Oy[k];
                    if (nx < 0 || ny < 0 || nx > w || ny > w) continue;
                    int j = ny * stride + nx;
                    if (!_seen[j] && _h[j] >= lo && _h[j] <= hi + 0.03f) { pass = j; break; }
                }
            }
            return hi;
        }

        // flood from seed through cells lower than `level`; true when it reaches the grid's border zone. Leaves the visited cells in _seen when `keep` != null.
        static bool Escapes(int seed, int w, int stride, float level, bool diag, int[] keep)
        {
            int n = stride * stride;
            if (_h[seed] >= level) return false;
            System.Array.Clear(_seen, 0, n);
            var q = keep ?? _q;
            int head = 0, tail = 0; q[tail++] = seed; _seen[seed] = true;
            int kk = diag ? 8 : 4; bool out_ = false;
            while (head < tail)
            {
                int i = q[head++]; int x = i % stride, y = i / stride;
                if (x <= 1 || y <= 1 || x >= w - 1 || y >= w - 1) { out_ = true; if (keep == null) return true; }
                for (int k = 0; k < kk; k++)
                {
                    int nx = x + Ox[k], ny = y + Oy[k];
                    if (nx < 0 || ny < 0 || nx > w || ny > w) continue;
                    int j = ny * stride + nx;
                    if (_seen[j] || _h[j] >= level) continue;
                    _seen[j] = true; q[tail++] = j;
                }
            }
            return out_;
        }

        // --- one run (a pool being observed) -----------------------------------------------------------------------------------
        sealed class Obs
        {
            public string Label; public LiquidVolume V; public Tank Tank; public float T0, Poured, Expected = float.NaN;
            public Snap Last; public float PouredAtLast;
            public long PrevOwn = long.MinValue, PrevZdo = long.MinValue; public int PrevBytes = int.MinValue, PrevHash;
            public float MaxLevel = float.MinValue; public int Drops, Reloads, Saves;
        }

        static Snap Sample(Obs r)
        {
            var s = Take(r.V, r.Tank);
            float t = Time.time - r.T0;
            long own, zdo; int bytes, hash;
            TarPits.Revisions(r.V, out own, out zdo, out bytes, out hash);
            var ev = new StringBuilder();
            if (r.PrevOwn != long.MinValue)
            {
                if (own != r.PrevOwn) { ev.Append(" EVT game-rev ").Append(r.PrevOwn).Append("->").Append(own); r.Reloads++; }
                if (zdo != r.PrevZdo) ev.Append(" EVT zdo-rev ").Append(r.PrevZdo).Append("->").Append(zdo);
                if (bytes != r.PrevBytes || hash != r.PrevHash) { ev.Append(" EVT saved-data ").Append(r.PrevBytes).Append("B->").Append(bytes).Append("B"); r.Saves++; }
            }
            r.PrevOwn = own; r.PrevZdo = zdo; r.PrevBytes = bytes; r.PrevHash = hash;
            if (r.Last != null && r.Poured <= r.PouredAtLast && s.Total - r.Last.Total < -DropEvent)
            {
                r.Drops++; ev.Append(" EVT DROP ").Append(F2(s.Total - r.Last.Total)).Append(" m3 without a pour");
            }
            if (s.Level > r.MaxLevel) r.MaxLevel = s.Level;
            var sb = new StringBuilder();
            sb.Append(r.Label).Append(" t=").Append(F2(t)).Append(" poured=").Append(F2(r.Poured)).Append(" total=").Append(F2(s.Total))
              .Append(" level=").Append(F(s.Level));
            if (!float.IsNaN(r.Expected)) sb.Append(" exp=").Append(F(r.Expected));
            sb.Append(" basin=").Append(F2(s.Inside)).Append(" elsewhere=").Append(F2(s.Outside)).Append(" gridedge=").Append(F2(s.Border));
            if (r.Tank != null) sb.Append(" tankIn=").Append(F2(s.TankIn)).Append(" onWalls=").Append(F2(s.Ring));
            sb.Append(" cells=").Append(s.Region).Append(" spread=").Append(F(s.Spread)).Append(" spill4=").Append(F(s.Spill4)).Append(" spill8=").Append(F(s.Spill8))
              .Append(" wet=").Append(s.Wet).Append(" walls=").Append(s.Walls).Append(ev);
            Out(sb.ToString());
            r.Last = s; r.PouredAtLast = r.Poured;
            return s;
        }

        static string ConfigLine()
        {
            return "cfg: WaterFlow=" + F2(ModSettings.WaterFlow.Value) + " PitFlowBoost=" + F2(ModSettings.PitFlowBoost.Value) + "/" + F2(ModSettings.PitFlowBoostSeconds.Value) + "s" +
                   " PoolWalls=" + ModSettings.PoolWalls.Value + " PoolOverflow=" + ModSettings.PoolOverflow.Value + " PoolShedFilm=" + ModSettings.PoolShedFilm.Value +
                   " PoolFilmDepth=" + F2(ModSettings.PoolFilmDepth.Value) + " PoolFilmEvaporates=" + ModSettings.PoolFilmEvaporates.Value + " PoolRunoff=" + ModSettings.PoolRunoff.Value +
                   " VolumePerStroke=" + F2(ModSettings.VolumePerStroke.Value) + " cell=" + F2(ModSettings.WaterCellSize.Value) + " size=" + F2(ModSettings.WaterPoolSize.Value);
        }

        static string EnvLine(Vector3 at)
        {
            float sea = 30f;
            var f = HarmonyLib.AccessTools.Field(typeof(ZoneSystem), "m_waterLevel");
            if (f != null && ZoneSystem.instance != null) sea = (float)f.GetValue(ZoneSystem.instance);
            float ground = ZoneSystem.instance != null ? ZoneSystem.instance.GetGroundHeight(at) : at.y;
            return "env: at " + at.ToString("0.0") + " terrain y " + F2(ground) + " sea level " + F2(sea) + " (terrain " + F2(ground - sea) + " m above the sea) dll built " +
                   File.GetLastWriteTime(Assembly.GetExecutingAssembly().Location).ToString("yyyy-MM-dd HH:mm:ss", Inv);
        }

        static bool Crosshair(out Vector3 point)
        {
            point = Vector3.zero;
            var cam = Camera.main;
            if (cam == null) return false;
            RaycastHit hit;
            if (!Physics.Raycast(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out hit, 80f, ~0, QueryTriggerInteraction.Ignore)) return false;
            point = hit.point;
            return true;
        }

        static LiquidVolume NearestPool(Vector3 p)
        {
            return TarPits.Nearest(p, LiquidType.Water);
        }

        // --- watch ------------------------------------------------------------------------------------------------------
        static IEnumerator Watch(float seconds)
        {
            var p = Player.m_localPlayer.transform.position;
            var v = NearestPool(p);
            if (v == null) { Say("ptest watch: no water pool is loaded."); yield break; }
            Out("=== WATCH " + F2(seconds) + " s of the nearest water pool " + v.transform.position.ToString("0.0") + " (" + F2(Vector3.Distance(v.transform.position, p)) + " m away)");
            Out(EnvLine(p)); Out(ConfigLine());
            var r = new Obs { Label = "W", V = v, T0 = Time.time };
            float end = Time.time + seconds;
            Snap first = null;
            while (Time.time < end && !_cancel && v != null)
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
        static IEnumerator Pour(int strokes, float interval)
        {
            Vector3 land;
            if (!Crosshair(out land)) { Say("ptest pour: the crosshair points at nothing within 80 m."); yield break; }
            var p = Player.m_localPlayer.transform.position;
            float vol = ModSettings.VolumePerStroke.Value;
            Out("=== POUR " + strokes + " strokes of " + F2(vol) + " m3 every " + F2(interval) + " s at the crosshair " + land.ToString("0.00") + " (the real deposit path), then " + F2(PoolHoldAfterPour) + " s of watching");
            Out(EnvLine(land)); Out(ConfigLine());
            var v = TarPits.Nearest(land, LiquidType.Water);
            float before = v != null ? TarPits.TotalVolume(v) : 0f;
            string first = TarPool.DebugPour(land, vol, PumpLiquid.Water);
            Out("first stroke: " + first);
            yield return null;
            v = TarPits.Nearest(land, LiquidType.Water);
            if (v == null) { Say("ptest pour: no pool exists after the first stroke: " + first); yield break; }
            var r = new Obs { Label = "P", V = v, T0 = Time.time, Poured = vol };
            Out("pool: " + v.transform.position.ToString("0.0") + " pump pool=" + TarPits.IsPumpPool(v) + " volume before=" + F2(before) + " m3");
            float next = Time.time + interval; int done = 1;
            float levelStart = float.NaN;
            while (done < strokes && !_cancel)
            {
                var s = Sample(r);
                if (float.IsNaN(levelStart) && s.Wet > 0) levelStart = s.Level;
                if (Time.time >= next) { TarPool.DebugPour(land, vol, PumpLiquid.Water); r.Poured += vol; done++; next = Time.time + interval; }
                yield return new WaitForSeconds(SampleSeconds);
            }
            var endPour = Sample(r);
            float tPourEnd = Time.time;
            while (Time.time < tPourEnd + PoolHoldAfterPour && !_cancel) { Sample(r); yield return new WaitForSeconds(SampleSeconds); }
            var fin = r.Last;
            float area = endPour.Region * v.m_scale * v.m_scale;
            Say("POUR done: poured " + F2(r.Poured) + " m3, pool total " + F2(before) + " -> " + F2(endPour.Total) + " right after the last stroke (kept " + F2(endPour.Total - before) + " = " +
                F2((endPour.Total - before) / Mathf.Max(0.01f, r.Poured) * 100f) + " %), " + F2(fin.Total) + " after " + F2(PoolHoldAfterPour) + " s more; level " + F(levelStart) + " -> " + F(endPour.Level) +
                " -> " + F(fin.Level) + " (max " + F(r.MaxLevel) + "), basin " + endPour.Region + " cells = " + F2(area) + " m2, spill level " + F(endPour.Spill4) + "; " + r.Reloads + " reload(s), " + r.Saves + " save(s), " + r.Drops + " drop(s)");
        }

        // --- the synthetic tank ----------------------------------------------------------------------------------------------
        sealed class Variant { public string Name; public bool Inject; public bool? Shed, Overflow, Runoff; public float? Boost, Flow; }

        static IEnumerator Matrix()
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
                    if (_cancel) break;
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

        sealed class Result { public string Summary = "(no result)"; }

        static IEnumerator TankTest(int radius, float wallH, int strokes, bool inject)
        {
            var res = new Result();
            var run = TankRun(inject ? "tank-inject" : "tank-flood", radius, wallH, strokes, inject, res);
            while (run.MoveNext()) yield return run.Current;
            Say(res.Summary);
        }

        static IEnumerator TankRun(string label, int radius, float wallH, int strokes, bool inject, Result res)
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
            while (!TarPits.Ready(v) && Time.time < t0 + 15f && !_cancel) yield return null;
            if (!TarPits.Ready(v)) { res.Summary = label + ": FAILED - the pool never got its ground heights"; TarPool.DestroyForTest(v); Synth.Remove(v); yield break; }
            t0 = Time.time;
            Snap st = null;
            while (Time.time < t0 + 6f && !_cancel)
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
            while (done < strokes && !_cancel)
            {
                if (Time.time >= next)
                {
                    if (inject) Inject(v, tank, vol); else TarPits.DepositLocal(v, centre, vol, 0);
                    r.Poured += vol; done++; next = Time.time + DefaultPourInterval;
                    r.Expected = o.y + tank.Floor + r.Poured / areaIn;
                }
                Sample(r);
                yield return new WaitForSeconds(SampleSeconds);
            }
            var endPour = Sample(r);
            float kept = endPour.Total / Mathf.Max(0.01f, r.Poured), levelRise = endPour.Level - (o.y + tank.Floor);
            float tEnd = Time.time;
            while (Time.time < tEnd + HoldAfterPour && !_cancel) { Sample(r); yield return new WaitForSeconds(SampleSeconds); }
            var fin = r.Last;
            var b = TarPits.BudgetFor(v);
            res.Summary = label + ": poured " + F2(r.Poured) + " m3 -> after the last stroke " + F2(endPour.Total) + " in the pool (" + F2(kept * 100f) + " %), final " + F2(fin.Total) + " (" + F2(fin.Total / Mathf.Max(0.01f, r.Poured) * 100f) + " %); " +
                          "level rise " + F2(levelRise) + " m (expected " + F2(r.Poured / areaIn) + ", " + F2(levelRise / Mathf.Max(0.001f, r.Poured / areaIn) * 100f) + " %), final level rise " + F2(fin.Level - (o.y + tank.Floor)) + ", max level rise " + F2(r.MaxLevel - (o.y + tank.Floor)) +
                          "; final: in tank " + F2(fin.TankIn) + ", on wall cells " + F2(fin.Ring) + ", elsewhere " + F2(fin.Total - fin.TankIn - fin.Ring) + ", grid edge " + F2(fin.Border) + "; reloads " + r.Reloads + ", saves " + r.Saves + ", drops " + r.Drops +
                          "; mod budget: stayed " + F2(b.Stayed) + " edge " + F2(b.Edge) + " thin " + F2(b.Thin) + " lost " + F2(b.Lost) + " shedEdge " + F2(b.ShedEdge) + " evap " + F2(b.Evap);
            Out("TANK RESULT " + res.Summary);
            TarPool.DestroyForTest(v); Synth.Remove(v);
        }

        // water spread evenly over the tank cells: bypasses FloodPass entirely
        static void Inject(LiquidVolume v, Tank t, float volume)
        {
            int w = v.m_width, stride = w + 1, c = w / 2, cells = (2 * t.R + 1) * (2 * t.R + 1);
            float per = volume / (cells * v.m_scale * v.m_scale);
            var d = TarPits.DepthList(v);
            using (TarPits.Lock(v))
            {
                for (int y = c - t.R; y <= c + t.R; y++)
                    for (int x = c - t.R; x <= c + t.R; x++) d[y * stride + x] += per;
                TarPits.MarkChanged(v);
            }
        }

        // --- rim: where would the water escape? ---------------------------------------------------------------------------------
        static void Rim()
        {
            var p = Player.m_localPlayer.transform.position;
            Vector3 at;
            if (!Crosshair(out at)) at = p;
            var v = NearestPool(at);
            if (v == null) { Say("ptest rim: no water pool is loaded."); return; }
            int w = v.m_width, stride = w + 1, n = stride * stride;
            var hl = TarPits.HeightList(v);
            if (hl == null || hl.Count < n) { Say("ptest rim: the pool has no heights yet."); return; }
            if (_d.Length < n) { _d = new float[n]; _h = new float[n]; _seen = new bool[n]; _q = new int[n]; }
            using (TarPits.Lock(v)) { hl.CopyTo(0, _h, 0, n); }
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
                F2(s4 - _h[seed]) + " m / " + F2(s8 - _h[seed]) + " m of water. Lowest wall cell near " + P(pass4) + " (diagonal: " + P(pass8) + "). Pool " + v.transform.position.ToString("0.0") + ", wall cells remembered: " + TarPits.WallCellCount(v) +
                ", pump pool: " + TarPits.IsPumpPool(v) + (s4 >= MaxLevelProbe - 0.01f ? ". The basin is closed up to the top of the grid's height range." : ""));
        }

        // --- fields: what the game's LiquidVolume is made of -------------------------------------------------------------------------
        static void Fields()
        {
            var p = Player.m_localPlayer.transform.position;
            var v = NearestPool(p);
            if (v == null && TarPits.All.Count > 0) v = TarPits.All[0];
            Out("=== FIELDS of LiquidVolume / LiquidSurface (names are what the game has; values from " + (v != null ? v.name + " @ " + v.transform.position.ToString("0.0") : "no pool loaded") + ")");
            Dump(typeof(LiquidVolume), v);
            LiquidSurface ls = v != null ? v.GetComponentInChildren<LiquidSurface>(true) : null;
            Dump(typeof(LiquidSurface), ls);
            Say("FIELDS written to " + Path.Combine(OutDir(), ReportFile));
        }

        static void Dump(System.Type t, object inst)
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
