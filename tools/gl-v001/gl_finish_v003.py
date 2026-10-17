"""새 GL 번들에 도너 참조·노드·의존 목록을 적용하고 원본은 보존합니다."""
import ast,copy,hashlib,json,pathlib,shutil,subprocess,sys,UnityPy,numpy as np
from scipy.spatial.transform import Rotation
ROOT=pathlib.Path(__file__).resolve().parents[1];GAME=pathlib.Path('E:/SPT 4.1/EscapeFromTarkov_Data/StreamingAssets/Windows')
OUT=ROOT/'Bundles/gl-v001';STAGE=ROOT/'Bundles/shader-v002';REPORT=json.loads((ROOT/'diagnostics/build-report-v002.json').read_text(encoding='utf-8'))
GL='assets/content/weapons/daewoo_k2/launcher_daewoo_k201_40x46_container.bundle';MESH='assets/content/weapons/daewoo_k2/daewoo_k201_meshes.bundle';TEX='assets/content/weapons/daewoo_k2/daewoo_k2_gl_textures.bundle';HG='assets/content/items/mods/handguards/handguard_daewoo_k2_gl.bundle'
DONOR='assets/content/weapons/m203/launcher_ar15_colt_m203_40x46_container.bundle';DONORHG='assets/content/items/mods/handguards/handguard_ar15_colt_m4_length_std.bundle'
K2=pathlib.Path('F:/코딩/Index/클로드방/K2-Project-Original/Assets-Local/Bundles/ak-v019')
assert not (OUT/GL).exists() and not (OUT/HG).exists(),'완료 번들 보호'
def run(*args):subprocess.run([sys.executable,*map(str,args)],check=True)
assert (STAGE/'shader-fix.json').exists(),'셰이더 단계 완료 확인'
OUT.mkdir(parents=True,exist_ok=True)
for key in (MESH,TEX):
 assert (OUT/key).read_bytes()==(STAGE/key).read_bytes(),'부분 결과 일치 확인'
assert (ROOT/'Bundles/hg-refs-v002.bundle').is_file()
def serialized(env):return next((n,s) for n,s in env.file.files.items() if hasattr(s,'objects'))
env=UnityPy.load(str(GAME/DONOR));oldcab,sf=serialized(env)
newcab='CAB-'+hashlib.sha256((GAME/DONOR).read_bytes()+(ROOT/'diagnostics/build-report-v002.json').read_bytes()+(OUT/MESH).read_bytes()).hexdigest()[:32]
def external(key,typ):
 cab,s=serialized(UnityPy.load(str(OUT/key)));ext=copy.copy(sf.externals[0]);ext.path=f'archive:/{cab}/{cab}';ext.guid=bytes(16);ext.type=0;sf.externals.append(ext)
 return len(sf.externals),{o.read_typetree()['m_Name']:o.path_id for o in s.objects.values() if o.type.name==typ},cab
mi,meshes,mcab=external(MESH,'Mesh');ti,mats,tcab=external(TEX,'Material')
names={o.path_id:o.read_typetree()['m_Name'] for o in sf.objects.values() if o.type.name=='GameObject'}
trans={o.path_id:o.read_typetree() for o in sf.objects.values() if o.type.name=='Transform'}
bygo={t['m_GameObject']['m_PathID']:i for i,t in trans.items()}
def model(go):
 i=bygo[go]
 while trans[i]['m_Father']['m_PathID']:i=trans[i]['m_Father']['m_PathID']
 return names[trans[i]['m_GameObject']['m_PathID']]
swaps={(s['model'],s['node']):s for s in REPORT['swaps']};ptrs=[];counts={};touched=[]
def ptr(f,i):
 p={'m_FileID':f,'m_PathID':i}
 if p not in ptrs:ptrs.append(p)
 return p
for o in sf.objects.values():
 typ=o.type.name
 if typ in ('MeshFilter','MeshRenderer'):
  t=o.read_typetree();go=t['m_GameObject']['m_PathID']
  if names[go]=='MuzzleJetCombinedMesh':continue # 바닐라 총구 효과 메시 유지
  k=(model(go),names[go]);assert k in swaps,('교체 누락',k);s=swaps[k]
  if typ=='MeshFilter':t['m_Mesh']=ptr(mi,meshes[s['mesh']])
  else:t['m_Materials']=[ptr(ti,mats[n]) for n in s['materials']]
  o.save_typetree(t);counts[typ]=counts.get(typ,0)+1
 elif typ=='Transform':
  t=o.read_typetree();n=names[t['m_GameObject']['m_PathID']]
  if n=='Weapon_root_original_position':t['m_LocalPosition']=REPORT['wrop'];touched.append(n)
  elif n=='mod_aim_camera' and not REPORT['aimAnimated']:t['m_LocalPosition']=REPORT['aim'];touched.append(n)
  else:continue
  o.save_typetree(t)
 elif typ=='AudioClip':
  t=o.read_typetree();t['m_Resource']['m_Source']=t['m_Resource']['m_Source'].replace(oldcab,newcab);o.save_typetree(t)
for o in sf.objects.values():
 if o.type.name!='AssetBundle':continue
 t=o.read_typetree();t['m_Name']=GL;t['m_AssetBundleName']=GL;t['m_PreloadTable']+=ptrs
 entry=next(c for k,c in t['m_Container'] if k.endswith('_container.prefab'));entry['preloadIndex']=0;entry['preloadSize']=len(t['m_PreloadTable']);t['m_Container']=[(GL.replace('.bundle','.prefab'),entry)];t['m_Dependencies']+= [mcab.lower(),tcab.lower()];o.save_typetree(t)
