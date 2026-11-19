# MizuEngine © 2026 rainy. Выкладка новой версии на GitHub одной командой (release.bat).
#
# Что делает:
#   1. Берёт версию из MizuEngine.csproj (например 0.10.1) и share\MizuEngine.exe (собирается build-share.bat).
#   2. Проверяет, что exe этой версии и подписан rainy.
#   3. Заливает код в репозиторий (без закрытых папок: Server, Security, signing и т. д.) — только изменённые файлы.
#   4. Создаёт релиз vX.Y.Z с описанием из release-notes\vX.Y.Z.md, добавляет SHA-256 и подпись.
#   5. Загружает MizuEngine.exe в релиз. Приложения у всех увидят обновление в течение 2 часов (или сразу по стрелке).
#
# Токен GitHub (fine-grained, доступ только к MizuEngineApp/MizuEngine, Contents: Read and write) спрашивается
# один раз и хранится зашифрованным (DPAPI) в %LocalAppData%\MizuEngine-release\github.token — открыть его можно
# только под твоей учётной записью Windows. «release.bat token» — ввести токен заново.
# Работает в обычном Windows PowerShell 5.1.
# «release.bat asset» (или release-asset.bat) — только догрузить share\MizuEngine.exe в уже созданный релиз
# его версии (версия берётся из самого exe, код и описание не трогаются). Нужно, если загрузка файла оборвалась.

param([string]$Mode = "")

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$Repo      = "MizuEngineApp/MizuEngine"
$AssetName = "MizuEngine.exe"
$Root      = Split-Path -Parent $PSScriptRoot
$Api       = "https://api.github.com"

# Что НЕ выкладываем (то же, что в .gitignore, плюс служебное)
$SkipDirs  = @("bin", "obj", "out", "share", "Server", "Security", "signing", ".git", ".vs", ".idea", "packages")
$SkipFiles = @("MizuEngine_context.md", "obfuscar.xml")
$SkipExt   = @(".pfx", ".cer", ".user", ".suo", ".log", ".token", ".tmp")

function Say($text, $color = "Gray") { Write-Host $text -ForegroundColor $color }
function Fail($text) { Write-Host ""; Write-Host $text -ForegroundColor Red; exit 1 }

# ---------- токен ----------
$TokenDir  = Join-Path $env:LOCALAPPDATA "MizuEngine-release"
$TokenFile = Join-Path $TokenDir "github.token"

function Read-Token {
    Say ""
    Say "Нужен токен GitHub (его создаёт Claude во встроенном браузере, ты нажимаешь Generate и копируешь)." "Cyan"
    $sec = Read-Host "Вставь токен (Ctrl+V, символы не видны) и нажми Enter" -AsSecureString
    if ($sec.Length -lt 20) { Fail "Токен слишком короткий." }
    New-Item -ItemType Directory -Force -Path $TokenDir | Out-Null
    $sec | ConvertFrom-SecureString | Set-Content -Path $TokenFile -Encoding ASCII
    return $sec
}

function Get-PlainToken {
    if ($Mode -eq "token" -or -not (Test-Path $TokenFile)) { $sec = Read-Token }
    else {
        try { $sec = (Get-Content $TokenFile -Raw).Trim() | ConvertTo-SecureString } catch { $sec = Read-Token }
    }
    $b = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($b) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b) }
}

$script:Headers = $null
function Set-Auth($tok) {
    $script:Headers = @{
        "Authorization"        = "Bearer $tok"
        "Accept"               = "application/vnd.github+json"
        "X-GitHub-Api-Version" = "2022-11-28"
        "User-Agent"           = "MizuEngine-release"
    }
}

function Gh($method, $path, $body = $null) {
    $uri = if ($path.StartsWith("http")) { $path } else { "$Api$path" }
    $p = @{ Method = $method; Uri = $uri; Headers = $script:Headers; TimeoutSec = 300 }
    if ($body -ne $null) {
        $json = if ($body -is [string]) { $body } else { $body | ConvertTo-Json -Depth 20 -Compress }
        $p.Body = [Text.Encoding]::UTF8.GetBytes($json)
        $p.ContentType = "application/json; charset=utf-8"
    }
    return Invoke-RestMethod @p
}

function Status-Of($err) {
    try { return [int]$err.Exception.Response.StatusCode } catch { return 0 }
}

