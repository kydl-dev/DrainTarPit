using System.Globalization;
using BepInEx.Configuration;
using DrainTarPit.Logging;
using DrainTarPit.Visuals;
using static DrainTarPit.ModSettings;

namespace DrainTarPit;

/// <summary>
/// The hose look config (user, 2026-10-20: the old [Visuals] HoseStyle / HoseOpacity split in two categories). The entries are fields of ModSettings.
///   [World Hose Visuals]      how the hoses ALREADY IN THE WORLD are shown. Applies at once to every loaded hose (config manager or a saved .cfg) and on
///                             every load. It never touches what a hose has stored, and it does not decide the look of a hose being built.
///   [Build Time Hose Visuals] the look a hose gets at the moment it is built. Stored on that hose (its ZDO) and never changed afterwards by anything;
///                             changing these settings only affects hoses built from then on, never already built ones.
/// </summary>
internal static class ModSettingsHose
{
    internal const string WorldSection = "World Hose Visuals", BuildSection = "Build Time Hose Visuals";

    public static void Bind(ConfigFile c)
    {
        WorldHoseStyle = c.Bind(WorldSection, "World Hose Style", WorldHoseStyleOption.Default,
            "How all hoses (straight + elbow) already built in the world are shown. Default = each hose shows the style and opacity it was built with " +
            "(its own Build Hose Style / Build Hose Opacity). Transparent = every hose see-through with World Hose Opacity. Solid = every hose opaque. " +
            "Applies right away to all loaded hoses and on every world load; it never changes what a hose has stored, so going back to Default restores each hose's own look.");
        WorldHoseOpacity = c.Bind(WorldSection, "World Hose Opacity", 0.45f,
            new ConfigDescription("Opacity of all hoses in the world while World Hose Style = Transparent (0 = invisible, 1 = solid). Applies right away.",
                new AcceptableValueRange<float>(0f, 1f)));
        BuildHoseStyle = c.Bind(BuildSection, "Build Hose Style", HoseStyleOption.Solid,
            "Style of a hose at the moment you build it: Transparent (the tar / water can be seen moving through it) or Solid. Stored on that hose for good. " +
            "A change only affects hoses built from then on; hoses already built keep the style they were built with.");
        BuildHoseOpacity = c.Bind(BuildSection, "Build Hose Opacity", 0.45f,
            new ConfigDescription("Opacity of a hose built with Build Hose Style = Transparent (0 = invisible, 1 = solid). Stored on that hose for good; " +
                "a change only affects hoses built from then on.",
                new AcceptableValueRange<float>(0f, 1f)));
    }

    /// The live reactions. World: every loaded hose is shown again (the next frame, main thread). Build: the hose prefabs (= the build ghost and the
    /// next hose built) get the new look; already built hoses are not touched.
    public static void Hook()
    {
        WorldHoseStyle.SettingChanged += (s, e) => { ModLog.Verbose("Config: World Hose Style = " + WorldHoseStyle.Value + ", all loaded hoses are shown again"); MaterialFixer.RequestWorldRefresh(); };
        WorldHoseOpacity.SettingChanged += (s, e) => { ModLog.Verbose("Config: World Hose Opacity = " + WorldHoseOpacity.Value.ToString("0.00") + ", all loaded hoses are shown again"); MaterialFixer.RequestWorldRefresh(); };
        BuildHoseStyle.SettingChanged += (s, e) => { ModLog.Verbose("Config: Build Hose Style = " + BuildHoseStyle.Value + ", for hoses built from now on"); MaterialFixer.RequestRefresh(); };
        BuildHoseOpacity.SettingChanged += (s, e) => { ModLog.Verbose("Config: Build Hose Opacity = " + BuildHoseOpacity.Value.ToString("0.00") + ", for hoses built from now on"); MaterialFixer.RequestRefresh(); };
    }

    /// Config version 4 (called by ModSettings.Migrate for files below v4, which then sets the version): the values of the old [Visuals] HoseStyle /
    /// HoseOpacity (orphaned entries of the file) become Build Hose Style / Opacity once. Returns how many values were carried over.
    public static int CarryOld(System.Collections.IDictionary orphans)
    {
        int moved = 0;
        if (orphans != null)
            foreach (System.Collections.DictionaryEntry e in orphans)
            {
                var def = e.Key as ConfigDefinition; var raw = e.Value as string;
                if (def == null || def.Section != "Visuals" || raw == null) continue;
                if (def.Key == "HoseStyle")
                {
                    try { BuildHoseStyle.Value = (HoseStyleOption)System.Enum.Parse(typeof(HoseStyleOption), raw.Trim(), true); moved++; }
                    catch (System.ArgumentException) { ModLog.Warn("Config: old HoseStyle '" + raw + "' not understood, Build Hose Style stays " + BuildHoseStyle.Value); }
                }
                else if (def.Key == "HoseOpacity")
                {
                    float o;
                    if (float.TryParse(raw.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out o)) { BuildHoseOpacity.Value = UnityEngine.Mathf.Clamp01(o); moved++; }
                    else ModLog.Warn("Config: old HoseOpacity '" + raw + "' not understood, Build Hose Opacity stays " + BuildHoseOpacity.Value.ToString("0.00"));
                }
            }
        ModLog.Info("Config: [Visuals] HoseStyle / HoseOpacity moved to [" + BuildSection + "] (" + moved + " value(s) carried over: Build Hose Style " +
            BuildHoseStyle.Value + ", Build Hose Opacity " + BuildHoseOpacity.Value.ToString("0.00") + ")");
        return moved;
    }
}
