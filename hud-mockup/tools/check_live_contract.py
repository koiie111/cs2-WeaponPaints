"""Conservatively count the live entity's combined ID/class/dialog-variable contract."""
import argparse, pathlib, re, xml.etree.ElementTree as ET
p=argparse.ArgumentParser();p.add_argument('addon',type=pathlib.Path);p.add_argument('--limit',type=int,default=1024);p.add_argument('--version',choices=('2','3','4','6'));a=p.parse_args()
base=a.addon/'panorama'; version=a.version or next((v for v in ('6','4','3','2') if (base/'layout/custom_game'/f'weaponpaints_v{v}.xml').exists()),None)
if version is None: raise ValueError('No versioned live layout found')
layout=base/'layout/custom_game'/f'weaponpaints_v{version}.xml'; tree=ET.parse(layout)
ids=set();classes=set();variables=set();styles=[]
for node in tree.iter():
 if node.get('id'): ids.add(node.get('id'))
 classes.update(node.get('class','').split())
 for value in node.attrib.values(): variables.update(re.findall(r'\{s:([A-Za-z0-9_-]+)\}',value))
 if node.tag=='include':
  name=node.get('src','').rsplit('/',1)[-1]
  if not name.endswith('.vcss_c'): raise ValueError('Unexpected stylesheet reference')
  styles.append(base/'styles/custom_game'/(name[:-7]+'.css'))
if not styles: raise ValueError('No stylesheets')
for path in styles:
 css=re.sub(r'/\*.*?\*/','',path.read_text(encoding='utf8'),flags=re.S)
 for selector in re.findall(r'([^{}]+)\{[^{}]*\}',css):
  classes.update(re.findall(r'\.([A-Za-z_][\w-]*)',selector))
  ids.update(re.findall(r'#([A-Za-z_][\w-]*)',selector))
all_names=ids|classes|variables
print(f'IDs={len(ids)}, classes={len(classes)}, dialog variables={len(variables)}, union={len(all_names)}, limit={a.limit}')
if max(len(ids),len(classes),len(variables),len(all_names))>a.limit: raise SystemExit('FAIL: entity string budget exceeded')
print('OK: conservative combined entity string budget')
