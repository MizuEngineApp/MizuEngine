// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace MizuEngine
{
    /// <summary>Маленькие «кирпичики» интерфейса в стиле приложения — чтобы страницы собирались в коде коротко и одинаково.</summary>
    internal static class UI
    {
        public static FrameworkElement Owner;   // главное окно: из него берутся стили

        static Style St(string key) => (Style)Owner.FindResource(key);

        public static TextBlock T(string text, double size = 14, bool bold = false, bool sub = false, bool wrap = false)
        {
            var t = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
            t.SetResourceReference(TextBlock.ForegroundProperty, sub ? "SubText" : "Text");
            if (wrap) t.TextWrapping = TextWrapping.Wrap;
            return t;
        }

        /// <summary>Текст из перевода — меняется сразу при смене языка.</summary>
        public static TextBlock L(string key, double size = 14, bool bold = false, bool sub = false, bool wrap = false)
        {
            var t = T("", size, bold, sub, wrap);
            t.SetResourceReference(TextBlock.TextProperty, key);
            return t;
        }

        public static TextBlock H1(string key) { var t = L(key, 28, true); return t; }

        public static TextBlock Accent(string text, double size = 14, bool bold = true)
        {
            var t = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
            t.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            return t;
        }

        public static TextBlock Error()
        {
            return new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        }

        public static void ShowError(TextBlock t, string msg)
        {
            t.Text = msg ?? "";
            t.Visibility = string.IsNullOrEmpty(msg) ? Visibility.Collapsed : Visibility.Visible;
        }

        public static Border Card(UIElement child, double top = 14)
        {
            return new Border { Style = St("Panel"), Child = child, Margin = new Thickness(0, top, 0, 0) };
        }

        public static Button Primary(string key, RoutedEventHandler click)
        {
            var b = new Button { Style = St("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Left };
            b.SetResourceReference(ContentControl.ContentProperty, key);
            if (click != null) b.Click += click;
            return b;
        }

        /// <summary>Стеклянная кнопка с иконкой и текстом.</summary>
        public static Button Glass(string glyph, string key, RoutedEventHandler click, double pad = 10)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (glyph != null) sp.Children.Add(Icon(glyph, 14, accent: true));
            if (key != null)
            {
                var t = L(key, 13, true);
                t.Margin = new Thickness(glyph != null ? 8 : 0, 0, 0, 0);
                t.VerticalAlignment = VerticalAlignment.Center;
                sp.Children.Add(t);
            }
            var b = new Button { Style = St("GlassButton"), Padding = new Thickness(pad + 4, pad, pad + 4, pad), Content = sp, HorizontalAlignment = HorizontalAlignment.Left };
            if (click != null) b.Click += click;
            return b;
        }

        public static TextBlock Icon(string glyph, double size = 14, bool accent = false)
        {
            var t = new TextBlock { Text = glyph, FontSize = size, FontFamily = (FontFamily)Owner.FindResource("Icons"), VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, accent ? "Accent" : "Text");
            return t;
        }

        public static TextBox Field(string text = "", int max = 200, bool multiline = false)
        {
            var tb = new TextBox { Style = St("Field"), Text = text ?? "", MaxLength = max };
            if (multiline) { tb.AcceptsReturn = true; tb.TextWrapping = TextWrapping.Wrap; tb.MinHeight = 80; tb.VerticalContentAlignment = VerticalAlignment.Top; }
            return tb;
        }

        public static PasswordBox Password() => new PasswordBox { Style = St("PassField"), MaxLength = 72 };

        /// <summary>
        /// Поле пароля с «глазиком» справа: нажал — пароль виден обычным текстом, нажал ещё раз — снова точки.
        /// Возвращает само поле и функцию, которая отдаёт введённый пароль.
        /// </summary>
        public static (FrameworkElement ui, Func<string> value) Secret()
        {
            var grid = new Grid();
            var hidden = Password();
            var shown = Field("", 72);
            hidden.Padding = new Thickness(10, 6, 44, 6);
            shown.Padding = new Thickness(10, 6, 44, 6);
            shown.Visibility = Visibility.Collapsed;

            var eye = new Button
            {
                Style = St("CaptionBtn"), Width = 34, Height = 26, FontSize = 14, Focusable = false,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0), Content = "\uE890", Cursor = Cursors.Hand
            };
            eye.SetResourceReference(Control.ForegroundProperty, "SubText");
            eye.Click += (s, e) =>
            {
                bool show = shown.Visibility != Visibility.Visible;
                if (show) shown.Text = hidden.Password; else hidden.Password = shown.Text;
                shown.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                hidden.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
                eye.Content = show ? "\uED1A" : "\uE890";   // «перечёркнутый глаз» / «глаз»
                if (show) { shown.Focus(); shown.CaretIndex = shown.Text.Length; } else hidden.Focus();
            };

            grid.Children.Add(hidden);
            grid.Children.Add(shown);
            grid.Children.Add(eye);
            return (grid, () => shown.Visibility == Visibility.Visible ? shown.Text : hidden.Password);
        }

        public static FrameworkElement Labeled(string key, UIElement field)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            var l = L(key, 12, sub: true);
            l.Margin = new Thickness(2, 0, 0, 6);
            sp.Children.Add(l);
            sp.Children.Add(field);
            return sp;
        }

        public static RadioButton Tab(string key, string group, bool isChecked, RoutedEventHandler click)
        {
            var r = new RadioButton { Style = St("Pill"), GroupName = group, IsChecked = isChecked };
            r.SetResourceReference(ContentControl.ContentProperty, key);
            if (click != null) r.Click += click;
            return r;
        }

        public static StackPanel Row(params UIElement[] items)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var i in items) sp.Children.Add(i);
            return sp;
        }

        public static Border Divider() => new Border { Height = 1, Margin = new Thickness(0, 14, 0, 14), Background = (Brush)Owner.FindResource("Divider") };

        // ---------- аватарка и галочка ----------

        /// <summary>
        /// Старый способ: WPF сам качает картинку. Оставлен для совместимости; новое — WebBrush/ImageCache
        /// (свой кэш на диске, повторные попытки, правильная обрезка).
        /// </summary>
        public static ImageSource Web(string url, int decode)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var ready = ImageCache.Peek(url, decode, false);
            if (ready != null) return ready;
            try
            {
                var b = new BitmapImage();
                b.BeginInit();
                b.UriSource = new Uri(url);
                b.DecodePixelWidth = decode;
                b.CreateOptions = BitmapCreateOptions.IgnoreImageCache;   // общий кэш WPF отдавал картинку то одного, то другого размера
                b.EndInit();   // картинка скачивается в фоне и появится сама
                return b;
            }
            catch { return null; }
        }

        /// <summary>
        /// Кисть с картинкой из интернета (или с диска): сразу пустая, сама заполняется, когда картинка готова.
        /// Картинка берётся из ImageCache: кэш на диске, 3 попытки, поворот по EXIF. square — квадрат из середины.
        /// </summary>
        public static ImageBrush WebBrush(string url, int px, Stretch stretch = Stretch.UniformToFill, bool square = false, Action ready = null)
        {
            var brush = new ImageBrush { Stretch = stretch };
            if (string.IsNullOrEmpty(url)) return brush;
            ImageCache.Load(url, px, square, bmp =>
            {
                brush.ImageSource = bmp;
                ready?.Invoke();
            });
            return brush;
        }

        /// <summary>Квадрат из середины картинки (по настоящим пикселям, без учёта DPI и прочих данных файла).</summary>
        public static BitmapSource Square(BitmapSource b)
        {
            if (b == null) return null;
            int w = b.PixelWidth, h = b.PixelHeight;
            if (w <= 1 || h <= 1 || w == h) return b;
            int s = Math.Min(w, h);
            try
            {
                var c = new CroppedBitmap(b, new Int32Rect((w - s) / 2, (h - s) / 2, s, s));
                if (b.IsFrozen) c.Freeze();
                return c;
            }
            catch { return b; }
        }

        /// <summary>
        /// Картинка аватарки поверх буквы. ImageCache сам качает (3 попытки, кэш на диске) и вырезает квадрат ровно
        /// из середины по настоящим пикселям — поэтому аватарка больше не «съезжает» и не пропадает.
        /// Если картинка уже в памяти — рисуется сразу, без мигания буквой.
        /// </summary>
        public static void AvatarFace(Panel g, string url, double size)
        {
            if (string.IsNullOrEmpty(url)) return;
            int px = (int)Math.Ceiling(size * 2.5);   // с запасом на масштаб экрана 150–250%
            var face = new Ellipse { Opacity = 0, IsHitTestVisible = false };
            g.Children.Add(face);
            var ready = ImageCache.Peek(url, px, true);
            if (ready != null)
            {
                face.Fill = new ImageBrush(ready) { Stretch = Stretch.Fill };
                face.Opacity = 1;
                return;
            }
            ImageCache.Load(url, px, true, bmp =>
            {
                face.Fill = new ImageBrush(bmp) { Stretch = Stretch.Fill };
                Anim.To(face, UIElement.OpacityProperty, 1, 220, from: 0);
            });
        }

        /// <summary>Круглая аватарка: первая буква ника на градиенте, а поверх — картинка, когда скачается.</summary>
        public static FrameworkElement Avatar(Profile p, double size)
        {
            var g = new Grid { Width = size, Height = size };
            var e = new Ellipse();
            e.Fill = (Brush)Owner.FindResource("LogoBrush");
            g.Children.Add(e);
            g.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(p?.Nickname) ? "?" : p.Nickname.Substring(0, 1).ToUpperInvariant(),
                FontSize = size * 0.42, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            });

            AvatarFace(g, p?.AvatarUrl, size);
            g.Children.Add(new Ellipse { Stroke = new SolidColorBrush(Color.FromArgb(0x50, 255, 255, 255)), StrokeThickness = 1 });
            return g;
        }

        // ---------- галочки трёх цветов ----------

        /// <summary>Цвета галочек: чёрная — владелец, синяя — официальный аккаунт, серая — 300+ часов в MizuEngine.</summary>
        static (Color a, Color b, Color glow) BadgeColors(string kind) => kind switch
        {
            "black" => (Color.FromRgb(0x4A, 0x4A, 0x52), Color.FromRgb(0x05, 0x05, 0x08), Color.FromRgb(0x9A, 0x9A, 0xB0)),
            "gray" => (Color.FromRgb(0xD3, 0xD9, 0xE2), Color.FromRgb(0x7C, 0x86, 0x96), Color.FromRgb(0xB4, 0xBC, 0xC8)),
            _ => (Color.FromRgb(0x6F, 0xC3, 0xFF), Color.FromRgb(0x1D, 0x5B, 0xF0), Color.FromRgb(0x3B, 0x8C, 0xFF))
        };

        public static string BadgeTip(string kind) => Loc.T(kind switch
        {
            "black" => "badge.black.tip",
            "gray" => "badge.gray.tip",
            _ => "badge.blue.tip"
        });

        /// <summary>
        /// Галочка: волнистая печать с буквой N. Все галочки живые — печать медленно вращается (30 кадров в секунду, почти без нагрузки).
        /// Крупные (big) ещё мягко «дышат» и светятся. Буква N всегда стоит ровно.
        /// </summary>
        public static FrameworkElement Badge(double size, string kind = "blue", bool big = false)
        {
            kind ??= "blue";
            var (ca, cb, cg) = BadgeColors(kind);
            var g = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
            var seal = new Path
            {
                Data = Seal(), Stretch = Stretch.Uniform, RenderTransformOrigin = new Point(0.5, 0.5),
                Fill = new LinearGradientBrush(ca, cb, 45)
            };
            if (kind == "black") { seal.Stroke = new SolidColorBrush(Color.FromArgb(0x70, 255, 255, 255)); seal.StrokeThickness = Math.Max(0.6, size / 26); }
            g.Children.Add(seal);
            g.Children.Add(new Path
            {
                Data = (Geometry)Owner.FindResource("LogoM"), Fill = kind == "gray" ? new SolidColorBrush(Color.FromRgb(0x2E, 0x34, 0x40)) : Brushes.White,
                Stretch = Stretch.Uniform, Width = size * 0.5, Height = size * 0.42
            });

            var spin = new RotateTransform();
            seal.RenderTransform = spin;
            var turn = new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(kind == "black" ? 12 : 9)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
            System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(turn, 30);
            spin.BeginAnimation(RotateTransform.AngleProperty, turn);

            if (big)
            {
                var pulse = new ScaleTransform(1, 1);
                g.RenderTransformOrigin = new Point(0.5, 0.5);
                g.RenderTransform = pulse;
                var breathe = new System.Windows.Media.Animation.DoubleAnimation(1, 1.1, TimeSpan.FromSeconds(1.4))
                {
                    AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                    EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
                };
                pulse.BeginAnimation(ScaleTransform.ScaleXProperty, breathe);
                pulse.BeginAnimation(ScaleTransform.ScaleYProperty, breathe);

                var glow = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = 14, Color = cg, Opacity = 0.9 };
                glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0.45, 1, TimeSpan.FromSeconds(1.4)) { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
                g.Effect = glow;
            }
            g.ToolTip = BadgeTip(kind);
            return g;
        }

        /// <summary>Крупная живая галочка (в шапке профиля, сообщества, группы).</summary>
        public static FrameworkElement AnimatedBadge(double size, string kind = "blue") => Badge(size, kind, true);

        // ---------- значок Mizu Plus ----------

        /// <summary>Открыть вкладку Mizu Plus (ставит главное окно). Нажатие на значок Plus ведёт туда.</summary>
        public static Action OpenPlus;
        /// <summary>Открыть раздел «Живые фоны MizuEngine» (ставит главное окно).</summary>
        public static Action OpenLiveBg;

        public static readonly Color PlusLight = Color.FromRgb(0xC4, 0xB5, 0xFD), PlusMid = Color.FromRgb(0x8B, 0x5C, 0xF6), PlusDeep = Color.FromRgb(0x6D, 0x28, 0xD9);

        static Geometry _hex, _plusSign, _hexShine;

        /// <summary>Фиолетовая кисть Plus (светлый → фиолетовый → глубокий).</summary>
        public static Brush PlusBrush()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            b.GradientStops.Add(new GradientStop(PlusLight, 0));
            b.GradientStops.Add(new GradientStop(PlusMid, 0.55));
            b.GradientStops.Add(new GradientStop(PlusDeep, 1));
            b.Freeze();
            return b;
        }

        /// <summary>
        /// Значок Mizu Plus: фиолетовый «кристалл» (шестигранник со скруглёнными углами) с белым плюсом.
        /// Его видят все — рядом с ником подписчика. Крупный (big) мягко светится и переливается бликом.
        /// </summary>
        public static FrameworkElement PlusBadge(double size, bool big = false, bool clickable = true)
        {
            if (_hex == null)
            {
                _hex = Geometry.Parse("M 12,1.8 L 20.8,6.9 L 20.8,17.1 L 12,22.2 L 3.2,17.1 L 3.2,6.9 Z"); _hex.Freeze();
                _plusSign = Geometry.Parse("M 12,7.6 L 12,16.4 M 7.6,12 L 16.4,12"); _plusSign.Freeze();
                _hexShine = Geometry.Parse("M 6.2,8.1 L 12,4.8 L 17.8,8.1"); _hexShine.Freeze();
            }
            var fill = PlusBrush();
            var canvas = new Canvas { Width = 24, Height = 24 };
            canvas.Children.Add(new Path { Data = _hex, Fill = fill, Stroke = fill, StrokeThickness = 2.4, StrokeLineJoin = PenLineJoin.Round });
            canvas.Children.Add(new Path { Data = _plusSign, Stroke = Brushes.White, StrokeThickness = size < 18 ? 3 : 2.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            if (size >= 20)
                canvas.Children.Add(new Path { Data = _hexShine, Stroke = new SolidColorBrush(Color.FromArgb(0x80, 255, 255, 255)), StrokeThickness = 0.9, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round });
            var box = new Viewbox { Width = size, Height = size, Child = canvas, VerticalAlignment = VerticalAlignment.Center };
            var g = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent };
            g.Children.Add(box);

            if (big)
            {
                var glow = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = 16, Color = PlusMid, Opacity = 0.85 };
                var pulse = new System.Windows.Media.Animation.DoubleAnimation(0.4, 0.95, TimeSpan.FromSeconds(1.6))
                {
                    AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                    EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
                };
                System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(pulse, 30);
                glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, pulse);
                g.Effect = glow;
            }
            g.ToolTip = Loc.T("plus.badge_tip");
            if (clickable && OpenPlus != null)
            {
                g.Cursor = Cursors.Hand;
                g.MouseLeftButtonUp += (s, e) => { e.Handled = true; OpenPlus?.Invoke(); };
            }
            return g;
        }

        /// <summary>Значок Plus с отступом слева — чтобы дописать его после ника и галочки.</summary>
        public static FrameworkElement PlusAfter(double size, double left = 6, bool big = false)
        {
            var b = PlusBadge(size, big);
            b.Margin = new Thickness(left, 0, 0, 0);
            return b;
        }

        // ---------- плашка подписи («CEO of MizuEngine») ----------

        /// <summary>Плашка подписи: фон любого цвета (null — фирменный синий градиент), текст белый или чёрный.</summary>
        public static FrameworkElement TitlePill(string title, string bg, string fg, double size = 12.5)
        {
            var pill = new Border { CornerRadius = new CornerRadius(11), Padding = new Thickness(12, 4, 12, 5), HorizontalAlignment = HorizontalAlignment.Center };
            var c = ParseColor(bg);
            pill.Background = c.HasValue
                ? new LinearGradientBrush(Mix(c.Value, Colors.White, 0.18), Mix(c.Value, Colors.Black, 0.12), 0)
                : new LinearGradientBrush(Color.FromRgb(0x5A, 0xB0, 0xFF), Color.FromRgb(0x1D, 0x4F, 0xE0), 0);
            pill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x38, 255, 255, 255));
            pill.BorderThickness = new Thickness(0.8);
            pill.Child = new TextBlock { Text = title, Foreground = fg == "black" ? new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x14)) : Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = size };
            return pill;
        }

        // ---------- цвета ----------

        public static Color? ParseColor(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return null;
            hex = hex.Trim().TrimStart('#');
            if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int v)) return null;
            return Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        }

        public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        public static Color Mix(Color a, Color b, double t) => Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        /// <summary>Светлый ли цвет (тогда текст на нём чёрный).</summary>
        public static bool IsLight(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255 > 0.66;

        public static Color FromHsl(double h, double s, double l)
        {
            h = ((h % 360) + 360) % 360 / 360;
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
            double F(double t)
            {
                if (t < 0) t += 1; if (t > 1) t -= 1;
                if (t < 1 / 6.0) return p + (q - p) * 6 * t;
                if (t < 0.5) return q;
                if (t < 2 / 3.0) return p + (q - p) * (2 / 3.0 - t) * 6;
                return p;
            }
            return Color.FromRgb((byte)Math.Round(F(h + 1 / 3.0) * 255), (byte)Math.Round(F(h) * 255), (byte)Math.Round(F(h - 1 / 3.0) * 255));
        }

        public static (double h, double s, double l) ToHsl(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2, h = 0, s = 0;
            if (max != min)
            {
                double d = max - min;
                s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
                if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
                else if (max == g) h = (b - r) / d + 2;
                else h = (r - g) / d + 4;
                h *= 60;
            }
            return (h, s, l);
        }

        static readonly string[] Palette =
        {
            "#0A84FF", "#1D4FE0", "#5E5CE6", "#BF5AF2", "#FF375F", "#FF453A", "#FF9F0A", "#FFD60A",
            "#30D158", "#00C7BE", "#64D2FF", "#A2845E", "#F2F2F7", "#8E8E93", "#3A3A3C", "#000000"
        };

        /// <summary>
        /// Выбор почти любого цвета: готовые цвета, ползунки «оттенок» и «яркость» и поле HEX.
        /// changed вызывается при каждом изменении. Возвращает сам выбор и функцию «поставить цвет».
        /// </summary>
        public static (FrameworkElement ui, Action<Color> set) ColorPicker(Color start, Action<Color> changed)
        {
            var sp = new StackPanel();
            bool busy = false;
            double H = 0, S = 0.8, L = 0.5;

            var swatches = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
            sp.Children.Add(swatches);
            var marks = new List<(Border ring, Color c)>();

            var hue = Bar(true);
            var light = Bar(false);
            sp.Children.Add(Labeled("color.hue", hue.ui));
            sp.Children.Add(Labeled("color.light", light.ui));

            var hexRow = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            hexRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hexRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            var preview = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 10, 0), BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 255, 255, 255)), BorderThickness = new Thickness(1) };
            var hexBox = Field("", 7);
            Grid.SetColumn(hexBox, 1);
            hexRow.Children.Add(preview);
            hexRow.Children.Add(hexBox);
            sp.Children.Add(hexRow);
            hexBox.TextChanged += (s, e) =>
            {
                if (busy) return;
                var c = ParseColor(hexBox.Text);
                if (c.HasValue) Set(c.Value, fromHex: true);
            };

            foreach (var h in Palette)
            {
                var c = ParseColor(h).Value;
                var dot = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(c), BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)), BorderThickness = new Thickness(1) };
                var ring = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(2), Child = dot, Margin = new Thickness(0, 0, 4, 4), Cursor = Cursors.Hand, Background = Brushes.Transparent };
                ring.MouseLeftButtonUp += (s, e) => { Set(c); Motion.Pop(ring, 0.85); };
                marks.Add((ring, c));
                swatches.Children.Add(ring);
            }

            hue.changed = v => { H = v * 360; if (S < 0.15) S = 0.8; Set(FromHsl(H, S, L), fromBars: true); };
            light.changed = v => { L = 0.06 + v * 0.88; Set(FromHsl(H, S, L), fromBars: true); };

            void Set(Color c, bool fromHex = false, bool fromBars = false)
            {
                busy = true;
                if (!fromBars) { var hsl = ToHsl(c); H = hsl.h; S = hsl.s; L = hsl.l; }
                hue.set(H / 360);
                light.set((L - 0.06) / 0.88);
                light.paint(FromHsl(H, Math.Max(S, 0.15), 0.5));
                preview.Background = new SolidColorBrush(c);
                if (!fromHex) hexBox.Text = Hex(c);
                foreach (var (ring, mc) in marks)
                    if (mc == c) ring.SetResourceReference(Border.BorderBrushProperty, "Text"); else ring.BorderBrush = Brushes.Transparent;
                busy = false;
                changed?.Invoke(c);
            }
            Set(start);
            return (sp, c => Set(c));
        }

        /// <summary>Ползунок-полоса для выбора цвета: радуга (оттенок) или тёмный → цвет → светлый (яркость).</summary>
        sealed class ColorBar { public FrameworkElement ui; public Action<double> set; public Action<Color> paint; public Action<double> changed; }

        static ColorBar Bar(bool rainbow)
        {
            var bar = new ColorBar();
            var grid = new Grid { Height = 26, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var track = new Border { Height = 12, CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Center, BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)), BorderThickness = new Thickness(1) };
            if (rainbow)
            {
                var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                for (int i = 0; i <= 6; i++) b.GradientStops.Add(new GradientStop(FromHsl(i * 60, 1, 0.5), i / 6.0));
                track.Background = b;
            }
            var canvas = new Canvas { IsHitTestVisible = false };
            var knob = new Ellipse { Width = 20, Height = 20, Fill = Brushes.White, Stroke = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)), StrokeThickness = 1,
                                     Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 1, BlurRadius = 6, Opacity = 0.35 } };
            Canvas.SetTop(knob, 3);
            canvas.Children.Add(knob);
            grid.Children.Add(track);
            grid.Children.Add(canvas);
            double value = 0;
            void Place() { double w = Math.Max(0, grid.ActualWidth - 20); Canvas.SetLeft(knob, value * w); }
            grid.SizeChanged += (s, e) => Place();
            void FromMouse(MouseEventArgs e)
            {
                double w = Math.Max(1, grid.ActualWidth - 20);
                value = Math.Max(0, Math.Min(1, (e.GetPosition(grid).X - 10) / w));
                Place();
                bar.changed?.Invoke(value);
            }
            grid.MouseLeftButtonDown += (s, e) => { grid.CaptureMouse(); FromMouse(e); };
            grid.MouseMove += (s, e) => { if (grid.IsMouseCaptured) FromMouse(e); };
            grid.MouseLeftButtonUp += (s, e) => grid.ReleaseMouseCapture();
            bar.ui = grid;
            bar.set = v => { value = Math.Max(0, Math.Min(1, v)); Place(); };
            bar.paint = c =>
            {
                if (rainbow) return;
                var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                b.GradientStops.Add(new GradientStop(Color.FromRgb(0x10, 0x10, 0x12), 0));
                b.GradientStops.Add(new GradientStop(c, 0.5));
                b.GradientStops.Add(new GradientStop(Color.FromRgb(0xF4, 0xF4, 0xF6), 1));
                track.Background = b;
            };
            return bar;
        }

        // ---------- фоны тем чатов ----------

        /// <summary>Готовые фоны для чатов: ключ → цвета градиента (сверху слева → снизу справа).</summary>
        public static readonly (string key, string[] colors)[] ThemeBackgrounds =
        {
            ("aurora",   new[] { "#0B3D2E", "#1E6F5C", "#2E6FD8" }),
            ("ocean",    new[] { "#1A2980", "#1C6FB8", "#26D0CE" }),
            ("night",    new[] { "#0F0C29", "#302B63", "#24243E" }),
            ("sunset",   new[] { "#5B2A86", "#C2416B", "#F7934C" }),
            ("sakura",   new[] { "#6C5B7B", "#C06C84", "#F6A5C0" }),
            ("forest",   new[] { "#0F3443", "#134E5E", "#71B280" }),
            ("lavender", new[] { "#3E2C7A", "#654EA3", "#C88FD6" }),
            ("ember",    new[] { "#1A0306", "#5C0A13", "#B3311F" }),
            ("graphite", new[] { "#141518", "#2A2D34", "#4A4F59" })
        };

        /// <summary>Кисть фона темы чата. null — без фона (обычная карточка).</summary>
        public static Brush ThemeBrush(ChatTheme t, int decode = 1400)
        {
            if (t == null || t.Bg == null) return null;
            if (t.Bg == "custom")
            {
                return string.IsNullOrEmpty(t.Img) ? null : WebBrush(t.Img, decode);
            }
            foreach (var (key, colors) in ThemeBackgrounds)
            {
                if (key != t.Bg) continue;
                var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                for (int i = 0; i < colors.Length; i++) b.GradientStops.Add(new GradientStop(ParseColor(colors[i]).Value, i / (double)(colors.Length - 1)));
                b.Freeze();
                return b;
            }
            return null;
        }

        /// <summary>Цвета своих сообщений.</summary>
        public static readonly string[] BubbleColors =
            { "#0A84FF", "#5E5CE6", "#BF5AF2", "#FF375F", "#FF9F0A", "#FFD60A", "#30D158", "#00C7BE", "#8E8E93", "#1C1C1E" };

        /// <summary>Кнопка опасного действия (бан, сброс): красный текст на стекле.</summary>
        public static Button Danger(string glyph, string key, RoutedEventHandler click)
        {
            var b = Glass(glyph, key, click, 9);
            var red = new SolidColorBrush(Color.FromRgb(0xFF, 0x5F, 0x57));
            foreach (var t in ((StackPanel)b.Content).Children.OfType<TextBlock>()) t.Foreground = red;
            return b;
        }

        static Geometry _seal;
        static Geometry Seal()
        {
            if (_seal != null) return _seal;
            const int lobes = 12;
            double inner = 0.86;
            var fig = new PathFigure { IsClosed = true };
            for (int i = 0; i <= lobes; i++)
            {
                double a = i * Math.PI * 2 / lobes;
                var pt = new Point(1 + inner * Math.Cos(a), 1 + inner * Math.Sin(a));
                if (i == 0) { fig.StartPoint = pt; continue; }
                fig.Segments.Add(new ArcSegment(pt, new Size(0.24, 0.24), 0, false, SweepDirection.Clockwise, true));
            }
            _seal = new PathGeometry(new[] { fig });
            _seal.Freeze();
            return _seal;
        }

        /// <summary>Строка пользователя в списках: аватарка, ник, галочка, @юз и кнопки справа.</summary>
        public static Button UserRow(Profile p, Action open, params UIElement[] trailing) =>
            UserRow(p, open, string.IsNullOrEmpty(p.Handle) ? "" : "@" + p.Handle, trailing);

        /// <summary>То же, но вместо @юза — своя подпись (например, «Группа»).</summary>
        public static Button UserRow(Profile p, Action open, string subtitle, params UIElement[] trailing)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Кружок «в сети» — только у людей (у групп и ботов его нет)
            bool person = p.Id != null && !p.Id.StartsWith("group:") && !p.Bot;
            grid.Children.Add(person ? Online.WithDot(Avatar(p, 44), p.Id, 44) : Avatar(p, 44));
            var name = Row(T(p.Nickname, 15, true));
            if (p.Verified) { var b = Badge(16, p.Badge); b.Margin = new Thickness(6, 0, 0, 0); name.Children.Add(b); }
            if (p.IsPlus) name.Children.Add(PlusAfter(16));
            var info = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(name);
            if (!string.IsNullOrEmpty(subtitle)) info.Children.Add(T(subtitle, 12, sub: true));
            Grid.SetColumn(info, 1);
            grid.Children.Add(info);

            var right = Row(trailing);
            right.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);

            var btn = new Button { Style = St("GlassButton"), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8), Content = grid, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            btn.Click += (s, e) => open?.Invoke();
            return btn;
        }

        public static ScrollViewer Scroll(UIElement content)
        {
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = content };
            SmoothScroll.Attach(sv);
            return sv;
        }

        public static FrameworkElement Loading()
        {
            var t = L("social.loading", 14, sub: true);
            t.Margin = new Thickness(0, 20, 0, 0);
            return t;
        }

        public static string Ago(DateTime t)
        {
            var d = DateTime.Now - t;
            if (d.TotalDays >= 1) return t.ToString("d MMM HH:mm");
            return t.ToString("HH:mm");
        }

        public static void Hand(FrameworkElement e) => e.Cursor = Cursors.Hand;

        // ---------- стиль macOS / iOS ----------

        /// <summary>Цвета значков как в настройках iOS.</summary>
        public static readonly (Color a, Color b) Blue = (Color.FromRgb(0x4D, 0xA8, 0xFF), Color.FromRgb(0x0A, 0x6C, 0xF0)),
            Orange = (Color.FromRgb(0xFF, 0xB3, 0x40), Color.FromRgb(0xFF, 0x86, 0x0A)),
            Green = (Color.FromRgb(0x5B, 0xE0, 0x7E), Color.FromRgb(0x24, 0xB0, 0x4C)),
            Purple = (Color.FromRgb(0xC9, 0x7B, 0xFF), Color.FromRgb(0x8E, 0x3C, 0xE8)),
            Teal = (Color.FromRgb(0x6F, 0xDB, 0xFF), Color.FromRgb(0x1F, 0x9E, 0xD8)),
            Red = (Color.FromRgb(0xFF, 0x75, 0x6B), Color.FromRgb(0xE8, 0x34, 0x2A)),
            Pink = (Color.FromRgb(0xFF, 0x8F, 0xB8), Color.FromRgb(0xF0, 0x4A, 0x86)),
            Gray = (Color.FromRgb(0xA4, 0xAE, 0xBE), Color.FromRgb(0x6C, 0x77, 0x8A));

        /// <summary>Цветной «скруглённый квадрат» с белым значком — как в настройках iPhone и Mac.</summary>
        public static FrameworkElement Squircle(string glyph, (Color a, Color b) c, double size = 28)
        {
            var b = new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.28), VerticalAlignment = VerticalAlignment.Center,
                Background = new LinearGradientBrush(c.a, c.b, 90),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)), BorderThickness = new Thickness(0.8)
            };
            b.Child = new TextBlock
            {
                Text = glyph, FontFamily = (FontFamily)Owner.FindResource("Icons"), FontSize = size * 0.5, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            return b;
        }

        /// <summary>Маленький серый заголовок над группой, заглавными буквами — как в iOS.</summary>
        public static TextBlock Section(string key, double top = 18)
        {
            var t = L(key, 11.5, true, sub: true);
            t.Typography.Capitals = FontCapitals.AllSmallCaps;
            t.Margin = new Thickness(16, top, 0, 7);
            t.Opacity = 0.9;
            return t;
        }

        /// <summary>Строка в группе iOS: значок, текст и что-то справа. click — строка нажимается.</summary>
        public static FrameworkElement IosRow(FrameworkElement icon, UIElement text, UIElement trailing = null, Action click = null)
        {
            var g = new Grid { Background = Brushes.Transparent, MinHeight = 44 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (icon != null) { icon.Margin = new Thickness(0, 0, 12, 0); g.Children.Add(icon); }
            if (text is FrameworkElement tf) tf.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(text, 1);
            g.Children.Add(text);
            if (trailing != null)
            {
                if (trailing is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(trailing, 2);
                g.Children.Add(trailing);
            }
            if (click == null) return g;

            var wrap = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var bg = new Border { CornerRadius = new CornerRadius(12), Margin = new Thickness(-8, 0, -8, 0), Opacity = 0 };
            bg.SetResourceReference(Border.BackgroundProperty, "HoverBg");
            wrap.Children.Add(bg);
            wrap.Children.Add(g);
            wrap.MouseEnter += (s, e) => Anim.To(bg, UIElement.OpacityProperty, 1, 160);
            wrap.MouseLeave += (s, e) => Anim.To(bg, UIElement.OpacityProperty, 0, 300);
            wrap.MouseLeftButtonUp += (s, e) => click();
            return wrap;
        }

        /// <summary>Группа строк iOS: одна карточка, строки разделены тонкими линиями с отступом слева.</summary>
        public static Border IosGroup(params UIElement[] rows)
        {
            var sp = new StackPanel();
            for (int i = 0; i < rows.Length; i++)
            {
                if (i > 0)
                {
                    var line = new Border { Height = 1, Margin = new Thickness(40, 2, 0, 2) };
                    line.SetResourceReference(Border.BackgroundProperty, "Divider");
                    sp.Children.Add(line);
                }
                sp.Children.Add(rows[i]);
            }
            var card = new Border { CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1), Padding = new Thickness(16, 6, 16, 6), Child = sp };
            card.SetResourceReference(Border.BackgroundProperty, "CardBg");
            card.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            return card;
        }

        /// <summary>Аватарка внутри строки UserRow — её снимок «перелетает» в профиль.</summary>
        public static FrameworkElement RowAvatar(Button row) =>
            row.Content is Grid g && g.Children.Count > 0 ? g.Children[0] as FrameworkElement : null;

        /// <summary>Шеврон «›» справа в строках, как в iOS.</summary>
        public static TextBlock Chevron()
        {
            var t = Icon("", 11);
            t.Opacity = 0.5;
            return t;
        }
    }
}
