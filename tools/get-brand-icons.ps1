# MizuEngine: скачивает настоящие значки приложений для ссылок в профиле в Assets\Brands (один раз, потом build.bat).
# Берём значок с сайта самого приложения (через icon.horse и Google), оставляем самый крупный настоящий PNG.
$ErrorActionPreference = 'Continue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'Assets\Brands'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$brands = [ordered]@{
  telegram = @('https://telegram.org/img/apple-touch-icon.png', 'https://icon.horse/icon/telegram.org', 'https://www.google.com/s2/favicons?domain=telegram.org&sz=256')
  discord  = @('https://icon.horse/icon/discord.com', 'https://www.google.com/s2/favicons?domain=discord.com&sz=256')
  youtube  = @('https://www.youtube.com/s/desktop/favicon_144x144.png', 'https://icon.horse/icon/youtube.com', 'https://www.google.com/s2/favicons?domain=youtube.com&sz=256')
  tiktok   = @('https://icon.horse/icon/tiktok.com', 'https://www.google.com/s2/favicons?domain=tiktok.com&sz=256')
  spotify  = @('https://icon.horse/icon/spotify.com', 'https://www.google.com/s2/favicons?domain=open.spotify.com&sz=256')
}
Add-Type -AssemblyName System.Drawing
foreach ($name in $brands.Keys) {
  $best = $null; $bestSize = 0
  foreach ($url in $brands[$name]) {
    try {
      $tmp = Join-Path $env:TEMP ("mizu-brand-" + [guid]::NewGuid().ToString('N'))
      Invoke-WebRequest -Uri $url -OutFile $tmp -UseBasicParsing -TimeoutSec 20 -Headers @{ 'User-Agent' = 'Mozilla/5.0' }
      $img = [System.Drawing.Image]::FromFile($tmp)
      $w = $img.Width
      $img.Dispose()
      Write-Host ("  {0}: {1} -> {2}px" -f $name, $url, $w)
      if ($w -gt $bestSize) { if ($best) { Remove-Item $best -Force }; $best = $tmp; $bestSize = $w } else { Remove-Item $tmp -Force }
    } catch { Write-Host ("  {0}: {1} -> ошибка: {2}" -f $name, $url, $_.Exception.Message) }
  }
  if ($best) {
    # Сохраняем как PNG (если пришёл ICO/JPEG — конвертируем)
    $img = [System.Drawing.Image]::FromFile($best)
    $dst = Join-Path $out ($name + '.png')
    $img.Save($dst, [System.Drawing.Imaging.ImageFormat]::Png)
    $img.Dispose()
    Remove-Item $best -Force
    Write-Host ("OK {0}.png ({1}px)" -f $name, $bestSize)
  } else { Write-Host ("НЕТ {0} — будет круг с буквой" -f $name) }
}
Write-Host ''
Write-Host 'Готово. Теперь можно запускать build.bat'
