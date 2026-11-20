// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace MizuEngine
{
    /// <summary>
    /// Живые баннеры и рамки аватарки (Mizu Plus). Всё рисуется самой программой, 30 кадров в секунду,
    /// и только пока элемент на экране — в фоне ничего не считается.
    /// </summary>
    internal static class ProfileFx
    {
        public static readonly string[] Banners = { "aurora", "neon", "fire", "snow", "sakura", "rainbow", "stars", "ocean" };
        public static readonly string[] Frames = { "neon", "fire", "snow", "sakura", "rainbow", "stars", "gold", "ice" };

        public static bool IsBanner(string id) => Array.IndexOf(Banners, id) >= 0;
        public static bool IsFrame(string id) => Array.IndexOf(Frames, id) >= 0;

        // ================== общий таймер ==================

        static readonly List<FxElement> Live = new List<FxElement>();
        static TimeSpan _last;
        static bool _hooked;

        internal static void Add(FxElement e)
        {
            if (!Live.Contains(e)) Live.Add(e);
            if (!_hooked) { CompositionTarget.Rendering += OnFrame; _hooked = true; }
        }

        internal static void Remove(FxElement e)
        {
            Live.Remove(e);
            if (Live.Count == 0 && _hooked) { CompositionTarget.Rendering -= OnFrame; _hooked = false; }
        }

        static void OnFrame(object sender, EventArgs e)
        {
            var t = ((RenderingEventArgs)e).RenderingTime;
            if ((t - _last).TotalMilliseconds < 31) return;   // ~30 к/с хватает, а нагрузки вдвое меньше
            double dt = Math.Min(0.1, (t - _last).TotalSeconds);
            _last = t;
            foreach (var x in Live.ToArray())
                if (x.IsVisible) x.Tick(dt);
        }

        internal static Color Hsl(double h, double s, double l, byte a = 255)
        {
            var c = UI.FromHsl(((h % 360) + 360) % 360, s, l);
            return Color.FromArgb(a, c.R, c.G, c.B);
        }

        static Brush Frozen(Brush b) { b.Freeze(); return b; }
        static Pen Frozen(Pen p) { p.Freeze(); return p; }

        internal static readonly Brush White = Frozen(new SolidColorBrush(Colors.White));

        internal static Brush Soft(Color c, double a)
        {
            var b = new RadialGradientBrush(Color.FromArgb((byte)(255 * a), c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        // Частица: позиция, скорость, размер, фаза, жизнь
        internal struct P { public double X, Y, Vx, Vy, S, Ph, Life, Max; public int K; }

        /// <summary>Четырёхлучевая звёздочка (блеск).</summary>
        internal static void Sparkle(DrawingContext dc, double x, double y, double r, Brush b)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(x, y - r), true, true);
                c.QuadraticBezierTo(new Point(x, y), new Point(x + r, y), true, false);
                c.QuadraticBezierTo(new Point(x, y), new Point(x, y + r), true, false);
                c.QuadraticBezierTo(new Point(x, y), new Point(x - r, y), true, false);
                c.QuadraticBezierTo(new Point(x, y), new Point(x, y - r), true, false);
            }
            g.Freeze();
            dc.DrawGeometry(b, null, g);
        }

        /// <summary>Лепесток сакуры.</summary>
        internal static void Petal(DrawingContext dc, double x, double y, double s, double angle, double flip, Brush b)
        {
            dc.PushTransform(new TranslateTransform(x, y));
            dc.PushTransform(new RotateTransform(angle));
            dc.PushTransform(new ScaleTransform(Math.Max(0.25, Math.Abs(flip)), 1));
            dc.DrawEllipse(b, null, new Point(0, 0), s * 0.55, s);
            dc.Pop(); dc.Pop(); dc.Pop();
        }

        internal static readonly Brush PetalBrush = Frozen(new LinearGradientBrush(Color.FromRgb(0xFF, 0xD1, 0xE3), Color.FromRgb(0xFF, 0x8F, 0xB8), 90));
        internal static readonly Brush FlameBrush = Frozen(new RadialGradientBrush(new GradientStopCollection
        {
            new GradientStop(Color.FromArgb(0xF0, 0xFF, 0xF2, 0xB0), 0),
            new GradientStop(Color.FromArgb(0xC0, 0xFF, 0x9A, 0x1F), 0.45),
            new GradientStop(Color.FromArgb(0x00, 0xE8, 0x2A, 0x0A), 1)
        }));
        internal static readonly Brush SnowBrush = Frozen(new RadialGradientBrush(Color.FromArgb(0xF5, 255, 255, 255), Color.FromArgb(0x00, 255, 255, 255)) { GradientOrigin = new Point(0.5, 0.5) });
        internal static readonly Brush StarBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xF8, 0xE0)));
    }

    /// <summary>Основа: подписывается на общий таймер, только пока на экране.</summary>
    internal abstract class FxElement : FrameworkElement
    {
        protected double T;
        protected readonly Random R = new Random();

        protected FxElement()
        {
            IsHitTestVisible = false;
            Loaded += (s, e) => ProfileFx.Add(this);
            Unloaded += (s, e) => ProfileFx.Remove(this);
        }

        internal void Tick(double dt)
        {
            T += dt;
            Step(dt);
            InvalidateVisual();
        }

        protected abstract void Step(double dt);
    }

    // ======================================================================

    /// <summary>Живой баннер: рисуется поверх картинки баннера (или градиента).</summary>
    internal sealed class BannerFx : FxElement
    {
        readonly string _kind;
        readonly List<ProfileFx.P> _p = new List<ProfileFx.P>();
        readonly double _radius;

        public BannerFx(string kind, double topRadius = 27) { _kind = kind; _radius = topRadius; }

        double W => Math.Max(1, ActualWidth);
        double H => Math.Max(1, ActualHeight);

        int Wanted => _kind switch { "fire" => 46, "snow" => 60, "sakura" => 22, "stars" => 34, _ => 0 };

        ProfileFx.P Spawn(bool anywhere)
        {
            var p = new ProfileFx.P { Ph = R.NextDouble() * Math.PI * 2 };
            switch (_kind)
            {
                case "fire":
                    p.X = R.NextDouble() * W; p.Y = H + 6; p.Vx = (R.NextDouble() - 0.5) * 14; p.Vy = -(26 + R.NextDouble() * 40);
                    p.S = 7 + R.NextDouble() * 12; p.Max = 1.2 + R.NextDouble() * 1.4;
                    if (anywhere) p.Life = R.NextDouble() * p.Max;
                    break;
                case "snow":
                    p.X = R.NextDouble() * W; p.Y = anywhere ? R.NextDouble() * H : -6; p.Vy = 10 + R.NextDouble() * 22; p.S = 1.2 + R.NextDouble() * 2.6;
                    break;
                case "sakura":
                    p.X = R.NextDouble() * W; p.Y = anywhere ? R.NextDouble() * H : -10; p.Vy = 14 + R.NextDouble() * 16; p.Vx = 8 + R.NextDouble() * 14;
                    p.S = 4 + R.NextDouble() * 3.5; p.K = R.Next(360);
                    break;
                case "stars":
                    p.X = R.NextDouble() * W; p.Y = R.NextDouble() * H * 0.9; p.S = 1 + R.NextDouble() * 2.4; p.Vy = 0.6 + R.NextDouble() * 1.6;
                    break;
            }
            return p;
        }

        // Падающая звезда
        double _shootAt = 2, _shootT = -1, _sx, _sy;

        protected override void Step(double dt)
        {
            if (_p.Count == 0) for (int i = 0; i < Wanted; i++) _p.Add(Spawn(true));
            for (int i = 0; i < _p.Count; i++)
            {
                var p = _p[i];
                p.Life += dt;
                switch (_kind)
                {
                    case "fire":
                        p.X += (p.Vx + Math.Sin(T * 3 + p.Ph) * 10) * dt; p.Y += p.Vy * dt;
                        if (p.Life > p.Max) p = Spawn(false);
                        break;
                    case "snow":
                        p.X += Math.Sin(T * 0.9 + p.Ph) * 9 * dt; p.Y += p.Vy * dt;
                        if (p.Y > H + 6) p = Spawn(false);
                        if (p.X < -6) p.X = W + 4; else if (p.X > W + 6) p.X = -4;
                        break;
                    case "sakura":
                        p.X += (p.Vx + Math.Sin(T * 1.4 + p.Ph) * 12) * dt; p.Y += p.Vy * dt; p.K += (int)(90 * dt) + 1;
                        if (p.Y > H + 10 || p.X > W + 12) { p = Spawn(false); if (R.Next(2) == 0) { p.X = -8; p.Y = R.NextDouble() * H; } }
                        break;
                }
                _p[i] = p;
            }
            if (_kind == "stars")
            {
                if (_shootT < 0 && T > _shootAt) { _shootT = 0; _sx = W * (0.2 + R.NextDouble() * 0.6); _sy = H * R.NextDouble() * 0.3; }
                if (_shootT >= 0) { _shootT += dt; if (_shootT > 0.9) { _shootT = -1; _shootAt = T + 3 + R.NextDouble() * 4; } }
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = W, h = H;
            dc.PushClip(TopRounded(w, h, _radius));
            switch (_kind)
            {
                case "aurora": Aurora(dc, w, h); break;
                case "neon": Neon(dc, w, h); break;
                case "rainbow": Rainbow(dc, w, h); break;
                case "ocean": Ocean(dc, w, h); break;
                case "fire":
                    dc.DrawRectangle(new LinearGradientBrush(Color.FromArgb(0, 0xFF, 0x50, 0x10), Color.FromArgb(0x70, 0xFF, 0x50, 0x10), 90), null, new Rect(0, h * 0.45, w, h * 0.55));
                    foreach (var p in _p)
                    {
                        double k = 1 - p.Life / p.Max, s = p.S * (0.4 + k * 0.8);
                        dc.PushOpacity(Math.Clamp(k * 1.3, 0, 1));
                        dc.DrawEllipse(ProfileFx.FlameBrush, null, new Point(p.X, p.Y), s, s * 1.35);
                        dc.Pop();
                    }
                    break;
                case "snow":
                    foreach (var p in _p) dc.DrawEllipse(ProfileFx.SnowBrush, null, new Point(p.X, p.Y), p.S * 1.6, p.S * 1.6);
                    break;
                case "sakura":
                    foreach (var p in _p) ProfileFx.Petal(dc, p.X, p.Y, p.S, p.K, Math.Sin(T * 2.4 + p.Ph), ProfileFx.PetalBrush);
                    break;
                case "stars":
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x30, 0x08, 0x0A, 0x30)), null, new Rect(0, 0, w, h));
                    foreach (var p in _p)
                    {
                        double tw = 0.35 + 0.65 * Math.Abs(Math.Sin(T * p.Vy + p.Ph));
                        dc.PushOpacity(tw);
                        ProfileFx.Sparkle(dc, p.X, p.Y, p.S * 2.2, ProfileFx.StarBrush);
                        dc.Pop();
                    }
                    if (_shootT >= 0)
                    {
                        double k = _shootT / 0.9, x = _sx + k * w * 0.35, y = _sy + k * h * 0.35;
                        var tail = new LinearGradientBrush(Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0xE0, 255, 255, 255), new Point(0, 0), new Point(1, 1));
                        dc.PushOpacity(Math.Sin(k * Math.PI));
                        dc.DrawLine(new Pen(tail, 2), new Point(x - 60, y - 60), new Point(x, y));
                        dc.Pop();
                    }
                    break;
            }
            dc.Pop();
        }

        static Geometry TopRounded(double w, double h, double r)
        {
            r = Math.Min(r, Math.Min(w, h) / 2);
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(0, h), true, true);
                c.LineTo(new Point(0, r), false, false);
                c.ArcTo(new Point(r, 0), new Size(r, r), 0, false, SweepDirection.Clockwise, false, false);
                c.LineTo(new Point(w - r, 0), false, false);
                c.ArcTo(new Point(w, r), new Size(r, r), 0, false, SweepDirection.Clockwise, false, false);
                c.LineTo(new Point(w, h), false, false);
            }
            g.Freeze();
            return g;
        }

        void Aurora(DrawingContext dc, double w, double h)
        {
            (Color c, double y, double amp, double sp)[] bands =
            {
                (Color.FromRgb(0x3C, 0xF2, 0xA6), 0.42, 0.16, 0.55),
                (Color.FromRgb(0x2E, 0xC5, 0xFF), 0.55, 0.13, 0.4),
                (Color.FromRgb(0xA7, 0x6B, 0xFF), 0.32, 0.12, 0.7)
            };
            int i = 0;
            foreach (var (c, yk, amp, sp) in bands)
            {
                var g = new StreamGeometry();
                double baseY = h * yk;
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(0, baseY), true, true);
                    for (double x = 0; x <= w + 8; x += 8)
                        ctx.LineTo(new Point(x, baseY + Math.Sin(x / w * 5 + T * sp + i * 2) * h * amp + Math.Sin(x / w * 11 - T * sp * 1.7) * h * amp * 0.35), true, true);
                    for (double x = w + 8; x >= 0; x -= 8)
                        ctx.LineTo(new Point(x, baseY + h * 0.22 + Math.Sin(x / w * 4 + T * sp * 0.8 + i) * h * amp * 0.7), true, true);
                }
                g.Freeze();
                var b = new LinearGradientBrush(Color.FromArgb(0x00, c.R, c.G, c.B), Color.FromArgb(0x00, c.R, c.G, c.B), 90);
                b.GradientStops.Insert(1, new GradientStop(Color.FromArgb((byte)(0x70 + 0x30 * Math.Sin(T * 0.8 + i)), c.R, c.G, c.B), 0.45));
                dc.DrawGeometry(b, null, g);
                i++;
            }
        }

        void Neon(DrawingContext dc, double w, double h)
        {
            double a = T * 0.6;
            var grad = new LinearGradientBrush
            {
                StartPoint = new Point(0.5 + Math.Cos(a) * 0.5, 0.5 + Math.Sin(a) * 0.5),
                EndPoint = new Point(0.5 - Math.Cos(a) * 0.5, 0.5 - Math.Sin(a) * 0.5),
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(0xFF, 0x2D, 0xD1), 0),
                    new GradientStop(Color.FromRgb(0x7B, 0x5C, 0xFF), 0.5),
                    new GradientStop(Color.FromRgb(0x00, 0xE5, 0xFF), 1)
                }
            };
            var rect = new Rect(2, 2, w - 4, h + 30);
            dc.PushOpacity(0.28 + 0.12 * Math.Sin(T * 2.2));
            dc.DrawRoundedRectangle(null, new Pen(grad, 12), rect, _radius, _radius);
            dc.Pop();
            dc.DrawRoundedRectangle(null, new Pen(grad, 2.6), rect, _radius - 1, _radius - 1);
            // Бегущая полоса света
            double x = (T * 140) % (w + 240) - 120;
            var shine = new LinearGradientBrush(Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 0);
            shine.GradientStops.Insert(1, new GradientStop(Color.FromArgb(0x38, 0xC8, 0xF0, 0xFF), 0.5));
            dc.PushTransform(new SkewTransform(-25, 0));
            dc.DrawRectangle(shine, null, new Rect(x, 0, 120, h));
            dc.Pop();
        }

        void Rainbow(DrawingContext dc, double w, double h)
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0.4), MappingMode = BrushMappingMode.RelativeToBoundingBox, SpreadMethod = GradientSpreadMethod.Repeat };
            double off = (T * 0.12) % 1;
            for (int i = 0; i <= 6; i++) b.GradientStops.Add(new GradientStop(ProfileFx.Hsl(i * 60 + 0, 0.95, 0.6, 0x60), i / 6.0));
            b.Transform = new TranslateTransform(off * w, 0);
            dc.DrawRectangle(b, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new LinearGradientBrush(Color.FromArgb(0x30, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90), null, new Rect(0, 0, w, h * 0.5));
        }

        void Ocean(DrawingContext dc, double w, double h)
        {
            (Color c, double y, double amp, double sp, double len)[] waves =
            {
                (Color.FromArgb(0x60, 0x6F, 0xD8, 0xFF), 0.58, 7, 1.1, 1.3),
                (Color.FromArgb(0x80, 0x2A, 0x8C, 0xF0), 0.70, 9, -0.8, 1.0),
                (Color.FromArgb(0xA0, 0x12, 0x4F, 0xC8), 0.82, 6, 1.5, 1.7)
            };
            foreach (var (c, yk, amp, sp, len) in waves)
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(0, h), true, true);
                    for (double x = 0; x <= w + 6; x += 6)
                        ctx.LineTo(new Point(x, h * yk + Math.Sin(x / w * Math.PI * 2 * len + T * sp) * amp), true, true);
                    ctx.LineTo(new Point(w + 6, h), true, true);
                }
                g.Freeze();
                dc.DrawGeometry(new SolidColorBrush(c), null, g);
            }
            // Блики на воде
            for (int i = 0; i < 9; i++)
            {
                double x = (i * 53 + T * 18) % (w + 20) - 10, y = h * (0.62 + (i % 3) * 0.1);
                dc.PushOpacity(0.25 + 0.5 * Math.Abs(Math.Sin(T * 1.8 + i)));
                ProfileFx.Sparkle(dc, x, y, 3, ProfileFx.White);
                dc.Pop();
            }
        }
    }

    // ======================================================================

    /// <summary>Рамка вокруг круглой аватарки. Элемент больше аватарки на Pad со всех сторон.</summary>
    internal sealed class FrameFx : FxElement
    {
        public const double Pad = 14;
        readonly string _kind;
        readonly double _avatar;   // диаметр аватарки вместе с её ободком
        readonly List<ProfileFx.P> _p = new List<ProfileFx.P>();

        public FrameFx(string kind, double avatarSize)
        {
            _kind = kind;
            _avatar = avatarSize;
            Width = Height = avatarSize + Pad * 2;
        }

        double C => (_avatar + Pad * 2) / 2;
        double Rr => _avatar / 2 + 1.5;

        protected override void Step(double dt)
        {
            int want = _kind switch { "fire" => 26, "snow" => 14, "sakura" => 7, "stars" => 9, "ice" => 6, _ => 0 };
            if (_p.Count == 0)
                for (int i = 0; i < want; i++)
                    _p.Add(new ProfileFx.P { Ph = R.NextDouble() * Math.PI * 2, S = R.NextDouble(), Life = R.NextDouble() * 1.5, Max = 1 + R.NextDouble() * 0.8, Vy = 0.3 + R.NextDouble() * 0.5 });
            for (int i = 0; i < _p.Count; i++)
            {
                var p = _p[i];
                p.Life += dt;
                if (_kind == "fire" && p.Life > p.Max) { p.Life = 0; p.Ph = R.NextDouble() * Math.PI * 2; p.S = R.NextDouble(); }
                if (_kind == "stars" && p.Life > p.Max * 2) { p.Life = 0; p.Ph = R.NextDouble() * Math.PI * 2; }
                _p[i] = p;
            }
        }

        Point On(double angle, double r) => new Point(C + Math.Cos(angle) * r, C + Math.Sin(angle) * r);

        /// <summary>Кольцо из коротких дуг разного цвета (как «конический» градиент).</summary>
        void Ring(DrawingContext dc, double r, double thick, Func<double, Color> color, int n = 72)
        {
            for (int i = 0; i < n; i++)
            {
                double a0 = i * Math.PI * 2 / n, a1 = (i + 1.15) * Math.PI * 2 / n;
                var pen = new Pen(new SolidColorBrush(color(a0)), thick) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(On(a0, r), false, false);
                    c.ArcTo(On(a1, r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                }
                g.Freeze();
                dc.DrawGeometry(null, pen, g);
            }
        }

        void Glow(DrawingContext dc, Color c, double alpha, double width)
        {
            var b = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), (Rr - width) / (Rr + width)),
                    new GradientStop(Color.FromArgb((byte)(255 * alpha), c.R, c.G, c.B), Rr / (Rr + width)),
                    new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1)
                }
            };
            dc.DrawEllipse(b, null, new Point(C, C), Rr + width, Rr + width);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double r = Rr;
            switch (_kind)
            {
                case "neon":
                {
                    Glow(dc, Color.FromRgb(0xB4, 0x4C, 0xFF), 0.45 + 0.15 * Math.Sin(T * 2.5), 11);
                    Ring(dc, r, 4, a => { double k = (Math.Sin(a + T * 2.2) + 1) / 2; return UI.Mix(Color.FromRgb(0xFF, 0x2D, 0xD1), Color.FromRgb(0x00, 0xE5, 0xFF), k); });
                    break;
                }
                case "rainbow":
                    Glow(dc, ProfileFx.Hsl(T * 60, 0.9, 0.6), 0.35, 9);
                    Ring(dc, r, 4.5, a => ProfileFx.Hsl(a * 180 / Math.PI + T * 120, 0.95, 0.6));
                    break;
                case "gold":
                    Ring(dc, r, 5, a =>
                    {
                        double shine = Math.Pow(Math.Max(0, Math.Cos(a - T * 1.6)), 14);
                        return UI.Mix(UI.Mix(Color.FromRgb(0xB8, 0x86, 0x1B), Color.FromRgb(0xF6, 0xD3, 0x65), (Math.Sin(a * 3) + 1) / 2), Colors.White, shine * 0.85);
                    });
                    for (int i = 0; i < 3; i++)
                    {
                        double a = T * 0.7 + i * 2.1;
                        dc.PushOpacity(0.4 + 0.6 * Math.Abs(Math.Sin(T * 2 + i)));
                        var pt = On(a, r);
                        ProfileFx.Sparkle(dc, pt.X, pt.Y, 4.5, ProfileFx.StarBrush);
                        dc.Pop();
                    }
                    break;
                case "ice":
                    Glow(dc, Color.FromRgb(0x9A, 0xE6, 0xFF), 0.35, 9);
                    Ring(dc, r, 4.5, a =>
                    {
                        double shine = Math.Pow(Math.Max(0, Math.Cos(a + T * 1.2)), 10);
                        return UI.Mix(UI.Mix(Color.FromRgb(0x7F, 0xCF, 0xFF), Color.FromRgb(0xE4, 0xF7, 0xFF), (Math.Sin(a * 4) + 1) / 2), Colors.White, shine);
                    });
                    foreach (var p in _p)
                    {
                        var pt = On(p.Ph + T * 0.25, r + 3);
                        dc.PushOpacity(0.5 + 0.5 * Math.Sin(T * 2 + p.Ph));
                        ProfileFx.Sparkle(dc, pt.X, pt.Y, 3.5, ProfileFx.White);
                        dc.Pop();
                    }
                    break;
                case "fire":
                    Glow(dc, Color.FromRgb(0xFF, 0x6A, 0x10), 0.45 + 0.1 * Math.Sin(T * 7), 12);
                    Ring(dc, r, 4, a => UI.Mix(Color.FromRgb(0xFF, 0x3D, 0x0A), Color.FromRgb(0xFF, 0xC4, 0x2E), (Math.Sin(a * 6 + T * 5) + 1) / 2));
                    foreach (var p in _p)
                    {
                        double k = p.Life / p.Max;
                        // Языки пламени чаще сверху
                        double ang = -Math.PI / 2 + (p.Ph - Math.PI) * 0.8;
                        var b0 = On(ang, r);
                        double up = k * 16;
                        dc.PushOpacity(Math.Clamp(1 - k, 0, 1));
                        dc.DrawEllipse(ProfileFx.FlameBrush, null, new Point(b0.X + Math.Sin(T * 6 + p.Ph) * 2, b0.Y - up), 4 + p.S * 3, 5 + p.S * 4);
                        dc.Pop();
                    }
                    break;
                case "snow":
                    Ring(dc, r, 3.5, a => UI.Mix(Color.FromRgb(0xDF, 0xF1, 0xFF), Colors.White, (Math.Sin(a * 5 + T) + 1) / 2));
                    foreach (var p in _p)
                    {
                        double a = p.Ph + T * p.Vy * 0.6;
                        var pt = On(a, r + 4 + Math.Sin(T * 1.5 + p.Ph) * 4);
                        dc.DrawEllipse(ProfileFx.SnowBrush, null, pt, 2 + p.S * 2, 2 + p.S * 2);
                    }
                    break;
                case "sakura":
                    Ring(dc, r, 3.5, a => UI.Mix(Color.FromRgb(0xFF, 0x9E, 0xC4), Color.FromRgb(0xFF, 0xE0, 0xEC), (Math.Sin(a * 3 - T) + 1) / 2));
                    foreach (var p in _p)
                    {
                        double a = p.Ph + T * 0.45;
                        var pt = On(a, r + 5);
                        ProfileFx.Petal(dc, pt.X, pt.Y, 4.2 + p.S * 1.5, a * 180 / Math.PI + 90, Math.Sin(T * 2 + p.Ph), ProfileFx.PetalBrush);
                    }
                    break;
                case "stars":
                    Ring(dc, r, 2.5, a => UI.Mix(Color.FromRgb(0x6C, 0x5C, 0xE8), Color.FromRgb(0xF6, 0xD3, 0x65), (Math.Sin(a * 2 + T) + 1) / 2));
                    foreach (var p in _p)
                    {
                        double k = p.Life / (p.Max * 2);
                        var pt = On(p.Ph, r + 2 + p.S * 6);
                        dc.PushOpacity(Math.Sin(Math.Clamp(k, 0, 1) * Math.PI));
                        ProfileFx.Sparkle(dc, pt.X, pt.Y, 3 + p.S * 4, ProfileFx.StarBrush);
                        dc.Pop();
                    }
                    break;
            }
        }
    }
}
