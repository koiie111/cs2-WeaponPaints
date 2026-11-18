"""Check transparent custom art fits V9's clipped hero viewport geometrically."""
from pathlib import Path
from PIL import Image

ROOT=Path(__file__).resolve().parents[2]
art=ROOT/'hud-mockup/custom-art-v9/materials/weaponpaints'
css=(ROOT/'hud-mockup/live-addon-v9/panorama/styles/custom_game/weaponpaints_v9.css').read_text(encoding='utf8')
assert '.heroImage.art-custom-10, .heroImage.art-custom-15, .heroImage.art-custom-16 { width: 400px; height: 240px; margin-top: -8px; }' in css
for index in range(19):
    image=Image.open(art/f'custom_model_v9_{index}.png').convert('RGBA')
    bounds=image.getchannel('A').getbbox()
    assert bounds,index
    boxw,boxh,top=(400,240,-8) if index in (10,15,16) else (560,336,-55)
    scale=min(boxw/image.width,boxh/image.height)
    drawn_h=image.height*scale
    offset=top+(boxh-drawn_h)/2
    top_px=offset+bounds[1]*scale;bottom_px=offset+bounds[3]*scale
    assert top_px>=-0.5 and bottom_px<=222.5,(index,bounds,top_px,bottom_px)
    # Both card modes and the settings preview contain the complete source
    # canvas inside their own panels, which themselves fit their parent boxes.
    for boxw,boxh,top,parent_height in ((186,112,0,150),(210,126,22,245),(530,318,55,620)):
        scale=min(boxw/image.width,boxh/image.height)
        x0=(boxw-image.width*scale)/2+bounds[0]*scale
        x1=(boxw-image.width*scale)/2+bounds[2]*scale
        y0=top+(boxh-image.height*scale)/2+bounds[1]*scale
        y1=top+(boxh-image.height*scale)/2+bounds[3]*scale
        assert 0<=x0<x1<=boxw and 0<=y0<y1<=parent_height,(index,boxw,boxh,bounds)
print('All 19 transparent custom images fit V9 hero, both card modes and preview by source alpha bounds')
