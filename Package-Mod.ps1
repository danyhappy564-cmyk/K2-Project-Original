# 서버 모드를 설치용 폴더·zip으로 묶는다(클라 DLL 없음 — 0.3.0부터 WTT 방식 번들만). 게임 폴더에는 아무것도 쓰지 않는다.
#   pwsh ./Package-Mod.ps1 -Version 0.3.0
# 결과: Assets-Local/Package/<버전>/K2-Project-Original-<버전>/SPT_Runtime/user/mods/K2-Project-Original/ (게임 폴더와 같은 구조)
# 설치(사용자): 이 폴더 안의 SPT_Runtime을 게임 폴더에 그대로 덮어 복사.
param([string]$Version = "0.3.0")
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
foreach ($proj in @("Server/K2.Server.csproj")) {
    dotnet build (Join-Path $root $proj) -c Release -nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "빌드 실패: $proj" }
}
$out = Join-Path $root "Assets-Local/Package/$Version"
if (Test-Path $out) { throw "이미 있음 — 덮어쓰지 않습니다: $out (버전을 올리세요)" }
$top = Join-Path $out "K2-Project-Original-$Version"
$server = Join-Path $top "SPT_Runtime/user/mods/K2-Project-Original"
New-Item -ItemType Directory -Force $server | Out-Null

$bin = Join-Path $root "Server/bin/Release/net10.0"
Copy-Item (Join-Path $bin "K2.Server.dll"), (Join-Path $bin "bundles.json") $server
Copy-Item (Join-Path $bin "data") $server -Recurse
Copy-Item (Join-Path $root "Server/bundles") $server -Recurse   # bin이 아니라 원본에서(지난 빌드의 남은 번들이 섞이지 않게)
Copy-Item (Join-Path $root "LICENSE") $server

# 검사: bundles.json 키마다 파일이 있는지, 목록에 없는 번들 파일이 섞이지 않았는지
$manifest = Get-Content (Join-Path $server "bundles.json") -Raw | ConvertFrom-Json
$keys = $manifest.manifest.key
$missing = $keys | Where-Object { -not (Test-Path (Join-Path $server "bundles/$_")) }
if ($missing) { throw "번들 파일 없음: $($missing -join ', ')" }
$files = Get-ChildItem (Join-Path $server "bundles") -Recurse -File | ForEach-Object { $_.FullName.Substring((Join-Path $server "bundles").Length + 1).Replace('\', '/') }
$extra = $files | Where-Object { $keys -notcontains $_ }
if ($extra) { throw "bundles.json에 없는 번들 파일: $($extra -join ', ')" }
Compress-Archive -Path (Join-Path $top "*") -DestinationPath (Join-Path $out "K2-Project-Original-$Version.zip")
Write-Host "완료: $top (서버 번들 $($keys.Count)개)"
