using System.Reflection;
using DrainTarPit.Logging;
using DrainTarPit.TextLocal;
using HarmonyLib;
using UnityEngine;

namespace DrainTarPit.Signs
{
    /// <summary>
    /// T-60 (user: "allow crafting pool signs only when close to a pool, preferably on top of it, but being within 5m of a pool is accepted"):
    /// the pool sign's build ghost turns red (placement Invalid) when no pool (pump pool or tar pit, PoolFinder) is under it or within
    /// PoolFinder.Reach; trying to place it then tells why. Every other piece is left alone.
    /// </summary>
    internal static class PoolSignPlacement
    {
        static readonly FieldInfo FGhost = AccessTools.Field(typeof(Player), "m_placementGhost");
        static readonly FieldInfo FStatus = AccessTools.Field(typeof(Player), "m_placementStatus");
        static readonly MethodInfo MGhostValid = AccessTools.Method(typeof(Player), "SetPlacementGhostValid");
        const int StatusValid = 0, StatusInvalid = 1;              // Player.PlacementStatus.Valid / Invalid
        static Vector3 _lastAt = new(float.MaxValue, 0f, 0f);
        static float _lastDist = -1f;
        static int _blocked;

        /// After the game placed the ghost: a pool sign without a pool near is refused. Returns 1 when this frame's placement was refused.
        internal static int Check(Player p)
        {
            if (p == null || p != Player.m_localPlayer || FGhost == null || FStatus == null) return 0;
            var ghost = FGhost.GetValue(p) as GameObject;
            if (ghost == null || ghost.GetComponent<PoolSign>() == null) { _blocked = 0; return 0; }
            if (System.Convert.ToInt32(FStatus.GetValue(p)) != StatusValid) { _blocked = 0; return 0; }     // the game refuses it already
            Vector3 at = ghost.transform.position;
            if ((at - _lastAt).sqrMagnitude > 0.01f)
            {
                _lastAt = at;
                float before = _lastDist;
                PoolFinder.Find(at, PoolFinder.Reach, out _lastDist);
                if ((before >= 0f) != (_lastDist >= 0f))
                    ModLog.Verbose("PoolSignPlacement: ghost at " + at.ToString("0.0") + (_lastDist >= 0f ? " - pool " + (_lastDist <= 0f ? "under it" : _lastDist.ToString("0.0") + " m away") + ", can be built" : " - no pool within " + PoolFinder.Reach + " m, refused"));
            }
            if (_lastDist >= 0f) { _blocked = 0; return 0; }
            FStatus.SetValue(p, System.Enum.ToObject(FStatus.FieldType, StatusInvalid));
            if (MGhostValid != null) MGhostValid.Invoke(p, new object[] { false });
            _blocked = 1;
            return 1;
        }

        /// Before the game tries to place a piece: says why a pool sign is refused (the game itself only says "invalid placement").
        /// Returns 1 when the message was shown.
        internal static int Tell(Player p, Piece piece)
        {
            if (_blocked != 1 || p == null || p != Player.m_localPlayer || piece == null || piece.GetComponent<PoolSign>() == null) return 0;
            p.Message(MessageHud.MessageType.TopLeft, Loc.T("dtp_sign_need_pool", PoolFinder.Reach.ToString("0")));
            ModLog.Verbose("PoolSignPlacement: pool sign not placed at " + _lastAt.ToString("0.0") + ", no pool within " + PoolFinder.Reach + " m");
            return 1;
        }

        internal static void LogTargets()
        {
            ModLog.Verbose("PoolSignPlacement: m_placementGhost " + (FGhost != null ? "found" : "MISSING") + ", m_placementStatus " + (FStatus != null ? "found" : "MISSING") +
                ", SetPlacementGhostValid " + (MGhostValid != null ? "found" : "MISSING") + (FGhost == null || FStatus == null ? " - pool signs can be built anywhere" : ""));
        }
    }

    [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
    static class Patch_PoolSignGhost
    {
        static void Postfix(Player __instance) { PoolSignPlacement.Check(__instance); }
    }

    [HarmonyPatch(typeof(Player), "TryPlacePiece")]
    static class Patch_PoolSignPlace
    {
        static void Prefix(Player __instance, Piece __0) { PoolSignPlacement.Tell(__instance, __0); }   // __0: the parameter's name is not checked
    }
}
