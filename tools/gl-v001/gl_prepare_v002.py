"""읽기 전용 게임/K2 번들에서 GL 빌드용 좌표와 의존 정보를 준비합니다."""
import json,pathlib,UnityPy
ROOT=pathlib.Path(__file__).resolve().parents[1]
GAME=pathlib.Path('E:/SPT 4.1/EscapeFromTarkov_Data/StreamingAssets/Windows')
K2=pathlib.Path('F:/코딩/Index/클로드방/K2-Project-Original/Assets-Local/Bundles/ak-v019')
GL='assets/content/weapons/m203/launcher_ar15_colt_m203_40x46_container.bundle'
HG='assets/content/items/mods/handguards/handguard_ar15_colt_m4_length_std.bundle'
def nodes(path):
 e=UnityPy.load(str(path));obs={o.path_id:o for o in e.objects};ts={i:o.read_typetree() for i,o in obs.items() if o.type.name=='Transform'}
 names={i:obs[t['m_GameObject']['m_PathID']].read_typetree()['m_Name'] for i,t in ts.items()}
 def node(i):
  t=ts[i];go=t['m_GameObject']['m_PathID'];mf=next((o.read_typetree() for o in obs.values() if o.type.name=='MeshFilter' and o.read_typetree()['m_GameObject']['m_PathID']==go),None)
  return dict(name=names[i],position=t['m_LocalPosition'],rotation=t['m_LocalRotation'],scale=t['m_LocalScale'],mesh=mf is not None,children=[node(c['m_PathID']) for c in t['m_Children']])
 return [node(i) for i,t in ts.items() if t['m_Father']['m_PathID']==0]
win=json.loads((GAME/'Windows.json').read_text(encoding='utf-8-sig'))
paths=[];seen=set()
def visit(k):
 if k in seen:return
 seen.add(k)
 for d in win[k]['Dependencies']:visit(d)
 paths.append(str(GAME/k))
for key in (GL,HG):visit(key)
for p in (K2/'load-order.txt').read_text(encoding='utf-8-sig').splitlines():
 if p.replace('\\','/').lower() not in {x.replace('\\','/').lower() for x in paths}:paths.append(p)
coord=json.loads((ROOT/'diagnostics/coordinates-v001.json').read_text(encoding='utf-8'))
report=dict(launcher=nodes(GAME/GL),offset=dict(zip('xyz',coord['k201_offset_m'])),k2path=str(K2).replace('\\','/'),loadPaths=paths)
(ROOT/'diagnostics/build-input-v002.json').write_text(json.dumps(report,ensure_ascii=False,indent=1),encoding='utf-8')
(ROOT/'diagnostics/build-load-order-v002.txt').write_text('\n'.join(paths),encoding='utf-8')
print('입력 준비 완료: 의존',len(paths),'GL 루트',len(report['launcher']))
