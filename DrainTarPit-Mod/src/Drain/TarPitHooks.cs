using System.Collections.Generic;
using DrainTarPit.Logging;
using HarmonyLib;
using UnityEngine;
using static DrainTarPit.Drain.TarPits;
using static DrainTarPit.Drain.TarPitDrain;
using static DrainTarPit.Drain.TarPitFlow;
using static DrainTarPit.Drain.TarPitSettle;

namespace DrainTarPit.Drain
{
    /// <summary>
    /// Harmony hooks on LiquidVolume (Awake / Update / OnDestroy / GetSurface) and the RPC registration. T-069: what the pools need (deposits,
    /// walls, films, runoff, mesh) is done by the WaterAndTarPools mod through PoolHooks (VolumeRegister / VolumeUpdate / VolumeDestroyed).
    /// </summary>
    internal static class TarPitHooks
    {
        // --- Harmony hooks ------------------------------------------------------------------------------------------
        // The three RPCs live on the pit's ZNetView. ZNetView.Register throws on a duplicate, and the ZDO may not exist yet when LiquidVolume.Awake
        // runs (component order), so registration is retried from Update until it has happened exactly once per volume.
        static readonly HashSet<LiquidVolume> Registered = new();

        /// Bug #2: callers that send an RPC right after spawning a pool ask first. Expected outcome: the volume is in the registered set afterwards.
        internal static bool TryRegister(LiquidVolume vol)
        {
            EnsureRegistered(vol);
            return vol != null && Registered.Contains(vol);
        }

        static void EnsureRegistered(LiquidVolume vol)
        {
            if (vol == null || Registered.Contains(vol)) return;
            var nv = Nview(vol);
            if (nv == null || !nv.IsValid() || nv.GetZDO() == null)
            { ModLog.Verbose("TarPits: RPCs of '" + vol.name + "' not registered yet, " + (nv == null ? "no ZNetView" : "its ZDO does not exist yet") + " (retried every frame)"); return; }
            Registered.Add(vol);
            nv.Register<ZPackage>(RpcDrain, (sender, pkg) => OnDrainRequest(vol, sender, pkg));
            nv.Register<ZPackage>(RpcDrained, (sender, pkg) => OnDrained(vol, sender, pkg));
            int more = PoolHooks.RaiseRegister(vol, nv);                     // WaterAndTarPools: the deposit RPC
            ModLog.Verbose("TarPits: RPCs registered on '" + vol.name + "' @ " + vol.transform.position.ToString("0") + (more > 0 ? " (+ " + more + " pool handler(s))" : ""));
        }

        // LiquidVolume.GetSurface clamps the point to the grid (GetDepth / GetHeight clamp), so an object that the volume's trigger still tracks while it is
        // OUTSIDE the grid (stale trigger state, a trigger bigger than the grid) got the surface height of the nearest edge cell: standing 30 m from a pump
        // pool the player was 'swimming in tar'. Outside the grid there is no liquid.
        [HarmonyPatch(typeof(LiquidVolume), "GetSurface", new[] { typeof(Vector3) })]
        static class Patch_GetSurface
        {
            static void Postfix(LiquidVolume __instance, Vector3 p, ref float __result)
            {
                if (!IsLiquid(__instance)) return;
                if (!InGrid(__instance, ToGrid(__instance, p))) __result = -10000f;
            }
        }

        [HarmonyPatch(typeof(LiquidVolume), "Awake")]
        static class Patch_Awake
        {
            static void Postfix(LiquidVolume __instance)
            {
                if (!IsLiquid(__instance)) return;
                if (!All.Contains(__instance)) All.Add(__instance);
                EnsureRegistered(__instance);
            }
        }

        [HarmonyPatch(typeof(LiquidVolume), "OnDestroy")]
        static class Patch_OnDestroy
        {
            static void Prefix(LiquidVolume __instance) { All.Remove(__instance); Registered.Remove(__instance); RefillTimers.Remove(__instance); SettleTimers.Remove(__instance); UnboostIfDone(__instance, true); PoolHooks.RaiseDestroyed(__instance); }
        }

        [HarmonyPatch(typeof(LiquidVolume), "Update")]
        static class Patch_Update
        {
            static void Postfix(LiquidVolume __instance)
            {
                if (!IsLiquid(__instance)) return;
                EnsureRegistered(__instance);
                if (Boosted.Count > 0) UnboostIfDone(__instance, false);
                RefillTick(__instance, Time.deltaTime);
                float next; SettleTimers.TryGetValue(__instance, out next);
                if (Time.time >= next) { SettleTimers[__instance] = Time.time + 1f; SettleFloaters(__instance); }
                PoolHooks.RaiseUpdate(__instance);                               // WaterAndTarPools: deposits, runoff, films, mesh
            }
        }
    }
}
