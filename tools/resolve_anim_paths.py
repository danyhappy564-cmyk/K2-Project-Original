"""AssetRipper가 이름을 못 찾아 숫자(해시)로 남긴 애니메이션 곡선 경로를 진짜 노드 경로로 되돌린다.

왜(0.1.x·0.3.0 인게임: 손이 이상하게 잡고 장전·재장전 동작이 안 나옴): 게임 번들의 클립은 노드 경로를 CRC32 해시로만 갖고 있다.
AssetRipper가 이름을 복원 못 한 경로는 "path: 3152829502"처럼 숫자로 내보내는데, Unity가 다시 빌드하면 그 숫자 문자열의
해시를 또 계산해 원래 노드와 연결이 끊긴다 — SCAR 클립 경로 참조의 절반(팔·손가락 뼈, Camera_animated 등)이 이렇게 죽어 있었다.

방법: 도너 무기 프리팹 계층(게임 번들 덤프 JSON)의 모든 경로에 CRC32를 계산해 표를 만들고(+ 슬롯 아래 부품 프리팹 경로),
클립 YAML의 곡선 경로 줄("    path: 숫자", 들여쓰기 4칸)만 이름으로 바꾼다. m_ClipBindingConstant의 경로(들여쓰기 6칸, 원래 해시)는 그대로 둔다.

  python tools/resolve_anim_paths.py <클립 폴더(SDK 안 추출본)> <모델 덤프 JSON> <모델 프리팹 이름> [부품 덤프 JSON ...]
"""
import sys, re, json, zlib, glob, collections

clip_dir, model_json, model_name = sys.argv[1], sys.argv[2], sys.argv[3]
mod_jsons = sys.argv[4:]


def walk(n, prefix, out):
    for c in n["ch"]:
        p = (prefix + "/" + c["n"]) if prefix else c["n"]
        out[p] = c
        walk(c, p, out)


d = json.load(open(model_json, encoding="utf-8"))
model = next(p for p in d["prefabs"] if p["n"] == model_name)
base = {}
walk(model, "", base)
table = {0: ""}   # CRC32("") = 0 → 루트
for p in base:
    table[zlib.crc32(p.encode("utf-8"))] = p
for mj in mod_jsons:   # 슬롯 아래 붙는 부품 프리팹(이름 그대로·(Clone)) 경로
    t = json.load(open(mj, encoding="utf-8"))["prefabs"][0]
    sub = {}
    walk(t, "", sub)
    for slot in [p for p in base if p.split("/")[-1].startswith("mod_")]:
        for rn in (t["n"], t["n"] + "(Clone)"):
            for c in [slot + "/" + rn] + [slot + "/" + rn + "/" + s for s in sub]:
                table.setdefault(zlib.crc32(c.encode("utf-8")), c)

curve_path = re.compile(r"^    path: (\d+)[ \t]*$", re.M)
stats = collections.Counter()
for f in glob.glob(clip_dir + "/**/*.anim", recursive=True):
    text = open(f, encoding="utf-8").read()

    def fix(m):
        h = int(m.group(1))
        if h in table:
            stats["resolved"] += 1
            return "    path: " + table[h]
        stats["unresolved"] += 1
        stats["u:" + str(h)] += 1
        return m.group(0)

    new = curve_path.sub(fix, text)
    if new != text:
        open(f, "w", encoding="utf-8", newline="\n").write(new)
        stats["files"] += 1
print(f"클립 파일 {stats['files']}개 수정, 곡선 경로 {stats['resolved']}개 복원, 못 찾음 {stats['unresolved']}개")
print("못 찾은 해시:", {k[2:]: v for k, v in stats.items() if k.startswith("u:")})
