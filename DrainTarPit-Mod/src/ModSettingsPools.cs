using BepInEx.Configuration;
using DrainTarPit.Drain;
using DrainTarPit.Logging;
using DrainTarPit.Visuals;
using UnityEngine;

namespace DrainTarPit;

/// <summary>
/// The pools the pump makes (water and tar), water, pool signs and the pool menu. T-068: these move to the WaterAndTarPools mod (stage 2 of the
/// config review); until then they are bound here under the categories they will have there.
/// </summary>
internal static partial class ModSettings
{
    public static ConfigEntry<bool> DepositTar;
    public static ConfigEntry<bool> DepositWater;
    public static ConfigEntry<float> MinWaterDepth;
    public static ConfigEntry<bool> PoolWalls;
    public static ConfigEntry<bool> PoolOverflow;
    public static ConfigEntry<bool> PoolRimDisappear;
    public static ConfigEntry<bool> PoolRunoff;
    public static ConfigEntry<bool> PoolShedFilm;
    public static ConfigEntry<bool> PoolFilmEvaporates;
    public static ConfigEntry<bool> PoolFishStayIn;
    public static ConfigEntry<bool> PoolJumpBoost;
    public static ConfigEntry<int> PoolJumpPresses;
    public static ConfigEntry<float> PoolJumpHeight;
    public static ConfigEntry<float> LiquidCellSize;
    public static ConfigEntry<float> WaterFlow;
    public static ConfigEntry<float> WaterPoolSize;
    public static ConfigEntry<float> WaterDepthLook;
    public static ConfigEntry<KeyCode> PoolMenuKey;
    public static ConfigEntry<bool> WaterSplashSound;
    public static ConfigEntry<bool> WaterSlugFlow;

    // fixed values (Tunables)
    public static readonly Tunable<bool> AllowWater = new("Pools", "AllowWater", true,
        "Strainers built in water pump water (sea / lake / a pump pool).");
    public static readonly Tunable<WaterStyleOption> WaterStyle = new("Visuals", "WaterStyle", WaterStyleOption.Game,
        "Water style of pools when no admin world style and no personal style is set.");
    public static readonly Tunable<bool> PoolMeshFix = new("Visuals", "PoolMeshFix", true,
        "Dry grid points near the water are put at the water level (no water sheet hanging on the walls).");
    public static readonly Tunable<int> PoolMeshReach = new("Visuals", "PoolMeshReach", 6,
        "PoolMeshFix: how many grid cells away from a wet point a dry point is still put at the water level.");
    public static readonly Tunable<float> SignBoardBrightness = new("Visuals", "SignBoardBrightness", 1.5f,
        "Pool sign: brightness of the wooden board.");
    public static readonly Tunable<float> SignTextBrightness = new("Visuals", "SignTextBrightness", 1.2f,
        "Pool sign: brightness of the name's colours.");
    public static readonly Tunable<float> PoolFilmDepth = new("Pools", "PoolFilmDepth", 0.05f,
        "Depth (m) below which liquid counts as a thin film for PoolShedFilm.");
    public static readonly Tunable<float> WaterSplashOffset = new("Effects", "WaterSplashOffset", -0.3f,
        "Seconds added to the moment the water splash plays (negative = earlier).");

