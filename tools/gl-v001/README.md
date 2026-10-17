# GL(K201) 번들 도구 기록 — gl-v001 (2026-10-17, Codex 작성·Claude 검토)

실행 위치는 Codex 작업 폴더 `F:\코딩\Index\코덱스방\K2-gl-v001`(경로가 그 폴더 기준으로 박혀 있음). 이 폴더는 기록·재현용 원본이며 그대로 실행하는 도구가 아니다.
Unity 에디터 스크립트는 SDK가 컴파일하지 않도록 `.cs.txt`로 둔다(레포 `Unity/Editor/K2Tools`를 SDK에 통째로 복사하는 흐름과 분리).

순서: `gl_split_v001.py`(Blender, GL_base → GL 핸드가드 / 런처 몸체 분리, k2_gl.fbx) → `gl_readback_v001.py`(FBX 재읽기 검사)
→ `gl_prepare_v002.py`·`gl_coordinates_v001.py`(바닐라 M4+Hanson+M203 부착·WROP 합성 검증, K201 정렬)
→ `K201BuildV002.cs`(Unity: K201 메시·GL 재질 번들, GL 핸드가드 부품 번들) → `gl_finish_v003.py`(셰이더·바닐라 참조, M203 컨테이너 개조, WROP·조준 카메라)
→ `gl_clip_audit_v003.py`(M203 클립 바인딩 해시 검사) → `gl_aim_v004.py`(조준 카메라 보정) → `K201ReadbackV005.cs`(재읽기·장전 샘플·조립 렌더) → `gl_report_v006.py`.
결과: `Assets-Local/Bundles/gl-v001`(보고서 `codex-report.md`).
