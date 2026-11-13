"""Build small cosmetic identities from explicit site/plugin IDs and economy model keys."""
import argparse,hashlib,json,pathlib,re
p=argparse.ArgumentParser();p.add_argument('--site',required=True,type=pathlib.Path);p.add_argument('--economy',required=True,type=pathlib.Path);p.add_argument('--plugin-data',required=True,type=pathlib.Path);p.add_argument('--out',type=pathlib.Path,default=pathlib.Path(__file__).resolve().parents[1]/'catalog/cosmetic-metadata.json');a=p.parse_args()
def read(path): return json.loads(path.read_text(encoding='utf8'))
data=a.site/'data';items=read(a.economy);siteAgents=read(data/'agents.json'); pluginAgents=read(a.plugin_data/'agents_ru.json')
agentAllowed={(int(x['team']),x['model']) for x in pluginAgents if x['model']!='null'}
models={}
for item in items:
 if item['type']=='agent':
  key=item.get('modelKey','')
  if not key.startswith('agents/models/'): continue
  key=key.removeprefix('agents/models/')
  if key in models: raise ValueError('duplicate agent model key')
  models[key]=item
entries=[]
for entry in siteAgents:
 team,model=int(entry['team']),entry['model']; item=models.get(model)
 if item is None or (team,model) not in agentAllowed: continue
 if item.get('team')!={2:0,3:1}[team]: raise ValueError('agent team mismatch')
 entries.append({'economyId':item['id'],'kind':'agent','team':team,'id':item['definitionIndex'],'model':model})
for kind,file,typ,index in [('music','music.json','musickit','variantIndex'),('pin','collectibles.json','collectible','definitionIndex')]:
 siteIds={int(z['id']) for z in read(data/file)}; pluginIds={int(z['id']) for z in read(a.plugin_data/('music_ru.json' if kind=='music' else 'collectibles_ru.json'))}
 for item in items:
  if item['type']==typ and item.get(index) in siteIds & pluginIds:entries.append({'economyId':item['id'],'kind':kind,'team':0,'id':item[index]})
byId={x['id']:x for x in items}
for row in read(data/'patches.json'):
 item=byId.get(row['economyId'])
 if item is None or item['type']!='patch' or item.get('variantIndex')!=row['id']: raise ValueError('patch mismatch')
 entries.append({'economyId':item['id'],'kind':'patch','team':0,'id':row['id']})
for row in read(data/'graffiti.json'):
 item=byId.get(row['id'])
 if item is None or item['type']!='graffiti' or item.get('variantIndex')!=row['graffiti'] or (item.get('tintIndex') or 0)!=row['tint']:raise ValueError('graffiti mismatch')
 entries.append({'economyId':item['id'],'kind':'graffiti','team':0,'id':row['graffiti'],'tint':row['tint']})
pet=read(a.site/'assets/pets/catalog.json');handler=(a.site/'handlers/save_pet.php').read_text(encoding='utf8')
match=re.search(r'\$hats\s*=\s*\[(.*?)\];',handler,re.S)
if not match:raise ValueError('missing site hat list')
hats=re.findall(r"'([^']*)'",match.group(1));petEntries=[]
for key,value in pet.items():
 for stage in (1,2,3):
  for variant in value['variants']:petEntries.append({'id':int(key),'stage':stage,'variant':variant['index'],'label':variant['label']})
if len({x['economyId'] for x in entries})!=len(entries):raise ValueError('duplicate economy ID')
files=[a.economy,a.site/'assets/pets/catalog.json',a.site/'handlers/save_pet.php']+[data/f for f in ('agents.json','music.json','collectibles.json','patches.json','graffiti.json')]+[a.plugin_data/f for f in ('agents_ru.json','music_ru.json','collectibles_ru.json')]
result={'version':1,'sourceSHA256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in files},'entries':sorted(entries,key=lambda x:x['economyId']),'pets':petEntries,'hats':hats}
a.out.parent.mkdir(parents=True,exist_ok=True);a.out.write_text(json.dumps(result,ensure_ascii=False,separators=(',',':'))+'\n',encoding='utf8');from collections import Counter
print('entries',len(entries),dict(Counter(x['kind'] for x in entries)),'pets',len(petEntries),'hats',len(hats))
