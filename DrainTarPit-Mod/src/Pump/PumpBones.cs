using System.Collections.Generic;
using UnityEngine;

namespace DrainTarPit.Pump
{
    /// Bones that the hand IK, the fingers and the reach bend rotate on top of the animator's pose. If the player's animator does not rewrite a
    /// bone every frame the rotation would pile up, so the bone's original local rotation is remembered and put back first whenever the bone
    /// still holds exactly what we set last time.
    internal static class PumpBones
    {
        sealed class Saved { public Quaternion Base, Applied; }
        static readonly Dictionary<Transform, Saved> Memo = new();

        /// Puts a bone back to the animator's pose when it still holds exactly what we set last frame (the animator did not rewrite it).
        public static void Begin(Transform t)
        {
            Saved s;
            if (Memo.TryGetValue(t, out s))
            {
                if (Quaternion.Angle(t.localRotation, s.Applied) < 0.01f) t.localRotation = s.Base; else s.Base = t.localRotation;
            }
            else Memo[t] = new Saved { Base = t.localRotation, Applied = t.localRotation };
        }

        public static void End(Transform t) { Saved s; if (Memo.TryGetValue(t, out s)) s.Applied = t.localRotation; }

        /// Rotates a bone by a world-space delta on top of the animator's pose.
        public static void ApplyDelta(Transform t, Quaternion worldDelta)
        {
            Saved s;
            if (Memo.TryGetValue(t, out s))
            {
                if (Quaternion.Angle(t.localRotation, s.Applied) < 0.01f) t.localRotation = s.Base;   // the animator did not rewrite it: start from the original pose
                else s.Base = t.localRotation;                                                           // animator wrote a fresh pose: that is the new base
            }
            else { s = new Saved { Base = t.localRotation }; Memo[t] = s; }
            t.rotation = worldDelta * t.rotation;
            s.Applied = t.localRotation;
        }

        /// Puts every bone we touched back to its original pose (the bones of player p, or all of them when p is null).
        public static void Release(Player p)
        {
            if (Memo.Count == 0) return;
            var gone = new List<Transform>();
            foreach (var kv in Memo)
            {
                var t = kv.Key;
                if (t == null) { gone.Add(t); continue; }
                if (p != null && !t.IsChildOf(p.transform)) continue;
                if (Quaternion.Angle(t.localRotation, kv.Value.Applied) < 0.01f) t.localRotation = kv.Value.Base;
                gone.Add(t);
            }
            foreach (var t in gone) Memo.Remove(t);
        }
    }
}
