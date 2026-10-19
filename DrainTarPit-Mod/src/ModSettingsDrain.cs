using BepInEx.Configuration;
using DrainTarPit.Drain;
using DrainTarPit.Logging;
using DrainTarPit.Visuals;
using UnityEngine;
using static DrainTarPit.ModSettings;

namespace DrainTarPit;

/// The [Drain] config entries and the water pool entries listed with them (flow, films, pool mesh, editor key). The entries themselves are fields of ModSettings; called from ModSettings.Bind.
internal static class ModSettingsDrain
{
    public static void Bind(ConfigFile c)
    {
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
    }
}
