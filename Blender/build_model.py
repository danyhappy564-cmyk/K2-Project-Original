"""구매 원본 모델 → 작업용 blend(vNNN) 생성. 원본은 읽기만 하고 결과는 새 파일로 저장한다.

실행 (Blender 5.2, 백그라운드):
  blender -b --factory-startup --disable-autoexec --python Blender/build_model.py -- <원본 폴더> <출력 폴더> <버전 예: v001>

하는 일
1. all_parts(분리 부품 28개) + 추가팩(클래식 핸드가드, AR·경량 개머리판)을 한 장면으로 모은다.
2. 끊긴 텍스처 경로(//..\\textures\\)를 원본 폴더의 실제 PNG로 다시 연결한다(파일은 복사하지 않음).
3. 실척: 단위 없는 원본을 미터로 맞춘다. 기준 = K2 전장 980mm(개머리판 펼침, 일반형) — 배율을 적용(apply)해 1유닛 = 1m.
4. 움직이는 부품(방아쇠·해머·노리쇠 등)의 원점을 리그 본 머리 위치로 옮긴다 — 나중에 Unity에서 회전·이동 기준점.
5. 부품을 용도별 컬렉션으로 나누고, 부품별 크기(m)·삼각형 수를 JSON으로 남기고, 측면 미리보기 JPG를 렌더한다.
출력 파일이 이미 있으면 덮어쓰지 않고 멈춘다.
"""
import bpy, sys, os, json, math
from mathutils import Vector, Matrix

args = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT, VER = args[0], args[1], args[2]
REAL_LENGTH_M = 0.980          # K2 전장(개머리판 펼침), 대우정밀 제원
NORMAL_PARTS = ["upper", "lower", "barrel_long", "handguard_long", "muzzle", "stock", "magazine"]

blend_out = os.path.join(OUT, f"K2-model-{VER}.blend")
json_out = os.path.join(OUT, f"K2-model-{VER}.json")
png_out = os.path.join(OUT, f"K2-model-{VER}-side.jpg")
for p in (blend_out, json_out, png_out):
    if os.path.exists(p):
        sys.exit(f"STOP: 이미 있음 — 덮어쓰지 않습니다: {p}")
os.makedirs(OUT, exist_ok=True)

# 1. 부품 모으기 ---------------------------------------------------------------
bpy.ops.wm.open_mainfile(filepath=os.path.join(SRC, "daewoo_K2_all_parts.blend"), load_ui=False)
scene = bpy.context.scene

def append_objects(path):
    with bpy.data.libraries.load(path, link=False) as (src, dst):
        dst.objects = [n for n in src.objects]
    for o in dst.objects:
        if o is not None and o.type == "MESH":
            scene.collection.objects.link(o)

append_objects(os.path.join(SRC, "daewoo_k2_UPDATE2", "daewoo_K2_classic_handguard.blend"))
append_objects(os.path.join(SRC, "daewoo_update_3_stocks_PACK", "daewoo_K2_stocks_all.blend"))
for o in list(scene.objects):
    if o.name.startswith("daewoo_K2_handguard_classic"):
        o.name = "daewoo_K2_handguard_classic"

# 2. 텍스처 재연결 --------------------------------------------------------------
tex_dirs = [os.path.join(SRC, "daewoo_K2_PACK", "textures"),
            os.path.join(SRC, "daewoo_k2_UPDATE2", "textures", "handguard"),
            os.path.join(SRC, "daewoo_update_3_stocks_PACK", "textures")]
relinked, missing = [], []
for img in bpy.data.images:
    if img.source != "FILE" or not img.filepath:
        continue
    name = os.path.basename(img.filepath.replace("\\", "/"))
    hit = next((os.path.join(d, name) for d in tex_dirs if os.path.isfile(os.path.join(d, name))), None)
    if hit:
        img.filepath = hit
        relinked.append(name)
    else:
        missing.append(name)

# 3. 실척 (일반형 조립 길이 기준) -------------------------------------------------
def world_bbox(objs):
    mn, mx = Vector((1e9,) * 3), Vector((-1e9,) * 3)
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            mn = Vector(map(min, mn, w)); mx = Vector(map(max, mx, w))
    return mn, mx

bpy.context.view_layer.update()   # 장면 평가 — K2-Project 크기 사고(비활성 장면의 낡은 matrix_world) 재발 방지
normal = [scene.objects["daewoo_K2_" + n] for n in NORMAL_PARTS]
mn, mx = world_bbox(normal)
length_units = mx.x - mn.x
scale = REAL_LENGTH_M / length_units

# 4. 움직이는 부품 원점 = 리그 본 머리 (rigged_all의 본 좌표, 리그 오브젝트 변환은 단위행렬) ----------
with bpy.data.libraries.load(os.path.join(SRC, "daewoo_K2_rigged_all.blend"), link=False) as (src, dst):
    dst.armatures = ["daewoo_K2_rig"] if "daewoo_K2_rig" in src.armatures else src.armatures[:1]
