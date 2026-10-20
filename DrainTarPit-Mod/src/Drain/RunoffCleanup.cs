using System.Collections.Generic;
using DrainTarPit.Logging;
using UnityEngine;
using static DrainTarPit.Drain.TarPits;

namespace DrainTarPit.Drain
{
    /// <summary>
    /// User, 2026-10-20 (D-030): small leftover pools must not exist for players. A pool the RUNOFF made (liquid that ran on over the edge of another
    /// pool's grid, TarPool.Continue) is marked in its ZDO; when such a pool still holds less than MinKeep a while after the last liquid reached it,
    /// it is removed (its characters released first) - the liquid in it "vanished at the ground" (D-029). Pools a player poured into are never
    /// removed here. Pools made before this mark existed are not marked: `dtp cleanpools small [m3]` removes those by hand.
    /// </summary>
    internal static class RunoffCleanup
    {
        public const string ZRunoff = "TPD_runoff";       // ZDO flag: made by runoff, may be removed when (almost) empty
        public const float MinKeep = 1f;                  // m3: a runoff pool with less than this is removed
        public const float Grace = 60f;                   // s after the last deposit into it (the game spreads / settles the liquid first)
        const float CheckEvery = 5f;

        static readonly Dictionary<LiquidVolume, float> LastTouched = new Dictionary<LiquidVolume, float>();
        static readonly Dictionary<LiquidVolume, float> NextCheck = new Dictionary<LiquidVolume, float>();

        internal static void MarkRunoff(LiquidVolume v)
        {
            var nv = v != null ? Nview(v) : null;
            if (nv == null || !nv.IsValid() || nv.GetZDO() == null) return;
            nv.GetZDO().Set(ZRunoff, true);
            LastTouched[v] = Time.time;
        }

        /// DepositLocal: liquid reached `v` (resets the grace time).
        internal static void Touched(LiquidVolume v) { if (v != null) LastTouched[v] = Time.time; }

        internal static void Forget(LiquidVolume v) { LastTouched.Remove(v); NextCheck.Remove(v); }

        /// LiquidVolume.Update postfix, owner only.
        internal static void Tick(LiquidVolume v)
        {
            float now = Time.time, next;
            if (NextCheck.TryGetValue(v, out next) && now < next) return;
            NextCheck[v] = now + CheckEvery;
            var nv = Nview(v);
            if (nv == null || !nv.IsValid() || !nv.IsOwner() || nv.GetZDO() == null || !nv.GetZDO().GetBool(ZRunoff, false)) return;
            float last;
            if (!LastTouched.TryGetValue(v, out last)) { LastTouched[v] = now; return; }     // loaded from the save: wait a full grace time
            if (now - last < Grace || !HeightsReady(v) || !TarPoolDiagnostics.GridIsValid(v)) return;
            float volume = TotalVolume(v);
            if (float.IsNaN(volume) || volume >= MinKeep) return;
            foreach (var l in TarPitTracking.ReleaseAll(v)) ModLog.Info("Runoff pool " + v.name + ": " + l);
            ModLog.Info("Runoff pool " + v.name + " @ " + v.transform.position.ToString("0") + " removed: it kept only " + volume.ToString("0.00") +
                " m3 (< " + MinKeep + " m3) - the runoff vanished at the ground");
            Forget(v);
            ZNetScene.instance.Destroy(v.gameObject);
        }
    }
}
