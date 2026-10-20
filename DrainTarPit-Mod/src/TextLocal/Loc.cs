using System.Collections.Generic;
using Jotunn.Managers;

namespace DrainTarPit.TextLocal
{
    /// <summary>
    /// All player-facing text as localization tokens ("$dtp_..."). English + German are registered with Jotunn at startup
    /// (Loc.Register); other languages fall back to English. Loc.T resolves a token right away, with {0} {1} ... filled in.
    /// Console / debug output (`dtp ...`) stays plain English on purpose.
    /// </summary>
    internal static class Loc
    {
        static readonly Dictionary<string, string> En = new()
        {
            // pieces
            { "dtp_piece_pump", "Hand pump" },
            { "dtp_piece_pump_desc", "A tarred-leather bellows pump for draining tar pits and moving water. Attach a strainer hose to the intake and a nozzle hose to the outlet." },
            { "dtp_piece_strainer", "Intake strainer hose" },
            { "dtp_piece_strainer_desc", "Hose end with a strainer. Goes into tar or water: it is locked to whichever its tip is in when you build it, and that pipe system only ever pumps that." },
            { "dtp_piece_nozzle", "Outlet nozzle hose" },
            { "dtp_piece_nozzle_desc", "Hose end with a nozzle. Drains away from the pit." },
            { "dtp_piece_hose1", "Hose (1 m)" },
            { "dtp_piece_hose1_desc", "A straight tarred-leather hose section." },
            { "dtp_piece_hose2", "Hose (2 m)" },
            { "dtp_piece_hose2_desc", "A long straight tarred-leather hose section." },
            { "dtp_piece_elbow", "Hose elbow" },
            { "dtp_piece_elbow_desc", "A 90 degree hose bend." },
            { "dtp_tilt_help", "Tilt: hold {0} + scroll = pitch, + {1} = roll, {2} = reset." },
            { "dtp_tilt_hud", "Tilt: pitch {0}\u00b0, roll {1}\u00b0" },
            // hover
            { "dtp_hand_pump", "Hand pump" },
            { "dtp_prime", "Prime" }, { "dtp_pump", "Pump" }, { "dtp_stop", "Stop" }, { "dtp_operate", "Operate" },
            { "dtp_pump_hold", "Pump (hold = keep pumping)" }, { "dtp_step_away", "Step away" },
            { "dtp_in_use_by", "In use by {0}" }, { "dtp_strokes", "Strokes: {0}" },
            // messages
            { "dtp_msg_cant_use", "You can't use the pump right now" },
            { "dtp_msg_too_heavy", "You are too heavy to operate the pump" },
            { "dtp_msg_in_use", "The pump is already in use" },
            { "dtp_msg_in_use_by", "The pump is in use by {0}" },
            { "dtp_msg_no_answer", "The pump did not answer" },
            { "dtp_msg_tired", "You are too tired to pump" },
            // drain status
            { "dtp_intake_ok", "Intake: strainer connected" }, { "dtp_intake_bad", "Intake: {0}" },
            { "dtp_outlet_ok", "Outlet: nozzle connected" }, { "dtp_outlet_bad", "Outlet: {0}" },
            { "dtp_ready", "Ready to pump" },
            { "dtp_err_not_in_tar", "The strainer is not in tar" },
            { "dtp_err_above_tar", "The strainer is above the tar" },
            { "dtp_err_nozzle_in_tar", "The nozzle is in tar - move it out of the pit" },
            { "dtp_err_air", "The strainer sucks air - tar has to flow back to it, or move it deeper" },
            { "dtp_err_not_in_liquid", "The strainer is not in tar or water" },
            { "dtp_err_not_in_water", "The strainer is not in water" },
            { "dtp_err_above_water", "The strainer is above the water" },
            { "dtp_err_nozzle_in_water", "The nozzle is in water - move it out of the water" },
            { "dtp_err_water_off", "Water pumping is switched off in the config" },
            { "dtp_liquid_tar", "tar" }, { "dtp_liquid_water", "water" },
            { "dtp_ready_liquid", "Ready to pump {0}" },
            // hoses
            { "dtp_side_intake", "intake" }, { "dtp_side_outlet", "outlet" },
            { "dtp_h_no_snap", "Pump has no {0} snap point" },
            { "dtp_h_nothing", "Nothing is attached to the pump {0}" },
            { "dtp_h_thin_air", "The {0} hose ends in thin air" },
            { "dtp_h_must_end_strainer", "The intake hose must end in a strainer" },
            { "dtp_h_must_end_nozzle", "The outlet hose must end in a nozzle" },
            { "dtp_h_broken", "Broken hose piece" },
            { "dtp_h_too_long", "The {0} hose chain is too long" },
            // T-023 pool sign + its menu
            { "dtp_piece_sign", "Pool sign" },
            { "dtp_piece_sign_desc", "A sign post for a pool. Build it on or next to a pool the pump made and give the pool a name (E). Named pools are listed in the pool menu." },
            { "dtp_sign_unnamed", "Unnamed pool sign" },
            { "dtp_sign_pool_ok", "Names the pool here" }, { "dtp_sign_pool_none", "No pump pool here" },
            { "dtp_sign_edit", "Edit sign" },
            { "dtp_ui_sign_title", "Pool sign" }, { "dtp_ui_pool", "Pool: {0}" },
            { "dtp_ui_name", "Name" }, { "dtp_ui_emoji", "Emoji" },
            { "dtp_ui_emoji_note", "Emoji show only if the chosen font has them." },
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
            { "dtp_ui_pref_saved", "Preferred style saved." }, { "dtp_ui_pref_failed", "Saving the preferred style failed (see the log)." },
            { "dtp_ui_pref_used", "Preferred style loaded - Save to keep it." }, { "dtp_ui_pref_none", "No preferred style saved yet." },
            { "dtp_ui_save", "Save" }, { "dtp_ui_cancel", "Cancel" },
        };

