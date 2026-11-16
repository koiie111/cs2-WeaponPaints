# Native inventory defaults

Stacked on PR #7 (`codex/weaponpaints-panorama-hud`, 1359cf6). No deployment or merge.

Ordinary weapons with paint 0 use the native item, even when legacy decoration fields are populated. A chosen nonstandard knife model with paint 0 remains an explicit bare-model override. Knife/glove resets mask team-0 choices. All native economy writes are gated before item identity changes. Snapshots retain both attribute vectors as managed values, including raw float payloads and original identity. Unknown attribute IDs refuse customization rather than erase an attribute we cannot restore. Dropped items cannot adopt another player's snapshot. Virtual counters require a tracked override.

The snapshot attribute-name map covers the attributes currently exercised by the native probe plus finish attributes. This is intentionally fail-closed; unknown attributes are logged. Snapshot state is in memory and does not survive reloads. A missing baseline cannot be reconstructed from a previously modified entity: a reset is deferred until fresh native issuance. The existing refresh path carries baselines across its replacements.

Verification: Release build has zero warnings/errors. 17 new linked snapshot/policy tests and 8 StatTrak event checks pass. Existing managed composition, equipment, redeploy, retirement, command refresh, network, HUD browser/controller/state/renderer/cosmetic/custom bridge, agent, pet, finish cache, optimization and preview checks pass. HUD persistence and native-probe projects compile. A separate disposable MySQL 8.4.6 instance on localhost:33383 passed 160 production-SQL assertions and 109 HUD persistence assertions, including native glove zero-model defaults. Both isolated schemas were removed by the harnesses and the temporary MySQL process was shut down. No native probe was loaded and no actual Steam inventory or client was modified.

## Required before ready for review / deployment

- Run disposable MySQL/HUD persistence scenarios including exact team vs team 0 and stale counter writes. Do not run fixtures against production data.
- On an isolated CS2 server, use a real client's ordinary and StatTrak inventory items: buy, override, reset, rebuy, respawn, reconnect; verify original item ID, paint, wear, name, accessories and counter display.
- Repeat for native knives and gloves, selected bare karambit, T/CT, pickup/drop and reset during pending saves. Check animation/model mesh and delayed subclass updates visually.
- Verify unknown-attribute refusal and missing-baseline feedback after a plugin reload.
- The source-linked NativeProbe adapter builds, but its existing override-oriented scenarios are not proof of native restoration. New client-driven acceptance is still required.
- Track PR #7 updates; rebase and retarget only after its merge. Website automatic refresh additionally depends on PR #8 and its explicit enable flag.

## Manual client handoff

Owner allowed any empty server except #11. A2S and RCON confirmed #17 (109.195.19.84:27022) had 0 humans / 0 bots before staging. Original full WeaponPaints folder and config are backed up outside autoload at /root/native-inventory-defaults-20261116/server17 on saratov. Candidate DLL SHA256: 528a1e105cf85d554049df56b68fb3d9f8026660dbc1aced0556c79f1c06e169. Temporary unload/load succeeded; loaded version 3.3a-db7-skins3d.14-wp-refresh.hud.1. No game restart, database modification, site deployment or #11 operation was performed. The client UI was handed back after the owner stopped Computer Use. Real Steam-inventory visual acceptance is pending owner testing. Restore originals after acceptance.
