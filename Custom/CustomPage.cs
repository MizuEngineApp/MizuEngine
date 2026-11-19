// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace MizuEngine
{
    /// <summary>Страница «Кастомизация»: курсоры, счётчик FPS, виджеты, рамки окон, звуки, горячие клавиши.</summary>
    internal sealed class CustomPage
    {
        readonly Grid _host;
        readonly Settings _s;
        readonly Effects _fx;
        readonly Action<string> _toast;

        static bool Win11 => Environment.OSVersion.Version.Build >= 22000;

        public CustomPage(Grid host, Settings s, Effects fx, Action<string> toast)
        {
            _host = host; _s = s; _fx = fx; _toast = toast;
        }

        public void Show()
        {
            _host.Children.Clear();
            _host.Children.Add(UI.Scroll(Build()));
        }

        /// <summary>Настройки поменяли горячей клавишей — перестраиваем, если страница открыта.</summary>
        public void Refresh()
        {
            if (_host.IsVisible) Show();
        }

        void SaveOverlays()
        {
            _s.Save();
            _fx.Run(() => _fx.Overlays.Apply());
        }

        FrameworkElement Build()
        {
            var root = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 14, 24) };
            root.Children.Add(UI.H1("nav.custom"));
            var sub = UI.L("custom.sub", 14, sub: true, wrap: true);
            sub.Margin = new Thickness(0, 6, 0, 0);
            root.Children.Add(sub);

            root.Children.Add(Header("", UI.Blue, "custom.cursors"));
            root.Children.Add(Cursors());
            root.Children.Add(Header("", UI.Green, "custom.fps"));
            root.Children.Add(Fps());
            root.Children.Add(Header("", UI.Purple, "custom.widgets"));
            root.Children.Add(Widgets());
            root.Children.Add(Header("", UI.Orange, "custom.borders"));
            root.Children.Add(Borders());
            root.Children.Add(Header("", UI.Pink, "custom.sounds"));
            root.Children.Add(Sounds());
            root.Children.Add(Header("", UI.Gray, "custom.hotkeys"));
            root.Children.Add(Hotkeys());
            Motion.Stagger(root, 2);
            return root;
        }

        static FrameworkElement Header(string glyph, (Color, Color) color, string key)
        {
            var row = UI.Row(UI.Squircle(glyph, color, 26), UI.L(key, 17, true));
            ((FrameworkElement)row.Children[1]).Margin = new Thickness(10, 0, 0, 0);
            row.Margin = new Thickness(2, 26, 0, 10);
            return row;
        }

        static Border Card(params UIElement[] items)
        {
            var sp = new StackPanel();
            foreach (var i in items) if (i != null) sp.Children.Add(i);
            return UI.Card(sp, 0);
        }

        static CheckBox Switch(string key, bool on, Action<bool> changed)
        {
            var cb = new CheckBox { Style = (Style)UI.Owner.FindResource("Switch"), IsChecked = on };
            cb.SetResourceReference(ContentControl.ContentProperty, key);
            cb.Click += (s, e) => changed(cb.IsChecked == true);
            return cb;
        }

        static FrameworkElement Hint(string key)
        {
            var t = UI.L(key, 12, sub: true, wrap: true);
            t.Margin = new Thickness(0, 2, 70, 6);
            return t;
        }

        static TextBlock Label(string key)
        {
            var t = UI.L(key, 13, sub: true);
            t.Margin = new Thickness(0, 14, 0, 10);
            return t;
        }

        static WrapPanel Pills(string group, (string key, string value)[] options, string current, Action<string> pick)
        {
            var wp = new WrapPanel();
            foreach (var (key, value) in options)
            {
                var v = value;
                wp.Children.Add(UI.Tab(key, group, current == v, (s, e) => pick(v)));
            }
            return wp;
        }

        // ================== курсоры ==================

        FrameworkElement Cursors()
        {
            var tiles = new WrapPanel();
            var all = new List<(Border frame, string pack)>();

            void Select(string pack)
            {
                foreach (var (frame, p) in all)
                {
                    frame.BorderThickness = new Thickness(p == pack ? 2 : 1);
                    frame.SetResourceReference(Border.BorderBrushProperty, p == pack ? "Accent" : "CardRim");
                }
            }

            void Tile(string pack, ImageSource preview, string nameKey)
            {
                var frame = new Border { Width = 96, Height = 74, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1) };
                frame.SetResourceReference(Border.BackgroundProperty, "HoverBg");
                if (preview != null) frame.Child = new Image { Source = preview, Width = 40, Height = 40 };
                else frame.Child = UI.Icon("", 26);
                var name = UI.L(nameKey, 12.5, true);
                name.HorizontalAlignment = HorizontalAlignment.Center;
                name.Margin = new Thickness(0, 6, 0, 0);
                var sp = new StackPanel { Margin = new Thickness(0, 0, 10, 10), Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent };
                sp.Children.Add(frame);
                sp.Children.Add(name);
                sp.MouseLeftButtonUp += (s, e) =>
                {
                    _s.CursorPack = pack;
                    _s.Save();
                    CursorPacks.Apply(_s);
                    Select(pack);
                    Motion.Pop(frame, 0.9);
                };
                all.Add((frame, pack));
                tiles.Children.Add(sp);
            }

            Tile("", null, "cursor.default");
            foreach (var p in CursorPacks.Packs)
            {
                ImageSource img = null;
                try { img = CursorPacks.Preview(p); } catch (Exception e) { Settings.Log(e); }
                Tile(p, img, "cursor." + p);
            }
            Select(_s.CursorPack ?? "");

            var size = Pills("cursize", new[] { ("cursor.size.s", "32"), ("cursor.size.m", "48"), ("cursor.size.l", "64") }, _s.CursorSize.ToString(), v =>
            {
                _s.CursorSize = int.Parse(v);
                _s.Save();
                CursorPacks.Apply(_s);
            });

            var own = UI.Glass("", "cursor.own", (s, e) =>
            {
                var dlg = new OpenFileDialog { Filter = "Cursors|*.cur;*.ani;*.png" };
                if (dlg.ShowDialog() != true) return;
                _s.CustomCursor = dlg.FileName;
                _s.CursorPack = "custom";
                _s.Save();
                CursorPacks.Apply(_s);
                Select("custom");
                _toast(Loc.T("cursor.applied"));
            }, 9);
            own.Margin = new Thickness(0, 14, 0, 0);

            return Card(tiles, Label("cursor.size"), size, own);
        }

        // ================== FPS ==================

        FrameworkElement Fps()
        {
            var on = Switch("fps.on", _s.FpsOn, v => { _s.FpsOn = v; SaveOverlays(); });
            var detailed = Switch("fps.detailed", _s.FpsDetailed, v => { _s.FpsDetailed = v; SaveOverlays(); });
            var locked = Switch("fps.locked", _s.FpsLocked, v => { _s.FpsLocked = v; SaveOverlays(); });

            UIElement admin = null;
            if (!PresentCounter.IsAdmin)
            {
                var sp = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
                var note = UI.L("fps.admin_note", 12.5, sub: true, wrap: true);
                sp.Children.Add(note);
                var restart = UI.Glass("", "fps.restart_admin", (s, e) =>
                {
                    if (!Program.RestartAsAdmin()) _toast(Loc.T("fps.admin_declined"));
                }, 8);
                restart.Margin = new Thickness(0, 10, 0, 0);
                sp.Children.Add(restart);
                admin = sp;
            }
            return Card(on, Hint("fps.on.d"), detailed, locked, Hint("fps.locked.d"), admin);
        }

        // ================== виджеты ==================

        FrameworkElement Widgets()
        {
            var sys = Switch("widget.system", _s.WidgetSystem, v => { _s.WidgetSystem = v; SaveOverlays(); });
            var clock = Switch("widget.clock", _s.WidgetClock, v => { _s.WidgetClock = v; SaveOverlays(); });
            var weather = Switch("widget.weather", _s.WidgetWeather, v => { _s.WidgetWeather = v; SaveOverlays(); });
            var top = Switch("widget.ontop", _s.WidgetsOnTop, v => { _s.WidgetsOnTop = v; SaveOverlays(); });

            var city = UI.Field(_s.WeatherCity ?? "", 60);
            city.SetResourceReference(FrameworkElement.TagProperty, "widget.city.ph");
            var save = UI.Primary("widget.city.save", (s, e) =>
            {
                _s.WeatherCity = city.Text.Trim();
                SaveOverlays();
                _toast(Loc.T("profile.saved"));
            });
            save.Margin = new Thickness(8, 0, 0, 0);
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(save, 1);
            g.Children.Add(city);
            g.Children.Add(save);
            var cityRow = UI.Labeled("widget.city", g);

            return Card(sys, clock, weather, cityRow, top, Hint("widget.drag"));
        }

        // ================== рамки окон ==================

        FrameworkElement Borders()
        {
            if (!Win11) return Card(UI.L("borders.win11", 13, sub: true, wrap: true));

            var swatches = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            string[] colors = { "#FF4A97FF", "#FF8E3CE8", "#FFFF5C8A", "#FFFF8A0A", "#FFFFD60A", "#FF30D158", "#FF64D2FF", "#FFFFFFFF" };
            foreach (var hex in colors)
            {
                var h = hex;
                var dot = new Border
                {
                    Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(0, 0, 10, 0), Cursor = System.Windows.Input.Cursors.Hand,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h)),
                    BorderThickness = new Thickness(h == _s.BorderColor ? 3 : 1),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, 255, 255, 255))
                };
                dot.MouseLeftButtonUp += (s, e) =>
                {
                    _s.BorderColor = h;
                    SaveOverlays();
                    foreach (Border d in swatches.Children) d.BorderThickness = new Thickness(d == dot ? 3 : 1);
                    Motion.Pop(dot, 0.8);
                };
                swatches.Children.Add(dot);
            }
            swatches.Visibility = _s.BorderMode == "color" ? Visibility.Visible : Visibility.Collapsed;

            var mode = Pills("bmode", new[] { ("borders.off", "off"), ("borders.accent", "accent"), ("borders.color", "color"), ("borders.rainbow", "rainbow") }, _s.BorderMode, v =>
            {
                _s.BorderMode = v;
                swatches.Visibility = v == "color" ? Visibility.Visible : Visibility.Collapsed;
                SaveOverlays();
            });
            var corners = Pills("bcorner", new[] { ("corners.default", "default"), ("corners.round", "round"), ("corners.square", "square") }, _s.Corners, v =>
            {
                _s.Corners = v;
                SaveOverlays();
            });
            return Card(UI.L("borders.mode", 13, sub: true), Spacer(10), mode, swatches, Label("corners.title"), corners);
        }

        static FrameworkElement Spacer(double h) => new Border { Height = h };

        // ================== звуки ==================

        FrameworkElement Sounds()
        {
            var on = Switch("sounds.on", _s.Sounds, v => { _s.Sounds = v; UiSounds.On = v; _s.Save(); if (v) UiSounds.Play(_s.SoundPack); });
            var pack = Pills("spack", UiSounds.Packs.Select(k => ("sounds." + k, k)).ToArray(), _s.SoundPack, v =>
            {
                _s.SoundPack = v;
                UiSounds.Pack = v;
                _s.Save();
                if (!UiSounds.On) UiSounds.Play(v);   // если звуки включены, щелчок и так прозвучит
            });
            return Card(on, Hint("sounds.d"), Label("sounds.pack"), pack);
        }

        // ================== горячие клавиши ==================

        FrameworkElement Hotkeys()
        {
            var on = Switch("hk.on", _s.Hotkeys, v => { _s.Hotkeys = v; _s.Save(); MizuHotkeys.Apply(); });
            var rows = new List<UIElement>();
            foreach (var k in MizuHotkeys.Keys)
            {
                var keys = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 4, 10, 5), BorderThickness = new Thickness(1) };
                keys.SetResourceReference(Border.BackgroundProperty, "HoverBg");
                keys.SetResourceReference(Border.BorderBrushProperty, "CardRim");
                keys.Child = UI.T("Ctrl + Alt + " + k.key, 12.5, true);
                rows.Add(UI.IosRow(null, UI.L(k.text, 13.5), keys));
            }
            var panic = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 4, 10, 5), BorderThickness = new Thickness(1) };
            panic.SetResourceReference(Border.BackgroundProperty, "HoverBg");
            panic.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            panic.Child = UI.T("Ctrl + Alt + Shift + N", 12.5, true);
            rows.Add(UI.IosRow(null, UI.L("hk.panic", 13.5), panic));
            var list = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var r in rows) list.Children.Add(r);
            return Card(on, list);
        }
    }

    // ======================================================================

    /// <summary>Страница «Анимации»: как переключаются страницы и с какой скоростью.</summary>
    internal sealed class AnimPage
    {
        readonly Grid _host;
        readonly Settings _s;

        public AnimPage(Grid host, Settings s) { _host = host; _s = s; }

        public void Show()
        {
            _host.Children.Clear();
            _host.Children.Add(UI.Scroll(Build()));
        }

        FrameworkElement Build()
        {
            var root = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 14, 24) };
            root.Children.Add(UI.H1("nav.anim"));
            var sub = UI.L("anim.sub", 14, sub: true, wrap: true);
            sub.Margin = new Thickness(0, 6, 0, 0);
            root.Children.Add(sub);

            root.Children.Add(UI.Section("anim.style", 22));
            var tiles = new WrapPanel();
            var frames = new List<(Border frame, string style)>();
            void Select(string style)
            {
                foreach (var (f, st) in frames)
                {
                    f.BorderThickness = new Thickness(st == style ? 2 : 1);
                    f.SetResourceReference(Border.BorderBrushProperty, st == style ? "Accent" : "CardRim");
                }
            }
            foreach (var style in Motion.Styles)
            {
                var st = style;
                var frame = new Border { Width = 168, Height = 104, CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), ClipToBounds = true };
                frame.SetResourceReference(Border.BackgroundProperty, "HoverBg");
                var demo = DemoCard();
                frame.Child = demo;
                var name = UI.L("anim." + st, 13.5, true);
                name.Margin = new Thickness(2, 8, 0, 0);
                var desc = UI.L("anim." + st + ".d", 11.5, sub: true, wrap: true);
                desc.Width = 168;
                var sp = new StackPanel { Margin = new Thickness(0, 0, 12, 14), Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent };
                sp.Children.Add(frame);
                sp.Children.Add(name);
                sp.Children.Add(desc);
                sp.MouseEnter += (s, e) => Play(demo, st);
                sp.MouseLeftButtonUp += (s, e) =>
                {
                    _s.UiAnim = st;
                    _s.Save();
                    Motion.Style = st;
                    Select(st);
                    Play(demo, st);
                };
                frames.Add((frame, st));
                tiles.Children.Add(sp);
            }
            Select(_s.UiAnim);
            root.Children.Add(tiles);

            root.Children.Add(UI.Section("anim.speed", 10));
            var speed = new WrapPanel();
            foreach (var (key, value) in new[] { ("anim.fast", 0.7), ("anim.normal", 1.0), ("anim.smooth", 1.35) })
            {
                var v = value;
                speed.Children.Add(UI.Tab(key, "aspeed", Math.Abs(_s.UiAnimSpeed - v) < 0.01, (s, e) =>
                {
                    _s.UiAnimSpeed = v;
                    _s.Save();
                    Motion.Speed = v;
                }));
            }
            root.Children.Add(speed);

            var tip = UI.L("anim.tip", 12.5, sub: true, wrap: true);
            tip.Margin = new Thickness(2, 18, 0, 0);
            root.Children.Add(tip);
            return root;
        }

        /// <summary>Мини-окошко внутри плитки — на нём показываем, как выглядит анимация.</summary>
        static FrameworkElement DemoCard()
        {
            var card = new Border { Margin = new Thickness(22, 16, 22, 16), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
            card.SetResourceReference(Border.BackgroundProperty, "CardBg");
            card.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            var sp = new StackPanel { Margin = new Thickness(10, 9, 10, 9) };
            var dot = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), HorizontalAlignment = HorizontalAlignment.Left };
            dot.SetResourceReference(Border.BackgroundProperty, "Accent");
            sp.Children.Add(dot);
            for (int i = 0; i < 2; i++)
            {
                var line = new Border { Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 7, i == 1 ? 30 : 0, 0), Opacity = 0.5 };
                line.SetResourceReference(Border.BackgroundProperty, "SubText");
                sp.Children.Add(line);
            }
            card.Child = sp;
            return card;
        }

        static void Play(FrameworkElement demo, string style)
        {
            string was = Motion.Style;
            Motion.Style = style;
            if (style == "none")
            {
                Anim.Set(demo, UIElement.OpacityProperty, 0);
                Anim.To(demo, UIElement.OpacityProperty, 1, 1, delayMs: 180);   // просто появляется, без движения
            }
            else Motion.Enter(demo, 1);
            Motion.Style = was;
        }
    }
}
