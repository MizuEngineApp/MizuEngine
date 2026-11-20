@echo off
chcp 65001 >nul
rem Временный помощник Claude: режет файлы программы AJAZZ на части по 3 МБ в obj\ajazz1 (для изучения протокола заряда)
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
 "$src = Join-Path (Get-Item (Join-Path $env:LOCALAPPDATA 'Programs\AJAZZ Driver*') | Select-Object -First 1).FullName 'resources\app';" ^
 "$out = Join-Path '%~dp0' 'obj\ajazz1'; New-Item -ItemType Directory -Force -Path $out | Out-Null;" ^
 "foreach ($f in @('dist\static\js\main_ace02afb.js','iot_driver.exe')) {" ^
 "  $b = [IO.File]::ReadAllBytes((Join-Path $src $f)); $n = [IO.Path]::GetFileName($f); $size = 1MB; $i = 0;" ^
 "  for ($o = 0; $o -lt $b.Length; $o += $size) { $len = [Math]::Min($size, $b.Length - $o); $part = New-Object byte[] $len; [Array]::Copy($b, $o, $part, 0, $len); [IO.File]::WriteAllBytes((Join-Path $out ($n + '.part' + $i.ToString('00'))), $part); $i++ }" ^
 "}; Get-ChildItem $out | Format-Table Name, Length | Out-String | Set-Content (Join-Path $out 'list.txt')"
echo Done.
timeout /t 2 >nul
