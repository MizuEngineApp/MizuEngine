// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace MizuEngine
{
    /// <summary>Список подписчиков сообщества (с 0.11.4). Открывается кликом по плитке «Подписчики», виден всем.</summary>
    internal sealed partial class SocialPages
    {
        async Task ShowCommunityMembers(Community c)
        {
            var sp = new StackPanel { MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 14, 20) };
            var back = UI.Glass("", null, (s, e) => _ = ShowCommunity(), 8);
            var count = UI.T("", 13.5, sub: true);
            var head = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            head.Children.Add(UI.T(c.Name, 24, true));
            head.Children.Add(count);
            sp.Children.Add(UI.Row(back, head));

            var search = UI.Field("", 40);
            search.SetResourceReference(FrameworkElement.TagProperty, "community.members.search");
            search.Margin = new Thickness(0, 16, 0, 10);
            sp.Children.Add(search);

            var list = new StackPanel();
            var card = UI.Card(list, 10);
            sp.Children.Add(card);
            var more = UI.Glass("", "admin.more", null, 8);
            more.HorizontalAlignment = HorizontalAlignment.Center;
            more.Margin = new Thickness(0, 12, 0, 0);
            more.Visibility = Visibility.Collapsed;
            sp.Children.Add(more);
            Motion.Replace(_communityHost, UI.Scroll(sp));

            const int Page = 60;
            var all = new List<(Profile user, DateTime since)>();
            int offset = 0;
            list.Children.Add(UI.Loading());
            count.Text = string.Format(Loc.T("community.members.count"), await SocialApi.MembersCount(c.Id));

            async Task LoadPage()
            {
                more.IsEnabled = false;
                var page = await SocialApi.CommunityMembers(c.Id, offset, Page);
                offset += page.Count;
                all.AddRange(page);
                more.IsEnabled = true;
                more.Visibility = page.Count == Page ? Visibility.Visible : Visibility.Collapsed;
                Render();
            }

            void Render()
            {
                string q = (search.Text ?? "").Trim().ToLowerInvariant();
                list.Children.Clear();
                var shown = all.Where(m => q.Length == 0 || (m.user.Nickname ?? "").ToLowerInvariant().Contains(q) || (m.user.Handle ?? "").ToLowerInvariant().Contains(q)).ToList();
                if (shown.Count == 0) { list.Children.Add(Empty(q.Length == 0 ? "community.members.none" : "community.members.nothing")); return; }
                foreach (var (user, since) in shown)
                {
                    var u = user;
                    string sub = "@" + u.Handle + "  ·  " + string.Format(Loc.T("community.members.since"),
                        since.ToLocalTime().ToString("d MMM yyyy", System.Globalization.CultureInfo.CurrentUICulture));
                    Button row = null;
                    row = UI.UserRow(u, () => { Motion.Capture(UI.RowAvatar(row), "avatar:" + u.Id); _ = OpenProfile(u); }, sub, UI.Chevron());
                    list.Children.Add(row);
                }
                Motion.Stagger(list);
            }

            search.TextChanged += (s, e) => Render();
            more.Click += async (s, e) => await LoadPage();
            await LoadPage();
        }
    }
}
