// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinMedia = Windows.Media.Control;

namespace MizuEngine
{
    /// <summary>Что сейчас играет.</summary>
    internal sealed class MediaInfo
    {
        public string Title = "", Artist = "", AppId = "";
        public ImageSource Art;
        public Color Tint = Color.FromRgb(0x30, 0xD1, 0x58);   // цвет «эквалайзера» — из обложки
        public bool Playing;
        public TimeSpan Position, Duration;
        public DateTime PositionAt = DateTime.UtcNow;

        /// <summary>Позиция с учётом времени, прошедшего с последнего обновления от плеера.</summary>
        public TimeSpan Now => Playing ? Position + (DateTime.UtcNow - PositionAt) : Position;
    }

    /// <summary>
    /// Музыка из любого плеера: Windows сам собирает это для своей панели громкости
    /// (Spotify, браузеры с YouTube и Яндекс Музыкой, «Медиаплеер», AIMP и т. д.). Мы берём оттуда название,
    /// исполнителя, обложку и управляем воспроизведением. Все события приходят в поток Shell.
    /// </summary>
    internal static class MediaWatcher
    {
        public static event Action Changed;
        public static MediaInfo Current { get; private set; }

        static WinMedia.GlobalSystemMediaTransportControlsSessionManager _mgr;
        static WinMedia.GlobalSystemMediaTransportControlsSession _session;
        static Dispatcher _d;
        static bool _started;
        static int _version;

        public static async void Start(Dispatcher d)
        {
            if (_started) return;
            _started = true;
            _d = d;
            try
            {
                _mgr = await WinMedia.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _mgr.CurrentSessionChanged += (s, e) => Post(Attach);
                _mgr.SessionsChanged += (s, e) => Post(Attach);
                Post(Attach);
            }
            catch (Exception e) { Settings.Log(e); _started = false; }
        }

        static void Post(Action a) => _d?.BeginInvoke(a);

        static void Attach()
        {
            if (_mgr == null) return;
            WinMedia.GlobalSystemMediaTransportControlsSession s = null;
            try { s = _mgr.GetCurrentSession(); } catch { }
            if (!ReferenceEquals(s, _session))
            {
                if (_session != null)
                {
                    try
                    {
                        _session.MediaPropertiesChanged -= OnMedia;
                        _session.PlaybackInfoChanged -= OnPlayback;
                        _session.TimelinePropertiesChanged -= OnTimeline;
                    }
                    catch { }
                }
                _session = s;
                if (s != null)
                {
                    s.MediaPropertiesChanged += OnMedia;
                    s.PlaybackInfoChanged += OnPlayback;
                    s.TimelinePropertiesChanged += OnTimeline;
                }
            }
            _ = Update(true);
        }

        static void OnMedia(WinMedia.GlobalSystemMediaTransportControlsSession s, WinMedia.MediaPropertiesChangedEventArgs e) => Post(() => _ = Update(true));
        static void OnPlayback(WinMedia.GlobalSystemMediaTransportControlsSession s, WinMedia.PlaybackInfoChangedEventArgs e) => Post(() => _ = Update(false));
        static void OnTimeline(WinMedia.GlobalSystemMediaTransportControlsSession s, WinMedia.TimelinePropertiesChangedEventArgs e) => Post(() => _ = Update(false));

