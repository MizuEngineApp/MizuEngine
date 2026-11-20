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

namespace MizuEngine
{
    /// <summary>Чаты: личные сообщения, группы, приглашения от бота MizuEngine.</summary>
    internal sealed partial class SocialPages
    {
        long _groupOpen;                                  // открытая группа (0 — открыт личный чат или ничего)
        Border _chatPanel;                                // правая часть страницы «Чаты»
        HashSet<string> _friendIds = new HashSet<string>();
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

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            var left = new DockPanel { Margin = new Thickness(0, 0, 18, 0) };
            var top = new Grid();
            top.Children.Add(UI.H1("nav.chats"));
            if (IsStaff)
            {
                var create = UI.Glass("", "groups.create", (s, e) => ShowCreateGroup(), 7);
                create.HorizontalAlignment = HorizontalAlignment.Right;
                create.VerticalAlignment = VerticalAlignment.Center;
                create.Margin = new Thickness(0, 0, 44, 0);
                top.Children.Add(create);
            }
            // Звуки сообщений и уведомления — открывают свой раздел в «Настройках»
            var sounds = UI.Glass("\uE767", null, (s, e) => _navigate("sounds"), 7);
            sounds.ToolTip = Loc.T("chats.sounds");
            sounds.HorizontalAlignment = HorizontalAlignment.Right;
            sounds.VerticalAlignment = VerticalAlignment.Center;
            top.Children.Add(sounds);
            DockPanel.SetDock(top, Dock.Top);
            left.Children.Add(top);
            var list = new StackPanel { Margin = new Thickness(0, 10, 8, 0) };
            left.Children.Add(UI.Scroll(list));
            grid.Children.Add(left);

            _chatPanel = new Border { CornerRadius = new CornerRadius(22), BorderThickness = new Thickness(1), ClipToBounds = true };
            _chatPanel.SetResourceReference(Border.BackgroundProperty, "CardBg");
            _chatPanel.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            Grid.SetColumn(_chatPanel, 1);
            grid.Children.Add(_chatPanel);
            Motion.Replace(_chatsHost, grid);

            list.Children.Add(UI.Loading());
            var friendsTask = SocialApi.Friends(Supa.Me);
            var unreadTask = SocialApi.UnreadCounts();
            var groupsTask = SocialApi.MyGroups();
            await Task.WhenAll(friendsTask, unreadTask, groupsTask);
            var friends = friendsTask.Result;
            var unread = unreadTask.Result;
            var groups = groupsTask.Result;
            _friendIds = new HashSet<string>(friends.Select(f => f.Id));
            var others = await SocialApi.OtherChatPartners(_friendIds);
            list.Children.Clear();

            if (groups.Count > 0)
            {
                list.Children.Add(UI.Section("groups.title", 0));
                _groupRows.Clear();
                foreach (var g in groups)
                {
                    // Строка группы в своём «слоте», чтобы после смены аватарки или галочки обновить её на месте
                    var slot = new ContentControl { Content = GroupRow(g), Focusable = false };
                    _groupRows[g.Id] = slot;
                    list.Children.Add(slot);
                }
            }

            list.Children.Add(UI.Section("chats.personal", groups.Count > 0 ? 14 : 0));
            if (friends.Count == 0 && others.Count == 0) list.Children.Add(Empty("chats.no_friends"));
            foreach (var f in others.Concat(friends))
            {
                var fr = f;
                _people[f.Id] = f;
                var trailing = new List<UIElement>();
                if (unread.TryGetValue(f.Id, out int n)) trailing.Add(UnreadDot(n));
                list.Children.Add(UI.UserRow(fr, () => OpenChat(fr), trailing.ToArray()));
            }
            Motion.Stagger(list);

            if (_groupOpen > 0 && groups.FirstOrDefault(x => x.Id == _groupOpen) is Group open) OpenGroup(open);
            else if (_chatWith != null && _people.TryGetValue(_chatWith, out var who)) OpenChat(who);
            else ShowChatHint();
        }

        readonly Dictionary<long, ContentControl> _groupRows = new Dictionary<long, ContentControl>();

        /// <summary>Строка группы слева: аватарка, название, галочка и @юз — как в шапке открытой группы.</summary>
        Button GroupRow(Group g) => UI.UserRow(g.Look(), () => OpenGroup(g), string.IsNullOrEmpty(g.Handle) ? Loc.T("groups.subtitle") : "@" + g.Handle);

        /// <summary>Перерисовать строку группы в списке слева (после смены аватарки или галочки).</summary>
        void RefreshGroupRow(Group g)
        {
            if (_groupRows.TryGetValue(g.Id, out var slot)) slot.Content = GroupRow(g);
        }

        static FrameworkElement UnreadDot(int n)
        {
            var dot = new Border { CornerRadius = new CornerRadius(10), MinWidth = 20, Padding = new Thickness(6, 1, 6, 2), Child = new TextBlock { Text = n.ToString(), Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center } };
            dot.SetResourceReference(Border.BackgroundProperty, "Accent");
            return dot;
        }

