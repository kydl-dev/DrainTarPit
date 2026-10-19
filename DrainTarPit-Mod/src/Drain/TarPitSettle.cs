using System.Collections.Generic;
using System.Reflection;
using DrainTarPit.Logging;
using HarmonyLib;
using UnityEngine;
using static DrainTarPit.Drain.TarPits;
using static DrainTarPit.Drain.TarPitStats;

namespace DrainTarPit.Drain
{
    /// <summary>
    /// Things that settle around a pool: items woken when liquid goes away, floating items with a stale level, thin films that run off.
    /// </summary>
    internal static class TarPitSettle
    {
        /// Items (Tar, meat, ...) that lie in the pit are NOT touched by the pump; they stay where they are. They only get
        /// woken up so they settle on the ground as the liquid under them goes away instead of hanging on a sleeping body.
        internal static void WakeItems(Vector3 center, float radius)
        {
            try
            {
                foreach (var col in Physics.OverlapSphere(center, radius + 1.5f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (col == null) continue;
                    var item = col.GetComponentInParent<ItemDrop>();
                    var rb = item != null ? item.GetComponent<Rigidbody>() : null;
                    if (rb != null && !rb.isKinematic) rb.WakeUp();
                }
            }
            catch (System.Exception e) { ModLog.VerboseWarn("WakeItems: " + e.Message); }
        }

        // --- floating items --------------------------------------------------------------------------------------------
        // Tar items (and anything else with a Floating component) that lie on the pit are held up by Floating.CustomFixedUpdate, which pushes the body up
        // towards the liquid level it was last given (m_tarLevel). When the liquid goes away (drained, or the pit flows away from under the item) that
        // level can stay stale - and the body is asleep, so it keeps hanging in the air instead of dropping to the ground. WakeItems only wakes items near the
        // strainer, but the whole pit flows. So every second every floating item that is inside a tar volume's grid and hangs above its surface is given the
        // real (low) level and woken.
        static readonly FieldInfo FFloatTar = AccessTools.Field(typeof(Floating), "m_tarLevel");
        static readonly FieldInfo FFloatWater = AccessTools.Field(typeof(Floating), "m_waterLevel");
        internal static readonly Dictionary<LiquidVolume, float> SettleTimers = new();

        internal static void SettleFloaters(LiquidVolume v)
        {
            try
            {
                foreach (var mu in Floating.Instances)
                {
                    var f = mu as Floating;      // Instances is a list of IMonoUpdater in current game versions
                    if (f == null) continue;
                    var pos = f.transform.position;
                    if (!InGrid(v, ToGrid(v, pos))) continue;
                    var rb = f.GetComponent<Rigidbody>();
                    if (rb == null || rb.isKinematic) continue;
                    float surf = v.GetSurface(pos);
                    if (pos.y <= surf + 0.3f) continue;                                    // in / on the liquid: Floating handles it
                    var lvl = v.m_liquidType == LiquidType.Water ? FFloatWater : FFloatTar;      // the level the item was last given by this kind of liquid
                    bool stale = lvl != null && (float)lvl.GetValue(f) > surf + 0.02f;
                    if (!stale && !(rb.IsSleeping() && !Physics.Raycast(pos, Vector3.down, 0.35f, ~0, QueryTriggerInteraction.Ignore))) continue;   // lying on something: leave it alone
                    if (stale) lvl.SetValue(f, surf);
                    rb.WakeUp();
                }
            }
            catch (System.Exception e) { ModLog.VerboseWarn("SettleFloaters: " + e.Message); }
        }

        // --- thin films run off fast --------------------------------------------------------------------------------------
        // The game's simulation spreads liquid thinly and slowly: what runs over the rim of a pool (or is pushed off a wall cell by ApplyWalls) lingered as a
        // film on the outside of the walls and on the ground for minutes. Every ShedInterval the owner goes through the cells of its pump pools that hold less than
        // [Drain] PoolFilmDepth of liquid: a film with lower liquid / ground next to it moves ALL its liquid to the lowest neighbour (about one cell per pass, so
        // it visibly slides down), a film that reaches the border of the grid leaves, and a film that lies flat with nothing deeper next to it evaporates.
        // The shore of a pool (a thin cell next to a deeper one at the same level) has no lower neighbour and stays.
        internal static readonly Dictionary<LiquidVolume, float> ShedTimers = new();
        static bool[] _filmGot = new bool[0];
        internal const float ShedInterval = 0.15f;       // s between passes
        const float FilmEvaporation = 0.05f;    // m/s for a film that lies flat
        static readonly int[] Nx = { 1, -1, 0, 0, 1, 1, -1, -1 }, Ny = { 0, 0, 1, -1, 1, -1, 1, -1 };

        internal static void ShedFilms(LiquidVolume v, float dt)
        {
            if (!ModSettings.PoolShedFilm.Value || !IsLiquid(v) || !IsPumpPool(v) || !HeightsReady(v)) return;
            var nv = Nview(v);
            if (nv == null || !nv.IsValid() || !nv.IsOwner()) return;
            var d = Depths(v); var h = Heights(v);
            int w = v.m_width, stride = w + 1, n = stride * stride;
            if (d == null || h == null || d.Count < n || h.Count < n) return;
            float film = ModSettings.PoolFilmDepth.Value;
            if (_filmGot.Length < n) _filmGot = new bool[n];
            bool changed = false;
            float edgeSum = 0f, evapSum = 0f;       // depth sums (cell units) that left the pool in this pass, for the editor's budget
            using (Hold(v))
            {
                System.Array.Clear(_filmGot, 0, n);
                for (int y = 0; y <= w; y++)
                    for (int x = 0; x <= w; x++)
                    {
                        int i = y * stride + x;
                        float di = d[i];
                        if (di <= 0f || di >= film || _filmGot[i]) continue;
                        if (x <= 1 || y <= 1 || x >= w - 1 || y >= w - 1) { edgeSum += di; d[i] = 0f; changed = true; continue; }   // runs off the grid
                        float bs = h[i] + di - 0.004f; int best = -1; bool deeper = false;
                        for (int k = 0; k < 8; k++)
                        {
                            int j = (y + Ny[k]) * stride + (x + Nx[k]);
                            if (d[j] >= film) deeper = true;
                            float sj = h[j] + d[j];
                            if (sj < bs) { bs = sj; best = j; }
                        }
                        if (best >= 0) { d[best] += di; d[i] = 0f; _filmGot[best] = true; changed = true; }
                        else if (!deeper && ModSettings.PoolFilmEvaporates.Value) { float ev = Mathf.Min(di, FilmEvaporation * dt); evapSum += ev; d[i] = di - ev; changed = true; }     // [Drain] PoolFilmEvaporates (default off) - it was bound but never read, so thin pools always evaporated
                    }
                if (changed) FDirty.SetValue(v, true);
            }
            if (edgeSum > 0f || evapSum > 0f)
            {
                var bg = BudgetOf(v); float c2 = v.m_scale * v.m_scale;
                bg.ShedEdge += edgeSum * c2; bg.Evap += evapSum * c2;
            }
            if (changed) FNeedsSaving.SetValue(v, true);
        }
    }
}
