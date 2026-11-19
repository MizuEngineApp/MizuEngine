// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace MizuEngine
{
    /// <summary>
    /// Вкладка «Mizu Plus» (под «Лидерами»).
    /// Без подписки: что даёт Plus, цены (130 ₽ в месяц или 500 ₽ навсегда), код для доната и кнопка оплаты.
    /// С подпиской: все функции Plus сразу настраиваются здесь — тема из видео, живые темы, живые обои,
    /// эффекты, курсоры и сборки от MizuEngine.
    /// </summary>
    internal sealed class PlusPage
    {
        readonly Grid _host;
        readonly Settings _s;
        readonly Effects _fx;
        readonly Action<string> _toast;
        readonly Action _reapplyTheme;       // заново применить тему окна (фон, видео, живая тема)
        readonly Action _openLive;           // перейти на страницу «Живые обои»
        readonly Action _openProfile;        // перейти в профиль (войти в аккаунт)
        bool _forever;                       // выбранный тариф

        public PlusPage(Grid host, Settings s, Effects fx, Action<string> toast, Action reapplyTheme, Action openLive, Action openProfile)
        {
            _host = host; _s = s; _fx = fx; _toast = toast; _reapplyTheme = reapplyTheme; _openLive = openLive; _openProfile = openProfile;
        }

        public void Show()
        {
            Plus.MarkHot();
            _host.Children.Clear();
            _host.Children.Add(UI.Scroll(Build()));
            if (Supa.SignedIn && !Plus.Known) _ = RefreshThenShow();
        }

        async Task RefreshThenShow()
        {
            await Plus.RefreshAsync();
            if (_host.IsVisible) { _host.Children.Clear(); _host.Children.Add(UI.Scroll(Build())); }
        }

        /// <summary>Подписка изменилась — перестраиваем, если вкладка открыта.</summary>
        public void Refresh()
        {
            if (_host.IsVisible) { _host.Children.Clear(); _host.Children.Add(UI.Scroll(Build())); }
        }

        FrameworkElement Build()
        {
            var root = new StackPanel { MaxWidth = 800, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 14, 28) };
            root.Children.Add(Hero());

            if (!Supa.SignedIn)
            {
                root.Children.Add(LoginCard());
                root.Children.Add(Section("plus.what"));
                root.Children.Add(Features(false));
            }
            else if (Plus.IsActive)
            {
                if (!Plus.Forever) { root.Children.Add(Section("plus.extend")); root.Children.Add(PayBlock()); }
                root.Children.Add(Section("livebg.title"));
                root.Children.Add(LiveBgLinkCard());
                root.Children.Add(Section("plus.live_wallpapers"));
                root.Children.Add(LiveWallpaperCard());
                root.Children.Add(Section("plus.effects"));
                root.Children.Add(EffectsCard());
                root.Children.Add(Section("plus.cursors"));
                root.Children.Add(CursorsCard());
                root.Children.Add(Section("plus.presets"));
                root.Children.Add(OfficialCard());
                root.Children.Add(Section("plus.accounts"));
                root.Children.Add(AccountsCard());
            }
            else
            {
                root.Children.Add(Section("plus.choose"));
                root.Children.Add(PayBlock());
                root.Children.Add(Section("plus.what"));
                root.Children.Add(Features(false));
            }
            Motion.Stagger(root, 2);
            return root;
        }

        // ================== шапка ==================

        FrameworkElement Hero()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var badge = UI.PlusBadge(76, big: true, clickable: false);
            badge.Margin = new Thickness(4, 0, 22, 0);
            g.Children.Add(badge);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(UI.T("Mizu Plus", 34, true));
            var sub = UI.L("plus.sub", 14.5, sub: true, wrap: true);
            sub.Margin = new Thickness(0, 6, 0, 0);
            text.Children.Add(sub);
            text.Children.Add(StatusChip());
            Grid.SetColumn(text, 1);
            g.Children.Add(text);

            var card = new Border { CornerRadius = new CornerRadius(26), BorderThickness = new Thickness(1.2), Padding = new Thickness(26), Child = g };
            card.Background = new LinearGradientBrush(Color.FromArgb(0x40, 0x8B, 0x5C, 0xF6), Color.FromArgb(0x14, 0x6D, 0x28, 0xD9), 20);
            card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xA7, 0x8B, 0xFA));
            card.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = UI.PlusMid, BlurRadius = 30, ShadowDepth = 0, Opacity = 0.35 };
            return card;
        }

        FrameworkElement StatusChip()
        {
            bool on = Plus.IsActive;
            var dot = new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center,
                                   Background = new SolidColorBrush(on ? Color.FromRgb(0x3D, 0xDC, 0x84) : Color.FromRgb(0x8E, 0x96, 0xA8)) };
            string text = !Supa.SignedIn ? Loc.T("plus.status.guest")
                        : on ? Loc.T("plus.status.on") + " · " + Plus.UntilText(Plus.Forever, Plus.Until)
                        : Loc.T("plus.status.free");
            var t = UI.T(text, 13, true);
            t.Margin = new Thickness(8, 0, 0, 0);
            var chip = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 6, 14, 7), Margin = new Thickness(0, 14, 0, 0),
                                    HorizontalAlignment = HorizontalAlignment.Left, Child = UI.Row(dot, t), BorderThickness = new Thickness(1) };
            chip.SetResourceReference(Border.BackgroundProperty, "HoverBg");
            chip.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            return chip;
        }

        TextBlock Section(string key)
        {
            var t = UI.L(key, 18, true);
            t.Margin = new Thickness(2, 26, 0, 12);
            return t;
        }

        static FrameworkElement Gap(FrameworkElement e, double top, double left = 0) { e.Margin = new Thickness(left, top, 0, 0); return e; }

        FrameworkElement LoginCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(UI.L("plus.login", 15, true, wrap: true));
            sp.Children.Add(Gap(UI.L("plus.login.d", 13, sub: true, wrap: true), 6));
            var b = UI.Primary("plus.login_btn", (s, e) => _openProfile());
            b.Margin = new Thickness(0, 14, 0, 0);
            sp.Children.Add(b);
            return UI.Card(sp, 16);
        }

        // ================== оплата ==================

        FrameworkElement PayBlock()
        {
            var sp = new StackPanel();
            var plans = new Grid();
            plans.ColumnDefinitions.Add(new ColumnDefinition());
            plans.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            plans.ColumnDefinitions.Add(new ColumnDefinition());

            Border month = null, forever = null;
            Button pay = null;
            void Pick(bool f)
            {
                _forever = f;
                Mark(month, !f);
                Mark(forever, f);
                if (pay != null) pay.Content = string.Format(Loc.T("plus.pay"), f ? Plus.PriceForever : Plus.PriceMonth);
            }
            month = Plan(Loc.T("plus.plan.month"), Plus.PriceMonth + " ₽", Loc.T("plus.plan.month.per"), Loc.T("plus.plan.month.d"), null, () => Pick(false));
            forever = Plan(Loc.T("plus.plan.forever"), Plus.PriceForever + " ₽", Loc.T("plus.plan.forever.per"), Loc.T("plus.plan.forever.d"), Loc.T("plus.plan.best"), () => Pick(true));
            Grid.SetColumn(forever, 2);
            plans.Children.Add(month);
            plans.Children.Add(forever);
            sp.Children.Add(plans);

            // Код для доната
            var codeBox = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            codeBox.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            codeBox.ColumnDefinitions.Add(new ColumnDefinition());
            codeBox.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = UI.L("plus.code", 13, sub: true);
            label.VerticalAlignment = VerticalAlignment.Center;
            codeBox.Children.Add(label);
            var code = new TextBlock
            {
                Text = Plus.Code ?? "MP-······", FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(12, 0, 0, 0),
                FontFamily = new FontFamily("Consolas, Cascadia Mono, Segoe UI"), VerticalAlignment = VerticalAlignment.Center
            };
            code.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            Grid.SetColumn(code, 1);
            codeBox.Children.Add(code);
            var copy = UI.Glass("", "plus.copy", (s, e) =>
            {
                if (Plus.Code == null) { _toast(Loc.T("err.network")); return; }
                try { Clipboard.SetText(Plus.Code); } catch { return; }
                _toast(Loc.T("plus.copied"));
            }, 9);
            Grid.SetColumn(copy, 2);
            codeBox.Children.Add(copy);
            var codeCard = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 12, 12, 12), Child = codeBox, BorderThickness = new Thickness(1.2),
                                        BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, 0xA7, 0x8B, 0xFA)), Margin = new Thickness(0, 16, 0, 0) };
            codeCard.Background = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0));
            sp.Children.Add(codeCard);
            if (Plus.Code == null) _ = FillCode(code);

            // Как это работает
            var steps = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            for (int i = 1; i <= 3; i++)
            {
                var num = new Border
                {
                    Width = 26, Height = 26, CornerRadius = new CornerRadius(13), VerticalAlignment = VerticalAlignment.Top,
                    Background = new SolidColorBrush(Color.FromArgb(0x50, 0x8B, 0x5C, 0xF6)),
                    Child = new TextBlock { Text = i.ToString(), Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                };
                var t = UI.L("plus.step" + i, 13.5, wrap: true);
                t.Margin = new Thickness(12, 3, 0, 0);
                var row = new DockPanel { Margin = new Thickness(0, i == 1 ? 0 : 10, 0, 0) };
                DockPanel.SetDock(num, Dock.Left);
                row.Children.Add(num);
                row.Children.Add(t);
                steps.Children.Add(row);
            }
            sp.Children.Add(steps);

            var buttons = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
            pay = new Button { Style = (Style)UI.Owner.FindResource("PrimaryButton"), Margin = new Thickness(0, 0, 10, 8) };
            pay.Click += (s, e) =>
            {
                if (Plus.Code != null) { try { Clipboard.SetText(Plus.Code); } catch { } }   // код сразу в буфере — останется вставить
                if (Plus.OpenDonate()) _toast(Loc.T(Plus.Code != null ? "plus.opened_copied" : "plus.opened"));
                else _toast(Loc.T("plus.open_failed"));
            };
            buttons.Children.Add(pay);
            var check = UI.Glass("", "plus.check", async (s, e) =>
            {
                _toast(Loc.T("plus.checking"));
                await Plus.RefreshAsync();
                if (!Plus.IsActive) _toast(Loc.T("plus.not_yet"));
            }, 10);
            check.Margin = new Thickness(0, 0, 10, 8);
            buttons.Children.Add(check);
            sp.Children.Add(buttons);
            sp.Children.Add(Gap(UI.L("plus.note", 12, sub: true, wrap: true), 6));

            Pick(_forever);
            return UI.Card(sp, 0);
        }

        async Task FillCode(TextBlock code)
        {
            await Plus.RefreshAsync();
            if (Plus.Code != null) code.Text = Plus.Code;
        }

        Border Plan(string title, string price, string per, string details, string ribbon, Action click)
        {
            var sp = new StackPanel();
            var head = new DockPanel();
            var t = UI.T(title, 14.5, true);
            head.Children.Add(t);
            if (ribbon != null)
            {
                var r = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 3, 9, 4), Margin = new Thickness(8, 0, 0, 0), Background = new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED)),
                                     Child = new TextBlock { Text = ribbon, Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold } };
                head.Children.Add(r);
            }
            sp.Children.Add(head);
            var priceRow = UI.Row(UI.T(price, 36, true), Gap(UI.T(per, 14, sub: true), 0, 8));
            ((FrameworkElement)priceRow.Children[1]).VerticalAlignment = VerticalAlignment.Bottom;
            ((FrameworkElement)priceRow.Children[1]).Margin = new Thickness(8, 0, 0, 8);
            priceRow.Margin = new Thickness(0, 6, 0, 0);
            sp.Children.Add(priceRow);
            sp.Children.Add(Gap(UI.T(details, 12.5, sub: true, wrap: true), 2));

            var card = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(20, 18, 20, 18), Child = sp, Cursor = Cursors.Hand, BorderThickness = new Thickness(1) };
            card.SetResourceReference(Border.BackgroundProperty, "CardBg");
            card.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            card.MouseLeftButtonDown += (s, e) => Motion.Pop(card, 0.98);
            card.MouseLeftButtonUp += (s, e) => click();
            return card;
        }

        static void Mark(Border card, bool on)
        {
            if (card == null) return;
            if (on)
            {
                card.BorderThickness = new Thickness(2);
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(0xA7, 0x8B, 0xFA));
                card.Background = new SolidColorBrush(Color.FromArgb(0x2E, 0x8B, 0x5C, 0xF6));
            }
            else
            {
                card.BorderThickness = new Thickness(1);
                card.SetResourceReference(Border.BorderBrushProperty, "CardRim");
                card.SetResourceReference(Border.BackgroundProperty, "CardBg");
            }
        }

        // ================== что даёт Plus ==================

        static readonly (string glyph, string key)[] FeatureList =
        {
            ("", "plus.f.video"), ("", "plus.f.live"), ("", "plus.f.accounts"),
            ("", "plus.f.custom"), ("", "plus.f.presets"), ("plus", "plus.f.badge")
        };

        FrameworkElement Features(bool unlocked)
        {
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
            foreach (var (glyph, key) in FeatureList)
            {
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                FrameworkElement icon = glyph == "plus" ? UI.PlusBadge(30, clickable: false) : UI.Squircle(glyph, (UI.PlusLight, UI.PlusDeep), 34);
                icon.VerticalAlignment = VerticalAlignment.Top;
                g.Children.Add(icon);
                var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
                text.Children.Add(UI.L(key, 14.5, true, wrap: true));
                text.Children.Add(Gap(UI.L(key + ".d", 12.5, sub: true, wrap: true), 3));
                Grid.SetColumn(text, 1);
                g.Children.Add(text);
                var card = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(16), Margin = new Thickness(0, 0, 10, 10), Child = g, BorderThickness = new Thickness(1) };
                card.SetResourceReference(Border.BackgroundProperty, "CardBg");
                card.SetResourceReference(Border.BorderBrushProperty, "CardRim");
                grid.Children.Add(card);
            }
            return grid;
        }

        // ================== тема из видео ==================

        FrameworkElement VideoThemeCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(UI.L("plus.video_theme.d", 13, sub: true, wrap: true));
            string file = File.Exists(_s.VideoTheme) ? Path.GetFileName(_s.VideoTheme) : Loc.T("plus.no_file");
            var name = UI.T(file, 13.5, true);
            name.Margin = new Thickness(0, 12, 0, 0);
            sp.Children.Add(name);

            var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            var pick = UI.Primary("plus.pick_video", (s, e) =>
            {
                var dlg = new OpenFileDialog { Filter = "MP4|*.mp4;*.m4v|Video|*.mp4;*.m4v;*.wmv;*.mov" };
                if (dlg.ShowDialog() != true) return;
                try { _s.VideoTheme = CopyVideo(dlg.FileName, "theme"); }
                catch (Exception ex) { Settings.Log(ex); _toast(Loc.T("err.generic")); return; }
                _s.Theme = "video";
                _s.FirstRunDone = true;
                _s.Save();
                _reapplyTheme();
                _toast(Loc.T("plus.video_applied"));
                Refresh();
            });
            pick.Margin = new Thickness(0, 0, 10, 8);
            buttons.Children.Add(pick);
            if (File.Exists(_s.VideoTheme) && _s.Theme != "video")
            {
                var on = UI.Glass("", "plus.use_video", (s, e) => { _s.Theme = "video"; _s.Save(); _reapplyTheme(); Refresh(); }, 10);
                on.Margin = new Thickness(0, 0, 10, 8);
                buttons.Children.Add(on);
            }
            if (_s.Theme == "video")
            {
                var off = UI.Glass("", "plus.theme_off", (s, e) => { _s.Theme = "blue"; _s.Save(); _reapplyTheme(); Refresh(); }, 10);
                off.Margin = new Thickness(0, 0, 10, 8);
                buttons.Children.Add(off);
            }
            sp.Children.Add(buttons);

            // Затемнение — чтобы текст хорошо читался на любом видео
            var dimLabel = UI.T(string.Format(Loc.T("plus.video_dim"), _s.VideoDim), 12.5, sub: true);
            dimLabel.Margin = new Thickness(0, 10, 0, 4);
            sp.Children.Add(dimLabel);
            var slider = new Slider { Minimum = 0, Maximum = 80, Value = _s.VideoDim, MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Left, Width = 360, IsSnapToTickEnabled = true, TickFrequency = 5 };
            if (UI.Owner.TryFindResource("NiceSlider") is Style ss) slider.Style = ss;
            var save = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            save.Tick += (o, e) => { save.Stop(); _s.Save(); };
            slider.ValueChanged += (o, e) =>
            {
                _s.VideoDim = (int)slider.Value;
                dimLabel.Text = string.Format(Loc.T("plus.video_dim"), _s.VideoDim);
                (Window.GetWindow(_host) as MainWindow)?.SetVideoDim(_s.VideoDim);
                save.Stop(); save.Start();
            };
            sp.Children.Add(slider);
            return UI.Card(sp, 0);
        }

        /// <summary>Видео копируется к нам — чтобы не пропало, если исходник удалят или переместят.</summary>
        internal static string CopyVideo(string file, string kind)
        {
            string dir = Path.Combine(Settings.Folder, "videos");
            Directory.CreateDirectory(dir);
            // Старые копии того же вида удаляем — видео бывают большими
            foreach (var old in Directory.GetFiles(dir, kind + "-*"))
                try { File.Delete(old); } catch { }
            string dest = Path.Combine(dir, $"{kind}-{DateTime.Now.Ticks}{Path.GetExtension(file)}");
            File.Copy(file, dest, true);
            return dest;
        }

        public static string CopyLiveVideo(string file) => CopyVideo(file, "live");

        // ================== живые темы ==================

        FrameworkElement LiveThemesCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(UI.L("plus.live_themes.d", 13, sub: true, wrap: true));
            var tiles = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            foreach (var name in Themes.Live)
            {
                string theme = name;
                var swatch = new Border { Width = 132, Height = 78, CornerRadius = new CornerRadius(12), Background = Themes.LivePreview(theme),
                                          BorderThickness = new Thickness(_s.Theme == theme ? 2.5 : 1),
                                          BorderBrush = _s.Theme == theme ? new SolidColorBrush(Color.FromRgb(0xA7, 0x8B, 0xFA)) : new SolidColorBrush(Color.FromArgb(0x50, 255, 255, 255)) };
                var label = UI.L("theme." + theme, 12.5, true);
                label.Margin = new Thickness(2, 7, 0, 0);
                var tile = new Button { Style = (Style)UI.Owner.FindResource("GlassButton"), Padding = new Thickness(8), Margin = new Thickness(0, 0, 10, 10),
                                        Content = new StackPanel { Children = { swatch, label } } };
                tile.Click += (s, e) =>
                {
                    _s.Theme = theme;
                    _s.FirstRunDone = true;
                    _s.Save();
                    _reapplyTheme();
                    Refresh();
                };
                tiles.Children.Add(tile);
            }
            sp.Children.Add(tiles);
            if (Themes.IsLive(_s.Theme))
            {
                var off = UI.Glass("", "plus.theme_off", (s, e) => { _s.Theme = "blue"; _s.Save(); _reapplyTheme(); Refresh(); }, 9);
                off.Margin = new Thickness(0, 4, 0, 0);
                sp.Children.Add(off);
            }
            return UI.Card(sp, 0);
        }

        /// <summary>Видео, GIF и живые фоны окна теперь в своём разделе «Живые фоны MizuEngine».</summary>
        FrameworkElement LiveBgLinkCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(UI.L("livebg.sub", 13, sub: true, wrap: true));
            var open = UI.Primary("livebg.open", (s, e) => UI.OpenLiveBg?.Invoke());
            open.Margin = new Thickness(0, 12, 0, 0);
            open.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(open);
            return UI.Card(sp, 0);
        }

        // ================== живые обои ==================

        FrameworkElement LiveWallpaperCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(UI.L("plus.live_wallpapers.d", 13, sub: true, wrap: true));
            string state = LiveWallpaper.Running ? Loc.T("live.state.on") + " · " + Path.GetFileName(_s.LiveWallpaper) : Loc.T("live.state.off");
            sp.Children.Add(Gap(UI.T(state, 13.5, true), 10));
            var open = UI.Primary("plus.open_live", (s, e) => _openLive());
            open.Margin = new Thickness(0, 12, 0, 0);
            sp.Children.Add(open);
            return UI.Card(sp, 0);
        }

        // ================== эффекты ==================

        FrameworkElement EffectsCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(UI.L("plus.effects.d", 13, sub: true, wrap: true));
            var pills = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            foreach (var w in Settings.PlusWeathers)
            {
                string weather = w;
                pills.Children.Add(UI.Tab("w." + weather, "plusweather", _s.Weather == weather && _s.EffectsOn, (s, e) =>
                {
                    _s.Weather = weather;
                    _s.EffectsOn = true;
                    _s.Save();
                    _fx.Run(() => _s.ApplyTo(_fx.Snow.Layer));
                    _toast(Loc.T("plus.effect_on"));
                }));
            }
            sp.Children.Add(pills);
            return UI.Card(sp, 0);
        }

        // ================== курсоры ==================

        FrameworkElement CursorsCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(UI.L("plus.cursors.d", 13, sub: true, wrap: true));
            var tiles = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            var frames = new List<(Border frame, string pack)>();
            void Select(string pack)
            {
                foreach (var (frame, p) in frames)
                {
                    frame.BorderThickness = new Thickness(p == pack ? 2 : 1);
                    frame.SetResourceReference(Border.BorderBrushProperty, p == pack ? "Accent" : "CardRim");
                }
            }
            foreach (var p in CursorPacks.PlusPacks)
            {
                string pack = p;
                var frame = new Border { Width = 90, Height = 70, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1) };
                frame.SetResourceReference(Border.BackgroundProperty, "HoverBg");
                try { frame.Child = new Image { Source = CursorPacks.Preview(pack), Width = 40, Height = 40 }; } catch (Exception e) { Settings.Log(e); }
                var name = UI.L("cursor." + pack, 12, true);
                name.HorizontalAlignment = HorizontalAlignment.Center;
                name.Margin = new Thickness(0, 6, 0, 0);
                var box = new StackPanel { Margin = new Thickness(0, 0, 10, 10), Cursor = Cursors.Hand, Background = Brushes.Transparent };
                box.Children.Add(frame);
                box.Children.Add(name);
                box.MouseLeftButtonUp += (s, e) =>
                {
                    _s.CursorPack = pack;
                    _s.Save();
                    CursorPacks.Apply(_s);
                    Select(pack);
                    Motion.Pop(frame, 0.9);
                };
                frames.Add((frame, pack));
                tiles.Children.Add(box);
            }
            Select(_s.CursorPack);
            sp.Children.Add(tiles);
            var reset = UI.Glass("", "plus.cursor_default", (s, e) => { _s.CursorPack = ""; _s.Save(); CursorPacks.Apply(_s); Select(""); }, 9);
            sp.Children.Add(reset);
            return UI.Card(sp, 0);
        }

        // ================== сборки от MizuEngine ==================

        FrameworkElement OfficialCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(UI.L("plus.presets.d", 13, sub: true, wrap: true));
            var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            list.Children.Add(UI.Loading());
            sp.Children.Add(list);
            _ = FillOfficial(list, true, _s, _toast, () => (Window.GetWindow(_host) as MainWindow)?.ReapplyAllPublic());
            return UI.Card(sp, 0);
        }

        /// <summary>Список сборок от MizuEngine. plusOnly — только сборки Plus (для этой вкладки), иначе все (для страницы «Сборки»).</summary>
        public static async Task FillOfficial(StackPanel list, bool plusOnly, Settings s, Action<string> toast, Action reapply)
        {
            var all = await PlusApi.Catalog();
            list.Children.Clear();
            if (all == null) { list.Children.Add(UI.L("err.network", 13, sub: true)); return; }
            var items = all.FindAll(p => !plusOnly || p.PlusOnly);
            if (items.Count == 0) { list.Children.Add(UI.L("plus.presets.none", 13, sub: true, wrap: true)); return; }
            foreach (var p in items) list.Children.Add(OfficialRow(p, s, toast, reapply));
        }

        static FrameworkElement OfficialRow(OfficialPreset p, Settings s, Action<string> toast, Action reapply)
        {
            bool locked = p.PlusOnly && !Plus.IsActive;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            FrameworkElement icon = p.PlusOnly ? UI.PlusBadge(30, clickable: false) : UI.Squircle("", UI.Blue, 30);
            icon.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(icon);
            var text = new StackPanel { Margin = new Thickness(12, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(UI.T(p.Name, 14.5, true));
            if (!string.IsNullOrWhiteSpace(p.Description)) text.Children.Add(UI.T(p.Description, 12, sub: true, wrap: true));
            Grid.SetColumn(text, 1);
            g.Children.Add(text);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (locked)
            {
                var lockBtn = UI.Glass("", "plus.unlock", (o, e) => UI.OpenPlus?.Invoke(), 8);
                buttons.Children.Add(lockBtn);
            }
            else
            {
                var apply = UI.Primary("presets.apply", null);
                apply.Click += async (o, e) =>
                {
                    apply.IsEnabled = false;
                    var full = await PlusApi.Load(p.Id);
                    apply.IsEnabled = true;
                    if (full == null) { toast(Loc.T("err.network")); return; }
                    string err = await PlusApi.Apply(full, s);
                    if (err != null) { toast(err); return; }
                    CursorPacks.Apply(s);
                    reapply?.Invoke();
                    toast(Loc.T("presets.applied"));
                };
                buttons.Children.Add(apply);
                var save = UI.Glass("", null, null, 9);
                save.ToolTip = Loc.T("plus.save_mine");
                save.Margin = new Thickness(8, 0, 0, 0);
                save.Click += async (o, e) =>
                {
                    var full = await PlusApi.Load(p.Id);
                    var saved = full == null ? null : await PlusApi.SaveToMine(full);
                    toast(saved != null ? Loc.T("presets.saved") : Loc.T("err.network"));
                };
                buttons.Children.Add(save);
            }
            Grid.SetColumn(buttons, 2);
            g.Children.Add(buttons);

            var card = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 8), Child = g, BorderThickness = new Thickness(1) };
            card.SetResourceReference(Border.BackgroundProperty, "HoverBg");
            card.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            if (locked) card.Opacity = 0.75;
            return card;
        }

        // ================== аккаунты ==================

        FrameworkElement AccountsCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(UI.Row(UI.Squircle("", (UI.PlusLight, UI.PlusDeep), 30), Gap(UI.L("plus.accounts.d", 13.5, wrap: true), 0, 12)));
            ((FrameworkElement)((StackPanel)sp.Children[0]).Children[1]).VerticalAlignment = VerticalAlignment.Center;
            ((FrameworkElement)((StackPanel)sp.Children[0]).Children[1]).MaxWidth = 620;
            return UI.Card(sp, 0);
        }
    }
}
