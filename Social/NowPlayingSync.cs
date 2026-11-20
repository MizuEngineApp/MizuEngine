// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>
    /// «Сейчас слушает» в профиле: берём трек из любого плеера на ПК (то же, что видит Dynamic Island)
    /// и отправляем на сервер — при смене трека, паузе/продолжении и раз в 90 секунд, пока играет.
    /// Если в настройках «Никто» — ничего не отправляется.
    /// </summary>
    internal static class NowPlayingSync
    {
        static Dispatcher _ui;
        static DispatcherTimer _debounce, _beat;
        static string _sentKey, _coverKey, _coverUrl;
        static bool _sentPlaying, _published, _busy, _again;

        public static void Start()
        {
            if (_ui != null) return;
            _ui = Dispatcher.CurrentDispatcher;
            _debounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _debounce.Tick += (s, e) => { _debounce.Stop(); _ = Push(false); };
            _beat = new DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
            _beat.Tick += (s, e) => _ = Push(true);
            _beat.Start();
            // Музыку слушает поток оболочки (там же Dynamic Island); если Island с музыкой выключен — запускаем сами
            Shell.Run(() => MediaWatcher.Start(Shell.D));
            MediaWatcher.Changed += () => _ui.BeginInvoke(new Action(Soon));
            Supa.SessionChanged += () => _ui.BeginInvoke(new Action(() => { _sentKey = null; _coverKey = null; _published = false; Soon(); }));
            Soon();
        }

        /// <summary>Настройку «кто видит» поменяли — отправляем заново (или убираем).</summary>
        public static void Refresh() { _sentKey = null; Soon(); }

        static void Soon()
        {
            _debounce.Stop();
            _debounce.Start();
        }

        static async Task Push(bool heartbeat)
        {
            if (_busy) { _again = true; return; }
            _busy = true;
            try
            {
                if (!Supa.SignedIn || !Supa.Configured) return;
                var me = SocialApi.MyProfile;
                var m = MediaWatcher.Current;
                bool hidden = me != null && me.NpVis == "nobody";
                if (hidden || m == null || string.IsNullOrWhiteSpace(m.Title))
                {
                    if (_published) { await NowPlayingApi.Set("", "", "", null, 0, 0, false); _published = false; }
                    _sentKey = null;
                    return;
                }
                string key = m.Title + "\n" + m.Artist + "\n" + m.AppId;
                bool changed = key != _sentKey || m.Playing != _sentPlaying;
                if (!changed && !(heartbeat && m.Playing)) return;

                if (key != _coverKey)
                {
                    _coverKey = key;
                    _coverUrl = await UploadCover(m.Art);
                }
                int dur = (int)Math.Round(Math.Max(0, m.Duration.TotalSeconds));
                int pos = (int)Math.Round(Math.Max(0, m.Now.TotalSeconds));
                if (dur > 0 && pos > dur) pos = dur;
                string err = await NowPlayingApi.Set(m.Title, m.Artist, NowPlayingApi.AppName(m.AppId), _coverUrl, dur, pos, m.Playing);
                if (err == null) { _sentKey = key; _sentPlaying = m.Playing; _published = true; }
            }
            catch (Exception e) { Settings.Log(e); }
            finally
            {
                _busy = false;
                if (_again) { _again = false; Soon(); }
            }
        }

        /// <summary>Обложка 160 px в своё место хранилища (один файл на человека, перезаписывается). Ссылка с меткой — чтобы не взялась старая.</summary>
        static async Task<string> UploadCover(ImageSource art)
        {
            if (art is not BitmapSource bmp) return null;
            try
            {
                byte[] data = Jpeg(bmp);
                if (data == null) return null;
                string url = await Supa.Upload($"{Supa.Me}/np-cover.jpg", data, "image/jpeg");
                return url == null ? null : url + "?v=" + DateTime.UtcNow.Ticks;
            }
            catch (Exception e) { Settings.Log(e); return null; }
        }

        static byte[] Jpeg(BitmapSource src)
        {
            double k = Math.Min(1.0, 160.0 / Math.Max(src.PixelWidth, src.PixelHeight));
            BitmapSource img = k < 1 ? new TransformedBitmap(src, new ScaleTransform(k, k)) : src;
            var enc = new JpegBitmapEncoder { QualityLevel = 85 };
            enc.Frames.Add(BitmapFrame.Create(img));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }

        /// <summary>При выходе из программы — убрать трек сразу (иначе он пропадёт сам через 4 минуты).</summary>
        public static void Clear()
        {
            try
            {
                if (!_published || !Supa.SignedIn) return;
                Task.Run(() => NowPlayingApi.Set("", "", "", null, 0, 0, false)).Wait(TimeSpan.FromSeconds(1.2));
                _published = false;
            }
            catch { }
        }
    }
}
