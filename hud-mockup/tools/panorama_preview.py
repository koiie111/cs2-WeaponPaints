#!/usr/bin/env python3
"""
panorama_preview - render a CS2 Panorama custom_hud_layout (.xml + .css) as browser HTML, optionally
filled with sample states and screenshotted to PNG with a local headless Chrome/Edge.

    python panorama_preview.py addon/panorama/layout/custom_game/menu.xml
    python panorama_preview.py menu.xml --states states.json --png --size 1000x820

WHAT IT IS. A translation good enough to judge spacing, hierarchy, colour, text budgets and artwork
placement in seconds instead of a compile -> VPK -> server -> join round trip.

WHAT IT IS NOT. Panorama is not a browser. Everything here is a hypothesis; the game is the authority.
Known gaps: `text-overflow: shrink` is shown as an ellipsis (nothing shrinks), `wash-color` is ignored,
game fonts are substituted (default Arial, which is WIDER than Stratum2, so a line that fits here fits
in game), label line boxes and glyph offsets differ from Panorama.

Translation (Panorama -> web):
  flow-children: down|right       -> flex column|row
  no flow-children                -> children overlay in one grid cell (Panorama's default stacking)
  horizontal-/vertical-align       -> auto margins / align-self
  visibility: collapse            -> display: none
  width|height: fit-children      -> fit-content / auto
  fill-parent-flow(n)             -> flex: n
  background-size: contains       -> contain
  box-shadow colour-first         -> web order
  url("s2r://materials/x.vtex")   -> materials/x.png next to the panorama/ tree, inlined as data URI
                                     (falls back to a placeholder gradient when the png is missing)
  gradient(linear, ...)           -> linear-gradient(...)

States file (JSON): {"state-name": {"vars": {"name": "text"}, "add": {"panel id": "classA classB"},
"remove": {"panel id": "Off"}, "hide": ["panel id"], "show": ["panel id"]}, ...}. Each state writes
<layout>.<state>.html (and .png with --png). Without --states one <layout>.preview.html is written with
variable names shown in italics.
"""
from __future__ import annotations

import argparse, base64, html, json, os, pathlib, re, shutil, subprocess, sys, xml.etree.ElementTree as ET

TAGS = {"Panel": "div", "Label": "div", "Button": "div", "Image": "div"}


def strip_comments(css: str) -> str:
    return re.sub(r'/\*.*?\*/', '', css, flags=re.S)


