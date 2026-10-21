using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DrainTarPit.Building;
using DrainTarPit.Drain;
using DrainTarPit.Logging;
using UnityEngine;

// T-069 (user, 2026-10-21): the pools, water and pool signs are the separate WaterAndTarPools mod, which needs DrainTarPit installed. It uses
// DrainTarPit's internal code directly (logging, settings, the pit sampling, the UI kit); the user's KydlTools mod too.
[assembly: InternalsVisibleTo("WaterAndTarPools")]
[assembly: InternalsVisibleTo("KydlTools")]

namespace DrainTarPit
{
    /// <summary>The colour of the water in see-through hoses and in the nozzle stream: Key = the water style it came from ("Game" = the fixed blue).</summary>
    internal struct WaterColour
    {
        public string Key;
        public Color Body, Glow;
        public static readonly Color GameBody = new(0.30f, 0.55f, 0.72f, 1f);
        public static WaterColour Game { get { return new WaterColour { Key = "Game", Body = GameBody, Glow = Color.black }; } }
        public override string ToString() { return Key + " (body " + Body + ", glow " + Glow + ")"; }
    }

    /// <summary>
    /// T-069: what DrainTarPit asks the WaterAndTarPools mod for. Without that mod every hook is empty: pumped liquid only pours out visually,
    /// strainers never pump water (ModSettings.AllowWater stays off), the hose water is the fixed blue. WaterAndTarPools fills them in its Awake.
    /// </summary>
    internal static class PoolHooks
    {
        /// Set by WaterAndTarPools when it is loaded (its version, for the log).
        internal static string PoolsMod = "";
        internal static bool PoolsInstalled { get { return PoolsMod.Length > 0; } }

        /// The pour of a stroke reaches the nozzle: deposit `volume` m3 of `liquid` where the stream from `at` (direction `dir`) lands.
        internal static Action<Vector3, Vector3, float, PumpLiquid> Deposit;

        /// The colour of the water one pump moves (pump, strainer tip, nozzle tip; any may be null). null = the fixed blue.
        internal static Func<DtpPiece, Vector3?, Vector3?, WaterColour> WaterColourFor;

        /// A liquid volume registers its RPCs on its ZNetView (once per volume, retried until its ZDO exists).
        internal static event Action<LiquidVolume, ZNetView> VolumeRegister;
        /// Every frame of a loaded liquid volume (LiquidVolume.Update postfix).
        internal static event Action<LiquidVolume> VolumeUpdate;
        /// A liquid volume is being destroyed (unloaded / removed).
        internal static event Action<LiquidVolume> VolumeDestroyed;

        /// Sounds other mods bring (WaterAndTarPools: water_splash), found by PumpFxSound.Clip when DrainTarPit's bundle has no such clip.
        internal static readonly Dictionary<string, AudioClip> Clips = new(StringComparer.OrdinalIgnoreCase);

        /// Extra `dtp` console sub-commands (name -> handler with the arguments after the name).
        internal static readonly Dictionary<string, Action<string[]>> Commands = new(StringComparer.OrdinalIgnoreCase);

        internal static WaterColour ColourFor(DtpPiece pump, Vector3? source, Vector3? dest)
        {
            if (WaterColourFor == null) { ModLog.Verbose("PoolHooks: water colour = the fixed blue (WaterAndTarPools not installed)"); return WaterColour.Game; }
            try { return WaterColourFor(pump, source, dest); }
            catch (MissingReferenceException e) { ModLog.VerboseWarn("PoolHooks: water colour lookup hit a removed object (" + e.Message + "), fixed blue used"); return WaterColour.Game; }
        }

        internal static int RaiseRegister(LiquidVolume v, ZNetView nv) { var h = VolumeRegister; if (h == null) return 0; h(v, nv); return h.GetInvocationList().Length; }
        internal static int RaiseUpdate(LiquidVolume v) { var h = VolumeUpdate; if (h == null) return 0; h(v); return 1; }
        internal static int RaiseDestroyed(LiquidVolume v) { var h = VolumeDestroyed; if (h == null) return 0; h(v); return 1; }
    }
}
