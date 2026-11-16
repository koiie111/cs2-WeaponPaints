# WeaponPaints Panorama HUD: integration status

This integration is under development. Do not deploy it as a finished HUD until
the native-client acceptance and controller review are complete.

`PanoramaHud.Enabled` defaults to `false`. When enabled, `css_whud` opens the
HUD and `css_whud_scale normal|compact|small` selects its scale for the current
session. The V8 layout and its matching contract and companion stock stylesheet must be
available to the client. A browser approximation is not a native-client test.

`PanoramaHud.CustomWeaponsIntegration` defaults to `required`. In that mode a
compatible, ready CustomWeapons HUD capability and matching database identity
are required for item editing and application. Set it to `disabled` only after
the operator has verified that CustomWeapons is absent; restart after changing
the integration mode. Do not use `disabled` alongside a legacy CustomWeapons
installation. The public bridge assembly must be installed once in the CSS
shared loader when the integration is used. `Reconciled` from the bridge means
that its database selection and cache agree; it is not proof of a live model.
The coordinated bridge contract is `customweapons:hud:v2` (assembly version 2);
the CustomWeapons and WeaponPaints builds must use the identical shared DLL.
The effective-selection getter is read-only and does not query SQL. The green
card marker uses confirmed per-team loadout caches and the effective CW choice,
not the preview; the both-teams filter labels a confirmed single-side match T or
CT, and shows the shared marker only when both sides match. Unknown
or deferred state has no marker. A successful refresh is not a visual inspection
of every game entity, and native acceptance remains necessary.

`PanoramaHud.SaveTimeoutSeconds` and `RefreshTimeoutSeconds` default to 30 and
are clamped to 5–90 seconds. After a committed save, refresh failures offer a
retry of reconciliation and native refresh only; they do not repeat the save.
For a two-team CustomWeapons selection, reconciliation confirms the player's
current team; the other team's selection remains saved for that team. Its live
application still requires native verification. A timeout does not prove that a
later native operation cannot finish.

`PanoramaHud.DiagnosticsEnabled` defaults to `false`. When enabled, server
console command `wp_hud_diag` reports a bounded sample of actual native drain
durations and write counts. The sampler runs only for pending HUD frames; it is
not an idle tick listener. Full performance and player-load acceptance remain pending.

Current known limitations include controller/game-host tests, real-client
layout validation, complete feature behavior and native confirmation of refresh.
Custom product and entitlement data is prefetched once in the background when
the session opens; navigation, search and page changes use that snapshot.
Transactions recheck access when applying. No performance claim is established
by offline tests.

Only graffiti and pets require an active 4ill+ subscription. Weapon finishes,
knives, gloves, agents, music, pins, stickers and charms are available without
4ill+. Custom models additionally require their own CustomWeapons access when
private. Mixed transactions containing graffiti or a pet require 4ill+ for the
whole transaction; the check is repeated inside the transaction before commit.

Historical native checkpoint, 13 November 2026: an isolated four-panel test confirmed
singular `background-size: contain` on this retail client. The earlier `contains`
spelling generated repeated parsing warnings and was removed from V6. The V6
home screen rendered with contained images and an opaque, bounded window at
4:3. That checkpoint did not accept the remaining screens or application flow.

Repeated client disconnections interrupted testing. A server connection record
identified `NETWORK_DISCONNECT_STEAM_LOGON (2009)`; its underlying cause and any
relationship to the test tooling remain undetermined. No real HUD Apply was
performed during that 13 November checkpoint.

On 16 November the owner confirmed that the V7 HUD and the AK-47 menu opened in
the native client and exercised a real Apply. The owner reported an unhelpful
deferred-application message and an invalid-parameters case. The subsequent V8
contract, DTO, validation and refresh-handshake fixes are source-only and have
not been deployed or accepted in a native client. Server 11 was restored to its
original public configuration and plugin binaries; the temporary profiler was
removed and the ten account changes made during testing were restored, with
StatTrak progress preserved.

On 16 November, one 10-second instrumented window was recorded for each of HUD
closed, open and idle, and closed again, on the same process with the owner
present. The WeaponPaints `OnTick` wrapper had p95 inclusive elapsed times of
0.002496, 0.002205 and 0.002743 ms respectively. These are single windows of
synchronous wrapper timings, not CPU time, full-frame cost or client FPS. The
requested paging window contained no observed HUD-click callback and included
death events, so it is not a paging benchmark. Further matched measurements and
native acceptance are still required.
