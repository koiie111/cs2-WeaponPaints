from pathlib import Path
import xml.etree.ElementTree as E, json, re, hashlib

P = Path(__file__).resolve().parents[1]
addon = P / "addon"
layout = addon / "panorama/layout/custom_game/weaponpaints.xml"
css = addon / "panorama/styles/custom_game/weaponpaints.css"
materials = addon / "materials/weaponpaints"
materials.mkdir(parents=True, exist_ok=True)
# PNG atlases are frozen inputs; optional refresh requires the original local catalog.
# See refresh_assets.py for source provenance and the reproducible refresh process.
root = E.Element("root")
styles = E.SubElement(root, "styles")
E.SubElement(
    styles, "include", {"src": "s2r://panorama/styles/custom_game/weaponpaints.vcss_c"}
)


def el(parent, tag, cls="", id="", text=None):
    a = {}
    if tag == "Label" or (
        tag == "Panel"
        and any(
            c in cls.split()
            for c in ("outer", "bank", "finishes", "rarity", "categoryStripe")
        )
    ):
        a["hittest"] = "false"
    if cls:
        a["class"] = cls
    if id:
        a["id"] = id
    if text is not None:
        a["text"] = text
    return E.SubElement(parent, tag, a)


def panel(p, c, id=""):
    return el(p, "Panel", c, id)


def label(p, c, t, id=""):
    return el(p, "Label", c, id, t)


def button(p, c, id, t):
    b = el(p, "Button", c, id)
    label(b, "buttonText", t)
    return b


outer = panel(root, "outer")
app = panel(outer, "app", "wp_root")
shell = panel(app, "shell")
side = panel(shell, "side")
label(side, "brand", "4ILL / SKINS")
for id, t in [
    ("nav_weapons", "⌁   ОРУЖИЕ"),
    ("nav_knives", "◇   НОЖИ"),
    ("nav_gloves", "▧   ПЕРЧАТКИ"),
    ("nav_agents", "◉   АГЕНТЫ"),
    ("nav_music", "♫   МУЗЫКА"),
    ("nav_pins", "✦   ЗНАЧКИ"),
    ("nav_graffiti", "▤   ГРАФФИТИ"),
    ("nav_pet", "◈   ПИТОМЕЦ"),
]:
    button(side, "nav", id, t)
main = panel(shell, "main")
top = panel(main, "top")
button(top, "back", "back", "‹")
label(top, "crumbText", "{s:breadcrumb}")
label(top, "title", "{s:title}")
button(top, "team", "team", "{s:team}")
button(top, "close", "close", "✕")
home = panel(main, "home pane", "home")
topMosaic = panel(home, "mosaicTop")


def category(parent, key, name, i, kind):
    card = el(parent, "Button", "mosaic " + kind, "category_" + key)
    panel(card, "categoryStripe")
    if i is not None:
        panel(card, "tileImage bank cell" + str(i))
    label(card, "tileTitle", name)


category(topMosaic, "rifles", "ВИНТОВКИ", 0, "large")
stack = panel(topMosaic, "mosaicStack")
category(stack, "pistols", "ПИСТОЛЕТЫ", 3, "wide")
category(stack, "smg", "ПИСТОЛЕТЫ-ПУЛЕМЁТЫ", 6, "wide")
bottom = panel(home, "mosaicBottom")
for key, name, i in [
    ("shotguns", "ДРОБОВИКИ", 7),
    ("snipers", "СНАЙПЕРСКИЕ", 2),
    ("heavy", "ПУЛЕМЁТЫ", 8),
    ("zeus", "ZEUS", 9),
]:
    category(bottom, key, name, i, "small")
