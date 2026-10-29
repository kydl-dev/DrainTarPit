# DrainTarPit

A hand pump with hoses to drain Valheim's tar pits. Build a pump, run an intake hose with a strainer into the tar and an outlet hose with a nozzle somewhere else, then work the lever: the tar level drops and the tar pours out of the nozzle. Only the liquid is removed - Tar items, meat or anything else lying in the pit stay where they are, so you can walk in and pick them up once it is drained.

Want the pumped tar to stay, fill your own pools or pump water? Install the add-on **WaterAndTarPools** (needs this mod).

## Pieces (Hammer > KY-Items)

| Piece | Cost | Notes |
|---|---|---|
| Hand pump | 10 Wood, 5 Iron, 4 Leather scraps, 5 Resin | Intake and outlet snap points, a lever and a place to stand |
| Intake strainer hose | 2 Iron, 2 Leather scraps, 2 Tar | The end that goes into the tar |
| Outlet nozzle hose | 2 Iron, 1 Leather scraps, 2 Tar | The tar comes out here |
| Hose (1 m) | 1 Leather scraps, 1 Tar | |
| Hose (2 m) | 2 Leather scraps, 2 Tar | |
| Hose elbow | 2 Leather scraps, 2 Tar | 90 degree bend |

Chain the hoses with their snap points. Tilt a held piece with **Alt + scroll** (pitch), **Alt + Ctrl + scroll** (roll) and **Backspace** to reset (keys configurable). Pieces only break when hit: no support, rain or fire damage.

## Using the pump

- **E** on the pump: step up to it. **E** again = one stroke. **Hold E** = keep pumping until you let go.
- **Shift + E**, Space or walking away: step off. Getting hit throws you off the pump.
- The hover text shows whether the intake and outlet chains are connected and why a stroke would fail.
- Each stroke costs a little stamina. The strainer does not have to be perfect: it works with tar around its tip and up to half a metre above the surface.
- Every stroke pulls 6 m³ of tar (strongest at the strainer, fading out over 4 m); the rest of the pit flows towards the hole faster while you pump.
- A drained pit slowly seeps back (60 minutes by default).
- See-through hoses show the tar moving through them; the stream, splashes and a short puddle appear where it comes out, with sounds. Everyone near the pump sees and hears it.
- Multiplayer: one player at a time on a pump, everyone else sees the lever and the pumping animation.

## Config

`BepInEx/config/com.kydldev.DrainTarPit.cfg` (changes are picked up while the game runs):

| Section | Settings |
|---|---|
| General | `EnableAll` - switch the mod off while playing (built pieces stay) |
| DebugLog | `Enable` - detailed log lines for bug reports |
| Building | `TiltKey`, `RollKey`, `TiltResetKey`, `TiltStep` |
| Effects | `SoundEffects` (all sounds of the mod), `EffectsVolume`, `ScratchSounds`, `TarSplashSound`, `TarSlugFlow` (tar visible in see-through hoses), `LiquidFlowSpeed`, `NozzleFlowAmount`, `NozzlePuddle`, `NozzlePressure`, `NozzleFlowSeconds`, `PourSoundOffset` |
| Drain | `VolumePerStroke` (5 - 30 m³), `DrainRadius`, `StrainerSearchRadius`, `StrainerReach`, `MinTarDepth`, `MaxChainPieces`, `SnapTolerance`, `RefillMinutes` (10 - 120) |
| Pump | `AutoReach` - step the player towards the lever so both hands reach the handle |
| Build Time Hose Visuals | `Build Hose Style` (Solid / Transparent) and `Build Hose Opacity`: the look a hose gets when you build it |
| World Hose Visuals | `World Hose Style` (Default / Transparent / Solid) and `World Hose Opacity`: how every hose already in the world is shown |

## Requirements

BepInEx and Jotunn. **Everyone on the server needs the mod.** English and German; other languages show English.

## Known limits

- The pour at the nozzle (stream, splashes, puddle) is shown to every player who has the pump loaded, but only while someone is pumping.
