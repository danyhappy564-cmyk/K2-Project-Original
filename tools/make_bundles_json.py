"""bundles.json 생성 + 외부 참조 검사.

각 K2 번들의 dependencyKeys = (Unity가 만든 의존: 공용 텍스처) + shaders·cubemaps + (바닐라 템플릿 번들과 그 의존 전부, Windows.json 기준).
그리고 번들 안 외부 참조(CAB)가 전부 그 의존 목록 안 번들의 CAB인지 확인한다 — 하나라도 없으면 게임에서 참조가 비어 버린다.

  python tools/make_bundles_json.py <번들 폴더> <validation.json> <게임 StreamingAssets/Windows> <출력 bundles.json>
"""
import os, sys, json
import UnityPy

root, validation, game, out = sys.argv[1:5]
# K2 번들 → 바닐라 템플릿 번들(참조를 옮겨 심은 원본)
TEMPLATE = {
    "assets/content/weapons/daewoo_k2/weapon_daewoo_k2_556x45_container.bundle": "assets/content/weapons/m4a1/weapon_colt_m4a1_556x45_container.bundle",
    "assets/content/items/mods/barrels/barrel_daewoo_k2_465mm.bundle": "assets/content/items/mods/barrels/barrel_ar15_370mm.bundle",
    "assets/content/items/mods/muzzle/muzzle_daewoo_k2_std_556x45.bundle": "assets/content/items/mods/muzzle/muzzle_ar15_colt_usgi_a2_556x45.bundle",
    "assets/content/items/mods/handguards/handguard_daewoo_k2_mits_long.bundle": "assets/content/items/mods/handguards/handguard_ar15_colt_m4_length_std.bundle",
    "assets/content/items/mods/recievers/reciever_daewoo_k2_std.bundle": "assets/content/items/mods/recievers/reciever_ar15_colt_m4a1_std.bundle",
    "assets/content/items/mods/stocks/stock_daewoo_k2_std.bundle": "assets/content/items/mods/stocks/stock_ar15_colt_stock_tube_std.bundle",
    "assets/content/items/mods/magazines/mag_stanag_daewoo_k2_std_556x45_30.bundle": "assets/content/items/mods/magazines/mag_stanag_colt_ar15_std_556x45_30.bundle",
}
manifest = json.load(open(os.path.join(game, "Windows.json"), encoding="utf-8"))


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
    deps = list(own_deps) + ["shaders", "cubemaps"]
    if key in TEMPLATE:
        deps += closure(TEMPLATE[key])
    deps = list(dict.fromkeys(deps))
    # 검사: 외부 참조 CAB이 의존 번들 중 하나의 CAB인가
    env = UnityPy.load(os.path.join(root, key))
    needed = set()
    for name, sf in env.file.files.items():
        for e in getattr(sf, "externals", None) or []:
            if e.path.startswith("archive:/"):
                needed.add(e.path.split("/")[1])
    have = set()
    for d in deps:
        p = os.path.join(root, d) if d in ours else os.path.join(game, d)
        if os.path.isfile(p):
            have |= cab_of_file(p)
    missing = sorted(needed - have)
    if missing:
        problems.append((key, missing))
    result.append({"key": key, "dependencyKeys": deps})
    print(f"{key.split('/')[-1]}: 의존 {len(deps)}개, 외부 참조 {len(needed)}개, 못 찾은 참조 {len(missing)}개")
json.dump({"manifest": result}, open(out, "w", encoding="utf-8"), indent=2)
for k, m in problems:
    print("  못 찾음", k.split("/")[-1], m)
sys.exit(1 if problems else 0)
