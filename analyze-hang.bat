@echo off
chcp 65001 >nul
cd /d "%~dp0"
rem Разбор снимка зависания: какой код стоял в каждом потоке MizuEngine, когда окно «не отвечало».
rem Снимок (hang.dmp) сам пишет сторож, если окно не отвечает 6+ секунд. Результат читает Claude: obj\hang-stacks.txt
set DUMP=%APPDATA%\MizuEngine\hang.dmp
if not exist "%DUMP%" (
  echo Снимка зависания нет: %DUMP%
  echo Значит, с последнего обновления окно не зависало.
  pause
  exit /b 0
)
if not exist obj mkdir obj
set DOTNET=dotnet
if exist "%ProgramFiles%\dotnet\dotnet.exe" set DOTNET="%ProgramFiles%\dotnet\dotnet.exe"
echo Устанавливаю dotnet-dump (один раз)...
%DOTNET% tool install -g dotnet-dump >nul 2>&1 || %DOTNET% tool update -g dotnet-dump >nul 2>&1
set DD=%USERPROFILE%\.dotnet\tools\dotnet-dump.exe
if not exist "%DD%" (
  echo Не получилось установить dotnet-dump. Скажи Claude.
  pause
  exit /b 1
)
echo Разбираю снимок (может занять минуту)...
"%DD%" analyze "%DUMP%" -c "setsymbolserver -ms" -c "clrthreads" -c "clrstack -all" -c "exit" > obj\hang-stacks.txt 2>&1
copy /y "%APPDATA%\MizuEngine\error.log" obj\hang-error.log >nul 2>&1
echo.
echo Готово: obj\hang-stacks.txt — скажи Claude, он прочитает.
pause
