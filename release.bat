@echo off
chcp 65001 >nul
cd /d "%~dp0"
rem Выкладка новой версии на GitHub: код + релиз + MizuEngine.exe (tools\release.ps1)
rem Перед этим: поднять <Version> в MizuEngine.csproj, написать release-notes\vX.Y.Z.md, запустить build-share.bat
powershell -NoProfile -ExecutionPolicy Bypass -File tools\release.ps1 %*
pause
