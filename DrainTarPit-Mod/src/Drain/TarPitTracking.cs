using System.Collections.Generic;
using System.Reflection;
using Kydl.Common;
using HarmonyLib;
using UnityEngine;
using static DrainTarPit.Drain.TarPits;

namespace DrainTarPit.Drain
{
    /// <summary>
    /// Stale liquid tracking: objects that left a pool's grid without an exit event are dropped from the LiquidSurface again (see the comments).
    /// </summary>
    internal static class TarPitTracking
    {
        static readonly FieldInfo FInWater = AccessTools.Field(typeof(LiquidSurface), "m_inWater");

        /// true when this volume's LiquidSurface(s) currently track `who` (a Character) as being inside it
        public static bool Tracks(LiquidVolume v, object who)
        {
            if (v == null || FInWater == null) return false;
            foreach (var ls in v.GetComponentsInChildren<LiquidSurface>(true))
            {
                var list = FInWater.GetValue(ls) as System.Collections.IEnumerable;
                if (list == null) continue;
                foreach (var o in list) if (ReferenceEquals(o, who)) return true;
            }
            return false;
        }

        // --- stale liquid tracking ------------------------------------------------------------------------------------------
        // A LiquidSurface keeps every character / floating item it saw enter in m_inWater and drops it only from OnTriggerExit. A tar volume rebuilds (and
        // empties) its trigger mesh while it is pumped, and Unity sends no exit for a collider whose shape was replaced: the entry stays, the player walks
        // 18-20 m away, UpdateFloaters keeps running for him every physics step and his liquid counter never drops back to 0.
        // Entries outside the grid (+ margin) are removed here with the game's own exit (LiquidSurface.OnTriggerExit, read from its IL):
        // Decrement -> if the count is 0, SetLiquidLevel(-10000) -> remove from m_inWater.
        static readonly FieldInfo FLiquid = AccessTools.Field(typeof(LiquidSurface), "m_liquid");
        const float StaleMargin = 1.5f;      // m outside the grid before a tracked object counts as gone

        static bool NearGrid(LiquidVolume v, Vector3 p, float margin)
        {
            var g = ToGrid(v, p); float m = margin / v.m_scale;
            return g.x >= -m && g.y >= -m && g.x <= v.m_width + m && g.y <= v.m_width + m;
        }

        // --- the game's liquid counter (read from the IL of the AssetRipper copy, 2026-10-20) ------------------------------------
        // Character / Floating / Fish keep m_liquids[type], a plain int. LiquidSurface.OnTriggerEnter increments it on EVERY enter (and adds the object
        // to m_inWater once); OnTriggerExit and OnDestroy decrement it and reset the level to -10000 only when the result is EXACTLY 0. One exit too
        // many leaves the counter at -1, and from then on no exit ever hits 0: the level of the last liquid stays on the character (T-16b, 'swimming
        // in invisible water'; T-17R: the player's Tar counter stood at -2). Two sources of unbalanced calls are closed here:
        //  - Unity re-reports an enter after the trigger mesh is rebuilt (no exit in between): skipped when that collider is already inside;
        //  - PurgeStale / ReleaseAll replay the exit, and Unity may still send the real exit later: skipped when that collider is no longer inside.
        // So the counter = the number of this object's colliders inside the liquid triggers, as the game intends.
        static readonly Dictionary<LiquidSurface, Dictionary<IWaterInteractable, HashSet<Collider>>> Inside =
            new Dictionary<LiquidSurface, Dictionary<IWaterInteractable, HashSet<Collider>>>();

        static IWaterInteractable Of(Collider col)
        {
            var rb = col != null ? col.attachedRigidbody : null;
            return rb != null ? rb.GetComponent<IWaterInteractable>() : null;
        }

        static bool Ours(LiquidSurface ls) { return ls != null && FLiquid != null && IsLiquid(FLiquid.GetValue(ls) as LiquidVolume); }

