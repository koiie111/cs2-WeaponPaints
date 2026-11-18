"""Validate custom-only V9 artwork identities and packaged PNG/VTEX inputs."""
import hashlib,json,re,unittest
from pathlib import Path
from PIL import Image
ROOT=Path(__file__).resolve().parents[1]
class CustomArtTests(unittest.TestCase):
 def test_exact_assets_and_identity_mapping(self):
  manifest=json.loads((ROOT/'hud-mockup/catalog/custom-art-v9.json').read_text(encoding='utf8'));entries=manifest['entries'];art=ROOT/'hud-mockup/custom-art-v9';materials=art/'materials/weaponpaints';css=(art/'weaponpaints_custom_v9.css').read_text(encoding='utf8');source=(ROOT/'Hud/HudCustomArtwork.cs').read_text(encoding='utf8')
  self.assertEqual(len(entries),19);self.assertEqual([x['index'] for x in entries],list(range(19)));self.assertEqual(entries[0]['model'],'daedalos');self.assertEqual(len({(x['weapon'],x['model']) for x in entries}),19)
  self.assertEqual({x.name for x in materials.iterdir()},{f'custom_model_v9_{i}.{ext}' for i in range(19) for ext in ('png','vtex')})
  for e in entries:
   i=e['index'];stem=f'custom_model_v9_{i}';file=materials/(stem+'.png');blob=file.read_bytes();self.assertEqual(len(blob),e['png_bytes']);self.assertEqual(hashlib.sha256(blob).hexdigest(),e['png_sha256'])
   with Image.open(file) as im:
    self.assertEqual(im.mode,'RGBA');self.assertEqual(im.size,(512,308));bbox=im.getchannel('A').getbbox();self.assertIsNotNone(bbox);self.assertLessEqual(bbox[2]-bbox[0],460);self.assertLessEqual(bbox[3]-bbox[1],268)
   self.assertIn(f'materials/weaponpaints/{stem}.png',(materials/(stem+'.vtex')).read_text(encoding='utf8'))
   self.assertIn(f'.art-custom-{i} {{ background-image: url("s2r://materials/weaponpaints/{stem}.vtex"); }}',css)
   self.assertIn(f'("{e["weapon"]}", "{e["model"]}") => {i},',source)
  self.assertEqual(len(re.findall(r'\.art-custom-\d+\s*\{',css)),19)
  self.assertIn('_ => null',source)
if __name__=='__main__':unittest.main()
