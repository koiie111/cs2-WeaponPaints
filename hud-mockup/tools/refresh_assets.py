from pathlib import Path
from PIL import Image
import json, hashlib, argparse

p = Path(__file__).resolve().parents[1]
ap = argparse.ArgumentParser()
ap.add_argument("--source", required=True, type=Path, help="website public root")
ap.add_argument("--catalog", required=True, type=Path, help="skins catalog JSON")
ap.add_argument("--custom-root", type=Path)
ap.add_argument("--custom-map", type=Path)
ap.add_argument("--custom-renders", type=Path)
ap.add_argument("--graffiti-images", type=Path)
ap.add_argument("--economy", type=Path)
args = ap.parse_args()
source = args.source
data = args.catalog
catalog = json.loads(data.read_text(encoding="utf8"))
byweapon = [
    "weapon_ak47",
    "weapon_m4a1",
    "weapon_awp",
    "weapon_deagle",
    "weapon_glock",
    "weapon_usp_silencer",
    "weapon_mp9",
    "weapon_nova",
    "weapon_m249",
    "weapon_taser",
]
batches = {"catalog": [], "finishes": []}
for weapon in byweapon:
    batches["catalog"].append(
        next(
            x
            for x in catalog
            if x["weapon_name"] == weapon
            and x["paint"] != 0
            and (source / x["image"].lstrip("/")).is_file()
        )
    )
batches["finishes"] = [
    x
    for x in catalog
    if x["weapon_name"] == "weapon_ak47"
    and x["paint"] != 0
    and (source / x["image"].lstrip("/")).is_file()
][:10]
# Catalogs without a local image remain explicit empty placeholders.
for bank, filename in [
    ("gloves", "gloves.json"),
    ("agents", "agents.json"),
    ("music", "music.json"),
    ("pins", "collectibles.json"),
    ("charm", "keychains.json"),
    ("stickers", "stickers.json"),
]:
    records = json.loads((args.catalog.parent / filename).read_text(encoding="utf8"))
    usable = [
        x
        for x in records
        if x.get("image") and (source / x["image"].lstrip("/")).is_file()
    ][:10]
    batches[bank] = [
        {
            "weapon_name": bank,
            "weapon_defindex": x.get("weapon_defindex"),
            "paint": x.get("id", x.get("paint", 0)),
            "paint_name": x.get("paint_name", x.get("agent_name", x.get("name", ""))),
            "image": x["image"],
        }
        for x in usable
    ]
knives = [
    x
    for x in catalog
    if ("knife" in x["weapon_name"] or "bayonet" in x["weapon_name"])
    and x.get("image")
    and (source / x["image"].lstrip("/")).is_file()
][:10]
batches["knives"] = knives
# Verified editor-render output, never FPS video posters. Custom key must match exactly.
if args.custom_renders:
    models = json.loads(
        (args.catalog.parent / "custom-models.json").read_text(encoding="utf8")
    )
    key = "daedalos"
    record = models["weapons"]["weapon_ak47"]["Skins"][key]
    render = args.custom_renders / (key + ".png")
    if render.is_file():
        batches["custom"] = [
            {
                "weapon_name": "weapon_ak47",
                "paint": key,
                "paint_name": record["Name"],
                "image": key + ".png",
                "_file": render,
                "_origin": "website_editor_render",
            }
        ]
rifle_specs = [
    ("AK-47", "ak47", 7),
    ("AUG", "aug", 8),
    ("FAMAS", "famas", 10),
    ("Galil AR", "galilar", 13),
    ("M4A4", "m4a1", 16),
    ("M4A1-S", "m4a1_silencer", 60),
    ("SG 553", "sg556", 39),
]
batches["rifles"] = [
    {
        "weapon_name": "weapon_" + key,
        "weapon_defindex": idx,
        "paint": 0,
        "paint_name": name,
        "image": "/static/images/skins/csgo/skins/weapon_" + key + ".png",
    }
    for name, key, idx in rifle_specs
]
if args.graffiti_images:
    records = json.loads(
        (args.catalog.parent / "graffiti.json").read_text(encoding="utf8")
    )
    batches["graffiti"] = [
        {
            "weapon_name": "graffiti",
            "paint": x["graffiti"],
            "paint_name": x["name"],
            "image": x["imagePath"],
            "_file": args.graffiti_images / Path(x["imagePath"]).name,
            "_origin": "cs2_lib",
            "_econ_id": x["id"],
        }
        for x in records[:10]
    ]
