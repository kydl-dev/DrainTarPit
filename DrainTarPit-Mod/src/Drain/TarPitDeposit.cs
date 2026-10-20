using System.Collections.Generic;
using DrainTarPit.Logging;
using UnityEngine;
using static DrainTarPit.Drain.TarPits;
using static DrainTarPit.Drain.TarPitFlood;
using static DrainTarPit.Drain.TarPitStats;
using static DrainTarPit.Drain.TarPitFlow;

namespace DrainTarPit.Drain
{
    /// <summary>
    /// Depositing pumped liquid into a volume: the RPC to the owner and DepositLocal, which raises the liquid in the basin around the landing point (queued until the ground heights are known).
    /// </summary>
    internal static class TarPitDeposit
    {
        // --- depositing (the pumped tar arrives) ----------------------------------------------------------------------------
        // The pumped tar is poured into a real vanilla tar LiquidVolume (see TarPool): either an existing one whose grid contains the
        // landing point, or a new one spawned there. The owner of that volume raises the liquid in the BASIN around the landing point
        // (steepest descent to the lowest reachable cell, then a priority flood: the level rises over the connected low cells until the
        // volume is used up; whatever would go over the rim of the grid runs off). The game's own simulation then spreads it, builds the
        // mesh and the liquid surface (players are slowed like in a vanilla pit), and it is saved with the volume.

        internal static readonly Dictionary<LiquidVolume, List<Dep>> PendingDeposits = new();     // waits for the ground heights
        internal struct Dep { public Vector3 At, Origin; public float Volume; public int Hop; }
        static readonly Dictionary<long, float> LastDepositAt = new();

        /// `hop` = how many pools in a row the liquid has already run on through (0 = poured by a player, see TarPool.Continue).
        /// `origin` = where the player's stroke landed (hop 0); runoff may not go further than RunoffRules.MaxRunoffDistance from it.
        public static void RequestDeposit(LiquidVolume v, Vector3 at, float volume, int hop = 0, Vector3? origin = null)
        {
            var nv = Nview(v);
            if (nv == null || !nv.IsValid()) return;
            var pkg = new ZPackage();
            pkg.Write(at); pkg.Write(volume); pkg.Write(hop); pkg.Write(origin ?? at);
            nv.InvokeRPC(RpcDeposit, pkg);
        }

        // runs on the owner of the volume; the request comes from another machine, so it is checked like a drain request
        internal static void OnDepositRequest(LiquidVolume v, long sender, ZPackage pkg)
        {
            var at = pkg.ReadVector3(); float volume = pkg.ReadSingle(); int hop = pkg.ReadInt(); var origin = pkg.ReadVector3();
            if (float.IsNaN(volume) || float.IsInfinity(volume) || volume <= 0f) return;
            if (hop < 0 || hop > TarPool.MaxRunoffHops + 1) return;
            if (float.IsNaN(at.x) || float.IsNaN(at.y) || float.IsNaN(at.z)) return;
            if (float.IsNaN(origin.x) || float.IsNaN(origin.z) || hop == 0) origin = at;
            volume = Mathf.Min(volume, ModSettings.VolumePerStroke.Value * 1.01f + 0.001f);
            if (!InGrid(v, ToGrid(v, at))) return;
            bool local = ZDOMan.instance != null && sender == ZDOMan.GetSessionID();
            if (!local)
            {
                var peer = ZNet.instance != null ? ZNet.instance.GetPeer(sender) : null;
                float reach = ModSettings.MaxChainPieces.Value * 2.5f + 25f;
                if (peer == null || (peer.m_refPos - at).sqrMagnitude > reach * reach) return;
            }
            // a pool that runs over into the next one sends its request in the same moment as the player's (and chains of them in one frame): only the player's own pours are rate limited
            if (hop == 0)
            {
                float last; float now = Time.unscaledTime;
                if (LastDepositAt.TryGetValue(sender, out last) && now - last < 0.2f) return;
                LastDepositAt[sender] = now;
            }
            DepositLocal(v, at, volume, hop, origin);
        }

