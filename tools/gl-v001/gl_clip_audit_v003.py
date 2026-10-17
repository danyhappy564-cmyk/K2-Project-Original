"""빌드된 클립의 실제 경로 해시로 보정 가능한 노드인지 확인합니다."""
import json,pathlib,zlib,UnityPy
ROOT=pathlib.Path(__file__).resolve().parents[1]
GAME=pathlib.Path('E:/SPT 4.1/EscapeFromTarkov_Data/StreamingAssets/Windows')
inp=json.loads((ROOT/'diagnostics/build-input-v002.json').read_text(encoding='utf8'))
model=next(n for n in inp['launcher'] if n['name'].endswith('model.generated'))
paths={}
def walk(n,path):
 paths[zlib.crc32(path.encode())]=path
 for c in n['children']:walk(c,path+'/'+c['name'] if path else c['name'])
walk(model,'')
env=UnityPy.load(str(GAME/'assets/content/weapons/m203/client_assets.bundle'))
rows=[]
for o in env.objects:
 if o.type.name!='AnimationClip':continue
 t=o.read_typetree();bindings=t['m_ClipBindingConstant']['genericBindings']
 rows.append({'name':t['m_Name'],'bindingCount':len(bindings),'aim':any(paths.get(b['path'])=='mod_aim_camera' for b in bindings),'wrop':any(paths.get(b['path'])=='Weapon_root_original_position' for b in bindings),'fireport':any(paths.get(b['path'])=='fireport' for b in bindings),'paths':sorted({paths.get(b['path'],'#'+str(b['path'])) for b in bindings})})
report=dict(passed=not any(r['aim'] or r['wrop'] for r in rows),clipCount=len(rows),bindingCount=sum(r['bindingCount'] for r in rows),aimBindings=[r['name'] for r in rows if r['aim']],wropBindings=[r['name'] for r in rows if r['wrop']],fireportBindings=[r['name'] for r in rows if r['fireport']],clips=rows)
assert report['passed'] and report['bindingCount']>0 and report['fireportBindings'],report
(ROOT/'Bundles/gl-v001/clip-binding-audit.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
print('클립 경로 해시 검사 통과',report['clipCount'],report['bindingCount'],'조준/WROP 직접 바인딩 0, fireport',len(report['fireportBindings']))
