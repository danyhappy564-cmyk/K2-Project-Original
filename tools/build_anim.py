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
- 탄창 칸: mod_magazine·mod_magazine_new +z LIFT(다른 탄창이 K2 탄창 구멍에), K2 전용 탄창 메시는 −LIFT.
m_ValueArrayDelta(곡선별 시작·끝 값)도 같은 선형 변환으로 맞춘다.

  python tools/build_anim.py <K2 번들 폴더(ak-v014)> <출력 폴더>   (0.9.3: anim-v009 → ak-v016)
필요: Codex 작업 폴더(코덱스방/K2-anim-v001)의 ak_graph_v003a·ak_curve_fix_v003a·build_ak_anim_v002a 모듈, bake-ak-v003/ik-curves.json,
      BAKE_R(K2AnimBakeCharge 결과). 조립: ak-v010 + 출력 → ak-v013, bundles.json에 override 번들 항목·컨테이너 의존 추가.
"""
import sys, pathlib, json, hashlib, copy, math
CODEX = pathlib.Path('F:/코딩/Index/코덱스방/K2-anim-v001')
sys.path.insert(0, str(CODEX))
import ak_graph_v003a as G
from ak_graph_v003a import *   # Graph, bindings, clipdata, modify_scalar, sf, ptr, visit, BODY, DONOR, BASE, ANIM, GAME
from ak_curve_fix_v003a import unity_to_clip, eval_scalar
from build_ak_anim_v002a import renamed

SRC = pathlib.Path(sys.argv[1]); OUT = pathlib.Path(sys.argv[2])
G.SOURCE = SRC
# K2 개머리판 경첩 핀 축(슬롯 기준 x·y, m) — 앞부품 경첩 마디 원통 맞춤(3종 공통, 반지름 ~10mm, 잔차 <1mm). 무기 좌표로 중심 오른쪽 20.6mm
HINGE = (0.0359, 0.0205)
LIFT = 0.027   # K2 모델을 올린 양(-k2SightDy 0.011118 − (−0.015882)) — aim_camera·탄창 칸에 같이 더한다
BAKE_R = 'F:/코딩/Index/클로드방/K2-Project-Original/Assets-Local/Bundles/bake-charge-v001/ik-curves.json'
BAKE_L = ['F:/코딩/Index/클로드방/K2-Project-Original/Assets-Local/Bundles/bake-fold-v001/ik-curves.json',      # 접기: 왼손이 총을 따라감
          'F:/코딩/Index/클로드방/K2-Project-Original/Assets-Local/Bundles/bake-firemode-v001/ik-curves.json']  # 조정간(총 굴림 거울 뒤)
# 약실 총알·탄피 배출 자리(0.9.3): AK patron_in_weapon(y −0.4555, z −0.0007)이 K2 약실(총열 뒤끝 y −0.4292, 축 z +0.0255)보다
# 26mm 앞·26mm 아래라 약실 확인 때 총알이 몸통 밖으로 보였다(인게임). 무기 좌표 (0, +0.0263, +0.0262) 이동 — 모든 클립 + 컨테이너 기본값.
CHAMBER_SHIFT = (0.0, 0.0263, 0.0262)
CHAMBER_NODES = ('patron_in_weapon', 'shellport')
SELECTOR = ('ak100_firemode_0', 'ak100_firemode_1')
MIRROR_NODES = ('weapon',)   # 조정간 클립에서 좌우 거울로 뒤집는 노드(총 굴림) — K2 조정간은 왼쪽
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
        for names in ([R_ARM, L_ARM] if t['m_Name'] in SELECTOR else []) + ([L_ARM] if t['m_Name'] in FOLD else []):
            for b in bs:
                last = b['path'].split('/')[-1]
                if last in solved and b['attribute'] == 4: continue
                if last in names or ('Base HumanL*' in names and last.startswith('Base HumanL')):
                    c = clipdata(t)
                    for j in range(b['dim']): lin(t, b['offset'] + j, scale=0, add=eval_scalar(c, b['offset'] + j, 0))
                    row.setdefault('frozen', []).append(last)
        # 조정간(0.9.2): AK는 총을 굴려 오른쪽 레버를 보여 준다 — K2 조정간은 왼쪽이라 총 움직임을 좌우 거울(위치 x, 회전 y·z 부호)
        if t['m_Name'] in SELECTOR:
            for b in bs:
                if b['path'].split('/')[-1] in MIRROR_NODES:
                    if b['attribute'] == 1: lin(t, b['offset'], scale=-1); row.setdefault('mirror', []).append('pos')
                    elif b['attribute'] == 4: lin(t, b['offset'] + 1, scale=-1); lin(t, b['offset'] + 2, scale=-1); row.setdefault('mirror', []).append('rot')
                    elif b['attribute'] == 2: raise AssertionError('쿼터니언 곡선은 처리 안 함')
        # 탄창 넣기(0.9.2): AK는 끼울 자리에서 앞으로 ~25° 기울여 걸었다 젖혀 넣는다 — K2 탄창은 회전 중심보다 위로 길어 총 몸을 뚫는다.
        # 끼울 자리에 가까울수록 기울기를 0으로(6cm 안 = 0, 14cm 밖 = 원래): 곧게 밀어 넣는 STANAG식. 밀집 곡선만(프레임별).
        if 'mag_in' in t['m_Name']:
            c = clipdata(t); de = c['m_DenseClip']; ns = c['m_StreamedClip']['curveCount']; nd = de['m_CurveCount']; a = de['m_SampleArray']
            pos = next((b for b in bs if b['path'].endswith('/mod_magazine') and b['attribute'] == 1), None)
            rot = next((b for b in bs if b['path'].endswith('/mod_magazine') and b['attribute'] == 4), None)
            if pos and rot and rot['storage'] == 'dense':
                seat = [eval_scalar(c, pos['offset'] + j, t['m_MuscleClip']['m_StopTime']) for j in range(3)]   # 끝 = 끼운 자리
                n = 0
                for f in range(de['m_FrameCount']):
                    tm = de['m_BeginTime'] + f / de['m_SampleRate']
                    pnow = [eval_scalar(c, pos['offset'] + j, tm) for j in range(3)]
                    d = math.dist(pnow, seat); u = min(1, max(0, (d - 0.06) / 0.08)); w = u * u * (3 - 2 * u)   # 0.9.3: 1~8cm → 6~14cm(0.9.2 인게임: 2~5cm에서 아직 기울어 리시버 앞을 뚫음)
                    for j in range(3):
                        k = f * nd + rot['offset'] - ns + j; a[k] *= w
                    n += w < 0.999
                row['magStraight'] = n
        # 탄창 칸(0.9.1): K2를 27mm 올린 만큼 mod_magazine·mod_magazine_new도 무기 +z 27mm(다른 탄창이 K2 탄창 구멍에 맞게).
        # K2 전용 탄창 메시는 −27mm(mag_parts)로 지금 자리 유지.
        for b in bs:
            if b['attribute'] == 1 and b['path'].split('/')[-1] in ('mod_magazine', 'mod_magazine_new'):
                lin(t, b['offset'] + 2, add=LIFT); row.setdefault('mag', []).append(b['path'].split('/')[-1])
        for b in bs:
            if b['attribute'] == 1 and b['path'].split('/')[-1] in CHAMBER_NODES:
                assert b['path'].endswith('/weapon/' + b['path'].split('/')[-1]), b['path']   # 부모가 weapon이라 로컬 = 무기 좌표
                for j in range(3):
                    if CHAMBER_SHIFT[j]: lin(t, b['offset'] + j, add=CHAMBER_SHIFT[j])
                row.setdefault('chamber', []).append(b['path'].split('/')[-1])
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
    for o in overrides:
        t = o.read_typetree(); existing = set()
        for pair in t['m_Clips']:
            origin = g.resolve(DONOR, pair['m_OriginalClip']); existing.add((origin[0], origin[1].path_id))
            effective = g.resolve(DONOR, pair['m_OverrideClip']) or origin; eid = (effective[0], effective[1].path_id)
            if eid in added: pair['m_OverrideClip'] = ptr(clipids[eid])
        for k, obj in base_refs:
            ident = (k, obj.path_id)
            if ident not in existing and ident in added:
                t['m_Clips'].append({'m_OriginalClip': ptr(obj.path_id, external(k)), 'm_OverrideClip': ptr(clipids[ident])}); existing.add(ident)
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
