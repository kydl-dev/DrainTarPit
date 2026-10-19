using System.Collections.Generic;
using System.Reflection;
using DrainTarPit.Logging;
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
                if (w.Decrement(v.m_liquidType) <= 0) w.SetLiquidLevel(-10000f, v.m_liquidType, ls);
                list.RemoveAt(i);
                purged++;
            }
            if (purged > 0 && ModSettings.DebugLog.Value) ModLog.Info("Pit " + v.name + ": dropped " + purged + " stale tracked object(s) outside its grid");
        }

        /// Replays the game's exit (Decrement -> at 0 SetLiquidLevel(-10000)) for EVERY object this volume's surfaces track, then empties their lists.
        /// Called before a pool is destroyed while someone may stand in it (test pools, TarPool.DestroyForTest): after the T-16 suite the player swam in
        /// the invisible water of the last, destroyed test pool (m_waterLevel stayed on its surface). Done before the destroy, so the game's own teardown
        /// finds empty lists and nothing is counted twice. Returns one line per released object (for the test report).
        internal static List<string> ReleaseAll(LiquidVolume v)
        {
            var lines = new List<string>();
            if (v == null || FInWater == null) return lines;
            foreach (var ls in v.GetComponentsInChildren<LiquidSurface>(true))
            {
                var list = FInWater.GetValue(ls) as List<IWaterInteractable>;
                if (list == null || list.Count == 0) continue;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var w = list[i];
                    var c = w as Component;
                    if (c == null) continue;
                    int left = w.Decrement(v.m_liquidType);
                    if (left <= 0) w.SetLiquidLevel(-10000f, v.m_liquidType, ls);
                    lines.Add(c.name + " released (" + v.m_liquidType + " counter now " + left + (left <= 0 ? ", level reset" : ", NOT reset: another volume still counts") + ")");
                }
                list.Clear();
            }
            return lines;
        }

        static readonly FieldInfo FWaterLevel = AccessTools.Field(typeof(Character), "m_waterLevel");
        static readonly FieldInfo FTarLevel = AccessTools.Field(typeof(Character), "m_tarLevel");

        /// The local player's water / tar level and feet height (test report: is a destroyed pool still felt?).
        internal static string PlayerLiquidLine()
        {
            var p = Player.m_localPlayer;
            if (p == null) return "no local player";
            string Lv(FieldInfo f) { var o = f != null ? f.GetValue(p) : null; return o is float x ? x.ToString("0.00") : "n/a"; }
            return "player waterLevel " + Lv(FWaterLevel) + " tarLevel " + Lv(FTarLevel) + " feet y " + p.transform.position.y.ToString("0.00") + (p.IsSwimming() ? " SWIMMING" : "");
        }

        /// true when this tar surface already tracks the object behind `col` (what the game's OnTriggerEnter would find by attachedRigidbody -> IWaterInteractable).
        internal static bool AlreadyTracked(LiquidSurface ls, Collider col)
        {
            if (ls == null || col == null || FInWater == null || FLiquid == null) return false;
            if (!IsLiquid(FLiquid.GetValue(ls) as LiquidVolume)) return false;
            var rb = col.attachedRigidbody;
            var w = rb != null ? rb.GetComponent<IWaterInteractable>() : null;
            if (w == null) return false;
            var list = FInWater.GetValue(ls) as List<IWaterInteractable>;
            return list != null && list.Contains(w);
        }
    }
}