arm = dst.armatures[0]
bone_heads = {b.name: b.head_local.copy() for b in arm.bones}
BONE_FOR = {"trigger": "trigger", "selector": "selector", "mag_release_R": "mag,release.R", "mag_release_L": "mag.release.L",
            "bolt_release": "bolt.release", "magazine": "magazine", "bullet": "bullet", "empty_case": "empty.case", "ammo": "ammo",
            "mag_fallower": "mag.fallower", "stock": "stock", "bolt_carrier": "bolt.carrier", "hammer": "hammer", "muzzle": "muzzle",
            "GL_trigger": "GL.trigger", "GL_release": "GL.release", "GL_tube": "GL.barrel", "GL_projectile": "GL.projectile",
            "GL_empty_case": "GL.empty.shell", "GL_side_sight": "GL.sight.side", "GL_top_sight": "GL.sight.top", "GL_base": "GL"}

pivots = {}
for part, bone in BONE_FOR.items():
    o = scene.objects.get("daewoo_K2_" + part)
    if o is None or bone not in bone_heads:
        continue
    head = bone_heads[bone]
    o.data.transform(Matrix.Translation(-head))       # 메시를 옮겨 원점이 본 머리에 오게
    o.matrix_world = Matrix.Translation(head) @ o.matrix_world
    pivots[o.name] = list(head)

# 배율 적용: 모든 메시 데이터와 위치에 곱하고 오브젝트 배율은 1로 유지 ------------------------------
S = Matrix.Scale(scale, 4)
done = set()
for o in scene.objects:
    if o.type != "MESH":
        continue
    if o.data.name not in done:
        o.data.transform(S); done.add(o.data.name)
    o.location = o.location * scale
for d in bpy.data.armatures:
    bpy.data.armatures.remove(d)
bpy.context.view_layer.update()

# 5. 컬렉션 정리 ---------------------------------------------------------------
GROUPS = {
    "K2_receiver": ["upper", "lower", "trigger", "hammer", "selector", "bolt_release", "mag_release_L", "mag_release_R", "bolt_carrier"],
    "K2_barrel": ["barrel_long", "barrel_short", "muzzle"],
    "K2_handguard": ["handguard_long", "handguard_short", "handguard_classic"],
    "K2_stock": ["stock", "ar_stock", "light"],
    "K2_magazine": ["magazine", "mag_fallower", "ammo", "bullet", "empty_case"],
    "K2_GL": ["GL_base", "GL_tube", "GL_trigger", "GL_release", "GL_projectile", "GL_empty_case", "GL_side_sight", "GL_top_sight"],
}
for cname, parts in GROUPS.items():
    col = bpy.data.collections.new(cname); scene.collection.children.link(col)
    for p in parts:
        o = scene.objects.get("daewoo_K2_" + p)
        if o is None:
            continue
        for c in list(o.users_collection):
            c.objects.unlink(o)
        col.objects.link(o)

scene.unit_settings.system = "METRIC"; scene.unit_settings.scale_length = 1.0

# 검증 기록 ---------------------------------------------------------------------
mn2, mx2 = world_bbox(normal)
report = {"version": VER, "source": SRC, "real_length_m": REAL_LENGTH_M, "length_units_before": round(length_units, 5),
          "scale_applied": scale, "normal_assembly_bbox_m": {"min": [round(v, 4) for v in mn2], "max": [round(v, 4) for v in mx2],
          "size": [round(b - a, 4) for a, b in zip(mn2, mx2)]},
          "textures_relinked": len(relinked), "textures_missing": sorted(set(missing)), "pivots_from_bones": len(pivots), "parts": {}}
for o in scene.objects:
    if o.type == "MESH":
        report["parts"][o.name] = {"collection": o.users_collection[0].name,
                                   "tris": sum(len(p.vertices) - 2 for p in o.data.polygons),
                                   "size_m": [round(v, 4) for v in o.dimensions],
                                   "origin_m": [round(v, 4) for v in o.location],
                                   "materials": [m.name for m in o.data.materials if m]}

# 미리보기: 일반형 조립만 보이게 측면 직교 렌더 -------------------------------------------
show = set("daewoo_K2_" + n for n in NORMAL_PARTS + ["trigger", "hammer", "selector", "bolt_release", "mag_release_L", "mag_release_R"])
for o in scene.objects:
    if o.type == "MESH":
        o.hide_render = o.name not in show
cam_data = bpy.data.cameras.new("PreviewCam"); cam_data.type = "ORTHO"
size = mx2 - mn2; center = (mn2 + mx2) / 2
cam_data.ortho_scale = max(size.x, size.z) * 1.08
cam = bpy.data.objects.new("PreviewCam", cam_data); scene.collection.objects.link(cam)
cam.location = (center.x, center.y - 3.0, center.z); cam.rotation_euler = (math.radians(90), 0, 0)
scene.camera = cam
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"; scene.display.shading.color_type = "TEXTURE"
scene.render.resolution_x, scene.render.resolution_y = 1800, int(1800 * size.z / size.x * 1.15) + 40
scene.render.film_transparent = False
scene.render.image_settings.file_format = "JPEG"
scene.render.filepath = png_out
bpy.ops.render.render(write_still=True)
bpy.data.objects.remove(cam); bpy.data.cameras.remove(cam_data)
for o in scene.objects:
    o.hide_render = False

bpy.ops.wm.save_as_mainfile(filepath=blend_out, copy=False)
with open(json_out, "w", encoding="utf-8") as f:
    json.dump(report, f, ensure_ascii=False, indent=1)
print("RESULT", json.dumps({k: report[k] for k in ("scale_applied", "normal_assembly_bbox_m", "textures_relinked", "textures_missing", "pivots_from_bones")}, ensure_ascii=False))
