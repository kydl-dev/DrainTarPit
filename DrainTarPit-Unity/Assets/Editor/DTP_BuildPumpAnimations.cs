using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// Rebuilds pump clips + controller from DTP_PumpCurves.json and re-applies player FBX clip settings.
/// Menu: DTP / Rebuild Pump Animations. Idempotent (assets updated in place).
public static class DTP_BuildPumpAnimations
{
    const string JsonPath = "Assets/Animation/DTP_PumpCurves.json";
    const string AnimDir = "Assets/Animation/";
    const string ControllerPath = "Assets/Animation/DTP_Pump.controller";
    const string PrefabPath = "Assets/Prefabs/DTP_Pump_Manual.prefab";
    const string PlayerFbx = "Assets/Animation/DTP_PlayerPump.fbx";
    const float ShortArm = 0.3f;   // lever pivot -> piston rod (m)
    const float RodRestY = 1.3f;   // PistonRod local Y at rest

    static readonly Dictionary<string, string> ClipFiles = new Dictionary<string, string>
    {
        { "PumpCycle", "DTP_Pump_Cycle.anim" }, { "PumpEnter", "DTP_Pump_Enter.anim" },
        { "PumpIdle",  "DTP_Pump_Idle.anim"  }, { "PumpPush",  "DTP_Pump_Push.anim"  },
        { "PumpPull",  "DTP_Pump_Pull.anim"  }, { "PumpExit",  "DTP_Pump_Exit.anim"  },
    };

    [MenuItem("DTP/Rebuild Pump Animations")]
    public static void RebuildAll()
    {
        BuildPumpClips(); BuildController(); ApplyPlayerClipSettings();
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
    }

    static void BuildPumpClips()
    {
        var json = Newtonsoft.Json.Linq.JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath).text);
        float fps = (float)json["fps"];
        foreach (var kv in ClipFiles)
        {
            float[] vals = json["clips"][kv.Key].ToObject<float[]>();
            string path = AnimDir + kv.Value;
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            bool isNew = clip == null;
            if (isNew) clip = new AnimationClip(); else clip.ClearCurves();
            clip.frameRate = fps;
            var lever = new AnimationCurve(); var rod = new AnimationCurve();
            for (int i = 0; i < vals.Length; i++)
            {
                float t = i / fps;
                lever.AddKey(new Keyframe(t, vals[i]));
                rod.AddKey(new Keyframe(t, RodRestY - ShortArm * Mathf.Sin(vals[i] * Mathf.Deg2Rad)));
            }
            for (int i = 0; i < lever.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(lever, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(lever, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyLeftTangentMode(rod, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(rod, i, AnimationUtility.TangentMode.ClampedAuto);
            }
            float len = vals.Length > 1 ? (vals.Length - 1) / fps : 1f / fps;
            var zero = AnimationCurve.Constant(0, len, 0f);
            clip.SetCurve("Lever", typeof(Transform), "localEulerAnglesRaw.x", lever);
            clip.SetCurve("Lever", typeof(Transform), "localEulerAnglesRaw.y", zero);
            clip.SetCurve("Lever", typeof(Transform), "localEulerAnglesRaw.z", zero);
            clip.SetCurve("PistonRod", typeof(Transform), "m_LocalPosition.x", zero);
            clip.SetCurve("PistonRod", typeof(Transform), "m_LocalPosition.y", rod);
            clip.SetCurve("PistonRod", typeof(Transform), "m_LocalPosition.z", zero);
            var s = AnimationUtility.GetAnimationClipSettings(clip);
            s.loopTime = kv.Key == "PumpCycle" || kv.Key == "PumpIdle";
            s.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(clip, s);
            if (isNew) AssetDatabase.CreateAsset(clip, path); else EditorUtility.SetDirty(clip);
        }
    }

    static void BuildController()
    {
        var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (c == null) c = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        for (int i = c.parameters.Length - 1; i >= 0; i--) c.RemoveParameter(i);
        var sm = c.layers[0].stateMachine;
        foreach (var s in sm.states) sm.RemoveState(s.state);
        c.AddParameter("active", AnimatorControllerParameterType.Bool);
        c.AddParameter("pumping", AnimatorControllerParameterType.Bool);
        c.AddParameter("pump", AnimatorControllerParameterType.Trigger);
        AnimatorState St(string n, string clip, Vector3 pos)
        {
            var st = sm.AddState(n, pos);
            if (clip != null) st.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimDir + "DTP_Pump_" + clip + ".anim");
            return st;
        }
        var rest = St("Rest", null, new Vector3(250, 0, 0));
        var enter = St("Enter", "Enter", new Vector3(250, 70, 0));
        var idle = St("Idle", "Idle", new Vector3(250, 140, 0));
        var cycle = St("Cycle", "Cycle", new Vector3(500, 140, 0));
        var push = St("Push", "Push", new Vector3(500, 220, 0));
        var pull = St("Pull", "Pull", new Vector3(250, 220, 0));
        var exit = St("Exit", "Exit", new Vector3(0, 140, 0));
        sm.defaultState = rest;
        AnimatorStateTransition T(AnimatorState a, AnimatorState b, bool exitTime)
        {
            var t = a.AddTransition(b);
            t.hasExitTime = exitTime; t.exitTime = 1f; t.duration = 0f; t.hasFixedDuration = true;
            return t;
        }
        T(rest, enter, false).AddCondition(AnimatorConditionMode.If, 0, "active");
        T(enter, idle, true);
        T(idle, cycle, false).AddCondition(AnimatorConditionMode.If, 0, "pumping");
        T(cycle, idle, true).AddCondition(AnimatorConditionMode.IfNot, 0, "pumping");
        T(idle, push, false).AddCondition(AnimatorConditionMode.If, 0, "pump");
        T(push, pull, true);
        T(pull, idle, true);
        T(idle, exit, false).AddCondition(AnimatorConditionMode.IfNot, 0, "active");
        T(exit, rest, true);
        EditorUtility.SetDirty(c);
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        var anim = root.GetComponentInChildren<Animator>(true);
        anim.runtimeAnimatorController = c;
        anim.applyRootMotion = false;
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
    }

    static void ApplyPlayerClipSettings()
    {
        var imp = (ModelImporter)AssetImporter.GetAtPath(PlayerFbx);
        var list = new List<ModelImporterClipAnimation>();
        foreach (var d in imp.defaultClipAnimations)
        {
            if (d.name.StartsWith("__preview__")) continue;
            bool loop = d.name.EndsWith("PumpCycle") || d.name.EndsWith("PumpIdle");
            d.loopTime = loop; d.loopPose = loop;
            d.lockRootRotation = true; d.keepOriginalOrientation = true;
            d.lockRootHeightY = true;  d.keepOriginalPositionY = true;
            d.lockRootPositionXZ = true; d.keepOriginalPositionXZ = true;
            list.Add(d);
        }
        imp.clipAnimations = list.ToArray();
        imp.SaveAndReimport();
    }
}
