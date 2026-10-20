using System.Collections.Generic;
using System.IO;
using BepInEx;
using DrainTarPit.Logging;

namespace DrainTarPit.Signs
{
    /// <summary>
    /// T-023 (user: "save preferred style (saves current values, besides name, as preferred style for the sign) and use preferred style"):
    /// one preferred sign style per player PC, in BepInEx/config/DrainTarPit.SignStyle.txt (the text form of SignStyle).
    /// </summary>
    internal static class SignPrefs
    {
        internal static string Path { get { return System.IO.Path.Combine(Paths.ConfigPath, "DrainTarPit.SignStyle.txt"); } }

        /// Saves the style (the name is never part of a style). Returns the number of characters written (0 = failed).
        internal static int Save(SignStyle style)
        {
            string text = style.Serialize();
            try
            {
                File.WriteAllText(Path, text);
                ModLog.Info("SignPrefs: preferred sign style saved to " + Path + " (" + text + ")");
                return text.Length;
            }
            catch (IOException e) { ModLog.Warn("SignPrefs: could not save the preferred style: " + e.Message); }
            catch (System.UnauthorizedAccessException e) { ModLog.Warn("SignPrefs: no permission to save the preferred style: " + e.Message); }
            return 0;
        }

        /// The preferred style, or null when none was saved yet (or it could not be read).
        internal static SignStyle Load()
        {
            if (!File.Exists(Path)) { ModLog.Verbose("SignPrefs: no preferred style saved yet (" + Path + ")"); return null; }
            try
            {
                var problems = new List<string>();
                var s = SignStyle.Parse(File.ReadAllText(Path).Trim(), problems);
                ModLog.Verbose("SignPrefs: preferred style loaded (" + s + ")" + (problems.Count > 0 ? ", problems: " + string.Join("; ", problems) : ""));
                return s;
            }
            catch (IOException e) { ModLog.Warn("SignPrefs: could not read the preferred style: " + e.Message); }
            catch (System.UnauthorizedAccessException e) { ModLog.Warn("SignPrefs: no permission to read the preferred style: " + e.Message); }
            return null;
        }
    }
}
