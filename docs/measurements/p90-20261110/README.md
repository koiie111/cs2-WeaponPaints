# P90 refresh / round-reset follow-up

A real perf.14 client reproduction on game #7 produced `CopyExistingEntity: missing client entity 368` at round start. Lifecycle capture identifies 368 as another player's pawn (`player`), not the P90. This does not by itself establish which mutation caused the client's snapshot failure.

The same capture demonstrates a concrete refresh defect: after each replacement, `MyWeapons` retains the destroyed full serials. Two full refreshes of knife/pistol/P90 leave six invalid inventory entries, which persist through round reset. Destruction alone does not detach an acquired item.

Perf.15 calls the engine's `RemovePlayerItem` before destroying owned originals and failed acquired replacements. It keeps the serial-invalidity barrier before reissue. This corrects the demonstrated inventory defect; resolving the client fatal still requires native inventory checks and a real-client round-reset retest with other player pawns present.

`NativeInventoryChecks.cs.txt` is a temporary, console-only, empty-server probe built separately against CSS API 1.0.376. It uses normal item-services grants and the actual production refresh method on a bot, checking P90+AK47+knife and lone P90 inventories over 30 sequential refresh batches (100 coalesced requests per batch). It checks every raw inventory entry, fixed inventory size, removal of the old full serial, ownership, active/last, paint, ammunition, attack timing and drained queues. It has no native-client rendering coverage and must be removed after the test.

Private client dumps and raw operational logs stay outside this repository.
