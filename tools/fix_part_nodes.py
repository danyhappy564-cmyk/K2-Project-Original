"""부품 번들의 템플릿 기준 노드(왼손 파지·탄창 총알)를 K2 메시와 AK-101 동작에 맞게 옮긴다.

왜(0.4.1 인게임: "탄창에 총알 저렇게 보이는거랑 잡는 위치가 이상"):
- 부품은 M4 템플릿(손잡이 handguard_ar15_colt_m4_length_std, 탄창 mag_stanag_colt_ar15_std)을 쓰고 메시만 K2로 bake했다.
  노드는 템플릿 위치 그대로라
  · 손잡이의 왼손 기준 노드 Base HumanLPalm(·Base HumanLPalm 1)이 AK-101 손잡이 위치보다 약 11cm 뒤(리시버 앞)에 있었다 → 왼손이 리시버를 잡음.
  · 탄창의 patron_001~003(위쪽 총알 3발 위치)이 Colt 탄창 메시 기준이라 K2 탄창보다 약 4.5cm 뒤·9mm 아래 → 총알이 탄창 밖에 보임.
- 고침: 왼손 노드는 바닐라 AK-74M 손잡이(AK-101 기본 구성)의 손바닥·손가락 자세를 그대로 가져와 무기 좌표에서 같은 자리에 두되
  두 손잡이 외곽(BoxCollider) 왼쪽 면·아랫면 차이(뒤끝이 AK보다 앞이면 그 차이도)만큼 옮겨 손과 손잡이 표면 사이 간격을 AK와 같게 하고,
  총알 노드는 Colt 탄창 윗면(급탄구 뒤끝·윗면) 기준 상대 위치를 K2 탄창 윗면 기준으로 옮긴다.

좌표: 무기(weapon) 노드 기준 -y가 앞, +z가 위. 슬롯 회전은 단위, 부품 루트는 슬롯 회전 × X축 +90°로 붙는다.

  python tools/fix_part_nodes.py <K2 번들 폴더(assets/ 위)> <출력 폴더> <게임 StreamingAssets/Windows(읽기 전용)> [--inplace]
  --inplace: 출력 폴더 = 입력 폴더, 고친 번들을 제자리에 바꿔 쓴다(finish_bundles.py). 두 번 돌리면 총알이 두 번 옮겨지므로
  part-nodes.json이 이미 있으면 멈춘다.
"""
import os, sys, json
import UnityPy

src_root, dst_root, game = sys.argv[1], sys.argv[2], sys.argv[3]
INPLACE = "--inplace" in sys.argv[4:]
if os.path.exists(os.path.join(dst_root, "part-nodes.json")): sys.exit(f"STOP: 이미 처리됨 — {dst_root}/part-nodes.json")
C = "assets/content/"
K2_CONTAINER = C + "weapons/daewoo_k2/weapon_daewoo_k2_556x45_container.bundle"
K2_UPPER = C + "items/mods/recievers/reciever_daewoo_k2_std.bundle"
K2_HANDGUARDS = [C + "items/mods/handguards/handguard_daewoo_k2_mits_long.bundle", C + "items/mods/handguards/handguard_daewoo_k2_classic.bundle"]
K2_MAG = C + "items/mods/magazines/mag_stanag_daewoo_k2_std_556x45_30.bundle"
AK_HANDGUARD = C + "items/mods/handguards/handguard_ak_izhmash_ak74m_std_plastic.bundle"   # AK-101 기본 프리셋 손잡이
COLT_MAG = C + "items/mods/magazines/mag_stanag_colt_ar15_std_556x45_30.bundle"            # K2 탄창 템플릿


# ── 쿼터니언·변환(x, y, z, w) ──
def qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz)
def qinv(q): return (-q[0], -q[1], -q[2], q[3])
def qrot(q, v):
    x, y, z, _ = qmul(qmul(q, (v[0], v[1], v[2], 0.0)), qinv(q)); return (x, y, z)
def add(a, b): return tuple(p + q for p, q in zip(a, b))
def sub(a, b): return tuple(p - q for p, q in zip(a, b))
def compose(parent, local):   # (위치, 회전) — 크기는 전부 1
    return add(parent[0], qrot(parent[1], local[0])), qmul(parent[1], local[1])
def relative(parent, world):
    inv = qinv(parent[1]); return qrot(inv, sub(world[0], parent[0])), qmul(inv, world[1])
RX90 = (0.70710678, 0.0, 0.0, 0.70710678)
ATTACH = ((0.0, 0.0, 0.0), RX90)   # 슬롯 → 부품 루트


