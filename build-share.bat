@echo off
chcp 65001 >nul
cd /d "%~dp0"
set DOTNET=dotnet
if not exist obj mkdir obj
if exist "%ProgramFiles%\dotnet\dotnet.exe" set DOTNET="%ProgramFiles%\dotnet\dotnet.exe"
set COMPRESS=false
if /i "%~1"=="compress" set COMPRESS=true
echo Building ONE-FILE protected MizuEngine.exe for friends (compress=%COMPRESS%)...
echo First run downloads packages from NuGet, please wait. Log: obj\share.log
rem Close a running MizuEngine first: share\MizuEngine.exe cannot be replaced while it runs
if exist "share\MizuEngine.exe" start "" "share\MizuEngine.exe" --quit
if exist "out\MizuEngine.exe" start "" "out\MizuEngine.exe" --quit
timeout /t 3 /nobreak >nul
taskkill /im MizuEngine.exe /f >nul 2>&1
timeout /t 1 /nobreak >nul
if exist share rmdir /s /q share
if exist share (
  echo.
  echo share\MizuEngine.exe is still in use. Close MizuEngine ^(tray icon - Exit^) and run build-share.bat again.
  pause
  exit /b 1
)
rem Clean compile: code obfuscation must run on a fresh build
if exist obj\Release rmdir /s /q obj\Release
set ARGS=MizuEngine.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=%COMPRESS% -p:DebugType=none -p:DebugSymbols=false -p:MizuRelease=true -o share
%DOTNET% publish %ARGS% -p:MizuObfuscate=true > obj\share.log 2>&1
if errorlevel 1 goto plain
goto sign

:plain
echo.
echo Code obfuscation step failed - building without it. Tell Claude, it will read obj\share.log.
if exist obj\Release rmdir /s /q obj\Release
if exist share rmdir /s /q share
%DOTNET% publish %ARGS% > obj\share-plain.log 2>&1
if errorlevel 1 (
  type obj\share-plain.log
  echo.
  echo BUILD FAILED. Tell Claude, it will read obj\share-plain.log.
  pause
  exit /b 1
)

:sign
if not exist signing\thumbprint.txt (
  echo.
  echo No certificate yet: run make-cert.bat once, then builds are signed with your name - rainy.
  goto done
)
echo Signing with rainy's certificate...
powershell -NoProfile -ExecutionPolicy Bypass -File signing\sign.ps1 "share\MizuEngine.exe"
if errorlevel 1 (
  echo.
  echo SIGNING FAILED - do NOT send this file, it will refuse to start. Tell Claude.
  pause
  exit /b 1
)

:done
echo.
echo Done! Your file: %~dp0share\MizuEngine.exe
powershell -NoProfile -Command "'Size: ' + [math]::Round((Get-Item 'share\MizuEngine.exe').Length/1MB) + ' MB'"
explorer share
pause
