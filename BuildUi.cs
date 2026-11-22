// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace MizuEngine
{
    /// <summary>
    /// Общее для «Сборок» и «Лучших сборок» (0.11.5): обложка, значки разделов, карточка сборки,
    /// окно «Что взять из сборки» и окно публикации.
    /// </summary>
    internal static class BuildUi
    {
        // ================== разделы ==================

        public static (string glyph, (Color a, Color b) color) PartLook(string part) => part switch
        {
            "theme" => ("", UI.Purple),
            "effects" => ("", UI.Teal),
            "cursor" => ("", UI.Pink),
            "dock" => ("", UI.Blue),
            "island" => ("", UI.Gray),
            "wallpaper" => ("", UI.Green),
            "anim" => ("", UI.Orange),
            "sounds" => ("", UI.Red),
            "widgets" => ("", UI.Orange),
            "windows" => ("", UI.Blue),
            _ => ("", UI.Gray)
        };

        static string T(string key) => Loc.T(key);
        static string OrKey(string key, string fallback) { string v = Loc.T(key); return v == key ? fallback : v; }

        /// <summary>Что именно в разделе: «Аврора», «Неон · 48 px», «Включён»…</summary>
        public static string PartDetail(JsonObject sum, string part)
        {
            if (sum == null) return "";
            string theme = (string)sum["theme"], weather = (string)sum["weather"], cursor = (string)sum["cursor"] ?? "";
            switch (part)
            {
                case "theme":
                    return theme == "custom" ? T("presets.custom_theme") : theme == null ? "" : OrKey("theme." + theme, theme);
                case "effects":
                    if ((bool?)sum["effects_on"] == false) return T("builds.off");
                    return weather == "custom" ? T("presets.particles") : weather == null ? "" : OrKey("w." + weather, weather);
                case "cursor":
                    string c = cursor == "" ? T("cursor.default") : cursor == "custom" ? T("builds.custom_cursor") : OrKey("cursor." + cursor, cursor);
                    return $"{c} · {(int?)sum["cursor_size"] ?? 32} px";
                case "dock": return T((bool?)sum["dock"] == true ? "builds.on" : "builds.off");
                case "island": return T((bool?)sum["island"] == false ? "builds.off" : "builds.on");
                case "wallpaper": return T((string)sum["wallpaper"] == "live" ? "builds.wp.live" : "builds.wp.image");
                case "anim": return OrKey("anim." + (string)sum["anim"], "");
                case "sounds": return (string)sum["sounds"] is string sp ? OrKey("sounds." + sp, sp) : T("builds.sounds_msg");
                case "widgets": return T("builds.widgets_d");
                case "windows": return (string)sum["border"] is string b && b != "off" ? OrKey("borders." + b, b) : T("builds.corners");
            }
            return "";
        }

        public static List<string> PartsOf(JsonObject sum) =>
            (sum?["parts"] as JsonArray)?.Select(x => (string)x).Where(x => x != null).ToList() ?? new List<string>();

        /// <summary>Ряд маленьких цветных значков разделов (с подсказками).</summary>
        public static WrapPanel Chips(JsonObject sum, double size = 22)
        {
            var wp = new WrapPanel();
            var plus = Presets.PlusParts(sum);
            foreach (var part in PartsOf(sum))
            {
                var (glyph, color) = PartLook(part);
                var sq = UI.Squircle(glyph, plus.Contains(part) ? (UI.PlusLight, UI.PlusDeep) : color, size);
                sq.Margin = new Thickness(0, 0, 5, 5);
                string detail = PartDetail(sum, part);
                sq.ToolTip = T("builds.part." + part) + (string.IsNullOrEmpty(detail) ? "" : " — " + detail) + (plus.Contains(part) ? "  ·  Mizu Plus" : "");
                wp.Children.Add(sq);
            }
            return wp;
        }

        // ================== обложка ==================

        /// <summary>Обложка: своя картинка (файл или ссылка) или значок MizuEngine на фирменном градиенте.</summary>
        public static Border Cover(double height, CornerRadius radius, string file = null, string url = null, int decode = 720)
        {
            var b = new Border { Height = height, CornerRadius = radius };
            ImageSource img = file != null ? BuildFiles.Thumb(file, decode) : null;
            if (img != null) { b.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill }; return b; }
            if (!string.IsNullOrEmpty(url))
            {
                b.SetResourceReference(Border.BackgroundProperty, "CardBg");
                var photo = new Border { CornerRadius = radius, Opacity = 0, IsHitTestVisible = false };
                photo.SetBinding(Border.CornerRadiusProperty, new System.Windows.Data.Binding("CornerRadius") { Source = b });   // скругление как у обложки (карточка его меняет)
                photo.Background = UI.WebBrush(url, decode, ready: () => Anim.To(photo, UIElement.OpacityProperty, 1, 240));
                b.Child = photo;
                return b;
            }
            b.Background = (Brush)UI.Owner.FindResource("LogoBrush");
            var g = new Grid();
            // Мягкий блик сверху — как стекло на значке
            var gloss = new Border { Background = new LinearGradientBrush(Color.FromArgb(0x45, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90) };
            // Без своего скругления блик торчал квадратными углами из-под закруглённой обложки
            gloss.SetBinding(Border.CornerRadiusProperty, new System.Windows.Data.Binding("CornerRadius") { Source = b });
            g.Children.Add(gloss);
            double s = Math.Min(height * 0.42, 74);
            g.Children.Add(new System.Windows.Shapes.Path
            {
                Data = (Geometry)UI.Owner.FindResource("LogoM"), Fill = Brushes.White, Stretch = Stretch.Uniform,
                Width = s, Height = s * 0.86, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.95
            });
            b.Child = g;
            return b;
        }

        // ================== карточка ==================

        /// <summary>
        /// Карточка сборки: обложка сверху, название (+ значок Plus справа), подпись, значки разделов и кнопки.
        /// top — что положить поверх обложки (место в рейтинге). click — нажатие по обложке/названию.
        /// </summary>
        public static Border Card(Border cover, string name, bool plus, FrameworkElement subtitle, JsonObject sum, UIElement buttons,
                                  FrameworkElement top = null, Action click = null, double width = 336)
        {
            var sp = new StackPanel();
            var coverHost = new Grid();
            coverHost.Children.Add(cover);
            if (top != null) { top.HorizontalAlignment = HorizontalAlignment.Left; top.VerticalAlignment = VerticalAlignment.Top; top.Margin = new Thickness(12); coverHost.Children.Add(top); }
            sp.Children.Add(coverHost);

            var body = new StackPanel { Margin = new Thickness(16, 12, 16, 14) };
            var nameText = UI.T(name, 15.5, true);
            nameText.TextTrimming = TextTrimming.CharacterEllipsis;
            nameText.VerticalAlignment = VerticalAlignment.Center;
            FrameworkElement title = nameText;
            if (plus)
            {
                // Значок Mizu Plus — сразу справа от названия
                nameText.MaxWidth = width - 32 - 30;
                var pb = UI.PlusBadge(18);
                pb.Margin = new Thickness(8, 0, 0, 0);
                pb.VerticalAlignment = VerticalAlignment.Center;
                title = UI.Row(nameText, pb);
            }
            body.Children.Add(title);
            if (subtitle != null) { subtitle.Margin = new Thickness(0, 4, 0, 0); body.Children.Add(subtitle); }
            var chips = Chips(sum, 22);
            chips.Margin = new Thickness(0, 10, 0, 0);
            body.Children.Add(chips);
            if (buttons is FrameworkElement bf) { bf.Margin = new Thickness(0, 8, 0, 0); body.Children.Add(bf); }
            sp.Children.Add(body);

            var card = new Border
            {
                Width = width, CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 14, 14),
                Child = sp, RenderTransform = new TranslateTransform(), ClipToBounds = false
            };
            card.SetResourceReference(Border.BackgroundProperty, "CardBg");
            card.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            // Обложка повторяет скругление карточки сверху
            cover.CornerRadius = new CornerRadius(19, 19, 0, 0);

            card.MouseEnter += (s, e) => Anim.To((TranslateTransform)card.RenderTransform, TranslateTransform.YProperty, -3, 220);
            card.MouseLeave += (s, e) => Anim.To((TranslateTransform)card.RenderTransform, TranslateTransform.YProperty, 0, 360);
            if (click != null)
            {
                UI.Hand(coverHost);
                coverHost.MouseLeftButtonUp += (s, e) => click();
                UI.Hand(title);
                title.MouseLeftButtonUp += (s, e) => click();
            }
            return card;
        }

        /// <summary>Значок места в рейтинге: 1 — золото, 2 — серебро, 3 — бронза.</summary>
        public static Border Rank(int n)
        {
            (Color a, Color b) c = n switch
            {
                1 => (Color.FromRgb(0xFF, 0xE0, 0x7A), Color.FromRgb(0xE8, 0xA3, 0x1C)),
                2 => (Color.FromRgb(0xF1, 0xF4, 0xF8), Color.FromRgb(0x9A, 0xA6, 0xB8)),
                3 => (Color.FromRgb(0xF3, 0xB0, 0x7C), Color.FromRgb(0xB8, 0x6A, 0x32)),
                _ => (Color.FromArgb(0xC0, 0x10, 0x14, 0x20), Color.FromArgb(0xC0, 0x10, 0x14, 0x20))
            };
            var t = new TextBlock { Text = "#" + n, FontWeight = FontWeights.Bold, FontSize = 13, Foreground = n <= 3 ? new SolidColorBrush(Color.FromRgb(0x2A, 0x1C, 0x05)) : Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (n == 2) t.Foreground = new SolidColorBrush(Color.FromRgb(0x24, 0x2C, 0x3A));
            return new Border
            {
                MinWidth = 34, Height = 26, CornerRadius = new CornerRadius(13), Padding = new Thickness(9, 0, 9, 0), Child = t,
                Background = new LinearGradientBrush(c.a, c.b, 90), BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 255, 255, 255))
            };
        }

        // ================== всплывающее окно поверх страницы ==================

        /// <summary>Тёмная подложка и карточка по центру страницы. Возвращает функцию «закрыть».</summary>
        public static Action Overlay(Grid host, FrameworkElement content, double width = 470, Action closed = null)
        {
            var layer = new Grid { Focusable = true };
            Grid.SetRowSpan(layer, 99);
            Grid.SetColumnSpan(layer, 99);
            var dim = new Border { Background = new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)), Opacity = 0 };
            layer.Children.Add(dim);

            bool light = Themes.Current == "light";
            var shade = new Border { CornerRadius = new CornerRadius(22), Background = new SolidColorBrush(light ? Color.FromArgb(0xF5, 255, 255, 255) : Color.FromArgb(0xC8, 0x0C, 0x10, 0x1A)) };
            var inner = new Border { CornerRadius = new CornerRadius(22), Padding = new Thickness(22, 20, 22, 18), Child = content };
            inner.SetResourceReference(Border.BackgroundProperty, "CardBg");
            var grid = new Grid();
            grid.Children.Add(shade);
            grid.Children.Add(inner);
            var card = new Border
            {
                Width = width, CornerRadius = new CornerRadius(22), BorderThickness = new Thickness(1), Child = grid,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(0.94, 0.94), Margin = new Thickness(0, 0, 14, 0)
            };
            card.SetResourceReference(Border.BackgroundProperty, "WinBg");
            card.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 8, Opacity = 0.45, Color = Colors.Black };
            card.MouseLeftButtonUp += (s, e) => e.Handled = true;
            layer.Children.Add(card);
            host.Children.Add(layer);

            Anim.To(dim, UIElement.OpacityProperty, 1, Motion.Ms(200));
            Anim.To(card, UIElement.OpacityProperty, 1, Motion.Ms(220));
            var sc = (ScaleTransform)card.RenderTransform;
            Anim.To(sc, ScaleTransform.ScaleXProperty, 1, Motion.Ms(380), ease: Anim.Spring);
            Anim.To(sc, ScaleTransform.ScaleYProperty, 1, Motion.Ms(380), ease: Anim.Spring);

            bool isClosed = false;
            void Close()
            {
                if (isClosed) return;
                isClosed = true;
                layer.IsHitTestVisible = false;
                Anim.To(dim, UIElement.OpacityProperty, 0, Motion.Ms(200));
                Anim.To(sc, ScaleTransform.ScaleXProperty, 0.96, Motion.Ms(200));
                Anim.To(sc, ScaleTransform.ScaleYProperty, 0.96, Motion.Ms(200));
                Anim.To(card, UIElement.OpacityProperty, 0, Motion.Ms(200), done: () => host.Children.Remove(layer));
                try { closed?.Invoke(); } catch (Exception e) { Settings.Log(e); }
            }
            dim.MouseLeftButtonUp += (s, e) => Close();
            layer.KeyDown += (s, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
            layer.Loaded += (s, e) => layer.Focus();
            return Close;
        }

        static TextBlock Title(string text) { var t = UI.T(text, 19, true); t.TextWrapping = TextWrapping.Wrap; return t; }

        static Button Small(string key, bool primary)
        {
            var b = new Button { Style = (Style)UI.Owner.FindResource(primary ? "PrimaryButton" : "GlassButton"), Margin = new Thickness(8, 0, 0, 0) };
            if (!primary) b.Padding = new Thickness(16, 9, 16, 9);
            b.SetResourceReference(ContentControl.ContentProperty, key);
            return b;
        }

        // ================== «Что взять из сборки» ==================

        /// <summary>
        /// Окно применения: разделы сборки с переключателями (по умолчанию включены все). Разделы Plus без подписки
        /// помечены — их настройки сохранятся и заработают вместе с Plus. apply(выбранные разделы) вызывается по кнопке.
        /// </summary>
        public static void ApplyDialog(Grid host, string name, JsonObject sum, Func<HashSet<string>, Task> apply, Action closed = null)
        {
            var sp = new StackPanel();
            sp.Children.Add(Title(string.Format(T("builds.apply_title"), name)));
            var sub = UI.L("builds.apply_sub", 13, sub: true, wrap: true);
            sub.Margin = new Thickness(0, 6, 0, 12);
            sp.Children.Add(sub);

            var plus = Presets.PlusParts(sum);
            var picked = new HashSet<string>();
            var rows = new List<UIElement>();
            foreach (var part in PartsOf(sum))
            {
                string p = part;
                picked.Add(p);
                var (glyph, color) = PartLook(p);
                bool locked = plus.Contains(p) && !Plus.IsActive;
                var text = new StackPanel();
                var nameRow = UI.Row(UI.T(T("builds.part." + p), 13.5, true));
                if (plus.Contains(p)) nameRow.Children.Add(UI.PlusAfter(13, 6));
                text.Children.Add(nameRow);
                string detail = locked ? T("builds.plus_later") : PartDetail(sum, p);
                if (!string.IsNullOrEmpty(detail)) { var d = UI.T(detail, 11.5, sub: true); d.TextTrimming = TextTrimming.CharacterEllipsis; text.Children.Add(d); }
                var sw = new CheckBox { Style = (Style)UI.Owner.FindResource("Switch"), IsChecked = true, Margin = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
                sw.Click += (s, e) => { if (sw.IsChecked == true) picked.Add(p); else picked.Remove(p); };
                rows.Add(UI.IosRow(UI.Squircle(glyph, plus.Contains(p) ? (UI.PlusLight, UI.PlusDeep) : color, 28), text, sw));
            }
            var group = UI.IosGroup(rows.ToArray());
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 380, Content = group };
            SmoothScroll.Attach(scroll);
            sp.Children.Add(scroll);

            var backup = UI.L("builds.backup_note", 12, sub: true, wrap: true);
            backup.Margin = new Thickness(4, 10, 0, 0);
            sp.Children.Add(backup);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var cancel = Small("builds.cancel", false);
            var ok = Small("presets.apply", true);
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            sp.Children.Add(buttons);

            Action close = Overlay(host, sp, closed: closed);
            cancel.Click += (s, e) => close();
            ok.Click += async (s, e) =>
            {
                if (picked.Count == 0) { close(); return; }
                ok.IsEnabled = cancel.IsEnabled = false;
                ok.Content = T("builds.applying");
                try { await apply(new HashSet<string>(picked)); }
                finally { close(); }
            };
        }

        // ================== публикация ==================

        /// <summary>Окно «Опубликовать в Лучшие сборки»: обложка, описание, предупреждения (Plus, часы, размер) и кнопка.</summary>
        public static void PublishDialog(Grid host, PresetInfo p, Action<string> toast, Action<long> published)
        {
            var sp = new StackPanel();
            sp.Children.Add(Title(T("builds.publish_title")));
            var sub = UI.L("builds.publish_sub", 13, sub: true, wrap: true);
            sub.Margin = new Thickness(0, 6, 0, 14);
            sp.Children.Add(sub);

            // Обложка: своя картинка или значок MizuEngine
            var coverHost = new ContentControl { Focusable = false };
            void PaintCover() => coverHost.Content = Cover(150, new CornerRadius(16), p.Cover, decode: 840);
            PaintCover();
            sp.Children.Add(coverHost);
            var coverRow = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            var pick = UI.Glass("", "builds.cover_pick", (s, e) =>
            {
                var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp" };
                if (dlg.ShowDialog() != true) return;
                Presets.SetCover(p, dlg.FileName);
                PaintCover();
            }, 8);
            coverRow.Children.Add(pick);
            var noCover = UI.Glass(null, "builds.cover_icon", (s, e) => { Presets.SetCover(p, null); PaintCover(); }, 8);
            noCover.Margin = new Thickness(8, 0, 0, 0);
            coverRow.Children.Add(noCover);
            sp.Children.Add(coverRow);

            var nameRow = UI.Row(UI.T(p.Name, 15, true));
            if (p.Plus) nameRow.Children.Add(UI.PlusAfter(16, 8));
            nameRow.Margin = new Thickness(2, 14, 0, 0);
            sp.Children.Add(nameRow);

            var desc = UI.Field(p.Description ?? "", 300, multiline: true);
            desc.MinHeight = 64;
            desc.SetResourceReference(FrameworkElement.TagProperty, "builds.desc_ph");
            desc.Margin = new Thickness(0, 10, 0, 0);
            sp.Children.Add(desc);

            void Note(string text, bool warn)
            {
                var t = UI.T(text, 12.5, wrap: true);
                if (warn) t.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x4A)); else t.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
                t.Margin = new Thickness(2, 8, 0, 0);
                sp.Children.Add(t);
            }
            var me = SocialApi.MyProfile;
            bool canPublish = Supa.SignedIn && me != null && !me.Banned && BuildsApi.EnoughHours(me);
            if (!Supa.SignedIn) Note(T("builds.need_login"), true);
            else if (me != null && me.Banned) Note(T("err.banned"), true);
            else if (me != null && !BuildsApi.EnoughHours(me)) Note(BuildsApi.NotEnoughHoursText(me.UsageSeconds), true);
            if (p.Plus) Note(T("builds.publish_plus"), false);
            long size = Presets.FolderSize(p);
            if (size > Presets.MaxPublishBytes) Note(string.Format(T("builds.publish_big"), size / 1024 / 1024), false);

            var status = UI.T("", 12.5, sub: true);
            status.Margin = new Thickness(2, 10, 0, 0);
            status.Visibility = Visibility.Collapsed;
            sp.Children.Add(status);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var cancel = Small("builds.cancel", false);
            var ok = Small("builds.publish", true);
            ok.IsEnabled = canPublish;
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            sp.Children.Add(buttons);

            Action close = Overlay(host, sp, 480);
            cancel.Click += (s, e) => close();
            ok.Click += async (s, e) =>
            {
                ok.IsEnabled = cancel.IsEnabled = pick.IsEnabled = noCover.IsEnabled = false;
                status.Visibility = Visibility.Visible;
                status.Text = T("builds.packing");
                Presets.SetDescription(p, desc.Text);
                var (id, err) = await BuildsApi.Publish(p, desc.Text, t => status.Text = t);
                if (err != null)
                {
                    status.Text = err;
                    status.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
                    ok.IsEnabled = cancel.IsEnabled = pick.IsEnabled = noCover.IsEnabled = true;
                    return;
                }
                close();
                toast(T("builds.published"));
                published?.Invoke(id);
            };
        }

        /// <summary>Подтверждение («Удалить?») в том же стиле, что и остальные окна.</summary>
        public static void Confirm(Grid host, string title, string text, string okKey, Func<Task> yes)
        {
            var sp = new StackPanel();
            sp.Children.Add(Title(title));
            if (!string.IsNullOrEmpty(text)) { var t = UI.T(text, 13, sub: true, wrap: true); t.Margin = new Thickness(0, 6, 0, 0); sp.Children.Add(t); }
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var cancel = Small("builds.cancel", false);
            var ok = Small(okKey, true);
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            sp.Children.Add(buttons);
            Action close = Overlay(host, sp, 420);
            cancel.Click += (s, e) => close();
            ok.Click += async (s, e) => { ok.IsEnabled = false; try { await yes(); } finally { close(); } };
        }

        /// <summary>Поле «Название» в окне (переименовать сборку).</summary>
        public static void Prompt(Grid host, string title, string value, int max, Action<string> done)
        {
            var sp = new StackPanel();
            sp.Children.Add(Title(title));
            var box = UI.Field(value ?? "", max);
            box.Margin = new Thickness(0, 14, 0, 0);
            sp.Children.Add(box);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var cancel = Small("builds.cancel", false);
            var ok = Small("builds.save", true);
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            sp.Children.Add(buttons);
            Action close = Overlay(host, sp, 420);
            cancel.Click += (s, e) => close();
            void Ok() { if (box.Text.Trim().Length == 0) return; done(box.Text.Trim()); close(); }
            ok.Click += (s, e) => Ok();
            box.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; Ok(); } };
            box.Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
        }
    }
}
