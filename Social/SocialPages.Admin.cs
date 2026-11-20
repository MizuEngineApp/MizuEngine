// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>
    /// Админ-панель: в профиле человека (галочка, подпись, роли, бан, мут, сброс) и общая страница
    /// (наказанные, группы и их галочки, журнал действий, цифры). Права проверяет сервер, здесь только интерфейс.
    /// </summary>
    internal sealed partial class SocialPages
    {
        static readonly string[] BadgeKinds = { null, "blue", "red", "black", "gray" };

        /// <summary>Можно ли мне управлять этим профилем: я админ, а он не владелец (№1) и не другой админ — их трогает только владелец.</summary>
        static bool CanManage(Profile p)
        {
            var me = SocialApi.MyProfile;
            if (me == null || !me.IsAdmin) return false;
            if (p.Id == me.Id) return true;
            if (p.Number == 1) return false;
            return !p.IsAdmin || me.Number == 1;
        }

        /// <summary>Выбор галочки пилюлями: нет / синяя / чёрная / серая.</summary>
        static FrameworkElement BadgeChooser(string group, string current, Action<string> chosen)
        {
            var row = new WrapPanel();
            foreach (var k in BadgeKinds)
            {
                string kind = k;
                var r = new RadioButton { Style = (Style)UI.Owner.FindResource("Pill"), GroupName = group, IsChecked = current == kind };
                var label = UI.L(kind == null ? "admin.badge.none" : "admin.badge." + kind, 12.5);
                r.Content = kind == null ? (object)label : UI.Row(UI.Badge(14, kind), Pad(label, 6));
                r.Click += (s, e) => chosen(kind);
                row.Children.Add(r);
            }
            return row;
        }

        static string Until(DateTime utc) =>
            SocialApi.Forever(utc) ? Loc.T("admin.forever") : utc.ToLocalTime().ToString("d MMM yyyy, HH:mm");

        // ================== карточка «ты заблокирован / в муте» ==================

        static Border PunishCard(Profile p)
        {
            var sp = new StackPanel();
            var red = Color.FromRgb(0xFF, 0x5F, 0x57);
            var title = UI.L(p.Banned ? "ban.you_title" : "mute.you_title", 16, true);
            title.Foreground = new SolidColorBrush(red);
            sp.Children.Add(UI.Row(UI.Squircle(p.Banned ? "" : "", p.Banned ? UI.Red : UI.Orange, 30), Pad(title, 10)));
            var until = UI.T(string.Format(Loc.T("ban.until"), Until(p.Banned ? p.BannedUntil : p.MutedUntil)), 13, sub: true, wrap: true);
            until.Margin = new Thickness(0, 10, 0, 0);
            sp.Children.Add(until);
            if (p.Banned && !string.IsNullOrWhiteSpace(p.BanReason))
            {
                var reason = UI.T(string.Format(Loc.T("ban.reason_is"), p.BanReason), 13, wrap: true);
                reason.Margin = new Thickness(0, 4, 0, 0);
                sp.Children.Add(reason);
            }
            var d = UI.L(p.Banned ? "ban.you_d" : "mute.you_d", 12.5, sub: true, wrap: true);
            d.Margin = new Thickness(0, 8, 0, 0);
            sp.Children.Add(d);
            var card = UI.Card(sp, 0);
            card.Margin = new Thickness(0, 0, 0, 14);
            return card;
        }

        // ================== админ-панель в профиле ==================

        Border AdminPanel(Profile p, bool isMe)
        {
            var me = SocialApi.MyProfile;
            var sp = new StackPanel();
            sp.Children.Add(UI.Row(UI.Squircle("", UI.Orange, 30), Pad(UI.L("admin.title", 16, true), 10)));
            var facts = UI.T(string.Format(Loc.T("admin.facts"), p.Number, Hours(p.UsageSeconds),
                                           p.Created > DateTime.MinValue ? p.Created.ToLocalTime().ToString("d MMM yyyy") : "—"), 12.5, sub: true, wrap: true);
            facts.Margin = new Thickness(0, 8, 0, 0);
            sp.Children.Add(facts);

            // ----- галочка -----
            sp.Children.Add(AdminSection("admin.badge"));
            string badge = p.Badge;
            var badgeRow = new WrapPanel();
            foreach (var k in BadgeKinds)
            {
                string kind = k;
                var r = new RadioButton { Style = (Style)UI.Owner.FindResource("Pill"), GroupName = "badge" + p.Id, IsChecked = badge == kind };
                var label = UI.L(kind == null ? "admin.badge.none" : "admin.badge." + kind, 13);
                r.Content = kind == null ? (object)label : UI.Row(UI.Badge(16, kind), Pad(label, 7));
                r.Click += (s, e) => badge = kind;
                badgeRow.Children.Add(r);
            }
            sp.Children.Add(badgeRow);

            // ----- подпись -----
            sp.Children.Add(AdminSection("admin.title_section"));
            var title = UI.Field(p.Title ?? "", 60);
            title.SetResourceReference(FrameworkElement.TagProperty, "admin.badge_title");
            sp.Children.Add(title);
            string bg = p.TitleBg, fg = p.TitleFg == "black" ? "black" : "white";
            var previewHost = new Border { Margin = new Thickness(0, 12, 0, 4), HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(previewHost);
            void Preview()
            {
                string text = string.IsNullOrWhiteSpace(title.Text) ? Loc.T("admin.title_sample") : title.Text.Trim();
                var pill = UI.TitlePill(text, bg, fg);
                pill.HorizontalAlignment = HorizontalAlignment.Left;
                previewHost.Child = pill;
            }
            title.TextChanged += (s, e) => Preview();

            var fgRow = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            var fgWhite = UI.Tab("admin.text_white", "fg" + p.Id, fg == "white", (s, e) => { fg = "white"; Preview(); });
            var fgBlack = UI.Tab("admin.text_black", "fg" + p.Id, fg == "black", (s, e) => { fg = "black"; Preview(); });
            var defaultBg = UI.Tab("admin.bg_default", "bgd" + p.Id, bg == null, null);
            fgRow.Children.Add(fgWhite);
            fgRow.Children.Add(fgBlack);
            fgRow.Children.Add(defaultBg);
            sp.Children.Add(fgRow);

            var (picker, setColor) = UI.ColorPicker(UI.ParseColor(bg) ?? Color.FromRgb(0x1D, 0x4F, 0xE0), c =>
            {
                bg = UI.Hex(c);
                defaultBg.IsChecked = false;
                Preview();
            });
            if (p.TitleBg == null) { bg = null; defaultBg.IsChecked = true; }
            defaultBg.Click += (s, e) => { bg = null; Preview(); };
            sp.Children.Add(Pad0(picker, 10));
            Preview();

            // ----- роли -----
            CheckBox staff = null, admin = null;
            if (!isMe)
            {
                sp.Children.Add(AdminSection("admin.roles"));
                staff = new CheckBox { Style = (Style)UI.Owner.FindResource("Switch"), IsChecked = p.Staff };
                staff.SetResourceReference(ContentControl.ContentProperty, "admin.role_staff");
                sp.Children.Add(staff);
                if (me?.Number == 1)
                {
                    admin = new CheckBox { Style = (Style)UI.Owner.FindResource("Switch"), IsChecked = p.IsAdmin, Margin = new Thickness(0, 8, 0, 0) };
                    admin.SetResourceReference(ContentControl.ContentProperty, "admin.role_admin");
                    sp.Children.Add(admin);
                }
            }

            var error = UI.Error();
            sp.Children.Add(error);
            var save = UI.Primary("admin.save", null);
            save.Margin = new Thickness(0, 14, 0, 0);
            sp.Children.Add(save);
            save.Click += async (s, e) =>
            {
                UI.ShowError(error, null);
                var ch = new JsonObject();
                string t = string.IsNullOrWhiteSpace(title.Text) ? null : title.Text.Trim();
                if (badge != p.Badge) ch["badge"] = badge;
                if (t != p.Title) ch["title"] = t;
                if (bg != p.TitleBg) ch["title_bg"] = bg;
                if (fg != (p.TitleFg ?? "white")) ch["title_fg"] = fg;
                if (staff != null && (staff.IsChecked == true) != p.Staff) ch["staff"] = staff.IsChecked == true;
                if (admin != null && (admin.IsChecked == true) != p.IsAdmin) ch["is_admin"] = admin.IsChecked == true;
                if (ch.Count == 0) { _toast(Loc.T("admin.nothing")); return; }
                save.IsEnabled = false;
                string err = await SocialApi.AdminUpdate(p.Id, ch);
                save.IsEnabled = true;
                if (err != null) { UI.ShowError(error, err); return; }
                _toast(Loc.T("profile.saved"));
                await ShowProfile(isMe ? null : p.Id);
            };

            if (!isMe)
            {
                sp.Children.Add(UI.Divider());
                sp.Children.Add(Moderation(p));
                sp.Children.Add(UI.Divider());
                sp.Children.Add(Resets(p));
            }
            // Mizu Plus: выдать или забрать (и себе тоже)
            sp.Children.Add(UI.Divider());
            sp.Children.Add(PlusAdmin(p));
            return UI.Card(sp, 16);
        }

        static TextBlock AdminSection(string key)
        {
            var t = UI.Section(key, 18);
            t.Margin = new Thickness(2, 18, 0, 8);
            return t;
        }

        static FrameworkElement Pad0(FrameworkElement e, double top) { e.Margin = new Thickness(0, top, 0, 0); return e; }

        /// <summary>Длительность наказания: 1 час, сутки, неделя, месяц, навсегда.</summary>
        static (FrameworkElement ui, Func<DateTime> until) DurationPicker(string group)
        {
            var spans = new (string key, TimeSpan? span)[]
            {
                ("admin.dur.hour", TimeSpan.FromHours(1)), ("admin.dur.day", TimeSpan.FromDays(1)),
                ("admin.dur.week", TimeSpan.FromDays(7)), ("admin.dur.month", TimeSpan.FromDays(30)), ("admin.dur.forever", null)
            };
            TimeSpan? chosen = TimeSpan.FromDays(1);
            var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var (key, span) in spans)
            {
                var sp = span;
                row.Children.Add(UI.Tab(key, group, sp == chosen, (s, e) => chosen = sp));
            }
            return (row, () => chosen.HasValue ? DateTime.UtcNow + chosen.Value : new DateTime(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc));
        }

        static string Iso(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);

        FrameworkElement Moderation(Profile p)
        {
            var sp = new StackPanel();
            sp.Children.Add(UI.Row(UI.Squircle("", UI.Red, 26), Pad(UI.L("admin.moderation", 15, true), 10)));

            // ----- бан -----
            if (p.Banned)
            {
                var st = UI.T(string.Format(Loc.T("admin.is_banned"), Until(p.BannedUntil)) +
                              (string.IsNullOrWhiteSpace(p.BanReason) ? "" : "\n" + string.Format(Loc.T("ban.reason_is"), p.BanReason)), 13, wrap: true);
                st.Margin = new Thickness(0, 10, 0, 0);
                sp.Children.Add(st);
                var unban = UI.Primary("admin.unban", null);
                unban.Margin = new Thickness(0, 10, 0, 0);
                unban.Click += async (s, e) => await Punish(p, new JsonObject { ["banned_until"] = null, ["ban_reason"] = null }, "admin.unbanned");
                sp.Children.Add(unban);
            }
            else
            {
                sp.Children.Add(AdminSection("admin.ban"));
                var reason = UI.Field("", 200);
                reason.SetResourceReference(FrameworkElement.TagProperty, "admin.ban_reason");
                sp.Children.Add(reason);
                var (dur, until) = DurationPicker("ban" + p.Id);
                sp.Children.Add(dur);
                var ban = UI.Danger("", "admin.ban_btn", null);
                ban.Margin = new Thickness(0, 6, 0, 0);
                ban.Click += async (s, e) =>
                {
                    if (!Confirm(string.Format(Loc.T("admin.ban_confirm"), p.Nickname))) return;
                    await Punish(p, new JsonObject { ["banned_until"] = Iso(until()), ["ban_reason"] = string.IsNullOrWhiteSpace(reason.Text) ? null : reason.Text.Trim() }, "admin.banned");
                };
                sp.Children.Add(ban);
            }

            // ----- мут -----
            if (p.MutedUntil > DateTime.UtcNow)
            {
                var st = UI.T(string.Format(Loc.T("admin.is_muted"), Until(p.MutedUntil)), 13, wrap: true);
                st.Margin = new Thickness(0, 16, 0, 0);
                sp.Children.Add(st);
                var unmute = UI.Glass("", "admin.unmute", async (s, e) => await Punish(p, new JsonObject { ["muted_until"] = null }, "admin.unmuted"), 9);
                unmute.Margin = new Thickness(0, 10, 0, 0);
                sp.Children.Add(unmute);
            }
            else
            {
                sp.Children.Add(AdminSection("admin.mute"));
                var hint = UI.L("admin.mute_d", 12, sub: true, wrap: true);
                sp.Children.Add(hint);
                var (dur, until) = DurationPicker("mute" + p.Id);
                sp.Children.Add(dur);
                var mute = UI.Danger("", "admin.mute_btn", async (s, e) => await Punish(p, new JsonObject { ["muted_until"] = Iso(until()) }, "admin.muted"));
                mute.Margin = new Thickness(0, 6, 0, 0);
                sp.Children.Add(mute);
            }
            return sp;
        }

        async Task Punish(Profile p, JsonObject ch, string doneKey)
        {
            string err = await SocialApi.AdminUpdate(p.Id, ch);
            if (err != null) { _toast(err); return; }
            _toast(Loc.T(doneKey));
            await ShowProfile(p.Id);
        }

        FrameworkElement Resets(Profile p)
        {
            var sp = new StackPanel();
            sp.Children.Add(UI.Row(UI.Squircle("", UI.Gray, 26), Pad(UI.L("admin.reset", 15, true), 10)));

            // Сменить @юз (например, оскорбительный)
            var handleRow = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            handleRow.ColumnDefinitions.Add(new ColumnDefinition());
            handleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var handle = UI.Field(p.Handle, 24);
            var setHandle = UI.Glass("", "admin.set_handle", null, 8);
            setHandle.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(setHandle, 1);
            handleRow.Children.Add(handle);
            handleRow.Children.Add(setHandle);
            sp.Children.Add(handleRow);
            setHandle.Click += async (s, e) =>
            {
                string h = handle.Text.Trim().TrimStart('@').ToLowerInvariant();
                if (h == p.Handle) return;
                if (!System.Text.RegularExpressions.Regex.IsMatch(h, "^[a-z0-9_]{3,24}$")) { _toast(Loc.T("err.handle_format")); return; }
                await Punish(p, new JsonObject { ["handle"] = h }, "profile.saved");
            };

            var wrap = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            void Add(string glyph, string key, Func<JsonObject> ch)
            {
                var b = UI.Glass(glyph, key, async (s, e) =>
                {
                    if (!Confirm(Loc.T(key) + " — " + p.Nickname + "?")) return;
                    await Punish(p, ch(), "admin.reset_done");
                }, 8);
                b.Margin = new Thickness(0, 0, 8, 8);
                wrap.Children.Add(b);
            }
            Add("", "admin.reset_avatar", () => new JsonObject { ["avatar_url"] = null });
            Add("", "admin.reset_banner", () => new JsonObject { ["banner_url"] = null });
            Add("", "admin.reset_bio", () => new JsonObject { ["bio"] = "" });
            Add("", "admin.reset_nick", () => new JsonObject { ["nickname"] = "User" + p.Number });
            sp.Children.Add(wrap);
            return sp;
        }

        static bool Confirm(string text) =>
            MessageBox.Show(Window.GetWindow(UI.Owner), text, "MizuEngine", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        // ================== страница «Админ-панель» ==================

        string _adminTab = "punished";

        public Task ShowAdminHome() => Guard(_profileHost, ShowAdminHomeCore);

        async Task ShowAdminHomeCore()
        {
            _viewedProfile = "admin";
            if (!Gate(_profileHost)) return;
            if (SocialApi.MyProfile?.IsAdmin != true) { await ShowProfile(null); return; }

            var root = new DockPanel();
            var top = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);

            var back = UI.Glass("\uE72B", "social.myprofile", async (s, e) => await ShowProfile(null), 8);
            back.Margin = new Thickness(0, 0, 0, 12);
            top.Children.Add(back);
            top.Children.Add(UI.Row(UI.Squircle("", UI.Orange, 38), Pad(UI.H1("admin.title"), 12)));

            // Цифры
            var stats = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Margin = new Thickness(-4, 18, 0, 0), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
            var sUsers = Stat("admin.stat.users", () => { _adminTab = "people"; _ = ShowAdminHome(); });
            var sGroups = Stat("admin.stat.groups", () => { _adminTab = "groups"; _ = ShowAdminHome(); });
            var sPosts = Stat("admin.stat.posts", () => { _adminTab = "reports"; _ = ShowAdminHome(); });
            var sPunished = Stat("admin.stat.punished", () => { _adminTab = "punished"; _ = ShowAdminHome(); });
            foreach (var x in new[] { sUsers, sGroups, sPosts, sPunished }) stats.Children.Add(x.ui);
            top.Children.Add(stats);

            var tabs = new WrapPanel { Margin = new Thickness(0, 18, 0, 10) };
            var list = new StackPanel { Margin = new Thickness(0, 0, 14, 20), MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var k in new[] { "punished", "reports", "people", "groups", "log", "plus", "official" })
            {
                string key = k;
                tabs.Children.Add(UI.Tab("admin.tab." + key, "admintabs", _adminTab == key, async (s, e) => { _adminTab = key; await FillAdminTab(list); }));
            }
            top.Children.Add(tabs);
            root.Children.Add(UI.Scroll(list));
            Motion.Replace(_profileHost, root);

            _ = LoadStats();
            async Task LoadStats()
            {
                var (u, g, p, pun) = await SocialApi.AdminStats();
                sUsers.value.Text = u.ToString(); sGroups.value.Text = g.ToString(); sPosts.value.Text = p.ToString(); sPunished.value.Text = pun.ToString();
            }
            await FillAdminTab(list);
        }

        async Task FillAdminTab(StackPanel list)
        {
            list.Children.Clear();
            switch (_adminTab)
            {
                case "people": FillAdminPeople(list); return;
                case "groups": FillAdminGroups(list); return;
                case "log": await FillAdminLog(list); return;
                case "reports": await FillPostReports(list); return;
                case "plus": await FillAdminPlus(list); return;
                case "official": await FillAdminOfficial(list); return;
                default: await FillPunished(list); return;
            }
        }

        async Task FillPunished(StackPanel list)
        {
            list.Children.Add(UI.Loading());
            var people = await SocialApi.Punished();
            list.Children.Clear();
            if (people.Count == 0) { list.Children.Add(Empty("admin.no_punished")); return; }
            foreach (var p in people)
            {
                var who = p;
                string text = who.Banned
                    ? string.Format(Loc.T("admin.is_banned"), Until(who.BannedUntil)) + (string.IsNullOrWhiteSpace(who.BanReason) ? "" : " · " + who.BanReason)
                    : string.Format(Loc.T("admin.is_muted"), Until(who.MutedUntil));
                var free = SmallButton(who.Banned ? "admin.unban" : "admin.unmute", true);
                free.Click += async (s, e) =>
                {
                    e.Handled = true;
                    var ch = who.Banned ? new JsonObject { ["banned_until"] = null, ["ban_reason"] = null } : new JsonObject { ["muted_until"] = null };
                    string err = await SocialApi.AdminUpdate(who.Id, ch);
                    _toast(err ?? Loc.T(who.Banned ? "admin.unbanned" : "admin.unmuted"));
                    if (err == null) await FillAdminTab(list);
                };
                list.Children.Add(UI.UserRow(who, () => _ = OpenProfile(who), text, free));
            }
            Motion.Stagger(list);
        }

        /// <summary>
        /// Все зарегистрированные: сразу список (по номеру аккаунта, по 40 штук, «Показать ещё»), поиск по нику, @юзу, почте и номеру.
        /// В строке — аватарка с кружком «в сети», ник, @юз, №, почта, дата регистрации и когда был в сети.
        /// Клик — профиль с «перелётом» аватарки, там все кнопки админа (галочка, подпись, Plus, бан, мут…).
        /// </summary>
        void FillAdminPeople(StackPanel list)
        {
            const int Page = 40;
            var search = UI.Field("", 60);
            search.SetResourceReference(FrameworkElement.TagProperty, "admin.users_search");
            search.Margin = new Thickness(0, 0, 0, 10);
            list.Children.Add(search);
            var count = UI.T("", 12.5, sub: true);
            count.Margin = new Thickness(2, 0, 0, 8);
            list.Children.Add(count);
            var found = new StackPanel();
            list.Children.Add(found);
            var more = SmallButton("admin.more", false);
            more.HorizontalAlignment = HorizontalAlignment.Left;
            more.Margin = new Thickness(0, 4, 0, 0);
            more.Visibility = Visibility.Collapsed;
            list.Children.Add(more);

            int offset = 0;
            string query = "";
            int version = 0;

            async Task Load(bool reset)
            {
                int v = reset ? ++version : version;
                if (reset) { offset = 0; found.Children.Clear(); found.Children.Add(UI.Loading()); }
                more.IsEnabled = false;
                var people = await SocialApi.AdminUsers(query, offset, Page);
                if (v != version) return;
                if (reset) found.Children.Clear();
                more.IsEnabled = true;
                int from = found.Children.Count;
                foreach (var u in people) found.Children.Add(AdminUserRow(u));
                offset += people.Count;
                more.Visibility = people.Count == Page ? Visibility.Visible : Visibility.Collapsed;
                if (offset == 0) found.Children.Add(Empty("friends.nothing"));
                count.Text = string.Format(Loc.T("admin.users_shown"), offset);
                Motion.Stagger(found, from);
            }

            var delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            delay.Tick += async (s, e) => { delay.Stop(); query = search.Text.Trim(); await Load(true); };
            search.TextChanged += (s, e) => { delay.Stop(); delay.Start(); };
            more.Click += async (s, e) => await Load(false);
            _ = Load(true);
        }

        FrameworkElement AdminUserRow(AdminUser u)
        {
            var p = u.Profile;
            string seen = u.Online ? Loc.T("online.now")
                        : u.LastSeen == default ? Loc.T("admin.never_online")
                        : Online.Describe(new Presence { Online = false, LastSeen = u.LastSeen });
            string sub = $"@{p.Handle}  ·  №{p.Number}  ·  {u.Email ?? "—"}";
            string sub2 = string.Format(Loc.T("admin.signed_up"), u.SignedUp.ToLocalTime().ToString("d MMM yyyy", System.Globalization.CultureInfo.CurrentUICulture)) + "  ·  " + seen;
            Button row = null;
            row = UI.UserRow(p, () => { Motion.Capture(UI.RowAvatar(row), "avatar:" + p.Id); _ = OpenProfile(p); }, sub, VersionPill(u), UI.Chevron());
            // Вторая строка подписи: дата регистрации и «в сети»
            if (row.Content is Grid g && g.Children.Count > 1 && g.Children[1] is StackPanel info)
            {
                var t = UI.T(sub2, 11.5, sub: true);
                t.TextTrimming = TextTrimming.CharacterEllipsis;
                if (u.Online) t.Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0xC7, 0x59));
                info.Children.Add(t);
            }
            return row;
        }

        /// <summary>
        /// Плашка с версией MizuEngine, с которой человек заходил последний раз: зелёная — как у меня или новее,
        /// оранжевая — старее, серая — версия до 0.11.4 (тогда версия ещё не отправлялась).
        /// </summary>
        static FrameworkElement VersionPill(AdminUser u)
        {
            if (u.LastSeen == default && u.AppVersion == null) return new FrameworkElement();
            string mine = SocialApi.AppVersion;
            string text = u.AppVersion == null ? Loc.T("admin.version.old") : "v" + u.AppVersion;
            Color c = u.AppVersion == null ? Color.FromRgb(0x8E, 0x95, 0xA3)
                    : Version.TryParse(u.AppVersion, out var a) && Version.TryParse(mine, out var b) && a < b ? Color.FromRgb(0xFF, 0x9F, 0x0A)
                    : Color.FromRgb(0x34, 0xC7, 0x59);
            var pill = new Border
            {
                CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 2, 8, 3), Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(0x2A, c.R, c.G, c.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, c.R, c.G, c.B)), BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(c) },
                ToolTip = Loc.T("admin.version.tip")
            };
            return pill;
        }

        void FillAdminGroups(StackPanel list)
        {
            var search = UI.Field("", 40);
            search.SetResourceReference(FrameworkElement.TagProperty, "admin.groups_search");
            search.Margin = new Thickness(0, 0, 0, 10);
            list.Children.Add(search);
            var found = new StackPanel();
            list.Children.Add(found);

            async Task Load()
            {
                string q = search.Text;
                found.Children.Clear();
                found.Children.Add(UI.Loading());
                var groups = await SocialApi.AdminGroups(q);
                if (search.Text != q) return;
                found.Children.Clear();
                if (groups.Count == 0) { found.Children.Add(Empty("admin.no_groups")); return; }
                foreach (var g in groups) found.Children.Add(AdminGroupRow(g));
                Motion.Stagger(found);
            }
            var delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            delay.Tick += async (s, e) => { delay.Stop(); await Load(); };
            search.TextChanged += (s, e) => { delay.Stop(); delay.Start(); };
            _ = Load();
        }

        /// <summary>Группа в админке: аватарка, название, @юз и выбор галочки.</summary>
        FrameworkElement AdminGroupRow(Group g)
        {
            var sp = new StackPanel();
            var head = UI.Row(UI.Avatar(g.Look(), 40));
            var names = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var nr = UI.Row(UI.T(g.Name, 14.5, true));
            if (g.Badge != null) nr.Children.Add(Pad(UI.Badge(15, g.Badge), 6));
            names.Children.Add(nr);
            names.Children.Add(UI.T(string.IsNullOrEmpty(g.Handle) ? Loc.T("groups.subtitle") + " #" + g.Id : "@" + g.Handle, 12, sub: true));
            head.Children.Add(names);
            sp.Children.Add(head);

            var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            foreach (var k in BadgeKinds)
            {
                string kind = k;
                var r = new RadioButton { Style = (Style)UI.Owner.FindResource("Pill"), GroupName = "gbadge" + g.Id, IsChecked = g.Badge == kind };
                var label = UI.L(kind == null ? "admin.badge.none" : "admin.badge." + kind, 12.5);
                r.Content = kind == null ? (object)label : UI.Row(UI.Badge(14, kind), Pad(label, 6));
                r.Click += async (s, e) =>
                {
                    if (g.Badge == kind) return;
                    string err = await SocialApi.UpdateGroup(g.Id, new JsonObject { ["badge"] = kind });
                    if (err != null) { _toast(err); return; }
                    g.Badge = kind;
                    _toast(Loc.T("profile.saved"));
                    nr.Children.RemoveRange(1, nr.Children.Count - 1);
                    if (kind != null) { var b = UI.Badge(15, kind); nr.Children.Add(Pad(b, 6)); Motion.Pop(b, 1.4); }
                };
                row.Children.Add(r);
            }
            sp.Children.Add(row);
            var card = Wrap(sp);
            card.Margin = new Thickness(0, 0, 0, 10);
            return card;
        }

        /// <summary>
        /// Жалобы на посты: пост целиком, автор, сколько жалоб и на что.
        /// «Удалить пост» — пост и жалобы исчезают; «Оставить» — жалобы снимаются, пост остаётся.
        /// </summary>
        async Task FillPostReports(StackPanel list)
        {
            list.Children.Add(UI.Loading());
            var items = await SocialApi.GetPostReports();
            list.Children.Clear();
            if (items == null) { list.Children.Add(Empty("err.network")); return; }
            if (items.Count == 0) { list.Children.Add(Empty("admin.no_reports")); return; }
            foreach (var it in items)
            {
                var rep = it;
                var sp = new StackPanel();

                var head = UI.Row(UI.Avatar(rep.Author, 34));
                var names = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                var nr = UI.Row(UI.T(rep.Author.Nickname, 13.5, true));
                if (rep.Author.Verified) { var b = UI.Badge(14, rep.Author.Badge); b.Margin = new Thickness(6, 0, 0, 0); nr.Children.Add(b); }
                names.Children.Add(nr);
                names.Children.Add(UI.T("@" + rep.Author.Handle + " · " + UI.Ago(rep.Post.Created), 11.5, sub: true));
                head.Children.Add(names);
                UI.Hand(head);
                head.MouseLeftButtonUp += (s, e) => _ = OpenProfile(rep.Author);
                sp.Children.Add(head);

                var body = UI.T(rep.Post.Body, 13.5, wrap: true);
                body.Margin = new Thickness(0, 10, 0, 0);
                sp.Children.Add(body);

                string reasons = string.Join(", ", rep.Reasons.Select(r => Loc.T("posts.report." + r)));
                var info = UI.T(string.Format(Loc.T("admin.reports_info"), rep.Count, reasons, UI.Ago(rep.Last)), 12, sub: true, wrap: true);
                info.Margin = new Thickness(0, 8, 0, 0);
                info.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A));
                sp.Children.Add(info);

                var del = SmallButton("admin.report_delete", true);
                del.Margin = new Thickness(0, 0, 0, 0);
                var keep = SmallButton("admin.report_keep", false);
                var row = UI.Row(del, keep);
                row.Margin = new Thickness(0, 12, 0, 0);
                sp.Children.Add(row);

                var card = Wrap(sp);
                card.Margin = new Thickness(0, 0, 0, 10);
                del.Click += async (s, e) =>
                {
                    del.IsEnabled = keep.IsEnabled = false;
                    string err = await SocialApi.DeletePost(rep.Post.Id);   // жалобы удалятся вместе с постом
                    if (err != null) { _toast(err); del.IsEnabled = keep.IsEnabled = true; return; }
                    _toast(Loc.T("posts.deleted"));
                    Motion.Exit(card, 0, () => list.Children.Remove(card));
                };
                keep.Click += async (s, e) =>
                {
                    del.IsEnabled = keep.IsEnabled = false;
                    string err = await SocialApi.DismissPostReports(rep.Post.Id);
                    if (err != null) { _toast(err); del.IsEnabled = keep.IsEnabled = true; return; }
                    _toast(Loc.T("admin.report_kept"));
                    Motion.Exit(card, 0, () => list.Children.Remove(card));
                };
                list.Children.Add(card);
            }
            Motion.Stagger(list);
        }

        async Task FillAdminLog(StackPanel list)
        {
            list.Children.Add(UI.Loading());
            var items = await SocialApi.AdminLog();
            list.Children.Clear();
            if (items.Count == 0) { list.Children.Add(Empty("admin.log_empty")); return; }
            var rows = new List<UIElement>();
            foreach (var it in items)
            {
                string target = it.TargetName ?? (it.TargetGroup > 0 ? Loc.T("groups.subtitle") + " #" + it.TargetGroup : "?");
                var text = new StackPanel();
                text.Children.Add(UI.T($"{it.AdminName} → {target}", 13.5, true));
                text.Children.Add(UI.T(DescribeChanges(it.Details), 12.5, sub: true, wrap: true));
                rows.Add(UI.IosRow(UI.Squircle(it.Action == "group" ? "" : "", it.Action == "group" ? UI.Teal : UI.Blue, 28), text, UI.T(UI.Ago(it.Created), 11.5, sub: true)));
            }
            list.Children.Add(UI.IosGroup(rows.ToArray()));
        }

        /// <summary>Изменения из журнала — понятными словами: «галочка: синяя, бан до …».</summary>
        static string DescribeChanges(JsonNode details)
        {
            if (details is not JsonObject o) return "";
            var parts = new List<string>();
            foreach (var kv in o)
            {
                string v = kv.Value == null ? "—" : kv.Value is JsonValue jv && jv.TryGetValue<string>(out var str) ? str : kv.Value.ToJsonString();
                switch (kv.Key)
                {
                    case "badge": parts.Add(Loc.T("admin.badge") + ": " + (kv.Value == null ? Loc.T("admin.badge.none") : Loc.T("admin.badge." + v))); break;
                    case "banned_until": parts.Add(kv.Value == null ? Loc.T("admin.unbanned") : Loc.T("admin.ban") + " → " + Until(SocialApi.Utc(kv.Value))); break;
                    case "muted_until": parts.Add(kv.Value == null ? Loc.T("admin.unmuted") : Loc.T("admin.mute") + " → " + Until(SocialApi.Utc(kv.Value))); break;
                    case "ban_reason": if (kv.Value != null) parts.Add(Loc.T("admin.ban_reason") + ": " + v); break;
                    case "title": parts.Add(Loc.T("admin.title_section") + ": " + v); break;
                    case "is_admin": parts.Add(Loc.T("admin.role_admin") + ": " + v); break;
                    case "staff": parts.Add(Loc.T("admin.role_staff") + ": " + v); break;
                    case "kind":   // выдача Mizu Plus
                        parts.Add("Mizu Plus: " + (v == "revoke" ? Loc.T("admin.plus.revoke") : v == "forever" ? Loc.T("plus.forever_short") : "+30 " + Loc.T("admin.plus.days")));
                        break;
                    case "until":
                    case "forever":
                        break;
                    default: parts.Add(kv.Key + ": " + (v.Length > 40 ? v.Substring(0, 40) + "…" : v)); break;
                }
            }
            return string.Join(" · ", parts);
        }
    }
}
