using System.Collections.Generic;
using DrainTarPit.Logging;
using HarmonyLib;
using UnityEngine;
using static DrainTarPit.Drain.TarPits;
using static DrainTarPit.Drain.TarPitDrain;
using static DrainTarPit.Drain.TarPitDeposit;
using static DrainTarPit.Drain.TarPitFlow;
using static DrainTarPit.Drain.TarPitSettle;
using static DrainTarPit.Drain.TarPitWalls;
using static DrainTarPit.Drain.TarPitStats;

namespace DrainTarPit.Drain
{
    /// <summary>
    /// Harmony hooks on LiquidVolume (Awake / Update / OnDestroy / UpdateHeights / GetSurface) and the RPC registration.
    /// </summary>
    internal static class TarPitHooks
    {
        // --- Harmony hooks ------------------------------------------------------------------------------------------
        // The three RPCs live on the pit's ZNetView. ZNetView.Register throws on a duplicate, and the ZDO may not exist yet when LiquidVolume.Awake
        // runs (component order), so registration is retried from Update until it has happened exactly once per volume.
        static readonly HashSet<LiquidVolume> Registered = new();

        static void EnsureRegistered(LiquidVolume vol)
        {
            if (vol == null || Registered.Contains(vol)) return;
            var nv = Nview(vol);
            if (nv == null || !nv.IsValid() || nv.GetZDO() == null) return;
            Registered.Add(vol);
            nv.Register<ZPackage>(RpcDrain, (sender, pkg) => OnDrainRequest(vol, sender, pkg));
            nv.Register<ZPackage>(RpcDrained, (sender, pkg) => OnDrained(vol, sender, pkg));
            nv.Register<ZPackage>(RpcDeposit, (sender, pkg) => OnDepositRequest(vol, sender, pkg));
            ModLog.Verbose("TarPits: RPCs registered on '" + vol.name + "' @ " + vol.transform.position.ToString("0"));
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

        [HarmonyPatch(typeof(LiquidVolume), "UpdateHeights")]
        static class Patch_UpdateHeights
        {
            // The game rewrites every ground height here (walls drop back to terrain) while the pool's worker thread keeps stepping the simulation. Between that
            // write and our wall pass one step could run over the rim and carry a lot of liquid out through every wall cell (a step moves up to half the height
            // difference). The pool's lock is held from before the game's write until the wall pass is done (Monitor, same thread: prefix and finalizer).
            static void Prefix(LiquidVolume __instance, out object __state)
            {
                __state = null;
                try { if (ModSettings.PoolWalls.Value && IsLiquid(__instance) && IsPumpPool(__instance)) __state = Hold(__instance); }
                catch (System.Exception e) { ModLog.VerboseWarn("UpdateHeights lock: " + e.Message); }
            }

            static void Finalizer(object __state)
            {
                var held = __state as System.IDisposable;
                if (held != null) held.Dispose();
            }

            static void Postfix(LiquidVolume __instance)
            {
                try { FixMissedHeights(__instance); }
                catch (System.Exception e) { ModLog.VerboseWarn("FixMissedHeights: " + e.Message); }
                try { ApplyWalls(__instance); }
                catch (System.Exception e) { ModLog.VerboseWarn("ApplyWalls: " + e.Message); }
                try { DrainTarPit.Debug.PoolTankTests.ApplySynthetic(__instance); }
                catch (System.Exception e) { ModLog.VerboseWarn("PoolTankTests.ApplySynthetic: " + e.Message); }
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
            static void Prefix(LiquidVolume __instance) { All.Remove(__instance); Registered.Remove(__instance); RefillTimers.Remove(__instance); Memos.Remove(__instance); PendingDeposits.Remove(__instance); SettleTimers.Remove(__instance); ShedTimers.Remove(__instance); Budgets.Remove(__instance); UnboostIfDone(__instance, true); }
        }

        [HarmonyPatch(typeof(LiquidVolume), "Update")]
        static class Patch_Update
        {
            static void Postfix(LiquidVolume __instance)
            {
                if (!IsLiquid(__instance)) return;
                EnsureRegistered(__instance);
                if (Boosted.Count > 0) UnboostIfDone(__instance, false);
                if (PendingDeposits.Count > 0) FlushDeposits(__instance);
                RefillTick(__instance, Time.deltaTime);
                float next; SettleTimers.TryGetValue(__instance, out next);
                if (Time.time >= next) { SettleTimers[__instance] = Time.time + 1f; SettleFloaters(__instance); }
                float shedAt; ShedTimers.TryGetValue(__instance, out shedAt);
                if (Time.time >= shedAt)
                {
                    float dt = shedAt > 0f ? Mathf.Min(ShedInterval * 4f, Time.time - (shedAt - ShedInterval)) : ShedInterval;
                    ShedTimers[__instance] = Time.time + ShedInterval;
                    ShedFilms(__instance, dt);
                }
            }
        }
    }
}
