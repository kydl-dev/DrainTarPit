using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Kydl.Common;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace DrainTarPit.Pump
{
    /// <summary>
    /// Plays the bundle's Humanoid player clips (DTP_Player_PumpEnter/Idle/Push/Pull/Cycle/Exit) on Valheim players: standing
    /// at the pump, hands up on the lever, bending down with it.
    ///
    /// 0.4.2: the clips are played through a PlayableGraph on the player's Animator. While a graph is attached the player's own
    /// controller is bypassed, so the vanilla chair-sit pose (attach_chair) can never show up and there is no clip slot to
    /// detect any more (the old AnimatorOverrideController swap picked no / the wrong slot and left the player sitting).
    /// Release() destroys the graph and the normal controller takes over again.
    ///
    /// 0.5.5: ALL clip playables are created once, when the graph is created, and never added / removed afterwards. A phase
    /// change only moves mixer weights, times and speeds. Before, every phase change created a new AnimationClipPlayable and
    /// connected it to the mixer (and destroyed the old one after the fade); on that frame the animator output ONE frame of the
    /// rig's default (bind) pose - hips up, arms out, feet apart - which showed as a flicker of the hands and feet at every
    /// Push -> Pull -> Idle change (measured in the proxy-rig simulation: 7 one-frame glitches per run, none with fixed playables).
    /// </summary>
    internal static class PlayerPumpAnimations
    {
        static readonly FieldInfo FAnimator = AccessTools.Field(typeof(Character), "m_animator");
        static readonly Dictionary<string, AnimationClip> Cache = new();

        // one mixer input per clip, in this order (lower-case clip names as used by PumpController.ClipOf)
        static readonly string[] ClipNames = { "pumpenter", "pumpidle", "pumppush", "pumppull", "pumpcycle", "pumpexit" };
        static int IndexOf(string name) { name = name.ToLowerInvariant(); for (int i = 0; i < ClipNames.Length; i++) if (ClipNames[i] == name) return i; return -1; }

        sealed class G
        {
            public Animator Anim; public PlayableGraph Graph; public AnimationPlayableOutput Out;
            public AnimationMixerPlayable Mixer;
            public AnimationClipPlayable[] Clips;       // one per ClipNames entry, created once, never removed
            public bool[] Has;                          // false when the clip is missing from the bundle (input stays empty, weight 0)
            public float[] W, W0;                       // current input weights / the weights when the last cross-fade started
            public int Cur = -1;                        // input of the clip that is playing now
            public float Blend = 1f, BlendDur;          // 0..1 progress of the cross-fade into Cur
            public bool HasCur { get { return Cur >= 0 && Has[Cur]; } }
            public AnimationClip Clip; public bool Loop;
        }
        static readonly Dictionary<Player, G> Graphs = new();
        static GameObject _host;
        const double IdleHoldTime = 0.1;       // Idle is calm (all legs constant) between 0 and 0.2 s

        /// true when the pump clips are in the bundle (nothing has to be detected any more).
        public static bool Ready { get { return Get("pumpidle") != null; } }

        // kept for the callers: no detection needed with the playable graph
        public static void BeginDetect(Player p) { }
        public static void TryDetect(Player p) { }

        public static AnimationClip Get(string name)
        {
            if (Plugin.Bundle == null) return null;
            AnimationClip c;
            if (Cache.TryGetValue(name, out c)) return c;
            c = Plugin.Bundle.LoadAsset<AnimationClip>("assets/animation/player/dtp_player_" + name.ToLowerInvariant() + ".anim");
            Cache[name] = c;
            return c;
        }

        public static Animator AnimatorOf(Player p) { return p == null ? null : FAnimator.GetValue(p) as Animator; }
        public static float Length(string name) { var c = Get(name); return c != null ? c.length : 0f; }

        // builds the graph with every clip already connected (the topology never changes after this)
        static G CreateGraph(Animator a)
        {
            int n = ClipNames.Length;
            var g = new G { Anim = a, Graph = PlayableGraph.Create("DTP_Pump") };
            g.Graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            g.Out = AnimationPlayableOutput.Create(g.Graph, "DTP", a);
            g.Mixer = AnimationMixerPlayable.Create(g.Graph, n);
            g.Out.SetSourcePlayable(g.Mixer);
            g.Clips = new AnimationClipPlayable[n]; g.Has = new bool[n]; g.W = new float[n]; g.W0 = new float[n];
            for (int i = 0; i < n; i++)
            {
                var clip = Get(ClipNames[i]);
                if (clip == null) continue;
                var cp = AnimationClipPlayable.Create(g.Graph, clip);
                cp.SetApplyFootIK(false);
                cp.SetSpeed(0.0);                                   // started by Play()
                g.Graph.Connect(cp, 0, g.Mixer, i);
                g.Mixer.SetInputWeight(i, 0f);
                g.Clips[i] = cp; g.Has[i] = true;
            }
            return g;
        }

        // weights of the cross-fade: every input moves from its weight at the start of the fade (W0) to 1 (the current clip) or 0
        // (all others), so the weights always add up to 1 - also when a new clip starts while a fade is still running
        static void ApplyWeights(G g)
        {
            float s = g.Blend >= 1f ? 1f : g.Blend * g.Blend * (3f - 2f * g.Blend);     // smoothstep
            for (int i = 0; i < g.W.Length; i++)
            {
                if (!g.Has[i]) continue;
                float w = Mathf.Lerp(g.W0[i], i == g.Cur ? 1f : 0f, s);
                g.W[i] = w;
                g.Mixer.SetInputWeight(i, w);
            }
        }

        public static void Play(Player p, string clipName)
        {
            var a = AnimatorOf(p); var clip = Get(clipName);
            if (p == null || a == null) return;
            if (clip == null) { ModLog.Warn("Missing player clip: " + clipName); return; }
            int idx = IndexOf(clipName);
            if (idx < 0) { ModLog.Warn("Unknown player clip: " + clipName); return; }
            try
            {
                G g;
                if (!Graphs.TryGetValue(p, out g) || !g.Graph.IsValid() || g.Anim != a)
                {
                    if (g != null) Kill(g);
                    g = CreateGraph(a);
                    Graphs[p] = g;
                    EnsureRunner();
                }
                if (!g.Has[idx]) { ModLog.Warn("Missing player clip: " + clipName); return; }

                bool hadCur = g.HasCur;
                bool hold = ModSettings.FreezeIdle.Value && clipName.ToLowerInvariant() == "pumpidle";
                var cp = g.Clips[idx];
                cp.SetTime(hold ? IdleHoldTime : 0.0);              // the Idle loop twitches the left leg: hold one calm frame instead
                cp.SetSpeed(hold ? 0.0 : 1.0);

                System.Array.Copy(g.W, g.W0, g.W.Length);           // the cross-fade starts from whatever is on screen right now
                float dur = ModSettings.ClipBlendSeconds.Value;
                if (hadCur && dur > 0.001f) { g.Blend = 0f; g.BlendDur = dur; }
                else { g.Blend = 1f; g.BlendDur = 0f; }
                g.Cur = idx;
                ApplyWeights(g);

                g.Clip = clip; g.Loop = clip.isLooping && !hold;
                if (!g.Graph.IsPlaying()) g.Graph.Play();
            }
            catch (System.Exception e) { ModLog.Warn("Pump animation failed (" + clipName + "): " + e.Message); }
        }

        /// Stops the pump clips on this player; the vanilla animator controller takes over again.
        public static void Release(Player p)
        {
            if (p == null) return;
            G g;
            if (Graphs.TryGetValue(p, out g)) { Kill(g); Graphs.Remove(p); }
        }

        static void Kill(G g)
        {
            try { if (g.Graph.IsValid()) g.Graph.Destroy(); } catch { }
        }

        // keeps looping clips (Idle, Cycle) looping, runs the cross-fades and drops graphs of players that are gone
        static void Tick()
        {
            if (Graphs.Count == 0) return;
            List<Player> gone = null;
            foreach (var kv in Graphs)
            {
                var g = kv.Value;
                if (kv.Key == null || !g.Graph.IsValid()) { (gone ?? (gone = new List<Player>())).Add(kv.Key); continue; }
                if (g.Blend < 1f && g.HasCur)
                {
                    g.Blend = g.BlendDur > 0.001f ? Mathf.Min(1f, g.Blend + Time.deltaTime / g.BlendDur) : 1f;
                    ApplyWeights(g);
                }
                if (g.HasCur && g.Loop && g.Clip != null && g.Clip.length > 0.05f)
                {
                    var cur = g.Clips[g.Cur];
                    double t = cur.GetTime();
                    if (t >= g.Clip.length) cur.SetTime(t % g.Clip.length);
                }
            }
            if (gone != null) foreach (var p in gone) { G g; if (Graphs.TryGetValue(p, out g)) Kill(g); Graphs.Remove(p); }
        }

        sealed class Runner : MonoBehaviour { void Update() { Tick(); } void OnDestroy() { foreach (var g in Graphs.Values) Kill(g); Graphs.Clear(); } }

        static void EnsureRunner()
        {
            if (_host != null) return;
            _host = new GameObject("DTP_PumpAnimRunner");
            Object.DontDestroyOnLoad(_host);
            _host.AddComponent<Runner>();
        }

        public static string Describe(Player p)
        {
            var a = AnimatorOf(p);
            if (a == null) return "no animator";
            G g; bool has = Graphs.TryGetValue(p, out g) && g.Graph.IsValid();
            var sb = new StringBuilder();
            sb.Append("humanoid=" + a.isHuman + "; pump clips in bundle=" + Ready + "; graph=" + (has ? "playing '" + (g.Clip != null ? g.Clip.name : "?") + "'" : "none"));
            sb.Append("; " + ModSettings.AttachBool.Value + "=" + HasParam(a, ModSettings.AttachBool.Value));
            return sb.ToString();
        }

        static string HasParam(Animator a, string n)
        {
            foreach (var p in a.parameters) if (p.name == n) return a.GetBool(n).ToString();
            return "param missing";
        }
    }
}
