# WeaponPaints Panorama — design fixtures

This is a native custom HUD layout and a fixture-driven visual study. It does not integrate with the plugin, save changes, initiate purchases, upload to Workshop or deploy to a server. The interactive-looking browser gallery is a simulation generated from the XML/CSS and fixture state; its substituted fonts, artwork and layout are approximate. It does not depict actual decoration placement.

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

The addon ships **one custom render texture**: the verified transparent website-editor render of AK-47 Дедал, not a video poster. All stock images point to client-mounted Valve `s2r://panorama/images/...` resources; no stock preview atlas is compiled or packed. The 109 curated mappings and local VPK evidence are in `fixtures/native-images.json` and `fixtures/native-image-proof.json`. Verify the recorded index again with `python hud-mockup/tools/check_native_images.py --index <vpk-index.txt>` after changing game builds. Two default-agent images and one default-glove image are explicitly representative because actual defaults depend on the game context. The pet fixture uses Valve's native `pet_chick.vsvg`; the SVG under `preview-assets/` is its decompiled browser-only counterpart.

Browser previews substitute exact decoded VPK images from `preview-assets/native/`, preserving the source dimensions and transparent padding; those files are not addon resources. Their SHA-256 values and VPK index provenance are in `fixtures/native-preview-proof.json`. Identity and custom-render source hashes are in `fixtures/asset-sources.json`. The custom editor render is a verified screenshot asset, not a poster from a gameplay video. Sticker/graffiti/patch identities and rarity metadata are from the local [cs2-lib](https://github.com/ianlucas/cs2-lib) dataset; rarity is applied only where identity matches. Weapon-model accents are category accents, not rarity. Rebuild the custom texture and source ledger with Pillow and `tools/refresh_assets.py --help`; it requires the source catalog, verified custom-editor render, cs2-lib image output and economy metadata. The price fixture is a read-only snapshot: `fixtures/custom-price.json` records the item, 150 ₽ price, public catalog and date verified. The locked panel is informational and does not open a checkout or perform a financial action.

The one custom source PNG is 640×384. BC7's base-level 4×4 block payload is 245,760 bytes; this excludes mipmaps, resource headers and engine overhead. Actual client memory, texture residency and in-game performance are unmeasured. Do not assume the full live catalog fits the entity's 1,024-name interning limits: phase 2 must bound or partition runtime class/ID/variable use and verify delivery.

## Local compilation and phase 2

`./hud-mockup/build.ps1 -Compile` copies current sources into its dedicated local content addon and forces resourcecompiler to rebuild only source-listed files, including dependencies. After compilation, `python hud-mockup/tools/pack.py --compiled-root <game/csgo_addons/cs2_4ill_weaponpaints_mockup>` builds a local VPK and SHA-256 manifest from an explicit source-derived allowlist; it never packages old addon tree files by recursion. The VPK v2 writer was adapted from the local `blue-ak-mam-20261111/vpk.py`. Neither script edits gameinfo, publishes, deploys or contacts a server. Retail addon delivery is unresolved.

For a later approved implementation, bind fixed IDs/dialog variables through PanoramaManager / `OnCustomHudClicked`, authorise every action, restore per-player state on restore, and block underlying actions while the `modal_layer`/`modal_shield` is active. Search and value entry require validated chat prompts. Apply must handle entitlement, stale state, errors and dirty Apply/Discard/Stay. Non-Plus players may browse but not apply. Custom skins require individual access and are selection-only; preserve the stock appearance and never attach decorations to them. Five sticker slots, three agent patch slots and charm/slab controls are fixture states, not live operations. The `4ill.ru/custom_skins` price panel does not act as a purchase button.

Before release, verify native s2r image resolution and fit, modal hit testing, viewport scales, game-client rendering and representative load/performance in CS2. No performance or in-game validation is claimed here.
