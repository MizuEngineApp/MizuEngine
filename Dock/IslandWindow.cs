// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>
    /// Dynamic Island, как на новом MacBook Pro: чёрная капсула вверху по центру экрана.
    /// В покое — маленькая; играет музыка — обложка и «эквалайзер»; наведи мышь — плеер с кнопками и прогрессом;
    /// новое сообщение MizuEngine — аватарка и текст. Плееры, таймеры и сам MizuEngine можно сворачивать
    /// прямо в неё (эффект «джинн» вверх), а их значки ждут внутри капсулы.
    /// Окно прозрачное, клики принимает только сама капсула.
    /// </summary>
    internal sealed class IslandWindow : Window
    {
        readonly Settings _s;
        readonly IslandView _view;
        readonly DispatcherTimer _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        readonly WindowTracker _fullscreen = new WindowTracker();
        IntPtr _h;
        Native.RECT _rgnPx;
        bool _shown;

        const double WinW = 480, WinH = 200;

        public IslandWindow(Settings s)
        {
            _s = s;
            Glass.Setup(this, clickThrough: false);
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            Title = "MizuEngine Island";
            _view = new IslandView(s);
            Content = _view;
            _view.ShapeChanged += UpdateRegion;
            SourceInitialized += (o, e) => { _h = new WindowInteropHelper(this).Handle; UpdateRegion(); };
            _tick.Tick += (o, e) => OnTick();
            MediaWatcher.Changed += () => _view.SetMedia(MediaWatcher.Current);
        }

        // ================== настройки (можно звать из любого потока) ==================

        public void Apply()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(Apply)); return; }
            if (_s.IslandOn)
            {
                Place();
                if (!_shown) { Show(); _shown = true; }
                _tick.Start();
                if (_s.IslandMedia) MediaWatcher.Start(Dispatcher);
                _view.SetMedia(_s.IslandMedia ? MediaWatcher.Current : null);
            }
            else
            {
                _tick.Stop();
                if (_shown) { Hide(); _shown = false; }
            }
            _view.Refresh();
            Shell.SyncAnimations();
        }

        public void Shutdown()
        {
            if (!Dispatcher.CheckAccess()) { try { Dispatcher.Invoke(new Action(Shutdown), TimeSpan.FromSeconds(2)); } catch { } return; }
            _tick.Stop();
            if (_shown) { Hide(); _shown = false; }
        }

        void Place()
        {
            Left = (SystemParameters.PrimaryScreenWidth - WinW) / 2;
            Top = 0;
            Width = WinW;
            Height = WinH;
        }

        /// <summary>
        /// Где живёт капсула (вкладка «Dynamic Island» → «Когда видна»):
        /// top — поверх окон, прячется в полноэкранных играх и видео (как раньше);
        /// always — всегда поверх, даже в играх (CS2 и т. п. в режиме «в окне без рамки»);
        /// desktop — на рабочем столе: окна программ её закрывают; на время уведомления она выходит наверх.
        /// </summary>
        string Layer => _s.IslandLayer == "always" || _s.IslandLayer == "desktop" ? _s.IslandLayer : "top";

        void OnTick()
        {
            string layer = Layer;
            if (_view.Animating && DateTime.UtcNow > _animUntil) _view.Animating = false;
            // В полноэкранной игре или видео капсула прячется (кроме режима «всегда»)
            bool fs = layer == "top" && _fullscreen.ForegroundIsFullscreen(_h, Native.GetSystemMetrics(0), Native.GetSystemMetrics(1));
            _view.Fullscreen = fs;
            _view.CleanupParked();
            _view.Refresh();
            ApplyLayer();
        }

        bool _onTop = true;

        /// <summary>Поставить капсулу на нужный «этаж»: поверх всех окон или прямо над рабочим столом.</summary>
        void ApplyLayer()
        {
            if (!_shown || _h == IntPtr.Zero) return;
            const uint flags = DockNative.SWP_NOMOVE | DockNative.SWP_NOSIZE | DockNative.SWP_NOACTIVATE;
            bool top = Layer != "desktop" || _view.Notifying || _view.Animating;
            if (top)
            {
                if (!_onTop) { Topmost = true; _onTop = true; }
                // Другие «поверх всех» окна могли перекрыть — поднимаемся снова
                DockNative.SetWindowPos(_h, new IntPtr(-1), 0, 0, 0, 0, flags);
                return;
            }
            if (_onTop) { Topmost = false; _onTop = false; DockNative.SetWindowPos(_h, new IntPtr(-2), 0, 0, 0, 0, flags); }
            // Сразу над рабочим столом: под окно, которое лежит прямо над Progman (рабочий стол с ярлыками)
            IntPtr desk = FindWindowW("Progman", null);
            if (desk == IntPtr.Zero) return;
            IntPtr above = GetWindow(desk, 3);   // GW_HWNDPREV
            if (above == _h || above == IntPtr.Zero) return;
            DockNative.SetWindowPos(_h, above, 0, 0, 0, 0, flags);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr FindWindowW(string cls, string title);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr GetWindow(IntPtr h, uint cmd);

        void UpdateRegion()
        {
            if (_h == IntPtr.Zero) return;
            Rect pill = _view.PillRect;
            var px = DockWindow.ToPixels(this, pill);
            if (px.Left == _rgnPx.Left && px.Top == _rgnPx.Top && px.Right == _rgnPx.Right && px.Bottom == _rgnPx.Bottom) return;
            _rgnPx = px;
            DockNative.SetWindowRgn(_h, DockWindow.RoundRegion(this, pill, _view.Radius), true);
        }

        // ================== для анимаций окон ==================

        /// <summary>Поднять капсулу над окном анимации: окно втягивается ПОД неё.</summary>
        public void RaiseAbove()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(RaiseAbove)); return; }
            _view.Animating = true;   // на время «джинна» капсула наверху даже в режиме «на рабочем столе»
            _animUntil = DateTime.UtcNow.AddSeconds(1.5);
            if (_shown && _h != IntPtr.Zero)
            {
                if (!_onTop) { Topmost = true; _onTop = true; }
                DockNative.SetWindowPos(_h, new IntPtr(-1), 0, 0, 0, 0, DockNative.SWP_NOMOVE | DockNative.SWP_NOSIZE | DockNative.SWP_NOACTIVATE);
            }
        }

        DateTime _animUntil;

        /// <summary>Окна сворачиваются в капсулу (настройка «вверх») — только когда капсула включена.</summary>
        public bool FliesUp => _s.IslandOn && _s.IslandFly == "up";

        static readonly HashSet<string> Players = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "spotify", "vlc", "aimp", "foobar2000", "musicbee", "winamp", "itunes", "applemusic", "yandex music", "yandexmusic",
            "potplayermini64", "potplayermini", "potplayer", "mpc-hc64", "mpc-hc", "mpc-be64", "mpc-be", "wmplayer", "deezer",
            "tidal", "youtube music", "ytmdesktop", "soundcloud", "music.ui", "microsoft.media.player", "time", "vk messenger"
        };

        /// <summary>Плеер, таймер или сам MizuEngine — такие окна улетают в капсулу.</summary>
        public bool IsIslandApp(IntPtr hwnd)
        {
            DockNative.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == (uint)Environment.ProcessId) return true;   // MizuEngine
            string exe = DockNative.ExePath(pid);
            if (string.IsNullOrEmpty(exe)) return false;
            string name = Path.GetFileNameWithoutExtension(exe);
            if (Players.Contains(name)) return true;
            string aumid = DockApps.AumidOfWindow(hwnd);
            if (aumid != null && (aumid.Contains("ZuneMusic") || aumid.Contains("WindowsAlarms") || aumid.Contains("Spotify")
                                  || aumid.Contains("MediaPlayer") || aumid.Contains("AppleMusic"))) return true;
            // Программа, которая прямо сейчас играет музыку (например, браузер с YouTube Music)
            var media = MediaWatcher.Current;
            string src = MediaWatcher.SourceApp;
            if (media != null && media.Playing && src.Length > 0)
            {
                if (aumid != null && string.Equals(src, aumid, StringComparison.OrdinalIgnoreCase)) return true;
                if (src.Equals(name + ".exe", StringComparison.OrdinalIgnoreCase) || src.Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
                if (name.Length >= 4 && src.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        /// <summary>Где капсула на экране (пиксели) — туда втягивается окно.</summary>
        public Rect ScreenRect()
        {
            if (!_shown || _h == IntPtr.Zero) return Rect.Empty;
            Rect r = _view.FlyRect;
            try { return new Rect(_view.PointToScreen(r.TopLeft), _view.PointToScreen(r.BottomRight)); }
            catch (InvalidOperationException) { return Rect.Empty; }
        }

        /// <summary>Окно втянулось в капсулу — его значок ждёт внутри.</summary>
        public void Park(IntPtr hwnd) => _view.Park(hwnd);

        public void Unpark(IntPtr hwnd) => _view.Unpark(hwnd);

        /// <summary>Поменяли форму или пружину во вкладке «Dynamic Island» — капсула перетекает в новый размер.</summary>
        public void Reshape()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(Reshape)); return; }
            _view.Refresh();
        }

        /// <summary>Кнопка «Проверить прыжок»: капсула подпрыгивает так же, как когда в неё влетает окно.</summary>
        public void TestBounce()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(TestBounce)); return; }
            _view.TestBounce();
        }

        /// <summary>Сообщение MizuEngine: капсула раскрывается с аватаркой и текстом.</summary>
        /// <summary>Можно ли сейчас показать уведомление в капсуле (включены Island и уведомления).</summary>
        public bool CanNotify => _s.IslandOn && _s.IslandNotify;

        public void Notify(string title, string text, string avatarUrl, Action click = null)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => Notify(title, text, avatarUrl, click))); return; }
            if (!_s.IslandOn || !_s.IslandNotify) return;
            _view.ShowNotification(title, text, avatarUrl, click);
            ApplyLayer();   // «на рабочем столе» — уведомление всё равно выходит поверх окон
        }

        /// <summary>Агент Muzule работает: капсула с переливающимся шаром и статусом («думает…», «меняет тему…»). null — убрать.</summary>
        public void Agent(string status, Action click = null)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => Agent(status, click))); return; }
            _view.SetAgent(_s.IslandOn ? status : null, click);
        }
    }

    /// <summary>Рисует капсулу и всё внутри неё. Размер меняется «пружинкой» — с лёгким перелётом, как в iOS.</summary>
    internal sealed class IslandView : FrameworkElement
    {
        enum Mode { Hidden, Idle, Media, Expanded, Notify, Agent }

        sealed class Parked { public IntPtr Hwnd; public ImageSource Icon; public string Name; }

        readonly Settings _s;
        public event Action ShapeChanged;
        public bool Fullscreen;
        public bool Animating;                                   // идёт «джинн» в капсулу или из неё
        public bool Notifying => DateTime.UtcNow < _notifyUntil;

        Mode _mode = Mode.Hidden, _content = Mode.Hidden;
        double _w, _h, _vw, _vh, _alpha, _contentAlpha;
        double _tw, _th;
        double _time;
        TimeSpan _last;
        bool _hooked, _hover, _expanded;
        DateTime _hoverAt, _leaveAt, _pausedAt = DateTime.MinValue, _notifyUntil = DateTime.MinValue;
        MediaInfo _media;
        readonly List<Parked> _parked = new List<Parked>();
        string _nTitle, _nText;
        string _aText;          // статус агента Muzule (null — агент не работает)
        Action _aClick;
        ImageSource _nAvatar;
        string _nAvatarUrl;
        Action _nClick;
        Rect _prev, _play, _next, _art;
        readonly List<Rect> _parkedRects = new List<Rect>();

        static readonly Brush Black = Frozen(new SolidColorBrush(Color.FromRgb(0, 0, 0)));
        static readonly Pen Rim = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x1C, 255, 255, 255)), 1));
        static readonly Brush Track = Frozen(new SolidColorBrush(Color.FromArgb(0x38, 255, 255, 255)));
        static readonly Brush White = Brushes.White;
        static readonly Brush Gray = Frozen(new SolidColorBrush(Color.FromArgb(0xA6, 255, 255, 255)));
        static readonly Brush HoverBg = Frozen(new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)));
        static readonly Typeface Text = new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        static readonly Typeface TextReg = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        static readonly Typeface Glyphs = new Typeface(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        const double TopGap = 6;

        public IslandView(Settings s)
        {
            _s = s;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            MouseEnter += (o, e) => { _hover = true; _hoverAt = DateTime.UtcNow; Wake(); };
            MouseLeave += (o, e) => { _hover = false; _leaveAt = DateTime.UtcNow; Wake(); };
            MouseMove += (o, e) => { if (_mode == Mode.Expanded) InvalidateVisual(); };
            MouseLeftButtonUp += (o, e) => Click(e.GetPosition(this));
            MouseRightButtonUp += (o, e) =>
            {
                var menu = GlassMenu.Create();
                GlassMenu.Item(menu, Loc.T("island.settings"), () => Shell.OpenApp?.Invoke());
                menu.PlacementTarget = this;
                menu.Placement = PlacementMode.Bottom;
                menu.PlacementRectangle = PillRect;
                menu.IsOpen = true;
                e.Handled = true;
            };
        }

        public Rect PillRect => _w < 1 || _h < 1 || _alpha < 0.02 ? Rect.Empty : new Rect((ActualWidth - _w) / 2, TopGap, _w, _h);
        public double Radius => IslandShape.Radius(_s, _h, _mode == Mode.Expanded || (_content == Mode.Expanded && _h > 60));

        /// <summary>Куда втягивается окно: маленькая капсула по центру (даже если сейчас она раскрыта или спрятана).</summary>
        public Rect FlyRect => new Rect(ActualWidth / 2 - _s.IslandWidth / 2.0, TopGap, _s.IslandWidth, _s.IslandHeight);

        // ================== данные ==================

        public void SetMedia(MediaInfo m)
        {
            if (_media != null && _media.Playing && (m == null || !m.Playing)) _pausedAt = DateTime.UtcNow;
            _media = m;
            Refresh();
        }

        public void ShowNotification(string title, string text, string avatarUrl, Action click)
        {
            _nTitle = title;
            _nText = text?.Replace('\n', ' ').Replace('\r', ' ');
            _nClick = click;
            _nAvatar = null;
            _nAvatarUrl = avatarUrl;
            ImageCache.Load(avatarUrl, 96, true, bmp => { if (_nAvatarUrl == avatarUrl) { _nAvatar = bmp; InvalidateVisual(); } });
            _notifyUntil = DateTime.UtcNow.AddSeconds(4.5);
            Refresh();
            Kick(40, 10);
        }

        public void SetAgent(string status, Action click)
        {
            bool was = _aText != null;
            _aText = status;
            _aClick = click;
            Refresh();
            if (!was && status != null) Kick(24, 8);   // агент начал работать — капсула «вздрагивает»
            else InvalidateVisual();
        }

        public void Park(IntPtr hwnd)
        {
            if (_parked.Exists(p => p.Hwnd == hwnd)) { Kick(26, 8); return; }
            var p = new Parked { Hwnd = hwnd };
            DockNative.GetWindowThreadProcessId(hwnd, out uint pid);
            string exe = DockNative.ExePath(pid);
            string aumid = DockApps.AumidOfWindow(hwnd);
            p.Name = aumid != null ? "" : DockApps.NameOf(exe);
            string src = aumid != null ? @"shell:AppsFolder\" + aumid : exe;
            p.Icon = DockApps.IconFor(src, img => Dispatcher.BeginInvoke(new Action(() => { p.Icon = img; InvalidateVisual(); })));
            _parked.Add(p);
            while (_parked.Count > 4) _parked.RemoveAt(0);
            Refresh();
            Kick(30, 9);   // капсула «глотает» окно
        }

        public void Unpark(IntPtr hwnd)
        {
            if (_parked.RemoveAll(p => p.Hwnd == hwnd) > 0) Refresh();
        }

        /// <summary>Окна, которые развернули другим способом или закрыли, — убираем из капсулы.</summary>
        public void CleanupParked()
        {
            if (_parked.RemoveAll(p => !DockNative.IsWindow(p.Hwnd) || !Native.IsIconic(p.Hwnd)) > 0) Refresh();
        }

        // ================== состояние ==================

        bool MediaVisible => _s.IslandMedia && _media != null &&
                             (_media.Playing || (DateTime.UtcNow - _pausedAt).TotalSeconds < 20 || (_hover && _expanded));

        Mode Decide()
        {
            if (!_s.IslandOn || Fullscreen) return Mode.Hidden;
            if (DateTime.UtcNow < _notifyUntil) return Mode.Notify;
            if (_aText != null) return Mode.Agent;
            if (MediaVisible) return _expanded ? Mode.Expanded : Mode.Media;
            if (_s.IslandHideIdle && _parked.Count == 0) return Mode.Hidden;
            return Mode.Idle;
        }

        public void Refresh()
        {
            var m = Decide();
            if (m != _mode)
            {
                _mode = m;
                if (_content != m) _contentAlpha = Math.Min(_contentAlpha, 0.3);   // старое содержимое быстро гаснет
            }
            // Размер капсулы в покое — из вкладки «Dynamic Island»; остальные режимы растут от него
            double iw = _s.IslandWidth, ih = _s.IslandHeight;
            (_tw, _th) = m switch
            {
                Mode.Hidden => (iw * 0.7, ih * 0.75),
                Mode.Idle => (_parked.Count == 0 ? iw : Math.Max(iw, iw - 22 + _parked.Count * 26.0), ih),
                Mode.Media => (Math.Max(304.0, iw + 150), Math.Max(34.0, ih + 2)),
                Mode.Expanded => (420.0, 160.0),
                Mode.Agent => (Math.Max(300.0, iw + 150), Math.Max(36.0, ih + 4)),
                _ => (384.0, Math.Max(72.0, ih + 40))
            };
            Wake();
        }

        void Kick(double w, double h)
        {
            double k = IslandShape.Kick(_s);   // сила подпрыгивания из вкладки «Dynamic Island» (0 — без прыжка)
            _vw += w * 6 * k;
            _vh += h * 6 * k;
            Wake();
        }

        public void TestBounce() => Kick(30, 9);

        void Wake()
        {
            if (_hooked) return;
            _hooked = true;
            _last = TimeSpan.Zero;
            CompositionTarget.Rendering += OnFrame;
        }

        void Sleep()
        {
            if (!_hooked) return;
            _hooked = false;
            CompositionTarget.Rendering -= OnFrame;
        }

        void OnFrame(object sender, EventArgs e)
        {
            var now = ((RenderingEventArgs)e).RenderingTime;
            if (now == _last) return;
            double dt = _last == TimeSpan.Zero ? 1.0 / 60 : Math.Min(1.0 / 30, (now - _last).TotalSeconds);
            _last = now;
            _time += dt;

            // Наведение: плеер раскрывается через мгновение, закрывается, когда мышь ушла
            var t = DateTime.UtcNow;
            if (_mode == Mode.Media && _hover && (t - _hoverAt).TotalSeconds > 0.25) { _expanded = true; Refresh(); }
            if (_expanded && !_hover && (t - _leaveAt).TotalSeconds > 0.6) { _expanded = false; Refresh(); }
            if (_mode == Mode.Notify && t >= _notifyUntil) Refresh();

            bool moving = IslandShape.Step(ref _w, ref _vw, _tw, dt, _s) | IslandShape.Step(ref _h, ref _vh, _th, dt, _s);
            moving |= Approach(ref _alpha, _mode == Mode.Hidden ? 0 : 1, dt, 10);
            bool near = Math.Abs(_w - _tw) < 14 && Math.Abs(_h - _th) < 8;
            if (_content != _mode && _contentAlpha < 0.05) { _content = _mode; }
            moving |= Approach(ref _contentAlpha, _content == _mode && near ? 1 : 0, dt, _content == _mode ? 9 : 22);

            InvalidateVisual();
            ShapeChanged?.Invoke();

            // Музыка играет — «эквалайзер» двигается; иначе, когда всё успокоилось, кадры не считаем
            bool live = (_media?.Playing == true && (_mode == Mode.Media || _mode == Mode.Expanded)) || _hover || _mode == Mode.Notify || _mode == Mode.Agent;
            if (!moving && !live) Sleep();
        }

        static bool Approach(ref double v, double target, double dt, double speed)
        {
            if (Math.Abs(v - target) < 0.004) { if (v != target) { v = target; return true; } return false; }
            v += (target - v) * (1 - Math.Exp(-dt * speed));
            return true;
        }

        // ================== клики ==================

        void Click(Point p)
        {
            switch (_mode)
            {
                case Mode.Notify:
                    _notifyUntil = DateTime.MinValue;
                    _nClick?.Invoke();
                    Refresh();
                    return;
                case Mode.Agent:
                    _aClick?.Invoke();
                    return;
                case Mode.Media:
                    _expanded = true;
                    _hoverAt = DateTime.UtcNow;
                    Refresh();
                    return;
                case Mode.Expanded:
                    if (_prev.Contains(p)) MediaWatcher.Previous();
                    else if (_play.Contains(p)) { MediaWatcher.PlayPause(); if (_media != null) { _media.Playing = !_media.Playing; _pausedAt = DateTime.UtcNow; } }
                    else if (_next.Contains(p)) MediaWatcher.Next();
                    else if (_art.Contains(p)) OpenMediaApp();
                    InvalidateVisual();
                    return;
                case Mode.Idle:
                    for (int i = 0; i < _parkedRects.Count && i < _parked.Count; i++)
                        if (_parkedRects[i].Contains(p)) { RestoreParked(_parked[i].Hwnd); return; }
                    if (_parked.Count == 1) RestoreParked(_parked[0].Hwnd);
                    return;
            }
        }

        /// <summary>Окно, которое ждёт в капсуле, «вытекает» обратно вниз.</summary>
        void RestoreParked(IntPtr hwnd)
        {
            Unpark(hwnd);
            if (!DockNative.IsWindow(hwnd)) return;
            var r = Shell.Island?.ScreenRect() ?? Rect.Empty;
            if (Native.IsIconic(hwnd) && !r.IsEmpty && Shell.Anim != null) Shell.Anim.Restore(hwnd, r, true);
            else WindowAnimator.BringToFront(hwnd);
        }

        /// <summary>Клик по обложке: показать плеер (если он свёрнут в капсулу — он вылетает из неё).</summary>
        void OpenMediaApp()
        {
            string src = MediaWatcher.SourceApp;
            if (src.Length == 0) return;
            foreach (var w in DockApps.EnumRunning())
            {
                string name = Path.GetFileNameWithoutExtension(w.Exe ?? "");
                bool match = (w.Aumid != null && string.Equals(w.Aumid, src, StringComparison.OrdinalIgnoreCase))
                             || src.Equals(name + ".exe", StringComparison.OrdinalIgnoreCase)
                             || (name.Length >= 4 && src.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!match) continue;
                if (Native.IsIconic(w.Hwnd)) RestoreParked(w.Hwnd);
                else WindowAnimator.BringToFront(w.Hwnd);
                return;
            }
        }

        // ================== отрисовка ==================

        protected override void OnRender(DrawingContext dc)
        {
            Rect pill = PillRect;
            if (pill.IsEmpty) return;
            double r = Radius;
            dc.PushOpacity(_alpha);
            dc.DrawRoundedRectangle(Black, Rim, pill, r, r);

            if (_contentAlpha > 0.01)
            {
                dc.PushClip(new RectangleGeometry(pill, r, r));
                dc.PushOpacity(_contentAlpha);
                double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                switch (_content)
                {
                    case Mode.Idle: DrawIdle(dc, pill); break;
                    case Mode.Media: DrawCompact(dc, pill); break;
                    case Mode.Expanded: DrawExpanded(dc, pill, dpi); break;
                    case Mode.Notify: DrawNotify(dc, pill, dpi); break;
                    case Mode.Agent: DrawAgent(dc, pill, dpi); break;
                }
                dc.Pop();
                dc.Pop();
            }
            dc.Pop();
        }

        void DrawIdle(DrawingContext dc, Rect pill)
        {
            _parkedRects.Clear();
            if (_parked.Count == 0) return;
            double s = 20, gap = 6, total = _parked.Count * s + (_parked.Count - 1) * gap;
            double x = pill.X + (pill.Width - total) / 2, y = pill.Y + (pill.Height - s) / 2;
            foreach (var p in _parked)
            {
                var rect = new Rect(x, y, s, s);
                if (p.Icon != null) dc.DrawImage(p.Icon, rect);
                else dc.DrawRoundedRectangle(Track, null, rect, 5, 5);
                _parkedRects.Add(new Rect(x - 3, pill.Y, s + 6, pill.Height));
                x += s + gap;
            }
        }

        void DrawCompact(DrawingContext dc, Rect pill)
        {
            if (_media == null) return;
            double s = pill.Height - 12;
            var art = new Rect(pill.X + 8, pill.Y + 6, s, s);
            DrawArt(dc, art, 6);
            DrawBars(dc, new Rect(pill.Right - 30, pill.Y + 9, 20, pill.Height - 18));
        }

        void DrawExpanded(DrawingContext dc, Rect pill, double dpi)
        {
            if (_media == null) return;
            double pad = 18;
            _art = new Rect(pill.X + pad, pill.Y + pad, 64, 64);
            DrawArt(dc, _art, 14);

            double tx = _art.Right + 14, tw = pill.Right - pad - 30 - tx;
            var title = Fmt(string.IsNullOrEmpty(_media.Title) ? "—" : _media.Title, Text, 15, White, dpi, tw);
            dc.DrawText(title, new Point(tx, _art.Y + 8));
            var artist = Fmt(_media.Artist ?? "", TextReg, 13, Gray, dpi, tw);
            dc.DrawText(artist, new Point(tx, _art.Y + 8 + title.Height + 1));
            DrawBars(dc, new Rect(pill.Right - pad - 20, _art.Y + 10, 20, 18));

            // Прогресс песни
            double py = _art.Bottom + 16;
            var bar = new Rect(pill.X + pad, py, pill.Width - 2 * pad, 4);
            dc.DrawRoundedRectangle(Track, null, bar, 2, 2);
            double dur = _media.Duration.TotalSeconds, pos = Math.Min(dur, Math.Max(0, _media.Now.TotalSeconds));
            if (dur > 1)
            {
                dc.DrawRoundedRectangle(White, null, new Rect(bar.X, bar.Y, bar.Width * pos / dur, 4), 2, 2);
                var a = Fmt(Time(pos), TextReg, 10.5, Gray, dpi, 80);
                var b = Fmt("-" + Time(dur - pos), TextReg, 10.5, Gray, dpi, 80);
                dc.DrawText(a, new Point(bar.X, bar.Bottom + 4));
                dc.DrawText(b, new Point(bar.Right - b.Width, bar.Bottom + 4));
            }

            // Кнопки
            double cy = pill.Bottom - 22, cx = pill.X + pill.Width / 2;
            _prev = Button(dc, "", cx - 70, cy, 20, dpi);
            _play = Button(dc, _media.Playing ? "" : "", cx, cy, 26, dpi);
            _next = Button(dc, "", cx + 70, cy, 20, dpi);
        }

        Rect Button(DrawingContext dc, string glyph, double cx, double cy, double size, double dpi)
        {
            var hit = new Rect(cx - 22, cy - 18, 44, 36);
            if (IsMouseOver && hit.Contains(Mouse.GetPosition(this))) dc.DrawRoundedRectangle(HoverBg, null, hit, 12, 12);
            var g = Fmt(glyph, Glyphs, size, White, dpi, 100);
            dc.DrawText(g, new Point(cx - g.Width / 2, cy - g.Height / 2));
            return hit;
        }

        BitmapSource _nSquareOf, _nSquare;

        void DrawNotify(DrawingContext dc, Rect pill, double dpi)
        {
            double s = pill.Height - 22;
            var av = new Rect(pill.X + 14, pill.Y + 11, s, s);
            dc.DrawEllipse(new LinearGradientBrush(Color.FromRgb(0x6F, 0xB6, 0xFF), Color.FromRgb(0x2F, 0x6B, 0xFF), 45), null,
                           new Point(av.X + s / 2, av.Y + s / 2), s / 2, s / 2);
            if (_nAvatar is BitmapSource bs && bs.PixelWidth > 1)
            {
                dc.PushClip(new EllipseGeometry(av));
                if (_nSquareOf != bs) { _nSquareOf = bs; _nSquare = UI.Square(bs); }   // квадрат из середины — аватарка не сплющивается и не съезжает
                dc.DrawImage(_nSquare, av);
                dc.Pop();
            }
            else if (!string.IsNullOrEmpty(_nTitle))
            {
                var l = Fmt(_nTitle.Substring(0, 1).ToUpperInvariant(), Text, s * 0.42, White, dpi, s);
                dc.DrawText(l, new Point(av.X + (s - l.Width) / 2, av.Y + (s - l.Height) / 2));
            }
            double tx = av.Right + 12, tw = pill.Right - 22 - tx;
            var t = Fmt(_nTitle ?? "", Text, 14, White, dpi, tw);
            var m = Fmt(_nText ?? "", TextReg, 12.5, Gray, dpi, tw);
            double top = pill.Y + (pill.Height - t.Height - m.Height) / 2;
            dc.DrawText(t, new Point(tx, top));
            dc.DrawText(m, new Point(tx, top + t.Height));
        }

        /// <summary>Агент Muzule: переливающийся фиолетово-голубой шар (дышит и вращается) и статус.</summary>
        void DrawAgent(DrawingContext dc, Rect pill, double dpi)
        {
            double s = pill.Height - 14;
            var c = new Point(pill.X + 10 + s / 2, pill.Y + pill.Height / 2);
            double pulse = 0.5 + 0.5 * Math.Sin(_time * 3.2);
            var glow = new SolidColorBrush(Color.FromArgb((byte)(40 + 50 * pulse), 0xA7, 0x8B, 0xFA));
            glow.Freeze();
            dc.DrawEllipse(glow, null, c, s / 2 + 2 + 2 * pulse, s / 2 + 2 + 2 * pulse);
            double a = _time * 2.4;
            var orb = new LinearGradientBrush
            {
                StartPoint = new Point(0.5 + 0.5 * Math.Cos(a), 0.5 + 0.5 * Math.Sin(a)),
                EndPoint = new Point(0.5 - 0.5 * Math.Cos(a), 0.5 - 0.5 * Math.Sin(a))
            };
            orb.GradientStops.Add(new GradientStop(Color.FromRgb(0xC4, 0xB5, 0xFD), 0));
            orb.GradientStops.Add(new GradientStop(Color.FromRgb(0x8B, 0x5C, 0xF6), 0.45));
            orb.GradientStops.Add(new GradientStop(Color.FromRgb(0x4D, 0xA8, 0xFF), 1));
            orb.Freeze();
            dc.DrawEllipse(orb, null, c, s / 2, s / 2);

            double tx = c.X + s / 2 + 10, tw = pill.Right - 16 - tx;
            var name = Fmt("Muzule", Text, 13, White, dpi, tw);
            dc.DrawText(name, new Point(tx, pill.Y + (pill.Height - name.Height) / 2));
            double sx = tx + name.Width + 8;
            if (pill.Right - 16 - sx > 20)
            {
                var st = Fmt(_aText ?? "", TextReg, 12.5, Gray, dpi, pill.Right - 16 - sx);
                dc.DrawText(st, new Point(sx, pill.Y + (pill.Height - st.Height) / 2));
            }
        }

        void DrawArt(DrawingContext dc, Rect r, double radius)
        {
            if (_media?.Art != null)
            {
                dc.PushClip(new RectangleGeometry(r, radius, radius));
                dc.DrawImage(_media.Art, r);
                dc.Pop();
            }
            else
            {
                dc.DrawRoundedRectangle(Track, null, r, radius, radius);
                var note = Fmt("", Glyphs, r.Height * 0.5, White, VisualTreeHelper.GetDpi(this).PixelsPerDip, r.Width);
                dc.DrawText(note, new Point(r.X + (r.Width - note.Width) / 2, r.Y + (r.Height - note.Height) / 2));
            }
        }

        /// <summary>«Эквалайзер» цвета обложки: пока играет — прыгает, на паузе — замирает.</summary>
        void DrawBars(DrawingContext dc, Rect r)
        {
            var brush = new SolidColorBrush(_media?.Tint ?? Color.FromRgb(0x30, 0xD1, 0x58));
            brush.Freeze();
            bool playing = _media?.Playing == true;
            double bw = 3, gap = (r.Width - 4 * bw) / 3;
            double[] speed = { 7.3, 9.1, 6.2, 8.4 }, phase = { 0, 1.7, 3.1, 4.4 };
            for (int i = 0; i < 4; i++)
            {
                double k = playing ? 0.3 + 0.7 * Math.Abs(Math.Sin(_time * speed[i] + phase[i]) * Math.Sin(_time * speed[i] * 0.37 + phase[i] * 2)) : 0.22;
                double h = Math.Max(bw, r.Height * k);
                dc.DrawRoundedRectangle(brush, null, new Rect(r.X + i * (bw + gap), r.Y + (r.Height - h) / 2, bw, h), 1.5, 1.5);
            }
        }

        static string Time(double sec)
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, sec));
            return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
        }

        static FormattedText Fmt(string s, Typeface tf, double size, Brush brush, double dpi, double maxWidth)
        {
            var t = new FormattedText(s ?? "", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, tf, Math.Max(1, size), brush, dpi);
            if (maxWidth > 4) { t.MaxTextWidth = maxWidth; t.MaxLineCount = 1; t.Trimming = TextTrimming.CharacterEllipsis; }
            return t;
        }

        static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
    }
}
