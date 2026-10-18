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
    /// (TarPits.ApplyWalls), so the quads between the water and the first wall cells climb straight up to the top of the wall: a translucent sheet
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
            DrainTarPit.Drain.TarPits.MarkMeshDirty(LiquidType.Water);       // the pools rebuild their mesh with the new setting
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
                a[i] = d[i] >= 0.001f ? h[i] + (d[i] > 0.1f ? d[i] : 0.1f) : inf;      // the same surface height CalcVertex gives a wet point

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
            for (int i = 0; i < n; i++)
            {
                if (d[i] >= 0.001f) { _fix[i] = float.NaN; continue; }               // wet: the game's vertex is right
                float win = a[i];
                _fix[i] = win < inf ? (h[i] < win ? h[i] : win) : h[i] - 1f;
            }
        }

        [HarmonyPatch(typeof(LiquidVolume), "PrebuildMesh")]
        static class Patch_PrebuildMesh
        {
            static void Prefix(LiquidVolume __instance, List<float> ___m_depths, List<float> ___m_heights)
            {
                _fixFor = null;
                if (!_enabled || __instance.m_liquidType != LiquidType.Water) return;
                try { _fixFor = __instance; Build(__instance, ___m_depths, ___m_heights); }
                catch (System.Exception) { _fixFor = null; }                 // never log from the worker thread; the game's own mesh is used
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
