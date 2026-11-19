@echo off
chcp 65001 >nul
cd /d "%~dp0"
rem Догрузить share\MizuEngine.exe в уже созданный релиз его версии (если загрузка в release.bat оборвалась)
powershell -NoProfile -ExecutionPolicy Bypass -File tools\release.ps1 asset
pause
