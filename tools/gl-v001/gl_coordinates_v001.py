"""바닐라 번들을 읽기만 하여 M203 부착 사슬과 K201 정렬을 검사합니다."""
import json, pathlib, UnityPy, numpy as np
from scipy.spatial.transform import Rotation
ROOT=pathlib.Path('F:/코딩/Index/코덱스방/K2-gl-v001')
GAME=pathlib.Path('E:/SPT 4.1/EscapeFromTarkov_Data/StreamingAssets/Windows')
C='assets/content/'
class Tree:
 def __init__(self,key):
  self.env=UnityPy.load(str(GAME/key));self.objs={o.path_id:o for o in self.env.objects}
  self.ts={i:o.read_typetree() for i,o in self.objs.items() if o.type.name=='Transform'}
  self.names={i:self.objs[t['m_GameObject']['m_PathID']].read_typetree()['m_Name'] for i,t in self.ts.items()}
 def find(self,name,under=None):
  ids=[i for i,n in self.names.items() if n==name and (under is None or self.under(i,under))]
  assert len(ids)==1,(name,[(i,self.names[i]) for i in ids]);return ids[0]
 def under(self,i,root):
  while i:
   if i==root:return True
   i=self.ts[i]['m_Father']['m_PathID']
  return False
 def local(self,i):
  t=self.ts[i];m=np.eye(4);m[:3,:3]=Rotation.from_quat([t['m_LocalRotation'][k] for k in 'xyzw']).as_matrix()@np.diag([t['m_LocalScale'][k] for k in 'xyz']);m[:3,3]=[t['m_LocalPosition'][k] for k in 'xyz'];return m
 def rel(self,i,root):
  m=np.eye(4)
  while i!=root:
   assert i; m=self.local(i)@m;i=self.ts[i]['m_Father']['m_PathID']
  return m
 def root(self):
  ids=[i for i,t in self.ts.items() if not t['m_Father']['m_PathID']];assert len(ids)==1;return ids[0]
 def model(self):
  return next(i for i,t in self.ts.items() if not t['m_Father']['m_PathID'] and any(self.names[c['m_PathID']]=='Weapon_root' for c in t['m_Children']) and 'simple' not in self.names[i])
launcher=C+'weapons/m203/launcher_ar15_colt_m203_40x46_container.bundle'
gl=Tree(launcher)
roots=[{'id':i,'name':gl.names[i]} for i,t in gl.ts.items() if not t['m_Father']['m_PathID']]
print('M203 루트',roots)
model=next(i for i,t in gl.ts.items() if not t['m_Father']['m_PathID'] and any(gl.names[c['m_PathID']]=='weapon_tube' for c in t['m_Children']))
m4=Tree(C+'weapons/m4a1/weapon_colt_m4a1_556x45_container.bundle');mr=m4.model();weapon=m4.find('weapon',mr)
upper=Tree(C+'items/mods/recievers/reciever_ar15_colt_m4a1_std.bundle');ur=upper.root()
barrel=Tree(C+'items/mods/barrels/barrel_ar15_ba_hanson_carbine_pro_348mm.bundle');br=barrel.root()
rx=np.eye(4);rx[:3,:3]=Rotation.from_euler('x',90,degrees=True).as_matrix()
chain=m4.rel(m4.find('mod_reciever',weapon),weapon)@rx@upper.local(ur)@upper.rel(upper.find('mod_barrel'),ur)@rx@barrel.local(br)@barrel.rel(barrel.find('mod_launcher'),br)@rx@gl.local(model)
wrop=gl.rel(gl.find('Weapon_root_original_position',model),model)
composition=chain@wrop
err_mm=np.linalg.norm(composition[:3,3])*1000
err_deg=Rotation.from_matrix(composition[:3,:3]).magnitude()*180/np.pi
source=json.loads((ROOT/'Model/split-validation.json').read_text(encoding='utf-8'))['source']
def fbpos(n):
 x,y,z=source['daewoo_K2_GL_'+n]['origin_m'];return np.array([y,z,-x])
nodes={n:gl.rel(gl.find(n,model),model)[:3,3] for n in ['weapon_trigger','patron_in_weapon','weapon_tube','fireport','weapon_mag_release','Base HumanLPalm','mod_aim_camera']}
offset=np.array([-.0001-fbpos('tube')[0],-.0552-fbpos('tube')[1],((nodes['weapon_trigger']-fbpos('trigger'))[2]+(nodes['patron_in_weapon']-fbpos('empty_case'))[2])/2])
win=json.loads((GAME/'Windows.json').read_text(encoding='utf-8-sig'));deps=set()
def visit(k):
 for d in win[k]['Dependencies']:
  if d not in deps:deps.add(d);visit(d)
visit(launcher)
report=dict(vanilla_wrop_translation_error_mm=err_mm,vanilla_wrop_rotation_error_deg=err_deg,vanilla_wrop_passed=bool(err_mm<1 and err_deg<.1),weapon_to_launcher_matrix=chain.tolist(),wrop_matrix=wrop.tolist(),composition=composition.tolist(),k201_offset_m=offset.tolist(),trigger_error_mm=((fbpos('trigger')+offset-nodes['weapon_trigger'])*1000).tolist(),chamber_error_mm=((fbpos('empty_case')+offset-nodes['patron_in_weapon'])*1000).tolist(),nodes_m={k:v.tolist() for k,v in nodes.items()},m203_dependencies=sorted(deps),status='Unity 라이선스 오류로 K2 사슬·최종 번들 미검증')
target=ROOT/'diagnostics/coordinates-v001.json';assert not target.exists();target.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print('바닐라 WROP 오차 mm/deg',err_mm,err_deg,'K201 이동 m',offset,'의존 번들',len(deps))
