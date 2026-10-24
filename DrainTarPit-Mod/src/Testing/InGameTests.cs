using System.Linq;
using Kydl.Common;
using UnityEngine;

namespace DrainTarPit.Testing
{
    /// <summary>
    /// In-game tests run by the user's Kydl.GameProbe (dev tool, never a dependency: it finds this class by its NAME `InGameTests`).
    ///   Startup_*  at the main menu;   World_*  5 s after a world is loaded and again on GameProbe's RetestKey (PageDown).
    /// Each returns "PASS: ..." / "FAIL: ..." / "SKIP: ..." with the numbers that decided it. Read-only: nothing is changed.
    /// </summary>
    internal static class InGameTests
    {
        static string Startup_ConfigBound()
        {
            if (ModSettings.EnableAll == null || ModSettings.DebugLog == null) return "FAIL: ModSettings.Bind did not run (EnableAll / DebugLog null)";
            string r = ModSettings.On ? "PASS: EnableAll on, debug log " + ModSettings.DebugLog.Value : "SKIP: EnableAll is off in the config";
            ModLog.Verbose("InGameTests: Startup_ConfigBound -> " + r);
            return r;
        }

        static string Startup_BundleLoaded()
        {
            var b = Plugin.Bundle;
            string r = b != null ? "PASS: bundle '" + Plugin.BundleName + "' with " + b.GetAllAssetNames().Length + " asset(s)" : "FAIL: Plugin.Bundle is null - no pump / hose pieces";
            ModLog.Verbose("InGameTests: Startup_BundleLoaded -> " + r);
            return r;
        }

        /// The 6 pieces (pump, strainer, nozzle, hose 1 m / 2 m, elbow) are registered, known to ZNetScene and in the Hammer's build table.
        static string World_PiecesRegistered()
        {
            var names = Building.PieceRegistrar.Registered;
            if (ZNetScene.instance == null) return "SKIP: ZNetScene not ready";
            var notInScene = names.Where(n => ZNetScene.instance.GetPrefab(n) == null).ToList();
            var table = HammerPieces();
            var notInHammer = table == null ? names.ToList() : names.Where(n => !table.m_pieces.Any(p => p != null && p.name == n)).ToList();
            string r;
            if (names.Count != Building.PieceRegistrar.ExpectedPieces) r = "FAIL: " + names.Count + " of " + Building.PieceRegistrar.ExpectedPieces + " pieces registered (" + string.Join(", ", names) + ")";
            else if (notInScene.Count > 0) r = "FAIL: not in ZNetScene: " + string.Join(", ", notInScene);
            else if (notInHammer.Count > 0) r = "FAIL: not in the Hammer build table" + (table == null ? " (Hammer not found)" : "") + ": " + string.Join(", ", notInHammer);
            else r = "PASS: " + names.Count + " pieces in ZNetScene and the Hammer (" + string.Join(", ", names) + ")";
            ModLog.Verbose("InGameTests: World_PiecesRegistered -> " + r);
            return r;
        }

        /// Tar pits near the player are tracked (informational: SKIP far away from tar pits).
        static string World_TarPitsTracked()
        {
            int n = Drain.TarPits.All.Count(v => v != null);
            string r = n > 0 ? "PASS: " + n + " tar pit(s) tracked" : "SKIP: no tar pit loaded near the player (go to the Ashlands / Mistlands tar pits to test)";
            ModLog.Verbose("InGameTests: World_TarPitsTracked -> " + r);
            return r;
        }

        static PieceTable HammerPieces()
        {
            var hammer = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab("Hammer") : null;
            var drop = hammer != null ? hammer.GetComponent<ItemDrop>() : null;
            return drop != null ? drop.m_itemData.m_shared.m_buildPieces : null;
        }
    }
}
