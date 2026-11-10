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

## Required remaining work (not accepted)
- Ordered persistence for all per-SteamID feature writes, reconnect read-after-write and failure/retry regression coverage. Current skin-only locks are retained, not a complete persistence ordering solution.
- Full deferred callback/timer lifecycle audit, menu/reverse-knife indexes and session/map race integration tests.
- Chicken native lifecycle acceptance, slow-query races, stickers/keychains and all feature-flag combinations.
- Server baseline/candidate/baseline, full-path percentiles/allocations and real-client visual acceptance. No candidate server installation performed.
Read-only #7 check: 0 humans/0 bots, 61 plugin slots, original WP 3.3a-db6 DLL SHA256 640b78d7511a08dc2c4fc23782cced5f87511abe57bed1c2242c4ce2f706f610. Other plugin state is changing (Advertisement unloaded); recapture a stable manifest before any comparison.
