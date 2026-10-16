"""빌드된 K2 번들의 셰이더 참조를 게임 shaders 번들의 진짜 셰이더로 바꾼다(번들에 셰이더를 넣지 않는다).

왜: SDK 셰이더(디컴파일본)를 번들에 넣으면 게임에서 흰색·회색으로 보였다(1차 인게임, all-v002).
바닐라·WTT 번들은 재질이 게임 shaders 번들(CAB-56d919…)의 셰이더를 외부 참조한다 — 그 형태로 맞춘다.

방법: Unity 빌드 때 SDK 셰이더를 가짜 "shaders" 번들로 빼서 외부 참조로 만든 뒤(K2Parts.cs),
  1) 외부 파일 경로(가짜 CAB) → 게임 shaders 번들의 CAB
  2) 재질의 셰이더 PathID(가짜) → 게임 shaders 번들에서 같은 이름 셰이더의 PathID
로 바꾼다. 원본 출력은 그대로 두고 새 폴더에 쓴다.

  python tools/fix_shader_refs.py <빌드 폴더(raw)> <새 출력 폴더> <게임 StreamingAssets/Windows>
"""
import os, sys, shutil, json
import UnityPy

raw, out, game = sys.argv[1], sys.argv[2], sys.argv[3]
# 셰이더가 비어 있는(추출 때 잃은) 재질은 같은 이름의 바닐라 재질이 쓰는 셰이더로 연결한다 — 템플릿이 의존하던 바닐라 번들
VANILLA_MATERIAL_SOURCES = ["assets/systems/effects/smoke.bundle", "assets/systems/effects/muzzleflash/muzzleflash.bundle",
                            "assets/systems/effects/heathaze/defaultheathaze.bundle"]
if os.path.exists(out) and os.listdir(out):
    sys.exit(f"STOP: 출력 폴더가 비어 있지 않음 — 덮어쓰지 않습니다: {out}")


def shader_names(path):
    """번들 안 셰이더: path_id → 이름, 그리고 내부 CAB 이름"""
    env = UnityPy.load(path)
    names, cab = {}, None
    for name, f in env.file.files.items():
        if name.startswith("CAB-") and not name.endswith((".resS", ".resource")):
            cab = name
    for obj in env.objects:
        if obj.type.name == "Shader":
            names[obj.path_id] = obj.read().m_ParsedForm.m_Name
    return names, cab


game_names, game_cab = shader_names(os.path.join(game, "shaders"))
fake_names, fake_cab = shader_names(os.path.join(raw, "shaders"))
game_by_name = {}
for pid, n in game_names.items():
    game_by_name.setdefault(n, pid)
remap = {}
for pid, n in fake_names.items():
    if n not in game_by_name:
        sys.exit(f"STOP: 게임 shaders 번들에 같은 이름 셰이더 없음: {n}")
    remap[pid] = game_by_name[n]
print("게임 shaders CAB:", game_cab, "/ 가짜 CAB:", fake_cab)
for pid, n in fake_names.items():
    print(f"  {n}: {pid} → {remap[pid]}")

# 바닐라 재질 이름 → 게임 셰이더 PathID
vanilla_mat = {}
for rel in VANILLA_MATERIAL_SOURCES:
    pth = os.path.join(game, rel)
    if not os.path.exists(pth):
        continue
    venv = UnityPy.load(pth)
    for _, vsf in venv.file.files.items():
        vext = getattr(vsf, "externals", None)
        if vext is None:
            continue
        for o in vsf.objects.values():
            if o.type.name == "Material":
                t = o.read_typetree(); sh = t["m_Shader"]
                if sh["m_FileID"] > 0 and game_cab in vext[sh["m_FileID"] - 1].path:
                    vanilla_mat.setdefault(t["m_Name"], sh["m_PathID"])
print("바닐라 재질 셰이더 표:", len(vanilla_mat), "개")

report = {"gameShadersCab": game_cab, "remap": {fake_names[k]: [k, v] for k, v in remap.items()}, "bundles": {}}
for dirpath, _, files in os.walk(raw):
    for fn in files:
        src = os.path.join(dirpath, fn)
        rel = os.path.relpath(src, raw).replace("\\", "/")
        dst = os.path.join(out, rel)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        if not fn.endswith(".bundle"):
            if rel != "shaders" and not rel.startswith("shaders."):   # 가짜 shaders 번들은 배포하지 않는다
                shutil.copy2(src, dst)
            continue
        env = UnityPy.load(src)
        changed = 0
        for name, sf in env.file.files.items():
            ext = getattr(sf, "externals", None)
            if not ext:
                continue
            fake_idx = [i + 1 for i, e in enumerate(ext) if fake_cab in e.path]
            for e in ext:
                if fake_cab in e.path:
                    e.path = e.path.replace(fake_cab, game_cab)

            def game_idx():
                """게임 shaders CAB 외부 항목 번호(없으면 기존 항목을 복사해 추가)"""
                for i, e in enumerate(ext):
                    if game_cab in e.path:
                        return i + 1
                import copy
                e = copy.copy(ext[0]); e.path = f"archive:/{game_cab}/{game_cab}"
                ext.append(e)
                return len(ext)

            for obj in sf.objects.values():
                if obj.type.name != "Material":
                    continue
                tree = obj.read_typetree()
                sh = tree["m_Shader"]
                if sh["m_FileID"] in fake_idx and sh["m_PathID"] in remap:
                    sh["m_PathID"] = remap[sh["m_PathID"]]
                elif sh["m_FileID"] == 0 and sh["m_PathID"] == 0 and tree.get("m_Name") in vanilla_mat:
                    sh["m_FileID"] = game_idx(); sh["m_PathID"] = vanilla_mat[tree["m_Name"]]
                else:
                    continue
                obj.save_typetree(tree)
                changed += 1
        with open(dst, "wb") as f:
            f.write(env.file.save(packer="lz4"))
        report["bundles"][rel] = changed
        print(f"{rel}: 재질 {changed}개 셰이더 참조 교체")

# 다시 읽어 확인: 모든 재질이 게임 CAB를 가리키는지
bad = []
for rel in report["bundles"]:
    env = UnityPy.load(os.path.join(out, rel))
    for name, sf in env.file.files.items():
        ext = getattr(sf, "externals", None) or []
        for obj in getattr(sf, "objects", {}).values():
            if obj.type.name == "Material":
                sh = obj.read_typetree()["m_Shader"]
                target = ext[sh["m_FileID"] - 1].path if sh["m_FileID"] > 0 else "(내부)"
                ok = game_cab in target and sh["m_PathID"] in game_names
                if not ok:
                    bad.append(f"{rel}: {obj.read_typetree().get('m_Name')} → {target} {sh['m_PathID']}")
report["notGameShader"] = bad
with open(os.path.join(out, "shader-fix.json"), "w", encoding="utf-8") as f:
    json.dump(report, f, ensure_ascii=False, indent=1)
print("게임 셰이더가 아닌 재질:", len(bad))
for b in bad:
    print("  ", b)
