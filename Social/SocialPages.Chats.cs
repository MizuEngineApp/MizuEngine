// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Ellipse = System.Windows.Shapes.Ellipse;
using Shape = System.Windows.Shapes.Shape;

namespace MizuEngine
{
    /// <summary>Чаты: личные сообщения, группы, приглашения от бота MizuEngine.</summary>
    internal sealed partial class SocialPages
    {
        long _groupOpen;                                  // открытая группа (0 — открыт личный чат или ничего)
        Border _chatPanel;                                // правая часть страницы «Чаты»
        HashSet<string> _friendIds = new HashSet<string>();
        HashSet<string> _requestIds = new HashSet<string>();
        /// <summary>Можно ли писать человеку (не другу): ответ сервера, чтобы не спрашивать при каждом открытии чата.</summary>
        readonly Dictionary<string, (bool ok, string dmFrom)> _dmCache = new Dictionary<string, (bool, string)>();
        readonly Dictionary<string, Profile> _groupPeople = new Dictionary<string, Profile>();
        DateTime _cooldownUntil;                          // медленный режим в группах: следующее сообщение не раньше
        const int SlowModeSeconds = 5;

        /// <summary>Своё сообщение, которое уже показано в ленте, но сервер его ещё не подтвердил (часики).</summary>
        sealed class Pending { public string Body; public FrameworkElement Bubble; public TextBlock Status; public Panel Feed; }
        readonly List<Pending> _pending = new List<Pending>();
        bool _historyLoaded;                              // первая загрузка переписки уже была (её не анимируем)
        const string IconClock = "\uE823", IconCheck = "\uE73E";
        // Мои личные сообщения, которые собеседник ещё не прочитал: номер → первая галочка (вторая лежит в её Tag)
        readonly Dictionary<long, TextBlock> _unreadTicks = new Dictionary<long, TextBlock>();

        bool IsStaff => SocialApi.MyProfile?.CanPost == true;

        DateTime _feedDay;                                // день последнего сообщения в ленте (плашки «Сегодня», «Вчера», дата)
        TextBox _inputBox;                                // поле сообщения открытой переписки («Отправить сообщение» в панели справа)

        /// <summary>Плашка дня по центру ленты (как на макете): «Сегодня», «Вчера» или дата.</summary>
        void DayMark(Panel feed, DateTime when, bool animate)
        {
            if (feed == null || when == default || when.Date == _feedDay) return;
            _feedDay = when.Date;
            var g = new Grid { Margin = new Thickness(0, 10, 0, 10) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            Border Line(bool leftSide)
            {
                var c = (UI.Owner.TryFindResource("Divider") as SolidColorBrush)?.Color ?? Color.FromArgb(0x30, 255, 255, 255);
                var c0 = Color.FromArgb(0, c.R, c.G, c.B);
                var c1 = Color.FromArgb((byte)Math.Min(255, c.A * 2), c.R, c.G, c.B);
                return new Border
                {
                    Height = 1, VerticalAlignment = VerticalAlignment.Center, Margin = leftSide ? new Thickness(40, 0, 12, 0) : new Thickness(12, 0, 40, 0),
                    Background = new LinearGradientBrush(leftSide ? c0 : c1, leftSide ? c1 : c0, 0)
                };
            }
            g.Children.Add(Line(true));
            var today = DateTime.Now.Date;
            string text = when.Date == today ? Loc.T("chats.today") : when.Date == today.AddDays(-1) ? Loc.T("chats.yesterday")
                : when.ToString(when.Year == today.Year ? "d MMMM" : "d MMMM yyyy", Loc.Culture);
            var pill = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Padding = new Thickness(18, 6, 18, 7), Child = UI.T(text, 12.5, true) };
            pill.SetResourceReference(Border.BackgroundProperty, "CardBg");
            pill.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            Grid.SetColumn(pill, 1);
            g.Children.Add(pill);
            var right = Line(false);
            Grid.SetColumn(right, 2);
            g.Children.Add(right);
            feed.Children.Add(g);
            if (!Motion.Off) Anim.To(g, UIElement.OpacityProperty, 1, Motion.Ms(animate ? 300 : 260), from: 0);
        }

        // Тема открытого чата: фон и цвет своих сообщений (видят все участники)
        ChatTheme _theme = new ChatTheme();
        Grid _chatRoot;                                   // переписка вместе со слоем фона
        Border _themeLayer;
        int _themePoll;                                   // тему проверяем не каждый опрос, а примерно раз в 15 секунд
        Func<Task<ChatTheme>> _themeSource;

        /// <summary>Какой чат сейчас на экране: "u:&lt;id&gt;", "g:&lt;id&gt;" или null.</summary>
        public string ActiveKey => !_chatsVisible ? null : _groupOpen > 0 ? "g:" + _groupOpen : _chatWith != null ? "u:" + _chatWith : null;

        static bool AppActive => MainWindow.Current?.IsActive == true;

        /// <summary>Клик по уведомлению: при следующем открытии «Чатов» сразу открыть эту переписку.</summary>
        public void OpenAt(string friendId, long groupId)
        {
            _groupOpen = groupId;
            _chatWith = groupId > 0 ? null : friendId;
            _lastMessageId = 0;
        }

        /// <summary>Окно снова в фокусе — открытая переписка считается прочитанной.</summary>
        public void OnWindowActivated()
        {
            if (!_chatsVisible) return;
            if (_groupOpen > 0) GroupSeen(_groupOpen, _lastMessageId);
            else if (_chatWith != null) _ = MarkReadNow(_chatWith);
        }

        long _groupReadSent;

        /// <summary>Группу дочитали: отметка у себя (цифра на «Чатах») и на сервере (две галочки у остальных).</summary>
        void GroupSeen(long gid, long last)
        {
            if (gid <= 0 || last <= 0) return;
            Inbox.MarkGroupRead(gid, last);
            long key = gid * 1_000_000_000L + last;
            if (key == _groupReadSent) return;
            _groupReadSent = key;
            _ = SocialApi.MarkGroupReadServer(gid, last);
        }

        static async Task MarkReadNow(string friendId)
        {
            await SocialApi.MarkRead(friendId);
            Inbox.Nudge();
        }

        public Task ShowChats() => Guard(_chatsHost, ShowChatsCore);