def translate_css(text: str, materials_root: pathlib.Path | None, font: str) -> str:
    def gradient(m):
        body = m.group(1)
        stops = re.findall(r'(?:from|to)\(\s*(#[0-9a-fA-F]+)\s*\)', body)
        mid = re.findall(r'color-stop\(\s*[\d.]+\s*,\s*(#[0-9a-fA-F]+)\s*\)', body)
        cols = ([stops[0]] + mid + stops[1:]) if len(stops) >= 2 else stops
        horiz = re.search(r'100%\s+0%', body) is not None
        return f"linear-gradient(to {'right' if horiz else 'bottom'}, {', '.join(cols) or '#444'})"

    cache: dict[str, str] = {}

    def texture(m):
        path = m.group(1)
        png = (materials_root / (path + '.png')) if materials_root else None
        if png and png.is_file():
            if path not in cache:
                cache[path] = 'data:image/png;base64,' + base64.b64encode(png.read_bytes()).decode('ascii')
            return f'background-image:url("{cache[path]}");'
        return 'background-image:linear-gradient(135deg,#ffffff22,#ffffff08);'

    t = strip_comments(text)
    t = re.sub(r'font-family:\s*Stratum2\s*;', 'font-family:Bahnschrift, Arial Narrow, Arial, sans-serif;', t)
    t = re.sub(r'gradient\(\s*(?:linear|radial)\s*,([^;]*?)\)\s*;', lambda m: gradient(m) + ";", t)
    t = re.sub(r'background-color:\s*(linear-gradient\([^;]+\));', r'background-image: \1;', t)
    t = re.sub(r'box-shadow:\s*(?:(inset|fill|hollow)\s+)?(#[0-9a-fA-F]+)((?:\s+-?[\d.]+px){2,4})\s*;',
               lambda m: f"box-shadow:{' inset' if m.group(1) == 'inset' else ''}{m.group(3)} {m.group(2)};", t)
    t = re.sub(r'flow-children:\s*down', "display:flex; flex-direction:column", t)
    t = re.sub(r'flow-children:\s*right', "display:flex; flex-direction:row", t)
    t = re.sub(r'flow-children:\s*none', "display:grid", t)
    t = re.sub(r'(width|height):\s*fill-parent-flow\(\s*([\d.]+)\s*\)', r"flex:\2 1 0", t)
    t = re.sub(r'width:\s*fit-children', "width:fit-content", t)
    t = re.sub(r'height:\s*fit-children', "height:auto", t)
    t = re.sub(r'(width|height):\s*width-percentage\([^)]*\)', r"\1:auto", t)
    # Panorama visibility never competes with layout: make collapse/visible !important so that, as in game,
    # only selector specificity between the visibility rules decides (.Pane{flow} must not resurrect .Off)
    t = re.sub(r'visibility:\s*collapse', "display:none !important", t)
    # `visibility: visible` reveals a collapsed panel: give it back the display its flow-children implies
    flows = {}
    for sel, body in re.findall(r'([^{}]+)\{([^{}]*)\}', t):
        if 'display:flex' in body:
            for c in re.findall(r'\.([A-Za-z][\w-]*)', sel):
                flows.setdefault(c, 'flex')

    def reveal(m):
        classes = re.findall(r'\.([A-Za-z][\w-]*)', m.group(1))
        mode = next((flows[c] for c in classes if c in flows), 'grid')
        return m.group(1) + '{' + re.sub(r'visibility:\s*visible', f'display:{mode} !important', m.group(2)) + '}'
    t = re.sub(r'([^{}]+)\{([^{}]*visibility:\s*visible[^{}]*)\}', reveal, t)
    t = re.sub(r'horizontal-align:\s*center', "margin-left:auto; margin-right:auto; justify-self:center", t)
    t = re.sub(r'horizontal-align:\s*right', "margin-left:auto; justify-self:end", t)
    t = re.sub(r'horizontal-align:\s*left', "margin-right:auto; justify-self:start", t)
    t = re.sub(r'vertical-align:\s*center', "align-self:center", t)
    t = re.sub(r'vertical-align:\s*top', "align-self:start", t)
    t = re.sub(r'vertical-align:\s*bottom', "align-self:end", t)
    t = re.sub(r'brightness:\s*([\d.]+)', r"filter:brightness(\1)", t)
    t = re.sub(r'(?:wash-color|ui-scale|clip|opacity-mask[\w-]*|sound|tooltip-position):\s*[^;]+;', "", t)
    t = re.sub(r'background-size:\s*contains', "background-size:contain", t)
    t = re.sub(r'background-image:\s*url\("s2r://([^"]+?)\.(?:vtex|png|psd)"\)\s*;', texture, t)
    t = re.sub(r'font-family:\s*Stratum2 Mono[^;]*;', "font-family:ui-monospace,monospace;", t)
    t = re.sub(r'font-family:\s*Stratum2 (?:Bold|Black|Heavy)[^;]*;', f"font-family:{font}; font-weight:700;", t)
    t = re.sub(r'font-family:\s*Stratum2[^;]*;', f"font-family:{font};", t)
    t = re.sub(r'text-overflow:\s*shrink\s*min\([^)]*\)\s*(ellipsis|clip)?', "text-overflow:ellipsis", t)
    t = re.sub(r'text-overflow:\s*shrink', "text-overflow:ellipsis", t)
    # Panorama: a label clips by default; keep white-space as authored (normal wraps, nowrap does not)
    # type selectors only (Label { }, .Row Label { }), never class names such as .Panel or .LabelBig
    for word, cls in (('Label', 'pv-label'), ('Button', 'pv-button'), ('Image', 'pv-image'), ('Panel', 'pv-panel')):
        t = re.sub(r'(?<![.#\w-])' + word + r'\b(?![\w-])(?=[^{}]*\{)', '.' + cls, t)
    return t


