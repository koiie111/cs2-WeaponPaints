"""Exercise retail-v6 rejection and legacy warning with synthetic CSS trees."""
import subprocess,sys,tempfile
from pathlib import Path
repo=Path(__file__).resolve().parents[2];validator=repo/'hud-mockup/tools/validate_panorama.py'
with tempfile.TemporaryDirectory(dir=repo) as temp:
 root=Path(temp)
 def check(version,style,should_pass,warning=False):
  addon=root/('live-addon-v'+str(version));layout=addon/'panorama/layout/custom_game';styles=addon/'panorama/styles/custom_game'
  layout.mkdir(parents=True,exist_ok=True);styles.mkdir(parents=True,exist_ok=True)
  (layout/'test.xml').write_text('<root><Panel class="outer" /></root>',encoding='utf8')
  (styles/('test_v'+str(version)+'.css')).write_text('.outer { background-size: '+style+'; }',encoding='utf8')
  result=subprocess.run([sys.executable,str(validator),str(addon)],capture_output=True,text=True)
  assert (result.returncode==0)==should_pass,(style,result.stdout,result.stderr)
  if warning:assert 'WARNING' in result.stdout,result.stdout
 check(4,'contains',True,True)
 check(6,'contain',True)
 check(6,'100% auto',True)
 check(6,'auto 100%',True)
 check(6,'contains',False)
 check(6,'contains 100%',False)
print('Legacy warning and v6 retail fit validation tests passed')
