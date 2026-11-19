// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MizuEngine
{
    /// <summary>
    /// Аватарка автора берётся из его публичного профиля Telegram (t.me/MrRainthisisme) и обновляется раз в несколько минут.
    /// Последняя скачанная хранится на диске — если Telegram недоступен, показывается она.
    /// </summary>
    internal static class TelegramAvatar
    {
        const string Profile = "https://t.me/MrRainthisisme";
        static readonly HttpClient Http = CreateClient();
        static string CacheFile => Path.Combine(Settings.Folder, "author_avatar.jpg");

        static HttpClient CreateClient()
        {
            var h = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
            return h;
        }

        /// <summary>Картинка из кэша (или null, если ещё ни разу не скачивали).</summary>
        public static ImageSource Cached() => Load(CacheFile);

        /// <summary>Скачивает свежую аватарку. Возвращает новую картинку, если она изменилась, иначе null.</summary>
        public static async Task<ImageSource> RefreshAsync()
        {
            try
            {
                string html = await Http.GetStringAsync(Profile);
                // Ссылка на фото профиля есть в meta og:image; если фото нет, там логотип Telegram — его пропускаем
                var m = Regex.Match(html, "<meta property=\"og:image\" content=\"([^\"]+)\"");
                if (!m.Success) return null;
                string url = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
                if (url.Contains("telegram.org/img")) return null;

                byte[] data = await Http.GetByteArrayAsync(url);
                if (data.Length < 500) return null;
                if (File.Exists(CacheFile) && File.ReadAllBytes(CacheFile).AsSpan().SequenceEqual(data)) return null;

                Directory.CreateDirectory(Settings.Folder);
                await File.WriteAllBytesAsync(CacheFile, data);
                return Load(CacheFile);
            }
            catch { return null; }   // нет интернета или Telegram заблокирован — остаётся последняя картинка
        }

        static ImageSource Load(string file)
        {
            if (!File.Exists(file)) return null;
            try
            {
                var b = new BitmapImage();
                b.BeginInit();
                b.UriSource = new Uri(file);
                b.DecodePixelWidth = 256;
                b.CacheOption = BitmapCacheOption.OnLoad;
                b.CreateOptions = BitmapCreateOptions.IgnoreImageCache;   // иначе WPF покажет старую картинку из своего кэша
                b.EndInit();
                b.Freeze();
                return b;
            }
            catch { return null; }
        }
    }
}
