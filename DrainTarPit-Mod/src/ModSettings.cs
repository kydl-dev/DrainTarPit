using BepInEx.Configuration;
using DrainTarPit.Drain;
using DrainTarPit.Logging;
using DrainTarPit.Visuals;
using UnityEngine;

namespace DrainTarPit;

/// All BepInEx config entries in one place. Bound once from Plugin.Awake.
internal static class ModSettings
{
    // [General] / [Visuals]
    public static ConfigEntry<bool> DebugLog;
    public static ConfigEntry<string> TestOutDir;
    public static ConfigEntry<int> ConfigVersion;
    public static ConfigEntry<string> ShaderName;
    public static ConfigEntry<bool> UseMaskMap;
    public static ConfigEntry<float> AlbedoBrightness;
    public static ConfigEntry<HoseStyleOption> HoseStyle;
    public static ConfigEntry<float> HoseOpacity;
    public static ConfigEntry<bool> TarFlow;
    public static ConfigEntry<float> TarFlowSpeed;
    public static ConfigEntry<float> TarSlugLength;
    public static ConfigEntry<float> NozzleFlowAmount;
    public static ConfigEntry<bool> NozzlePuddle;
    public static ConfigEntry<float> NozzlePressure;
    public static ConfigEntry<float> NozzleFlowSeconds;
    public static ConfigEntry<bool> SyncPour;
    public static ConfigEntry<float> PourSoundOffset;

    // [Building]
    public static ConfigEntry<KeyCode> TiltKey;
    public static ConfigEntry<KeyCode> RollKey;
    public static ConfigEntry<KeyCode> TiltResetKey;
    public static ConfigEntry<float> TiltStep;
    public static ConfigEntry<float> PieceHealth;
    public static ConfigEntry<bool> Effects;
    public static ConfigEntry<float> EffectsVolume;
    public static ConfigEntry<float> SoundTailSeconds;
    public static ConfigEntry<bool> PumpDroplets;

    // [Drain]
    public static ConfigEntry<float> VolumePerStroke;
    public static ConfigEntry<float> DrainRadius;
    public static ConfigEntry<float> StrainerSearchRadius;
    public static ConfigEntry<float> StrainerReach;
    public static ConfigEntry<float> MinTarDepth;
    public static ConfigEntry<int> MaxChainPieces;
    public static ConfigEntry<float> SnapTolerance;
    public static ConfigEntry<float> PitFlowBoost;
    public static ConfigEntry<float> PitFlowBoostSeconds;
    public static ConfigEntry<float> RefillMinutes;
    public static ConfigEntry<bool> DepositTar;
    public static ConfigEntry<bool> PoolWalls;
    public static ConfigEntry<bool> PoolOverflow;
    public static ConfigEntry<bool> PoolRimDisappear;
    public static ConfigEntry<bool> AllowWater;
    public static ConfigEntry<float> MinWaterDepth;
    public static ConfigEntry<bool> DepositWater;
    public static ConfigEntry<float> WaterPoolSize;
    public static ConfigEntry<float> WaterCellSize;
    public static ConfigEntry<float> WaterDepthLook;
    public static ConfigEntry<bool> PoolShedFilm;
    public static ConfigEntry<float> PoolFilmDepth;
    public static ConfigEntry<float> WaterFlow;
    public static ConfigEntry<bool> PoolRunoff;
    public static ConfigEntry<bool> PoolMeshFix;
    public static ConfigEntry<int> PoolMeshReach;
    public static ConfigEntry<bool> PoolFilmEvaporates;
    public static ConfigEntry<KeyCode> EditorKey;

