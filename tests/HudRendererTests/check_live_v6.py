"""V6 retail proof invariants; never compile, publish or touch existing addons."""
import re,sys
from pathlib import Path
root=Path(__file__).resolve().parents[2]
addon=root/'hud-mockup/live-addon-v6/panorama'
main=(addon/'styles/custom_game/weaponpaints_v6.css').read_text(encoding='utf8')
stock=(addon/'styles/custom_game/weaponpaints_stock_v6.css').read_text(encoding='utf8')
source=(root/'hud-mockup/catalog/stock.css').read_text(encoding='utf8')
layout=(addon/'layout/custom_game/weaponpaints_v6.xml').read_text(encoding='utf8')
assert stock==source and 'background-size:' not in stock, 'stock bank must remain image-only'
assert not re.search(r'background-size\s*:\s*contains\b',main+'\n'+stock)
fits=re.findall(r'background-size\s*:\s*contain\s*;',main)
assert len(fits)==3, 'one shared art rule and two icon rules only'
art=re.search(r'\.art\s*\{([^{}]*)\}',main);assert art and all(x in art.group(1) for x in ('background-size: contain;', 'background-position: 50% 50%;','background-repeat: no-repeat;'))
for cls in ('backIcon','searchIcon'):
 rules=re.findall(r'\.'+cls+r'\s*\{([^{}]*)\}',main)
 assert any('background-image:' in x and 'background-size: contain;' in x and 'background-position: center;' in x and 'background-repeat: no-repeat;' in x for x in rules),cls
assert 'weaponpaints_v6.vcss_c' in layout and 'weaponpaints_stock_v6.vcss_c' in layout
assert 'height: fit-children' in main and '.app { ui-scale: 90%; }' in main
print('V6 shared fit, bank, icons, modal and immutable layout invariants passed')
