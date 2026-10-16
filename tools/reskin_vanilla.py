"""Copy donor (AK-101 vanilla, or AK5C from WTT-Armory since 0.7.0) container bundle; preserve serialized gameplay/sound data; replace only visual references and fireport."""
import copy,hashlib,json,pathlib,sys,UnityPy
from verify_reskin import verify,serialized,KEY

def patch(donor,root,mapfile,out):
 root=pathlib.Path(root);out=pathlib.Path(out); assert not out.exists(),f'Refusing overwrite: {out}'
 mapping=json.loads(pathlib.Path(mapfile).read_text(encoding='utf-8-sig'))
 env=UnityPy.load(donor);oldcab,sf=serialized(env)
 seed=pathlib.Path(donor).read_bytes()+pathlib.Path(mapfile).read_bytes()+ (root/mapping['meshBundle']).read_bytes()
 newcab='CAB-'+hashlib.sha256(seed).hexdigest()[:32]
 def target(key,typ):
  e=UnityPy.load(str(root/key));cab,s=serialized(e);lookup={o.read_typetree()['m_Name']:o.path_id for o in s.objects.values() if o.type.name==typ}
  ext=copy.copy(sf.externals[0]);ext.path=f'archive:/{cab}/{cab}';ext.guid=bytes(16);ext.type=0;sf.externals.append(ext)
  return cab,len(sf.externals),lookup
 meshcab,meshidx,meshes=target(mapping['meshBundle'],'Mesh'); matcab,matidx,mats=target(mapping['texturesBundle'],'Material')
 names={o.path_id:o.read_typetree()['m_Name'] for o in sf.objects.values() if o.type.name=='GameObject'}
 ptrs=[];counts={'MeshFilter':0,'MeshRenderer':0,'fireport':0,'AudioClip':0}
 def ptr(idx,pid):
  p={'m_FileID':idx,'m_PathID':pid}
  if p not in ptrs:ptrs.append(p)
  return p
 for o in sf.objects.values():
  typ=o.type.name
  if typ in ('MeshFilter','MeshRenderer'):
   t=o.read_typetree();name=names[t['m_GameObject']['m_PathID']]
   if name not in mapping['meshes']:continue
   spec=mapping['meshes'][name]
   if typ=='MeshFilter':t['m_Mesh']=ptr(meshidx,meshes[spec['mesh']])
   else:t['m_Materials']=[ptr(matidx,mats[n]) for n in spec['materials']]
   o.save_typetree(t);counts[typ]+=1
  elif typ=='Transform':
   t=o.read_typetree();name=names[t['m_GameObject']['m_PathID']]
   if name!='fireport':continue
   parent=sf.objects[t['m_Father']['m_PathID']].read_typetree();assert names[parent['m_GameObject']['m_PathID']]=='weapon','Fireport basis mismatch'
   t['m_LocalPosition']=dict(zip('xyz',mapping['transforms']['fireport']));o.save_typetree(t);counts['fireport']+=1
  elif typ=='AudioClip':
   t=o.read_typetree();t['m_Resource']['m_Source']=t['m_Resource']['m_Source'].replace(oldcab,newcab);o.save_typetree(t);counts[typ]+=1
 assert counts['MeshFilter']==counts['MeshRenderer']>0 and counts['fireport']>=1,counts   # AK-101 20/20/2, AK5C 10/10/1
 for o in sf.objects.values():
  if o.type.name!='AssetBundle':continue
  t=o.read_typetree();t['m_Name']=KEY;t['m_AssetBundleName']=KEY
  oldpre=t['m_PreloadTable'];newpre=[];containers=[]
  for name,c in t['m_Container']:
   pre=oldpre[c['preloadIndex']:c['preloadIndex']+c['preloadSize']]+ptrs
   c['preloadIndex']=len(newpre);c['preloadSize']=len(pre);newpre.extend(pre)
   name=name.replace('/ak100/','/daewoo_k2/').replace('weapon_izhmash_ak101_556x45','weapon_daewoo_k2_556x45')
   if 'daewoo_k2' not in name and name.lower().endswith('_container.prefab'):name='assets/content/weapons/daewoo_k2/weapon_daewoo_k2_556x45_container.prefab'   # AK5C 등 다른 도너
   containers.append((name.lower(),c))
  t['m_PreloadTable']=newpre;t['m_Container']=containers
  t['m_Dependencies']+= [meshcab.lower(),matcab.lower()];o.save_typetree(t)
 env.file.files={name.replace(oldcab,newcab):f for name,f in env.file.files.items()}
 sf.name=newcab
 out.parent.mkdir(parents=True,exist_ok=True)
 out.write_bytes(env.file.save(packer='lz4'))
 report=verify(out,root,mapping,donor)
 (root/'reskin-validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
 print(json.dumps(report))
if __name__=='__main__':patch(*sys.argv[1:5])
