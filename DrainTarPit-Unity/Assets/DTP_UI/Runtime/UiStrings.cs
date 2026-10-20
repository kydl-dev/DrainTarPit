using System.Collections.Generic;

namespace DrainTarPit.Ui
{
    /// <summary>
    /// T-56R: the player-facing text of the mod's menus (English + German), shared by the game (merged into TextLocal.Loc, registered with
    /// Jotunn) and the Unity preview (shown as English). Keys start with dtp_ like every other text of the mod.
    /// </summary>
    public static class UiStrings
    {
        public static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            { "dtp_ui_sign_title", "Pool sign" }, { "dtp_ui_pool", "Pool: {0}" },
            { "dtp_ui_name", "Name" }, { "dtp_ui_emoji", "Emoji" },
            { "dtp_ui_emoji_note", "Emoji always use the game's emoji font, whatever font the name has." },
            { "dtp_ui_text", "Text" }, { "dtp_ui_font", "Font" }, { "dtp_ui_font_default", "Default" },
            { "dtp_ui_size", "Size" }, { "dtp_ui_auto", "Auto" }, { "dtp_ui_style", "Style" },
            { "dtp_ui_bold", "Bold" }, { "dtp_ui_italic", "Italic" }, { "dtp_ui_underline", "Underline" }, { "dtp_ui_strike", "Strike" }, { "dtp_ui_upper", "Upper case" },
            { "dtp_ui_align", "Alignment" }, { "dtp_ui_left", "Left" }, { "dtp_ui_center", "Centre" }, { "dtp_ui_right", "Right" },
            { "dtp_ui_spacing", "Letter spacing" }, { "dtp_ui_colour", "Colour" },
            { "dtp_ui_letters", "Letter colours" },
            { "dtp_ui_letters_note", "Click letters to select them, pick a colour below, then paint them. Gradient runs from the text colour to the picked colour." },
            { "dtp_ui_all", "All" }, { "dtp_ui_none", "None" }, { "dtp_ui_paint", "Paint" }, { "dtp_ui_rainbow", "Rainbow" }, { "dtp_ui_gradient", "Gradient" }, { "dtp_ui_clear", "Clear" },
            { "dtp_ui_select_first", "Select some letters first." }, { "dtp_ui_name_full", "The name is full." },
            { "dtp_ui_pref", "Preferred style" }, { "dtp_ui_pref_save", "Save preferred style" }, { "dtp_ui_pref_use", "Use preferred style" },
            { "dtp_ui_reset", "Reset to default style" }, { "dtp_ui_reset_done", "Default style loaded - Save to keep it." },
            { "dtp_ui_pref_saved", "Preferred style saved." }, { "dtp_ui_pref_failed", "Saving the preferred style failed (see the log)." },
            { "dtp_ui_pref_used", "Preferred style loaded - Save to keep it." }, { "dtp_ui_pref_none", "No preferred style saved yet." },
            { "dtp_ui_save", "Save" }, { "dtp_ui_cancel", "Cancel" },
        };

        public static readonly Dictionary<string, string> De = new Dictionary<string, string>
        {
            { "dtp_ui_sign_title", "Beckenschild" }, { "dtp_ui_pool", "Becken: {0}" },
            { "dtp_ui_name", "Name" }, { "dtp_ui_emoji", "Emoji" },
            { "dtp_ui_emoji_note", "Emoji nutzen immer die Emoji-Schrift des Spiels, egal welche Schrift der Name hat." },
            { "dtp_ui_text", "Text" }, { "dtp_ui_font", "Schrift" }, { "dtp_ui_font_default", "Standard" },
            { "dtp_ui_size", "Größe" }, { "dtp_ui_auto", "Auto" }, { "dtp_ui_style", "Stil" },
            { "dtp_ui_bold", "Fett" }, { "dtp_ui_italic", "Kursiv" }, { "dtp_ui_underline", "Unterstrichen" }, { "dtp_ui_strike", "Durchgestrichen" }, { "dtp_ui_upper", "Großbuchstaben" },
            { "dtp_ui_align", "Ausrichtung" }, { "dtp_ui_left", "Links" }, { "dtp_ui_center", "Mitte" }, { "dtp_ui_right", "Rechts" },
            { "dtp_ui_spacing", "Zeichenabstand" }, { "dtp_ui_colour", "Farbe" },
            { "dtp_ui_letters", "Buchstabenfarben" },
            { "dtp_ui_letters_note", "Buchstaben anklicken, um sie auszuwählen, unten eine Farbe wählen, dann einfärben. Verlauf geht von der Textfarbe zur gewählten Farbe." },
            { "dtp_ui_all", "Alle" }, { "dtp_ui_none", "Keine" }, { "dtp_ui_paint", "Einfärben" }, { "dtp_ui_rainbow", "Regenbogen" }, { "dtp_ui_gradient", "Verlauf" }, { "dtp_ui_clear", "Entfernen" },
            { "dtp_ui_select_first", "Zuerst Buchstaben auswählen." }, { "dtp_ui_name_full", "Der Name ist voll." },
            { "dtp_ui_pref", "Bevorzugter Stil" }, { "dtp_ui_pref_save", "Bevorzugten Stil speichern" }, { "dtp_ui_pref_use", "Bevorzugten Stil verwenden" },
            { "dtp_ui_reset", "Auf Standardstil zurücksetzen" }, { "dtp_ui_reset_done", "Standardstil geladen - Speichern, um ihn zu behalten." },
            { "dtp_ui_pref_saved", "Bevorzugter Stil gespeichert." }, { "dtp_ui_pref_failed", "Speichern des bevorzugten Stils fehlgeschlagen (siehe Log)." },
            { "dtp_ui_pref_used", "Bevorzugter Stil geladen - Speichern, um ihn zu behalten." }, { "dtp_ui_pref_none", "Noch kein bevorzugter Stil gespeichert." },
            { "dtp_ui_save", "Speichern" }, { "dtp_ui_cancel", "Abbrechen" },
        };

        /// English text with {0} {1} ... filled in (the Unity preview; the game uses its own localization).
        public static string English(string key, params string[] args)
        {
            string s;
            if (!En.TryGetValue(key, out s)) s = key;
            if (args != null) for (int i = 0; i < args.Length; i++) s = s.Replace("{" + i + "}", args[i]);
            return s;
        }
    }
}
