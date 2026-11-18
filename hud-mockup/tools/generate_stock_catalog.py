"""Generate an auditable, stock-only HUD catalogue from the local CS2 economy and VPK index.

No inferred artwork: ambiguous and missing paths intentionally have resource=null.
Ordinal is the stable economy ID; bank/cell are its quotient/remainder in base 256.
"""
import argparse, collections, hashlib, json, pathlib, re

ROOT = pathlib.Path(__file__).resolve().parents[1]
TYPES = {'weapon':'weapons','melee':'knives','glove':'gloves','agent':'agents',
         'patch':'patches','musickit':'music','collectible':'pins','graffiti':'graffiti',
         'sticker':'stickers','keychain':'charms','pet':'pet'}

def main():
    p = argparse.ArgumentParser()
    p.add_argument('--items', type=pathlib.Path, required=True)
    p.add_argument('--translations', type=pathlib.Path, required=True)
    p.add_argument('--index', type=pathlib.Path, required=True)
    p.add_argument('--items-game', type=pathlib.Path, required=True)
    p.add_argument('--capabilities', type=pathlib.Path, required=True)
    p.add_argument('--site-skins', type=pathlib.Path, required=True)
    p.add_argument('--out', type=pathlib.Path, default=ROOT / 'catalog')
    a = p.parse_args()
    items = json.loads(a.items.read_text(encoding='utf8'))
    names = {}
    for row in json.loads(a.site_skins.read_text(encoding='utf8')):
        idx, name = row['weapon_defindex'], row['weapon_name']
        if idx in names and names[idx] != name: raise ValueError('conflicting site weapon name')
        names[idx] = name
    source_by_id = {item['id']: item for item in items}
    site_caps = json.loads(a.capabilities.read_text(encoding='utf8'))
    def inherited(item, field):
        current, visited = item, set()
        while current and current['id'] not in visited:
            visited.add(current['id'])
            if current.get(field) is not None: return current[field]
            parent = source_by_id.get(current.get('parentId'))
            # Stub or unrelated parents are group containers, not model metadata.
            current = parent if parent and parent['type'] == item['type'] else None
        return None
    text = a.translations.read_text(encoding='utf8')
    translations = json.loads(text[text.index('= {') + 2:text.rindex(';')])
    data = a.index.read_bytes()
    index = {line.split()[0] for line in data.decode('utf8').splitlines() if line.strip()}
    by_name = collections.defaultdict(list)
    for path in sorted(index):
        name = path.rsplit('/', 1)[-1]
        for ext in ('_png.vtex_c', '_svg.vtex_c'):
            if name.endswith(ext): by_name[name[:-len(ext)].lower()].append(path)
    # The material is the authoritative directory for sticker kits; basenames
    # are not unique across tournament sets.
    game = a.items_game.read_text(encoding='utf8')
    kit_material = {}
    # Valve's decompiled file repeats the top-level sticker_kits block.
    for match in re.finditer(r'^\s*"sticker_kits"\s*$', game, re.MULTILINE):
        depth = 0
        kit = None
        pending = None
        for line in game[match.end():].splitlines():
            tokens = re.findall(r'"([^"]*)"', line)
            if depth == 1 and len(tokens) == 1 and tokens[0].isdigit(): pending = int(tokens[0])
            if depth == 2 and kit is not None and len(tokens) >= 2 and tokens[0] == 'sticker_material':
                kit_material[kit] = tokens[1]
            if '{' in line:
                depth += 1
                if depth == 2: kit, pending = pending, None
            if '}' in line:
                depth -= 1
                if depth == 1: kit = None
                if depth == 0: break
    tints = {}
    tint_section = game[game.index('"graffiti_tints"'):game.index('"player_loadout_slots"')]
    for tint_id, color in re.findall(r'"id"\s*"(\d+)"\s*"hex_color"\s*"(#[0-9a-fA-F]{6})"', tint_section):
        tints[int(tint_id)] = color
    records = []
    for item in sorted(items, key=lambda i:i['id']):
        typ = item['type']
        if typ not in TYPES: continue
        ordinal = item['id']
        defindex = inherited(item, 'definitionIndex')
        kit = item.get('variantIndex')
        team = inherited(item, 'team')
        limits = site_caps['items'].get(f"{defindex}:{kit if kit is not None else 0}") if typ in ('weapon','melee','glove') else None
        if not 0 <= ordinal < 65536: raise ValueError('economy ID exceeds 256x256')
        stem = re.sub(r'_[0-9a-f]{8}\.webp$', '', item.get('imagePath', '').rsplit('/',1)[-1])
        keys = [stem + '_light', stem] if typ in ('weapon','melee','glove') and not item.get('isBase') else [stem]
        paths = []
        for key in keys:
            paths = [v for v in by_name.get(key.lower(), []) if v.startswith('panorama/images/econ/')]
            if paths: break
        # Restrict by exact kit material from Valve's schema when available.
        if typ in ('sticker', 'graffiti') and item.get('variantIndex') in kit_material:
            material = kit_material[item['variantIndex']]
            paths = [v for v in paths if v == 'panorama/images/econ/stickers/' + material + '_png.vtex_c']
        # Sticker and spray directories can share a stem, but category is authoritative.
        if typ == 'sticker': paths = [v for v in paths if '/stickers/' in v and '/valve_sprays/' not in v]
        if typ == 'keychain': paths = [v for v in paths if '/keychains/' in v]
        if typ == 'graffiti' and item.get('tintIndex') in tints:
            material = kit_material.get(item.get('variantIndex'), '')
            base = material.rsplit('/',1)[-1]
            if stem == base + '_' + tints[item['tintIndex']][1:].lower():
                candidate = 'panorama/images/econ/stickers/' + material + '_png.vtex_c'
                paths = [candidate] if candidate in index else []
        resource = 's2r://' + paths[0][:-2] if len(paths) == 1 else None
        name = translations.get(str(ordinal), {}).get('name')
        records.append({'id':ordinal, 'category':TYPES[typ], 'defindex':defindex,
            'kit':kit, 'weaponName':names.get(defindex) if typ in ('weapon','melee','glove') else None, 'name':name or '', 'rarity':item.get('rarityColor'),
            'resource':resource, 'ordinal':ordinal, 'bank':ordinal//256, 'cell':ordinal%256,
            'base':bool(item.get('isBase')), 'team':team,
            'capabilities':{'stickerSlots':limits.get('stickerSchemaCount',0) if limits else 0,
                            'keychain':bool(limits and all('keychainPosition'+axis+bound in limits for axis in 'XYZ' for bound in ('Min','Max'))),
                            'wear':bool(limits and 'wearMin' in limits and 'wearMax' in limits),
                            'limits':limits, 'source':'site' if limits else None},
            'imageStatus':'mapped' if resource else ('ambiguous' if len(paths)>1 else 'missing'),
            'artBank': ordinal//256, 'artCell':ordinal%256, 'artKind':'native' if resource else 'fallback',
            'tintIndex': item.get('tintIndex'), 'displayedStickerId':item.get('displayedStickerId')})
    # Reuse the base spray image and the exact embedded sticker instead of
    # synthesising thousands of false preview textures.
    by_id = {r['id']: r for r in records}
    base_spray = {}
    for r in records:
        if r['category']=='graffiti' and r['resource']:
            base_spray.setdefault(r['kit'], r)
    for r in records:
        source = None
        if r['category']=='graffiti' and r['tintIndex']:
            source = base_spray.get(r['kit'])
            kind = 'tinted-spray-icon'
        elif r['category']=='charms' and r['displayedStickerId'] is not None:
            source = by_id.get(r['displayedStickerId'])
            kind = 'embedded-sticker-icon'
        if source and source['resource']:
            r['resource'] = source['resource']
            r['artBank'], r['artCell'] = source['bank'], source['cell']
            r['imageStatus'], r['artKind'] = 'mapped', kind
    a.out.mkdir(parents=True, exist_ok=True)
    (a.out/'stock.json').write_text(json.dumps({'version':1,'indexSHA256':hashlib.sha256(data).hexdigest(),'items':records},ensure_ascii=False,separators=(',',':'))+'\n',encoding='utf8')
    css = ''.join(f'.art.bank-{r["bank"]}.cell-{r["cell"]} {{ background-image: url("{r["resource"]}"); }}\n' for r in records if r['resource'] and r['artBank']==r['bank'] and r['artCell']==r['cell'])
    css += ''.join(f'.art.tint-{i} {{ wash-color: {color}; }}\n' for i,color in sorted(tints.items()))
    (a.out/'stock.css').write_text(css,encoding='utf8')
    counts = collections.defaultdict(collections.Counter)
    for r in records: counts[r['category']][r['imageStatus']]+=1
    print(json.dumps({'count':len(records),'classes':len({r['bank'] for r in records})+len({r['cell'] for r in records}), 'cssBytes':len(css.encode()),'counts':counts},ensure_ascii=False,indent=2))
if __name__=='__main__': main()
