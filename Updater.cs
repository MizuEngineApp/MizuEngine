// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>
    /// Обновления через GitHub Releases: проверка при запуске, каждые 2 часа и по кнопке на странице «Обновления».
    /// Новая версия = последний релиз с тегом вида v0.10.1, который больше версии из MizuEngine.csproj.
    /// Если нашлась — главное окно показывает обязательную плашку, а трей присылает уведомление.
    /// На плашке два пути: «Обновить сейчас» (MizuEngine сам скачивает MizuEngine.exe из релиза,
    /// проверяет подпись rainy и перезапускается) или «Страница на GitHub» (браузер, всё вручную).
    /// Если интернета нет — плашка прячется и проверка повторяется через 5 минут.
    /// </summary>
    internal static class Updater
    {
        /// <summary>Репозиторий на GitHub в виде «ник/название». Пусто — проверка не выполняется.</summary>
        public const string Repo = "MizuEngineApp/MizuEngine";

        /// <summary>Имя файла программы в релизе.</summary>
        const string AssetName = "MizuEngine.exe";

        public enum State { Unknown, Checking, UpToDate, Available, Error }

        /// <summary>Чем закончилась установка.</summary>
        public enum InstallResult { Restarting, OpenedBrowser, Failed }

        public static State Status { get; private set; } = State.Unknown;
        /// <summary>Новая версия (только когда Status == Available).</summary>
        public static Version Latest { get; private set; }
        /// <summary>Страница релиза на GitHub, где лежит новый MizuEngine.exe.</summary>
        public static string ReleaseUrl { get; private set; }
        /// <summary>Прямая ссылка на MizuEngine.exe из релиза (null — файла в релизе нет).</summary>
        public static string AssetUrl { get; private set; }
        static long _assetSize;
        static string _assetSha256;
        static string _notes;
        public static DateTime LastCheck { get; private set; }
        /// <summary>Идёт скачивание и установка.</summary>
        public static bool Installing { get; private set; }

        public static Version Current { get; } =
            Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

        /// <summary>Статус поменялся (вызывается в потоке интерфейса).</summary>
        public static event Action Changed;

        static readonly TimeSpan Normal = TimeSpan.FromHours(2);
        static readonly TimeSpan Retry = TimeSpan.FromMinutes(5);
        static DispatcherTimer _timer;
        static HttpClient _http;
        static Task<State> _running;
        static bool _failedBefore;   // не забиваем error.log одной и той же ошибкой каждые 5 минут

        static string UpdateFolder => Path.Combine(Settings.Folder, "update");

        /// <summary>Запуск из Program: первая проверка через несколько секунд после старта, дальше по таймеру.</summary>
        public static void Start()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _timer.Tick += async (s, e) => await CheckAsync();
            _timer.Start();
            // Хвосты прошлого обновления (старый exe ещё мог закрываться — поэтому с задержкой)
            Task.Delay(TimeSpan.FromSeconds(12)).ContinueWith(_ => Cleanup());
        }

        public static string Short(Version v) => v == null ? "" : "v" + v.ToString(3);

        /// <summary>Проверить сейчас. Если проверка уже идёт — ждём её же, а не запускаем вторую.</summary>
        public static Task<State> CheckAsync()
        {
            if (Installing) return Task.FromResult(Status);
            if (_running != null && !_running.IsCompleted) return _running;
            _running = DoCheck();
            return _running;
        }

        static async Task<State> DoCheck()
        {
            _timer?.Stop();
            Set(State.Checking);
            State result;
            try
            {
                if (string.IsNullOrWhiteSpace(Repo)) throw new InvalidOperationException("Updater.Repo is empty");
                _http ??= CreateClient(TimeSpan.FromSeconds(15));
                using var resp = await _http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest");
                if (!resp.IsSuccessStatusCode) throw new HttpRequestException("GitHub: " + (int)resp.StatusCode);
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                var root = doc.RootElement;
                string tag = Str(root, "tag_name");
                string url = Str(root, "html_url");

                var v = Parse(tag);
                if (v != null && v > Normalize(Current))
                {
                    Latest = v;
                    ReleaseUrl = string.IsNullOrEmpty(url) ? $"https://github.com/{Repo}/releases/latest" : url;
                    _notes = Str(root, "body");
                    ReadAsset(root);
                    result = State.Available;
                }
                else
                {
                    Latest = null;
                    result = State.UpToDate;
                }
                LastCheck = DateTime.Now;
            }
            catch (Exception ex)
            {
                // Нет интернета, GitHub недоступен или репозиторий ещё не создан — плашку не показываем, пробуем позже
                if (!string.IsNullOrWhiteSpace(Repo) && !_failedBefore) Settings.Log(ex);
                result = State.Error;
            }

            _failedBefore = result == State.Error;
            Set(result);
            if (_timer != null)
            {
                _timer.Interval = result == State.Error ? Retry : Normal;
                _timer.Start();
            }
            return result;
        }

        /// <summary>MizuEngine.exe в релизе: ссылка, размер и SHA-256 (GitHub считает его сам).</summary>
        static void ReadAsset(JsonElement root)
        {
            AssetUrl = null; _assetSize = 0; _assetSha256 = null;
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return;
            JsonElement? pick = null;
            foreach (var a in assets.EnumerateArray())
            {
                string name = Str(a, "name") ?? "";
                if (name.Equals(AssetName, StringComparison.OrdinalIgnoreCase)) { pick = a; break; }
                if (pick == null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) pick = a;
            }
            if (pick is not JsonElement p) return;
            AssetUrl = Str(p, "browser_download_url");
            if (p.TryGetProperty("size", out var sz) && sz.TryGetInt64(out long n)) _assetSize = n;
            string digest = Str(p, "digest");   // «sha256:abc…»
            if (digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) _assetSha256 = digest.Substring(7).Trim().ToLowerInvariant();
        }

        /// <summary>«Что нового» из описания релиза: русская часть для русского языка, английская — для остальных.</summary>
        public static string Notes(string lang)
        {
            if (string.IsNullOrWhiteSpace(_notes)) return "";
            string text = _notes.Replace("\r", "");
            int foot = Regex.Match(text, @"^\s*---\s*$", RegexOptions.Multiline) is { Success: true } m ? m.Index : -1;
            if (foot >= 0) text = text.Substring(0, foot);   // ниже черты — SHA-256 и подпись, это для сайта

            var en = Regex.Match(text, @"^#+\s*What'?s new.*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            if (en.Success) text = lang == "ru" ? text.Substring(0, en.Index) : text.Substring(en.Index + en.Length);

            var sb = new StringBuilder();
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("<!--")) continue;
                line = Regex.Replace(line, @"^[-*]\s+", "• ");
                line = line.Replace("**", "").Replace("`", "");
                line = Regex.Replace(line, @"\[([^\]]+)\]\([^)]+\)", "$1");   // [текст](ссылка) → текст
                sb.AppendLine(line);
            }
            string res = sb.ToString().Trim();
            return res.Length > 1500 ? res.Substring(0, 1500).TrimEnd() + "…" : res;
        }

        /// <summary>Открыть страницу релиза в браузере. false — браузер не открылся.</summary>
        public static bool OpenRelease()
        {
            string url = ReleaseUrl ?? (string.IsNullOrWhiteSpace(Repo) ? null : $"https://github.com/{Repo}/releases/latest");
            return OpenUrl(url);
        }

        static bool OpenUrl(string url)
        {
            if (url == null) return false;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); return true; }
            catch (Exception ex) { Settings.Log(ex); return false; }
        }

        /// <summary>
        /// «Обновить сейчас»: скачать MizuEngine.exe из релиза, проверить размер, SHA-256 и подпись rainy,
        /// положить на место текущего exe и перезапуститься. Работающий exe нельзя перезаписать, но можно
        /// переименовать — старый становится «MizuEngine.exe.old» и удаляется при следующем запуске.
        /// Сборка для разработки (без подписи) и папка без прав на запись — скачивание через браузер.
        /// progress: (скачано байт, всего байт; 0 — неизвестно).
        /// </summary>
        public static async Task<InstallResult> InstallAsync(Action<long, long> progress, Action<string> stage)
        {
            if (Installing) return InstallResult.Failed;
            if (AssetUrl == null || !Guard.Official)
                return OpenUrl(AssetUrl ?? ReleaseUrl) ? InstallResult.OpenedBrowser : InstallResult.Failed;

            Installing = true;
            _timer?.Stop();
            string file = null;
            try
            {
                Directory.CreateDirectory(UpdateFolder);
                file = Path.Combine(UpdateFolder, $"MizuEngine-{Latest?.ToString(3)}.exe");
                string part = file + ".part";

                // ---- скачивание ----
                stage?.Invoke("download");
                using (var http = CreateClient(TimeSpan.FromMinutes(30)))
                using (var resp = await http.GetAsync(AssetUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    resp.EnsureSuccessStatusCode();
                    long total = resp.Content.Headers.ContentLength ?? _assetSize;
                    using var src = await resp.Content.ReadAsStreamAsync();
                    using var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true);
                    var buf = new byte[1 << 16];
                    long done = 0, lastReport = 0;
                    var sw = Stopwatch.StartNew();
                    int read;
                    while ((read = await src.ReadAsync(buf.AsMemory(0, buf.Length))) > 0)
                    {
                        await dst.WriteAsync(buf.AsMemory(0, read));
                        done += read;
                        if (sw.ElapsedMilliseconds - lastReport > 80) { lastReport = sw.ElapsedMilliseconds; progress?.Invoke(done, total); }
                    }
                    progress?.Invoke(done, total);
                }
                if (File.Exists(file)) File.Delete(file);
                File.Move(part, file);

                // ---- проверка: файл целиком, не подменён, подписан rainy ----
                stage?.Invoke("verify");
                await Task.Run(() =>
                {
                    var info = new FileInfo(file);
                    if (_assetSize > 0 && info.Length != _assetSize) throw new InvalidDataException("update: size mismatch");
                    if (_assetSha256 != null)
                    {
                        using var fs = File.OpenRead(file);
                        string hash = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
                        if (hash != _assetSha256) throw new InvalidDataException("update: sha256 mismatch");
                    }
                    if (!Guard.IsGenuine(file)) throw new InvalidDataException("update: signature is not rainy's");
                });

                // ---- замена exe ----
                stage?.Invoke("restart");
                string exe = Environment.ProcessPath;
                string old = exe + ".old";
                try { if (File.Exists(old)) File.Delete(old); }
                catch { old = exe + "." + DateTime.Now.Ticks + ".old"; }   // прошлый «.old» ещё занят
                try
                {
                    File.Move(exe, old);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
                {
                    // Папка только для чтения (например, Program Files) — пусть браузер скачает файл
                    Settings.Log(ex);
                    Installing = false;
                    return OpenUrl(AssetUrl) ? InstallResult.OpenedBrowser : InstallResult.Failed;
                }
                try { File.Copy(file, exe, false); }
                catch { try { File.Move(old, exe); } catch { } throw; }

                Process.Start(new ProcessStartInfo(exe, "--updated") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) });
                return InstallResult.Restarting;
            }
            catch (Exception ex)
            {
                Settings.Log(ex);
                try { if (file != null) { File.Delete(file + ".part"); File.Delete(file); } } catch { }
                Installing = false;
                if (_timer != null) { _timer.Interval = Normal; _timer.Start(); }
                return InstallResult.Failed;
            }
        }

        /// <summary>Удалить старый exe (MizuEngine.exe.old) и скачанные файлы обновления.</summary>
        public static void Cleanup()
        {
            try
            {
                string exe = Environment.ProcessPath;
                string dir = Path.GetDirectoryName(exe), name = Path.GetFileName(exe);
                foreach (var f in Directory.GetFiles(dir, name + "*.old"))
                    try { File.Delete(f); } catch { }
            }
            catch { }
            try { if (Directory.Exists(UpdateFolder)) Directory.Delete(UpdateFolder, true); } catch { }
        }

        /// <summary>Браузер не открылся — прячем плашку до следующей проверки (через 5 минут).</summary>
        public static void Postpone()
        {
            Set(State.Error);
            if (_timer != null) { _timer.Stop(); _timer.Interval = Retry; _timer.Start(); }
        }

        static void Set(State s)
        {
            Status = s;
            Changed?.Invoke();
        }

        static string Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        static HttpClient CreateClient(TimeSpan timeout)
        {
            var c = new HttpClient { Timeout = timeout };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("MizuEngine/" + Current.ToString(3));   // без него GitHub отвечает 403
            c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return c;
        }

        /// <summary>«v0.8.0», «0.8», «V1.2.3-beta» → версия из трёх чисел.</summary>
        static Version Parse(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            string s = tag.Trim().TrimStart('v', 'V');
            int cut = s.IndexOfAny(new[] { '-', '+', ' ' });
            if (cut > 0) s = s.Substring(0, cut);
            return Version.TryParse(s, out var v) ? Normalize(v) : null;
        }

        static Version Normalize(Version v) => new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }
}