        /// OnTriggerEnter prefix: false = skip the game's enter (this collider is already counted).
        internal static bool AllowEnter(LiquidSurface ls, Collider col)
        {
            if (!Ours(ls)) return true;
            var w = Of(col);
            if (w == null) return true;
            Dictionary<IWaterInteractable, HashSet<Collider>> map;
            if (!Inside.TryGetValue(ls, out map)) Inside[ls] = map = new Dictionary<IWaterInteractable, HashSet<Collider>>();
            HashSet<Collider> cols;
            if (!map.TryGetValue(w, out cols)) map[w] = cols = new HashSet<Collider>();
            return cols.Add(col);
        }

        /// OnTriggerExit prefix: false = skip the game's exit (this collider was never counted or was already released).
        internal static bool AllowExit(LiquidSurface ls, Collider col)
        {
            if (!Ours(ls)) return true;
            var w = Of(col);
            if (w == null) return true;
            Dictionary<IWaterInteractable, HashSet<Collider>> map; HashSet<Collider> cols;
            if (!Inside.TryGetValue(ls, out map) || !map.TryGetValue(w, out cols) || !cols.Remove(col)) return false;
            if (cols.Count == 0) map.Remove(w);
            return true;
        }

        /// The game's exit for `w` once per collider it has inside `ls`; the counter is brought back to 0 if it was already below (an earlier
        /// session's imbalance). Returns the counter after the exit.
        static int Release(LiquidSurface ls, IWaterInteractable w, LiquidType type)
        {
            int n = 1;
            Dictionary<IWaterInteractable, HashSet<Collider>> map; HashSet<Collider> cols;
            if (Inside.TryGetValue(ls, out map) && map.TryGetValue(w, out cols)) { n = Mathf.Max(1, cols.Count); map.Remove(w); }
            int left = 0;
            for (int i = 0; i < n; i++) left = w.Decrement(type);
            while (left < 0) left = w.Increment(type);
            if (left == 0) w.SetLiquidLevel(-10000f, type, ls);
            return left;
        }

        /// LiquidSurface.OnDestroy prefix: the bookkeeping of a destroyed surface goes with it (the game's own OnDestroy does the exits).
        internal static void Forget(LiquidSurface ls) { if (ls != null) Inside.Remove(ls); }

        // --- characters deep under the surface (user, 2026-10-20: not swimming 5 m deep in the 6 m pump pool) --------------------------------
        // The game's trigger is NOT the volume of the liquid: PrebuildMesh builds the MeshCollider from CalcVertex(..., true) over every 2nd grid
        // point, i.e. a SHEET at the surface (m_physicsOffset -1 m below it), read from the IL of the AssetRipper copy. OnTriggerEnter fires only
        // while the character's capsule touches that sheet; a character standing on the floor of a pool deeper than about 3 m never touches it
        // (or leaves it when he sinks): not counted, not tracked, no water level -> he walks on the floor. Vanilla tar pits are shallow, so the game
        // never needed more. For pump pools a character inside the grid whose feet are under the surface is entered here with the game's own
        // enter (Increment + m_inWater), counted on his capsule collider like a real enter, so the later real enter / exit stay balanced.
        static readonly FieldInfo FCharCollider = AccessTools.Field(typeof(Character), "m_collider");

        internal static void AdoptSubmerged(LiquidSurface ls)
        {
            if (!Ours(ls) || FInWater == null) return;
            var v = FLiquid.GetValue(ls) as LiquidVolume;
            if (!IsPumpPool(v)) return;
            var list = FInWater.GetValue(ls) as List<IWaterInteractable>;
            if (list == null) return;
            foreach (var c in Character.GetAllCharacters())
            {
                if (c == null) continue;
                var p = c.transform.position;
                if (!NearGrid(v, p, 0f) || list.Contains(c)) continue;
                if (v.GetSurface(p) <= p.y) continue;                                     // dry cell or above the surface
                var col = (FCharCollider != null ? FCharCollider.GetValue(c) as Collider : null) ?? c.GetComponent<Collider>();
                if (col == null || !AllowEnter(ls, col)) continue;
                ((IWaterInteractable)c).Increment(v.m_liquidType);
                list.Add(c);
                if (ModSettings.DebugLog.Value) ModLog.Info("Pit " + v.name + ": " + c.name + " is under the surface (below the trigger sheet) - entered");
            }
        }

