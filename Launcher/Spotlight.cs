// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>
    /// Spotlight по Ctrl+Alt+Space: программы, файлы (Рабочий стол, Загрузки, Документы), калькулятор, курсы валют,
    /// разделы MizuEngine, поиск в интернете и вопрос к Muzule — в одном стеклянном окне.
    /// </summary>
    internal static class Spotlight
    {
        static SpotWindow _w;
        public static Settings S;

        public static void Init(Settings s) { S = s; }

        public static void Toggle()
        {
            if (S == null || !S.SpotlightOn) return;
            _w ??= new SpotWindow(S);
            if (_w.IsVisible && _w.IsActive) _w.HideSpot();
            else _w.ShowSpot();
        }
    }

    internal sealed class SpotItem
    {
        public string Group, Title, Sub, Glyph, Right;
        public ImageSource Icon;
        public AppEntry App;
        public string Path;          // файл или папка — Ctrl+Enter открывает папку с ним
        public Action Run;
        public (Color a, Color b) Tint = UI.Blue;
    }

    internal sealed class SpotWindow : Window
    {
        readonly Settings _s;
        readonly Grid _root;
        readonly Border _under;
        readonly TextBox _input;
        readonly TextBlock _ph;
        readonly StackPanel _list;
        readonly ScrollViewer _scroll;
        readonly Border _results;
        readonly TextBlock _hint;
        readonly DispatcherTimer _debounce;
        List<SpotItem> _items = new List<SpotItem>();
        readonly List<Border> _rows = new List<Border>();
        int _sel;
        bool _hiding;
        int _version;

        public SpotWindow(Settings s)
        {
            _s = s;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = 720;
            Title = "Spotlight";
            UseLayoutRounding = true;

            // Подложка как у быстрого окна Muzule: плотный слой + фон темы + стекло — читается на любом фоне
            _root = new Grid { Margin = new Thickness(30, 20, 30, 40), RenderTransformOrigin = new Point(0.5, 0.2), RenderTransform = new ScaleTransform(1, 1) };
            _under = new Border { CornerRadius = new CornerRadius(22), Effect = new DropShadowEffect { BlurRadius = 34, ShadowDepth = 8, Direction = 270, Opacity = 0.45, Color = Colors.Black } };
            _root.Children.Add(_under);
            var bg = new Border { CornerRadius = new CornerRadius(22) };
            bg.SetResourceReference(Border.BackgroundProperty, "WinBg");
            _root.Children.Add(bg);
            var card = new Border { CornerRadius = new CornerRadius(22), BorderThickness = new Thickness(1) };
            card.SetResourceReference(Border.BackgroundProperty, "CardBg");
            card.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            _root.Children.Add(card);

            var content = new StackPanel { Margin = new Thickness(18, 12, 18, 12) };
            _root.Children.Add(content);

            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition());
            var glass = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 20, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 2, 14, 0) };
            glass.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
            top.Children.Add(glass);
            var host = new Grid();
            Grid.SetColumn(host, 1);
            _input = new TextBox { FontSize = 22, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0, 6, 0, 6), FocusVisualStyle = null, MaxLength = 200 };
            _input.SetResourceReference(Control.ForegroundProperty, "Text");
            _input.SetResourceReference(TextBox.CaretBrushProperty, "Text");
            _input.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.SelectionBrushProperty, "Accent");
            _ph = new TextBlock { FontSize = 22, IsHitTestVisible = false, Margin = new Thickness(2, 6, 0, 0), Opacity = 0.55 };
            _ph.SetResourceReference(TextBlock.TextProperty, "spot.ph");
            _ph.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
            host.Children.Add(_input);
            host.Children.Add(_ph);
            top.Children.Add(host);
            content.Children.Add(top);

            _list = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 480, Content = _list, Focusable = false };
            SmoothScroll.Attach(_scroll);
            _results = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(0, 8, 0, 0), Child = _scroll, Visibility = Visibility.Collapsed };
            _results.SetResourceReference(Border.BorderBrushProperty, "Divider");
            content.Children.Add(_results);

            _hint = new TextBlock { FontSize = 11, Margin = new Thickness(0, 8, 0, 0), Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis, Visibility = Visibility.Collapsed };
            _hint.SetResourceReference(TextBlock.TextProperty, "spot.hint");
            _hint.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
            content.Children.Add(_hint);

            Content = _root;
            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
            _debounce.Tick += (o, e) => { _debounce.Stop(); _ = Search(); };
            _input.TextChanged += (o, e) =>
            {
                _ph.Visibility = _input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                _debounce.Stop();
                _debounce.Start();
            };
            PreviewKeyDown += OnKey;
            Deactivated += (o, e) => HideSpot();
            AppCatalog.Changed += () => { if (IsVisible && _input.Text.Length > 0) _ = Search(); };
        }

        // ================== показать / спрятать ==================

        public void ShowSpot()
        {
            _hiding = false;
            bool light = Application.Current.TryFindResource("Text") is SolidColorBrush tb && (0.299 * tb.Color.R + 0.587 * tb.Color.G + 0.114 * tb.Color.B) / 255 < 0.5;
            _under.Background = new SolidColorBrush(light ? Color.FromArgb(0xF8, 0xF7, 0xF9, 0xFC) : Color.FromArgb(0xF4, 0x12, 0x16, 0x20));
            _ = AppCatalog.Refresh();
            _ = FileIndex.Refresh();
            Place();
            _root.Opacity = 0;
            if (!IsVisible) Show();
            Activate();
            LauncherNative.ForceForeground(this);
            _input.Focus();
            Keyboard.Focus(_input);
            _input.SelectAll();
            var sc = (ScaleTransform)_root.RenderTransform;
            Anim.To(_root, UIElement.OpacityProperty, 1, Motion.Off ? 1 : 170, from: 0);
            Anim.To(sc, ScaleTransform.ScaleXProperty, 1, Motion.Off ? 1 : 300, from: 0.965, ease: Anim.Spring);
            Anim.To(sc, ScaleTransform.ScaleYProperty, 1, Motion.Off ? 1 : 300, from: 0.965, ease: Anim.Spring);
            if (_input.Text.Length > 0) _ = Search();
        }

        public void HideSpot()
        {
            if (!IsVisible || _hiding) return;
            _hiding = true;
            Anim.To(_root, UIElement.OpacityProperty, 0, Motion.Off ? 1 : 120, done: () =>
            {
                if (!_hiding) return;
                Hide();
                _hiding = false;
                _input.Text = "";   // в следующий раз — с чистого листа
            });
        }

        /// <summary>По центру монитора под курсором, на 18% от верха.</summary>
        void Place()
        {
            var mon = LauncherNative.MonitorUnderCursor();
            var src = PresentationSource.FromVisual(this);
            double k = src?.CompositionTarget != null ? src.CompositionTarget.TransformFromDevice.M11 : 96.0 / 96;
            if (src == null)
            {
                var area = SystemParameters.WorkArea;
                Left = area.Left + (area.Width - Width) / 2;
                Top = area.Top + area.Height * 0.18;
                return;
            }
            Left = mon.Left * k + ((mon.Right - mon.Left) * k - Width) / 2;
            Top = mon.Top * k + (mon.Bottom - mon.Top) * k * 0.18;
        }

        // ================== клавиши ==================

        void OnKey(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    e.Handled = true;
                    if (_input.Text.Length > 0) _input.Text = ""; else HideSpot();
                    break;
                case Key.Down:
                    e.Handled = true;
                    Select(_sel + 1);
                    break;
                case Key.Up:
                    e.Handled = true;
                    Select(_sel - 1);
                    break;
                case Key.Enter:
                    e.Handled = true;
                    if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) Reveal(Current); else Run(Current);
                    break;
            }
        }

        SpotItem Current => _sel >= 0 && _sel < _items.Count ? _items[_sel] : null;

        void Run(SpotItem it)
        {
            if (it?.Run == null) return;
            HideSpot();
            try { it.Run(); } catch (Exception e) { Settings.Log(e); }
        }

        void Reveal(SpotItem it)
        {
            if (it?.Path == null) { Run(it); return; }
            HideSpot();
            try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + it.Path + "\"") { UseShellExecute = true }); }
            catch (Exception e) { Settings.Log(e); }
        }

        // ================== поиск ==================

        async Task Search()
        {
            int ver = ++_version;
            string q = _input.Text.Trim();
            if (q.Length == 0) { Fill(new List<SpotItem>()); return; }
            var items = Build(q);
            Fill(items);

            // Курсы валют — из интернета (или сохранённые), поэтому дописываем, когда придут
            var cur = Currency.Parse(q);
            if (cur != null)
            {
                var (amount, from, targets) = cur.Value;
                var extra = new List<SpotItem>();
                foreach (var to in targets)
                {
                    double? v = await Currency.Convert(amount, from, to);
                    if (ver != _version) return;
                    if (v == null) continue;
                    string text = Currency.Format(v.Value, to);
                    extra.Add(new SpotItem
                    {
                        Group = "spot.g.currency", Title = text, Sub = Currency.Format(amount, from) + "  =  " + text + "  ·  " + Loc.T("spot.copy"),
                        Glyph = "", Tint = UI.Green, Run = () => Copy(v.Value.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture))
                    });
                }
                if (extra.Count > 0 && ver == _version)
                {
                    items.InsertRange(items.TakeWhile(i => i.Group == "spot.g.calc").Count(), extra);
                    Fill(items);
                }
            }

            // Файлы могли ещё индексироваться — когда список готов, обновим
            if (!FileIndex.Ready)
            {
                await FileIndex.Refresh();
                if (ver == _version) Fill(Build(q));
            }
        }

        List<SpotItem> Build(string q)
        {
            var res = new List<SpotItem>();

            // Калькулятор — первым, если это пример
            var calc = Calc.TryEval(q);
            if (calc != null)
            {
                string r = Calc.Format(calc.Value);
                res.Add(new SpotItem { Group = "spot.g.calc", Title = "= " + r, Sub = q + "  ·  " + Loc.T("spot.copy"), Glyph = "", Tint = UI.Orange, Run = () => Copy(r) });
            }

            // Программы
            var apps = AppCatalog.Apps.Select(a => (a, s: AppCatalog.Score(a.Name, q))).Where(x => x.s > 0).OrderByDescending(x => x.s).Take(7).ToList();
            foreach (var (a, _) in apps)
            {
                var app = a;
                var it = new SpotItem { Group = "spot.g.apps", Title = a.Name, Sub = Loc.T("spot.app"), App = a, Run = () => AppCatalog.Launch(app) };
                res.Add(it);
            }

            // Разделы MizuEngine
            foreach (var (nav, key, words) in Sections)
            {
                string title = Loc.T(key);
                if (AppCatalog.Score(title, q) > 0 || words.Split(' ').Any(w => w.StartsWith(q, StringComparison.CurrentCultureIgnoreCase)) || (q.Length >= 3 && words.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0))
                {
                    string n = nav;
                    res.Add(new SpotItem { Group = "spot.g.mizu", Title = title, Sub = "MizuEngine", Glyph = "", Tint = UI.Purple, Run = () => OpenNav(n) });
                    if (res.Count(i => i.Group == "spot.g.mizu") >= 4) break;
                }
            }

            // Файлы
            foreach (var f in FileIndex.Search(q, 8))
            {
                var file = f;
                res.Add(new SpotItem
                {
                    Group = "spot.g.files", Title = f.Name, Sub = ShortPath(f.Path), Path = f.Path,
                    Glyph = f.Dir ? "" : "", Tint = f.Dir ? UI.Orange : UI.Gray,
                    Right = f.Dir ? null : UI.Ago(f.Modified.ToLocalTime()),
                    Run = () => { try { Process.Start(new ProcessStartInfo(file.Path) { UseShellExecute = true }); } catch (Exception e) { Settings.Log(e); } }
                });
            }

            // Интернет и Muzule — всегда внизу
            res.Add(new SpotItem { Group = "spot.g.web", Title = string.Format(Loc.T("spot.web"), q), Sub = EngineName(), Glyph = "", Tint = UI.Blue, Run = () => Web(q) });
            res.Add(new SpotItem { Group = "spot.g.web", Title = string.Format(Loc.T("spot.muzule"), q), Sub = Plus.IsActive ? "Muzule" : "Muzule · Mizu Plus", Glyph = "", Tint = UI.Purple, Run = () => AskMuzule(q) });
            return res;
        }

        /// <summary>Разделы MizuEngine: пункт меню, название, слова для поиска (ru и en).</summary>
        static readonly (string nav, string key, string words)[] Sections =
        {
            ("NavHome", "nav.home", "главная home"),
            ("NavSnow", "nav.snow", "эффекты снег дождь сакура светлячки погода effects snow rain weather"),
            ("NavWe", "nav.we", "обои wallpaper engine steam"),
            ("NavDock", "nav.dock", "док панель задач taskbar dock трей tray заряд battery"),
            ("NavIsland", "spot.island", "dynamic island остров капсула музыка music"),
            ("NavCustom", "nav.custom", "кастомизация курсор fps виджеты рамки звуки горячие клавиши launchpad spotlight cursor widgets hotkeys sounds"),
            ("NavAnim", "nav.anim", "анимации animations"),
            ("NavPresets", "nav.presets", "сборки presets пресеты"),
            ("NavLiveBg", "nav.livebg", "живые фоны видео gif live backgrounds"),
            ("NavProfile", "nav.profile", "профиль аккаунт ссылки оформление profile account links"),
            ("NavFriends", "nav.friends", "друзья подписчики friends followers"),
            ("NavChats", "nav.chats", "чаты сообщения группы chats messages groups"),
            ("NavCommunity", "nav.community", "сообщество community посты posts"),
            ("NavLeaders", "nav.leaders", "лидеры топ leaders top"),
            ("NavPlus", "nav.plus", "mizu plus подписка subscription донат"),
            ("NavMuzule", "nav.muzule", "muzule claude ии ai агенты agents"),
            ("NavSettings", "nav.settings", "настройки тема язык автозапуск пароль settings theme language password"),
            ("NavUpdates", "nav.updates", "обновления updates версия version"),
            ("NavAuthor", "nav.author", "автор поддержка донат author support donate"),
            ("NavAbout", "nav.about", "о приложении about")
        };

        static void OpenNav(string nav) => Muzule.OpenPage(nav);

        static string ShortPath(string p)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string dir = System.IO.Path.GetDirectoryName(p) ?? "";
            if (dir.StartsWith(home, StringComparison.OrdinalIgnoreCase)) dir = "~" + dir.Substring(home.Length);
            return dir;
        }

        string EngineName() => _s.SpotlightEngine switch
        {
            "bing" => "Bing", "yandex" => "Яндекс", "baidu" => "Baidu", "duckduckgo" => "DuckDuckGo", _ => "Google"
        };

        void Web(string q)
        {
            string e = Uri.EscapeDataString(q);
            string url = _s.SpotlightEngine switch
            {
                "bing" => "https://www.bing.com/search?q=" + e,
                "yandex" => "https://yandex.ru/search/?text=" + e,
                "baidu" => "https://www.baidu.com/s?wd=" + e,
                "duckduckgo" => "https://duckduckgo.com/?q=" + e,
                _ => "https://www.google.com/search?q=" + e
            };
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception ex) { Settings.Log(ex); }
        }

        static void AskMuzule(string q)
        {
            if (!Muzule.Ready) { Muzule.OpenPage("NavMuzule"); return; }
            MuzuleSpotlight.Ask(q);
        }

        static void Copy(string text)
        {
            try { Clipboard.SetText(text); } catch { }
        }

        // ================== список ==================

        void Fill(List<SpotItem> items)
        {
            _items = items;
            _rows.Clear();
            _list.Children.Clear();
            string group = null;
            foreach (var it in items)
            {
                if (it.Group != group)
                {
                    group = it.Group;
                    var h = UI.L(group, 11.5, true, sub: true);
                    h.Margin = new Thickness(10, _list.Children.Count == 0 ? 2 : 10, 0, 4);
                    _list.Children.Add(h);
                }
                _list.Children.Add(Row(it, _rows.Count));
            }
            _results.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _hint.Visibility = _results.Visibility;
            Select(0);
        }

        Border Row(SpotItem it, int index)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            FrameworkElement icon;
            if (it.App != null)
            {
                var img = new Image { Width = 30, Height = 30 };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                img.Source = AppCatalog.Icon(it.App, i => img.Source = i);
                icon = img;
            }
            else icon = UI.Squircle(it.Glyph ?? "", it.Tint, 30);
            icon.Margin = new Thickness(0, 0, 12, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(icon);

            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var t = UI.T(it.Title, 14.5, true);
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            texts.Children.Add(t);
            if (!string.IsNullOrEmpty(it.Sub))
            {
                var s = UI.T(it.Sub, 12, sub: true);
                s.TextTrimming = TextTrimming.CharacterEllipsis;
                texts.Children.Add(s);
            }
            Grid.SetColumn(texts, 1);
            g.Children.Add(texts);
            if (!string.IsNullOrEmpty(it.Right))
            {
                var r = UI.T(it.Right, 11.5, sub: true);
                r.VerticalAlignment = VerticalAlignment.Center;
                r.Margin = new Thickness(10, 0, 4, 0);
                Grid.SetColumn(r, 2);
                g.Children.Add(r);
            }

            var row = new Border { CornerRadius = new CornerRadius(11), Padding = new Thickness(10, 7, 10, 7), Child = g, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            row.MouseEnter += (o, e) => Select(index, false);
            row.MouseLeftButtonUp += (o, e) => Run(it);
            _rows.Add(row);
            return row;
        }

        void Select(int i, bool scroll = true)
        {
            if (_rows.Count == 0) { _sel = -1; return; }
            i = Math.Clamp(i, 0, _rows.Count - 1);
            if (_sel >= 0 && _sel < _rows.Count) _rows[_sel].Background = Brushes.Transparent;
            _sel = i;
            _rows[i].SetResourceReference(Border.BackgroundProperty, "HoverBg");
            if (scroll) _rows[i].BringIntoView();
        }
    }
}
