"""작업 blend(vNNN) → Unity용 FBX + EFT 셰이더용 텍스처. 원본·작업 blend는 저장하지 않는다(읽기 전용).

실행 (Blender 5.2, 백그라운드):
  blender -b --factory-startup --disable-autoexec <K2-model-vNNN.blend> --python Blender/export_unity.py -- <출력 폴더> <텍스처 원본 루트>

하는 일
1. 총구 방향을 -X → -Y(Blender 정면)로 돌린다(메시·위치에 적용, 원점=회전 기준점 유지).
   FBX는 Forward -Z / Up Y / bake_space_transform으로 내보내 Unity에서 앞 = +Z, 위 = +Y, 회전 0이 되게 한다(Unity에서 숫자로 확인).
2. 텍스처를 2048로 줄이고 EFT `p0/Reflective/Bumped Specular SMap` 규칙으로 합친다:
   _MainTex = BaseColor RGB + Specular(회색값) A / _SpecMap = Specular RGB + (1 - Roughness) A / _BumpMap = Normal.
3. 결과 크기(m)를 JSON으로 남긴다. 출력 폴더에 같은 이름 파일이 있으면 멈춘다.
"""
import bpy, sys, os, json, math
import numpy as np
from mathutils import Matrix

args = sys.argv[sys.argv.index("--") + 1:]
OUT, TEX_ROOT = args[0], args[1]
SIZE = 2048
fbx_out = os.path.join(OUT, "k2_parts.fbx")
if os.path.exists(fbx_out):
    sys.exit(f"STOP: 이미 있음 — 덮어쓰지 않습니다: {fbx_out}")
os.makedirs(os.path.join(OUT, "Textures"), exist_ok=True)

scene = bpy.context.scene
bpy.context.view_layer.update()

# 1. 방향: Z축 +90° 회전 → 총구(-X)가 -Y로
R = Matrix.Rotation(math.radians(90), 4, "Z")
done = set()
for o in scene.objects:
    if o.type != "MESH":
        continue
    if o.data.name not in done:
        o.data.transform(R); done.add(o.data.name)
    o.location = R @ o.location
bpy.context.view_layer.update()

meshes = [o for o in scene.objects if o.type == "MESH"]
for o in scene.objects:
    o.select_set(o.type == "MESH")
bpy.ops.export_scene.fbx(filepath=fbx_out, use_selection=True, object_types={"MESH"}, axis_forward="-Z", axis_up="Y",
                         bake_space_transform=True, apply_unit_scale=True, global_scale=1.0, mesh_smooth_type="FACE",
                         use_mesh_modifiers=True, add_leaf_bones=False, path_mode="STRIP", embed_textures=False)

# 2. 텍스처 변환 -------------------------------------------------------------------
def find_tex(name):
    for d, _, files in os.walk(TEX_ROOT):
        if "FDE" in d or "dirty" in d:      # 기본(검정) 텍스처만
            continue
        if name in files:
            return os.path.join(d, name)
    return None

def load(path, gray=False):
    img = bpy.data.images.load(path, check_existing=False)
    img.colorspace_settings.name = "Non-Color"
    if img.size[0] != SIZE:
        img.scale(SIZE, SIZE)
    a = np.empty(SIZE * SIZE * 4, dtype=np.float32); img.pixels.foreach_get(a)
    bpy.data.images.remove(img)
    a = a.reshape(SIZE, SIZE, 4)
    return a[..., 0] if gray else a

def save(arr, path):
    img = bpy.data.images.new(os.path.basename(path), SIZE, SIZE, alpha=True, float_buffer=False)
    img.colorspace_settings.name = "Non-Color"
    img.pixels.foreach_set(np.ascontiguousarray(arr, dtype=np.float32).ravel())
    img.filepath_raw = path; img.file_format = "PNG"; img.save()
    bpy.data.images.remove(img)

materials = sorted({m.name for o in meshes for m in o.data.materials if m})
tex_report = {}
for mat in materials:
    base = find_tex(mat + "_BaseColor.png"); spec = find_tex(mat + "_Specular.png")
    rough = find_tex(mat + "_Roughness.png"); norm = find_tex(mat + "_Normal.png")
    if not all((base, spec, rough, norm)):
        tex_report[mat] = "텍스처 없음"; continue
    b = load(base); s = load(spec); r = load(rough, gray=True); n = load(norm)
    main = b.copy(); main[..., 3] = s[..., 0]
    gloss = s.copy(); gloss[..., 3] = 1.0 - r
    save(main, os.path.join(OUT, "Textures", mat + "_main.png"))
    save(gloss, os.path.join(OUT, "Textures", mat + "_spec.png"))
    save(n, os.path.join(OUT, "Textures", mat + "_normal.png"))
    tex_report[mat] = "ok"

report = {"fbx": fbx_out, "rotation": "Z +90deg (muzzle -X -> -Y)", "texture_size": SIZE, "textures": tex_report,
          "parts": {o.name: {"origin_m": [round(v, 4) for v in o.location], "size_m": [round(v, 4) for v in o.dimensions],
                             "materials": [m.name for m in o.data.materials if m]} for o in meshes}}
with open(os.path.join(OUT, "k2_parts.json"), "w", encoding="utf-8") as f:
    json.dump(report, f, ensure_ascii=False, indent=1)
print("RESULT", json.dumps(tex_report, ensure_ascii=False))
