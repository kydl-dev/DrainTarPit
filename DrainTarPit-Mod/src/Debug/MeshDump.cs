using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using DrainTarPit.Drain;
using DrainTarPit.Logging;
using DrainTarPit.Visuals;
using HarmonyLib;
using UnityEngine;

namespace DrainTarPit.Debug
{
    /// <summary>
    /// Console `dtp meshdump [radius]` (user, 2026-10-20: "add a debugger", after five rounds of jagged pool edges). Aim at the edge of a pump pool
    /// and run it: for every grid point within `radius` points (default 6) of the crosshair it writes the ground height, the liquid depth, the wall
    /// top the wall pass raised it to (if any), the y the GAME would draw, the y the mod draws (PoolMesh) and the vertex alpha. A CSV goes to
    /// DrainTarPit.MeshDump.txt in [General] TestOutDir (or the BepInEx config folder), a character map and a summary to the log:
    ///   map: '#' wall cell, '~' deep liquid (>= 0.1 m), '.' thin liquid, ' ' dry; second map: drawn y minus the liquid level in cm (clamped).
    /// Read-only: nothing in the pool is changed.
    /// </summary>
    internal static class MeshDump
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly MethodInfo MCalcVertex = AccessTools.Method(typeof(LiquidVolume), "CalcVertex");
        const string FileName = "DrainTarPit.MeshDump.txt";

        /// Expected outcome: as many rows written as grid points in the window (the returned text says both numbers).
        internal static string Run(int radius)
        {
            radius = Mathf.Clamp(radius, 1, 24);
            var cam = Camera.main;
            if (cam == null) return "meshdump: no camera";
            RaycastHit hit;
            if (!Physics.Raycast(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out hit, 80f, ~0, QueryTriggerInteraction.Ignore))
                return "meshdump: the crosshair points at nothing within 80 m";
            LiquidVolume pool = null; float best = float.MaxValue;
            foreach (var v in TarPits.All)
            {
                if (v == null || !TarPits.IsPumpPool(v) || !TarPits.InGrid(v, TarPits.ToGrid(v, hit.point))) continue;
                float dd = (v.transform.position - hit.point).sqrMagnitude;
                if (dd < best) { best = dd; pool = v; }
            }
            if (pool == null) return "meshdump: no pump pool covers " + hit.point.ToString("0.00");
            try { return Dump(pool, hit.point, radius); }
            catch (IOException e) { return "meshdump: could not write " + FileName + " (" + e.Message + ") - is the file open in another program?"; }
            catch (System.ArgumentOutOfRangeException e) { return "meshdump: grid changed while reading (" + e.Message + ") - run it again"; }
            catch (System.Exception e) { ModLog.Warn("meshdump failed: " + e); return "meshdump failed: " + e.GetType().Name + ": " + e.Message; }
        }

        static string Dump(LiquidVolume v, Vector3 at, int radius)
        {
            int w = v.m_width, stride = w + 1, n = stride * stride;
            float s = v.m_scale, half = w * s * 0.5f;
            var o = v.transform.position;
            var g = TarPits.ToGrid(v, at);
            int cx = Mathf.RoundToInt(g.x), cy = Mathf.RoundToInt(g.y);
            float[] h, d, fix;
            using (TarPits.Hold(v))
            {
                var hl = TarPits.Heights(v); var dl = TarPits.Depths(v);
                if (hl == null || dl == null || hl.Count < n || dl.Count < n) return "meshdump: the pool's grid is not ready";
                h = hl.GetRange(0, n).ToArray(); d = dl.GetRange(0, n).ToArray();
                fix = PoolMesh.Explain(v, dl, hl);
            }
            TarPitWalls.WallMemo memo;
            var walls = TarPitWalls.Memos.TryGetValue(v, out memo) ? memo.Cells : new Dictionary<int, float>();

            float level = float.NegativeInfinity;
            for (int y = cy - radius; y <= cy + radius; y++)
                for (int x = cx - radius; x <= cx + radius; x++)
                {
                    if (x < 0 || y < 0 || x > w || y > w) continue;
                    int i = y * stride + x;
                    if (d[i] >= 0.1f && h[i] + d[i] > level) level = h[i] + d[i];
                }

            var csv = new StringBuilder();
            csv.AppendLine("# DrainTarPit meshdump " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv) + " pool " + v.name + " id " + v.GetInstanceID() +
                " origin " + o.ToString("0.00") + " grid " + w + "x" + w + " @ " + s.ToString("0.###", Inv) + " m, centre point (" + cx + "," + cy + "), level (deepest local surface, relative to origin) " +
                (float.IsNegativeInfinity(level) ? "none" : level.ToString("0.000", Inv)));
            csv.AppendLine("gx,gy,worldX,worldZ,groundRel,depth,wallTopRel,gameY,drawnY,drawnMinusLevelCm");
            var map = new StringBuilder(); var delta = new StringBuilder();
            int expected = 0, written = 0;
            for (int y = cy + radius; y >= cy - radius; y--)
            {
                map.Append("  "); delta.Append("  ");
                for (int x = cx - radius; x <= cx + radius; x++)
                {
                    if (x < 0 || y < 0 || x > w || y > w) { map.Append('?'); delta.Append("  ?? "); continue; }
                    expected++;
                    int i = y * stride + x;
                    float wallTop; bool isWall = walls.TryGetValue(i, out wallTop);
                    float gameY = GameY(v, x, y);
                    float drawn = fix != null && i < fix.Length && !float.IsNaN(fix[i]) ? fix[i] : gameY;
                    float dc = float.IsNegativeInfinity(level) ? 0f : (drawn - level) * 100f;
                    csv.Append(x).Append(',').Append(y).Append(',').Append((o.x - half + x * s).ToString("0.00", Inv)).Append(',').Append((o.z - half + y * s).ToString("0.00", Inv))
                       .Append(',').Append(h[i].ToString("0.000", Inv)).Append(',').Append(d[i].ToString("0.000", Inv)).Append(',').Append(isWall ? wallTop.ToString("0.000", Inv) : "")
                       .Append(',').Append(gameY.ToString("0.000", Inv)).Append(',').Append(drawn.ToString("0.000", Inv)).Append(',').Append(dc.ToString("0", Inv)).AppendLine();
                    written++;
                    map.Append(isWall ? '#' : d[i] >= 0.1f ? '~' : d[i] >= 0.001f ? '.' : ' ');
                    delta.Append(Mathf.Clamp(Mathf.RoundToInt(dc), -999, 999).ToString(Inv).PadLeft(5));
                }
                map.AppendLine(); delta.AppendLine();
            }
            var path = Path.Combine(PoolTestOut.OutDir(), FileName);
            File.AppendAllText(path, csv.ToString() + "\n");
            ModLog.Info("meshdump " + v.name + " around (" + cx + "," + cy + "), north = top, '#' wall, '~' deep, '.' thin, ' ' dry:\n" + map);
            ModLog.Info("meshdump drawn y minus level (cm):\n" + delta);
            return "meshdump: " + written + " of " + expected + " points written (expected " + expected + ") to " + path + (written == expected ? "" : " - ROWS MISSING");
        }

        // the game's own vertex (on the main thread the PoolMesh postfix does not touch it: its map belongs to the mesh worker thread)
        static float GameY(LiquidVolume v, int x, int y)
        {
            if (MCalcVertex == null) return float.NaN;
            var r = MCalcVertex.Invoke(v, new object[] { x, y, false });
            return r is Vector3 p ? p.y : float.NaN;
        }
    }
}
