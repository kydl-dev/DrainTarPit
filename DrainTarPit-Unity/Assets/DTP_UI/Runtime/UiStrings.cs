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
            { "dtp_ui_pools_title", "Pools" }, { "dtp_ui_pools_close", "{0} or Esc to close" },
            { "dtp_ui_world_style", "World water style" }, { "dtp_ui_world_style_all", "All pools" },
            { "dtp_ui_world_style_note", "Every pool in this world shows this style for every player. A player's own choice for a pool (below) still wins on their screen. Not set: each player's config decides." },
            { "dtp_ui_world_style_admin_only", "Only a server admin can change this." },
            { "dtp_ui_style_notset", "Not set" }, { "dtp_ui_style_game", "Game water" }, { "dtp_ui_style_tropical", "Tropical" }, { "dtp_ui_style_lake", "Lake" }, { "dtp_ui_style_ashlands", "Ashlands" },
            { "dtp_ui_style_default", "Default ({0})" },
            { "dtp_ui_pools", "Named pools" }, { "dtp_ui_pools_count", "{0} pool(s)" }, { "dtp_ui_refresh", "Refresh" },
            { "dtp_ui_pools_note", "Pools with a named pool sign, anywhere in the world. Your style choice is only on your screen." },
            { "dtp_ui_pools_loading", "Asking the server..." },
            { "dtp_ui_pools_none", "No pool has a named sign yet. Build a pool sign at a pool and name it (E)." },
            { "dtp_ui_pool_pos", "Position {0}, {1} (height {2})" }, { "dtp_ui_pool_portal", "Closest portal: {0} ({1} m)" },
            { "dtp_ui_pool_noportal", "No portal in this world" }, { "dtp_ui_pool_portal_unnamed", "(no name)" }, { "dtp_ui_my_style", "My style" }, { "dtp_ui_tar_pool", "Tar pool (no water styles)" },            { "dtp_ui_save", "Save" }, { "dtp_ui_cancel", "Cancel" }, { "dtp_ui_esc_close", "Esc to close" },
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
            { "dtp_ui_pools_title", "Becken" }, { "dtp_ui_pools_close", "{0} oder Esc zum Schließen" },
            { "dtp_ui_world_style", "Wasserstil der Welt" }, { "dtp_ui_world_style_all", "Alle Becken" },
            { "dtp_ui_world_style_note", "Jedes Becken dieser Welt zeigt diesen Stil für jeden Spieler. Die eigene Wahl eines Spielers für ein Becken (unten) gilt trotzdem auf seinem Bildschirm. Nicht gesetzt: die Konfiguration jedes Spielers entscheidet." },
            { "dtp_ui_world_style_admin_only", "Nur ein Server-Admin kann das ändern." },
            { "dtp_ui_style_notset", "Nicht gesetzt" }, { "dtp_ui_style_game", "Spielwasser" }, { "dtp_ui_style_tropical", "Tropisch" }, { "dtp_ui_style_lake", "See" }, { "dtp_ui_style_ashlands", "Aschelande" },
            { "dtp_ui_style_default", "Standard ({0})" },
            { "dtp_ui_pools", "Benannte Becken" }, { "dtp_ui_pools_count", "{0} Becken" }, { "dtp_ui_refresh", "Aktualisieren" },
            { "dtp_ui_pools_note", "Becken mit einem benannten Beckenschild, irgendwo in der Welt. Deine Stilwahl gilt nur auf deinem Bildschirm." },
            { "dtp_ui_pools_loading", "Frage den Server..." },
            { "dtp_ui_pools_none", "Noch kein Becken hat ein benanntes Schild. Baue ein Beckenschild an ein Becken und benenne es (E)." },
            { "dtp_ui_pool_pos", "Position {0}, {1} (Höhe {2})" }, { "dtp_ui_pool_portal", "Nächstes Portal: {0} ({1} m)" },
            { "dtp_ui_pool_noportal", "Kein Portal in dieser Welt" }, { "dtp_ui_pool_portal_unnamed", "(ohne Namen)" }, { "dtp_ui_my_style", "Mein Stil" }, { "dtp_ui_tar_pool", "Teerbecken (keine Wasserstile)" },            { "dtp_ui_save", "Speichern" }, { "dtp_ui_cancel", "Abbrechen" }, { "dtp_ui_esc_close", "Esc zum Schließen" },
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