catalog = panel(main, "catalog pane", "catalog")
toolbar = panel(catalog, "toolbar")
button(toolbar, "tab active", "tab_standard", "Обычные скины")
button(toolbar, "tab", "tab_custom", "Кастомные скины")
button(toolbar, "search", "search", "⌕  Поиск в чате")
button(toolbar, "sort", "sort", "По названию  ▾")
hero = panel(catalog, "hero", "hero")
heroArt = panel(hero, "heroArt")
panel(heroArt, "heroImage finishes cell0", "hero_art")
hi = panel(hero, "heroCopy")
label(hi, "heroTitle", "{s:hero_title}")
label(hi, "heroSub", "{s:hero_subtitle}")
label(hi, "heroValue", "{s:hero_wear}")
label(hi, "heroValue", "{s:hero_seed}")
label(hi, "heroValue", "{s:hero_stattrak}")
button(hi, "configure", "configure", "НАСТРОИТЬ")
grid = panel(catalog, "grid", "grid")
for row in range(2):
    r = panel(grid, "cardRow")
    for col in range(5):
        i = row * 5 + col
        c = el(r, "Button", "item", "item_" + str(i))
        panel(c, "rarity")
        label(c, "selectedMark", "✓")
        panel(c, "itemImage finishes cell" + str(i), "image_" + str(i))
        label(c, "itemName", "{s:item_" + str(i) + "}")
        label(c, "itemSub", "{s:sub_" + str(i) + "}")
footer = panel(catalog, "catalogFooter", "catalog_footer")
label(footer, "muted", "{s:count}")
button(footer, "page", "prev", "‹")
label(footer, "pageNumber", "{s:page}")
button(footer, "page", "next", "›")
settings = panel(main, "settings pane", "settings")
left = panel(settings, "previewColumn")
panel(left, "previewImage bank cell0", "preview_art")
label(left, "previewName", "{s:preview_name}")
right = panel(settings, "settingsRight")


def setting(key, name):
    r = panel(right, "settingRow", "row_" + key)
    label(r, "settingLabel", name)
    if key in ("wear", "seed"):
        controls = panel(r, "valueControls")
        button(controls, "step", key + "_minus", "−")
        button(controls, "valueInput", key, "{s:" + key + "}")
        button(controls, "step", key + "_plus", "+")
    else:
        button(r, "settingValue", key, "{s:" + key + "}")


setting("nametag", "ИМЕННОЙ ЯРЛЫК")
setting("wear", "ИЗНОС")
wearPresets = panel(right, "presetRow", "presets")
for grade in ["FN", "MW", "FT", "WW", "BS"]:
    button(wearPresets, "preset", "wear_" + grade.lower(), grade)
setting("seed", "ПАТТЕРН")
setting("stattrak", "STATTRAK™")
setting("decor", "НАКЛЕЙКИ")
decor = panel(right, "decorRow", "decor_slots")
for i in range(5):
    button(decor, "decorSlot", "decor_" + str(i), "+")
setting("charm", "БРЕЛОК")
feature = panel(right, "feature", "feature")
for i in range(8):
    row = panel(feature, "featureRow", "feature_row_" + str(i))
    label(row, "featureLabel", "{s:feature_label_" + str(i) + "}")
    button(row, "step", "feature_minus_" + str(i), "−")
    button(
        row,
        "featureInput",
        "feature_value_" + str(i),
        "{s:feature_value_" + str(i) + "}",
    )
    button(row, "step", "feature_plus_" + str(i), "+")
featureSlots = panel(feature, "decorRow", "feature_slots")
for i in range(3):
    button(featureSlots, "decorSlot", "feature_slot_" + str(i), "+")
applyRow = panel(right, "applyRow")
button(applyRow, "apply", "apply", "ПРИМЕНИТЬ")
plus = panel(applyRow, "plus", "plus")
label(plus, "plusTitle", "Доступно с 4ill+")
button(plus, "plusLink", "plus_link", "4ill.ru/plus  ↗")
overlay = panel(main, "overlay", "overlay")
label(overlay, "sectionTitle", "{s:state_title}")
label(overlay, "stateText", "{s:state_text}")
actions = panel(overlay, "overlayActions")
for i in range(3):
    button(actions, "stateButton", "state_" + str(i), "{s:state_" + str(i) + "}")
