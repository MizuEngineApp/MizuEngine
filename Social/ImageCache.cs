// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>
    /// Картинки из интернета (аватарки, баннеры, фото постов) — надёжно и без «съезжания».
    /// Раньше WPF сам качал картинку по адресу и держал свой кэш: одна и та же картинка приходила то в одном размере,
    /// то в другом, иногда ещё не готовой — отсюда буква вместо аватарки и квадрат, вырезанный не из середины.
    /// Теперь: файл качается один раз (3 попытки, общий HttpClient), лежит на диске в %AppData%\MizuEngine\cache\img,
    /// декодируется в фоне по настоящим пикселям, поворачивается по EXIF, для аватарок вырезается ровно середина,
    /// и готовая (замороженная) картинка отдаётся в любой поток. Повторно — мгновенно из памяти.
    /// </summary>
    internal static class ImageCache
    {
        static readonly HttpClient Http = CreateClient();
        static readonly object Lock = new object();
        // Готовые картинки: ключ — адрес + размер + «квадрат». Держим последние 160, остальное — по слабым ссылкам
        static readonly Dictionary<string, BitmapSource> Strong = new Dictionary<string, BitmapSource>();
        static readonly LinkedList<string> Order = new LinkedList<string>();
        static readonly Dictionary<string, WeakReference<BitmapSource>> Weak = new Dictionary<string, WeakReference<BitmapSource>>();
        // Один адрес качается один раз, даже если его попросили 20 аватарок сразу
        static readonly Dictionary<string, Task<byte[]>> Downloads = new Dictionary<string, Task<byte[]>>();
        static readonly SemaphoreSlim Gate = new SemaphoreSlim(6);   // не больше 6 загрузок одновременно
        const int StrongMax = 160;
        static int _pruned;

        static string Folder => Path.Combine(Settings.Folder, "cache", "img");

        static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("MizuEngine");
            return c;
        }

        static string Key(string url, int px, bool square) => url + "|" + px + "|" + (square ? "s" : "r");

        /// <summary>Картинка уже готова в памяти — без ожидания (чтобы аватарка не мигала буквой).</summary>
        public static BitmapSource Peek(string url, int px, bool square)
        {
            if (string.IsNullOrEmpty(url)) return null;
            string k = Key(url, px, square);
            lock (Lock)
            {
                if (Strong.TryGetValue(k, out var b)) { Touch(k); return b; }
                if (Weak.TryGetValue(k, out var w) && w.TryGetTarget(out b)) { Remember(k, b); return b; }
            }
            return null;
        }

        /// <summary>
        /// Загрузить картинку. done вызывается в потоке, который позвал Load (если он с Dispatcher), или сразу, если картинка уже в памяти.
        /// px — нужная ширина в пикселях (для квадрата — сторона); square — вырезать квадрат из середины.
        /// При ошибке done не вызывается — остаётся то, что было (буква на градиенте).
        /// </summary>
        public static void Load(string url, int px, bool square, Action<BitmapSource> done)
        {
            if (string.IsNullOrEmpty(url) || done == null) return;
            px = Math.Clamp(px, 16, 2400);
            var ready = Peek(url, px, square);
            if (ready != null) { done(ready); return; }
            var d = Dispatcher.FromThread(Thread.CurrentThread);
            _ = GetAsync(url, px, square).ContinueWith(t =>
            {
                if (t.Status != TaskStatus.RanToCompletion || t.Result == null) return;
                if (d == null || d.HasShutdownStarted) return;
                d.BeginInvoke(new Action(() => { try { done(t.Result); } catch (Exception e) { Settings.Log(e); } }));
            }, TaskScheduler.Default);
        }

        public static async Task<BitmapSource> GetAsync(string url, int px, bool square)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var ready = Peek(url, px, square);
            if (ready != null) return ready;
            try
            {
                byte[] bytes = await Bytes(url).ConfigureAwait(false);
                if (bytes == null || bytes.Length == 0) return null;
                var bmp = await Task.Run(() => Decode(bytes, px, square)).ConfigureAwait(false);
                if (bmp == null) return null;
                lock (Lock) Remember(Key(url, px, square), bmp);
                return bmp;
            }
            catch (Exception e)
            {
                Settings.Log(new Exception("image: " + Short(url), e));
                return null;
            }
        }

        // ================== файл: диск или интернет ==================

        static Task<byte[]> Bytes(string url)
        {
            lock (Lock)
            {
                if (Downloads.TryGetValue(url, out var running)) return running;
                var t = Fetch(url);
                Downloads[url] = t;
                t.ContinueWith(_ => { lock (Lock) Downloads.Remove(url); }, TaskScheduler.Default);
                return t;
            }
        }

        static async Task<byte[]> Fetch(string url)
        {
            // Картинка из самой программы (значок MizuEngine для уведомлений)
            if (url.StartsWith("pack://", StringComparison.OrdinalIgnoreCase))
            {
                var res = Application.GetResourceStream(new Uri(url));
                if (res == null) return null;
                using var st = res.Stream;
                using var copy = new MemoryStream();
                await st.CopyToAsync(copy).ConfigureAwait(false);
                return copy.ToArray();
            }
            // Свой файл с диска (например, фото для поста перед отправкой)
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return File.Exists(url) ? await File.ReadAllBytesAsync(url).ConfigureAwait(false) : null;

            string file = Path.Combine(Folder, Hash(url) + ".img");
            try
            {
                if (File.Exists(file))
                {
                    File.SetLastAccessTimeUtc(file, DateTime.UtcNow);
                    return await File.ReadAllBytesAsync(file).ConfigureAwait(false);
                }
            }
            catch { }

            await Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                Exception last = null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (attempt > 0) await Task.Delay(attempt == 1 ? 700 : 2000).ConfigureAwait(false);
                    try
                    {
                        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                        if ((int)resp.StatusCode == 404) return null;   // картинку удалили — повторять бессмысленно
                        resp.EnsureSuccessStatusCode();
                        byte[] data = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        if (data.Length == 0) throw new IOException("empty image");
                        try
                        {
                            Directory.CreateDirectory(Folder);
                            string part = file + "." + Guid.NewGuid().ToString("N").Substring(0, 6) + ".part";
                            await File.WriteAllBytesAsync(part, data).ConfigureAwait(false);
                            File.Move(part, file, true);
                            if (Interlocked.Increment(ref _pruned) % 40 == 1) _ = Task.Run(Prune);
                        }
                        catch { }
                        return data;
                    }
                    catch (Exception e) { last = e; }
                }
                throw last ?? new IOException("download failed");
            }
            finally { Gate.Release(); }
        }

        /// <summary>Кэш на диске не больше ~80 МБ: самые давно не нужные файлы удаляются.</summary>
        static void Prune()
        {
            try
            {
                var dir = new DirectoryInfo(Folder);
                if (!dir.Exists) return;
                var files = new List<FileInfo>(dir.GetFiles("*.img"));
                long total = 0;
                foreach (var f in files) total += f.Length;
                if (total < 80L << 20) return;
                files.Sort((a, b) => a.LastAccessTimeUtc.CompareTo(b.LastAccessTimeUtc));
                foreach (var f in files)
                {
                    if (total < 60L << 20) break;
                    total -= f.Length;
                    try { f.Delete(); } catch { }
                }
            }
            catch { }
        }

        // ================== декодирование ==================

        /// <summary>Картинка по настоящим пикселям: поворот по EXIF, квадрат из середины, уменьшение до px. Готовая — замороженная.</summary>
        static BitmapSource Decode(byte[] bytes, int px, bool square)
        {
            using var ms = new MemoryStream(bytes, false);
            var dec = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            if (dec.Frames.Count == 0) return null;
            BitmapSource b = dec.Frames[0];

            // Телефонные фото часто «лежат на боку» и поворачиваются только по метке EXIF
            int orient = 1;
            try { if (dec.Frames[0].Metadata is BitmapMetadata md && md.GetQuery("System.Photo.Orientation") is ushort o) orient = o; } catch { }
            if (orient == 3 || orient == 6 || orient == 8)
                b = new TransformedBitmap(b, new RotateTransform(orient == 3 ? 180 : orient == 6 ? 90 : 270));

            int w = b.PixelWidth, h = b.PixelHeight;
            if (w < 1 || h < 1) return null;

            if (square && w != h)
            {
                int s = Math.Min(w, h);
                b = new CroppedBitmap(b, new Int32Rect((w - s) / 2, (h - s) / 2, s, s));
                w = h = s;
            }

            // Уменьшаем до нужного размера (увеличивать не нужно — кисть растянет сама)
            if (w > px)
            {
                double k = px / (double)w;
                b = new TransformedBitmap(b, new ScaleTransform(k, k));
            }

            // Всё дерево преобразований сводим в одну готовую картинку: её можно отдавать в любой поток и рисовать без пересчёта
            var final = new WriteableBitmap(new FormatConvertedBitmap(b, PixelFormats.Pbgra32, null, 0));
            final.Freeze();
            return final;
        }

        // ================== память ==================

        static void Remember(string k, BitmapSource b)
        {
            if (!Strong.ContainsKey(k)) Order.AddFirst(k); else Touch(k);
            Strong[k] = b;
            Weak[k] = new WeakReference<BitmapSource>(b);
            while (Strong.Count > StrongMax && Order.Last != null)
            {
                Strong.Remove(Order.Last.Value);
                Order.RemoveLast();
            }
            if (Weak.Count > 2000)
            {
                var dead = new List<string>();
                foreach (var kv in Weak) if (!kv.Value.TryGetTarget(out _)) dead.Add(kv.Key);
                foreach (var d in dead) Weak.Remove(d);
            }
        }

        static void Touch(string k)
        {
            var n = Order.Find(k);
            if (n != null && n != Order.First) { Order.Remove(n); Order.AddFirst(n); }
        }

        static string Hash(string s)
        {
            var h = SHA1.HashData(Encoding.UTF8.GetBytes(s));
            return Convert.ToHexString(h).ToLowerInvariant();
        }

        static string Short(string url) => url.Length > 80 ? url.Substring(0, 80) + "…" : url;
    }
}
