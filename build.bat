@echo off
chcp 65001 >nul
cd /d "%~dp0"
set DOTNET=dotnet
if not exist obj mkdir obj
if exist "%ProgramFiles%\dotnet\dotnet.exe" set DOTNET="%ProgramFiles%\dotnet\dotnet.exe"
rem Ask a running copy to close properly (it brings the Windows taskbar back), force-kill only if it hangs
if exist "out\MizuEngine.exe" start "" "out\MizuEngine.exe" --quit
timeout /t 2 /nobreak >nul
taskkill /im MizuEngine.exe /f >nul 2>&1
echo Building MizuEngine... (log: obj\build.log)
%DOTNET% publish MizuEngine.csproj -c Release -o out > obj\build.log 2>&1
if errorlevel 1 (
  type obj\build.log
  echo.
  echo BUILD FAILED. Tell Claude, it will read obj\build.log.
  pause
  exit /b 1
)
echo Done! Starting out\MizuEngine.exe
start "" "out\MizuEngine.exe"