E.indent(root)
layout.write_text(E.tostring(root, encoding="unicode"), encoding="utf-8")
base = """
.outer { font-family: Stratum2; width: 100%; height: 100%; z-index: 99999; }
.app { width: 1480px; height: 820px; horizontal-align: center; vertical-align: center; visibility: collapse; }
.app.show { visibility: visible; }
.app.compact { ui-scale: 80%; }
.app.small { ui-scale: 65%; }
.shell { width: 1480px; height: 820px; flow-children: right; background-color: #111d2af5; border: 1px solid #344b5c; }
.side { width: 235px; height: 820px; flow-children: down; padding: 35px 22px; background-color: #0c1722; }
.brand { color: #e6f5ff; font-size: 25px; font-weight: bold; margin-bottom: 54px; }
.nav { width: 190px; height: 42px; padding: 12px 12px; }
.nav.active { background-color: #203e50; border-left: 3px solid #79c7ef; }
.nav:hover { background-color: #213a4b; }
.nav .buttonText { color: #a4b7c4; font-size: 15px; }
.main { width: 1245px; height: 820px; padding: 28px 42px; flow-children: down; }
.top { width: 1161px; height: 30px; flow-children: right; }
.close { width: 36px; height: 32px; background-color: #253744; }
.close .buttonText { color: #b4c7d2; font-size: 17px; horizontal-align: center; vertical-align: center; }
.back { width: 28px; height: 34px; }
.back .buttonText { color: #80c9ef; font-size: 26px; }
.crumbText { color: #8da6b5; font-size: 13px; margin-top: 8px; }
.title { color: #eef7fc; font-size: 32px; font-weight: bold; margin-top: 6px; }
.team { width: 190px; height: 35px; margin-top: 29px; padding: 10px; background-color: #253947; }
.team .buttonText { color: #bad0dc; font-size: 12px; }
.pane { visibility: collapse; }
.is-hidden { visibility: collapse; }
.home.show, .catalog.show, .settings.show { visibility: visible; }
.home { width: 1161px; height: 580px; flow-children: down; }
.mosaicTop { width: 1161px; height: 325px; flow-children: right; }
.mosaicBottom { width: 1161px; height: 210px; flow-children: right; margin-top: 12px; }
.mosaic { height: 100%; background-color: #1c3040; border: 1px solid #324b5c; margin-right: 12px; }
.mosaic.large { width: 475px; }
.mosaic.small { width: 327px; }
.mosaicBottom .mosaic.small { width: 277px; }
.tileTitle { margin: 45px 22px; color: #f0f7fb; font-size: 19px; font-weight: bold; }
.tileImage { width: 280px; height: 150px; horizontal-align: center; vertical-align: center; }
.mosaic.large .tileImage { width: 420px; height: 224px; }
.mosaicBottom .tileImage { width: 230px; height: 123px; margin-top: 44px; }
.catalog { width: 1161px; height: 585px; flow-children: down; }
.toolbar { width: 1161px; height: 48px; flow-children: right; }
.tab { width: 170px; height: 36px; padding: 10px; border-bottom: 2px solid #324b5c; }
.tab.active { border-bottom: 2px solid #78c7ee; }
.tab .buttonText { color: #adc3cf; font-size: 12px; font-weight: bold; }
.search { width: 310px; height: 34px; margin-left: 195px; padding: 9px; background-color: #263b4a; }
.sort { width: 230px; height: 34px; margin-left: 20px; padding: 9px; background-color: #263b4a; }
.search .buttonText, .sort .buttonText { color: #aac1ce; font-size: 12px; }
.hero { width: 1161px; height: 145px; flow-children: right; background-color: #243d4e; margin-bottom: 12px; }
.heroCopy { width: 650px; padding: 24px; flow-children: down; }
.heroTitle { color: #f3f8fb; font-size: 26px; font-weight: bold; margin-top: 8px; }
.heroSub { color: #a4bac8; font-size: 13px; margin-top: 5px; }
.heroImage { width: 270px; height: 144px; }
.grid { width: 1161px; height: 310px; flow-children: down; }
.cardRow { width: 1161px; height: 150px; flow-children: right; margin-bottom: 8px; }
.item { width: 224px; height: 150px; margin-right: 8px; background-color: #1c3040; border: 1px solid #31495a; }
.rarity { width: 224px; height: 3px; vertical-align: bottom; background-color: #718998; }
.selectedMark { visibility: collapse; color: #83d4f7; font-size: 18px; margin: 8px; }
.item.selected .selectedMark { visibility: visible; }
.itemImage { width: 186px; height: 112px; horizontal-align: center; }
.itemName { color: #ecf4f8; font-size: 13px; font-weight: bold; margin: 103px 12px 0px 12px; }
.itemSub { color: #7893a3; font-size: 10px; margin: 123px 12px 0px 12px; }
.catalogFooter { width: 1161px; height: 45px; flow-children: right; padding-top: 10px; }
.muted { color: #829ba9; font-size: 12px; }
.catalogFooter .muted { width: 1000px; }
.page { width: 30px; height: 26px; background-color: #263b4a; }
.page .buttonText, .pageNumber { color: #c8dbe5; font-size: 14px; horizontal-align: center; }
.pageNumber { width: 70px; text-align: center; }
.settings { width: 1161px; height: 585px; flow-children: right; }
.previewColumn { width: 585px; height: 560px; flow-children: down; background-color: #1c3040; padding: 24px; }
.previewImage { width: 530px; height: 318px; margin-top: 40px; }
.previewName { color: #f0f7fb; font-size: 22px; font-weight: bold; margin-top: 30px; margin-bottom: 10px; }
.settingsRight { width: 550px; height: 565px; margin-left: 24px; flow-children: down; }
.sectionTitle { color: #eff7fb; font-size: 20px; font-weight: bold; }
.settingsRight .muted { margin-top: 5px; margin-bottom: 10px; }
.settingRow { width: 550px; height: 57px; flow-children: right; border-bottom: 1px solid #31495a; }
.settingLabel { color: #dbe9f0; font-size: 14px; }
.settingValue { width: 220px; height: 34px; margin-top: 11px; padding: 10px; background-color: #263e4e; }
.settingValue .buttonText { color: #cde4ef; font-size: 12px; }
.valueControls { width: 220px; height: 34px; margin-top: 11px; flow-children: right; }
.step { width: 34px; height: 34px; padding: 8px; background-color: #304d5e; }
.step .buttonText { color: #cde4ef; font-size: 14px; }
.valueInput { width: 152px; height: 34px; padding: 9px; background-color: #263e4e; }
.valueInput .buttonText { color: #cde4ef; font-size: 12px; }
.presetRow { width: 550px; height: 32px; flow-children: right; margin-top: 7px; }
.preset { width: 53px; height: 25px; padding: 6px; margin-right: 5px; background-color: #304d5e; }
.preset .buttonText { color: #cde4ef; font-size: 10px; }
.decorRow { width: 550px; height: 55px; flow-children: right; margin-top: 10px; }
.decorSlot { width: 62px; height: 45px; margin-right: 8px; background-color: #263e4e; }
.decorSlot .buttonText { color: #9ebac9; font-size: 15px; horizontal-align: center; vertical-align: center; }
.feature { width: 550px; height: 500px; flow-children: down; visibility: collapse; }
.feature.show { visibility: visible; }
.featureRow { width: 550px; height: 45px; flow-children: right; margin-bottom: 6px; background-color: #1e3240; }
.featureLabel { width: 275px; color: #a6bdc9; font-size: 13px; margin: 13px 10px; }
.featureInput { width: 150px; height: 34px; padding: 9px; background-color: #263e4e; }
.featureInput .buttonText { color: #cde4ef; font-size: 12px; }
.applyRow { width: 550px; height: 70px; flow-children: right; }
.apply { width: 220px; height: 44px; padding: 14px; background-color: #73bfe5; }
.apply .buttonText { color: #0a2635; font-size: 12px; font-weight: bold; }
.apply.disabled { background-color: #465966; }
.plus { width: 310px; margin-left: 10px; height: 66px; padding: 10px; flow-children: down; background-color: #263d4d; visibility: collapse; }
.plus.show { visibility: visible; }
.plusTitle { color: #9bdaf8; font-size: 11px; font-weight: bold; }
.plusLink .buttonText { color: #87ccee; font-size: 11px; }
.overlay { ignore-parent-flow: true; horizontal-align: center; vertical-align: center; width: 520px; height: 200px; padding: 25px; flow-children: down; background-color: #233b4c; visibility: collapse; }
.overlay.show { visibility: visible; }
.stateText { color: #acc2ce; font-size: 14px; margin-top: 18px; }
.overlayActions { flow-children: right; margin-top: 25px; }
.stateButton { width: 140px; height: 34px; padding: 9px; margin-right: 10px; background-color: #42677d; }
.stateButton .buttonText { color: #e9f6fc; font-size: 12px; }
.finishes { background-image: url("s2r://materials/weaponpaints/finishes.vtex"); background-size: 500% 200%; background-repeat: no-repeat; }
.bank { background-image: url("s2r://materials/weaponpaints/catalog.vtex"); background-size: 500% 200%; background-repeat: no-repeat; }
"""
base += """
.side { width: 260px; padding: 32px 20px; }
.brand { font-size: 18px; margin-bottom: 34px; letter-spacing: 1px; }
.title, .tileTitle, .heroTitle, .nav .buttonText { letter-spacing: 1px; }
.nav .buttonText { font-size: 15px; font-weight: bold; }
.nav { width: 220px; height: 44px; }
.main { width: 1220px; padding: 20px 32px; }
.top { width: 1156px; height: 64px; flow-children: right; }
.top .back { margin-top: 8px; }
.top .crumbText { width: 170px; margin-top: 13px; }
.top .title { width: 700px; font-size: 23px; margin-top: 7px; }
.top .team { width: 190px; margin-top: 6px; }
.top .close { margin-left: 15px; margin-top: 6px; }
.close { horizontal-align: right; }
.title { font-size: 25px; margin-top: 0px; }
.team { margin-top: 0px; }
.home { width: 1156px; height: 700px; }
.mosaicTop { width: 1156px; height: 460px; }
.mosaicStack { width: 555px; height: 460px; flow-children: down; }
.mosaic.large { width: 589px; height: 460px; margin-right: 12px; }
.mosaic.wide { width: 555px; height: 224px; margin-right: 0px; margin-bottom: 12px; }
.mosaicBottom { width: 1156px; height: 190px; margin-top: 12px; }
.mosaicBottom .mosaic.small { width: 280px; height: 190px; }
.mosaic { background-color: gradient( linear, 0% 0%, 100% 100%, from( #303d49 ), to( #24323e ) ); border: 1px solid #314654; }
.categoryStripe { width: 100%; height: 3px; vertical-align: bottom; background-color: #719dde; }
.tileTitle { margin: 0px 22px 22px 22px; font-size: 24px; vertical-align: bottom; }
.tileImage { width: 340px; height: 204px; horizontal-align: center; vertical-align: center; }
.mosaic.large .tileImage { width: 570px; height: 342px; }
.mosaic.large .tileTitle { font-size: 32px; }
.mosaic.large { background-color: gradient( linear, 0% 0%, 100% 100%, from( #364351 ), to( #563e47 ) ); }
.mosaic.wide { background-color: gradient( linear, 0% 0%, 100% 100%, from( #33414c ), to( #404157 ) ); }
.mosaic.wide .tileImage { width: 310px; height: 186px; horizontal-align: right; margin-right: 30px; }
.mosaicBottom .tileImage { width: 220px; height: 132px; margin-top: 0px; }
.catalog { height: 660px; }
.hero { height: 225px; flow-children: right; }
.heroCopy { width: 451px; height: 225px; padding: 18px 28px; flow-children: down; background-color: #111e29; }
.heroArt { width: 710px; height: 225px; overflow: clip; }
.heroImage { width: 560px; height: 336px; horizontal-align: center; margin-top: -55px; }
.heroValue { color: #9eb1be; font-size: 12px; margin-top: 5px; }
.heroTitle { font-size: 26px; }
.heroSub { font-size: 13px; margin-top: 12px; }
.configure { width: 145px; height: 32px; margin-top: 8px; padding: 11px; background-color: #76bfe5; }
.configure .buttonText { color: #112939; font-size: 12px; font-weight: bold; }
.settings { height: 660px; }
.previewColumn { height: 620px; }
.previewImage { margin-top: 55px; }
.settingsRight { height: 620px; padding-top: 0px; }
.settingRow { height: 53px; border: 1px solid #3b4b58; margin-bottom: 6px; padding-left: 12px; }
.settingLabel { width: 275px; margin-top: 17px; font-size: 13px; font-weight: bold; color: #a2b4bf; }
.settingValue, .valueControls { margin-top: 9px; }
.presetRow { height: 32px; margin-top: 0px; }
"""
for bank in (
    "gloves",
    "agents",
    "music",
    "pins",
    "charm",
    "stickers",
    "knives",
    "custom",
    "pet",
):
    base += f'.bank-{bank} {{ background-image: url("s2r://materials/weaponpaints/{bank}.vtex"); background-size: 500% 200%; background-repeat: no-repeat; }}\n'
