"""Check V7 home alignment at the native scale classes in browser approximation."""
import importlib.util
from pathlib import Path
import subprocess
import sys
import tempfile
from playwright.sync_api import sync_playwright

ROOT=Path(__file__).resolve().parents[2]
HUD=ROOT/'hud-mockup'
spec=importlib.util.spec_from_file_location('preview',HUD/'tools/panorama_preview.py')
preview=importlib.util.module_from_spec(spec);spec.loader.exec_module(preview)
browser=preview.find_browser(None)
if not browser:raise RuntimeError('Chrome or Edge required')
with tempfile.TemporaryDirectory() as tmp:
    subprocess.run([sys.executable,str(HUD/'tools/panorama_preview.py'),
        str(HUD/'live-addon-v7/panorama/layout/custom_game/weaponpaints_v7.xml'),
        '--states',str(HUD/'fixtures/states.json'),'--only','home,home_compact,home_small,settings,settings_compact,settings_small',
        '--out',tmp],check=True,stdout=subprocess.DEVNULL)
    with sync_playwright() as pw:
        engine=pw.chromium.launch(executable_path=browser,headless=True)
        for width,height,suffix in ((1600,900,''),(1440,900,'_compact'),(1280,960,'_compact'),(1280,720,'_compact'),(1024,768,'_small')):
            page=engine.new_page(viewport={'width':width,'height':height})
            page.goto((Path(tmp)/f'weaponpaints_v7.home{suffix}.html').as_uri())
            rectangles=page.evaluate('''() => {
              const ids=['rifles','pistols','smg','shotguns','snipers','heavy','zeus'];
              return Object.fromEntries(ids.map(id=>{
                const r=document.querySelector(`[data-id="category_${id}"]`).getBoundingClientRect();
                return [id,{left:r.left,top:r.top,right:r.right,bottom:r.bottom}];
              }));
            }''')
            rifle,pistol,smg=(rectangles[k] for k in ('rifles','pistols','smg'))
            assert abs(rifle['top']-pistol['top'])<1.1,rectangles
            assert abs(rifle['bottom']-smg['bottom'])<1.1,rectangles
            tiles=[rectangles[k] for k in ('shotguns','snipers','heavy','zeus')]
            assert abs(tiles[0]['left']-rifle['left'])<1.1,rectangles
            assert abs(tiles[-1]['right']-pistol['right'])<1.1,rectangles
            gaps=[tiles[i+1]['left']-tiles[i]['right'] for i in range(3)]
            assert min(gaps)>0 and max(gaps)-min(gaps)<1.1,rectangles
            assert max(t['top'] for t in tiles)-min(t['top'] for t in tiles)<1.1,rectangles
            assert max(t['bottom'] for t in tiles)-min(t['bottom'] for t in tiles)<1.1,rectangles
            page.goto((Path(tmp)/f'weaponpaints_v7.settings{suffix}.html').as_uri())
            slots=page.evaluate("""() => Array.from({length:5},(_,i)=>{
              const slot=document.querySelector(`[data-id="decor_${i}"]`);
              const box=slot.getBoundingClientRect();const art=slot.querySelector('.decorThumb').getBoundingClientRect();
              const plus=slot.querySelector('.buttonText').getBoundingClientRect();
              return {box:{x:box.x,y:box.y,w:box.width,h:box.height},
                art:{x:art.x,y:art.y,w:art.width,h:art.height},
                plus:{x:plus.x,y:plus.y,w:plus.width,h:plus.height}, labels:slot.querySelectorAll('.pv-label').length};
            })""")
            for slot in slots:
                box,art,plus=(slot[k] for k in ('box','art','plus'))
                for child in (art,plus):
                    assert abs((box['x']+box['w']/2)-(child['x']+child['w']/2))<1.1,slot
                    assert abs((box['y']+box['h']/2)-(child['y']+child['h']/2))<1.1,slot
                assert slot['labels']==1,slot
            page.close()
        engine.close()
print('V7 home alignment and centred image-only slots passed at five viewport/scale combinations (browser approximation)')
