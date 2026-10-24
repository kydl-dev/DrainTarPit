using System.Collections.Generic;
using Kydl.Common;
using UnityEngine;

namespace DrainTarPit.Pump
{
    /// Reach assist for the hand IK: bend the back, then the knees. The lever handle is out of arm's reach at the bottom of the stroke. Before
    /// the arm IK runs, the shoulders are brought to the handle: the spine chain bends forward (up to MaxBackBend degrees, spread over Spine /
    /// Chest / UpperChest), and if that is still not enough the hips sink (MaxKneeBend m) with the feet kept on the ground by a two-bone leg IK.
    /// The amounts are found per frame (bisection on how far the hands are out of reach) and smoothed, so the player follows the lever.
    internal static class PumpReachBend
    {
        static readonly Dictionary<Animator, float[]> BendState = new();                // smoothed { back degrees, hip drop m, hip relax }
        sealed class HipRefState { public Vector3 Pos; public Quaternion Rot; }
        static readonly Dictionary<Animator, HipRefState> HipRef = new();               // slow average of the animated hips (local), the spot the hips are held at
        static Quaternion _hipsRot = Quaternion.identity;                               // hips local rotation BendPose sets (the locked one)
        const float HipRefSeconds = 1.2f;                                               // time constant of that average (longer than a stroke)
        static float _nextReachLog;
        static readonly Dictionary<Transform, Vector3[]> HipPos = new();                // hips: { animator's local position, what we set }
        static readonly Transform[] BB = new Transform[3];
        static readonly float[] BW = { 0.4f, 0.35f, 0.25f };
        static readonly Quaternion[] BSnap = new Quaternion[3];
        const float ReachMargin = 0.03f;        // keep the arms this far from straight (m)
        const float PalmBeyondWrist = 0.05f;    // the grip point is on the palm, a bit beyond the wrist

        static float OutOfReach(Transform A, Transform B, Transform C, Vector3 grip)
        {
            float l1 = (B.position - A.position).magnitude, l2 = (C.position - B.position).magnitude;
            return (grip - A.position).magnitude - ((l1 + l2) * PumpHandIK.ReachComfort + PalmBeyondWrist);
        }

        static void BendPose(Transform hips, Vector3 hipsLocal, Vector3 axis, float degrees, float drop)
        {
            hips.localPosition = hipsLocal;
            hips.localRotation = _hipsRot;
            if (drop > 0f) hips.position -= Vector3.up * drop;
            for (int i = 0; i < 3; i++) if (BB[i] != null) BB[i].localRotation = BSnap[i];
            if (degrees != 0f)
                for (int i = 0; i < 3; i++) if (BB[i] != null) BB[i].rotation = Quaternion.AngleAxis(degrees * BW[i], axis) * BB[i].rotation;
        }

        public static void Apply(Player p, Animator a, Vector3 rightGrip, Vector3 leftGrip, float w)
        {
            var hips = a.GetBoneTransform(HumanBodyBones.Hips);
            BB[0] = a.GetBoneTransform(HumanBodyBones.Spine); BB[1] = a.GetBoneTransform(HumanBodyBones.Chest); BB[2] = a.GetBoneTransform(HumanBodyBones.UpperChest);
            var rA = a.GetBoneTransform(HumanBodyBones.RightUpperArm); var rB = a.GetBoneTransform(HumanBodyBones.RightLowerArm); var rC = a.GetBoneTransform(HumanBodyBones.RightHand);
            var lA = a.GetBoneTransform(HumanBodyBones.LeftUpperArm); var lB = a.GetBoneTransform(HumanBodyBones.LeftLowerArm); var lC = a.GetBoneTransform(HumanBodyBones.LeftHand);
            if (hips == null || BB[0] == null || rA == null || rB == null || rC == null || lA == null || lB == null || lC == null) return;
            float maxDeg = ModSettings.MaxBackBend.Value, maxDrop = ModSettings.MaxKneeBend.Value, hipLock = Mathf.Clamp01(ModSettings.HipLock.Value);
            if (maxDeg <= 0.01f && maxDrop <= 0.001f && hipLock <= 0.001f) return;

            // legs: remember where the feet are before anything moves
            var uL = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg); var kL = a.GetBoneTransform(HumanBodyBones.LeftLowerLeg); var fL = a.GetBoneTransform(HumanBodyBones.LeftFoot);
            var uR = a.GetBoneTransform(HumanBodyBones.RightUpperLeg); var kR = a.GetBoneTransform(HumanBodyBones.RightLowerLeg); var fR = a.GetBoneTransform(HumanBodyBones.RightFoot);
            bool legs = uL != null && kL != null && fL != null && uR != null && kR != null && fR != null;

