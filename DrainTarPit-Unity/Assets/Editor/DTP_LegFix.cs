using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// Temporary tool (not part of the mod): removes the knee / leg snaps in the player pump clips and measures the result.
public static class DTP_LegFix
{
    const string Dir = "Assets/Animation/Player/DTP_Player_Pump";
    public static AnimationClip Load(string n) { return AssetDatabase.LoadAssetAtPath<AnimationClip>(Dir + n + ".anim"); }

    static bool IsLegMuscle(string p)
    {
        foreach (var side in new[] { "Left ", "Right " })
            foreach (var part in new[] { "Upper Leg", "Lower Leg", "Foot", "Toes" })
                if (p.StartsWith(side + part)) return true;
        return false;
    }

    static float[] Gauss(int r, float sigmaSamples)
    {
        var k = new float[2 * r + 1]; float s = 0f;
        for (int i = -r; i <= r; i++) { k[i + r] = Mathf.Exp(-0.5f * i * i / (sigmaSamples * sigmaSamples)); s += k[i + r]; }
        for (int i = 0; i < k.Length; i++) k[i] /= s;
        return k;
    }

    /// Smoothed copy of `src`: leg muscle curves resampled at 60 Hz and low-passed (Gaussian, sigma seconds).
    /// Non-looping clips keep their first / last pose exactly (the correction fades in over `edge` seconds); looping clips are smoothed circularly.
    public static AnimationClip Smoothed(AnimationClip src, float sigma, float edge, bool writeInPlace)
    {
        var dst = writeInPlace ? src : UnityEngine.Object.Instantiate(src);
        bool loop = AnimationUtility.GetAnimationClipSettings(src).loopTime;
        const float dt = 1f / 60f;
        int n = Mathf.Max(2, Mathf.RoundToInt(src.length / dt) + 1);
        float sig = sigma / dt; int r = Mathf.CeilToInt(sig * 3f); var kern = Gauss(r, sig);
        foreach (var b in AnimationUtility.GetCurveBindings(src))
        {
            if (b.type != typeof(Animator) || !IsLegMuscle(b.propertyName)) continue;
            var cv = AnimationUtility.GetEditorCurve(src, b);
            if (cv == null || cv.length < 2) continue;
            var v = new float[n]; for (int i = 0; i < n; i++) v[i] = cv.Evaluate(Mathf.Min(i * dt, src.length));
            var o = new float[n];
            for (int i = 0; i < n; i++)
            {
                float acc = 0f;
                for (int j = -r; j <= r; j++)
                {
                    int idx = i + j;
                    if (loop) { idx %= (n - 1); if (idx < 0) idx += n - 1; } else idx = Mathf.Clamp(idx, 0, n - 1);
                    acc += kern[j + r] * v[idx];
                }
                float tt = i * dt; float k = 1f;
                if (!loop && edge > 0.001f) { float m = Mathf.Clamp01(Mathf.Min(tt, src.length - tt) / edge); k = m * m * (3f - 2f * m); }
                o[i] = Mathf.Lerp(v[i], acc, k);
            }
            if (loop) o[n - 1] = o[0];
            var nc = new AnimationCurve();
            for (int i = 0; i < n; i++) nc.AddKey(new Keyframe(Mathf.Min(i * dt, src.length), o[i]));
            for (int i = 0; i < nc.length; i++) { AnimationUtility.SetKeyLeftTangentMode(nc, i, AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(nc, i, AnimationUtility.TangentMode.ClampedAuto); }
            AnimationUtility.SetEditorCurve(dst, b, nc);
        }
        return dst;
    }

    public static AnimationClip Negated(AnimationClip src, string prop, bool writeInPlace)
    {
        var c = writeInPlace ? src : UnityEngine.Object.Instantiate(src);
        foreach (var ax in new[] { "x", "y", "z", "w" })
        {
            var b = EditorCurveBinding.FloatCurve("", typeof(Animator), prop + "." + ax);
            var cv = AnimationUtility.GetEditorCurve(c, b);
            if (cv == null) continue;
            var nk = new AnimationCurve();
            for (int i = 0; i < cv.length; i++) { var k = cv[i]; k.value = -k.value; k.inTangent = -k.inTangent; k.outTangent = -k.outTangent; nk.AddKey(k); }
            AnimationUtility.SetEditorCurve(c, b, nk);
        }
        return c;
    }

    /// Plays Idle(hold) -> Push -> Pull -> Idle(hold) by sampling and tracks knees / feet / hips on the proxy rig.
    public static string Metrics(Func<string, AnimationClip> get)
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Animation/DTP_PlayerPump.fbx");
        var go = UnityEngine.Object.Instantiate(fbx); go.hideFlags = HideFlags.HideAndDontSave;
        var animator = go.GetComponentInChildren<Animator>();
        var parts = new[] { HumanBodyBones.Hips, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };
        var tr = parts.Select(p => animator.GetBoneTransform(p)).ToArray();
        var seq = new List<Tuple<string, float>>();
        const float dt = 1f / 60f;
        for (float t = 0; t < 0.1f; t += dt) seq.Add(Tuple.Create("Idle", 0.1f));
        for (float t = 0; t <= 0.5f + 1e-4f; t += dt) seq.Add(Tuple.Create("Push", Mathf.Min(t, 0.5f)));
        for (float t = 0; t <= 0.633f + 1e-4f; t += dt) seq.Add(Tuple.Create("Pull", Mathf.Min(t, 0.633f)));
        for (float t = 0; t < 0.1f; t += dt) seq.Add(Tuple.Create("Idle", 0.1f));
        var pos = new List<Vector3[]>();
        foreach (var s in seq) { get(s.Item1).SampleAnimation(animator.gameObject, s.Item2); pos.Add(tr.Select(x => animator.transform.InverseTransformPoint(x.position)).ToArray()); }
        var sb = new System.Text.StringBuilder();
        for (int b = 0; b < parts.Length; b++)
        {
            float worstA = 0; int wi = 0; float maxV = 0; int mi = 0;
            for (int i = 2; i < pos.Count; i++)
            {
                float v1 = (pos[i][b] - pos[i - 1][b]).magnitude / dt, v0 = (pos[i - 1][b] - pos[i - 2][b]).magnitude / dt;
                if (v1 > maxV) { maxV = v1; mi = i; }
                float a = Mathf.Abs(v1 - v0) / dt; if (a > worstA) { worstA = a; wi = i; }
            }
            float travel = 0f; foreach (var p in pos) travel = Mathf.Max(travel, (p[b] - pos[0][b]).magnitude);
            sb.AppendLine(parts[b] + ": max speed " + maxV.ToString("F2") + " m/s (" + seq[mi].Item1 + "@" + seq[mi].Item2.ToString("F2") + "), worst accel " + worstA.ToString("F0") + " m/s2 (" + seq[wi].Item1 + "@" + seq[wi].Item2.ToString("F2") + "), travel " + travel.ToString("F3") + " m");
        }
        UnityEngine.Object.DestroyImmediate(go);
        return sb.ToString();
    }

