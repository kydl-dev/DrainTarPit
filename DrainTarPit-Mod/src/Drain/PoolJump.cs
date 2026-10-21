using System.Collections.Generic;
using DrainTarPit.Logging;
using HarmonyLib;
using UnityEngine;

namespace DrainTarPit.Drain
{
    /// <summary>
    /// T-63 (user: "give a boost in jump to player if they struggle to get out of pool at edge of it? like 2m jump after they do 3-4 jumps"):
    /// the local player in a pump pool (water or tar), close to its edge, who presses Jump [Drain] PoolJumpPresses times within PressWindow s
    /// is lifted so the feet reach PoolJumpHeight m above where they were, pushed towards the edge. The game itself does not jump in deep water
    /// (Character.Jump needs ground and refuses at swim depth), and the walls are higher than a swim stroke.
    /// Input: Player.SetControls (jump = pressed this step). Lift: after Character.UpdateMotion (which sets the swim / walk velocity) the upward
    /// speed is raised to what reaches the target height, every physics step until the player is out of the liquid or at the height.
    /// </summary>
    internal static class PoolJump
    {
        const float PressWindow = 3f;        // s in which the presses must come
        const float EdgeReach = 2f;          // m from the player to the dry edge
        const float MinDepth = 0.3f;         // m of liquid at the player that counts as "in the pool"
        const float Push = 2.5f;             // m/s towards the edge during the lift
        const float MaxLift = 1.5f;          // s, safety end of a lift
        static readonly AccessTools.FieldRef<Character, Rigidbody> Body = AccessTools.FieldRefAccess<Character, Rigidbody>("m_body");

        static readonly List<float> Presses = new();
        static float _liftEnd = -1f, _targetY;
        static Vector3 _edgeDir;

        /// A jump press of the local player. Returns 1 when it started a lift.
        internal static int Pressed(Player p)
        {
            if (ModSettings.PoolJumpBoost == null || !ModSettings.PoolJumpBoost.Value || p == null || p != Player.m_localPlayer || p.IsDead()) return 0;
            Vector3 pos = p.transform.position;
            LiquidVolume v; float depth, surface;
            if (!TarPits.TrySample(pos, out v, out depth, out surface, LiquidType.All) || !TarPits.IsPumpPool(v) || depth < MinDepth || surface < pos.y + 0.2f)
            { if (Presses.Count > 0) ModLog.Verbose("PoolJump: jump outside a pump pool, count reset"); Presses.Clear(); return 0; }
            Vector3 dir;
            int dry = DryAround(v, pos, out dir);
            if (dry == 0) { if (Presses.Count > 0) ModLog.Verbose("PoolJump: jump in the middle of " + v.name + " (no edge within " + EdgeReach + " m), count reset"); Presses.Clear(); return 0; }
            float now = Time.time;
            Presses.Add(now);
            Presses.RemoveAll(t => now - t > PressWindow);
            int need = ModSettings.PoolJumpPresses.Value;
            ModLog.Verbose("PoolJump: jump " + Presses.Count + " of " + need + " at the edge of " + v.name + " (" + TarPits.LiquidOf(v) + ", " + depth.ToString("0.0") + " m deep, " + dry + " dry direction(s))");
            if (Presses.Count < need) return 0;
            Presses.Clear();
            _targetY = pos.y + ModSettings.PoolJumpHeight.Value;
            _edgeDir = dir;
            _liftEnd = now + MaxLift;
            ModLog.Info("PoolJump: lift out of " + v.name + " from y " + pos.y.ToString("0.0") + " to " + _targetY.ToString("0.0") + ", towards " + dir.ToString("0.0"));
            return 1;
        }

        /// How many of 8 directions reach a dry spot (or a spot outside the pool) within EdgeReach; `dir` = their average (flat, length 1).
        static int DryAround(LiquidVolume v, Vector3 pos, out Vector3 dir)
        {
            dir = Vector3.zero;
            int n = 0;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 0.25f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (float r = 0.5f; r <= EdgeReach + 0.001f; r += 0.5f)
                {
                    LiquidVolume v2; float depth, s;
                    var q = pos + d * r;
                    if (TarPits.TrySample(q, out v2, out depth, out s, LiquidType.All) && v2 == v && depth > 0.15f) continue;
                    dir += d; n++;
                    break;
                }
            }
            if (n > 0) { dir.y = 0f; dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.zero; }
            return n;
        }

        /// After the game's motion step of the local player: keeps the upward speed at what reaches the target height. Returns 1 while lifting.
        internal static int Lift(Character c)
        {
            if (_liftEnd < 0f || c == null || c != Player.m_localPlayer) return 0;
            var b = Body(c);
            float now = Time.time, left = _targetY - c.transform.position.y;
            if (b == null || now > _liftEnd || left < 0.05f)
            {
                ModLog.Verbose("PoolJump: lift ended (" + (b == null ? "no body" : now > _liftEnd ? "time" : "height reached") + ") at y " + c.transform.position.y.ToString("0.0"));
                _liftEnd = -1f; return 0;
            }
            var vel = b.linearVelocity;
            float up = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * left);         // the speed whose arc tops out at the target height
            if (vel.y < up) vel.y = up;
            var flat = new Vector3(vel.x, 0f, vel.z);
            if (Vector3.Dot(flat, _edgeDir) < Push) flat = flat - _edgeDir * Vector3.Dot(flat, _edgeDir) + _edgeDir * Push;
            b.linearVelocity = new Vector3(flat.x, vel.y, flat.z);
            if (!c.InLiquid())                                                     // out of the liquid: the arc carries the rest
            {
                ModLog.Verbose("PoolJump: out of the liquid at y " + c.transform.position.y.ToString("0.0") + ", speed up " + vel.y.ToString("0.0") + " m/s, the arc does the rest");
                _liftEnd = -1f;
            }
            return 1;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
        static class Patch_SetControls
        {
            static void Prefix(Player __instance, bool jump)
            {
                if (!jump) return;
                try { Pressed(__instance); }
                catch (System.NullReferenceException e) { ModLog.VerboseWarn("PoolJump: pool lookup failed (" + e.Message + "), jump not counted"); }
            }
        }

        [HarmonyPatch(typeof(Character), "UpdateMotion")]
        static class Patch_UpdateMotion
        {
            static void Postfix(Character __instance)
            {
                if (_liftEnd < 0f) return;
                try { Lift(__instance); }
                catch (System.MissingFieldException e) { ModLog.Warn("PoolJump: Character.m_body missing in this game version (" + e.Message + "), no lift"); _liftEnd = -1f; }
            }
        }
    }
}
