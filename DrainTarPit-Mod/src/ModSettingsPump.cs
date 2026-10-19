using BepInEx.Configuration;
using DrainTarPit.Drain;
using DrainTarPit.Logging;
using DrainTarPit.Visuals;
using UnityEngine;
using static DrainTarPit.ModSettings;

namespace DrainTarPit;

/// The [Pump] config entries (stroke, posture, hand IK, stand position). The entries themselves are fields of ModSettings; called from ModSettings.Bind.
internal static class ModSettingsPump
{
    public static void Bind(ConfigFile c)
    {
        StaminaPerStroke = c.Bind("Pump", "StaminaPerStroke", 3f,
            new ConfigDescription("Stamina used per pump stroke.", new AcceptableValueRange<float>(0f, 50f)));
        HoldToCycleSeconds = c.Bind("Pump", "HoldToCycleSeconds", 0.4f,
            new ConfigDescription(
                "Hold the use button (E) this long to switch from single strokes to continuous pumping. Release to stop.",
                new AcceptableValueRange<float>(0.1f, 2f)));
        CycleStrokeSeconds = c.Bind("Pump", "CycleStrokeSeconds", 1.2f,
            new ConfigDescription(
                "Fallback seconds per loop while pumping continuously; the real Cycle clip length is used when the clip is found.",
                new AcceptableValueRange<float>(0.3f, 5f)));
        FacePump = c.Bind("Pump", "FacePump", true,
            "Turn the player towards the lever when mounting the pump (the stand point is rotated at runtime). Turn off to use the stand_point's own rotation.");
        StandYawOffset = c.Bind("Pump", "StandYawOffset", 0f,
            new ConfigDescription(
                "Extra yaw (degrees) added to the facing direction when mounting. Use 90 / -90 / 180 if the player stands sideways or backwards at the pump.",
                new AcceptableValueRange<float>(-180f, 180f)));
        AttachBool = c.Bind("Pump", "AttachBool", "attach_chair",
            "Vanilla player animator bool used to pin the player in place while pumping. Its clip is replaced by the pump clips.");
        AttachClipOverride = c.Bind("Pump", "AttachClipOverride", "",
            "Name of the vanilla clip behind AttachBool, to be replaced by the pump clips. Empty = auto-detect at runtime (see `dtp clips`).");

        MaxPumpDistance = c.Bind("Pump", "MaxPumpDistance", 3f,
            new ConfigDescription(
                "If the mounted player ends up further than this (m) from the stand point (pushed, teleported, ...) they are released at once.",
                new AcceptableValueRange<float>(1f, 20f)));
        HandIK = c.Bind("Pump", "HandIK", true,
            "Pin the player's hands to the lever handle with IK while pumping (stops the hands clipping through the lever).");
        HandIKBlendSeconds = c.Bind("Pump", "HandIKBlendSeconds", 0.35f,
            new ConfigDescription("Seconds to blend the hands onto / off the handle when mounting / stepping away.",
                new AcceptableValueRange<float>(0.05f, 2f)));
        GripSpread = c.Bind("Pump", "GripSpread", 0.17f,
            new ConfigDescription("Half the distance (m) between the two hands on the handle.",
                new AcceptableValueRange<float>(0f, 0.5f)));
        GripUp = c.Bind("Pump", "GripUp", 0f,
            new ConfigDescription(
                "Move the grip point up (+) / down (-) from the lever's interact_point (m). Tweak if the hands sit above/below the handle. The arms can only reach so far: to get the whole player higher use StandHeight.",
                new AcceptableValueRange<float>(-1f, 1f)));
        GripForward = c.Bind("Pump", "GripForward", -0.025f,
            new ConfigDescription(
                "Move the grip point along the lever (+ = away from you, towards the pump column, m).",
                new AcceptableValueRange<float>(-1f, 1f)));
        HandRotate = c.Bind("Pump", "HandRotate", true,
            "Turn the hands palm-down onto the handle bar (overhand grip) instead of leaving them in the animation's orientation. Turn off to compare / if the hands twist wrongly on a modded player model.");
        FingerCurl = c.Bind("Pump", "FingerCurl", 0.79f,
            new ConfigDescription(
                "How far the fingers curl around the handle bar (0 = open hand, 1 = tight wrap). Needs a player rig with finger bones.",
                new AcceptableValueRange<float>(0f, 1.3f)));
        GripPalmOffset = c.Bind("Pump", "GripPalmOffset", -0.002f,
            new ConfigDescription(
                "Gap (m) between the palm and the top of the handle bar. Lower = hands sink into the bar, higher = hands float above it.",
                new AcceptableValueRange<float>(-0.05f, 0.1f)));
        ThumbCurl = c.Bind("Pump", "ThumbCurl", 0.8f,
            new ConfigDescription("How far the thumb curls down and under the handle bar (0 = thumb keeps the animation pose, 1 = tight wrap). Needs finger bones.",
                new AcceptableValueRange<float>(0f, 1.3f)));
        ThumbSpread = c.Bind("Pump", "ThumbSpread", 25f,
            new ConfigDescription("Degrees the thumb is swung away from the fist (sideways, before it curls under the bar). Raise it if the thumb still sits against the index finger, lower it if it sticks out.",
                new AcceptableValueRange<float>(0f, 60f)));
        ClipBlendSeconds = c.Bind("Pump", "ClipBlendSeconds", 0.12f,
            new ConfigDescription("Seconds to cross-fade between the pump clips (Enter / Idle / Push / Pull / Cycle / Exit). 0 = hard cuts like before. Removes the pose popping at every phase change.",
                new AcceptableValueRange<float>(0f, 0.5f)));
        BendToReach = c.Bind("Pump", "BendToReach", true,
            "When the hands cannot reach the lever handle (typically at the bottom of the stroke), bend the player to get there: first forward from the back (spine), then down at the knees. The bend follows the lever, so it is deepest when the lever is down. Turn off for the plain clips.");
        MaxBackBend = c.Bind("Pump", "MaxBackBend", 45f,
            new ConfigDescription("Most (degrees, spread over spine / chest / upper chest) the player may bend forward from the back to reach the handle.",
                new AcceptableValueRange<float>(0f, 70f)));
        MaxKneeBend = c.Bind("Pump", "MaxKneeBend", 0.08f,
            new ConfigDescription("Most (m) the hips may sink with bent knees (feet stay on the ground) when the back bend alone is not enough.",
                new AcceptableValueRange<float>(0f, 0.6f)));
        HipLock = c.Bind("Pump", "HipLock", 0.85f,
            new ConfigDescription("How much the hips are held still while pumping (0 = they follow the clips like before, 1 = fixed at their calm position; the feet stay planted by leg IK). The stroke then comes from the upper body: the back bends towards the lever and the arms push. Needs BendToReach.",
                new AcceptableValueRange<float>(0f, 1f)));
        BendSmooth = c.Bind("Pump", "BendSmooth", 8f,
            new ConfigDescription("How fast (1/s) the back bend follows the lever. Lower = softer, more rounded movement; higher = snappier.",
                new AcceptableValueRange<float>(1f, 30f)));
        FreezeIdle = c.Bind("Pump", "FreezeIdle", true,
            "Hold the Idle pose still instead of looping the Idle clip. The Idle clip makes the left leg twitch every loop; with this on the legs stay calm (the arms follow the lever IK anyway).");

        StandHeight = c.Bind("Pump", "StandHeight", 0f,
            new ConfigDescription(
                "Lifts (+) or lowers (-) the player at the pump (m). Raise it if the hands cannot reach the handle (arms stretched, hands below the lever). Applied when you step onto the pump.",
                new AcceptableValueRange<float>(-1f, 2f)));
        StandForward = c.Bind("Pump", "StandForward", -0.28f,
            new ConfigDescription("Moves the player towards (+) or away from (-) the lever (m).",
                new AcceptableValueRange<float>(-1f, 1f)));
        StandSide = c.Bind("Pump", "StandSide", 0f,
            new ConfigDescription("Moves the player sideways (m). + = to the player's right when facing the lever.",
                new AcceptableValueRange<float>(-1f, 1f)));
        AutoReach = c.Bind("Pump", "AutoReach", true,
            "While pumping, move the player just far enough towards the lever (and, only if that is not enough, up) for both hands to reach the handle at the top of the stroke. Turn off to use StandHeight / StandForward only.");
        MaxAutoStep = c.Bind("Pump", "MaxAutoStep", 0.25f,
            new ConfigDescription("Most (m) AutoReach may step the player towards the lever.", new AcceptableValueRange<float>(0f, 1f)));
        MaxAutoRaise = c.Bind("Pump", "MaxAutoRaise", 0.15f,
            new ConfigDescription("Most (m) AutoReach may then lift the player (a standing player should barely float).", new AcceptableValueRange<float>(0f, 1f)));
    }
}
