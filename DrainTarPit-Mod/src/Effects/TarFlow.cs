using System.Collections.Generic;
using DrainTarPit.Building;
using DrainTarPit.Drain;
using DrainTarPit.Logging;
using UnityEngine;
using UnityEngine.Rendering;

namespace DrainTarPit.Effects
{
    /// <summary>
    /// Shows the tar being pulled through the hollow hoses. Every pump stroke sends a slug of tar from the strainer tip up the
    /// intake chain, through the pump and on along the outlet chain to the nozzle. The chains are traced from the placed
    /// pieces (the same snap-point rule as HoseNetwork), straight pieces follow their snap points and elbows follow the real
    /// bend of the model (radius 0.45 m, straight stubs). A slug is a tube mesh rebuilt every frame along that path; it is
    /// thinner than the bore, so it sits inside the hose wall. Skipped on a dedicated server and when [Effects] TarFlow = false.
    /// </summary>
    internal static class TarFlow
    {
        const float BendRadius = 0.45f;         // elbow centre line radius (Blender build_elbow, R)
        const float PumpGap = 0.6f;             // virtual length through the pump body (not drawn)
        const float Step = 0.08f;               // ring spacing along the path
        const int Seg = 8;
        const int MaxSlugs = 8;
        static readonly Vector3 ElbowCorner = new(0.31f, 0.11f, -0.31f);    // elbow prefab: corner of the two legs (local)

        sealed class Path
        {
            public readonly List<Vector3> Pts = new();
            public readonly List<float> Cum = new();
            public float Length;
            public void Add(Vector3 p)
            {
                if (Pts.Count > 0)
                {
                    float d = Vector3.Distance(Pts[Pts.Count - 1], p);
                    if (d < 0.002f) return;
                    Length += d;
                }
                Pts.Add(p); Cum.Add(Length);
            }
        }

        sealed class Slug { public Path A, B; public float T0, Total; public bool Claimed, NoPour; public DtpPiece Pump; public GameObject Go; public Mesh Mesh; }

        const float MatchWindow = 1.5f;         // a pour belongs to a slug that started at most this long ago

        static readonly List<Slug> Slugs = new();
        static GameObject _host;
        static Material _mat, _matWater;

        static bool Enabled { get { return ModSettings.Effects.Value && ModSettings.TarFlow.Value && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null; } }

        static void EnsureHost()
        {
            if (_host != null) return;
            _host = new GameObject("DTP_TarFlow");
            Object.DontDestroyOnLoad(_host);
            _host.AddComponent<TarFlowRunner>();
        }

        // --- timing hooks for the nozzle effects --------------------------------------------------------------------------
        // The pour at the nozzle (stream, sound, deposit) is meant to happen when the slug of its stroke gets there. Strokes and pours
        // are matched first-in first-out per pump: every stroke that takes tar starts one slug (Stroke), every confirmed drain claims the
        // oldest unclaimed one (ClaimArrival).

        /// Seconds from now until the slug of the oldest unclaimed stroke of this pump reaches the nozzle (0 = it is there already), and
        /// marks that slug as taken. -1 = no slug to follow (TarFlow off, none in flight): the caller should not wait.
        public static float ClaimArrival(DtpPiece pump)
        {
            if (pump == null) return -1f;
            float speed = Mathf.Max(0.1f, ModSettings.TarFlowSpeed.Value), now = Time.time;
            for (int i = 0; i < Slugs.Count; i++)
            {
                var s = Slugs[i];
                if (s.Pump != pump || s.Claimed || s.NoPour || s.B == null || now - s.T0 > MatchWindow) continue;
                s.Claimed = true;
                return Mathf.Max(0f, s.T0 + s.Total / speed - now);      // same formula as Tick: the head is at the nozzle when (now - T0) * speed >= Total
            }
            return -1f;
        }

        sealed class Timed { public float At; public System.Action Do; }
        static readonly List<Timed> Pending = new();
        static readonly List<System.Action> Due = new();

        /// Runs `what` after `delay` seconds, on the same clock (Time.time) and in the same frame step as the slug animation.
        public static void Schedule(float delay, System.Action what)
        {
            if (what == null) return;
            EnsureHost();
            Pending.Add(new Timed { At = Time.time + delay, Do = what });
        }

        static void RunDue()
        {
            float now = Time.time;
            Due.Clear();
            for (int i = 0; i < Pending.Count;)
            {
                if (Pending[i].At <= now) { Due.Add(Pending[i].Do); Pending.RemoveAt(i); }
                else i++;
            }
            for (int i = 0; i < Due.Count; i++)
            {
                try { Due[i](); }
                catch (System.Exception e) { ModLog.Warn("TarFlow: scheduled effect failed: " + e.Message); }
            }
            Due.Clear();
        }

