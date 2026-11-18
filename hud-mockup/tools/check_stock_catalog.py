"""Check generated stock catalogue and its exact VPK resource references."""
import argparse, json, pathlib, re, hashlib
root=pathlib.Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('--site-skins',required=True,type=pathlib.Path);p.add_argument('--index',required=True,type=pathlib.Path); p.add_argument('--capabilities',required=True,type=pathlib.Path); a=p.parse_args()
data=a.index.read_bytes(); paths={line.split()[0] for line in data.decode('utf8').splitlines() if line.strip()}
siteNames={}
for item in json.loads(a.site_skins.read_text(encoding='utf8')):
 name=siteNames.setdefault(item['weapon_defindex'],item['weapon_name']);assert name==item['weapon_name']
site=json.loads(a.capabilities.read_text(encoding='utf8'))['items']
catalog=json.loads((root/'catalog/stock.json').read_text(encoding='utf8')); records=catalog['items']; css=(root/'catalog/stock.css').read_text(encoding='utf8')
assert hashlib.sha256(data).hexdigest()==catalog['indexSHA256']
assert len({r['id'] for r in records})==len(records)
for r in records:
 assert r['name'] and 0<=r['bank']<256 and 0<=r['cell']<256
 assert (r['bank'],r['cell'])==divmod(r['id'],256)
 if r['category'] in ('weapons','knives','gloves'):
  assert r['weaponName']==siteNames.get(r['defindex'])
  limits=site.get(f"{r['defindex']}:{r['kit'] if r['kit'] is not None else 0}")
  assert r['capabilities']['limits']==limits
  assert r['capabilities']['stickerSlots']==(limits.get('stickerSchemaCount',0) if limits else 0)
 else: assert r['capabilities']['limits'] is None
 if r['resource']:
  assert r['resource'].startswith('s2r://') and r['resource'][6:]+'_c' in paths, r['id']
  selector=f'.art.bank-{r["artBank"]}.cell-{r["artCell"]} {{ background-image: url("{r["resource"]}"); }}'
  assert selector in css, r['id']
 else: assert r['artKind']=='fallback'
 if r['artKind']=='tinted-spray-icon': assert f'.art.tint-{r["tintIndex"]} ' in css
 if r['artKind']=='embedded-sticker-icon': assert r['displayedStickerId'] is not None
weapons=[r for r in records if r['category'] in ('weapons','knives','gloves')]
print(f'OK: {len(records)} items, {sum(bool(r["resource"]) for r in records)} mapped; {sum(r["weaponName"] is not None for r in weapons)}/{len(weapons)} weapon names from site, all mappings verified against VPK index')