        async Task ShowChatsCore()
        {
            _chatsVisible = true;
            if (!Gate(_chatsHost)) return;
            if (!Supa.SignedIn) { Motion.Replace(_chatsHost, NeedLogin()); return; }
            if (SocialApi.MyProfile == null) await SocialApi.LoadMe();
            if (SocialApi.MyProfile?.Banned == true)
            {
                var ban = new StackPanel { MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
                ban.Children.Add(UI.H1("nav.chats"));
                var card = PunishCard(SocialApi.MyProfile);
                card.Margin = new Thickness(0, 22, 0, 0);
                ban.Children.Add(card);
                Motion.Replace(_chatsHost, ban);
                return;
            }

            // 0.11.6: три колонки, как в макете rainy — список чатов, переписка, профиль собеседника (или группы).
            // Панель справа открывается и закрывается с анимацией; когда её нет, переписка плавно занимает место.
            var grid = new Grid { Margin = new Thickness(0, 0, 14, 0) };
            _listCol = new ColumnDefinition { Width = new GridLength(ListWide) };
            grid.ColumnDefinitions.Add(_listCol);
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // ----- левая колонка: «Чаты», поиск, разделы -----
            var leftCard = Pane();
            leftCard.Margin = new Thickness(0, 0, 12, 0);
            var left = new DockPanel { Margin = new Thickness(16, 18, 10, 6) };
            leftCard.Child = left;

            var top = new DockPanel { Margin = new Thickness(6, 0, 6, 0), LastChildFill = true };
            var tools = UI.Row();
            tools.VerticalAlignment = VerticalAlignment.Center;
            if (IsStaff)
            {
                var create = RoundPill("", "groups.create", () => ShowCreateGroup());
                create.Margin = new Thickness(0, 0, 8, 0);
                create.ToolTip = Loc.T("groups.new");
                tools.Children.Add(create);
                _createLabel = ((StackPanel)create.Content).Children[1] as FrameworkElement;
                _createBtn = create;
            }
            else _createLabel = _createBtn = null;
            // Звуки сообщений и уведомления — открывают свой раздел в «Настройках»
            var sounds = RoundIcon("", 40, () => _navigate("sounds"), accent: true);
            sounds.ToolTip = Loc.T("chats.sounds");
            tools.Children.Add(sounds);
            DockPanel.SetDock(tools, Dock.Right);
            top.Children.Add(tools);
            var title = UI.L("nav.chats", 30, true);
            title.VerticalAlignment = VerticalAlignment.Center;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            top.Children.Add(title);
            DockPanel.SetDock(top, Dock.Top);
            left.Children.Add(top);

            // Поиск по чатам: фильтрует строки на лету (по нику и @юзу)
            var search = new TextBox { Style = (Style)UI.Owner.FindResource("PillField"), MaxLength = 40, Padding = new Thickness(46, 0, 16, 0), Margin = new Thickness(0, 18, 6, 4) };
            search.SetResourceReference(FrameworkElement.TagProperty, "chats.search");
            var searchIcon = UI.Icon("", 16);
            searchIcon.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
            searchIcon.HorizontalAlignment = HorizontalAlignment.Left;
            searchIcon.Margin = new Thickness(18, 18, 0, 4);
            searchIcon.IsHitTestVisible = false;
            var searchBox = new Grid();
            searchBox.Children.Add(search);
            searchBox.Children.Add(searchIcon);
            DockPanel.SetDock(searchBox, Dock.Top);
            left.Children.Add(searchBox);

            var list = new StackPanel { Margin = new Thickness(0, 8, 6, 8) };
            left.Children.Add(UI.Scroll(list));
            grid.Children.Add(leftCard);

            // ----- середина: переписка -----
            _chatPanel = Pane();
            Grid.SetColumn(_chatPanel, 1);
            grid.Children.Add(_chatPanel);

            // ----- справа: профиль собеседника / группы -----
            _infoInner = new Border { Width = InfoWide, HorizontalAlignment = HorizontalAlignment.Right, RenderTransform = new TranslateTransform() };
            _infoHost = new Border { Width = 0, ClipToBounds = true, Child = _infoInner, Opacity = 0 };
            _infoShown = false;
            _infoKey = null;
            Grid.SetColumn(_infoHost, 2);
            grid.Children.Add(_infoHost);
            grid.SizeChanged += (s, e) => FitColumns(e.NewSize.Width);
            Motion.Replace(_chatsHost, grid);

            list.Children.Add(UI.Loading());
            _dmCache.Clear();   // кто-то мог подписаться или поменять «Кто может мне писать»
            var friendsTask = SocialApi.Friends(Supa.Me);
            var unreadTask = SocialApi.UnreadCounts();
            var groupsTask = SocialApi.MyGroups();
            var requestsTask = SocialApi.DmRequests();
            await Task.WhenAll(friendsTask, unreadTask, groupsTask, requestsTask);
            var friends = friendsTask.Result;
            var unread = unreadTask.Result;
            var groups = groupsTask.Result;
            _requestIds = requestsTask.Result ?? Inbox.Requests;
            _friendIds = new HashSet<string>(friends.Select(f => f.Id));
            var others = await SocialApi.OtherChatPartners(_friendIds);
            var requests = others.Where(p => _requestIds.Contains(p.Id)).ToList();
            others = others.Where(p => !_requestIds.Contains(p.Id)).ToList();
            list.Children.Clear();
            _rowSelect.Clear();
            var sections = new List<(FrameworkElement head, Panel rows, Action<bool> force)>();

            // «Запросы» (0.11.5): незнакомые, которые написали первыми, — отдельно, пока не примешь или не ответишь
            if (requests.Count > 0)
            {
                var (head, rows, force) = ListSection("chats.requests", "requests");
                var reqHint = UI.L("chats.requests.d", 12, sub: true, wrap: true);
                reqHint.Margin = new Thickness(6, 0, 0, 8);
                rows.Children.Add(reqHint);
                foreach (var f in requests)
                {
                    var fr = f;
                    _people[f.Id] = f;
                    rows.Children.Add(ChatRow(fr, "@" + fr.Handle, "u:" + fr.Id, unread.TryGetValue(f.Id, out int n) ? n : 0, () => OpenChat(fr)));
                }
                list.Children.Add(head);
                list.Children.Add(rows);
                sections.Add((head, rows, force));
            }

            if (groups.Count > 0)
            {
                var (head, rows, force) = ListSection("groups.title", "groups");
                _groupRows.Clear();
                foreach (var g in groups)
                {
                    // Строка группы в своём «слоте», чтобы после смены аватарки или галочки обновить её на месте
                    var slot = new ContentControl { Content = GroupRow(g), Focusable = false };
                    _groupRows[g.Id] = slot;
                    rows.Children.Add(slot);
                }
                list.Children.Add(head);
                list.Children.Add(rows);
                sections.Add((head, rows, force));
            }

            {
                var (head, rows, force) = ListSection("chats.personal", "personal");
                if (friends.Count == 0 && others.Count == 0) rows.Children.Add(Empty("chats.no_friends"));
                foreach (var f in others.Concat(friends))
                {
                    var fr = f;
                    _people[f.Id] = f;
                    rows.Children.Add(ChatRow(fr, string.IsNullOrEmpty(fr.Handle) ? "" : "@" + fr.Handle, "u:" + fr.Id, unread.TryGetValue(f.Id, out int n) ? n : 0, () => OpenChat(fr)));
                }
                list.Children.Add(head);
                list.Children.Add(rows);
                sections.Add((head, rows, force));
            }
            Motion.Stagger(list);

            // Поиск: прячем строки, которые не подходят, и разделы, где ничего не осталось
            search.TextChanged += (s, e) =>
            {
                string q = search.Text.Trim().TrimStart('@').ToLowerInvariant();
                foreach (var (head, rows, force) in sections)
                {
                    int shown = 0;
                    foreach (FrameworkElement row in rows.Children)
                    {
                        var target = row is ContentControl cc ? cc.Content as FrameworkElement : row;
                        if (target?.Tag is not string text) { row.Visibility = q.Length == 0 ? Visibility.Visible : Visibility.Collapsed; continue; }
                        bool ok = q.Length == 0 || text.Contains(q);
                        row.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
                        if (ok) shown++;
                    }
                    force(q.Length > 0);
                    head.Visibility = q.Length > 0 && shown == 0 ? Visibility.Collapsed : Visibility.Visible;
                    rows.Visibility = head.Visibility;
                }
            };

            if (_groupOpen > 0 && groups.FirstOrDefault(x => x.Id == _groupOpen) is Group open) OpenGroup(open);
            else if (_chatWith != null && _people.TryGetValue(_chatWith, out var who)) OpenChat(who);
            else ShowChatHint();
        }

        // ================== 0.11.6: колонки, строки списка, кнопки ==================

        const double ListWide = 300, ListNarrow = 252, InfoWide = 300, InfoNarrow = 262;
        ColumnDefinition _listCol;
        FrameworkElement _createLabel;
        Button _createBtn;
        Border _infoHost, _infoInner;
        bool _infoShown, _narrow;
        string _infoKey;                                  // чей профиль сейчас в панели справа ("u:…"/"g:…" + состояние)

        /// <summary>Подстраиваем ширину колонок под окно. Совсем узко — панель справа прячется сама (настройка не меняется).</summary>
        void FitColumns(double w)
        {
            if (_listCol == null || w <= 0) return;
            _listCol.Width = new GridLength(w < 980 ? ListNarrow : ListWide);
            // Узкий список: у «+ Группа» остаётся только «+», чтобы заголовок «Чаты» не обрезался
            if (_createLabel != null)
            {
                bool slim = w < 980;
                _createLabel.Visibility = slim ? Visibility.Collapsed : Visibility.Visible;
                _createBtn.Padding = slim ? new Thickness(0) : new Thickness(16, 0, 18, 0);
                _createBtn.Width = slim ? 40 : double.NaN;
            }
            double info = w < 1120 ? InfoNarrow : InfoWide;
            if (_infoInner != null && Math.Abs(_infoInner.Width - info) > 0.5)
            {
                _infoInner.Width = info;
                if (_infoShown) Anim.Set(_infoHost, FrameworkElement.WidthProperty, info + 12);
            }
            bool narrow = w < 820;
            if (narrow == _narrow) return;
            _narrow = narrow;
            UpdateInfo(true);
        }

        /// <summary>Хочет ли человек видеть панель справа (по умолчанию — да; запоминается).</summary>
        static bool InfoWanted
        {
            get => Inbox.Prefs?.ChatInfoOpen ?? true;
            set { var s = Inbox.Prefs; if (s == null || s.ChatInfoOpen == value) return; s.ChatInfoOpen = value; s.Save(); }
        }

        /// <summary>Показать или спрятать панель справа: ширина плавно растёт/сжимается, содержимое выезжает и проявляется.</summary>
        void UpdateInfo(bool animate)
        {
            if (_infoHost == null) return;
            bool show = InfoWanted && !_narrow && _infoKey != null;
            if (show == _infoShown && animate) return;
            _infoShown = show;
            double to = show ? _infoInner.Width + 12 : 0;
            var move = (TranslateTransform)_infoInner.RenderTransform;
            if (!animate || Motion.Off)
            {
                Anim.Set(_infoHost, FrameworkElement.WidthProperty, to);
                Anim.Set(_infoHost, UIElement.OpacityProperty, show ? 1 : 0);
                Anim.Set(move, TranslateTransform.XProperty, 0);
                return;
            }
            double ms = Motion.Ms(show ? 460 : 380);
            Anim.To(_infoHost, FrameworkElement.WidthProperty, to, ms);
            Anim.To(_infoHost, UIElement.OpacityProperty, show ? 1 : 0, Motion.Ms(show ? 320 : 240), delayMs: show ? Motion.Ms(60) : 0);
            if (show) Anim.To(move, TranslateTransform.XProperty, 0, ms, from: 36, ease: Anim.Spring);
            else Anim.To(move, TranslateTransform.XProperty, 30, ms);
        }

        /// <summary>Крестик в панели / пункт «Профиль» в меню ⋮ / клик по шапке переписки.</summary>
        void ToggleInfo(bool? open = null)
        {
            bool want = open ?? !_infoShown;
            InfoWanted = want;
            UpdateInfo(true);
        }

        /// <summary>
        /// Ник и значки сразу за ним; длинный ник обрезается «…», а значки остаются видны.
        /// (Grid прижат влево/по центру: «звёздная» колонка получает ровно ширину текста или остаток места.)
        /// </summary>
        static Grid NameWithBadges(TextBlock name, HorizontalAlignment align, params FrameworkElement[] after)
        {
            var g = new Grid { HorizontalAlignment = align };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(name);
            int col = 1;
            foreach (var a in after)
            {
                if (a == null) continue;
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                a.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(a, col++);
                g.Children.Add(a);
            }
            return g;
        }

        /// <summary>Стеклянная карточка-колонка со скруглением, как на макете.</summary>
        static Border Pane()
        {
            var b = new Border { CornerRadius = new CornerRadius(24), BorderThickness = new Thickness(1), ClipToBounds = true };
            b.SetResourceReference(Border.BackgroundProperty, "CardBg");
            b.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            return b;
        }

        /// <summary>Круглая стеклянная кнопка со значком.</summary>
        static Button RoundIcon(string glyph, double size, Action click, bool accent = false, double iconSize = 0)
        {
            var icon = UI.Icon(glyph, iconSize > 0 ? iconSize : Math.Round(size * 0.38), accent);
            var b = new Button { Style = (Style)UI.Owner.FindResource("RoundGlass"), Width = size, Height = size, Content = icon };
            if (click != null) b.Click += (s, e) => click();
            return b;
        }

        /// <summary>Стеклянная «пилюля» со значком и текстом («+ Группа», «Открыть профиль»).</summary>
        static Button RoundPill(string glyph, string key, Action click, double height = 40)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (glyph != null) sp.Children.Add(UI.Icon(glyph, 14, accent: true));
            var t = UI.L(key, 13.5, true);
            t.VerticalAlignment = VerticalAlignment.Center;
            t.Margin = new Thickness(glyph != null ? 8 : 0, 0, 0, 1);
            sp.Children.Add(t);
            var b = new Button { Style = (Style)UI.Owner.FindResource("RoundGlass"), Height = height, Padding = new Thickness(16, 0, 18, 0), Content = sp };
            if (click != null) b.Click += (s, e) => click();
            return b;
        }

        /// <summary>Заголовок раздела «⌄ Группы ⌄»: нажал — раздел плавно сворачивается (запоминается до перезапуска).</summary>
        static readonly HashSet<string> CollapsedSections = new HashSet<string>();

        (FrameworkElement head, Panel rows, Action<bool> force) ListSection(string key, string id)
        {
            var rows = new StackPanel { LayoutTransform = new ScaleTransform(1, 1) };
            var chevron = UI.Icon("", 10);
            chevron.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
            chevron.RenderTransformOrigin = new Point(0.5, 0.5);
            chevron.RenderTransform = new RotateTransform();
            var label = UI.L(key, 14, true);
            label.Margin = new Thickness(8, 0, 6, 1);
            var tail = UI.Icon("", 8.5);
            tail.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
            tail.Margin = new Thickness(0, 2, 0, 0);
            var head = UI.Row(chevron, label, tail);
            head.Margin = new Thickness(6, 10, 0, 10);
            head.Background = Brushes.Transparent;
            head.HorizontalAlignment = HorizontalAlignment.Left;
            UI.Hand(head);

            bool forced = false;
            void Paint(bool animate)
            {
                bool closed = CollapsedSections.Contains(id) && !forced;
                var scale = (ScaleTransform)rows.LayoutTransform;
                var rot = (RotateTransform)chevron.RenderTransform;
                double ms = animate && !Motion.Off ? Motion.Ms(320) : 1;
                Anim.To(rot, RotateTransform.AngleProperty, closed ? -90 : 0, ms);
                if (closed)
                {
                    Anim.To(rows, UIElement.OpacityProperty, 0, ms * 0.7);
                    Anim.To(scale, ScaleTransform.ScaleYProperty, 0.001, ms, done: () => { if (CollapsedSections.Contains(id) && !forced) rows.IsHitTestVisible = false; });
                }
                else
                {
                    rows.IsHitTestVisible = true;
                    Anim.To(scale, ScaleTransform.ScaleYProperty, 1, ms);
                    Anim.To(rows, UIElement.OpacityProperty, 1, ms, delayMs: animate ? Motion.Ms(60) : 0);
                }
            }
            head.MouseLeftButtonUp += (s, e) =>
            {
                if (forced) return;
                if (!CollapsedSections.Remove(id)) CollapsedSections.Add(id);
                Paint(true);
            };
            Paint(false);
            return (head, rows, on => { if (forced == on) return; forced = on; Paint(true); });
        }

        /// <summary>Выбранная строка слева подсвечивается цветом темы: ключ чата → «подсветить / погасить».</summary>
        readonly Dictionary<string, Action<bool>> _rowSelect = new Dictionary<string, Action<bool>>();
        string _selectedKey;

        void SelectRow(string key)
        {
            if (_selectedKey != null && _rowSelect.TryGetValue(_selectedKey, out var off)) off(false);
            _selectedKey = key;
            if (key != null && _rowSelect.TryGetValue(key, out var on)) on(true);
        }

        /// <summary>
        /// Строка чата слева (макет 0.11.6): «пилюля» с аватаркой, ником, галочкой и @юзом; синяя точка — непрочитанные;
        /// «⋯» — быстрое меню. Выбранный чат подсвечен цветом темы.
        /// </summary>
        FrameworkElement ChatRow(Profile p, string subtitle, string key, int unread, Action open)
        {
            const double R = 20;
            var root = new Grid { Margin = new Thickness(0, 0, 0, 8), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            root.Tag = ((p.Nickname ?? "") + " " + (p.Handle ?? "")).ToLowerInvariant();   // для поиска

            var baseBg = new Border { CornerRadius = new CornerRadius(R), BorderThickness = new Thickness(1) };
            baseBg.SetResourceReference(Border.BackgroundProperty, "CardBg");
            baseBg.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            root.Children.Add(baseBg);

            // Подсветка выбранной строки: цвет темы + блик + яркая кромка
            var sel = new Grid { Opacity = 0, IsHitTestVisible = false };
            var selFill = new Border { CornerRadius = new CornerRadius(R), Opacity = 0.42 };
            selFill.SetResourceReference(Border.BackgroundProperty, "Accent");
            sel.Children.Add(selFill);
            sel.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(R),
                Background = new LinearGradientBrush(Color.FromArgb(0x38, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), new Point(0, 0), new Point(0.6, 1))
            });
            var selRim = new Border { CornerRadius = new CornerRadius(R), BorderThickness = new Thickness(1.4) };
            selRim.SetResourceReference(Border.BorderBrushProperty, "Accent");
            sel.Children.Add(selRim);
            root.Children.Add(sel);

            var hover = new Border { CornerRadius = new CornerRadius(R), Opacity = 0, IsHitTestVisible = false };
            hover.SetResourceReference(Border.BackgroundProperty, "HoverBg");
            root.Children.Add(hover);

