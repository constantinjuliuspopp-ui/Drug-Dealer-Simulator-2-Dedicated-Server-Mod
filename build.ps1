<#
.SYNOPSIS
    Baut DDS2Mod.exe und DDS2Server.exe, stellt die Installer-Dateien zusammen und erzeugt dist\DDS2Mod-Setup-<Version>.exe.
#>
param([switch]$SkipInstaller)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = (Get-Content "$root\src\shared\version.json" -Raw | ConvertFrom-Json).modVersion
$stage = "$root\out\stage"
$ue4ss = "$root\third_party\ue4ss-experimental"
$utf8 = New-Object Text.UTF8Encoding $false

Write-Host "== DDS2Mod $version bauen"
foreach ($proj in "src\DDS2Mod.csproj", "src\server\DDS2Server.csproj") {
    dotnet publish "$root\$proj" -c Release -o "$root\out\publish" --nologo
    if ($LASTEXITCODE) { throw "dotnet publish fehlgeschlagen ($proj)" }
}

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
foreach ($d in 'app\licenses', 'game', 'server') { New-Item -ItemType Directory -Force "$stage\$d" | Out-Null }

# Programmordner
Copy-Item "$root\out\publish\DDS2Mod.exe", "$root\out\publish\DDS2Server.exe" "$stage\app\"
Copy-Item "$root\README.md" "$stage\app\"
Copy-Item "$root\src\server\start_server.bat" "$stage\app\start_server.bat.template"
Copy-Item "$ue4ss\ue4ss\LICENSE" "$stage\app\licenses\UE4SS-LICENSE.txt"

# Spielordner (Binaries\Win64): UE4SS + DDS2Net
if (-not (Test-Path "$ue4ss\dwmapi.dll")) { throw "UE4SS fehlt in $ue4ss" }
Copy-Item "$ue4ss\*" "$stage\game\" -Recurse
Copy-Item "$root\installer\ue4ss\mods.txt", "$root\installer\ue4ss\mods.json" "$stage\game\ue4ss\Mods\" -Force
Copy-Item "$root\src\shared\DDS2Net" "$stage\game\ue4ss\Mods\DDS2Net" -Recurse
$lua = "$stage\game\ue4ss\Mods\DDS2Net\Scripts\main.lua"
[IO.File]::WriteAllText($lua, [IO.File]::ReadAllText($lua).Replace('@MOD_VERSION@', $version), $utf8)

# Server-Ordner
Copy-Item "$root\src\shared\server.cfg.example" "$stage\server\server.cfg"
Copy-Item "$root\src\server\install_server.bat" "$stage\server\"

if ($SkipInstaller) { Write-Host "Stage fertig: $stage"; return }

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 (ISCC.exe) nicht gefunden" }
& $iscc /Q "/DAppVersion=$version" "$root\installer\DDS2Mod.iss"
if ($LASTEXITCODE) { throw "Inno Setup fehlgeschlagen" }
# Für die Ersteinrichtung per SteamCMD auch einzeln (wird vor dem Setup ausgeführt).
Copy-Item "$root\src\server\install_server.bat" "$root\dist\install_server.bat" -Force
Write-Host "Fertig: $root\dist\DDS2Mod-Setup-$version.exe und dist\install_server.bat" -ForegroundColor Green