for i in range(10):
    base += f".cell{i} {{ background-position: {i%5*25}% {i//5*100}%; }}\n"
css.write_text(base, encoding="utf-8")
ids = [n.attrib["id"] for n in root.iter() if "id" in n.attrib]
vars = sorted(set(re.findall(r"\{s:([^}]+)\}", E.tostring(root, encoding="unicode"))))
classes = sorted(set(" ".join(n.get("class", "") for n in root.iter()).split()))
contract = {
    "ids": ids,
    "variables": vars,
    "classes": classes,
    "atlas_cells": json.loads(
        (P / "fixtures/asset-sources.json").read_text(encoding="utf8")
    ),
}
(P / "fixtures/contract.json").write_text(
    json.dumps(contract, ensure_ascii=False, indent=2), encoding="utf-8"
)
(P / "fixtures/Contract.cs.txt").write_text(
    "// Generated contract; copy into phase-2 project intentionally.\nnamespace WeaponPaints.HudContract;\npublic static class Ids {\n"
    + "".join(
        "    public const string "
        + "".join(x.title() for x in id.split("_"))
        + ' = "'
        + id
        + '";\n'
        for id in ids
    )
    + "}\n",
    encoding="utf-8",
)
common = {
    "breadcrumb": "Обзор",
    "title": "ОРУЖИЕ",
    "subtitle": "Выберите категорию и настройте свой стиль",
    "team": "СТОРОНА  ·  ОБЕ",
    "hero_title": json.loads(
        (P / "fixtures/asset-sources.json").read_text(encoding="utf8")
    )["finishes"][0]["name"],
    "hero_subtitle": "AK-47",
    "hero_wear": "Износ 0.1500",
    "hero_seed": "Паттерн 123",
    "hero_stattrak": "StatTrak™ 248 убийств",
    "count": "Найдено предметов: 124",
    "page": "1 / 13",
    "preview_name": "AK-47  |  "
    + json.loads((P / "fixtures/asset-sources.json").read_text(encoding="utf8"))[
        "finishes"
    ][0]["name"],
    "wear": "0.1500  ·  чат",
    "wear_hint": "FN  ·  MW  ·  FT  ·  WW  ·  BS",
    "seed": "123  ·  чат",
    "seed_hint": "Введите значение в чате",
    "nametag": "Добавить имя",
    "nametag_hint": "Ввод через чат",
    "stattrak": "ВКЛ  ·  248 убийств",
    "stattrak_hint": "Счётчик доступен только для просмотра",
    "decor": "ВЫБРАТЬ  ›",
    "charm": "ВЫБРАТЬ  ›",
    "decor_hint": "Стикеры, нашивки и брелок",
}
names = [
    x["name"]
    for x in json.loads((P / "fixtures/asset-sources.json").read_text(encoding="utf8"))[
        "finishes"
    ]
]
for i, n in enumerate(names):
    common["item_" + str(i)] = n
    common["sub_" + str(i)] = ""