    /// Candidate comparison: smoothing sigma / edge on in-memory copies (nothing is written).
    public static string Compare(float[] sigmas, float edge)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("== ORIGINAL ==\n" + Metrics(Load));
        foreach (var s in sigmas)
        {
            var push = Smoothed(Load("Push"), s, edge, false); var pull = Smoothed(Load("Pull"), s, edge, false); var idle = Load("Idle");
            Func<string, AnimationClip> get = n => n == "Push" ? push : n == "Pull" ? pull : idle;
            sb.AppendLine("== sigma " + s.ToString("F3") + " s, edge " + edge.ToString("F2") + " ==\n" + Metrics(get));
        }
        return sb.ToString();
    }

    /// Writes the fix into the clip assets (originals are backed up outside Assets first).
    public static string Apply(float sigma, float edge, string backupDir)
    {
        System.IO.Directory.CreateDirectory(backupDir);
        var sb = new System.Text.StringBuilder();
        foreach (var n in new[] { "Enter", "Push", "Pull", "Cycle", "Exit" })
        {
            string p = Dir + n + ".anim";
            string bk = System.IO.Path.Combine(backupDir, "DTP_Player_Pump" + n + ".anim");
            if (!System.IO.File.Exists(bk)) System.IO.File.Copy(p, bk);
            var c = Load(n);
            Smoothed(c, sigma, edge, true);
            if (n == "Push") Negated(c, "LeftFootQ", true);
            if (n == "Exit") Negated(c, "RightHandQ", true);
            EditorUtility.SetDirty(c);
            sb.AppendLine("patched " + n);
        }
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }
}
