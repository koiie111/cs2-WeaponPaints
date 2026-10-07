# Changelog

## 3.3a-db3 — 2026-10-07

Merged Nereziel/cs2-WeaponPaints through `6b1039d63042b0bbadbf142054c86f79284fdb6a`.

- Updated Windows/Linux attribute signatures and CounterStrikeSharp.API to 1.0.376; the plugin now targets .NET 10 and requires a matching server runtime.
- Initialized weapon synchronization during configuration so cold starts work without reloading the plugin.
- Added native hook cleanup on unload, null/entity checks, and explicit configuration/gamedata load failures. Removed unused memory patch helpers.
- Updated item data, images, and translations from upstream.
- Preserved this fork's batched MySQL writes, lock/retry handling, player-load tracking, disconnect snapshots, and weapon refresh without recreating entities. Conflicting upstream delayed refresh callbacks were superseded by the fork's existing player identity checks.
- Built once per branch push, pull request to main, or manual workflow run; uploaded plugin/website ZIP artifacts and published the same archives on pushes to main. Plugin archives extract directly into `game/csgo/`.

Validation: Release build passed without warnings or errors; 207 JSON files parsed successfully; actionlint passed; the GitHub Actions build and packaging job passed. CS2 server behavior still requires an in-game check.
