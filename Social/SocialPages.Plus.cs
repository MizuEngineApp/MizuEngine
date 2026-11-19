// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MizuEngine
{
    /// <summary>Mizu Plus в админ-панели: выдать/забрать подписку, донаты и выдачи, сборки от MizuEngine.</summary>
    internal sealed partial class SocialPages
    {
        /// <summary>Settings нужны, чтобы публиковать текущие настройки как сборку от MizuEngine.</summary>
        public static Settings AppSettings;

        // ================== в профиле человека ==================

        /// <summary>Блок «Mizu Plus» в админ-панели профиля: статус и кнопки «130 ₽ +30 дней», «500 ₽ навсегда», «Забрать».</summary>
        FrameworkElement PlusAdmin(Profile p)
        {
            var sp = new StackPanel();
            sp.Children.Add(UI.Row(UI.PlusBadge(26, clickable: false), Pad(UI.L("admin.plus", 15, true), 10)));
            string status = p.IsPlus ? Loc.T("plus.status.on") + " · " + Plus.UntilText(p.PlusForever, p.PlusUntil) : Loc.T("admin.plus.none");
            var st = UI.T(status, 13, wrap: true);
            st.Margin = new Thickness(0, 10, 0, 0);
            sp.Children.Add(st);

            var row = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            void Add(string key, string kind, bool danger)
            {
                var b = danger ? UI.Danger("", key, null) : UI.Glass("", key, null, 9);
                b.Margin = new Thickness(0, 0, 8, 8);
                b.Click += async (s, e) =>
                {
                    string what = Loc.T(key);
                    if (!Confirm(string.Format(Loc.T("admin.plus.confirm"), what, p.Nickname))) return;
                    b.IsEnabled = false;
                    string err = await PlusApi.AdminGrant(p.Id, kind);
                    b.IsEnabled = true;
                    if (err != null) { _toast(err); return; }
                    _toast(Loc.T(kind == "revoke" ? "admin.plus.revoked" : "admin.plus.granted"));
                    await ShowProfile(p.Id == Supa.Me ? null : p.Id);
                };
                row.Children.Add(b);
            }
            Add("admin.plus.month", "month", false);
            Add("admin.plus.forever", "forever", false);
            if (p.IsPlus) Add("admin.plus.revoke", "revoke", true);
            sp.Children.Add(row);
            var hint = UI.L("admin.plus.hint", 12, sub: true, wrap: true);
            sp.Children.Add(hint);
            return sp;
        }

        // ================== вкладка «Mizu Plus» общей админ-панели ==================

        async Task FillAdminPlus(StackPanel list)
        {
            list.Children.Add(UI.Loading());
            var j = await PlusApi.Overview();
            list.Children.Clear();
            if (j == null) { list.Children.Add(Empty("err.network")); return; }

            // DonationAlerts
            bool connected = (bool?)j["connected"] ?? false;
            var da = new StackPanel();
            da.Children.Add(UI.Row(UI.Squircle("", connected ? UI.Green : UI.Red, 28), Pad(UI.L(connected ? "admin.plus.da_on" : "admin.plus.da_off", 15, true), 10)));
            string facts = string.Format(Loc.T("admin.plus.counts"), (int?)j["active"] ?? 0, (int?)j["forever"] ?? 0);
            if (connected)
            {
                string who = (string)j["da_user"];
                if (!string.IsNullOrEmpty(who)) facts = "DonationAlerts: " + who + "\n" + facts;
                var poll = SocialApi.Utc(j["last_poll"]);
                if (poll > DateTime.MinValue) facts += "\n" + string.Format(Loc.T("admin.plus.last_poll"), poll.ToLocalTime().ToString("d MMM HH:mm:ss"));
            }
            string err = (string)j["last_error"];
            if (!string.IsNullOrEmpty(err)) facts += "\n" + Loc.T("admin.plus.error") + " " + err;
            var f = UI.T(facts, 12.5, sub: true, wrap: true);
            f.Margin = new Thickness(0, 8, 0, 0);
            da.Children.Add(f);
            if (!connected) { var h = UI.L("admin.plus.setup_hint", 12.5, wrap: true); h.Margin = new Thickness(0, 8, 0, 0); da.Children.Add(h); }
            list.Children.Add(UI.Card(da, 0));

            // Донаты
            list.Children.Add(UI.Section("admin.plus.donations", 18));
            var dons = j["donations"] as JsonArray;
            if (dons == null || dons.Count == 0) list.Children.Add(Empty("admin.plus.no_donations"));
            else
            {
                var rows = new System.Collections.Generic.List<UIElement>();
                foreach (var d in dons)
                {
                    string res = (string)d["result"] ?? "";
                    string name = (string)d["nickname"];
                    string line = $"{(string)d["username"] ?? "—"} · {(decimal?)d["amount"] ?? 0} {(string)d["currency"]}" +
                                  (name != null ? " → " + name + " @" + (string)d["handle"] : "");
                    string msg = (string)d["message"];
                    var text = new StackPanel();
                    text.Children.Add(UI.T(line, 13.5, true));
                    string sub = Loc.T("admin.plus.res." + res) + " · " + SocialApi.Utc(d["created_at"]).ToLocalTime().ToString("d MMM HH:mm");
                    if (!string.IsNullOrWhiteSpace(msg)) sub += "\n«" + msg + "»";
                    text.Children.Add(UI.T(sub, 12, sub: true, wrap: true));
                    var c = res == "forever" || res == "month" ? UI.Green : res == "dup" ? UI.Gray : UI.Orange;
                    rows.Add(UI.IosRow(UI.Squircle(res == "forever" || res == "month" ? "" : "", c, 26), text));
                }
                list.Children.Add(UI.IosGroup(rows.ToArray()));
            }

            // Выдачи
            list.Children.Add(UI.Section("admin.plus.grants", 18));
            var grants = j["grants"] as JsonArray;
            if (grants == null || grants.Count == 0) list.Children.Add(Empty("admin.plus.no_grants"));
            else
            {
                var rows = new System.Collections.Generic.List<UIElement>();
                foreach (var g in grants)
                {
                    string kind = (string)g["kind"] ?? "";
                    string src = (string)g["source"] == "admin" ? Loc.T("admin.plus.by_admin") + " " + (string)g["admin_nick"] : Loc.T("admin.plus.by_donation");
                    string what = kind == "revoke" ? Loc.T("admin.plus.revoke") : kind == "forever" ? Loc.T("plus.forever_short") : "+" + ((int?)g["days"] ?? 0) + " " + Loc.T("admin.plus.days");
                    var text = new StackPanel();
                    text.Children.Add(UI.T($"{(string)g["nickname"]} @{(string)g["handle"]} · {what}", 13.5, true));
                    text.Children.Add(UI.T(src + " · " + SocialApi.Utc(g["created_at"]).ToLocalTime().ToString("d MMM HH:mm"), 12, sub: true, wrap: true));
                    rows.Add(UI.IosRow(UI.PlusBadge(24, clickable: false), text));
                }
                list.Children.Add(UI.IosGroup(rows.ToArray()));
            }
        }

        // ================== вкладка «Сборки» общей админ-панели ==================

        async Task FillAdminOfficial(StackPanel list)
        {
            // Опубликовать текущие настройки
            var form = new StackPanel();
            form.Children.Add(UI.Row(UI.Squircle("", UI.Blue, 28), Pad(UI.L("admin.official.publish", 15, true), 10)));
            form.Children.Add(Pad0(UI.L("admin.official.publish_d", 12.5, sub: true, wrap: true), 8));
            var name = UI.Field("", 60);
            name.SetResourceReference(FrameworkElement.TagProperty, "presets.name");
            form.Children.Add(Pad0(name, 12));
            var desc = UI.Field("", 300, multiline: true);
            desc.SetResourceReference(FrameworkElement.TagProperty, "admin.official.desc");
            form.Children.Add(Pad0(desc, 8));
            var plusOnly = new CheckBox { Style = (Style)UI.Owner.FindResource("Switch"), IsChecked = true, Margin = new Thickness(0, 12, 0, 0) };
            plusOnly.SetResourceReference(ContentControl.ContentProperty, "admin.official.plus_only");
            form.Children.Add(plusOnly);
            var error = UI.Error();
            form.Children.Add(error);
            var publish = UI.Primary("admin.official.publish_btn", null);
            publish.Margin = new Thickness(0, 14, 0, 0);
            publish.Click += async (s, e) =>
            {
                UI.ShowError(error, null);
                if (AppSettings == null) return;
                publish.IsEnabled = false;
                string err = await PlusApi.Publish(AppSettings, name.Text, desc.Text, plusOnly.IsChecked == true);
                publish.IsEnabled = true;
                if (err != null) { UI.ShowError(error, err); return; }
                _toast(Loc.T("admin.official.published"));
                await FillAdminTab(list);
            };
            form.Children.Add(publish);
            list.Children.Add(UI.Card(form, 0));

            // Уже опубликованные
            list.Children.Add(UI.Section("admin.official.list", 18));
            var items = new StackPanel();
            list.Children.Add(items);
            items.Children.Add(UI.Loading());
            var all = await PlusApi.Catalog();
            items.Children.Clear();
            if (all == null) { items.Children.Add(Empty("err.network")); return; }
            if (all.Count == 0) { items.Children.Add(Empty("plus.presets.none")); return; }
            foreach (var op in all)
            {
                var preset = op;
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                FrameworkElement icon = preset.PlusOnly ? UI.PlusBadge(26, clickable: false) : UI.Squircle("", UI.Blue, 26);
                icon.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(icon);
                var text = new StackPanel { Margin = new Thickness(12, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(UI.T(preset.Name, 14, true));
                text.Children.Add(UI.T((preset.PlusOnly ? Loc.T("admin.official.for_plus") : Loc.T("admin.official.for_all")) + " · " + preset.Created.ToLocalTime().ToString("d MMM yyyy"), 12, sub: true));
                Grid.SetColumn(text, 1);
                g.Children.Add(text);
                var del = UI.Danger("", "presets.delete", null);
                del.Click += async (s, e) =>
                {
                    if (!Confirm(Loc.T("presets.delete") + " — " + preset.Name + "?")) return;
                    string err = await PlusApi.Delete(preset.Id);
                    if (err != null) { _toast(err); return; }
                    await FillAdminTab(list);
                };
                Grid.SetColumn(del, 2);
                g.Children.Add(del);
                var card = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 10, 12, 10), Margin = new Thickness(0, 0, 0, 8), Child = g, BorderThickness = new Thickness(1) };
                card.SetResourceReference(Border.BackgroundProperty, "CardBg");
                card.SetResourceReference(Border.BorderBrushProperty, "CardRim");
                items.Children.Add(card);
            }
        }
    }
}
