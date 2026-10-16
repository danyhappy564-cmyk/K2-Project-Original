"""바닐라 번들을 모드 안에 새 이름·새 CAB으로 복사하고, 우리 번들의 외부 참조를 그 복사본으로 옮긴다.

왜(0.8.0~0.8.1 인게임: 팔 펴짐·총 떠 있음, errors.log "LayersDefaultStates.Length 7 != _animator.layerCount 0"):
바닐라 SCAR의 애니메이터 컨트롤러는 FDE 컨테이너 번들(CAB-d2d9…)에 있다. 그런데 WTT-ContentBackport가 같은 키로 자기 FDE 번들
(CAB-e69b…)을 덮어써서, 게임에선 K2 컨테이너의 CAB-d2d9 참조가 비었다(Unity 에디터에서 바닐라를 읽으면 정상).
다른 모드가 바닐라 키를 덮어써도 영향받지 않게, 필요한 바닐라 번들을 우리 키·우리 CAB으로 들고 다닌다.
CAB을 바꾸므로 원본(또는 덮어쓴 번들)과 함께 불려도 충돌하지 않는다.

  python tools/vendor_bundle.py <원본 번들> <출력 번들> <새 번들 키> <우리 번들(외부 참조 옮김, 제자리)> ...
"""
import hashlib, os, sys
import UnityPy

src, dst, key = sys.argv[1], sys.argv[2], sys.argv[3]
users = sys.argv[4:]
if os.path.exists(dst):
    sys.exit(f"STOP: 이미 있음 — 덮어쓰지 않습니다: {dst}")


def serialized(env):
    return next((n, f) for n, f in env.file.files.items() if hasattr(f, "objects"))


raw = open(src, "rb").read()
env = UnityPy.load(raw)
oldcab, sf = serialized(env)
newcab = "CAB-" + hashlib.sha256(raw + key.encode()).hexdigest()[:32]
for o in sf.objects.values():
    if o.type.name == "AssetBundle":
        t = o.read_typetree(); t["m_Name"] = key; t["m_AssetBundleName"] = key; o.save_typetree(t)
    elif o.type.name in ("AudioClip", "Texture2D", "Mesh"):
        t = o.read_typetree()
        for f in ("m_Resource", "m_StreamData"):
            if f in t and isinstance(t[f], dict):
                for p in ("m_Source", "path"):
                    if p in t[f] and isinstance(t[f][p], str) and oldcab in t[f][p]:
                        t[f][p] = t[f][p].replace(oldcab, newcab); o.save_typetree(t)
env.file.files = {n.replace(oldcab, newcab): f for n, f in env.file.files.items()}
sf.name = newcab
os.makedirs(os.path.dirname(dst), exist_ok=True)
open(dst, "wb").write(env.file.save(packer="lz4"))
print(f"복사: {os.path.basename(src)} {oldcab} → {key} {newcab}")

for u in users:
    with open(u, "rb") as f:
        e = UnityPy.load(f.read())
    _, usf = serialized(e)
    n = 0
    for ext in usf.externals:
        if oldcab.lower() in ext.path.lower():
            ext.path = f"archive:/{newcab}/{newcab}"; n += 1
    for o in usf.objects.values():
        if o.type.name == "AssetBundle":
            t = o.read_typetree()
            deps = t.get("m_Dependencies", [])
            t["m_Dependencies"] = [key if d.lower().endswith(os.path.basename(src).lower()) else d for d in deps]
            o.save_typetree(t)
    data = e.file.save(packer="lz4")
    open(u + ".tmp", "wb").write(data); os.replace(u + ".tmp", u)
    print(f"참조 옮김: {os.path.basename(u)} 외부 {n}개 {oldcab} → {newcab}")
    if not n:
        sys.exit("STOP: 옮길 참조가 없음")

# 다시 읽어 확인
chk = UnityPy.load(dst); c, csf = serialized(chk)
assert c == newcab and set(csf.objects) == set(sf.objects), "복사본 확인 실패"
for u in users:
    _, usf = serialized(UnityPy.load(u))
    assert any(newcab in x.path for x in usf.externals) and not any(oldcab in x.path for x in usf.externals), "참조 확인 실패"
print("확인 통과")
