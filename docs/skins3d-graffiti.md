# Graffiti integration (Draft)

ModuleVersion 3.3a-db7-skins3d.8, CounterStrikeSharp API >=376. The new
`Additional.GraffitiEnabled` flag defaults to false. Install the website SQL
`20261111_0001_skins3d_graffiti.sql` in the same cosmetics database, deploy the
matching plugin package including `data/graffiti.json`, then enable the flag.
Website PR10 contains the editor, transactional endpoint and catalogue generator.

Selection is global to the player, including its native tint; website state is
still a draft until Apply. Reconnect or !wp reloads it through the normal
identity/generation-guarded snapshot. !graffiti links to the website; !spray paints
onto a static surface within128 units. Default cooldown30s, configurable1..3600.
There is at most one plugin-created spray per player, removed on replacement,
disconnect and unload. Map transitions discard native handles and preserve cached
selection until the ordinary reconnect/load path replaces it. No per-tick SQL.

`ReplaceNativeGraffiti` is optional and defaults to false. It changes the kit/tint
of a native spray when enabled; it does not replace Steam's ownership/charge
behaviour. The plugin's !spray path creates a decal without spending inventory
items. QA must use !spray, never trigger the owner's consumable Steam graffiti.
No client key binding is changed automatically.

Behaviour reference: https://github.com/Letaryat/WeaponSkins at
cb561164bbd8dfdea5f5010feadc8892cc82463c, AGPL3. The implementation here is
independent CounterStrikeSharp code using the public game schema and trace API;
no SwiftlyS2 source is compiled or copied into this plugin. The native catalogue
comes from the existing pinned local cs2-lib/game export, build26445667.

Enabled graffiti and pins share one parameterized query with one result set; when pins
are disabled, only graffiti is read. This avoids a separate network round trip
without changing the ordered operation, connection, identity check or atomic
snapshot publication. The disabled graffiti path still supports an old schema
without its table.

Local validation: Release build with warnings as errors, eight regression
suites, 125 linked-source MySQL assertions (including graffiti load/reset,
pin fallback/override, disabled flags and slot reuse during the batched read),
12 website MySQL graffiti checks and browser draft/Apply/local-image workflow.
Owner accepted native spray image/colour and reconnect/repeat !wp on frozen .5
dbc8dcc. The native spray implementation is unchanged in .7. Map/cooldown and
additional client scenarios remain pending. Completed .5 and .6 CSSProfiler
comparisons found increased SQL queue/end-to-end tails on the isolated
shared-bot-key/SSH fixture. The .7 query now returns a single joined result set;
Release warnings-as-errors and all 125 MySQL assertions passed again. The focused
.7 SQL baseline/candidate/repeat finished with12 windows and12 p95/p99
comparisons: no threshold flags. Original server6 files/configuration were
restored; all288 runtime hashes match. This is not a distinguishable speedup,
production database latency, apply/refresh rerun or whole-build acceptance.
Raw profiles and tables live in website PR10 under
`docs/measurements/skins3d-20261111/server-v7-sql/`. The frozen .7 DLL was built
before source commit360ab5e, so assembly InformationalVersion retains5c4b918;
normalized compiled source was checked against360ab5e. ModuleVersion and actual
DLL SHA256ce4d70d70fad5bae1c5b9adfafe1b6ef63338b6c520680ed3bae6977feed3039
identify the measured artifact. Owner-only agent/patch/pet/!wp follows. GitHub Actions is
disabled for this repository (workflow dispatch HTTP422); local checks are not
reported as a CI pass. Old .4 profiles remain scoped to their own build.
Both PRs remain Draft. No fleet rollout or production schema changes performed.


## 4ill+ access (.8)

Plugin sprays and optional native spray overrides require an active4ill+
subscription. The existing joined pin/graffiti query also returns the Plus expiry
from plus_active, converted MSK->UTC; no extra SQL command is added. Every use
checks UTC expiry, so an expired cache cannot spray. Round subscription refresh
reuses the pet batch, or the existing patch batch when pets are off (including
agent-off/graffiti-on). Disconnect/config disable/unload clear the expiry cache.
Stored graffiti choices survive expiry/renewal. Ordinary Steam-owned graffiti is
not intercepted when the plugin override is unavailable.

Website read returns graffitiEnabled, nonzero writes require the current
subscription on the server, and clearing an inactive selection remains allowed.
The editor has a compact direct-console example: `bind "h" "css_spray"`.
No bind is sent to a client automatically. CounterStrikeSharp console commands:
https://docs.cssharp.dev/docs/features/console-commands.html

.8 Release warnings-as-errors and all eight regression suites pass;131
linked-source MySQL assertions include missing/expired/renewed/removed Plus,
UTC conversion, the actual per-use expiry gate and flag-off old schema support.
Website:18 real MySQL checks plus subscriber/expired browser Apply/retained-draft
and copy-button checks. Previous .7 profiles remain evidence for .7, not .8.
.8 has not been deployed or natively profiled. Server6 is restored with all288
runtime hashes unchanged after the owner session.
