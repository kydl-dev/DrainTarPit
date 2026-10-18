using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// Temporary diagnostic: simulates the plugin's Idle -> Push -> Pull -> Idle cross-fades on the proxy rig and reports
/// the biggest per-frame bone rotation jumps. Not part of the mod.
public static class DTP_FlickerSim
{

public static string Diag()
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Animation/DTP_PlayerPump.fbx");
        var go = UnityEngine.Object.Instantiate(fbx); go.hideFlags = HideFlags.HideAndDontSave;
        var animator = go.GetComponentInChildren<Animator>();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("animator on " + animator.gameObject.name + " enabled=" + animator.enabled + " isHuman=" + animator.isHuman + " init=" + animator.isInitialized);
        var b = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        sb.AppendLine("before: " + b.localRotation.eulerAngles);
        var graph = PlayableGraph.Create("t"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var o = AnimationPlayableOutput.Create(graph, "o", animator);
        var cp = AnimationClipPlayable.Create(graph, Load("Push")); o.SetSourcePlayable(cp);
        graph.Evaluate(0.25f);
        sb.AppendLine("after 0.25: " + b.localRotation.eulerAngles + " t=" + cp.GetTime());
        graph.Evaluate(0.2f);
        sb.AppendLine("after 0.45: " + b.localRotation.eulerAngles + " t=" + cp.GetTime());
        graph.Destroy(); UnityEngine.Object.DestroyImmediate(go);
        return sb.ToString();
    }

    static AnimationClip Load(string n) { return AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animation/Player/DTP_Player_Pump" + n + ".anim"); }

    static AnimationClip Negated(AnimationClip src, string prop)
    {
        var c = UnityEngine.Object.Instantiate(src);
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

    public static string Run(bool fixPush, float blend, bool footIk)
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Animation/DTP_PlayerPump.fbx");
        var go = UnityEngine.Object.Instantiate(fbx); go.hideFlags = HideFlags.HideAndDontSave;
        var animator = go.GetComponentInChildren<Animator>(); animator.enabled = true;
        var bones = new List<HumanBodyBones>(); var tr = new List<Transform>();
        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++) { var t = animator.GetBoneTransform((HumanBodyBones)i); if (t != null) { bones.Add((HumanBodyBones)i); tr.Add(t); } }

        var graph = PlayableGraph.Create("sim"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var outp = AnimationPlayableOutput.Create(graph, "o", animator);
        var idle = AnimationClipPlayable.Create(graph, Load("Idle")); idle.SetApplyFootIK(footIk); idle.SetTime(0.1); idle.SetSpeed(0);
        var push = AnimationClipPlayable.Create(graph, fixPush ? Negated(Load("Push"), "LeftFootQ") : Load("Push")); push.SetApplyFootIK(footIk); push.SetSpeed(0);
        var pull = AnimationClipPlayable.Create(graph, Load("Pull")); pull.SetApplyFootIK(footIk); pull.SetSpeed(0);
        var idle2 = AnimationClipPlayable.Create(graph, Load("Idle")); idle2.SetApplyFootIK(footIk); idle2.SetTime(0.1); idle2.SetSpeed(0);
        var mix = AnimationMixerPlayable.Create(graph, 4); outp.SetSourcePlayable(mix);
        graph.Connect(idle, 0, mix, 0); graph.Connect(push, 0, mix, 1); graph.Connect(pull, 0, mix, 2); graph.Connect(idle2, 0, mix, 3);

        animator.Rebind(); animator.Update(0f); graph.Play();
        float dt = 1f / 60f;
        float[] starts = { 0f, 0.2f, 0.7f, 1.333f }; float total = 1.8f;
        var prev = new Quaternion[tr.Count]; var worst = new float[tr.Count]; var worstT = new float[tr.Count];
        bool first = true;
        for (float t = 0f; t <= total; t += dt)
        {
            int cur = 0; for (int i = 0; i < starts.Length; i++) if (t >= starts[i]) cur = i;
            float since = t - starts[cur];
            float w = cur == 0 ? 1f : (blend > 0.001f ? Mathf.Clamp01(since / blend) : 1f); w = w * w * (3f - 2f * w);
            for (int i = 0; i < 4; i++) mix.SetInputWeight(i, 0f);
            mix.SetInputWeight(cur, w); if (cur > 0) mix.SetInputWeight(cur - 1, 1f - w);
            if (cur == 1) push.SetTime(since);
            if (cur == 2) { pull.SetTime(since); push.SetTime(0.5); }
            graph.Evaluate(0f);
            for (int i = 0; i < tr.Count; i++)
            {
                var q = tr[i].localRotation;
                if (!first) { float a = Quaternion.Angle(prev[i], q); if (a > worst[i]) { worst[i] = a; worstT[i] = t; } }
                prev[i] = q;
            }
            first = false;
        }
        graph.Destroy();
        UnityEngine.Object.DestroyImmediate(go);
        var rows = new List<Tuple<float, string>>();
        for (int i = 0; i < tr.Count; i++) rows.Add(Tuple.Create(worst[i], bones[i] + " " + worst[i].ToString("F1") + " deg/frame @" + worstT[i].ToString("F2")));
        rows.Sort((a, b) => b.Item1.CompareTo(a.Item1));
        return string.Join("\n", rows.Take(8).Select(r => r.Item2).ToArray());
    }
}
