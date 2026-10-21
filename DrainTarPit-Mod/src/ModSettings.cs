using BepInEx.Configuration;
using DrainTarPit.Drain;
using DrainTarPit.Logging;
using DrainTarPit.Visuals;
using UnityEngine;

namespace DrainTarPit;

/// <summary>
/// All settings in one place. T-068 (user, config review 2026-10-21): what a player may change is a BepInEx config entry (bound once from
/// Plugin.Awake); everything else is a Tunable with the value the user chose: not in the config file, changeable at runtime only from the
/// user's own KydlTools mod. Split over partial files: ModSettingsDrain ([Drain]), ModSettingsPump ([Pump]), ModSettingsHose (the two hose categories).
/// T-069: the water / pool settings are in the WaterAndTarPools mod's config; it sets the water Tunables below (AllowWater, MinWaterDepth, ...).
/// </summary>
internal static partial class ModSettings
{
    // [General] / [DebugLog]
    public static ConfigEntry<bool> EnableAll;
    public static ConfigEntry<bool> DebugLog;
    /// Is the mod switched on ([General] EnableAll)? Off = inert: no pumping, no effects, no menus; built pieces stay in the world.
    public static bool On { get { return EnableAll == null || EnableAll.Value; } }

    // [World Hose Visuals] / [Build Time Hose Visuals] (ModSettingsHose)
    public static ConfigEntry<WorldHoseStyleOption> WorldHoseStyle;
    public static ConfigEntry<float> WorldHoseOpacity;
    public static ConfigEntry<HoseStyleOption> BuildHoseStyle;
    public static ConfigEntry<float> BuildHoseOpacity;

    // [Building]
    public static ConfigEntry<KeyCode> TiltKey;
    public static ConfigEntry<KeyCode> RollKey;
    public static ConfigEntry<KeyCode> TiltResetKey;
    public static ConfigEntry<float> TiltStep;

    // [Effects]
    public static ConfigEntry<bool> SoundEffects;
    public static ConfigEntry<float> EffectsVolume;
    public static ConfigEntry<bool> ScratchSounds;
    public static ConfigEntry<bool> TarSplashSound;
    public static ConfigEntry<bool> TarSlugFlow;
    public static ConfigEntry<float> LiquidFlowSpeed;
    public static ConfigEntry<float> NozzleFlowAmount;
    public static ConfigEntry<bool> NozzlePuddle;
    public static ConfigEntry<float> NozzlePressure;
    public static ConfigEntry<float> NozzleFlowSeconds;
    public static ConfigEntry<float> PourSoundOffset;

    // [Drain] (ModSettingsDrain)
    public static ConfigEntry<float> VolumePerStroke;
    public static ConfigEntry<float> DrainRadius;
    public static ConfigEntry<float> StrainerSearchRadius;
    public static ConfigEntry<float> StrainerReach;
    public static ConfigEntry<float> MinTarDepth;
    public static ConfigEntry<int> MaxChainPieces;
    public static ConfigEntry<float> SnapTolerance;
    public static ConfigEntry<float> RefillMinutes;

    // [Pump] (ModSettingsPump: AutoReach in the config, the rest Tunables)
    public static ConfigEntry<bool> AutoReach;

    // --- fixed values (Tunables): not in the config file, KydlTools can change them for testing --------------------------------------------
    public static readonly Tunable<string> TestOutDir = new("DebugLog", "TestOutDir", "",
        "Folder where the console tests (dtp ptest ...) write their report DrainTarPit.PoolTests.txt. Empty = the BepInEx config folder.");
    public static readonly Tunable<string> ShaderName = new("Visuals", "ShaderName", "Custom/Piece",
        "Valheim shader the pump / hose materials are swapped to; falls back to Custom/Piece, then Standard.");
    public static readonly Tunable<bool> UseMaskMap = new("Visuals", "UseMaskMap", true,
        "Use the packed mask textures for metallic / smoothness (off = scalar values).");
    public static readonly Tunable<float> AlbedoBrightness = new("Visuals", "AlbedoBrightness", 1.5f,
        "Brightens the colour textures of all pump / hose pieces (gamma lift). Read when the pieces are prepared (game start).");
    public static readonly Tunable<float> PieceHealth = new("Building", "PieceHealth", 400f,
        "Health of the hoses, strainer, nozzle and pool sign; the pump has twice this. Structural support is not changed.");
    public static readonly Tunable<float> SoundTailSeconds = new("Effects", "SoundTailSeconds", 0.3f,
        "The lever sound (pump_up) is faded out this many seconds after its loudest moment.");
    public static readonly Tunable<bool> DrySound = new("Effects", "DrySound", true,
        "Pump sounds go to the game's Effects mixer group (no outdoor reverb), with the game's sound-effects volume.");
    public static readonly Tunable<bool> PumpDroplets = new("Effects", "PumpDroplets", false,
        "Puff of droplets at the pump's outlet on every stroke.");
    public static readonly Tunable<bool> SyncPour = new("Effects", "SyncPour", true,
        "Stream, splashes, puddle, pour sound and deposit happen when the slug of that stroke reaches the nozzle.");
    public static readonly Tunable<float> TarSlugLength = new("Effects", "TarSlugLength", 2f,
        "Length (m) of the liquid slug in the hoses.");
    public static readonly Tunable<float> PitFlowBoost = new("Drain", "PitFlowBoost", 9f,
        "While a pit is pumped its tar flows towards the hole faster: multiplies the pit's viscosity value (9 = about 3x faster).");
    public static readonly Tunable<float> PitFlowBoostSeconds = new("Drain", "PitFlowBoostSeconds", 20f,
        "How long (s) after the last stroke a pit keeps PitFlowBoost.");

