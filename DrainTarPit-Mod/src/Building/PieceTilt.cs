using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using Kydl.Common;
using DrainTarPit.TextLocal;
using HarmonyLib;
using UnityEngine;

namespace DrainTarPit.Building
{
    /// <summary>
    /// Vertical rotation for DTP build pieces. Vanilla only yaws the placement ghost:
    ///   Player.UpdatePlacementGhost: rot = Quaternion.Euler(0, m_placeRotationDegrees * m_placeRotation, 0)
    /// That single quaternion drives the ghost transform, the manual snap offsets and (via TryPlacePiece, which reads
    /// the ghost's transform) the final placement, and the full quaternion is stored in the ZDO.
    ///
    ///  - Transpiler: that Euler call becomes PieceTilt.Euler(), which adds pitch (X) and roll (Z) while a DTP_ piece
    ///    is the held ghost. Quaternion.Euler order is Y*X*Z, so pitch tilts the piece up/down relative to its facing
    ///    and roll spins it around its own forward (hose) axis.
    ///  - UpdatePlacement postfix: while the tilt modifier is held, the scroll step vanilla applied to yaw is undone
    ///    and applied to pitch (or roll with the extra modifier) instead.
    /// Keyboard/mouse only; gamepad keeps yaw rotation.
    /// </summary>
    internal static class PieceTilt
    {
        public static float Pitch;
        public static float Roll;
        public static bool GhostIsDtp;

        static readonly FieldInfo GhostField = AccessTools.Field(typeof(Player), "m_placementGhost");
        static readonly FieldInfo PlaceRotField = AccessTools.Field(typeof(Player), "m_placeRotation");

        public static bool IsDtpGhost(Player p)
        {
            var ghost = GhostField.GetValue(p) as GameObject;
            return ghost != null && ghost.name.StartsWith("DTP_");
        }

        public static int GetPlaceRotation(Player p) { return (int)PlaceRotField.GetValue(p); }

        /// Replacement for Quaternion.Euler(x, y, z) inside UpdatePlacementGhost.
        public static Quaternion Euler(float x, float y, float z)
        {
            if (!GhostIsDtp) return Quaternion.Euler(x, y, z);
            return Quaternion.Euler(x + Pitch, y, z + Roll);
        }

        public static void HandleInput(Player p, bool takeInput, int rotationBefore)
        {
            if (!takeInput || !IsDtpGhost(p)) return;
            var input = UnityInput.Current;

            if (input.GetKeyDown(ModSettings.TiltResetKey.Value))
            {
                Pitch = 0f; Roll = 0f;
                Show(p);
                return;
            }
            if (!input.GetKey(ModSettings.TiltKey.Value)) return;

            int now = GetPlaceRotation(p);
            if (now == rotationBefore) return;
            PlaceRotField.SetValue(p, rotationBefore);                 // undo vanilla's yaw step
            int dir = now > rotationBefore ? 1 : -1;
            float step = ModSettings.TiltStep.Value;

            if (input.GetKey(ModSettings.RollKey.Value)) Roll = Mathf.Repeat(Roll + dir * step, 360f);
            else Pitch = Mathf.Repeat(Pitch + dir * step, 360f);
            Show(p);
        }

        static void Show(Player p)
        {
            p.Message(MessageHud.MessageType.TopLeft, Loc.T("dtp_tilt_hud", Mathf.RoundToInt(Pitch).ToString(), Mathf.RoundToInt(Roll).ToString()));
        }

        static GameObject _lastGhost;
        /// Tilt belongs to the piece being placed: it is cleared when the ghost changes (other piece, hammer put away).
        public static void TrackGhost(Player p)
        {
            var g = GhostField.GetValue(p) as GameObject;
            if (g == _lastGhost) return;
            _lastGhost = g;
            Pitch = 0f; Roll = 0f;
        }

        public static string HelpText()
        {
            return "\n\n" + Loc.Token("dtp_tilt_help");     // key names are filled in when the tokens are registered (Loc.Register)
        }
    }

    [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
    internal static class Patch_UpdatePlacementGhost
    {
        static void Prefix(Player __instance) { PieceTilt.TrackGhost(__instance); PieceTilt.GhostIsDtp = PieceTilt.IsDtpGhost(__instance); }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var euler = AccessTools.Method(typeof(Quaternion), "Euler", new[] { typeof(float), typeof(float), typeof(float) });
            var replacement = AccessTools.Method(typeof(PieceTilt), "Euler");
            var degrees = AccessTools.Field(typeof(Player), "m_placeRotationDegrees");

            var code = new List<CodeInstruction>(instructions);
            int patched = 0;
            for (int i = 0; i < code.Count; i++)
            {
                var mi = code[i].operand as MethodInfo;
                if (code[i].opcode != OpCodes.Call || mi == null || mi != euler) continue;
                // only the yaw one: m_placeRotationDegrees is loaded shortly before the call
                bool yaw = false;
                for (int j = System.Math.Max(0, i - 8); j < i; j++)
                    if (code[j].opcode == OpCodes.Ldfld && (code[j].operand as FieldInfo) == degrees) { yaw = true; break; }
                if (!yaw) continue;
                code[i].operand = replacement;
                patched++;
            }
            if (patched == 0) ModLog.Error("PieceTilt: could not find the yaw Quaternion.Euler call in Player.UpdatePlacementGhost - vertical rotation disabled (game update?).");
            else ModLog.Verbose("PieceTilt: patched " + patched + " Euler call(s) in UpdatePlacementGhost.");
            return code;
        }
    }

    [HarmonyPatch(typeof(Player), "UpdatePlacement")]
    internal static class Patch_UpdatePlacement
    {
        static void Prefix(Player __instance, ref int __state) { __state = PieceTilt.GetPlaceRotation(__instance); }
        static void Postfix(Player __instance, bool takeInput, int __state) { PieceTilt.HandleInput(__instance, takeInput, __state); }
    }
}
