@echo off
rem Startet DDS2Relay mit relay.cfg aus diesem Ordner (Windows).
chcp 65001 >nul
cd /d "%~dp0"
if not exist relay.cfg copy relay.cfg.example relay.cfg >nul
DDS2Relay.exe --config "%~dp0relay.cfg" %*
if errorlevel 1 pause