def state(pane, **kw):
    a = {"wp_root": "show", pane: "show", "nav_weapons": "active"}
    a.update(kw.pop("add", {}))
    v = common.copy()
    v.update(kw)
    return {"add": a, "vars": v}


states = {
    "home": state("home"),
    "finishes": state(
        "catalog",
        add={"item_0": "selected"},
        breadcrumb="ВИНТОВКИ",
        title="AK-47",
        subtitle="Выберите скин для AK-47",
    ),
    "settings": state(
        "settings",
        breadcrumb="AK-47",
        title=json.loads(
            (P / "fixtures/asset-sources.json").read_text(encoding="utf8")
        )["finishes"][0]["name"].upper(),
        subtitle="Соберите свой уникальный образ",
    ),
}
# Reuse the same ten card slots for every catalog. Missing fixture artwork is intentionally blank.
art = json.loads((P / "fixtures/asset-sources.json").read_text(encoding="utf8"))
for key, title in [
    ("weapons", "ВИНТОВКИ"),
    ("knives", "НОЖИ"),
    ("gloves", "ПЕРЧАТКИ"),
    ("agents", "АГЕНТЫ"),
    ("music", "МУЗЫКА"),
    ("pins", "ЗНАЧКИ"),
    ("graffiti", "ГРАФФИТИ"),
    ("pet", "ПИТОМЕЦ"),
    ("custom", "Кастомные скины"),
]:
    st = state(
        "catalog",
        breadcrumb="НАЗАД",
        title=title,
        count="Образцы каталога",
        page="1 / 1",
    )
    st["hide"] = ["hero"]
    if key not in ("weapons", "knives", "gloves", "agents", "custom"):
        st["hide"].append("team")
    bank = "catalog" if key == "weapons" else key
    items = art.get(bank, [])
    if key == "graffiti":
        items = [
            {"name": x["name"], "weapon": "graffiti"}
            for x in json.loads(
                (P / "fixtures/graffiti.json").read_text(encoding="utf8")
            )
        ]
    for i in range(10):
        if i >= len(items):
            st["hide"].append("item_" + str(i))
            continue
        item = items[i]
        st["vars"]["item_" + str(i)] = item["name"]
        st["vars"]["sub_" + str(i)] = (
            item["weapon"].replace("weapon_", "").upper() if key == "weapons" else ""
        )
        if key == "graffiti":
            st["hide"].append("image_" + str(i))
            continue
        st.setdefault("remove", {})["image_" + str(i)] = "finishes"
        st["add"]["image_" + str(i)] = "bank" if bank == "catalog" else "bank-" + bank
    st["add"].pop("nav_weapons", None)
    st["add"]["nav_" + ("weapons" if key in ("weapons", "custom") else key)] = "active"
    if not items:
        st["vars"]["count"] = "Для этого образца изображений пока нет"
    states[key] = st
