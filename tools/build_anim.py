"""K2 동작 번들(anim-v004, Claude) — Codex build_final_anim_v003b.py를 바탕으로 접기 위치만 다른 방식으로.

Codex v003b: 접기 위치 곡선(스트림)을 프레임마다 키를 새로 넣어 다시 인코딩 → Unity SampleAnimation에서 네이티브 크래시(EvaluateClip).
여기서는 다시 인코딩하지 않는다. 오른쪽 접기 = 펼친 상태의 mod_stock x(=c)를 지나는 좌우 거울:
  회전 오일러 y·z 부호 반전(어떤 회전 순서든 M·R·M), 위치 x → 2c − x (y·z 그대로). 둘 다 기존 키 계수의 선형 변환이라
  modify_scalar(scale, add)로 끝난다(스트림 키 수·구조 불변).
카메라(+z 0.027)·장전 IK(dense)는 Codex 그대로. m_ValueArrayDelta(곡선별 시작·끝 값)도 같은 변환으로 맞춘다.

  python tools/build_anim.py <K2 번들 폴더(ak-v010)> <출력 폴더>
필요: Codex 작업 폴더(코덱스방/K2-anim-v001)의 ak_graph_v003a·ak_curve_fix_v003a·build_ak_anim_v002a 모듈과 bake-ak-v003/ik-curves.json(장전 IK 결과).
0.9.0 조립: ak-v010(-k2SightDy 0.011118) + 이 출력 5개 파일 → ak-v011, bundles.json에 override 번들 항목과 컨테이너 의존 추가.
"""
import sys, pathlib, json, hashlib, copy
CODEX = pathlib.Path('F:/코딩/Index/코덱스방/K2-anim-v001')
sys.path.insert(0, str(CODEX))
import ak_graph_v003a as G
from ak_graph_v003a import *   # Graph, bindings, clipdata, modify_scalar, sf, ptr, visit, BODY, DONOR, BASE, ANIM, GAME
from ak_curve_fix_v003a import unity_to_clip, eval_scalar
from build_ak_anim_v002a import renamed

SRC = pathlib.Path(sys.argv[1]); OUT = pathlib.Path(sys.argv[2])
G.SOURCE = SRC
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
    baked = {c['name']: c for c in bake['clips']}; changed = {}; changes = []
    for data in baked.values():
        for sample in data['frames']:
            for field in ('upper', 'forearm', 'palm'): sample[field] = unity_to_clip(sample[field])
    folding = next(o.read_typetree() for o in refs.values() if o.read_typetree()['m_Name'] == 'ak100_stock_folding_left')
    pb = next(b for b in bindings(folding, g.paths) if b['path'].endswith('/mod_stock') and b['attribute'] == 1)
    pivot_x = eval_scalar(clipdata(folding), pb['offset'], 0)   # 펼친 상태 x — 거울면
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
        # 오른쪽 접기: 총 중심선(x=0) 기준 거울 — mod_stock 위치 x 부호 반전(모든 클립), 접기 클립은 회전 오일러 y·z도 반전.
        # 펼친 상태 축이 x −15.3mm → +15.3mm로 가므로 K2 개머리판 메시를 2·pivot_x만큼 옮겨 상쇄한다(아래 mirror_parts).
        st = [b for b in bs if b['path'].endswith('/mod_stock')]
        assert {b['attribute'] for b in st} <= {1, 3, 4} and 1 in {b['attribute'] for b in st}, t['m_Name']
        for b in st:
            if b['attribute'] == 1: lin(t, b['offset'], scale=-1); row['fold'].append('posX')
            elif b['attribute'] == 4 and t['m_Name'] in FOLD: lin(t, b['offset'] + 1, scale=-1); lin(t, b['offset'] + 2, scale=-1); row['fold'].append('rotYZ')
        if t['m_Name'] in FOLD:
            c = clipdata(t); end = max(t['m_MuscleClip']['m_StopTime'], 0)
            row['foldPosition'] = [[round(eval_scalar(c, pb2['offset'] + j, tm), 5) for j in range(3)] for pb2 in st if pb2['attribute'] == 1 for tm in (0, end)]
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
                assert abs(tr['m_LocalPosition']['x'] - pivot_x) < 0.002, tr['m_LocalPosition']
                tr['m_LocalPosition']['x'] = -tr['m_LocalPosition']['x']; o.save_typetree(tr); nstock += 1
    assert nstock >= 1
    mirror_parts(SRC, OUT / 'bundles', 2 * pivot_x)
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


def mirror_parts(src, dst, dx):
    """K2 개머리판 3종: 루트 바로 아래 노드(LOD 메시)를 무기 x로 dx 이동 — 슬롯·루트 회전은 x축 회전뿐이라 로컬 x = 무기 x."""
    for key in STOCKS:
        env = UnityPy.load(str(src / key)); _, f = sf(env)
        ts = {o.path_id: o for o in f.objects.values() if o.type.name == 'Transform'}
        root = next(i for i, o in ts.items() if o.read_typetree()['m_Father']['m_PathID'] == 0)
        rt = ts[root].read_typetree(); assert all(abs(rt['m_LocalRotation'][k]) < 1e-6 for k in 'xyz'), rt['m_LocalRotation']
        for ch in rt['m_Children']:
            o = ts[ch['m_PathID']]; tr = o.read_typetree(); q = tr['m_LocalRotation']
            assert abs(q['y']) < 1e-4 and abs(q['z']) < 1e-4, q   # x축 회전만
            tr['m_LocalPosition']['x'] += dx; o.save_typetree(tr)
        out = dst / key; out.parent.mkdir(parents=True, exist_ok=True); out.write_bytes(env.file.save(packer='lz4'))


if __name__ == '__main__': main()
