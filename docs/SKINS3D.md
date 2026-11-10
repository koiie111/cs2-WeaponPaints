# Local website composition integration (Draft)

Website PR: https://github.com/koiie111/4ill.ru/pull/10.
This branch depends on the optimization PR #3 and keeps its lifecycle changes.

## Native attributes

- Every sticker writes its stored `Schema` as a uint bitcast in
  `sticker slot N schema`, independent of its X/Y offsets. Schema is the physical
  anchor; N is the draw-order slot. Do not substitute zero for moved stickers.
- Sticker display case uses keychain kit **37**. Its embedded sticker kit is
  written as a uint bitcast in `keychain slot 0 sticker`.
- Ordinary keychains explicitly write zero to that attribute to clear old slabs.

## Schema compatibility and rollout

The website's standalone SQL
`php/migrations/sql/20261110_0001_skins3d_slab.sql` adds
`wp_player_skins.weapon_keychain_sticker INT UNSIGNED NOT NULL DEFAULT 0`.
Run it once on the isolated test database before enabling website composition
writes. Existing five-field `weapon_keychain` strings remain unchanged.

Fresh plugin tables include the field. On existing databases the plugin treats
an absent field as zero and does not run an automatic ALTER. Loading the plugin
therefore does not substitute for applying the website SQL migration.

MySqlConnector is updated from 2.5.0 to 2.6.1 to address
GHSA-473q-m89c-ghf8. Include the matching dependency when packaging the plugin.

## Validation and remaining acceptance

Release warnings-as-errors build, managed optimization checks against the real
catalog, 22 pet lifecycle and 47 linked production preview checks pass locally.
The website Docker checks cover slab persistence, clearing and transaction rollback.

No native/game visual acceptance or CSSProfiler comparison has been performed.
Agent patches, complete material parity and refresh-wear behavior remain open.
Do not deploy or claim parity based on these managed tests alone. Use the approved
isolated empty-server test, baseline/candidate/baseline profiles and restore the
exact prior instance configuration after acceptance.
