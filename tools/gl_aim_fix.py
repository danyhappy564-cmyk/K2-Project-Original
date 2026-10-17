"""K201 유탄 조준을 K201 조준기(왼쪽 가늠자 핀 + 가늠쇠)로 하고, 발사 방향을 그 조준선에 맞춘다(0.9.10).

0.9.8: Codex가 눈을 조준기 밖(총열 중심보다 44mm 왼쪽)에 둬 보이는 조준기와 화면 중앙이 어긋남 → 인게임 "탄착 왼쪽 아래".
0.9.9: 눈을 총열 중심·K2 가늠쇠 정렬로 옮김 → 인게임 "유탄 조준기 안 쓰고 왜 아이언 사이트로 조준해".
런처는 영점 보정 없이 fireport 방향(fireport 로컬 −y)으로 쏜다(Player 런처 발사 = fireport.TransformDirection(PWA._shotDirection), 기본값 down).
그래서 조준선(뒤 가늠자 핀 → 가늠쇠, 총열보다 4.5° 오른쪽·1.8° 위)을 그대로 쓰려면 fireport도 그 선으로 돌려야 한다.
  - 눈(mod_aim_camera): 조준선 위 z = −0.337, 시선(카메라 −z) = 조준선, 위(+y) ≈ 런처 위.
  - 발사 방향(fireport −y): 조준선을 5° 위로(바닐라 M203의 시선–총열 각 = 약 100m 영점). fireport +z ≈ 런처 위(바닐라 규약).
두 점은 K2GLAimView.cs로 두 카메라 위치에서 렌더해 삼각측량(오차 <0.1mm, 런처 모델 좌표).

  python tools/gl_aim_fix.py <입력 런처 번들> <출력 런처 번들>
"""
import sys, math, pathlib, UnityPy

REAR = (-0.0271, 0.0530, -0.0185)    # 뒤 가늠자(K201 옆 조준기) 핀 끝
FRONT = (-0.0002, 0.0639, 0.3278)    # 가늠쇠 끝(고리 안)
EYE_Z = -0.337
ELEV = 5.0                           # 발사 방향 = 조준선 + 5° 위


def norm(v):
    n = math.sqrt(sum(x * x for x in v)); return [x / n for x in v]


def cross(a, b): return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]


def dot(a, b): return sum(x * y for x, y in zip(a, b))


def perp_up(d):
    return norm([u - d[i] * dot(d, (0, 1, 0)) for i, u in enumerate((0, 1, 0))])


def quat_from_basis(x, y, z):
    m00, m01, m02 = x[0], y[0], z[0]; m10, m11, m12 = x[1], y[1], z[1]; m20, m21, m22 = x[2], y[2], z[2]
    tr = m00 + m11 + m22
    if tr > 0:
        s = math.sqrt(tr + 1) * 2; return ((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, s / 4)
    if m00 > m11 and m00 > m22:
        s = math.sqrt(1 + m00 - m11 - m22) * 2; return (s / 4, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s)
    if m11 > m22:
        s = math.sqrt(1 + m11 - m00 - m22) * 2; return ((m01 + m10) / s, s / 4, (m12 + m21) / s, (m02 - m20) / s)
    s = math.sqrt(1 + m22 - m00 - m11) * 2; return ((m02 + m20) / s, (m12 + m21) / s, s / 4, (m10 - m01) / s)


d = norm([FRONT[i] - REAR[i] for i in range(3)])
k = (EYE_Z - REAR[2]) / d[2]; eye = [REAR[i] + k * d[i] for i in range(3)]
cz = [-x for x in d]; cx = norm(cross([0, 1, 0], cz)); cy = cross(cz, cx)          # 카메라 = LookRotation(−d, up)
qcam = quat_from_basis(cx, cy, cz)
up = perp_up(d); e = math.radians(ELEV); s = norm([d[i] * math.cos(e) + up[i] * math.sin(e) for i in range(3)])
fy = [-x for x in s]; fz = perp_up(s); fx = cross(fy, fz)                           # fireport: −y = 발사 방향, +z = 위
qfire = quat_from_basis(fx, fy, fz)
print('눈', [round(v, 5) for v in eye], '조준선', [round(v, 4) for v in d], '발사', [round(v, 4) for v in s])

src, dst = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2])
env = UnityPy.load(str(src)); n = {'mod_aim_camera': 0, 'fireport': 0}
gos = {o.path_id: o.read_typetree()['m_Name'] for o in env.objects if o.type.name == 'GameObject'}
for o in env.objects:
    if o.type.name != 'Transform': continue
    t = o.read_typetree(); name = gos.get(t['m_GameObject']['m_PathID'])
    if name not in n: continue
    print(name, '이전', t['m_LocalPosition'], t['m_LocalRotation'])
    if name == 'mod_aim_camera':
        t['m_LocalPosition'] = dict(zip('xyz', eye)); t['m_LocalRotation'] = dict(zip('xyzw', qcam))
    else:
        t['m_LocalRotation'] = dict(zip('xyzw', qfire))
    o.save_typetree(t); n[name] += 1
assert n == {'mod_aim_camera': 2, 'fireport': 2}, n   # 일반·simple 모델
dst.parent.mkdir(parents=True, exist_ok=True); dst.write_bytes(env.file.save(packer='lz4'))
print('저장', dst, n)
