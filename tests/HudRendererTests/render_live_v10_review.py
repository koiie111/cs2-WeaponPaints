"""Render stable V10 review PNGs and check that sidebar text and icons are painted.

Browser approximation only: native asset resolution still needs client testing.
"""
from pathlib import Path
import argparse
import importlib.util
import subprocess
import sys
import tempfile
from PIL import Image
from playwright.sync_api import sync_playwright

parser = argparse.ArgumentParser()
parser.add_argument("--only", choices=("all", "settings"), default="all")
parser.add_argument("--prefix", default="final-20261117")
args = parser.parse_args()
if not args.prefix.replace("-", "").isalnum():
    raise ValueError("Invalid preview filename prefix")

ROOT = Path(__file__).resolve().parents[2]
HUD = ROOT / "hud-mockup"
OUT = ROOT / "obj/v10-review"
spec = importlib.util.spec_from_file_location("preview", HUD / "tools/panorama_preview.py")
preview = importlib.util.module_from_spec(spec)
spec.loader.exec_module(preview)
browser = preview.find_browser(None)
if not browser:
    raise RuntimeError("Chrome or Edge required")
OUT.mkdir(parents=True, exist_ok=True)
with tempfile.TemporaryDirectory() as temp:
    subprocess.run([sys.executable, str(HUD / "tools/panorama_preview.py"),
                    str(HUD / "live-addon-v10/panorama/layout/custom_game/weaponpaints_v10.xml"),
                    "--states", str(HUD / "fixtures/states-v10.json"),
                    "--only", "home,home_compact,weapons,weapons_compact,settings,settings_compact",
                    "--out", temp], check=True)
    with sync_playwright() as pw:
        engine = pw.chromium.launch(executable_path=browser, headless=True)
        for size, width, height, suffix in (("16x9", 1600, 900, ""), ("4x3", 1280, 960, "_compact")):
            for state in (("home", "weapons", "settings") if args.only == "all" else ("settings",)):
                page = engine.new_page(viewport={"width": width, "height": height})
                page.goto((Path(temp) / f"weaponpaints_v10.{state}{suffix}.html").as_uri(), wait_until="load")
                page.evaluate("""async () => {
                    await document.fonts.ready;
                    const nodes = [...document.querySelectorAll('.navPicture, .backIcon, .searchIcon')];
                    await Promise.all(nodes.map(async node => {
                        const bg = getComputedStyle(node).backgroundImage;
                        const match = bg.match(/^url\\([\"']?(.*?)[\"']?\\)$/);
                        if (!match || !match[1].startsWith('file:')) throw Error('Missing preview icon: ' + bg);
                        const image = new Image(); image.src = match[1]; await image.decode();
                    }));
                    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                }""")
                side = page.locator('.side')
                if side.locator('.nav').count() != 8 or not side.locator('.brand').is_visible():
                    raise RuntimeError(f"Missing sidebar: {state} {size}")
                labels = [side.locator('.brand').bounding_box()]
                labels += [side.locator('.navText').nth(i).bounding_box() for i in range(8)]
                if any(box is None for box in labels):
                    raise RuntimeError(f"Missing sidebar label bounds: {state} {size}")
                png = page.screenshot()
                from io import BytesIO
                with Image.open(BytesIO(png)).convert('RGB') as image:
                    for box in labels:
                        crop = image.crop((int(box['x']), int(box['y']), int(box['x'] + box['width']), int(box['y'] + box['height'])))
                        if sum(1 for r, g, b in crop.getdata() if r > 100 and g > 110 and b > 110) < 5:
                            raise RuntimeError(f"Unpainted sidebar label: {state} {size}")
                # Replace atomically so reviewers never see a partially written PNG.
                target = OUT / f"{args.prefix}-{state}-{size}.png"
                if target.exists():
                    raise RuntimeError(f"Refusing to overwrite existing review PNG: {target}")
                staged = OUT / f".{args.prefix}-{state}-{size}.tmp"
                staged.write_bytes(png)
                staged.replace(target)
                page.close()
        engine.close()
print("V10 browser previews rendered; sidebar labels and icon files verified")
