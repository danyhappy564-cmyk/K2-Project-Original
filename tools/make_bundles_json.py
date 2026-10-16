"""bundles.json 생성 + 외부 참조 검사.

각 K2 번들의 dependencyKeys = (Unity가 만든 의존: 공용 텍스처) + shaders·cubemaps + (바닐라 템플릿 번들과 그 의존 전부, Windows.json 기준).
그리고 번들 안 외부 참조(CAB)가 전부 그 의존 목록 안 번들의 CAB인지 확인한다 — 하나라도 없으면 게임에서 참조가 비어 버린다.

  python tools/make_bundles_json.py <번들 폴더> <validation.json> <게임 StreamingAssets/Windows> <출력 bundles.json> [ak5c|scar]
"""
import os, sys, json
import UnityPy

root, validation, game, out = sys.argv[1:5]
# K2 번들 → 바닐라 템플릿 번들(참조를 옮겨 심은 원본)
TEMPLATE = {
    "assets/content/weapons/daewoo_k2/weapon_daewoo_k2_556x45_container.bundle": "assets/content/weapons/ak100/weapon_izhmash_ak101_556x45_container.bundle",
    "assets/content/items/mods/barrels/barrel_daewoo_k2_465mm.bundle": "assets/content/items/mods/barrels/barrel_ar15_370mm.bundle",
    "assets/content/items/mods/muzzle/muzzle_daewoo_k2_std_556x45.bundle": "assets/content/items/mods/muzzle/muzzle_ar15_colt_usgi_a2_556x45.bundle",
    "assets/content/items/mods/handguards/handguard_daewoo_k2_mits_long.bundle": "assets/content/items/mods/handguards/handguard_ar15_colt_m4_length_std.bundle",
    "assets/content/items/mods/recievers/reciever_daewoo_k2_std.bundle": "assets/content/items/mods/recievers/reciever_ar15_colt_m4a1_std.bundle",
    "assets/content/items/mods/stocks/stock_daewoo_k2_std.bundle": "assets/content/items/mods/stocks/stock_ar15_colt_stock_tube_std.bundle",
    "assets/content/items/mods/magazines/mag_stanag_daewoo_k2_std_556x45_30.bundle": "assets/content/items/mods/magazines/mag_stanag_colt_ar15_std_556x45_30.bundle",
}
manifest = json.load(open(os.path.join(game, "Windows.json"), encoding="utf-8"))
# 0.7.0 AK5C 도너: 컨테이너 원본이 WTT-Armory 번들 — 그 의존 목록(게임 번들)으로 후보를 만든다
WTT_MANIFEST = "E:/SPT 4.1/SPT_Runtime/user/mods/WTT-Armory/bundles.json"
if len(sys.argv) > 5 and sys.argv[5] == "scar":   # 0.8.0 바닐라 SCAR 도너
    TEMPLATE["assets/content/weapons/daewoo_k2/weapon_daewoo_k2_556x45_container.bundle"] = "assets/content/weapons/scar/weapon_fn_mk16_556x45_container.bundle"
if len(sys.argv) > 5 and sys.argv[5] == "ak5c":
    wtt = {e["key"]: e["dependencyKeys"] for e in json.load(open(WTT_MANIFEST, encoding="utf-8"))["manifest"]}
    TEMPLATE["assets/content/weapons/daewoo_k2/weapon_daewoo_k2_556x45_container.bundle"] = "WeaponAK5C/weapon_ak5c_container.bundle"
    manifest["WeaponAK5C/weapon_ak5c_container.bundle"] = {"Dependencies": wtt["WeaponAK5C/weapon_ak5c_container.bundle"]}


def closure(key):
    seen, todo = [], [key]
    while todo:
        k = todo.pop()
        if k in seen:
            continue
        seen.append(k)
        todo += manifest.get(k, {}).get("Dependencies", [])
    return seen


cab_cache = {}


def cab_of_file(path):
    if path not in cab_cache:
        env = UnityPy.load(path)
        cab_cache[path] = {n for n in env.file.files if n.startswith("CAB-") and not n.endswith((".resS", ".resource"))}
    return cab_cache[path]


val = json.load(open(validation, encoding="utf-8"))
ours = {b["key"]: b["dependencyKeys"] for b in val["bundles"]}
result, problems = [], []
for key, own_deps in ours.items():
    # 후보 = Unity 의존 + shaders·cubemaps + 바닐라 템플릿과 그 의존. 실제로 참조하는 CAB을 가진 번들만 남긴다(0.3.0 — WTT처럼 최소 의존)
    env = UnityPy.load(os.path.join(root, key))
    needed = set()
    for name, sf in env.file.files.items():
        for e in getattr(sf, "externals", None) or []:
            if e.path.startswith("archive:/"):
                needed.add(e.path.split("/")[1])
    candidates = list(own_deps) + ["shaders", "cubemaps"] + (closure(TEMPLATE[key]) if key in TEMPLATE else [])
    deps, have = [], set()
    for d in dict.fromkeys(candidates):
        p = os.path.join(root, d) if d in ours else os.path.join(game, d)
        cabs = cab_of_file(p) if os.path.isfile(p) else set()
        if d in own_deps or d in ("shaders", "cubemaps") or (cabs & needed):
            deps.append(d); have |= cabs
    missing = sorted(needed - have)
    if missing:
        problems.append((key, missing))
    result.append({"key": key, "dependencyKeys": deps})
    print(f"{key.split('/')[-1]}: 의존 {len(deps)}개, 외부 참조 {len(needed)}개, 못 찾은 참조 {len(missing)}개")
json.dump({"manifest": result}, open(out, "w", encoding="utf-8"), indent=2)
for k, m in problems:
    print("  못 찾음", k.split("/")[-1], m)
sys.exit(1 if problems else 0)

