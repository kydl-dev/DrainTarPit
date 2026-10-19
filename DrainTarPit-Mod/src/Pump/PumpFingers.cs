using System.Collections.Generic;
using UnityEngine;

namespace DrainTarPit.Pump
{
    /// Fingers and thumb of a hand pinned to the lever: the four fingers curl around the bar, the thumb swings away from the fist and curls under it.
    internal static class PumpFingers
    {
        // finger bone chains that curl (index .. little); the thumb keeps its animation pose
        static readonly HumanBodyBones[][] RightFingers =
        {
            new[] { HumanBodyBones.RightIndexProximal,  HumanBodyBones.RightIndexIntermediate,  HumanBodyBones.RightIndexDistal },
            new[] { HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal },
            new[] { HumanBodyBones.RightRingProximal,   HumanBodyBones.RightRingIntermediate,   HumanBodyBones.RightRingDistal },
            new[] { HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal },
        };
        static readonly HumanBodyBones[][] LeftFingers =
        {
            new[] { HumanBodyBones.LeftIndexProximal,  HumanBodyBones.LeftIndexIntermediate,  HumanBodyBones.LeftIndexDistal },
            new[] { HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal },
            new[] { HumanBodyBones.LeftRingProximal,   HumanBodyBones.LeftRingIntermediate,   HumanBodyBones.LeftRingDistal },
            new[] { HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal },
        };
        static readonly HumanBodyBones[] RightThumb = { HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal };
        static readonly HumanBodyBones[] LeftThumb  = { HumanBodyBones.LeftThumbProximal,  HumanBodyBones.LeftThumbIntermediate,  HumanBodyBones.LeftThumbDistal };
        static readonly float[] ThumbAngles = { 25f, 35f, 30f };    // degrees per joint at ThumbCurl = 1
        static readonly Dictionary<Transform, float> ThumbSign = new();   // which way is "away from the fist" for a thumb bone (decided once)
        // curl per joint (degrees at FingerCurl = 1): knuckle, middle joint, fingertip joint; the little finger wraps a bit more
        static readonly float[] CurlAngles = { 70f, 80f, 40f };
        static readonly float[] FingerScale = { 0.9f, 1f, 1f, 1.1f };

        /// Curls the four fingers about the knuckle axis (finger direction f x palm normal n).
        public static void Curl(Animator a, bool isRight, Vector3 f, Vector3 n, float curl)
        {
            Vector3 axis = Vector3.Cross(f, n).normalized;       // rotating by + about this axis swings the fingertips towards the palm side
            var chains = isRight ? RightFingers : LeftFingers;
            for (int i = 0; i < chains.Length; i++)
                for (int j = 0; j < chains[i].Length; j++)
                {
                    var bone = a.GetBoneTransform(chains[i][j]);
                    if (bone != null) PumpBones.ApplyDelta(bone, Quaternion.AngleAxis(CurlAngles[j] * FingerScale[i] * curl, axis));
                }
        }

        /// Thumb: the proximal bone is swung `spread` degrees away from the fist about the palm normal, then all three joints
        /// curl about the knuckle axis (same axis as the fingers) so the tip ends up under the bar.
        public static void Thumb(Animator a, bool isRight, Vector3 f, Vector3 n, float curl, float spread)
        {
            var chain = isRight ? RightThumb : LeftThumb;
            var b0 = a.GetBoneTransform(chain[0]); if (b0 == null) return;
            var idx = a.GetBoneTransform(isRight ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
            var lit = a.GetBoneTransform(isRight ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
            Vector3 axis = Vector3.Cross(f, n).normalized;

            for (int j = 0; j < chain.Length; j++)
            {
                var bone = a.GetBoneTransform(chain[j]); if (bone == null) continue;
                Quaternion delta = Quaternion.AngleAxis(ThumbAngles[j] * curl, axis);
                if (j == 0 && spread > 0.001f && idx != null && lit != null)
                {
                    float sign;
                    if (!ThumbSign.TryGetValue(b0, out sign))
                    {
                        // decide once: rotate a little about the palm normal, keep the direction that moves the thumb towards the thumb side of the hand
                        var tip = a.GetBoneTransform(chain[2]) ?? a.GetBoneTransform(chain[1]);
                        Vector3 side = (idx.position - lit.position).normalized;
                        Vector3 d = (tip != null ? tip.position : b0.position + f * 0.05f) - b0.position;
                        sign = Vector3.Dot(Quaternion.AngleAxis(10f, n) * d, side) >= Vector3.Dot(d, side) ? 1f : -1f;
                        ThumbSign[b0] = sign;
                    }
                    delta = Quaternion.AngleAxis(sign * spread, n) * delta;
                }
                PumpBones.ApplyDelta(bone, delta);
            }
        }

        /// Forgets the thumb decisions of player p (or all of them when p is null).
        public static void Release(Player p)
        {
            if (ThumbSign.Count == 0) return;
            var dead = new List<Transform>();
            foreach (var kv in ThumbSign) if (kv.Key == null || (p != null && kv.Key.IsChildOf(p.transform))) dead.Add(kv.Key);
            foreach (var t in dead) ThumbSign.Remove(t);
        }
    }
}