class Tree:
    """번들 하나의 Transform을 이름·경로로 다룬다."""
    def __init__(self, path):
        self.path = path
        with open(path, "rb") as f: self.env = UnityPy.load(f.read())   # 바이트로 — 제자리 저장 때 파일을 잡고 있지 않게
        self.objs = {o.path_id: o for o in self.env.objects}
        self.tt = {pid: o.read_typetree() for pid, o in self.objs.items() if o.type.name == "Transform"}
        self.names = {pid: self.objs[t["m_GameObject"]["m_PathID"]].read_typetree()["m_Name"] for pid, t in self.tt.items()}
        self.dirty = set()

    def find(self, name, under=None):
        hits = [pid for pid, n in self.names.items() if n == name and (under is None or self.is_under(pid, under))]
        if len(hits) != 1: sys.exit(f"STOP: {os.path.basename(self.path)} 노드 '{name}' {len(hits)}개")
        return hits[0]

    def is_under(self, pid, anc):
        while pid:
            if pid == anc: return True
            pid = self.tt[pid]["m_Father"]["m_PathID"]
        return False

    def children(self, pid): return [c["m_PathID"] for c in self.tt[pid]["m_Children"]]
    def scale_ok(self, pid):
        s = self.tt[pid]["m_LocalScale"]; return all(abs(s[k] - 1) < 1e-4 for k in "xyz")

    def local(self, pid):
        t = self.tt[pid]; p, r = t["m_LocalPosition"], t["m_LocalRotation"]
        return (p["x"], p["y"], p["z"]), (r["x"], r["y"], r["z"], r["w"])

    def set_local(self, pid, tr):
        t = self.tt[pid]
        t["m_LocalPosition"] = dict(zip("xyz", map(float, tr[0])))
        t["m_LocalRotation"] = dict(zip("xyzw", map(float, tr[1])))
        self.dirty.add(pid)

    def world(self, pid, stop):   # stop 노드 기준 좌표
        chain = []
        while pid != stop:
            if not self.scale_ok(pid): sys.exit(f"STOP: 크기 1 아님 {self.names[pid]}")
            chain.append(pid); pid = self.tt[pid]["m_Father"]["m_PathID"]
        tr = ((0.0, 0.0, 0.0), (0.0, 0.0, 0.0, 1.0))
        for p in reversed(chain): tr = compose(tr, self.local(p))
        return tr

    def save(self, out):
        if os.path.exists(out) and not (INPLACE and os.path.samefile(out, self.path)): sys.exit(f"STOP: 이미 있음 — 덮어쓰지 않습니다: {out}")
        for pid in self.dirty: self.objs[pid].save_typetree(self.tt[pid])
        os.makedirs(os.path.dirname(out), exist_ok=True)
        data = self.env.file.save(packer="lz4")
        with open(out + ".tmp", "wb") as f: f.write(data)
        os.replace(out + ".tmp", out)


def fmt(v): return "(" + ", ".join(f"{x:+.4f}" for x in v) + ")"
report = {}

# ── 1) 왼손: AK-74M 손잡이의 손바닥 자세를 무기 좌표에서 같은 자리로 ──
cont = Tree(os.path.join(src_root, K2_CONTAINER))
model = next(pid for pid, n in cont.names.items() if n.endswith(".generated") and not n.endswith("_simple.generated"))
weapon = cont.find("weapon", under=model)
upper, akhg = Tree(os.path.join(src_root, K2_UPPER)), Tree(os.path.join(game, AK_HANDGUARD))
up_root = next(pid for pid, t in upper.tt.items() if t["m_Father"]["m_PathID"] == 0)
ak_root = next(pid for pid, t in akhg.tt.items() if t["m_Father"]["m_PathID"] == 0)

# AK-101 기본 구성: 손잡이는 본체 mod_handguard 슬롯에 바로 붙는다 / K2: 본체 mod_reciever → K2 상부 mod_handguard
ak_hg_world = compose(cont.world(cont.find("mod_handguard", under=weapon), weapon), ATTACH)
up_world = compose(cont.world(cont.find("mod_reciever", under=weapon), weapon), ATTACH)
k2_hg_world = compose(compose(up_world, upper.world(upper.find("mod_handguard"), up_root)), ATTACH)


def box(tree, root, root_world):
    """부품의 BoxCollider(손잡이 외곽)를 무기 좌표 축정렬 범위로."""
    o = next(o for o in tree.env.objects if o.type.name == "BoxCollider"); t = o.read_typetree()
    node = next(pid for pid, tr in tree.tt.items() if tr["m_GameObject"]["m_PathID"] == t["m_GameObject"]["m_PathID"])
    nw = compose(root_world, tree.world(node, root))
    c, s = t["m_Center"], t["m_Size"]
    pts = [add(nw[0], qrot(nw[1], (c["x"] + sx * s["x"] / 2, c["y"] + sy * s["y"] / 2, c["z"] + sz * s["z"] / 2)))
           for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)]
    return tuple(min(p[i] for p in pts) for i in range(3)), tuple(max(p[i] for p in pts) for i in range(3))


