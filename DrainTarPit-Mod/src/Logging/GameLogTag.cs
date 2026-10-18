using HarmonyLib;

namespace DrainTarPit.Logging
{
    /// <summary>
    /// Every line the mod itself writes goes through ModLog and carries the mod's name ("[Info :DrainTarPit]"). Some lines ABOUT the mod's pieces are written
    /// by the game, though: when you build a piece it logs "Placed DTP_Hose_Straight_2m" and "DTP_Hose_Straight_2m(Clone) placed at height ..." through
    /// ZLog, which BepInEx shows as "[Info   : Unity Log]" - no mod name. Those calls are caught here (prefix on ZLog.Log / LogWarning / LogError):
    /// when the text names one of this mod's prefabs (they all start with "DTP_") it is written through ModLog instead (so with the mod name, date and
    /// time like every other line) and the untagged original is skipped. Everything else the game logs is left alone.
    /// </summary>
    internal static class GameLogTag
    {
        const string Marker = "DTP_";

        static bool Ours(object[] args, out string text)
        {
            text = null;
            if (args == null || args.Length == 0 || args[0] == null) return false;
            text = args[0] as string ?? args[0].ToString();
            return text != null && text.IndexOf(Marker, System.StringComparison.Ordinal) >= 0;
        }

        // __args instead of a named parameter: works whatever the game calls the argument of ZLog.Log
        [HarmonyPatch(typeof(ZLog), "Log", typeof(object))]
        static class Patch_Log
        {
            static bool Prefix(object[] __args)
            {
                string t;
                if (!Ours(__args, out t)) return true;
                ModLog.Info(t);
                return false;
            }
        }

        [HarmonyPatch(typeof(ZLog), "LogWarning", typeof(object))]
        static class Patch_LogWarning
        {
            static bool Prefix(object[] __args)
            {
                string t;
                if (!Ours(__args, out t)) return true;
                ModLog.Warn(t);
                return false;
            }
        }

        [HarmonyPatch(typeof(ZLog), "LogError", typeof(object))]
        static class Patch_LogError
        {
            static bool Prefix(object[] __args)
            {
                string t;
                if (!Ours(__args, out t)) return true;
                ModLog.Error(t);
                return false;
            }
        }
    }
}
