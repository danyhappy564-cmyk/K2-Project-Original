"""GL 연결 조각을 분리하고 원본 좌표를 보존한 새 FBX를 내보냅니다."""
import bpy, bmesh, json, math
from pathlib import Path
from mathutils import Matrix, Vector
root=Path('F:/코딩/Index/코덱스방/K2-gl-v001')
out=root/'Model'
out.mkdir(exist_ok=True)
assert not (out/'k2_gl.fbx').exists(), '기존 결과 덮어쓰기 금지'
base=bpy.data.objects.get('daewoo_K2_GL_base')
assert base is not None
scene=next(s for s in bpy.data.scenes if base.name in s.objects)
bpy.context.window.scene=scene
bpy.context.view_layer.update()
mesh=base.data
adj=[set() for _ in mesh.vertices]
for e in mesh.edges:
 a,b=e.vertices;adj[a].add(b);adj[b].add(a)
seen=set();islands=[];hand=set()
for i in range(len(adj)):
 if i in seen:continue
 todo=[i];ids=set()
 while todo:
  j=todo.pop()
  if j in ids:continue
  ids.add(j);todo.extend(adj[j]-ids)
 seen.update(ids)
 faces=[p for p in mesh.polygons if p.vertices[0] in ids]
 coords=[mesh.vertices[j].co for j in ids]
 lo=[min(v[k] for v in coords) for k in range(3)];hi=[max(v[k] for v in coords) for k in range(3)]
 ishand=len(faces) in (2500,1006) or (len(faces)==138 and lo[2]>.04)
 if ishand:hand.update(ids)
 islands.append(dict(vertices=len(ids),faces=len(faces),min_m=lo,max_m=hi,handguard=ishand))
assert len(islands)==15, len(islands)
assert sorted(x['faces'] for x in islands if x['handguard'])==[138,138,1006,2500]
new=[]
for suffix,keep in [('handguard',hand),('frame',set(range(len(adj)))-hand)]:
 obj=base.copy();obj.data=base.data.copy();obj.name='daewoo_K2_GL_'+suffix;scene.collection.objects.link(obj)
 bm=bmesh.new();bm.from_mesh(obj.data);bm.verts.ensure_lookup_table()
 bmesh.ops.delete(bm,geom=[v for v in bm.verts if v.index not in keep],context='VERTS')
 bm.to_mesh(obj.data);bm.free();obj.data.update();new.append(obj)
bpy.context.view_layer.update()
parts=new+[o for o in scene.objects if o.type=='MESH' and 'GL_' in o.name and o!=base and o not in new]
before={o.name:dict(origin_m=list(o.location),size_m=list(o.dimensions),faces=len(o.data.polygons)) for o in parts}
assert not (out/'K2-gl-v001.blend').exists()
bpy.ops.wm.save_as_mainfile(filepath=str(out/'K2-gl-v001.blend'))
R=Matrix.Rotation(math.radians(90),4,'Z')
for o in scene.objects:o.select_set(False)
for o in parts:
 o.data=o.data.copy();o.data.transform(R);o.location=R@o.location;o.select_set(True)
bpy.context.view_layer.update()
bpy.ops.export_scene.fbx(filepath=str(out/'k2_gl.fbx'),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',bake_space_transform=True,apply_unit_scale=True,global_scale=1.0,mesh_smooth_type='FACE',use_mesh_modifiers=True,add_leaf_bones=False,path_mode='STRIP',embed_textures=False)
# 실제 내보낸 FBX를 새 장면으로 읽어 크기·원점을 비교합니다.
check=bpy.data.scenes.new('GL 내보내기 재읽기 검사');bpy.context.window.scene=check
bpy.ops.import_scene.fbx(filepath=str(out/'k2_gl.fbx'))
bpy.context.view_layer.update()
after={}
for o in check.objects:
 if o.type!='MESH':continue
 pts=[o.matrix_world@Vector(c) for c in o.bound_box]
 size=[max(p[k] for p in pts)-min(p[k] for p in pts) for k in range(3)]
 expected=before[o.name]['size_m'];expected=[expected[1],expected[0],expected[2]]
 assert max(abs(a-b) for a,b in zip(size,expected))<1e-5,(o.name,size,expected)
 after[o.name]=dict(origin_m=list(o.matrix_world.translation),size_m=size)
report=dict(islands=islands,source=before,readback=after,passed=True)
(out/'split-validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print('분리 및 FBX 재읽기 통과',json.dumps(after,ensure_ascii=False))
