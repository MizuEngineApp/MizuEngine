// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MizuEngine
{
    /// <summary>
    /// «Живые фоны MizuEngine» (только Mizu Plus): живой фон самого окна программы.
    /// 6 готовых фонов (рисуются программой на лету — чёткие на любом экране и почти ничего не весят),
    /// своё видео (MP4, WebM, MOV, WMV) или GIF, затемнение и скорость.
    /// Это не обои рабочего стола — для них есть «Живые обои».
    /// </summary>
    internal sealed class LiveBgPage
    {
        readonly Grid _host;
        readonly Settings _s;
        readonly Action<string> _toast;
        readonly Action _reapplyTheme;

        public LiveBgPage(Grid host, Settings s, Action<string> toast, Action reapplyTheme)
        {
            _host = host; _s = s; _toast = toast; _reapplyTheme = reapplyTheme;
        }

        public void Show()
        {
            _host.Children.Clear();
            _host.Children.Add(UI.Scroll(Build()));
        }

        public void Refresh() { if (_host.IsVisible) Show(); }

        FrameworkElement Build()
        {
            var root = new StackPanel { MaxWidth = 800, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 14, 28) };
            var title = UI.Row(UI.H1("livebg.title"));
            var pb = UI.PlusBadge(22, clickable: false);
            pb.Margin = new Thickness(10, 4, 0, 0);
            title.Children.Add(pb);
            root.Children.Add(title);
            var sub = UI.L("livebg.sub", 13.5, sub: true, wrap: true);
            sub.Margin = new Thickness(0, 6, 0, 0);
            root.Children.Add(sub);

            if (!Plus.IsActive)
            {
                var lockSp = new StackPanel();
                lockSp.Children.Add(UI.L("livebg.locked", 14, wrap: true));
                var go = UI.Primary("livebg.get_plus", (s, e) => UI.OpenPlus?.Invoke());
                go.Margin = new Thickness(0, 14, 0, 0);
                go.HorizontalAlignment = HorizontalAlignment.Left;
                lockSp.Children.Add(go);
                root.Children.Add(Gap(UI.Card(lockSp, 0), 20));
                root.Children.Add(Gap(UI.Section("livebg.ready"), 8));
                root.Children.Add(Tiles(false));
                Motion.Stagger(root, 2);
                return root;
            }

            root.Children.Add(Gap(UI.Section("livebg.ready"), 8));
            root.Children.Add(Tiles(true));
            root.Children.Add(UI.Section("livebg.own"));
            root.Children.Add(OwnCard());
            root.Children.Add(UI.Section("livebg.look"));
            root.Children.Add(LookCard());
            if (Themes.IsLive(_s.Theme) || _s.Theme == "video")
            {
                var off = UI.Glass("", "livebg.off", (s, e) => { _s.Theme = "blue"; _s.Save(); _reapplyTheme(); Refresh(); }, 10);
                off.Margin = new Thickness(0, 14, 0, 0);
                off.HorizontalAlignment = HorizontalAlignment.Left;
                root.Children.Add(off);
            }
            Motion.Stagger(root, 2);
            return root;
        }

        static FrameworkElement Gap(FrameworkElement e, double top) { e.Margin = new Thickness(e.Margin.Left, top, e.Margin.Right, e.Margin.Bottom); return e; }

        /// <summary>6 готовых живых фонов: плитка с превью, выбранная — с фиолетовой рамкой.</summary>
        FrameworkElement Tiles(bool enabled)
        {
            var tiles = new WrapPanel();
            foreach (var name in Themes.Live)
            {
                string theme = name;
                bool on = _s.Theme == theme;
                var swatch = new Border
                {
                    Width = 168, Height = 100, CornerRadius = new CornerRadius(14), Background = Themes.LivePreview(theme),
                    BorderThickness = new Thickness(on ? 2.5 : 1),
                    BorderBrush = on ? new SolidColorBrush(Color.FromRgb(0xA7, 0x8B, 0xFA)) : new SolidColorBrush(Color.FromArgb(0x50, 255, 255, 255))
                };
                var label = UI.L("theme." + theme, 13, true);
                label.Margin = new Thickness(2, 8, 0, 0);
                var tile = new Button
                {
                    Style = (Style)UI.Owner.FindResource("GlassButton"), Padding = new Thickness(8), Margin = new Thickness(0, 0, 12, 12),
                    Content = new StackPanel { Children = { swatch, label } }, IsEnabled = enabled, Opacity = enabled ? 1 : 0.6
                };
                tile.Click += (s, e) =>
                {
                    _s.Theme = theme;
                    _s.FirstRunDone = true;
                    _s.Save();
                    _reapplyTheme();
                    Refresh();
                };
                tiles.Children.Add(tile);
            }
            return tiles;
        }

        /// <summary>Своё видео или GIF. Файл копируется к нам — не пропадёт, если исходник удалят.</summary>
        FrameworkElement OwnCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(UI.L("livebg.own.d", 13, sub: true, wrap: true));
            string file = File.Exists(_s.VideoTheme) ? Path.GetFileName(_s.VideoTheme) : Loc.T("plus.no_file");
            var name = UI.T(file, 13.5, true);
            name.Margin = new Thickness(0, 12, 0, 0);
            sp.Children.Add(name);

            var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            var pick = UI.Primary("livebg.pick", (s, e) =>
            {
                var dlg = new OpenFileDialog { Filter = Loc.T("livebg.filter") + "|*.mp4;*.m4v;*.webm;*.mov;*.wmv;*.gif|MP4|*.mp4;*.m4v|GIF|*.gif" };
                if (dlg.ShowDialog() != true) return;
                try { _s.VideoTheme = PlusPage.CopyVideo(dlg.FileName, "theme"); }
                catch (Exception ex) { Settings.Log(ex); _toast(Loc.T("err.generic")); return; }
                _s.Theme = "video";
                _s.FirstRunDone = true;
                _s.Save();
                _reapplyTheme();
                _toast(Loc.T("plus.video_applied"));
                Refresh();
            });
            pick.Margin = new Thickness(0, 0, 10, 8);
            buttons.Children.Add(pick);
            if (File.Exists(_s.VideoTheme) && _s.Theme != "video")
            {
                var use = UI.Glass("", "plus.use_video", (s, e) => { _s.Theme = "video"; _s.Save(); _reapplyTheme(); Refresh(); }, 10);
                use.Margin = new Thickness(0, 0, 10, 8);
                buttons.Children.Add(use);
            }
            sp.Children.Add(buttons);
            return UI.Card(sp, 0);
        }

        /// <summary>Затемнение (чтобы текст читался) и скорость фона.</summary>
        FrameworkElement LookCard()
        {
            var sp = new StackPanel();
            var main = Window.GetWindow(_host) as MainWindow;

            var dimLabel = UI.T(string.Format(Loc.T("plus.video_dim"), _s.VideoDim), 12.5, sub: true);
            dimLabel.Margin = new Thickness(0, 0, 0, 4);
            sp.Children.Add(dimLabel);
            var dim = MakeSlider(0, 80, _s.VideoDim, 5);
            sp.Children.Add(dim);

            var speedLabel = UI.T(string.Format(Loc.T("livebg.speed"), _s.LiveBgSpeed.ToString("0.0#")), 12.5, sub: true);
            speedLabel.Margin = new Thickness(0, 14, 0, 4);
            sp.Children.Add(speedLabel);
            var speed = MakeSlider(0.25, 2, _s.LiveBgSpeed, 0.25);
            sp.Children.Add(speed);

            var save = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            save.Tick += (o, e) => { save.Stop(); _s.Save(); main?.SetLiveSpeed(_s.LiveBgSpeed); };
            dim.ValueChanged += (o, e) =>
            {
                _s.VideoDim = (int)dim.Value;
                dimLabel.Text = string.Format(Loc.T("plus.video_dim"), _s.VideoDim);
                main?.SetVideoDim(_s.VideoDim);
                save.Stop(); save.Start();
            };
            speed.ValueChanged += (o, e) =>
            {
                _s.LiveBgSpeed = Math.Round(speed.Value, 2);
                speedLabel.Text = string.Format(Loc.T("livebg.speed"), _s.LiveBgSpeed.ToString("0.0#"));
                save.Stop(); save.Start();   // скорость меняем, когда ползунок отпустили (фон пересоздаётся)
            };
            return UI.Card(sp, 0);
        }

        static Slider MakeSlider(double min, double max, double value, double step)
        {
            var s = new Slider { Minimum = min, Maximum = max, Value = value, MaxWidth = 380, Width = 380, HorizontalAlignment = HorizontalAlignment.Left, IsSnapToTickEnabled = true, TickFrequency = step };
            if (UI.Owner.TryFindResource("NiceSlider") is Style st) s.Style = st;
            return s;
        }
    }

    /// <summary>
    /// Анимированный GIF фоном окна. Кадры собираются по одному прямо во время показа (с учётом прозрачности и
    /// способа смены кадров GIF), а не хранятся все сразу — даже длинная гифка занимает память одной картинки.
    /// </summary>
    internal sealed class GifView : Image
    {
        readonly GifBitmapDecoder _dec;
        readonly DispatcherTimer _timer = new DispatcherTimer(DispatcherPriority.Render);
        readonly WriteableBitmap _canvas;
        readonly int _w, _h;
        readonly byte[] _buf;
        int _index;
        (int x, int y, int w, int h, int disposal) _prev;
        byte[] _saved, _px;
        public double Speed = 1;

        public GifView(string file)
        {
            _dec = new GifBitmapDecoder(new Uri(file), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);   // кадры читаются по мере показа, а не все сразу
            var meta = _dec.Metadata as BitmapMetadata;
            _w = Read<ushort>(meta, "/logscrdesc/Width", (ushort)_dec.Frames[0].PixelWidth);
            _h = Read<ushort>(meta, "/logscrdesc/Height", (ushort)_dec.Frames[0].PixelHeight);
            _canvas = new WriteableBitmap(_w, _h, 96, 96, PixelFormats.Pbgra32, null);
            _buf = new byte[_w * _h * 4];
            Source = _canvas;
            Stretch = Stretch.UniformToFill;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.LowQuality);
            _timer.Tick += (s, e) => Next();
            Loaded += (s, e) => { if (!_timer.IsEnabled) Next(); };
            Unloaded += (s, e) => _timer.Stop();
        }

        public void Pause() => _timer.Stop();
        public void Play() { if (!_timer.IsEnabled && IsLoaded) Next(); }

        static T Read<T>(BitmapMetadata m, string q, T def)
        {
            try { return m != null && m.GetQuery(q) is T v ? v : def; } catch { return def; }
        }

        void Next()
        {
            _timer.Stop();
            try
            {
                if (_dec.Frames.Count == 0) return;
                // Что было с прошлым кадром: 2 — стереть его место, 3 — вернуть то, что было под ним
                if (_prev.disposal == 2) Clear(_prev.x, _prev.y, _prev.w, _prev.h);
                else if (_prev.disposal == 3 && _saved != null) Buffer.BlockCopy(_saved, 0, _buf, 0, _buf.Length);
                if (_index == 0) Array.Clear(_buf, 0, _buf.Length);

                var f = _dec.Frames[_index];
                var m = f.Metadata as BitmapMetadata;
                int x = Read<ushort>(m, "/imgdesc/Left", 0), y = Read<ushort>(m, "/imgdesc/Top", 0);
                int delay = Read<ushort>(m, "/grctlext/Delay", 10) * 10;
                int disposal = Read<byte>(m, "/grctlext/Disposal", 0);
                if (disposal == 3) { _saved ??= new byte[_buf.Length]; Buffer.BlockCopy(_buf, 0, _saved, 0, _buf.Length); }

                var src = new FormatConvertedBitmap(f, PixelFormats.Bgra32, null, 0);
                int fw = src.PixelWidth, fh = src.PixelHeight;
                if (_px == null || _px.Length < fw * fh * 4) _px = new byte[fw * fh * 4];   // один буфер на все кадры
                var px = _px;
                src.CopyPixels(px, fw * 4, 0);
                for (int r = 0; r < fh; r++)
                {
                    int ty = y + r;
                    if (ty < 0 || ty >= _h) continue;
                    for (int c = 0; c < fw; c++)
                    {
                        int tx = x + c;
                        if (tx < 0 || tx >= _w) continue;
                        int si = (r * fw + c) * 4;
                        if (px[si + 3] == 0) continue;   // прозрачный пиксель GIF — под ним остаётся прошлый кадр
                        int di = (ty * _w + tx) * 4;
                        _buf[di] = px[si]; _buf[di + 1] = px[si + 1]; _buf[di + 2] = px[si + 2]; _buf[di + 3] = 255;
                    }
                }
                _canvas.WritePixels(new Int32Rect(0, 0, _w, _h), _buf, _w * 4, 0);
                _prev = (x, y, fw, fh, disposal);
                _index = (_index + 1) % _dec.Frames.Count;
                _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(20, (delay <= 10 ? 100 : delay) / Math.Max(0.1, Speed)));
                if (IsLoaded) _timer.Start();
            }
            catch (Exception e) { Settings.Log(e); }
        }

        void Clear(int x, int y, int w, int h)
        {
            for (int r = Math.Max(0, y); r < Math.Min(_h, y + h); r++)
                Array.Clear(_buf, (r * _w + Math.Max(0, x)) * 4, Math.Max(0, Math.Min(_w, x + w) - Math.Max(0, x)) * 4);
        }
    }
}
