@echo off
cd /d "%~dp0"
rem Run ONCE: creates rainy's code-signing certificate. After that build-share.bat signs MizuEngine.exe.
powershell -NoProfile -ExecutionPolicy Bypass -File signing\make-cert.ps1
pause
