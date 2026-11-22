// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace MizuEngine
{
    /// <summary>
    /// Лайк (0.11.5): объёмное глянцевое сердечко (свой вектор в духе эмодзи — ободок, градиент, блики) и анимация как у
    /// реакций в Telegram: большое сердце появляется над кнопкой, падает в неё, переворачивается и пружинит,
    /// вокруг разлетаются искры и кольцо. Используется в постах, комментариях и сборках.
    /// </summary>
    internal static class Heart
    {
        // Сердце в квадрате 100×100 (широкие «доли», острый низ)
        const string PathData = "M50,91 C45.5,87.2 6,63 6,34.5 C6,19.5 17.2,9 30.6,9 C39.6,9 46.4,13.6 50,20.6 C53.6,13.6 60.4,9 69.4,9 C82.8,9 94,19.5 94,34.5 C94,63 54.5,87.2 50,91 Z";

        static Geometry _shape;
        static DrawingImage _image;

        public static Geometry HeartGeometry
        {
            get
            {
                if (_shape == null) { var g = Geometry.Parse(PathData); g.Freeze(); _shape = g; }
                return _shape;
            }
        }

        static Color C(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

        /// <summary>Глянцевое сердце. Рисуется один раз на всё приложение и только читается — память не растёт.</summary>
        public static DrawingImage Picture
        {
            get
            {
                if (_image != null) return _image;

                // Тело — то же сердце, чуть меньше: вокруг остаётся тёмный ободок
                var inner = HeartGeometry.Clone();
                inner.Transform = new TransformGroup { Children = { new TranslateTransform(-50, -48.6), new ScaleTransform(0.915, 0.915), new TranslateTransform(50, 50) } };
                inner.Freeze();

                var rim = new LinearGradientBrush(C(0xFFE4232B), C(0xFFB9070F), new Point(0, 0), new Point(0, 1));
                var body = new RadialGradientBrush
                {
                    Center = new Point(0.5, 0.42), GradientOrigin = new Point(0.5, 0.42), RadiusX = 0.62, RadiusY = 0.62,
                    GradientStops = { new GradientStop(C(0xFFFF4040), 0), new GradientStop(C(0xFFF42326), 0.65), new GradientStop(C(0xFFDD1219), 1) }
                };
                var gloss = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
                    GradientStops = { new GradientStop(C(0x6BFFFFFF), 0), new GradientStop(C(0x1FFFFFFF), 0.28), new GradientStop(C(0x00FFFFFF), 0.5) }
                };
                RadialGradientBrush Shine(byte a) => new RadialGradientBrush { GradientStops = { new GradientStop(Color.FromArgb(a, 255, 255, 255), 0), new GradientStop(Color.FromArgb(0, 255, 255, 255), 1) } };

                var shine1 = new EllipseGeometry(new Point(29, 24), 13, 8) { Transform = new RotateTransform(-28, 29, 24) };
                var shine2 = new EllipseGeometry(new Point(72, 22), 8, 5) { Transform = new RotateTransform(25, 72, 22) };

                var light = new DrawingGroup { ClipGeometry = inner };
                light.Children.Add(new GeometryDrawing(gloss, null, inner));
                light.Children.Add(new GeometryDrawing(Shine(0x80), null, shine1));
                light.Children.Add(new GeometryDrawing(Shine(0x4D), null, shine2));

                var all = new DrawingGroup();
                all.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 100, 100))));   // рамка 100×100 — сердце всегда по центру
                all.Children.Add(new GeometryDrawing(rim, null, HeartGeometry));
                all.Children.Add(new GeometryDrawing(body, null, inner));
                all.Children.Add(light);

                var img = new DrawingImage(all);
                img.Freeze();
                _image = img;
                return _image;
            }
        }

        /// <summary>Значок лайка: контур (не лайкнуто) или глянцевое сердце (лайкнуто).</summary>
        internal sealed class Icon : Grid
        {
            readonly Path _outline;
            readonly Image _filled;
            public bool Liked { get; private set; }

            public Icon(double size, bool liked)
            {
                Width = size; Height = size;
                VerticalAlignment = VerticalAlignment.Center;
                RenderTransformOrigin = new Point(0.5, 0.5);
                _outline = new Path { Data = HeartGeometry, Stretch = Stretch.Uniform, StrokeThickness = Math.Max(1.4, size / 10.5), Margin = new Thickness(size * 0.06), StrokeLineJoin = PenLineJoin.Round };
                _outline.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Text");
                _filled = new Image { Source = Picture, Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapScalingMode(_filled, BitmapScalingMode.HighQuality);
                Children.Add(_outline);
                Children.Add(_filled);
                Set(liked);
            }

            public void Set(bool liked)
            {
                Liked = liked;
                _outline.Visibility = liked ? Visibility.Collapsed : Visibility.Visible;
                _filled.Visibility = liked ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Кнопка лайка «сердце + число». toggle(новое состояние) отправляет лайк/снятие на сервер и возвращает ошибку (null — ок).
        /// Пока ответ не пришёл, кнопка уже показывает новое состояние (как в Telegram); ошибка — всё откатывается.
        /// </summary>
        public static Button LikeButton(bool liked, int count, Func<bool, Task<string>> toggle, Action<string> toast,
                                    double iconSize = 16, Action<bool, int> changed = null, bool enabled = true)
        {
            var icon = new Icon(iconSize, liked);
            var num = UI.T(count.ToString(), 13, true);
            num.Margin = new Thickness(7, 0, 0, 0);
            num.VerticalAlignment = VerticalAlignment.Center;
            var b = new Button { Style = (Style)UI.Owner.FindResource("GlassButton"), Padding = new Thickness(12, 6, 12, 6), Content = UI.Row(icon, num), IsEnabled = enabled };
            bool busy = false;
            b.Click += async (s, e) =>
            {
                if (busy) return;
                busy = true;
                bool was = icon.Liked;
                int was_n = count;
                count += was ? -1 : 1;
                icon.Set(!was);
                num.Text = count.ToString();
                if (!was) Burst(icon); else Unlike(icon);
                changed?.Invoke(!was, count);
                string err = await toggle(!was);
                if (err != null)
                {
                    count = was_n;
                    icon.Set(was);
                    num.Text = count.ToString();
                    changed?.Invoke(was, count);
                    toast?.Invoke(err);
                }
                busy = false;
            };
            return b;
        }

        /// <summary>Маленький лайк для комментариев: сердечко и число (0 не пишем), без рамки кнопки.</summary>
        public static FrameworkElement LikeChip(bool liked, int count, Func<bool, Task<string>> toggle, Action<string> toast, double iconSize = 13, bool enabled = true)
        {
            var icon = new Icon(iconSize, liked);
            icon.Opacity = 1;
            var num = UI.T(count > 0 ? count.ToString() : "", 11.5, true, sub: true);
            num.Margin = new Thickness(4, 0, 0, 0);
            num.VerticalAlignment = VerticalAlignment.Center;
            var chip = new Border { Background = Brushes.Transparent, Padding = new Thickness(6, 4, 6, 4), CornerRadius = new CornerRadius(9), Child = UI.Row(icon, num) };
            chip.ToolTip = Loc.T("likes.like");
            if (!enabled) { chip.Opacity = 0.55; return chip; }
            UI.Hand(chip);
            chip.MouseEnter += (s, e) => chip.SetResourceReference(Border.BackgroundProperty, "HoverBg");
            chip.MouseLeave += (s, e) => chip.Background = Brushes.Transparent;
            bool busy = false;
            chip.MouseLeftButtonUp += async (s, e) =>
            {
                e.Handled = true;
                if (busy) return;
                busy = true;
                bool was = icon.Liked;
                int old = count;
                count += was ? -1 : 1;
                icon.Set(!was);
                num.Text = count > 0 ? count.ToString() : "";
                if (!was) Burst(icon); else Unlike(icon);
                string err = await toggle(!was);
                if (err != null)
                {
                    count = old;
                    icon.Set(was);
                    num.Text = count > 0 ? count.ToString() : "";
                    toast?.Invoke(err);
                }
                busy = false;
            };
            return chip;
        }

        // ================== анимация ==================

        static readonly IEasingFunction OutBack = Frozen(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 });
        static readonly IEasingFunction InOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseInOut });
        static readonly IEasingFunction Out = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
        static readonly IEasingFunction In = Frozen(new QuadraticEase { EasingMode = EasingMode.EaseIn });
        static IEasingFunction Frozen(EasingFunctionBase e) { e.Freeze(); return e; }

        static DoubleAnimationUsingKeyFrames Keys(params (double ms, double v, IEasingFunction e)[] keys)
        {
            var a = new DoubleAnimationUsingKeyFrames();
            foreach (var k in keys)
                a.KeyFrames.Add(new EasingDoubleKeyFrame(k.v, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(Motion.Ms(k.ms))), k.e));
            a.Freeze();
            return a;
        }

        /// <summary>Сняли лайк: сердце чуть сжимается и возвращается контуром.</summary>
        public static void Unlike(FrameworkElement icon) => Motion.Pop(icon, 0.75);

        /// <summary>Поставили лайк — полная анимация поверх окна. Без слоя или с выключенными анимациями — простая «пружинка».</summary>
        public static void Burst(FrameworkElement icon)
        {
            var layer = Motion.Layer;
            if (Motion.Off || layer == null || !icon.IsVisible || icon.ActualWidth < 1) { Motion.Pop(icon, 1.35); return; }
            Rect r;
            try { r = icon.TransformToVisual(layer).TransformBounds(new Rect(0, 0, icon.ActualWidth, icon.ActualHeight)); }
            catch { Motion.Pop(icon, 1.35); return; }

            double cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, s = Math.Max(10, r.Width);
            const double K = 2.6;              // большое сердце в 2.6 раза больше значка
            double big = s * K, fin = 1 / K;   // масштаб, при котором большое сердце совпадает со значком

            // Настоящий значок прячем, пока летит большое сердце
            icon.BeginAnimation(UIElement.OpacityProperty, null);
            icon.Opacity = 0;

            // ---- 1. большое сердце: появляется над кнопкой → падает в неё → переворачивается → пружинит ----
            var img = new Image { Source = Picture, Width = big, Height = big, IsHitTestVisible = false, RenderTransformOrigin = new Point(0.5, 0.5), Opacity = 0 };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var sc = new ScaleTransform(0.25, 0.25);
            var tr = new TranslateTransform(0, -s * 1.7);
            img.RenderTransform = new TransformGroup { Children = { sc, tr } };
            Canvas.SetLeft(img, cx - big / 2);
            Canvas.SetTop(img, cy - big / 2);
            layer.Children.Add(img);

            img.BeginAnimation(UIElement.OpacityProperty, Keys((0, 0, null), (90, 1, Out)));
            tr.BeginAnimation(TranslateTransform.YProperty, Keys((0, -s * 1.7, null), (200, -s * 1.7, null), (430, 0, InOut)));
            // Переворот вокруг вертикальной оси — сердце сужается и снова расширяется, затем «сплющивается» и выпрямляется
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, Keys((0, 0.25, null), (200, 1.0, OutBack), (430, fin, InOut),
                                                                  (520, fin * 0.08, In), (610, fin, Out),
                                                                  (690, fin * 1.22, Out), (800, fin * 0.94, InOut), (880, fin, InOut)));
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, Keys((0, 0.25, null), (200, 1.0, OutBack), (430, fin, InOut),
                                                                  (610, fin, null),
                                                                  (690, fin * 0.84, Out), (800, fin * 1.06, InOut), (880, fin, InOut)));

            // ---- 2. кольцо и искры в момент «приземления» ----
            Ring(layer, cx, cy, s, 420);
            var rnd = new Random();
            int n = 10;
            for (int i = 0; i < n; i++)
            {
                double ang = (i * 360.0 / n + rnd.Next(-14, 15)) * Math.PI / 180;
                double dist = s * (1.25 + rnd.NextDouble() * 0.8);
                bool heart = i % 3 == 0;
                double size = heart ? s * (0.38 + rnd.NextDouble() * 0.14) : s * (0.14 + rnd.NextDouble() * 0.1);
                FrameworkElement p;
                if (heart) p = new Image { Source = Picture, Width = size, Height = size };
                else
                {
                    var dot = new Ellipse { Width = size, Height = size };
                    dot.Fill = new SolidColorBrush((i % 3) == 1 ? C(0xFFFF5C7A) : C(0xFFFFB3C6));
                    p = dot;
                }
                Spark(layer, p, cx, cy, size, Math.Cos(ang) * dist, Math.Sin(ang) * dist, 430 + rnd.Next(0, 40), heart ? rnd.Next(-40, 41) : 0);
            }

            // ---- 3. большое сердце превращается в настоящий значок ----
            var done = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Motion.Ms(880)) };
            done.Tick += (o, e) =>
            {
                done.Stop();
                icon.Opacity = 1;
                // Ещё кадр держим большое сердце поверх — без мигания между ними
                var gone = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(34) };
                gone.Tick += (o2, e2) => { gone.Stop(); layer.Children.Remove(img); };
                gone.Start();
            };
            done.Start();
        }

        static void Ring(Canvas layer, double cx, double cy, double s, double delay)
        {
            var ring = new Ellipse { Width = s * 1.2, Height = s * 1.2, StrokeThickness = Math.Max(1.5, s / 9), Stroke = new SolidColorBrush(C(0xFFFF3B5C)), IsHitTestVisible = false, Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5) };
            var sc = new ScaleTransform(0.5, 0.5);
            ring.RenderTransform = sc;
            Canvas.SetLeft(ring, cx - ring.Width / 2);
            Canvas.SetTop(ring, cy - ring.Height / 2);
            layer.Children.Add(ring);
            var grow = Keys((0, 0.5, null), (delay, 0.5, null), (delay + 460, 2.1, Out));
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            var fade = Keys((0, 0, null), (delay, 0.75, null), (delay + 460, 0, Out));
            var clock = fade.CreateClock();
            clock.Completed += (o, e) => layer.Children.Remove(ring);
            ring.ApplyAnimationClock(UIElement.OpacityProperty, clock);
        }

        static void Spark(Canvas layer, FrameworkElement p, double cx, double cy, double size, double dx, double dy, double delay, double spin)
        {
            p.IsHitTestVisible = false;
            p.Opacity = 0;
            p.RenderTransformOrigin = new Point(0.5, 0.5);
            var sc = new ScaleTransform(0.3, 0.3);
            var rot = new RotateTransform(0);
            var tr = new TranslateTransform();
            p.RenderTransform = new TransformGroup { Children = { sc, rot, tr } };
            Canvas.SetLeft(p, cx - size / 2);
            Canvas.SetTop(p, cy - size / 2);
            layer.Children.Add(p);

            double life = 560;
            tr.BeginAnimation(TranslateTransform.XProperty, Keys((0, 0, null), (delay, 0, null), (delay + life, dx, Out)));
            tr.BeginAnimation(TranslateTransform.YProperty, Keys((0, 0, null), (delay, 0, null), (delay + life, dy, Out)));
            var scale = Keys((0, 0.3, null), (delay, 0.3, null), (delay + life * 0.35, 1.1, Out), (delay + life, 0.2, In));
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
            if (spin != 0) rot.BeginAnimation(RotateTransform.AngleProperty, Keys((0, 0, null), (delay, 0, null), (delay + life, spin, Out)));
            var fade = Keys((0, 0, null), (delay, 1, null), (delay + life * 0.55, 1, null), (delay + life, 0, In));
            var clock = fade.CreateClock();
            clock.Completed += (o, e) => layer.Children.Remove(p);
            p.ApplyAnimationClock(UIElement.OpacityProperty, clock);
        }
    }
}
