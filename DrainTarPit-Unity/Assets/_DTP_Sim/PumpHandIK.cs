using System.Collections.Generic;
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
        internal static readonly Dictionary<Player, int> Pinned = new Dictionary<Player, int>();
        internal static bool IsPinned(Player p) { int f; return p != null && Pinned.TryGetValue(p, out f) && Time.frameCount - f <= 3; }
        const float WristToPalm = 0.06f;        // fallback (no finger bones): the grip point is the palm, the hand bone sits at the wrist
        const float BarHalfHeight = 0.036f;     // the crossbar mesh is 7 cm tall
        const float PalmCentre = 0.6f;          // palm centre = this fraction of the way from the wrist to the middle knuckle
        const float WristTwistShare = 0.5f;     // how much of the hand's roll is taken by the forearm

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
        static readonly Dictionary<Transform, float> ThumbSign = new Dictionary<Transform, float>();   // which way is "away from the fist" for a thumb bone (decided once)
        // curl per joint (degrees at FingerCurl = 1): knuckle, middle joint, fingertip joint; the little finger wraps a bit more
        static readonly float[] CurlAngles = { 70f, 80f, 40f };
        static readonly float[] FingerScale = { 0.9f, 1f, 1f, 1.1f };

        // Finger bones are rotated on top of whatever the animator wrote this frame. If the player's animator does not
        // rewrite a finger bone every frame the rotation would pile up, so the bone's original local rotation is
        // remembered and put back first whenever the bone still holds exactly what we set last time.
        sealed class Saved { public Quaternion Base, Applied; }
        static readonly Dictionary<Transform, Saved> Fingers = new();

        const float ReachComfort = 0.97f;       // AutoReach wants the wrist within this fraction of the full arm length; the IK never stretches the arm further than this (a fully straight arm makes the elbow direction flip)
        const float PoleSmooth = 14f;           // 1/s: how fast the elbow bend direction may change (low-pass, stops the elbow flicker)
        static readonly Dictionary<Transform, Vector3> Poles = new Dictionary<Transform, Vector3>();   // last elbow bend direction per upper-arm bone

        /// Returns how many metres the wrist target is out of (comfortable) arm reach for the worse of the two hands
        /// (0 = both hands reach). PumpController uses it to lift the player (AutoReach).
        public static float Apply(Player p, Transform handle, float weight)
        {
            var a = PlayerPumpAnimations.AnimatorOf(p);
            if (a == null || !a.isHuman)
            {
                if (!_warned) { _warned = true; Plugin.Log.LogWarning("Pump hand IK: player animator is not humanoid - hands will not be pinned to the lever."); }
                return 0f;
            }

            Pinned[p] = Time.frameCount;
            float spread = ModSettings.GripSpread.Value;
            Vector3 lift = handle.up * (BarHalfHeight + ModSettings.GripPalmOffset.Value + ModSettings.GripUp.Value);
            Vector3 centre = handle.position + lift + handle.forward * ModSettings.GripForward.Value;
            bool rightIsPlusX = Vector3.Dot(handle.right, p.transform.right) >= 0f;
            Vector3 rightGrip = centre + handle.right * (rightIsPlusX ? spread : -spread);
            Vector3 leftGrip = centre + handle.right * (rightIsPlusX ? -spread : spread);

            if (ModSettings.BendToReach.Value && weight > 0.001f) ReachBend(p, a, rightGrip, leftGrip, weight);

            float r = Hand(a, true, rightGrip, handle, weight);
            float l = Hand(a, false, leftGrip, handle, weight);
            return Mathf.Max(r, l);
        }

        /// Puts every finger bone we touched back to its original pose (called when the IK has blended out).
        public static void Release(Player p)
        {
            if (p != null) Pinned.Remove(p);
            if (Poles.Count > 0) { var dead = new List<Transform>(); foreach (var kv in Poles) if (kv.Key == null || (p != null && kv.Key.IsChildOf(p.transform))) dead.Add(kv.Key); foreach (var t in dead) Poles.Remove(t); }
            if (ThumbSign.Count > 0) { var dead2 = new List<Transform>(); foreach (var kv in ThumbSign) if (kv.Key == null || (p != null && kv.Key.IsChildOf(p.transform))) dead2.Add(kv.Key); foreach (var t in dead2) ThumbSign.Remove(t); }
            if (BendState.Count > 0) { var dead3 = new List<Animator>(); foreach (var kv in BendState) if (kv.Key == null || (p != null && kv.Key.transform.IsChildOf(p.transform))) dead3.Add(kv.Key); foreach (var k in dead3) BendState.Remove(k); }
            if (HipPos.Count > 0)
            {
                var deadH = new List<Transform>();
                foreach (var kv in HipPos)
                {
                    var t = kv.Key;
                    if (t == null) { deadH.Add(t); continue; }
                    if (p != null && !t.IsChildOf(p.transform)) continue;
                    if ((t.localPosition - kv.Value[1]).sqrMagnitude < 1e-8f) t.localPosition = kv.Value[0];
                    deadH.Add(t);
                }
                foreach (var t in deadH) HipPos.Remove(t);
            }
            if (Fingers.Count == 0) return;
            var gone = new List<Transform>();
            foreach (var kv in Fingers)
            {
                var t = kv.Key;
                if (t == null) { gone.Add(t); continue; }
                if (p != null && !t.IsChildOf(p.transform)) continue;
                if (Quaternion.Angle(t.localRotation, kv.Value.Applied) < 0.01f) t.localRotation = kv.Value.Base;
                gone.Add(t);
            }
            foreach (var t in gone) Fingers.Remove(t);
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
            {
                Vector3 axis = Vector3.Cross(f, n).normalized;       // rotating by + about this axis swings the fingertips towards the palm side
                var chains = isRight ? RightFingers : LeftFingers;
                for (int i = 0; i < chains.Length; i++)
                    for (int j = 0; j < chains[i].Length; j++)
                    {
                        var bone = a.GetBoneTransform(chains[i][j]);
                        if (bone != null) CurlBone(bone, axis, CurlAngles[j] * FingerScale[i] * curl);
                    }
            }

            // 5) thumb: swung away from the fist, then curled down and under the bar (opposing the fingers)
            float tc = ModSettings.ThumbCurl.Value * w, ts = ModSettings.ThumbSpread.Value * w;
            if ((tc > 0.001f || ts > 0.001f) && HandFrame(a, C, isRight, out f, out n, out palm))
                Thumb(a, isRight, f, n, tc, ts);
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

        static void CurlBone(Transform t, Vector3 axis, float degrees) { ApplyDelta(t, Quaternion.AngleAxis(degrees, axis)); }

        /// Rotates a finger/thumb bone by a world-space delta on top of the animator's pose (see Saved).
        static void ApplyDelta(Transform t, Quaternion worldDelta)
        {
            Saved s;
            if (Fingers.TryGetValue(t, out s))
            {
                if (Quaternion.Angle(t.localRotation, s.Applied) < 0.01f) t.localRotation = s.Base;   // the animator did not rewrite it: start from the original pose
                else s.Base = t.localRotation;                                                           // animator wrote a fresh pose: that is the new base
            }
            else { s = new Saved { Base = t.localRotation }; Fingers[t] = s; }
            t.rotation = worldDelta * t.rotation;
            s.Applied = t.localRotation;
        }

        /// Thumb: the proximal bone is swung `spread` degrees away from the fist about the palm normal, then all three joints
        /// curl about the knuckle axis (same axis as the fingers) so the tip ends up under the bar.
        static void Thumb(Animator a, bool isRight, Vector3 f, Vector3 n, float curl, float spread)
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
                ApplyDelta(bone, delta);
            }
        }

        // --- reach assist: bend the back, then the knees ----------------------------------------------------------------------
        // The lever handle is out of arm's reach at the bottom of the stroke. Before the arm IK runs, the shoulders are brought to
        // the handle: the spine chain bends forward (up to MaxBackBend degrees, spread over Spine / Chest / UpperChest), and if that is
        // still not enough the hips sink (MaxKneeBend m) with the feet kept on the ground by a two-bone leg IK. The amounts are found
        // per frame (bisection on how far the hands are out of reach) and smoothed, so the player follows the lever down and up.
        static readonly Dictionary<Animator, float[]> BendState = new();                // smoothed { back degrees, hip drop m }
        static readonly Dictionary<Transform, Vector3[]> HipPos = new();                // hips: { animator's local position, what we set }
        static readonly Transform[] BB = new Transform[3];
        static readonly float[] BW = { 0.4f, 0.35f, 0.25f };
        static readonly Quaternion[] BSnap = new Quaternion[3];
        const float ReachMargin = 0.03f;        // keep the arms this far from straight (m)
        const float PalmBeyondWrist = 0.05f;    // the grip point is on the palm, a bit beyond the wrist

        // puts a bone back to the animator's pose when it still holds exactly what we set last frame (the animator did not rewrite it)
        static void Begin(Transform t)
        {
            Saved s;
            if (Fingers.TryGetValue(t, out s))
            {
                if (Quaternion.Angle(t.localRotation, s.Applied) < 0.01f) t.localRotation = s.Base; else s.Base = t.localRotation;
            }
            else Fingers[t] = new Saved { Base = t.localRotation, Applied = t.localRotation };
        }
        static void End(Transform t) { Saved s; if (Fingers.TryGetValue(t, out s)) s.Applied = t.localRotation; }

        static float OutOfReach(Transform A, Transform B, Transform C, Vector3 grip)
        {
            float l1 = (B.position - A.position).magnitude, l2 = (C.position - B.position).magnitude;
            return (grip - A.position).magnitude - ((l1 + l2) * ReachComfort + PalmBeyondWrist);
        }

        static void BendPose(Transform hips, Vector3 hipsLocal, Vector3 axis, float degrees, float drop)
        {
            hips.localPosition = hipsLocal;
            if (drop > 0f) hips.position -= Vector3.up * drop;
            for (int i = 0; i < 3; i++) if (BB[i] != null) BB[i].localRotation = BSnap[i];
            if (degrees != 0f)
                for (int i = 0; i < 3; i++) if (BB[i] != null) BB[i].rotation = Quaternion.AngleAxis(degrees * BW[i], axis) * BB[i].rotation;
        }

        static void ReachBend(Player p, Animator a, Vector3 rightGrip, Vector3 leftGrip, float w)
        {
            var hips = a.GetBoneTransform(HumanBodyBones.Hips);
            BB[0] = a.GetBoneTransform(HumanBodyBones.Spine); BB[1] = a.GetBoneTransform(HumanBodyBones.Chest); BB[2] = a.GetBoneTransform(HumanBodyBones.UpperChest);
            var rA = a.GetBoneTransform(HumanBodyBones.RightUpperArm); var rB = a.GetBoneTransform(HumanBodyBones.RightLowerArm); var rC = a.GetBoneTransform(HumanBodyBones.RightHand);
            var lA = a.GetBoneTransform(HumanBodyBones.LeftUpperArm); var lB = a.GetBoneTransform(HumanBodyBones.LeftLowerArm); var lC = a.GetBoneTransform(HumanBodyBones.LeftHand);
            if (hips == null || BB[0] == null || rA == null || rB == null || rC == null || lA == null || lB == null || lC == null) return;
            float maxDeg = ModSettings.MaxBackBend.Value, maxDrop = ModSettings.MaxKneeBend.Value;
            if (maxDeg <= 0.01f && maxDrop <= 0.001f) return;

            // legs: remember where the feet are before anything moves
            var uL = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg); var kL = a.GetBoneTransform(HumanBodyBones.LeftLowerLeg); var fL = a.GetBoneTransform(HumanBodyBones.LeftFoot);
            var uR = a.GetBoneTransform(HumanBodyBones.RightUpperLeg); var kR = a.GetBoneTransform(HumanBodyBones.RightLowerLeg); var fR = a.GetBoneTransform(HumanBodyBones.RightFoot);
            bool legs = uL != null && kL != null && fL != null && uR != null && kR != null && fR != null;

            // the animator's pose (undo what we added last frame if the animator did not rewrite the bones)
            Vector3[] hp;
            if (HipPos.TryGetValue(hips, out hp)) { if ((hips.localPosition - hp[1]).sqrMagnitude < 1e-8f) hips.localPosition = hp[0]; else hp[0] = hips.localPosition; }
            else HipPos[hips] = hp = new[] { hips.localPosition, hips.localPosition };
            for (int i = 0; i < 3; i++) if (BB[i] != null) Begin(BB[i]);
            if (legs) { Begin(uL); Begin(kL); Begin(fL); Begin(uR); Begin(kR); Begin(fR); }
            Vector3 fLp = legs ? fL.position : Vector3.zero, fRp = legs ? fR.position : Vector3.zero;
            Quaternion fLr = legs ? fL.rotation : Quaternion.identity, fRr = legs ? fR.rotation : Quaternion.identity;

            Vector3 hipsLocal = hips.localPosition;
            for (int i = 0; i < 3; i++) BSnap[i] = BB[i] != null ? BB[i].localRotation : Quaternion.identity;
            Vector3 right = p.transform.right;

            // forward bend = positive rotation about the player's right axis (Unity: AngleAxis(+, right) tips "up" towards "forward").
            // Fixed on purpose: the old "try both directions, keep the closer one" decision flipped from frame to frame when the camera
            // twisted the torso, and the whole back snapped between leaning forwards and backwards (the flicker).
            Vector3 axis = right;

            float wantDeg = 0f, wantDrop = 0f;
            BendPose(hips, hipsLocal, axis, 0f, 0f);
            float S0 = Mathf.Max(OutOfReach(rA, rB, rC, rightGrip), OutOfReach(lA, lB, lC, leftGrip));
            if (S0 > -ReachMargin)
            {
                BendPose(hips, hipsLocal, axis, maxDeg, 0f);
                float S1 = Mathf.Max(OutOfReach(rA, rB, rC, rightGrip), OutOfReach(lA, lB, lC, leftGrip));
                if (S1 <= -ReachMargin)
                {
                    float lo = 0f, hi = maxDeg;
                    for (int it = 0; it < 8; it++)
                    {
                        float mid = 0.5f * (lo + hi);
                        BendPose(hips, hipsLocal, axis, mid, 0f);
                        float s = Mathf.Max(OutOfReach(rA, rB, rC, rightGrip), OutOfReach(lA, lB, lC, leftGrip));
                        if (s > -ReachMargin) lo = mid; else hi = mid;
                    }
                    wantDeg = hi;
                }
                else
                {
                    wantDeg = maxDeg;
                    if (maxDrop > 0.001f && legs)
                    {
                        BendPose(hips, hipsLocal, axis, maxDeg, maxDrop);
                        float S2 = Mathf.Max(OutOfReach(rA, rB, rC, rightGrip), OutOfReach(lA, lB, lC, leftGrip));
                        if (S2 > -ReachMargin) wantDrop = maxDrop;
                        else
                        {
                            float lo = 0f, hi = maxDrop;
                            for (int it = 0; it < 8; it++)
                            {
                                float mid = 0.5f * (lo + hi);
                                BendPose(hips, hipsLocal, axis, maxDeg, mid);
                                float s = Mathf.Max(OutOfReach(rA, rB, rC, rightGrip), OutOfReach(lA, lB, lC, leftGrip));
                                if (s > -ReachMargin) lo = mid; else hi = mid;
                            }
                            wantDrop = hi;
                        }
                    }
                }
            }

            // smooth (the lever moves fast), then apply
            float[] st;
            if (!BendState.TryGetValue(a, out st)) BendState[a] = st = new float[2];
            float k = 1f - Mathf.Exp(-10f * Mathf.Max(Time.deltaTime, 0.001f));
            st[0] = Mathf.Lerp(st[0], wantDeg, k); st[1] = Mathf.Lerp(st[1], wantDrop, k);
            float deg = st[0] * w, drop = st[1] * w;
            BendPose(hips, hipsLocal, axis, deg, drop);
            hp[1] = hips.localPosition;
            for (int i = 0; i < 3; i++) if (BB[i] != null) End(BB[i]);

            if (legs && drop > 0.002f)
            {
                Vector3 fwd = Vector3.ProjectOnPlane(p.transform.forward, Vector3.up);
                if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
                Leg(uL, kL, fL, fLp, fLr, fwd); Leg(uR, kR, fR, fRp, fRr, fwd);
            }
            if (legs) { End(uL); End(kL); End(fL); End(uR); End(kR); End(fR); }
        }

        /// Two-bone leg IK: the hips have sunk, so thigh and shin swing (knee forward) to keep the foot where it was.
        static void Leg(Transform U, Transform L, Transform F, Vector3 foot, Quaternion footRot, Vector3 kneeDir)
        {
            Vector3 pa = U.position, pb = L.position, pc = F.position;
            float l1 = (pb - pa).magnitude, l2 = (pc - pb).magnitude;
            if (l1 < 0.01f || l2 < 0.01f) return;
            Vector3 toT = foot - pa;
            float d = Mathf.Clamp(toT.magnitude, 0.05f, (l1 + l2) * 0.999f);
            Vector3 dir = toT.sqrMagnitude > 1e-8f ? toT.normalized : (pc - pa).normalized;
            Vector3 pole = Vector3.ProjectOnPlane(kneeDir, dir);
            if (pole.sqrMagnitude < 1e-6f) return;
            pole.Normalize();
            float cosA = Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f);
            float sinA = Mathf.Sqrt(1f - cosA * cosA);
            Vector3 knee = pa + (dir * cosA + pole * sinA) * l1;
            Vector3 ankle = pa + dir * d;
            U.rotation = Quaternion.FromToRotation(pb - pa, knee - pa) * U.rotation;
            Vector3 b2 = L.position;
            L.rotation = Quaternion.FromToRotation(F.position - b2, ankle - b2) * L.rotation;
            F.rotation = footRot;
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
