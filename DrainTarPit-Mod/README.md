# DrainTarPit

Drain tar pits and move water with a hand pump and hoses. Build a pump, run an intake hose with a strainer into the pit (or the water) and an outlet hose with a nozzle to dry ground, then work the lever: the level drops and the liquid pours out of the nozzle. Only the liquid is removed: Tar items, meat or anything else lying in the pit stay where they are, so you can walk in and pick them up once it is drained.

## Tar or water

A strainer is locked to what its tip sits in **when you build it** - tar or water - and its whole pipe system only ever pumps that. Tar behaves as described below. Water: the world's water never runs dry, so a water pump has an endless supply; the water pours out of the nozzle (blue stream, splashes, puddle) and runs away - it is not deposited anywhere yet. A strainer built on dry ground locks on its first real stroke. `[Drain] AllowWater` / `MinWaterDepth` in the config.

## Pieces (Hammer > Misc)

| Piece | Notes |
|---|---|
| Hand pump | Intake + outlet snap points, a stand point and a lever |
| Intake strainer hose | The end that goes into the tar |
| Outlet nozzle hose | Tar comes out here |
| Hose (1 m), Hose (2 m), Hose elbow | Chain them with snap points |

Tilt a held piece with **Alt + scroll** (pitch), **Alt + Ctrl + scroll** (roll), **Backspace** to reset. Keys are configurable.

## Using the pump

- **E** on the pump: step up to it. **E** again = one stroke. **Hold E** = keep pumping until you let go.
- **Shift + E**, Space or walking away: step off. Getting hit throws you off the pump.
- The hover text shows whether the intake and outlet chains are connected and why a stroke would fail.
- Each stroke costs stamina. The strainer does not have to be perfect: it works with tar around its tip and up to half a metre above the surface.
- Every stroke pulls 6 m3 of tar liquid (strongest at the strainer, fading out over 4 m) and the rest of the pit flows towards the hole about 3x faster than vanilla while you pump; both are configurable. Only the liquid is taken - Tar items, meat and so on in the pit are never removed.
- A drained pit slowly seeps back (default 60 minutes).
- The pumped tar is poured into a real tar liquid volume where the stream from the nozzle lands: dig a hole with the pickaxe, or build a ring of stone / wood walls, point the nozzle at it and it fills up to the lowest point of the rim. Once it is full the rest runs over the rim and fills whatever lies behind it (or runs off). It behaves like vanilla tar (slows you, is saved, can be pumped again).
- Multiplayer: one player at a time on a pump, others see the lever and the pumping animation.

## Config (BepInEx/config)

`Drain` (`VolumePerStroke`, `DrainRadius`, `PitFlowBoost` / `PitFlowBoostSeconds` = how fast the rest of the pit runs towards the hole, strainer reach, refill time, `PoolWalls` = built pieces hold the tar, `PoolOverflow` = the rest runs over the lowest point of the rim, `PoolRimDisappear` = off: a full pool overflows over its lowest point and the liquid runs away; on: the liquid stops at the rim and the excess disappears), `Pump` (stamina, hold time, hand IK grip: `GripSpread`, `GripUp`, `GripForward`, `GripPalmOffset`, `HandRotate`, `FingerCurl`; `StandHeight` / `StandForward` / `StandSide` to move the player at the pump, `BendToReach` / `MaxBackBend` / `MaxKneeBend` (the player bends the back, then the knees, so the hands reach the lever at the bottom of the stroke), `AutoReach` / `MaxAutoLift` to lift the player until the hands reach the handle), `Building` (tilt keys, piece health), `Effects` (droplets + sounds, `NozzleFlowAmount` = how much tar visibly pours out of the nozzle, `NozzlePressure` = how far it shoots, `NozzleFlowSeconds` = how long it runs after each press), `Visuals` (texture brightness, water look), `Build Time Hose Visuals` (`Build Hose Style` Solid / Transparent and `Build Hose Opacity`: the look a hose gets when you build it, stored on that hose for good), `World Hose Visuals` (`World Hose Style` Default / Transparent / Solid and `World Hose Opacity`: how all hoses already in the world are shown, right away; Default = each hose its own build look), `General` (debug log). Edits to the `.cfg` file are picked up while the game runs. Console test commands (`dtp ...`) need devcommands.

## Requirements

BepInEx and Jotunn. **Everyone on the server needs the mod.** Pieces are English and German; other languages show English.

## Known limits

- Pump animation on other players' screens needs the game's sit-animation slot. If another player's pumping does not animate for you, set `[Pump] AttachClipOverride` to the clip name printed in the log (`Pump: player attach clip is '...'`).
- The pour at the nozzle (stream, splashes, puddle) is shown to every player who has the pump loaded, but only while someone is mounted on it.