features = {
    "stickers": (
        "НАКЛЕЙКА",
        [
            ("СЛОТ", "1 / 5", False),
            ("ТОЧКА КРЕПЛЕНИЯ", "1", False),
            ("СМЕЩЕНИЕ X", "0.00", True),
            ("СМЕЩЕНИЕ Y", "0.00", True),
            ("ИЗНОС", "0.00", True),
            ("МАСШТАБ", "1.00", True),
            ("ПОВОРОТ", "0°", True),
        ],
    ),
    "patches": ("НАШИВКИ АГЕНТА", []),
    "charm": (
        "БРЕЛОК И СТИКЕР-СЛЭБ",
        [
            ("БРЕЛОК", "Выбрать", False),
            ("СМЕЩЕНИЕ X", "0.00", True),
            ("СМЕЩЕНИЕ Y", "0.00", True),
            ("СМЕЩЕНИЕ Z", "0.00", True),
            ("ПАТТЕРН", "1", True),
            ("СТИКЕР В СЛЭБЕ", "Выбрать", False),
        ],
    ),
    "pet_options": (
        "ПИТОМЕЦ",
        [
            ("ПИТОМЕЦ", "Включён", False),
            ("ВОЗРАСТ", "Цыплёнок", False),
            ("РАЗНОВИДНОСТЬ", "Обычная", False),
            ("ПАТТЕРН", "1", True),
            ("ИМЯ", "Ввести в чате", False),
            ("ГОЛОВНОЙ УБОР", "Без убора", False),
        ],
    ),
    "custom_selection": (
        "КАСТОМНЫЙ СКИН",
        [("СКИН", art["custom"][0]["name"] if art.get("custom") else "Выбрать", False)],
    ),
}
for key, (title, rows) in features.items():
    st = state("settings", breadcrumb="НАЗАД", title=title, add={"feature": "show"})
    st["hide"] = [
        "row_" + n for n in ("nametag", "wear", "seed", "stattrak", "decor", "charm")
    ] + ["presets", "decor_slots"]
    for i in range(8):
        if i < len(rows):
            (
                st["vars"]["feature_label_" + str(i)],
                st["vars"]["feature_value_" + str(i)],
                numeric,
            ) = rows[i]
            if not numeric:
                st["hide"] += ["feature_minus_" + str(i), "feature_plus_" + str(i)]
        else:
            st["hide"].append("feature_row_" + str(i))
    if key != "patches":
        st["hide"].append("feature_slots")
    if key in ("patches", "pet_options", "custom_selection"):
        bank = {
            "patches": "agents",
            "pet_options": "pet",
            "custom_selection": "custom",
        }[key]
        if art.get(bank):
            st["remove"] = {"preview_art": "bank"}
            st["add"]["preview_art"] = "bank-" + bank
            st["vars"]["preview_name"] = art[bank][0]["name"]
        else:
            st["hide"].append("preview_art")
            st["vars"]["preview_name"] = title
    if key in ("patches", "pet_options"):
        st["add"].pop("nav_weapons", None)
        st["add"]["nav_agents" if key == "patches" else "nav_pet"] = "active"
    if key == "pet_options":
        st["hide"].append("team")
    states[key] = st
