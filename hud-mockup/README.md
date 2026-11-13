# WeaponPaints Panorama: stage 1

Native custom HUD layout and fixture-driven design study only. No plugin integration, purchases, saving, Workshop upload or server deployment is included. Review the native source and three PNGs in `previews/`. The browser gallery is a simulation: fonts and Panorama layout differ, and it does not represent actual decoration positions.

## Reproduce

From the repository root, with Python 3.10+:

```text
python hud-mockup/tools/generate.py
python hud-mockup/tools/check.py
python hud-mockup/tools/gallery.py --png
```

Python 3.10+ is required for generation and validation. PNG capture and geometry checks additionally require Playwright for Python (`pip install playwright`) and a locally installed Edge or Chrome; refreshing source images requires Pillow. Run `python hud-mockup/tools/check_geometry.py` for the five-viewport browser approximation check. Open `hud-mockup/previews/index.html` via file://. Optional `--only home,finishes,settings` and `--size 1600x900` limit rendering; if home is not selected open a linked state. PNG/HTML previews other than the three reference PNGs are ignored by git. Native XML, CSS, fixture states, JSON and C#-text ID contracts derive from `tools/generate.py`; the C# text is deliberately outside compilation. The renderer was adapted from [panorama-hud-preview](https://github.com/koiie111/panorama-hud-preview) at `9fa28b3f7acf92c1aafa14d579d6d300fe71a767`; it substitutes Bahnschrift for Stratum2 and translates a subset of Panorama CSS. Generated screenshots are not proof of in-game layout.

Frozen atlas PNGs derive from the website catalog images listed with source paths, IDs and SHA-256 in `fixtures/asset-sources.json`. To refresh them intentionally, install Pillow and run `python hud-mockup/tools/refresh_assets.py --source <website-public-root> --catalog <skins5-data>/skins.json`, then regenerate. The three large atlas banks use 640×384 cells; eight auxiliary banks use 320×192 cells, each in a 5×2 layout. Together their BGRA8888 pixel planes total 49,152,000 bytes (46.875 MiB) before mipmaps and engine overhead; the actual client footprint and performance are unmeasured. These are curated fixture banks, not a full catalog. Custom-skin images are joined by exact model key to the local previews mapping; the pet image is explicitly an illustration. Missing graffiti images are intentionally not invented. To refresh the optional custom bank, also pass `--custom-root <skins3d-public-root> --custom-map <custom_skins>/previews.json`. The neutral rarity stripe is a placeholder: the source catalog does not provide rarity metadata.

For local resource compilation only, run `./hud-mockup/build.ps1 -Compile` with Workshop Tools installed. It copies into its dedicated `cs2_4ill_weaponpaints_mockup` content addon and produces game addon resources; it does not edit gameinfo, install overrides, publish or contact servers. Client delivery and real-game rendering remain unverified.

## Phase 2 contract

The bundled `tools/validate_panorama.py` and `references/panorama-css-reference.txt` are from the local `cs2-panorama-hud` skill; its validator has only UTF-8 read adjustments here. The JSON contract and `Contract.cs.txt` expose fixed IDs and dialog variables; map clicks through PanoramaManager / `OnCustomHudClicked`, bind state per player, and reapply it on restore. Each selector must authorise access at action time. Search, text and numeric entry require a chat prompt; no native text entry is present. Apply must validate all values server-side and handle stale versions, errors and dirty Apply/Discard/Stay transitions. Non-Plus players may browse but cannot apply; the Plus link action should post `4ill.ru/plus` in chat. Custom skins require their own individual access check and are selection-only; preserve the existing stock appearance and never apply decorations to them. Fixture buttons do nothing.

The selected item preview is a static catalog image, not a 3D preview or a promise of decoration placement. Five sticker slots and three agent patch slots are represented; the charm view includes a slab choice, and pet options are fixture values. Before release, validate every viewport including 16:9, 16:10 and 4:3 in CS2, verify compact `ui-scale` behavior, visual fidelity, class/variable interning, action routing, permission and conflict handling, and profile frame/network cost with representative concurrent viewers. No performance measurements or game-client validation have been made. Retail addon delivery must be established separately; local compilation is not a shipping method.
