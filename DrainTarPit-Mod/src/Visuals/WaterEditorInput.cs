using DrainTarPit.Logging;
using HarmonyLib;
using UnityEngine;

namespace DrainTarPit.Visuals
{
    /// Blocks the game's input while the water editor window is open (WaterEditor): no walking / attacking (every TakeInput gate), no camera follow,
    /// no mouse-wheel zoom. Harmony patches made by hand and only when the game method is found. NOT tested in game.
    internal static class WaterEditorInput
    {
        static Harmony _harmony;

        /// Called once from Plugin.Awake after the other patches. The patches are made by hand and only when the game method is found,
        /// so a renamed method in a future game version costs the editor's input blocking, not the whole mod.
        /// Player input probably goes through more than one TakeInput gate in the game (Player.TakeInput and PlayerController.TakeInput; from memory of the
        /// game's code, NOT checked in assembly_valheim.dll). 0001 patched only Player.TakeInput and the user asked again for 'no input at all while the editor is
        /// open', so now every method called
        /// TakeInput that returns bool is patched on both classes (whatever its parameters), and every patched method is written to the log.
        public static void Install()
        {
            try
            {
                _harmony = new Harmony(Plugin.ModGuid + ".editor");
                int n = PatchTakeInput(typeof(Player)) + PatchTakeInput(AccessTools.TypeByName("PlayerController"));
                if (n == 0) ModLog.Warn("WaterEditor: no TakeInput method found - the player can still move while the editor is open.");
                var cap = AccessTools.Method(typeof(GameCamera), "UpdateMouseCapture");
                if (cap != null) _harmony.Patch(cap, prefix: new HarmonyMethod(typeof(WaterEditorInput), "CameraPrefix"));
                else ModLog.Warn("WaterEditor: GameCamera.UpdateMouseCapture not found - the camera may keep following the mouse while the editor is open.");
                // the mouse wheel zooms the game camera: while the editor is open it only scrolls the editor
                int w = PatchScroll(AccessTools.TypeByName("ZInput"));
                if (w == 0) ModLog.Warn("WaterEditor: ZInput.GetMouseScrollWheel not found - the mouse wheel may zoom the camera while the editor is open.");
            }
            catch (System.Exception e) { ModLog.Warn("WaterEditor: install failed: " + e.Message); }
        }

        static int PatchTakeInput(System.Type t)
        {
            int n = 0;
            if (t == null) return 0;
            foreach (var m in AccessTools.GetDeclaredMethods(t))
            {
                if (m.Name != "TakeInput" || m.ReturnType != typeof(bool) || m.IsAbstract || m.IsGenericMethod) continue;
                try
                {
                    _harmony.Patch(m, postfix: new HarmonyMethod(typeof(WaterEditorInput), "TakeInputPostfix"));
                    n++;
                    ModLog.Info("WaterEditor: input blocked through " + t.Name + ".TakeInput (" + m.GetParameters().Length + " parameter(s))");
                }
                catch (System.Exception e) { ModLog.Warn("WaterEditor: patching " + t.Name + ".TakeInput failed: " + e.Message); }
            }
            return n;
        }

        static int PatchScroll(System.Type t)
        {
            int n = 0;
            if (t == null) return 0;
            foreach (var m in AccessTools.GetDeclaredMethods(t))
            {
                if (m.Name != "GetMouseScrollWheel" || m.ReturnType != typeof(float) || m.IsGenericMethod) continue;
                try { _harmony.Patch(m, postfix: new HarmonyMethod(typeof(WaterEditorInput), "ScrollPostfix")); n++; }
                catch (System.Exception e) { ModLog.Warn("WaterEditor: patching ZInput.GetMouseScrollWheel failed: " + e.Message); }
            }
            return n;
        }

        static void TakeInputPostfix(ref bool __result) { if (WaterEditor.IsOpen) __result = false; }
        static void ScrollPostfix(ref float __result) { if (WaterEditor.IsOpen) __result = 0f; }
        static bool CameraPrefix() { return !WaterEditor.IsOpen; }
    }
}