            // the animator's pose (undo what we added last frame if the animator did not rewrite the bones)
            Vector3[] hp;
            if (HipPos.TryGetValue(hips, out hp)) { if ((hips.localPosition - hp[1]).sqrMagnitude < 1e-8f) hips.localPosition = hp[0]; else hp[0] = hips.localPosition; }
            else HipPos[hips] = hp = new[] { hips.localPosition, hips.localPosition };
            PumpBones.Begin(hips);
            for (int i = 0; i < 3; i++) if (BB[i] != null) PumpBones.Begin(BB[i]);
            if (legs) { PumpBones.Begin(uL); PumpBones.Begin(kL); PumpBones.Begin(fL); PumpBones.Begin(uR); PumpBones.Begin(kR); PumpBones.Begin(fR); }
            Vector3 fLp = legs ? fL.position : Vector3.zero, fRp = legs ? fR.position : Vector3.zero;
            Quaternion fLr = legs ? fL.rotation : Quaternion.identity, fRr = legs ? fR.rotation : Quaternion.identity;

            Vector3 hipsPos0 = hips.position; Quaternion hipsRot0 = hips.rotation;      // the animator's hips (world), to see later whether the legs need IK
            Vector3 hipsLocal = hips.localPosition; _hipsRot = hips.localRotation;
            if (hipLock > 0.001f)
            {
                // Hip lock: the clips swing the hips with every stroke. A slow average of the animated hips is the calm spot; the hips are held
                // there (HipLock = how much) so the stroke comes from the upper body (spine bend + arms), and the leg IK below keeps the feet planted.
                HipRefState hr;
                if (!HipRef.TryGetValue(a, out hr)) HipRef[a] = hr = new HipRefState { Pos = hipsLocal, Rot = _hipsRot };
                float kr = 1f - Mathf.Exp(-Mathf.Max(Time.deltaTime, 0.001f) / HipRefSeconds);
                hr.Pos = Vector3.Lerp(hr.Pos, hipsLocal, kr); hr.Rot = Quaternion.Slerp(hr.Rot, _hipsRot, kr);
                float relax = 0f; float[] rs;
                if (BendState.TryGetValue(a, out rs) && rs.Length > 2) relax = rs[2];
                float lw = hipLock * w * (1f - relax);
                hipsLocal = Vector3.Lerp(hipsLocal, hr.Pos, lw); _hipsRot = Quaternion.Slerp(_hipsRot, hr.Rot, lw);
            }
            for (int i = 0; i < 3; i++) BSnap[i] = BB[i] != null ? BB[i].localRotation : Quaternion.identity;
            Vector3 right = p.transform.right;

            // forward bend = positive rotation about the player's right axis (Unity: AngleAxis(+, right) tips "up" towards "forward").
            // Fixed on purpose: the old "try both directions, keep the closer one" decision flipped from frame to frame when the camera
            // twisted the torso, and the whole back snapped between leaning forwards and backwards (the flicker).
            Vector3 axis = right;

