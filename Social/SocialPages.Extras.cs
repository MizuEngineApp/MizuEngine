// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MizuEngine
{
    /// <summary>
    /// 0.11.4: строка «официальный аккаунт» у владельца, ссылки (как подключения в Discord), «Сейчас слушает»,
    /// живой баннер, рамки и живая аватарка (Mizu Plus).
    /// </summary>
    internal sealed partial class SocialPages
    {
        // ================== строка под описанием владельца ==================

        /// <summary>Как у Дурова в Telegram: наклонным серым шрифтом под описанием.</summary>
        static FrameworkElement OfficialLine(bool afterBio)
        {
            var t = UI.L("profile.official", 12.5, sub: true, wrap: true);
            t.FontStyle = FontStyles.Italic;
            t.TextAlignment = TextAlignment.Center;
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.Opacity = 0.9;
            t.Margin = new Thickness(6, afterBio ? 8 : 12, 6, 0);
            return t;
        }

        // ================== живой баннер и аватарка ==================

        /// <summary>Поверх обычного баннера: свой GIF/MP4 и/или готовый живой стиль (только пока у человека есть Plus).</summary>
        static void DecorateBanner(FrameworkElement banner, Profile p)
        {
            if (banner is not Grid g || p == null || !p.IsPlus) return;
            int at = 1;   // над картинкой баннера, под затемнением
            if (!string.IsNullOrEmpty(p.BannerAnim))
            {
                var anim = MediaFileCache.View(p.BannerAnim);
                RoundTop(anim, 27);
                g.Children.Insert(at++, anim);
            }
            if (ProfileFx.IsBanner(p.BannerFx))
                g.Children.Insert(Math.Min(g.Children.Count, at + 1), new BannerFx(p.BannerFx));   // над затемнением, под кнопкой «Сменить баннер»
        }

        /// <summary>Скругляет только верхние углы (как у баннера).</summary>
        static void RoundTop(FrameworkElement e, double r)
        {
            e.SizeChanged += (s, a) =>
            {
                double w = e.ActualWidth, h = e.ActualHeight;
                if (w <= 0 || h <= 0) return;
                double rr = Math.Min(r, Math.Min(w, h) / 2);
                var geo = new StreamGeometry();
                using (var c = geo.Open())
                {
                    c.BeginFigure(new Point(0, h), true, true);
                    c.LineTo(new Point(0, rr), false, false);
                    c.ArcTo(new Point(rr, 0), new Size(rr, rr), 0, false, SweepDirection.Clockwise, false, false);
                    c.LineTo(new Point(w - rr, 0), false, false);
                    c.ArcTo(new Point(w, rr), new Size(rr, rr), 0, false, SweepDirection.Clockwise, false, false);
                    c.LineTo(new Point(w, h), false, false);
                }
                geo.Freeze();
                e.Clip = geo;
            };
        }

        /// <summary>Аватарка профиля: живая (GIF/MP4) поверх обычной — у подписчиков Plus.</summary>
        static FrameworkElement ProfileAvatar(Profile p, double size)
        {
            var face = UI.Avatar(p, size);
            if (p == null || !p.IsPlus || string.IsNullOrEmpty(p.AvatarAnim)) return face;
            var g = new Grid { Width = size, Height = size };
            g.Children.Add(face);
            var anim = MediaFileCache.View(p.AvatarAnim);
            anim.Clip = new EllipseGeometry(new Point(size / 2, size / 2), size / 2, size / 2);
            g.Children.Add(anim);
            return g;
        }

        /// <summary>Рамка вокруг аватарки в шапке профиля. ring — круглая аватарка с ободком (её размер не меняется).</summary>
        static FrameworkElement WithFrame(Border ring, Profile p, double ringSize)
        {
            if (p == null || !p.IsPlus || !ProfileFx.IsFrame(p.AvatarFrame)) return ring;
            var holder = new Grid { HorizontalAlignment = ring.HorizontalAlignment, VerticalAlignment = ring.VerticalAlignment, Margin = ring.Margin };
            ring.Margin = new Thickness(0);
            holder.Children.Add(ring);
            holder.Children.Add(new FrameFx(p.AvatarFrame, ringSize) { Margin = new Thickness(-FrameFx.Pad) });
            return holder;
        }

        // ================== «Сейчас слушает» ==================

        /// <summary>Карточка «Сейчас слушает»: обложка, название, исполнитель, полоска. Пусто — карточки нет.</summary>
        FrameworkElement NowPlayingCard(Profile p)
        {
            var card = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Padding = new Thickness(10), Margin = new Thickness(0, 14, 0, 0), Visibility = Visibility.Collapsed, Opacity = 0 };
            card.SetResourceReference(Border.BackgroundProperty, "HoverBg");
            card.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            card.Child = grid;

            // Обложка (пока нет — нота на градиенте)
            var coverHost = new Grid { Width = 54, Height = 54, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            var coverBg = new Border { CornerRadius = new CornerRadius(11), Background = new LinearGradientBrush(Color.FromRgb(0x4D, 0xA8, 0xFF), Color.FromRgb(0x8B, 0x5C, 0xF6), 45) };
            var note = new TextBlock { Text = "", FontFamily = (FontFamily)UI.Owner.FindResource("Icons"), FontSize = 22, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            coverBg.Child = note;
            var cover = new Border { CornerRadius = new CornerRadius(11), Opacity = 0 };
            coverHost.Children.Add(coverBg);
            coverHost.Children.Add(cover);
            grid.Children.Add(coverHost);

            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(texts, 1);
            grid.Children.Add(texts);

            // «Сейчас слушает · Spotify» + маленький эквалайзер
            var eq = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 6, 2), Height = 10 };
            var bars = new List<ScaleTransform>();
            for (int i = 0; i < 3; i++)
            {
                var st = new ScaleTransform(1, 0.4);
                var bar = new Border { Width = 2.4, Height = 10, CornerRadius = new CornerRadius(1.2), Margin = new Thickness(0, 0, 1.6, 0), RenderTransformOrigin = new Point(0.5, 1), RenderTransform = st, VerticalAlignment = VerticalAlignment.Bottom };
                bar.SetResourceReference(Border.BackgroundProperty, "Accent");
                eq.Children.Add(bar);
                bars.Add(st);
            }
            var head = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            head.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            var headRow = new DockPanel { LastChildFill = true };
            headRow.Children.Add(eq);
            headRow.Children.Add(head);
            texts.Children.Add(headRow);

            var title = UI.T("", 14, true);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.Margin = new Thickness(0, 2, 0, 0);
            var artist = UI.T("", 12.5, sub: true);
            artist.TextTrimming = TextTrimming.CharacterEllipsis;
            texts.Children.Add(title);
            texts.Children.Add(artist);

            var track = new Grid { Height = 3, Margin = new Thickness(0, 7, 0, 0) };
            var trackBg = new Border { CornerRadius = new CornerRadius(1.5) };
            trackBg.SetResourceReference(Border.BackgroundProperty, "Divider");
            var fill = new Border { CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            fill.SetResourceReference(Border.BackgroundProperty, "Accent");
            track.Children.Add(trackBg);
            track.Children.Add(fill);
            texts.Children.Add(track);
            var times = new Grid { Margin = new Thickness(0, 3, 0, 0) };
            var tNow = UI.T("", 10.5, sub: true);
            var tAll = UI.T("", 10.5, sub: true);
            tAll.HorizontalAlignment = HorizontalAlignment.Right;
            times.Children.Add(tNow);
            times.Children.Add(tAll);
            texts.Children.Add(times);

            NowPlayingInfo cur = null;
            string shownCover = null;
            bool eqOn = false;

            static string Clock(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

            void Equalizer(bool on)
            {
                if (on == eqOn) return;
                eqOn = on;
                for (int i = 0; i < bars.Count; i++)
                {
                    if (on && !Motion.Off)
                    {
                        var a = new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(380 + i * 130)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
                        bars[i].BeginAnimation(ScaleTransform.ScaleYProperty, a);
                    }
                    else { bars[i].BeginAnimation(ScaleTransform.ScaleYProperty, null); bars[i].ScaleY = on ? 0.7 : 0.35; }
                }
            }

            void Progress()
            {
                if (cur == null) return;
                var now = cur.Now;
                tNow.Text = Clock(now);
                tAll.Text = cur.Duration > 0 ? Clock(TimeSpan.FromSeconds(cur.Duration)) : "";
                double k = cur.Duration > 0 ? Math.Clamp(now.TotalSeconds / cur.Duration, 0, 1) : 0;
                fill.Width = Math.Max(0, track.ActualWidth * k);
                track.Visibility = times.Visibility = cur.Duration > 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            void Show(NowPlayingInfo n)
            {
                if (n == null)
                {
                    if (card.Visibility == Visibility.Visible)
                        Anim.To(card, UIElement.OpacityProperty, 0, 200, done: () => { if (cur == null) card.Visibility = Visibility.Collapsed; });
                    cur = null;
                    Equalizer(false);
                    return;
                }
                cur = n;
                head.Text = Loc.T(n.Playing ? "np.title" : "np.paused") + (string.IsNullOrEmpty(n.App) ? "" : "  ·  " + n.App);
                title.Text = n.Title;
                title.ToolTip = n.Title;
                artist.Text = n.Artist;
                artist.Visibility = string.IsNullOrEmpty(n.Artist) ? Visibility.Collapsed : Visibility.Visible;
                if (n.CoverUrl != shownCover)
                {
                    shownCover = n.CoverUrl;
                    cover.Opacity = 0;
                    cover.Background = string.IsNullOrEmpty(n.CoverUrl) ? null : UI.WebBrush(n.CoverUrl, 160, ready: () => Anim.To(cover, UIElement.OpacityProperty, 1, 220));
                }
                Equalizer(n.Playing);
                Progress();
                if (card.Visibility != Visibility.Visible)
                {
                    card.Visibility = Visibility.Visible;
                    Anim.To(card, UIElement.OpacityProperty, 1, 260, from: 0);
                }
            }

            async Task Load()
            {
                var n = await NowPlayingApi.Get(p.Id);
                if (card.IsLoaded) Show(n);
            }

            var tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            int seconds = 0;
            tick.Tick += async (s, e) =>
            {
                Progress();
                if (++seconds % 20 == 0) await Load();
            };
            card.Loaded += async (s, e) => { tick.Start(); await Load(); };
            card.Unloaded += (s, e) => { tick.Stop(); Equalizer(false); };
            track.SizeChanged += (s, e) => Progress();
            return card;
        }

        // ================== ссылки: редактор ==================

        /// <summary>Ссылки в профиле: список с удалением, добавление (приложение + адрес), лимит 2 / 6 с Plus.</summary>
        Border LinksPanel(Profile p)
        {
            var sp = new StackPanel();
            var header = UI.Row(UI.L("links.title", 16, true));
            sp.Children.Add(header);
            var sub = UI.L("links.sub", 12.5, sub: true, wrap: true);
            sub.Margin = new Thickness(0, 4, 0, 8);
            sp.Children.Add(sub);

            var links = new List<ProfileLink>(p.Links ?? new List<ProfileLink>());
            var list = new StackPanel();
            sp.Children.Add(list);
            var count = UI.T("", 12, sub: true);
            count.Margin = new Thickness(2, 8, 0, 0);
            sp.Children.Add(count);

            // Добавление: выбор приложения значками + поле адреса
            string kind = ProfileLinks.Kinds[0];
            var picker = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            var pickFrames = new Dictionary<string, Border>();
            var field = UI.Field("", 200);
            var ph = UI.T("", 13, sub: true);
            ph.IsHitTestVisible = false;
            ph.Margin = new Thickness(12, 0, 0, 0);
            ph.VerticalAlignment = VerticalAlignment.Center;
            ph.Opacity = 0.6;
            var fieldHost = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            fieldHost.Children.Add(field);
            fieldHost.Children.Add(ph);
            field.TextChanged += (s, e) => ph.Visibility = field.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

            void Pick(string k)
            {
                kind = k;
                foreach (var kv in pickFrames)
                {
                    kv.Value.BorderThickness = new Thickness(kv.Key == k ? 2 : 1);
                    kv.Value.SetResourceReference(Border.BorderBrushProperty, kv.Key == k ? "Accent" : "CardRim");
                }
                ph.Text = ProfileLinks.Example(k);
            }
            foreach (var k in ProfileLinks.Kinds)
            {
                string kk = k;
                var inner = UI.Row(ProfileLinks.Bubble(k, 22), Pad(UI.T(ProfileLinks.Name(k), 12.5, true), 7));
                var chip = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(6, 5, 12, 5), Margin = new Thickness(0, 0, 8, 8), BorderThickness = new Thickness(1), Child = inner, Cursor = Cursors.Hand };
                chip.SetResourceReference(Border.BackgroundProperty, "HoverBg");
                chip.MouseLeftButtonUp += (s, e) => { Pick(kk); field.Focus(); };
                pickFrames[k] = chip;
                picker.Children.Add(chip);
            }
            sp.Children.Add(picker);
            sp.Children.Add(fieldHost);
            var error = UI.Error();
            sp.Children.Add(error);
            var add = UI.Primary("links.add", null);
            add.Margin = new Thickness(0, 12, 0, 0);
            sp.Children.Add(add);
            Pick(kind);

            async Task<bool> Save(List<ProfileLink> next)
            {
                UI.ShowError(error, null);
                string err = await SocialApi.UpdateMe(new JsonObject { ["links"] = ProfileLinks.ToJson(next) });
                if (err != null) { UI.ShowError(error, err); return false; }
                links = next;
                p.Links = next;
                Render();
                return true;
            }

            void Render()
            {
                list.Children.Clear();
                foreach (var l in links)
                {
                    var link = l;
                    var del = UI.Glass("", null, null, 6);
                    del.ToolTip = Loc.T("links.remove");
                    del.Click += async (s, e) =>
                    {
                        del.IsEnabled = false;
                        if (!await Save(links.Where(x => x != link).ToList())) del.IsEnabled = true;
                    };
                    var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    names.Children.Add(UI.T(ProfileLinks.Name(link.Kind), 13.5, true));
                    var u = UI.T(ProfileLinks.Short(link.Url), 12, sub: true);
                    u.TextTrimming = TextTrimming.CharacterEllipsis;
                    names.Children.Add(u);
                    var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
                    var bubble = ProfileLinks.Bubble(link.Kind, 32);
                    bubble.Margin = new Thickness(0, 0, 10, 0);
                    DockPanel.SetDock(bubble, System.Windows.Controls.Dock.Left);
                    DockPanel.SetDock(del, System.Windows.Controls.Dock.Right);
                    row.Children.Add(bubble);
                    row.Children.Add(del);
                    row.Children.Add(names);
                    list.Children.Add(row);
                }
                if (links.Count == 0) list.Children.Add(UI.L("links.none", 13, sub: true));
                int lim = ProfileLinks.Limit(p);
                count.Text = string.Format(Loc.T("links.count"), links.Count, lim) + (lim < 6 ? "  ·  " + Loc.T("links.plus") : "");
                bool full = links.Count >= lim;
                add.IsEnabled = !full;
                field.IsEnabled = !full;
            }
            Render();

            add.Click += async (s, e) =>
            {
                string raw = field.Text.Trim();
                if (raw.Length == 0) return;
                // Вставили ссылку другого приложения — сами выберем нужное
                string guess = ProfileLinks.Guess(raw);
                if (guess != null && guess != kind) Pick(guess);
                string url = ProfileLinks.Normalize(kind, raw);
                if (url == null) { UI.ShowError(error, string.Format(Loc.T("links.bad"), ProfileLinks.Name(kind), ProfileLinks.Example(kind))); return; }
                if (links.Any(x => string.Equals(x.Url, url, StringComparison.OrdinalIgnoreCase))) { UI.ShowError(error, Loc.T("err.link_duplicate")); return; }
                add.IsEnabled = false;
                var next = new List<ProfileLink>(links) { new ProfileLink { Kind = kind, Url = url } };
                if (await Save(next)) { field.Text = ""; _toast(Loc.T("profile.saved")); }
                else add.IsEnabled = true;
            };
            field.KeyDown += (s, e) => { if (e.Key == Key.Enter && add.IsEnabled) add.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); };
            return UI.Card(sp);
        }

        /// <summary>Кто видит «Сейчас слушает» — для панели редактирования профиля.</summary>
        FrameworkElement NowPlayingVisibility(Profile p)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            sp.Children.Add(UI.L("np.vis", 13.5, true));
            var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var k in new[] { "everyone", "friends", "nobody" })
            {
                string key = k;
                row.Children.Add(UI.Tab("np.vis." + key, "npvis_" + p.Id, (p.NpVis ?? "everyone") == key, async (o, e) =>
                {
                    string r = await SocialApi.UpdateMe(new JsonObject { ["np_vis"] = key });
                    if (r == null) { p.NpVis = key; NowPlayingSync.Refresh(); }
                    _toast(r ?? Loc.T("set.saved"));
                }));
            }
            sp.Children.Add(row);
            var hint = UI.L("np.vis.d", 12, sub: true, wrap: true);
            hint.Margin = new Thickness(0, 6, 0, 0);
            sp.Children.Add(hint);
            return sp;
        }

        // ================== оформление (Mizu Plus) ==================

        /// <summary>Живой баннер, рамка аватарки, свои GIF/MP4. Без Plus — всё видно (живые превью), но выбрать нельзя.</summary>
        Border DecorPanel(Profile p)
        {
            var sp = new StackPanel();
            var head = UI.Row(UI.L("decor.title", 16, true), UI.PlusAfter(18, 8));
            sp.Children.Add(head);
            var sub = UI.L(Plus.IsActive ? "decor.sub" : "decor.sub.locked", 12.5, sub: true, wrap: true);
            sub.Margin = new Thickness(0, 4, 0, 0);
            sp.Children.Add(sub);
            if (!Plus.IsActive)
            {
                var get = UI.Primary("decor.get", (s, e) => UI.OpenPlus?.Invoke());
                get.Margin = new Thickness(0, 10, 0, 0);
                sp.Children.Add(get);
            }
            var error = UI.Error();

            async Task Save(JsonObject ch)
            {
                UI.ShowError(error, null);
                string err = await SocialApi.UpdateMe(ch);
                if (err != null) { UI.ShowError(error, err); return; }
                _toast(Loc.T("profile.saved"));
                await ShowProfile(null);
            }

            // --- баннер ---
            sp.Children.Add(SubHead("decor.banner"));
            var bannerTiles = new WrapPanel();
            void BannerTile(string id)
            {
                var g = new Grid { Width = 132, Height = 58 };
                var bg = new Border { CornerRadius = new CornerRadius(12) };
                bg.Background = string.IsNullOrEmpty(p.BannerUrl)
                    ? new LinearGradientBrush(Color.FromRgb(0x7F, 0xC4, 0xFF), Color.FromRgb(0x7A, 0x3C, 0xE8), 30)
                    : UI.WebBrush(p.BannerUrl, 400);
                g.Children.Add(bg);
                BannerFx fx = null;
                if (id != null)
                {
                    fx = new BannerFx(id, 12) { Paused = true };   // превью оживает только под мышью
                    g.Children.Add(fx);
                }
                var label = new Border { CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(Color.FromArgb(0x90, 0, 0, 0)), Padding = new Thickness(7, 2, 7, 3), Margin = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
                var lt = new TextBlock { Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold };
                lt.SetResourceReference(TextBlock.TextProperty, id == null ? "decor.none" : "decor.fx." + id);
                label.Child = lt;
                g.Children.Add(label);
                bool sel = (p.BannerFx ?? "") == (id ?? "");
                var frame = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(sel ? 2.5 : 1), Padding = new Thickness(2), Margin = new Thickness(0, 0, 8, 8), Child = g, Cursor = Plus.IsActive ? Cursors.Hand : Cursors.Arrow };
                frame.SetResourceReference(Border.BorderBrushProperty, sel ? "Accent" : "CardRim");
                g.Clip = new RectangleGeometry(new Rect(0, 0, 132, 58), 12, 12);
                if (fx != null) { frame.MouseEnter += (s, e) => fx.Paused = false; frame.MouseLeave += (s, e) => fx.Paused = true; }
                if (Plus.IsActive) frame.MouseLeftButtonUp += async (s, e) => await Save(new JsonObject { ["banner_fx"] = id });
                bannerTiles.Children.Add(frame);
            }
            BannerTile(null);
            foreach (var id in ProfileFx.Banners) BannerTile(id);
            sp.Children.Add(bannerTiles);
            sp.Children.Add(MediaRow(p, "banner", p.BannerAnim, Save));

            // --- рамка аватарки ---
            sp.Children.Add(SubHead("decor.frame"));
            var frameTiles = new WrapPanel();
            void FrameTile(string id)
            {
                const double av = 56;
                var g = new Grid { Width = av + FrameFx.Pad * 2, Height = av + FrameFx.Pad * 2 };
                var face = UI.Avatar(p, av);
                face.HorizontalAlignment = HorizontalAlignment.Center;
                face.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(face);
                FrameFx ffx = id != null ? new FrameFx(id, av) { Paused = true } : null;
                if (ffx != null) g.Children.Add(ffx);
                var stack = new StackPanel();
                stack.Children.Add(g);
                var lt = UI.L(id == null ? "decor.none" : "decor.frame." + id, 11.5, true);
                lt.HorizontalAlignment = HorizontalAlignment.Center;
                stack.Children.Add(lt);
                bool sel = (p.AvatarFrame ?? "") == (id ?? "");
                var frame = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(sel ? 2.5 : 1), Padding = new Thickness(4, 2, 4, 6), Margin = new Thickness(0, 0, 8, 8), Child = stack, Cursor = Plus.IsActive ? Cursors.Hand : Cursors.Arrow };
                frame.SetResourceReference(Border.BorderBrushProperty, sel ? "Accent" : "CardRim");
                frame.SetResourceReference(Border.BackgroundProperty, "HoverBg");
                if (ffx != null) { frame.MouseEnter += (s, e) => ffx.Paused = false; frame.MouseLeave += (s, e) => ffx.Paused = true; }
                if (Plus.IsActive) frame.MouseLeftButtonUp += async (s, e) => await Save(new JsonObject { ["avatar_frame"] = id });
                frameTiles.Children.Add(frame);
            }
            FrameTile(null);
            foreach (var id in ProfileFx.Frames) FrameTile(id);
            sp.Children.Add(frameTiles);
            sp.Children.Add(MediaRow(p, "avatar", p.AvatarAnim, Save));
            sp.Children.Add(error);
            return UI.Card(sp);
        }

        static FrameworkElement SubHead(string key)
        {
            var t = UI.L(key, 13.5, true);
            t.Margin = new Thickness(0, 16, 0, 8);
            return t;
        }

        /// <summary>«Свой GIF/MP4» и «Убрать» для баннера или аватарки.</summary>
        FrameworkElement MediaRow(Profile p, string what, string current, Func<JsonObject, Task> save)
        {
            string column = what == "banner" ? "banner_anim" : "avatar_anim";
            var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
            var pick = UI.Glass("", what == "banner" ? "decor.own_banner" : "decor.own_avatar", null, 8);
            pick.IsEnabled = Plus.IsActive;
            pick.Click += async (s, e) =>
            {
                var dlg = new OpenFileDialog { Filter = "GIF / MP4|*.gif;*.mp4;*.webm;*.mov" };
                if (dlg.ShowDialog() != true) return;
                var fi = new FileInfo(dlg.FileName);
                if (fi.Length > MediaFileCache.MaxUpload) { _toast(string.Format(Loc.T("decor.too_big"), MediaFileCache.MaxUpload / 1024 / 1024)); return; }
                string ext = fi.Extension.ToLowerInvariant();
                string type = ext switch { ".gif" => "image/gif", ".webm" => "video/webm", ".mov" => "video/quicktime", _ => "video/mp4" };
                pick.IsEnabled = false;
                _toast(Loc.T("profile.uploading"));
                string url = await Supa.Upload($"{Supa.Me}/{what}-anim-{DateTime.UtcNow.Ticks}{ext}", await File.ReadAllBytesAsync(dlg.FileName), type);
                pick.IsEnabled = true;
                if (url == null) { _toast(Loc.T("err.upload")); return; }
                await save(new JsonObject { [column] = url });
            };
            pick.Margin = new Thickness(0, 4, 8, 0);
            row.Children.Add(pick);
            if (!string.IsNullOrEmpty(current))
            {
                var remove = UI.Glass("", "decor.remove_own", async (s, e) => await save(new JsonObject { [column] = null }), 8);
                remove.Margin = new Thickness(0, 4, 0, 0);
                row.Children.Add(remove);
            }
            var hint = UI.L("decor.own.d", 11.5, sub: true, wrap: true);
            hint.Margin = new Thickness(0, 6, 0, 0);
            var sp = new StackPanel();
            sp.Children.Add(row);
            sp.Children.Add(hint);
            return sp;
        }
    }
}
