import pathlib,json,UnityPy,sys
base=pathlib.Path(__file__).resolve().parent.parent;root=base/'Bundles/ak-v004-final';game=pathlib.Path('E:/SPT 4.1/EscapeFromTarkov_Data/StreamingAssets/Windows')
ours={a['key']:a['dependencyKeys'] for a in json.loads((root/'bundles.json').read_text())['manifest']};win=json.loads((game/'Windows.json').read_text(encoding='utf-8-sig'))
seen=set();order=[]
def visit(k):
 if k in seen:return
 seen.add(k)
 for d in ours.get(k,win.get(k,{}).get('Dependencies',[])):visit(d)
 order.append(k)
for k in ours:visit(k)
loaded={};files={};paths=[]
for k in order:
 p=(root if k in ours else game)/k; assert p.is_file(),p
 e=UnityPy.load(str(p));files[k]=e;paths.append(str(p))
 for n,sf in e.file.files.items():
  if hasattr(sf,'objects'):
   assert n.lower() not in loaded,'Duplicate CAB '+n
   loaded[n.lower()]=sf
missing=[];count=0

def ptrs(v):
 if isinstance(v,dict):
  if 'm_FileID' in v and 'm_PathID' in v:yield v
  else:
   for a in v.values():yield from ptrs(a)
 elif isinstance(v,(list,tuple)):
  for a in v:yield from ptrs(a)
for k in ours:
 for n,sf in files[k].file.files.items():
  if not hasattr(sf,'objects'):continue
  for o in sf.objects.values():
   for p in ptrs(o.read_typetree()):
    if not p['m_PathID']:continue
    count+=1
    target=sf if p['m_FileID']==0 else loaded.get(sf.externals[p['m_FileID']-1].path.split('/')[-1].lower())
    if target is None or p['m_PathID'] not in target.objects:missing.append([k,o.path_id,p])
report={'passed':not missing,'customBundles':len(ours),'loadedBundles':len(order),'checkedPointers':count,'missing':missing}
(root/'reference-audit.json').write_text(json.dumps(report,indent=2),encoding='utf-8');(base/'diagnostics/load-order.txt').write_text('\n'.join(paths),encoding='utf-8')
print(json.dumps(report));sys.exit(bool(missing))

