"""K2 동작 번들(0.9.1, Claude) — AK-101 override 컨트롤러 3개 + 수정 클립 98개. 기반 컨트롤러·fast 데이터는 바닐라(kb 9.20).

모든 수정은 곡선 계수의 선형 변환(값×s+d) 또는 밀집 곡선 프레임 값 교체 — 스트림 곡선을 다시 인코딩하지 않는다
(Codex v003b의 재인코딩 후보는 Unity EvaluateClip 네이티브 크래시).
- 방아쇠: aim_camera 위치 +z LIFT(모델을 같은 양 올림, ak-v010).
- 장전손잡이: 오른손이 mod_charge를 만지는 10개 클립 IK(K2AnimBakeCharge, 거리 기반 비중) + charge_2 왼손(Codex bake-ak-v003).
- 오른쪽 접기: 회전 축 = K2 실제 경첩 핀(HINGE). mod_stock 위치 x → −x + a, y + b, z + c(모든 클립), 접기 클립 오일러 y·z 반전,
  개머리판 메시 −HINGE(mirror_parts), 컨테이너 mod_stock 기본값도 같은 변환.
- 접기 손: AK 왼손은 왼쪽으로 미는 동작이라 반대 — 왼팔·손가락은 처음 자세로 고정, 상완·전완1·손바닥은 총을 따라가는 IK(K2AnimBakeFollow, BAKE_L).
- 조정간: firemode 클립의 오른팔 고정(손가락만) + 총 굴림을 좌우 거울(AK는 오른쪽 레버를, K2는 왼쪽 조정간을 보여 줌)
  + 왼손은 총을 따라 핸드가드를 잡음(0.9.3 — 0.9.2는 왼팔이 원래 동작대로 총을 놓쳐 허공으로 뻗음).
- 약실 총알·탄피 배출: patron_in_weapon·shellport를 K2 약실 자리로(CHAMBER_SHIFT, 0.9.3).
- 탄창 넣기: mag_in 클립에서 끼울 자리에 가까울수록 mod_magazine 기울기를 0으로(곧게 밀어 넣음 — K2 탄창이 총 몸을 뚫던 문제).
- 탄창 칸: mod_magazine·mod_magazine_new +z LIFT는 끼운 자리 근처(2~8cm)에서만(0.9.4), 탄창을 든 왼손 IK 마커도 같이. K2 전용 탄창 메시는 −LIFT.
- 접기(0.9.4): 총 굴림 좌우 거울(오른쪽 면이 보임) + 오른손 IK 마커가 손잡이→개머리판 윗면→접음→복귀(K2AnimBakeFoldR, BAKE_FOLD_R).
  2단계 빌드: BAKE_FOLD_R 없이 1차 → K2AnimBakeFoldR(1차 결과 기준) → 2차. 스트림 곡선은 encode_stream으로 다시 인코딩(+inf 끝 프레임 비움).
- 1인칭 손은 팔 뼈가 아니라 IK 마커를 따른다(EFT Player.IkProcess) — 손 위치를 바꿀 때는 마커를 바꾼다.
m_ValueArrayDelta(곡선별 시작·끝 값)도 같은 선형 변환으로 맞춘다.

  python tools/build_anim.py <K2 번들 폴더(ak-v014)> <출력 폴더>   (0.9.4: anim-v010 → ak-v017)
필요: Codex 작업 폴더(코덱스방/K2-anim-v001)의 ak_graph_v003a·ak_curve_fix_v003a·build_ak_anim_v002a 모듈, bake-ak-v003/ik-curves.json,
      BAKE_R(K2AnimBakeCharge 결과). 조립: ak-v010 + 출력 → ak-v013, bundles.json에 override 번들 항목·컨테이너 의존 추가.
"""
import sys, pathlib, json, hashlib, copy, math, struct, statistics
CODEX = pathlib.Path('F:/코딩/Index/코덱스방/K2-anim-v001')
sys.path.insert(0, str(CODEX))
import ak_graph_v003a as G
from ak_graph_v003a import *   # Graph, bindings, clipdata, modify_scalar, sf, ptr, visit, BODY, DONOR, BASE, ANIM, GAME
from ak_curve_fix_v003a import unity_to_clip, eval_scalar, stream_frames
from build_ak_anim_v002a import renamed