        /// Called on every stroke (the pumping player and every client that sees the pump). `takesTar` = false for a stroke that found nothing to pump:
        /// its slug is shown, but no pour will follow it.
        public static void Stroke(DtpPiece pump, bool takesTar = true, PumpLiquid liquid = PumpLiquid.Tar)
        {
            if (!Enabled || pump == null) return;
            try
            {
                var intake = Trace(pump, "intake");
                var outlet = Trace(pump, "outlet");
                if (intake == null && outlet == null) return;
                if (intake != null) intake = Reverse(intake);           // strainer tip -> pump
                EnsureHost();
                while (Slugs.Count >= MaxSlugs) Remove(0);
                var go = new GameObject("slug");
                go.transform.SetParent(_host.transform, false);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = Mat(liquid);
                mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                var mesh = new Mesh { name = "DTP_slug" }; mesh.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                float la = intake != null ? intake.Length : 0f, lb = outlet != null ? outlet.Length : 0f;
                Slugs.Add(new Slug
                {
                    A = intake, B = outlet, T0 = Time.time, Go = go, Mesh = mesh, Pump = pump, NoPour = !takesTar,
                    Total = la + (intake != null && outlet != null ? PumpGap : 0f) + lb
                });
            }
            catch (System.Exception e) { ModLog.Warn("TarFlow disabled after an error: " + e.Message); ModSettings.TarFlow.Value = false; }
        }

        static void Remove(int i)
        {
            var s = Slugs[i];
            if (s.Mesh != null) Object.Destroy(s.Mesh);
            if (s.Go != null) Object.Destroy(s.Go);
            Slugs.RemoveAt(i);
        }

        static Material Mat(PumpLiquid liquid = PumpLiquid.Tar)
        {
            if (liquid == PumpLiquid.Water) return WaterMat();
            if (_mat != null) return _mat;
            Shader sh = Shader.Find("Standard") ?? Shader.Find("Custom/Piece") ?? Shader.Find("Sprites/Default");
            _mat = new Material(sh) { name = "DTP_TarLiquid" };
            var c = new Color(0.035f, 0.028f, 0.02f, 1f);
            _mat.color = c;
            if (_mat.HasProperty("_Glossiness")) _mat.SetFloat("_Glossiness", 0.9f);
            if (_mat.HasProperty("_Metallic")) _mat.SetFloat("_Metallic", 0.1f);
            return _mat;
        }

        // water in the hoses: lighter, bluish and glossy so it reads as water against the dark tar
        static Material WaterMat()
        {
            if (_matWater != null) return _matWater;
            Shader sh = Shader.Find("Standard") ?? Shader.Find("Custom/Piece") ?? Shader.Find("Sprites/Default");
            _matWater = new Material(sh) { name = "DTP_WaterLiquid" };
            _matWater.color = new Color(0.30f, 0.55f, 0.72f, 1f);
            if (_matWater.HasProperty("_Glossiness")) _matWater.SetFloat("_Glossiness", 0.95f);
            if (_matWater.HasProperty("_Metallic")) _matWater.SetFloat("_Metallic", 0f);
            return _matWater;
        }

        // --- path tracing -------------------------------------------------------------------------------------------------
        /// Pump snap -> ... -> strainer tip / nozzle tip. null = the chain is not complete. Uses the same walk as the drain check
        /// (HoseNetwork.Walk), so what is drawn is exactly what is validated.
        static Path Trace(DtpPiece pump, string snapKey)
        {
            var w = HoseNetwork.Walk(pump, snapKey, snapKey.Contains("intake") ? DtpKind.Strainer : DtpKind.Nozzle);
            if (!w.Ok) return null;
            var path = new Path();
            path.Add(w.Start.position);
            foreach (var l in w.Links)
            {
                if (l.Piece.Kind == DtpKind.Elbow) AddElbow(path, l.Piece, l.Entry.position, l.Exit.position);
                else { path.Add(l.Entry.position); path.Add(l.Exit.position); }
            }
            path.Add(w.TerminalEntry.position);
            path.Add(w.Terminal.Kind == DtpKind.Strainer ? w.Terminal.StrainerTip : w.Terminal.NozzleTip);
            return path;
        }

        /// Centre line of the elbow tube: stub, quarter circle of BendRadius, stub (same as the Blender model).
        static void AddElbow(Path path, DtpPiece elbow, Vector3 a, Vector3 b)
        {
            Vector3 c = elbow.transform.TransformPoint(ElbowCorner);
            Vector3 u = (a - c).normalized, w = (b - c).normalized;
            float r = BendRadius;
            Vector3 t1 = c + u * r, t2 = c + w * r, o = c + (u + w) * r;
            path.Add(a); path.Add(t1);
            Vector3 d0 = t1 - o, d1 = t2 - o;
            for (int k = 1; k < 8; k++) path.Add(o + Vector3.Slerp(d0, d1, k / 8f));
            path.Add(t2); path.Add(b);
        }

        static Path Reverse(Path p)
        {
            var r = new Path();
            for (int i = p.Pts.Count - 1; i >= 0; i--) r.Add(p.Pts[i]);
            return r;
        }

