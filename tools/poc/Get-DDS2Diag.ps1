<#
.SYNOPSIS
    Sammelt die netzwerkrelevanten Zeilen aus dem neuesten Spiel-Log.
.DESCRIPTION
    Schreibt eine Zusammenfassung auf den Desktop (DDS2-Diagnose.txt),
    die du mir schicken kannst.
#>
param([int]$MaxLines = 300)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DDS2Common.ps1')

$saved = Get-DDS2SavedDir
$out = Join-Path ([Environment]::GetFolderPath('Desktop')) 'DDS2-Diagnose.txt'
$report = New-Object System.Collections.Generic.List[string]

$report.Add("DDS2 Diagnose $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
$report.Add("Spielordner: $(Find-DDS2GameDir)")

$ini = Join-Path (Get-DDS2ConfigDir) 'Engine.ini'
$active = (Test-Path $ini) -and ((Get-Content $ini -Raw) -match [regex]::Escape($script:BlockStart))
$report.Add("Direkt-IP-Block in Engine.ini: $active")

$log = Get-ChildItem (Join-Path $saved 'Logs') -Filter '*.log' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $log) {
    $report.Add("KEIN Log gefunden in $saved\Logs")
} else {
    $report.Add("Log: $($log.FullName) ($($log.LastWriteTime))")
    $report.Add('')
    $pattern = 'LogNet|NetDriver|Listen|Welcomed|NMT_|SteamAuth|LogOnline|Travel|PendingConnection|NetworkFailure|LogGameMode|LogLoad.*Took|Error'
    Select-String -Path $log.FullName -Pattern $pattern |
        Select-Object -Last $MaxLines | ForEach-Object { $report.Add($_.Line) }
}

[IO.File]::WriteAllLines($out, $report, (New-Object Text.UTF8Encoding $true))
Write-Host "Diagnose gespeichert: $out" -ForegroundColor Green