            float wantDeg = 0f, wantDrop = 0f, resid = 0f;      // resid: how far the hands are still out of reach with every limit used
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
                    wantDeg = maxDeg; resid = S1 + ReachMargin;
                    if (maxDrop > 0.001f && legs)
                    {
                        BendPose(hips, hipsLocal, axis, maxDeg, maxDrop);
                        resid = 0f;
                        float S2 = Mathf.Max(OutOfReach(rA, rB, rC, rightGrip), OutOfReach(lA, lB, lC, leftGrip));
                        if (S2 > -ReachMargin) { wantDrop = maxDrop; resid = S2 + ReachMargin; }
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
            if (!BendState.TryGetValue(a, out st)) BendState[a] = st = new float[3];
            float dt = Mathf.Max(Time.deltaTime, 0.001f);
            float k = 1f - Mathf.Exp(-Mathf.Max(1f, ModSettings.BendSmooth.Value) * dt);       // release (back up): soft
            float ka = 1f - Mathf.Exp(-30f * dt);                                                // attack (down to the lever): quick, so the hands are never left behind
            st[0] = Mathf.Lerp(st[0], wantDeg, wantDeg > st[0] ? ka : k); st[1] = Mathf.Lerp(st[1], wantDrop, wantDrop > st[1] ? ka : k);
            // still out of reach with the hips held: let the hips follow the clip again for a while (fast on, slow off)
            float relaxTo = resid > 0.01f ? 1f : 0f;
            st[2] = Mathf.Lerp(st[2], relaxTo, relaxTo > st[2] ? ka : 1f - Mathf.Exp(-1.5f * dt));
            if (resid > 0.01f && ModSettings.DebugLog.Value && Time.time > _nextReachLog) { _nextReachLog = Time.time + 1f; ModLog.Info("Pump reach: still " + resid.ToString("0.00") + " m short with back bend " + wantDeg.ToString("0") + " deg, hip drop " + wantDrop.ToString("0.00") + " m, hip relax " + st[2].ToString("0.00")); }
            float deg = st[0] * w, drop = st[1] * w;
            BendPose(hips, hipsLocal, axis, deg, drop);
            hp[1] = hips.localPosition;
            for (int i = 0; i < 3; i++) if (BB[i] != null) PumpBones.End(BB[i]);
            PumpBones.End(hips);

            bool hipsMoved = (hips.position - hipsPos0).sqrMagnitude > 4e-6f || Quaternion.Angle(hips.rotation, hipsRot0) > 0.2f;
            if (legs && (drop > 0.002f || hipsMoved))
            {
                Vector3 fwd = Vector3.ProjectOnPlane(p.transform.forward, Vector3.up);
                if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
                Leg(uL, kL, fL, fLp, fLr, fwd); Leg(uR, kR, fR, fRp, fRr, fwd);
            }
            if (legs) { PumpBones.End(uL); PumpBones.End(kL); PumpBones.End(fL); PumpBones.End(uR); PumpBones.End(kR); PumpBones.End(fR); }
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

        /// Forgets the bend state of player p (or all of it when p is null) and puts the hips back where the animator had them.
        public static void Release(Player p)
        {
            Prune(BendState, p);
            Prune(HipRef, p);
            if (HipPos.Count == 0) return;
            var dead = new List<Transform>();
            foreach (var kv in HipPos)
            {
                var t = kv.Key;
                if (t == null) { dead.Add(t); continue; }
                if (p != null && !t.IsChildOf(p.transform)) continue;
                if ((t.localPosition - kv.Value[1]).sqrMagnitude < 1e-8f) t.localPosition = kv.Value[0];
                dead.Add(t);
            }
            foreach (var t in dead) HipPos.Remove(t);
        }

        static void Prune<T>(Dictionary<Animator, T> map, Player p)
        {
            if (map.Count == 0) return;
            var dead = new List<Animator>();
            foreach (var kv in map) if (kv.Key == null || (p != null && kv.Key.transform.IsChildOf(p.transform))) dead.Add(kv.Key);
            foreach (var k in dead) map.Remove(k);
        }
    }
}
