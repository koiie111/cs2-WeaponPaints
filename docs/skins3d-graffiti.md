# Graffiti integration (Draft)

ModuleVersion 3.3a-db7-skins3d.7, CounterStrikeSharp API >=376. The new
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
Release warnings-as-errors and all 125 MySQL assertions passed again. A focused
matched SQL profile is required before accepting this change. These measurements
do not describe production database latency. GitHub Actions is
disabled for this repository (workflow dispatch HTTP422); local checks are not
reported as a CI pass. Old .4 profiles remain scoped to their own build.
Both PRs remain Draft. No fleet rollout or production schema changes performed.