            var grid = new Grid { Margin = new Thickness(12, 10, 10, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bool person = p.Id != null && !p.Id.StartsWith("group:") && !p.Bot;
            grid.Children.Add(person ? Online.WithDot(UI.Avatar(p, 50), p.Id, 50) : UI.Avatar(p, 50));

            var name = NameWithBadges(UI.T(p.Nickname, 15.5, true), HorizontalAlignment.Left,
                p.Verified ? Pad(UI.Badge(17, p.Badge), 6) : null, p.IsPlus ? UI.PlusAfter(17) : null);
            var info = new StackPanel { Margin = new Thickness(13, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(name);
            if (!string.IsNullOrEmpty(subtitle))
            {
                var sub = UI.T(subtitle, 13, sub: true);
                sub.TextTrimming = TextTrimming.CharacterEllipsis;
                sub.Margin = new Thickness(0, 2, 0, 0);
                info.Children.Add(sub);
            }
            Grid.SetColumn(info, 1);
            grid.Children.Add(info);

            // Справа: «⋯» (у выбранной строки и при наведении) и синяя точка непрочитанных
            var right = new Grid { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);
            var dot = UnreadDot(unread);
            right.Children.Add(dot);
            var more = RoundIcon("", 30, null, iconSize: 13);
            more.Opacity = 0;
            more.IsHitTestVisible = false;
            more.ToolTip = Loc.T("chats.more");
            more.Click += (s, e) => RowMenu(more, p, key);
            right.Children.Add(more);
            root.Children.Add(grid);

            bool selected = false;
            void Paint()
            {
                bool showMore = selected || root.IsMouseOver;
                Anim.To(more, UIElement.OpacityProperty, showMore ? 1 : 0, Motion.Ms(showMore ? 180 : 260));
                more.IsHitTestVisible = showMore;
                if (unread > 0) Anim.To(dot, UIElement.OpacityProperty, showMore ? 0 : 1, Motion.Ms(200));
            }
            root.MouseEnter += (s, e) => { Anim.To(hover, UIElement.OpacityProperty, selected ? 0.4 : 1, 160); Paint(); };
            root.MouseLeave += (s, e) => { Anim.To(hover, UIElement.OpacityProperty, 0, 320); Paint(); };
            root.MouseLeftButtonUp += (s, e) =>
            {
                if (more.IsMouseOver) return;
                Motion.Pop(root, 0.985);
                if (unread > 0) { unread = 0; dot.IsHitTestVisible = false; Anim.To(dot, UIElement.OpacityProperty, 0, Motion.Ms(240)); }   // прочитано
                open();
            };
            _rowSelect[key] = on =>
            {
                if (selected == on) return;
                selected = on;
                Anim.To(sel, UIElement.OpacityProperty, on ? 1 : 0, Motion.Ms(on ? 260 : 360));
                Paint();
            };
            if (key == _selectedKey) { selected = true; sel.Opacity = 1; Paint(); }
            return root;
        }

        /// <summary>Меню «⋯» у строки: уведомления, профиль.</summary>
        void RowMenu(FrameworkElement anchor, Profile p, string key)
        {
            bool muted = Inbox.IsMuted(key);
            var items = new List<(string key, string glyph, Action act)>
            {
                (muted ? "chats.notify_on" : "chats.notify_off", muted ? "" : "", () => { Inbox.SetMuted(key, !muted); _toast(Loc.T(muted ? "chats.notify_on.done" : "chats.notify_off.done")); RefreshInfo(); })
            };
            if (key.StartsWith("u:")) items.Add(("chats.open_profile", "", () => _ = OpenProfile(p)));
            ShowMenu(anchor, items.ToArray());
        }

        /// <summary>Выпадающее меню в стиле приложения (стекло в цветах темы) со значками; красные — опасные действия.</summary>
        static void ShowMenu(FrameworkElement anchor, params (string key, string glyph, Action act)[] items)
        {
            var popup = new System.Windows.Controls.Primitives.Popup
            {
                PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                StaysOpen = false, AllowsTransparency = true, PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.Fade,
                VerticalOffset = 2, HorizontalOffset = -12
            };
            var list = new StackPanel { Margin = new Thickness(6) };
            foreach (var (key, glyph, act) in items)
            {
                var a = act;
                bool danger = key == "groups.leave" || key == "groups.delete";
                var row = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, MinHeight = 38 };
                var hover = new Border { CornerRadius = new CornerRadius(10), Opacity = 0 };
                hover.SetResourceReference(Border.BackgroundProperty, "HoverBg");
                row.Children.Add(hover);
                var icon = UI.Icon(glyph ?? "", 14, accent: !danger);
                icon.Width = 18;
                var text = UI.T(Loc.T(key), 13.5, true);
                text.Margin = new Thickness(10, 0, 0, 1);
                text.VerticalAlignment = VerticalAlignment.Center;
                if (danger) { icon.Foreground = DangerBrush; text.Foreground = DangerBrush; }
                var content = UI.Row(icon, text);
                content.Margin = new Thickness(12, 0, 16, 0);
                content.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(content);
                row.MouseEnter += (s, e) => Anim.To(hover, UIElement.OpacityProperty, 1, 140);
                row.MouseLeave += (s, e) => Anim.To(hover, UIElement.OpacityProperty, 0, 260);
                row.MouseLeftButtonUp += (s, e) => { popup.IsOpen = false; a(); };
                list.Children.Add(row);
            }
            var layers = new Grid();
            var back = new Border { CornerRadius = new CornerRadius(16) };
            back.SetResourceReference(Border.BackgroundProperty, "WinBg");
            var glass = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1) };
            glass.SetResourceReference(Border.BackgroundProperty, "CardBg");
            glass.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            layers.Children.Add(back);
            layers.Children.Add(glass);
            layers.Children.Add(list);
            var card = new Border
            {
                Child = layers, MinWidth = 220, Margin = new Thickness(12, 4, 12, 16),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 22, ShadowDepth = 6, Direction = 270, Opacity = 0.35, Color = Colors.Black }
            };
            popup.Child = card;
            popup.IsOpen = true;
        }

        readonly Dictionary<long, ContentControl> _groupRows = new Dictionary<long, ContentControl>();

        /// <summary>Строка группы слева: аватарка, название, галочка и @юз — как в шапке открытой группы.</summary>
        FrameworkElement GroupRow(Group g) =>
            ChatRow(g.Look(), string.IsNullOrEmpty(g.Handle) ? Loc.T("groups.subtitle") : "@" + g.Handle, "g:" + g.Id, Inbox.GroupUnread(g.Id), () => OpenGroup(g));

        /// <summary>Перерисовать строку группы в списке слева (после смены аватарки или галочки).</summary>
        void RefreshGroupRow(Group g)
        {
            if (_groupRows.TryGetValue(g.Id, out var slot)) slot.Content = GroupRow(g);
        }

        /// <summary>Непрочитанные: синяя точка со свечением, как на макете (число — в подсказке).</summary>
        static FrameworkElement UnreadDot(int n)
        {
            var g = new Grid { Width = 20, Height = 20, Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed, IsHitTestVisible = n > 0 };
            var halo = new Ellipse { Width = 18, Height = 18, Opacity = 0.28 };
            halo.SetResourceReference(Shape.FillProperty, "Accent");
            var core = new Ellipse { Width = 10, Height = 10, Stroke = new SolidColorBrush(Color.FromArgb(0x90, 255, 255, 255)), StrokeThickness = 1 };
            core.SetResourceReference(Shape.FillProperty, "Accent");
            g.Children.Add(halo);
            g.Children.Add(core);
            if (n > 0) g.ToolTip = string.Format(Loc.T("chats.unread_n"), n);
            return g;
        }

        void ShowChatHint()
        {
            SelectRow(null);
            ShowInfo(null, null);
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var circle = new Border { Width = 84, Height = 84, CornerRadius = new CornerRadius(42), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center };
            circle.SetResourceReference(Border.BackgroundProperty, "HoverBg");
            circle.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            var ic = UI.Icon("", 34, accent: true);
            ic.HorizontalAlignment = HorizontalAlignment.Center;
            circle.Child = ic;
            sp.Children.Add(circle);
            var hint = UI.L("chats.pick", 14, sub: true, wrap: true);
            hint.TextAlignment = TextAlignment.Center;
            hint.MaxWidth = 300;
            hint.Margin = new Thickness(0, 16, 0, 0);
            sp.Children.Add(hint);
            SetPanel(sp);
        }

        /// <summary>Содержимое средней части меняется плавно.</summary>
        void SetPanel(UIElement content)
        {
            if (_chatPanel == null) return;
            var host = _chatPanel.Child as Grid;
            if (host == null) { host = new Grid(); _chatPanel.Child = host; }
            Motion.Replace(host, content);
        }

        public void HideChats()
        {
            _chatsVisible = false;
            _poll.Stop();
        }

        // ================== личный чат ==================

        /// <summary>Почему нельзя написать: «rainy принимает сообщения только от друзей» и т. п.</summary>
        static string DmClosedText(Profile p, string dmFrom) => string.Format(Loc.T(dmFrom switch
        {
            "friends" => "chats.dm.friends",
            "followers" => "chats.dm.followers",
            _ => "chats.dm.closed"
        }), p.Nickname);