SRC = pathlib.Path(sys.argv[1]); OUT = pathlib.Path(sys.argv[2])
G.SOURCE = SRC
# K2 개머리판 경첩 핀 축(슬롯 기준 x·y, m) — 앞부품 경첩 마디 원통 맞춤(3종 공통, 반지름 ~10mm, 잔차 <1mm). 무기 좌표로 중심 오른쪽 20.6mm
HINGE = (0.0359, 0.0205)
LIFT = 0.027   # K2 모델을 올린 양(-k2SightDy 0.011118 − (−0.015882)) — aim_camera·탄창 칸에 같이 더한다
BAKE_R = 'F:/코딩/Index/클로드방/K2-Project-Original/Assets-Local/Bundles/bake-charge-v001/ik-curves.json'
BAKE_L = ['F:/코딩/Index/클로드방/K2-Project-Original/Assets-Local/Bundles/bake-firemode-v001/ik-curves.json']  # 조정간(총 굴림 거울 뒤) 왼팔 뼈
# 1인칭 손은 팔 뼈가 아니라 IK 마커(weapon_L/R_IK_marker, 총 기준)를 따른다(EFT Player.IkProcess — 마커가 손 기준점 10cm 안이면 손잡이 자세로 빨려 듦).
# 접기 오른손(0.9.4): 오른손이 손잡이를 놓고 개머리판을 잡아 접은 뒤 돌아오는 마커 궤적(K2AnimBakeFoldR, 총 굴림 거울 뒤 기준). 없으면 건너뜀(1차 빌드).
BAKE_FOLD_R = 'F:/코딩/Index/클로드방/K2-Project-Original/Assets-Local/Bundles/bake-foldR-fx11/fold-right.json'   # 0.9.5: G36 오른손 상대 자세 이식(K2AnimBakeFoldDonor)
BAKE_CATCH = 'F:/코딩/Index/클로드방/K2-Project-Original/Assets-Local/Bundles/bake-catch-c007/catch.json'   # 0.9.6: 빈 탄창 장전 charge_2 → M4식 멈치 때리기(K2AnimBakeCatch)
CATCH_SCALE = 0.8 / 1.6   # 1.0.0: 멈치 때리기 클립 1.6초 → 0.8초(M4 접근 1.2배, 1.1초는 "0.9.9와 비슷") — 소리 시각(정규화)도 같이 당겨져 대기 없이 맞춤
SLOT_SWAP = {'ak100_reload_charge_1': 'ak100_reload_charge_2'}   # 1.0.0: 원래 클립 칸 → 이 클립(수정본)으로 재생. 장전 상태는 숙련도(WeaponLevel)로 charge_0·1·2 선택 → 0단계 당기기(charge_0), 1·2단계 멈치 때리기(서버가 K2 독립 숙련도 추가)
BAKE_FOLD_W = 'F:/코딩/Index/클로드방/K2-Project-Original/Assets-Local/Bundles/bake-foldW-fx02/fold-weapon.json'   # 0.9.7: G36 총 궤적 80% + 0.4초 평활(K2AnimBakeFoldWeapon -k2Amp 0.8 -k2EndWin .35 -k2Smooth .4) — 0.9.6(100%)은 1.7초에 압축돼 총구 529°/s(G36 171)
MAG_SEAT = (-0.0001, -0.4402, -0.0241)   # AK 탄창 끼운 자리(무기 좌표)
# 조정간(0.9.5): K2 조정간 메시를 weapon_selector에 분리(K2Builder, ak-v019) — 노드를 K2 축으로(빌드 출력 selector-pivot.json), 회전은 M16식.
# 메시는 노드 회전 x 5.85°(AK 프리팹 기본)에서 구웠다 = K2 안전(레버 앞). 단발 = 위로 90°, 연발 = 뒤로 180°(+x축 기준 음수가 앞→위).
# AK 클립 값: 단발 5.793°, 연발 0.445° → x' = −174.15 + (x − 0.445)·90/5.348
SELECTOR_PIVOT = (-0.003088481, -0.284393162, -0.0126879439)
SELECTOR_SCALE = 90 / (5.793 - 0.445); SELECTOR_ADD = -174.15 - 0.445 * SELECTOR_SCALE
RELEASE_ANGLE, RELEASE_PUSH = 28.0, 0.003   # AK 멈치 최대 회전(°) → K2 버튼 눌림(m)
MAG_TIME = 0.12   # 0.9.6: 곧은 경로·높이·쥔 손 비중 전환을 시간으로 최소 약 2×0.12초에 걸쳐(tsmooth)
MAG_STRAIGHT, MAG_BLEND = 0.08, 0.13      # 이 거리 안은 곧은 경로, 그 사이는 섞음(K2 탄창은 끼우면 입구 위로 53mm 들어감)
# 약실 총알·탄피 배출 자리(0.9.3): AK patron_in_weapon(y −0.4555, z −0.0007)이 K2 약실(총열 뒤끝 y −0.4292, 축 z +0.0255)보다
# 26mm 앞·26mm 아래라 약실 확인 때 총알이 몸통 밖으로 보였다(인게임). 무기 좌표 (0, +0.0263, +0.0262) 이동 — 모든 클립 + 컨테이너 기본값.
CHAMBER_SHIFT = (0.0, 0.0263, 0.0262)
CHAMBER_NODES = ('patron_in_weapon', 'shellport')
SELECTOR = ('ak100_firemode_0', 'ak100_firemode_1')
MIRROR_NODES = ('weapon',)   # 조정간·접기 클립에서 좌우 거울로 뒤집는 노드(총 굴림) — K2 조정간은 왼쪽, 접기는 오른쪽
R_ARM = {'Base HumanRCollarbone', 'Base HumanRUpperarm', 'Base HumanRForearm1', 'Base HumanRForearm2', 'Base HumanRForearm3', 'Base HumanRPalm',
         'weapon_R_IK_marker', 'weapon_R_hand_marker', 'Bend_Goal_Right'}
L_ARM = {'Base HumanL*', 'weapon_L_IK_marker', 'weapon_L_hand_marker', 'Bend_Goal_Left'}
FOLD = ('ak100_stock_folding_left', 'ak100_stock_folded_left', 'ak100_stock_unfolding_left')


def vad(t, index, scale=1.0, add=0.0):
    for field in ('m_ValueArrayDelta',):
        a = t['m_MuscleClip'][field]
        a[index]['m_Start'] = a[index]['m_Start'] * scale + add; a[index]['m_Stop'] = a[index]['m_Stop'] * scale + add


def lin(t, index, scale=1.0, add=0.0):
    modify_scalar(t, index, scale=scale, add=add); vad(t, index, scale, add)


F32 = lambda x: struct.unpack('<f', struct.pack('<f', x))[0]


def smooth(a, b, x):
    u = min(1.0, max(0.0, (x - a) / (b - a))); return u * u * (3 - 2 * u)


def tsmooth(w, r, dilate=True):
    """프레임별 비중을 시간 축에서 매끄럽게(0.9.6): 넓히기(앞뒤 r프레임 최대) 후 삼각 평균(반폭 r). 거리로만 정하면 탄창이 그 거리 구간을
    1~2프레임에 지나갈 때 비중이 0→1로 튀어 속도가 순간 2배 이상(탄 검사 0.9→2.2m/s, 인게임 "프레임이 모자란 듯 부자연스러움")."""
    n = len(w)
    if dilate: w = [max(w[max(0, i - r):i + r + 1]) for i in range(n)]
    out = []
    for i in range(n):
        acc = wt = 0.0
        for j in range(max(0, i - r), min(n, i + r + 1)):
            k = r + 1 - abs(i - j); acc += w[j] * k; wt += k
        out.append(acc / wt)
    return out


def dense_times(t):
    de = clipdata(t)['m_DenseClip']; return [de['m_BeginTime'] + i / de['m_SampleRate'] for i in range(de['m_FrameCount'])]


