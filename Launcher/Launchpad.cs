// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>
    /// Launchpad как на Mac: все программы из «Пуска» сеткой на весь экран поверх размытого рабочего стола.
    /// Открывается значком в доке, Ctrl+Alt+L или мышью в левый нижний угол. Значки можно переставлять,
    /// перетащил значок на значок — получилась папка. Порядок и папки сохраняются в settings.json.
    /// </summary>
    internal static class Launchpad
    {
        static LaunchpadWindow _w;
        public static Settings S;

        public static void Init(Settings s)
        {
            S = s;
            AppCatalog.Refresh();   // список программ готов заранее — Launchpad открывается сразу
        }

        public static bool IsOpen => _w != null && _w.IsVisible;

        public static void Toggle()
        {
            if (S == null) return;
            if (IsOpen) { _w.Close(true); return; }
            _w ??= new LaunchpadWindow(S);
            _w.Open();
        }
    }

    internal sealed class LaunchpadWindow : Window
    {
        const double IconSize = 74, CellW = 132, CellH = 124;

        readonly Settings _s;
        readonly Grid _root;
        readonly Image _bg;
        readonly Rectangle _tint;
        readonly Grid _content;
        readonly ScaleTransform _contentScale = new ScaleTransform(1, 1);
        readonly TextBox _search;
        readonly TextBlock _searchPh;
        readonly Grid _viewport;
        readonly Canvas _strip;
        readonly TranslateTransform _stripX = new TranslateTransform();
        readonly StackPanel _dots;
        readonly Canvas _overlay;   // призрак перетаскиваемого значка
        readonly Grid _folderLayer;

        readonly Dictionary<LaunchSlot, Tile> _tiles = new Dictionary<LaunchSlot, Tile>();
        List<LaunchSlot> _slots = new List<LaunchSlot>();
        int _page, _cols = 7, _rows = 5;
        bool _closing;

        public LaunchpadWindow(Settings s)
        {
            _s = s;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            Title = "Launchpad";
            UseLayoutRounding = true;
            FocusVisualStyle = null;

            _root = new Grid { Background = Brushes.Transparent, ClipToBounds = true };
            _bg = new Image { Stretch = Stretch.Fill, Effect = new BlurEffect { Radius = 26, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance }, Margin = new Thickness(-40) };
            RenderOptions.SetBitmapScalingMode(_bg, BitmapScalingMode.HighQuality);
            _tint = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(0x80, 0x08, 0x0C, 0x16)) };
            _root.Children.Add(_bg);
            _root.Children.Add(_tint);

            _content = new Grid { RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _contentScale };
            _content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _content.RowDefinitions.Add(new RowDefinition());
            _content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _root.Children.Add(_content);

            // Поиск
            var searchBox = new Border
            {
                Width = 280, Height = 34, CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 46, 0, 18), Background = new SolidColorBrush(Color.FromArgb(0x38, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)), BorderThickness = new Thickness(1)
            };
            var sg = new Grid();
            var glass = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 13, Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 1, 0, 0) };
            _search = new TextBox
            {
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.White, CaretBrush = Brushes.White,
                FontSize = 14, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(32, 0, 10, 0), FocusVisualStyle = null, MaxLength = 80
            };
            _search.SelectionBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x4D, 0xA8, 0xFF));
            _searchPh = new TextBlock { FontSize = 14, Foreground = new SolidColorBrush(Color.FromArgb(0x99, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(35, 0, 0, 1), IsHitTestVisible = false };
            _searchPh.SetResourceReference(TextBlock.TextProperty, "lp.search");
            sg.Children.Add(glass);
            sg.Children.Add(_search);
            sg.Children.Add(_searchPh);
            searchBox.Child = sg;
            _content.Children.Add(searchBox);
            _search.TextChanged += (o, e) => { _searchPh.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Relayout(true); };

            // Страницы со значками
            _viewport = new Grid { ClipToBounds = true, Background = Brushes.Transparent };
            Grid.SetRow(_viewport, 1);
            _strip = new Canvas { RenderTransform = _stripX };
            _viewport.Children.Add(_strip);
            _content.Children.Add(_viewport);
            _viewport.SizeChanged += (o, e) => Relayout(false);

            _dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 40), Height = 10 };
            Grid.SetRow(_dots, 2);
            _content.Children.Add(_dots);

            _folderLayer = new Grid { Visibility = Visibility.Collapsed, Background = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)) };
            _root.Children.Add(_folderLayer);
            _overlay = new Canvas { IsHitTestVisible = false };
            _root.Children.Add(_overlay);

            Content = _root;

            PreviewKeyDown += OnKey;
            PreviewMouseWheel += OnWheel;
            _root.MouseLeftButtonUp += OnBackgroundClick;
            Deactivated += (o, e) => { if (_drag == null) Close(false); };
            AppCatalog.Changed += () => { if (IsVisible) { Sync(); Relayout(false); } };

            _dragTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(50) };
            _dragTimer.Tick += (o, e) => DragTick();
        }

        // ================== открыть / закрыть ==================

        public void Open()
        {
            _closing = false;
            var mon = LauncherNative.MonitorUnderCursor();
            _bg.Source = LauncherNative.Capture(mon);   // снимок до показа окна — в нём нет самого Launchpad
            var h = new WindowInteropHelper(this).EnsureHandle();
            SetWindowPos(h, new IntPtr(-1), mon.Left, mon.Top, mon.Right - mon.Left, mon.Bottom - mon.Top, 0x0010);   // поверх всех, без активации
            _search.Text = "";
            _page = 0;
            CloseFolder(false);
            Sync();
            if (!IsVisible) Show();
            // Монитор с другим масштабом мог подправить размер — ставим ровно на весь монитор ещё раз
            SetWindowPos(h, new IntPtr(-1), mon.Left, mon.Top, mon.Right - mon.Left, mon.Bottom - mon.Top, 0x0010);
            Activate();
            LauncherNative.ForceForeground(this);
            _search.Focus();
            Keyboard.Focus(_search);
            Relayout(false);
            _ = AppCatalog.Refresh();

            bool off = Motion.Off;
            Anim.To(_root, UIElement.OpacityProperty, 1, off ? 1 : 200, from: 0);
            Anim.To(_contentScale, ScaleTransform.ScaleXProperty, 1, off ? 1 : 300, from: 1.07);
            Anim.To(_contentScale, ScaleTransform.ScaleYProperty, 1, off ? 1 : 300, from: 1.07);
        }

        /// <summary>Спрятать (окно не удаляется — следующее открытие мгновенное). instant — без анимации.</summary>
        public void Close(bool animate)
        {
            if (!IsVisible || _closing) return;
            _closing = true;
            EndDrag(false);
            void Done()
            {
                if (!_closing) return;
                Hide();
                _bg.Source = null;   // снимок экрана не держим в памяти
                _closing = false;
            }
            if (!animate || Motion.Off) { Done(); return; }
            Anim.To(_contentScale, ScaleTransform.ScaleXProperty, 1.06, 170);
            Anim.To(_contentScale, ScaleTransform.ScaleYProperty, 1.06, 170);
            Anim.To(_root, UIElement.OpacityProperty, 0, 170, done: Done);
        }

        void Launch(AppEntry a)
        {
            if (a == null) return;
            AppCatalog.Launch(a);
            Close(true);
        }

        // ================== раскладка ==================

        /// <summary>Порядок из настроек + новые программы в конец; удалённые программы убираются, папки из 1 значка распадаются.</summary>
        void Sync()
        {
            var apps = AppCatalog.Apps;
            var known = new HashSet<string>(apps.Select(a => a.Id), StringComparer.OrdinalIgnoreCase);
            bool prune = apps.Count > 0;
            var list = new List<LaunchSlot>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sl in _s.LaunchpadLayout)
            {
                if (sl.IsFolder)
                {
                    var inside = sl.Apps.Where(id => (!prune || known.Contains(id)) && used.Add(id)).ToList();
                    if (inside.Count >= 2) { sl.Apps = inside; list.Add(sl); }
                    else if (inside.Count == 1) list.Add(new LaunchSlot { App = inside[0] });
                }
                else if (!string.IsNullOrEmpty(sl.App) && (!prune || known.Contains(sl.App)) && used.Add(sl.App)) list.Add(sl);
            }
            // Первый запуск: служебные программы Windows (ODBC, iSCSI, «Службы»…) — в папку в конце, как «Другие» на Mac
            bool fresh = _s.LaunchpadLayout.Count == 0;
            var tools = new List<string>();
            foreach (var a in apps)
            {
                if (!used.Add(a.Id)) continue;
                if (fresh && IsWindowsTool(a.Id)) tools.Add(a.Id);
                else list.Add(new LaunchSlot { App = a.Id });
            }
            if (tools.Count >= 2) list.Add(new LaunchSlot { Folder = Loc.T("lp.tools"), Apps = tools });
            else if (tools.Count == 1) list.Add(new LaunchSlot { App = tools[0] });
            _slots = list;
            if (prune) Save();
        }

        // Папки System32, SysWOW64 и Windows в именах shell:AppsFolder
        static readonly string[] SystemFolders = { "{1ac14e77-02e7-4e5d-b744-2eb1ae5198b7}", "{d65231b0-b2f1-4857-a4ce-a8e7c6ea7d27}", "{f38bf404-1d43-42f2-9305-67de0b28fc23}" };
        // Эти системные программы нужны часто — их оставляем снаружи
        static readonly string[] KeepOutside = { "taskmgr.exe", "cmd.exe", "control.exe", "explorer.exe", "mspaint.exe", "notepad.exe", "snippingtool.exe", "calc.exe", "wt.exe" };

        static bool IsWindowsTool(string id)
        {
            string low = (id ?? "").ToLowerInvariant();
            if (!SystemFolders.Any(f => low.StartsWith(f)) && !low.Contains("administrative tools") && !low.Contains("windows tools")) return false;
            return !KeepOutside.Any(x => low.EndsWith("\\" + x));
        }

        void Save()
        {
            _s.LaunchpadLayout = _slots.Select(x => x).ToList();
            _s.Save();
        }

        int PerPage => _cols * _rows;
        double W => Math.Max(1, _viewport.ActualWidth);
        double H => Math.Max(1, _viewport.ActualHeight);
        double Left0 => (W - _cols * CellW) / 2;
        double Top0 => Math.Max(0, (H - _rows * CellH) / 2);
        bool Searching => _search.Text.Trim().Length > 0;

        /// <summary>Что показывать: при поиске — подходящие программы (и из папок) одной страницей, иначе — все места.</summary>
        List<LaunchSlot> Visible()
        {
            if (!Searching) return _slots;
            string q = _search.Text.Trim();
            var hits = new List<(LaunchSlot slot, int score)>();
            foreach (var a in AppCatalog.Apps)
            {
                int sc = AppCatalog.Score(a.Name, q);
                if (sc > 0) hits.Add((SlotFor(a.Id), sc));
            }
            return hits.OrderByDescending(h => h.score).Take(PerPage).Select(h => h.slot).ToList();
        }

        // При поиске программа может лежать в папке — для неё отдельное (временное) место
        readonly Dictionary<string, LaunchSlot> _searchSlots = new Dictionary<string, LaunchSlot>(StringComparer.OrdinalIgnoreCase);
        LaunchSlot SlotFor(string id)
        {
            var own = _slots.FirstOrDefault(x => !x.IsFolder && string.Equals(x.App, id, StringComparison.OrdinalIgnoreCase));
            if (own != null) return own;
            if (!_searchSlots.TryGetValue(id, out var sl)) _searchSlots[id] = sl = new LaunchSlot { App = id };
            return sl;
        }

        void Relayout(bool animate)
        {
            if (!IsLoaded && !IsVisible) return;
            _cols = Math.Clamp((int)((W - 80) / CellW), 4, 8);
            _rows = Math.Clamp((int)(H / CellH), 2, 6);
            var vis = Visible();
            int pages = Math.Max(1, (vis.Count + PerPage - 1) / PerPage);
            if (_drag != null && !Searching) pages = Math.Max(pages, (vis.Count + PerPage) / PerPage);   // при перетаскивании можно унести на новую страницу
            if (Searching) _page = 0;
            _page = Math.Clamp(_page, 0, pages - 1);

            var shown = new HashSet<LaunchSlot>(vis);
            foreach (var kv in _tiles.ToList())
                if (!shown.Contains(kv.Key)) { _strip.Children.Remove(kv.Value); _tiles.Remove(kv.Key); }

            for (int i = 0; i < vis.Count; i++)
            {
                var slot = vis[i];
                if (!_tiles.TryGetValue(slot, out var tile))
                {
                    tile = new Tile(this, slot);
                    _tiles[slot] = tile;
                    _strip.Children.Add(tile);
                    tile.Place(Pos(i), false);
                    continue;
                }
                tile.Refresh();
                tile.Place(Pos(i), animate);
            }
            ScrollTo(_page, false);
            Dots(pages);
        }

        Point Pos(int i)
        {
            int p = i / PerPage, k = i % PerPage;
            return new Point(p * W + Left0 + (k % _cols) * CellW, Top0 + (k / _cols) * CellH);
        }

        void ScrollTo(int page, bool animate)
        {
            _page = page;
            double x = -page * W;
            if (animate && !Motion.Off)
            {
                var a = new DoubleAnimation(x, TimeSpan.FromMilliseconds(380)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                _stripX.BeginAnimation(TranslateTransform.XProperty, a);
            }
            else { _stripX.BeginAnimation(TranslateTransform.XProperty, null); _stripX.X = x; }
            for (int i = 0; i < _dots.Children.Count; i++)
                ((Ellipse)_dots.Children[i]).Fill = new SolidColorBrush(Color.FromArgb(i == page ? (byte)0xF0 : (byte)0x60, 255, 255, 255));
        }

        void Dots(int pages)
        {
            _dots.Children.Clear();
            if (pages <= 1) return;
            for (int i = 0; i < pages; i++)
            {
                int p = i;
                var d = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(5, 0, 5, 0), Cursor = Cursors.Hand };
                d.MouseLeftButtonUp += (o, e) => { e.Handled = true; ScrollTo(p, true); };
                _dots.Children.Add(d);
            }
            ScrollTo(_page, false);
        }

        int Pages => Math.Max(1, _dots.Children.Count);

        void Page(int delta)
        {
            int next = Math.Clamp(_page + delta, 0, Pages - 1);
            if (next != _page) ScrollTo(next, true);
        }

        // ================== клавиши, колесо, клик мимо ==================

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                if (_folderSlot != null) CloseFolder(true);
                else if (Searching) _search.Text = "";
                else Close(true);
                return;
            }
            if (e.Key == Key.Enter && _folderSlot == null)
            {
                e.Handled = true;
                var first = Visible().FirstOrDefault();
                if (first == null) return;
                if (first.IsFolder) OpenFolder(first); else Launch(AppCatalog.Find(first.App));
                return;
            }
            if (!Searching && _folderSlot == null)
            {
                if (e.Key == Key.Right || e.Key == Key.PageDown) { e.Handled = true; Page(1); }
                else if (e.Key == Key.Left || e.Key == Key.PageUp) { e.Handled = true; Page(-1); }
            }
        }

        DateTime _wheelAt;
        void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (_folderSlot != null || (DateTime.UtcNow - _wheelAt).TotalMilliseconds < 380) return;
            _wheelAt = DateTime.UtcNow;
            Page(e.Delta < 0 ? 1 : -1);
        }

        void OnBackgroundClick(object sender, MouseButtonEventArgs e)
        {
            if (_drag != null || _pressed != null) return;
            if (e.OriginalSource == _root || e.OriginalSource == _bg || e.OriginalSource == _tint || e.OriginalSource == _viewport || e.OriginalSource == _strip || e.OriginalSource == _content)
                Close(true);
            else if (e.OriginalSource == _folderLayer) CloseFolder(true);
        }

        // ================== значок ==================

        sealed class Tile : Grid
        {
            readonly LaunchpadWindow _lp;
            public readonly LaunchSlot Slot;
            readonly TranslateTransform _pos = new TranslateTransform();
            readonly ScaleTransform _scale = new ScaleTransform(1, 1);
            readonly Border _icon;
            readonly TextBlock _label;
            public Point At;

            public Tile(LaunchpadWindow lp, LaunchSlot slot)
            {
                _lp = lp;
                Slot = slot;
                Width = CellW;
                Height = CellH;
                Background = Brushes.Transparent;
                RenderTransform = _pos;
                _icon = new Border { Width = IconSize, Height = IconSize, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 0, 0), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _scale };
                _label = new TextBlock
                {
                    Foreground = Brushes.White, FontSize = 12.5, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6, IconSize + 15, 6, 0), MaxHeight = 34
                };
                Children.Add(_icon);
                Children.Add(_label);
                Refresh();
                MouseLeftButtonDown += (o, e) => { e.Handled = true; _lp.Press(this, e.GetPosition(_lp._root)); };
                MouseEnter += (o, e) => { if (_lp._drag == null) Zoom(1.06); };
                MouseLeave += (o, e) => { if (_lp._mergeTarget != this) Zoom(1); };
            }

            public void Zoom(double k)
            {
                Anim.To(_scale, ScaleTransform.ScaleXProperty, k, 160);
                Anim.To(_scale, ScaleTransform.ScaleYProperty, k, 160);
            }

            public void Refresh()
            {
                if (Slot.IsFolder)
                {
                    _label.Text = string.IsNullOrWhiteSpace(Slot.Folder) ? Loc.T("lp.folder") : Slot.Folder;
                    var box = new Border { CornerRadius = new CornerRadius(IconSize * 0.24), Background = new SolidColorBrush(Color.FromArgb(0x3A, 255, 255, 255)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 255, 255, 255)), BorderThickness = new Thickness(1), Padding = new Thickness(8) };
                    var grid = new UniformGrid { Columns = 3, Rows = 3 };
                    foreach (var id in Slot.Apps.Take(9))
                    {
                        var img = new Image { Margin = new Thickness(1.5) };
                        var app = AppCatalog.Find(id);
                        if (app != null) img.Source = AppCatalog.Icon(app, i => img.Source = i);
                        grid.Children.Add(img);
                    }
                    box.Child = grid;
                    _icon.Child = box;
                    ToolTip = null;
                }
                else
                {
                    var app = AppCatalog.Find(Slot.App);
                    _label.Text = app?.Name ?? "";
                    if (_icon.Child is not Image img)
                    {
                        img = new Image { Opacity = 0 };
                        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                        _icon.Child = img;
                        if (app != null)
                        {
                            var ready = AppCatalog.Icon(app, i => { img.Source = i; Anim.To(img, UIElement.OpacityProperty, 1, 180); });
                            if (ready != null) { img.Source = ready; img.Opacity = 1; }
                        }
                    }
                }
            }

            public void Place(Point p, bool animate)
            {
                At = p;
                if (animate && !Motion.Off)
                {
                    var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                    _pos.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(p.X, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
                    _pos.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(p.Y, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
                }
                else
                {
                    _pos.BeginAnimation(TranslateTransform.XProperty, null);
                    _pos.BeginAnimation(TranslateTransform.YProperty, null);
                    _pos.X = p.X; _pos.Y = p.Y;
                }
            }

            /// <summary>Картинка значка для «призрака» при перетаскивании.</summary>
            public UIElement Snapshot() => new Rectangle { Width = IconSize, Height = IconSize, Fill = new VisualBrush(_icon) { Stretch = Stretch.None } };
        }

        // ================== нажатие, перетаскивание, папки ==================

        Tile _pressed;
        Point _pressAt;
        DragState _drag;
        Tile _mergeTarget;
        readonly DispatcherTimer _dragTimer;

        sealed class DragState
        {
            public LaunchSlot Slot;
            public Tile Tile;
            public UIElement Ghost;
            public Point Grab;             // где схватили значок (относительно его левого верхнего угла)
            public int HoverIndex = -1;
            public DateTime HoverSince;
            public DateTime EdgeSince;
            public int EdgeDir;
        }

        void Press(Tile t, Point at)
        {
            _pressed = t;
            _pressAt = at;
            t.CaptureMouse();
            t.MouseMove += PressedMove;
            t.MouseLeftButtonUp += PressedUp;
            t.LostMouseCapture += PressedLost;
        }

        void Unhook(Tile t)
        {
            t.MouseMove -= PressedMove;
            t.MouseLeftButtonUp -= PressedUp;
            t.LostMouseCapture -= PressedLost;
        }

        void PressedMove(object sender, MouseEventArgs e)
        {
            if (_pressed == null) return;
            var p = e.GetPosition(_root);
            if (_drag == null && !Searching && (Math.Abs(p.X - _pressAt.X) > 7 || Math.Abs(p.Y - _pressAt.Y) > 7))
                BeginDrag(_pressed, e.GetPosition(_pressed));
            if (_drag != null) DragMove(p);
        }

        void PressedUp(object sender, MouseButtonEventArgs e)
        {
            var t = _pressed;
            _pressed = null;
            if (t == null) return;
            Unhook(t);
            t.ReleaseMouseCapture();
            e.Handled = true;
            if (_drag != null) { EndDrag(true); return; }
            if (t.Slot.IsFolder) OpenFolder(t.Slot);
            else Launch(AppCatalog.Find(t.Slot.App));
        }

        void PressedLost(object sender, MouseEventArgs e)
        {
            if (_pressed == null) return;
            var t = _pressed;
            _pressed = null;
            Unhook(t);
            if (_drag != null) EndDrag(true);
        }

        void BeginDrag(Tile t, Point grabInTile)
        {
            var ghost = t.Snapshot();
            ghost.Opacity = 0.92;
            ghost.RenderTransform = new ScaleTransform(1.1, 1.1, IconSize / 2, IconSize / 2);
            _overlay.Children.Add(ghost);
            _drag = new DragState
            {
                Slot = t.Slot, Tile = t, Ghost = ghost,
                Grab = new Point(grabInTile.X - (CellW - IconSize) / 2, grabInTile.Y - 8)
            };
            t.Opacity = 0;
            _dragTimer.Start();
        }

        void DragMove(Point p)
        {
            Canvas.SetLeft(_drag.Ghost, p.X - _drag.Grab.X);
            Canvas.SetTop(_drag.Ghost, p.Y - _drag.Grab.Y);
        }

        /// <summary>Каждые 50 мс: что под курсором — переставить, сделать папку или листнуть страницу.</summary>
        void DragTick()
        {
            if (_drag == null) { _dragTimer.Stop(); return; }
            var p = Mouse.GetPosition(_viewport);

            // Край экрана — следующая/предыдущая страница
            int edge = p.X < 50 ? -1 : p.X > W - 50 ? 1 : 0;
            if (edge != _drag.EdgeDir) { _drag.EdgeDir = edge; _drag.EdgeSince = DateTime.UtcNow; }
            else if (edge != 0 && (DateTime.UtcNow - _drag.EdgeSince).TotalMilliseconds > 650)
            {
                _drag.EdgeSince = DateTime.UtcNow;
                int next = Math.Clamp(_page + edge, 0, Math.Max(0, (_slots.Count + PerPage - 1) / PerPage));
                if (next != _page) { Relayout(false); ScrollTo(next, true); }
                return;
            }

            // Ячейка под курсором
            int col = (int)Math.Floor((p.X - Left0) / CellW), row = (int)Math.Floor((p.Y - Top0) / CellH);
            if (col < 0 || col >= _cols || row < 0 || row >= _rows) { SetMerge(null); return; }
            int index = Math.Min(_page * PerPage + row * _cols + col, _slots.Count - 1);
            var target = index >= 0 && index < _slots.Count ? _slots[index] : null;
            int from = _slots.IndexOf(_drag.Slot);

            // Близко к центру чужого значка — папка (папку в папку не кладём)
            if (target != null && target != _drag.Slot && !_drag.Slot.IsFolder && _tiles.TryGetValue(target, out var tt))
            {
                var c = new Point(Left0 + col * CellW + CellW / 2, Top0 + row * CellH + 8 + IconSize / 2);
                if ((p - c).Length < IconSize * 0.36)
                {
                    if (_drag.HoverIndex != -2 - index) { _drag.HoverIndex = -2 - index; _drag.HoverSince = DateTime.UtcNow; }
                    else if ((DateTime.UtcNow - _drag.HoverSince).TotalMilliseconds > 280) SetMerge(tt);
                    return;
                }
            }
            SetMerge(null);
            if (index < 0 || index == from) { _drag.HoverIndex = index; return; }
            if (_drag.HoverIndex != index) { _drag.HoverIndex = index; _drag.HoverSince = DateTime.UtcNow; return; }
            if ((DateTime.UtcNow - _drag.HoverSince).TotalMilliseconds < 140) return;
            // Переставляем
            _slots.RemoveAt(from);
            _slots.Insert(Math.Min(index, _slots.Count), _drag.Slot);
            _drag.HoverIndex = -1;
            Relayout(true);
        }

        void SetMerge(Tile t)
        {
            if (_mergeTarget == t) return;
            _mergeTarget?.Zoom(1);
            _mergeTarget = t;
            _mergeTarget?.Zoom(1.18);
        }

        void EndDrag(bool drop)
        {
            _dragTimer.Stop();
            var d = _drag;
            if (d == null) return;
            _drag = null;
            _overlay.Children.Remove(d.Ghost);
            d.Tile.Opacity = 1;
            var merge = _mergeTarget;
            SetMerge(null);
            if (drop && merge != null && merge.Slot != d.Slot && !d.Slot.IsFolder)
            {
                var target = merge.Slot;
                if (target.IsFolder) target.Apps.Add(d.Slot.App);
                else
                {
                    // Значок на значок — новая папка на месте того, на который бросили
                    int at = _slots.IndexOf(target);
                    var folder = new LaunchSlot { Folder = Loc.T("lp.folder"), Apps = new List<string> { target.App, d.Slot.App } };
                    _slots[at] = folder;
                }
                _slots.Remove(d.Slot);
                if (_tiles.TryGetValue(target, out var oldTile)) oldTile.Refresh();
            }
            Save();
            Relayout(true);
        }

        // ================== папка ==================

        LaunchSlot _folderSlot;

        void OpenFolder(LaunchSlot folder)
        {
            _folderSlot = folder;
            _folderLayer.Children.Clear();
            var panel = new Border
            {
                CornerRadius = new CornerRadius(30), Background = new SolidColorBrush(Color.FromArgb(0xE8, 0x26, 0x2B, 0x38)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 255, 255, 255)), BorderThickness = new Thickness(1),
                Padding = new Thickness(26, 18, 26, 22), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1)
            };
            var sp = new StackPanel();
            var name = new TextBox
            {
                Text = string.IsNullOrWhiteSpace(folder.Folder) ? Loc.T("lp.folder") : folder.Folder, FontSize = 22, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White, CaretBrush = Brushes.White, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 200,
                MaxLength = 40, Margin = new Thickness(0, 0, 0, 14), FocusVisualStyle = null
            };
            name.TextChanged += (o, e) =>
            {
                folder.Folder = name.Text.Trim();
                if (_tiles.TryGetValue(folder, out var t)) t.Refresh();
            };
            name.LostKeyboardFocus += (o, e) => Save();
            name.KeyDown += (o, e) => { if (e.Key == Key.Enter) { e.Handled = true; _search.Focus(); } };
            sp.Children.Add(name);

            int cols = Math.Clamp((int)Math.Ceiling(Math.Sqrt(folder.Apps.Count)), 3, 5);
            var grid = new WrapPanel { Width = cols * CellW };
            foreach (var id in folder.Apps.ToList())
            {
                var app = AppCatalog.Find(id);
                if (app == null) continue;
                var cell = new Grid { Width = CellW, Height = CellH, Background = Brushes.Transparent, Cursor = Cursors.Hand };
                var img = new Image { Width = IconSize, Height = IconSize, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 0, 0) };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                img.Source = AppCatalog.Icon(app, i => img.Source = i);
                var label = new TextBlock { Text = app.Name, Foreground = Brushes.White, FontSize = 12.5, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6, IconSize + 15, 6, 0) };
                cell.Children.Add(img);
                cell.Children.Add(label);
                Point down = default;
                bool pressed = false;
                cell.MouseLeftButtonDown += (o, e) => { e.Handled = true; pressed = true; down = e.GetPosition(_root); cell.CaptureMouse(); };
                cell.MouseMove += (o, e) =>
                {
                    if (!pressed) return;
                    // Унесли значок за край папки — он выходит из папки и дальше перетаскивается по Launchpad
                    var p = e.GetPosition(panel);
                    if (p.X < -10 || p.Y < -10 || p.X > panel.ActualWidth + 10 || p.Y > panel.ActualHeight + 10)
                    {
                        pressed = false;
                        cell.ReleaseMouseCapture();
                        var slot = TakeOut(folder, id);
                        CloseFolder(false);
                        Relayout(false);
                        if (slot != null && _tiles.TryGetValue(slot, out var tile) && Mouse.LeftButton == MouseButtonState.Pressed)
                        {
                            // Сразу продолжаем тащить его по Launchpad
                            var now = e.GetPosition(_root);
                            Press(tile, now);
                            BeginDrag(tile, new Point(CellW / 2, 8 + IconSize / 2));
                            DragMove(now);
                        }
                    }
                };
                cell.MouseLeftButtonUp += (o, e) =>
                {
                    if (!pressed) return;
                    pressed = false;
                    cell.ReleaseMouseCapture();
                    e.Handled = true;
                    if ((e.GetPosition(_root) - down).Length < 8) Launch(app);
                };
                var menu = new ContextMenu();
                var out1 = new MenuItem { Header = Loc.T("lp.take_out") };
                out1.Click += (o, e) => { TakeOut(folder, id); CloseFolder(false); Relayout(true); };
                menu.Items.Add(out1);
                cell.ContextMenu = menu;
                grid.Children.Add(cell);
            }
            sp.Children.Add(grid);
            panel.Child = sp;
            _folderLayer.Children.Add(panel);
            _folderLayer.Visibility = Visibility.Visible;
            var st = (ScaleTransform)panel.RenderTransform;
            Anim.To(_folderLayer, UIElement.OpacityProperty, 1, Motion.Off ? 1 : 180, from: 0);
            Anim.To(st, ScaleTransform.ScaleXProperty, 1, Motion.Off ? 1 : 260, from: 0.86, ease: Anim.Spring);
            Anim.To(st, ScaleTransform.ScaleYProperty, 1, Motion.Off ? 1 : 260, from: 0.86, ease: Anim.Spring);
            Anim.To(_contentScale, ScaleTransform.ScaleXProperty, 0.97, 220);
            Anim.To(_contentScale, ScaleTransform.ScaleYProperty, 0.97, 220);
            Anim.To(_content, UIElement.OpacityProperty, 0.18, 200);   // сетка за папкой гаснет — читается только папка
        }

        /// <summary>Вынуть программу из папки: она встаёт сразу после папки. Папка из одного значка распадается.</summary>
        LaunchSlot TakeOut(LaunchSlot folder, string id)
        {
            int at = _slots.IndexOf(folder);
            if (at < 0) return null;
            folder.Apps.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
            var slot = new LaunchSlot { App = id };
            if (folder.Apps.Count == 1) { _slots[at] = new LaunchSlot { App = folder.Apps[0] }; }
            else if (folder.Apps.Count == 0) { _slots.RemoveAt(at); at--; }
            _slots.Insert(Math.Clamp(at + 1, 0, _slots.Count), slot);
            if (_tiles.TryGetValue(folder, out var t)) t.Refresh();
            Save();
            return slot;
        }

        void CloseFolder(bool animate)
        {
            if (_folderSlot == null && _folderLayer.Visibility != Visibility.Visible) return;
            _folderSlot = null;
            Save();
            void Done() { if (_folderSlot == null) { _folderLayer.Visibility = Visibility.Collapsed; _folderLayer.Children.Clear(); } }
            Anim.To(_contentScale, ScaleTransform.ScaleXProperty, 1, 220);
            Anim.To(_contentScale, ScaleTransform.ScaleYProperty, 1, 220);
            Anim.To(_content, UIElement.OpacityProperty, 1, 200);
            if (animate && !Motion.Off) Anim.To(_folderLayer, UIElement.OpacityProperty, 0, 150, done: Done);
            else Done();
            _search.Focus();
        }

        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }
}