        /// <summary>Вместо поля ввода — серая «заглушка» с замком и причиной (как кнопка «Написать» в профиле).</summary>
        static FrameworkElement DmClosedNote(Profile p, string dmFrom, bool known, out TextBlock text)
        {
            var lockIcon = new TextBlock { Text = "\uE72E", FontFamily = (FontFamily)UI.Owner.FindResource("Icons"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            lockIcon.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
            text = UI.T(known ? DmClosedText(p, dmFrom) : "…", 12.5, sub: true, wrap: true);
            text.VerticalAlignment = VerticalAlignment.Center;
            var row = new DockPanel();
            DockPanel.SetDock(lockIcon, Dock.Left);
            row.Children.Add(lockIcon);
            row.Children.Add(text);
            var box = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(14, 10, 14, 14), Opacity = 0.75, Child = row, BorderThickness = new Thickness(1) };
            box.SetResourceReference(Border.BackgroundProperty, "CardBg");
            box.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            return box;
        }

        /// <summary>Внизу чата-запроса: кто это и три кнопки — принять, удалить, заблокировать.</summary>
        FrameworkElement RequestBar(Profile p)
        {
            var sp = new StackPanel { Margin = new Thickness(16, 10, 16, 14) };
            var text = UI.T(string.Format(Loc.T("chats.request.text"), p.Nickname), 12.5, sub: true, wrap: true);
            text.Margin = new Thickness(2, 0, 0, 10);
            sp.Children.Add(text);
            var row = new WrapPanel();
            async Task Act(Button b, Func<Task<string>> call, string doneKey)
            {
                b.IsEnabled = false;
                string err = await call();
                b.IsEnabled = true;
                if (err != null) { _toast(err); return; }
                _toast(string.Format(Loc.T(doneKey), p.Nickname));
                _requestIds.Remove(p.Id);
                _dmCache.Remove(p.Id);
                Inbox.RefreshRequests();
                if (doneKey != "chats.request.accepted") { _chatWith = null; }
                await ShowChats();
            }
            Button accept = null, delete = null, block = null;
            accept = UI.Primary("chats.request.accept", async (s, e) => await Act(accept, () => SocialApi.DmAccept(p.Id), "chats.request.accepted"));
            delete = UI.Glass("\uE74D", "chats.request.delete", async (s, e) => await Act(delete, () => SocialApi.DmDeleteRequest(p.Id), "chats.request.deleted"), 9);
            block = UI.Danger("\uE8F8", "chats.request.block", async (s, e) => await Act(block, () => SocialApi.DmBlock(p.Id, true), "chats.request.blocked"));
            foreach (var b in new FrameworkElement[] { accept, delete, block })
            {
                b.Margin = new Thickness(0, 0, 8, 0);
                row.Children.Add(b);
            }
            sp.Children.Add(row);
            return sp;
        }

        void OpenChat(Profile friend)
        {
            _chatWith = friend.Id;
            _groupOpen = 0;
            _lastMessageId = 0;
            // Кому можно писать (0.11.5): друзьям — всегда; остальным — по их настройке «Кто может мне писать» (решает сервер).
            // Бот MizuEngine присылает только приглашения. Запрос от незнакомого — сначала принять.
            bool isRequest = _requestIds.Contains(friend.Id);
            bool known = _friendIds.Contains(friend.Id) || friend.Bot || isRequest || _dmCache.ContainsKey(friend.Id);
            bool canWrite = !friend.Bot && !isRequest && (_friendIds.Contains(friend.Id) || (_dmCache.TryGetValue(friend.Id, out var dm) && dm.ok));
            UIElement footer = null;
            if (isRequest) footer = RequestBar(friend);
            TextBlock closedText = null;
            if (!canWrite && !friend.Bot && !isRequest)
                footer = DmClosedNote(friend, _dmCache.TryGetValue(friend.Id, out var cached) ? cached.dmFrom : null, known, out closedText);
            if (!known)
            {
                // Спрашиваем сервер; писать можно — перерисовываем чат с полем ввода, нельзя — уточняем подсказку
                string id = friend.Id;
                _ = CheckDm();
                async Task CheckDm()
                {
                    var st = await SocialApi.DmStatus(id);
                    _dmCache[id] = (st.ok, st.dmFrom);
                    if (_chatWith != id || _groupOpen != 0) return;
                    if (st.ok) OpenChat(friend);
                    else if (closedText != null) closedText.Text = DmClosedText(friend, st.dmFrom);
                }
            }

            // Шапка переписки (0.11.6): аватарка с «в сети», ник с галочкой, статус; справа — звонок и ⋮
            var head = ChatHead(friend.Bot ? UI.Avatar(friend, 48) : Online.WithDot(UI.Avatar(friend, 48), friend.Id, 48),
                                friend.Nickname, friend.Verified ? friend.Badge : null, friend.IsPlus,
                                friend.Bot ? null : Online.Status(friend.Id, 12.5), null);
            head.Call.Click += (s, e) => _toast(Loc.T("chats.call_soon"));
            head.More.Click += (s, e) =>
            {
                string key = "u:" + friend.Id;
                bool muted = Inbox.IsMuted(key);
                var items = new List<(string key, string glyph, Action act)>
                {
                    (_infoShown ? "chats.info_hide" : "chats.info_show", "\uE77B", () => HeadClick(friend)),
                    ("chats.open_profile", "\uE8A7", () => _ = OpenProfile(friend))
                };
                // Тема чата: фон и цвет сообщений видят оба
                if (canWrite) items.Add(("theme.title", "\uE790", () => ShowThemePicker(_theme, t => SocialApi.SetChatTheme(friend.Id, t), () => OpenChat(friend))));
                items.Add((muted ? "chats.notify_on" : "chats.notify_off", muted ? "\uEA8F" : "\uE7ED", () => { Inbox.SetMuted(key, !muted); _toast(Loc.T(muted ? "chats.notify_on.done" : "chats.notify_off.done")); RefreshInfo(); }));
                ShowMenu(head.More, items.ToArray());
            };
            head.Who.MouseLeftButtonUp += (s, e) => HeadClick(friend, head.Avatar);

            _themeSource = () => SocialApi.GetChatTheme(friend.Id);
            BuildChatView(head.Root, canWrite, async text => await SocialApi.SendText(friend.Id, text), friend, footer);
            _ = ReloadTheme(true);
            SelectRow("u:" + friend.Id);
            ShowPersonInfo(friend, canWrite);
        }

        /// <summary>Клик по шапке: панель справа открывается/закрывается. Если окно совсем узкое — сразу профиль.</summary>
        void HeadClick(Profile friend, FrameworkElement avatar = null)
        {
            if (_narrow)
            {
                if (avatar != null) Motion.Capture(avatar, "avatar:" + friend.Id);
                _ = OpenProfile(friend);
                return;
            }
            ToggleInfo();
        }

        /// <summary>Части шапки переписки.</summary>
        sealed class Head
        {
            public Grid Root;
            public FrameworkElement Who, Avatar;
            public Button Call, More;
            public TextBlock Sub;
        }

        /// <summary>Шапка переписки: аватарка, имя, галочка, строка под именем; справа круглые кнопки «звонок» и «⋮».</summary>
        Head ChatHead(FrameworkElement avatar, string name, string badge, bool plus, TextBlock status, string subtitle)
        {
            var h = new Head { Root = new Grid(), Avatar = avatar };
            h.Root.ColumnDefinitions.Add(new ColumnDefinition());
            h.Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameRow = NameWithBadges(UI.T(name, 18, true), HorizontalAlignment.Left,
                badge != null ? Pad(UI.Badge(17, badge), 7) : null, plus ? UI.PlusAfter(17) : null);
            var titleCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(13, 0, 10, 0) };
            titleCol.Children.Add(nameRow);
            if (status != null) { status.Margin = new Thickness(0, 2, 0, 0); titleCol.Children.Add(status); h.Sub = status; }
            else if (subtitle != null) { var t = UI.T(subtitle, 12.5, sub: true); t.TextTrimming = TextTrimming.CharacterEllipsis; t.Margin = new Thickness(0, 2, 0, 0); titleCol.Children.Add(t); h.Sub = t; }

            // Аватарка + имя; имя получает только оставшееся место (длинное — с «…», не налезает на кнопки)
            var who = new Grid { Background = Brushes.Transparent };
            who.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            who.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            avatar.VerticalAlignment = VerticalAlignment.Center;
            who.Children.Add(avatar);
            Grid.SetColumn(titleCol, 1);
            who.Children.Add(titleCol);
            who.HorizontalAlignment = HorizontalAlignment.Left;
            who.ToolTip = Loc.T("chats.info_tip");
            UI.Hand(who);
            h.Who = who;
            h.Root.Children.Add(who);

            h.Call = RoundIcon("\uE717", 44, null, iconSize: 17);
            h.Call.ToolTip = Loc.T("chats.call");
            h.More = RoundIcon("\uE712", 44, null, iconSize: 16);
            h.More.ToolTip = Loc.T("chats.more");
            // «⋮» — та же иконка «⋯», повёрнутая вертикально
            if (h.More.Content is FrameworkElement dots) { dots.RenderTransformOrigin = new Point(0.5, 0.5); dots.RenderTransform = new RotateTransform(90); }
            h.More.Margin = new Thickness(10, 0, 0, 0);
            var buttons = UI.Row(h.Call, h.More);
            buttons.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(buttons, 1);
            h.Root.Children.Add(buttons);
            return h;
        }

        // ================== группа ==================

        void OpenGroup(Group g)
        {
            _groupOpen = g.Id;
            _chatWith = null;
            _lastMessageId = 0;

            var head = ChatHead(UI.Avatar(g.Look(), 48), g.Name, g.Badge, false, null, "…");
            var membersText = head.Sub;
            bool owner = g.Owner == Supa.Me;
            bool admin = SocialApi.MyProfile?.IsAdmin == true;
            head.Call.Click += (s, e) => _toast(Loc.T("chats.call_soon"));
            head.More.Click += (s, e) =>
            {
                string key = "g:" + g.Id;
                bool muted = Inbox.IsMuted(key);
                var items = new List<(string key, string glyph, Action act)>
                {
                    (_infoShown ? "chats.info_hide" : "groups.info_show", "\uE946", () => { if (_narrow) ShowMembers(g); else ToggleInfo(); }),
                    ("groups.members", "\uE716", () => ShowMembers(g)),
                    ("groups.invite", "\uE8FA", () => ShowInvite(g))
                };
                if (owner || admin) items.Add(("groups.settings", "\uE713", () => ShowGroupSettings(g)));
                items.Add((owner ? "groups.delete" : "groups.leave", owner ? "\uE74D" : "\uE7E8", () => _ = LeaveOrDelete(g)));
                items.Add((muted ? "chats.notify_on" : "chats.notify_off", muted ? "\uEA8F" : "\uE7ED", () => { Inbox.SetMuted(key, !muted); _toast(Loc.T(muted ? "chats.notify_on.done" : "chats.notify_off.done")); RefreshInfo(); }));
                ShowMenu(head.More, items.ToArray());
            };
            head.Who.MouseLeftButtonUp += (s, e) => { if (_narrow) ShowMembers(g); else ToggleInfo(); };

            _ = LoadMembers();
            async Task LoadMembers()
            {
                var list = await SocialApi.GroupMembers(g.Id);
                foreach (var m in list) _groupPeople[m.User.Id] = m.User;
                int joined = list.Count(m => m.Joined);
                membersText.Text = (string.IsNullOrEmpty(g.Handle) ? "" : "@" + g.Handle + " · ") +
                                   string.Format(Loc.T("groups.count"), joined) + (g.MaxMembers.HasValue ? $" / {g.MaxMembers}" : "");
            }

            long gid = g.Id;
            _themeSource = async () => (await SocialApi.GetGroup(gid))?.Theme;
            BuildChatView(head.Root, true, async text =>
            {
                if (!IsStaff && DateTime.Now < _cooldownUntil) return Loc.T("err.slow_mode");
                string err = await SocialApi.SendGroupText(g.Id, text);
                if (err == null && !IsStaff) _cooldownUntil = DateTime.Now.AddSeconds(SlowModeSeconds);
                return err;
            }, null);
            ApplyTheme(g.Theme);
            SelectRow("g:" + g.Id);
            ShowGroupInfo(g);
        }

        // ================== общее окно переписки ==================

        /// <summary>Шапка, лента сообщений и поле ввода. send возвращает текст ошибки или null. friend — для личного чата (кнопка «Сборка»).</summary>
        void BuildChatView(FrameworkElement head, bool canWrite, Func<string, Task<string>> send, Profile friend, UIElement footer = null)
        {
            var dock = new DockPanel();
            head.Margin = new Thickness(20, 16, 16, 14);
            DockPanel.SetDock(head, Dock.Top);
            dock.Children.Add(head);

            var divider = new Border { Height = 1 };
            divider.SetResourceReference(Border.BackgroundProperty, "Divider");
            DockPanel.SetDock(divider, Dock.Top);
            dock.Children.Add(divider);

            var me = SocialApi.MyProfile;
            bool muted = canWrite && me?.Muted == true;
            if (muted) canWrite = false;
            _inputBox = null;

            if (canWrite)
            {
                // Поле ввода (0.11.6): круглая «скрепка», поле-«пилюля» и круглая кнопка «Отправить» цвета темы
                var input = new Grid { Margin = new Thickness(14, 10, 14, 14) };
                input.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                input.ColumnDefinitions.Add(new ColumnDefinition());
                input.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var box = new TextBox { Style = (Style)UI.Owner.FindResource("PillField"), MaxLength = 4000, MinHeight = 52, Padding = new Thickness(22, 0, 18, 0) };
                box.Margin = new Thickness(friend != null ? 10 : 0, 0, 10, 0);
                box.SetResourceReference(FrameworkElement.TagProperty, "chats.placeholder");
                Grid.SetColumn(box, 1);
                var sendBtn = new Button { Style = (Style)UI.Owner.FindResource("RoundAccent"), Width = 54, Height = 54 };
                var sendIcon = new TextBlock { Text = "\uE724", FontFamily = (FontFamily)UI.Owner.FindResource("Icons"), FontSize = 19, Foreground = Brushes.White, Margin = new Thickness(2, 1, 0, 0) };
                sendBtn.Content = sendIcon;
                sendBtn.ToolTip = Loc.T("chats.send");
                Grid.SetColumn(sendBtn, 2);
                input.Children.Add(box);
                input.Children.Add(sendBtn);
                _inputBox = box;
                if (friend != null)
                {
                    // «Скрепка» — отправить сборку
                    Button presetBtn = null;
                    presetBtn = RoundIcon("\uE723", 52, () => PresetMenu(presetBtn, friend), iconSize: 19);
                    presetBtn.ToolTip = Loc.T("chats.send_preset");
                    input.Children.Add(presetBtn);
                }
                DockPanel.SetDock(input, Dock.Bottom);
                dock.Children.Add(input);

                // Медленный режим: на кнопке обратный отсчёт до следующего сообщения
                var tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                tick.Tick += (s, e) =>
                {
                    double left = (_cooldownUntil - DateTime.Now).TotalSeconds;
                    if (_groupOpen == 0 || left <= 0) { sendBtn.IsEnabled = true; sendBtn.Content = sendIcon; tick.Stop(); return; }
                    sendBtn.IsEnabled = false;
                    sendBtn.Content = new TextBlock { Text = Math.Ceiling(left).ToString(), FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
                };
                dock.Unloaded += (s, e) => tick.Stop();

                async Task DoSend()
                {
                    string text = box.Text.Trim();
                    if (text.Length == 0 || !sendBtn.IsEnabled) return;
                    if (_groupOpen > 0 && !IsStaff && DateTime.Now < _cooldownUntil) { _toast(Loc.T("err.slow_mode")); return; }
                    box.Text = "";
                    // Сообщение сразу вылетает в ленту с часиками, не дожидаясь сервера — как в Telegram
                    var pending = AddPending(text, _groupOpen > 0);
                    ChatSounds.PlayOut();
                    string err = await send(text);
                    if (err != null)
                    {
                        RemovePending(pending);
                        _toast(err);
                        if (box.Text.Length == 0) box.Text = text;
                    }
                    else await PollMessages();
                    if (_groupOpen > 0 && !IsStaff) tick.Start();
                }
                sendBtn.Click += async (s, e) => await DoSend();
                box.KeyDown += async (s, e) => { if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) { e.Handled = true; await DoSend(); } };
                if (_groupOpen > 0 && !IsStaff && DateTime.Now < _cooldownUntil) tick.Start();
            }
            else if (!muted && footer != null)
            {
                DockPanel.SetDock(footer, Dock.Bottom);
                dock.Children.Add(footer);
            }
            else
            {
                var note = muted
                    ? UI.T(string.Format(Loc.T("chats.muted"), Until(me.Banned ? me.BannedUntil : me.MutedUntil)), 12.5, sub: true, wrap: true)
                    : UI.L("chats.service", 12.5, sub: true, wrap: true);
                note.Margin = new Thickness(18, 10, 18, 16);
                DockPanel.SetDock(note, Dock.Bottom);
                dock.Children.Add(note);
            }

            _messages = new StackPanel { Margin = new Thickness(18, 10, 18, 10), RenderTransform = new TranslateTransform() };
            _feedDay = default;
            _messagesScroll = UI.Scroll(_messages);
            _pending.Clear();
            _historyLoaded = false;
            _unreadTicks.Clear();
            dock.Children.Add(_messagesScroll);

            // Под перепиской — слой фона темы (со скруглением, как у карточки)
            _chatRoot = new Grid();
            _themeLayer = new Border { CornerRadius = new CornerRadius(23), IsHitTestVisible = false, Opacity = 0 };
            _chatRoot.Children.Add(_themeLayer);
            _chatRoot.Children.Add(dock);
            _themePoll = 0;
            ApplyTheme(new ChatTheme(), animate: false);
            SetPanel(_chatRoot);

            _ = PollMessages();
            _poll.Start();
        }

