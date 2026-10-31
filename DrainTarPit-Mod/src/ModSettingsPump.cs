using Kydl.Common;
using BepInEx.Configuration;

namespace DrainTarPit;

/// <summary>
/// [Pump]. T-068 (user, config review 2026-10-21): only AutoReach stays in the player's config; every other pump setting has a fixed value
/// (Tunable, the values the user tested) and can be changed at runtime from the user's KydlTools mod when a bug is reported.
/// </summary>
internal static partial class ModSettings
{
    public static readonly Tunable<float> StaminaPerStroke = new("Pump", "StaminaPerStroke", 3f, "Stamina used per pump stroke.");
    public static readonly Tunable<float> HoldToCycleSeconds = new("Pump", "HoldToCycleSeconds", 0.4f, "Hold the use button (E) this long to switch from single strokes to continuous pumping. Release to stop.");
    public static readonly Tunable<float> CycleStrokeSeconds = new("Pump", "CycleStrokeSeconds", 1.2f, "Fallback seconds per loop while pumping continuously; the real Cycle clip length is used when the clip is found.");
    public static readonly Tunable<bool> FacePump = new("Pump", "FacePump", true, "Turn the player towards the lever when mounting the pump (the stand point is rotated at runtime). Turn off to use the stand_point's own rotation.");
    public static readonly Tunable<float> StandYawOffset = new("Pump", "StandYawOffset", 0f, "Extra yaw (degrees) added to the facing direction when mounting. Use 90 / -90 / 180 if the player stands sideways or backwards at the pump.");
    public static readonly Tunable<string> AttachBool = new("Pump", "AttachBool", "attach_chair", "Vanilla player animator bool used to pin the player in place while pumping. Its clip is replaced by the pump clips.");
    public static readonly Tunable<float> MaxPumpDistance = new("Pump", "MaxPumpDistance", 3f, "If the mounted player ends up further than this (m) from the stand point (pushed, teleported, ...) they are released at once.");
    public static readonly Tunable<bool> HandIK = new("Pump", "HandIK", true, "Pin the player's hands to the lever handle with IK while pumping (stops the hands clipping through the lever).");
    public static readonly Tunable<float> HandIKBlendSeconds = new("Pump", "HandIKBlendSeconds", 0.35f, "Seconds to blend the hands onto / off the handle when mounting / stepping away.");
    public static readonly Tunable<float> GripSpread = new("Pump", "GripSpread", 0.17f, "Half the distance (m) between the two hands on the handle.");
    public static readonly Tunable<float> GripUp = new("Pump", "GripUp", 0f, "Move the grip point up (+) / down (-) from the lever's interact_point (m). Tweak if the hands sit above/below the handle. The arms can only reach so far: to get the whole player higher use StandHeight.");
    public static readonly Tunable<float> GripForward = new("Pump", "GripForward", -0.025f, "Move the grip point along the lever (+ = away from you, towards the pump column, m).");
    public static readonly Tunable<bool> HandRotate = new("Pump", "HandRotate", true, "Turn the hands palm-down onto the handle bar (overhand grip) instead of leaving them in the animation's orientation. Turn off to compare / if the hands twist wrongly on a modded player model.");
    public static readonly Tunable<float> FingerCurl = new("Pump", "FingerCurl", 0.79f, "How far the fingers curl around the handle bar (0 = open hand, 1 = tight wrap). Needs a player rig with finger bones.");
    public static readonly Tunable<float> GripPalmOffset = new("Pump", "GripPalmOffset", -0.002f, "Gap (m) between the palm and the top of the handle bar. Lower = hands sink into the bar, higher = hands float above it.");
    public static readonly Tunable<float> ThumbCurl = new("Pump", "ThumbCurl", 0.8f, "How far the thumb curls down and under the handle bar (0 = thumb keeps the animation pose, 1 = tight wrap). Needs finger bones.");
    public static readonly Tunable<float> ThumbSpread = new("Pump", "ThumbSpread", 25f, "Degrees the thumb is swung away from the fist (sideways, before it curls under the bar). Raise it if the thumb still sits against the index finger, lower it if it sticks out.");
    public static readonly Tunable<float> ClipBlendSeconds = new("Pump", "ClipBlendSeconds", 0.12f, "Seconds to cross-fade between the pump clips (Enter / Idle / Push / Pull / Cycle / Exit). 0 = hard cuts like before. Removes the pose popping at every phase change.");
    public static readonly Tunable<bool> BendToReach = new("Pump", "BendToReach", true, "When the hands cannot reach the lever handle (typically at the bottom of the stroke), bend the player to get there: first forward from the back (spine), then down at the knees. The bend follows the lever, so it is deepest when the lever is down. Turn off for the plain clips.");
    public static readonly Tunable<float> MaxBackBend = new("Pump", "MaxBackBend", 45f, "Most (degrees, spread over spine / chest / upper chest) the player may bend forward from the back to reach the handle.");
    public static readonly Tunable<float> MaxKneeBend = new("Pump", "MaxKneeBend", 0.08f, "Most (m) the hips may sink with bent knees (feet stay on the ground) when the back bend alone is not enough.");
    public static readonly Tunable<float> HipLock = new("Pump", "HipLock", 0.85f, "How much the hips are held still while pumping (0 = they follow the clips like before, 1 = fixed at their calm position; the feet stay planted by leg IK). The stroke then comes from the upper body: the back bends towards the lever and the arms push. Needs BendToReach.");
    public static readonly Tunable<float> BendSmooth = new("Pump", "BendSmooth", 8f, "How fast (1/s) the back bend follows the lever. Lower = softer, more rounded movement; higher = snappier.");
    public static readonly Tunable<bool> FreezeIdle = new("Pump", "FreezeIdle", true, "Hold the Idle pose still instead of looping the Idle clip. The Idle clip makes the left leg twitch every loop; with this on the legs stay calm (the arms follow the lever IK anyway).");
    public static readonly Tunable<float> StandHeight = new("Pump", "StandHeight", 0f, "Lifts (+) or lowers (-) the player at the pump (m). Raise it if the hands cannot reach the handle (arms stretched, hands below the lever). Applied when you step onto the pump.");
    public static readonly Tunable<float> StandForward = new("Pump", "StandForward", -0.28f, "Moves the player towards (+) or away from (-) the lever (m).");
    public static readonly Tunable<float> StandSide = new("Pump", "StandSide", 0f, "Moves the player sideways (m). + = to the player's right when facing the lever.");
    public static readonly Tunable<float> MaxAutoStep = new("Pump", "MaxAutoStep", 0.25f, "Most (m) AutoReach may step the player towards the lever.");
    public static readonly Tunable<float> MaxAutoRaise = new("Pump", "MaxAutoRaise", 0.15f, "Most (m) AutoReach may then lift the player (a standing player should barely float).");

    static void BindPump(ConfigFile c)
    {
        AutoReach = c.Bind("General", "AutoReach", true,
            "While pumping, move the player just far enough towards the lever (and, only if that is not enough, up) for both hands to reach the handle at the top of the stroke. Turn off to use StandHeight / StandForward only.");
    }
}
