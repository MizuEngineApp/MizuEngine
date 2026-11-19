// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>
    /// Эффект «джинн», как в macOS и Windhawk: окно изгибается воронкой и втягивается в значок дока
    /// (или в Dynamic Island), а при открытии — вытекает обратно.
    ///
    /// Как сделано (так же работает Windhawk): перед анимацией снимаем один кадр окна, натягиваем его
    /// на сетку из треугольников и каждый кадр двигаем только вершины сетки. Рисует видеокарта за один проход —
    /// 60, 120 или 144 кадра в секунду, сколько умеет монитор. Раньше окно резалось на сотни миниатюр DWM,
    /// и каждую приходилось обновлять отдельным вызовом — отсюда лаги.
    ///
    /// Окно анимации всегда поверх обычных окон (выше любых программ), но под доком и Dynamic Island —
    /// воронка «ныряет» в значок. Вне анимации у окна пустая форма: его не видно, клики проходят насквозь.
    /// </summary>
    internal sealed class WindowAnimator : Window
    {
        const double MinimizeTime = 0.46, RestoreTime = 0.40;   // секунд
        const int HoldFrames = 2, FadeFrames = 6;   // после разворачивания картинка держится 2 кадра (окно успевает отрисоваться) и гаснет
        const int Cols = 8, Rows = 96;               // сетка: 96 рядов дают гладкий изгиб без «ступенек»

        static readonly IntPtr TopMost = new IntPtr(-1);

        IntPtr _h, _target, _thumb;
        bool _minimizing, _up, _active, _meshMode, _realDone;
        int _phase, _prep, _settle, _wait;   // 0 — подготовка, 1 — анимация, 2 — окно развёрнуто, картинка гаснет поверх него
        double _t;
        TimeSpan _last;
        int _sw, _sh;
        int _bx, _by, _bw, _bh;                 // где сейчас окно анимации (пиксели экрана)
        double _wl, _wt, _wr, _wb;              // видимая часть окна (без невидимых рамок)
        double _il, _it, _ir, _ib;              // значок
        Native.RECT _full;                      // окно целиком, с невидимыми рамками (для запасного режима)

        readonly Viewport3D _vp = new Viewport3D { ClipToBounds = true, IsHitTestVisible = false };
        readonly OrthographicCamera _cam = new OrthographicCamera { LookDirection = new Vector3D(0, 0, -1), UpDirection = new Vector3D(0, 1, 0), NearPlaneDistance = 0.1, FarPlaneDistance = 100 };
        readonly MeshGeometry3D _geo = new MeshGeometry3D();
        readonly ImageBrush _brush = new ImageBrush { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1), Stretch = Stretch.Fill };
        readonly DispatcherTimer _safety = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };

        /// <summary>Анимация закончилась: окно, свернули ли его (true) или развернули, летело ли вверх (в Dynamic Island).</summary>
        public event Action<IntPtr, bool, bool> Finished;

        /// <summary>Окно, которое сейчас анимируется (события «свернули» от него не обрабатываем второй раз).</summary>
        public IntPtr Current => _target;

        public WindowAnimator()
        {
            Glass.Setup(this, clickThrough: false);
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            Title = "MizuEngine Window Animation";
            Left = 0; Top = 0; Width = 10; Height = 10;

            // Сетка: индексы треугольников постоянные, каждый кадр меняются только вершины
            var idx = new Int32Collection(Rows * Cols * 6);
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                {
                    int a = r * (Cols + 1) + c, b = a + 1, d = a + Cols + 1, e = d + 1;
                    idx.Add(a); idx.Add(d); idx.Add(b);
                    idx.Add(b); idx.Add(d); idx.Add(e);
                }
            idx.Freeze();
            _geo.TriangleIndices = idx;
            RenderOptions.SetBitmapScalingMode(_brush, BitmapScalingMode.HighQuality);   // при уменьшении — без «ряби»
            RenderOptions.SetCachingHint(_brush, CachingHint.Cache);
            var mat = new DiffuseMaterial(_brush);
            var group = new Model3DGroup();
            group.Children.Add(new AmbientLight(Colors.White));   // белый свет — цвета картинки ровно как в окне
            group.Children.Add(new GeometryModel3D(_geo, mat) { BackMaterial = mat });
            _vp.Camera = _cam;
            _vp.Children.Add(new ModelVisual3D { Content = group });
            RenderOptions.SetBitmapScalingMode(_vp, BitmapScalingMode.HighQuality);
            Content = _vp;

            SourceInitialized += (s, e) =>
            {
                _h = new WindowInteropHelper(this).Handle;
                SetIdle();   // пустая форма ещё до показа — окно ни разу не мелькнёт
                HwndSource.FromHwnd(_h).AddHook((IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) =>
                {
                    if (msg == 0x02E0) handled = true;   // WM_DPICHANGED: размер ставим сами, в пикселях
                    return IntPtr.Zero;
                });
                UpdateScreen();
            };
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s, e) => Dispatcher.BeginInvoke(new Action(UpdateScreen));
            _safety.Tick += (s, e) => Abort();
        }

        void UpdateScreen()
        {
            _sw = Native.GetSystemMetrics(0);
            _sh = Native.GetSystemMetrics(1);
        }

        DateTime _beganAt;

        /// <summary>Самопроверка (из Shell раз в 4 с): анимация идёт подозрительно долго — сбрасываем, окно не должно застрять.</summary>
        public void Unstick()
        {
            if (_active && (DateTime.UtcNow - _beganAt).TotalSeconds > 4) Abort();
            if (!_active && _h != IntPtr.Zero && !IsVisible) { Show(); SetIdle(); }   // окно анимации кто-то спрятал — возвращаем
        }

        /// <summary>После сбоя Windows: заново узнаём размеры экранов и ставим окно анимации в исходное состояние.</summary>
        public void Recover()
        {
            try
            {
                if (_active) Abort();
                UpdateScreen();
                if (!IsVisible) Show();
                SetIdle();
            }
            catch (Exception e) { Settings.Log(e); }
        }

        void SetIdle() { if (_h != IntPtr.Zero) DockNative.SetWindowRgn(_h, DockNative.CreateRectRgn(0, 0, 0, 0), true); }

        void SetVisible() { if (_h != IntPtr.Zero) DockNative.SetWindowRgn(_h, DockNative.CreateRectRgn(0, 0, _bw, _bh), false); }

        // ================== запуск ==================

        /// <summary>
        /// Свернуть окно в значок. iconPx — куда (пиксели экрана); up — значок выше окна (Dynamic Island).
        /// alreadyMinimized — окно уже свернули (кнопкой «_» или Win+M), картинку берём из последнего кадра DWM.
        /// </summary>
        public void Minimize(IntPtr hwnd, Rect iconPx, bool up, bool alreadyMinimized = false)
        {
            Abort();
            Native.RECT full, frame;
            bool ok;
            if (alreadyMinimized) ok = Rects.Restored(hwnd, out full, out frame);
            else
            {
                ok = Native.GetWindowRect(hwnd, out full);
                if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out frame, Marshal.SizeOf<Native.RECT>()) != 0 || frame.Width <= 0) frame = full;
                if (ok) Rects.Learn(hwnd, full, frame);
            }
            if (!ok || iconPx.IsEmpty || !Begin(hwnd, full, frame, iconPx, up))
            {
                if (!alreadyMinimized) SetMinimized(hwnd, true);
                return;
            }
            _minimizing = true;
            _realDone = alreadyMinimized;

            BitmapSource shot = null;
            if (!Themes.SafeMode)
            {
                if (!alreadyMinimized) shot = Capture.Window(hwnd, full, frame);
                else
                {
                    // Окно уже исчезло: ставим на его место живую миниатюру DWM (последний кадр окна) —
                    // со стороны кажется, что окно никуда не делось, — и снимаем с экрана картинку
                    ShowThumb(hwnd);
                    PoseThumb(0, 255);
                    SetVisible();
                    DwmFlush(); DwmFlush();   // два кадра: окно анимации переехало и миниатюра уже нарисована
                    shot = Capture.Screen(frame, hwnd);
                }
            }
            if (shot != null) Shots.Put(hwnd, shot, full, frame);
            else if (_thumb == IntPtr.Zero) ShowThumb(hwnd);   // снимка нет — запасной режим: живая миниатюра уменьшается в значок
            Start(shot);
            Pose(0);
        }

        /// <summary>Развернуть свёрнутое окно из значка.</summary>
        public void Restore(IntPtr hwnd, Rect iconPx, bool up)
        {
            Abort();
            var shot = Shots.Get(hwnd);
            Native.RECT full, frame;
            bool ok;
            if (shot != null) { full = shot.Full; frame = shot.Frame; ok = true; }   // окно развернётся ровно туда, где его свернули
            else ok = Rects.Restored(hwnd, out full, out frame);
            if (iconPx.IsEmpty || !ok || !Begin(hwnd, full, frame, iconPx, up))
            {
                SetMinimized(hwnd, false);
                BringToFront(hwnd);
                return;
            }
            _minimizing = false;
            _realDone = false;
            if (shot == null || Themes.SafeMode) ShowThumb(hwnd);   // снимка нет — запасной режим: живая миниатюра «вырастает» из значка
            Start(Themes.SafeMode ? null : shot?.Image);
            Pose(1);
        }

        bool Begin(IntPtr hwnd, Native.RECT full, Native.RECT frame, Rect icon, bool up)
        {
            if (_h == IntPtr.Zero) return false;
            if (!IsVisible) Show();
            UpdateScreen();
            // Анимация идёт в пределах монитора окна: на нём же должен быть и значок (свой док монитора или Island на главном)
            if (frame.Width < 60 || frame.Height < 40) return false;
            var mon = Monitors.BoundsOf(frame);
            double icx = (icon.Left + icon.Right) / 2, icy = (icon.Top + icon.Bottom) / 2;
            if (icx < mon.Left || icx > mon.Right || icy < mon.Top || icy > mon.Bottom)
            {
                // Значок на другом мониторе (окно перетащили, а док ещё не обновился) — летим вниз по центру монитора окна
                double s = Math.Max(24, Math.Min(64, icon.Width));
                icon = new Rect((mon.Left + mon.Right) / 2.0 - s / 2, mon.Bottom - s - 14, s, s);
                up = false;
            }

            _target = hwnd;
            _up = up;
            _full = full;
            _wl = frame.Left; _wt = frame.Top; _wr = frame.Right; _wb = frame.Bottom;
            _il = icon.Left; _it = icon.Top; _ir = icon.Right; _ib = icon.Bottom;

            // Окно анимации — только на площади «окно + значок», а не на весь экран: видеокарте меньше работы
            int l = (int)Math.Floor(Math.Min(full.Left, icon.Left)) - 2, t = (int)Math.Floor(Math.Min(full.Top, icon.Top)) - 2;
            int r = (int)Math.Ceiling(Math.Max(full.Right, icon.Right)) + 2, b = (int)Math.Ceiling(Math.Max(full.Bottom, icon.Bottom)) + 2;
            l = Math.Max(mon.Left, l); t = Math.Max(mon.Top, t); r = Math.Min(mon.Right, r); b = Math.Min(mon.Bottom - 1, b);   // на 1 px ниже экрана — чтобы Wallpaper Engine не принял за игру
            if (r - l < 10 || b - t < 10) { _target = IntPtr.Zero; return false; }
            _bx = l; _by = t; _bw = r - l; _bh = b - t;
            DockNative.SetWindowPos(_h, TopMost, _bx, _by, _bw, _bh, DockNative.SWP_NOACTIVATE);
            _cam.Width = _bw;
            _cam.Position = new Point3D(_bx + _bw / 2.0, -(_by + _bh / 2.0), 10);
            _active = true;
            _beganAt = DateTime.UtcNow;
            return true;
        }

        void Start(BitmapSource shot)
        {
            _meshMode = shot != null;
            _brush.ImageSource = shot;
            _brush.Opacity = 1;
            if (!_meshMode) _geo.Positions = null;
            _phase = 0;
            _prep = 2;   // ждём 2 кадра: WPF успевает нарисовать сетку, и только потом окно анимации появляется
            _t = 0;
            _last = TimeSpan.Zero;
            CompositionTarget.Rendering -= OnFrame;
            CompositionTarget.Rendering += OnFrame;
            _safety.Stop();
            _safety.Start();
        }

        // ================== кадры ==================

        void OnFrame(object sender, EventArgs e)
        {
            var now = ((RenderingEventArgs)e).RenderingTime;
            if (now == _last) return;
            double dt = _last == TimeSpan.Zero ? 1.0 / 60 : Math.Min(0.05, (now - _last).TotalSeconds);
            _last = now;

            if (_phase == 0)
            {
                if (--_prep > 0) return;
                // Сетка уже нарисована: показываем окно анимации (точная копия окна), и только потом прячем настоящее —
                // так ни одного кадра без окна, никакого мигания
                SetVisible();
                DockNative.SetWindowPos(_h, TopMost, 0, 0, 0, 0, DockNative.SWP_NOMOVE | DockNative.SWP_NOSIZE | DockNative.SWP_NOACTIVATE);
                Shell.RaiseChrome();
                DwmFlush();
                if (_meshMode) HideThumb();
                if (_minimizing && !_realDone) { _realDone = true; SetMinimized(_target, true); }
                _phase = 1;
                return;
            }

            if (_phase == 2)
            {
                // Окно уже развёрнуто под картинкой: держим её пару кадров, пока окно дорисовывается, потом плавно гасим
                _settle++;
                if (_settle <= HoldFrames) return;
                // Окно программы от администратора разворачивается само и чуть позже — держим картинку, пока оно не появится (до ~0.5 с)
                if (_settle == HoldFrames + 1 && Native.IsIconic(_target) && _wait < 30) { _wait++; _settle = HoldFrames; return; }
                if (_settle == HoldFrames + 1 && _wait > 0) DockNative.SetForegroundWindow(_target);
                double a = 1 - (double)(_settle - HoldFrames) / FadeFrames;
                if (a <= 0) { Finish(); return; }
                if (_meshMode) _brush.Opacity = a * a;
                else PoseThumb(0, (byte)(255 * a * a));
                return;
            }

            _t = Math.Min(1, _t + dt / (_minimizing ? MinimizeTime : RestoreTime));
            Pose(_minimizing ? EaseInOut(_t) : 1 - EaseOut(_t));

            if (_t >= 1)
            {
                var hwnd = _target;
                bool up = _up;
                if (_minimizing)
                {
                    Finish();
                    Finished?.Invoke(hwnd, true, up);
                    return;
                }
                _realDone = true;
                SetMinimized(hwnd, false);
                BringToFront(hwnd);
                Shots.Drop(hwnd);
                _phase = 2;
                _settle = 0;
                _wait = 0;
                Finished?.Invoke(hwnd, false, up);
                // Окно снова на экране — запоминаем его невидимые рамки, пригодится при следующем сворачивании кнопкой «_»
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (Native.GetWindowRect(hwnd, out var f) && Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out Native.RECT fr, Marshal.SizeOf<Native.RECT>()) == 0)
                        Rects.Learn(hwnd, f, fr);
                }));
            }
        }

        void Pose(double p)
        {
            if (_meshMode) PoseMesh(p);
            else PoseThumb(p, 255);
        }

        /// <summary>
        /// Вершины сетки. p = 0 — окно на своём месте, p = 1 — окно целиком «втянулось» в значок.
        /// Считаем так, будто значок ниже окна; если выше (Dynamic Island) — переворачиваем по вертикали.
        /// Ряды сетки идут по экрану, а не по окну: часть окна, уже ушедшая в значок, просто не рисуется.
        /// </summary>
        void PoseMesh(double p)
        {
            const int N = (Rows + 1) * (Cols + 1);
            var pos = new Point3DCollection(N);
            var tex = new PointCollection(N);
            double s = _up ? -1 : 1;
            double wt = _up ? -_wb : _wt, wb = _up ? -_wt : _wb;
            double it = _up ? -_ib : _it, ib = _up ? -_it : _ib;
            double H = wb - wt;
            double D = ib - wt;   // на сколько сдвинуть окно, чтобы его край ушёл в значок
            double alpha;

            if (D <= 4 || H <= 4)
            {
                // Значок «не по пути» (окно ниже дока) — окно плавно уменьшается в значок
                double e = p * p * (3 - 2 * p);
                double l = Lerp(_wl, _il, e), t = Lerp(_wt, _it, e), r = Lerp(_wr, _ir, e), b = Lerp(_wb, _ib, e);
                for (int i = 0; i <= Rows; i++)
                {
                    double v = (double)i / Rows, y = Lerp(t, b, v);
                    for (int c = 0; c <= Cols; c++)
                    {
                        double u = (double)c / Cols;
                        pos.Add(new Point3D(Lerp(l, r, u), -y, 0));
                        tex.Add(new Point(u, v));
                    }
                }
                alpha = 1 - Smooth(Clamp01((p - 0.7) / 0.3));
            }
            else
            {
                double p1 = Smooth(Clamp01(p / 0.45));               // сначала окно изгибается воронкой к значку
                double p2 = EaseIn(Clamp01((p - 0.18) / 0.82));      // потом стекает в неё
                double ft = wt, fb = Math.Max(wt + 1, it);           // где воронка: от верха окна до верха значка
                double top = wt + p2 * D, bottom = Math.Min(wb + p2 * D, ib);
                if (bottom - top < 0.5) bottom = top + 0.5;
                for (int i = 0; i <= Rows; i++)
                {
                    double y = Lerp(top, bottom, (double)i / Rows);
                    double v = (y - top) / H;                          // какая строка картинки окна оказалась на этой высоте
                    double k = y <= ft ? 0 : y >= fb ? 1 : Bend((y - ft) / (fb - ft));
                    double f = k * p1;
                    double l = Lerp(_wl, _il, f), r = Lerp(_wr, _ir, f);
                    double sy = s > 0 ? y : -y, tv = s > 0 ? v : 1 - v;
                    for (int c = 0; c <= Cols; c++)
                    {
                        double u = (double)c / Cols;
                        pos.Add(new Point3D(Lerp(l, r, u), -sy, 0));
                        tex.Add(new Point(u, tv));
                    }
                }
                alpha = 1 - 0.5 * Smooth(Clamp01((p - 0.86) / 0.14));
            }
            pos.Freeze(); tex.Freeze();
            _geo.Positions = pos;
            _geo.TextureCoordinates = tex;
            _brush.Opacity = alpha;
        }

        // ================== запасной режим: живая миниатюра DWM ==================

        void ShowThumb(IntPtr hwnd)
        {
            HideThumb();
            if (Native.DwmRegisterThumbnail(_h, hwnd, out _thumb) != 0) _thumb = IntPtr.Zero;
        }

        void HideThumb()
        {
            if (_thumb == IntPtr.Zero) return;
            Native.DwmUnregisterThumbnail(_thumb);
            _thumb = IntPtr.Zero;
        }

        /// <summary>Окно целиком (миниатюра) плавно уменьшается в значок — когда снимка окна нет или включён безопасный режим.</summary>
        void PoseThumb(double p, byte alpha)
        {
            if (_thumb == IntPtr.Zero) return;
            double e = EaseInOut(p);
            double l = Lerp(_full.Left, _il, e), t = Lerp(_full.Top, _it, e), r = Lerp(_full.Right, _ir, e), b = Lerp(_full.Bottom, _ib, e);
            byte a = (byte)(alpha * (1 - Smooth(Clamp01((p - 0.75) / 0.25))));
            var dst = new Native.RECT { Left = (int)Math.Round(l) - _bx, Top = (int)Math.Round(t) - _by, Right = (int)Math.Round(r) - _bx, Bottom = (int)Math.Round(b) - _by };
            bool visible = dst.Right - dst.Left >= 1 && dst.Bottom - dst.Top >= 1 && a > 0;
            var props = new Native.DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = Native.DWM_TNP_RECTDESTINATION | Native.DWM_TNP_OPACITY | Native.DWM_TNP_VISIBLE | Native.DWM_TNP_SOURCECLIENTAREAONLY,
                rcDestination = dst, opacity = a, fVisible = visible, fSourceClientAreaOnly = false
            };
            Native.DwmUpdateThumbnailProperties(_thumb, ref props);
        }

        // ================== конец ==================

        /// <summary>Прервать анимацию (новая началась или что-то зависло): настоящее окно доводим до нужного состояния.</summary>
        void Abort()
        {
            if (_target == IntPtr.Zero) { Finish(); return; }
            var h = _target;
            bool min = _minimizing, done = _realDone;
            Finish();
            if (done) return;
            if (min) SetMinimized(h, true);
            else if (Native.IsIconic(h)) { SetMinimized(h, false); BringToFront(h); }
        }

        void Finish()
        {
            _safety.Stop();
            CompositionTarget.Rendering -= OnFrame;
            HideThumb();
            _target = IntPtr.Zero;
            _phase = 0;
            if (_active)
            {
                SetIdle();
                _active = false;
                _geo.Positions = null;
                _geo.TextureCoordinates = null;
                _brush.ImageSource = null;   // картинку окна отпускаем сразу — память не копится
            }
        }

        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
        static double Smooth(double v) => v * v * (3 - 2 * v);
        /// <summary>Форма воронки: мягкий S-изгиб (как у macOS), без резкого «горлышка».</summary>
        static double Bend(double v) => 0.5 - 0.5 * Math.Cos(Math.PI * v);
        static double EaseIn(double v) => v * v * (0.55 + 0.45 * v);
        static double EaseInOut(double v) => v < 0.5 ? 2 * v * v : 1 - Math.Pow(-2 * v + 2, 2) / 2;
        static double EaseOut(double v) => 1 - Math.Pow(1 - v, 3);
        static double Lerp(double a, double b, double t) => a + (b - a) * t;

        [DllImport("dwmapi.dll")] static extern int DwmFlush();

        // ================== системная анимация ==================

        /// <summary>Своя анимация вместо системной: на долю секунды выключаем стандартную анимацию Windows.</summary>
        public static void WithoutSystemAnimation(Action act)
        {
            var ai = new Native.ANIMATIONINFO { cbSize = 8 };
            bool had = Native.SystemParametersInfo(0x48, 8, ref ai, 0) && ai.iMinAnimate != 0;   // SPI_GETANIMATION
            if (had) { ai.iMinAnimate = 0; Native.SystemParametersInfo(0x49, 8, ref ai, 0); }    // SPI_SETANIMATION, без сохранения
            try { act(); }
            finally { if (had) { ai.iMinAnimate = 1; Native.SystemParametersInfo(0x49, 8, ref ai, 0); } }
        }

        /// <summary>
        /// Свернуть или развернуть чужое окно. Окна программ, запущенных от администратора (диспетчер задач, многие VPN),
        /// Windows не даёт трогать через ShowWindow — вызов молча не срабатывает, и окно оставалось свёрнутым.
        /// Тогда, как это делает Explorer, просим окно само: WM_SYSCOMMAND (SC_MINIMIZE / SC_RESTORE).
        /// </summary>
        public static void SetMinimized(IntPtr h, bool minimize)
        {
            WithoutSystemAnimation(() => DockNative.ShowWindow(h, minimize ? DockNative.SW_MINIMIZE : DockNative.SW_RESTORE));
            if (Native.IsIconic(h) == minimize) return;
            DockNative.GetWindowThreadProcessId(h, out uint pid);
            if (!minimize) DockNative.AllowSetForegroundWindow(pid);   // окно само выйдет на передний план
            DockNative.PostMessage(h, 0x0112, new IntPtr(minimize ? 0xF020 : 0xF120), IntPtr.Zero);   // WM_SYSCOMMAND
        }

        /// <summary>Выводит окно на передний план (Windows иногда не даёт — короткое нажатие Alt снимает ограничение).</summary>
        public static void BringToFront(IntPtr h)
        {
            if (!DockNative.SetForegroundWindow(h))
            {
                DockNative.PressKeys(DockNative.VK_MENU);
                DockNative.SetForegroundWindow(h);
            }
        }

        // ================== где окно ==================

        /// <summary>Размеры окон: целиком (с невидимыми рамками для растягивания) и видимая часть.</summary>
        static class Rects
        {
            // Невидимые рамки окна (слева, сверху, справа, снизу), измеренные, пока окно было на экране
            static readonly Dictionary<IntPtr, (int l, int t, int r, int b)> _learned = new Dictionary<IntPtr, (int, int, int, int)>();

            public static void Learn(IntPtr h, Native.RECT full, Native.RECT frame)
            {
                if (Maximized(h)) return;
                if (_learned.Count > 64) _learned.Clear();
                _learned[h] = (frame.Left - full.Left, frame.Top - full.Top, full.Right - frame.Right, full.Bottom - frame.Bottom);
            }

            public static bool Maximized(IntPtr h)
            {
                var wp = new Native.WINDOWPLACEMENT { length = Marshal.SizeOf<Native.WINDOWPLACEMENT>() };
                if (!Native.GetWindowPlacement(h, ref wp)) return false;
                return Native.IsIconic(h) ? (wp.flags & 2) != 0 : DockNative.IsZoomed(h);
            }

            /// <summary>Где окно будет, когда его развернут (у свёрнутого окна настоящего положения нет).</summary>
            public static bool Restored(IntPtr hwnd, out Native.RECT full, out Native.RECT frame)
            {
                full = frame = default;
                var wp = new Native.WINDOWPLACEMENT { length = Marshal.SizeOf<Native.WINDOWPLACEMENT>() };
                if (!Native.GetWindowPlacement(hwnd, ref wp)) return false;
                full = wp.rcNormalPosition;   // «рабочие» координаты: от края рабочей области главного монитора
                var primaryWork = DockNative.WorkAreaOf(new Native.RECT { Left = 0, Top = 0, Right = 1, Bottom = 1 });
                full.Left += primaryWork.Left; full.Right += primaryWork.Left;
                full.Top += primaryWork.Top; full.Bottom += primaryWork.Top;

                if (Maximized(hwnd))
                {
                    frame = DockNative.WorkAreaOf(full);   // развёрнутое окно занимает ровно рабочую область
                    int o = Margins(hwnd).l;   // развёрнутое окно свисает за край рабочей области на толщину рамки со всех сторон
                    full = new Native.RECT { Left = frame.Left - o, Top = frame.Top - o, Right = frame.Right + o, Bottom = frame.Bottom + o };
                }
                else
                {
                    var m = Margins(hwnd);
                    frame = new Native.RECT { Left = full.Left + m.l, Top = full.Top + m.t, Right = full.Right - m.r, Bottom = full.Bottom - m.b };
                }
                return frame.Width > 0 && frame.Height > 0;
            }

            /// <summary>Невидимые рамки: измеренные, а если окно ещё не мерили — как у обычных окон Windows 10/11.</summary>
            static (int l, int t, int r, int b) Margins(IntPtr h)
            {
                if (_learned.TryGetValue(h, out var m)) return m;
                long style = Native.GetWindowLongPtr(h, -16).ToInt64();   // GWL_STYLE
                bool standard = (style & 0x00040000) != 0 && (style & 0x00C00000) == 0x00C00000;   // WS_THICKFRAME + WS_CAPTION
                if (!standard) return (0, 0, 0, 0);
                int f = Math.Max(0, Native.GetSystemMetrics(32) + Native.GetSystemMetrics(92) - 1);   // SM_CXSIZEFRAME + SM_CXPADDEDBORDER, минус видимая линия
                return (f, 0, f, f);
            }
        }

        // ================== снимки окон ==================

        /// <summary>
        /// Снимок окна, сделанный при сворачивании, — из него «вытекает» окно при разворачивании.
        /// Храним только для свёрнутых окон (не больше 6), после разворачивания сразу выбрасываем.
        /// </summary>
        sealed class Shot { public BitmapSource Image; public Native.RECT Full, Frame; public long Tick; }

        static class Shots
        {
            static readonly Dictionary<IntPtr, Shot> _all = new Dictionary<IntPtr, Shot>();

            public static void Put(IntPtr h, BitmapSource img, Native.RECT full, Native.RECT frame)
            {
                Clean();
                while (_all.Count >= 6)
                {
                    IntPtr oldest = IntPtr.Zero; long min = long.MaxValue;
                    foreach (var kv in _all) if (kv.Value.Tick < min) { min = kv.Value.Tick; oldest = kv.Key; }
                    _all.Remove(oldest);
                }
                _all[h] = new Shot { Image = img, Full = full, Frame = frame, Tick = Environment.TickCount64 };
            }

            public static Shot Get(IntPtr h)
            {
                Clean();
                return _all.TryGetValue(h, out var s) && Native.IsIconic(h) ? s : null;
            }

            public static void Drop(IntPtr h) => _all.Remove(h);

            static void Clean()
            {
                List<IntPtr> dead = null;
                foreach (var kv in _all)
                    if (!DockNative.IsWindow(kv.Key) || !Native.IsIconic(kv.Key)) (dead ??= new List<IntPtr>()).Add(kv.Key);
                if (dead != null) foreach (var h in dead) _all.Remove(h);
            }
        }

        static class Capture
        {
            [StructLayout(LayoutKind.Sequential)]
            struct BITMAPINFO { public DockNative.BITMAPINFOHEADER h; public int c0; }

            [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
            [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
            [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
            [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
            [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
            [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO bi, uint usage, out IntPtr bits, IntPtr section, uint offset);
            [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
            const uint SRCCOPY = 0x00CC0020, PW_RENDERFULLCONTENT = 2;

            static readonly bool Win11 = Environment.OSVersion.Version.Build >= 22000;

            /// <summary>Снимок окна, которое сейчас на экране. Сначала просим само окно нарисоваться (без перекрывающих окон и снега),
            /// если получилась чернота (некоторые игры и плееры) — берём то, что видно на экране.</summary>
            public static BitmapSource Window(IntPtr hwnd, Native.RECT full, Native.RECT frame)
            {
                try
                {
                    int w = full.Width, h = full.Height;
                    if (w <= 0 || h <= 0 || w > 8192 || h > 8192) return null;
                    int ox = frame.Left - full.Left, oy = frame.Top - full.Top, fw = frame.Width, fh = frame.Height;
                    if (ox < 0 || oy < 0 || ox + fw > w || oy + fh > h) { ox = oy = 0; fw = w; fh = h; }
                    return Grab(w, h, (mem, screen, bits) =>
                    {
                        bool ok = PrintWindow(hwnd, mem, PW_RENDERFULLCONTENT);
                        if (!ok || Blank(bits, w, ox, oy, fw, fh)) BitBlt(mem, ox, oy, fw, fh, screen, frame.Left, frame.Top, SRCCOPY);
                    }, ox, oy, fw, fh, Win11 && !Rects.Maximized(hwnd) ? Radius(hwnd) : 0);
                }
                catch (Exception e) { Settings.Log(e); return null; }
            }

            /// <summary>То, что сейчас на экране в этом месте.</summary>
            public static BitmapSource Screen(Native.RECT r, IntPtr hwnd)
            {
                try
                {
                    int w = r.Width, h = r.Height;
                    if (w <= 0 || h <= 0 || w > 8192 || h > 8192) return null;
                    return Grab(w, h, (mem, screen, bits) => BitBlt(mem, 0, 0, w, h, screen, r.Left, r.Top, SRCCOPY), 0, 0, w, h, Win11 && !Rects.Maximized(hwnd) ? Radius(hwnd) : 0);
                }
                catch (Exception e) { Settings.Log(e); return null; }
            }

            static int Radius(IntPtr h)
            {
                uint dpi = 96;
                try { dpi = GetDpiForWindow(h); } catch { }
                return (int)Math.Round(8 * (dpi == 0 ? 96 : dpi) / 96.0);
            }

            static BitmapSource Grab(int w, int h, Action<IntPtr, IntPtr, IntPtr> draw, int ox, int oy, int fw, int fh, int radius)
            {
                IntPtr screen = DockNative.GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), bits;
                var bi = new BITMAPINFO { h = new DockNative.BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 } };
                IntPtr hbm = CreateDIBSection(screen, ref bi, 0, out bits, IntPtr.Zero, 0);
                if (hbm == IntPtr.Zero) { DeleteDC(mem); DockNative.ReleaseDC(IntPtr.Zero, screen); return null; }
                IntPtr old = SelectObject(mem, hbm);
                try
                {
                    draw(mem, screen, bits);
                    // Переносим в картинку WPF построчно (без огромных временных массивов), делаем непрозрачной и скругляем углы как у Windows 11
                    var wb = new WriteableBitmap(fw, fh, 96, 96, PixelFormats.Pbgra32, null);
                    wb.Lock();
                    var row = new byte[fw * 4];
                    for (int y = 0; y < fh; y++)
                    {
                        Marshal.Copy(bits + ((oy + y) * w + ox) * 4, row, 0, row.Length);
                        int dy = y < radius ? radius - y : y >= fh - radius ? y - (fh - radius) + 1 : 0;
                        for (int x = 0; x < fw; x++)
                        {
                            int i = x * 4;
                            double cov = 1;
                            if (dy > 0)
                            {
                                int dx = x < radius ? radius - x : x >= fw - radius ? x - (fw - radius) + 1 : 0;
                                if (dx > 0)
                                {
                                    double dist = Math.Sqrt((dx - 0.5) * (dx - 0.5) + (dy - 0.5) * (dy - 0.5));
                                    cov = Math.Clamp(radius - dist + 0.5, 0, 1);
                                }
                            }
                            if (cov >= 1) row[i + 3] = 255;
                            else
                            {
                                row[i] = (byte)(row[i] * cov); row[i + 1] = (byte)(row[i + 1] * cov); row[i + 2] = (byte)(row[i + 2] * cov);
                                row[i + 3] = (byte)(255 * cov);
                            }
                        }
                        Marshal.Copy(row, 0, wb.BackBuffer + y * wb.BackBufferStride, row.Length);
                    }
                    wb.AddDirtyRect(new Int32Rect(0, 0, fw, fh));
                    wb.Unlock();
                    return wb;
                }
                finally
                {
                    SelectObject(mem, old);
                    DockNative.DeleteObject(hbm);
                    DeleteDC(mem);
                    DockNative.ReleaseDC(IntPtr.Zero, screen);
                }
            }

            /// <summary>Окно нарисовалось чёрным (так бывает у игр и видео) — проверяем 64 точки.</summary>
            static bool Blank(IntPtr bits, int stride, int ox, int oy, int fw, int fh)
            {
                for (int gy = 0; gy < 8; gy++)
                    for (int gx = 0; gx < 8; gx++)
                    {
                        int x = ox + (int)((gx + 0.5) * fw / 8), y = oy + (int)((gy + 0.5) * fh / 8);
                        int px = Marshal.ReadInt32(bits, (y * stride + x) * 4);
                        if ((px & 0xFFFFFF) != 0) return false;
                    }
                return true;
            }
        }
    }

    /// <summary>
    /// Пока работают наши анимации сворачивания, стандартная анимация Windows выключена (иначе играли бы обе).
    /// Исходная настройка запоминается в settings.json и возвращается при выходе — даже после вылета.
    /// </summary>
    internal static class SysAnim
    {
        static bool _off;

        public static void Disable(Settings s)
        {
            if (_off) return;
            var ai = new Native.ANIMATIONINFO { cbSize = 8 };
            if (!Native.SystemParametersInfo(0x48, 8, ref ai, 0)) return;
            _off = true;
            if (ai.iMinAnimate == 0) return;   // у пользователя она и так выключена
            if (s.SavedMinAnimate < 0) { s.SavedMinAnimate = 1; s.Save(); }
            ai.iMinAnimate = 0;
            Native.SystemParametersInfo(0x49, 8, ref ai, 0);
        }

        /// <summary>
        /// Самопроверка: если системную анимацию сворачивания кто-то включил обратно (другой экземпляр программы,
        /// «Параметры» Windows) — выключаем снова, иначе играли бы две анимации или наша терялась.
        /// </summary>
        public static void Ensure(Settings s)
        {
            if (!_off) { Disable(s); return; }
            var ai = new Native.ANIMATIONINFO { cbSize = 8 };
            if (!Native.SystemParametersInfo(0x48, 8, ref ai, 0) || ai.iMinAnimate == 0) return;
            if (s.SavedMinAnimate < 0) { s.SavedMinAnimate = 1; s.Save(); }
            ai.iMinAnimate = 0;
            Native.SystemParametersInfo(0x49, 8, ref ai, 0);
        }

        public static void Restore(Settings s)
        {
            _off = false;
            if (s.SavedMinAnimate != 1) return;
            var ai = new Native.ANIMATIONINFO { cbSize = 8, iMinAnimate = 1 };
            Native.SystemParametersInfo(0x49, 8, ref ai, 0);
            s.SavedMinAnimate = -1;
            s.Save();
        }
    }
}
