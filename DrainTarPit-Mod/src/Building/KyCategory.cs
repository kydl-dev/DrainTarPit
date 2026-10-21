using DrainTarPit.Logging;

namespace DrainTarPit.Building
{
    /// <summary>
    /// T-66 (user: "a Filtered item here called KY-Items in which all current items from this mod and any future items we might build for my
    /// mods go"): the build menu category every piece of the user's mods uses. Jotunn makes one custom category per NAME, so every mod that
    /// uses exactly this string ends up in the same "KY-Items" entry of the hammer's build menu. Keep the string identical in every mod.
    /// </summary>
    internal static class KyCategory
    {
        internal const string Name = "KY-Items";

        /// The category for a new piece (logged once per piece, so a piece that ends up elsewhere can be traced).
        internal static string For(string piece)
        {
            ModLog.Verbose("KyCategory: piece '" + piece + "' goes into the build menu category '" + Name + "'");
            return Name;
        }
    }
}