        void ShowChatHint()
        {
            var hint = UI.L("chats.pick", 14, sub: true);
            hint.HorizontalAlignment = HorizontalAlignment.Center;
            hint.VerticalAlignment = VerticalAlignment.Center;
            SetPanel(hint);
        }

        /// <summary>Содержимое правой части меняется плавно.</summary>
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

        void OpenChat(Profile friend)
        {
            _chatWith = friend.Id;
            _groupOpen = 0;
            _lastMessageId = 0;
            bool canWrite = _friendIds.Contains(friend.Id);   // писать можно только друзьям; бот MizuEngine присылает только приглашения

            // ник и галочка — в своём ряду, выровненном по центру аватарки (иначе ник прилипает к верху, а галочка «съезжает» вниз)
            var nameRow = UI.Row(UI.T(friend.Nickname, 15, true));
            nameRow.VerticalAlignment = VerticalAlignment.Center;
            nameRow.Margin = new Thickness(10, 0, 0, 0);
            if (friend.Verified) nameRow.Children.Add(Pad(UI.Badge(15, friend.Badge), 6));
            if (friend.IsPlus) nameRow.Children.Add(UI.PlusAfter(15));
            ((FrameworkElement)nameRow.Children[0]).VerticalAlignment = VerticalAlignment.Center;
            nameRow.Margin = new Thickness(0);
            var titleCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            titleCol.Children.Add(nameRow);
            if (!friend.Bot) titleCol.Children.Add(Online.Status(friend.Id, 12));   // «в сети» под ником, как в Telegram
            var who = UI.Row(friend.Bot ? UI.Avatar(friend, 38) : Online.WithDot(UI.Avatar(friend, 38), friend.Id, 38), titleCol);
            var av = (FrameworkElement)who.Children[0];
            UI.Hand(who);
            who.Background = Brushes.Transparent;
            who.HorizontalAlignment = HorizontalAlignment.Left;
            who.MouseLeftButtonUp += (s, e) => { Motion.Capture(av, "avatar:" + friend.Id); _ = OpenProfile(friend); };

            var head = new Grid();
            head.Children.Add(who);
            if (canWrite)
            {
                // Тема чата: фон и цвет сообщений видят оба
                var theme = UI.Glass("\uE790", null, (s, e) => ShowThemePicker(_theme, t => SocialApi.SetChatTheme(friend.Id, t), () => OpenChat(friend)), 7);
                theme.ToolTip = Loc.T("theme.title");
                theme.HorizontalAlignment = HorizontalAlignment.Right;
                theme.VerticalAlignment = VerticalAlignment.Center;
                head.Children.Add(theme);
            }

            _themeSource = () => SocialApi.GetChatTheme(friend.Id);
            BuildChatView(head, canWrite, async text => await SocialApi.SendText(friend.Id, text), friend);
            _ = ReloadTheme(true);
        }

        // ================== группа ==================

        void OpenGroup(Group g)
        {
            _groupOpen = g.Id;
            _chatWith = null;
            _lastMessageId = 0;

            var head = new Grid();
            var title = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var nameRow = UI.Row(UI.T(g.Name, 15, true));
            if (g.Badge != null) nameRow.Children.Add(Pad(UI.Badge(15, g.Badge), 6));
            title.Children.Add(nameRow);
            var membersText = UI.T("…", 12, sub: true);
            // «N участников» под названием — клик открывает список (видят все участники группы)
            membersText.Cursor = System.Windows.Input.Cursors.Hand;
            membersText.ToolTip = Loc.T("groups.members");
            membersText.MouseEnter += (s, e) => membersText.TextDecorations = TextDecorations.Underline;
            membersText.MouseLeave += (s, e) => membersText.TextDecorations = null;
            membersText.MouseLeftButtonUp += (s, e) => ShowMembers(g);
            title.Children.Add(membersText);
            var who = UI.Row(UI.Avatar(g.Look(), 38), title);
            head.Children.Add(who);

            bool owner = g.Owner == Supa.Me;
            var settings = UI.Glass("\uE713", null, (s, e) => ShowGroupSettings(g), 7);
            settings.ToolTip = Loc.T("groups.settings");
            settings.Margin = new Thickness(0, 0, 6, 0);
            settings.Visibility = owner || SocialApi.MyProfile?.IsAdmin == true ? Visibility.Visible : Visibility.Collapsed;
            var invite = UI.Glass("", null, (s, e) => ShowInvite(g), 7);
            invite.ToolTip = Loc.T("groups.invite");
            var members = UI.Glass("", null, (s, e) => ShowMembers(g), 7);
            members.ToolTip = Loc.T("groups.members");
            members.Margin = new Thickness(6, 0, 0, 0);
            var leave = UI.Glass(owner ? "" : "", null, async (s, e) =>
            {
                string err = owner ? await SocialApi.DeleteGroup(g.Id) : await SocialApi.LeaveGroup(g.Id);
                if (err != null) { _toast(err); return; }
                _toast(Loc.T(owner ? "groups.deleted" : "groups.left"));
                _groupOpen = 0;
                await ShowChats();
            }, 7);
            leave.ToolTip = Loc.T(owner ? "groups.delete" : "groups.leave");
            leave.Margin = new Thickness(6, 0, 0, 0);
            var buttons = UI.Row(settings, invite, members, leave);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            head.Children.Add(buttons);

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
            BuildChatView(head, true, async text =>
            {
                if (!IsStaff && DateTime.Now < _cooldownUntil) return Loc.T("err.slow_mode");
                string err = await SocialApi.SendGroupText(g.Id, text);
                if (err == null && !IsStaff) _cooldownUntil = DateTime.Now.AddSeconds(SlowModeSeconds);
                return err;
            }, null);
            ApplyTheme(g.Theme);
        }

