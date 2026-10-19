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
        internal struct Dep { public Vector3 At; public float Volume; public int Hop; }
        static readonly Dictionary<long, float> LastDepositAt = new();

        /// `hop` = how many pools in a row the liquid has already run on through (0 = poured by a player, see TarPool.Continue).
        public static void RequestDeposit(LiquidVolume v, Vector3 at, float volume, int hop = 0)
        {
            var nv = Nview(v);
            if (nv == null || !nv.IsValid()) return;
            var pkg = new ZPackage();
            pkg.Write(at); pkg.Write(volume); pkg.Write(hop);
            nv.InvokeRPC(RpcDeposit, pkg);
        }

        // runs on the owner of the volume; the request comes from another machine, so it is checked like a drain request
        internal static void OnDepositRequest(LiquidVolume v, long sender, ZPackage pkg)
        {
            var at = pkg.ReadVector3(); float volume = pkg.ReadSingle(); int hop = pkg.ReadInt();
            if (float.IsNaN(volume) || float.IsInfinity(volume) || volume <= 0f) return;
            if (hop < 0 || hop > TarPool.MaxRunoffHops + 1) return;
            if (float.IsNaN(at.x) || float.IsNaN(at.y) || float.IsNaN(at.z)) return;
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
            DepositLocal(v, at, volume, hop);
        }

        /// Runs on the owner. Raises the liquid in the basin around `at` by `volume` m3. Queued until the volume knows the ground heights (a freshly spawned one).
        /// What reaches the edge of the grid while the ground goes on downhill continues into the next pool (TarPool.Continue); `hop` counts those pools.
        public static void DepositLocal(LiquidVolume v, Vector3 at, float volume, int hop = 0)
        {
            var nv = Nview(v);
            if (v == null || nv == null || !nv.IsValid() || !nv.IsOwner() || volume <= 0f) return;
            if (!HeightsReady(v))
            {
                List<Dep> q;
                if (!PendingDeposits.TryGetValue(v, out q)) PendingDeposits[v] = q = new List<Dep>();
                q.Add(new Dep { At = at, Volume = volume, Hop = hop });
                return;
            }
            float added, runoff; Vector3 runoffAt;
            DropThin = 0f;
            using (Hold(v)) added = FillBasin(v, at, volume, out runoff, out runoffAt);
            float dropped = DropThin * v.m_scale * v.m_scale;
            float lost = Mathf.Max(0f, volume - added - runoff - dropped);       // what is in none of the other numbers: no basin / no reachable cell
            var bg = BudgetOf(v);
            bg.Poured += volume; bg.Stayed += added; bg.Edge += runoff; bg.Thin += dropped; bg.Lost += lost;
            if (added > 0f) { FDirty.SetValue(v, true); FNeedsSaving.SetValue(v, true); BoostFlow(v); }     // the freshly poured liquid spreads out quickly
            ModLog.Verbose("Pit " + v.name + ": " + added.ToString("0.00") + " of " + volume.ToString("0.00") + " m3 stayed in the basin" + (runoff > 0.01f ? ", " + runoff.ToString("0.00") + " m3 reached the edge of the grid" : " (the rest ran off)"));
            LastDeposit = Time.time.ToString("0") + "s: " + added.ToString("0.00") + " of " + volume.ToString("0.00") + " m3 stayed, " + runoff.ToString("0.00") + " m3 reached the grid edge, " + dropped.ToString("0.00") + " m3 dropped as a thin film behind a rim, " + lost.ToString("0.00") + " m3 found no basin (hop " + hop + ")";
            LastDiag = Diag;
            if (ModSettings.DebugLog.Value) ModLog.Info("Pit " + v.name + " deposit: " + LastDeposit + " | " + Diag);
            if (runoff > 0.05f) TarPool.Continue(v, runoffAt, runoff, hop + 1);
        }

        internal static void FlushDeposits(LiquidVolume v)
        {
            List<Dep> q;
            if (PendingDeposits.Count == 0 || !PendingDeposits.TryGetValue(v, out q) || !HeightsReady(v)) return;
            var nv = Nview(v);
            if (nv == null || !nv.IsValid() || !nv.IsOwner()) return;
            PendingDeposits.Remove(v);
            foreach (var p in q) DepositLocal(v, p.At, p.Volume, p.Hop);
        }
    }
}
