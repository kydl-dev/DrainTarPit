using System.Collections.Generic;
using DrainTarPit.Logging;
using UnityEngine;
using static DrainTarPit.Drain.TarPits;
using static DrainTarPit.Drain.TarPitFlow;
using static DrainTarPit.Drain.TarPitSettle;

namespace DrainTarPit.Drain
{
    /// <summary>
    /// Draining a pit with the pump (RPC to the pit owner, DrainLocal) and the slow refill of a drained vanilla pit.
    /// </summary>
    internal static class TarPitDrain
    {
        const string VarRefillTime = "TPD_refillT";  // world time (ms) of the last drain / refill step; lets refill catch up after unload

        /// Tar (m3) one stroke could pull from within radius of world point c (the pull is weighted, see Weight).
        public static float Available(LiquidVolume v, Vector3 c, float radius)
        {
            float sum = 0f;
            ForCells(v, c, radius, (i, depth, w) => sum += depth * w);
            return sum * v.m_scale * v.m_scale;
        }

        // Suction strength at a cell: 1 at the strainer, 0.2 at the edge of the radius (t = distance / radius, squared) - the pit
        // gets a funnel around the strainer instead of a flat-bottomed hole.
        static float Weight(float distSqOverRadiusSq)
        {
            float t = Mathf.Clamp01(1f - distSqOverRadiusSq);
            return 0.2f + 0.8f * t * t;
        }

        static void ForCells(LiquidVolume v, Vector3 c, float radius, System.Action<int, float, float> f)
        {
            using (Hold(v)) ForCellsLocked(v, c, radius, f);
        }

        static void ForCellsLocked(LiquidVolume v, Vector3 c, float radius, System.Action<int, float, float> f)
        {
            var d = Depths(v); int stride = v.m_width + 1;
            var g = ToGrid(v, c);
            float r = radius / v.m_scale;
            int x0 = Mathf.Max(1, Mathf.FloorToInt(g.x - r)), x1 = Mathf.Min(v.m_width - 1, Mathf.CeilToInt(g.x + r));
            int y0 = Mathf.Max(1, Mathf.FloorToInt(g.y - r)), y1 = Mathf.Min(v.m_width - 1, Mathf.CeilToInt(g.y + r));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x - g.x, dy = y - g.y;
                    float q = (dx * dx + dy * dy) / (r * r);
                    if (q > 1f) continue;
                    int i = y * stride + x;
                    if (d[i] > 0f) f(i, d[i], Weight(q));
                }
        }

        // --- draining -----------------------------------------------------------------------------------------------
        /// Asks the pit's owner to remove tar (single player routes to itself). The owner answers with RpcDrained so the
        /// requesting client - the one standing at the pump - shows exactly what was removed pouring out at the nozzle.
        public static void RequestDrain(LiquidVolume v, Vector3 center, float radius, float volume, Vector3 nozzleTip, ZDOID pumpId)
        {
            var nv = Nview(v);
            if (nv == null || !nv.IsValid()) return;
            var pkg = new ZPackage();
            pkg.Write(center); pkg.Write(radius); pkg.Write(volume); pkg.Write(nozzleTip); pkg.Write(pumpId);
            nv.InvokeRPC(RpcDrain, pkg);
        }

        // runs on the pit's owner. The request comes from another machine, so it is checked: sane numbers, capped at what one
        // stroke can take, centre inside this pit, the sender near it (the chain is at most MaxChainPieces * ~2.5 m long) and
        // not faster than a pump can be worked.
        static readonly Dictionary<long, float> LastDrainAt = new();
        internal static void OnDrainRequest(LiquidVolume v, long sender, ZPackage pkg)
        {
            var center = pkg.ReadVector3(); float radius = pkg.ReadSingle(), volume = pkg.ReadSingle();
            var nozzle = pkg.ReadVector3(); var pumpId = pkg.ReadZDOID();
            if (float.IsNaN(radius) || float.IsNaN(volume) || float.IsInfinity(radius) || float.IsInfinity(volume) || volume <= 0f || radius <= 0f) return;
            volume = Mathf.Min(volume, ModSettings.VolumePerStroke.Value * 1.01f + 0.001f);
            radius = Mathf.Min(radius, ModSettings.DrainRadius.Value + 0.01f);
            if (!InGrid(v, ToGrid(v, center))) return;
            bool local = ZDOMan.instance != null && sender == ZDOMan.GetSessionID();
            if (!local)
            {
                var peer = ZNet.instance != null ? ZNet.instance.GetPeer(sender) : null;
                float reach = ModSettings.MaxChainPieces.Value * 2.5f + 15f;
                if (peer == null || (peer.m_refPos - center).sqrMagnitude > reach * reach) return;
            }
            float last; float now = Time.unscaledTime;
            if (LastDrainAt.TryGetValue(sender, out last) && now - last < 0.2f) return;
            LastDrainAt[sender] = now;

            float removed = DrainLocal(v, center, radius, volume);
            if (removed <= 0f) return;
            var reply = new ZPackage();
            reply.Write(removed); reply.Write(nozzle); reply.Write(pumpId);
            Nview(v).InvokeRPC(sender, RpcDrained, reply);
        }