        // ================== общее окно переписки ==================

        /// <summary>Шапка, лента сообщений и поле ввода. send возвращает текст ошибки или null. friend — для личного чата (кнопка «Сборка»).</summary>
        void BuildChatView(FrameworkElement head, bool canWrite, Func<string, Task<string>> send, Profile friend)
        {
            var dock = new DockPanel();
            head.Margin = new Thickness(18, 14, 18, 12);
            DockPanel.SetDock(head, Dock.Top);
            dock.Children.Add(head);

            var divider = UI.Divider();
            divider.Margin = new Thickness(0);
            DockPanel.SetDock(divider, Dock.Top);
            dock.Children.Add(divider);

            var me = SocialApi.MyProfile;
            bool muted = canWrite && me?.Muted == true;
            if (muted) canWrite = false;

            if (canWrite)
            {
                var input = new Grid { Margin = new Thickness(14, 10, 14, 14) };
                input.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                input.ColumnDefinitions.Add(new ColumnDefinition());
                input.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var box = UI.Field("", 4000);
                box.Margin = new Thickness(friend != null ? 8 : 0, 0, 8, 0);
                box.SetResourceReference(FrameworkElement.TagProperty, "chats.placeholder");
                Grid.SetColumn(box, 1);
                var sendBtn = new Button { Style = (Style)UI.Owner.FindResource("PrimaryButton"), MinWidth = 48 };
                var sendIcon = new TextBlock { Text = "", FontFamily = (FontFamily)UI.Owner.FindResource("Icons"), FontSize = 14 };
                sendBtn.Content = sendIcon;
                Grid.SetColumn(sendBtn, 2);
                input.Children.Add(box);
                input.Children.Add(sendBtn);
                if (friend != null)
                {
                    var presetBtn = UI.Glass("", null, null, 9);
                    presetBtn.ToolTip = Loc.T("chats.send_preset");
                    presetBtn.Click += (s, e) => PresetMenu(presetBtn, friend);
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
                    sendBtn.Content = new TextBlock { Text = Math.Ceiling(left).ToString(), FontSize = 13, FontWeight = FontWeights.SemiBold };
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
            else
            {
                var note = muted
                    ? UI.T(string.Format(Loc.T("chats.muted"), Until(me.Banned ? me.BannedUntil : me.MutedUntil)), 12.5, sub: true, wrap: true)
                    : UI.L("chats.service", 12.5, sub: true, wrap: true);
                note.Margin = new Thickness(18, 10, 18, 16);
                DockPanel.SetDock(note, Dock.Bottom);
                dock.Children.Add(note);
            }

            _messages = new StackPanel { Margin = new Thickness(16, 8, 16, 8), RenderTransform = new TranslateTransform() };
            _messagesScroll = UI.Scroll(_messages);
            _pending.Clear();
            _historyLoaded = false;
            _unreadTicks.Clear();
            dock.Children.Add(_messagesScroll);

            // Под перепиской — слой фона темы (со скруглением, как у карточки)
            _chatRoot = new Grid();
            _themeLayer = new Border { CornerRadius = new CornerRadius(21), IsHitTestVisible = false, Opacity = 0 };
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
            if (incoming && !first && AppActive) ChatSounds.PlayIn();   // новое сообщение от собеседника — звук (окно не в фокусе — звук даст уведомление)
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

            var b = new Border
            {
                CornerRadius = mine ? new CornerRadius(18, 18, 6, 18) : new CornerRadius(18, 18, 18, 6),
                Padding = new Thickness(14, 9, 14, 8), Margin = new Thickness(mine ? 80 : 0, 3, mine ? 0 : 80, 3),
                HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left, MaxWidth = 460
            };
            b.SetResourceReference(Border.BackgroundProperty, mine ? "ChatMine" : "ChatOther");

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
            var time = new TextBlock { Text = UI.Ago(m.Created), FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
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
            b.Child = sp;
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
                _themeLayer.Child = _theme.Bg == "custom" ? new Border { CornerRadius = new CornerRadius(21), Background = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)) } : null;
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