        internal static void PurgeStale(LiquidSurface ls)
        {
            if (ls == null || FInWater == null || FLiquid == null) return;
            var list = FInWater.GetValue(ls) as List<IWaterInteractable>;
            if (list == null || list.Count == 0) return;
            var v = FLiquid.GetValue(ls) as LiquidVolume;
            if (!IsLiquid(v)) return;
            int purged = 0;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var w = list[i];
                var c = w as Component;
                if (c == null) continue;                                     // null / destroyed: the game's own loop drops it
                if (NearGrid(v, c.transform.position, StaleMargin)) continue;
                Release(ls, w, v.m_liquidType);
                list.RemoveAt(i);
                purged++;
            }
            if (purged > 0 && ModSettings.DebugLog.Value) ModLog.Info("Pit " + v.name + ": dropped " + purged + " stale tracked object(s) outside its grid");
        }

        /// Replays the game's exit (Decrement per counted collider -> at 0 SetLiquidLevel(-10000)) for EVERY object this volume's surfaces track, then empties their lists.
        /// Called before a pool is destroyed while someone may stand in it (test pools, TarPool.DestroyForTest): after the T-16 suite the player swam in
        /// the invisible water of the last, destroyed test pool (m_waterLevel stayed on its surface). Done before the destroy, so the game's own teardown
        /// finds empty lists and nothing is counted twice. Returns one line per released object (for the test report).
        internal static List<string> ReleaseAll(LiquidVolume v)
        {
            var lines = new List<string>();
            if (v == null) return lines;
            foreach (var ls in v.GetComponentsInChildren<LiquidSurface>(true)) ReleaseSurface(ls, lines);
            return lines;
        }

        /// The exits of every object `ls` tracks (per counted collider), then its list and bookkeeping are emptied. Also runs as the prefix of the
        /// game's LiquidSurface.OnDestroy, which would decrement only once per object.
        internal static void ReleaseSurface(LiquidSurface ls, List<string> lines)
        {
            if (ls == null || FInWater == null || !Ours(ls)) return;
            var v = FLiquid.GetValue(ls) as LiquidVolume;
            var list = FInWater.GetValue(ls) as List<IWaterInteractable>;
            if (list != null)
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var w = list[i];
                    var c = w as Component;
                    if (c == null) continue;
                    int left = Release(ls, w, v.m_liquidType);
                    if (lines != null) lines.Add(c.name + " released (" + v.m_liquidType + " counter now " + left + (left == 0 ? ", level reset" : ", NOT reset: another volume still counts") + ")");
                }
                list.Clear();
            }
            Forget(ls);
        }

        static readonly FieldInfo FWaterLevel = AccessTools.Field(typeof(Character), "m_waterLevel");
        static readonly FieldInfo FTarLevel = AccessTools.Field(typeof(Character), "m_tarLevel");
        static readonly FieldInfo FLiquids = AccessTools.Field(typeof(Character), "m_liquids");     // int[] per LiquidType: the game's enter / exit counter

        static string Counters(Character p)
        {
            var a = FLiquids != null ? FLiquids.GetValue(p) as int[] : null;
            if (a == null) return "";
            int w = (int)LiquidType.Water, t = (int)LiquidType.Tar;
            return " counters water " + (w < a.Length ? a[w].ToString() : "?") + " tar " + (t < a.Length ? a[t].ToString() : "?") + " (must be 0 on dry ground)";
        }

        /// The local player's water / tar level and feet height (test report: is a destroyed pool still felt?).
        internal static string PlayerLiquidLine()
        {
            var p = Player.m_localPlayer;
            if (p == null) return "no local player";
            string Lv(FieldInfo f) { var o = f != null ? f.GetValue(p) : null; return o is float x ? x.ToString("0.00") : "n/a"; }
            return "player waterLevel " + Lv(FWaterLevel) + " tarLevel " + Lv(FTarLevel) + " feet y " + p.transform.position.y.ToString("0.00") + (p.IsSwimming() ? " SWIMMING" : "") + Counters(p);
        }
    }
}
