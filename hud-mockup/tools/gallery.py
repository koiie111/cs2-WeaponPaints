from pathlib import Path
import argparse, json, subprocess, sys, html

p = Path(__file__).resolve().parents[1]
ap = argparse.ArgumentParser()
ap.add_argument("--png", action="store_true")
ap.add_argument("--only", default="")
ap.add_argument("--size", default="1600x900")
a = ap.parse_args()
out = p / "previews"
out.mkdir(exist_ok=True)
args = [
    sys.executable,
    str(p / "tools/panorama_preview.py"),
    str(p / "addon/panorama/layout/custom_game/weaponpaints.xml"),
    "--states",
    str(p / "fixtures/states.json"),
    "--out",
    str(out),
    "--size",
    a.size,
]
if a.png:
    args.append("--png")
if a.only:
    args += ["--only", a.only]
subprocess.run(args, check=True)
states = json.loads((p / "fixtures/states.json").read_text(encoding="utf8"))
names = a.only.split(",") if a.only else states.keys()
labels = {
    "home": "Оружие",
    "finishes": "Скины AK-47",
    "settings": "Настройки",
    "weapons": "Винтовки",
    "knives": "Ножи",
    "gloves": "Перчатки",
    "agents": "Агенты",
    "music": "Музыка",
    "pins": "Значки",
    "graffiti": "Граффити",
    "pet": "Питомец",
    "custom": "Кастомные скины",
    "stickers": "Наклейка",
    "patches": "Нашивки",
    "charm": "Брелок",
    "pet_options": "Параметры питомца",
    "custom_selection": "Выбор кастомного скина",
    "no_plus": "Без 4ill+",
    "dirty": "Несохранённые изменения",
    "no_results": "Нет результатов",
    "saving": "Сохранение",
    "error": "Ошибка",
    "conflict": "Конфликт",
    "custom_unavailable": "Нет доступа к скину",
    "custom_missing": "Каталог кастомных скинов недоступен",
    "chat_prompt": "Ввод в чате",
}
links = "".join(
    f'<a href="weaponpaints.{html.escape(n)}.html" target="preview">{html.escape(labels.get(n,n.replace("_compact"," · 80%").replace("_small"," · 65%")))}</a>'
    for n in names
)
(out / "index.html").write_text(
    '<!doctype html><meta charset="utf-8"><title>WeaponPaints HUD — fixture gallery</title><style>body{margin:0;background:#101923;color:#c8d9e4;font:14px Arial}nav{padding:12px;display:flex;gap:12px;flex-wrap:wrap}a{color:#82cbed}iframe{border:0;width:100%;height:900px}</style><nav>'
    + links
    + '</nav><p>Симуляция: HTML приближает Panorama. Изображение не показывает точное положение украшений. Сохранение и действия не реализованы.</p><iframe name="preview" src="weaponpaints.home.html"></iframe>',
    encoding="utf8",
)
print("Gallery:", out / "index.html")
