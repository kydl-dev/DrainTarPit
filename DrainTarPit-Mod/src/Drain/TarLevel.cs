using System.Runtime.CompilerServices;
using DrainTarPit.Logging;
using HarmonyLib;
using UnityEngine;

namespace DrainTarPit.Drain
{
    /// <summary>
    /// Why a player standing 4 m under the surface of a tar volume was not tarred (log: tarLevel=-10000, waterLevel=30, 'tracks you' on four volumes):
    /// every LiquidSurface that still tracks a character calls Character.SetLiquidLevel(surface, Tar) from its FixedUpdate, and the call just ASSIGNS
    /// m_tarLevel. With several tar volumes tracking the same player (overlapping vanilla pits, pump pools whose trigger still holds the player 18 m away),
    /// whichever surface updates LAST wins: a pool that has no tar there (TarPits.Patch_GetSurface answers -10000 outside its grid, a drained pit answers
    /// its ground height) overwrote the real level of the pit the player was in. m_tarLevel stayed below the water level -> InTar = false, never Tared.
    ///
    /// Fix: within one physics step the highest tar level written by a surface UPDATE wins (a character is in the deepest tar around it).
    ///
    /// Exits are not updates: OnTriggerExit / OnDestroy (and TarPits.PurgeStale, which replays them) send -10000 once the LAST tar volume let go of the
    /// character. Those always go through. (0.6.8 filtered them as well; the exit callback runs in the same physics step as the FixedUpdate that wrote the
    /// last level, so the reset was swallowed and m_tarLevel stayed on the old surface height after leaving the pit.) The two are told apart by a flag that
    /// is set while LiquidSurface.UpdateFloaters runs.
    /// </summary>
    internal static class TarLevel
    {
        sealed class Stamp { public float Time = -1f, WaterTime = -1f; public float Level = -10000f, WaterLevel = -10000f; }
        static readonly ConditionalWeakTable<Character, Stamp> Stamps = new();

        const float ExitLevel = -9999f;
        static bool _inUpdate;      // true while LiquidSurface.UpdateFloaters runs (its SetLiquidLevel calls are surface updates, not exits)

        [HarmonyPatch(typeof(Character), "SetLiquidLevel")]
        static class Patch_SetLiquidLevel
        {
            // __args: { float level, LiquidType type, Component liquidSurface } (parameter names differ between game versions, so they are read by position)
            static bool Prefix(Character __instance, object[] __args)
            {
                if (__instance == null || __args == null || __args.Length < 2) return true;
                if (!(__args[0] is float) || !(__args[1] is LiquidType)) return true;
                var type = (LiquidType)__args[1];
                if (type != LiquidType.Tar && type != LiquidType.Water) return true;           // water pools write m_waterLevel the same way (and compete with the ocean's WaterVolume)
                bool water = type == LiquidType.Water;
                float level = (float)__args[0];
                var st = Stamps.GetOrCreateValue(__instance);
                if (!_inUpdate && level <= ExitLevel)       // a real exit: nothing tracks the character any more
                {
                    if (water) { st.WaterTime = -1f; st.WaterLevel = level; } else { st.Time = -1f; st.Level = level; }
                    return true;
                }
                float now = Time.fixedTime;
                float lastTime = water ? st.WaterTime : st.Time, lastLevel = water ? st.WaterLevel : st.Level;
                if (Mathf.Approximately(lastTime, now) && lastLevel > level) return false;      // another volume already gave a higher level in this step
                if (water) { st.WaterTime = now; st.WaterLevel = level; } else { st.Time = now; st.Level = level; }
                return true;
            }
        }

        /// Marks the surface updates (see above) and first drops the entries that no longer belong to the surface (TarPits.PurgeStale).
        [HarmonyPatch(typeof(LiquidSurface), "UpdateFloaters")]
        static class Patch_UpdateFloaters
        {
            static void Prefix(LiquidSurface __instance)
            {
                _inUpdate = false;                                  // the purge replays OnTriggerExit: its -10000 is an exit
                try { TarPits.PurgeStale(__instance); }
                catch (System.Exception e) { ModLog.VerboseWarn("PurgeStale: " + e.Message); }
                _inUpdate = true;
            }

            static void Finalizer() { _inUpdate = false; }          // also when the game's own loop throws
        }

        /// A second OnTriggerEnter for an object the surface already tracks (Unity re-reports it after the trigger mesh was rebuilt) bumped the object's
        /// liquid counter again without adding a second list entry: one exit could never bring the counter back to 0, the tar level was never reset.
        [HarmonyPatch(typeof(LiquidSurface), "OnTriggerEnter")]
        static class Patch_OnTriggerEnter
        {
            static bool Prefix(LiquidSurface __instance, object[] __args)
            {
                var col = __args != null && __args.Length > 0 ? __args[0] as Collider : null;
                return !TarPits.AlreadyTracked(__instance, col);
            }
        }
    }
}
