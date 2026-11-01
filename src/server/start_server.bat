@echo off
rem Startet den DDS2-Server mit der server.cfg aus diesem Ordner.
rem Parameter werden durchgereicht, z. B.: start_server.bat --port 7778 --maxplayers 4
rem @DDS2MOD_EXE@ wird vom Installer durch den Pfad zur DDS2Mod.exe ersetzt.
cd /d "%~dp0"
start "" "@DDS2MOD_EXE@" --server --config "%~dp0server.cfg" %*
