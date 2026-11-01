<#
.SYNOPSIS
    Startet das Spiel und verbindet direkt mit einem Host per IP.
.DESCRIPTION
    Unreal verbindet sich beim Start sofort, wenn die erste Kommandozeilen-Option
    eine Adresse ist. Steam muss laufen; die Besitzprüfung bleibt unverändert.
.EXAMPLE
    .\Join-DirectIP.ps1 -Address 192.168.178.20
#>
param(
    [string]$Address = '127.0.0.1',
    [ValidateRange(1, 65535)][int]$Port = 7777,
    [switch]$NoLog
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DDS2Common.ps1')

if ($Address -notmatch '^[A-Za-z0-9.\-]+$') {
    Write-Host "Ungültige Adresse: '$Address' (nur IP oder Hostname, ohne Port)" -ForegroundColor Red
    exit 1
}

$game = Find-DDS2GameDir
if (-not $game) {
    Write-Host 'Drug Dealer Simulator 2 wurde in keiner Steam-Bibliothek gefunden.' -ForegroundColor Red
    exit 1
}

$gameArgs = @("${Address}:$Port")
if (-not $NoLog) { $gameArgs += '-log' }

Write-Host "Starte $game\DrugDealerSimulator2.exe $($gameArgs -join ' ')"
Start-Process -FilePath (Join-Path $game 'DrugDealerSimulator2.exe') -ArgumentList $gameArgs -WorkingDirectory $game