econ = json.loads(args.economy.read_text(encoding="utf8")) if args.economy else []
by_pair = {
    (str(x.get("definitionIndex")), str(x.get("variantIndex"))): x
    for x in econ
    if x.get("variantIndex") is not None
}
by_id = {x["id"]: x for x in econ}


def rarity(item, bank):
    if item.get("_econ_id") in by_id:
        return by_id[item["_econ_id"]].get("rarityColor")
    if item.get("weapon_defindex") and item.get("paint") not in (0, "0"):
        return by_pair.get((str(item["weapon_defindex"]), str(item["paint"])), {}).get(
            "rarityColor"
        )
    if bank == "pins":
        match = next(
            (
                x
                for x in econ
                if x.get("type") == "collectible"
                and str(x.get("definitionIndex")) == str(item.get("paint"))
            ),
            None,
        )
        return match.get("rarityColor") if match else None
    if bank in ("stickers", "charm", "music"):
        typ = {"stickers": "sticker", "charm": "keychain", "music": "musickit"}[bank]
        match = next(
            (
                x
                for x in econ
                if x.get("type") == typ
                and str(x.get("variantIndex")) == str(item.get("paint"))
            ),
            None,
        )
        return match.get("rarityColor") if match else None
    if bank == "agents":
        import re

        m = re.search(r"agent-(\d+)", item["image"])
        match = next(
            (
                x
                for x in econ
                if x.get("type") == "agent"
                and m
                and x.get("definitionIndex") == int(m.group(1))
            ),
            None,
        )
        return match.get("rarityColor") if match else None
    return None


if args.graffiti_images and econ:
    patches = json.loads(
        (args.catalog.parent / "patches.json").read_text(encoding="utf8")
    )
    entry = patches[0]
    meta = by_id[entry["economyId"]]
    batches["patches"] = [
        {
            "weapon_name": "patch",
            "paint": entry["id"],
            "paint_name": entry["name"],
            "image": meta["imagePath"],
            "_file": args.graffiti_images / Path(meta["imagePath"]).name,
            "_origin": "cs2_lib",
            "_econ_id": entry["economyId"],
        }
    ]
ledger = {}
for bank, items in batches.items():
    atlas = Image.new("RGBA", (640, 384)) if bank == "custom" else None
    ledger[bank] = []
    for i, x in enumerate(items):
        f = x.get("_file") or x.get("_root", source) / x["image"].lstrip("/")
        if atlas is not None:
            im = Image.open(f).convert("RGBA")
            im = im.crop(im.getbbox())
            im.thumbnail((600, 340), Image.Resampling.LANCZOS)
            atlas.alpha_composite(im, ((640 - im.width) // 2, (384 - im.height) // 2))
        ledger[bank].append(
            {
                "cell": i,
                "name": x["paint_name"].split("|")[-1].strip(),
                "weapon": x["weapon_name"],
                "paint": x["paint"],
                "source": x["image"],
                "origin": x.get("_origin", "website"),
                "sha256": hashlib.sha256(f.read_bytes()).hexdigest(),
                "definition_index": x.get("weapon_defindex"),
                "rarity_color": rarity(x, bank),
            }
        )
    if atlas is not None:
        atlas.save(p / "addon/materials/weaponpaints/custom.png", optimize=True)
pet_icon = p / "preview-assets/native/panorama/images/icons/ui/pet_chick.svg"
ledger["pet"] = [
    {
        "cell": 0,
        "name": "Цыплёнок",
        "weapon": "pet",
        "paint": "chick",
        "source": "panorama/images/icons/ui/pet_chick.vsvg_c",
        "origin": "valve_native_decompiled",
        "sha256": hashlib.sha256(pet_icon.read_bytes()).hexdigest(),
        "definition_index": None,
        "rarity_color": None,
    }
]
(p / "fixtures/asset-sources.json").write_text(
    json.dumps(ledger, ensure_ascii=False, indent=2), encoding="utf8"
)
