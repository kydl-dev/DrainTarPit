# Top 10 bugs (source pass, 0.6.8 code)

Severity: H = wrong gameplay state, M = lost tar / packaging, L = perf / hygiene. "Probable" = from reading the code, not reproduced.

| # | Sev | Where | Bug | Status |
|---|-----|-------|-----|--------|
| 1 | H | `TarLevel.cs` | The exit reset `SetLiquidLevel(-10000)` was filtered like a surface update. The exit callback runs in the same physics step as the FixedUpdate that wrote the last level, so it was swallowed and `m_tarLevel` stayed on the old surface height after leaving a pit. | Fixed 0.6.9 |
| 2 | M | `TarPool.Spawn` / `TarPits.EnsureRegistered` | The first deposit into a freshly spawned pool is sent in the same frame as `Instantiate`. If `LiquidVolume.Awake` ran before the ZDO existed, the RPC is not registered yet (retry is in `Update`) and that stroke's tar is lost. Probable, depends on component order. | Open |
| 3 | M | `TarFlow.Stroke` | A runtime error sets `ModSettings.TarFlow.Value = false`, which BepInEx writes to the config file. `PumpFx` was already changed to a runtime-only switch in 0.4.5; this one was missed. | Open |
| 4 | M | `Plugin.cs`, `manifest.json` | Version 0.6.5 in code and manifest, 0.6.8 in the changelog. Matters for `NetworkCompatibility(Minor)` and the package version. | Fixed 0.6.9 |
| 5 | M | `DrainTarPit.csproj` | `CopyDll` copies to `%USERPROFILE%\AppData\Roaming\com.kesomannen.gale\...` unconditionally. Any other machine or CI creates that folder tree or fails. Should use the `DeployDir` property. | Open |
| 6 | L | `TarPits.FixMissedHeights` | Holds `m_meshDataLock` while calling `GetGroundHeight` for up to 33x33 cells on every height pass; the pit's worker thread waits meanwhile. Sample first, lock only for the write. | Open |
| 7 | L | `TarPool.Landing` | `Physics.RaycastAll` allocates an array per 20 ms step, up to 250 per stroke (GC spikes while pumping continuously). Use `RaycastNonAlloc`. | Open |
| 8 | L | `TarFlow.MatchWindow` | A pour only claims a slug started less than 1.5 s ago. On a laggy host the drain reply arrives later, the claim fails and the pour plays immediately, out of sync with the slug. | Open |
| 9 | L | `TarPits` `LastDrainAt` / `LastDepositAt` | Per-sender dictionaries are never pruned (one entry per player id ever seen until restart). | Open |
| 10 | L | `DtpCommands.Spawn` | `ZNetScene.instance.GetPrefab` is not null-checked (NRE from the main menu); `Pits()` calls `TotalVolume` on volumes whose depth list may still be null. Cheat console only. | Open |

## Reported, fixed in 0.6.9

- **Stale liquid tracking** (`tracks you: True` at 18-20 m). Cause: a tar volume replaces its trigger mesh while pumped and Unity sends no exit for it. `TarPits.PurgeStale` now drops tar-surface entries more than 1.5 m outside the grid with the game's exit sequence (`Decrement`, `SetLiquidLevel(-10000)` at 0, remove from `m_inWater`). Also skips a repeated `OnTriggerEnter` for an object already tracked (it inflated the counter).
- **Pool depth.** `FillBasin` treated floor-valued cells (`-m_maxDepth`) as solid. For pump pools they are ground deeper than the rays reach and are now filled; `TarPool.MaxDepth` 6 -> 12. Vanilla pits keep the old rule (their `m_maxDepth` is 4, their real floor below that is unknown).

Not built or tested in game.
