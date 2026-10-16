"""K2 번들의 컴포넌트가 게임 원본(바닐라) 에셋을 외부 참조하도록 옮겨 심는다 — 애니메이터·아바타·사운드·총구 효과.

왜(2차 인게임, 0.1.1): 추출본(AssetRipper)으로 만든 사운드 참조는 대부분 비어 총소리가 안 났고, 재구성한 애니메이터·아바타로는
애니메이션이 동작하지 않고 파지 자세가 이상했다. 바닐라 M4A1 번들은 같은 컴포넌트가 게임 번들의 원본 에셋을 참조한다.

방법: 우리 번들의 컴포넌트(MonoBehaviour·Animator)마다 바닐라 번들에서 짝(같은 GameObject 이름·같은 스크립트)을 찾아,
  바닐라 쪽 참조가 ① 다른 번들(외부)이면 같은 외부 파일·PathID로, ② 바닐라 번들 안의 에셋(컨트롤러·클립·아바타·SO 등)이면
  바닐라 번들 CAB을 외부 참조로 바꿔 넣는다. 프리팹 안의 GameObject·Transform·컴포넌트 참조는 우리 것을 그대로 둔다.
  구조가 다른 필드는 건드리지 않는다(우리 타입트리 기준).

  python tools/fix_vanilla_refs.py <우리 번들(입력)> <출력 번들> <바닐라 번들> [우리이름=바닐라이름 ...]
"""
import os, sys, copy
import UnityPy

src, dst, vpath = sys.argv[1], sys.argv[2], sys.argv[3]
name_map = dict(a.split("=", 1) for a in sys.argv[4:])
if os.path.exists(dst):
    sys.exit(f"STOP: 이미 있음 — 덮어쓰지 않습니다: {dst}")
HIER = {"GameObject", "Transform", "RectTransform"}


def cab_of(env):
    for name in env.file.files:
        if name.startswith("CAB-") and not name.endswith((".resS", ".resource")):
            return name


def main_sf(env):
    return env.file.files[cab_of(env)]


def pptrs(tree, path=()):
    if isinstance(tree, dict):
        if "m_FileID" in tree and "m_PathID" in tree:
            yield path, tree
            return
        for k, v in tree.items():
            yield from pptrs(v, path + (k,))
    elif isinstance(tree, list):
        for i, v in enumerate(tree):
            yield from pptrs(v, path + (i,))


def get(tree, path):
    for p in path:
        if isinstance(p, int):
            if not isinstance(tree, list) or p >= len(tree):
                return None
        elif not isinstance(tree, dict) or p not in tree:
            return None
        tree = tree[p]
    return tree


def index(sf):
    """컴포넌트 → (GameObject 이름, 종류/스크립트 이름, 순번) 키"""
    objs = sf.objects
    trees = {}

    def tree(pid):
        if pid not in trees:
            try:
                trees[pid] = objs[pid].read_typetree()
            except Exception:
                trees[pid] = None
        return trees[pid]

    go_name = {pid: (tree(pid) or {}).get("m_Name", "") for pid, o in objs.items() if o.type.name == "GameObject"}
    keys, count = {}, {}
    for pid, o in objs.items():
        if o.type.name not in ("MonoBehaviour", "Animator"):
            continue
        t = tree(pid)
        if not t:
            continue
        go = t.get("m_GameObject", {}).get("m_PathID", 0)
        if not go:
            continue   # 프리팹에 붙지 않은 것(애니메이터 상태 동작·SO)은 제외
        kind = o.type.name
        if kind == "MonoBehaviour":
            s = t.get("m_Script", {})
            st = tree(s.get("m_PathID")) if s.get("m_FileID") == 0 and s.get("m_PathID") in objs else None
            kind = (st or {}).get("m_ClassName") or (st or {}).get("m_Name") or f"script{s.get('m_PathID')}"
        base = (go_name.get(go, ""), kind)
        n = count.get(base, 0); count[base] = n + 1
        keys[base + (n,)] = pid
    return keys, tree


venv = UnityPy.load(vpath); oenv = UnityPy.load(src)
vsf, osf = main_sf(venv), main_sf(oenv)
vcab = cab_of(venv)
vkeys, vtree = index(vsf)
okeys, otree = index(osf)
oext = osf.externals


def ext_index(path):
    for i, e in enumerate(oext):
        if e.path == path:
            return i + 1
    e = copy.copy(oext[0]) if oext else None
    if e is None:
        sys.exit("STOP: 우리 번들에 외부 항목이 하나도 없어 복사할 틀이 없음")
    e.path = path
    oext.append(e)
    return len(oext)


def norm(name):
    for a, b in name_map.items():
        name = name.replace(a, b)
    return name


changed_total = 0
for (goname, kind, n), opid in okeys.items():
    vpid = vkeys.get((norm(goname), kind, n))
    if vpid is None:
        continue
    vt, ot = vtree(vpid), otree(opid)
    if not vt or not ot:
        continue
    changed = 0
    for path, vp in pptrs(vt):
        if path and path[0] in ("m_GameObject", "m_Script"):
            continue
        op = get(ot, path)
        if not isinstance(op, dict) or "m_PathID" not in op:
            continue
        fid, pid = vp["m_FileID"], vp["m_PathID"]
        if pid == 0:
            continue
        if fid == 0:
            target = vsf.objects.get(pid)
            if target is None or target.type.name in HIER:
                continue
            tt = vtree(pid) or {}
            if "m_GameObject" in tt and tt["m_GameObject"].get("m_PathID"):
                continue   # 프리팹 안 컴포넌트 참조 → 우리 것 유지
            new = {"m_FileID": ext_index(f"archive:/{vcab}/{vcab}"), "m_PathID": pid}
        else:
            new = {"m_FileID": ext_index(vsf.externals[fid - 1].path), "m_PathID": pid}
        if op.get("m_FileID") != new["m_FileID"] or op.get("m_PathID") != new["m_PathID"]:
            op.update(new); changed += 1
    if changed:
        osf.objects[opid].save_typetree(ot)
        changed_total += changed
        print(f"  {goname} [{kind}#{n}]: 참조 {changed}개 → 바닐라")
os.makedirs(os.path.dirname(dst), exist_ok=True)
with open(dst, "wb") as f:
    f.write(oenv.file.save(packer="lz4"))
print(f"완료: {os.path.basename(dst)} — 참조 {changed_total}개 교체, 바닐라 CAB {vcab}, 외부 항목 {len(oext)}개")
