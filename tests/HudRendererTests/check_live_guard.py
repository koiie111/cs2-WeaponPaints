"""Verify the native-only partial-frame guard without modifying either addon."""
from pathlib import Path
import re
root = Path(__file__).resolve().parents[2] / 'hud-mockup'
live = (root / 'live-addon-v6/panorama/styles/custom_game/weaponpaints_v6.css').read_text(encoding='utf8')
mock = (root / 'addon/panorama/styles/custom_game/weaponpaints.css').read_text(encoding='utf8')
pattern = r'\.app\.rendering\s+\.main\s*\{\s*opacity\s*:\s*0\s*;\s*\}'
assert len(re.findall(pattern, live)) == 1, 'live partial-frame guard missing or duplicated'
assert not re.search(pattern, mock), 'approved mockup was changed'
print('Live rendering guard invariant passed')
