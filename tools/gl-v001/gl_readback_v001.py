"""저장된 분리 모델과 실제 FBX의 미터 크기를 독립 비교합니다."""
import bpy,json
from pathlib import Path
from mathutils import Vector
p=Path('F:/코딩/Index/코덱스방/K2-gl-v001/Model')
bpy.ops.wm.open_mainfile(filepath=str(p/'K2-gl-v001.blend'))
base=bpy.data.objects['daewoo_K2_GL_base']
bpy.context.window.scene=next(s for s in bpy.data.scenes if base.name in s.objects)
bpy.context.view_layer.update()
src={o.name:dict(origin_m=list(o.location),size_m=list(o.dimensions),faces=len(o.data.polygons)) for o in bpy.context.scene.objects if o.type=='MESH' and 'GL_' in o.name and o!=base}
assert src['daewoo_K2_GL_handguard']['faces']==3782
assert src['daewoo_K2_GL_handguard']['faces']+src['daewoo_K2_GL_frame']['faces']==len(base.data.polygons)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(p/'k2_gl.fbx'))
bpy.context.view_layer.update()
dst={}
for o in bpy.context.scene.objects:
 if o.type!='MESH':continue
 pts=[o.matrix_world@Vector(c) for c in o.bound_box]
 size=[max(v[k] for v in pts)-min(v[k] for v in pts) for k in range(3)]
 want=src[o.name]['size_m'];want=[want[1],want[0],want[2]]
 origin=src[o.name]['origin_m'];origin=[-origin[1],origin[0],origin[2]]
 assert max(abs(a-b) for a,b in zip(size,want))<1e-5,(o.name,size,want)
 assert (o.matrix_world.translation-Vector(origin)).length<1e-5
 dst[o.name]=dict(origin_m=list(o.matrix_world.translation),size_m=size)
report=dict(passed=True,source=src,readback=dst,handguard_faces=3782,frame_faces=src['daewoo_K2_GL_frame']['faces'])
assert not (p/'split-validation.json').exists()
(p/'split-validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print('미터 크기 재읽기 통과',json.dumps(report,ensure_ascii=False))
