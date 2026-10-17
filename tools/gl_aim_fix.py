"""K201 런처 조준 카메라(mod_aim_camera) 위치를 앞 가늠쇠가 화면 중앙·총열 중심에 오도록 옮긴다(0.9.9, 회전은 바닐라 M203 그대로).

왜: 0.9.8 인게임 "유탄 탄착이 왼쪽 아래". Codex(gl_aim_v004)가 눈을 조준기 바깥(왼쪽·위)으로 옮겨, 화면에 보이는 조준기가 중앙보다
오른쪽 위에 있었다 — 그 조준기를 표적에 맞추면 실제 조준점(화면 중앙)은 왼쪽 아래.
방법: K2GLAimView.cs로 두 카메라 위치에서 렌더해 뒤 가늠자(귀 사이 은색 핀)와 앞 가늠쇠(K2 가늠쇠, 고리 안) 끝을 삼각측량(오차 <0.1mm, 런처 모델 좌표).
런처는 영점 보정 없이 fireport 방향(총열 +z) 그대로 쏜다(ProceduralWeaponAnimation._shotDirection 기본값, Player 런처 발사) — 그래서 카메라 회전은
바닐라 M203 그대로(총열보다 5° 아래를 봄 = 약 100m 영점), 눈 = 앞 가늠쇠 끝이 화면 중앙에 오는 자리(좌우 총열 중심, z −0.337).
두 점을 잇는 선(조준선)으로 시선을 돌리면 사진은 맞지만 그 선이 총열보다 4.5° 오른쪽이라 탄은 여전히 왼쪽으로 간다(이 모델의 뒤 가늠자는 접힌 K201 옆 조준기).

  python tools/gl_aim_fix.py <입력 런처 번들> <출력 런처 번들>
"""
import sys, pathlib, UnityPy

FRONT = (-0.0002, 0.0639, 0.3278)    # 앞 가늠쇠 끝(런처 모델 좌표, m) — 참고: 뒤 가늠자 핀 (-0.0271, 0.0530, -0.0185)
EYE_Z = -0.337


SIGHT = (0.0, -0.0872, 0.9962)                       # 바닐라 M203 시선(카메라 −z, 런처 좌표) — 그대로 둔다
k = (EYE_Z - FRONT[2]) / SIGHT[2]; eye = [FRONT[i] + k * SIGHT[i] for i in range(3)]
print('눈', [round(v, 5) for v in eye], '시선(유지)', SIGHT)

src, dst = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2])
env = UnityPy.load(str(src)); n = 0
gos = {o.path_id: o.read_typetree()['m_Name'] for o in env.objects if o.type.name == 'GameObject'}
for o in env.objects:
    if o.type.name != 'Transform': continue
    t = o.read_typetree()
    if gos.get(t['m_GameObject']['m_PathID']) != 'mod_aim_camera': continue
    print('이전', t['m_LocalPosition'], t['m_LocalRotation'])
    t['m_LocalPosition'] = {'x': eye[0], 'y': eye[1], 'z': eye[2]}
    o.save_typetree(t); n += 1
assert n == 2, n   # 일반·simple 모델
dst.parent.mkdir(parents=True, exist_ok=True); dst.write_bytes(env.file.save(packer='lz4'))
print('저장', dst, n)
