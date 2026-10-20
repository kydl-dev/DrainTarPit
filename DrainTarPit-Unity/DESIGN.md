# DrainTarPit - status and design (WIP)

## Inventory (verified from disk / Blender)
Unity project: E:\Projects\Valheim\DrainTarPit\DrainTarPit (Unity 6000.0.75f1, HDRP)
- Models (FBX, Y-up): Hose_Straight_1m, Hose_Straight_2m, Hose_Elbow90, Hose_IntakeStrainer, Hose_OutletNozzle, Pump_Manual
- Prefabs for all six in Assets/Prefabs, all labelled asset bundle `draintarpit`; bundle already built to AssetBundles/draintarpit
- Pump prefab: `model` child (rot Y 180), Animator + Assets/Animation/DTP_Pump.controller (bool param `pump`), clip DTP_Pump_Cycle.anim, `fx_outlet` child
- Editor: Assets/Editor/DrainTarPitBundleBuilder.cs (menu DrainTarPit/Build AssetBundle)
- No BepInEx/Jotunn plugin project exists yet.

## Snap / interact points (Blender coords, Z-up, metres)
| Piece | Empty | Local pos |
|---|---|---|
| Straight_1m | snapA / snapB | (0,-0.5,0.11) / (0,0.5,0.11) |
| Straight_2m | snapA / snapB | (0,-1,0.11) / (0,1,0.11) |
| Elbow90 | snapA / snapB | (-0.31,-0.31,0.11) / (0.31,0.31,0.11) |
| IntakeStrainer | snapA | (0,0,0) |
| OutletNozzle | snapA | (0,0,0) |
| Pump_Manual | snapIntake / snapOutlet | (-0.46,0,0.38) / (0.46,0,0.76) |
| Pump_Manual_Lever | interactPoint | (0,0.95,0) child of lever |

## Proposed mechanic (not implemented)
1. Strainer placed in tar -> hoses snap in a chain -> pump intake.
2. Player uses pump lever (Interactable on interactPoint) -> Animator `pump` -> each stroke drains N units.
3. Drained tar lowers the pit's tar level and yields tar items; nozzle outlet = output / fx_outlet particles.
4. Must be ZDO-synced for multiplayer.

## Blockers
- Unity MCP: "No Unity Editor instances found" - editor not connected.
- Primary reference repo (kydl-dev/Reference-files) not reachable from this session; Valheim tar/LiquidVolume API is UNVERIFIED.
- Need: Valheim managed DLLs or the reference files to write the plugin against real APIs.
