# Local website composition integration (Draft)

Website PR: https://github.com/koiie111/4ill.ru/pull/10.
Optimization PR #3 and the subsequent weapon-refresh PR #5 are merged. This
branch is rebased onto `bceb553` and retains their lifecycle and held-weapon fixes.

## Native attributes

- Every sticker writes its stored `Schema` as a uint bitcast in
  `sticker slot N schema`, independent of its X/Y offsets. Schema is the physical
  anchor; N is the draw-order slot. Do not substitute zero for moved stickers.
- Sticker display case uses keychain kit **37**. Its embedded sticker kit is
  written as a uint bitcast in `keychain slot 0 sticker`.
- Ordinary keychains explicitly write zero to that attribute to clear old slabs.
- Agent patches use the first three `m_vecPlayerPatchEconIndices` entries, with
  native patch-kit IDs and separate CT/T slots. The remaining native entries are
  cleared, preventing decorations from a previously selected agent from leaking.
- Patches require an active `plus_active` subscription. Convert its MSK expiry to
  UTC and remove patches at expiry; keep the saved choices for renewal. Missing
  subscription data disables patches while retaining the other cosmetics.
- Patch expiry timers are bound to SteamID, controller/pawn identity, map generation
  and entitlement version. Native patches on an ordinary Steam-owned agent are
  retained unless this plugin owns an override on that exact pawn. Renewal reuses
  the pet subscription batch, or a separate round batch when pets are disabled.

## Schema compatibility and rollout

The website's standalone SQL
`php/migrations/sql/20261110_0001_skins3d_slab.sql` adds
`wp_player_skins.weapon_keychain_sticker INT UNSIGNED NOT NULL DEFAULT 0`.
Run it once on the isolated test database before enabling website composition
writes. Existing five-field `weapon_keychain` strings remain unchanged.

Fresh plugin tables include the field. On existing databases the plugin treats
an absent field as zero and does not run an automatic ALTER. Loading the plugin
therefore does not substitute for applying the website SQL migration.

Apply website SQL `20261110_0002_skins3d_patches.sql` and
`20261110_0003_skins3d_format.sql` next. The latter adds an explicit composition
format version: pre-versioned rows preserve the old effective sticker anchor
(moved stickers used schema 0; unmoved stickers used their draw-slot anchor).
Version 1 rows always use the selected schema, including schemas above 4 where
the concrete weapon supports them. Do not rewrite legacy rows during installation.

MySqlConnector is updated from 2.5.0 to 2.6.1 to address
GHSA-473q-m89c-ghf8. Include the matching dependency when packaging the plugin.

The persisted weapon wear is unchanged. During a map, distinct sticker layouts
sharing paint, seed and rounded wear reserve nearby runtime wear steps so the
client regenerates its finish. The closest step in the same exterior is preferred;
repeated refreshes of the same layout do not accumulate wear changes.
Decorations and explicitly saved v1 defaults also apply when paint kit is zero.
Uncustomized legacy default rows retain their random-skin fallback behavior.

Deferred equipment work also binds to the captured team, since `SwitchTeam` can
keep the same pawn alive. Connect and hot-reload application retain a separate
busy lease after clearing the SQL marker, so `!wp` waits for native completion.
On unload, tracked agent overrides restore their original pawn model before the
tracker is discarded. When upgrading from an older build that did not restore
agents on unload, a pawn already wearing a plugin agent needs one respawn to
establish its original model for later resets.

`tests/fixtures/composition-roundtrip.json` matches the website fixture byte for
byte. It covers six physical anchors versus five draw slots, legacy anchors,
positions, degree rotation, scales, precision and the separate embedded slab kit.

## Validation and remaining acceptance

Release warnings-as-errors build, managed optimization checks against the real
catalog, 22 pet lifecycle, 47 linked production preview and 16 patch lifecycle
checks pass locally. The rebased candidate also passes 149 MySQL integration,
32 equipment scheduling, 125 retirement and 39 redeploy assertions.
The website Docker checks cover slab persistence, clearing and transaction rollback.

The current .12 matched-transport CSSProfiler campaign is incomplete: only the
first baseline was recorded before the owner ended testing for the day. It is
not a candidate comparison. See [the checkpoint](measurements/stattrak-20261112/README.md).
Native patch appearance, complete material parity and the remaining owner
lifecycle/performance acceptance remain open. Do not deploy or claim parity
based on managed tests or a baseline-only recording. Complete the approved
baseline/candidate/repeat-baseline protocol and restore the prior configuration.

## StatTrak persistence in .12

Background and disconnect saves merge the kill count with `GREATEST`; they do
not change an existing StatTrak preference. When a side-specific row must be
created, the writer copies the current database row for that side, or its team-0
fallback, including the complete name, stickers and keychain composition. It
uses that authoritative preference instead of the disconnected player's old
flag. With no persisted preference, a new counter row defaults to StatTrak off.

`!stattrak` explicitly saves only the active side's choice and a monotonic count.
It enters the same per-account FIFO queue and MySQL advisory lock as refresh
and background saves before returning to the engine. Commands during an
in-flight player-data load are ignored, so a pending snapshot cannot undo the
local toggle. Rapid toggles followed immediately by `!wp` retain their order.
Old schemas without the additive composition columns retain a legacy write
fallback; no schema migration is run by these changes.

149 real MySQL assertions cover stale true/false snapshots, live counts,
immediate reload, rapid toggles, team-0 fallback creation and old schemas.
These managed/SQL checks do not establish game-client or performance acceptance.
