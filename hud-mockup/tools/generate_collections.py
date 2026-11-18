"""Compose the opt-in Collections layout using the existing V10 resources."""

import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT / "live-addon-v10/panorama/layout/custom_game/weaponpaints_v10.xml"
LAYOUT = ROOT / "live-addon-v11/panorama/layout/custom_game/weaponpaints_v11.xml"


def panel(parent, css, name=None, tag="Panel"):
    node = ET.SubElement(parent, tag, {"class": css})
    if name:
        node.set("id", name)
    return node


def label(parent, text, css):
    ET.SubElement(parent, "Label", {"class": css, "text": text, "hittest": "false"})


def button(parent, name, text, css="wcButton"):
    node = panel(parent, css, "wc_" + name, "Button")
    label(node, text, "wcButtonText")
    return node


def art(parent, css, name):
    panel(parent, css + " art", "wc_" + name).set("hittest", "false")


def main():
    root = ET.parse(BASE).getroot()
    ET.SubElement(root.find("styles"), "include",
                  {"src": "s2r://panorama/styles/custom_game/weaponpaints_collections_v11.vcss_c"})
    sidebar = root.find(".//Panel[@class='side']")
    nav = panel(sidebar, "nav", "nav_collections", "Button")
    icon = panel(nav, "navIconRail")
    icon.set("hittest", "false")
    panel(icon, "wcNavIcon").set("hittest", "false")
    label(nav, "КОЛЛЕКЦИИ", "navText")
    pane = panel(root.find(".//Panel[@class='main']"), "pane wcPane", "wc_pane")
    tabs = panel(pane, "wcTabs")
    for key, title in (("mine", "Мои коллекции"), ("feed", "Мастерская"), ("crafts", "Крафты")):
        button(tabs, key, title, "wcButton wcTab")
    tools = panel(pane, "wcTools", "wc_tools")
    for key in ("type", "search", "sort"):
        button(tools, key, "{s:wc_" + key + "}", "wcButton wcFilter")
    label(pane, "{s:wc_summary}", "wcSummary")
    body = panel(pane, "wcBody")
    rows = panel(body, "wcRows")
    for i in range(6):
        row = panel(rows, "wcRow", f"wc_row_{i}", "Button")
        art(row, "wcThumb", f"thumb_{i}")
        copy = panel(row, "wcRowCopy")
        label(copy, f"{{s:wc_title_{i}}}", "wcRowTitle")
        label(copy, f"{{s:wc_meta_{i}}}", "wcMeta")
    footer = panel(rows, "wcFooter")
    button(footer, "prev", "‹")
    label(footer, "{s:wc_page}", "wcPage")
    button(footer, "next", "›")
    button(rows, "reload", "Повторить")
    detail = panel(body, "wcDetail", "wc_detail")
    cover = panel(detail, "wcCover", "wc_cover")
    art(cover, "wcCoverArt", "cover_art")
    caption = panel(cover, "wcCaption")
    label(caption, "{s:wc_detail_meta}", "wcMeta")
    label(caption, "{s:wc_detail_title}", "wcDetailTitle")
    head = panel(detail, "wcDetailHead")
    label(head, "{s:wc_detail_heading}", "wcHeading")
    button(head, "ct", "КТ")
    button(head, "t", "Т")
    for i in range(6):
        slot = panel(detail, "wcSlot", f"wc_slot_{i}")
        art(slot, "wcSlotArt", f"slot_art_{i}")
        label(slot, f"{{s:wc_slot_{i}}}", "wcMeta")
    label(detail, "{s:wc_detail_footer}", "wcSummary")
    button(detail, "detail_next", "{s:wc_detail_page}", "wcButton wcDetailNext")
    ET.indent(root)
    LAYOUT.parent.mkdir(parents=True, exist_ok=True)
    LAYOUT.write_text(ET.tostring(root, encoding="unicode"), encoding="utf8")
    nodes = list(root.iter())
    authored = {n.get("id"): n.get("class", "").split() for n in nodes if n.get("id")}
    assert len(authored) == sum(bool(n.get("id")) for n in nodes)
    variables = sorted({name for n in nodes for v in n.attrib.values() for name in re.findall(r"\{s:([\w-]+)\}", v)})
    buttons = sorted(n.get("id") for n in nodes if n.tag == "Button")
    contract = {"version": 1, "authoredClasses": authored, "variables": variables, "buttons": buttons}
    (ROOT / "catalog/hud-live-contract-v11.json").write_text(json.dumps(contract, indent=2) + "\n", encoding="utf8")


if __name__ == "__main__":
    main()
