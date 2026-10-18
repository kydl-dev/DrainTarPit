using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
#if UNITY_EDITOR
using UnityEditor;
#endif

// ---- temporary simulation harness (NOT part of the mod): runs the mod's real PumpHandIK on the proxy rig ----
public class Player : MonoBehaviour { }

namespace DrainTarPit
{
    public class Cfg<T> { public T Value; public Cfg(T v) { Value = v; } }
    public class LogShim { public void LogWarning(string s) { Debug.LogWarning(s); } public void LogInfo(string s) { } }
    public static class Plugin { public static readonly LogShim Log = new LogShim(); }
    public static class ModSettings
    {
        public static Cfg<float> GripSpread = new Cfg<float>(0.17f), GripUp = new Cfg<float>(0f), GripForward = new Cfg<float>(-0.025f), GripPalmOffset = new Cfg<float>(-0.002f);
        public static Cfg<bool> HandRotate = new Cfg<bool>(true), BendToReach = new Cfg<bool>(true);
        public static Cfg<float> FingerCurl = new Cfg<float>(0.79f), ThumbCurl = new Cfg<float>(0.8f), ThumbSpread = new Cfg<float>(25f), MaxBackBend = new Cfg<float>(35f), MaxKneeBend = new Cfg<float>(0.25f);
    }
}

namespace DrainTarPit.Pump
{
    internal static class PlayerPumpAnimations
    {
        public static Animator AnimatorOf(Player p) { return p == null ? null : p.GetComponentInChildren<Animator>(); }
    }

    [DefaultExecutionOrder(1000)]
    public class DTP_SimHarness : MonoBehaviour
    {
        public static readonly StringBuilder Report = new StringBuilder();
        public static int Finished, Started;
        public int Variant; public Vector3 Origin; public float LeverlessIk = 0f;

        GameObject pump, rig; Animator pumpAnim, rigAnim; Transform interact, stand; Player player;
        PlayableGraph graph; AnimationMixerPlayable mixer; readonly AnimationClipPlayable[] slot = new AnimationClipPlayable[2]; readonly bool[] has = new bool[2];
        int cur = -1; float blend = 1f; AnimationClip curClip; bool loop;
        Transform[] track; readonly string[] names = { "LHand", "RHand", "LFoot", "RFoot", "LElbow", "Hips", "Head" };
        readonly List<Vector3[]> samples = new List<Vector3[]>(); readonly List<float> times = new List<float>();
        readonly List<Vector3[]> pre = new List<Vector3[]>(); readonly List<Vector3> ipos = new List<Vector3>(); readonly List<float> ikws = new List<float>();
        float t, ikW; int nextEvent; bool finished;
        const float End = 4.7f;

        static Transform FindDeep(Transform r, string n) { foreach (var x in r.GetComponentsInChildren<Transform>(true)) if (x.name == n) return x; return null; }

#if UNITY_EDITOR
        AnimationClip C(string n) { return AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animation/Player/DTP_Player_Pump" + n + ".anim"); }
        // (time, clip, hold, leverTrigger)
        readonly List<Tuple<float, string, bool, bool>> events = new List<Tuple<float, string, bool, bool>>
        {
            Tuple.Create(0.0f, "Enter", false, false), Tuple.Create(0.5f, "Idle", true, false),
            Tuple.Create(1.2f, "Push", false, true), Tuple.Create(1.7f, "Pull", false, false), Tuple.Create(2.333f, "Idle", true, false),
            Tuple.Create(3.0f, "Push", false, true), Tuple.Create(3.5f, "Pull", false, false), Tuple.Create(4.133f, "Idle", true, false),
        };

        void Start()
        {
            Started++;
            Time.captureFramerate = 60;
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/DTP_Pump_Manual.prefab");
            pump = Instantiate(pf, Origin, Quaternion.identity);
            pumpAnim = pump.GetComponentInChildren<Animator>(true); interact = FindDeep(pump.transform, "interact_point"); stand = FindDeep(pump.transform, "stand_point");
            var baseWorld = stand.position;
            var toLever = interact.position - baseWorld; toLever.y = 0f; var fwd = toLever.normalized;
            stand.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            var standPos = baseWorld + fwd * -0.28f;
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Animation/DTP_PlayerPump.fbx");
            rig = Instantiate(fbx, standPos, stand.rotation); player = rig.AddComponent<Player>();
            rigAnim = rig.GetComponentInChildren<Animator>(); rigAnim.applyRootMotion = false;
            var hb = new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftLowerArm, HumanBodyBones.Hips, HumanBodyBones.Head };
            track = hb.Select(b => rigAnim.GetBoneTransform(b)).ToArray();
            graph = PlayableGraph.Create("sim" + Variant); graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var o = AnimationPlayableOutput.Create(graph, "o", rigAnim); mixer = AnimationMixerPlayable.Create(graph, 2); o.SetSourcePlayable(mixer); graph.Play();
            if (Variant == 4) BuildPersistent(o);
            pumpAnim.SetBool("active", true);
        }

