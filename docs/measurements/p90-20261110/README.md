# P90 refresh / round-reset follow-up

A real perf.14 client reproduction on game #7 produced `CopyExistingEntity: missing client entity 368` at round start. Lifecycle capture identifies 368 as another player's pawn (`player`), not the P90. This does not by itself establish which mutation caused the client's snapshot failure.

The same capture demonstrates a concrete refresh defect: after each replacement, `MyWeapons` retains the destroyed full serials. Two full refreshes of knife/pistol/P90 leave six invalid inventory entries, which persist through round reset. Destruction alone does not detach an acquired item.

Native engine checks on CS2 build 10955 established:

| Retirement path | Old full serial in MyWeapons after removal |
| --- | --- |
| Entity Remove (perf.14) | Present, reproduced with a real client |
| Pawn RemovePlayerItem + entity Remove (perf.15) | Present; native regression failed, not accepted |
| Item-services DropActivePlayerWeapon + entity Remove | Absent; native isolated P90/pistol/knife experiment reduced inventory size 3 -> 2 -> 1 -> 0 |

Perf.16 uses the normal item-services drop path for each captured weapon before destroying it, including cleanup of failed acquired replacements. It retains the full-serial invalidity barrier before reissue. No raw inventory-vector compaction or manual entity construction is introduced. Resolving the client fatal still requires the complete production-refresh regression and a real-client round-reset retest with other player pawns present.

`NativeInventoryChecks.cs.txt` is a temporary, console-only, empty-server probe built separately against CSS API 1.0.376. It uses normal item-services grants and the actual production refresh method on a bot, checking P90+Deagle+knife and lone P90 inventories over 30 sequential refresh batches (100 coalesced requests per batch). It checks every raw inventory entry, fixed inventory size, removal of the old full serial, ownership, active/last, paint, ammunition, attack timing and drained queues. It has no native-client rendering coverage and must be removed after the test.

Private client dumps and raw operational logs stay outside this repository.

## Perf.16 client regression and perf.17 follow-up

Owner confirmed the P90 -> !wp twice -> round transition no longer crashed in that run, but the held P90 fell to the ground and had to be picked up. Perf.16 is not accepted. Real-client snapshots also retain invalid inventory entries after the full refresh; the earlier native per-definition/lone checks do not cover full multi-item retirement.

Perf.17 selects the intended victim as ActiveWeapon only for the synchronous native drop operation, verifies detachment before destruction, and restores an unrelated previously held weapon. This avoids dropping the held P90 when processing its knife/pistol companions. Failed detachment leaves attached items alive. The linked production retirement regression checks every held slot, every victim and all six victim orders with an active-only native adapter (125 assertions). Native full-refresh and owner client acceptance are still required.

Perf.17 native full refresh reached the LastWeapon assertion in phase0 with four live owned items, correct replacement set and no surviving dropped originals, but LastWeapon differed after native redeploy. It remains unaccepted. Perf.18 restores the resolved captured LastWeapon after completion of native redeploy, guarded by full player identity and the captured active selection.


## Final native inventory matrix on perf.18

The corrected matrix completed 24/24 cases and 2,400 coalesced requests on empty public6. It settles initial engine equipment before capturing the actual LastWeapon, then checks all six weapon grant orders, held P90/pistol/knife and pistol-only refresh while P90 stays held. Every case preserves four owned live items, active/last, P90 ammo/attack timing and the untouched smoke serial; all intended originals are invalid and no dropped original survives. Work queues drain. See `native-inventory-perf18.txt`, metadata and the linked probe source.

The original LastWeapon failure used a fabricated value overwritten by the engine before queued refresh and is not proof of a player-facing lastinv regression. Earlier paint44 fixtures also used one paint for incompatible gun definitions. The final probe uses actual catalogue entries (P90=1020, Deagle=37) and records fallback paint observations separately from inventory assertions. CSS NetworkedVector does not enumerate CEconItemAttribute; bot fallback values vary by multi-item case. Real-client P90 AND pistol skin rendering remains a required acceptance item. These observations are not reported as a native paint-retention pass.

The managed 125-assertion retirement regression and CI on source74fd6d8 pass, including existing MySQL integration. Public6 entry is reopened and original gamecfg/runtime cvars are restored. Real-client acceptance and merge remain pending. Public7 was occupied and remains on the older perf16 candidate; updating/restoring it still requires an empty window or explicit occupied-maintenance authorization.