        static readonly Dictionary<string, string> De = new()
        {
            { "dtp_piece_pump", "Handpumpe" },
            { "dtp_piece_pump_desc", "Eine geteerte Lederbalgpumpe zum Abpumpen von Teergruben und zum Umpumpen von Wasser. Schließe einen Siebschlauch an den Einlass und einen Düsenschlauch an den Auslass an." },
            { "dtp_piece_strainer", "Einlass-Siebschlauch" },
            { "dtp_piece_strainer_desc", "Schlauchende mit Sieb. Kommt in Teer oder Wasser: Es wird beim Bauen auf das festgelegt, worin die Spitze steckt, und diese Schlauchkette pumpt nur noch das." },
            { "dtp_piece_nozzle", "Auslass-Düsenschlauch" },
            { "dtp_piece_nozzle_desc", "Schlauchende mit Düse. Hier fließt der Teer ab, abseits der Grube." },
            { "dtp_piece_hose1", "Schlauch (1 m)" },
            { "dtp_piece_hose1_desc", "Ein gerades, geteertes Lederschlauchstück." },
            { "dtp_piece_hose2", "Schlauch (2 m)" },
            { "dtp_piece_hose2_desc", "Ein langes, gerades, geteertes Lederschlauchstück." },
            { "dtp_piece_elbow", "Schlauchbogen" },
            { "dtp_piece_elbow_desc", "Ein 90-Grad-Schlauchbogen." },
            { "dtp_tilt_help", "Neigen: {0} halten + Scrollen = Neigung, + {1} = Rollen, {2} = Zurücksetzen." },
            { "dtp_tilt_hud", "Neigung: {0}\u00b0, Rollen {1}\u00b0" },
            { "dtp_hand_pump", "Handpumpe" },
            { "dtp_prime", "Vorpumpen" }, { "dtp_pump", "Pumpen" }, { "dtp_stop", "Stopp" }, { "dtp_operate", "Bedienen" },
            { "dtp_pump_hold", "Pumpen (halten = weiterpumpen)" }, { "dtp_step_away", "Wegtreten" },
            { "dtp_in_use_by", "In Benutzung von {0}" }, { "dtp_strokes", "Hübe: {0}" },
            { "dtp_msg_cant_use", "Du kannst die Pumpe gerade nicht benutzen" },
            { "dtp_msg_too_heavy", "Du trägst zu viel, um die Pumpe zu bedienen" },
            { "dtp_msg_in_use", "Die Pumpe wird bereits benutzt" },
            { "dtp_msg_in_use_by", "Die Pumpe wird von {0} benutzt" },
            { "dtp_msg_no_answer", "Die Pumpe antwortet nicht" },
            { "dtp_msg_tired", "Du bist zu erschöpft zum Pumpen" },
            { "dtp_intake_ok", "Einlass: Sieb angeschlossen" }, { "dtp_intake_bad", "Einlass: {0}" },
            { "dtp_outlet_ok", "Auslass: Düse angeschlossen" }, { "dtp_outlet_bad", "Auslass: {0}" },
            { "dtp_ready", "Bereit zum Pumpen" },
            { "dtp_err_not_in_tar", "Das Sieb steckt nicht im Teer" },
            { "dtp_err_above_tar", "Das Sieb hängt über dem Teer" },
            { "dtp_err_nozzle_in_tar", "Die Düse steckt im Teer - bring sie aus der Grube" },
            { "dtp_err_air", "Das Sieb saugt Luft - Teer muss nachfließen oder das Sieb tiefer setzen" },
            { "dtp_err_not_in_liquid", "Das Sieb steckt weder in Teer noch in Wasser" },
            { "dtp_err_not_in_water", "Das Sieb steckt nicht im Wasser" },
            { "dtp_err_above_water", "Das Sieb hängt über dem Wasser" },
            { "dtp_err_nozzle_in_water", "Die Düse steckt im Wasser - bring sie aus dem Wasser" },
            { "dtp_err_water_off", "Wasserpumpen ist in der Konfiguration ausgeschaltet" },
            { "dtp_liquid_tar", "Teer" }, { "dtp_liquid_water", "Wasser" },
            { "dtp_ready_liquid", "Bereit zum Pumpen: {0}" },
            { "dtp_side_intake", "Einlass" }, { "dtp_side_outlet", "Auslass" },
            { "dtp_h_no_snap", "Die Pumpe hat keinen Anschlusspunkt: {0}" },
            { "dtp_h_nothing", "Am {0} der Pumpe ist nichts angeschlossen" },
            { "dtp_h_thin_air", "Der Schlauch am {0} endet im Nichts" },
            { "dtp_h_must_end_strainer", "Der Einlassschlauch muss in einem Sieb enden" },
            { "dtp_h_must_end_nozzle", "Der Auslassschlauch muss in einer Düse enden" },
            { "dtp_h_broken", "Defektes Schlauchstück" },
            { "dtp_h_too_long", "Die Schlauchkette am {0} ist zu lang" },
            { "dtp_piece_sign", "Beckenschild" },
            { "dtp_piece_sign_desc", "Ein Schildpfosten für ein Becken. Auf oder neben ein von der Pumpe gefülltes Becken bauen und dem Becken einen Namen geben (E). Benannte Becken stehen im Beckenmenü." },
            { "dtp_sign_unnamed", "Unbenanntes Beckenschild" },
            { "dtp_sign_pool_ok", "Benennt das Becken hier" }, { "dtp_sign_pool_none", "Hier ist kein Pumpenbecken" },
            { "dtp_sign_edit", "Schild bearbeiten" },
            { "dtp_ui_sign_title", "Beckenschild" }, { "dtp_ui_pool", "Becken: {0}" },
            { "dtp_ui_name", "Name" }, { "dtp_ui_emoji", "Emoji" },
            { "dtp_ui_emoji_note", "Emoji erscheinen nur, wenn die gewählte Schrift sie hat." },
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
            { "dtp_ui_pref_saved", "Bevorzugter Stil gespeichert." }, { "dtp_ui_pref_failed", "Speichern des bevorzugten Stils fehlgeschlagen (siehe Log)." },
            { "dtp_ui_pref_used", "Bevorzugter Stil geladen - Speichern, um ihn zu behalten." }, { "dtp_ui_pref_none", "Noch kein bevorzugter Stil gespeichert." },
            { "dtp_ui_save", "Speichern" }, { "dtp_ui_cancel", "Abbrechen" },
        };