# ---------- версия и exe ----------
[xml]$proj = Get-Content (Join-Path $Root "MizuEngine.csproj") -Encoding UTF8
$Version = ($proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
if (-not $Version) { Fail "Не нашёл <Version> в MizuEngine.csproj." }
$Exe = Join-Path $Root "share\$AssetName"
$AssetOnly = ($Mode -eq "asset")
if ($AssetOnly -and (Test-Path $Exe)) {
    $fv = (Get-Item $Exe).VersionInfo.FileVersion
    if ($fv) { $Version = ($fv.Split('.')[0..2] -join '.') }
}
$Tag = "v$Version"

Say "MizuEngine $Tag → github.com/$Repo" "Cyan"
if (-not (Test-Path $Exe)) { Fail "Нет share\$AssetName. Сначала запусти build-share.bat." }

$fileVer = (Get-Item $Exe).VersionInfo.FileVersion
if (-not $fileVer -or -not $fileVer.StartsWith($Version)) {
    Fail "share\$AssetName собран как версия $fileVer, а в MizuEngine.csproj $Version. Запусти build-share.bat ещё раз."
}

$sig = Get-AuthenticodeSignature $Exe
if ($sig.SignerCertificate -eq $null -or $sig.Status -eq "NotSigned" -or $sig.Status -eq "HashMismatch") {
    if (-not (Test-Path (Join-Path $Root "signing\thumbprint.txt"))) {
        Fail "share\$AssetName не подписан: на этом ПК ещё нет сертификата rainy. Запусти make-cert.bat (один раз), потом build-share.bat, потом снова release.bat."
    }
    Fail "share\$AssetName не подписан (или подпись сломана: $($sig.Status)). Запусти build-share.bat ещё раз и посмотри, что он пишет про подпись."
}
if ($sig.SignerCertificate.Subject -notmatch "CN=rainy") { Fail "Файл подписан не rainy: $($sig.SignerCertificate.Subject)" }
$Thumb = $sig.SignerCertificate.Thumbprint
$ExeSha256 = (Get-FileHash $Exe -Algorithm SHA256).Hash.ToLower()   # не $Sha: PowerShell не различает регистр, $sha ниже — это git-блобы
$SizeMb = [math]::Round((Get-Item $Exe).Length / 1MB, 1)
Say "Файл: $SizeMb МБ, подпись rainy ($Thumb), SHA-256 $ExeSha256"

$NotesFile = Join-Path $Root "release-notes\$Tag.md"
if (-not (Test-Path $NotesFile)) { Fail "Нет описания release-notes\$Tag.md. Попроси Claude написать его." }
$Notes = [IO.File]::ReadAllText($NotesFile, [Text.Encoding]::UTF8).Trim()

# ---------- вход ----------
Set-Auth (Get-PlainToken)
try { $repoInfo = Gh GET "/repos/$Repo" }
catch {
    $code = Status-Of $_
    if ($code -eq 401) { Remove-Item $TokenFile -ErrorAction SilentlyContinue; Fail "GitHub не принял токен. Запусти release.bat ещё раз и вставь новый." }
    Fail "Не открылся репозиторий $Repo ($code): $($_.Exception.Message)"
}
$Branch = $repoInfo.default_branch

if ($AssetOnly) {
    Say ""
    Say "Только файл: догружаю $AssetName в релиз $Tag (код и описание не трогаю)." "Cyan"
    try { $release = Gh GET "/repos/$Repo/releases/tags/$Tag" }
    catch { Fail "Релиза $Tag на GitHub нет — запусти обычный release.bat." }
} else {
# ---------- 1. код ----------
Say ""
Say "1/3  Код → ветка $Branch" "Cyan"

function Git-Sha([byte[]]$bytes) {
    $head = [Text.Encoding]::ASCII.GetBytes("blob $($bytes.Length)`0")
    $all = New-Object byte[] ($head.Length + $bytes.Length)
    [Buffer]::BlockCopy($head, 0, $all, 0, $head.Length)
    [Buffer]::BlockCopy($bytes, 0, $all, $head.Length, $bytes.Length)
    $sha1 = [Security.Cryptography.SHA1]::Create()
    return (-join ($sha1.ComputeHash($all) | ForEach-Object { $_.ToString("x2") }))
}

$files = Get-ChildItem -Path $Root -Recurse -File -Force | Where-Object {
    $rel = $_.FullName.Substring($Root.Length + 1)
    $top = $rel.Split('\')[0]
    -not ($SkipDirs -contains $top) -and -not ($SkipFiles -contains $_.Name) -and -not ($SkipExt -contains $_.Extension.ToLower())
}

$ref = Gh GET "/repos/$Repo/git/ref/heads/$Branch"
$headSha = $ref.object.sha
$headCommit = Gh GET "/repos/$Repo/git/commits/$headSha"
$remote = @{}
$tree = Gh GET "/repos/$Repo/git/trees/$($headCommit.tree.sha)?recursive=1"
foreach ($t in $tree.tree) { if ($t.type -eq "blob") { $remote[$t.path] = $t.sha } }

$entries = New-Object System.Collections.ArrayList
$uploaded = 0
foreach ($f in $files) {
    $path = $f.FullName.Substring($Root.Length + 1).Replace('\', '/')
    $bytes = [IO.File]::ReadAllBytes($f.FullName)
    $sha = Git-Sha $bytes
    if ($remote[$path] -ne $sha) {
        $blob = Gh POST "/repos/$Repo/git/blobs" ('{"encoding":"base64","content":"' + [Convert]::ToBase64String($bytes) + '"}')
        $sha = $blob.sha
        $uploaded++
        Say "   + $path"
    }
    [void]$entries.Add(@{ path = $path; mode = "100644"; type = "blob"; sha = $sha })
}

$newTree = Gh POST "/repos/$Repo/git/trees" @{ tree = $entries }
if ($newTree.sha -eq $headCommit.tree.sha) {
    Say "   Код не изменился — коммит не нужен."
} else {
    $commit = Gh POST "/repos/$Repo/git/commits" @{ message = "MizuEngine $Tag"; tree = $newTree.sha; parents = @($headSha) }
    Gh PATCH "/repos/$Repo/git/refs/heads/$Branch" @{ sha = $commit.sha; force = $false } | Out-Null
    Say "   Готово: $uploaded файл(ов) обновлено, файлов всего $($entries.Count)." "Green"
}

# ---------- 2. релиз ----------
Say ""
Say "2/3  Релиз $Tag" "Cyan"
$footer = @"


---

**$AssetName** · $SizeMb MB · SHA-256: ``$ExeSha256``

Подписан сертификатом **rainy** (``CN=rainy, O=MizuEngine``), отпечаток ``$Thumb``.
Signed by **rainy**. Как проверить, что файл настоящий / How to verify: [README](https://github.com/$Repo#-это-не-вирус-как-проверить).

Windows может показать «Windows защитила ваш компьютер» — нажми «Подробнее» → «Выполнить в любом случае». Если MizuEngine уже стоит, просто нажми «Обновить сейчас» в программе.
"@
$body = @{ tag_name = $Tag; target_commitish = $Branch; name = "MizuEngine $Tag"; body = ($Notes + $footer); draft = $false; prerelease = $false; make_latest = "true" }

$release = $null
try { $release = Gh GET "/repos/$Repo/releases/tags/$Tag" } catch { if ((Status-Of $_) -ne 404) { throw } }
if ($release) {
    $release = Gh PATCH "/repos/$Repo/releases/$($release.id)" @{ name = $body.name; body = $body.body; draft = $false; prerelease = $false; make_latest = "true" }
    Say "   Релиз уже был — обновил описание."
} else {
    $release = Gh POST "/repos/$Repo/releases" $body
    Say "   Создан." "Green"
}
}

# ---------- 3. файл ----------
Say ""
Say "3/3  Загружаю $AssetName ($SizeMb МБ) — может занять несколько минут…" "Cyan"
$asset = $null
foreach ($a in $release.assets) {
    if ($a.name -ne $AssetName) { continue }
    if ($a.size -eq (Get-Item $Exe).Length -and $a.digest -eq "sha256:$ExeSha256") { $asset = $a; Say "   Этот же файл уже в релизе — загружать заново не нужно." "Green" }
    else { Gh DELETE "/repos/$Repo/releases/assets/$($a.id)" | Out-Null; Say "   Старый файл в релизе удалён." }
}
if (-not $asset) {
    $upUri = "https://uploads.github.com/repos/$Repo/releases/$($release.id)/assets?name=$AssetName"
    # Большой файл через VPN иногда обрывается — до 4 попыток; после обрыва GitHub может оставить
    # недогруженный файл с тем же именем, его удаляем перед новой попыткой.
    for ($try = 1; $try -le 4 -and -not $asset; $try++) {
        try {
            $asset = Invoke-RestMethod -Method POST -Uri $upUri -Headers $script:Headers -InFile $Exe -ContentType "application/octet-stream" -TimeoutSec 3600
        } catch {
            Say "   Попытка $try не удалась: $($_.Exception.Message)" "Yellow"
            Start-Sleep -Seconds (5 * $try)
            try {
                $fresh = Gh GET "/repos/$Repo/releases/$($release.id)"
                foreach ($a in $fresh.assets) {
                    if ($a.name -ne $AssetName) { continue }
                    if ($a.state -eq "uploaded" -and $a.size -eq (Get-Item $Exe).Length -and $a.digest -eq "sha256:$ExeSha256") { $asset = $a; Say "   Файл всё-таки дошёл целиком." "Green" }
                    else { Gh DELETE "/repos/$Repo/releases/assets/$($a.id)" | Out-Null }
                }
            } catch { }
            if (-not $asset -and $try -eq 4) { Fail "Не получилось загрузить $AssetName (4 попытки). Проверь интернет/VPN и запусти release-asset.bat." }
        }
    }
}
if ($asset.size -ne (Get-Item $Exe).Length) { Fail "GitHub принял файл не целиком ($($asset.size) байт). Запусти release.bat ещё раз." }
if ($asset.digest -and $asset.digest -ne "sha256:$ExeSha256") { Fail "SHA-256 на GitHub ($($asset.digest)) не совпал. Запусти release.bat ещё раз." }

# ---------- проверка: так же, как спрашивает приложение ----------
Start-Sleep -Seconds 2
try {
    $latest = Gh GET "/repos/$Repo/releases/latest"
    if ($latest.tag_name -ne $Tag) { Say "Внимание: последним релизом GitHub пока считает $($latest.tag_name)." "Yellow" }
} catch { Say "Не получилось перепроверить последний релиз (это не страшно): $($_.Exception.Message)" "Yellow" }

Say ""
Say "Готово! MizuEngine $Tag выложен: $($release.html_url)" "Green"
Say "Приложения с проверкой обновлений увидят его при запуске или в течение 2 часов."
Start-Process $release.html_url
