"""개조한 컨테이너 번들의 AssetBundle 등록 목록(m_Container)을 컨테이너 프리팹 하나로 정리한다.

왜(0.4.0 인게임: 총이 안 보이고 팔이 펴짐): EFT errors.log에 "Invalid _weaponObject: weapon_izhmash_ak101_556x45.generated"가 반복됐다.
개조본은 프리팹 3개(.generated·_container·_simple)가 등록돼 있고 사전 로딩 목록 0번이 모델(.generated)이었다.
게임이 0번 항목을 본체로 집어 들어 무기 생성이 실패했다. WTT HK417과 0.3.0 번들은 컨테이너 하나만, 사전 로딩 0번부터 전체로 등록돼 있다.

  python tools/fix_container_table.py <입력 번들> <출력 번들> <컨테이너 에셋 경로(소문자)>
"""
import os, sys
import UnityPy

src, dst, key = sys.argv[1], sys.argv[2], sys.argv[3]
if os.path.exists(dst):
    sys.exit(f"STOP: 이미 있음 — 덮어쓰지 않습니다: {dst}")
env = UnityPy.load(src)
for name, sf in env.file.files.items():
    if not hasattr(sf, "objects"):
        continue
    ab_obj = next(o for o in sf.objects.values() if o.type.name == "AssetBundle")
    ab = ab_obj.read_typetree()
    entry = next(info for k, info in ab["m_Container"] if k.endswith("_container.prefab"))
    total = len(ab["m_PreloadTable"])
    entry["preloadIndex"] = 0
    entry["preloadSize"] = total
    ab["m_Container"] = [[key, entry]]
    ab_obj.save_typetree(ab)
    print(f"등록 목록: {key} → 사전 로딩 0+{total}, 대상 PathID {entry['asset']['m_PathID']}")
os.makedirs(os.path.dirname(dst), exist_ok=True)
with open(dst, "wb") as f:
    f.write(env.file.save(packer="lz4"))
# 다시 읽어 확인
env2 = UnityPy.load(dst)
for name, sf in env2.file.files.items():
    if not hasattr(sf, "objects"):
        continue
    ab = next(o for o in sf.objects.values() if o.type.name == "AssetBundle").read_typetree()
    for k, info in ab["m_Container"]:
        o = sf.objects[info["asset"]["m_PathID"]]
        print("확인:", k, info["preloadIndex"], info["preloadSize"], "→", o.read_typetree()["m_Name"])