        void PresetMenu(Button anchor, Profile friend)
        {
            var menu = new ContextMenu();
            var presets = Presets.List();
            if (presets.Count == 0) menu.Items.Add(new MenuItem { Header = Loc.T("presets.none"), IsEnabled = false });
            foreach (var p in presets)
            {
                var pr = p;
                var item = new MenuItem { Header = pr.Name };
                item.Click += async (o, a) =>
                {
                    _toast(Loc.T("profile.uploading"));
                    string err = await SocialApi.SendPreset(friend.Id, pr);
                    if (err != null) _toast(err); else { ChatSounds.PlayOut(); await PollMessages(); }
                };
                menu.Items.Add(item);
            }
            menu.PlacementTarget = anchor;
            menu.IsOpen = true;
        }

        async Task PollMessages()
        {
            if (!_chatsVisible || _messages == null || (_chatWith == null && _groupOpen == 0)) { _poll.Stop(); return; }
            string friendId = _chatWith;
            long groupId = _groupOpen;
            var messages = _messages;
            var list = groupId > 0 ? await SocialApi.GroupConversation(groupId, _lastMessageId) : await SocialApi.Conversation(friendId, _lastMessageId);
            if (friendId != _chatWith || groupId != _groupOpen || messages != _messages) return;
            bool first = !_historyLoaded;
            _historyLoaded = true;
            if (++_themePoll % 6 == 0) _ = ReloadTheme(false);
            if (list.Count == 0) { await CheckRead(friendId, groupId, messages); return; }
            bool incoming = false;
            bool atBottom = _messagesScroll.VerticalOffset >= _messagesScroll.ScrollableHeight - 30;
            foreach (var m in list)
            {
                if (m.Id <= _lastMessageId) continue;
                _lastMessageId = m.Id;

                // Сервер подтвердил наше сообщение — оно уже в ленте, меняем часики на галочку
                if (m.Sender == Supa.Me && m.Kind == "text")
                {
                    var p = _pending.FirstOrDefault(x => x.Feed == messages && x.Body == m.Body);
                    if (p != null)
                    {
                        _pending.Remove(p);
                        MarkSent(p.Status);
                        if (m.Read) MarkRead(p.Status, true); else _unreadTicks[m.Id] = p.Status;
                        continue;
                    }
                }
                if (m.Sender != Supa.Me) incoming = true;

                DayMark(messages, m.Created, !first);
                var b = Bubble(m, groupId > 0);
                if (first)
                {
                    // История при открытии чата просто мягко проявляется
                    messages.Children.Add(b);
                    Anim.To(b, UIElement.OpacityProperty, 1, Motion.Ms(260), from: 0);
                }
                else Append(b, fly: atBottom || m.Sender == Supa.Me, mine: m.Sender == Supa.Me);
            }
            // Прочитано — только когда окно правда перед глазами (как в Telegram); иначе придёт уведомление
            if (AppActive)
            {
                if (groupId == 0 && list.Any(m => m.Sender == friendId)) _ = MarkReadNow(friendId);
                if (groupId > 0) GroupSeen(groupId, _lastMessageId);
            }
            if (incoming && !first && AppActive && !Inbox.IsMuted(groupId > 0 ? "g:" + groupId : "u:" + friendId)) ChatSounds.PlayIn();   // новое сообщение от собеседника — звук (окно не в фокусе — звук даст уведомление)
            await CheckRead(friendId, groupId, messages);
            if (first)
                _messagesScroll.Dispatcher.BeginInvoke(new Action(() => _messagesScroll.ScrollToEnd()), DispatcherPriority.Background);
        }

        /// <summary>Личный чат: спрашиваем сервер, до какого моего сообщения собеседник дочитал, и ставим там две синие галочки.</summary>
        async Task CheckRead(string friendId, long groupId, StackPanel feed)
        {
            if ((friendId == null && groupId <= 0) || _unreadTicks.Count == 0) return;
            // В группе две галочки — когда моё сообщение прочитал хоть кто-то из участников (как в Telegram)
            long upTo = groupId > 0 ? await SocialApi.GroupReadMark(groupId) : await SocialApi.LastReadByFriend(friendId);
            if (upTo == 0 || friendId != _chatWith || groupId != _groupOpen || feed != _messages) return;
            foreach (var id in _unreadTicks.Keys.Where(id => id <= upTo).ToList())
            {
                MarkRead(_unreadTicks[id], true);
                _unreadTicks.Remove(id);
            }
        }

        Pending AddPending(string text, bool inGroup)
        {
            var m = new ChatMessage { Sender = Supa.Me, Kind = "text", Body = text, Created = DateTime.Now };
            DayMark(_messages, m.Created, true);
            var b = Bubble(m, inGroup, pending: true);
            var p = new Pending { Body = text, Bubble = b, Status = (TextBlock)b.Tag, Feed = _messages };
            _pending.Add(p);
            Append(b, fly: true, mine: true);
            return p;
        }

        void RemovePending(Pending p)
        {
            _pending.Remove(p);
            Anim.To(p.Bubble, UIElement.OpacityProperty, 0, Motion.Ms(250), done: () => p.Feed.Children.Remove(p.Bubble));
        }

        /// <summary>Прочитано: появляется вторая галочка, обе становятся синими.</summary>
        static void MarkRead(TextBlock first, bool animate)
        {
            if (first == null || first.Tag is not TextBlock second) return;
            // Цвет зависит от цвета пузыря, а его можно узнать, только когда сообщение уже в ленте
            void Paint()
            {
                var blue = new SolidColorBrush(ReadTickColor(first));
                blue.Freeze();
                first.Foreground = blue;
                second.Foreground = blue;
            }
            if (first.IsLoaded) Paint();
            else
            {
                RoutedEventHandler once = null;
                once = (s, e) => { first.Loaded -= once; Paint(); };
                first.Loaded += once;
            }
            first.Opacity = 1;
            if (!animate || Motion.Off) { second.Opacity = 1; return; }
            var move = new TranslateTransform(-4, 0);
            second.RenderTransform = move;
            Anim.To(second, UIElement.OpacityProperty, 1, Motion.Ms(260), from: 0);
            Anim.To(move, TranslateTransform.XProperty, 0, Motion.Ms(320), ease: Anim.Spring);
        }

        /// <summary>Синий цвет галочек, заметный на пузыре любого цвета (на синем пузыре — светло-голубой).</summary>
        static Color ReadTickColor(FrameworkElement on)
        {
            var bg = (on.TryFindResource("ChatMine") as SolidColorBrush)?.Color ?? Color.FromRgb(0x4A, 0x97, 0xFF);
            if (UI.IsLight(bg)) return Color.FromRgb(0x1A, 0x8C, 0xFF);
            if (bg.B > bg.R + 40 && bg.B >= bg.G) return Color.FromRgb(0xA8, 0xED, 0xFF);
            return Color.FromRgb(0x4F, 0xC3, 0xF7);
        }

        static void MarkSent(TextBlock status)
        {
            if (status == null) return;
            status.Text = IconCheck;
            Anim.To(status, UIElement.OpacityProperty, 0.85, Motion.Ms(300), from: 0.2);
        }

        /// <summary>
        /// Добавляет сообщение в конец ленты. fly — как в Telegram: лента плавно уезжает вверх,
        /// а новое сообщение выезжает снизу, от поля ввода. Без fly просто проявляется на месте.
        /// </summary>
        void Append(FrameworkElement bubble, bool fly, bool mine)
        {
            var sv = _messagesScroll;
            var feed = _messages;
            if (!fly || Motion.Off || !sv.IsLoaded)
            {
                feed.Children.Add(bubble);
                Anim.To(bubble, UIElement.OpacityProperty, 1, Motion.Ms(260), from: 0);
                return;
            }

            var shift = (TranslateTransform)feed.RenderTransform;
            double oldOffset = sv.VerticalOffset;
            double curShift = shift.Y;                     // если прошлая анимация ещё идёт — продолжаем с текущего места
            feed.Children.Add(bubble);
            sv.UpdateLayout();
            double newOffset = sv.ScrollableHeight;
            sv.ScrollToVerticalOffset(newOffset);
            // Лента визуально остаётся где была и затем плавно доезжает (если были далеко вверху — не дольше одного экрана)
            double start = Math.Min(curShift + (newOffset - oldOffset), sv.ViewportHeight);

            // Где сообщение будет стоять в окне ленты и сколько ему ещё ехать от нижнего края (там поле ввода)
            double y = bubble.TranslatePoint(new Point(0, 0), feed).Y + feed.Margin.Top - newOffset;
            double extra = Math.Max(0, sv.ViewportHeight - (y + start));

            var move = new TranslateTransform();
            var scale = new ScaleTransform(0.9, 0.9);
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(move);
            bubble.RenderTransformOrigin = new Point(mine ? 1 : 0, 1);
            bubble.RenderTransform = group;

            double ms = Motion.Ms(440);
            Anim.To(shift, TranslateTransform.YProperty, 0, ms, from: start);
            if (extra > 0) Anim.To(move, TranslateTransform.YProperty, 0, ms, from: extra);
            Anim.To(scale, ScaleTransform.ScaleXProperty, 1, Motion.Ms(520), ease: Anim.Spring);
            Anim.To(scale, ScaleTransform.ScaleYProperty, 1, Motion.Ms(520), ease: Anim.Spring);
            Anim.To(bubble, UIElement.OpacityProperty, 1, Motion.Ms(200), from: 0);
        }

