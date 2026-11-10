# Held knife type change: perf.11

Source: 1010d3a. Owner accepted immediate visible gun/knife paint on perf.8, but changing the active knife type left empty hands until switching away and back. Hat death cleanup, single wp response and prior gameplay acceptance stand.

## Change
Restoring ActiveWeapon by handle does not request the client holster/deploy sequence. After a held knife changes type, wait for deferred subclass/paint, select an already-owned primary/pistol, then request slot3 after 0.1s. If the inventory contains only the knife, expose a holstered active handle before requesting slot3; restore fake-client state explicitly because bots do not execute client console commands. Map/unload cleanup also restores an outstanding knife-only holster when serial/owner/controller still match.

Each phase rechecks connection/controller/pawn/lifecycle, knife serial and ownership. A different active item cancels the return. The request remains coalesced until completion, including selections arriving during the delay. Follow-ups preserve the selected definition filter. No dummy weapon is created; removal confirmation remains the barrier before any normal replacement grant. Knife-menu changes filter to knives, including default knife IDs 42/59 which are absent from the custom catalog. Default knife selection is supported. Gun clips/reserves, attack timing and active/last slots remain covered by the prior replacement path.

The client command behaviour follows the [CSS controller API](https://docs.cssharp.dev/api/CounterStrikeSharp.API.Core.CCSPlayerController.html); server-context commands do not stand in for client slot selection. Native fixture state cannot prove client viewmodel rendering. The related upstream [knife timing report](https://github.com/Nereziel/cs2-WeaponPaints/issues/531) is contextual evidence, not proof for this server.

## Executed checks
- Release with warnings as errors: pass. Existing 22 pet checks and optimization/FIFO/identity/menu checks: pass. Both GitHub CI builds on source1010d3a pass, including103 real-MySQL assertions: [push run](https://github.com/koiie111/cs2-WeaponPaints/actions/runs/42809118780), [PR run](https://github.com/koiie111/cs2-WeaponPaints/actions/runs/42809127860).
- Empty server #7; item-services fixtures only, no raw weapon DispatchSpawn.
- Two batches of 50 gun refresh requests: exactly one AK, old serial invalid, paints44/180, ammo/primary attack timing and active slot retained, exactly one world AK.
- Four knife targets 507/515/526/42: 100 requests immediately plus 50 interleaved during the pending operation for each target. Exactly one owned knife; old serial invalid; correct definition and active handle; queue empty at inspection. Primary handle and ammo retained by knife-only refresh. Includes knife-only inventory and default knife.
- Death during a queued knife redeploy clears the request. Hat cleanup/schema offsets/idempotent16commands retain previous checks.
- The initial perf.9 fixture was rejected: its knife filter used catalog lookup for default ID42, absent from the custom-only dictionary. Fixed in the final candidate; those rejected results are not acceptance evidence. Direct GiveNamedItem("weapon_knife_karambit") returned null in a separate safe probe; not used in production.

No new tick listener or persistent world/inventory scanner. One bounded one-shot timer only for active knife TYPE changes; paint-only refresh does not use the redeploy timer. These checks establish bounded behaviour and inventory invariants, not a measured timing improvement or p95/p99 acceptance.

## Owner retest
On #7, hold a knife and use !knife to change types several times (e.g. Karambit, Butterfly, Kukri, Default). The knife must appear without manual weapon cycling. Repeat while holding a pistol: selecting a knife type should leave the pistol selected. Check Q after the operation, paint-only change, and rapid selections without extra world/inventory items. If practical, repeat with only a knife in inventory. Client rendering/animation remains OPEN until owner acceptance; PR stays Draft.

Candidate is intentionally left on #7 for the authorized owner test. No fleet rollout or merge. Final cold-start/hash/config/error checks are in final-verification.json. Diagnostics and test bots are removed; restart clears synthetic ground drops.

## Artifact / rollback
Local DLL/deps/PDB and SHA256 manifest: C:/Users/koiie/.codex/tasks/wp-release-perf11-20261110. Full install package remains available from the PR CI build artifact; dependencies/runtime/config/gamedata were not changed on #7.

Restore only when #7 is empty: unload WeaponPaints, restore the private original DLL C:/Users/koiie/.codex/tasks/wp-owner-check-20261110/server7-original.dll to the existing WeaponPaints plugin directory, restart only LinuxGSM cs2_7, verify original SHA256 640b78d7511a08dc2c4fc23782cced5f87511abe57bed1c2242c4ce2f706f610, exact LOADED version and healthy status. Original configuration is unchanged. Do not replace configs/SQL tables or restart the host.
