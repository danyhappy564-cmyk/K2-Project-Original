#!/bin/bash
# K2 번들 전체 파이프라인 — 버전 이름을 받아 Assets-Local/Bundles/<버전>-raw → <버전>-shader → <버전> 순서로 새 폴더에 만든다(덮어쓰기 없음).
#   bash tools/build_bundles.sh all-v005
# 1) Unity(SDK 작업본)에서 K2Builder.BuildAll — 본체·부품·공용 텍스처 번들 + 셰이더는 가짜 "shaders" 외부 참조
# 2) fix_shader_refs.py — 재질이 게임 shaders 번들의 진짜 셰이더를 참조하게(내장 SDK 셰이더는 게임에서 흰색)
# 3) fix_vanilla_refs.py — 애니메이터·아바타·사운드·총구 효과를 바닐라 M4·부품 번들의 원본으로 외부 참조
# 4) make_bundles_json.py — bundles.json(바닐라 의존 포함) + 외부 참조가 전부 의존 안에 있는지 검사
# 그다음 Server/bundles·Server/bundles.json으로 복사하고 Package-Mod.ps1.
set -euo pipefail
VER="$1"
REPO="$(cd "$(dirname "$0")/.." && pwd -W 2>/dev/null || pwd)"
SDK="F:/코딩/Index/클로드방/EFT-SDK"
UNITY="/g/Software/Unity2022.3.43f1/Editor/Unity.exe"
GAME="E:/SPT 4.1/EscapeFromTarkov_Data/StreamingAssets/Windows"   # 읽기 전용
B="$REPO/Assets-Local/Bundles"
TOOLS="${K2TOOLS:-F:/코딩/레포/Cluade_For_spt/projects/K2-Project-Original/unity-editor/K2Tools}"   # 에디터 도구 원본(2026-10-18 지식 저장소로 옮김)
cp "$TOOLS"/*.cs "$SDK/Assets/Editor/K2Tools/"
"$UNITY" -batchmode -quit -projectPath "$SDK" -executeMethod K2Tools.K2Builder.BuildAll -k2Out "$B/$VER-raw" -logFile "$B/$VER-unity.log"
python3 "$REPO/tools/fix_shader_refs.py" "$B/$VER-raw" "$B/$VER-shader" "$GAME"
mkdir -p "$B/$VER"
fix() { python3 "$REPO/tools/fix_vanilla_refs.py" "$B/$VER-shader/$1" "$B/$VER/$1" "$GAME/$2" "${@:3}" | tail -1; }
# 컨테이너: WTT 방식 — 재구성한 SCAR 컨트롤러·클립·효과음은 번들 안에 두고, 총구 효과 3종만 바닐라 외부 참조
fix assets/content/weapons/daewoo_k2/weapon_daewoo_k2_556x45_container.bundle assets/content/weapons/scar/weapon_fn_mk16_556x45_container.bundle "daewoo_k2_556x45=fn_mk16_556x45" --only DefaultMuzzlePrefab,DefaultSmokeport,DefaultHeatHazeEffect
fix assets/content/items/mods/recievers/reciever_daewoo_k2_std.bundle assets/content/items/mods/recievers/reciever_ar15_colt_m4a1_std.bundle "reciever_daewoo_k2_std=reciever_ar15_colt_m4a1_std"
fix assets/content/items/mods/barrels/barrel_daewoo_k2_465mm.bundle assets/content/items/mods/barrels/barrel_ar15_370mm.bundle "barrel_daewoo_k2_465mm=barrel_ar15_370mm"
fix assets/content/items/mods/handguards/handguard_daewoo_k2_mits_long.bundle assets/content/items/mods/handguards/handguard_ar15_colt_m4_length_std.bundle "handguard_daewoo_k2_mits_long=handguard_ar15_colt_m4_length_std"
fix assets/content/items/mods/muzzle/muzzle_daewoo_k2_std_556x45.bundle assets/content/items/mods/muzzle/muzzle_ar15_colt_usgi_a2_556x45.bundle "muzzle_daewoo_k2_std_556x45=muzzle_ar15_colt_usgi_a2_556x45"
fix assets/content/items/mods/stocks/stock_daewoo_k2_std.bundle assets/content/items/mods/stocks/stock_ar15_colt_stock_tube_std.bundle "stock_daewoo_k2_std=stock_ar15_colt_stock_tube_std"
fix assets/content/items/mods/magazines/mag_stanag_daewoo_k2_std_556x45_30.bundle assets/content/items/mods/magazines/mag_stanag_colt_ar15_std_556x45_30.bundle "mag_stanag_daewoo_k2_std_556x45_30=mag_stanag_colt_ar15_std_556x45_30"
mkdir -p "$B/$VER/assets/content/weapons/daewoo_k2"
cp "$B/$VER-shader/assets/content/weapons/daewoo_k2/daewoo_k2_textures.bundle" "$B/$VER/assets/content/weapons/daewoo_k2/"
cp "$B/$VER-shader/validation.json" "$B/$VER-shader/shader-fix.json" "$B/$VER/"
python3 "$REPO/tools/make_bundles_json.py" "$B/$VER" "$B/$VER/validation.json" "$GAME" "$B/$VER/bundles.json"
echo "완료: $B/$VER"
