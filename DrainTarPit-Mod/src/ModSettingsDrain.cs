using BepInEx.Configuration;

namespace DrainTarPit;

/// [Drain]: pumping tar out of pits (the strainer, the hose chain, how much each stroke moves, refilling).
internal static partial class ModSettings
{
    static void BindDrain(ConfigFile c)
    {
        DrainEnable = c.Bind("Drain", "Enable", true,
            "Pumping liquid out of pits and pools. Off = a stroke moves nothing (the pump and the hoses can still be built and used).");
        VolumePerStroke = c.Bind("Drain", "VolumePerStroke", 6f,
            new ConfigDescription(
                "Cubic meters of liquid moved per pump stroke. Use the console command `dtp pits` to see how big the pits are.",
                new AcceptableValueRange<float>(5f, 30f)));
        DrainRadius = c.Bind("Drain", "DrainRadius", 4f,
            new ConfigDescription(
                "Meters around the strainer that tar is pulled from. The pull is strongest at the strainer and fades towards the edge, so the pit gets a funnel instead of a flat hole; tar outside the radius flows in.",
                new AcceptableValueRange<float>(0.25f, 8f)));
        StrainerSearchRadius = c.Bind("Drain", "StrainerSearchRadius", 1f,
            new ConfigDescription(
                "The strainer counts as 'in tar' if there is tar this close (m, sideways) to its tip, and pulls from the deepest tar around it. 0 = only straight under the tip.",
                new AcceptableValueRange<float>(0f, 3f)));
        StrainerReach = c.Bind("Drain", "StrainerReach", 0.5f,
            new ConfigDescription("How far (m) the strainer tip may hang ABOVE the liquid surface and still pump.",
                new AcceptableValueRange<float>(0f, 2f)));
        MinTarDepth = c.Bind("Drain", "MinTarDepth", 0.1f,
            new ConfigDescription("Minimum tar depth (m) under the strainer tip for it to count as 'in tar'.",
                new AcceptableValueRange<float>(0.01f, 2f)));
        MaxChainPieces = c.Bind("Drain", "MaxChainPieces", 40,
            new ConfigDescription("Longest hose chain (pieces) that is followed from the pump to a strainer / nozzle.",
                new AcceptableValueRange<int>(2, 100)));
        SnapTolerance = c.Bind("Drain", "SnapTolerance", 0.3f,
            new ConfigDescription("Max distance (m) between two snap points for two pieces to count as connected.",
                new AcceptableValueRange<float>(0.05f, 1f)));
        RefillMinutes = c.Bind("Drain", "RefillMinutes", 60f,
            new ConfigDescription(
                "Minutes (world time) for a drained tar pit to seep back to its original volume. Restarts with every pump stroke.",
                new AcceptableValueRange<float>(10f, 120f)));
    }
}
