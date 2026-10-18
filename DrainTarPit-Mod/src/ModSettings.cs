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

        VolumePerStroke = c.Bind("Drain", "VolumePerStroke", 6f,
            new ConfigDescription(
                "Cubic meters of tar liquid removed per pump stroke (the liquid only; Tar items lying in the pit are never touched). Use the console command `dtp pits` to see how big the pits are.",
                new AcceptableValueRange<float>(0.05f, 100f)));
        DrainRadius = c.Bind("Drain", "DrainRadius", 4f,
            new ConfigDescription(
                "Meters around the strainer that tar is pulled from. The pull is strongest at the strainer and fades towards the edge, so the pit gets a funnel instead of a flat hole. Tar outside the radius flows in (see PitFlowBoost).",
                new AcceptableValueRange<float>(0.25f, 8f)));
        PitFlowBoost = c.Bind("Drain", "PitFlowBoost", 9f,
            new ConfigDescription(
                "While a pit is being pumped (and for PitFlowBoostSeconds after the last stroke) its tar flows towards the hole faster: this multiplies the pit's own viscosity value, which the game's simulation uses for the flow between neighbouring cells (the flow grows with its square root, so 9 is about 3x faster). 1 = vanilla speed.",
                new AcceptableValueRange<float>(1f, 100f)));
        PitFlowBoostSeconds = c.Bind("Drain", "PitFlowBoostSeconds", 20f,
            new ConfigDescription("How long (seconds) after the last stroke a pit keeps the PitFlowBoost speed.",
                new AcceptableValueRange<float>(0f, 300f)));
        StrainerSearchRadius = c.Bind("Drain", "StrainerSearchRadius", 1f,
            new ConfigDescription(
                "The strainer counts as 'in tar' if there is tar this close (m, sideways) to its tip, and pulls from the deepest tar around it. 0 = only straight under the tip.",
                new AcceptableValueRange<float>(0f, 3f)));
        StrainerReach = c.Bind("Drain", "StrainerReach", 0.5f,
            new ConfigDescription("How far (m) the strainer tip may hang ABOVE the tar surface and still pump.",
                new AcceptableValueRange<float>(0f, 2f)));
        MinTarDepth = c.Bind("Drain", "MinTarDepth", 0.1f,
            new ConfigDescription("Minimum tar depth (m) under the strainer tip for it to count as 'in tar'.",
                new AcceptableValueRange<float>(0.01f, 2f)));
        MaxChainPieces = c.Bind("Drain", "MaxChainPieces", 24,
            new ConfigDescription("Longest hose chain (pieces) that is followed from the pump to a strainer/nozzle.",
                new AcceptableValueRange<int>(2, 100)));
        SnapTolerance = c.Bind("Drain", "SnapTolerance", 0.3f,
            new ConfigDescription("Max distance (m) between two snap points for two pieces to count as connected.",
                new AcceptableValueRange<float>(0.05f, 1f)));
        RefillMinutes = c.Bind("Drain", "RefillMinutes", 60f,
            new ConfigDescription(
                "Minutes (world time) for a drained pit to seep back to its original volume. 0 = never refills. Resets with every pump stroke.",
                new AcceptableValueRange<float>(0f, 10000f)));

        DepositTar = c.Bind("Drain", "DepositTar", true,
            "The pumped tar is poured into a real vanilla tar liquid volume where the stream from the nozzle lands: the liquid rises in the lowest basin around that point (a hole dug with the pickaxe, a dip enclosed by walls) up to its rim. It is a normal tar surface (slows players, saved in the world, can be pumped again). Whatever does not fit runs off. Off = the tar only pours out visually and vanishes.");
        PoolWalls = c.Bind("Drain", "PoolWalls", true,
            "Built pieces (stone / wood walls, floors, ...) hold the tar of the pools the pump makes, like terrain does: a ring of walls (or a dip with walls on it) fills up to the lowest point of its rim instead of the tar sinking into the ground. Pieces of this mod, players and items are ignored; a roof floating over the pool does not count.");
        PoolOverflow = c.Bind("Drain", "PoolOverflow", true,
            "When the tar reaches the lowest point of the rim (the lowest wall top) the rest runs over it and fills whatever lies behind it (up to 3 more basins); what finds no basin runs off. Off = old behaviour: the lower ground behind the rim is filled up to the level of the pool.");
        AllowWater = c.Bind("Drain", "AllowWater", true,
            "Strainers built in water pump water, from the sea / a lake (endless) or from a water pool the pump made (finite). A strainer is locked to what its tip sits in when it is built (tar or water) and its whole pipe system only ever moves that. The water is poured into a real water volume where the stream lands (see DepositWater). Off = strainers in water do nothing (tar is unaffected).");
        MinWaterDepth = c.Bind("Drain", "MinWaterDepth", 0.3f,
            new ConfigDescription("Minimum water depth (m) over the terrain at the strainer tip for it to count as 'in water'.",
                new AcceptableValueRange<float>(0.05f, 3f)));
        DepositWater = c.Bind("Drain", "DepositWater", true,
            "The pumped water is poured into a real water liquid volume where the stream from the nozzle lands (same rules as DepositTar): it fills a hole dug with the pickaxe, a moat, or a dip enclosed by walls up to the lowest point of its rim, then runs over the rim and down the slope. The volume is saved in the world and can be pumped again with a strainer. Water that lands in the sea or a lake just joins it. Off = the water only pours out visually.");
        WaterPoolSize = c.Bind("Drain", "WaterPoolSize", 24f,
            new ConfigDescription(
                "Side length (m) of one water pool grid. Bigger pools hold longer moats in one piece but cost more simulation time. Read when the world loads; pools that already exist keep their size. (Replaces the old WaterPoolWidth, which counted cells.)",
                new AcceptableValueRange<float>(8f, 48f)));
        WaterCellSize = c.Bind("Drain", "WaterCellSize", 0.25f,
            new ConfigDescription(
                "Size (m) of one grid cell of a water pool. The water surface ends at a grid point, so the gap between the water and a stone wall is at most about one cell: smaller = water reaches the wall more closely (4x the cells at 0.25 vs 0.5, so more simulation time). Read when the world loads; pools that already exist keep their size.",
                new AcceptableValueRange<float>(0.15f, 0.5f)));
        WaterDepthLook = c.Bind("Visuals", "WaterDepthLook", 0.15f,
            new ConfigDescription(
                "How deep the water looks to the game's ocean shader (the pools use a copy of the ocean material). 0 = shallow, calm and clear (like the shore), 1 = open sea. Raise it if you want more colour / less see-through; lower it for calmer water. Applies live.",
                new AcceptableValueRange<float>(0f, 1f)));
        PoolShedFilm = c.Bind("Drain", "PoolShedFilm", true,
            "Liquid that runs over the rim of a pump pool (tar and water) does not linger as a thin film on the outside of the walls or on the ground: a film thinner than PoolFilmDepth that has lower ground next to it is moved down to the next cell about 7 times a second (it visibly slides off, fast), and a film that is left lying flat evaporates in a second or two. A film that reaches the border of the pool grid runs off. Off = the game's own slow spreading.");
        PoolFilmDepth = c.Bind("Drain", "PoolFilmDepth", 0.05f,
            new ConfigDescription("Depth (m) below which liquid counts as a thin film for PoolShedFilm. Higher = thicker sheets are cleared away too (also the thin shore of a pool on a slope).",
                new AcceptableValueRange<float>(0.01f, 0.2f)));
        WaterFlow = c.Bind("Drain", "WaterFlow", 4f,
            new ConfigDescription(
                "How freely water runs compared with tar: the pool simulation moves liquid between neighbouring cells with a speed that grows with the square root of this value (tar pits use their own, thick value). 1 = as thick as tar, 4 = about twice as fast, 16 = about four times. Read when the world loads.",
                new AcceptableValueRange<float>(1f, 64f)));
        PoolRunoff = c.Bind("Drain", "PoolRunoff", true,
            "When the liquid in a pump pool reaches the edge of its grid and the ground goes on downhill (a long moat, a ditch, a slope) the rest continues into the next pool beyond the edge (a new one is made if needed, at most 6 in a row), so a moat longer than one pool fills end to end. On flat ground nothing continues: it runs off. Applies to tar and water.");

        PoolMeshFix = c.Bind("Visuals", "PoolMeshFix", true,
            "Water pools only. The game draws the water mesh over the whole pool grid and puts the dry grid points at ground height, and next to a wall that is the TOP of the wall, so a sheet of water seemed to hang on the walls. On: the dry grid points near the water are put at the water level (the surface just continues under the wall / bank) and the others are sunk under the ground. Applies live.");
        PoolMeshReach = c.Bind("Visuals", "PoolMeshReach", 6,
            new ConfigDescription("PoolMeshFix: how many grid cells away from a wet point a dry point is still put at the water level (the rest is sunk 1 m). Higher hides the surface edge deeper in thick walls; lower is cheaper. Applies live.",
                new AcceptableValueRange<int>(1, 16)));
        PoolFilmEvaporates = c.Bind("Drain", "PoolFilmEvaporates", false,
            "PoolShedFilm: a film that lies flat with nothing deeper next to it evaporates (0.05 m/s). Off (default): such films stay (the game's slow spreading); only films on a slope / the face of a wall slide down. On was the 0.9.x behaviour: a pool with less than PoolFilmDepth of water evaporated in a second or two.");
        EditorKey = c.Bind("Visuals", "EditorKey", KeyCode.F8,
            "Opens / closes the live water editor in game: every property of the pool water material as sliders, the pool mesh / simulation settings, the numbers of the nearest water pool and test buttons (pour water at the crosshair, empty the pool). The look can be saved to BepInEx/config/DrainTarPit.WaterLook.txt and is applied again at the next start. Set to None to disable.");

        StaminaPerStroke = c.Bind("Pump", "StaminaPerStroke", 3f,
            new ConfigDescription("Stamina used per pump stroke.", new AcceptableValueRange<float>(0f, 50f)));
        HoldToCycleSeconds = c.Bind("Pump", "HoldToCycleSeconds", 0.4f,
            new ConfigDescription(
                "Hold the use button (E) this long to switch from single strokes to continuous pumping. Release to stop.",
                new AcceptableValueRange<float>(0.1f, 2f)));
        CycleStrokeSeconds = c.Bind("Pump", "CycleStrokeSeconds", 1.2f,
            new ConfigDescription(
                "Fallback seconds per loop while pumping continuously; the real Cycle clip length is used when the clip is found.",
                new AcceptableValueRange<float>(0.3f, 5f)));
        FacePump = c.Bind("Pump", "FacePump", true,
            "Turn the player towards the lever when mounting the pump (the stand point is rotated at runtime). Turn off to use the stand_point's own rotation.");
        StandYawOffset = c.Bind("Pump", "StandYawOffset", 0f,
            new ConfigDescription(
                "Extra yaw (degrees) added to the facing direction when mounting. Use 90 / -90 / 180 if the player stands sideways or backwards at the pump.",
                new AcceptableValueRange<float>(-180f, 180f)));
        AttachBool = c.Bind("Pump", "AttachBool", "attach_chair",
            "Vanilla player animator bool used to pin the player in place while pumping. Its clip is replaced by the pump clips.");
        AttachClipOverride = c.Bind("Pump", "AttachClipOverride", "",
            "Name of the vanilla clip behind AttachBool, to be replaced by the pump clips. Empty = auto-detect at runtime (see `dtp clips`).");

        MaxPumpDistance = c.Bind("Pump", "MaxPumpDistance", 3f,
            new ConfigDescription(
                "If the mounted player ends up further than this (m) from the stand point (pushed, teleported, ...) they are released at once.",
                new AcceptableValueRange<float>(1f, 20f)));
        HandIK = c.Bind("Pump", "HandIK", true,
            "Pin the player's hands to the lever handle with IK while pumping (stops the hands clipping through the lever).");
        HandIKBlendSeconds = c.Bind("Pump", "HandIKBlendSeconds", 0.35f,
            new ConfigDescription("Seconds to blend the hands onto / off the handle when mounting / stepping away.",
                new AcceptableValueRange<float>(0.05f, 2f)));
        GripSpread = c.Bind("Pump", "GripSpread", 0.17f,
            new ConfigDescription("Half the distance (m) between the two hands on the handle.",
                new AcceptableValueRange<float>(0f, 0.5f)));
        GripUp = c.Bind("Pump", "GripUp", 0f,
            new ConfigDescription(
                "Move the grip point up (+) / down (-) from the lever's interact_point (m). Tweak if the hands sit above/below the handle. The arms can only reach so far: to get the whole player higher use StandHeight.",
                new AcceptableValueRange<float>(-1f, 1f)));
        GripForward = c.Bind("Pump", "GripForward", -0.025f,
            new ConfigDescription(
                "Move the grip point along the lever (+ = away from you, towards the pump column, m).",
                new AcceptableValueRange<float>(-1f, 1f)));
        HandRotate = c.Bind("Pump", "HandRotate", true,
            "Turn the hands palm-down onto the handle bar (overhand grip) instead of leaving them in the animation's orientation. Turn off to compare / if the hands twist wrongly on a modded player model.");
        FingerCurl = c.Bind("Pump", "FingerCurl", 0.79f,
            new ConfigDescription(
                "How far the fingers curl around the handle bar (0 = open hand, 1 = tight wrap). Needs a player rig with finger bones.",
                new AcceptableValueRange<float>(0f, 1.3f)));
        GripPalmOffset = c.Bind("Pump", "GripPalmOffset", -0.002f,
            new ConfigDescription(
                "Gap (m) between the palm and the top of the handle bar. Lower = hands sink into the bar, higher = hands float above it.",
                new AcceptableValueRange<float>(-0.05f, 0.1f)));
        ThumbCurl = c.Bind("Pump", "ThumbCurl", 0.8f,
            new ConfigDescription("How far the thumb curls down and under the handle bar (0 = thumb keeps the animation pose, 1 = tight wrap). Needs finger bones.",
                new AcceptableValueRange<float>(0f, 1.3f)));
        ThumbSpread = c.Bind("Pump", "ThumbSpread", 25f,
            new ConfigDescription("Degrees the thumb is swung away from the fist (sideways, before it curls under the bar). Raise it if the thumb still sits against the index finger, lower it if it sticks out.",
                new AcceptableValueRange<float>(0f, 60f)));
        ClipBlendSeconds = c.Bind("Pump", "ClipBlendSeconds", 0.12f,
            new ConfigDescription("Seconds to cross-fade between the pump clips (Enter / Idle / Push / Pull / Cycle / Exit). 0 = hard cuts like before. Removes the pose popping at every phase change.",
                new AcceptableValueRange<float>(0f, 0.5f)));
        BendToReach = c.Bind("Pump", "BendToReach", true,
            "When the hands cannot reach the lever handle (typically at the bottom of the stroke), bend the player to get there: first forward from the back (spine), then down at the knees. The bend follows the lever, so it is deepest when the lever is down. Turn off for the plain clips.");
        MaxBackBend = c.Bind("Pump", "MaxBackBend", 45f,
            new ConfigDescription("Most (degrees, spread over spine / chest / upper chest) the player may bend forward from the back to reach the handle.",
                new AcceptableValueRange<float>(0f, 70f)));
        MaxKneeBend = c.Bind("Pump", "MaxKneeBend", 0.08f,
            new ConfigDescription("Most (m) the hips may sink with bent knees (feet stay on the ground) when the back bend alone is not enough.",
                new AcceptableValueRange<float>(0f, 0.6f)));
        HipLock = c.Bind("Pump", "HipLock", 0.85f,
            new ConfigDescription("How much the hips are held still while pumping (0 = they follow the clips like before, 1 = fixed at their calm position; the feet stay planted by leg IK). The stroke then comes from the upper body: the back bends towards the lever and the arms push. Needs BendToReach.",
                new AcceptableValueRange<float>(0f, 1f)));
        BendSmooth = c.Bind("Pump", "BendSmooth", 8f,
            new ConfigDescription("How fast (1/s) the back bend follows the lever. Lower = softer, more rounded movement; higher = snappier.",
                new AcceptableValueRange<float>(1f, 30f)));
        FreezeIdle = c.Bind("Pump", "FreezeIdle", true,
            "Hold the Idle pose still instead of looping the Idle clip. The Idle clip makes the left leg twitch every loop; with this on the legs stay calm (the arms follow the lever IK anyway).");

        StandHeight = c.Bind("Pump", "StandHeight", 0f,
            new ConfigDescription(
                "Lifts (+) or lowers (-) the player at the pump (m). Raise it if the hands cannot reach the handle (arms stretched, hands below the lever). Applied when you step onto the pump.",
                new AcceptableValueRange<float>(-1f, 2f)));
        StandForward = c.Bind("Pump", "StandForward", -0.28f,
            new ConfigDescription("Moves the player towards (+) or away from (-) the lever (m).",
                new AcceptableValueRange<float>(-1f, 1f)));
        StandSide = c.Bind("Pump", "StandSide", 0f,
            new ConfigDescription("Moves the player sideways (m). + = to the player's right when facing the lever.",
                new AcceptableValueRange<float>(-1f, 1f)));
        AutoReach = c.Bind("Pump", "AutoReach", true,
            "While pumping, move the player just far enough towards the lever (and, only if that is not enough, up) for both hands to reach the handle at the top of the stroke. Turn off to use StandHeight / StandForward only.");
        MaxAutoStep = c.Bind("Pump", "MaxAutoStep", 0.25f,
            new ConfigDescription("Most (m) AutoReach may step the player towards the lever.", new AcceptableValueRange<float>(0f, 1f)));
        MaxAutoRaise = c.Bind("Pump", "MaxAutoRaise", 0.15f,
            new ConfigDescription("Most (m) AutoReach may then lift the player (a standing player should barely float).", new AcceptableValueRange<float>(0f, 1f)));

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