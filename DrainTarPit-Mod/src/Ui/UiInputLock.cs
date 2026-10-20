using System.Collections.Generic;
using DrainTarPit.Logging;
using HarmonyLib;
using UnityEngine;

namespace DrainTarPit.Ui
{
    /// <summary>
    /// T-023 (user: "don't move the character when interacting with the UI menu"): while one of the mod's menus is open the game behaves as if
    /// its own text input box were open. The game asks TextInput.IsVisible() before walking / looking (PlayerController.TakeInput, Player.TakeInput),
    /// before locking the mouse (GameCamera.UpdateMouseCapture), before Escape opens the main menu (Menu), before the map / chat keys
    /// (Minimap, Chat): one postfix covers all of them (read from assembly_valheim: 6 callers). The mouse wheel is also kept from zooming.
    /// Patches are made by hand, only when the game method exists, so a renamed method costs the lock, not the mod.
    /// </summary>
    internal static class UiInputLock
    {
        static readonly HashSet<string> Holders = new();
        static Harmony _harmony;
        static int _patched;

        /// Number of open menus that hold the lock (0 = the game has its input).
        internal static int Count { get { return Holders.Count; } }

        internal static void Install()
        {
            try
            {
                _harmony = new Harmony(Plugin.ModGuid + ".uilock");
                var vis = AccessTools.Method(typeof(TextInput), "IsVisible");
                if (vis != null) { _harmony.Patch(vis, postfix: new HarmonyMethod(typeof(UiInputLock), nameof(VisiblePostfix))); _patched++; }
                else ModLog.Warn("UiInputLock: TextInput.IsVisible not found - the player can still move while a DrainTarPit menu is open");
                foreach (var m in AccessTools.GetDeclaredMethods(typeof(ZInput)))
                {
                    if (m.Name != "GetMouseScrollWheel" || m.ReturnType != typeof(float) || m.IsGenericMethod) continue;
                    _harmony.Patch(m, postfix: new HarmonyMethod(typeof(UiInputLock), nameof(ScrollPostfix)));
                    _patched++;
                }
                ModLog.Verbose("UiInputLock: " + _patched + " game method(s) patched (TextInput.IsVisible + ZInput.GetMouseScrollWheel)");
            }
            catch (HarmonyException e) { ModLog.Warn("UiInputLock: patching failed: " + e.Message); }
            catch (System.Exception e) { ModLog.Warn("UiInputLock: install failed: " + e.Message); }
        }

        /// A menu opens: the game stops taking input, the mouse is freed. Returns the number of holders after the call.
        internal static int Hold(string who)
        {
            Holders.Add(who);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            ModLog.Verbose("UiInputLock: held by '" + who + "' (" + Holders.Count + " holder(s))");
            return Holders.Count;
        }

        /// A menu closes. The game gets its input back when the last holder lets go. Returns the number of holders left.
        internal static int Release(string who)
        {
            int before = Holders.Count;
            Holders.Remove(who);
            if (before != Holders.Count) ModLog.Verbose("UiInputLock: released by '" + who + "' (" + Holders.Count + " holder(s) left)");
            return Holders.Count;
        }

        static void VisiblePostfix(ref bool __result) { if (Holders.Count > 0) __result = true; }
        static void ScrollPostfix(ref float __result) { if (Holders.Count > 0) __result = 0f; }
    }
}
