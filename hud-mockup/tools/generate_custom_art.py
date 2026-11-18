"""Build exact-ID custom-model tiles from previously recorded editor GPU renders.

Inputs are local, explicitly supplied website catalogue and its verified render
report; no downloads or video thumbnails are used. Unknown runtime IDs receive
no artwork. Output only generated private/user-supplied custom assets.
"""
import argparse,hashlib,json,re
from pathlib import Path
from PIL import Image

EXPECTED={
 'daedalos':'weapon_ak47','ak_12':'weapon_ak47','ak_insane':'weapon_ak47',
 'ak_future_ag2':'weapon_ak47','ak_oni_ag2':'weapon_ak47','kuronami':'weapon_ak47',
 'prime_ak':'weapon_ak47','weapon_rif_ak47':'weapon_ak47',
 'm4a1s_daeldalus':'weapon_m4a1_silencer','weapon_rif_m4a1_silencer':'weapon_m4a1_silencer',
 'awp_insane':'weapon_awp','awp_animes':'weapon_awp','awp_afterglow_ag2':'weapon_awp',
 'scout_anim':'weapon_ssg08','m4_fool':'weapon_m4a1','m4_masyna_ag2':'weapon_m4a1',
 'm4_oni_ag2':'weapon_m4a1','weapon_pist_deagle':'weapon_deagle','weapon_pist_glock18':'weapon_glock',
}
ORDER=['daedalos']+sorted(k for k in EXPECTED if k!='daedalos')

def sha(data):return hashlib.sha256(data).hexdigest()
def main():
 p=argparse.ArgumentParser();p.add_argument('--catalog',required=True,type=Path);p.add_argument('--renders',required=True,type=Path);p.add_argument('--out',required=True,type=Path);p.add_argument('--manifest',required=True,type=Path);p.add_argument('--cs',required=True,type=Path);a=p.parse_args()
 raw=a.catalog.read_bytes();data=json.loads(raw.decode('utf8'));models={}
 for weapon,record in data['weapons'].items():
  for key in record.get('Skins',{}):
   if key in models:raise ValueError('duplicate configured model ID')
   models[key]=weapon
 if models!=EXPECTED:raise ValueError('configured model set or weapon identity differs from approved 19')
 report_raw=(a.renders/'report.json').read_bytes();report=json.loads(report_raw)
 desktop=[r for entry in report if entry.get('profile')=='desktop' for r in entry.get('results',[])]
 if len(desktop)!=19 or {x.get('name') for x in desktop}!=set(EXPECTED) or any(not isinstance(x.get('calls'),int) or x['calls']<1 for x in desktop):raise ValueError('missing verified GPU render')
 if a.out.exists():raise ValueError('output already exists')
 if a.manifest.exists() or a.cs.exists():raise ValueError('manifest or lookup already exists')
 material=a.out/'materials'/'weaponpaints';material.mkdir(parents=True)
 template=(Path(__file__).resolve().parents[1]/'addon/materials/weaponpaints/custom.vtex').read_text(encoding='utf8')
 if template.count('materials/weaponpaints/custom.png')!=1:raise ValueError('unexpected texture descriptor template')
 entries=[];rules=[]
 for index,key in enumerate(ORDER):
  source=a.renders/('desktop-'+key+'.png')
  if source.is_symlink() or not source.is_file():raise ValueError('render missing or symlink')
  blob=source.read_bytes();im=Image.open(source)
  if im.mode!='RGBA' or im.size!=(1100,800):raise ValueError('unexpected render format')
  bbox=im.getchannel('A').getbbox()
  if bbox is None:raise ValueError('empty render')
  crop=im.crop(bbox);crop.thumbnail((460,268),Image.Resampling.LANCZOS)
  tile=Image.new('RGBA',(512,308),(0,0,0,0));tile.alpha_composite(crop,((512-crop.width)//2,(308-crop.height)//2))
  stem=f'custom_model_v9_{index}';image=material/(stem+'.png');tile.save(image,optimize=True)
  (material/(stem+'.vtex')).write_text(template.replace('materials/weaponpaints/custom.png',f'materials/weaponpaints/{stem}.png'),encoding='utf8')
  rules.append(f'.art-custom-{index} {{ background-image: url("s2r://materials/weaponpaints/{stem}.vtex"); }}')
  entries.append({'index':index,'weapon':EXPECTED[key],'model':key,'source':'desktop-'+key+'.png','source_sha256':sha(blob),'png_sha256':sha(image.read_bytes()),'png_bytes':image.stat().st_size,'content_size':[crop.width,crop.height]})
 (a.out/'weaponpaints_custom_v9.css').write_text('\n'.join(rules)+'\n',encoding='utf8')
 manifest={'schema':1,'source_kind':'website_editor_gpu_render','catalog_sha256':sha(raw),'render_report_sha256':sha(report_raw),'image_size':[512,308],'fit_size':[460,268],'entries':entries}
 a.manifest.write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
 lines=['// Generated from custom-art-v9.json; unknown identities intentionally have no artwork.','namespace WeaponPaints.Hud;','','internal static class HudCustomArtwork','{','    internal static int? Index(string weapon, string model) => (weapon, model) switch','    {']
 for entry in entries:
  if not re.fullmatch(r'[a-z0-9_]+',entry['weapon']) or not re.fullmatch(r'[a-z0-9_]+',entry['model']):raise ValueError('unsafe identifier')
  lines.append(f'        ("{entry["weapon"]}", "{entry["model"]}") => {entry["index"]},')
 lines+=['        _ => null','    };','}']
 a.cs.write_text('\n'.join(lines)+'\n',encoding='utf8')
 print(json.dumps({'count':len(entries),'png_bytes':sum(e['png_bytes'] for e in entries),'manifest_sha256':sha(a.manifest.read_bytes())}))
if __name__=='__main__':main()
