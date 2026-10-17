"""최종 검사 결과와 1차 보고서를 합쳐 두 지정 위치에 기록합니다."""
import json,pathlib,hashlib
ROOT=pathlib.Path(__file__).resolve().parents[1];OUT=ROOT/'Bundles/gl-v001'
SHARED=pathlib.Path('C:/Users/Real/AppData/Roaming/Claude/side-session-notes/local_668c9bc2-0e6d-4b33-ac77-3c48f4744acb')
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
u=read(OUT/'unity-readback.json');a=read(OUT/'reference-audit.json');b=read(ROOT/'diagnostics/build-report-v002.json');c=read(ROOT/'diagnostics/coordinates-v001.json');p=read(OUT/'patch-validation.json');clips=read(OUT/'clip-binding-audit.json')
assert all(x['passed'] for x in (u,a,p,clips))
log=(ROOT/'diagnostics/unity-readback-v006.log').read_text(encoding='utf8',errors='replace');assert 'Application will terminate with return code 0' in log
files=sorted(OUT.rglob('*.bundle'));assert len(files)==4
inventory=[dict(path=str(f.relative_to(OUT)).replace('\\','/'),bytes=f.stat().st_size,sha256=hashlib.sha256(f.read_bytes()).hexdigest()) for f in files]
(OUT/'bundle-inventory.json').write_text(json.dumps(inventory,indent=2),encoding='utf8')
old=(ROOT/'GL-codex-report.md').read_text(encoding='utf-8-sig');assert '## 2차' not in old,'중복 보고 방지'
total=sum(f['bytes'] for f in inventory)/1048576
new=f'''# K201 GL 번들 작업 보고 — 2026-10-17

## 2차 — 번들 생성·재읽기 완료

- 한 일: 복사본 SDK의 Unity 라이선스 연결 확인 후, GL 부품 원점 비교·바닐라 부착 검증·번들 4개 생성·도너 참조 이식·참조 감사·Unity 재읽기·장전 샘플·조립 렌더를 마쳤습니다.
- 결과 폴더: `F:/코딩/Index/코덱스방/K2-gl-v001/Bundles/gl-v001/`입니다. 번들 4개 합계 **{total:.2f} MiB**이며 `assets/`와 `bundles.json`이 전달 대상입니다. 계약 키와 슬롯 이름은 바꾸지 않았습니다.
- 확인: 참조 **{a['checkedPointers']}개, 누락 0개**, 의존 포함 {a['loadedBundles']}개 번들을 감사했습니다. 최종 Unity 배치는 종료 코드 0입니다.
- 확인: K2 조립 길이 **{u['assembledSizeMetres']['z']:.6f}m**, 폭 **{u['assembledSizeMetres']['x']:.6f}m**, 높이 **{u['assembledSizeMetres']['y']:.6f}m**입니다. 원본/새 FBX 공통 GL 부품의 원점 차이는 **{b['originErrorMm']:.3f}mm**입니다.
- 확인: 바닐라 WROP 합성 위치 오차 **{c['vanilla_wrop_translation_error_mm']:.6f}mm**, 최종 K2 재읽기 합성 오차 **{u['wropPositionErrorMm']:.6f}mm / {u['wropRotationErrorDegrees']:.6f}°**입니다.
- 확인: `m203_ammo_in_wpn` 장전 클립을 21시점에서 샘플링했습니다. 총열 전진 **{max(s['tubeForwardMm'] for s in u['samples']):.3f}mm**, 메시 추종 최대 오차 **{max(s['tubeMeshFollowErrorMm'] for s in u['samples']):.6f}mm**입니다.
- 미검증: 게임 내 손 IK·손가락 접촉, 탄착/거리별 조준, 소리, 실제 게임 셰이더 외관은 검증하지 않았습니다. 총구는 변경하지 않은 도너 fireport보다 **{b['fireportAheadMm']:.2f}mm** 앞입니다.
- 사용자가 할 일: 번들과 검사 결과를 Claude에 전달해 서버 설정과 결합·검토하도록 하면 됩니다. 여기서는 DLL 빌드·게임 설치·서버/게임 실행을 하지 않았습니다.

### 좌표·애니메이션 상세

- K201 → M203 모델 공간 이동(m): `(-0.000100, -0.067321, -0.217297)`입니다. 회전은 추가하지 않았습니다.
- 방아쇠 축 잔차(mm): `(-0.097, -1.303, -5.471)`, 약실 뒤끝 잔차(mm): `(-0.038, -0.011, +5.471)`입니다. 앞뒤 두 기준의 평균 정렬을 적용했습니다.
- `weapon_mag_release`와 K201 멈치 축 잔차는 약 `(0.267, -0.912, -0.827)mm`입니다. GL 총열 축은 지정한 `(-0.0001, -0.0552)`에 배치했습니다. 도너 총열 노드 피벗과 실제 약실 축은 약 0.83mm 차이가 있으므로 같은 값으로 오해하면 안 됩니다.
- 기존 상부 번들의 정점에서 K2 이동을 복원했습니다: `({b['k2Offset']['x']:.9f}, {b['k2Offset']['y']:.9f}, {b['k2Offset']['z']:.9f})m`. 정점별 최대 잔차는 {b['upperResidualMm']:.5f}mm입니다. K2 조립 기준은 브리프의 `ak-v019` 부품입니다.
- 보정 WROP 위치(m): `(-0.000149563, -0.017597575, -0.484336436)`입니다. 회전과 애니메이션 대상 노드는 도너 값을 유지했습니다.
- 24클립의 실제 저장 바인딩 {clips['bindingCount']}개를 경로 해시로 검사했습니다. `mod_aim_camera`와 WROP 자체 바인딩은 0개, `fireport` 바인딩은 24클립 모두에 있습니다. 따라서 조준 카메라와 WROP 위치만 옮기고 fireport는 유지했습니다.
- 카메라가 총몸에 가려지는 초기 렌더를 발견해, 사분의 조준기 외곽보다 왼쪽 8mm·위 5mm로 눈 위치를 보정했습니다. 최종 카메라 로컬 위치(m)는 `(-0.044381016, 0.061462315, -0.336994410)`입니다. 접힌 상태의 조준기 뒤 시점이며, 가늠자 전개·거리별 탄도 조준 일치는 미검증입니다.
- M203 손바닥 마커는 그대로 유지했습니다. 정적 마커–최근접 메시 **정점** 거리는 {b['palmVertexDistanceMm']:.2f}mm입니다. 장전 전 구간은 {min(s['palmVertexDistanceMm'] for s in u['samples']):.2f}~{max(s['palmVertexDistanceMm'] for s in u['samples']):.2f}mm이며 탄을 집으러 손을 떼는 구간도 포함합니다. 이것은 손 피부와 표면의 접촉 오차가 아니므로 파지 검증 통과로 해석하면 안 됩니다.

### 결과 구조와 보존 확인

- 런처는 M203 컨테이너를 새 CAB으로 개조했습니다. 메시/재질 참조 각각 16개를 교체했으며 기본·단순 모델 모두 포함합니다. 최종 일반 모델 재읽기에서는 8개 메시와 텍스처 누락 0개를 확인했습니다.
- 도너 MonoBehaviour 55개, Animator 2개, AnimatorController 1개는 원본 바이트 동일 검사를 통과했습니다. 도너의 총구 효과 메시도 유지했습니다. GL_projectile·GL_empty_case는 최종 메시 번들에 넣지 않았습니다.
- GL 핸드가드의 슬롯은 `mod_launcher`, `mod_scope` 두 개입니다. 하위 `mod_handguard`는 제거했습니다. 왼손 포즈와 손가락은 AK-74M 기준으로 표면 차이를 보정했습니다. 표면 보정은 무기 좌표 기준 `(-2.216, 0, +33.566)mm`입니다.
- 공용 재질은 재사용하지 않았습니다. 새 GL 재질과 upper 사본 재질, 2048² 텍스처 6개를 **18.97 MiB** 전용 번들에 넣었습니다. upper 사본 3장의 이미지 데이터는 밉맵 포함 약 16 MiB이며, 최종 압축 번들 안에서의 독립 압축 기여량은 따로 측정하지 않았습니다.
- M203의 Windows.json 의존 전체 14개와 커스텀 메시·재질 의존을 펼쳐 넣었습니다. `bundles.json`에는 계약 키 4개만 등록했습니다.
- 변경 전 조준 카메라 번들과 렌더는 `diagnostics/pre-aim-v001/`에 보관했습니다. 원본 모델·게임·Claude 저장소와 SDK는 수정하지 않았습니다. 중앙 지식 저장소도 이번 쓰기 허용 범위 밖이므로 갱신하지 않았습니다.

### 확인 파일

- `reference-audit.json`: 참조 검사입니다.
- `unity-readback.json`: 최종 조립·WROP·클립 재생 수치입니다.
- `clip-binding-audit.json`: 클립 경로 해시 검사입니다.
- `patch-validation.json`: 도너 데이터 보존과 교체 목록입니다.
- `aim-adjustment.json`: 최종 조준 카메라 변경 근거입니다.
- `texture-inventory.json`, `bundle-inventory.json`: 텍스처 정보·번들 크기·SHA-256입니다.
- `readback-left.png`, `readback-right.png`, `readback-three-quarter.png`, `readback-launcher-aim.png`: 최종 번들을 다시 읽어 만든 화면입니다. Standard 대체 재질로 형태를 확인했으며 게임 셰이더 외관 검사는 아닙니다.

### 새 도구와 실행 이력

- `EFT-SDK/Assets/Editor/K2Tools/K201BuildV002.cs`: GL 메시·재질·핸드가드 번들을 만드는 Unity 에디터 도구입니다.
- `EFT-SDK/Assets/Editor/K2Tools/K201ReadbackV005.cs`: 최종 번들 조립·샘플링·렌더 검사 도구입니다. 최종 실행 로그는 `diagnostics/unity-readback-v006.log`입니다.
- `tools/gl_prepare_v002.py`: 읽기 전용 번들에서 빌드 입력과 로드 목록을 준비합니다.
- `tools/gl_coordinates_v001.py`: 바닐라 부착 합성과 초기 정렬을 계산합니다. 2차 실행에서 성공했습니다. 이 파일의 JSON status 문구는 1차 중단 당시 문구이므로 최종 상태는 이 보고서와 최종 검사 파일을 따릅니다.
- `tools/gl_finish_v002.py` → `gl_finish_v003.py`: 셰이더/바닐라 참조 수정과 M203 개조를 수행했습니다. v002는 총구 효과 메시를 교체 대상으로 분류해 멈췄고, v003에서 해당 효과를 원본 유지하도록 수정해 완료했습니다. v003은 부분 결과를 확인하고 이어가는 전용 도구입니다.
- `tools/gl_clip_audit_v003.py`: 압축된 게임 클립의 실제 바인딩을 검사합니다. Unity 편집용 GetCurveBindings는 이 클립들에 빈 목록을 반환해 판정 근거에서 제외했습니다.
- `tools/gl_aim_v004.py`: 이전 결과를 보존한 뒤 눈 위치를 보정했습니다.
- `tools/gl_report_v006.py`: 이 보고서와 최종 파일 목록을 기록합니다.
- 재읽기 v003은 시각화용 복제본에 Avatar가 없어 장전 샘플링이 적용되지 않았습니다. v004부터 실제 번들 프리팹을 복제하고 도너 Animator/Avatar를 보존해 재생했습니다. v005는 오른쪽 화면의 검사 조명을 보완했습니다. 실패한 검사 버전도 진단용으로 남겼습니다.
- 전용 Python 환경은 `.venv/`입니다. config 값은 `gpt-6-astra` / `medium`, 실제 실행 모델 ID는 미확인입니다.

---

## 1차 — 이전 보고서 원문

{old}
'''
SHARED.mkdir(parents=True,exist_ok=True)
(ROOT/'GL-codex-report.md').write_text(new,encoding='utf8')
(SHARED/'GL-codex-report.md').write_text(new,encoding='utf8')
assert (ROOT/'GL-codex-report.md').read_bytes()==(SHARED/'GL-codex-report.md').read_bytes()
print('보고서 두 위치 기록·일치 확인, 번들',len(files),'총 MiB',round(total,2))
