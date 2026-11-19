// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace MizuEngine
{
    /// <summary>
    /// Mizu Plus в главном окне: вкладки «Mizu Plus» и «Живые обои», фон из видео и живые темы,
    /// эффекты Plus на странице «Эффекты» и поздравление с эффектом и звуком, когда подписка выдана.
    /// </summary>
    public partial class MainWindow
    {
        PlusPage _plusPage;
        LivePage _livePage;
        LiveBgPage _liveBgPage;

        void InitPlus()
        {
            _plusPage = new PlusPage(PagePlus, _s, _fx, ShowToast, ReapplyThemeOnly, () => NavWallpapers.IsChecked = true, () => NavProfile.IsChecked = true);
            _livePage = new LivePage(PageLive, _s, ShowToast);
            _liveBgPage = new LiveBgPage(PageLiveBg, _s, ShowToast, ReapplyThemeOnly);
            UI.OpenPlus = OpenPlusTab;
            UI.OpenLiveBg = () => { if (NavLiveBg.IsChecked == true) _liveBgPage.Show(); else NavLiveBg.IsChecked = true; };
            SocialPages.AppSettings = _s;   // админ публикует текущие настройки как сборку от MizuEngine
            AddPlusWeatherPills();
            SourceInitialized += (o, e) => UpdateBackdropLayer();
            StateChanged += (o, e) =>
            {
                // Свернули окно — видео-фон на паузе
                bool min = WindowState == WindowState.Minimized;
                if (_video != null) { if (min) _video.Pause(); else _video.Play(); }
                if (_gif != null) { if (min) _gif.Pause(); else _gif.Play(); }
            };
            Plus.Changed += OnPlusChanged;
            Closed += (o, e) =>
            {
                Plus.Changed -= OnPlusChanged;
                if (UI.OpenPlus == (Action)OpenPlusTab) UI.OpenPlus = null;
                var grid = Root.Child as Grid;
                if (grid != null) { RemoveVideo(grid); RemoveLive(grid); }
            };
        }

        void OpenPlusTab()
        {
            if (NavPlus.IsChecked == true) _plusPage.Show();
            else NavPlus.IsChecked = true;
        }

        /// <summary>Подписка появилась или закончилась: перекрашиваем окно и перестраиваем открытые вкладки.</summary>
        void OnPlusChanged()
        {
            Themes.Apply(_s.Theme, this);
            UpdateBackdropLayer();
            _plusPage.Refresh();
            _livePage.Refresh();
            _liveBgPage.Refresh();
            _custom.Refresh();
            if (NavProfile.IsChecked == true) OnPageShown("NavProfile");
        }

        /// <summary>Сборка Plus от MizuEngine применена — перечитываем все настройки.</summary>
        internal void ReapplyAllPublic() => ReapplyAll();

        /// <summary>Только тема окна (фон, видео, живая тема) — с плавным переходом.</summary>
        void ReapplyThemeOnly()
        {
            Themes.VideoFile = _s.VideoTheme;
            CrossFade(() => { Themes.Apply(_s.Theme, this); UpdateBackdropLayer(); });
        }

        // ================== фон: видео и живые темы ==================

        MediaElement _video;
        Border _videoShade;
        string _videoFile;
        Canvas _liveLayer;
        string _liveName;

        /// <summary>Ставит или убирает слой фона под цвет текущей темы (тема уже применена в Themes).</summary>
        void UpdateBackdropLayer()
        {
            if (Root.Child is not Grid grid) return;
            string t = Themes.Current;
            if (t == "video") EnsureVideo(grid); else RemoveVideo(grid);
            if (Themes.IsLive(t)) EnsureLive(grid, t); else RemoveLive(grid);
        }

        internal void SetVideoDim(int percent)
        {
            double d = Math.Clamp(percent, 0, 80) / 100.0;
            if (_videoShade != null) _videoShade.Opacity = d;
            if (_liveLayer != null) _liveLayer.Opacity = 1 - d * 0.9;   // у готовых живых фонов «затемнение» — приглушение облаков
        }

        /// <summary>Скорость живого фона: видео и GIF играют быстрее/медленнее, готовые фоны плывут быстрее/медленнее.</summary>
        internal void SetLiveSpeed(double speed)
        {
            if (_video != null) try { _video.SpeedRatio = speed; } catch { }
            if (_gif != null) _gif.Speed = speed;
            if (_liveLayer != null && Root.Child is Grid grid)
            {
                string name = _liveName;
                RemoveLive(grid);
                EnsureLive(grid, name);
            }
        }

        GifView _gif;

        void EnsureVideo(Grid grid)
        {
            if ((_video != null || _gif != null) && _videoFile == _s.VideoTheme) return;
            RemoveVideo(grid);
            try
            {
                _videoFile = _s.VideoTheme;
                if (string.Equals(System.IO.Path.GetExtension(_videoFile), ".gif", StringComparison.OrdinalIgnoreCase))
                {
                    // GIF: свой проигрыватель (MediaElement гифки не анимирует)
                    _gif = new GifView(_videoFile) { IsHitTestVisible = false, Speed = _s.LiveBgSpeed };
                    _videoShade = new Border { Background = Brushes.Black, Opacity = Math.Clamp(_s.VideoDim, 0, 80) / 100.0, IsHitTestVisible = false };
                    Grid.SetColumnSpan(_gif, 2);
                    Grid.SetColumnSpan(_videoShade, 2);
                    grid.Children.Insert(0, _videoShade);
                    grid.Children.Insert(0, _gif);
                    return;
                }
                _video = new MediaElement
                {
                    Source = new Uri(_videoFile), IsMuted = true, Stretch = Stretch.UniformToFill, IsHitTestVisible = false,
                    LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close
                };
                var v = _video;
                v.MediaEnded += (o, e) => { v.Position = TimeSpan.Zero; v.Play(); };
                v.MediaFailed += (o, e) => Settings.Log(new Exception("video theme: " + e.ErrorException?.Message));
                _videoShade = new Border { Background = Brushes.Black, Opacity = Math.Clamp(_s.VideoDim, 0, 80) / 100.0, IsHitTestVisible = false };
                Grid.SetColumnSpan(_video, 2);
                Grid.SetColumnSpan(_videoShade, 2);
                grid.Children.Insert(0, _videoShade);
                grid.Children.Insert(0, _video);
                v.MediaOpened += (o, e) => { try { v.SpeedRatio = _s.LiveBgSpeed; } catch { } };
                v.Play();
            }
            catch (Exception e) { Settings.Log(e); RemoveVideo(grid); }
        }

        void RemoveVideo(Grid grid)
        {
            if (_video != null)
            {
                try { _video.Stop(); _video.Close(); } catch { }
                grid.Children.Remove(_video);
                _video = null;
            }
            if (_gif != null) { _gif.Pause(); grid.Children.Remove(_gif); _gif = null; }
            if (_videoShade != null) { grid.Children.Remove(_videoShade); _videoShade = null; }
            _videoFile = null;
        }

        /// <summary>Живая тема: три светящихся «облака» цвета темы медленно плывут по окну (24 кадра в секунду — почти без нагрузки).</summary>
        void EnsureLive(Grid grid, string name)
        {
            if (_liveLayer != null && _liveName == name) return;
            RemoveLive(grid);
            _liveName = name;
            var blobs = Themes.LiveColors[name].blobs;
            var layer = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
            Grid.SetColumnSpan(layer, 2);
            var start = new[] { (-0.2, -0.3), (0.45, 0.2), (0.0, 0.45) };
            var items = new List<(Ellipse e, TranslateTransform t, double fx, double fy)>();
            for (int i = 0; i < 3; i++)
            {
                var c = (Color)ColorConverter.ConvertFromString(blobs[i]);
                var br = new RadialGradientBrush();
                br.GradientStops.Add(new GradientStop(Color.FromArgb(0x9A, c.R, c.G, c.B), 0));
                br.GradientStops.Add(new GradientStop(Color.FromArgb(0x38, c.R, c.G, c.B), 0.45));
                br.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, c.R, c.G, c.B), 1));
                br.Freeze();
                var t = new TranslateTransform();
                var e = new Ellipse { Fill = br, RenderTransform = t };
                layer.Children.Add(e);
                items.Add((e, t, start[i].Item1, start[i].Item2));
            }

            void Layout()
            {
                double w = layer.ActualWidth, h = layer.ActualHeight;
                if (w < 1 || h < 1) return;
                double size = Math.Max(w, h) * 0.9;
                for (int i = 0; i < items.Count; i++)
                {
                    var (e, t, fx, fy) = items[i];
                    e.Width = size;
                    e.Height = size;
                    Canvas.SetLeft(e, fx * w);
                    Canvas.SetTop(e, fy * h);
                    double sp = Math.Clamp(_s.LiveBgSpeed, 0.25, 2);
                    t.BeginAnimation(TranslateTransform.XProperty, Drift(w * 0.2, (14 + i * 5) / sp));
                    t.BeginAnimation(TranslateTransform.YProperty, Drift(h * 0.16, (18 + i * 4) / sp));
                }
            }
            layer.SizeChanged += (o, e) => Layout();
            layer.Opacity = 1 - Math.Clamp(_s.VideoDim, 0, 80) / 100.0 * 0.9;
            grid.Children.Insert(0, layer);
            if (layer.ActualWidth > 0) Layout();
            _liveLayer = layer;
        }

        static DoubleAnimation Drift(double amp, double seconds)
        {
            var a = new DoubleAnimation(-amp, amp, TimeSpan.FromSeconds(seconds))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Timeline.SetDesiredFrameRate(a, 24);
            return a;
        }

        void RemoveLive(Grid grid)
        {
            if (_liveLayer == null) return;
            foreach (var child in _liveLayer.Children)
                if (child is Ellipse e && e.RenderTransform is TranslateTransform t)
                {
                    t.BeginAnimation(TranslateTransform.XProperty, null);
                    t.BeginAnimation(TranslateTransform.YProperty, null);
                }
            grid.Children.Remove(_liveLayer);
            _liveLayer = null;
            _liveName = null;
        }

        // ================== эффекты Plus на странице «Эффекты» ==================

        /// <summary>Эффекты Plus видны всем (со значком Plus). Без подписки нажатие ведёт на вкладку Mizu Plus.</summary>
        void AddPlusWeatherPills()
        {
            foreach (var w in Settings.PlusWeathers)
            {
                string weather = w;
                var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
                label.SetResourceReference(TextBlock.TextProperty, "w." + weather);
                var badge = UI.PlusBadge(14, clickable: false);
                badge.Margin = new Thickness(6, 0, 0, 0);
                var pill = new RadioButton
                {
                    Style = (Style)FindResource("Pill"), GroupName = "weather", Tag = weather,
                    Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { label, badge } },
                    IsChecked = _s.Weather == weather
                };
                pill.Click += (o, e) =>
                {
                    if (!Plus.IsActive)
                    {
                        _loading = true;
                        LoadSnowControls();   // выбор возвращается на прежний эффект
                        _loading = false;
                        ShowToast(Loc.T("plus.need"));
                        OpenPlusTab();
                        return;
                    }
                    Weather_Click(pill, e);
                };
                WeatherPanel.Children.Add(pill);
            }
        }

        // ================== поздравление ==================

        Grid _celebration;
        EventHandler _confettiTick;

        /// <summary>
        /// Подписка выдана: затемнение, большой значок Plus «прилетает» с пружиной, по кругу расходится свечение,
        /// из центра вылетает конфетти, звучит светлое арпеджио. Пользователь сразу видит, что Plus включён.
        /// </summary>
        internal void CelebratePlus(PlusGrant g)
        {
            CloseCelebration(instant: true);
            UiSounds.Play("plus_grant");

            var overlay = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x05, 0x06, 0x12)), Opacity = 0 };
            var confetti = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
            overlay.Children.Add(confetti);

            // Расходящееся свечение за значком
            var ringBrush = UI.PlusBrush();
            Ellipse Ring()
            {
                var r = new Ellipse
                {
                    Width = 150, Height = 150, Stroke = ringBrush, StrokeThickness = 3, IsHitTestVisible = false, Opacity = 0,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(0.6, 0.6), Margin = new Thickness(0, 0, 0, 230)
                };
                overlay.Children.Add(r);
                return r;
            }
            var ring1 = Ring();
            var ring2 = Ring();

            // Карточка
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var badge = UI.PlusBadge(118, big: true, clickable: false);
            badge.HorizontalAlignment = HorizontalAlignment.Center;
            var scale = new ScaleTransform(0.2, 0.2);
            var spin = new RotateTransform(-30);
            badge.RenderTransformOrigin = new Point(0.5, 0.5);
            badge.RenderTransform = new TransformGroup { Children = { scale, spin } };
            sp.Children.Add(badge);

            var title = new TextBlock { Text = Loc.T("plus.congrats"), FontSize = 30, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 0), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
            sp.Children.Add(title);
            string when = g.Forever ? Loc.T("plus.congrats.forever") : string.Format(Loc.T("plus.congrats.until"), g.Until.ToLocalTime().ToString("d MMMM yyyy"));
            sp.Children.Add(new TextBlock { Text = when, FontSize = 15, Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xD6, 0xFE)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
            if (g.Source == "admin")
                sp.Children.Add(new TextBlock { Text = Loc.T("plus.congrats.admin"), FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromArgb(0xC0, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });

            var chips = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0), MaxWidth = 470 };
            foreach (var key in new[] { "plus.chip.video", "plus.chip.live", "plus.chip.accounts", "plus.chip.custom", "plus.chip.badge" })
                chips.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(12), Padding = new Thickness(11, 5, 11, 6), Margin = new Thickness(4),
                    Background = new SolidColorBrush(Color.FromArgb(0x40, 0x8B, 0x5C, 0xF6)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, 0xC4, 0xB5, 0xFD)), BorderThickness = new Thickness(1),
                    Child = new TextBlock { Text = Loc.T(key), Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.SemiBold }
                });
            sp.Children.Add(chips);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 0) };
            var setup = new Button { Style = (Style)FindResource("PrimaryButton"), Content = Loc.T("plus.congrats.setup"), Margin = new Thickness(0, 0, 10, 0) };
            setup.Click += (o, e) => { CloseCelebration(); OpenPlusTab(); };
            var close = new Button { Style = (Style)FindResource("GlassButton"), Content = Loc.T("plus.congrats.close"), Padding = new Thickness(18, 10, 18, 10) };
            close.Click += (o, e) => CloseCelebration();
            buttons.Children.Add(setup);
            buttons.Children.Add(close);
            sp.Children.Add(buttons);

            var cardBg = new LinearGradientBrush(Color.FromArgb(0xF2, 0x1A, 0x14, 0x33), Color.FromArgb(0xF2, 0x0E, 0x10, 0x1E), 90);
            var card = new Border
            {
                Child = sp, CornerRadius = new CornerRadius(30), Padding = new Thickness(44, 36, 44, 32), MaxWidth = 560,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Background = cardBg, BorderBrush = ringBrush, BorderThickness = new Thickness(1.5),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = UI.PlusMid, BlurRadius = 60, ShadowDepth = 0, Opacity = 0.7 },
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(0.85, 0.85)
            };
            overlay.Children.Add(card);
            Shell.Children.Add(overlay);
            _celebration = overlay;

            // ---------- анимации ----------
            Anim.To(overlay, OpacityProperty, 1, 320);
            var cs = (ScaleTransform)card.RenderTransform;
            Anim.To(cs, ScaleTransform.ScaleXProperty, 1, 650, ease: Anim.Spring);
            Anim.To(cs, ScaleTransform.ScaleYProperty, 1, 650, ease: Anim.Spring);
            Anim.To(scale, ScaleTransform.ScaleXProperty, 1, 900, delayMs: 120, ease: Anim.Spring);
            Anim.To(scale, ScaleTransform.ScaleYProperty, 1, 900, delayMs: 120, ease: Anim.Spring);
            Anim.To(spin, RotateTransform.AngleProperty, 0, 900, delayMs: 120, ease: Anim.Spring);
            Pulse(ring1, 250);
            Pulse(ring2, 700);
            StartConfetti(confetti);
        }

        static void Pulse(Ellipse ring, int delayMs)
        {
            var st = (ScaleTransform)ring.RenderTransform;
            var grow = new DoubleAnimation(0.6, 3.2, TimeSpan.FromMilliseconds(1300)) { BeginTime = TimeSpan.FromMilliseconds(delayMs), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            var fade = new DoubleAnimation(0.95, 0, TimeSpan.FromMilliseconds(1300)) { BeginTime = TimeSpan.FromMilliseconds(delayMs) };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            ring.BeginAnimation(OpacityProperty, fade);
        }

        /// <summary>Конфетти цветов Plus разлетается из центра и падает (3,5 секунды), потом всё убирается.</summary>
        void StartConfetti(Canvas canvas)
        {
            var rnd = new Random();
            Color[] colors =
            {
                UI.PlusLight, UI.PlusMid, Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xFF, 0xD2, 0x4D),
                Color.FromRgb(0xFF, 0x8F, 0xB8), Color.FromRgb(0x7D, 0xF9, 0xFF)
            };
            var parts = new List<(Rectangle r, RotateTransform rot, double[] s)>();   // s: x, y, vx, vy, vr
            var clock = Stopwatch.StartNew();
            double last = 0;
            bool spawned = false;

            void Spawn()
            {
                double cx = canvas.ActualWidth / 2, cy = canvas.ActualHeight / 2 - 90;
                for (int i = 0; i < 150; i++)
                {
                    double a = -Math.PI / 2 + (rnd.NextDouble() - 0.5) * Math.PI * 1.7, sp = 320 + rnd.NextDouble() * 720;
                    var rot = new RotateTransform(rnd.NextDouble() * 360);
                    var r = new Rectangle
                    {
                        Width = 5 + rnd.NextDouble() * 5, Height = 8 + rnd.NextDouble() * 7, RadiusX = 1.5, RadiusY = 1.5,
                        Fill = new SolidColorBrush(colors[rnd.Next(colors.Length)]), RenderTransform = rot, RenderTransformOrigin = new Point(0.5, 0.5)
                    };
                    canvas.Children.Add(r);
                    parts.Add((r, rot, new[] { cx, cy, Math.Cos(a) * sp, Math.Sin(a) * sp, (rnd.NextDouble() - 0.5) * 900 }));
                }
            }

            _confettiTick = (o, e) =>
            {
                if (!spawned)
                {
                    if (canvas.ActualWidth < 1) return;   // ждём, пока слой получит размер
                    spawned = true;
                    Spawn();
                }
                double now = clock.Elapsed.TotalSeconds, dt = Math.Min(0.05, now - last);
                last = now;
                foreach (var (r, rot, s) in parts)
                {
                    s[2] *= 1 - 1.4 * dt;              // сопротивление воздуха
                    s[3] += 820 * dt;                  // гравитация
                    s[3] *= 1 - 0.9 * dt;
                    s[0] += s[2] * dt;
                    s[1] += s[3] * dt;
                    rot.Angle += s[4] * dt;
                    Canvas.SetLeft(r, s[0]);
                    Canvas.SetTop(r, s[1]);
                    if (now > 2.6) r.Opacity = Math.Max(0, 1 - (now - 2.6) / 0.9);
                }
                if (now > 3.5) StopConfetti(canvas);
            };
            CompositionTarget.Rendering += _confettiTick;
        }

        void StopConfetti(Canvas canvas)
        {
            if (_confettiTick != null) { CompositionTarget.Rendering -= _confettiTick; _confettiTick = null; }
            canvas?.Children.Clear();
        }

        void CloseCelebration(bool instant = false)
        {
            var c = _celebration;
            if (c == null) return;
            _celebration = null;
            StopConfetti(c.Children.Count > 0 ? c.Children[0] as Canvas : null);
            if (instant) { Shell.Children.Remove(c); return; }
            c.IsHitTestVisible = false;
            Anim.To(c, OpacityProperty, 0, 260, done: () => Shell.Children.Remove(c));
        }
    }
}
