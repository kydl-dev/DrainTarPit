using System.Collections.Generic;
using System.Reflection;
using DrainTarPit.Logging;
using HarmonyLib;
using UnityEngine;
using static DrainTarPit.Drain.TarPits;

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
    }
}
