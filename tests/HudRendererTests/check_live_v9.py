"""V9 retail proof invariants; never compile, publish or touch existing addons."""
import re,sys
from pathlib import Path
root=Path(__file__).resolve().parents[2]
addon=root/'hud-mockup/live-addon-v9/panorama'
main=(addon/'styles/custom_game/weaponpaints_v9.css').read_text(encoding='utf8')
stock=(addon/'styles/custom_game/weaponpaints_stock_v9.css').read_text(encoding='utf8')
source=(root/'hud-mockup/catalog/stock.css').read_text(encoding='utf8')
layout=(addon/'layout/custom_game/weaponpaints_v9.xml').read_text(encoding='utf8')
assert stock==source and 'background-size:' not in stock, 'stock bank must remain image-only'
assert not re.search(r'background-size\s*:\s*contains\b',main+'\n'+stock)
fits=re.findall(r'background-size\s*:\s*contain\s*;',main)
assert len(fits)==3, 'one shared art rule and two icon rules only'
art=re.search(r'\.art\s*\{([^{}]*)\}',main);assert art and all(x in art.group(1) for x in ('background-size: contain;', 'background-position: 50% 50%;','background-repeat: no-repeat;'))
for cls in ('backIcon','searchIcon'):
 rules=re.findall(r'\.'+cls+r'\s*\{([^{}]*)\}',main)
 assert any('background-image:' in x and 'background-size: contain;' in x and 'background-position: center;' in x and 'background-repeat: no-repeat;' in x for x in rules),cls
assert 'weaponpaints_v9.vcss_c' in layout and 'weaponpaints_stock_v9.vcss_c' in layout
assert 'height: fit-children' in main and '.app { ui-scale: 90%; }' in main
import xml.etree.ElementTree as ET
xml=ET.fromstring(layout)
for i in range(5):
 slot=xml.find(f".//Button[@id='decor_{i}']")
 assert slot is not None and slot.find(f"Panel[@id='decor_art_{i}']") is not None
 labels=slot.findall('Label')
 assert len(labels)==1 and labels[0].get('text')=='+', 'sticker slots must have no name labels'
assert xml.find(".//Panel[@id='charm_art']") is not None
footer=xml.find(".//Panel[@id='catalog_footer']")
assert footer is not None
box=footer.find("Panel[@class='pageNumberBox']")
assert box is not None and box.get('hittest')=='false'
count=box.find("Label[@class='buttonText pageNumber']")
assert count is not None and count.get('hittest')=='false' and count.get('text')=='{s:page}'
assert '.pageNumberBox { width: 70px; height: 26px; }' in main
assert '.catalogFooter .page { width: 40px; height: 26px; }' in main
charm=xml.find(".//Button[@id='charm']")
plus=charm.find("Label[@class='charmPlus']")
assert plus is not None and plus.get('text')=='+' and plus.get('hittest')=='false'
assert '.charmValue.empty .charmPlus { visibility: visible; }' in main
assert '.previewColumn, .heroArt { border-bottom: 3px solid #718998; }' in main
for color in ('ded6cc','b0c3d9','5e98d9','4b69ff','8847ff','d32ce6','eb4b4b','e4ae39'):
 assert f'.previewColumn.tier-{color}, .heroArt.tier-{color} {{ border-bottom-color: #{color}; }}' in main
plus=xml.find(".//Panel[@id='plus']");apply=xml.find(".//Button[@id='apply']")
row=next((e for e in xml.iter() if plus in list(e) and apply in list(e)),None)
assert row is not None and list(row).index(plus)<list(row).index(apply)
assert '.plusTitle { font-size: 16px; }' in main
assert 'custom_v9.vtex' not in main
for i in range(19):
 assert main.count(f'.art-custom-{i} {{ background-image:')==1
 for ext in ('png','vtex'):assert (root/f'hud-mockup/live-addon-v9/materials/weaponpaints/custom_model_v9_{i}.{ext}').is_file()
print('V9 rarity borders, larger Plus, native structure and custom asset invariants passed')
