# 서버 모드를 설치용 폴더·zip으로 묶는다. 게임 폴더에는 아무것도 쓰지 않는다.
#   pwsh ./Package-Mod.ps1            → Assets-Local/Package/<버전>/K2-Project-Original/ + K2-Project-Original-<버전>.zip
# 설치(사용자): 결과 폴더의 K2-Project-Original 폴더를 <SPT>\SPT_Runtime\user\mods\ 에 복사.
param([string]$Version = "0.1.0")
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
dotnet build "$root/Server/K2.Server.csproj" -c Release -nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw "빌드 실패" }
$bin = Join-Path $root "Server/bin/Release/net10.0"
$out = Join-Path $root "Assets-Local/Package/$Version"
if (Test-Path $out) { throw "이미 있음 — 덮어쓰지 않습니다: $out (버전을 올리세요)" }
$mod = Join-Path $out "K2-Project-Original"
New-Item -ItemType Directory -Force $mod | Out-Null
Copy-Item (Join-Path $bin "K2.Server.dll"), (Join-Path $bin "bundles.json") $mod
Copy-Item (Join-Path $bin "data") $mod -Recurse
Copy-Item (Join-Path $bin "bundles") $mod -Recurse
Copy-Item (Join-Path $root "LICENSE") $mod
# bundles.json의 키마다 실제 파일이 있는지 확인
$manifest = Get-Content (Join-Path $mod "bundles.json") -Raw | ConvertFrom-Json
$missing = $manifest.manifest | Where-Object { -not (Test-Path (Join-Path $mod "bundles/$($_.key)")) }
if ($missing) { throw "번들 파일 없음: $($missing.key -join ', ')" }
Compress-Archive -Path $mod -DestinationPath (Join-Path $out "K2-Project-Original-$Version.zip")
Write-Host "완료: $mod (번들 $($manifest.manifest.Count)개 확인)"
