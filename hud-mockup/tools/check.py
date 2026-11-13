from pathlib import Path
import json, re, subprocess, sys, xml.etree.ElementTree as ET

p = Path(__file__).resolve().parents[1]
addon = p / "addon"
xml = addon / "panorama/layout/custom_game/weaponpaints.xml"
css = addon / "panorama/styles/custom_game/weaponpaints.css"
files = [
    xml,
    css,
    p / "fixtures/states.json",
    p / "fixtures/contract.json",
    p / "fixtures/Contract.cs.txt",
]
before = [f.read_text(encoding="utf8") for f in files]
subprocess.run([sys.executable, str(p / "tools/generate.py")], check=True, cwd=p)
assert before == [
    f.read_text(encoding="utf8") for f in files
], "Generated files differ: run generate.py"
root = ET.parse(xml).getroot()
panels = list(root.iter())
allowed = {"Panel", "Label", "Button", "Image"}
attrs = {"id", "class", "hittest", "text", "src", "texturewidth", "textureheight"}
for el in panels:
    if el.tag in ("root", "styles", "include"):
        continue
    assert el.tag in allowed, (el.tag, el.attrib)
    assert set(el.attrib) <= attrs, (el.tag, el.attrib)
assert all(
    x.get("hittest") == "false" for x in panels if x.tag == "Label"
), "Decorative labels must not intercept clicks"
outer = next(x for x in root if x.tag == "Panel")
assert outer.get("hittest") == "false"
assert "id" not in outer.attrib and "outer" in outer.get("class", "").split()
ids = [x.get("id") for x in panels if x.get("id")]
assert len(ids) == len(set(ids)) and len(ids) < 1024
variables = set(re.findall(r"\{s:([^}]+)\}", xml.read_text(encoding="utf8")))
assert len(variables) < 1024
sty = css.read_text(encoding="utf8")
classes = set(re.findall(r"\.([A-Za-z][\w-]*)", re.sub(r"url\([^)]*\)", "", sty)))
assert len(classes) < 1024
assert "z-index: 99999" in sty and ".app { " in sty and "visibility: collapse" in sty
states = json.loads((p / "fixtures/states.json").read_text(encoding="utf8"))
for name, state in states.items():
    for op in ("add", "remove"):
        for id, cl in state.get(op, {}).items():
            assert id in ids, (name, id)
            for c in cl.split():
                assert c in classes, (name, id, c)
    for op in ("hide", "show"):
        for id in state.get(op, []):
            assert id in ids, (name, id)
assert len([i for i in ids if re.fullmatch(r"item_\d+", i)]) == 15
assert len(panels) < 500
contract = json.loads((p / "fixtures/contract.json").read_text(encoding="utf8"))
assert contract["ids"] == ids and set(contract["variables"]) == variables
for image in (addon / "materials/weaponpaints").glob("*.png"):
    assert image.with_suffix(".vtex").is_file(), image
print(
    f"OK: {len(panels)} XML elements, {len(ids)} IDs, {len(variables)} variables, {len(classes)} classes, {len(states)} fixture states; generated files reproducible"
)

subprocess.run(
    [sys.executable, str(p / "tools/validate_panorama.py"), str(addon)], check=True
)


subprocess.run([sys.executable, str(p / "tools/check_native_images.py")], check=True)
