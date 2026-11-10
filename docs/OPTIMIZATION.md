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
