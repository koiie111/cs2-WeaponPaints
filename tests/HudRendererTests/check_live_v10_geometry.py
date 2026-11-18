"""Check V10 home and navigation alignment at the native scale classes in browser approximation."""
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
        str(HUD/'live-addon-v10/panorama/layout/custom_game/weaponpaints_v10.xml'),
        '--states',str(HUD/'fixtures/states-v10.json'),'--only','home,home_compact,home_small,settings,settings_compact,settings_small,finishes,finishes_compact,finishes_small,no_plus_graffiti,no_plus_graffiti_compact,no_plus_graffiti_small,no_plus_pet,no_plus_pet_compact,no_plus_pet_small',
        '--out',tmp],check=True,stdout=subprocess.DEVNULL)
    with sync_playwright() as pw:
        engine=pw.chromium.launch(executable_path=browser,headless=True)
        for width,height,suffix in ((1600,900,''),(1440,900,'_compact'),(1280,960,'_compact'),(1280,720,'_compact'),(1024,768,'_small')):
            page=engine.new_page(viewport={'width':width,'height':height})
            page.goto((Path(tmp)/f'weaponpaints_v10.home{suffix}.html').as_uri())
            navigation=page.evaluate("""() => {
              const rect=e=>{const r=e.getBoundingClientRect();return {l:r.left,r:r.right,t:r.top,b:r.bottom}};
              const side=document.querySelector('.side'); const brand=side.querySelector('.brand');
              return {side:rect(side),brand:rect(brand),brandText:brand.textContent,
                brandOverflow:brand.scrollWidth>brand.clientWidth+1,
                rows:Array.from(side.querySelectorAll('.nav')).map(row=>({box:rect(row),
                  rail:rect(row.querySelector('.navIconRail')),icon:rect(row.querySelector('.navPicture')),label:rect(row.querySelector('.navText')),
                  overflow:row.querySelector('.navText').scrollWidth>row.querySelector('.navText').clientWidth+1}))};
            }""")
            assert navigation['brandText']=='Скинченджер / 4ill.ru' and not navigation['brandOverflow'],navigation
            for index,row in enumerate(navigation['rows']):
                assert not row['overflow'] and row['icon']['r']<=row['label']['l']+1,navigation
                gap=row['label']['l']-row['rail']['r']
                scale=1 if not suffix else .8 if suffix=='_compact' else .65
                assert abs(gap-11*scale)<1.1,navigation
                assert abs((row['rail']['l']+row['rail']['r'])/2-(row['icon']['l']+row['icon']['r'])/2)<1.1,navigation
                assert abs((row['rail']['t']+row['rail']['b'])/2-(row['icon']['t']+row['icon']['b'])/2)<1.1,navigation
                assert abs(row['label']['l']-navigation['rows'][0]['label']['l'])<1.1,navigation
                assert row['box']['l']>=navigation['side']['l']-1 and row['box']['r']<=navigation['side']['r']+1,navigation
                if index: assert navigation['rows'][index-1]['box']['b']<=row['box']['t']+1,navigation
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
            page.goto((Path(tmp)/f'weaponpaints_v10.settings{suffix}.html').as_uri())
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
            charm=page.evaluate('''() => {
              const box=document.querySelector('[data-id="charm"]');
              if (!box.classList.contains('empty')) throw Error('Fixture charm is not empty');
              const label=box.querySelector('.charmPlus');
              const rect=e=>{const r=e.getBoundingClientRect();return {x:r.x,y:r.y,w:r.width,h:r.height}};
              return {box:rect(box),label:rect(label)};
            }''')
            assert abs((charm['box']['x']+charm['box']['w']/2)-(charm['label']['x']+charm['label']['w']/2))<1.1,charm
            assert abs((charm['box']['y']+charm['box']['h']/2)-(charm['label']['y']+charm['label']['h']/2))<1.1,charm
            page.goto((Path(tmp)/f'weaponpaints_v10.finishes{suffix}.html').as_uri())
            pager=page.evaluate("""() => {
              const rect=e=>{const r=e.getBoundingClientRect();return {x:r.x,y:r.y,w:r.width,h:r.height}};
              const previous=document.querySelector('[data-id="prev"]');const next=document.querySelector('[data-id="next"]');
              const box=document.querySelector('.pageNumberBox');const label=box.querySelector('.pageNumber');
              const child=label.querySelector('.pv-var')||label;const text=rect(child);
              return {previous:rect(previous),next:rect(next),box:rect(box),label:rect(label),text};
            }""")
            cy=lambda r:r['y']+r['h']/2
            assert abs(cy(pager['box'])-cy(pager['previous']))<1.1,pager
            assert abs(cy(pager['box'])-cy(pager['next']))<1.1,pager
            assert abs(cy(pager['label'])-cy(pager['box']))<1.1,pager
            assert abs(cy(pager['text'])-cy(pager['box']))<2.1,pager
            for state in ('no_plus_graffiti','no_plus_pet'):
                page.goto((Path(tmp)/f'weaponpaints_v10.{state}{suffix}.html').as_uri())
                offer=page.evaluate("""() => {
                  const rect=e=>{const r=e.getBoundingClientRect();return {x:r.x,y:r.y,right:r.right,bottom:r.bottom,w:r.width,h:r.height}};
                  return {plus:rect(document.querySelector('[data-id="plus"]')),apply:rect(document.querySelector('[data-id="apply"]')),
                    shell:rect(document.querySelector('.shell')),preview:rect(document.querySelector('[data-id="preview_panel"]'))};
                }""")
                assert offer['plus']['bottom']<=offer['apply']['y']+1,offer
                assert offer['plus']['w']>offer['apply']['w'],offer
                assert offer['apply']['bottom']<=offer['shell']['bottom']+1,offer
            page.close()
        engine.close()
print('V10 home, pager, plus offer, empty charm and image-only slots passed at five viewport/scale combinations (browser approximation)')
