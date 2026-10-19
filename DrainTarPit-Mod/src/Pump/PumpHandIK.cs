using System.Collections.Generic;
using DrainTarPit.Logging;
using UnityEngine;

namespace DrainTarPit.Pump
{
    /// <summary>
    /// Keeps the mounted player's hands on the lever handle. The player clips are authored for an average body, so on
    /// other bodies (and with the lever moving) the hands drift through the handle. After the animators have run:
    ///   1. each hand gets a grip point on the handle bar (overhand: palm on top of the bar),
    ///   2. a two-bone IK (upper arm + forearm, humanoid bones) puts the wrist where the palm lands on that point,
    ///   3. half of the wrist roll is moved onto the forearm (no candy-wrapper wrist), the hand is turned palm-down with the
    ///      fingers pointing along the lever,
    ///   4. the four fingers curl around the bar.
    /// Done by rotating the bones in LateUpdate, so it does not depend on the player controller having an IK pass.
    /// Finger / thumb curl lives in PumpFingers, the back / knee reach bend and leg IK in PumpReachBend, the bone pose bookkeeping in PumpBones.
    ///
    /// Grip points = the lever's interact_point +- GripSpread along the handle (left/right is decided by where the
    /// player stands), lifted by half the bar height + GripPalmOffset, then shifted by GripUp / GripForward
    /// (all in the interact_point's own axes, metres).
    ///
    /// Handle axes (interact_point): right = along the bar, up = bar normal, forward = along the lever towards the pump column.
    /// </summary>
    internal static class PumpHandIK
    {
        static bool _warned;

        /// Players whose hands were pinned in the last frames (value = frame). Patch_NoLookIkOnPump skips the vanilla head / body look-at IK for them:
        /// that IK turns the spine and head towards the camera, which moved the shoulders every frame (flicker when the camera went to the side).
        internal static readonly Dictionary<Player, int> Pinned = new();
        internal static bool IsPinned(Player p) { int f; return p != null && Pinned.TryGetValue(p, out f) && Time.frameCount - f <= 3; }
        const float WristToPalm = 0.06f;        // fallback (no finger bones): the grip point is the palm, the hand bone sits at the wrist
        const float BarHalfHeight = 0.036f;     // the crossbar mesh is 7 cm tall
        const float PalmCentre = 0.6f;          // palm centre = this fraction of the way from the wrist to the middle knuckle
        const float WristTwistShare = 0.5f;     // how much of the hand's roll is taken by the forearm

        internal const float ReachComfort = 0.97f;       // AutoReach wants the wrist within this fraction of the full arm length; the IK never stretches the arm further than this (a fully straight arm makes the elbow direction flip)
        const float PoleSmooth = 14f;           // 1/s: how fast the elbow bend direction may change (low-pass, stops the elbow flicker)
        static readonly Dictionary<Transform, Vector3> Poles = new();   // last elbow bend direction per upper-arm bone

        /// Returns how many metres the wrist target is out of (comfortable) arm reach for the worse of the two hands
        /// (0 = both hands reach). PumpController uses it to lift the player (AutoReach).
        public static float Apply(Player p, Transform handle, float weight)
        {
            var a = PlayerPumpAnimations.AnimatorOf(p);
            if (a == null || !a.isHuman)
            {
                if (!_warned) { _warned = true; ModLog.Warn("Pump hand IK: player animator is not humanoid - hands will not be pinned to the lever."); }
                return 0f;
            }

            Pinned[p] = Time.frameCount;
            float spread = ModSettings.GripSpread.Value;
            Vector3 lift = handle.up * (BarHalfHeight + ModSettings.GripPalmOffset.Value + ModSettings.GripUp.Value);
            Vector3 centre = handle.position + lift + handle.forward * ModSettings.GripForward.Value;
            bool rightIsPlusX = Vector3.Dot(handle.right, p.transform.right) >= 0f;
            Vector3 rightGrip = centre + handle.right * (rightIsPlusX ? spread : -spread);
            Vector3 leftGrip = centre + handle.right * (rightIsPlusX ? -spread : spread);

            if (ModSettings.BendToReach.Value && weight > 0.001f) PumpReachBend.Apply(p, a, rightGrip, leftGrip, weight);

            float r = Hand(a, true, rightGrip, handle, weight);
            float l = Hand(a, false, leftGrip, handle, weight);
            return Mathf.Max(r, l);
        }