        /// Registers the tokens with Jotunn. Call once from Plugin.Awake, before any piece is added.
        public static void Register()
        {
            var loc = LocalizationManager.Instance.GetLocalization();
            loc.AddJsonFile("English", ToJson(WithKeys(En)));
            loc.AddJsonFile("German", ToJson(WithKeys(De)));
        }

        // the tilt help text names the configured keys; filled in here because the game localizes piece descriptions itself
        static Dictionary<string, string> WithKeys(Dictionary<string, string> src)
        {
            var d = new Dictionary<string, string>(src);
            d["dtp_tilt_help"] = src["dtp_tilt_help"].Replace("{0}", ModSettings.TiltKey.Value.ToString())
                .Replace("{1}", ModSettings.RollKey.Value.ToString()).Replace("{2}", ModSettings.TiltResetKey.Value.ToString());
            return d;
        }

        static string ToJson(Dictionary<string, string> d)
        {
            var sb = new System.Text.StringBuilder("{");
            bool first = true;
            foreach (var kv in d)
            {
                if (!first) sb.Append(','); first = false;
                sb.Append('"').Append(Esc(kv.Key)).Append("\":\"").Append(Esc(kv.Value)).Append('"');
            }
            return sb.Append('}').ToString();
        }

        static string Esc(string s) { return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n"); }

        /// "$dtp_xxx" for use in names / descriptions that Valheim localizes itself.
        public static string Token(string key) { return "$" + key; }

        /// Localized text for a key, {0} {1} ... replaced by args.
        public static string T(string key, params string[] args)
        {
            var l = Localization.instance;
            string s;
            if (l != null) s = l.Localize("$" + key);
            else if (!En.TryGetValue(key, out s)) s = key;
            // filled in here (not via Localize(text, words)): the {0} of 'Strokes: {0}' stayed in the hover text
            if (args != null) for (int i = 0; i < args.Length; i++) s = s.Replace("{" + i + "}", args[i]);
            return s;
        }
    }
}
