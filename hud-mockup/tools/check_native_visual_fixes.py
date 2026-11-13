"""Check the published v6 source contract without generating or editing assets.

This is a static regression check. It does not assert native rendering,
performance, successful Workshop delivery, or that an uploaded VPK matches.
"""
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "live-addon-v6"
sys.path.insert(0, str(ROOT / "tools"))
from pack_temporary_v6 import SOURCES, RESOURCES

expected_sources = {
    "addoninfo.txt",
    "panorama/layout/custom_game/weaponpaints_v6.xml",
    "panorama/styles/custom_game/weaponpaints_v6.css",
    "panorama/styles/custom_game/weaponpaints_stock_v6.css",
    "materials/weaponpaints/custom_v6.vtex",
}
expected_compiled = {
    "addoninfo.txt",
    "panorama/layout/custom_game/weaponpaints_v6.vxml_c",
    "panorama/styles/custom_game/weaponpaints_v6.vcss_c",
    "panorama/styles/custom_game/weaponpaints_stock_v6.vcss_c",
    "materials/weaponpaints/custom_v6.vtex_c",
}
assert SOURCES == expected_sources and RESOURCES == expected_compiled, "V6 packing allowlist changed"
actual_sources = {p.relative_to(SOURCE).as_posix() for p in SOURCE.rglob("*")
                  if p.is_file() and p.suffix in {".xml", ".css", ".vtex"}}
if (SOURCE / "addoninfo.txt").is_file():
    actual_sources.add("addoninfo.txt")
assert actual_sources == expected_sources, "V6 source set differs from five-resource allowlist"

layout = ET.parse(SOURCE / "panorama/layout/custom_game/weaponpaints_v6.xml").getroot()
includes = [n.get("src") for n in layout.findall("./styles/include")]
assert includes == ["s2r://panorama/styles/custom_game/weaponpaints_v6.vcss_c",
                    "s2r://panorama/styles/custom_game/weaponpaints_stock_v6.vcss_c"]
for button_id, icon_class in (("back", "backIcon"), ("search", "searchIcon")):
    buttons = layout.findall(f".//Button[@id='{button_id}']")
    assert len(buttons) == 1, f"Expected one {button_id} button"
    nodes = [n for n in buttons[0].iter() if icon_class in n.get("class", "").split()]
    assert len(nodes) == 1 and nodes[0].tag == "Panel" and nodes[0].get("hittest") == "false"
    assert nodes[0].get("text") is None, f"{icon_class} still uses a font glyph"

styles = SOURCE / "panorama/styles/custom_game"
main = (styles / "weaponpaints_v6.css").read_text(encoding="utf8")
stock = (styles / "weaponpaints_stock_v6.css").read_text(encoding="utf8")
main = re.sub(r"/\*.*?\*/", "", main, flags=re.S)
stock = re.sub(r"/\*.*?\*/", "", stock, flags=re.S)
assert not re.search(r"background-size\s*:\s*contains\b", main + "\n" + stock), "Retail-invalid contains value"
assert not re.search(r"\bbackground-size\s*:", stock), "Stock bank repeats image fitting"
rules = [(selector.strip(), body) for selector, body in re.findall(r"([^{}]+)\{([^{}]*)\}", main)]
art = [body for selector, body in rules if selector == ".art"]
assert len(art) == 1, "Expected exactly one shared .art rule"
assert re.search(r"background-size\s*:\s*contain\s*;", art[0])
assert re.search(r"background-position\s*:\s*50%\s+50%\s*;", art[0])
assert re.search(r"background-repeat\s*:\s*no-repeat\s*;", art[0])
for icon_class, resource in (("backIcon", "back.vsvg"), ("searchIcon", "search.vsvg")):
    matching = [body for selector, body in rules if selector == "." + icon_class and "background-image" in body]
    assert len(matching) == 1, f"Missing unique {icon_class} image rule"
    body = matching[0]
    assert f"s2r://panorama/images/icons/ui/{resource}" in body
    assert re.search(r"background-size\s*:\s*contain\s*;", body)
    assert re.search(r"background-repeat\s*:\s*no-repeat\s*;", body)
    assert re.search(r"background-position\s*:\s*center\s*;", body)
print("V6 source: shared retail fit, stock icons, and five-resource allowlist verified (static only)")