        FrameworkElement Bubble(ChatMessage m, bool inGroup, bool pending = false)
        {
            bool mine = m.Sender == Supa.Me;
            if (m.Kind == "group_invite") return InviteCard(m);
            if (m.Kind == "group_added") return AddedCard(m);

            // 0.11.6: пузыри как на макете — свои с бликом и светлой кромкой, чужие — стекло с кромкой
            var corners = mine ? new CornerRadius(20, 20, 6, 20) : new CornerRadius(20, 20, 20, 6);
            var b = new Border
            {
                CornerRadius = corners, BorderThickness = new Thickness(1),
                Margin = new Thickness(mine ? 80 : 0, 4, mine ? 0 : 80, 4),
                HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left, MaxWidth = 460
            };
            b.SetResourceReference(Border.BackgroundProperty, mine ? "ChatMine" : "ChatOther");
            if (mine) b.BorderBrush = new SolidColorBrush(Color.FromArgb(0x48, 255, 255, 255));
            else b.SetResourceReference(Border.BorderBrushProperty, "CardRim");

            var sp = new StackPanel();
            if (inGroup && !mine)
            {
                // В группе над чужим сообщением — кто написал. Ник подгружается, даже если человека уже нет в группе.
                // Клик по нику — профиль.
                var author = UI.Accent(_groupPeople.TryGetValue(m.Sender ?? "", out var who) ? who.Nickname : " ", 12, true);
                author.Margin = new Thickness(0, 0, 0, 2);
                author.Cursor = Cursors.Hand;
                author.HorizontalAlignment = HorizontalAlignment.Left;
                string senderId = m.Sender;
                author.MouseLeftButtonUp += (s, e) => { e.Handled = true; OpenSender(senderId, null); };
                if (who == null) AwaitAuthor(senderId, p => author.Text = p.Nickname);
                sp.Children.Add(author);
            }
            if (m.Kind == "preset")
            {
                var icon = new TextBlock { Text = "", FontFamily = (FontFamily)UI.Owner.FindResource("Icons"), FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
                icon.SetResourceReference(TextBlock.ForegroundProperty, mine ? "ChatMineText" : "Accent");
                var name = new TextBlock { Text = m.Body, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
                name.SetResourceReference(TextBlock.ForegroundProperty, mine ? "ChatMineText" : "Text");
                sp.Children.Add(UI.Row(icon, Pad(name, 8)));
                var sub = new TextBlock { Text = Loc.T("chats.preset"), FontSize = 12, Opacity = mine ? 0.75 : 1, Margin = new Thickness(0, 2, 0, 0) };
                sub.SetResourceReference(TextBlock.ForegroundProperty, mine ? "ChatMineText" : "SubText");
                sp.Children.Add(sub);
                if (!mine)
                {
                    string url = (string)m.Payload?["url"];
                    var install = UI.Primary("chats.install", null);
                    install.Margin = new Thickness(0, 10, 0, 2);
                    install.Click += async (s, e) => await InstallShared(url, install);
                    sp.Children.Add(install);
                }
            }
            else
            {
                var t = new TextBlock { Text = m.Body, TextWrapping = TextWrapping.Wrap, FontSize = 14 };
                t.SetResourceReference(TextBlock.ForegroundProperty, mine ? "ChatMineText" : "Text");
                sp.Children.Add(t);
            }
            var time = new TextBlock { Text = m.Created.ToString("HH:mm"), FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
            time.SetResourceReference(TextBlock.ForegroundProperty, mine ? "ChatMineText" : "SubText");
            var timeRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 3, 0, 0) };
            timeRow.Children.Add(time);
            if (mine)
            {
                // Часики — отправляется, одна галочка — доставлено, две — прочитано (в группе — хоть одним участником)
                var icons = (FontFamily)UI.Owner.FindResource("Icons");
                var status = new TextBlock
                {
                    Text = pending ? IconClock : IconCheck, FontFamily = icons, FontSize = 10.5,
                    Opacity = pending ? 0.6 : 0.85, VerticalAlignment = VerticalAlignment.Center
                };
                var second = new TextBlock { Text = IconCheck, FontFamily = icons, FontSize = 10.5, Opacity = 0, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                status.SetResourceReference(TextBlock.ForegroundProperty, "ChatMineText");
                second.SetResourceReference(TextBlock.ForegroundProperty, "ChatMineText");
                status.Tag = second;
                // Место под вторую галочку занято сразу, чтобы время не прыгало, когда она появится
                var ticks = new Grid { Margin = new Thickness(5, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                ticks.Children.Add(status);
                ticks.Children.Add(second);
                timeRow.Children.Add(ticks);
                b.Tag = status;
                if (!pending && m.Kind != "group_invite" && m.Kind != "group_added")
                {
                    if (m.Read) MarkRead(status, false);
                    else if (m.Id > 0) _unreadTicks[m.Id] = status;
                }
            }
            sp.Children.Add(timeRow);
            sp.Margin = new Thickness(15, 10, 14, 8);
            var layers = new Grid();
            if (mine)
                layers.Children.Add(new Border
                {
                    CornerRadius = corners, IsHitTestVisible = false,
                    Background = new LinearGradientBrush(new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0x40, 255, 255, 255), 0),
                        new GradientStop(Color.FromArgb(0x00, 255, 255, 255), 0.6),
                        new GradientStop(Color.FromArgb(0x22, 0, 0, 0x30), 1)
                    }, new Point(0, 0), new Point(1, 1))
                });
            layers.Children.Add(sp);
            b.Child = layers;
            if (!inGroup || mine) return b;

            // Чужое сообщение в группе — аватарка слева, как в Telegram. Клик — профиль.
            var avHost = new Border { Width = 30, Height = 30, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 8, 4), Cursor = Cursors.Hand };
            string sid = m.Sender;
            void SetAvatar(Profile p) => avHost.Child = UI.Avatar(p, 30);
            if (_groupPeople.TryGetValue(sid ?? "", out var known)) SetAvatar(known);
            else { SetAvatar(new Profile { Nickname = " " }); AwaitAuthor(sid, SetAvatar); }
            avHost.MouseLeftButtonUp += (s, e) => { e.Handled = true; OpenSender(sid, avHost); };
            b.Margin = new Thickness(0, 3, 80, 3);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
            row.Children.Add(avHost);
            row.Children.Add(b);
            return row;
        }

        // ================== авторы сообщений в группах ==================

        readonly Dictionary<string, List<Action<Profile>>> _authorWaiters = new Dictionary<string, List<Action<Profile>>>();

