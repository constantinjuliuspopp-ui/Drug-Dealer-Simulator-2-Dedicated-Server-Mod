# Installer

Inno Setup 6 (`DDS2Mod.iss`), gebaut von `build.ps1`. Eine Setup-Datei mit Auswahl
„Client-Mod“ / „Dedicated Server“ / „Beides“. Findet das Spiel über Steam-Registry und `libraryfolders.vdf`
(oder `C:\DDS2Server\game` nach SteamCMD), sichert vorhandene UE4SS-Dateien, installiert UE4SS und die Mod,
legt optional Firewall-Regeln für den Server an und macht beim Deinstallieren alles rückgängig.
