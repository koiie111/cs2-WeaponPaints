"""V8 retail proof invariants; never compile, publish or touch existing addons."""
import re,sys
from pathlib import Path
root=Path(__file__).resolve().parents[2]
addon=root/'hud-mockup/live-addon-v8/panorama'
main=(addon/'styles/custom_game/weaponpaints_v8.css').read_text(encoding='utf8')
stock=(addon/'styles/custom_game/weaponpaints_stock_v8.css').read_text(encoding='utf8')
source=(root/'hud-mockup/catalog/stock.css').read_text(encoding='utf8')
layout=(addon/'layout/custom_game/weaponpaints_v8.xml').read_text(encoding='utf8')
assert stock==source and 'background-size:' not in stock, 'stock bank must remain image-only'
assert not re.search(r'background-size\s*:\s*contains\b',main+'\n'+stock)
fits=re.findall(r'background-size\s*:\s*contain\s*;',main)
assert len(fits)==3, 'one shared art rule and two icon rules only'
art=re.search(r'\.art\s*\{([^{}]*)\}',main);assert art and all(x in art.group(1) for x in ('background-size: contain;', 'background-position: 50% 50%;','background-repeat: no-repeat;'))
for cls in ('backIcon','searchIcon'):
 rules=re.findall(r'\.'+cls+r'\s*\{([^{}]*)\}',main)
 assert any('background-image:' in x and 'background-size: contain;' in x and 'background-position: center;' in x and 'background-repeat: no-repeat;' in x for x in rules),cls
assert 'weaponpaints_v8.vcss_c' in layout and 'weaponpaints_stock_v8.vcss_c' in layout
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
print('V8 native structure, pager geometry, charm state, shared fit and bank invariants passed')