        void PlayClip(AnimationClip clip, bool hold)
        {
            if (Variant == 4) { PlayPersistent(clip, hold); return; }
            bool hadCur = cur >= 0 && has[cur]; int old = cur, ns = hadCur ? 1 - old : 0;
            if (has[ns]) Drop(ns);
            var cp = AnimationClipPlayable.Create(graph, clip); cp.SetApplyFootIK(false); cp.SetTime(0.0);
            if (hold) { cp.SetTime(0.1); cp.SetSpeed(0.0); }
            graph.Connect(cp, 0, mixer, ns); slot[ns] = cp; has[ns] = true; cur = ns;
            if (hadCur) { blend = 0f; mixer.SetInputWeight(ns, 0f); mixer.SetInputWeight(old, 1f); } else { blend = 1f; mixer.SetInputWeight(ns, 1f); }
            curClip = clip; loop = clip.isLooping && !hold;
        }
        // Variant 4: every clip playable exists from the start (no graph topology change at a phase switch), only weights / times / speeds change
        readonly Dictionary<string, int> pidx = new Dictionary<string, int>(); AnimationClipPlayable[] pp; int pcur = -1, pprev = -1; float pblend = 1f;
        void BuildPersistent(AnimationPlayableOutput o2)
        {
            string[] names4 = { "Enter", "Idle", "Push", "Pull" };
            mixer.Destroy(); mixer = AnimationMixerPlayable.Create(graph, names4.Length);
            o2.SetSourcePlayable(mixer);
            pp = new AnimationClipPlayable[names4.Length];
            for (int i = 0; i < names4.Length; i++) { var cp = AnimationClipPlayable.Create(graph, C(names4[i])); cp.SetApplyFootIK(false); cp.SetSpeed(0.0); graph.Connect(cp, 0, mixer, i); mixer.SetInputWeight(i, 0f); pp[i] = cp; pidx["DTP_Player_Pump" + names4[i]] = i; }
        }
        void PlayPersistent(AnimationClip clip, bool hold)
        {
            int idx = pidx[clip.name]; int old = pcur;
            if (pprev >= 0 && pprev != old) { mixer.SetInputWeight(pprev, 0f); pp[pprev].SetSpeed(0.0); }
            pprev = old; pcur = idx;
            pp[idx].SetTime(hold ? 0.1 : 0.0); pp[idx].SetSpeed(hold ? 0.0 : 1.0);
            if (old >= 0 && old != idx) { pblend = 0f; mixer.SetInputWeight(idx, 0f); mixer.SetInputWeight(old, 1f); } else { pblend = 1f; pprev = -1; mixer.SetInputWeight(idx, 1f); }
        }
        void UpdatePersistentBlend(float dt)
        {
            if (pblend >= 1f || pcur < 0) return;
            pblend = Mathf.Min(1f, pblend + dt / 0.12f); float w = pblend * pblend * (3f - 2f * pblend);
            mixer.SetInputWeight(pcur, w);
            if (pprev >= 0) { if (pblend >= 1f) { mixer.SetInputWeight(pprev, 0f); pp[pprev].SetSpeed(0.0); pprev = -1; } else mixer.SetInputWeight(pprev, 1f - w); }
        }

        void PlayClip_Unused() { }
        void Drop(int s) { if (!has[s]) return; try { graph.Disconnect(mixer, s); slot[s].Destroy(); } catch { } has[s] = false; }

        void Update()
        {
            if (finished || graph.Equals(default(PlayableGraph))) return;
            float dt = Time.deltaTime;
            if (Variant == 4) UpdatePersistentBlend(dt);
            if (Variant != 4 && blend < 1f && cur >= 0 && has[cur])
            {
                blend = Mathf.Min(1f, blend + dt / 0.12f); float w = blend * blend * (3f - 2f * blend); int other = 1 - cur;
                mixer.SetInputWeight(cur, w);
                if (has[other]) { if (blend >= 1f) Drop(other); else mixer.SetInputWeight(other, 1f - w); }
            }
            while (nextEvent < events.Count && t >= events[nextEvent].Item1)
            {
                var e = events[nextEvent++]; PlayClip(C(e.Item2), e.Item3); if (e.Item4) pumpAnim.SetTrigger("pump");
            }
            ikW = Mathf.MoveTowards(ikW, 1f, dt / 0.35f);
            t += dt;
        }