        /// Puts every bone we touched back to its original pose (called when the IK has blended out).
        public static void Release(Player p)
        {
            if (p != null) Pinned.Remove(p);
            if (Poles.Count > 0) { var dead = new List<Transform>(); foreach (var kv in Poles) if (kv.Key == null || (p != null && kv.Key.IsChildOf(p.transform))) dead.Add(kv.Key); foreach (var t in dead) Poles.Remove(t); }
            PumpFingers.Release(p);
            PumpReachBend.Release(p);
            PumpBones.Release(p);
        }

        static float Hand(Animator a, bool isRight, Vector3 palmTarget, Transform handle, float w)
        {
            var A = a.GetBoneTransform(isRight ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
            var B = a.GetBoneTransform(isRight ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
            var C = a.GetBoneTransform(isRight ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            if (A == null || B == null || C == null) return 0f;

            // desired hand frame: fingers along the lever (towards the pump column), palm facing down onto the bar
            Quaternion want = Quaternion.LookRotation(handle.forward, -handle.up);

            Vector3 f = Vector3.zero, n = Vector3.zero, palm = Vector3.zero;
            bool frame = ModSettings.HandRotate.Value && HandFrame(a, C, isRight, out f, out n, out palm);

            // 1) where must the wrist be so that the palm lands on the grip point once the hand is turned?
            Vector3 wrist;
            if (frame)
            {
                Quaternion turn = want * Quaternion.Inverse(Quaternion.LookRotation(f, n));
                wrist = palmTarget - turn * (palm - C.position);
            }
            else wrist = palmTarget - (C.position - B.position).normalized * WristToPalm;

            // 2) arm
            float short_ = Arm(A, B, C, wrist, w, a.transform.right, isRight);
            if (!frame) return short_;

            // 3) hand roll: the forearm takes part of it, the hand bone the rest
            if (HandFrame(a, C, isRight, out f, out n, out palm))
            {
                Quaternion roll = want * Quaternion.Inverse(Quaternion.LookRotation(f, n));
                Vector3 forearm = C.position - B.position;
                if (forearm.sqrMagnitude > 1e-6f)
                {
                    Quaternion twist = TwistAround(roll, forearm.normalized);
                    B.rotation = Quaternion.Slerp(Quaternion.identity, twist, WristTwistShare * w) * B.rotation;   // moves the hand along
                }
            }
            if (HandFrame(a, C, isRight, out f, out n, out palm))
            {
                Quaternion rest = want * Quaternion.Inverse(Quaternion.LookRotation(f, n));
                C.rotation = Quaternion.Slerp(Quaternion.identity, rest, w) * C.rotation;
            }

            // 4) fingers: curl around the bar, axis = across the knuckles (finger direction x palm normal)
            float curl = ModSettings.FingerCurl.Value * w;
            if (curl > 0.001f && HandFrame(a, C, isRight, out f, out n, out palm))
                PumpFingers.Curl(a, isRight, f, n, curl);

            // 5) thumb: swung away from the fist, then curled down and under the bar (opposing the fingers)
            float tc = ModSettings.ThumbCurl.Value * w, ts = ModSettings.ThumbSpread.Value * w;
            if ((tc > 0.001f || ts > 0.001f) && HandFrame(a, C, isRight, out f, out n, out palm))
                PumpFingers.Thumb(a, isRight, f, n, tc, ts);
            return short_;
        }

        /// Finger direction f (wrist -> middle knuckle), palm normal n (out of the palm) and palm centre, from the humanoid
        /// finger bones. false when the rig has no finger bones.
        static bool HandFrame(Animator a, Transform hand, bool isRight, out Vector3 f, out Vector3 n, out Vector3 palm)
        {
            f = n = Vector3.zero; palm = hand.position;
            var mid = a.GetBoneTransform(isRight ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
            var idx = a.GetBoneTransform(isRight ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
            var lit = a.GetBoneTransform(isRight ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
            if (mid == null || idx == null || lit == null) return false;

            Vector3 toMid = mid.position - hand.position;
            if (toMid.sqrMagnitude < 1e-6f) return false;
            f = toMid.normalized;
            Vector3 across = idx.position - lit.position;                 // little finger -> index finger
            n = Vector3.Cross(f, across);                                 // right hand: out of the palm; the left hand is mirrored
            if (n.sqrMagnitude < 1e-8f) return false;
            n.Normalize();
            if (!isRight) n = -n;
            palm = hand.position + toMid * PalmCentre;
            return true;
        }

        /// The part of rotation q that turns around `axis` (swing-twist split).
        static Quaternion TwistAround(Quaternion q, Vector3 axis)
        {
            Vector3 r = new Vector3(q.x, q.y, q.z);
            Vector3 proj = Vector3.Project(r, axis);
            float m = Mathf.Sqrt(proj.x * proj.x + proj.y * proj.y + proj.z * proj.z + q.w * q.w);
            if (m < 1e-6f) return Quaternion.identity;
            return new Quaternion(proj.x / m, proj.y / m, proj.z / m, q.w / m);
        }

        /// Two-bone IK: swings the upper arm and forearm so the wrist (hand bone) ends up on `wrist`.
        static float Arm(Transform A, Transform B, Transform C, Vector3 wristTarget, float w, Vector3 side, bool isRight)
        {
            Vector3 pa = A.position, pb = B.position, pc = C.position;
            float l1 = (pb - pa).magnitude, l2 = (pc - pb).magnitude;
            if (l1 < 0.01f || l2 < 0.01f) return 0f;

            Vector3 toT = wristTarget - pa;
            float shortfall = Mathf.Max(0f, toT.magnitude - (l1 + l2) * ReachComfort);
            float d = Mathf.Clamp(toT.magnitude, 0.05f, (l1 + l2) * ReachComfort);
            Vector3 dir = toT.sqrMagnitude > 1e-8f ? toT.normalized : (pc - pa).normalized;

            // elbow bend direction: purely geometric (elbow down and a little outwards), NOT read from the animated arm. The animated arm
            // is moved around by the camera look-at / back bend every frame, which made the elbow jump. Low-pass filtered on top.
            Vector3 outward = (isRight ? 1f : -1f) * side;
            Vector3 hint = Vector3.ProjectOnPlane(Vector3.down + outward * 0.45f, dir);
            if (hint.sqrMagnitude < 1e-6f) hint = Vector3.ProjectOnPlane(outward, dir);
            if (hint.sqrMagnitude < 1e-6f) hint = Vector3.ProjectOnPlane(-A.up, dir);
            hint.Normalize();
            Vector3 want = hint;
            Vector3 pole;
            if (Poles.TryGetValue(A, out pole) && pole.sqrMagnitude > 1e-6f)
            {
                float k = 1f - Mathf.Exp(-PoleSmooth * Mathf.Max(Time.deltaTime, 0.001f));
                pole = Vector3.Slerp(pole, want, k);
            }
            else pole = want;
            pole = Vector3.ProjectOnPlane(pole, dir);
            if (pole.sqrMagnitude < 1e-6f) pole = hint;
            pole.Normalize();
            Poles[A] = pole;

            float cosA = Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f);
            float sinA = Mathf.Sqrt(1f - cosA * cosA);
            Vector3 elbow = pa + (dir * cosA + pole * sinA) * l1;
            Vector3 wrist = pa + dir * d;

            // 1) upper arm: swing the elbow onto its new place
            Quaternion r1 = Quaternion.FromToRotation(pb - pa, elbow - pa);
            A.rotation = Quaternion.Slerp(Quaternion.identity, r1, w) * A.rotation;

            // 2) forearm: swing the wrist onto the target (the elbow has moved, so measure from where it is now)
            Vector3 b2 = B.position;
            Quaternion r2 = Quaternion.FromToRotation(C.position - b2, wrist - b2);
            B.rotation = Quaternion.Slerp(Quaternion.identity, r2, w) * B.rotation;
            return shortfall;
        }
    }
}
