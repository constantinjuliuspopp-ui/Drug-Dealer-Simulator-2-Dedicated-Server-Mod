# Gemeinsame Hilfsfunktionen für die PoC-Skripte. Wird per Dot-Sourcing geladen.

$script:DDS2AppId = 1708850
$script:BlockStart = '; >>> DDS2Mod DirectIP (nicht von Hand bearbeiten)'
$script:BlockEnd = '; <<< DDS2Mod DirectIP'

function Get-SteamLibraryPaths {
    $roots = @()
    foreach ($key in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam') {
        $props = Get-ItemProperty $key -ErrorAction SilentlyContinue
        if ($props.SteamPath) { $roots += $props.SteamPath }
        if ($props.InstallPath) { $roots += $props.InstallPath }
    }
    $libs = @()
    foreach ($root in ($roots | ForEach-Object { $_ -replace '/', '\' } | Select-Object -Unique)) {
        $libs += $root
        $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $libs += $m.Groups[1].Value -replace '\\\\', '\'
            }
        }
    }
    $libs | ForEach-Object { $_.TrimEnd('\') } | Select-Object -Unique
}

function Find-DDS2GameDir {
    foreach ($lib in Get-SteamLibraryPaths) {
        $manifest = Join-Path $lib "steamapps\appmanifest_$script:DDS2AppId.acf"
        if (-not (Test-Path $manifest)) { continue }
        $installDir = 'Drug Dealer Simulator 2'
        if ((Get-Content $manifest -Raw) -match '"installdir"\s+"([^"]+)"') { $installDir = $Matches[1] }
        $dir = Join-Path $lib "steamapps\common\$installDir"
        if (Test-Path (Join-Path $dir 'DrugDealerSimulator2.exe')) { return $dir }
    }
    return $null
}

function Get-DDS2SavedDir {
    Join-Path $env:LOCALAPPDATA 'DrugDealerSimulator2\Saved'
}

function Get-DDS2ConfigDir {
    Join-Path (Get-DDS2SavedDir) 'Config\Windows'
}

function Remove-DirectIPBlock([string]$Text) {
    $pattern = '(?ms)\r?\n?' + [regex]::Escape($script:BlockStart) + '.*?' + [regex]::Escape($script:BlockEnd) + '\r?\n?'
    [regex]::Replace($Text, $pattern, "`r`n").TrimEnd()
}