        // runs on the client that pumped; `v` is this machine's copy of the pit that answered, which says what came out (tar or water)
        internal static void OnDrained(LiquidVolume v, long sender, ZPackage pkg)
        {
            float removed = pkg.ReadSingle(); var nozzle = pkg.ReadVector3(); var pumpId = pkg.ReadZDOID();
            var go = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(pumpId) : null;
            DrainService.Poured(go != null ? go.GetComponent<ZNetView>() : null, nozzle, removed, LiquidOf(v));
        }

        static long NowMs() { return ZNet.instance != null ? (long)(ZNet.instance.GetTimeSeconds() * 1000.0) : 0L; }

        /// Runs on the pit's owner. Returns the volume actually removed.
        public static float DrainLocal(LiquidVolume v, Vector3 center, float radius, float volume)
        {
            var nv = Nview(v);
            if (nv == null || !nv.IsValid() || !nv.IsOwner()) return 0f;
            var d = Depths(v);
            float removed = 0f;
            using (Hold(v))
            {
                float scale2 = v.m_scale * v.m_scale;
                float availableDepthSum = 0f;          // weighted: what a full-strength pull (fraction 1) would take
                ForCells(v, center, radius, (i, depth, w) => availableDepthSum += depth * w);
                if (availableDepthSum <= 0f) return 0f;

                // remember the full volume the first time, so a refill knows where to stop
                var zdo = nv.GetZDO();
                if (zdo != null)
                {
                    if (zdo.GetFloat(VarBaseline, 0f) <= 0f) zdo.Set(VarBaseline, TotalVolume(v));
                    zdo.Set(VarRefillTime, NowMs()); SyncRevision(v);       // refill counts from the last drain
                }

                float wantDepthSum = Mathf.Min(availableDepthSum, volume / scale2);
                float fraction = wantDepthSum / availableDepthSum;     // 0..1; every wet cell in range gives depth * weight * fraction
                ForCells(v, center, radius, (i, depth, w) => { float take = depth * w * fraction; d[i] = depth - take; removed += take; });
                FDirty.SetValue(v, true);
                FNeedsSaving.SetValue(v, true);
                removed *= scale2;
            }
            if (removed > 0f) { BoostFlow(v); WakeItems(center, radius); }
            ModLog.Verbose("Pit " + v.name + ": drained " + removed.ToString("0.00") + " m3");
            return removed;
        }

        // --- refill (owner only, config RefillMinutes > 0) --------------------------------------------------------------
        // A drained pit seeps back to its baseline volume linearly over RefillMinutes (of world time, so it also
        // catches up for the time the pit was unloaded). Nothing is written for pits that were never drained.
        internal static readonly Dictionary<LiquidVolume, float> RefillTimers = new();

        internal static void RefillTick(LiquidVolume v, float dt)
        {
            float minutes = ModSettings.RefillMinutes.Value;
            if (minutes <= 0f) return;
            var nv = Nview(v);
            if (nv == null || !nv.IsValid() || !nv.IsOwner()) return;
            float t; RefillTimers.TryGetValue(v, out t);
            t += dt;
            if (t < 2f) { RefillTimers[v] = t; return; }
            RefillTimers[v] = 0f;

            var zdo = nv.GetZDO();
            if (zdo.GetBool(TarPool.ZPool, false)) return;      // pools made from pumped tar do not seep back on their own (that would create tar)
            float baseline = zdo.GetFloat(VarBaseline, 0f);
            if (baseline <= 0f) return;                         // never drained

            var d = Depths(v);
            using (Hold(v))
            {
                float scale2 = v.m_scale * v.m_scale;
                float depthSum = 0f;
                for (int i = 0; i < d.Count; i++) if (d[i] > 0f) depthSum += d[i];
                float deficit = baseline - depthSum * scale2;
                if (deficit < 0.01f) return;                    // full again

                long now = NowMs(), last = zdo.GetLong(VarRefillTime, 0L);
                zdo.Set(VarRefillTime, now); SyncRevision(v);
                if (last <= 0L || now <= last) return;
                float add = Mathf.Min(deficit, baseline / (minutes * 60f) * ((now - last) / 1000f));
                if (add <= 0f) return;

                if (depthSum > 0f)
                {
                    float k = 1f + add / scale2 / depthSum;            // raise every wet cell proportionally; the sim spreads it
                    for (int i = 0; i < d.Count; i++) if (d[i] > 0f) d[i] *= k;
                }
                else
                {
                    var h = Heights(v); int stride = v.m_width + 1, best = -1; float bh = float.MaxValue;
                    for (int y = 1; y < v.m_width; y++)
                        for (int x = 1; x < v.m_width; x++) if (h[y * stride + x] < bh) { bh = h[y * stride + x]; best = y * stride + x; }
                    if (best >= 0) d[best] += add / scale2;            // fully drained: seep back in at the lowest point
                }
                FDirty.SetValue(v, true);
                FNeedsSaving.SetValue(v, true);
            }
        }
    }
}
