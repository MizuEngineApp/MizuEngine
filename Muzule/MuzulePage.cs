// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>
    /// Вкладка «Muzule» (в самом низу меню, только Mizu Plus): чат с агентами Claude, свои агенты, ключ и настройки.
    /// Без Plus — описание и кнопка «Оформить», без ключа — пошаговое подключение ключа Claude API.
    /// </summary>
    internal sealed class MuzulePage
    {
        readonly Grid _host;
        readonly Settings _s;
        readonly Action<string> _toast;
        readonly Func<string, bool> _watching;

        string _tab = "chat";                 // chat / agents / settings
        MuzuleChat _chat;                     // открытый чат (новый — ещё не сохранён)
        MuzuleRun _run;                       // запуск, к которому подключена лента
        AnswerView _live;
        MuzuleAgent _editing;                 // агент в редакторе (копия)

        StackPanel _feed, _chatList;
        ScrollViewer _feedScroll;
        TextBox _input;
        Button _send;
        TextBlock _sendIcon, _tokens;
        bool _stick = true;
        const double FontSize = 14;

        public MuzulePage(Grid host, Settings s, Action<string> toast)
        {
            _host = host; _s = s; _toast = toast;
            _watching = id => _host.IsVisible && _tab == "chat" && _chat?.Id == id && MainWindow.Current?.IsActive == true
                              && MainWindow.Current.WindowState != WindowState.Minimized;
            Muzule.PageWatching = _watching;
            MuzuleStore.ChatsChanged += OnChatsChanged;
            Muzule.RunsChanged += OnRunsChanged;
        }

        /// <summary>Окно закрывается: отписываемся (агент при этом продолжает работать).</summary>
        public void Detach()
        {
            MuzuleStore.ChatsChanged -= OnChatsChanged;
            Muzule.RunsChanged -= OnRunsChanged;
            if (Muzule.PageWatching == _watching) Muzule.PageWatching = null;
            DetachRun();
        }

        public void Show()
        {
            DetachRun();
            _feed = _chatList = null;   // старые элементы больше не на экране — не обновляем их
            _feedScroll = null;
            _input = null;
            _sendIcon = _tokens = null;
            _host.Children.Clear();
            _host.Children.Add(Build());
        }

        public void Refresh() { if (_host.IsVisible) Show(); }

        /// <summary>Выбрать чат (из уведомления, Dynamic Island или быстрого окна). Показывает его следующий Show().</summary>
        public void OpenChat(string id)
        {
            var c = MuzuleStore.LoadChat(id);
            if (c != null) _chat = c;
            _tab = "chat";
            _editing = null;
        }

        FrameworkElement Build()
        {
            if (!Plus.IsActive) return Locked();
            if (!MuzuleStore.HasKey) return KeySetup();

            var g = new Grid { Margin = new Thickness(0, 0, 14, 0) };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition());
            g.Children.Add(Header());
            FrameworkElement body = _tab == "agents" ? (_editing != null ? AgentEditor() : AgentsTab()) : _tab == "settings" ? SettingsTab() : ChatTab();
            Grid.SetRow(body, 1);
            g.Children.Add(body);
            Motion.Enter(body, 0);
            return g;
        }

        FrameworkElement Header()
        {
            var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 14), LastChildFill = false };
            var title = UI.Row(UI.T("Muzule", 28, true));
            var pb = UI.PlusBadge(22, clickable: false);
            pb.Margin = new Thickness(10, 4, 0, 0);
            title.Children.Add(pb);
            title.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(title, Dock.Left);
            dock.Children.Add(title);

            var tabs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
            foreach (var (id, key) in new[] { ("chat", "muzule.tab.chat"), ("agents", "muzule.tab.agents"), ("settings", "muzule.tab.settings") })
            {
                string tab = id;
                tabs.Children.Add(UI.Tab(key, "muzule_tab", _tab == tab, (s, e) =>
                {
                    if (_tab == tab && _editing == null) return;
                    _tab = tab;
                    _editing = null;
                    Show();
                }));
            }
            DockPanel.SetDock(tabs, Dock.Right);
            dock.Children.Add(tabs);
            return dock;
        }

        // ================== без Plus / без ключа ==================

        FrameworkElement Locked()
        {
            var root = new StackPanel { MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 14, 28) };
            var title = UI.Row(UI.T("Muzule", 28, true));
            var pb = UI.PlusBadge(22, clickable: false);
            pb.Margin = new Thickness(10, 4, 0, 0);
            title.Children.Add(pb);
            root.Children.Add(title);
            var sub = UI.L("muzule.sub", 13.5, sub: true, wrap: true);
            sub.Margin = new Thickness(0, 6, 0, 0);
            root.Children.Add(sub);

            var card = new StackPanel();
            card.Children.Add(UI.L("muzule.locked", 14, wrap: true));
            var go = UI.Primary("livebg.get_plus", (s, e) => UI.OpenPlus?.Invoke());
            go.Margin = new Thickness(0, 14, 0, 0);
            card.Children.Add(go);
            var c = UI.Card(card, 20);
            root.Children.Add(c);
            root.Children.Add(UI.Section("muzule.what"));
            root.Children.Add(Features());
            Motion.Stagger(root, 2);
            return UI.Scroll(root);
        }

        static FrameworkElement Features() => UI.IosGroup(
            Feature("", UI.Purple, "muzule.f1", "muzule.f1.d"),
            Feature("", UI.Blue, "muzule.f2", "muzule.f2.d"),
            Feature("", UI.Teal, "muzule.f3", "muzule.f3.d"),
            Feature("", UI.Gray, "muzule.f4", "muzule.f4.d"));

        static FrameworkElement Feature(string glyph, (Color a, Color b) c, string key, string sub)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
            sp.Children.Add(UI.L(key, 13.5, true));
            sp.Children.Add(UI.L(sub, 12, sub: true, wrap: true));
            return UI.IosRow(UI.Squircle(glyph, c, 30), sp);
        }

        FrameworkElement KeySetup(bool embedded = false)
        {
            var root = new StackPanel { MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 14, 28) };
            if (!embedded)
            {
                var title = UI.Row(UI.T("Muzule", 28, true));
                var pb = UI.PlusBadge(22, clickable: false);
                pb.Margin = new Thickness(10, 4, 0, 0);
                title.Children.Add(pb);
                root.Children.Add(title);
                var sub = UI.L("muzule.key.sub", 13.5, sub: true, wrap: true);
                sub.Margin = new Thickness(0, 6, 0, 0);
                root.Children.Add(sub);
            }

            var steps = new StackPanel();
            steps.Children.Add(Step("1", "muzule.key.step1"));
            var console = UI.Glass("", "muzule.key.console", (s, e) => Md.OpenUrl(ClaudeApi.ConsoleUrl), 9);
            console.Margin = new Thickness(34, 8, 0, 6);
            steps.Children.Add(console);
            steps.Children.Add(Step("2", "muzule.key.step2"));
            steps.Children.Add(Step("3", "muzule.key.step3"));

            var (field, value) = UI.Secret();
            field.Margin = new Thickness(0, 12, 0, 0);
            steps.Children.Add(field);
            var err = UI.Error();
            steps.Children.Add(err);
            Button save = null;
            save = UI.Primary("muzule.key.save", async (s, e) =>
            {
                string key = value()?.Trim() ?? "";
                if (key.Length < 20) { UI.ShowError(err, Loc.T("muzule.key.short")); return; }
                save.IsEnabled = false;
                UI.ShowError(err, null);
                save.SetResourceReference(ContentControl.ContentProperty, "muzule.key.checking");
                string r = await ClaudeApi.CheckKeyAsync(key);
                save.IsEnabled = true;
                save.SetResourceReference(ContentControl.ContentProperty, "muzule.key.save");
                if (r != null) { UI.ShowError(err, Loc.T(r)); return; }
                try { MuzuleStore.SaveKey(key); }
                catch (Exception ex) { Settings.Log(ex); UI.ShowError(err, Loc.T("err.generic")); return; }
                _toast(Loc.T("muzule.key.saved"));
                if (embedded) _tab = "settings"; else _tab = "chat";
                Show();
            });
            save.Margin = new Thickness(0, 14, 0, 0);
            steps.Children.Add(save);
            var safe = UI.L("muzule.key.safe", 12, sub: true, wrap: true);
            safe.Margin = new Thickness(0, 14, 0, 0);
            steps.Children.Add(safe);
            root.Children.Add(UI.Card(steps, embedded ? 0 : 20));
            if (embedded) return root;
            Motion.Stagger(root, 2);
            return UI.Scroll(root);
        }

        static FrameworkElement Step(string n, string key)
        {
            var g = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var num = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Top };
            num.SetResourceReference(Border.BackgroundProperty, "Accent");
            num.Child = new TextBlock { Text = n, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(num);
            var t = UI.L(key, 13.5, wrap: true);
            t.Margin = new Thickness(0, 2, 0, 0);
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            return g;
        }

        // ================== чат ==================

        FrameworkElement ChatTab()
        {
            if (_chat == null)
            {
                _chat = MuzuleStore.NewChat(_s.MuzuleAgent);
                _chat.Model = MuzuleStore.Agent(_chat.AgentId).Model ?? _s.MuzuleModel;
            }

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(232) });
            g.ColumnDefinitions.Add(new ColumnDefinition());

            // Слева: новый чат и история
            var left = new Grid { Margin = new Thickness(0, 0, 16, 0) };
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.RowDefinitions.Add(new RowDefinition());
            var newChat = UI.Glass("", "muzule.new", (s, e) => NewChat(_chat?.AgentId ?? _s.MuzuleAgent), 9);
            newChat.HorizontalAlignment = HorizontalAlignment.Stretch;
            left.Children.Add(newChat);
            _chatList = new StackPanel();
            var listScroll = UI.Scroll(_chatList);
            listScroll.Margin = new Thickness(0, 12, 0, 0);
            Grid.SetRow(listScroll, 1);
            left.Children.Add(listScroll);
            FillChatList();
            g.Children.Add(left);

            // Справа: агент и модель, лента, поле ввода
            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition());
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(right, 1);
            g.Children.Add(right);

            right.Children.Add(TopBar());

            _feed = new StackPanel { Margin = new Thickness(0, 6, 18, 12) };
            _feedScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _feed, Focusable = false };
            SmoothScroll.Attach(_feedScroll);
            _feedScroll.ScrollChanged += (s, e) =>
            {
                if (e.ExtentHeightChange != 0) return;   // выросла лента — это не прокрутка человеком
                _stick = _feedScroll.VerticalOffset >= _feedScroll.ScrollableHeight - 30;
            };
            Grid.SetRow(_feedScroll, 1);
            right.Children.Add(_feedScroll);

            var composer = Composer();
            Grid.SetRow(composer, 2);
            right.Children.Add(composer);

            FillFeed();
            return g;
        }

        FrameworkElement TopBar()
        {
            var bar = new DockPanel { Margin = new Thickness(0, 0, 18, 6), LastChildFill = false };
            var agent = MuzuleStore.Agent(_chat.AgentId);

            // Кнопка выбора агента: значок, имя, стрелка
            var pick = new StackPanel { Orientation = Orientation.Horizontal };
            pick.Children.Add(Md.AgentIcon(agent, 24));
            var name = UI.T(agent.Name, 14.5, true);
            name.Margin = new Thickness(9, 0, 6, 0);
            name.VerticalAlignment = VerticalAlignment.Center;
            pick.Children.Add(name);
            var chev = UI.Icon("", 10);
            chev.Opacity = 0.6;
            pick.Children.Add(chev);
            var pickBtn = new Button { Style = (Style)UI.Owner.FindResource("GlassButton"), Padding = new Thickness(8, 6, 12, 6), Content = pick };
            pickBtn.Click += (s, e) => AgentMenu(pickBtn);
            DockPanel.SetDock(pickBtn, Dock.Left);
            bar.Children.Add(pickBtn);

            // Модель
            var models = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 8, 0, 0) };
            string cur = MuzuleStore.ValidModel(_chat.Model ?? agent.Model ?? _s.MuzuleModel);
            foreach (var m in MuzuleStore.Models)
            {
                string id = m.Id;
                var r = new RadioButton { Style = (Style)UI.Owner.FindResource("Pill"), GroupName = "muzule_model", IsChecked = id == cur, Content = m.Short, FontSize = 12, Padding = new Thickness(0) };
                r.SetResourceReference(FrameworkElement.ToolTipProperty, m.Key);
                r.Click += (s, e) =>
                {
                    _chat.Model = id;
                    _s.MuzuleModel = id;   // запоминаем как модель по умолчанию
                    _s.Save();
                };
                models.Children.Add(r);
            }
            DockPanel.SetDock(models, Dock.Left);
            bar.Children.Add(models);

            _tokens = UI.T("", 11.5, sub: true);
            _tokens.VerticalAlignment = VerticalAlignment.Center;
            _tokens.Margin = new Thickness(0, 0, 4, 0);
            DockPanel.SetDock(_tokens, Dock.Right);
            bar.Children.Add(_tokens);
            UpdateTokens();
            return bar;
        }

        void UpdateTokens()
        {
            if (_tokens == null || _chat == null) return;
            _tokens.Text = _chat.InTokens + _chat.OutTokens == 0 ? "" : string.Format(Loc.T("muzule.tokens"), Short(_chat.InTokens), Short(_chat.OutTokens));
            _tokens.ToolTip = Loc.T("muzule.tokens.tip");
        }

        static string Short(long n) => n >= 1_000_000 ? (n / 1_000_000.0).ToString("0.#") + "M" : n >= 1000 ? (n / 1000.0).ToString("0.#") + "k" : n.ToString();

        void AgentMenu(FrameworkElement target)
        {
            var menu = GlassMenu.Create();
            foreach (var a in MuzuleStore.Agents)
            {
                var agent = a;
                GlassMenu.Item(menu, (agent.Id == _chat.AgentId ? "✓  " : "     ") + agent.Name, () =>
                {
                    _s.MuzuleAgent = agent.Id;
                    _s.Save();
                    if (_chat.Messages.Count > 0) NewChat(agent.Id);   // у другого агента — свой характер, начинаем новый чат
                    else { _chat.AgentId = agent.Id; _chat.Model = agent.Model ?? _s.MuzuleModel; Show(); }
                });
            }
            GlassMenu.Separator(menu);
            GlassMenu.Item(menu, Loc.T("muzule.agent.create"), () => { _tab = "agents"; _editing = NewAgent(); Show(); });
            menu.PlacementTarget = target;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        void NewChat(string agentId)
        {
            DetachRun();
            _chat = MuzuleStore.NewChat(agentId);
            _chat.Model = MuzuleStore.Agent(agentId).Model ?? _s.MuzuleModel;
            _tab = "chat";
            Show();
            _input?.Focus();
        }

        void OpenStored(string id)
        {
            if (_chat?.Id == id) return;
            var c = MuzuleStore.LoadChat(id);
            if (c == null) { _toast(Loc.T("err.generic")); return; }
            DetachRun();
            _chat = c;
            Show();
        }

        // --- список чатов ---

        void OnChatsChanged() { if (_chatList != null && _host.IsVisible) FillChatList(); }
        void OnRunsChanged()
        {
            if (_chatList != null && _host.IsVisible) FillChatList();
            UpdateSend();
        }

        void FillChatList()
        {
            if (_chatList == null) return;
            _chatList.Children.Clear();
            var chats = MuzuleStore.Chats;
            if (chats.Count == 0)
            {
                var t = UI.L("muzule.history.empty", 12.5, sub: true, wrap: true);
                t.Margin = new Thickness(6, 4, 0, 0);
                _chatList.Children.Add(t);
                return;
            }
            string lastGroup = null;
            foreach (var c in chats)
            {
                string group = c.Updated.Date == DateTime.Today ? Loc.T("muzule.today") : c.Updated.Date == DateTime.Today.AddDays(-1) ? Loc.T("muzule.yesterday") : c.Updated.ToString("d MMMM");
                if (group != lastGroup)
                {
                    var h = UI.T(group, 11.5, true, sub: true);
                    h.Margin = new Thickness(8, _chatList.Children.Count == 0 ? 0 : 12, 0, 4);
                    _chatList.Children.Add(h);
                    lastGroup = group;
                }
                _chatList.Children.Add(ChatRow(c));
            }
        }

        FrameworkElement ChatRow(MuzuleChatInfo c)
        {
            bool sel = _chat?.Id == c.Id;
            bool running = Muzule.RunOf(c.Id) != null;
            var agent = MuzuleStore.Agent(c.AgentId);
            var g = new Grid { Margin = new Thickness(8, 6, 8, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = Md.AgentIcon(agent, 20);
            icon.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(icon);
            var title = UI.T(string.IsNullOrEmpty(c.Title) ? Loc.T("muzule.untitled") : c.Title, 13, sel);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.Margin = new Thickness(9, 0, 4, 0);
            title.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(title, 1);
            g.Children.Add(title);
            if (running)
            {
                // Агент ещё работает в этом чате — точка цвета темы
                var dot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), VerticalAlignment = VerticalAlignment.Center };
                dot.SetResourceReference(Border.BackgroundProperty, "Accent");
                Grid.SetColumn(dot, 2);
                g.Children.Add(dot);
            }

            var row = new Border { CornerRadius = new CornerRadius(10), Child = g, Cursor = Cursors.Hand, Margin = new Thickness(0, 1, 0, 1) };
            if (sel) row.SetResourceReference(Border.BackgroundProperty, "NavSel");
            else row.Background = Brushes.Transparent;
            row.MouseEnter += (s, e) => { if (!sel) row.SetResourceReference(Border.BackgroundProperty, "HoverBg"); };
            row.MouseLeave += (s, e) => { if (!sel) row.Background = Brushes.Transparent; };
            string id = c.Id;
            row.MouseLeftButtonUp += (s, e) => OpenStored(id);
            row.MouseRightButtonUp += (s, e) =>
            {
                e.Handled = true;
                var menu = GlassMenu.Create();
                GlassMenu.Item(menu, Loc.T("muzule.chat.delete"), () =>
                {
                    Muzule.RunOf(id)?.Stop();
                    MuzuleStore.DeleteChat(id);
                    if (_chat?.Id == id) NewChat(_chat.AgentId);
                });
                menu.PlacementTarget = row;
                menu.Placement = PlacementMode.MousePoint;
                menu.IsOpen = true;
            };
            row.ToolTip = c.Title;
            return row;
        }

        // --- лента ---

        void FillFeed()
        {
            _feed.Children.Clear();
            _live = null;
            var agent = MuzuleStore.Agent(_chat.AgentId);
            if (_chat.Messages.Count == 0) { _feed.Children.Add(Empty(agent)); return; }

            var last = AnswerView.RenderHistory(_feed, _chat, agent, FontSize, (root, view) => AnswerRow(agent, root), UserBubble);
            var run = Muzule.RunOf(_chat.Id);
            if (run != null)
            {
                // Агент ещё работает (вкладку открыли посреди ответа) — продолжаем печатать в том же ответе
                if (last == null)
                {
                    last = new AnswerView(FontSize, _s.MuzuleTyping);
                    _feed.Children.Add(AnswerRow(agent, last.Root));
                }
                AttachRun(run, last);
                last.Seed(run.Live.ToString());
                if (run.Live.Length == 0) last.ShowThinking();
            }
            else last?.Finish();
            ScrollEnd(true);
        }

        FrameworkElement Empty(MuzuleAgent agent)
        {
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 50, 0, 0), MaxWidth = 520 };
            var icon = Md.AgentIcon(agent, 64);
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(icon);
            var hi = UI.T(string.Format(Loc.T("muzule.hello"), agent.Name), 22, true);
            hi.HorizontalAlignment = HorizontalAlignment.Center;
            hi.Margin = new Thickness(0, 16, 0, 0);
            sp.Children.Add(hi);
            var sub = UI.L(agent.Control ? "muzule.hello.sub" : "muzule.hello.sub2", 13.5, sub: true, wrap: true);
            sub.TextAlignment = TextAlignment.Center;
            sub.Margin = new Thickness(0, 6, 0, 18);
            sp.Children.Add(sub);
            if (agent.Control)
            {
                var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
                foreach (var k in new[] { "muzule.sugg1", "muzule.sugg2", "muzule.sugg3", "muzule.sugg4" })
                {
                    string text = Loc.T(k);
                    var b = new Button { Style = (Style)UI.Owner.FindResource("GlassButton"), Padding = new Thickness(14, 9, 14, 9), Margin = new Thickness(0, 0, 8, 8), Content = UI.T(text, 13) };
                    b.Click += (s, e) => { _input.Text = text; Send(); };
                    wrap.Children.Add(b);
                }
                sp.Children.Add(wrap);
            }
            return sp;
        }

        static FrameworkElement UserBubble(string text)
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(18, 18, 6, 18), Padding = new Thickness(14, 9, 14, 10),
                Margin = new Thickness(90, 14, 0, 4), HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 560
            };
            b.SetResourceReference(Border.BackgroundProperty, "Accent");
            var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = FontSize, Foreground = Brushes.White, LineHeight = FontSize * 1.4 };
            b.Child = t;
            b.ContextMenu = CopyMenu(text);
            return b;
        }

        static ContextMenu CopyMenu(string text)
        {
            var menu = GlassMenu.Create();
            GlassMenu.Item(menu, Loc.T("muzule.copy"), () => { try { Clipboard.SetText(text); } catch { } });
            return menu;
        }

        static FrameworkElement AnswerRow(MuzuleAgent agent, FrameworkElement content)
        {
            var g = new Grid { Margin = new Thickness(0, 14, 40, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var icon = Md.AgentIcon(agent, 28);
            g.Children.Add(icon);
            var sp = new StackPanel();
            var name = UI.T(agent.Name, 12.5, true, sub: true);
            name.Margin = new Thickness(0, 5, 0, 6);
            sp.Children.Add(name);
            sp.Children.Add(content);
            Grid.SetColumn(sp, 1);
            g.Children.Add(sp);
            return g;
        }

        void ScrollEnd(bool force = false)
        {
            if (_feedScroll == null || (!_stick && !force)) return;
            _feedScroll.Dispatcher.BeginInvoke(new Action(() => _feedScroll?.ScrollToEnd()), DispatcherPriority.Background);
        }

        // --- поле ввода ---

        FrameworkElement Composer()
        {
            // Поле ввода в стиле остальных полей MizuEngine и круглая кнопка «отправить» справа
            var card = new Border { Margin = new Thickness(0, 0, 18, 0) };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _input = UI.Field("", 8000, multiline: true);
            _input.MinHeight = 40;
            _input.MaxHeight = 180;
            _input.Padding = new Thickness(12, 9, 12, 9);
            _input.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _input.VerticalContentAlignment = VerticalAlignment.Center;
            _input.Tag = string.Format(Loc.T("muzule.placeholder"), MuzuleStore.Agent(_chat.AgentId).Name);
            _input.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
                {
                    e.Handled = true;
                    Send();
                }
            };
            g.Children.Add(_input);

            _sendIcon = new TextBlock { Text = "", FontFamily = Md.Icons, FontSize = 15, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var circle = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(19), Child = _sendIcon };
            circle.SetResourceReference(Border.BackgroundProperty, "Accent");
            _send = new Button { Content = circle, Cursor = Cursors.Hand, Margin = new Thickness(8, 0, 2, 0), VerticalAlignment = VerticalAlignment.Bottom, Focusable = false };
            _send.Template = BareButton();
            _send.Click += (s, e) =>
            {
                var run = Muzule.RunOf(_chat?.Id);
                if (run != null) run.Stop();
                else Send();
            };
            Grid.SetColumn(_send, 1);
            g.Children.Add(_send);
            card.Child = g;

            var wrap = new StackPanel();
            wrap.Children.Add(card);
            var hint = UI.L("muzule.hint", 11, sub: true);
            hint.Margin = new Thickness(14, 6, 0, 0);
            hint.Opacity = 0.75;
            wrap.Children.Add(hint);
            UpdateSend();
            return wrap;
        }

        static ControlTemplate BareButton()
        {
            var t = new ControlTemplate(typeof(Button));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            t.VisualTree = cp;
            return t;
        }

        void UpdateSend()
        {
            if (_sendIcon == null) return;
            bool running = Muzule.RunOf(_chat?.Id) != null;
            _sendIcon.Text = running ? "" : "";   // «стоп» / «отправить»
            _send.ToolTip = Loc.T(running ? "muzule.stop" : "muzule.send");
        }

        void Send()
        {
            if (_input == null || _chat == null) return;
            string text = _input.Text.Trim();
            if (text.Length == 0 || Muzule.RunOf(_chat.Id) != null) return;
            if (!Muzule.Ready) { Show(); return; }

            var agent = MuzuleStore.Agent(_chat.AgentId);
            if (_chat.Messages.Count == 0) _feed.Children.Clear();   // убираем приветствие
            var run = Muzule.Send(_chat, text);
            if (run == null) return;
            _input.Text = "";
            _feed.Children.Add(UserBubble(text));
            var view = new AnswerView(FontSize, _s.MuzuleTyping);
            var row = AnswerRow(agent, view.Root);
            _feed.Children.Add(row);
            row.Opacity = 0;
            Anim.To(row, UIElement.OpacityProperty, 1, Motion.Ms(260));
            AttachRun(run, view);
            view.ShowThinking();
            _stick = true;
            ScrollEnd(true);
            UpdateSend();
        }

        // --- подключение к запуску ---

        void AttachRun(MuzuleRun run, AnswerView view)
        {
            DetachRun();
            _run = run;
            _live = view;
            view.Changed = () => ScrollEnd();
            run.Text += OnText;
            run.ToolStarted += OnToolStarted;
            run.ToolDone += OnToolDone;
            run.RoundStarted += OnRound;
            run.Finished += OnFinished;
        }

        void DetachRun()
        {
            if (_run == null) return;
            _run.Text -= OnText;
            _run.ToolStarted -= OnToolStarted;
            _run.ToolDone -= OnToolDone;
            _run.RoundStarted -= OnRound;
            _run.Finished -= OnFinished;
            _run = null;
            if (_live != null) _live.Changed = null;
            _live = null;
        }

        void OnText(string t) => _live?.Text(t);
        void OnToolStarted(string name) => _live?.ToolStart(name);
        void OnToolDone(string name, string result, bool ok) => _live?.ToolDone(name, ok);
        void OnRound() => _live?.ShowThinking();

        void OnFinished()
        {
            var run = _run;
            var view = _live;
            if (run == null || view == null) return;
            if (run.Error != null)
            {
                Action fix = null;
                string fixText = null;
                if (run.Error == "muzule.err.key") { fix = () => { _tab = "settings"; Show(); }; fixText = Loc.T("muzule.key.change"); }
                else if (run.Error == "muzule.err.billing") { fix = () => Md.OpenUrl(ClaudeApi.ConsoleUrl); fixText = Loc.T("muzule.key.console"); }
                else if (run.Error == "muzule.err.web") { fix = () => { _tab = "agents"; _editing = Clone(MuzuleStore.Agent(_chat.AgentId)); Show(); }; fixText = Loc.T("muzule.agent.edit"); }
                view.Error(Loc.T(run.Error), run.ErrorDetail, fix, fixText);
            }
            if (run.Note != null) view.Note(Loc.T(run.Note));
            if (run.Cancelled) view.Note(Loc.T("muzule.stopped"));
            view.Finish();
            DetachRun();
            UpdateSend();
            UpdateTokens();
            ScrollEnd();
        }

        // ================== агенты ==================

        FrameworkElement AgentsTab()
        {
            var root = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 14, 28) };
            var sub = UI.L("muzule.agents.sub", 13.5, sub: true, wrap: true);
            root.Children.Add(sub);
            var rows = new List<UIElement>();
            foreach (var a in MuzuleStore.Agents)
            {
                var agent = a;
                var sp = new StackPanel { Margin = new Thickness(0, 7, 0, 7) };
                sp.Children.Add(UI.T(agent.Name, 14, true));
                var tags = new List<string> { agent.Model == null ? Loc.T("muzule.model.default") : MuzuleStore.ModelShort(agent.Model) };
                if (agent.Control) tags.Add(Loc.T("muzule.agent.tag.control"));
                if (agent.Web) tags.Add(Loc.T("muzule.agent.tag.web"));
                if (agent.BuiltIn) tags.Insert(0, Loc.T("muzule.agent.builtin"));
                sp.Children.Add(UI.T(string.Join(" · ", tags), 12, sub: true));
                var icon = Md.AgentIcon(agent, 32);
                icon.VerticalAlignment = VerticalAlignment.Center;
                rows.Add(UI.IosRow(icon, sp, UI.Chevron(), () => { _editing = Clone(agent); Show(); }));
            }
            var group = UI.IosGroup(rows.ToArray());
            group.Margin = new Thickness(0, 16, 0, 0);
            root.Children.Add(group);
            var create = UI.Primary("muzule.agent.create", (s, e) => { _editing = NewAgent(); Show(); });
            create.Margin = new Thickness(0, 16, 0, 0);
            root.Children.Add(create);
            Motion.Stagger(root, 1);
            return UI.Scroll(root);
        }

        static MuzuleAgent Clone(MuzuleAgent a) => new MuzuleAgent
        {
            Id = a.Id, Name = a.Name, Glyph = a.Glyph, Color = a.Color, Instructions = a.Instructions, Model = a.Model, Control = a.Control, Web = a.Web
        };

        MuzuleAgent NewAgent() => new MuzuleAgent
        {
            Id = null, Name = "", Glyph = MuzuleStore.Glyphs[1], Color = MuzuleStore.Colors[(MuzuleStore.Agents.Count) % MuzuleStore.Colors.Length],
            Instructions = "", Model = null, Control = false, Web = false
        };

        FrameworkElement AgentEditor()
        {
            var a = _editing;
            var root = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 14, 28) };
            var back = UI.Glass("", "muzule.agents.back", (s, e) => { _editing = null; Show(); }, 8);
            root.Children.Add(back);

            var card = new StackPanel();
            // Превью: значок и имя
            var preview = new StackPanel { Orientation = Orientation.Horizontal };
            var iconHost = new ContentControl { Content = Md.AgentIcon(a, 52), Focusable = false };
            preview.Children.Add(iconHost);
            var pName = UI.T(string.IsNullOrWhiteSpace(a.Name) ? Loc.T("muzule.agent.new") : a.Name, 20, true);
            pName.VerticalAlignment = VerticalAlignment.Center;
            pName.Margin = new Thickness(14, 0, 0, 0);
            preview.Children.Add(pName);
            card.Children.Add(preview);
            void RefreshIcon() => iconHost.Content = Md.AgentIcon(a, 52);

            var name = UI.Field(a.Name, 32);
            name.Tag = Loc.T("muzule.agent.name.ph");
            name.IsEnabled = !a.BuiltIn;
            name.TextChanged += (s, e) => { a.Name = name.Text; pName.Text = string.IsNullOrWhiteSpace(a.Name) ? Loc.T("muzule.agent.new") : a.Name; };
            card.Children.Add(UI.Labeled("muzule.agent.name", name));

            // Значок
            var glyphs = new WrapPanel();
            var glyphCells = new List<Border>();
            foreach (var gl in MuzuleStore.Glyphs)
            {
                string glyph = gl;
                var cell = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand, BorderThickness = new Thickness(2) };
                cell.SetResourceReference(Border.BackgroundProperty, "HoverBg");
                cell.Child = new TextBlock { Text = glyph, FontFamily = Md.Icons, FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                ((TextBlock)cell.Child).SetResourceReference(TextBlock.ForegroundProperty, "Text");
                cell.Tag = glyph;
                cell.MouseLeftButtonUp += (s, e) =>
                {
                    a.Glyph = glyph;
                    foreach (var c in glyphCells) Ring(c, (string)c.Tag == a.Glyph);
                    RefreshIcon();
                };
                glyphCells.Add(cell);
                Ring(cell, glyph == a.Glyph);
                glyphs.Children.Add(cell);
            }
            card.Children.Add(UI.Labeled("muzule.agent.icon", glyphs));

            // Цвет
            var colors = new WrapPanel();
            var colorCells = new List<Border>();
            foreach (var cn in MuzuleStore.Colors)
            {
                string color = cn;
                var c = MuzuleStore.ColorOf(color);
                var dot = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = new LinearGradientBrush(c.a, c.b, 90) };
                var ring = new Border { CornerRadius = new CornerRadius(17), BorderThickness = new Thickness(2.5), Padding = new Thickness(2), Margin = new Thickness(0, 0, 8, 8), Child = dot, Cursor = Cursors.Hand, Tag = color };
                ring.MouseLeftButtonUp += (s, e) =>
                {
                    a.Color = color;
                    foreach (var r in colorCells) Ring(r, (string)r.Tag == a.Color);
                    RefreshIcon();
                };
                colorCells.Add(ring);
                Ring(ring, color == a.Color);
                colors.Children.Add(ring);
            }
            card.Children.Add(UI.Labeled("muzule.agent.color", colors));

            // Модель
            var models = new WrapPanel();
            models.Children.Add(ModelPill("muzule.model.default", null, a));
            foreach (var m in MuzuleStore.Models) models.Children.Add(ModelPill(null, m.Id, a, m.Short));
            card.Children.Add(UI.Labeled("muzule.agent.model", models));

            // Инструкции
            var instr = UI.Field(a.Instructions, 4000, multiline: true);
            instr.MinHeight = 120;
            instr.MaxHeight = 260;
            instr.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            instr.Tag = Loc.T(a.BuiltIn ? "muzule.agent.extra.ph" : "muzule.agent.instr.ph");
            instr.TextChanged += (s, e) => a.Instructions = instr.Text;
            card.Children.Add(UI.Labeled(a.BuiltIn ? "muzule.agent.extra" : "muzule.agent.instr", instr));

            // Что можно агенту
            var control = Switch("muzule.agent.control", a.Control, v => a.Control = v);
            control.Margin = new Thickness(0, 16, 0, 0);
            control.IsEnabled = !a.BuiltIn;   // Mizu для того и нужен
            card.Children.Add(control);
            card.Children.Add(Hint("muzule.agent.control.d"));
            card.Children.Add(Switch("muzule.agent.web", a.Web, v => a.Web = v));
            card.Children.Add(Hint("muzule.agent.web.d"));

            var err = UI.Error();
            card.Children.Add(err);
            var buttons = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
            var save = UI.Primary("muzule.agent.save", (s, e) =>
            {
                a.Name = (a.Name ?? "").Trim();
                if (a.Name.Length == 0) { UI.ShowError(err, Loc.T("muzule.agent.noname")); return; }
                a.Instructions = (a.Instructions ?? "").Trim();
                MuzuleStore.SaveAgent(a);
                _toast(Loc.T("muzule.agent.saved"));
                _editing = null;
                Show();
            });
            save.Margin = new Thickness(0, 0, 10, 8);
            buttons.Children.Add(save);
            if (a.Id != null)
            {
                var chat = UI.Glass("", "muzule.agent.chat", (s, e) =>
                {
                    MuzuleStore.SaveAgent(a);
                    _s.MuzuleAgent = a.Id;
                    _s.Save();
                    _editing = null;
                    NewChat(a.Id);
                }, 10);
                chat.Margin = new Thickness(0, 0, 10, 8);
                buttons.Children.Add(chat);
            }
            if (a.Id != null && !a.BuiltIn)
            {
                bool armed = false;
                Button del = null;
                del = UI.Danger("", "muzule.agent.delete", (s, e) =>
                {
                    if (!armed)
                    {
                        armed = true;
                        if (del.Content is StackPanel sp && sp.Children.Count > 1 && sp.Children[1] is TextBlock t) t.SetResourceReference(TextBlock.TextProperty, "muzule.sure");
                        return;
                    }
                    MuzuleStore.DeleteAgent(a.Id);
                    if (_s.MuzuleAgent == a.Id) { _s.MuzuleAgent = MuzuleStore.MizuId; _s.Save(); }
                    if (_chat != null && _chat.AgentId == a.Id && _chat.Messages.Count == 0) _chat = null;
                    _editing = null;
                    Show();
                });
                del.Margin = new Thickness(0, 0, 10, 8);
                buttons.Children.Add(del);
            }
            card.Children.Add(buttons);
            root.Children.Add(UI.Card(card, 16));
            return UI.Scroll(root);
        }

        static void Ring(Border b, bool on)
        {
            if (on) b.SetResourceReference(Border.BorderBrushProperty, "Accent");
            else b.BorderBrush = Brushes.Transparent;
        }

        static RadioButton ModelPill(string key, string id, MuzuleAgent a, string text = null)
        {
            var r = new RadioButton { Style = (Style)UI.Owner.FindResource("Pill"), GroupName = "muzule_agent_model", IsChecked = a.Model == id };
            if (key != null) r.SetResourceReference(ContentControl.ContentProperty, key); else r.Content = text;
            if (id != null) r.SetResourceReference(FrameworkElement.ToolTipProperty, MuzuleStore.Models.First(m => m.Id == id).Key);
            r.Click += (s, e) => a.Model = id;
            return r;
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
            t.Margin = new Thickness(0, 0, 70, 6);
            return t;
        }

        // ================== настройки ==================

        bool _changingKey;

        FrameworkElement SettingsTab()
        {
            var root = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 14, 28) };

            // Ключ
            root.Children.Add(UI.Section("muzule.set.key", 0));
            if (_changingKey) root.Children.Add(KeySetup(embedded: true));
            else
            {
                var sp = new StackPanel();
                var row = new DockPanel();
                var ok = new TextBlock { Text = "", FontFamily = Md.Icons, FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0xC7, 0x59)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                row.Children.Add(ok);
                var k = UI.T(MuzuleStore.MaskedKey, 13.5, true);
                k.FontFamily = Md.Mono;
                k.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(k);
                sp.Children.Add(row);
                var safe = UI.L("muzule.key.safe", 12, sub: true, wrap: true);
                safe.Margin = new Thickness(0, 10, 0, 0);
                sp.Children.Add(safe);
                var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
                var change = UI.Glass("", "muzule.key.change", (s, e) => { _changingKey = true; Show(); }, 9);
                change.Margin = new Thickness(0, 0, 10, 8);
                buttons.Children.Add(change);
                var console = UI.Glass("", "muzule.key.console", (s, e) => Md.OpenUrl(ClaudeApi.ConsoleUrl), 9);
                console.Margin = new Thickness(0, 0, 10, 8);
                buttons.Children.Add(console);
                bool armed = false;
                Button del = null;
                del = UI.Danger("", "muzule.key.delete", (s, e) =>
                {
                    if (!armed)
                    {
                        armed = true;
                        if (del.Content is StackPanel p && p.Children.Count > 1 && p.Children[1] is TextBlock t) t.SetResourceReference(TextBlock.TextProperty, "muzule.sure");
                        return;
                    }
                    Muzule.StopAll();
                    MuzuleStore.DeleteKey();
                    _toast(Loc.T("muzule.key.deleted"));
                    Show();
                });
                del.Margin = new Thickness(0, 0, 10, 8);
                buttons.Children.Add(del);
                sp.Children.Add(buttons);
                root.Children.Add(UI.Card(sp, 0));
            }
            if (_changingKey)
            {
                var cancel = UI.Glass(null, "muzule.cancel", (s, e) => { _changingKey = false; Show(); }, 9);
                cancel.Margin = new Thickness(0, 10, 0, 0);
                root.Children.Add(cancel);
            }

            // Модель по умолчанию
            root.Children.Add(UI.Section("muzule.set.model"));
            var rows = new List<UIElement>();
            foreach (var m in MuzuleStore.Models)
            {
                string id = m.Id;
                var sp = new StackPanel { Margin = new Thickness(0, 7, 0, 7) };
                sp.Children.Add(UI.T(m.Short, 14, true));
                sp.Children.Add(UI.L(m.Key, 12, sub: true, wrap: true));
                FrameworkElement check = null;
                if (_s.MuzuleModel == id) { var c = UI.Icon("", 14, accent: true); check = c; }
                rows.Add(UI.IosRow(null, sp, check, () => { _s.MuzuleModel = id; _s.Save(); Show(); }));
            }
            root.Children.Add(UI.IosGroup(rows.ToArray()));
            var price = UI.L("muzule.set.price", 12, sub: true, wrap: true);
            price.Margin = new Thickness(16, 8, 0, 0);
            root.Children.Add(price);

            // Вид и вызов
            root.Children.Add(UI.Section("muzule.set.look"));
            var look = new StackPanel();
            look.Children.Add(Switch("muzule.set.typing", _s.MuzuleTyping, v => { _s.MuzuleTyping = v; _s.Save(); }));
            look.Children.Add(Switch("muzule.set.island", _s.MuzuleIsland, v => { _s.MuzuleIsland = v; _s.Save(); }));
            look.Children.Add(Hint("muzule.set.island.d"));
            look.Children.Add(Switch("muzule.set.spotlight", _s.MuzuleSpotlight, v => { _s.MuzuleSpotlight = v; _s.Save(); MizuHotkeys.Reapply(); }));
            look.Children.Add(Hint(_s.Hotkeys ? "muzule.set.spotlight.d" : "muzule.set.spotlight.off"));
            root.Children.Add(UI.Card(look, 0));

            // История
            root.Children.Add(UI.Section("muzule.set.history"));
            var hist = new StackPanel();
            hist.Children.Add(UI.T(string.Format(Loc.T("muzule.set.history.count"), MuzuleStore.Chats.Count), 13.5));
            hist.Children.Add(Hint("muzule.set.history.d"));
            bool armedH = false;
            Button clear = null;
            clear = UI.Danger("", "muzule.set.history.clear", (s, e) =>
            {
                if (!armedH)
                {
                    armedH = true;
                    if (clear.Content is StackPanel p && p.Children.Count > 1 && p.Children[1] is TextBlock t) t.SetResourceReference(TextBlock.TextProperty, "muzule.sure");
                    return;
                }
                Muzule.StopAll();
                MuzuleStore.DeleteAllChats();
                _chat = null;
                _toast(Loc.T("muzule.set.history.cleared"));
                Show();
            });
            clear.Margin = new Thickness(0, 8, 0, 0);
            hist.Children.Add(clear);
            root.Children.Add(UI.Card(hist, 0));

            Motion.Stagger(root, 0);
            return UI.Scroll(root);
        }
    }
}
