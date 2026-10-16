"""Independent readback checks: preserved donor objects/audio and replaced model references."""
import sys,json,hashlib,pathlib,UnityPy
DONOR='E:/SPT 4.1/EscapeFromTarkov_Data/StreamingAssets/Windows/assets/content/weapons/ak100/weapon_izhmash_ak101_556x45_container.bundle'
KEY='assets/content/weapons/daewoo_k2/weapon_daewoo_k2_556x45_container.bundle'
def serialized(env): return next((n,f) for n,f in env.file.files.items() if hasattr(f,'objects'))
def verify(path,root,mapping,donor=DONOR):
 old=UnityPy.load(donor); new=UnityPy.load(str(path)); oc,os=serialized(old); nc,ns=serialized(new)
 assert nc!=oc,'CAB identity still equals AK donor'
 assert set(os.objects)==set(ns.objects),'Donor object IDs changed'
 bundle=next(o.read_typetree() for o in ns.objects.values() if o.type.name=='AssetBundle')
 assert bundle['m_Name']==KEY and bundle['m_AssetBundleName']==KEY,'Wrong bundle name'
 assert all('daewoo_k2' in c[0] for c in bundle['m_Container'])
 names={o.path_id:o.read_typetree()['m_Name'] for o in ns.objects.values() if o.type.name=='GameObject'}
 targets={}
 for key in [mapping['meshBundle'],mapping['texturesBundle']]:
  env=UnityPy.load(str(pathlib.Path(root)/key)); cab,sf=serialized(env); targets[cab]={o.path_id:o for o in sf.objects.values()}
 def resolve(ptr):
  cab=ns.externals[ptr['m_FileID']-1].path.split('/')[-1]; return targets[cab][ptr['m_PathID']]
 expect_mesh=sum(1 for o in os.objects.values() if o.type.name=='MeshFilter' and names.get(o.read_typetree()['m_GameObject']['m_PathID']) in mapping['meshes'])
 changed=[]; counters={'MeshFilter':0,'MeshRenderer':0,'fireport':0,'preservedMonoBehaviour':0,'preservedAnimator':0,'preservedAudioClip':0}
 for pid,o in ns.objects.items():
  orig=os.objects[pid]; typ=o.type.name
  if typ in ('MeshFilter','MeshRenderer'):
   t=o.read_typetree(); name=names[t['m_GameObject']['m_PathID']]
   if name in mapping['meshes']:
    spec=mapping['meshes'][name]
    if typ=='MeshFilter': assert resolve(t['m_Mesh']).read_typetree()['m_Name']==spec['mesh']
    else: assert [resolve(p).read_typetree()['m_Name'] for p in t['m_Materials']]==spec['materials']
    counters[typ]+=1; changed.append(pid);continue
  if typ=='Transform':
   t=o.read_typetree(); name=names[t['m_GameObject']['m_PathID']]
   if name=='fireport':
    assert all(abs(t['m_LocalPosition'][k]-v)<1e-6 for k,v in zip('xyz',mapping['transforms']['fireport']))
    a=orig.read_typetree();a['m_LocalPosition']=t['m_LocalPosition'];assert a==t
    counters['fireport']+=1;changed.append(pid);continue
  if typ=='AudioClip':
   a=orig.read_typetree();b=o.read_typetree();a['m_Resource']['m_Source']=a['m_Resource']['m_Source'].replace(oc,nc);assert a==b
   counters['preservedAudioClip']+=1;continue
  if typ=='AssetBundle':continue
  assert orig.get_raw_data()==o.get_raw_data(),f'Unexpected change: {pid} {typ}'
  if 'preserved'+typ in counters:counters['preserved'+typ]+=1
 # AK-101: 모델·simple 두 벌이라 메시 20·fireport 2 / AK5C: 모델 하나라 메시 10·fireport 1
 assert counters['MeshFilter']==counters['MeshRenderer']==expect_mesh,counters   # SCAR: fireport는 총열 부품(0)
 for name,f in old.file.files.items():
  if name.endswith(('.resource','.resS')):
   a=f.bytes; b=new.file.files[name.replace(oc,nc)].bytes;assert hashlib.sha256(a).digest()==hashlib.sha256(b).digest()
 for _,c in bundle['m_Container']:
  pre=bundle['m_PreloadTable'][c['preloadIndex']:c['preloadIndex']+c['preloadSize']]
  assert any(p['m_FileID']>len(os.externals) for p in pre),'Missing custom preload references'
 return {'passed':True,'oldCAB':oc,'newCAB':nc,'counts':counters,'objectCount':len(ns.objects),'audioResourceUnchanged':True}
if __name__=='__main__':
 path,root,mapfile=sys.argv[1:4]; report=verify(path,root,json.load(open(mapfile,encoding='utf-8-sig')),*sys.argv[5:6]);print(json.dumps(report,ensure_ascii=False))
 if len(sys.argv)>4:pathlib.Path(sys.argv[4]).write_text(json.dumps(report,indent=2),encoding='utf-8')
