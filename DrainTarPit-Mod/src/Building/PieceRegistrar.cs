using System.Collections.Generic;
using Kydl.Common;
using DrainTarPit.Pump;
using DrainTarPit.Visuals;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace DrainTarPit.Building
{
    /// Registers every bundle prefab as a Hammer > Misc piece and refreshes the icons once a world is loaded.
    internal static class PieceRegistrar
    {
        static readonly List<GameObject> IconPrefabs = new();
        /// Prefab names of the pieces added to the Hammer (read by Testing/InGameTests); ExpectedPieces = the Register calls below.
        internal static readonly List<string> Registered = new();
        internal const int ExpectedPieces = 6;

        public static void RegisterAll()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= RegisterAll;
            try
            {
                // asset names inside the bundle are lowercase paths
                Register("assets/prefabs/dtp_pump_manual.prefab", "dtp_piece_pump",
                    true, new[] { Req("Wood", 10), Req("Iron", 5), Req("LeatherScraps", 4), Req("Resin", 5) });

                Register("assets/prefabs/dtp_hose_intakestrainer.prefab", "dtp_piece_strainer", false, new[] { Req("Iron", 2), Req("LeatherScraps", 2), Req("Tar", 2) });
                Register("assets/prefabs/dtp_hose_outletnozzle.prefab", "dtp_piece_nozzle", false, new[] { Req("Iron", 2), Req("LeatherScraps", 1), Req("Tar", 2) });
                Register("assets/prefabs/dtp_hose_straight_1m.prefab", "dtp_piece_hose1", false, new[] { Req("LeatherScraps", 1), Req("Tar", 1) });
                Register("assets/prefabs/dtp_hose_straight_2m.prefab", "dtp_piece_hose2", false, new[] { Req("LeatherScraps", 2), Req("Tar", 2) });
                Register("assets/prefabs/dtp_hose_elbow90.prefab", "dtp_piece_elbow", false, new[] { Req("LeatherScraps", 2), Req("Tar", 2) });
            }
            catch (System.Exception e) { ModLog.Error("RegisterAll failed: " + e); }
            ModLog.Check("PieceRegistrar: pieces registered", Registered.Count, ExpectedPieces, 0, "");
            if (!PieceSetup.Headless) Plugin.Instance.RunCoroutine(RefreshIcons());     // no icons on a dedicated server
        }

        static System.Collections.IEnumerator RefreshIcons()
        {
            while (ZNetScene.instance == null || Player.m_localPlayer == null) yield return null;
            yield return new WaitForSeconds(3f);
            foreach (var prefab in IconPrefabs)
            {
                var sprite = PieceSetup.RenderIcon(prefab);
                var piece = prefab.GetComponent<Piece>();
                if (sprite != null && piece != null) piece.m_icon = sprite;
                yield return null;
            }
            ModLog.Info("Icons refreshed.");
        }

        static RequirementConfig Req(string item, int amount) { return new RequirementConfig(item, amount, 0, true); }

        static void Register(string assetPath, string key, bool isPump, RequirementConfig[] reqs)
        {
            try
            {
                var prefab = Plugin.Bundle.LoadAsset<GameObject>(assetPath);
                if (prefab == null) { ModLog.Error("Missing in bundle: " + assetPath); return; }

                MaterialFixer.Apply(prefab);
                MaterialFixer.MakeHosesClear(prefab);
                PieceSetup.Prepare(prefab);
                if (isPump && prefab.GetComponent<PumpController>() == null) prefab.AddComponent<PumpController>();

                var piece = prefab.GetComponent<Piece>();
                if (piece.m_icon == null) piece.m_icon = PieceSetup.Placeholder();   // must exist before AddPiece; real icon rendered later
                IconPrefabs.Add(prefab);

                var cfg = new PieceConfig
                {
                    Name = Loc.Token(key),
                    Description = Loc.Token(key + "_desc") + PieceTilt.HelpText(),
                    PieceTable = PieceTables.Hammer,
                    Category = KyCategory.For(prefab.name),            // T-66: the user's own build menu category
                    Requirements = reqs,
                };
                PieceManager.Instance.AddPiece(new CustomPiece(prefab, true, cfg));
                Registered.Add(prefab.name);
                ModLog.Info("Registered piece " + prefab.name + " (" + Registered.Count + " of " + ExpectedPieces + ")");
            }
            catch (System.Exception e) { ModLog.Error("Failed to register '" + assetPath + "': " + e); }
        }
    }
}