    // T-069: set by WaterAndTarPools from its config (without that mod: no water pumping, AllowWater stays off)
    public static readonly Tunable<bool> AllowWater = new("Pools", "AllowWater", false,
        "Strainers built in water pump water (sea / lake / a pump pool). On while WaterAndTarPools is installed.");
    public static readonly Tunable<float> MinWaterDepth = new("Pools", "MinWaterDepth", 0.3f,
        "Minimum water depth (m) at the strainer tip for it to count as 'in water' (WaterAndTarPools config).");
    public static readonly Tunable<bool> WaterSplashSound = new("Effects", "WaterSplashSound", true,
        "Water splash where the pumped water comes down (WaterAndTarPools config).");
    public static readonly Tunable<float> WaterSplashVolume = new("Effects", "WaterSplashVolume", 0.6f,
        "Volume of the water splash (WaterAndTarPools config).");
    public static readonly Tunable<float> WaterSplashOffset = new("Effects", "WaterSplashOffset", -0.3f,
        "Seconds added to the moment the water splash plays (negative = earlier).");
    public static readonly Tunable<bool> WaterSlugFlow = new("Effects", "WaterSlugFlow", true,
        "Slug of water travelling through see-through hoses (WaterAndTarPools config).");

    public static void Bind(ConfigFile c)
    {
        var savedLines = ModSettingsCommaFix.Capture(c);     // before the first Bind: each Bind saves the file with what BepInEx parsed
        EnableAll = c.Bind("General", "EnableAll", true,
            "Switches the whole mod on / off while playing. Off: no pumping, no liquid moving, no sounds or effects from this mod; pieces already built stay in the world and can still be built.");
        DebugLog = c.Bind("DebugLog", "Enable", false,
            "Writes detailed lines about everything the mod does to the BepInEx log (for bug reports). Leave off while playing normally.");
        ModSettingsHose.Bind(c);                                  // [World Hose Visuals] / [Build Time Hose Visuals]

        TiltKey = c.Bind("Building", "TiltKey", KeyCode.LeftAlt,
            "Hold while scrolling to tilt the held piece up/down (pitch) instead of turning it. Keyboard/mouse only.");
        RollKey = c.Bind("Building", "RollKey", KeyCode.LeftControl,
            "Hold together with TiltKey while scrolling to roll the piece around its own axis instead of pitching it.");
        TiltResetKey = c.Bind("Building", "TiltResetKey", KeyCode.Backspace,
            "Resets pitch and roll of the held piece to flat.");
        TiltStep = c.Bind("Building", "TiltStep", 45f,
            new ConfigDescription("Degrees per scroll step for pitch/roll.", new AcceptableValueRange<float>(5f, 90f)));

        SoundEffects = c.Bind("Effects", "SoundEffects", true,
            "All sounds of this mod (pump strokes, the liquid coming out of the nozzle). Off = the mod makes no sound at all.");
        EffectsVolume = c.Bind("Effects", "EffectsVolume", 0.6f,
            new ConfigDescription("Volume of the pump sounds and of the tar coming out of the nozzle (0 = silent).",
                new AcceptableValueRange<float>(0f, 1f)));
        ScratchSounds = c.Bind("Effects", "ScratchSounds", true,
            "Pump stroke sound in three parts: a scratch while the piston rises, a scratch while it falls, and the lever hit when it reaches the bottom. Off = only the lever hit.");
        TarSplashSound = c.Bind("Effects", "TarSplashSound", true,
            "Play the sound of tar pouring out of the nozzle. Off = tar pours silently.");
        TarSlugFlow = c.Bind("Effects", "TarSlugFlow", true,
            "Show a slug of tar travelling through the hoses on every pump stroke (strainer -> pump -> nozzle; see-through hoses).");
        LiquidFlowSpeed = c.Bind("Effects", "LiquidFlowSpeed", 3.5f,
            new ConfigDescription("Speed (m/s) of the liquid slug through the hoses (tar and water).",
                new AcceptableValueRange<float>(0.5f, 10f)));
        NozzleFlowAmount = c.Bind("Effects", "NozzleFlowAmount", 1f,
            new ConfigDescription(
                "How much liquid visibly pours out of the nozzle on every stroke (a falling stream, splashes where it lands and a puddle that fades). 0 = off, 1 = default, 4 = a lot. Shown to every player near the pump.",
                new AcceptableValueRange<float>(0f, 4f)));
        NozzlePuddle = c.Bind("Effects", "NozzlePuddle", true,
            "Show a small puddle on the ground where the stream lands (it spreads and fades after a few seconds). Off = just the stream and splashes.");
        NozzlePressure = c.Bind("Effects", "NozzlePressure", 4.5f,
            new ConfigDescription(
                "Speed (m/s) the liquid leaves the nozzle with. Higher = the stream shoots further away from the nozzle before it falls (a nozzle pointing sideways throws it in an arc). 0.5 = it just drips out.",
                new AcceptableValueRange<float>(0.5f, 12f)));
        NozzleFlowSeconds = c.Bind("Effects", "NozzleFlowSeconds", 0.45f,
            new ConfigDescription(
                "How long (seconds) the stream keeps running after each press (a short, strong spurt that fades out). Lower = it stops sooner after the last press.",
                new AcceptableValueRange<float>(0.1f, 3f)));
        PourSoundOffset = c.Bind("Effects", "PourSoundOffset", 0f,
            new ConfigDescription(
                "Fine tuning (seconds) of the pour sound against the stream at the nozzle. Negative = the sound comes earlier, positive = later. Use it if your audio output has a delay (Bluetooth, large buffers).",
                new AcceptableValueRange<float>(-0.5f, 0.5f)));

        BindDrain(c);
        BindPump(c);

        ModSettingsCommaFix.Apply(c, savedLines);       // after every Bind: "3,5" typed by hand means 3.5, not 35
        DropRemoved(c);

        // hoses: a change (config manager, file reload) is applied live, see MaterialFixer.Tick
        ModSettingsHose.Hook();
        EnableAll.SettingChanged += (s, e) => ModLog.Info("Config: EnableAll = " + EnableAll.Value + (EnableAll.Value ? ", the mod is on" : ", the mod is inert (no pumping, effects or menus)"));
        ModLog.Verbose("Config: bound (EnableAll " + EnableAll.Value + ", debug log " + DebugLog.Value + ")");
    }

