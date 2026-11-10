# WeaponPaints optimization audit
Baseline: ba00df35cf0cf307474cbacfaa12e68a1408c651. Status: functional_accepted; synthetic_verified; full_tick_awaiting_live.

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

## MySQL acceptance completed
103 real-MySQL assertions and CI service added; report: measurements/mysql-20261110/REPORT.md. Full suite links production synchronization code with explicit engine adapters. Found/fixed sticker-slot compaction, failed-open cleanup, pet collation JOIN failure (PRIMARY index retained). Release is now3.3a-db6-perf.2. Remaining acceptance is native/client behavior in GAME-ACCEPTANCE.md and active-load/full-tick profiling; the earlier note that no MySQL integration exists is superseded.


## Final independent verification — 2026-11-10
Owner accepted all remaining gameplay scenarios on perf.11. Final perf.12 caches active preview recipients with SteamID/full-controller-handle checks. Production DLL informational source46576b6 (runtime code3220421) SHA256cceeee922f1209a9a9d77c813a54ada3203c2a640fbf3337246197f1484f10af. Release,22pet checks,103MySQL assertions and28actual catalogue/57876row checks pass in CI; cached-recipient native expiry/disconnect/serial tests pass.

New controlled8bot baseline/candidate/baseline,3x30s each, plus overhead control/refresh stress and623s continuous observation (4992requested refreshes). OnTick bytes1600→512 with unchanged HTML API call frequency; paint53496→3440 and native-owner lookup118368→13912bytes/call. Raw per-call percentiles, CPU/RSS/GC and scope-union timings are retained; no global CPU/full-tick/FPS gain accepted. Full FrameTotal unavailable on cold bot-only starts; entity spans/wall intervals are not full tick. SimpleAdmin data/admins.json changed independently and is an explicit confound for whole-process causality. Corrected queue inspection and release native checks pass; initial diagnostic assumptions are separately documented.

Task original3.3a-db6 restored after tests, SHA256640b78d7511a08dc2c4fc23782cced5f87511abe57bed1c2242c4ce2f706f610; runtime/config/dependencies restored/verified; diagnostics and24remote exports removed;0humans/0bots and0WP warnings/errors at final verification. Ready build/manifests/rollback outside webroot. See reviews/O5-WeaponPaints.md and docs/measurements/active-20261110/REPORT.md. Code is ready for review with bounded synthetic claims; full-frame real-client acceptance remains open. No merge/fleet rollout.