def render(node, out, populate, seen, parent=""):
    tag = TAGS.get(node.tag)
    if tag is None:
        return
    cls = node.get("class", "")
    ident = node.get("id", "")
    if "hidden" in cls.split() and populate:
        stem = parent + "/" + re.sub(r'\d+', "#", ident or cls)
        seen[stem] = seen.get(stem, 0) + 1
        if seen[stem] <= populate:
            cls = " ".join(c for c in cls.split() if c != "hidden")
    kind = {"Panel": "pv-panel", "Label": "pv-label", "Button": "pv-button", "Image": "pv-image"}[node.tag]
    attrs = f' class="{kind} {html.escape(cls)}"'
    attrs += f' data-id="{html.escape(ident)}"' if ident else ""
    text = node.get("text", "")
    body = ""
    if text:
        m = re.fullmatch(r'\{[sdgt]:([^}]+)\}', text.strip())
        body = f'<span class="pv-var" data-var="{html.escape(m.group(1))}">{html.escape(m.group(1))}</span>' if m else html.escape(text)
    out.append(f"<{tag}{attrs}>{body}")
    for child in node:
        render(child, out, populate, seen, ident or cls)
    out.append(f"</{tag}>")


BASE_CSS = """
  html,body{width:100%;height:100%} .pv-note{display:none!important}
  *{box-sizing:border-box;margin:0;padding:0;border:0 solid transparent}
  body{background:#12161c url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='40' height='40'%3E%3Cpath d='M0 0h40v40H0z' fill='%23161b22'/%3E%3Cpath d='M0 0h20v20H0zM20 20h20v20H20z' fill='%23181e26'/%3E%3C/svg%3E");
       min-height:100vh;display:flex;align-items:center;justify-content:center;color:#fff;font-family:__FONT__}
  /* Panorama default: a panel without flow-children overlays its children at the top-left, each sized to fit */
  .pv-panel,.pv-button,.pv-image{display:grid;grid-template:auto/auto;align-items:start;justify-items:start;flex-shrink:0}
  .pv-panel>*,.pv-button>*,.pv-image>*{grid-area:1/1}
  .pv-label{display:block;overflow:hidden;white-space:nowrap;flex-shrink:0;text-overflow:clip}
  .pv-button{cursor:pointer}
  .pv-var{opacity:.45;font-style:italic}
  .pv-var.pv-filled{opacity:1;font-style:normal}
  .pv-note{position:fixed;left:12px;bottom:10px;font:11px/1.5 ui-monospace,monospace;color:#5d6672;max-width:52ch}
"""


def build_html(src, css_blocks, body, title, font, overrides=""):
    return f"""<!doctype html><meta charset="utf-8"><title>{html.escape(title)}</title>
<style>{BASE_CSS.replace('__FONT__', font)}
{chr(10).join(css_blocks)}
  [data-id]{{transform:none !important}}
  .outer{{position:relative}} .app[data-id]{{position:absolute;left:50%;top:50%;margin:0!important;transform:translate(-50%,-50%)!important}}
  .app.compact[data-id]{{transform:translate(-50%,-50%) scale(.8)!important}} .app.small[data-id]{{transform:translate(-50%,-50%) scale(.65)!important}}
  .main{{position:relative}} .overlay{{position:absolute;left:50%;top:50%;margin-left:-260px;margin-top:-100px}}
  .hidden,.pv-force-hide{{display:none !important}}
  .pv-force-show{{display:revert !important}}
{overrides}
</style>
<div style="display:contents">{body}</div>
<div class="pv-note">{html.escape(src.name)} - approximation of Panorama: flexbox/grid stand in for flow-children, no shrink/wash-color,
fonts substituted. Judge spacing, hierarchy and text budgets here; judge everything else in game.</div>
"""


def apply_state(body: str, state: dict) -> str:
    for name, value in (state.get("vars") or {}).items():
        body = re.sub(r'<span class="pv-var" data-var="' + re.escape(name) + r'">[^<]*</span>',
                      lambda m: f'<span class="pv-var pv-filled" data-var="{html.escape(name)}">{html.escape(value)}</span>', body)

    def edit(ident, fn):
        nonlocal body
        body = re.sub(r'(<div class=")([^"]*)("[^>]*data-id="' + re.escape(ident) + r'")', lambda m: m.group(1) + fn(m.group(2)) + m.group(3), body)

    for ident, classes in (state.get("remove") or {}).items():
        drop = set(classes.split())
        edit(ident, lambda c: " ".join(x for x in c.split() if x not in drop))
    for ident, classes in (state.get("add") or {}).items():
        edit(ident, lambda c: c + " " + classes)
    for ident in state.get("hide") or []:
        edit(ident, lambda c: c + " pv-force-hide")
    for ident in state.get("show") or []:
        edit(ident, lambda c: c + " pv-force-show")
    return body