    /// Entries that are not bound any more (removed or fixed in code) are dropped from the file, so the file only lists what can be changed.
    /// Returns the number of dropped entries.
    static int DropRemoved(ConfigFile c)
    {
        int dropped = 0;
        try
        {
            // the property is not public in every BepInEx build
            var orphans = typeof(ConfigFile).GetProperty("OrphanedEntries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            var dict = orphans != null ? orphans.GetValue(c, null) as System.Collections.IDictionary : null;
            if (dict != null) { dropped = dict.Count; dict.Clear(); }
            c.Save();
            if (dropped > 0) ModLog.Info("Config: " + dropped + " entry(ies) that are no longer settings were removed from the file");
        }
        catch (System.Reflection.TargetInvocationException e) { ModLog.Warn("Config: old entries could not be removed: " + e.InnerException?.Message); }
        catch (System.IO.IOException e) { ModLog.Warn("Config: the file could not be saved after removing old entries: " + e.Message); }
        return dropped;
    }
}

/// Entries of an enum config value show up as a drop-down menu in the config manager (and list their values in the .cfg file).
internal enum HoseStyleOption { Transparent, Solid }

/// [World Hose Visuals] World Hose Style: Default = every hose shows the look it was built with; Transparent / Solid = all hoses in the world, for now.
internal enum WorldHoseStyleOption { Default, Transparent, Solid }

/// Water pool look: Game = the game's own water shader with the saved / built-in look; the others = the mod's own pool water shader
/// (Unity, DTP/PoolWater, all procedural) with a preset material from the bundle.
internal enum WaterStyleOption { Game, Tropical, Lake, Ashlands }