    // [Pump]
    public static ConfigEntry<float> StaminaPerStroke;
    public static ConfigEntry<float> HoldToCycleSeconds;
    public static ConfigEntry<float> CycleStrokeSeconds;
    public static ConfigEntry<bool> FacePump;
    public static ConfigEntry<float> StandYawOffset;
    public static ConfigEntry<string> AttachBool;
    public static ConfigEntry<string> AttachClipOverride;
    public static ConfigEntry<float> MaxPumpDistance;
    public static ConfigEntry<bool> HandIK;
    public static ConfigEntry<float> HandIKBlendSeconds;
    public static ConfigEntry<float> GripSpread;
    public static ConfigEntry<float> GripUp;
    public static ConfigEntry<float> GripForward;
    public static ConfigEntry<bool> HandRotate;
    public static ConfigEntry<float> FingerCurl;
    public static ConfigEntry<float> GripPalmOffset;
    public static ConfigEntry<float> ThumbCurl;
    public static ConfigEntry<float> ThumbSpread;
    public static ConfigEntry<bool> FreezeIdle;
    public static ConfigEntry<bool> BendToReach;
    public static ConfigEntry<float> MaxBackBend;
    public static ConfigEntry<float> MaxKneeBend;
    public static ConfigEntry<float> HipLock;
    public static ConfigEntry<float> BendSmooth;
    public static ConfigEntry<float> ClipBlendSeconds;
    public static ConfigEntry<float> StandHeight;
    public static ConfigEntry<float> StandForward;
    public static ConfigEntry<float> StandSide;
    public static ConfigEntry<bool> AutoReach;
    public static ConfigEntry<float> MaxAutoStep;
    public static ConfigEntry<float> MaxAutoRaise;

