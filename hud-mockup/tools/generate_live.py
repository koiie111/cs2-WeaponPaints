"""Build versioned live-layout sources from the approved mockup and stock bank.

This does not compile, publish, deploy, or modify the fixture addon.
"""

from pathlib import Path
import json
import argparse
import re
import shutil
import xml.etree.ElementTree as ET
from live_native_visual_fixes import explicit_image_fitting, replace_font_icons, shell_and_hero_overrides

ROOT = Path(__file__).resolve().parents[1]
MOCK = ROOT / "addon"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--version", choices=("2", "3", "4", "6", "7", "8"), default="8")
    args = parser.parse_args()
    version = args.version
    live = ROOT / ("live-addon" if version == "2" else f"live-addon-v{version}")
    layout_dir = live / "panorama/layout/custom_game"
    style_dir = live / "panorama/styles/custom_game"
    material_dir = live / "materials/weaponpaints"
    for directory in (layout_dir, style_dir, material_dir):
        directory.mkdir(parents=True, exist_ok=True)
    xml = ET.parse(MOCK / "panorama/layout/custom_game/weaponpaints.xml").getroot()
    if version in ("3", "4", "6", "7", "8"):
        replace_font_icons(xml)
    includes = xml.find("styles")
    if includes is None:
        raise ValueError("Approved layout has no stylesheet include")
    includes.clear()
    ET.SubElement(
        includes,
        "include",
        {"src": f"s2r://panorama/styles/custom_game/weaponpaints_v{version}.vcss_c"},
    )
    ET.SubElement(
        includes,
        "include",
        {"src": f"s2r://panorama/styles/custom_game/weaponpaints_stock_v{version}.vcss_c"},
    )
    if version in ("3", "4", "6", "7", "8"):
        sort = xml.find(".//Button[@id='sort']/Label")
        if sort is None:
            raise ValueError("Approved layout has no sort label")
        sort.set("text", "{s:sort}")
    if version in ("7", "8"):
        # Only the first wide tile carries the inter-row gap. The last small
        # tile has no trailing gutter; both rows then fit their parent exactly.
        for panel_id, css_class in (("category_pistols", "mosaicGap"), ("category_zeus", "mosaicLast")):
            panel = xml.find(f".//Button[@id='{panel_id}']")
            if panel is None:
                raise ValueError("Missing home mosaic tile: " + panel_id)
            panel.set("class", panel.get("class", "") + " " + css_class)
    if version in ("7", "8"):
        strip = xml.find(".//Panel[@id='decor_slots']")
        charm = xml.find(".//Button[@id='charm']")
        if strip is None or charm is None:
            raise ValueError("Missing decoration controls")
        strip.set("class", strip.get("class", "") + " decorStrip")
        for i in range(5):
            slot = strip.find(f"Button[@id='decor_{i}']")
            if slot is None:
                raise ValueError("Missing decoration slot")
            ET.SubElement(slot, "Panel", {"id": f"decor_art_{i}", "class": "decorThumb art", "hittest": "false"})
        charm.set("class", charm.get("class", "") + " charmValue")
        ET.SubElement(charm, "Panel", {"id": "charm_art", "class": "charmThumb art", "hittest": "false"})
    if version == "8":
        charm = xml.find(".//Button[@id='charm']")
        if charm is None:
            raise ValueError("Missing charm control")
        ET.SubElement(charm, "Label", {"class": "charmPlus", "text": "+", "hittest": "false"})
        footer = xml.find(".//Panel[@id='catalog_footer']")
        if footer is None:
            raise ValueError("Missing catalog footer")
        page = next((node for node in footer if node.tag == "Label" and "pageNumber" in node.get("class", "").split()), None)
        if page is None:
            raise ValueError("Missing page count")
        # Give the count the same 26px-high containing box as the two pager
        # buttons. Align its Label inside that box exactly like button labels,
        # rather than centring a Label against the taller padded footer.
        index = list(footer).index(page)
        footer.remove(page)
        box = ET.Element("Panel", {"class": "pageNumberBox", "hittest": "false"})
        page.set("class", "buttonText pageNumber")
        box.append(page)
        footer.insert(index, box)
    ET.indent(xml)
    (layout_dir / f"weaponpaints_v{version}.xml").write_text(
        ET.tostring(xml, encoding="unicode"), encoding="utf8"
    )
    # Runtime reset values come from the actual authored XML, not fixture examples.
    nodes = list(xml.iter())
    authored = {n.get("id"): n.get("class", "").split() for n in nodes if n.get("id")}
    if len(authored) != sum(1 for n in nodes if n.get("id")):
        raise ValueError("Duplicate layout panel ID")
    variables = sorted({name for n in nodes for value in n.attrib.values()
                        for name in re.findall(r"\{s:([A-Za-z0-9_-]+)\}", value)})
    buttons = sorted(n.get("id") for n in nodes if n.tag == "Button" and n.get("id"))
    contract = {"version": 1, "authoredClasses": authored, "variables": variables, "buttons": buttons}
    (ROOT / f"catalog/hud-live-contract-v{version}.json").write_text(
        json.dumps(contract, ensure_ascii=False, indent=2) + "\n", encoding="utf8"
    )
    css = (MOCK / "panorama/styles/custom_game/weaponpaints.css").read_text(
        encoding="utf8"
    )
    css = css.replace(
        "materials/weaponpaints/custom.vtex", f"materials/weaponpaints/custom_v{version}.vtex"
    )
    if version in ("3", "4"):
        css, _ = explicit_image_fitting(css)
        css += "\n" + shell_and_hero_overrides()
    elif version in ("6", "7", "8"):
        # Retail MINI v5: `contain` fits; `contains` caused warning floods.
        # Keep background fitting on ONE shared .art rule, never 14k copies.
        original = ".art { background-size: contains; background-repeat: no-repeat; background-position: center; }"
        replacement = ".art { background-size: contain; background-position: 50% 50%; background-repeat: no-repeat; }"
        if css.count(original) != 1:
            raise ValueError("Unexpected approved .art rule")
        css = css.replace(original, replacement)
        css += "\n" + shell_and_hero_overrides().replace("background-size: contains;", "background-size: contain;")
        if "background-size: contains" in css:
            raise ValueError("Invalid retail contains value remains")
    if version in ("4", "6", "7", "8"):
        css += "\n" + (ROOT / "tools/live_native_modal_v4.css").read_text(encoding="utf8")
    if version in ("7", "8"):
        old = ".mosaic.wide { width: 555px; height: 224px; margin-right: 0px; margin-bottom: 12px;"
        new = ".mosaic.wide { width: 555px; height: 224px; margin-right: 0px; margin-bottom: 0px;"
        if css.count(old) != 1:
            raise ValueError("Unexpected home mosaic styling")
        css = css.replace(old, new)
        css += """
.mosaic.wide.mosaicGap { margin-bottom: 12px; }
.mosaicBottom .mosaic.mosaicLast { margin-right: 0px; }
.item.selected .itemSub { color: #56d99b; font-size: 12px; font-weight: bold; }
.decorStrip .decorSlot { width: 102px; height: 50px; }
.decorStrip .decorSlot.hasItem .buttonText { visibility: collapse; }
.decorThumb { width: 52px; height: 38px; horizontal-align: center; vertical-align: center; }
.charmThumb { width: 32px; height: 22px; vertical-align: center; }
.charmValue .buttonText { margin-left: 36px; width: 166px; text-overflow: shrink min( 9px ) ellipsis; }
"""
    if version == "8":
        css += """
.pageNumberBox { width: 70px; height: 26px; }
.pageNumberBox .pageNumber { horizontal-align: center; vertical-align: center; }
.charmPlus { visibility: collapse; horizontal-align: center; vertical-align: center; font-size: 15px; color: #cde4ef; }
.charmValue { padding: 0px; }
.charmThumb { margin-left: 8px; }
.charmValue .buttonText { margin-left: 44px; }
.charmValue.empty .buttonText { visibility: collapse; }
.charmValue.empty .charmPlus { visibility: visible; }
"""
    # The live controller needs a visible current slot and page bounds.
    # These native Panorama rules intentionally do not alter the approved mockup.
    css += "\n.decorSlot.active { border: 1px solid #79c7ef; }\n.page.disabled { opacity: 0.45; }\n.app.rendering .main { opacity: 0; }\n"
    (style_dir / f"weaponpaints_v{version}.css").write_text(css, encoding="utf8")
    stock = (ROOT / "catalog/stock.css").read_text(encoding="utf8")
    if version in ("3", "4"):
        stock, _ = explicit_image_fitting(stock)
    (style_dir / f"weaponpaints_stock_v{version}.css").write_text(stock, encoding="utf8")
    shutil.copyfile(
        MOCK / "materials/weaponpaints/custom.png", material_dir / f"custom_v{version}.png"
    )
    descriptor = (MOCK / "materials/weaponpaints/custom.vtex").read_text(
        encoding="utf8"
    )
    descriptor = descriptor.replace(
        "materials/weaponpaints/custom.png", f"materials/weaponpaints/custom_v{version}.png"
    )
    (material_dir / f"custom_v{version}.vtex").write_text(descriptor, encoding="utf8")
    shutil.copyfile(MOCK / "addoninfo.txt", live / "addoninfo.txt")
    print("Built versioned source addon:", live)


if __name__ == "__main__":
    main()