        void LateUpdate()
        {
            if (finished || track == null) return;
            var preNow = track.Select(x => x.position).ToArray(); pre.Add(preNow); ipos.Add(interact.position); ikws.Add(ikW);
            if (Variant > 0)
            {
                DrainTarPit.ModSettings.BendToReach.Value = Variant != 2;
                DrainTarPit.ModSettings.MaxKneeBend.Value = Variant == 3 ? 0f : 0.25f;
                PumpHandIK.Apply(player, interact, ikW);
            }
            samples.Add(track.Select(x => x.position).ToArray()); times.Add(t);
            if (t >= End) { finished = true; Analyze(); Finished++; try { graph.Destroy(); } catch { } }
        }

        void Analyze()
        {
            float dt = 1f / 60f;
            if (Variant == 1)
            {
                var tr = new StringBuilder("TRACE V1 (t | handL pre->post | interact | footL pre->post | hips pre->post | ikW)\n");
                for (int k = 0; k < samples.Count; k++)
                {
                    if (times[k] < 1.62f || times[k] > 2.45f) continue;
                    Func<Vector3, string> f = v => v.x.ToString("F3") + "," + v.y.ToString("F3") + "," + v.z.ToString("F3");
                    tr.AppendLine(times[k].ToString("F3") + " | " + f(pre[k][0]) + " -> " + f(samples[k][0]) + " | " + f(ipos[k]) + " | " + f(pre[k][2]) + " -> " + f(samples[k][2]) + " | " + f(pre[k][5]) + " -> " + f(samples[k][5]) + " | " + ikws[k].ToString("F2"));
                }
                Report.AppendLine(tr.ToString());
            }
            { var sb0 = new StringBuilder("V" + Variant + " PRE-IK anim travel:"); for (int p = 0; p < names.Length; p++) { float mn = 1e9f, mx = -1e9f; foreach (var s in pre) { mn = Mathf.Min(mn, s[p].y); mx = Mathf.Max(mx, s[p].y); } sb0.Append("  " + names[p] + " yRange " + (mx - mn).ToString("F3")); } Report.AppendLine(sb0.ToString()); }
            { var gl = new StringBuilder("V" + Variant + " ONE-FRAME GLITCHES in animator output (hips jump >8cm and back):"); int n = 0; for (int k = 1; k < pre.Count - 1; k++) { float a = pre[k][5].y - pre[k - 1][5].y, b = pre[k + 1][5].y - pre[k][5].y; if (Mathf.Abs(a) > 0.08f && Mathf.Abs(b) > 0.08f && a * b < 0f) { gl.Append(" t=" + times[k].ToString("F2")); n++; } } Report.AppendLine(gl.ToString() + " count=" + n); }
            var wins = new[] { Tuple.Create("Push1", 1.2f, 1.7f), Tuple.Create("Pull1", 1.7f, 2.5f), Tuple.Create("Push2", 3.0f, 3.5f), Tuple.Create("Pull2", 3.5f, 4.3f) };
            foreach (var w in wins)
            {
                var sb = new StringBuilder("V" + Variant + " " + w.Item1 + ":");
                for (int p = 0; p < names.Length; p++)
                {
                    float maxA = 0f, at = 0f; int rev = 0;
                    for (int k = 2; k < samples.Count; k++)
                    {
                        if (times[k] < w.Item2 || times[k] > w.Item3) continue;
                        var v1 = (samples[k][p] - samples[k - 1][p]) / dt; var v0 = (samples[k - 1][p] - samples[k - 2][p]) / dt;
                        float a = (v1 - v0).magnitude / dt; if (a > maxA) { maxA = a; at = times[k]; }
                        if (v1.magnitude > 0.03f && v0.magnitude > 0.03f && Vector3.Dot(v1, v0) < 0f) rev++;
                    }
                    sb.Append("  " + names[p] + " acc " + maxA.ToString("F0") + "@" + at.ToString("F2") + " rev " + rev);
                }
                Report.AppendLine(sb.ToString());
            }
        }
#endif
    }
}