        /// Runs on the owner. Raises the liquid in the basin around `at` by `volume` m3. Queued until the volume knows the ground heights (a freshly spawned one).
        /// What reaches the edge of the grid while the ground goes on downhill continues into the next pool (TarPool.Continue); `hop` counts those pools.
        public static void DepositLocal(LiquidVolume v, Vector3 at, float volume, int hop = 0, Vector3? origin = null)
        {
            var nv = Nview(v);
            if (v == null || nv == null || !nv.IsValid() || !nv.IsOwner() || volume <= 0f) return;
            if (!HeightsReady(v))
            {
                List<Dep> q;
                if (!PendingDeposits.TryGetValue(v, out q)) PendingDeposits[v] = q = new List<Dep>();
                q.Add(new Dep { At = at, Origin = origin ?? at, Volume = volume, Hop = hop });
                return;
            }
            // Volume checkpoints (step 5) and the per-stroke accounting equation (step 6): every part of the stroke has exactly one destination.
            float before, after; HeightField.FillResult r;
            using (Hold(v))
            {
                before = TotalVolume(v);
                r = FillBasin(v, at, volume);
                after = TotalVolume(v);
            }
            RunoffCleanup.Touched(v);
            if (r == null) { ModLog.Warn("Pit " + v.name + ": deposit of " + volume.ToString("0.00") + " m3 skipped, the grid is not usable"); return; }
            float c2 = v.m_scale * v.m_scale;
            float kept = r.Kept * c2, runoff = r.ToEdge * c2, rimRemoved = r.RimRemoved * c2, overflow = r.OverflowRemoved * c2, unplaced = r.Unplaced * c2;
            var runoffAt = r.EdgeCell >= 0 ? CellWorld(v, r.EdgeCell) : at;
            bool continued = runoff > 0.05f && TarPool.Continue(v, runoffAt, runoff, hop + 1, origin ?? at, at);
            float nextPool = continued ? runoff : 0f, edgeDiscarded = continued ? 0f : runoff;
            var bg = BudgetOf(v);
            bg.Poured += volume; bg.Stayed += kept; bg.Inner += r.OtherBasins * c2; bg.OutsideRim += r.Outside * c2; bg.Edge += runoff; bg.Thin += overflow;
            bg.RimRemoved += rimRemoved; bg.Lost += unplaced;
            if (kept > 0f) { FDirty.SetValue(v, true); FNeedsSaving.SetValue(v, true); BoostFlow(v); }     // the freshly poured liquid spreads out quickly
            TarPitLedger.Checkpoint(v, "deposit", before, after, "(poured " + volume.ToString("0.000") + " m3, hop " + hop + ")");
            TarPitLedger.Stroke(v, hop, volume, r, c2, nextPool, edgeDiscarded, after - before);
            ModLog.Verbose("Pit " + v.name + ": " + kept.ToString("0.00") + " of " + volume.ToString("0.00") + " m3 stayed" + (runoff > 0.01f ? ", " + runoff.ToString("0.00") + " m3 reached the edge of the grid" + (continued ? " (continues into the next pool)" : " (discarded)") : "") +
                (rimRemoved > 0.01f ? ", " + rimRemoved.ToString("0.00") + " m3 removed at the rim" : "") + (overflow > 0.01f ? ", " + overflow.ToString("0.00") + " m3 ran over the rim (not tracked)" : ""));
            LastDeposit = Time.time.ToString("0") + "s: " + kept.ToString("0.00") + " of " + volume.ToString("0.00") + " m3 stayed (landing basin " + (r.Retained * c2).ToString("0.00") + ", inner dips " + (r.OtherBasins * c2).ToString("0.00") +
                ", outside the rim " + (r.Outside * c2).ToString("0.00") + "), " + runoff.ToString("0.00") + " m3 reached the grid edge, " + rimRemoved.ToString("0.00") + " m3 removed at the rim, " + overflow.ToString("0.00") +
                " m3 ran over the rim (thin, not tracked), " + unplaced.ToString("0.00") + " m3 unplaced (hop " + hop + ")";
            LastDiag = Diag;
            if (ModSettings.DebugLog.Value) ModLog.Info("Pit " + v.name + " deposit: " + LastDeposit + " | " + Diag);
        }

        internal static void FlushDeposits(LiquidVolume v)
        {
            List<Dep> q;
            if (PendingDeposits.Count == 0 || !PendingDeposits.TryGetValue(v, out q) || !HeightsReady(v)) return;
            var nv = Nview(v);
            if (nv == null || !nv.IsValid() || !nv.IsOwner()) return;
            PendingDeposits.Remove(v);
            foreach (var p in q) DepositLocal(v, p.At, p.Volume, p.Hop, p.Origin);
        }
    }
}
