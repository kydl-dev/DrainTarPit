using System.Collections.Generic;

namespace DrainTarPit
{
    /// <summary>The player-facing English text of DrainTarPit (pieces, hover texts, messages). Keys start with dtp_; registered with Kydl.Common.Loc in Plugin.Awake. (The pool menu and sign texts moved to WaterAndTarPools.)</summary>
    internal static class DtpText
    {
        internal static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
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
            { "dtp_hand_pump", "Hand pump" },
            { "dtp_prime", "Prime" },
            { "dtp_pump", "Pump" },
            { "dtp_stop", "Stop" },
            { "dtp_operate", "Operate" },
            { "dtp_pump_hold", "Pump (hold = keep pumping)" },
            { "dtp_step_away", "Step away" },
            { "dtp_in_use_by", "In use by {0}" },
            { "dtp_strokes", "Strokes: {0}" },
            { "dtp_msg_cant_use", "You can't use the pump right now" },
            { "dtp_msg_too_heavy", "You are too heavy to operate the pump" },
            { "dtp_msg_in_use", "The pump is already in use" },
            { "dtp_msg_in_use_by", "The pump is in use by {0}" },
            { "dtp_msg_no_answer", "The pump did not answer" },
            { "dtp_msg_tired", "You are too tired to pump" },
            { "dtp_intake_ok", "Intake: strainer connected" },
            { "dtp_intake_bad", "Intake: {0}" },
            { "dtp_outlet_ok", "Outlet: nozzle connected" },
            { "dtp_outlet_bad", "Outlet: {0}" },
            { "dtp_ready", "Ready to pump" },
            { "dtp_err_not_in_tar", "The strainer is not in tar" },
            { "dtp_err_above_tar", "The strainer is above the tar" },
            { "dtp_err_nozzle_in_tar", "The nozzle is in tar - move it out of the pit" },
            { "dtp_err_air", "The strainer sucks air - tar has to flow back to it, or move it deeper" },
            { "dtp_err_not_in_liquid", "The strainer is not in tar or water" },
            { "dtp_err_not_in_water", "The strainer is not in water" },
            { "dtp_err_above_water", "The strainer is above the water" },
            { "dtp_err_nozzle_in_water", "The nozzle is in water - move it out of the water" },
            { "dtp_err_water_off", "Pumping water needs the WaterAndTarPools mod" },
            { "dtp_mod_off", "DrainTarPit is switched off ([General] EnableAll)" },
            { "dtp_drain_off", "Draining is switched off in the config ([Drain] Enable)." },
            { "dtp_liquid_tar", "tar" },
            { "dtp_liquid_water", "water" },
            { "dtp_ready_liquid", "Ready to pump {0}" },
            { "dtp_side_intake", "intake" },
            { "dtp_side_outlet", "outlet" },
            { "dtp_h_no_snap", "Pump has no {0} snap point" },
            { "dtp_h_nothing", "Nothing is attached to the pump {0}" },
            { "dtp_h_thin_air", "The {0} hose ends in thin air" },
            { "dtp_h_must_end_strainer", "The intake hose must end in a strainer" },
            { "dtp_h_must_end_nozzle", "The outlet hose must end in a nozzle" },
            { "dtp_h_broken", "Broken hose piece" },
            { "dtp_h_too_long", "The {0} hose chain is too long" },
        };

        /// The table with the configured tilt keys filled into the tilt help (the game localizes piece descriptions itself, so it cannot be done at display time).
        internal static Dictionary<string, string> WithKeys()
        {
            var d = new Dictionary<string, string>(En);
            d["dtp_tilt_help"] = En["dtp_tilt_help"].Replace("{0}", ModSettings.TiltKey.Value.ToString())
                .Replace("{1}", ModSettings.RollKey.Value.ToString()).Replace("{2}", ModSettings.TiltResetKey.Value.ToString());
            return d;
        }
    }
}