        static async Task Update(bool props)
        {
            var s = _session;
            int version = ++_version;
            if (s == null) { Current = null; Changed?.Invoke(); return; }
            try
            {
                var info = new MediaInfo();
                var old = Current;
                if (old != null && !props) { info.Title = old.Title; info.Artist = old.Artist; info.Art = old.Art; info.Tint = old.Tint; }
                info.AppId = s.SourceAppUserModelId ?? "";

                var pb = s.GetPlaybackInfo();
                info.Playing = pb != null && pb.PlaybackStatus == WinMedia.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

                var tl = s.GetTimelineProperties();
                if (tl != null)
                {
                    info.Duration = tl.EndTime - tl.StartTime;
                    info.Position = tl.Position - tl.StartTime;
                    // Плеер сообщает позицию не каждую секунду — досчитываем от момента его последнего отчёта
                    var at = tl.LastUpdatedTime.UtcDateTime;
                    info.PositionAt = at.Year > 2000 && at <= DateTime.UtcNow ? at : DateTime.UtcNow;
                }

                if (props || old == null)
                {
                    var mp = await s.TryGetMediaPropertiesAsync();
                    if (version != _version) return;   // пока ждали, пришло более свежее
                    if (mp != null)
                    {
                        info.Title = mp.Title ?? "";
                        info.Artist = mp.Artist ?? "";
                        if (mp.Thumbnail != null)
                        {
                            var art = await LoadArt(mp.Thumbnail);
                            if (version != _version) return;
                            if (art != null) { info.Art = art; info.Tint = TintOf(art); }
                        }
                        else info.Art = null;
                    }
                }
                Current = string.IsNullOrEmpty(info.Title) && !info.Playing ? null : info;
            }
            catch (Exception e) { Settings.Log(e); Current = null; }
            Changed?.Invoke();
        }

        static async Task<ImageSource> LoadArt(Windows.Storage.Streams.IRandomAccessStreamReference reference)
        {
            try
            {
                using var stream = await reference.OpenReadAsync();
                using var input = stream.AsStreamForRead();
                var ms = new MemoryStream();
                await input.CopyToAsync(ms);
                ms.Position = 0;
                var img = new BitmapImage();
                img.BeginInit();
                img.StreamSource = ms;
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.DecodePixelWidth = 160;
                img.EndInit();
                img.Freeze();
                return img;
            }
            catch { return null; }
        }

        /// <summary>Яркий цвет обложки — им светится «эквалайзер», как в iPhone.</summary>
        static Color TintOf(ImageSource src)
        {
            try
            {
                var small = new FormatConvertedBitmap(new TransformedBitmap((BitmapSource)src,
                    new ScaleTransform(16.0 / ((BitmapSource)src).PixelWidth, 16.0 / ((BitmapSource)src).PixelHeight)), PixelFormats.Bgra32, null, 0);
                int w = small.PixelWidth, h = small.PixelHeight;
                var px = new byte[w * h * 4];
                small.CopyPixels(px, w * 4, 0);
                double best = -1; Color pick = Color.FromRgb(0x30, 0xD1, 0x58);
                for (int i = 0; i < px.Length; i += 4)
                {
                    byte b = px[i], g = px[i + 1], r = px[i + 2];
                    double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
                    double sat = max <= 0 ? 0 : (max - min) / max, light = max / 255.0;
                    double score = sat * 0.7 + light * 0.3;
                    if (light < 0.25) score -= 1;   // тёмные пиксели не берём — на чёрном Island их не видно
                    if (score > best) { best = score; pick = Color.FromRgb(r, g, b); }
                }
                // Слишком тёмный цвет осветляем
                double l = (0.299 * pick.R + 0.587 * pick.G + 0.114 * pick.B) / 255;
                if (l < 0.45) pick = Color.FromRgb((byte)Math.Min(255, pick.R + 90), (byte)Math.Min(255, pick.G + 90), (byte)Math.Min(255, pick.B + 90));
                return pick;
            }
            catch { return Color.FromRgb(0x30, 0xD1, 0x58); }
        }

        // ================== управление ==================

        public static void PlayPause() { try { _ = _session?.TryTogglePlayPauseAsync(); } catch { } }
        public static void Next() { try { _ = _session?.TrySkipNextAsync(); } catch { } }
        public static void Previous() { try { _ = _session?.TrySkipPreviousAsync(); } catch { } }

        /// <summary>Чей это плеер: AUMID или имя exe (Spotify.exe, chrome, Microsoft.ZuneMusic_…!Microsoft.ZuneMusic).</summary>
        public static string SourceApp => Current?.AppId ?? "";
    }
}
