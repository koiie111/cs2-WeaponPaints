# Stock catalogue generation

`stock.json` is generated from the separately supplied `items.json`, Russian translation map, decompiled `items_game.txt`, the site composition-capabilities.json, and the installed game's VPK index. Rebuild after updating CS2:

```powershell
python hud-mockup/tools/generate_stock_catalog.py --items <items.json> --translations <russian.ts> --items-game <items_game.txt> --capabilities <composition-capabilities.json> --site-skins <skins.json> --index <vpk-panorama-list.txt>
python hud-mockup/tools/check_stock_catalog.py --index <vpk-panorama-list.txt> --capabilities <composition-capabilities.json> --site-skins <skins.json>
```

The catalogue contains one record per supported economy item, including entries without artwork. `id`, `bank`, and `cell` encode the original economy identity; use `artBank` and `artCell` to select artwork. The `.art.bank-N.cell-M` rules refer only to resources verified in the input index. `resource: null` and `artKind: fallback` require one shared empty-image presentation. `embedded-sticker-icon` is the actual sticker inside a charm, not a rendering of its physical case. `tinted-spray-icon` uses the base spray icon and `tint-N`: the tint values are the `graffiti_tints` in items_game. The visual correspondence of Panorama `wash-color` to the game's spray rendering remains unverified in client.

The `capabilities.limits` object comes from the site composition catalogue for an exact weapon/paint pair; unknown or unrelated categories have no inferred composition permissions. The index hash is stored in JSON. The generated JSON should be read once and shared by the runtime, never parsed per player. CSS and JSON sizes, actual compiler limits and game-client performance must be assessed when integrating; class-count arithmetic alone cannot establish client performance. The generator contains no stock textures and does not package or deploy anything.
