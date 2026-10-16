"""검사기(K2Validation)가 쓸 메시 정점·삼각형을 번들에서 직접 뽑는다(UnityPy MeshHandler).

왜: 바닐라 메시는 읽기 금지(isReadable false)라 Unity 에디터에서 mesh.vertices를 못 읽는다. 번들의 m_IsReadable만 true로 바꾼 사본(Codex
prepare_readable_v003c)은 Unity가 정점 채널을 복사하다 네이티브 크래시(VertexUtility::CopyChannel). 그래서 정점은 파이썬에서 풀어 파일로 넘긴다.
출력: <폴더>/<메시 이름>.bin = int32 정점 수, int32 삼각형 인덱스 수, float32 xyz × 정점, int32 × 인덱스(모든 서브메시 합침, 메시 로컬 좌표).

  python tools/export_meshes.py <작업 JSON(loadOrder·parts·body·source·game)> <출력 폴더>
"""
import sys, json, pathlib, struct
import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler

job = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding='utf-8'))
out = pathlib.Path(sys.argv[2]); out.mkdir(parents=True, exist_ok=True)
keys = list(dict.fromkeys(job['loadOrder'] + [job['body']] + [p['key'] for p in job['parts']]))
seen = {}
for key in keys:
    p = pathlib.Path(job.get('source') or '') / key
    if not job.get('source') or not p.is_file(): p = pathlib.Path(job['game']) / key
    if not p.is_file(): continue
    for o in UnityPy.load(str(p)).objects:
        if o.type.name != 'Mesh': continue
        m = o.read(); name = m.m_Name
        if name in seen: continue
        h = MeshHandler(m); h.process()
        if not h.m_Vertices: continue
        v = [tuple(x[:3]) for x in h.m_Vertices]; idx = [i for sub in h.get_triangles() for t in sub for i in t]
        if not v or not idx: continue
        with open(out / (name + '.bin'), 'wb') as f:
            f.write(struct.pack('<ii', len(v), len(idx)))
            f.write(struct.pack('<%df' % (3 * len(v)), *[c for x in v for c in x]))
            f.write(struct.pack('<%di' % len(idx), *idx))
        seen[name] = key
(out / 'index.json').write_text(json.dumps(seen, ensure_ascii=False, indent=1), encoding='utf-8')
print('메시', len(seen))