env.file.files={n.replace(oldcab,newcab):f for n,f in env.file.files.items()};sf.name=newcab;(OUT/GL).parent.mkdir(parents=True,exist_ok=True);(OUT/GL).write_bytes(env.file.save(packer='lz4'))
assert counts['MeshFilter']==counts['MeshRenderer']==len(swaps),counts
# 기존 손바닥 보정 도구의 순수 함수·Tree 클래스만 읽어 사용합니다(명령 본문은 실행하지 않습니다).
source=ast.parse((ROOT/'tools/fix_part_nodes.py').read_text(encoding='utf-8-sig'))
ns={};defs=[n for n in source.body if isinstance(n,(ast.Import,ast.ImportFrom,ast.FunctionDef,ast.ClassDef))]
exec(compile(ast.Module(body=defs,type_ignores=[]),'fix_part_nodes_definitions','exec'),ns)
Tree=ns['Tree'];compose=ns['compose'];relative=ns['relative'];box=ns['box'];add=ns['add'];ATTACH=((0.,0.,0.),(.7071067811865476,0.,0.,.7071067811865476))
body=Tree(str(K2/'assets/content/weapons/daewoo_k2/weapon_daewoo_k2_556x45_container.bundle'));up=Tree(str(K2/'assets/content/items/mods/recievers/reciever_daewoo_k2_std.bundle'));hg=Tree(str(ROOT/'Bundles/hg-refs-v002.bundle'));ak=Tree(str(GAME/'assets/content/items/mods/handguards/handguard_ak_izhmash_ak74m_std_plastic.bundle'))
root=lambda tree:next(i for i,t in tree.tt.items() if not t['m_Father']['m_PathID'])
broot=next(i for i,t in body.tt.items() if not t['m_Father']['m_PathID'] and any(body.names[c['m_PathID']]=='Weapon_root' for c in t['m_Children']) and 'simple' not in body.names[i]);weapon=body.find('weapon',broot)
hgworld=compose(compose(compose(body.world(body.find('mod_reciever',weapon),weapon),ATTACH),up.world(up.find('mod_handguard'),root(up))),ATTACH)
akworld=compose(body.world(body.find('mod_handguard',weapon),weapon),ATTACH);hb=box(hg,root(hg),hgworld);ab=box(ak,root(ak),akworld)
surface=(hb[0][0]-ab[0][0],min(0.,hb[1][1]-ab[1][1]),hb[0][2]-ab[0][2]);palms={}
def pose(dst,src):
 dc,sc=hg.children(dst),ak.children(src);assert len(dc)==len(sc)
 for d,s in zip(dc,sc):hg.set_local(d,ak.local(s));pose(d,s)
for name in ('Base HumanLPalm','Base HumanLPalm 1'):
 h,a=hg.find(name),ak.find(name);target=compose(akworld,ak.local(a));target=(add(target[0],surface),target[1]);hg.set_local(h,relative(hgworld,target));pose(h,a);palms[name]=target[0]
(OUT/HG).parent.mkdir(parents=True,exist_ok=True);hg.save(str(OUT/HG))
# Windows.json 전체 의존을 manifest에 펼칩니다.
win=json.loads((GAME/'Windows.json').read_text(encoding='utf-8-sig'))
def closure(k):
 seen=set()
 def visit(x):
  if x in seen:return
  seen.add(x)
  for d in win[x]['Dependencies']:visit(d)
 visit(k);return seen
manifest=[{'key':MESH,'dependencyKeys':[]},{'key':TEX,'dependencyKeys':['shaders']},{'key':HG,'dependencyKeys':sorted(closure(DONORHG)|{TEX,'shaders','cubemaps'})},{'key':GL,'dependencyKeys':sorted((closure(DONOR)-{DONOR})|{MESH,TEX,'shaders','cubemaps'})}]
(OUT/'bundles.json').write_text(json.dumps({'manifest':manifest},indent=2),encoding='utf-8')
# 도너의 로직과 동작 노드는 그대로인지 다시 읽어 검증합니다.
_,original=serialized(UnityPy.load(str(GAME/DONOR)));_,final=serialized(UnityPy.load(str(OUT/GL)))
assert original.objects.keys()==final.objects.keys()
unchanged={}
for i,o in original.objects.items():
 if o.type.name in ('MonoBehaviour','Animator','AnimationClip','AnimatorController','Avatar'):
  assert o.get_raw_data()==final.objects[i].get_raw_data(),('로직 변경',i);unchanged[o.type.name]=unchanged.get(o.type.name,0)+1
 if o.type.name=='Transform':
  t=o.read_typetree();n=names[t['m_GameObject']['m_PathID']]
  if n not in touched:assert t==final.objects[i].read_typetree(),('동작 노드 변경',n)
rep=dict(passed=True,mesh_counts=counts,changed_transform_names=touched,preserved_object_types=unchanged,new_cab=newcab,handguard_surface_shift_m=surface,palms_weapon_m=palms,manifest_keys=[m['key'] for m in manifest])
(OUT/'patch-validation.json').write_text(json.dumps(rep,ensure_ascii=False,indent=2),encoding='utf-8');print('GL 개조 완료',json.dumps(rep,ensure_ascii=False))

