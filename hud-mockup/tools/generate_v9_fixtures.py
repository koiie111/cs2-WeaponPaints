"""Derive V9 review states without changing the frozen mockup fixtures."""
import copy
import json
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
source=json.loads((ROOT/'fixtures/states.json').read_text(encoding='utf8'))
pet=copy.deepcopy(source['pet_options'])
pet['add']['plus']='show';pet['add']['apply']='disabled'
source['no_plus_pet']=pet

graffiti=copy.deepcopy(source['pet_options'])
graffiti['add'].pop('nav_pet',None)
graffiti['add'].update({'nav_graffiti':'active','plus':'show','apply':'disabled',
    'preview_panel':'tier-4b69ff','preview_art':'art-graffiti-0','feature_art':'art-graffiti-0'})
for i in range(1,8):graffiti['add'][f'feature_row_{i}']='is-hidden'
graffiti['vars'].update({'breadcrumb':'Граффити','title':'Эйс','preview_name':'Граффити | Эйс',
    'feature_context':'Граффити','feature_name':'Эйс','feature_label_0':'','feature_value_0':'Снять'})
source['no_plus_graffiti']=graffiti
# The historical AK fixture was illustrative, but would imply the wrong policy.
source['no_plus']=copy.deepcopy(graffiti)
for name in ('no_plus_pet','no_plus_graffiti'):
    for suffix,scale in (('_compact','compact'),('_small','small')):
        state=copy.deepcopy(source[name]);state['add']['wp_root']='show '+scale;source[name+suffix]=state
(ROOT/'fixtures/states-v9.json').write_text(json.dumps(source,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
print('Generated V9 graffiti/pet Plus review fixtures')