def encode_stream(t, repl):
    """스트림 곡선 몇 개를 표본으로 교체해 다시 인코딩한다. repl = {곡선 번호: (시간들, 값들)}.
    원본 구조 유지: 첫 프레임(-FLT_MAX)에 모든 곡선 [0,0,0,v], 끝 프레임(+inf)은 키 0개 — Codex v003b는 +inf 프레임에 키를 넣어 Unity가 죽었다.
    구간마다 3차 Hermite(값·기울기 연속), 마지막 키 [0,0,0,v]. 기존 프레임 시각과 0.2ms 안이면 그 시각에 붙인다."""
    c = clipdata(t); st = c['m_StreamedClip']; frames = stream_frames(c)
    assert frames[-1][0] == float('inf') and not frames[-1][1], '스트림 끝 프레임 예상 밖'
    existing = [tm for tm, _ in frames if abs(tm) < 1e30]

    def snap(x):
        e = min(existing, key=lambda e: abs(e - x)); return e if abs(e - x) < 2e-4 else F32(x)
    table = {tm: [(i, cf) for i, cf in rows if i not in repl] for tm, rows in frames}
    ninf = frames[0][0]
    for idx, (ts, vs) in repl.items():
        ts = [snap(x) for x in ts]; n = len(ts)
        m = [(vs[min(i + 1, n - 1)] - vs[max(i - 1, 0)]) / (ts[min(i + 1, n - 1)] - ts[max(i - 1, 0)]) if n > 1 else 0 for i in range(n)]
        table[ninf].append((idx, [0.0, 0.0, 0.0, vs[0]]))
        for i in range(n - 1):
            h = ts[i + 1] - ts[i]; v0, v1, m0, m1 = vs[i], vs[i + 1], m[i], m[i + 1]
            A = (2 * (v0 - v1) + h * (m0 + m1)) / h ** 3; B = (3 * (v1 - v0) - h * (2 * m0 + m1)) / h ** 2
            table.setdefault(ts[i], []).append((idx, [A, B, m0, v0]))
        table.setdefault(ts[-1], []).append((idx, [0.0, 0.0, 0.0, vs[-1]]))
    raw = bytearray()
    for tm in sorted(table):
        rows = sorted(table[tm], key=lambda r: r[0])
        if not rows and abs(tm) < 1e30: continue
        raw += struct.pack('<fI', tm, len(rows))
        for i, cf in rows: raw += struct.pack('<I4f', i, *cf)
    t['m_MuscleClipSize'] += len(raw) - 4 * len(st['data'])
    st['data'] = list(struct.unpack('<%dI' % (len(raw) // 4), raw))


def set_samples(t, index, values):
    """곡선 하나를 밀집 프레임 격자 표본으로 교체(밀집 = 값 직접, 스트림 = 다시 인코딩)."""
    c = clipdata(t); ns = c['m_StreamedClip']['curveCount']; de = c['m_DenseClip']; nd = de['m_CurveCount']
    assert len(values) == de['m_FrameCount']
    if index < ns: encode_stream(t, {index: (dense_times(t), values)})
    elif index < ns + nd:
        for f, v in enumerate(values): de['m_SampleArray'][f * nd + index - ns] = v
    else: raise AssertionError('상수 곡선은 표본으로 못 바꿈')
    a = t['m_MuscleClip']['m_ValueArrayDelta'][index]; a['m_Start'] = values[0]; a['m_Stop'] = values[-1]


def qmul(a, b):
    x1, y1, z1, w1 = a; x2, y2, z2, w2 = b
    return (w1 * x2 + x1 * w2 + y1 * z2 - z1 * y2, w1 * y2 - x1 * z2 + y1 * w2 + z1 * x2, w1 * z2 + x1 * y2 - y1 * x2 + z1 * w2, w1 * w2 - x1 * x2 - y1 * y2 - z1 * z2)


def qinv(q): return (-q[0], -q[1], -q[2], q[3])


def qrot(q, v):
    r = qmul(qmul(q, (v[0], v[1], v[2], 0.0)), qinv(q)); return [r[0], r[1], r[2]]


def qslerp(a, b, t):
    dot = sum(x * y for x, y in zip(a, b))
    if dot < 0: b = tuple(-x for x in b); dot = -dot
    if dot > 0.9995:
        r = [x + (y - x) * t for x, y in zip(a, b)]
    else:
        th = math.acos(dot); s0 = math.sin((1 - t) * th) / math.sin(th); s1 = math.sin(t * th) / math.sin(th); r = [s0 * x + s1 * y for x, y in zip(a, b)]
    n = math.sqrt(sum(x * x for x in r)); return tuple(x / n for x in r)


def clip_quat(e):
    """클립 오일러(도) → 쿼터니언. 클립 순서 Z·Y·X(q = qz·qy·qx) — ak_curve_fix unity_to_clip과 같은 규약."""
    hx, hy, hz = [math.radians(v) / 2 for v in e]
    qx = (math.sin(hx), 0.0, 0.0, math.cos(hx)); qy = (0.0, math.sin(hy), 0.0, math.cos(hy)); qz = (0.0, 0.0, math.sin(hz), math.cos(hz))
    return qmul(qmul(qz, qy), qx)


def quat_clip(q):
    x, y, z, w = q
    return [math.degrees(math.atan2(2 * (w * x + y * z), 1 - 2 * (x * x + y * y))), math.degrees(math.asin(max(-1, min(1, 2 * (w * y - z * x))))),
            math.degrees(math.atan2(2 * (w * z + x * y), 1 - 2 * (y * y + z * z)))]


def unwrap(eul, ref):
    """오일러 표본을 원래 곡선(ref) 근처 각도로(±360) 맞춰 튀지 않게."""
    out = []
    for e, r in zip(eul, ref): out.append([v + 360 * round((rv - v) / 360) for v, rv in zip(e, r)])
    return out


def euler_track(rots, start):
    """Unity 오일러 표본들 → 클립 Z·Y·X 오일러 연속 열(0.9.10). 같은 자세의 두 표기 (x, y, z) ≡ (x+180, 180−y, z+180) 중
    직전 값에 가까운 쪽 + 축별 ±360 — 축마다 따로 ±360만 맞추면 표기가 바뀌는 프레임에서 보간이 엉뚱한 자세를 지나 손이 "덜덜" 돈다."""
    out = []; prev = list(start)
    for r in rots:
        e = unity_to_clip(r); a = [e['x'], e['y'], e['z']]; b = [a[0] + 180, 180 - a[1], a[2] + 180]
        best = None
        for c in (a, b):
            c = [v + 360 * round((p - v) / 360) for v, p in zip(c, prev)]
            d = sum(abs(v - p) for v, p in zip(c, prev))
            if best is None or d < best[0]: best = (d, c)
        out.append(best[1]); prev = best[1]
    return out


def rescale_time(t, k):
    """클립 재생 시간을 k배로(1.0.0, 멈치 때리기). 밀집 = 프레임 그대로 샘플레이트 ÷k, 스트림 = 키 시각 ×k·계수 [a,b,c,d] → [a/k³, b/k², c/k, d]
    (같은 곡선을 빨리 재생), 시작·끝 시각 ×k. 상태 이벤트는 정규화 시각이라 같이 당겨진다. 바이트 수는 그대로라 m_MuscleClipSize 불변."""
    c = clipdata(t); st = c['m_StreamedClip']; de = c['m_DenseClip']
    raw = bytearray()
    for tm, rows in stream_frames(c):
        raw += struct.pack('<fI', tm * k if abs(tm) < 1e30 else tm, len(rows))
        for i, (a, b, cc, d) in rows: raw += struct.pack('<I4f', i, a / k ** 3, b / k ** 2, cc / k, d)
    assert len(raw) == 4 * len(st['data']); st['data'] = list(struct.unpack('<%dI' % (len(raw) // 4), raw))
    de['m_SampleRate'] /= k; de['m_BeginTime'] *= k
    t['m_MuscleClip']['m_StartTime'] *= k; t['m_MuscleClip']['m_StopTime'] *= k
    if 'm_SampleRate' in t: t['m_SampleRate'] /= k


def curve_values(t, b, j):
    c = clipdata(t); return [eval_scalar(c, b['offset'] + j, tm) for tm in dense_times(t)]


def main():
    assert not OUT.exists(), '기존 결과 덮어쓰기 금지'
    g = Graph(); base, overrides, refs = g.animation_refs()
    bake = json.loads((CODEX / 'bake-ak-v003/ik-curves.json').read_text(encoding='utf-8')); assert bake['passed']
    baked = {c['name']: c for c in bake['clips'] if c['side'] == 'L'}          # charge_2(왼손 당김)는 Codex 결과
    mine = json.loads(pathlib.Path(BAKE_R).read_text(encoding='utf-8')); assert mine['passed']
    baked.update({c['name']: c for c in mine['clips']})                         # 오른손이 손잡이 만지는 10개(K2AnimBakeCharge)
    for bl in BAKE_L:
        fold = json.loads(pathlib.Path(bl).read_text(encoding='utf-8')); assert fold['passed']
        baked.update({c['name']: c for c in fold['clips']})                     # 왼손이 총(핸드가드)을 따라감(K2AnimBakeFollow)
    fold_r = {}
    if pathlib.Path(BAKE_FOLD_R).exists():
        fj = json.loads(pathlib.Path(BAKE_FOLD_R).read_text(encoding='utf-8')); assert fj['passed'], fj['error']
        fold_r = {c['name']: c for c in fj['clips']}
    print('접기 오른손 궤적', list(fold_r) or '없음(1차 빌드)')
    fold_w = {}
    if pathlib.Path(BAKE_FOLD_W).exists():
        wj = json.loads(pathlib.Path(BAKE_FOLD_W).read_text(encoding='utf-8')); assert wj['passed'], wj['error']
        fold_w = {c['name']: c for c in wj['clips']}
    print('접기 총 궤적', list(fold_w) or '없음(1차 빌드)')
    catch = {}
    if pathlib.Path(BAKE_CATCH).exists():
        cj = json.loads(pathlib.Path(BAKE_CATCH).read_text(encoding='utf-8')); assert cj['passed'], cj['error']
        catch = {cj['clip']: cj}; baked.pop(cj['clip'], None)   # Codex 왼팔(오른쪽 손잡이로 넘어감) 대신
    print('멈치 때리기', list(catch) or '없음')
    changed = {}; changes = []
    for data in baked.values():
        for sample in data['frames']:
            for field in ('upper', 'forearm', 'palm'): sample[field] = unity_to_clip(sample[field])
    folding = next(o.read_typetree() for o in refs.values() if o.read_typetree()['m_Name'] == 'ak100_stock_folding_left')
    pb = next(b for b in bindings(folding, g.paths) if b['path'].endswith('/mod_stock') and b['attribute'] == 1)
    pivot_x = eval_scalar(clipdata(folding), pb['offset'], 0)   # 펼친 상태 x — 거울면
    rb = next(b for b in bindings(folding, g.paths) if b['path'].endswith('/mod_stock') and b['attribute'] == 4)
    p0 = [eval_scalar(clipdata(folding), pb['offset'] + j, 0) for j in range(3)]
    th = math.radians(eval_scalar(clipdata(folding), rb['offset'], 0))   # 펼친 상태 X 회전(−4.18°)
    hw = (HINGE[0], HINGE[1] * math.cos(th), HINGE[1] * math.sin(th))     # 슬롯 기준 경첩 → 무기 좌표 변위
    shift = (p0[0] + hw[0] + p0[0], hw[1], hw[2])                          # x: −p0x + a = p0x + hwx
    print('경첩(무기 좌표)', [round(p0[j] + hw[j], 5) for j in range(3)], 'shift', [round(v, 5) for v in shift])
    for ident, o in refs.items():
        t = o.read_typetree(); bs = list(bindings(t, g.paths))
        aim = [b for b in bs if b['path'].endswith('/aim_camera') and b['attribute'] == 1]
        if not aim: continue
        row = {'sourceKey': ident[0], 'pathID': ident[1], 'name': t['m_Name'], 'camera': [], 'ik': [], 'fold': []}
        for b in aim: lin(t, b['offset'] + 2, add=.027); row['camera'].append(b['offset'])
        if t['m_Name'] in baked:
            data = baked[t['m_Name']]; side = data['side']; c = clipdata(t); de = c['m_DenseClip']
            ns = c['m_StreamedClip']['curveCount']; nd = de['m_CurveCount']; assert de['m_FrameCount'] == len(data['frames'])
            targets = {'Base Human' + side + 'Upperarm': 'upper', 'Base Human' + side + 'Forearm1': 'forearm', 'Base Human' + side + 'Palm': 'palm'}
            for b in bs:
                name = b['path'].split('/')[-1]
                field = targets.get(name) if b['attribute'] == 4 else 'marker' if side == 'R' and name == 'weapon_R_IK_marker' and b['attribute'] == 1 else None
                if not field: continue
                assert b['storage'] == 'dense'; index = b['offset'] - ns
                for f, sample in enumerate(data['frames']):
                    if sample['weight'] == 0: continue   # 시작·끝은 바닐라 값 유지
                    for j, axis in enumerate('xyz'):
                        at = f * nd + index + j; value = sample[field][axis]
                        if b['attribute'] == 4:
                            value += 360 * round((de['m_SampleArray'][at] - value) / 360)
                        de['m_SampleArray'][at] = value
                row['ik'].append(name + ':' + field)
        # 오른쪽 접기(0.9.1): 회전 축을 K2 실제 경첩 핀으로. mod_stock 위치 x → −x + a, y + b, z + c(모든 클립 — 펼친 상태에서 노드 = 경첩 핀),
        # 접기 클립은 회전 오일러 y·z 반전(오른쪽으로 돎). 개머리판 메시는 −HINGE만큼 옮겨 펼친 모습 유지(mirror_parts).
        st = [b for b in bs if b['path'].endswith('/mod_stock')]
        assert {b['attribute'] for b in st} <= {1, 3, 4} and 1 in {b['attribute'] for b in st}, t['m_Name']
        for b in st:
            if b['attribute'] == 1:
                for j in range(3): lin(t, b['offset'] + j, scale=-1 if j == 0 else 1, add=shift[j])
                row['fold'].append('pos')
            elif b['attribute'] == 4 and t['m_Name'] in FOLD: lin(t, b['offset'] + 1, scale=-1); lin(t, b['offset'] + 2, scale=-1); row['fold'].append('rotYZ')
        if t['m_Name'] in FOLD:
            c = clipdata(t); end = max(t['m_MuscleClip']['m_StopTime'], 0)
            row['foldPosition'] = [[round(eval_scalar(c, pb2['offset'] + j, tm), 5) for j in range(3)] for pb2 in st if pb2['attribute'] == 1 for tm in (0, end)]
        # 조정간(0.9.1): AK 손이 오른쪽 레버로 가는 대신 팔은 제자리, 손가락 움직임만(엄지로 K2 왼쪽 조정간을 돌리는 모양)
        # 접기(0.9.2): 왼팔·손가락은 처음 자세로 고정하되 상완·전완1·손바닥은 IK(총을 따라 핸드가드를 계속 잡음 — 0.9.1은 손이 떴다)
        solved = set()
        if t['m_Name'] in baked:
            sd = baked[t['m_Name']]['side']; solved = {'Base Human' + sd + n for n in ('Upperarm', 'Forearm1', 'Palm')}
        for names in ([R_ARM, L_ARM] if t['m_Name'] in SELECTOR else []) + ([L_ARM] if t['m_Name'] in FOLD or t['m_Name'] in catch else []):
            for b in bs:
                last = b['path'].split('/')[-1]
                if last in solved and b['attribute'] == 4: continue
                if last in names or ('Base HumanL*' in names and last.startswith('Base HumanL')):
                    c = clipdata(t)
                    for j in range(b['dim']): lin(t, b['offset'] + j, scale=0, add=eval_scalar(c, b['offset'] + j, 0))
                    row.setdefault('frozen', []).append(last)
        # 왼손(0.9.5): 접기·조정간 클립은 왼손 IK 마커 = 왼손 기준점(weapon_L_hand_marker) — 10cm 안이면 게임이 핸드가드 GripPose로 붙인다.
        # 0.9.4는 마커를 첫 프레임 값으로만 고정해 기준점과 어긋나(검사기: 핸드가드와 54mm) 접는 동안 왼손이 떠 있었다. G36 접기도 마커 = 기준점(1mm).
        if t['m_Name'] in FOLD or t['m_Name'] in SELECTOR:
            for attr in (1, 4):
                src = next((x for x in bs if x['path'].endswith('/weapon_L_hand_marker') and x['attribute'] == attr), None)
                dst = next((x for x in bs if x['path'].endswith('/weapon_L_IK_marker') and x['attribute'] == attr), None)
                if src and dst:
                    c = clipdata(t)
                    for j in range(3): lin(t, dst['offset'] + j, scale=0, add=eval_scalar(c, src['offset'] + j, 0))
                    row.setdefault('lToRef', []).append(attr)
        # 조정간(0.9.2): AK는 총을 굴려 오른쪽 레버를 보여 준다 — K2 조정간은 왼쪽이라 총 움직임을 좌우 거울(위치 x, 회전 y·z 부호)
        if t['m_Name'] in SELECTOR or t['m_Name'] in FOLD:
            for b in bs:
                if b['path'].split('/')[-1] in MIRROR_NODES:
                    if b['attribute'] == 1: lin(t, b['offset'], scale=-1); row.setdefault('mirror', []).append('pos')
                    elif b['attribute'] == 4: lin(t, b['offset'] + 1, scale=-1); lin(t, b['offset'] + 2, scale=-1); row.setdefault('mirror', []).append('rot')
                    elif b['attribute'] == 2: raise AssertionError('쿼터니언 곡선은 처리 안 함')
        # 탄창(0.9.5): M4처럼 탄창 구멍 근처에서는 구멍 축(무기 -z)을 따라 곧게 — AK는 앞을 걸고 젖혀 넣어 구멍 안에서도 앞뒤로 흔들리고 기운다
        # (검사기 K2Validation 탄창 구멍 검사: 0.9.4 넣기 16mm·빼기 14mm 구멍 밖 = 영상 "탄창이 몸통을 뚫음", 바닐라 M4 넣기 0mm).
        # 끼운 자리에서 거리 d: d < STRAIGHT = 끼운 자세(회전 0)로 자리 바로 아래 d, STRAIGHT~BLEND = 원래 경로와 섞음, 그 밖 = 원래.
        # 그다음 K2 높이(+LIFT): 구멍 안(d < 6cm) 전부, 12cm 밖 0. 탄창을 쥔 왼손 IK 마커는 탄창에 붙여 같은 강체 변환으로 따라간다.
        for nm in ('mod_magazine', 'mod_magazine_new'):
            pb = next((x for x in bs if x['path'].endswith('/' + nm) and x['attribute'] == 1), None)
            rb = next((x for x in bs if x['path'].endswith('/' + nm) and x['attribute'] == 4), None)
            if not pb: continue
            P = [curve_values(t, pb, j) for j in range(3)]; n = len(P[0])
            R = [curve_values(t, rb, j) for j in range(3)] if rb else [[0.0] * n] * 3
            d = [math.dist([P[0][f], P[1][f], P[2][f]], MAG_SEAT) for f in range(n)]
            if max(d) < 0.005:   # 늘 끼워져 있음
                lin(t, pb['offset'] + 2, add=LIFT); row.setdefault('mag', []).append(nm + ':fixed'); continue
            assert rb, (t['m_Name'], nm, '회전 곡선 없음')   # 밀집·스트림 모두 set_samples가 처리
            newP, newR = [], []
            r = round(MAG_TIME * clipdata(t)['m_DenseClip']['m_SampleRate'])
            K = tsmooth([1 - smooth(MAG_STRAIGHT, MAG_BLEND, x) for x in d], r)   # 1 = 곧은 경로
            LW = tsmooth([1 - smooth(0.06, 0.12, x) for x in d], r)
            for f in range(n):
                q_old = clip_quat([R[0][f], R[1][f], R[2][f]]); p_old = [P[0][f], P[1][f], P[2][f]]
                k = K[f]
                p_s = [MAG_SEAT[0], MAG_SEAT[1], MAG_SEAT[2] - d[f]]
                p_n = [p_old[j] + (p_s[j] - p_old[j]) * k for j in range(3)]
                q_n = qslerp(q_old, (0.0, 0.0, 0.0, 1.0), k)
                lift = LIFT * LW[f]; p_n[2] += lift
                newP.append(p_n); newR.append(q_n)
            for j in range(3): set_samples(t, pb['offset'] + j, [x[j] for x in newP])
            eul = unwrap([quat_clip(q) for q in newR], [[R[0][f], R[1][f], R[2][f]] for f in range(n)])
            for j in range(3): set_samples(t, rb['offset'] + j, [e[j] for e in eul])
            row.setdefault('mag', []).append(nm + ':straight')
            # 쥔 손(왼손 마커): 탄창을 쥐고 있는 동안(손-탄창 노드 거리가 '들고 있을 때 거리' ± 2~5cm) 탄창과 같은 강체 변환
            lm = next((x for x in bs if x['path'].endswith('/weapon_L_IK_marker') and x['attribute'] == 1), None)
            lr = next((x for x in bs if x['path'].endswith('/weapon_L_IK_marker') and x['attribute'] == 4), None)
            if not lm: continue
            L = [curve_values(t, lm, j) for j in range(3)]; LR = [curve_values(t, lr, j) for j in range(3)] if lr else None
            hd = [math.dist([L[0][f], L[1][f], L[2][f]], [P[0][f], P[1][f], P[2][f]]) for f in range(n)]
            # 탄창 순간이동(교체 표시 — mag_hit·mag_admin, 프레임당 >4m/s) 앞뒤 프레임은 쥔 손 계산·따라가기에서 뺀다(0.9.6: 손이 같이 튐)
            rate = clipdata(t)['m_DenseClip']['m_SampleRate']
            jump = [f > 0 and math.dist([P[j][f] for j in range(3)], [P[j][f - 1] for j in range(3)]) * rate > 4 for f in range(n)]
            tele = {g for f in range(n) if jump[f] for g in (f - 2, f - 1, f, f + 1)}
            held_frames = [hd[f] for f in range(n) if d[f] > 0.05 and f not in tele]
            if tele: row['magTele'] = len(tele); continue   # 탄창 순간이동 클립(버리기·교체 표시: mag_hit·mag_admin·mag_out_all) — 손은 탄창을 쥐고 있지 않다(바닐라 손 거의 정지)
            if len(held_frames) < 0.15 * rate: continue   # 실제로 빼서 들고 있는 구간이 없음
            hold = statistics.median(held_frames)
            if sum(abs(x - hold) < 0.02 for x in held_frames) < 0.7 * len(held_frames): continue   # 손–탄창 거리가 일정하지 않음 = 쥐고 있지 않음(떨어뜨림)
            outP, outR, moved = [], [], 0.0
            HW = tsmooth([0.0 if f in tele else 1 - smooth(0.02, 0.05, abs(hd[f] - hold)) for f in range(n)], r, dilate=False)
            for f in range(n):
                hw = HW[f]
                q_old = clip_quat([R[0][f], R[1][f], R[2][f]]); delta = qmul(newR[f], qinv(q_old))
                lp = [L[0][f], L[1][f], L[2][f]]; rel = [lp[j] - P[j][f] for j in range(3)]
                rigid = [newP[f][j] + qrot(delta, rel)[j] for j in range(3)]
                outP.append([lp[j] + (rigid[j] - lp[j]) * hw for j in range(3)]); moved = max(moved, math.dist(outP[-1], lp))
                if LR: outR.append(qslerp(clip_quat([LR[0][f], LR[1][f], LR[2][f]]), qmul(delta, clip_quat([LR[0][f], LR[1][f], LR[2][f]])), hw))
            if moved < 0.0005: continue
            pass
            for j in range(3): set_samples(t, lm['offset'] + j, [x[j] for x in outP])
            if LR:
                eul = unwrap([quat_clip(q) for q in outR], [[LR[0][f], LR[1][f], LR[2][f]] for f in range(n)])
                for j in range(3): set_samples(t, lr['offset'] + j, [e[j] for e in eul])
            row['magHand'] = round(moved, 4)
        for b in bs:
            if b['path'].split('/')[-1] != 'weapon_selector': continue
            if b['attribute'] == 1:
                for j in range(3): lin(t, b['offset'] + j, scale=0, add=SELECTOR_PIVOT[j])
                row.setdefault('selector', []).append('pos')
            elif b['attribute'] == 4:
                lin(t, b['offset'], scale=SELECTOR_SCALE, add=SELECTOR_ADD); row.setdefault('selector', []).append('rot')
        # 탄창 멈치(0.9.5): AK는 weapon_release 레버를 x축으로 약 28° 돌린다 — K2(M16식)는 오른쪽 버튼이 안쪽으로 눌린다.
        # M4 mag_out 멈치는 회전 0·x 약 3mm(Codex 조사). 같은 타이밍(AK 회전량 비율)으로 회전 대신 무기 −x로 RELEASE_PUSH 이동.
        rr = next((x for x in bs if x['path'].endswith('/weapon_release') and x['attribute'] == 4), None)
        rp = next((x for x in bs if x['path'].endswith('/weapon_release') and x['attribute'] == 1), None)
        if rr and rr['storage'] != 'constant':
            RX = curve_values(t, rr, 0)
            if max(abs(v) for v in RX) > 1.0:
                press = [min(1.0, abs(v) / RELEASE_ANGLE) for v in RX]
                set_samples(t, rr['offset'], [0.0] * len(RX))
                if rp and rp['storage'] != 'constant':
                    PX = curve_values(t, rp, 0); set_samples(t, rp['offset'], [x - RELEASE_PUSH * k for x, k in zip(PX, press)]); row['release'] = 'push'
                else: row['release'] = 'rot0-only'
        for b in bs:
            if b['attribute'] == 1 and b['path'].split('/')[-1] in CHAMBER_NODES:
                assert b['path'].endswith('/weapon/' + b['path'].split('/')[-1]), b['path']   # 부모가 weapon이라 로컬 = 무기 좌표
                for j in range(3):
                    if CHAMBER_SHIFT[j]: lin(t, b['offset'] + j, add=CHAMBER_SHIFT[j])
                row.setdefault('chamber', []).append(b['path'].split('/')[-1])
        if t['m_Name'] in fold_r:
            fr = fold_r[t['m_Name']]; n = clipdata(t)['m_DenseClip']['m_FrameCount']; assert len(fr['frames']) == n, (len(fr['frames']), n)
            for b in bs:
                if b['path'].endswith('/weapon_R_IK_marker') and b['attribute'] in (1, 4):
                    eul = euler_track([f['rot'] for f in fr['frames']], [curve_values(t, b, j)[0] for j in range(3)]) if b['attribute'] == 4 else None
                    for j, ax in enumerate('xyz'):
                        vals = [f['pos'][ax] for f in fr['frames']] if b['attribute'] == 1 else [e[j] for e in eul]
                        set_samples(t, b['offset'] + j, vals)
                    row.setdefault('foldR', []).append(b['attribute'])
        # 접기 총 궤적(0.9.6): weapon 노드 위치·회전 = G36이 접을 때 총을 내미는 움직임(1차 결과 = 거울 적용 후 기준으로 구운 값이라 거울 뒤에 덮어씀)
        if t['m_Name'] in fold_w:
            fw = fold_w[t['m_Name']]
            for b in bs:
                if b['path'].split('/')[-1] == 'weapon' and b['attribute'] in (1, 4):
                    n = len(curve_values(t, b, 0)); assert n == len(fw['frames']), (t['m_Name'], n, len(fw['frames']))
                    eul = euler_track([f['rot'] for f in fw['frames']], [curve_values(t, b, j)[0] for j in range(3)]) if b['attribute'] == 4 else None
                    for j, ax in enumerate('xyz'):
                        vals = [f['pos'][ax] for f in fw['frames']] if b['attribute'] == 1 else [e[j] for e in eul]
                        set_samples(t, b['offset'] + j, vals)
                    row.setdefault('foldW', []).append((b['attribute'], b['storage']))
        # 멈치 때리기(0.9.6): 총·왼손 IK 마커·왼손가락·노리쇠 위치 = K2AnimBakeCatch(M4 m4a1_reload_catch_0 이식). 왼팔 뼈는 위에서 첫 값 고정.
        if t['m_Name'] in catch:
            cf = catch[t['m_Name']]['frames']
            def put(b, key, rot):
                n = len(curve_values(t, b, 0)); assert n == len(cf), (t['m_Name'], b['path'], n, len(cf))
                eul = euler_track([key(f) for f in cf], [curve_values(t, b, j)[0] for j in range(3)]) if rot else None
                for j, ax in enumerate('xyz'):
                    vals = [key(f)[ax] for f in cf] if not rot else [e[j] for e in eul]
                    set_samples(t, b['offset'] + j, vals)
            fingers = {x['name'] for x in cf[0]['fingers']}
            for b in bs:
                last = b['path'].split('/')[-1]
                if last == 'weapon' and b['attribute'] in (1, 4): put(b, (lambda f: f['weaponPos']) if b['attribute'] == 1 else (lambda f: f['weaponRot']), b['attribute'] == 4)
                elif last == 'weapon_L_IK_marker' and b['attribute'] in (1, 4): put(b, (lambda f: f['lPos']) if b['attribute'] == 1 else (lambda f: f['lRot']), b['attribute'] == 4)
                elif last == 'weapon_bolt' and b['attribute'] == 1: put(b, lambda f: f['boltPos'], False)
                elif last in fingers and b['attribute'] == 4: put(b, lambda f, nm=last: next(x['rot'] for x in f['fingers'] if x['name'] == nm), True)
                else: continue
                row.setdefault('catch', []).append(last + ':' + str(b['attribute']))
            rescale_time(t, CATCH_SCALE); row['catchScale'] = CATCH_SCALE   # 곡선을 다 쓴 뒤에(쓰기는 원래 프레임 격자 기준)
        changed[ident] = t; changes.append(row)
    assert len(changed) == 98, len(changed)

    # override 번들: 원본 override 파일 형식 보존, 필요한 객체만
    e = g.env[DONOR]; old, s = sf(e); ab = next(o for o in s.objects.values() if o.type.name == 'AssetBundle')
    s.objects = {o.path_id: o for o in [ab, *overrides]}
    cab = 'CAB-' + hashlib.sha256(b'K2-anim-v004-claude-camera27-charge-mirrorfold').hexdigest()[:32]

    def external(key):
        name = sf(g.env[key])[0]
        for i, ext in enumerate(s.externals):
            if ext.path.split('/')[-1].lower() == name.lower(): return i + 1
        ext = copy.copy(s.externals[0]); ext.path = 'archive:/' + name + '/' + name; s.externals.append(ext); return len(s.externals)

    def remap_source(key, p):
        if not p['m_PathID']: return
        k, obj = g.resolve(key, p); p['m_FileID'] = external(k); p['m_PathID'] = obj.path_id

    added = {}; clipids = {}; typeids = {}
    for ident, t in changed.items():
        o = refs[ident]; pid = o.path_id; assert pid not in s.objects
        co = copy.copy(o); co.assets_file = s; tk = (ident[0], o.type_id)
        if tk not in typeids: typeids[tk] = len(s.types); s.types.append(copy.copy(o.serialized_type))
        co.type_id = typeids[tk]; co.serialized_type = s.types[co.type_id]
        visit(t, lambda p: remap_source(ident[0], p)); co.save_typetree(t); s.objects[pid] = co; added[ident] = co; clipids[ident] = pid
    base_refs = [g.resolve(BASE, p) for p in base.read_typetree()['m_AnimationClips'] if p['m_PathID']]
    # 칸 바꿔 끼우기: 빈 탄창 장전 상태(RELOAD CHARGE END)는 숙련도(WeaponLevel 0·1·2)로 charge_0·1·2를 고른다.
    # 1.0.0(사용자): 처음(0)은 장전손잡이 당기기, 숙련도가 오르면(1·2) 멈치 때리기 — charge_1 칸 → 멈치 때리기 클립(charge_2 수정본).
    by_name = {changed[i]['m_Name']: clipids[i] for i in added}
    swap = {a: by_name[b] for a, b in SLOT_SWAP.items() if b in by_name}
    for o in overrides:
        t = o.read_typetree(); existing = set()
        for pair in t['m_Clips']:
            origin = g.resolve(DONOR, pair['m_OriginalClip']); existing.add((origin[0], origin[1].path_id))
            effective = g.resolve(DONOR, pair['m_OverrideClip']) or origin; eid = (effective[0], effective[1].path_id)
            if eid in added: pair['m_OverrideClip'] = ptr(clipids[eid])
            for nm in {origin[1].read_typetree()['m_Name'], effective[1].read_typetree()['m_Name']} & set(swap): pair['m_OverrideClip'] = ptr(swap[nm])
        for k, obj in base_refs:
            ident = (k, obj.path_id)
            if ident not in existing and ident in added:
                nm = changed[ident]['m_Name']
                t['m_Clips'].append({'m_OriginalClip': ptr(obj.path_id, external(k)), 'm_OverrideClip': ptr(swap.get(nm, clipids[ident]))}); existing.add(ident)
        o.save_typetree(t)
    t = ab.read_typetree(); t['m_Name'] = ANIM; t['m_AssetBundleName'] = ANIM
    allobjs = [*overrides, *added.values()]; t['m_PreloadTable'] = [ptr(o.path_id) for o in allobjs]
    t['m_Container'] = [('assets/content/weapons/daewoo_k2/anim_v002/' + o.read_typetree()['m_Name'].lower() + ('.anim' if o.type.name == 'AnimationClip' else '.overridecontroller'),
                         {'preloadIndex': 0, 'preloadSize': len(allobjs), 'asset': ptr(o.path_id)}) for o in allobjs]
    if 'm_MainAsset' in t: t['m_MainAsset'] = {'preloadIndex': 0, 'preloadSize': 0, 'asset': ptr(0)}
    ab.save_typetree(t); e.file.files = {old: s}; renamed(e, cab)
    OUT.mkdir(parents=True); dest = OUT / 'bundles' / ANIM; dest.parent.mkdir(parents=True); dest.write_bytes(e.file.save(packer='lz4'))

    # K2 컨테이너(원래 키): 컨트롤러 3개만 우리 override로. fast 데이터·메시 참조 불변
    bodyenv = g.env[BODY]; _, body = sf(bodyenv)
    fast = {o.path_id: hashlib.sha256(o.get_raw_data()).hexdigest() for o in body.objects.values() if o.type.name == 'TextAsset'}
    ext = copy.copy(body.externals[0]); ext.path = 'archive:/' + cab + '/' + cab; body.externals.append(ext)
    for o in body.objects.values():
        if o.type.name == 'MonoBehaviour':
            t = o.read_typetree()
            if '_originalAnimatorController' in t:
                for field in ('_originalAnimatorController', '_animatorSimple', '_animatorSpirit'):
                    assert t[field]['m_PathID'] in s.objects; t[field]['m_FileID'] = len(body.externals)
                o.save_typetree(t)
    # 컨테이너 모델의 mod_stock 기본 위치도 거울(애니메이션 없는 곳 — 인벤토리 미리보기·바닥 등)
    gos = {o.path_id: o.read_typetree()['m_Name'] for o in body.objects.values() if o.type.name == 'GameObject'}
    nstock = 0
    for o in body.objects.values():
        if o.type.name == 'Transform':
            tr = o.read_typetree()
            if gos.get(tr['m_GameObject']['m_PathID']) == 'mod_stock':
                lp = tr['m_LocalPosition']; assert abs(lp['x'] - pivot_x) < 0.002, lp
                lp['x'] = -lp['x'] + shift[0]; lp['y'] += shift[1]; lp['z'] += shift[2]; o.save_typetree(tr); nstock += 1
    assert nstock >= 1
    mirror_parts(SRC, OUT / 'bundles', (-HINGE[0], -HINGE[1]))
    nmag = 0
    for o in body.objects.values():
        if o.type.name == 'Transform':
            tr = o.read_typetree()
            if gos.get(tr['m_GameObject']['m_PathID']) in ('mod_magazine', 'mod_magazine_new'):
                tr['m_LocalPosition']['z'] += LIFT; o.save_typetree(tr); nmag += 1
    assert nmag >= 2, nmag
    nch = 0
    for o in body.objects.values():
        if o.type.name == 'Transform':
            tr = o.read_typetree()
            if gos.get(tr['m_GameObject']['m_PathID']) in CHAMBER_NODES:
                for k, v in zip('xyz', CHAMBER_SHIFT): tr['m_LocalPosition'][k] += v
                o.save_typetree(tr); nch += 1
    print('약실·배출 노드(컨테이너)', nch)
    nsel = 0
    for o in body.objects.values():
        if o.type.name == 'Transform':
            tr = o.read_typetree()
            if gos.get(tr['m_GameObject']['m_PathID']) == 'weapon_selector':
                for k, v in zip('xyz', SELECTOR_PIVOT): tr['m_LocalPosition'][k] = v
                o.save_typetree(tr); nsel += 1
    print('조정간 노드(컨테이너)', nsel)
    shift_children(SRC, OUT / 'bundles', MAGS, (0, -LIFT, 0))   # 슬롯 회전 0(평소 자세) → 루트 기준 = X−90 × (0,0,−LIFT) = (0, −LIFT, 0)
    renamed(bodyenv, 'CAB-' + hashlib.sha256(b'K2-anim-v004-claude-container').hexdigest()[:32])
    dest = OUT / 'bundles' / BODY; dest.parent.mkdir(parents=True, exist_ok=True); dest.write_bytes(bodyenv.file.save(packer='lz4'))
    _, check = sf(UnityPy.load(str(dest)))
    assert fast == {o.path_id: hashlib.sha256(o.get_raw_data()).hexdigest() for o in check.objects.values() if o.type.name == 'TextAsset'}
    win = json.loads((GAME / 'Windows.json').read_text(encoding='utf-8-sig'))
    proof = {'source': str(SRC), 'changedClips': len(added), 'pivotX': pivot_x, 'animDeps': win[DONOR]['Dependencies'],
             'changes': changes, 'sizes': {k: (OUT / 'bundles' / k).stat().st_size for k in (ANIM, BODY)}}
    (OUT / 'build-proof.json').write_text(json.dumps(proof, ensure_ascii=False, indent=1), encoding='utf-8')
    print(json.dumps({k: v for k, v in proof.items() if k != 'changes'}, ensure_ascii=False))
    for r in changes:
        if r['ik'] or 'rotYZ' in r['fold']: print(r['name'], r['ik'], r['fold'], r.get('foldPosition'))


STOCKS = ['assets/content/items/mods/stocks/stock_daewoo_k2_std.bundle', 'assets/content/items/mods/stocks/stock_daewoo_k2_ar.bundle',
          'assets/content/items/mods/stocks/stock_daewoo_k2_light.bundle']


MAGS = ['assets/content/items/mods/magazines/mag_stanag_daewoo_k2_std_556x45_30.bundle']


def shift_children(src, dst, keys, d):
    """부품 루트 바로 아래 노드 전부(LOD 메시·총알 노드)를 루트 기준 d만큼 이동."""
    for key in keys:
        env = UnityPy.load(str(src / key)); _, f = sf(env)
        ts = {o.path_id: o for o in f.objects.values() if o.type.name == 'Transform'}
        root = next(i for i, o in ts.items() if o.read_typetree()['m_Father']['m_PathID'] == 0)
        for ch in ts[root].read_typetree()['m_Children']:
            o = ts[ch['m_PathID']]; tr = o.read_typetree()
            for k, v in zip('xyz', d): tr['m_LocalPosition'][k] += v
            o.save_typetree(tr)
        out = dst / key; out.parent.mkdir(parents=True, exist_ok=True); out.write_bytes(env.file.save(packer='lz4'))


def mirror_parts(src, dst, d):
    """K2 개머리판 3종: 루트 바로 아래 노드(LOD 메시)를 슬롯 기준 (dx, dy)만큼 이동. 루트 = 슬롯 × X+90이라 루트 기준 = X−90 × 슬롯 기준 = (dx, 0, −dy)."""
    for key in STOCKS:
        env = UnityPy.load(str(src / key)); _, f = sf(env)
        ts = {o.path_id: o for o in f.objects.values() if o.type.name == 'Transform'}
        root = next(i for i, o in ts.items() if o.read_typetree()['m_Father']['m_PathID'] == 0)
        rt = ts[root].read_typetree(); assert all(abs(rt['m_LocalRotation'][k]) < 1e-6 for k in 'xyz'), rt['m_LocalRotation']
        for ch in rt['m_Children']:
            o = ts[ch['m_PathID']]; tr = o.read_typetree(); q = tr['m_LocalRotation']
            assert abs(q['y']) < 1e-4 and abs(q['z']) < 1e-4, q   # x축 회전만
            tr['m_LocalPosition']['x'] += d[0]; tr['m_LocalPosition']['z'] -= d[1]; o.save_typetree(tr)
        out = dst / key; out.parent.mkdir(parents=True, exist_ok=True); out.write_bytes(env.file.save(packer='lz4'))


if __name__ == '__main__': main()
