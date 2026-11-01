<#
.SYNOPSIS
    Macht Enable-DirectIP.ps1 rückgängig und stellt die Original-Engine.ini wieder her.
.PARAMETER ConfigDir
    Nur zum Testen: anderer Config-Ordner.
#>
param([string]$ConfigDir)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DDS2Common.ps1')

if (-not $ConfigDir) { $ConfigDir = Get-DDS2ConfigDir }
$ini = Join-Path $ConfigDir 'Engine.ini'
$backup = "$ini.dds2mod-backup"
$noFileMarker = "$ini.dds2mod-nofile"

if (Test-Path $backup) {
    Copy-Item $backup $ini -Force
    Remove-Item $backup
    Write-Host "Original-Engine.ini wiederhergestellt." -ForegroundColor Green
} elseif (Test-Path $noFileMarker) {
    if (Test-Path $ini) { Remove-Item $ini }
    Remove-Item $noFileMarker
    Write-Host "Engine.ini entfernt (es gab vorher keine)." -ForegroundColor Green
} elseif ((Test-Path $ini) -and ((Get-Content $ini -Raw) -match [regex]::Escape($script:BlockStart))) {
    # Kein Backup vorhanden: nur unseren Block herausschneiden.
    $rest = Remove-DirectIPBlock (Get-Content $ini -Raw)
    [IO.File]::WriteAllText($ini, "$rest`r`n", (New-Object Text.UTF8Encoding $false))
    Write-Host "DDS2Mod-Block aus Engine.ini entfernt (kein Backup gefunden)." -ForegroundColor Yellow
} else {
    Write-Host 'Direkt-IP war nicht aktiv, nichts zu tun.'
}