        /// <summary>Ник и аватарка автора появятся, как только узнаем его профиль (из списка участников или отдельным запросом).</summary>
        void AwaitAuthor(string id, Action<Profile> apply)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (_groupPeople.TryGetValue(id, out var known)) { apply(known); return; }
            if (_authorWaiters.TryGetValue(id, out var list)) { list.Add(apply); return; }
            _authorWaiters[id] = new List<Action<Profile>> { apply };
            _ = Load();
            async Task Load()
            {
                await Task.Delay(350);   // обычно за это время приходит список участников группы
                var who = _groupPeople.TryGetValue(id, out var k) ? k : await SocialApi.GetProfile(id);
                if (who != null) _groupPeople[id] = who;
                if (!_authorWaiters.Remove(id, out var acts)) return;
                if (who != null) foreach (var a in acts) { try { a(who); } catch (Exception e) { Settings.Log(e); } }
            }
        }

        /// <summary>Открыть профиль автора сообщения (с «перелётом» аватарки, если кликнули по ней).</summary>
        async void OpenSender(string id, FrameworkElement avatar)
        {
            if (string.IsNullOrEmpty(id)) return;
            var p = _groupPeople.TryGetValue(id, out var k) ? k : await SocialApi.GetProfile(id);
            if (p == null) return;
            _groupPeople[id] = p;
            if (avatar != null) Motion.Capture(avatar, "avatar:" + id);
            await OpenProfile(p);
        }

        /// <summary>«Тебя добавили в группу» от бота MizuEngine — с кнопкой «Открыть».</summary>
        FrameworkElement AddedCard(ChatMessage m)
        {
            long gid = (long?)m.Payload?["group_id"] ?? 0;
            string name = (string)m.Payload?["name"] ?? m.Body;
            var card = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(0, 6, 80, 6), HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 420, BorderThickness = new Thickness(1) };
            card.SetResourceReference(Border.BackgroundProperty, "HoverBg");
            card.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            var sp = new StackPanel();
            sp.Children.Add(UI.IosRow(UI.Squircle("", UI.Teal, 34), UI.T(string.Format(Loc.T("groups.you_were_added"), name), 14, true, wrap: true)));
            var open = UI.Primary("groups.open", null);
            open.Margin = new Thickness(0, 10, 0, 0);
            open.HorizontalAlignment = HorizontalAlignment.Left;
            open.Click += async (s, e) => { _groupOpen = gid; _chatWith = null; _lastMessageId = 0; await ShowChats(); };
            if (gid > 0) sp.Children.Add(open);
            card.Child = sp;
            return card;
        }

        /// <summary>Приглашение в группу от бота MizuEngine: «Принять» / «Отклонить».</summary>
        FrameworkElement InviteCard(ChatMessage m)
        {
            long gid = (long?)m.Payload?["group_id"] ?? 0;
            string name = (string)m.Payload?["name"] ?? m.Body;

            var card = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(0, 6, 80, 6), HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 420, BorderThickness = new Thickness(1) };
            card.SetResourceReference(Border.BackgroundProperty, "HoverBg");
            card.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            var sp = new StackPanel();
            var title = UI.T(string.Format(Loc.T("groups.invited"), name), 14, true, wrap: true);
            sp.Children.Add(UI.IosRow(UI.Squircle("", UI.Teal, 34), title));
            var state = UI.T("", 12.5, sub: true);
            state.Margin = new Thickness(0, 6, 0, 0);
            var accept = UI.Primary("groups.accept", null);
            var decline = UI.Glass(null, "groups.decline", null, 8);
            decline.Margin = new Thickness(8, 0, 0, 0);
            var buttons = UI.Row(accept, decline);
            buttons.Margin = new Thickness(0, 10, 0, 0);
            buttons.Visibility = Visibility.Collapsed;
            sp.Children.Add(buttons);
            sp.Children.Add(state);
            card.Child = sp;

            async Task Refresh()
            {
                string st = gid > 0 ? await SocialApi.GroupStatus(gid) : null;
                buttons.Visibility = st == "invited" ? Visibility.Visible : Visibility.Collapsed;
                state.Text = st == "joined" ? Loc.T("groups.you_joined") : st == "invited" ? "" : Loc.T("groups.invite_gone");
            }
            async Task Respond(bool yes)
            {
                accept.IsEnabled = decline.IsEnabled = false;
                string err = await SocialApi.RespondInvite(gid, yes);
                accept.IsEnabled = decline.IsEnabled = true;
                if (err != null) { _toast(err); return; }
                if (yes)
                {
                    _toast(Loc.T("groups.joined"));
                    _groupOpen = gid;
                    await ShowChats();
                }
                else await Refresh();
            }
            accept.Click += async (s, e) => await Respond(true);
            decline.Click += async (s, e) => await Respond(false);
            _ = Refresh();
            Motion.Pop(card, 0.9);
            return card;
        }

        async Task InstallShared(string url, Button button)
        {
            if (string.IsNullOrEmpty(url)) return;
            button.IsEnabled = false;
            byte[] data = await Supa.Download(url);
            if (data == null) { button.IsEnabled = true; _toast(Loc.T("err.network")); return; }
            string tmp = Path.Combine(Path.GetTempPath(), $"mizuengine-{Guid.NewGuid():N}.mizupreset");
            File.WriteAllBytes(tmp, data);
            var preset = Presets.Import(tmp);
            try { File.Delete(tmp); } catch { }
            if (preset == null) { button.IsEnabled = true; _toast(Loc.T("presets.bad_file")); return; }
            _applyPreset(preset);
            button.SetResourceReference(ContentControl.ContentProperty, "chats.installed");
        }

        // ================== создание группы и приглашения ==================

        void ShowCreateGroup()
        {
            _poll.Stop();
            _chatWith = null;
            _groupOpen = 0;
            SelectRow(null);
            ShowInfo(null, null);
            var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            sp.Children.Add(UI.Row(UI.Squircle("", UI.Teal, 34), Pad(UI.L("groups.new", 20, true), 12)));

            var name = UI.Field("", 48);
            sp.Children.Add(UI.Labeled("groups.name", name));

            var unlimited = new CheckBox { Style = (Style)UI.Owner.FindResource("Switch"), IsChecked = true, Margin = new Thickness(0, 14, 0, 0) };
            unlimited.SetResourceReference(ContentControl.ContentProperty, "groups.unlimited");
            sp.Children.Add(unlimited);
            var max = UI.Field("50", 4);
            var maxRow = UI.Labeled("groups.max", max);
            maxRow.Visibility = Visibility.Collapsed;
            sp.Children.Add(maxRow);
            unlimited.Click += (s, e) => maxRow.Visibility = unlimited.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;

            var (picker, chosen) = InvitePicker(null);
            sp.Children.Add(picker);

            var error = UI.Error();
            sp.Children.Add(error);
            var create = UI.Primary("groups.create_btn", null);
            create.Margin = new Thickness(0, 16, 0, 0);
            sp.Children.Add(create);
            create.Click += async (s, e) =>
            {
                UI.ShowError(error, null);
                string n = name.Text.Trim();
                if (n.Length == 0) { UI.ShowError(error, Loc.T("err.group_name")); return; }
                int? limit = null;
                if (unlimited.IsChecked != true)
                {
                    if (!int.TryParse(max.Text.Trim(), out int v) || v < 2 || v > 1000) { UI.ShowError(error, Loc.T("err.group_max")); return; }
                    limit = v;
                }
                create.IsEnabled = false;
                var (id, err) = await SocialApi.CreateGroup(n, limit);
                if (err != null) { create.IsEnabled = true; UI.ShowError(error, err); return; }
                foreach (var who in chosen)
                {
                    var (_, ie) = await SocialApi.AddToGroup(id, who.Id);   // создатель группы добавляет сразу (если человек не против)
                    if (ie != null) _toast($"@{who.Handle}: {ie}");
                }
                _toast(Loc.T("groups.created"));
                _groupOpen = id;
                await ShowChats();
            };
            SetPanel(UI.Scroll(sp));
        }

        void ShowInvite(Group g)
        {
            _poll.Stop();
            var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            var back = UI.Glass("", "common.back", (s, e) => OpenGroup(g), 8);
            sp.Children.Add(back);
            // Владелец группы (и владелец MizuEngine) добавляет сразу; кто выбрал «только по приглашению» — получит приглашение.
            // Обычный участник только приглашает.
            bool canAdd = g.Owner == Supa.Me || SocialApi.MyProfile?.Number == 1;
            var title = UI.T(string.Format(Loc.T(canAdd ? "groups.add_to" : "groups.invite_to"), g.Name), 20, true, wrap: true);
            title.Margin = new Thickness(0, 14, 0, 0);
            sp.Children.Add(title);
            if (canAdd)
            {
                var hint = UI.L("groups.add_hint", 12.5, sub: true, wrap: true);
                hint.Margin = new Thickness(0, 4, 0, 0);
                sp.Children.Add(hint);
            }
            var (picker, chosen) = InvitePicker(g.Id);
            sp.Children.Add(picker);
            var send = UI.Primary(canAdd ? "groups.add_people" : "groups.send_invites", null);
            send.Margin = new Thickness(0, 16, 0, 0);
            sp.Children.Add(send);
            send.Click += async (s, e) =>
            {
                if (chosen.Count == 0) return;
                send.IsEnabled = false;
                int added = 0, invited = 0;
                foreach (var who in chosen)
                {
                    var (wasAdded, err) = await SocialApi.AddToGroup(g.Id, who.Id);
                    if (err != null) _toast($"@{who.Handle}: {err}");
                    else if (wasAdded) added++;
                    else invited++;
                }
                send.IsEnabled = true;
                if (added > 0 && invited > 0) _toast(string.Format(Loc.T("groups.added_and_invited"), added, invited));
                else if (added > 0) _toast(string.Format(Loc.T("groups.added"), added));
                else if (invited > 0) _toast(string.Format(Loc.T("groups.invites_sent"), invited));
                OpenGroup(g);
            };
            SetPanel(UI.Scroll(sp));
        }

        /// <summary>Выбор людей: друзья галочками и любой человек по @юзу. Возвращает список выбранных (обновляется на лету).</summary>
        (FrameworkElement ui, List<Profile> chosen) InvitePicker(long? groupId)
        {
            var chosen = new List<Profile>();
            var sp = new StackPanel();
            sp.Children.Add(UI.Section("groups.invite_people", 18));

            // По @юзу
            var byHandle = new Grid();
            byHandle.ColumnDefinitions.Add(new ColumnDefinition());
            byHandle.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var handle = UI.Field("", 30);
            handle.Tag = "@handle";
            var add = UI.Glass("", "groups.add", null, 8);
            add.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(add, 1);
            byHandle.Children.Add(handle);
            byHandle.Children.Add(add);
            sp.Children.Add(byHandle);

            var picked = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            sp.Children.Add(picked);
            var friendsList = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            sp.Children.Add(friendsList);

            var boxes = new Dictionary<string, CheckBox>();
            void Repaint()
            {
                picked.Children.Clear();
                foreach (var p in chosen)
                {
                    var who = p;
                    var chip = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(6, 4, 10, 4), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand };
                    chip.SetResourceReference(Border.BackgroundProperty, "HoverBg");
                    chip.Child = UI.Row(UI.Avatar(who, 22), Pad(UI.T(who.Nickname, 12.5, true), 6), Pad(UI.Icon("", 9), 6));
                    chip.ToolTip = Loc.T("groups.remove_chip");
                    chip.MouseLeftButtonUp += (s, e) =>
                    {
                        chosen.Remove(who);
                        if (boxes.TryGetValue(who.Id, out var cb)) cb.IsChecked = false;
                        Repaint();
                    };
                    picked.Children.Add(chip);
                }
            }
            void Toggle(Profile p, bool on)
            {
                chosen.RemoveAll(x => x.Id == p.Id);
                if (on) chosen.Add(p);
                Repaint();
            }

            async Task AddHandle()
            {
                var p = await SocialApi.FindByHandle(handle.Text);
                if (p == null) { _toast(Loc.T("groups.not_found")); return; }
                if (p.Id == Supa.Me) return;
                handle.Text = "";
                Toggle(p, true);
                if (boxes.TryGetValue(p.Id, out var cb)) cb.IsChecked = true;
            }
            add.Click += async (s, e) => await AddHandle();
            handle.KeyDown += async (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; await AddHandle(); } };

            _ = Fill();
            async Task Fill()
            {
                friendsList.Children.Add(UI.Loading());
                var friends = await SocialApi.Friends(Supa.Me);
                var already = new HashSet<string>();
                if (groupId.HasValue) foreach (var m in await SocialApi.GroupMembers(groupId.Value)) already.Add(m.User.Id);
                friendsList.Children.Clear();
                foreach (var f in friends)
                {
                    if (already.Contains(f.Id)) continue;
                    var fr = f;
                    var cb = new CheckBox { Style = (Style)UI.Owner.FindResource("Switch"), IsHitTestVisible = false, Width = 52 };
                    boxes[fr.Id] = cb;
                    friendsList.Children.Add(UI.UserRow(fr, () => { cb.IsChecked = cb.IsChecked != true; Toggle(fr, cb.IsChecked == true); }, cb));
                }
                if (friendsList.Children.Count == 0) friendsList.Children.Add(Empty("groups.no_friends_left"));
                Motion.Stagger(friendsList);
            }
            return (sp, chosen);
        }

        void ShowMembers(Group g)
        {
            _poll.Stop();
            var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            sp.Children.Add(UI.Glass("", "common.back", (s, e) => OpenGroup(g), 8));
            var title = UI.T(g.Name, 20, true);
            title.Margin = new Thickness(0, 14, 0, 6);
            sp.Children.Add(title);
            var list = new StackPanel();
            sp.Children.Add(list);
            SetPanel(UI.Scroll(sp));

            _ = Fill();
            async Task Fill()
            {
                list.Children.Clear();
                list.Children.Add(UI.Loading());
                var members = await SocialApi.GroupMembers(g.Id);
                list.Children.Clear();
                foreach (var m in members)
                {
                    var mem = m;
                    var trailing = new List<UIElement>();
                    if (mem.User.Id == g.Owner) trailing.Add(UI.L("groups.owner", 12, sub: true));
                    else if (!mem.Joined) trailing.Add(UI.L("groups.pending", 12, sub: true));
                    if (g.Owner == Supa.Me && mem.User.Id != Supa.Me)
                    {
                        var kick = SmallButton("groups.kick", false);
                        kick.Click += async (s, e) =>
                        {
                            e.Handled = true;
                            string err = await SocialApi.Kick(g.Id, mem.User.Id);
                            if (err != null) _toast(err); else await Fill();
                        };
                        trailing.Add(kick);
                    }
                    list.Children.Add(UI.UserRow(mem.User, () => _ = OpenProfile(mem.User), trailing.ToArray()));
                }
                Motion.Stagger(list);
            }
        }

        // ================== темы чатов ==================

        /// <summary>
        /// Ставит тему на открытый чат. С фоном чат становится «тёмным стеклом» поверх картинки:
        /// белый текст, полупрозрачные поля и пузыри — так всё читается на любом фоне и в любой теме приложения.
        /// </summary>
        void ApplyTheme(ChatTheme t, bool animate = true)
        {
            if (_chatRoot == null || _themeLayer == null) return;
            _theme = t?.Copy() ?? new ChatTheme();
            var brush = UI.ThemeBrush(_theme);
            bool themed = brush != null;
            var res = _chatRoot.Resources;
            var app = UI.Owner;

            if (themed)
            {
                _themeLayer.Background = brush;
                // На своей картинке — лёгкое затемнение, чтобы пузыри не терялись
                _themeLayer.Child = _theme.Bg == "custom" ? new Border { CornerRadius = new CornerRadius(23), Background = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)) } : null;
                res["Text"] = Brushes.White;
                res["SubText"] = new SolidColorBrush(Color.FromArgb(0xB8, 255, 255, 255));
                res["CardBg"] = new SolidColorBrush(Color.FromArgb(0x48, 0x0C, 0x0D, 0x12));
                res["CardRim"] = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255));
                res["HoverBg"] = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255));
                res["Divider"] = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255));
                res["ChatOther"] = new SolidColorBrush(Color.FromArgb(0xC8, 0x1A, 0x1B, 0x22));
            }
            else
            {
                foreach (var k in new[] { "Text", "SubText", "CardBg", "CardRim", "HoverBg", "Divider" }) res.Remove(k);
                res["ChatOther"] = app.FindResource("HoverBg");
            }
            var bubble = UI.ParseColor(_theme.Bubble);
            res["ChatMine"] = bubble.HasValue ? new SolidColorBrush(bubble.Value) : app.FindResource("Accent");
            res["ChatMineText"] = bubble.HasValue && UI.IsLight(bubble.Value) ? new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x16)) : Brushes.White;

            double to = themed ? 1 : 0;
            if (animate) Anim.To(_themeLayer, UIElement.OpacityProperty, to, Motion.Ms(420));
            else _themeLayer.Opacity = to;
        }

        /// <summary>Тема могла поменяться у собеседника — подтягиваем. first — сразу после открытия чата.</summary>
        async Task ReloadTheme(bool first)
        {
            var source = _themeSource;
            var root = _chatRoot;
            if (source == null) return;
            ChatTheme t;
            try { t = await source(); } catch (Exception ex) { Settings.Log(ex); return; }
            if (t == null || root != _chatRoot || source != _themeSource) return;   // нет сети или чат уже другой
            if (!first && t.ToJson().ToJsonString() == _theme.ToJson().ToJsonString()) return;
            ApplyTheme(t);
        }

        /// <summary>Выбор темы: готовые фоны, своя картинка, цвет своих сообщений и живой пример.</summary>
        void ShowThemePicker(ChatTheme current, Func<ChatTheme, Task<string>> save, Action back)
        {
            _poll.Stop();
            var t = current?.Copy() ?? new ChatTheme();
            var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            sp.Children.Add(UI.Glass("", "common.back", (s, e) => back(), 8));
            var title = UI.Row(UI.Squircle("", UI.Pink, 34), Pad(UI.L("theme.title", 20, true), 12));
            title.Margin = new Thickness(0, 14, 0, 0);
            sp.Children.Add(title);
            var hint = UI.L("theme.d", 12.5, sub: true, wrap: true);
            hint.Margin = new Thickness(0, 6, 0, 0);
            sp.Children.Add(hint);

            // ----- живой пример -----
            var preview = new Grid { Height = 170, Margin = new Thickness(0, 16, 0, 0), MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left, Width = 560 };
            var pvBg = new Border { CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1) };
            pvBg.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            var pvShade = new Border { CornerRadius = new CornerRadius(18) };
            var pvList = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16, 0, 16, 14) };
            preview.Children.Add(pvBg);
            preview.Children.Add(pvShade);
            preview.Children.Add(pvList);
            sp.Children.Add(preview);

            FrameworkElement PvBubble(string text, bool mine, Brush bg, Brush fg) => new Border
            {
                CornerRadius = mine ? new CornerRadius(16, 16, 5, 16) : new CornerRadius(16, 16, 16, 5), Padding = new Thickness(12, 7, 12, 8),
                Margin = new Thickness(mine ? 60 : 0, 3, mine ? 0 : 60, 3), HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Background = bg, Child = new TextBlock { Text = text, Foreground = fg, FontSize = 13.5, TextWrapping = TextWrapping.Wrap }
            };

            var tiles = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            var swatches = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            var tileRings = new List<(Border ring, string key)>();
            var swatchRings = new List<(Border ring, string hex)>();

            void Paint()
            {
                var brush = UI.ThemeBrush(t, 900);
                bool themed = brush != null;
                if (themed) pvBg.Background = brush; else pvBg.SetResourceReference(Border.BackgroundProperty, "CardBg");
                pvShade.Background = t.Bg == "custom" ? new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)) : null;
                var bc = UI.ParseColor(t.Bubble);
                Brush mineBg = bc.HasValue ? new SolidColorBrush(bc.Value) : (Brush)UI.Owner.FindResource("Accent");
                Brush mineFg = bc.HasValue && UI.IsLight(bc.Value) ? new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x16)) : Brushes.White;
                Brush otherBg = themed ? new SolidColorBrush(Color.FromArgb(0xC8, 0x1A, 0x1B, 0x22)) : (Brush)UI.Owner.FindResource("HoverBg");
                Brush otherFg = themed ? Brushes.White : (Brush)UI.Owner.FindResource("Text");
                pvList.Children.Clear();
                pvList.Children.Add(PvBubble(Loc.T("theme.sample1"), false, otherBg, otherFg));
                pvList.Children.Add(PvBubble(Loc.T("theme.sample2"), true, mineBg, mineFg));
                foreach (var (ring, key) in tileRings)
                    if (key == (t.Bg ?? "none")) ring.SetResourceReference(Border.BorderBrushProperty, "Accent"); else ring.BorderBrush = Brushes.Transparent;
                foreach (var (ring, hex) in swatchRings)
                    if (hex == (t.Bubble ?? "")) ring.SetResourceReference(Border.BorderBrushProperty, "Text"); else ring.BorderBrush = Brushes.Transparent;
            }

            // ----- фоны -----
            sp.Children.Add(UI.Section("theme.backgrounds", 18));
            Border Tile(string key, Brush bg, UIElement content, Action click)
            {
                var inner = new Border { Width = 96, Height = 64, CornerRadius = new CornerRadius(12), Background = bg, Child = content, ClipToBounds = true };
                if (bg == null) inner.SetResourceReference(Border.BackgroundProperty, "HoverBg");
                var ring = new Border { CornerRadius = new CornerRadius(15), BorderThickness = new Thickness(2.5), Padding = new Thickness(2), Margin = new Thickness(0, 0, 8, 8), Child = inner, Cursor = Cursors.Hand, Background = Brushes.Transparent };
                ring.MouseLeftButtonUp += (s, e) => { click(); Motion.Pop(ring, 0.9); Paint(); };
                tileRings.Add((ring, key));
                tiles.Children.Add(ring);
                return inner;
            }
            FrameworkElement TileLabel(string key, string glyph)
            {
                var spl = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var ic = UI.Icon(glyph, 16);
                ic.HorizontalAlignment = HorizontalAlignment.Center;
                spl.Children.Add(ic);
                var l = UI.L(key, 11, true);
                l.HorizontalAlignment = HorizontalAlignment.Center;
                l.Margin = new Thickness(0, 3, 0, 0);
                spl.Children.Add(l);
                return spl;
            }
            Tile("none", null, TileLabel("theme.none", ""), () => { t.Bg = null; t.Img = null; });
            foreach (var (key, _) in UI.ThemeBackgrounds)
            {
                string k = key;
                Tile(k, UI.ThemeBrush(new ChatTheme { Bg = k }), null, () => { t.Bg = k; t.Img = null; });
            }
            Border customTile = null;
            customTile = Tile("custom", t.Bg == "custom" ? UI.ThemeBrush(t, 200) : null, TileLabel("theme.custom", ""), async () =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp" };
                if (dlg.ShowDialog() != true) return;
                if (new FileInfo(dlg.FileName).Length > 25 * 1024 * 1024) { _toast(Loc.T("err.too_big")); return; }
                _toast(Loc.T("profile.uploading"));
                string url = await SocialApi.UploadImage(dlg.FileName, "theme", 1600);
                if (url == null) { _toast(Loc.T("err.upload")); return; }
                t.Bg = "custom"; t.Img = url;
                customTile.Background = UI.ThemeBrush(t, 200);
                customTile.Child = null;
                Paint();
            });
            sp.Children.Add(tiles);

            // ----- цвет своих сообщений -----
            sp.Children.Add(UI.Section("theme.bubbles", 14));
            Border Swatch(string hex, Brush fill, UIElement content)
            {
                var dot = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = fill, Child = content, BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)), BorderThickness = new Thickness(1) };
                var ring = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(19), BorderThickness = new Thickness(2), Child = dot, Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand, Background = Brushes.Transparent };
                ring.MouseLeftButtonUp += (s, e) => { t.Bubble = hex == "" ? null : hex; Motion.Pop(ring, 0.85); Paint(); };
                swatchRings.Add((ring, hex));
                swatches.Children.Add(ring);
                return dot;
            }
            var auto = new TextBlock { Text = "A", FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
            Swatch("", (Brush)UI.Owner.FindResource("Accent"), auto).ToolTip = Loc.T("theme.bubble_auto");
            foreach (var hex in UI.BubbleColors) Swatch(hex, new SolidColorBrush(UI.ParseColor(hex).Value), null);
            sp.Children.Add(swatches);

            // Свой цвет — тот же выбор, что и для плашек
            var customColor = UI.Glass("", "theme.bubble_custom", null, 8);
            customColor.Margin = new Thickness(0, 4, 0, 0);
            sp.Children.Add(customColor);
            var pickerHost = new Border { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0), MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(pickerHost);
            customColor.Click += (s, e) =>
            {
                if (pickerHost.Visibility == Visibility.Visible) { pickerHost.Visibility = Visibility.Collapsed; return; }
                var (picker, _) = UI.ColorPicker(UI.ParseColor(t.Bubble) ?? Color.FromRgb(0x0A, 0x84, 0xFF), c => { t.Bubble = UI.Hex(c); Paint(); });
                pickerHost.Child = picker;
                pickerHost.Visibility = Visibility.Visible;
                Motion.Enter(picker);
            };

            var error = UI.Error();
            sp.Children.Add(error);
            var saveBtn = UI.Primary("theme.save", null);
            var reset = UI.Glass("", "theme.reset", null, 9);
            reset.Margin = new Thickness(8, 0, 0, 0);
            var row = UI.Row(saveBtn, reset);
            row.Margin = new Thickness(0, 18, 0, 0);
            sp.Children.Add(row);

            async Task Save(ChatTheme theme)
            {
                saveBtn.IsEnabled = reset.IsEnabled = false;
                string err = await save(theme);
                saveBtn.IsEnabled = reset.IsEnabled = true;
                if (err != null) { UI.ShowError(error, err); return; }
                _toast(Loc.T("theme.saved"));
                back();
            }
            saveBtn.Click += async (s, e) => await Save(t);
            reset.Click += async (s, e) => await Save(new ChatTheme());

            Paint();
            SetPanel(UI.Scroll(sp));
        }

        // ================== настройки группы ==================

        /// <summary>Владелец: аватарка, название, @юз, тема. Админ: галочка группы.</summary>
        void ShowGroupSettings(Group g)
        {
            _poll.Stop();
            bool owner = g.Owner == Supa.Me;
            bool admin = SocialApi.MyProfile?.IsAdmin == true;
            var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 20), MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(UI.Glass("", "common.back", (s, e) => OpenGroup(g), 8));

            // Аватарка: нажми, чтобы сменить
            var avHost = new Grid { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 16, 0, 0) };
            void PaintAvatar()
            {
                avHost.Children.Clear();
                avHost.Children.Add(UI.Avatar(g.Look(), 88));
                if (owner)
                {
                    var cam = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Child = UI.Icon("", 13) };
                    ((TextBlock)cam.Child).HorizontalAlignment = HorizontalAlignment.Center;
                    ((TextBlock)cam.Child).Foreground = Brushes.White;
                    cam.SetResourceReference(Border.BackgroundProperty, "Accent");
                    avHost.Children.Add(cam);
                }
            }
            PaintAvatar();
            if (owner)
            {
                UI.Hand(avHost);
                avHost.ToolTip = Loc.T("groups.avatar");
                avHost.MouseLeftButtonUp += async (s, e) =>
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp" };
                    if (dlg.ShowDialog() != true) return;
                    if (new FileInfo(dlg.FileName).Length > 25 * 1024 * 1024) { _toast(Loc.T("err.too_big")); return; }
                    _toast(Loc.T("profile.uploading"));
                    string url = await SocialApi.UploadImage(dlg.FileName, "group-" + g.Id, 512);
                    if (url == null) { _toast(Loc.T("err.upload")); return; }
                    string err = await SocialApi.UpdateGroup(g.Id, new System.Text.Json.Nodes.JsonObject { ["avatar_url"] = url });
                    if (err != null) { _toast(err); return; }
                    g.AvatarUrl = url;
                    PaintAvatar();
                    RefreshGroupRow(g);
                    Motion.Pop(avHost, 0.9);
                    _toast(Loc.T("profile.saved"));
                };
            }
            sp.Children.Add(avHost);
            var head = UI.Row(UI.T(g.Name, 20, true));
            if (g.Badge != null) head.Children.Add(Pad(UI.AnimatedBadge(20, g.Badge), 8));
            head.Margin = new Thickness(0, 12, 0, 0);
            sp.Children.Add(head);

            if (owner)
            {
                var name = UI.Field(g.Name, 48);
                var handle = UI.Field(g.Handle ?? "", 24);
                handle.Tag = "@handle";
                sp.Children.Add(UI.Labeled("groups.name", name));
                sp.Children.Add(UI.Labeled("groups.handle", handle));
                var left = g.HandleChangedAt + (IsStaff ? TimeSpan.FromSeconds(5) : TimeSpan.FromHours(24)) - DateTime.UtcNow;
                var hint = !string.IsNullOrEmpty(g.Handle) && !IsStaff && left > TimeSpan.Zero
                    ? UI.T(string.Format(Loc.T("profile.handle.wait"), (int)left.TotalHours, left.Minutes), 12, sub: true)
                    : UI.L(IsStaff ? "profile.handle.staff" : "groups.handle.d", 12, sub: true, wrap: true);
                hint.Margin = new Thickness(2, 6, 0, 0);
                sp.Children.Add(hint);

                var error = UI.Error();
                sp.Children.Add(error);
                var save = UI.Primary("profile.save", null);
                save.Margin = new Thickness(0, 14, 0, 0);
                sp.Children.Add(save);
                save.Click += async (s, e) =>
                {
                    UI.ShowError(error, null);
                    var ch = new System.Text.Json.Nodes.JsonObject();
                    string n = name.Text.Trim();
                    string h = handle.Text.Trim().TrimStart('@').ToLowerInvariant();
                    if (n.Length == 0) { UI.ShowError(error, Loc.T("err.group_name")); return; }
                    if (n != g.Name) ch["name"] = n;
                    if (h != (g.Handle ?? ""))
                    {
                        if (h.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(h, "^[a-z0-9_]{3,24}$")) { UI.ShowError(error, Loc.T("err.handle_format")); return; }
                        ch["handle"] = h.Length == 0 ? null : h;
                    }
                    if (ch.Count == 0) return;
                    save.IsEnabled = false;
                    string err = await SocialApi.UpdateGroup(g.Id, ch);
                    save.IsEnabled = true;
                    if (err != null) { UI.ShowError(error, err); return; }
                    _toast(Loc.T("profile.saved"));
                    _groupOpen = g.Id;
                    await ShowChats();
                };

                // Тема группы — её видят все участники
                sp.Children.Add(UI.Section("theme.title", 22));
                sp.Children.Add(UI.IosGroup(UI.IosRow(UI.Squircle("", UI.Pink), UI.L("groups.theme", 13.5, true), UI.Chevron(),
                    () => ShowThemePicker(g.Theme, async t =>
                    {
                        string err = await SocialApi.UpdateGroup(g.Id, new System.Text.Json.Nodes.JsonObject { ["theme"] = t.IsEmpty ? null : t.ToJson() });
                        if (err == null) g.Theme = t.Copy();
                        return err;
                    }, () => OpenGroup(g)))));
            }

            if (admin)
            {
                sp.Children.Add(UI.Section("admin.badge", 22));
                var row = new WrapPanel();
                foreach (var k in BadgeKinds)
                {
                    string kind = k;
                    var r = new RadioButton { Style = (Style)UI.Owner.FindResource("Pill"), GroupName = "gs" + g.Id, IsChecked = g.Badge == kind };
                    var label = UI.L(kind == null ? "admin.badge.none" : "admin.badge." + kind, 12.5);
                    r.Content = kind == null ? (object)label : UI.Row(UI.Badge(14, kind), Pad(label, 6));
                    r.Click += async (s, e) =>
                    {
                        if (g.Badge == kind) return;
                        string err = await SocialApi.UpdateGroup(g.Id, new System.Text.Json.Nodes.JsonObject { ["badge"] = kind });
                        if (err != null) { _toast(err); return; }
                        g.Badge = kind;
                        RefreshGroupRow(g);
                        _toast(Loc.T("profile.saved"));
                        ShowGroupSettings(g);
                    };
                    row.Children.Add(r);
                }
                sp.Children.Add(row);
            }
            SetPanel(UI.Scroll(sp));
        }
    }
}