    public static void Bind(ConfigFile c)
    {
        ShaderName = c.Bind("Visuals", "ShaderName", "Custom/Piece",
            "Valheim shader the pump/hose materials are swapped to. Falls back to Custom/Piece, then Standard.");
        UseMaskMap = c.Bind("Visuals", "UseMaskMap", true,
            "Use the packed mask textures for metallic/smoothness. Turn off if metal/gloss looks wrong; scalar values are used instead.");
        AlbedoBrightness = c.Bind("Visuals", "AlbedoBrightness", 1.6f,
            new ConfigDescription(
                "Brightens the colour textures of all pump / hose pieces (gamma lift). 1 = original, 1.6 = a bit brighter, 2.5 = much brighter.",
                new AcceptableValueRange<float>(1f, 3f)));
        HoseStyle = c.Bind("Visuals", "HoseStyle", HoseStyleOption.Solid,
            "Look of the hose pieces (straight + elbow). Transparent = semi-transparent so the tar can be seen moving through them; Solid = the normal opaque texture. A change (config manager or editing the .cfg file while the game runs) applies right away to hoses you build from then on; hoses that are already built keep the look they were built with.");
        HoseOpacity = c.Bind("Visuals", "HoseOpacity", 0.45f,
            new ConfigDescription("How opaque the hoses are when HoseStyle = Transparent (1 = solid). Like HoseStyle it only applies to hoses built after the change.",
                new AcceptableValueRange<float>(0.1f, 1f)));
        DebugLog = c.Bind("General", "DebugLog", false,
            "Verbose logging (bundle contents, shader properties, per-material fixes, drain/pump events). Turn off once everything works.");
        TestOutDir = c.Bind("General", "TestOutDir", "",
            "Folder where the console tests (dtp ptest ...) write their report DrainTarPit.PoolTests.txt. Empty = the BepInEx config folder. The console command 'dtp ptest out <folder>' sets it.");

        TiltKey = c.Bind("Building", "TiltKey", KeyCode.LeftAlt,
            "Hold while scrolling to tilt the held piece up/down (pitch) instead of turning it. Keyboard/mouse only.");
        RollKey = c.Bind("Building", "RollKey", KeyCode.LeftControl,
            "Hold together with TiltKey while scrolling to roll the piece around its own axis instead of pitching it.");
        TiltResetKey = c.Bind("Building", "TiltResetKey", KeyCode.Backspace,
            "Resets pitch and roll of the held piece to flat.");
        TiltStep = c.Bind("Building", "TiltStep", 45f,
            new ConfigDescription("Degrees per scroll step for pitch/roll.", new AcceptableValueRange<float>(5f, 90f)));

        PieceHealth = c.Bind("Building", "PieceHealth", 200f,
            new ConfigDescription(
                "Health of the hoses, strainer and nozzle (the pump has twice this). Pieces never collapse from missing support, rain or fire; they only break when hit.",
                new AcceptableValueRange<float>(10f, 5000f)));
        Effects = c.Bind("Effects", "Effects", true,
            "Droplet effects and pump / splash sounds. Turn off if you do not want them (they are skipped automatically on dedicated servers).");
        EffectsVolume = c.Bind("Effects", "EffectsVolume", 0.6f,
            new ConfigDescription("Volume of the pump and splash sounds (0 = silent).",
                new AcceptableValueRange<float>(0f, 1f)));
        SoundTailSeconds = c.Bind("Effects", "SoundTailSeconds", 0.3f,
            new ConfigDescription(
                "The lever sound (pump_up) is faded out this many seconds after its loudest moment, which cuts the long reverb-like tail that sounded like an echo. 0 = play the whole recording.",
                new AcceptableValueRange<float>(0f, 2f)));

        PumpDroplets = c.Bind("Effects", "PumpDroplets", false,
            "Puff of tar droplets at the pump's outlet on every stroke. Off by default: the outlet is always fitted with a hose, so the droplets showed up as oil dripping between the pump's top pipe and the first hose (the tar is already shown moving through the hoses, see TarFlow).");

        TarFlow = c.Bind("Effects", "TarFlow", true,
            "Show a slug of tar travelling through the hoses on every pump stroke (strainer -> pump -> nozzle).");
        TarFlowSpeed = c.Bind("Effects", "TarFlowSpeed", 3.5f,
            new ConfigDescription("Speed (m/s) of the tar slug through the hoses.",
                new AcceptableValueRange<float>(0.5f, 10f)));
        TarSlugLength = c.Bind("Effects", "TarSlugLength", 2f,
            new ConfigDescription("Length (m) of the tar slug. Longer = the hose looks fuller while pumping.",
                new AcceptableValueRange<float>(0.2f, 6f)));

        NozzleFlowAmount = c.Bind("Effects", "NozzleFlowAmount", 1f,
            new ConfigDescription(
                "How much tar visibly pours out of the nozzle on every stroke (a falling stream, splashes where it lands and a puddle that fades). 0 = off, 1 = default, 4 = a lot. Shown to every player near the pump.",
                new AcceptableValueRange<float>(0f, 4f)));

        NozzlePuddle = c.Bind("Effects", "NozzlePuddle", true,
            "Show a small dark puddle on the ground where the stream lands (it spreads and fades after a few seconds). Turn off for just the stream and splashes.");

        NozzlePressure = c.Bind("Effects", "NozzlePressure", 4.5f,
            new ConfigDescription(
                "Speed (m/s) the tar leaves the nozzle with. Higher = the stream shoots further away from the nozzle before it falls (a nozzle pointing sideways throws it in an arc). 0.5 = it just drips out.",
                new AcceptableValueRange<float>(0.5f, 12f)));
        NozzleFlowSeconds = c.Bind("Effects", "NozzleFlowSeconds", 0.45f,
            new ConfigDescription(
                "How long (seconds) the stream keeps running after each press (a short, strong spurt that fades out). Lower = it stops sooner after the last press.",
                new AcceptableValueRange<float>(0.1f, 3f)));

        SyncPour = c.Bind("Effects", "SyncPour", true,
            "The stream, splashes, puddle, oil_pumped sound and the tar deposit happen at the moment the tar slug of that stroke reaches the nozzle (needs TarFlow). Off = they happen as soon as the pit has been drained, i.e. while the slug is still travelling through the hoses.");
        PourSoundOffset = c.Bind("Effects", "PourSoundOffset", 0f,
            new ConfigDescription(
                "Fine tuning (seconds) of the oil_pumped sound against the stream at the nozzle. Negative = the sound comes earlier, positive = later. Use it if your audio output has a delay (Bluetooth, large buffers). The silence at the start of the recording is already skipped.",
                new AcceptableValueRange<float>(-0.5f, 0.5f)));

        ModSettingsDrain.Bind(c);

        ModSettingsPump.Bind(c);

        Migrate(c);

        // hoses: a change (config manager, file reload) is applied live, see MaterialFixer.Tick
        HoseStyle.SettingChanged += (s, e) => MaterialFixer.RequestRefresh();
        HoseOpacity.SettingChanged += (s, e) => MaterialFixer.RequestRefresh();
        WaterDepthLook.SettingChanged += (s, e) => PoolWater.RequestRefresh();
        PoolMeshFix.SettingChanged += (s, e) => PoolMesh.Refresh();
        PoolMeshReach.SettingChanged += (s, e) => PoolMesh.Refresh();
        WaterFlow.SettingChanged += (s, e) => TarPool.ApplyWaterFlow();       // live: existing water pools + the prefabs for new ones
        PoolMesh.Refresh();
    }

