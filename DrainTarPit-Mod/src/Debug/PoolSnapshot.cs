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
using static DrainTarPit.Debug.PoolTankTests;

namespace DrainTarPit.Debug
{
    /// Reads a pool's depth / height lists into a snapshot (volume, level, where the water is) and finds the level at which its basin overflows (PoolTests).
    internal static class PoolSnapshot
    {
        internal const float MaxLevelProbe = 12.5f;          // top of the height range a pool grid can hold (m_maxDepth 12 + margin)

        // --- snapshot of a pool ------------------------------------------------------------------------------------------
        internal sealed class Snap
        {
            public float Total, Level, Border, Spread, Spill4 = float.NaN, Spill8 = float.NaN;
            public float Inside, Outside, TankIn, Ring, CentreH, RingH, CellScale;
            public int Wet, Region, Walls, Width, DepthCount = -1, HeightCount = -1;
            public bool Valid;
            /// Step 4: the measurement state - VALID-WET, VALID-EMPTY (a real zero), or INVALID (never a volume result).
            public string State { get { return !Valid ? "INVALID" : Wet > 0 ? "VALID-WET" : "VALID-EMPTY"; } }
            public string InvalidReason = "not sampled", Pass = "-";
        }

        internal static float[] _d = new float[0], _h = new float[0];
        internal static bool[] _seen = new bool[0];
        internal static int[] _q = new int[0];
        internal static readonly int[] Ox = { 1, -1, 0, 0, 1, 1, -1, -1 }, Oy = { 0, 0, 1, -1, 1, -1, 1, -1 };

        internal static Snap Take(LiquidVolume v, Tank tank)
        {
            var s = new Snap();
            if (v == null) { s.InvalidReason = "pool object is null or destroyed"; return s; }
            if (!TarPits.All.Contains(v)) { s.InvalidReason = "pool is not in the registry any more (stale / unloaded / replaced)"; return s; }
            int w = v.m_width;
            s.Width = w; s.CellScale = v.m_scale;
            try
            {
                if (!TarPitDebug.Ready(v)) { s.InvalidReason = "game reports height grid not ready"; return s; }
            }
            catch (System.Exception e) { s.InvalidReason = "could not read height-ready flag: " + e.GetType().Name; return s; }
            if (w <= 0 || w > 512 || v.m_scale <= 0f || float.IsNaN(v.m_scale) || float.IsInfinity(v.m_scale))
            {
                s.InvalidReason = "invalid dimensions or cell scale (width=" + w + ", scale=" + v.m_scale + "; allowed width 1..512)";
                return s;
            }
            long nLong = ((long)w + 1L) * ((long)w + 1L);
            if (nLong > int.MaxValue) { s.InvalidReason = "grid is too large"; return s; }
            int stride = w + 1, n = (int)nLong;
            if (_d.Length < n) { _d = new float[n]; _h = new float[n]; _seen = new bool[n]; _q = new int[n]; }
            try
            {
                using (TarPitDebug.Lock(v))
                {
                    var dl = TarPitDebug.DepthList(v); var hl = TarPitDebug.HeightList(v);
                    s.DepthCount = dl != null ? dl.Count : -1;
                    s.HeightCount = hl != null ? hl.Count : -1;
                    if (dl == null || hl == null || dl.Count < n || hl.Count < n)
                    {
                        s.InvalidReason = "depth/height grids are null or shorter than expected " + n + " cells (depths=" + s.DepthCount + ", heights=" + s.HeightCount + ")";
                        return s;
                    }
                    dl.CopyTo(0, _d, 0, n); hl.CopyTo(0, _h, 0, n);
                }
            }
            catch (System.Exception e)
            {
                s.InvalidReason = "failed to read grid: " + e.GetType().Name + ": " + e.Message;
                return s;
            }
            s.Valid = true;
            s.InvalidReason = "";
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
            s.Walls = TarPitWalls.WallCellCount(v);
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
        internal static float SpillLevel(int seed, int w, int stride, bool diag, out int pass)
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
        internal static bool Escapes(int seed, int w, int stride, float level, bool diag, int[] keep)
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
    }
}
