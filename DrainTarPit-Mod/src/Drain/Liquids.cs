using Kydl.Common;
using HarmonyLib;
using UnityEngine;

namespace DrainTarPit.Drain
{
    /// What a pump system moves. A strainer is locked to ONE of these when it is built (DtpPiece.Liquid); the whole pipe system then only ever pumps that.
    public enum PumpLiquid { None = 0, Tar = 1, Water = 2 }

    /// <summary>
    /// Finds out what a strainer tip is sitting in.
    ///   Tar   - a vanilla tar LiquidVolume with at least [Drain] MinTarDepth of tar under / around the tip (TarPits).
    ///   Water - a water pool (a LiquidVolume of type Water that this mod filled, TarPool) with at least [Drain] MinTarDepth of water under / around the tip, or the
    ///           world's water: Valheim has one global water plane (ZoneSystem.m_waterLevel, 30 m) over the terrain, so there is nothing to
    ///           lower. The water at a point is `waterLevel - terrain height`; a tip counts as 'in water' when that is at least [Drain] MinWaterDepth
    ///           and the tip hangs at most [Drain] StrainerReach above the surface. The sea never runs dry; a pool does (it is a finite volume).
    /// Tar is checked first (a pit is never below the water plane, but a tip inside tar must never be taken for water).
    /// </summary>
    internal static class Liquids
    {
        static readonly System.Reflection.FieldInfo FWaterLevel = AccessTools.Field(typeof(ZoneSystem), "m_waterLevel");
        const float DefaultWaterLevel = 30f;

        public static float WaterLevel()
        {
            if (ZoneSystem.instance != null && FWaterLevel != null)
            {
                try { return (float)FWaterLevel.GetValue(ZoneSystem.instance); } catch { }
            }
            return DefaultWaterLevel;
        }

        /// true when there is at least MinWaterDepth of water over the terrain at the x/z of p. depth = water depth there, surfaceY = water surface height.
        public static bool WaterDepthAt(Vector3 p, out float depth, out float surfaceY)
        {
            surfaceY = WaterLevel(); depth = 0f;
            if (ZoneSystem.instance == null) return false;
            float ground = ZoneSystem.instance.GetGroundHeight(p);
            depth = surfaceY - ground;
            return depth >= ModSettings.MinWaterDepth.Value;
        }

        /// What a strainer tip at `tip` is in right now: Tar, Water or None.
        public static PumpLiquid Detect(Vector3 tip)
        {
            LiquidVolume pit; float depth, surface; Vector3 at;
            if (TarPits.TrySampleNear(tip, ModSettings.StrainerSearchRadius.Value, out pit, out depth, out surface, out at)
                && depth >= ModSettings.MinTarDepth.Value && tip.y <= surface + ModSettings.StrainerReach.Value)
                return PumpLiquid.Tar;
            if (ModSettings.AllowWater.Value && TarPits.TrySampleNear(tip, ModSettings.StrainerSearchRadius.Value, out pit, out depth, out surface, out at, LiquidType.Water)
                && depth >= ModSettings.MinTarDepth.Value && tip.y <= surface + ModSettings.StrainerReach.Value)
                return PumpLiquid.Water;
            if (ModSettings.AllowWater.Value && WaterDepthAt(tip, out depth, out surface) && tip.y <= surface + ModSettings.StrainerReach.Value)
                return PumpLiquid.Water;
            return PumpLiquid.None;
        }

        /// Localized name ("tar" / "water") for status text.
        public static string Name(PumpLiquid l)
        {
            return Loc.T(l == PumpLiquid.Water ? "dtp_liquid_water" : "dtp_liquid_tar");
        }
    }
}
