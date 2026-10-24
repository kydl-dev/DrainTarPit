using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using Kydl.Common;

namespace DrainTarPit;

/// <summary>
/// A number typed with a decimal COMMA (`DrainRadius = 3,5`, the habit in most of Europe) is parsed by BepInEx with the invariant culture, where
/// the comma is a thousands separator: 3,5 became 35, the range then clamped it to 8 (integration test Bind_NumberWithCommaDecimal_...).
/// The file is read BEFORE the first Bind (every Bind saves the file and would already write the misread value back), and after all entries are bound: a float / double entry whose saved text is `digits,digits` gets the value it
/// was meant to have (the comma read as the decimal point, clamped by the entry's range as usual) and a warning names the line. Setting the value
/// writes it back with a dot, so the warning appears once. Text entries and whole numbers are never touched.
/// </summary>
internal static class ModSettingsCommaFix
{
    static readonly Regex Section = new Regex(@"^\s*\[(?<s>[^\]]+)\]\s*$");
    static readonly Regex CommaNumber = new Regex(@"^\s*(?<k>[^=#]+?)\s*=\s*(?<v>-?\d+,\d+)\s*$");

    /// The config file's lines as they were before binding (null when there is no file yet).
    internal static string[] Capture(ConfigFile c)
    {
        try { return c != null && !string.IsNullOrEmpty(c.ConfigFilePath) && File.Exists(c.ConfigFilePath) ? File.ReadAllLines(c.ConfigFilePath) : null; }
        catch (System.Exception e) { ModLog.Warn("Config comma check skipped: " + e.Message); return null; }
    }

    internal static void Apply(ConfigFile c, string[] lines)
    {
        try
        {
            if (c == null || lines == null) return;
            string section = "";
            foreach (var line in lines)
            {
                var sm = Section.Match(line);
                if (sm.Success) { section = sm.Groups["s"].Value.Trim(); continue; }
                var m = CommaNumber.Match(line);
                if (!m.Success) continue;
                var def = new ConfigDefinition(section, m.Groups["k"].Value);
                if (!c.ContainsKey(def)) continue;
                var entry = c[def];
                if (entry.SettingType != typeof(float) && entry.SettingType != typeof(double)) continue;
                double meant;
                if (!double.TryParse(m.Groups["v"].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out meant)) continue;
                var was = entry.BoxedValue;
                entry.BoxedValue = entry.SettingType == typeof(float) ? (object)(float)meant : meant;
                ModLog.Warn("Config [" + section + "] " + def.Key + " = " + m.Groups["v"].Value + ": a comma is read as the decimal point -> " +
                            System.Convert.ToString(entry.BoxedValue, CultureInfo.InvariantCulture) + " (BepInEx had read " +
                            System.Convert.ToString(was, CultureInfo.InvariantCulture) + "). Please use a dot in the config file.");
            }
        }
        catch (System.Exception e) { ModLog.Warn("Config comma check skipped: " + e.Message); }
    }
}
