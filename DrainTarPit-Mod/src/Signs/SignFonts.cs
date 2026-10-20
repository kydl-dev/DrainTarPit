using System.Collections.Generic;
using DrainTarPit.Logging;
using TMPro;
using UnityEngine;

namespace DrainTarPit.Signs
{
    /// <summary>
    /// T-023 (user: "font (drop-down based on all fonts loaded into the game)"): the TextMeshPro fonts loaded in the game, by name, and putting a
    /// style's font / size on a sign's text. The list is made once per session (fonts the game loads later are picked up by Refresh).
    /// </summary>
    internal static class SignFonts
    {
        static readonly Dictionary<string, TMP_FontAsset> ByName = new();
        static readonly List<string> Names = new();
        static int _reported;

        /// Every font name (sorted); "" (the sign's default font) is not in the list.
        internal static IList<string> All()
        {
            if (Names.Count == 0) Refresh();
            return Names;
        }

        internal static int Refresh()
        {
            ByName.Clear(); Names.Clear();
            foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (f == null || string.IsNullOrEmpty(f.name) || ByName.ContainsKey(f.name)) continue;
                ByName[f.name] = f; Names.Add(f.name);
            }
            Names.Sort(System.StringComparer.OrdinalIgnoreCase);
            ModLog.Verbose("SignFonts: " + Names.Count + " TextMeshPro font(s) loaded: " + string.Join(", ", Names));
            if (_reported++ == 0) ReportEmoji();
            return Names.Count;
        }

        /// Emoji on signs (user: "Valheim allows some emoji usage into signs"): what the game's TextMeshPro has for them, for the debug log.
        static void ReportEmoji()
        {
            var sa = TMP_Settings.defaultSpriteAsset;
            ModLog.Verbose("SignFonts: default sprite asset " + (sa != null ? "'" + sa.name + "' with " + (sa.spriteCharacterTable != null ? sa.spriteCharacterTable.Count : 0) + " sprite(s)" : "none") +
                ", emoji fallback sprite assets " + (TMP_Settings.emojiFallbackTextAssets != null ? TMP_Settings.emojiFallbackTextAssets.Count : 0));
        }

        /// Puts the style's font and size on the text. `defFont` / `defMin` / `defMax` are the sign's own (kept from the prefab).
        internal static string Apply(TMP_Text text, SignStyle style, TMP_FontAsset defFont, float defMin, float defMax)
        {
            if (text == null) return "no text object";
            TMP_FontAsset f = null;
            if (!string.IsNullOrEmpty(style.Font))
            {
                if (!ByName.TryGetValue(style.Font, out f) || f == null) { Refresh(); ByName.TryGetValue(style.Font, out f); }
            }
            text.font = f != null ? f : defFont;
            if (style.Size <= 0f) { text.enableAutoSizing = true; text.fontSizeMin = defMin; text.fontSizeMax = defMax; }
            else { text.enableAutoSizing = false; text.fontSize = style.Size; }
            return "font " + (f != null ? f.name : "default" + (string.IsNullOrEmpty(style.Font) ? "" : " ('" + style.Font + "' not loaded)")) +
                   ", size " + (style.Size <= 0f ? "auto" : style.Size.ToString("0.0"));
        }
    }
}
