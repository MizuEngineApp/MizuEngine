// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>
    /// Всё, что MizuEngine рисует поверх рабочего стола, кроме снега и дока: счётчик FPS, виджеты, цветные рамки окон.
    /// Живёт в потоке эффектов; главное окно присылает команды через Effects.Run.
    /// </summary>
    internal sealed class Overlays
    {
        readonly Settings _s;
        FpsWindow _fps;
        SystemWidget _sys;
        ClockWidget _clock;
        WeatherWidget _weather;
        SysStats _stats;
        readonly DispatcherTimer _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        readonly DispatcherTimer _borders = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        readonly DispatcherTimer _rainbow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        readonly HashSet<IntPtr> _touched = new HashSet<IntPtr>();
        double _hue;
        IntPtr _lastFg;

        /// <summary>Виджеты временно спрятаны горячей клавишей (не сохраняется).</summary>
        public bool WidgetsHidden;

        public event Action StatsUpdated;
        public SysStats Stats => _stats;

        static bool Win11 => Environment.OSVersion.Version.Build >= 22000;

        public Overlays(Settings s)
        {
            _s = s;
            _statsTimer.Tick += (o, e) => { _stats?.Update(); StatsUpdated?.Invoke(); };
            _borders.Tick += (o, e) => PaintAll();
            _rainbow.Tick += (o, e) => Rainbow();
        }

        public void Apply()
        {
            // Счётчик FPS
            if (_s.FpsOn)
            {
                if (_fps == null) { _fps = new FpsWindow(_s, this); _fps.Show(); }
                _fps.ApplySettings();
            }
            else if (_fps != null) { _fps.Close(); _fps = null; PresentCounter.Stop(); }

            // Виджеты
            bool show = !WidgetsHidden;
            _sys = Toggle(_sys, show && _s.WidgetSystem, () => new SystemWidget(_s, this));
            _clock = Toggle(_clock, show && _s.WidgetClock, () => new ClockWidget(_s));
            _weather = Toggle(_weather, show && _s.WidgetWeather, () => new WeatherWidget(_s));
            foreach (var w in new OverlayWindow[] { _sys, _clock, _weather }) w?.SetOnTop(_s.WidgetsOnTop);
            _weather?.Refresh();

            // Загрузку ПК считаем, только если её кто-то показывает
            bool needStats = _sys != null || (_fps != null && _s.FpsDetailed);
            if (needStats && _stats == null) { _stats = new SysStats(); _statsTimer.Start(); }
            else if (!needStats && _stats != null) { _statsTimer.Stop(); _stats.Dispose(); _stats = null; }

            ApplyBorders();
        }

        static T Toggle<T>(T w, bool on, Func<T> make) where T : OverlayWindow
        {
            if (on && w == null) { w = make(); w.Show(); }
            else if (!on && w != null) { w.Close(); w = null; }
            return w;
        }

        public void Shutdown()
        {
            _fps?.Close(); _sys?.Close(); _clock?.Close(); _weather?.Close();
            _fps = null; _sys = null; _clock = null; _weather = null;
            PresentCounter.Stop();
            _statsTimer.Stop();
            _stats?.Dispose();
            _stats = null;
            _borders.Stop();
            _rainbow.Stop();
            ResetBorders();
        }

        // ================== рамки и углы окон (Windows 11) ==================

        void ApplyBorders()
        {
            bool on = Win11 && (_s.BorderMode != "off" || _s.Corners != "default");
            if (!on) { _borders.Stop(); _rainbow.Stop(); ResetBorders(); return; }
            _borders.Start();
            if (_s.BorderMode == "rainbow") _rainbow.Start(); else _rainbow.Stop();
            ResetBorders();   // режим могли поменять — красим всё заново
            PaintAll();
        }

        void PaintAll()
        {
            foreach (var w in DockApps.EnumRunning()) Paint(w.Hwnd, BaseColor());
            _touched.RemoveWhere(h => !IsWindow(h));
        }

        int BaseColor() => _s.BorderMode switch
        {
            "accent" => ColorRef(AccentHex()),
            "color" => ColorRef(_s.BorderColor),
            "rainbow" => ColorRef(AccentHex()),
            _ => unchecked((int)0xFFFFFFFF)   // DWMWA_COLOR_DEFAULT
        };

        string AccentHex() => _s.Theme switch
        {
            "custom" => _s.CustomAccent,
            "glass" => "#FF5AA8FF",
            "dark" => "#FF3B82F6",
            "light" => "#FF2563EB",
            _ => "#FF4C9AFF"
        };

        void Paint(IntPtr h, int color)
        {
            Native.DwmSetWindowAttribute(h, 34, ref color, 4);   // DWMWA_BORDER_COLOR
            int corner = _s.Corners == "round" ? 2 : _s.Corners == "square" ? 1 : 0;
            Native.DwmSetWindowAttribute(h, 33, ref corner, 4);  // DWMWA_WINDOW_CORNER_PREFERENCE
            _touched.Add(h);
        }

        /// <summary>«Радуга»: рамка активного окна плавно переливается.</summary>
        void Rainbow()
        {
            _hue = (_hue + 2.5) % 360;
            IntPtr fg = Native.GetForegroundWindow();
            if (fg != _lastFg && _lastFg != IntPtr.Zero && _touched.Contains(_lastFg)) Paint(_lastFg, BaseColor());
            _lastFg = fg;
            if (fg == IntPtr.Zero || !_touched.Contains(fg)) return;
            var c = Hsv(_hue);
            int v = c.R | (c.G << 8) | (c.B << 16);
            Native.DwmSetWindowAttribute(fg, 34, ref v, 4);
        }

        void ResetBorders()
        {
            foreach (var h in _touched)
            {
                if (!IsWindow(h)) continue;
                int def = unchecked((int)0xFFFFFFFF), corner = 0;
                Native.DwmSetWindowAttribute(h, 34, ref def, 4);
                Native.DwmSetWindowAttribute(h, 33, ref corner, 4);
            }
            _touched.Clear();
        }

        static int ColorRef(string hex)
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(hex);
                return c.R | (c.G << 8) | (c.B << 16);
            }
            catch { return unchecked((int)0xFFFFFFFF); }
        }

        public static Color Hsv(double h)
        {
            double x = 1 - Math.Abs(h / 60 % 2 - 1), r, g, b;
            if (h < 60) { r = 1; g = x; b = 0; }
            else if (h < 120) { r = x; g = 1; b = 0; }
            else if (h < 180) { r = 0; g = 1; b = x; }
            else if (h < 240) { r = 0; g = x; b = 1; }
            else if (h < 300) { r = x; g = 0; b = 1; }
            else { r = 1; g = 0; b = x; }
            return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    }

    // ======================================================================

    /// <summary>
    /// Маленькое прозрачное окно-виджет: его можно перетащить мышкой, оно не мешает фокусу и не появляется в Alt+Tab.
    /// Лежит прямо на рабочем столе (под окнами) или поверх всего.
    /// </summary>
    internal class OverlayWindow : Window
    {
        protected readonly Settings S;
        readonly Func<string> _getPos;
        readonly Action<string> _setPos;
        readonly DispatcherTimer _z = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        IntPtr _h;
        bool _onTop, _locked;
        protected readonly Border Card;

        public OverlayWindow(Settings s, Func<string> getPos, Action<string> setPos, double defX, double defY)
        {
            S = s;
            _getPos = getPos;
            _setPos = setPos;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            Title = "MizuEngine Widget";
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");

            Card = new Border
            {
                CornerRadius = new CornerRadius(20), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(12),
                Background = new LinearGradientBrush(Color.FromArgb(0xC8, 0x1A, 0x22, 0x34), Color.FromArgb(0xB0, 0x0E, 0x14, 0x22), 90),
                BorderBrush = new LinearGradientBrush(Color.FromArgb(0x70, 255, 255, 255), Color.FromArgb(0x18, 255, 255, 255), 90),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { ShadowDepth = 4, BlurRadius = 14, Opacity = 0.35, Direction = 270 }
            };
            TextElement.SetForeground(Card, Brushes.White);
            Content = Card;

            var wa = SystemParameters.WorkArea;
            var pos = Parse(getPos());
            Left = pos?.X ?? wa.Left + wa.Width * defX;
            Top = pos?.Y ?? wa.Top + wa.Height * defY;

            MouseLeftButtonDown += (o, e) =>
            {
                if (_locked) return;
                try { DragMove(); } catch { }
                _setPos(string.Format(CultureInfo.InvariantCulture, "{0:0},{1:0}", Left, Top));
                S.Save();
            };

            SourceInitialized += (o, e) =>
            {
                _h = new WindowInteropHelper(this).Handle;
                ApplyExStyle();
                KeepZ();
            };
            _z.Tick += (o, e) => KeepZ();
            _z.Start();
            Closed += (o, e) => _z.Stop();
        }

        static Point? Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var parts = s.Split(',');
            if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
            {
                // Виджет не должен оказаться за пределами экрана (например, после смены монитора)
                var v = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
                if (v.Contains(new Point(x + 20, y + 20))) return new Point(x, y);
            }
            return null;
        }

        public void SetOnTop(bool top)
        {
            _onTop = top;
            Topmost = top;
            KeepZ();
        }

        /// <summary>Закреплён — клики проходят сквозь окно (например, чтобы не мешал в игре).</summary>
        public void SetLocked(bool locked)
        {
            _locked = locked;
            ApplyExStyle();
        }

        void ApplyExStyle()
        {
            if (_h == IntPtr.Zero) return;
            long ex = Native.GetWindowLongPtr(_h, Native.GWL_EXSTYLE).ToInt64();
            ex |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
            if (_locked) ex |= Native.WS_EX_TRANSPARENT; else ex &= ~Native.WS_EX_TRANSPARENT;
            Native.SetWindowLongPtr(_h, Native.GWL_EXSTYLE, new IntPtr(ex));
        }

        /// <summary>
        /// Поверх всего — каждые 1.5 с снова поднимаемся наверх (игры любят перекрывать окна).
        /// На рабочем столе — ставим себя сразу над слоем со значками рабочего стола, под всеми окнами.
        /// </summary>
        void KeepZ()
        {
            if (_h == IntPtr.Zero) return;
            const uint flags = 0x0001 | 0x0002 | 0x0010;   // NOSIZE | NOMOVE | NOACTIVATE
            if (_onTop) { SetWindowPos(_h, new IntPtr(-1), 0, 0, 0, 0, flags); return; }
            IntPtr icons = DesktopIcons();
            if (icons == IntPtr.Zero) return;
            IntPtr above = GetWindow(icons, 3);   // GW_HWNDPREV — окно прямо над рабочим столом
            if (above == _h) return;
            SetWindowPos(_h, above == IntPtr.Zero ? new IntPtr(0) : above, 0, 0, 0, 0, flags);
        }

        /// <summary>Окно, в котором Проводник рисует значки рабочего стола (Progman или WorkerW).</summary>
        static IntPtr DesktopIcons()
        {
            IntPtr found = IntPtr.Zero;
            IntPtr progman = FindWindowW("Progman", null);
            if (progman != IntPtr.Zero && FindWindowExW(progman, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) return progman;
            Native.EnumWindows((h, l) =>
            {
                if (FindWindowExW(h, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        protected static TextBlock Text(string s, double size, bool bold = false, double opacity = 1) =>
            new TextBlock { Text = s, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Opacity = opacity, Foreground = Brushes.White };

        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowW(string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string cls, string title);
    }

    // ======================================================================

    /// <summary>Счётчик FPS: цифра крупно, цвет по плавности, ниже — загрузка процессора и видеокарты.</summary>
    internal sealed class FpsWindow : OverlayWindow
    {
        readonly Overlays _o;
        readonly TextBlock _fps = Text("—", 30, true), _label = Text("FPS", 11, true, 0.75), _app = Text("", 11, false, 0.7), _load = Text("", 11, false, 0.8);
        readonly DispatcherTimer _t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        DateTime _last = DateTime.Now;
        double _smooth;
        bool _etw;
        int _lastPid;

        public FpsWindow(Settings s, Overlays o) : base(s, () => s.FpsPos, v => s.FpsPos = v, 0.02, 0.03)
        {
            _o = o;
            Card.Padding = new Thickness(14, 8, 14, 9);
            Card.CornerRadius = new CornerRadius(16);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(_fps);
            _label.Margin = new Thickness(6, 0, 0, 5);
            _label.VerticalAlignment = VerticalAlignment.Bottom;
            row.Children.Add(_label);
            var sp = new StackPanel();
            sp.Children.Add(row);
            sp.Children.Add(_load);
            sp.Children.Add(_app);
            Card.Child = sp;
            Topmost = true;
            ToolTip = Loc.T("fps.drag");

            _t.Tick += (a, b) => Tick();
            _t.Start();
            Closed += (a, b) => _t.Stop();
        }

        public void ApplySettings()
        {
            SetOnTop(true);
            SetLocked(S.FpsLocked);
            _load.Visibility = _app.Visibility = S.FpsDetailed ? Visibility.Visible : Visibility.Collapsed;
            _etw = PresentCounter.Start();
        }

        void Tick()
        {
            var now = DateTime.Now;
            double dt = Math.Max(0.1, (now - _last).TotalSeconds);
            _last = now;

            IntPtr fg = Native.GetForegroundWindow();
            DockNative.GetWindowThreadProcessId(fg, out uint pid);
            if ((int)pid != _lastPid) { _smooth = 0; _lastPid = (int)pid; }

            if (!_etw)
            {
                _fps.Text = "—";
                _label.Text = Loc.T("fps.need_admin");
                _fps.Foreground = Brushes.White;
            }
            else
            {
                int frames = PresentCounter.Take((int)pid);
                double fps = frames / dt;
                _smooth = _smooth <= 0 ? fps : _smooth * 0.5 + fps * 0.5;
                _label.Text = "FPS";
                if (_smooth < 0.5) { _fps.Text = "—"; _fps.Foreground = Brushes.White; }
                else
                {
                    _fps.Text = ((int)Math.Round(_smooth)).ToString();
                    _fps.Foreground = new SolidColorBrush(_smooth >= 55 ? Color.FromRgb(0x5B, 0xE0, 0x7E) : _smooth >= 30 ? Color.FromRgb(0xFF, 0xC8, 0x4A) : Color.FromRgb(0xFF, 0x6B, 0x6B));
                }
            }

            if (S.FpsDetailed)
            {
                var st = _o.Stats;
                _load.Text = st == null ? "" : $"CPU {st.Cpu:0}%   GPU {st.Gpu:0}%   RAM {st.Ram:0}%";
                _app.Text = AppName(fg, (int)pid);
            }
        }

        static string AppName(IntPtr h, int pid)
        {
            string exe = pid > 0 ? DockNative.ExePath((uint)pid) : null;
            return exe == null ? "" : System.IO.Path.GetFileNameWithoutExtension(exe);
        }
    }

    // ======================================================================

    /// <summary>Виджет «Система»: процессор, видеокарта и память с полосками.</summary>
    internal sealed class SystemWidget : OverlayWindow
    {
        readonly (TextBlock value, Border bar)[] _rows = new (TextBlock, Border)[3];
        readonly Overlays _o;
        readonly TextBlock _ram = Text("", 11, false, 0.65);

        public SystemWidget(Settings s, Overlays o) : base(s, () => s.SystemPos, v => s.SystemPos = v, 0.78, 0.06)
        {
            _o = o;
            var sp = new StackPanel { Width = 200 };
            sp.Children.Add(Text(Loc.T("widget.system"), 12, true, 0.7));
            string[] names = { "CPU", "GPU", "RAM" };
            Color[] colors = { Color.FromRgb(0x4D, 0xA8, 0xFF), Color.FromRgb(0xC9, 0x7B, 0xFF), Color.FromRgb(0x5B, 0xE0, 0x7E) };
            for (int i = 0; i < 3; i++)
            {
                var head = new Grid { Margin = new Thickness(0, 8, 0, 4) };
                head.Children.Add(Text(names[i], 13, true));
                var value = Text("0%", 13, true);
                value.HorizontalAlignment = HorizontalAlignment.Right;
                head.Children.Add(value);
                sp.Children.Add(head);
                var track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)) };
                var bar = new Border { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0, Background = new LinearGradientBrush(colors[i], Color.FromArgb(0xFF, (byte)(colors[i].R * 0.7), (byte)(colors[i].G * 0.7), colors[i].B), 0) };
                var g = new Grid();
                g.Children.Add(track);
                g.Children.Add(bar);
                sp.Children.Add(g);
                _rows[i] = (value, bar);
            }
            _ram.Margin = new Thickness(0, 6, 0, 0);
            sp.Children.Add(_ram);
            Card.Child = sp;
            o.StatsUpdated += Update;
            Closed += (a, b) => o.StatsUpdated -= Update;
        }

        void Update()
        {
            var st = _o.Stats;
            if (st == null) return;
            double[] v = { st.Cpu, st.Gpu, st.Ram };
            for (int i = 0; i < 3; i++)
            {
                _rows[i].value.Text = $"{v[i]:0}%";
                Anim.To(_rows[i].bar, WidthProperty, 200 * v[i] / 100, 600);
            }
            _ram.Text = string.Format(CultureInfo.CurrentCulture, "{0:0.0} / {1:0.0} GB", st.RamUsedGb, st.RamTotalGb);
        }
    }

    /// <summary>Виджет «Часы»: крупное время и дата.</summary>
    internal sealed class ClockWidget : OverlayWindow
    {
        readonly TextBlock _time = Text("", 46, false), _sec = Text("", 16, false, 0.6), _date = Text("", 13, false, 0.75);
        readonly DispatcherTimer _t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

        public ClockWidget(Settings s) : base(s, () => s.ClockPos, v => s.ClockPos = v, 0.78, 0.32)
        {
            _time.FontWeight = FontWeights.Light;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(_time);
            _sec.VerticalAlignment = VerticalAlignment.Bottom;
            _sec.Margin = new Thickness(6, 0, 0, 10);
            row.Children.Add(_sec);
            var sp = new StackPanel();
            sp.Children.Add(row);
            sp.Children.Add(_date);
            Card.Child = sp;
            Card.Padding = new Thickness(20, 8, 22, 14);
            _t.Tick += (a, b) => Update();
            _t.Start();
            Closed += (a, b) => _t.Stop();
            Update();
        }

        void Update()
        {
            var n = DateTime.Now;
            _time.Text = n.ToString("HH:mm");
            _sec.Text = n.ToString("ss");
            string d = n.ToString("dddd, d MMMM", CultureInfo.CurrentUICulture);
            _date.Text = d.Length > 0 ? char.ToUpper(d[0]) + d.Substring(1) : d;
        }
    }

    /// <summary>Виджет «Погода»: значок, температура, описание и город. Обновляется раз в 15 минут.</summary>
    internal sealed class WeatherWidget : OverlayWindow
    {
        readonly TextBlock _icon = Text("☁", 34), _temp = Text("…", 32, true), _desc = Text("", 13, false, 0.8), _place = Text("", 12, false, 0.6);
        readonly DispatcherTimer _t = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        string _city;

        public WeatherWidget(Settings s) : base(s, () => s.WeatherPos, v => s.WeatherPos = v, 0.78, 0.5)
        {
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            _icon.FontFamily = new FontFamily("Segoe UI Symbol");
            _icon.VerticalAlignment = VerticalAlignment.Center;
            top.Children.Add(_icon);
            _temp.Margin = new Thickness(12, 0, 0, 0);
            _temp.VerticalAlignment = VerticalAlignment.Center;
            top.Children.Add(_temp);
            var sp = new StackPanel { MinWidth = 170 };
            sp.Children.Add(top);
            sp.Children.Add(_desc);
            sp.Children.Add(_place);
            Card.Child = sp;
            _t.Tick += async (a, b) => await Load();
            _t.Start();
            Closed += (a, b) => _t.Stop();
        }

        /// <summary>Город поменяли в настройках — загружаем заново.</summary>
        public void Refresh()
        {
            if (_city == S.WeatherCity) return;
            _city = S.WeatherCity;
            _ = Load();
        }

        async System.Threading.Tasks.Task Load()
        {
            if (string.IsNullOrWhiteSpace(S.WeatherCity))
            {
                _temp.Text = "—";
                _desc.Text = Loc.T("widget.weather.city");
                _place.Text = "";
                return;
            }
            var w = await WeatherApi.Get(S.WeatherCity);
            if (w == null) { _desc.Text = Loc.T("err.network"); return; }
            var (glyph, key) = WeatherApi.Describe(w.Code);
            _icon.Text = glyph;
            _temp.Text = $"{Math.Round(w.Temp):0}°";
            _desc.Text = Loc.T(key) + string.Format(CultureInfo.CurrentCulture, " · {0:0} km/h", w.Wind);
            _place.Text = w.Place;
        }
    }
}
