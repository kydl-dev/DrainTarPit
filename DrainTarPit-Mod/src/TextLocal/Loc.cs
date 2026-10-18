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
