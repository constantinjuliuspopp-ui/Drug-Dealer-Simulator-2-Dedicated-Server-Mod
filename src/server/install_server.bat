@echo off
setlocal
rem ---------------------------------------------------------------------------
rem  DDS2 Server: Spiel per SteamCMD installieren oder aktualisieren
rem
rem  - Laedt SteamCMD von Valve (steamcdn-a.akamaihd.net), falls noch nicht da.
rem  - Installiert/aktualisiert Drug Dealer Simulator 2 (App 1708850) nach C:\DDS2Server\game
rem    (anderer Ordner: install_server.bat "D:\Pfad\zum\Ordner").
rem  - Die Anmeldung macht SteamCMD selbst: Passwort und Steam-Guard-Code tippst du dort ein,
rem    dieses Skript sieht und speichert sie nicht.
rem  - Das Konto muss das Spiel besitzen. Eine anonyme Anmeldung geht bei diesem Spiel nicht,
rem    weil es keine eigene Server-App gibt.
rem  - Zum Starten des Servers muss spaeter trotzdem der Steam-Client laufen und angemeldet sein.
rem ---------------------------------------------------------------------------
chcp 65001 >nul
rem Nicht unter Dokumente (oft per OneDrive synchronisiert, das Spiel ist mehrere GB gross).
set "BASE=%SystemDrive%\DDS2Server"
set "STEAMCMD=%BASE%\steamcmd"
set "GAMEDIR=%BASE%\game"
if not "%~1"=="" set "GAMEDIR=%~1"

echo.
echo  DDS2 Server - Spiel per SteamCMD installieren / aktualisieren
echo  Zielordner: %GAMEDIR%
echo.

if not exist "%STEAMCMD%\steamcmd.exe" (
    echo SteamCMD wird von Valve heruntergeladen ...
    mkdir "%STEAMCMD%" 2>nul
    powershell -NoProfile -ExecutionPolicy Bypass -Command ^
      "$ErrorActionPreference='Stop'; [Net.ServicePointManager]::SecurityProtocol='Tls12';" ^
      "Invoke-WebRequest 'https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip' -OutFile '%STEAMCMD%\steamcmd.zip';" ^
      "Expand-Archive '%STEAMCMD%\steamcmd.zip' '%STEAMCMD%' -Force; Remove-Item '%STEAMCMD%\steamcmd.zip'"
    if errorlevel 1 (
        echo FEHLER: Download von SteamCMD fehlgeschlagen. Internetverbindung pruefen.
        pause
        exit /b 1
    )
)

set "STEAMUSER="
set /p "STEAMUSER=Steam-Benutzername (Konto, dem das Spiel gehoert): "
if "%STEAMUSER%"=="" (
    echo Abgebrochen: kein Benutzername.
    pause
    exit /b 1
)

echo.
echo SteamCMD fragt jetzt nach Passwort und ggf. Steam-Guard-Code.
echo Hinweis: Ist dasselbe Konto gerade im Steam-Client angemeldet, kann Steam es dort abmelden.
echo          Danach im Steam-Client einfach wieder anmelden.
echo.
"%STEAMCMD%\steamcmd.exe" +@sSteamCmdForcePlatformType windows +force_install_dir "%GAMEDIR%" +login %STEAMUSER% +app_update 1708850 validate +quit
if not exist "%GAMEDIR%\DrugDealerSimulator2.exe" (
    echo.
    echo FEHLER: Das Spiel wurde nicht installiert. Meldungen von SteamCMD oben pruefen
    echo        ^(falsches Passwort, Steam Guard, Konto besitzt das Spiel nicht?^).
    pause
    exit /b 1
)

echo.
echo Fertig. Spiel liegt in: %GAMEDIR%
echo.
echo Naechste Schritte:
echo  1. DDS2Mod-Setup ausfuehren, Art "Dedicated Server" waehlen und als Spielordner
echo     "%GAMEDIR%" angeben ^(nur beim ersten Mal oder nach einem Mod-Update^).
echo  2. Steam-Client starten und anmelden.
echo  3. DDS2 Server starten.
echo.
echo Spiel-Updates: dieses Skript einfach erneut ausfuehren ^(Server vorher mit "stop" beenden^).
pause
