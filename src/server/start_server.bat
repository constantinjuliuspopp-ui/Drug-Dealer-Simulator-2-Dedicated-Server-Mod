@echo off
rem Startet den DDS2 Dedicated Server mit der server.cfg aus diesem Ordner.
rem Weitere Parameter werden durchgereicht, z. B.:
rem   start_server.bat --port 7777 --maxplayers 2 --world "Welt1"
rem @DDS2SERVER_EXE@ wird vom Installer durch den Pfad zur DDS2Server.exe ersetzt.
chcp 65001 >nul
cd /d "%~dp0"
"@DDS2SERVER_EXE@" --config "%~dp0server.cfg" %*
if errorlevel 1 pause