    /// 0.5.0 raised several defaults (a config file keeps the old default as a saved value, so they would never apply) and removed
    /// the Tar-item options. Values that still equal the OLD default are moved to the new default once; anything you changed
    /// yourself is left alone. Entries that no longer exist are dropped from the file.
    static void Migrate(ConfigFile c)
    {
        try
        {
            ConfigVersion = c.Bind("General", "ConfigVersion", 0,
                new ConfigDescription("Internal: used to carry old config files over to new defaults. Do not change.", null, new ConfigurationManagerAttributes { Browsable = false }));
            if (ConfigVersion.Value < 1)
            {
                if (Mathf.Approximately(VolumePerStroke.Value, 1.5f)) VolumePerStroke.Value = 6f;
                if (Mathf.Approximately(DrainRadius.Value, 2.5f)) DrainRadius.Value = 4f;
                if (Mathf.Approximately(TarFlowSpeed.Value, 2.5f)) TarFlowSpeed.Value = 3.5f;
                if (Mathf.Approximately(TarSlugLength.Value, 1.2f)) TarSlugLength.Value = 2f;
                ConfigVersion.Value = 1;
            }
            if (ConfigVersion.Value < 2)
            {
                // pump posture: hips held still, the upper body does the work (little knee bend, more back bend)
                if (Mathf.Approximately(MaxKneeBend.Value, 0.25f)) MaxKneeBend.Value = 0.08f;
                if (Mathf.Approximately(MaxBackBend.Value, 35f)) MaxBackBend.Value = 45f;
                ConfigVersion.Value = 2;
            }
            // entries of removed options (DropTarItems / TarItemsPerCubicMeter / ClearHoses) are dropped from the file; the property is not public in every BepInEx build
            var orphans = typeof(ConfigFile).GetProperty("OrphanedEntries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            var dict = orphans != null ? orphans.GetValue(c, null) as System.Collections.IDictionary : null;
            if (ConfigVersion.Value < 3)
            {
                // 0.7.0: [Visuals] ClearHoses (true / false) became the HoseStyle dropdown; carry the old choice over once
                if (dict != null)
                    foreach (System.Collections.DictionaryEntry e in dict)
                    {
                        var def = e.Key as ConfigDefinition;
                        if (def != null && def.Section == "Visuals" && def.Key == "ClearHoses" && string.Equals(e.Value as string, "false", System.StringComparison.OrdinalIgnoreCase))
                            HoseStyle.Value = HoseStyleOption.Solid;
                    }
                ConfigVersion.Value = 3;
            }
            if (dict != null) dict.Clear();
            c.Save();
        }
        catch (System.Exception e) { ModLog.Warn("Config migration skipped: " + e.Message); }
    }

    sealed class ConfigurationManagerAttributes { public bool? Browsable; }
}

/// Entries of an enum config value show up as a drop-down menu in the config manager (and list their values in the .cfg file).
internal enum HoseStyleOption { Transparent, Solid }