def find_browser(explicit: str | None):
    cands = [explicit, os.environ.get("CHROME_PATH"),
             r"C:\Program Files\Google\Chrome\Application\chrome.exe", r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
             r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
             "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
             shutil.which("google-chrome"), shutil.which("chromium"), shutil.which("chromium-browser"), shutil.which("chrome")]
    return next((c for c in cands if c and pathlib.Path(c).is_file()), None)


def screenshot(browser, page: pathlib.Path, png: pathlib.Path, size: str):
    from playwright.sync_api import sync_playwright
    w, h = (int(x) for x in size.lower().split("x"))
    with sync_playwright() as pw:
        engine = pw.chromium.launch(executable_path=browser, headless=True)
        tab = engine.new_page(viewport={"width": w, "height": h}, device_scale_factor=1)
        tab.goto(page.resolve().as_uri(), wait_until="load")
        tab.screenshot(path=str(png.resolve()))
        engine.close()
    return png.is_file()


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("layout", type=pathlib.Path, help="panorama/layout/custom_game/<name>.xml")
    ap.add_argument("--states", type=pathlib.Path, help="JSON file of named states (see module docstring)")
    ap.add_argument("--only", help="comma-separated state names to render")
    ap.add_argument("--out", type=pathlib.Path, help="output directory (default: <addon>/previews, outside panorama/)")
    ap.add_argument("--populate", type=int, default=3, help="un-hide this many members of each 'hidden' pool (default 3)")
    ap.add_argument("--font", default="Bahnschrift, Arial Narrow, Arial, sans-serif", help="stand-in for Stratum2 (default Arial: wider, so conservative)")
    ap.add_argument("--css", action="append", default=[], type=pathlib.Path, help="extra preview-only CSS appended last")
    ap.add_argument("--png", action="store_true", help="also screenshot every page with headless Chrome/Edge")
    ap.add_argument("--size", default="1280x960", help="screenshot window WxH (default 1280x960)")
    ap.add_argument("--browser", help="path to chrome/msedge (else CHROME_PATH or the usual install paths)")
    a = ap.parse_args()

    src = a.layout
    root = ET.fromstring(src.read_text(encoding="utf-8"))
    panorama = src.resolve().parents[2]
    addon = panorama.parent
    css = []
    for inc in root.iter("include"):
        name = (inc.get("src") or "").rsplit("/", 1)[-1]
        name = re.sub(r'\.vcss_c$', '.css', name)
        cand = panorama / "styles" / "custom_game" / name
        if cand.exists():
            css.append(f"/* ---- {name} ---- */\n" + translate_css(cand.read_text(encoding="utf-8"), addon, a.font))
        else:
            print(f"warning: stylesheet {name} not found at {cand}", file=sys.stderr)
    out = []
    for child in root:
        if child.tag not in ("styles", "scripts", "snippets"):
            render(child, out, a.populate, {})
    body = "".join(out)
    extra = "\n".join(p.read_text(encoding="utf-8") for p in a.css)
    out_dir = a.out or (addon / "previews")
    out_dir.mkdir(parents=True, exist_ok=True)

    pages = []
    if a.states:
        states = json.loads(a.states.read_text(encoding="utf-8"))
        wanted = set(a.only.split(",")) if a.only else None
        for name, state in states.items():
            if name.startswith("_") or (wanted and name not in wanted):
                continue
            page = out_dir / f"{src.stem}.{name}.html"
            page.write_text(build_html(src, css, apply_state(body, state), f"{src.stem} - {name}", a.font, extra), encoding="utf-8")
            pages.append(page)
    else:
        page = out_dir / f"{src.stem}.preview.html"
        page.write_text(build_html(src, css, body, f"{src.stem} preview", a.font, extra + "\n  [data-id]{opacity:1 !important}"), encoding="utf-8")
        pages.append(page)

    browser = find_browser(a.browser) if a.png else None
    if a.png and not browser:
        print("warning: no Chrome/Edge found for --png (set CHROME_PATH or --browser)", file=sys.stderr)
    for page in pages:
        line = str(page)
        if browser:
            png = page.with_suffix(".png")
            line += "  ->  " + (str(png) if screenshot(browser, page, png, a.size) else "screenshot FAILED")
        print(line)


if __name__ == "__main__":
    main()
