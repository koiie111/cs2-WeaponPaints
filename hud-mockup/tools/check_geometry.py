from pathlib import Path
import importlib.util, subprocess, sys, tempfile, json
from playwright.sync_api import sync_playwright

p = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location(
    "preview", p / "tools/panorama_preview.py"
)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)
browser = mod.find_browser(None)
assert browser, "Chrome or Edge is required"
variants = [
    (1600, 900, ""),
    (1440, 900, "_compact"),
    (1280, 960, "_compact"),
    (1280, 720, "_compact"),
    (1024, 768, "_small"),
]
with tempfile.TemporaryDirectory() as tmp:
    subprocess.run(
        [
            sys.executable,
            str(p / "tools/panorama_preview.py"),
            str(p / "addon/panorama/layout/custom_game/weaponpaints.xml"),
            "--states",
            str(p / "fixtures/states.json"),
            "--out",
            tmp,
        ],
        check=True,
        stdout=subprocess.DEVNULL,
    )
    with sync_playwright() as pw:
        engine = pw.chromium.launch(executable_path=browser, headless=True)
        for w, h, suffix in variants:
            page = engine.new_page(viewport={"width": w, "height": h})
            for state in ("home", "finishes", "settings"):
                page.goto((Path(tmp) / f"weaponpaints.{state}{suffix}.html").as_uri())
                result = page.evaluate("""() => {
      const rect = el => { let r=el.getBoundingClientRect(); return {left:r.left,top:r.top,right:r.right,bottom:r.bottom,width:r.width,height:r.height}; };
      const shell=rect(document.querySelector('.shell')); const nodes=['back','team','close'].map(x=>[x,rect(document.querySelector(`[data-id="${x}"]`))]);
      return {shell,nodes};
    }""")
                b = result["shell"]
                assert (
                    b["left"] >= -1
                    and b["top"] >= -1
                    and b["right"] <= w + 1
                    and b["bottom"] <= h + 1
                ), (w, h, state, result)
                for name, b in result["nodes"]:
                    assert (
                        b["left"] >= 0
                        and b["right"] <= w
                        and b["top"] >= 0
                        and b["bottom"] <= h
                    ), (w, h, state, name, b)
            page.close()
        page = engine.new_page(viewport={"width": 1600, "height": 900})
        states = json.loads((p / "fixtures/states.json").read_text(encoding="utf8"))
        for state in states:
            if state.endswith(("_compact", "_small")):
                continue
            page.goto((Path(tmp) / f"weaponpaints.{state}.html").as_uri())
            issues = page.evaluate("""() => {
              const shell = document.querySelector('.shell').getBoundingClientRect();
              const visible = e => e.getClientRects().length && getComputedStyle(e).visibility !== 'hidden';
              const issues = [];
              for (const el of document.querySelectorAll('.pv-button,.pv-label')) {
                if (!visible(el)) continue;
                const r = el.getBoundingClientRect();
                if (r.left < shell.left-1 || r.right > shell.right+1 || r.top < shell.top-1 || r.bottom > shell.bottom+1)
                  issues.push(['outside shell', el.dataset.id || el.textContent, r.x, r.y]);
                if (el.classList.contains('pv-label') && el.scrollWidth > el.clientWidth+2)
                  issues.push(['text overflow', el.textContent, el.scrollWidth, el.clientWidth]);
              }
              const groups = '.toolbar,.cardRow,.settingRow,.valueControls,.featureRow,.decorRow,.overlayActions,.presetRow,.top';
              for (const group of document.querySelectorAll(groups)) {
                if (!visible(group)) continue;
                const children = [...group.children].filter(visible).map(el => [el, el.getBoundingClientRect()]);
                for (let i=0; i<children.length; i++) for(let j=i+1; j<children.length; j++) {
                  const [a,x]=children[i], [b,y]=children[j];
                  if (Math.min(x.right,y.right)-Math.max(x.left,y.left)>1 && Math.min(x.bottom,y.bottom)-Math.max(x.top,y.top)>1)
                    issues.push(['flow overlap', a.dataset.id || a.textContent, b.dataset.id || b.textContent]);
                }
              }
              return issues;
            }""")
            assert not issues, (state, issues)
        page.close()
        engine.close()
print(
    "OK: primary geometry in five viewport sizes and visible controls in all base states; browser approximation only"
)
