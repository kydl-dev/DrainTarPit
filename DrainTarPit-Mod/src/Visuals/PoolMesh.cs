using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DrainTarPit.Visuals
{
    /// <summary>
    /// Why a water pool next to a wall showed a "sheet" that hangs on the wall (read from the IL of assembly_valheim.dll, LiquidVolume):
    ///   - the render mesh covers the WHOLE grid (PrebuildMesh: one vertex per grid point, every quad is drawn),
    ///   - a wet grid point (depth > 0) gets y = ground + max(depth, 0.1 m), a dry one gets y = ground height (CalcVertex),
    ///   - the vertex colour alpha is 0 for dry points and 1 for wet ones.
    /// For a tar pit on a gentle slope that is fine: the dry points sit on the ground. But the walls of a pool are cells whose ground height is the TOP of the wall
    /// (TarPitWalls.ApplyWalls), so the quads between the water and the first wall cells climb straight up to the top of the wall: a translucent sheet
    /// (with spiky triangles, because each quad is split along one diagonal) hangs on the wall, and the ocean material does not hide it with the vertex alpha.
    ///
    /// Fix, for volumes of type Water only (every one of them is a pool of this mod): while the worker thread builds the mesh a dry grid point
    ///   - within [Visuals] PoolMeshReach cells of a wet one is put at min(ground, lowest wet surface in that window) = the water plane simply continues
    ///     under the wall / the bank (hidden by it) instead of climbing it,
    ///   - further away is sunk 1 m under the ground (hidden by the terrain, like the game's own physics mesh does).
    /// It only ever LOWERS a vertex, so nothing can poke out of the ground. The normals are computed by the game after the vertices, so they follow.
    /// Applies live (the pools are told to rebuild their mesh when the setting changes). NOT tested in game.
    /// </summary>
    internal static class PoolMesh
    {
        static volatile bool _enabled = true;
        // Tar pump pools get the same mesh fix as water (user, 2026-10-20: tar 'climbed' the marble walls of a tar pool). Vanilla tar pits do not.
        // Filled on the main thread (MarkPumpPool from LiquidVolume.Update); read by the mesh worker thread, hence a concurrent map.
        static readonly System.Collections.Concurrent.ConcurrentDictionary<LiquidVolume, byte> PumpPools = new System.Collections.Concurrent.ConcurrentDictionary<LiquidVolume, byte>();
        internal static void MarkPumpPool(LiquidVolume v) { if (v != null && v.m_liquidType == LiquidType.Tar) PumpPools.TryAdd(v, 0); }
        internal static void Forget(LiquidVolume v) { byte b; if (v != null) PumpPools.TryRemove(v, out b); }
        // 2026-10-20 (user screenshots of a NEW, empty pool built inside an old runoff pool's grid: a thin sheet on the outer wall faces): a film
        // thinner than this is drawn like dry ground (hidden). The game draws every wet point at least 0.1 m thick, so a few millimetres of film
        // on a wall top or a slope looked like a sheet of water; and a film point on a wall top made the window below treat the ground outside the
        // wall as "below the water plane" (sunk), which hung a curtain from the wall top down its outer face. Only liquid at least this deep is
        // drawn as water and counts for the water plane.
        const float DrawDepth = 0.05f;
        const int EdgeReach = 3;            // grid points the level surface runs on into a wall / bank (hidden inside it)
        static volatile int _reach = 6;

        // the worker thread of a pool builds its mesh in PrebuildMesh: the map is made in the prefix and read by the CalcVertex postfix of the same call
        [System.ThreadStatic] static float[] _fix;            // per grid point: the local y a dry vertex gets; NaN = keep the game's value
        [System.ThreadStatic] static LiquidVolume _fixFor;
        [System.ThreadStatic] static float[] _bufA;
        [System.ThreadStatic] static float[] _bufB;

        public static void Refresh()
        {
            if (ModSettings.PoolMeshFix != null) _enabled = ModSettings.PoolMeshFix.Value;
            if (ModSettings.PoolMeshReach != null) _reach = Mathf.Clamp(ModSettings.PoolMeshReach.Value, 1, 24);
            DrainTarPit.Drain.TarPitDebug.MarkMeshDirty(LiquidType.Water);       // the pools rebuild their mesh with the new setting
        }

        // min over a (2R+1) x (2R+1) window of the wet surface heights, done as two 1-D passes
        static void Build(LiquidVolume v, List<float> d, List<float> h)
        {
            int w = v.m_width, stride = w + 1, n = stride * stride;
            if (d == null || h == null || d.Count < n || h.Count < n) { _fixFor = null; return; }
            if (_fix == null || _fix.Length < n) { _fix = new float[n]; _bufA = new float[n]; _bufB = new float[n]; }
            int r = _reach;
            float[] a = _bufA, b = _bufB;
            const float inf = float.PositiveInfinity;

            for (int i = 0; i < n; i++)
                a[i] = d[i] >= DrawDepth ? h[i] + (d[i] > 0.1f ? d[i] : 0.1f) : inf;      // the same surface height CalcVertex gives a wet point

            for (int y = 0; y < stride; y++)
            {
                int row = y * stride;
                for (int x = 0; x < stride; x++)
                {
                    int x0 = x - r < 0 ? 0 : x - r, x1 = x + r > w ? w : x + r;
                    float m = inf;
                    for (int xx = x0; xx <= x1; xx++) { float s = a[row + xx]; if (s < m) m = s; }
                    b[row + x] = m;
                }
            }
            for (int y = 0; y < stride; y++)
            {
                int y0 = y - r < 0 ? 0 : y - r, y1 = y + r > w ? w : y + r;
                for (int x = 0; x < stride; x++)
                {
                    float m = inf;
                    for (int yy = y0; yy <= y1; yy++) { float s = b[yy * stride + x]; if (s < m) m = s; }
                    a[y * stride + x] = m;
                }
            }
            // 2026-10-20 (user screenshots: a translucent curtain on the OUTSIDE faces of the pool walls while no water is outside): a dry point
            // whose ground is LOWER than the water plane near it lies outside a barrier (the water would stand there if it could reach it). It used
            // to get y = its ground, so the quad from the wall top (water plane) down to it hung on the outer wall face. Now such a point is sunk
            // 1 m under its ground, and a dry wall / bank point that touches one of them is sunk with it, so the step down happens INSIDE the
            // wall body (a wall is several cells thick) instead of in front of its outer face. Real water outside (rim switch off, overflow) is wet
            // and is drawn by the game as before.
            // 2026-10-20 (user: one stroke, 0.8 m of water inside, a sheet up to 5 m high on the OUTER face of the wall): a dry point away from the
            // water used to go 1 m under ITS ground - but under a wall "its ground" is the wall top (the wall pass raises those heights), so the
            // point sat 4 m up inside the wall and the quad to the next point outside hung down the outer face. Every dry point that is not
            // directly under a bank / wall next to the water now goes 1 m under the LOWEST ground of the whole grid: all of them are under the
            // terrain everywhere, and the water ends at the inner face of the wall (its last quad dips into the wall body).
            float hidden = inf;
            for (int i = 0; i < n; i++) if (h[i] < hidden) hidden = h[i];
            hidden -= 1f;
            for (int i = 0; i < n; i++)
            {
                if (d[i] >= DrawDepth) { _fix[i] = float.NaN; continue; }            // water: the game's vertex is right
                float win = a[i];
                _fix[i] = win < inf && h[i] >= win ? win : hidden;
                b[i] = win < inf && h[i] < win ? 1f : 0f;                            // 1 = dry point below the water plane (outside a barrier)
            }
            for (int y = 0; y < stride; y++)
                for (int x = 0; x < stride; x++)
                {
                    int i = y * stride + x;
                    if (d[i] >= DrawDepth || b[i] > 0f || float.IsNaN(_fix[i])) continue;
                    float low = float.PositiveInfinity;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            if (xx < 0 || yy < 0 || xx > w || yy > w) continue;
                            int j = yy * stride + xx;
                            if (b[j] > 0f && _fix[j] < low) low = _fix[j];
                        }
                    if (low < _fix[i]) _fix[i] = low;
                }
            // User, 2026-10-20 (tar pool from above: golden saw-teeth and flicker along the walls): a dry point next to the liquid took the LOWEST
            // drawn surface in its window, so the last quads sloped down from the real surface (ripples, the higher surface at the pour point) and
            // caught the sun; the window minimum also changed every rebuild = flicker. A dry point that touches drawn liquid and is not lower than it
            // (a wall / bank point) now takes the highest surface of the liquid points touching it: the surface runs on LEVEL into the wall (one
            // cell, hidden inside it) and ends there - no slope, no visible edge.
            for (int y = 0; y < stride; y++)
                for (int x = 0; x < stride; x++)
                {
                    int i = y * stride + x;
                    if (d[i] >= DrawDepth) continue;
                    // 2026-10-20 second look (still golden teeth): the wall pass marks grid points up to 0.2 m IN FRONT of a wall face as wall
                    // (sphere cast), so one cell of level surface ended in front of the face and the slope behind it was visible. The level now
                    // runs EdgeReach cells (0.75 m at 0.25 m cells) into the wall. A point that touches lower dry ground (outside the wall) is
                    // left alone, so nothing is drawn level in front of an OUTER face.
                    float top = float.NegativeInfinity;
                    for (int dy = -EdgeReach; dy <= EdgeReach; dy++)
                        for (int dx = -EdgeReach; dx <= EdgeReach; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            if (xx < 0 || yy < 0 || xx > w || yy > w) continue;
                            int j = yy * stride + xx;
                            if (d[j] < DrawDepth) continue;
                            float sj = h[j] + (d[j] > 0.1f ? d[j] : 0.1f);
                            if (sj > top) top = sj;
                        }
                    if (top == float.NegativeInfinity || h[i] < top) continue;
                    bool outerFace = false;
                    for (int dy = -1; dy <= 1 && !outerFace; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            if (xx < 0 || yy < 0 || xx > w || yy > w) continue;
                            int j = yy * stride + xx;
                            if (d[j] < DrawDepth && h[j] < top) { outerFace = true; break; }
                        }
                    if (!outerFace) _fix[i] = top;
                }
        }

        [HarmonyPatch(typeof(LiquidVolume), "PrebuildMesh")]
        static class Patch_PrebuildMesh
        {
            static void Prefix(LiquidVolume __instance, List<float> ___m_depths, List<float> ___m_heights)
            {
                _fixFor = null;
                if (!_enabled || (__instance.m_liquidType != LiquidType.Water && !PumpPools.ContainsKey(__instance))) return;
                try { _fixFor = __instance; Build(__instance, ___m_depths, ___m_heights); }
                catch (System.Exception) { _fixFor = null; }                 // never log from the worker thread; the game's own mesh is used
            }

            // User, 2026-10-20 (tar pool seen from above: a see-through zig-zag band along the walls): the game gives a dry grid point vertex alpha 0
            // and a wet one alpha 1, and the tar shader fades the quads between them - one cell of half-transparent tar along every edge. A dry point
            // next to drawn liquid (it is the one hidden in / under the wall by the fix above) now gets alpha 1 too: the liquid is opaque up to the wall.
            static void Postfix(LiquidVolume __instance, List<float> ___m_depths, List<Color32> ___m_tempColors)
            {
                if (!ReferenceEquals(_fixFor, __instance) || ___m_depths == null || ___m_tempColors == null) return;
                try
                {
                    int w = __instance.m_width, stride = w + 1, n = stride * stride;
                    var d = ___m_depths; var c = ___m_tempColors;
                    if (d.Count < n || c.Count < n) return;
                    for (int y = 0; y < stride; y++)
                        for (int x = 0; x < stride; x++)
                        {
                            int i = y * stride + x;
                            if (d[i] >= DrawDepth || c[i].a == 255) continue;
                            bool near = false;
                            for (int dy = -1; dy <= 1 && !near; dy++)
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    int xx = x + dx, yy = y + dy;
                                    if (xx < 0 || yy < 0 || xx > w || yy > w) continue;
                                    if (d[yy * stride + xx] >= DrawDepth) { near = true; break; }
                                }
                            if (near) { var col = c[i]; col.a = 255; c[i] = col; }
                        }
                }
                catch (System.Exception) { }                                    // worker thread: never log; the game's colours stay
            }

            static void Finalizer() { _fixFor = null; }
        }

        // CalcVertex(x, y, physics): physics == true is the collider mesh, left alone
        [HarmonyPatch(typeof(LiquidVolume), "CalcVertex")]
        static class Patch_CalcVertex
        {
            static void Postfix(LiquidVolume __instance, int __0, int __1, bool __2, ref Vector3 __result)
            {
                if (__2 || !ReferenceEquals(_fixFor, __instance)) return;
                var f = _fix;
                int i = __1 * (__instance.m_width + 1) + __0;
                if (f == null || i < 0 || i >= f.Length) return;
                float y = f[i];
                if (!float.IsNaN(y)) __result.y = y;
            }
        }
    }
}
