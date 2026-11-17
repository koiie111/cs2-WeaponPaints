# WeaponPaints Panorama HUD: integration status

This integration is under development. Do not deploy it as a finished HUD until
the remaining feature, refresh and performance acceptance work is complete.

`PanoramaHud.Enabled` defaults to `false`. When enabled, `css_whud` opens the
HUD and `css_whud_scale normal|compact|small` selects its scale for the current
session. The V10 layout, matching contract and stock stylesheet must be
available to the client. The V10 layout and installed-art changes have not been
accepted in the native client. The owner accepted the earlier V9 appearance and
observed Apply scenarios on 16 November 2026. A browser approximation is not
a native-client test.

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
later native operation cannot finish. Transient deferred refresh results use
bounded 1/2/4/5-second backoff until the phase deadline; terminal failures do
not retry and retries never repeat the committed SQL save.

`PanoramaHud.DiagnosticsEnabled` defaults to `false`. When enabled, server
console command `wp_hud_diag` reports a bounded sample of actual native drain
durations and write counts. The sampler runs only for pending HUD frames; it is
not an idle tick listener. Full performance and player-load acceptance remain pending.
The same command includes aggregate Apply stage/outcome counts, the latest
trace phase and validation reason, and separate store/CW/refresh timings. The
`last_refresh_failure_mask` bits are music=1, pet=2, agent=4, equipment=8,
pin=16, other/unknown=32; zero means no recorded subsystem failure. A count
does not prove a particular in-game appearance. Diagnostics do not include
player identities, item values, SQL text or raw error messages.

For opt-in main-thread timing, `wp_hud_timing summary`, `firstapply`, `max`
or `last` selects a bounded summary or individual click sample. The samples
are elapsed synchronous phases, not full-frame or CPU measurements; disabled
diagnostics perform no sampling.

Home and weapon-model artwork is captured into a per-session immutable index
from loaded player state and the CustomWeapons capability. Browsing reads the
index without SQL. Explicit T/CT views use that side; Both uses the current
player side without changing the browser selection. Confirmed custom selections
use matching renders; unknown or unavailable state falls back to a base image
rather than claiming an installed selection. The index refreshes after confirmed
refresh and invalidates when the published player snapshot identity or the
CustomWeapons generation changes. V10's
actual client appearance and these transitions still require native QA.

Remaining work includes broader controller/game-host coverage, feature behavior,
refresh confirmation across scenarios and full performance acceptance.
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

On 16 November the owner opened V7 and V8 in the native client. During the V8
trial the owner reported a successful AK-47 T-side apply and a successful
Glock apply for both teams with StatTrak, a third sticker and a charm. The owner
also reported intermittent cases where settings were saved but the visible item
was not updated; this remains unresolved and Apply is not accepted as complete.
After that trial eight scoped account differences were restored and server 11
returned to its original public configuration (final audit prefix `24982`). No
CSSProfiler measurement was taken during the V8 trial.

On 16 November the owner accepted V9 in the native client: the preview rarity
line, larger 4ill+ card above Apply, and custom AK, AWP and Glock previews.
A cold Glock T-side Apply and a subsequent both-team Apply with StatTrak, a
sticker and a charm succeeded. These observations do not establish complete
feature, refresh or load acceptance; the earlier intermittent V8 refresh issue
has not had its underlying cause established. V9 uses 19 custom editor-render
images; their compiled 512×308 BC7 textures total 582,499 bytes, not a client
memory measurement.

On 16 November, one 10-second instrumented window was recorded for each of HUD
closed, open and idle, and closed again, on the same process with the owner
present. The WeaponPaints `OnTick` wrapper had p95 inclusive elapsed times of
0.002496, 0.002205 and 0.002743 ms respectively. These are single windows of
synchronous wrapper timings, not CPU time, full-frame cost or client FPS. The
requested paging window contained no observed HUD-click callback and included
death events, so it is not a paging benchmark. Further matched measurements and
native acceptance are still required.

Preliminary 17 November instrumented measurements are documented in the
[measurement report](measurements/panorama-20261117/REPORT.md). Two paging windows and one successful Apply diagnostic have been recorded.
The associated Apply-window trace contains an unexplained 46.289049 ms HUD-click
wrapper peak; its button and cause are unknown. Three sampled windows have
been collected; further attribution and performance acceptance remain pending.

After the 17 November trial, the test server was verified restored to its
original public configuration. Temporary profiling and round-rule changes were
removed, pre-existing profiler logs preserved, and the two changed preference
fields restored.
