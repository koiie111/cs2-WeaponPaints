# WeaponPaints optimization audit
Baseline: ba00df35cf0cf307474cbacfaa12e68a1408c651. Status: in_progress.

## Confirmed findings
- Events.OnTick: full player traversal and HTML allocation each tick for previews.
- Commands: old two-second preview timers can remove a newer preview; no disconnect cleanup.
- WeaponAction: repeated JObject scans on give; random paint sampled three times for one item.
- WeaponSynchronization: background reads publish slot dictionaries before load identity validation.
- Commands: glove worker mutates live slot state; saves read slot state after await.
- Events: spawn gate incorrectly couples music/agent/pin to knives/gloves.
- WeaponAction: ItemIDHigh shifts a uint by 32, which C# masks to zero.
- Pets.NativePets: repeated world enumeration per owner.

## Contracts and verification
Preserve commands/config/schema, per-tick HTML cadence, native give and entity callbacks, deferred knife paint, StatTrak, stickers, keychains and pets. Entity API remains on game thread. Background results require request/session/map/plugin identity checks before publication.

Implement independent commits; Release build and behavioral tests before pilot. One empty instance only, baseline/candidate/baseline, three 30-second windows per series. Restore original binaries/config after testing. No merge or fleet rollout. Synthetic and real-client acceptance are distinct.

References: https://docs.cssharp.dev/docs/features/game-events.html ; https://docs.cssharp.dev/docs/guides/referencing-players.html ; https://docs.cssharp.dev/api/CounterStrikeSharp.API.Server.html

## Implemented at 2026-11-10
- Cached catalog metadata/random lookup and coherent random paint attributes.
- Active preview entries with prebuilt HTML, expiry and SteamID; no preview timers.
- Async read snapshots published after request/SteamID/plugin-map generation validation; retained old refresh data on failure.
- Eager async save invocation and primitive skin snapshot before lock wait; agent capture before connection await.
- Native entity callback resolves serial handle; independent spawn functions and ItemID high bits fixed.
- Chicken handle inventory maintained on spawn/delete and recovered once; native owner is read fresh to preserve owner changes.
- Release warnings-as-errors and both portable suites pass. Synthetic catalog benchmark in docs/measurements.

## Continuation completed
- FIFO per-SteamID reservation shared by scalar saves, skin/StatTrak batches and player loads, before connection acquisition. Idle keys are removed; obsolete queued loads are skipped. Existing SQL skin locks and bounded retries remain.
- Deferred pin/glove/agent work validates controller serial, pawn, SteamID and map/plugin generation. One-shot references are removed and pending timers explicitly cleared at map end/unload.
- Reverse knife lookup and indexed skin-menu groups/images preserve first match and source order.
- Added tests for FIFO/failure cleanup, independent identities, controller/pawn/map invalidation, menu order and empty-image suppression.

## Acceptance remaining
Portable tests validate scheduling/identity predicates, not real MySQL transactions or native entity lifecycle. Real-client functional acceptance and active-load profiling remain open. Full tick/timers/SQL are not covered by the idle pilot. See measurements/pilot-20261110/REPORT.md. Keep PR Draft; no fleet rollout or merge.