    static void BindPools(ConfigFile c)
    {
        DepositTar = c.Bind("Pools", "DepositTar", true,
            "Pumped tar is poured into a real tar pool where the stream from the nozzle lands: it fills the lowest basin around that point (a hole dug with the pickaxe, a dip enclosed by walls) up to its rim. The pool slows players, is saved in the world and can be pumped again. Off = the tar only pours out visually.");
        DepositWater = c.Bind("Pools", "DepositWater", true,
            "Pumped water is poured into a real water pool where the stream from the nozzle lands (a hole, a moat, a dip enclosed by walls), saved in the world and pumpable again. Water that lands in the sea or a lake just joins it. Off = the water only pours out visually.");
        MinWaterDepth = c.Bind("Pools", "MinWaterDepth", 0.3f,
            new ConfigDescription("Minimum water depth (m) at the strainer tip for it to count as 'in water'.",
                new AcceptableValueRange<float>(0.05f, 3f)));
        PoolWalls = c.Bind("Pools", "PoolWalls", true,
            "Built pieces (stone / wood walls, floors, ...) hold the liquid of the pools the pump makes, like terrain does: a ring of walls fills up to the lowest point of its rim. Pieces of this mod, players and items are ignored; a roof floating over the pool does not count.");
        PoolRimDisappear = c.Bind("Pools", "PoolRimDisappear", true,
            "The rim is the lowest point of the walls / ground around a pool. On: the liquid rises to the rim and never higher; whatever would run over it disappears right there. Off: the liquid runs over the rim (see PoolOverflow).");
        PoolOverflow = c.Bind("Pools", "PoolOverflow", true,
            "Only with PoolRimDisappear off: liquid running over the rim fills whatever basin lies behind it (up to 3 more); what finds no basin runs off. Off = it just runs off.");
        PoolRunoff = c.Bind("Pools", "PoolRunoff", true,
            "When the liquid reaches the edge of a pool's grid and the ground goes on downhill (a long moat, a ditch) it continues into the next pool beyond the edge (at most 6 in a row), so a moat longer than one pool fills end to end. On flat ground it runs off.");
        PoolShedFilm = c.Bind("Pools", "PoolShedFilm", true,
            "Liquid thinner than 5 cm (a film) on the outside of walls or on a slope slides off quickly instead of lingering. Off = the game's own slow spreading.");
        PoolFilmEvaporates = c.Bind("Pools", "PoolFilmEvaporates", true,
            "With PoolShedFilm: a film lying flat with nothing deeper next to it evaporates in a second or two. Off = such films stay.");
        PoolFishStayIn = c.Bind("Pools", "PoolFishStayIn", true,
            "Fish in a water pool the pump made stay in its water: they turn back at the walls and cannot jump out. Fish in the sea / lakes are never touched.");
        PoolJumpBoost = c.Bind("Pools", "PoolJumpBoost", true,
            "Getting out of a pool the pump made (water or tar): close to its edge (within 2 m), pressing Jump PoolJumpPresses times within 3 seconds lifts you PoolJumpHeight m up and towards the edge.");
        PoolJumpPresses = c.Bind("Pools", "PoolJumpPresses", 3,
            new ConfigDescription("Jump presses within 3 seconds at the edge of a pool that give the lift (PoolJumpBoost).", new AcceptableValueRange<int>(1, 6)));
        PoolJumpHeight = c.Bind("Pools", "PoolJumpHeight", 2f,
            new ConfigDescription("How high (m) the lift out of a pool takes your feet (PoolJumpBoost).", new AcceptableValueRange<float>(0.5f, 5f)));
        WaterPoolSize = c.Bind("Pools", "WaterPoolSize", 24f,
            new ConfigDescription("Side length (m) of one pool grid. Bigger pools hold longer moats in one piece but cost more simulation time. Read when the world loads; pools that already exist keep their size.",
                new AcceptableValueRange<float>(8f, 48f)));
        LiquidCellSize = c.Bind("Pools", "LiquidCellSize", 0.25f,
            new ConfigDescription("Size (m) of one grid cell of a pool (tar and water). The liquid surface ends at a grid point, so smaller = it reaches the walls more closely, at more simulation time. Read when the world loads; pools that already exist keep their size.",
                new AcceptableValueRange<float>(0.15f, 0.5f)));
        WaterFlow = c.Bind("Pools", "WaterFlow", 4f,
            new ConfigDescription("How freely water runs compared with tar: 1 = as thick as tar, 4 = about twice as fast, 16 = about four times.",
                new AcceptableValueRange<float>(1f, 64f)));
        WaterDepthLook = c.Bind("Visuals", "WaterDepthLook", 0.15f,
            new ConfigDescription("Game water style only: how deep the water looks (0 = shallow, calm and clear like the shore, 1 = open sea). Applies live.",
                new AcceptableValueRange<float>(0f, 1f)));
        PoolMenuKey = c.Bind("Visuals", "PoolMenuKey", KeyCode.F9,
            "Opens / closes the pool menu: the world water style (server admins), and every pool with a named pool sign (position, closest portal) with your own water style for it (only on your screen). None = no menu.");
        WaterSplashSound = c.Bind("Effects", "WaterSplashSound", true,
            "Play a water splash where the pumped water comes down. Off = water pours silently.");
        WaterSlugFlow = c.Bind("Effects", "WaterSlugFlow", true,
            "Show a slug of water travelling through see-through hoses on every pump stroke.");
    }

    static void HookPools()
    {
        WaterDepthLook.SettingChanged += (s, e) => PoolWater.RequestRefresh();
        WaterStyle.SettingChanged += (s, e) => { ModLog.Verbose("Config: WaterStyle = " + WaterStyle.Value + ", water pools redraw"); PoolWater.RequestRefresh(); };
        PoolMeshFix.SettingChanged += (s, e) => PoolMesh.Refresh();
        PoolMeshReach.SettingChanged += (s, e) => PoolMesh.Refresh();
        SignBoardBrightness.SettingChanged += (s, e) => ModLog.Verbose("Config: SignBoardBrightness = " + SignBoardBrightness.Value + ", " + Signs.PoolSign.BrightnessChanged() + " sign(s) redrawn");
        SignTextBrightness.SettingChanged += (s, e) => ModLog.Verbose("Config: SignTextBrightness = " + SignTextBrightness.Value + ", " + Signs.PoolSign.BrightnessChanged() + " sign(s) redrawn");
        WaterFlow.SettingChanged += (s, e) => TarPool.ApplyWaterFlow();       // live: existing water pools + the prefabs for new ones
    }
}
