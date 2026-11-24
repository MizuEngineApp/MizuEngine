// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace MizuEngine
{
    /// <summary>
    /// 0.11.6: панель справа в «Чатах» — профиль собеседника или группы (макет rainy).
    /// Человек: аватарка, ник, @юз, «Открыть профиль», статус, ID, уведомления, «Отправить сообщение», «Пожаловаться», «Заблокировать».
    /// Группа: то же для группы + участники, «Пригласить», «Настройки группы», «Покинуть/Удалить группу».
    /// Внизу — карточка MizuEngine. Открывается и закрывается плавно (SocialPages.Chats.cs → UpdateInfo).
    /// </summary>
    internal sealed partial class SocialPages
    {
        Func<FrameworkElement> _infoBuild;
        static readonly Brush DangerBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x5F, 0x57)));
        static Brush Frozen(Brush b) { b.Freeze(); return b; }

        /// <summary>Поставить в панель профиль (key — чей он; null — панель не нужна, например, ничего не выбрано).</summary>
        void ShowInfo(string key, Func<FrameworkElement> build, bool force = false)
        {
            if (_infoInner == null) return;
            if (key == null) { _infoKey = null; UpdateInfo(true); return; }   // содержимое уезжает вместе с панелью
            bool same = key == _infoKey;
            _infoKey = key;
            _infoBuild = build;
            if (!same || force)
            {
                if (_infoInner.Child is not Grid host) { host = new Grid(); _infoInner.Child = host; }
                FrameworkElement content;
                try { content = build(); }
                catch (Exception e) { Settings.Log(e); content = UI.L("err.generic", 14, sub: true); }
                if (_infoShown && host.Children.Count > 0) Motion.Replace(host, content);
                else { host.Children.Clear(); host.Children.Add(content); }
            }
            UpdateInfo(true);
        }

        /// <summary>Перерисовать панель (поменялись уведомления, блокировка и т. п.).</summary>
        void RefreshInfo()
        {
            if (_infoKey != null && _infoBuild != null) ShowInfo(_infoKey, _infoBuild, true);
        }

        void ShowPersonInfo(Profile p, bool canWrite) => ShowInfo("u:" + p.Id + (canWrite ? ":w" : ""), () => PersonInfo(p, canWrite));
        void ShowGroupInfo(Group g) => ShowInfo("g:" + g.Id + ":" + g.Name + ":" + g.AvatarUrl + ":" + g.Badge, () => GroupInfo(g));

        // ================== человек ==================

        FrameworkElement PersonInfo(Profile p, bool canWrite)
        {
            var (root, sp) = InfoFrame();

            var avatar = UI.Avatar(p, 124);
            var face = p.Bot ? avatar : Online.WithDot(avatar, p.Id, 124);
            sp.Children.Add(Centered(face, 0));
            sp.Children.Add(NameLine(p.Nickname, p.Verified ? p.Badge : null, p.IsPlus));
            if (!string.IsNullOrEmpty(p.Handle)) sp.Children.Add(SubLine("@" + p.Handle));

            var open = RoundPill("", "chats.open_profile", () => { Motion.Capture(avatar, "avatar:" + p.Id); _ = OpenProfile(p); }, 42);
            sp.Children.Add(Centered(open, 14));

            // Статус и ID
            var rows = new List<UIElement>();
            if (!p.Bot)
            {
                var status = Online.Status(p.Id, 12.5);
                // Пока статус не пришёл — «…»; не пришёл совсем (человек скрыл «в сети») — «скрыт»
                var hidden = UI.T("…", 12.5, sub: true);
                var later = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
                later.Tick += (s, e) => { later.Stop(); hidden.Text = Loc.T("chats.status_hidden"); };
                later.Start();
                hidden.Unloaded += (s, e) => later.Stop();
                status.IsVisibleChanged += (s, e) => hidden.Visibility = status.IsVisible ? Visibility.Collapsed : Visibility.Visible;
                var lines = new StackPanel();
                lines.Children.Add(UI.L("chats.status", 14.5, true));
                var stGrid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
                stGrid.Children.Add(hidden);
                stGrid.Children.Add(status);
                lines.Children.Add(stGrid);
                rows.Add(InfoRow(Online.Dot(p.Id, 11), lines, null));
            }
            if (p.Number > 0) rows.Add(IdRow("chats.user_id", p.Number.ToString()));
            if (rows.Count > 0) sp.Children.Add(InfoCard(rows.ToArray()));

            sp.Children.Add(NotifyCard("u:" + p.Id));

            if (canWrite)
                sp.Children.Add(ActionCard("", "chats.write", false, () =>
                {
                    if (_inputBox == null) return;
                    _inputBox.Focus();
                    Keyboard.Focus(_inputBox);
                    _inputBox.CaretIndex = _inputBox.Text.Length;
                    Motion.Pop(_inputBox, 1.03);
                }));
            if (!p.Bot && p.Id != Supa.Me)
            {
                sp.Children.Add(ReportCard(r => SocialApi.ReportUser(p.Id, r)));
                sp.Children.Add(BlockCard(p));
            }
            return root;
        }

        /// <summary>«Заблокировать» / «Разблокировать». Блокировка — со вторым нажатием (чтобы не нажать случайно).</summary>
        FrameworkElement BlockCard(Profile p)
        {
            bool blocked = false, armed = false;
            Button card = null;
            TextBlock label = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            void Paint() => label.Text = Loc.T(blocked ? "chats.unblock" : armed ? "chats.block.confirm" : "chats.block");
            timer.Tick += (s, e) => { timer.Stop(); armed = false; Paint(); };
            card = ActionCard("", "chats.block", true, async () =>
            {
                if (!blocked && !armed) { armed = true; Paint(); Motion.Pop(card, 0.97); timer.Start(); return; }
                timer.Stop();
                armed = false;
                card.IsEnabled = false;
                string err = await SocialApi.DmBlock(p.Id, !blocked);
                card.IsEnabled = true;
                if (err != null) { _toast(err); Paint(); return; }
                blocked = !blocked;
                _toast(string.Format(Loc.T(blocked ? "chats.request.blocked" : "chats.unblocked"), p.Nickname));
                _dmCache.Remove(p.Id);
                Inbox.RefreshRequests();
                Paint();
            });
            label = (TextBlock)card.Tag;
            card.Unloaded += (s, e) => timer.Stop();
            _ = Load();
            async Task Load()
            {
                var st = await SocialApi.DmStatus(p.Id);
                blocked = st.blocked;
                Paint();
            }
            return card;
        }

        // ================== группа ==================

        FrameworkElement GroupInfo(Group g)
        {
            var (root, sp) = InfoFrame();
            bool owner = g.Owner == Supa.Me;
            bool admin = SocialApi.MyProfile?.IsAdmin == true;

            sp.Children.Add(Centered(UI.Avatar(g.Look(), 124), 0));
            sp.Children.Add(NameLine(g.Name, g.Badge, false));
            sp.Children.Add(SubLine(string.IsNullOrEmpty(g.Handle) ? Loc.T("groups.subtitle") : "@" + g.Handle));
            sp.Children.Add(Centered(RoundPill("", "groups.invite", () => ShowInvite(g), 42), 14));

            var count = UI.T("…", 12.5, sub: true);
            var lines = new StackPanel();
            lines.Children.Add(UI.L("groups.members", 14.5, true));
            count.Margin = new Thickness(0, 2, 0, 0);
            lines.Children.Add(count);
            var membersIcon = UI.Icon("", 15, accent: true);
            sp.Children.Add(InfoCard(InfoRow(membersIcon, lines, null, () => ShowMembers(g)), IdRow("groups.id", g.Id.ToString())));

            sp.Children.Add(NotifyCard("g:" + g.Id));

            if (owner || admin) sp.Children.Add(ActionCard("", "groups.settings", false, () => ShowGroupSettings(g)));
            if (owner) sp.Children.Add(ActionCard("", "groups.theme", false, () => ShowThemePicker(g.Theme, async t =>
            {
                string err = await SocialApi.UpdateGroup(g.Id, new System.Text.Json.Nodes.JsonObject { ["theme"] = t.IsEmpty ? null : t.ToJson() });
                if (err == null) g.Theme = t.Copy();
                return err;
            }, () => OpenGroup(g))));

            // Покинуть / удалить — со вторым нажатием
            bool armed = false;
            Button leave = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            leave = ActionCard(owner ? "" : "", owner ? "groups.delete" : "groups.leave", true, async () =>
            {
                var label = (TextBlock)leave.Tag;
                if (!armed)
                {
                    armed = true;
                    label.Text = Loc.T(owner ? "groups.delete.confirm" : "groups.leave.confirm");
                    Motion.Pop(leave, 0.97);
                    timer.Start();
                    return;
                }
                timer.Stop();
                leave.IsEnabled = false;
                await DoLeave(g);
                leave.IsEnabled = true;
            });
            timer.Tick += (s, e) => { timer.Stop(); armed = false; ((TextBlock)leave.Tag).Text = Loc.T(owner ? "groups.delete" : "groups.leave"); };
            leave.Unloaded += (s, e) => timer.Stop();
            sp.Children.Add(leave);
            if (!owner) sp.Children.Add(ReportCard(r => SocialApi.ReportGroup(g.Id, r)));

            // Участники: первые 30, остальные — «Все участники»
            var membersTitle = UI.Section("groups.members", 20);
            membersTitle.Margin = new Thickness(6, 20, 0, 8);
            sp.Children.Add(membersTitle);
            var list = new StackPanel();
            list.Children.Add(UI.Loading());
            sp.Children.Add(list);
            _ = Fill();
            async Task Fill()
            {
                var members = await SocialApi.GroupMembers(g.Id);
                foreach (var m in members) _groupPeople[m.User.Id] = m.User;
                int joined = members.Count(m => m.Joined);
                count.Text = string.Format(Loc.T("groups.count"), joined) + (g.MaxMembers.HasValue ? $" / {g.MaxMembers}" : "");
                list.Children.Clear();
                // Создатель — первым, дальше те, кто в группе, внизу приглашённые
                foreach (var m in members.OrderByDescending(m => m.User.Id == g.Owner).ThenByDescending(m => m.Joined).Take(30))
                    list.Children.Add(MemberRow(m, g));
                if (members.Count > 30 || owner)
                {
                    var all = RoundPill(null, "groups.all_members", () => ShowMembers(g), 38);
                    all.Margin = new Thickness(0, 6, 0, 0);
                    all.HorizontalAlignment = HorizontalAlignment.Center;
                    list.Children.Add(all);
                }
                Motion.Stagger(list);
            }
            return root;
        }

        /// <summary>Покинуть группу (или удалить, если ты создатель).</summary>
        async Task DoLeave(Group g)
        {
            bool owner = g.Owner == Supa.Me;
            string err = owner ? await SocialApi.DeleteGroup(g.Id) : await SocialApi.LeaveGroup(g.Id);
            if (err != null) { _toast(err); return; }
            _toast(Loc.T(owner ? "groups.deleted" : "groups.left"));
            _groupOpen = 0;
            await ShowChats();
        }

        long _leaveArmed;
        DateTime _leaveArmedAt;

        /// <summary>Из меню ⋮: первое нажатие просит подтвердить, второе (в течение 5 секунд) — выполняет.</summary>
        async Task LeaveOrDelete(Group g)
        {
            if (_leaveArmed != g.Id || (DateTime.Now - _leaveArmedAt).TotalSeconds > 5)
            {
                _leaveArmed = g.Id;
                _leaveArmedAt = DateTime.Now;
                _toast(Loc.T(g.Owner == Supa.Me ? "groups.delete.menu_confirm" : "groups.leave.menu_confirm"));
                return;
            }
            _leaveArmed = 0;
            await DoLeave(g);
        }

        /// <summary>Участник в панели группы: аватарка с «в сети», ник, галочка; под ником — «создатель», «приглашён» или статус.</summary>
        FrameworkElement MemberRow(GroupMember m, Group g)
        {
            var u = m.User;
            var avatar = UI.Avatar(u, 38);
            var name = NameWithBadges(UI.T(u.Nickname, 14, true), HorizontalAlignment.Left,
                u.Verified ? Pad(UI.Badge(14, u.Badge), 5) : null, u.IsPlus ? UI.PlusAfter(14, 5) : null);
            var col = new StackPanel { Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            col.Children.Add(name);
            if (u.Id == g.Owner) col.Children.Add(UI.L("groups.owner", 12, sub: true));
            else if (!m.Joined) col.Children.Add(UI.L("groups.pending", 12, sub: true));
            else if (!u.Bot) col.Children.Add(Online.Status(u.Id, 12));
            var row = new Grid { Margin = new Thickness(4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(u.Bot ? avatar : Online.WithDot(avatar, u.Id, 38));
            Grid.SetColumn(col, 1);
            row.Children.Add(col);
            return UI.IosRow(null, row, null, () => { Motion.Capture(avatar, "avatar:" + u.Id); _ = OpenProfile(u); });
        }

        // ================== кирпичики панели ==================

        /// <summary>Каркас панели: крестик сверху, прокрутка посередине, карточка MizuEngine внизу.</summary>
        (FrameworkElement root, StackPanel content) InfoFrame()
        {
            var card = Pane();
            var dock = new DockPanel { Margin = new Thickness(16, 14, 16, 16) };
            card.Child = dock;

            var brand = BrandCard();
            brand.Margin = new Thickness(0, 12, 0, 0);
            DockPanel.SetDock(brand, Dock.Bottom);
            dock.Children.Add(brand);

            // Крестик — над прокруткой: полоса прокрутки справа не должна его перекрывать
            var close = RoundIcon("\uE711", 36, () => ToggleInfo(false), iconSize: 12.5);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.ToolTip = Loc.T("chats.info_close");
            DockPanel.SetDock(close, Dock.Top);
            dock.Children.Add(close);

            var sp = new StackPanel { Margin = new Thickness(0, 0, 10, 4) };
            var scroll = UI.Scroll(sp);
            scroll.Margin = new Thickness(0, -6, -12, 0);
            dock.Children.Add(scroll);
            return (card, sp);
        }

        static FrameworkElement Centered(FrameworkElement e, double top)
        {
            e.HorizontalAlignment = HorizontalAlignment.Center;
            e.Margin = new Thickness(0, top, 0, 0);
            return e;
        }

        static FrameworkElement NameLine(string name, string badge, bool plus)
        {
            var row = NameWithBadges(UI.T(name, 22, true), HorizontalAlignment.Center,
                badge != null ? Pad(UI.Badge(20, badge), 7) : null, plus ? UI.PlusAfter(19) : null);
            return Centered(row, 14);
        }

        static FrameworkElement SubLine(string text)
        {
            var t = UI.T(text, 14, sub: true);
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            return Centered(t, 3);
        }

        /// <summary>Карточка со строками (статус, ID, участники).</summary>
        static Border InfoCard(params UIElement[] rows)
        {
            var sp = new StackPanel();
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i] is FrameworkElement fe && i > 0) fe.Margin = new Thickness(0, 10, 0, 0);
                sp.Children.Add(rows[i]);
            }
            var card = new Border { CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1), Padding = new Thickness(16, 14, 14, 14), Margin = new Thickness(0, 18, 0, 0), Child = sp };
            card.SetResourceReference(Border.BackgroundProperty, "CardBg");
            card.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            return card;
        }

        /// <summary>Строка карточки: значок (или кружок статуса) слева, две строки текста, что-то справа.</summary>
        static FrameworkElement InfoRow(FrameworkElement icon, FrameworkElement text, FrameworkElement trailing, Action click = null)
        {
            var g = new Grid { Background = Brushes.Transparent };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (icon != null)
            {
                icon.HorizontalAlignment = HorizontalAlignment.Left;
                icon.VerticalAlignment = VerticalAlignment.Top;
                icon.Margin = new Thickness(1, 5, 0, 0);
                g.Children.Add(icon);
            }
            text.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(text, 1);
            g.Children.Add(text);
            if (trailing != null)
            {
                trailing.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(trailing, 2);
                g.Children.Add(trailing);
            }
            if (click != null) { UI.Hand(g); g.MouseLeftButtonUp += (s, e) => click(); }
            return g;
        }

        /// <summary>«ID пользователя» / «ID группы» с кнопкой «Скопировать».</summary>
        FrameworkElement IdRow(string key, string value)
        {
            var lines = new StackPanel();
            lines.Children.Add(UI.L(key, 14.5, true));
            var v = UI.T(value, 13, sub: true);
            v.Margin = new Thickness(0, 2, 0, 0);
            lines.Children.Add(v);
            var copy = RoundIcon("", 34, null, iconSize: 13);
            copy.ToolTip = Loc.T("chats.copy");
            copy.Click += (s, e) =>
            {
                try { Clipboard.SetText(value); _toast(Loc.T("chats.copied")); Motion.Pop(copy, 0.85); }
                catch (Exception ex) { Settings.Log(ex); }
            };
            var slot = new Border { Width = 11, Height = 11 };   // под кружком статуса — ровная колонка
            return InfoRow(slot, lines, copy);
        }

        /// <summary>«Уведомления» с тумблером: выключил — у этого чата нет звука, карточки и уведомления в Island.</summary>
        FrameworkElement NotifyCard(string chatKey)
        {
            var sw = new CheckBox { Style = (Style)UI.Owner.FindResource("Switch"), IsChecked = !Inbox.IsMuted(chatKey), Margin = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
            sw.Click += (s, e) =>
            {
                bool on = sw.IsChecked == true;
                Inbox.SetMuted(chatKey, !on);
                _toast(Loc.T(on ? "chats.notify_on.done" : "chats.notify_off.done"));
            };
            var bell = UI.Icon("", 18);
            var label = UI.L("chats.notifications", 15, true);
            label.Margin = new Thickness(14, 0, 0, 1);
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(UI.Row(bell, label));
            Grid.SetColumn(sw, 1);
            g.Children.Add(sw);
            var card = new Border { CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1), Padding = new Thickness(16, 12, 14, 12), Margin = new Thickness(0, 12, 0, 0), Child = g };
            card.SetResourceReference(Border.BackgroundProperty, "CardBg");
            card.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            return card;
        }

        /// <summary>Отдельная карточка-кнопка: значок в кружке и текст (красный — для опасных действий). Tag — текст (TextBlock).</summary>
        static Button ActionCard(string glyph, string key, bool danger, Action click)
        {
            var icon = UI.Icon(glyph, 15, accent: !danger);
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            var ring = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), BorderThickness = new Thickness(1.6), Child = icon };
            if (danger) { icon.Foreground = DangerBrush; ring.BorderBrush = DangerBrush; }
            else ring.SetResourceReference(Border.BorderBrushProperty, "Accent");
            var text = UI.L(key, 14.5, true);
            text.Margin = new Thickness(12, 0, 0, 1);
            text.VerticalAlignment = VerticalAlignment.Center;
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            if (danger) text.Foreground = DangerBrush;
            var b = new Button
            {
                Style = (Style)UI.Owner.FindResource("GlassButton"), Padding = new Thickness(12, 11, 10, 11), Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch, Content = UI.Row(ring, text), Tag = text
            };
            b.Click += (s, e) => click();
            return b;
        }

        /// <summary>«Пожаловаться»: под кнопкой раскрывается выбор причины (спам / оскорбления / другое).</summary>
        FrameworkElement ReportCard(Func<string, Task<string>> send)
        {
            var sp = new StackPanel();
            var box = new Border { CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
            box.SetResourceReference(Border.BackgroundProperty, "CardBg");
            box.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            var panel = new StackPanel();
            panel.Children.Add(UI.L("chats.report_title", 13.5, true));
            var reasons = new WrapPanel { Margin = new Thickness(-8, 6, 0, 0) };
            panel.Children.Add(reasons);
            box.Child = panel;

            bool sent = false;
            var card = ActionCard("", "chats.report", true, () =>
            {
                if (sent) { _toast(Loc.T("chats.report_already")); return; }
                bool open = box.Visibility != Visibility.Visible;
                box.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                if (open) Motion.Enter(box);
            });
            foreach (var reason in new[] { "spam", "insult", "other" })
            {
                string r = reason;
                var b = SmallButton("posts.report." + r, false);
                b.Margin = new Thickness(8, 4, 0, 0);
                b.Click += async (s, e) =>
                {
                    foreach (var x in reasons.Children.OfType<Button>()) x.IsEnabled = false;
                    string err = await send(r);
                    if (err != null && err != Loc.T("chats.report_already"))
                    {
                        _toast(err);
                        foreach (var x in reasons.Children.OfType<Button>()) x.IsEnabled = true;
                        return;
                    }
                    sent = true;
                    _toast(err ?? Loc.T("chats.report_sent"));
                    Motion.Exit(box, 0, () => box.Visibility = Visibility.Collapsed);
                };
                reasons.Children.Add(b);
            }
            sp.Children.Add(card);
            sp.Children.Add(box);
            return sp;
        }

        static ImageSource _logo;

        /// <summary>Карточка внизу панели: значок MizuEngine и «Modern • Fast • Stylish».</summary>
        static FrameworkElement BrandCard()
        {
            if (_logo == null)
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri("pack://application:,,,/Assets/MizuEngine_logo_128.png");
                    bmp.DecodePixelWidth = 128;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    _logo = bmp;
                }
                catch (Exception e) { Settings.Log(e); }
            }
            var logo = new Grid { Width = 54, Height = 54 };
            logo.Children.Add(new Ellipse { Fill = _logo != null ? new ImageBrush(_logo) { Stretch = Stretch.UniformToFill } : (Brush)UI.Owner.FindResource("LogoBrush") });
            logo.Children.Add(new Ellipse { Stroke = new SolidColorBrush(Color.FromArgb(0x50, 255, 255, 255)), StrokeThickness = 1 });
            var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(UI.T("MizuEngine", 16, true));
            var tag = UI.T("Modern • Fast • Stylish", 12, sub: true);
            tag.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(tag);
            var row = UI.Row(logo, text);
            row.HorizontalAlignment = HorizontalAlignment.Center;
            var card = new Border { CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1), Padding = new Thickness(14, 16, 14, 16), Child = row };
            card.SetResourceReference(Border.BackgroundProperty, "CardBg");
            card.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            return card;
        }
    }
}
