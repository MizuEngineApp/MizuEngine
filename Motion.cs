// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace MizuEngine
{
    /// <summary>
    /// Анимации интерфейса: смена страниц, «перелёт» аватарки из списка в профиль, появление списков.
    /// Стиль и скорость выбираются на странице «Анимации».
    /// </summary>
    internal static class Motion
    {
        public static readonly string[] Styles = { "flight", "fade", "slide", "zoom", "none" };

        public static string Style = "flight";
        public static double Speed = 1.0;   // 0.7 — быстро, 1 — обычно, 1.35 — плавно
        public static Canvas Layer;         // слой поверх окна для летящих картинок (задаёт главное окно)

        public static bool Off => Style == "none";
        public static double Ms(double ms) => Off ? 1 : ms * Speed;

        // ================== страницы ==================

        /// <summary>Страница появляется. dir: +1 — пришли «вниз» по меню, -1 — «вверх», 0 — без направления.</summary>
        public static void Enter(FrameworkElement page, int dir = 0, double delay = 0)
        {
            var (sc, tr) = Tx(page);
            page.Visibility = Visibility.Visible;
            if (Off)
            {
                Anim.Set(page, UIElement.OpacityProperty, 1);
                Anim.Set(sc, ScaleTransform.ScaleXProperty, 1); Anim.Set(sc, ScaleTransform.ScaleYProperty, 1);
                Anim.Set(tr, TranslateTransform.XProperty, 0); Anim.Set(tr, TranslateTransform.YProperty, 0);
                return;
            }
            double scale = 1, x = 0, y = 0, ms = 360;
            switch (Style)
            {
                case "flight": scale = 0.965; y = dir == 0 ? 14 : 22 * dir; ms = 420; break;
                case "slide": x = dir < 0 ? -46 : 46; ms = 380; break;
                case "zoom": scale = 0.9; ms = 400; break;
                default: ms = 300; break;   // fade
            }
            page.RenderTransformOrigin = new Point(0.5, 0.35);
            Anim.To(page, UIElement.OpacityProperty, 1, Ms(ms * 0.8), from: 0, delayMs: delay);
            Anim.To(sc, ScaleTransform.ScaleXProperty, 1, Ms(ms), from: scale, delayMs: delay);
            Anim.To(sc, ScaleTransform.ScaleYProperty, 1, Ms(ms), from: scale, delayMs: delay);
            Anim.To(tr, TranslateTransform.XProperty, 0, Ms(ms), from: x, delayMs: delay);
            Anim.To(tr, TranslateTransform.YProperty, 0, Ms(ms), from: y, delayMs: delay);
        }

        /// <summary>Страница уходит; done вызывается, когда её уже не видно.</summary>
        public static void Exit(FrameworkElement page, int dir, Action done)
        {
            if (Off) { Anim.Set(page, UIElement.OpacityProperty, 0); done(); return; }
            var (sc, tr) = Tx(page);
            double ms = Ms(Style == "fade" ? 140 : 170);
            Anim.To(page, UIElement.OpacityProperty, 0, ms, done: done);
            switch (Style)
            {
                case "flight":
                    Anim.To(sc, ScaleTransform.ScaleXProperty, 0.985, ms); Anim.To(sc, ScaleTransform.ScaleYProperty, 0.985, ms);
                    Anim.To(tr, TranslateTransform.YProperty, -10 * (dir == 0 ? 1 : dir), ms);
                    break;
                case "slide": Anim.To(tr, TranslateTransform.XProperty, dir < 0 ? 40 : -40, ms); break;
                case "zoom":
                    Anim.To(sc, ScaleTransform.ScaleXProperty, 1.04, ms); Anim.To(sc, ScaleTransform.ScaleYProperty, 1.04, ms);
                    break;
            }
        }

        /// <summary>Меняет содержимое контейнера: старое плавно уходит, новое появляется на его месте.</summary>
        public static void Replace(Panel host, UIElement next, int dir = 0)
        {
            var old = new List<UIElement>();
            foreach (UIElement c in host.Children) old.Add(c);
            foreach (var o in old)
            {
                o.IsHitTestVisible = false;
                if (o is FrameworkElement fe) Exit(fe, dir, () => host.Children.Remove(o));
                else host.Children.Remove(o);
            }
            host.Children.Add(next);
            if (next is FrameworkElement nf) Enter(nf, dir, old.Count > 0 ? Ms(60) : 0);
        }

        /// <summary>Пока грузятся новые данные — старое содержимое слегка тускнеет, а не пропадает.</summary>
        public static void Dim(Panel host)
        {
            foreach (UIElement c in host.Children) Anim.To(c, UIElement.OpacityProperty, 0.55, Ms(200));
        }

        /// <summary>Элементы списка появляются волной сверху вниз.</summary>
        public static void Stagger(Panel panel, int from = 0)
        {
            if (Off) return;
            int i = 0;
            for (int k = from; k < panel.Children.Count; k++)
            {
                if (panel.Children[k] is not FrameworkElement fe) continue;
                var tr = new TranslateTransform();
                fe.RenderTransform = tr;
                double delay = Ms(Math.Min(i++, 14) * 32);
                Anim.To(fe, UIElement.OpacityProperty, 1, Ms(320), from: 0, delayMs: delay);
                Anim.To(tr, TranslateTransform.YProperty, 0, Ms(420), from: 12, delayMs: delay);
            }
        }

        /// <summary>«Пружинка» — для лайка и маленьких кнопок.</summary>
        public static void Pop(FrameworkElement e, double amount = 1.25)
        {
            if (Off) return;
            var sc = new ScaleTransform(1, 1);
            e.RenderTransformOrigin = new Point(0.5, 0.5);
            e.RenderTransform = sc;
            var a = new DoubleAnimation(amount, 1, TimeSpan.FromMilliseconds(Ms(520))) { EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 4, EasingMode = EasingMode.EaseOut } };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        static (ScaleTransform, TranslateTransform) Tx(FrameworkElement e)
        {
            if (e.RenderTransform is TransformGroup g && g.Children.Count == 2 && g.Children[0] is ScaleTransform s0 && g.Children[1] is TranslateTransform t0 && !g.IsFrozen)
                return (s0, t0);
            var s = new ScaleTransform(1, 1);
            var t = new TranslateTransform();
            e.RenderTransform = new TransformGroup { Children = { s, t } };
            return (s, t);
        }

        // ================== перелёт ==================

        sealed class Shot
        {
            public ImageSource Image;
            public Rect From;
            public DateTime At;
        }

        static readonly Dictionary<string, Shot> Pending = new Dictionary<string, Shot>();

        /// <summary>
        /// Запоминает, где сейчас элемент и как он выглядит. Когда на новой странице появится элемент с тем же ключом (Hero),
        /// снимок «перелетит» на его место.
        /// </summary>
        public static void Capture(FrameworkElement from, string key)
        {
            if (Style != "flight" || Layer == null || from == null || !from.IsVisible || from.ActualWidth < 1) return;
            try
            {
                var r = from.TransformToVisual(Layer).TransformBounds(new Rect(0, 0, from.ActualWidth, from.ActualHeight));
                var dpi = VisualTreeHelper.GetDpi(from);
                var bmp = new RenderTargetBitmap((int)Math.Ceiling(from.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(from.ActualHeight * dpi.DpiScaleY),
                                                 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen()) dc.DrawRectangle(new VisualBrush(from), null, new Rect(0, 0, from.ActualWidth, from.ActualHeight));
                bmp.Render(dv);
                bmp.Freeze();
                Pending[key] = new Shot { Image = bmp, From = r, At = DateTime.Now };
            }
            catch (Exception e) { Settings.Log(e); }
        }

        /// <summary>Место приземления перелёта. Пока снимок летит, сам элемент спрятан, а в конце плавно проявляется.</summary>
        public static void Hero(FrameworkElement target, string key)
        {
            if (!Pending.TryGetValue(key, out var shot)) return;
            if ((DateTime.Now - shot.At).TotalSeconds > 4 || Style != "flight" || Layer == null) { Pending.Remove(key); return; }
            target.Opacity = 0;
            void Start(object s, RoutedEventArgs e)
            {
                target.Loaded -= Start;
                Pending.Remove(key);
                Fly(shot, target);
            }
            if (target.IsLoaded) Start(null, null); else target.Loaded += Start;
        }

        static void Fly(Shot shot, FrameworkElement target)
        {
            var img = new Image { Source = shot.Image, Stretch = Stretch.Fill, IsHitTestVisible = false };
            Layer.Children.Add(img);
            double total = Ms(460) / 1000.0;
            var start = DateTime.Now;
            var ease = Anim.Out;

            void Frame(object s, EventArgs e)
            {
                double t = Math.Min(1, (DateTime.Now - start).TotalSeconds / total);
                double k = ease.Ease(t);
                Rect to;
                try { to = target.TransformToVisual(Layer).TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight)); }
                catch { to = shot.From; t = 1; }   // цель уже убрали со страницы
                double x = shot.From.X + (to.X - shot.From.X) * k, y = shot.From.Y + (to.Y - shot.From.Y) * k;
                double w = shot.From.Width + (to.Width - shot.From.Width) * k, h = shot.From.Height + (to.Height - shot.From.Height) * k;
                Canvas.SetLeft(img, x); Canvas.SetTop(img, y);
                img.Width = Math.Max(1, w); img.Height = Math.Max(1, h);
                // К концу полёта снимок растворяется в настоящем элементе
                double fade = Math.Clamp((t - 0.7) / 0.3, 0, 1);
                img.Opacity = 1 - fade;
                target.Opacity = fade;
                if (t >= 1)
                {
                    CompositionTarget.Rendering -= Frame;
                    Layer.Children.Remove(img);
                    target.Opacity = 1;
                }
            }
            CompositionTarget.Rendering += Frame;
        }
    }
}
