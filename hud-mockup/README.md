# WeaponPaints Panorama — design fixtures

This directory contains the native fixture layout and the V10 live-resource generator. The fixture gallery does not save changes, initiate purchases, upload to Workshop or deploy to a server; the separate opt-in plugin integration is described in [the runtime guide](../docs/panorama-hud-runtime.md). The interactive-looking browser gallery is a simulation generated from the XML/CSS and fixture state; its substituted fonts, artwork and layout are approximate. It does not depict actual decoration placement.

## Generate and inspect

From the repository root, with Python 3.10+:

```text
python hud-mockup/tools/generate.py
python hud-mockup/tools/check.py
python hud-mockup/tools/check_geometry.py
python hud-mockup/tools/gallery.py --png
```

Geometry checks and PNGs additionally require Python Playwright (`pip install playwright`) and locally installed Edge or Chrome. Open `hud-mockup/previews/index.html` via file://. Use `--only home,weapons,finishes,settings` or `--size 1600x900` to narrow rendering. HTML and extra PNGs are ignored by git. The HTML renderer is adapted from [panorama-hud-preview](https://github.com/koiie111/panorama-hud-preview) at `9fa28b3f7acf92c1aafa14d579d6d300fe71a767`; it uses Bahnschrift instead of Stratum2 and implements only a subset of Panorama. Browser checks are not proof of in-game rendering.

`tools/generate.py` generates the native XML/CSS, fixture states and JSON/C#-text ID contracts together; the `.cs.txt` contract is outside compilation. `tools/check.py` runs the bundled CS2 Panorama skill validator and checks the bounded contract. `tools/check_geometry.py` checks browser label and glyph alignment, spacing, long-name containment and fixture identities across the base states and checks three principal screens at five viewport sizes.

## Images and sources

The original fixture addon ships one custom render texture. The live V10 source uses **19 custom editor-render textures**, including AK-47 Дедал; these are website-editor renders, not video posters. All stock images point to client-mounted Valve `s2r://panorama/images/...` resources; no stock preview atlas is compiled or packed. The 109 curated mappings and local VPK evidence are in `fixtures/native-images.json` and `fixtures/native-image-proof.json`. Verify the recorded index again with `python hud-mockup/tools/check_native_images.py --index <vpk-index.txt>` after changing game builds. Two default-agent images and one default-glove image are explicitly representative because actual defaults depend on the game context. The pet fixture uses Valve's native `pet_chick.vsvg`; the SVG under `preview-assets/` is its decompiled browser-only counterpart.

Browser previews substitute exact decoded VPK images from `preview-assets/native/`, preserving the source dimensions and transparent padding; those files are not addon resources. Their SHA-256 values and VPK index provenance are in `fixtures/native-preview-proof.json`. Identity and custom-render source hashes are in `fixtures/asset-sources.json`. The custom editor render is a verified screenshot asset, not a poster from a gameplay video. Sticker/graffiti/patch identities and rarity metadata are from the local [cs2-lib](https://github.com/ianlucas/cs2-lib) dataset; rarity is applied only where identity matches. Weapon-model accents are category accents, not rarity. Rebuild the custom texture and source ledger with Pillow and `tools/refresh_assets.py --help`; it requires the source catalog, verified custom-editor render, cs2-lib image output and economy metadata. The price fixture is a read-only snapshot: `fixtures/custom-price.json` records the item, 150 ₽ price, public catalog and date verified. The locked panel is informational and does not open a checkout or perform a financial action.

The 19 live custom images are 512×308 BC7. Their V9 compiled textures totalled 582,499 bytes; this is not a V10 runtime memory measurement. Actual client memory, texture residency and in-game performance are unmeasured. The generated stock bank bounds the image class vocabulary; validate the combined entity budget again after changes.

## V10 live sources and local compilation

Generate and validate the versioned source tree without changing V9:

```text
python hud-mockup/tools/generate_live.py --version 10
python hud-mockup/tools/validate_panorama.py hud-mockup/live-addon-v10
python hud-mockup/tools/check_live_contract.py hud-mockup/live-addon-v10 --version 10
```

For a fresh dedicated local addon, compile with `./hud-mockup/build.ps1
-SourceRoot <absolute-live-addon-v10> -Addon <fresh-addon-name> -Compile`.
The script defaults to the original fixture addon when `-SourceRoot` is omitted.
Pack with `python hud-mockup/tools/pack_temporary_v10.py --compiled-root
<game/csgo_addons/fresh-addon-name> --out <fresh-output.vpk>` and independently
verify its manifest and archive. The source allowlist includes only layout,
styles and the 19 custom textures; stock art remains game-provided. The VPK
writer is adapted from `blue-ak-mam-20261111/vpk.py`. These scripts do not
publish or deploy. See [the acceptance procedure](../docs/weaponpaints-hud-acceptance.md)
for delivery and restoration requirements.

V10 uses a fixed panel pool and bounded stock-image classes. Home and weapon
model cards read a session-cached, team-aware representation of confirmed loaded
selections; unknown state falls back to base artwork. Navigation does not query
SQL. V10 has not been accepted in the native client. Check viewport scales,
icon rendering, input, image identity and refresh behavior in game; browser
previews and offline tests do not establish performance or native correctness.
