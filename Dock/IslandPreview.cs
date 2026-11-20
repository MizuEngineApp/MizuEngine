// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Windows;
using System.Windows.Media;

namespace MizuEngine
{
    /// <summary>Общие правила формы и «пружины» капсулы — их используют и сама капсула, и предпросмотр во вкладке.</summary>
    internal static class IslandShape
    {
        /// <summary>Жёсткость и затухание пружины. Сила подпрыгивания — меньше затухание (больше перелёт), упругость — жёстче и быстрее.</summary>
        public static (double k, double d) Spring(Settings s)
        {
            double k = 160 + 440 * Math.Clamp(s.IslandSpring, 0, 100) / 100.0;
            double zeta = 0.95 - 0.63 * Math.Clamp(s.IslandBounce, 0, 100) / 100.0;
            return (k, 2 * zeta * Math.Sqrt(k));
        }

        /// <summary>Насколько сильно капсула «вздрагивает», когда в неё влетает окно или приходит сообщение.</summary>
        public static double Kick(Settings s) => Math.Clamp(s.IslandBounce, 0, 100) / 35.0;

        public static double Radius(Settings s, double h, bool expanded)
        {
            double full = expanded ? Math.Min(h / 2, 34) : h / 2;
            double min = Math.Min(full, 7);
            return min + (full - min) * Math.Clamp(s.IslandRound, 0, 100) / 100.0;
        }

        public static bool Step(ref double x, ref double v, double target, double dt, Settings s)
        {
            if (Math.Abs(target - x) < 0.25 && Math.Abs(v) < 2) { if (x != target) { x = target; v = 0; return true; } return false; }
            var (k, d) = Spring(s);
            double a = k * (target - x) - d * v;
            v += a * dt;
            x += v * dt;
            if (x < 0) { x = 0; v = 0; }
            return true;
        }
    }

    /// <summary>
    /// Живой предпросмотр во вкладке «Dynamic Island»: маленький «экран» с капсулой сверху и окном.
    /// Двигаешь ползунки — капсула меняется пружинкой; «Проверить прыжок» — окно втягивается в капсулу и она подпрыгивает.
    /// </summary>
    internal sealed class IslandPreview : FrameworkElement
    {
        readonly Settings _s;
        double _w, _h, _vw, _vh;
        double _test = -1;     // время с начала проверки, -1 — нет
        bool _hooked, _parked;
        TimeSpan _last;

        static readonly Brush Screen = Freeze(new LinearGradientBrush(Color.FromRgb(0x07, 0x14, 0x3F), Color.FromRgb(0x10, 0x2C, 0x86), 60));
        static readonly Brush Glow = Freeze(new RadialGradientBrush(Color.FromArgb(0x90, 0x3B, 0x82, 0xF6), Color.FromArgb(0, 0x3B, 0x82, 0xF6)));
        static readonly Pen ScreenRim = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)), 1));
        static readonly Brush WinFill = Freeze(new LinearGradientBrush(Color.FromRgb(0x3B, 0x6F, 0xF0), Color.FromRgb(0x1E, 0x3A, 0x8A), 90));
        static readonly Brush WinBar = Freeze(new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)));
        static readonly Pen WinRim = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x60, 255, 255, 255)), 1));
        static readonly Pen PillRim = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x24, 255, 255, 255)), 1));
        static readonly Brush Dot = Freeze(new LinearGradientBrush(Color.FromRgb(0x7C, 0xB8, 0xFF), Color.FromRgb(0x2F, 0x6B, 0xFF), 90));

        public IslandPreview(Settings s)
        {
            _s = s;
            _w = s.IslandWidth; _h = s.IslandHeight;
            Height = 230;
            Loaded += (o, e) => Wake();
            Unloaded += (o, e) => Sleep();
            IsVisibleChanged += (o, e) => { if (IsVisible) Wake(); };
        }

        /// <summary>Настройки поменялись — капсула перетекает в новый размер.</summary>
        public void Apply() => Wake();

        public void Test()
        {
            _test = 0;
            _parked = false;
            Wake();
        }

        double Scale => Math.Max(0.4, ActualWidth / 480.0);

        void Wake()
        {
            if (_hooked || !IsVisible) { InvalidateVisual(); return; }
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
            bool moving = false;
            if (_test >= 0)
            {
                _test += dt;
                if (!_parked && _test >= 0.45)
                {
                    _parked = true;   // окно влетело — капсула «глотает» его
                    double k = IslandShape.Kick(_s);
                    _vw += 30 * 6 * k; _vh += 9 * 6 * k;
                }
                if (_test > 2.0) { _test = -1; _parked = false; }
                moving = true;
            }
            moving |= IslandShape.Step(ref _w, ref _vw, _s.IslandWidth + (_parked ? 22 : 0), dt, _s);
            moving |= IslandShape.Step(ref _h, ref _vh, _s.IslandHeight, dt, _s);
            InvalidateVisual();
            if (!moving) Sleep();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double W = ActualWidth, H = ActualHeight;
            if (W < 10 || H < 10) return;
            var scr = new Rect(0, 0, W, H);
            dc.DrawRoundedRectangle(Screen, ScreenRim, scr, 14, 14);
            dc.PushClip(new RectangleGeometry(scr, 14, 14));
            dc.DrawEllipse(Glow, null, new Point(W * 0.72, H * 0.25), W * 0.5, H * 0.6);

            double k = Scale;
            double pw = _w * k, ph = _h * k;
            var pill = new Rect(W / 2 - pw / 2, 6 * k, pw, ph);

            // Окно: стоит по центру; при проверке втягивается в капсулу
            var win = new Rect(W / 2 - W * 0.21, H * 0.42, W * 0.42, H * 0.42);
            double p = _test < 0 ? 0 : Math.Clamp(_test / 0.45, 0, 1);
            double back = _test > 1.45 ? Math.Clamp((_test - 1.45) / 0.35, 0, 1) : 0;
            if (_test < 0 || p < 1 || back > 0)
            {
                Rect r = win;
                double alpha = 1;
                if (_test >= 0 && back <= 0)
                {
                    double e = p * p * p;
                    var to = new Rect(W / 2 - 10 * k, pill.Top + ph / 2 - 6 * k, 20 * k, 12 * k);
                    r = new Rect(Lerp(win.X, to.X, e), Lerp(win.Y, to.Y, e), Lerp(win.Width, to.Width, e), Lerp(win.Height, to.Height, e));
                    alpha = 1 - Math.Clamp((p - 0.8) / 0.2, 0, 1);
                }
                else if (back > 0) alpha = back;
                dc.PushOpacity(alpha);
                dc.DrawRoundedRectangle(WinFill, WinRim, r, 8, 8);
                dc.DrawRoundedRectangle(WinBar, null, new Rect(r.X, r.Y, r.Width, Math.Min(r.Height, 14 * k)), 8, 8);
                dc.Pop();
            }

            double rad = IslandShape.Radius(_s, ph, false);
            dc.DrawRoundedRectangle(Brushes.Black, PillRim, pill, rad, rad);
            if (_parked && pw > 40)   // значок окна ждёт внутри капсулы
            {
                double d = Math.Min(ph * 0.62, 18 * k);
                dc.DrawRoundedRectangle(Dot, null, new Rect(pill.Right - d - ph * 0.22, pill.Top + (ph - d) / 2, d, d), d * 0.28, d * 0.28);
            }
            dc.Pop();
        }

        static double Lerp(double a, double b, double t) => a + (b - a) * t;
        static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
    }
}
