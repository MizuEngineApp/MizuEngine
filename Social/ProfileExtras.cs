// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace MizuEngine
{
    /// <summary>Ссылка в профиле (как «подключения» в Discord): Telegram-канал, Discord-сервер, YouTube, TikTok, Spotify.</summary>
    internal sealed class ProfileLink
    {
        public string Kind, Url;
    }

    /// <summary>Ссылки в профиле: проверка (как на сервере, v08.sql), приведение к https-адресу, значки приложений.</summary>
    internal static class ProfileLinks
    {
        public static readonly string[] Kinds = { "telegram", "discord", "youtube", "tiktok", "spotify" };

        /// <summary>Обычным — до 2 ссылок, с Mizu Plus (и сотрудникам) — до 6.</summary>
        public static int Limit(Profile p) => p != null && (p.IsPlus || p.CanPost) ? 6 : 2;

        public static string Name(string kind) => kind switch
        {
            "telegram" => "Telegram",
            "discord" => "Discord",
            "youtube" => "YouTube",
            "tiktok" => "TikTok",
            "spotify" => "Spotify",
            _ => kind
        };

        /// <summary>Пример, что вводить (подсказка в поле).</summary>
        public static string Example(string kind) => kind switch
        {
            "telegram" => "t.me/MizuEngine",
            "discord" => "discord.gg/invite",
            "youtube" => "youtube.com/@channel",
            "tiktok" => "tiktok.com/@name",
            "spotify" => "open.spotify.com/artist/…",
            _ => ""
        };

        static Color Brand(string kind) => kind switch
        {
            "telegram" => Color.FromRgb(0x27, 0xA7, 0xE7),
            "discord" => Color.FromRgb(0x58, 0x65, 0xF2),
            "youtube" => Color.FromRgb(0xFF, 0x00, 0x33),
            "tiktok" => Color.FromRgb(0x10, 0x10, 0x14),
            "spotify" => Color.FromRgb(0x1D, 0xB9, 0x54),
            _ => Color.FromRgb(0x66, 0x77, 0x88)
        };

        static readonly Dictionary<string, Regex> Rules = new Dictionary<string, Regex>
        {
            ["telegram"] = new Regex(@"^https://(t\.me|telegram\.me)/[A-Za-z0-9_+]{2,}(/[0-9]+)?/?$", RegexOptions.IgnoreCase),
            ["discord"] = new Regex(@"^https://(discord\.gg|discord\.com/invite)/[A-Za-z0-9-]{2,}/?$", RegexOptions.IgnoreCase),
            ["youtube"] = new Regex(@"^https://((www|m)\.)?youtube\.com/(@[A-Za-z0-9_.-]{2,}|channel/[A-Za-z0-9_-]{10,}|c/[A-Za-z0-9_.-]{2,}|user/[A-Za-z0-9_.-]{2,})/?$", RegexOptions.IgnoreCase),
            ["tiktok"] = new Regex(@"^https://(www\.)?tiktok\.com/@[A-Za-z0-9_.]{2,}/?$", RegexOptions.IgnoreCase),
            ["spotify"] = new Regex(@"^https://open\.spotify\.com/(intl-[a-z]{2}/)?(user|artist|playlist|album|show)/[A-Za-z0-9_.-]{2,}/?$", RegexOptions.IgnoreCase),
        };

        public static bool Valid(string kind, string url) =>
            url != null && url.Length <= 200 && Rules.TryGetValue(kind ?? "", out var r) && r.IsMatch(url);

        /// <summary>
        /// Что ввёл человек → правильная ссылка или null. Понимает «@name», «t.me/name», «http://…», хвосты «?si=…».
        /// </summary>
        public static string Normalize(string kind, string input)
        {
            string s = (input ?? "").Trim();
            if (s.Length == 0) return null;
            int cut = s.IndexOfAny(new[] { '?', '#' });
            if (cut >= 0) s = s.Substring(0, cut);
            s = s.TrimEnd('/');
            if (s.StartsWith("@"))
            {
                string name = s.Substring(1);
                s = kind switch
                {
                    "telegram" => "https://t.me/" + name,
                    "tiktok" => "https://www.tiktok.com/@" + name,
                    "youtube" => "https://www.youtube.com/@" + name,
                    _ => s
                };
            }
            if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) s = "https://" + s.Substring(7);
            if (!s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) s = "https://" + s;
            // Канал Telegram без адреса сайта: «MizuEngine» → https://t.me/MizuEngine; инвайт Discord: «abc123» → discord.gg/abc123
            if (kind == "telegram" && Regex.IsMatch(input.Trim(), @"^[A-Za-z0-9_]{4,}$")) s = "https://t.me/" + input.Trim();
            if (kind == "discord" && Regex.IsMatch(input.Trim(), @"^[A-Za-z0-9-]{4,}$")) s = "https://discord.gg/" + input.Trim();
            return Valid(kind, s) ? s : null;
        }

        /// <summary>Какое приложение по адресу (если человек вставил ссылку, не выбрав приложение).</summary>
        public static string Guess(string input)
        {
            string s = (input ?? "").ToLowerInvariant();
            if (s.Contains("t.me/") || s.Contains("telegram.me/")) return "telegram";
            if (s.Contains("discord.gg/") || s.Contains("discord.com/invite")) return "discord";
            if (s.Contains("youtube.com/") || s.Contains("youtu.be/")) return "youtube";
            if (s.Contains("tiktok.com/")) return "tiktok";
            if (s.Contains("spotify.com/")) return "spotify";
            return null;
        }

        /// <summary>Короткий адрес для подсказки: t.me/MizuEngine.</summary>
        public static string Short(string url) => (url ?? "").Replace("https://", "").Replace("www.", "").TrimEnd('/');

        public static List<ProfileLink> Parse(JsonNode j)
        {
            var list = new List<ProfileLink>();
            if (j is not JsonArray arr) return list;
            foreach (var n in arr)
            {
                string kind = (string)n?["kind"], url = (string)n?["url"];
                if (Kinds.Contains(kind) && Valid(kind, url)) list.Add(new ProfileLink { Kind = kind, Url = url });
            }
            return list;
        }

        public static JsonArray ToJson(IEnumerable<ProfileLink> links)
        {
            var arr = new JsonArray();
            foreach (var l in links) arr.Add(new JsonObject { ["kind"] = l.Kind, ["url"] = l.Url });
            return arr;
        }

        public static void Open(string url)
        {
            if (string.IsNullOrEmpty(url) || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception e) { Settings.Log(e); }
        }

        // ================== значки ==================

        static readonly Dictionary<string, ImageSource> _icons = new Dictionary<string, ImageSource>();

        /// <summary>
        /// Настоящий значок приложения (Assets\Brands\&lt;kind&gt;.png, скачан с сайта самого приложения скриптом get-brand-icons.bat).
        /// Файла нет — null, тогда рисуется круг цвета бренда с буквой.
        /// </summary>
        public static ImageSource Icon(string kind)
        {
            if (_icons.TryGetValue(kind, out var img)) return img;
            try
            {
                var b = new BitmapImage();
                b.BeginInit();
                b.UriSource = new Uri($"pack://application:,,,/Assets/Brands/{kind}.png");
                b.CacheOption = BitmapCacheOption.OnLoad;
                b.DecodePixelWidth = 96;
                b.EndInit();
                b.Freeze();
                img = b;
            }
            catch { img = null; }
            _icons[kind] = img;
            return img;
        }

        /// <summary>Круглая кнопка-значок приложения.</summary>
        public static FrameworkElement Bubble(string kind, double size)
        {
            var g = new Grid { Width = size, Height = size };
            var icon = Icon(kind);
            if (icon != null)
            {
                g.Children.Add(new Ellipse { Fill = new SolidColorBrush(Color.FromArgb(0x26, 0, 0, 0)), Margin = new Thickness(-1) });
                g.Children.Add(new Ellipse { Fill = new ImageBrush(icon) { Stretch = Stretch.UniformToFill } });
            }
            else
            {
                g.Children.Add(new Ellipse { Fill = new SolidColorBrush(Brand(kind)) });
                g.Children.Add(new TextBlock
                {
                    Text = Name(kind).Substring(0, 1), Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = size * 0.46,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                });
            }
            g.Children.Add(new Ellipse { Stroke = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)), StrokeThickness = 1 });
            return g;
        }

        /// <summary>Ряд значков под описанием профиля: наведение — название и адрес, клик — открыть.</summary>
        public static FrameworkElement Row(List<ProfileLink> links)
        {
            var wp = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
            foreach (var l in links)
            {
                var link = l;
                var b = Bubble(link.Kind, 34);
                b.Margin = new Thickness(5, 0, 5, 0);
                b.Cursor = Cursors.Hand;
                b.ToolTip = Name(link.Kind) + "  ·  " + Short(link.Url);
                var st = new ScaleTransform(1, 1);
                b.RenderTransform = st;
                b.RenderTransformOrigin = new Point(0.5, 0.5);
                b.MouseEnter += (s, e) => { Anim.To(st, ScaleTransform.ScaleXProperty, 1.12, 160, ease: Anim.Spring); Anim.To(st, ScaleTransform.ScaleYProperty, 1.12, 160, ease: Anim.Spring); };
                b.MouseLeave += (s, e) => { Anim.To(st, ScaleTransform.ScaleXProperty, 1, 200); Anim.To(st, ScaleTransform.ScaleYProperty, 1, 200); };
                b.MouseLeftButtonUp += (s, e) => Open(link.Url);
                wp.Children.Add(b);
            }
            return wp;
        }
    }

    // ======================================================================

    /// <summary>Что сейчас играет у человека (с сервера, с учётом его настройки «кто видит»).</summary>
    internal sealed class NowPlayingInfo
    {
        public string Title, Artist, App, CoverUrl;
        public int Duration, Position;
        public bool Playing;
        public DateTime At;   // когда (по нашим часам) была эта позиция

        public TimeSpan Now
        {
            get
            {
                double p = Position + (Playing ? (DateTime.UtcNow - At).TotalSeconds : 0);
                if (Duration > 0) p = Math.Min(p, Duration);
                return TimeSpan.FromSeconds(Math.Max(0, p));
            }
        }
    }

    internal static class NowPlayingApi
    {
        public static async Task<NowPlayingInfo> Get(string userId)
        {
            if (!Supa.SignedIn || string.IsNullOrEmpty(userId)) return null;
            try
            {
                var (json, err) = await Supa.Call("now_playing_get", new JsonObject { ["p_user"] = userId });
                if (err != null || json is not JsonArray arr || arr.Count == 0) return null;
                var j = arr[0];
                int age = (int?)j["age_s"] ?? 0;
                return new NowPlayingInfo
                {
                    Title = (string)j["title"] ?? "",
                    Artist = (string)j["artist"] ?? "",
                    App = (string)j["app"] ?? "",
                    CoverUrl = (string)j["cover_url"],
                    Duration = (int?)j["duration_s"] ?? 0,
                    Position = (int?)j["position_s"] ?? 0,
                    Playing = (bool?)j["playing"] ?? true,
                    At = DateTime.UtcNow.AddSeconds(-Math.Max(0, age))
                };
            }
            catch (Exception e) { Settings.Log(e); return null; }
        }

        public static async Task<string> Set(string title, string artist, string app, string cover, int duration, int position, bool playing)
        {
            var (_, err) = await Supa.Call("now_playing_set", new JsonObject
            {
                ["p_title"] = title ?? "", ["p_artist"] = artist ?? "", ["p_app"] = app ?? "", ["p_cover"] = cover,
                ["p_duration"] = duration, ["p_position"] = position, ["p_playing"] = playing
            });
            return err;
        }

        /// <summary>Понятное имя плеера: Spotify.exe → Spotify, chrome → Chrome, AUMID Store-приложения → его имя.</summary>
        public static string AppName(string appId)
        {
            string a = (appId ?? "").Trim();
            if (a.Length == 0) return "";
            string low = a.ToLowerInvariant();
            if (low.Contains("spotify")) return "Spotify";
            if (low.Contains("soundcloud")) return "SoundCloud";
            if (low.Contains("applemusic") || low.Contains("apple music")) return "Apple Music";
            if (low.Contains("yandex") && low.Contains("music")) return "Яндекс Музыка";
            if (low.Contains("zunemusic") || low.Contains("mediaplayer")) return "Media Player";
            if (low.Contains("aimp")) return "AIMP";
            if (low.Contains("foobar")) return "foobar2000";
            if (low.Contains("vlc")) return "VLC";
            if (low.Contains("msedge")) return "Edge";
            if (low.Contains("chrome")) return "Chrome";
            if (low.Contains("firefox")) return "Firefox";
            if (low.Contains("opera")) return "Opera";
            if (low.Contains("browser") && low.Contains("yandex")) return "Яндекс Браузер";
            if (low.Contains("telegram")) return "Telegram";
            if (low.Contains("discord")) return "Discord";
            if (low.Contains("cloudmusic") || low.Contains("netease")) return "NetEase Music";
            if (low.Contains("qqmusic")) return "QQ Music";
            // AUMID: Publisher.App_hash!App → App; путь/имя exe → имя без .exe
            if (a.Contains('!')) a = a.Substring(0, a.IndexOf('!'));
            if (a.Contains('_')) a = a.Substring(0, a.IndexOf('_'));
            a = System.IO.Path.GetFileNameWithoutExtension(a);
            if (a.Contains('.')) a = a.Substring(a.LastIndexOf('.') + 1);
            return a.Length == 0 ? "" : char.ToUpperInvariant(a[0]) + a.Substring(1);
        }
    }

    // ======================================================================

    /// <summary>
    /// GIF и MP4 из профиля (живой баннер и живая аватарка Mizu Plus): качаем один раз в %AppData%\MizuEngine\cache\media
    /// (до 150 МБ, старые удаляются) и показываем с диска.
    /// </summary>
    internal static class MediaFileCache
    {
        public const long MaxUpload = 10L * 1024 * 1024;   // свой файл — до 10 МБ
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        static readonly Dictionary<string, Task<string>> Running = new Dictionary<string, Task<string>>();
        static string Folder => System.IO.Path.Combine(Settings.Folder, "cache", "media");

        public static bool IsVideo(string urlOrFile)
        {
            string e = System.IO.Path.GetExtension((urlOrFile ?? "").Split('?')[0]).ToLowerInvariant();
            return e == ".mp4" || e == ".webm" || e == ".mov" || e == ".wmv" || e == ".m4v";
        }

        public static Task<string> Get(string url)
        {
            if (string.IsNullOrEmpty(url)) return Task.FromResult<string>(null);
            lock (Running)
            {
                if (Running.TryGetValue(url, out var t)) return t;
                t = Fetch(url);
                Running[url] = t;
                return t;
            }
        }

        static async Task<string> Fetch(string url)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string name = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant().Substring(0, 24);
                string ext = System.IO.Path.GetExtension(url.Split('?')[0]).ToLowerInvariant();
                if (ext.Length == 0 || ext.Length > 5) ext = ".bin";
                string file = System.IO.Path.Combine(Folder, name + ext);
                if (File.Exists(file) && new FileInfo(file).Length > 0)
                {
                    try { File.SetLastAccessTimeUtc(file, DateTime.UtcNow); } catch { }
                    return file;
                }
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                        if (!resp.IsSuccessStatusCode) { await Task.Delay(800); continue; }
                        if (resp.Content.Headers.ContentLength > 3 * MaxUpload) return null;
                        string tmp = file + ".part";
                        using (var fs = File.Create(tmp)) await resp.Content.CopyToAsync(fs);
                        File.Move(tmp, file, true);
                        _ = Task.Run(Trim);
                        return file;
                    }
                    catch { await Task.Delay(800); }
                }
            }
            catch (Exception e) { Settings.Log(e); }
            lock (Running) Running.Remove(url);
            return null;
        }

        static void Trim()
        {
            try
            {
                var files = new DirectoryInfo(Folder).GetFiles().OrderBy(f => f.LastAccessTimeUtc).ToList();
                long total = files.Sum(f => f.Length);
                foreach (var f in files)
                {
                    if (total < 150L * 1024 * 1024) break;
                    total -= f.Length;
                    try { f.Delete(); } catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// Живой GIF/MP4: сначала пусто (под ним виден обычный баннер/аватарка), потом мягко проявляется.
        /// MP4 — без звука и по кругу. Пока элемент не на экране — не играет.
        /// </summary>
        public static FrameworkElement View(string url)
        {
            var host = new Grid { IsHitTestVisible = false, Opacity = 0, ClipToBounds = true };
            host.Loaded += async (s, e) =>
            {
                if (host.Children.Count > 0) return;
                string file = await Get(url);
                if (file == null || !host.IsLoaded) return;
                try
                {
                    FrameworkElement media;
                    if (IsVideo(file))
                    {
                        var me = new MediaElement
                        {
                            Source = new Uri(file), IsMuted = true, Volume = 0, Stretch = Stretch.UniformToFill,
                            LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Stop, ScrubbingEnabled = false
                        };
                        me.MediaEnded += (o, a) => { me.Position = TimeSpan.Zero; me.Play(); };
                        me.MediaOpened += (o, a) => Anim.To(host, UIElement.OpacityProperty, 1, 300);
                        me.MediaFailed += (o, a) => Settings.Log(a.ErrorException ?? new Exception("media failed: " + url));
                        me.Loaded += (o, a) => me.Play();
                        media = me;
                    }
                    else
                    {
                        media = new GifView(file);
                        Anim.To(host, UIElement.OpacityProperty, 1, 300);
                    }
                    host.Children.Add(media);
                }
                catch (Exception ex) { Settings.Log(ex); }
            };
            host.Unloaded += (s, e) =>
            {
                foreach (var c in host.Children) if (c is MediaElement me) me.Close();
                host.Children.Clear();
                host.Opacity = 0;
            };
            return host;
        }
    }
}