states["no_plus"] = state(
    "settings", title="AK-47  /  СКИН", add={"plus": "show", "apply": "disabled"}
)
for key, title, body, actions in [
    (
        "dirty",
        "ЕСТЬ НЕСОХРАНЁННЫЕ ИЗМЕНЕНИЯ",
        "Применить изменения перед выходом?",
        ["Применить", "Не сохранять", "Остаться"],
    ),
    (
        "no_results",
        "НИЧЕГО НЕ НАЙДЕНО",
        "Попробуйте другой поисковый запрос.",
        ["Изменить поиск", "", ""],
    ),
    ("saving", "СОХРАНЕНИЕ", "Сохраняем изменения…", ["", "", ""]),
    (
        "error",
        "НЕ УДАЛОСЬ СОХРАНИТЬ",
        "Повторите попытку позже.",
        ["Повторить", "Закрыть", ""],
    ),
    (
        "conflict",
        "ДАННЫЕ ИЗМЕНИЛИСЬ",
        "Обновите предмет перед сохранением.",
        ["Обновить", "Остаться", ""],
    ),
    (
        "custom_unavailable",
        "КАСТОМНЫЙ СКИН НЕДОСТУПЕН",
        "Для этого скина нужен индивидуальный доступ.",
        ["Понятно", "", ""],
    ),
    (
        "custom_missing",
        "КАСТОМНЫЕ СКИНЫ НЕДОСТУПНЫ",
        "На сервере не подключён каталог кастомных скинов.",
        ["Понятно", "", ""],
    ),
    (
        "chat_prompt",
        "ВВОД В ЧАТЕ",
        "Введите значение в чат или напишите cancel.",
        ["Отмена", "", ""],
    ),
]:
    st = state(
        "catalog" if key == "no_results" else "settings",
        add={"overlay": "show"},
        state_title=title,
        state_text=body,
        **{"state_" + str(i): v for i, v in enumerate(actions)},
    )
    st["hide"] = ["state_" + str(i) for i, v in enumerate(actions) if not v]
    if key == "no_results":
        st["hide"] += ["hero", "grid", "catalog_footer"]
    states[key] = st
for base_name in ("home", "finishes", "settings"):
    compact = json.loads(json.dumps(states[base_name], ensure_ascii=False))
    compact["add"]["wp_root"] = "show compact"
    states[base_name + "_compact"] = compact
    small = json.loads(json.dumps(states[base_name], ensure_ascii=False))
    small["add"]["wp_root"] = "show small"
    states[base_name + "_small"] = small
for st in states.values():
    for ident in st.pop("hide", []):
        st["add"][ident] = (st["add"].get(ident, "") + " is-hidden").strip()
(P / "fixtures/states.json").write_text(
    json.dumps(states, ensure_ascii=False, indent=2), encoding="utf-8"
)
print("generated", len(ids), "ids", len(vars), "variables", len(classes), "classes")
