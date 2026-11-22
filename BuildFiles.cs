// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MizuEngine
{
    /// <summary>
    /// Файлы сборок (0.11.5): обои рабочего стола Windows, обложка, пересжатие больших картинок и видео
    /// (видео — встроенным кодеком Windows, без сторонних программ) и упаковка сборки для «Лучших сборок».
    /// </summary>
    internal static class BuildFiles
    {
        // ================== обои Windows ==================

        const uint SPI_GETDESKWALLPAPER = 0x0073, SPI_SETDESKWALLPAPER = 0x0014, SPIF_UPDATEINIFILE = 0x01, SPIF_SENDCHANGE = 0x02;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SystemParametersInfo(uint action, uint param, StringBuilder pv, uint winIni);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SystemParametersInfoW")]
        static extern bool SystemParametersInfoSet(uint action, uint param, string pv, uint winIni);

        /// <summary>Картинка на рабочем столе сейчас (или null — сплошной цвет / слайд-шоу без файла).</summary>
        public static string CurrentDesktopWallpaper()
        {
            try
            {
                var sb = new StringBuilder(1024);
                if (!SystemParametersInfo(SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0)) return null;
                string path = sb.ToString();
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
                // Свои копии MizuEngine (обои из чужой сборки) тоже подходят — это обычные картинки
                return new FileInfo(path).Length > 0 ? path : null;
            }
            catch (Exception e) { Settings.Log(e); return null; }
        }

        /// <summary>Ставит картинку на рабочий стол (в фоне: Windows может думать над большой картинкой пару секунд).</summary>
        public static void SetDesktopWallpaper(string file)
        {
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return;
            Task.Run(() =>
            {
                try { SystemParametersInfoSet(SPI_SETDESKWALLPAPER, 0, file, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE); }
                catch (Exception e) { Settings.Log(e); }
            });
        }

        // ================== картинки ==================

        /// <summary>Расширение по содержимому файла (у обоев Windows его часто нет: TranscodedWallpaper).</summary>
        public static string ImageExt(string file)
        {
            try
            {
                using var f = File.OpenRead(file);
                var b = new byte[4];
                int n = f.Read(b, 0, 4);
                if (n >= 2 && b[0] == 0xFF && b[1] == 0xD8) return ".jpg";
                if (n >= 4 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return ".png";
                if (n >= 2 && b[0] == 0x42 && b[1] == 0x4D) return ".bmp";
            }
            catch { }
            string ext = Path.GetExtension(file)?.ToLowerInvariant();
            return string.IsNullOrEmpty(ext) ? ".jpg" : ext == ".jpeg" ? ".jpg" : ext;
        }

        static BitmapSource Load(string file, int maxSide)
        {
            var b = new BitmapImage();
            b.BeginInit();
            b.UriSource = new Uri(file);
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            b.EndInit();
            b.Freeze();
            if (maxSide > 0 && Math.Max(b.PixelWidth, b.PixelHeight) > maxSide)
            {
                double k = (double)maxSide / Math.Max(b.PixelWidth, b.PixelHeight);
                var t = new TransformedBitmap(b, new ScaleTransform(k, k));
                t.Freeze();
                return t;
            }
            return b;
        }

        static void SaveJpeg(BitmapSource src, string dest, int quality)
        {
            var enc = new JpegBitmapEncoder { QualityLevel = quality };
            enc.Frames.Add(BitmapFrame.Create(src));
            using var fs = File.Create(dest);
            enc.Save(fs);
        }

        /// <summary>Копирует картинку; если она больше maxBytes — пересжимает в JPEG (до 3840 px). Возвращает итоговый путь или null.</summary>
        public static string CopyImage(string src, string dest, long maxBytes)
        {
            try
            {
                if (new FileInfo(src).Length <= maxBytes) { File.Copy(src, dest, true); return dest; }
                string jpg = Path.ChangeExtension(dest, ".jpg");
                SaveJpeg(Load(src, 3840), jpg, 92);
                return jpg;
            }
            catch (Exception e) { Settings.Log(e); return null; }
        }

        /// <summary>Обложка сборки: JPEG до 1280 px по большей стороне.</summary>
        public static bool SaveCover(string src, string dest)
        {
            try
            {
                string tmp = dest + ".tmp";
                SaveJpeg(Load(src, 1280), tmp, 90);
                File.Move(tmp, dest, true);
                return true;
            }
            catch (Exception e) { Settings.Log(e); return false; }
        }

        /// <summary>Картинка с диска для показа (уменьшенная при загрузке — меньше памяти).</summary>
        public static ImageSource Thumb(string file, int width)
        {
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return null;
            try
            {
                var b = new BitmapImage();
                b.BeginInit();
                b.UriSource = new Uri(file);
                b.DecodePixelWidth = width;
                b.CacheOption = BitmapCacheOption.OnLoad;
                b.CreateOptions = BitmapCreateOptions.IgnoreImageCache;   // обложку могли заменить — берём свежий файл
                b.EndInit();
                b.Freeze();
                return b;
            }
            catch { return null; }
        }

        // ================== видео ==================

        /// <summary>
        /// Пересжимает видео в MP4 (H.264, не больше 1280 px по большей стороне) так, чтобы оно поместилось в targetBytes.
        /// Использует кодек Windows (Media Foundation) — работает на любом Windows 10/11 без сторонних программ.
        /// </summary>
        public static async Task<bool> ShrinkVideo(string src, string dest, long targetBytes)
        {
            try
            {
                var input = await global::Windows.Storage.StorageFile.GetFileFromPathAsync(src);
                var props = await input.Properties.GetVideoPropertiesAsync();
                double sec = Math.Max(1, props.Duration.TotalSeconds);
                uint w = props.Width, h = props.Height;
                if (w == 0 || h == 0) { w = 1280; h = 720; }
                double k = Math.Min(1.0, 1280.0 / Math.Max(w, h));
                uint ow = (uint)(Math.Round(w * k / 2) * 2), oh = (uint)(Math.Round(h * k / 2) * 2);

                var folder = await global::Windows.Storage.StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(dest));
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    bool withAudio = attempt == 0;
                    var output = await folder.CreateFileAsync(Path.GetFileName(dest), global::Windows.Storage.CreationCollisionOption.ReplaceExisting);
                    var profile = global::Windows.Media.MediaProperties.MediaEncodingProfile.CreateMp4(global::Windows.Media.MediaProperties.VideoEncodingQuality.HD720p);
                    double audioBits = withAudio ? 96000 : 0;
                    double bits = targetBytes * 8 * 0.9 / sec - audioBits;
                    profile.Video.Bitrate = (uint)Math.Clamp(bits, 350_000, 6_000_000);
                    profile.Video.Width = ow;
                    profile.Video.Height = oh;
                    if (withAudio) profile.Audio.Bitrate = 96000; else profile.Audio = null;

                    var tc = new global::Windows.Media.Transcoding.MediaTranscoder { HardwareAccelerationEnabled = true };
                    var prep = await tc.PrepareFileTranscodeAsync(input, output, profile);
                    if (!prep.CanTranscode) { Settings.Log(new Exception("transcode: " + prep.FailureReason)); continue; }
                    await prep.TranscodeAsync();
                    long len = new FileInfo(dest).Length;
                    if (len > 0 && len <= targetBytes * 1.05) return true;
                }
            }
            catch (Exception e) { Settings.Log(e); }
            return false;
        }

        // ================== упаковка для «Лучших сборок» ==================

        /// <summary>
        /// Готовит файл сборки для загрузки: zip до 50 МБ. Если больше — пересжимает видео (живой фон окна, живые обои).
        /// status — что сейчас происходит (для подписи на кнопке). Возвращает путь к временному файлу или ошибку.
        /// </summary>
        public static async Task<(string file, string error)> PackForUpload(PresetInfo p, Action<string> status)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "MizuEngine-build-" + Guid.NewGuid().ToString("N"));
            try
            {
                // Копия папки сборки — оригинал не трогаем
                await Task.Run(() =>
                {
                    foreach (var f in Directory.GetFiles(p.Folder, "*", SearchOption.AllDirectories))
                    {
                        string rel = Path.GetRelativePath(p.Folder, f);
                        string to = Path.Combine(tmp, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(to));
                        File.Copy(f, to, true);
                    }
                });
                long Total() => Directory.GetFiles(tmp, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);

                long limit = Presets.MaxPublishBytes - 512 * 1024;   // запас на zip
                if (Total() > limit)
                {
                    var copy = Presets.Read(tmp);
                    var videos = new[] { "video", "live" }
                        .Select(kind => (kind, file: copy == null ? null : Presets.FileOf(copy, kind)))
                        .Where(v => v.file != null && !v.file.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(v => new FileInfo(v.file).Length).ToList();
                    foreach (var v in videos)
                    {
                        long others = Total() - new FileInfo(v.file).Length;
                        long room = limit - others;
                        if (room < 1024 * 1024) continue;
                        status?.Invoke(Loc.T("builds.compressing"));
                        string mp4 = Path.Combine(Path.GetDirectoryName(v.file), v.kind + "-small.mp4");
                        if (await ShrinkVideo(v.file, mp4, room))
                        {
                            string final = Path.Combine(Path.GetDirectoryName(v.file), v.kind + ".mp4");
                            File.Delete(v.file);
                            File.Move(mp4, final, true);
                            Presets.Update(copy, j => j["files"][v.kind] = "files/" + v.kind + ".mp4");
                        }
                        else try { File.Delete(mp4); } catch { }
                        if (Total() <= limit) break;
                    }
                }
                if (Total() > limit) return (null, Loc.T("builds.too_big"));

                status?.Invoke(Loc.T("builds.packing"));
                string zip = tmp + ".mizupreset";
                await Task.Run(() => ZipFile.CreateFromDirectory(tmp, zip, CompressionLevel.Optimal, false));
                if (new FileInfo(zip).Length > Presets.MaxPublishBytes) { try { File.Delete(zip); } catch { } return (null, Loc.T("builds.too_big")); }
                return (zip, null);
            }
            catch (Exception e) { Settings.Log(e); return (null, Loc.T("err.generic")); }
            finally { try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { } }
        }
    }
}
