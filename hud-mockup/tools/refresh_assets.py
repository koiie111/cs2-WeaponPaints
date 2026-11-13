from pathlib import Path
from PIL import Image
import json, hashlib, argparse

p = Path(__file__).resolve().parents[1]
ap = argparse.ArgumentParser()
ap.add_argument("--source", required=True, type=Path, help="website public root")
ap.add_argument("--catalog", required=True, type=Path, help="skins catalog JSON")
ap.add_argument("--custom-root", type=Path)
ap.add_argument("--custom-map", type=Path)
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
if args.custom_root and args.custom_map:
    mapping = json.loads(args.custom_map.read_text(encoding="utf8"))
    models = json.loads(
        (args.catalog.parent / "custom-models.json").read_text(encoding="utf8")
    )
    bykey = {Path(k.split("?")[0]).stem: v for k, v in mapping.items()}
    custom = []
    for key, record in models["weapons"]["weapon_ak47"]["Skins"].items():
        url = bykey.get(key)
        if url and (args.custom_root / url.lstrip("/")).is_file():
            custom.append(
                {
                    "weapon_name": "weapon_ak47",
                    "paint": key,
                    "paint_name": record["Name"],
                    "image": url,
                    "_root": args.custom_root,
                    "_origin": "custom_site",
                }
            )
    batches["custom"] = custom[:10]
pet = "/static/images/plus/perk-chicken.webp"
if (source / pet.lstrip("/")).is_file():
    batches["pet"] = [
        {
            "weapon_name": "pet",
            "paint": "illustration",
            "paint_name": "Курица · иллюстрация",
            "image": pet,
        }
    ]
ledger = {}
for bank, items in batches.items():
    cell_w, cell_h = (
        (640, 384) if bank in ("catalog", "finishes", "custom") else (320, 192)
    )
    atlas = Image.new("RGBA", (cell_w * 5, cell_h * 2))
    ledger[bank] = []
    for i, x in enumerate(items):
        f = x.get("_root", source) / x["image"].lstrip("/")
        im = Image.open(f).convert("RGBA")
        im = im.crop(im.getbbox())
        im.thumbnail((cell_w - 40, cell_h - 44), Image.Resampling.LANCZOS)
        atlas.alpha_composite(
            im,
            (
                (i % 5) * cell_w + (cell_w - im.width) // 2,
                (i // 5) * cell_h + (cell_h - im.height) // 2,
            ),
        )
        ledger[bank].append(
            {
                "cell": i,
                "name": x["paint_name"].split("|")[-1].strip(),
                "weapon": x["weapon_name"],
                "paint": x["paint"],
                "source": x["image"],
                "origin": x.get("_origin", "website"),
                "sha256": hashlib.sha256(f.read_bytes()).hexdigest(),
            }
        )
    atlas.save(p / "addon/materials/weaponpaints" / f"{bank}.png", optimize=True)
(p / "fixtures/asset-sources.json").write_text(
    json.dumps(ledger, ensure_ascii=False, indent=2), encoding="utf8"
)
