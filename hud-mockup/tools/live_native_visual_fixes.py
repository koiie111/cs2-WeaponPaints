"""Apply native-only image rules to generated CSS before writing a NEW version.

Do not run against an already published native resource path. Panorama's
background layers can merge across selectors; keep image and fitting properties
in the same rule rather than relying only on the base `.art` selector. This is
an evidence-based hypothesis until verified on the retail client.
"""
import re
from pathlib import Path

FITTING = " background-size: contains; background-position: center; background-repeat: no-repeat; "
_RULE = re.compile(r"([^{}]+)\{([^{}]*)\}")
_IMAGE = re.compile(r"(?:^|;)\s*background-image\s*:", re.I)
_ART = re.compile(r"\.art(?:-|[.\s,:]|$)")


def explicit_image_fitting(css: str) -> tuple[str, int]:
    count = 0
    def patch(match: re.Match) -> str:
        nonlocal count
        selector, body = match.group(1), match.group(2)
        if not _IMAGE.search(body):
            return match.group(0)
        if not _ART.search(selector):
            raise ValueError(f"Unexpected non-art image selector: {selector.strip()}")
        # Refuse conflicting authored fitting, rather than silently changing it.
        if re.search(r"(?:^|;)\s*background-(?:size|position|repeat)\s*:", body, re.I):
            raise ValueError(f"Image selector already has fitting properties: {selector.strip()}")
        count += 1
        return selector + "{" + body.rstrip() + FITTING + "}"
    result = _RULE.sub(patch, css)
    if count == 0:
        raise ValueError("No image selectors found; refuse incomplete native fix")
    return result, count


def shell_and_hero_overrides() -> str:
    return Path(__file__).with_suffix(".css").read_text(encoding="utf8")


def replace_font_icons(xml):
    """Replace exactly two decorative Labels with whitelisted background Panels.

    Button nodes and their IDs remain unchanged. Paths are stock game VPK
    resources and are referenced, never copied to the addon.
    """
    for icon_class in ("backIcon", "searchIcon"):
        matches = [node for node in xml.iter() if icon_class in node.get("class", "").split()]
        if len(matches) != 1:
            raise ValueError(f"Expected exactly one {icon_class} node")
        node = matches[0]
        if node.tag != "Label" or node.get("id") is not None or len(node):
            raise ValueError(f"Unexpected {icon_class} structure")
        if set(node.attrib) != {"hittest", "class", "text"} or node.get("hittest") != "false":
            raise ValueError(f"Unexpected {icon_class} attributes")
        node.tag = "Panel"
        del node.attrib["text"]