ak_box = box(akhg, ak_root, ak_hg_world)
saved = []
for rel in K2_HANDGUARDS:
    hg = Tree(os.path.join(src_root, rel)); name = os.path.basename(rel)
    hg_root = next(pid for pid, t in hg.tt.items() if t["m_Father"]["m_PathID"] == 0)

    def copy_pose(dst, src):   # 손가락: 같은 순서의 자식끼리 로컬 자세 복사(바닐라 이름에 " 1"이 섞여 있어 이름으로 짝짓지 않음)
        dc, sc = hg.children(dst), akhg.children(src)
        if len(dc) != len(sc): sys.exit(f"STOP: 손가락 구조 다름 {hg.names[dst]} {len(dc)} ↔ {akhg.names[src]} {len(sc)}")
        for d, s in zip(dc, sc):
            hg.set_local(d, akhg.local(s)); copy_pose(d, s)

    # AK 손바닥과 손잡이 왼쪽 면·아랫면 사이 간격을 K2에서도 같게: K2 손잡이는 AK보다 아랫면이 높고 폭이 넓다
    k2_box = box(hg, hg_root, k2_hg_world)
    # 앞뒤: AK 손바닥은 손잡이 뒤끝에 걸쳐 있다. K2 손잡이 뒤끝이 AK보다 앞이면(클래식) 그만큼 앞으로, 더 뒤까지 오면(MITS 긴 것) 그대로
    surface = (k2_box[0][0] - ak_box[0][0], min(0.0, k2_box[1][1] - ak_box[1][1]), k2_box[0][2] - ak_box[0][2])
    print(f"{name} 외곽(무기 좌표): AK {fmt(ak_box[0])}..{fmt(ak_box[1])} / K2 {fmt(k2_box[0])}..{fmt(k2_box[1])} → 왼쪽 면·앞뒤·아랫면 보정 {fmt(surface)} m")
    rep = report[name] = {"surface_shift": surface, "box": k2_box}
    for palm in ["Base HumanLPalm", "Base HumanLPalm 1"]:
        k_pid, a_pid = hg.find(palm), akhg.find(palm)
        before = compose(k2_hg_world, hg.local(k_pid))[0]
        target = compose(ak_hg_world, akhg.local(a_pid))
        target = (add(target[0], surface), target[1])
        hg.set_local(k_pid, relative(k2_hg_world, target))
        copy_pose(k_pid, a_pid)
        after = compose(k2_hg_world, hg.local(k_pid))[0]
        print(f"  왼손 {palm}: 무기 좌표 {fmt(before)} → {fmt(after)} (목표 {fmt(target[0])}, 이동 {fmt(sub(after, before))} m)")
        if not (k2_box[0][1] - 0.03 <= after[1] <= k2_box[1][1] + 0.03): print(f"  경고: 손바닥 앞뒤 위치가 손잡이 범위 밖 y{after[1]:+.4f}")
        rep[palm] = {"before": before, "after": after}
    hg.save(os.path.join(dst_root, rel)); saved.append((rel, ["Base HumanLPalm", "Base HumanLPalm 1"]))

# ── 2) 탄창 총알: Colt 윗면 기준 상대 위치 → K2 윗면 기준 ──
def mag_top(path):
    """탄창 윗면(가장 높은 z)과 급탄구 뒤끝(윗부분 2cm 안에서 가장 큰 y), 무기 좌표 기준(메시→루트 Rx-90, 루트→슬롯 Rx+90 → 항등)."""
    with open(path, "rb") as f: env = UnityPy.load(f.read())
    mesh = next(o.read() for o in env.objects if o.type.name == "Mesh" and o.read().m_Name.endswith("_LOD0"))
    v = [tuple(float(a) for a in l.split()[1:4]) for l in mesh.export().splitlines() if l.startswith("v ")]
    top = max(p[2] for p in v)
    rear = max(p[1] for p in v if p[2] > top - 0.02)
    return rear, top


mag = Tree(os.path.join(src_root, K2_MAG))
colt_rear, colt_top = mag_top(os.path.join(game, COLT_MAG))
k2_rear, k2_top = mag_top(os.path.join(src_root, K2_MAG))
d_slot = (0.0, k2_rear - colt_rear, k2_top - colt_top)           # 슬롯(무기) 축 기준 이동량
d_root = qrot(qinv(RX90), d_slot)                                 # 탄창 루트 축으로
print(f"탄창 윗면: Colt 뒤끝 y{colt_rear:+.4f} 위 z{colt_top:+.4f} / K2 뒤끝 y{k2_rear:+.4f} 위 z{k2_top:+.4f} → 총알 이동 {fmt(d_slot)} m")
for n in ["patron_001", "patron_002", "patron_003"]:
    pid = mag.find(n); p, r = mag.local(pid)
    mag.set_local(pid, (add(p, d_root), r))
    print(f"  {n}: 루트 {fmt(p)} → {fmt(add(p, d_root))}")
report["patron_shift_slot"] = d_slot

mag.save(os.path.join(dst_root, K2_MAG)); saved.append((K2_MAG, ["patron_001", "patron_002", "patron_003"]))
json.dump(report, open(os.path.join(dst_root, "part-nodes.json"), "w", encoding="utf-8"), indent=1)

# 다시 읽어 확인
for rel, names in saved:
    t = Tree(os.path.join(dst_root, rel))
    for n in names: print("확인:", os.path.basename(rel), n, fmt(t.local(t.find(n))[0]))
