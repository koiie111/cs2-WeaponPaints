"""Verify the runtime contract, packaged file and versioned native XML agree."""
import argparse
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--output', required=True, help='built WeaponPaints plugin directory')
args = parser.parse_args()
surface = (root / 'Hud/HudSurface.cs').read_text(encoding='utf-8')
controller = (root / 'WeaponPaints.Hud.cs').read_text(encoding='utf-8')
match = re.search(r'internal\s+const\s+string\s+ResourceVersion\s*=\s*"([0-9]+)"\s*;', surface)
assert match, 'HudSurface.ResourceVersion missing'
version = match.group(1)
assert re.search(r'Layout\s*=\s*"panorama/layout/custom_game/weaponpaints_v"\s*\+\s*ResourceVersion\s*\+\s*"\.vxml_c"', surface), 'layout does not use resource version'
assert re.search(r'ContractFile\s*=\s*"hud-live-contract-v"\s*\+\s*ResourceVersion\s*\+\s*"\.json"', surface), 'contract does not use resource version'
assert re.search(r'HudLayoutContract\.Load\s*\(\s*Path\.Combine\s*\(\s*ModuleDirectory\s*,\s*"data"\s*,\s*HudSurface\.ContractFile\s*\)\s*\)', controller), 'runtime entrypoint does not load the versioned contract'
name = f'hud-live-contract-v{version}.json'
source = root / 'hud-mockup' / 'catalog' / name
project = ET.parse(root / 'WeaponPaints.csproj').getroot()
entries = [n for n in project.iter('None') if n.get('Link') == f'data/{name}']
assert len(entries) == 1 and entries[0].get('Include') == f'hud-mockup/catalog/{name}' and entries[0].get('CopyToOutputDirectory') in ('Always', 'PreserveNewest'), 'project must copy the matching contract'
contract = json.loads(source.read_text(encoding='utf-8'))
output = Path(args.output) / 'data' / name
assert output.read_bytes() == source.read_bytes(), 'built contract missing or different from source'
layout = root / f'hud-mockup/live-addon-v{version}/panorama/layout/custom_game/weaponpaints_v{version}.xml'
nodes = list(ET.parse(layout).getroot().iter())
authored = {n.get('id'): n.get('class', '').split() for n in nodes if n.get('id')}
assert len(authored) == sum(bool(n.get('id')) for n in nodes), 'duplicate layout panel IDs'
variables = sorted({name for n in nodes for value in n.attrib.values() for name in re.findall(r'\{s:([A-Za-z0-9_-]+)\}', value)})
buttons = sorted(n.get('id') for n in nodes if n.tag == 'Button' and n.get('id'))
assert contract == {'version': 1, 'authoredClasses': authored, 'variables': variables, 'buttons': buttons}, 'contract differs from the versioned native XML'
print(f'PASS: runtime layout v{version}, XML contract and built data agree')
