<#
.SYNOPSIS
    Stellt den Spiel-Netzwerktreiber von Steam-P2P auf normale IP-Verbindungen um.
.DESCRIPTION
    Schreibt einen Block in die BENUTZER-Engine.ini des Spiels
    (%LOCALAPPDATA%\DrugDealerSimulator2\Saved\Config\Windows\Engine.ini).
    Spieldateien werden nicht verändert. Vorher wird ein Backup angelegt;
    Disable-DirectIP.ps1 stellt den Originalzustand wieder her.
.PARAMETER Port
    Port, auf dem der Host lauscht (Standard 7777).
.PARAMETER ConfigDir
    Nur zum Testen: anderer Config-Ordner.
#>
param(
    [ValidateRange(1, 65535)][int]$Port = 7777,
    [string]$ConfigDir
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DDS2Common.ps1')

if (-not $ConfigDir) { $ConfigDir = Get-DDS2ConfigDir }
if (-not (Test-Path $ConfigDir)) {
    Write-Host "Config-Ordner nicht gefunden: $ConfigDir" -ForegroundColor Red
    Write-Host 'Starte das Spiel einmal bis ins Hauptmenü und beende es dann. Danach dieses Skript erneut ausführen.'
    exit 1
}

$ini = Join-Path $ConfigDir 'Engine.ini'
$backup = "$ini.dds2mod-backup"
$noFileMarker = "$ini.dds2mod-nofile"

# Backup nur beim ersten Aktivieren, damit ein zweiter Aufruf das Original nicht überschreibt.
if (-not (Test-Path $backup) -and -not (Test-Path $noFileMarker)) {
    if (Test-Path $ini) {
        Copy-Item $ini $backup
        Write-Host "Backup angelegt: $backup"
    } else {
        New-Item -ItemType File $noFileMarker | Out-Null
    }
}

$existing = ''
if (Test-Path $ini) { $existing = Remove-DirectIPBlock (Get-Content $ini -Raw) }

$block = @"
$script:BlockStart
[/Script/Engine.GameEngine]
!NetDriverDefinitions=ClearArray
+NetDriverDefinitions=(DefName="GameNetDriver",DriverClassName="/Script/OnlineSubsystemUtils.IpNetDriver",DriverClassNameFallback="/Script/OnlineSubsystemUtils.IpNetDriver")
+NetDriverDefinitions=(DefName="BeaconNetDriver",DriverClassName="/Script/OnlineSubsystemUtils.IpNetDriver",DriverClassNameFallback="/Script/OnlineSubsystemUtils.IpNetDriver")
+NetDriverDefinitions=(DefName="DemoNetDriver",DriverClassName="/Script/Engine.DemoNetDriver",DriverClassNameFallback="/Script/Engine.DemoNetDriver")

[URL]
Port=$Port
$script:BlockEnd
"@

$content = if ($existing) { "$existing`r`n`r`n$block`r`n" } else { "$block`r`n" }
[IO.File]::WriteAllText($ini, $content, (New-Object Text.UTF8Encoding $false))

Write-Host "Direkt-IP aktiviert (Port $Port): $ini" -ForegroundColor Green
Write-Host 'Hinweis: Einladen/Beitreten über Steam funktioniert so lange NICHT. Rückgängig: Disable-DirectIP.ps1'