        // --- geometry -------------------------------------------------------------------------------------------------------
        static Vector3 At(Path p, float s)
        {
            s = Mathf.Clamp(s, 0f, p.Length);
            int i = p.Cum.BinarySearch(s);
            if (i < 0) i = ~i;
            i = Mathf.Clamp(i, 1, p.Pts.Count - 1);
            float seg = p.Cum[i] - p.Cum[i - 1];
            float t = seg > 1e-5f ? (s - p.Cum[i - 1]) / seg : 0f;
            return Vector3.Lerp(p.Pts[i - 1], p.Pts[i], t);
        }

        static readonly List<Vector3> Verts = new();
        static readonly List<int> Tris = new();

        /// Appends a tube for path distance [s0, s1] (clipped to the path).
        static void AddTube(Path p, float s0, float s1, float radius)
        {
            if (p == null || p.Pts.Count < 2) return;
            s0 = Mathf.Max(s0, 0f); s1 = Mathf.Min(s1, p.Length);
            if (s1 - s0 < 0.02f) return;

            int rings = Mathf.Max(2, Mathf.CeilToInt((s1 - s0) / Step) + 1);
            int first = Verts.Count;
            Vector3 n = Vector3.zero, tanFirst = Vector3.forward, tanLast = Vector3.forward, centreFirst = Vector3.zero, centreLast = Vector3.zero;
            for (int i = 0; i < rings; i++)
            {
                float s = Mathf.Lerp(s0, s1, i / (float)(rings - 1));
                Vector3 c = At(p, s);
                Vector3 tan = (At(p, s + 0.04f) - At(p, s - 0.04f)).normalized;
                if (tan.sqrMagnitude < 0.5f) tan = Vector3.forward;
                if (i == 0)
                {
                    n = Vector3.Cross(tan, Mathf.Abs(tan.y) > 0.95f ? Vector3.right : Vector3.up).normalized;
                    tanFirst = tan; centreFirst = c;
                }
                else n = (n - tan * Vector3.Dot(n, tan)).normalized;        // parallel transport: no twisting between rings
                Vector3 bi = Vector3.Cross(tan, n);
                for (int j = 0; j < Seg; j++)
                {
                    float ang = 2f * Mathf.PI * j / Seg;
                    Verts.Add(c + (n * Mathf.Cos(ang) + bi * Mathf.Sin(ang)) * radius);
                }
                tanLast = tan; centreLast = c;
            }
            for (int i = 0; i < rings - 1; i++)
                for (int j = 0; j < Seg; j++)
                {
                    int j2 = (j + 1) % Seg;
                    int a = first + i * Seg + j, b = first + i * Seg + j2, c2 = first + (i + 1) * Seg + j, d = first + (i + 1) * Seg + j2;
                    Tris.Add(a); Tris.Add(b); Tris.Add(c2);
                    Tris.Add(b); Tris.Add(d); Tris.Add(c2);
                }
            // rounded-ish ends: one apex vertex in front of the head ring and behind the tail ring
            int apexHead = Verts.Count; Verts.Add(centreLast + tanLast * radius * 0.9f);
            int apexTail = Verts.Count; Verts.Add(centreFirst - tanFirst * radius * 0.9f);
            int lastRing = first + (rings - 1) * Seg;
            for (int j = 0; j < Seg; j++)
            {
                int j2 = (j + 1) % Seg;
                Tris.Add(lastRing + j); Tris.Add(lastRing + j2); Tris.Add(apexHead);
                Tris.Add(first + j2); Tris.Add(first + j); Tris.Add(apexTail);
            }
        }

        internal static void Tick()
        {
            if (Pending.Count > 0) RunDue();                                // before the meshes: the pour starts in the frame the head reaches the nozzle
            float speed = ModSettings.TarFlowSpeed.Value, len = ModSettings.TarSlugLength.Value;
            const float radius = 0.042f;                                    // bore radius is 0.048
            for (int i = Slugs.Count - 1; i >= 0; i--)
            {
                var s = Slugs[i];
                float la = s.A != null ? s.A.Length : 0f, lb = s.B != null ? s.B.Length : 0f;
                float total = la + (s.A != null && s.B != null ? PumpGap : 0f) + lb;
                float head = (Time.time - s.T0) * speed, tail = head - len;
                if (tail >= total) { Remove(i); continue; }

                Verts.Clear(); Tris.Clear();
                if (s.A != null) AddTube(s.A, tail, head, radius);
                if (s.B != null)
                {
                    float off = la + (s.A != null ? PumpGap : 0f);
                    AddTube(s.B, tail - off, head - off, radius);
                }
                s.Mesh.Clear();
                if (Verts.Count == 0) continue;
                s.Mesh.SetVertices(Verts);
                s.Mesh.SetTriangles(Tris, 0);
                s.Mesh.RecalculateNormals();
                s.Mesh.RecalculateBounds();
            }
        }
    }

    internal sealed class TarFlowRunner : MonoBehaviour
    {
        void Update() { TarFlow.Tick(); }
    }
}
