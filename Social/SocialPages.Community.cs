// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace MizuEngine
{
    /// <summary>
    /// Страница «Сообщество»: сообщество MizuEngine, в которое может вступить любой.
    /// Создаёт и настраивает его только админ, посты пишут сотрудники (ты и твинк), комментировать могут все.
    /// </summary>
    internal sealed partial class SocialPages
    {
        public Task ShowCommunity() => Guard(_communityHost, ShowCommunityCore);

        async Task ShowCommunityCore()
        {
            if (!Gate(_communityHost)) return;
            if (_communityHost.Children.Count == 0) _communityHost.Children.Add(UI.Loading());
            else Motion.Dim(_communityHost);
            if (Supa.SignedIn && SocialApi.MyProfile == null) await SocialApi.LoadMe();

            var (c, ok) = await SocialApi.GetCommunity();
            if (!ok) { Motion.Replace(_communityHost, UI.L("err.network", 14, sub: true)); return; }
            if (c == null)
            {
                Motion.Replace(_communityHost, UI.Scroll(SocialApi.MyProfile?.IsAdmin == true ? CreateCommunityView() : NoCommunityView()));
                return;
            }
            Motion.Replace(_communityHost, UI.Scroll(CommunityView(c)));
        }

        static FrameworkElement NoCommunityView()
        {
            var sp = new StackPanel { MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(UI.H1("nav.community"));
            var c = new StackPanel();
            c.Children.Add(UI.IosRow(UI.Squircle("", UI.Purple, 34), UI.L("community.soon", 15, true)));
            var d = UI.L("community.soon.d", 13, sub: true, wrap: true);
            d.Margin = new Thickness(0, 8, 0, 0);
            c.Children.Add(d);
            sp.Children.Add(UI.Card(c, 18));
            return sp;
        }

        FrameworkElement CreateCommunityView()
        {
            var sp = new StackPanel { MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(UI.H1("community.create"));
            var sub = UI.L("community.create.d", 14, sub: true, wrap: true);
            sub.Margin = new Thickness(0, 6, 0, 0);
            sp.Children.Add(sub);
            var form = new StackPanel();
            var name = UI.Field("MizuEngine", 48);
            var desc = UI.Field("", 500, multiline: true);
            form.Children.Add(UI.Labeled("community.name", name));
            form.Children.Add(UI.Labeled("community.description", desc));
            var error = UI.Error();
            form.Children.Add(error);
            var create = UI.Primary("community.create_btn", null);
            create.Margin = new Thickness(0, 16, 0, 0);
            form.Children.Add(create);
            create.Click += async (s, e) =>
            {
                if (name.Text.Trim().Length == 0) { UI.ShowError(error, Loc.T("err.group_name")); return; }
                create.IsEnabled = false;
                string err = await SocialApi.CreateCommunity(name.Text.Trim(), desc.Text.Trim());
                create.IsEnabled = true;
                if (err != null) { UI.ShowError(error, err); return; }
                _toast(Loc.T("community.created"));
                await ShowCommunity();
            };
            sp.Children.Add(UI.Card(form, 18));
            return sp;
        }

        FrameworkElement CommunityView(Community c)
        {
            bool admin = SocialApi.MyProfile?.IsAdmin == true;
            var outer = new StackPanel { Margin = new Thickness(0, 0, 14, 20) };
            var (_, root, side) = TwoColumns(outer, 420);

            // ----- карточка сообщества: как профиль, но длиннее -----
            var card = new Border { CornerRadius = new CornerRadius(28), BorderThickness = new Thickness(1) };
            card.SetResourceReference(Border.BackgroundProperty, "CardBg");
            card.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            var body = new StackPanel();
            card.Child = body;
            body.Children.Add(Banner(c.BannerUrl, 200, admin ? () => _ = ChangeCommunityImage(c, "banner", 1600) : (Action)null));

            var logo = CommunityAvatar(c, 112);
            logo.HorizontalAlignment = HorizontalAlignment.Center;
            logo.Margin = new Thickness(0, -58, 0, 0);
            if (admin)
            {
                UI.Hand(logo);
                logo.ToolTip = Loc.T("profile.avatar");
                logo.MouseLeftButtonUp += async (s, e) => await ChangeCommunityImage(c, "avatar", 512);
            }
            body.Children.Add(logo);

            var info = new StackPanel { Margin = new Thickness(22, 12, 22, 22) };
            var nameRow = UI.Row(UI.T(c.Name, 26, true));
            nameRow.HorizontalAlignment = HorizontalAlignment.Center;
            if (c.Badge != null)
            {
                var badge = UI.AnimatedBadge(24, c.Badge);
                badge.Margin = new Thickness(8, 2, 0, 0);
                nameRow.Children.Add(badge);
            }
            info.Children.Add(nameRow);
            var kind = string.IsNullOrEmpty(c.Handle) ? UI.L("community.kind", 13.5, sub: true) : UI.T("@" + c.Handle + " · " + Loc.T("community.kind"), 13.5, sub: true);
            kind.HorizontalAlignment = HorizontalAlignment.Center;
            info.Children.Add(kind);
            if (!string.IsNullOrWhiteSpace(c.Description))
            {
                var d = UI.T(c.Description, 14, wrap: true);
                d.TextAlignment = TextAlignment.Center;
                d.Margin = new Thickness(0, 14, 0, 0);
                info.Children.Add(d);
            }

            var stats = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 18, 0, 0) };
            var sMembers = Stat("community.members", () => { });
            var sPosts = Stat("community.posts", () => { });
            stats.Children.Add(sMembers.ui);
            stats.Children.Add(sPosts.ui);
            info.Children.Add(stats);

            var actions = new WrapPanel { Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            info.Children.Add(actions);
            body.Children.Add(info);
            root.Children.Add(card);

            async Task RefreshStats()
            {
                var m = SocialApi.MembersCount(c.Id);
                var p = Supa.Count("posts", $"community_id=eq.{c.Id}");
                await Task.WhenAll(m, p);
                sMembers.value.Text = m.Result.ToString();
                sPosts.value.Text = p.Result.ToString();
            }
            _ = RefreshStats();

            // Вступить / выйти
            var join = new Button { Style = (Style)UI.Owner.FindResource("PrimaryButton"), MinWidth = 160 };
            join.SetResourceReference(ContentControl.ContentProperty, "community.join");
            join.Visibility = Visibility.Collapsed;
            AddAction(actions, join);
            bool member = false;
            async Task LoadMember()
            {
                if (!Supa.SignedIn) { join.Visibility = Visibility.Visible; return; }
                member = await SocialApi.IsMember(c.Id);
                join.SetResourceReference(ContentControl.ContentProperty, member ? "community.leave" : "community.join");
                join.Visibility = Visibility.Visible;
                Motion.Enter(join);
            }
            _ = LoadMember();
            join.Click += async (s, e) =>
            {
                if (!Supa.SignedIn) { _toast(Loc.T("social.need_login")); return; }
                join.IsEnabled = false;
                string err = member ? await SocialApi.Leave(c.Id) : await SocialApi.Join(c.Id);
                join.IsEnabled = true;
                if (err != null) { _toast(err); return; }
                member = !member;
                join.SetResourceReference(ContentControl.ContentProperty, member ? "community.leave" : "community.join");
                Motion.Pop(join, 0.92);
                if (member) _toast(Loc.T("community.joined"));
                await RefreshStats();
            };

            if (admin)
            {
                Border editCard = null;
                var edit = UI.Glass("", "profile.edit", (s, e) =>
                {
                    if (editCard != null) { var old = editCard; editCard = null; Motion.Exit(old, 0, () => root.Children.Remove(old)); return; }
                    editCard = CommunityEdit(c);
                    root.Children.Insert(1, editCard);
                    Motion.Enter(editCard);
                }, 9);
                AddAction(actions, edit);
            }

            root.Children.Add(UI.Section("community.about"));
            var about = new System.Collections.Generic.List<UIElement>();
            if (c.Badge != null) about.Add(UI.IosRow(UI.Badge(28, c.Badge), UI.L("community.official", 13.5, wrap: true)));
            about.Add(UI.IosRow(UI.Squircle("", UI.Teal), UI.L("community.comments_open", 13.5, wrap: true)));
            root.Children.Add(UI.IosGroup(about.ToArray()));

            // ----- посты справа -----
            side.Children.Add(UI.Section("posts.title", 0));
            side.Children.Add(Wrap(PostsBlock(() => SocialApi.GetCommunityPosts(c.Id), c.AsAuthor(), Supa.SignedIn && IsStaff, c.Id)));
            return outer;
        }

        /// <summary>Аватарка сообщества — скруглённый квадрат, чтобы отличалась от людей.</summary>
        static FrameworkElement CommunityAvatar(Community c, double size)
        {
            var b = new Border { Width = size + 8, Height = size + 8, CornerRadius = new CornerRadius((size + 8) * 0.28), BorderThickness = new Thickness(4) };
            b.SetResourceReference(Border.BorderBrushProperty, "WinBg");
            var inner = new Border { CornerRadius = new CornerRadius(size * 0.26) };
            if (!string.IsNullOrEmpty(c.AvatarUrl)) inner.Background = UI.WebBrush(c.AvatarUrl, (int)(size * 2.5), Stretch.Fill, square: true);
            else
            {
                inner.Background = (Brush)UI.Owner.FindResource("LogoBrush");
                inner.Child = new System.Windows.Shapes.Path
                {
                    Data = (Geometry)UI.Owner.FindResource("LogoM"), Fill = Brushes.White, Stretch = Stretch.Uniform,
                    Width = size * 0.42, Height = size * 0.36, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                };
            }
            b.Child = inner;
            return b;
        }

        Border CommunityEdit(Community c)
        {
            var sp = new StackPanel();
            sp.Children.Add(UI.L("community.edit", 16, true));
            var name = UI.Field(c.Name, 48);
            var handle = UI.Field(c.Handle ?? "", 24);
            handle.Tag = "@handle";
            var desc = UI.Field(c.Description, 500, multiline: true);
            sp.Children.Add(UI.Labeled("community.name", name));
            sp.Children.Add(UI.Labeled("community.handle", handle));
            sp.Children.Add(UI.Labeled("community.description", desc));

            // Галочка сообщества
            string badge = c.Badge;
            sp.Children.Add(UI.Labeled("admin.badge", BadgeChooser("cbadge", badge, k => badge = k)));

            var error = UI.Error();
            sp.Children.Add(error);
            var save = UI.Primary("profile.save", null);
            save.Margin = new Thickness(0, 16, 0, 0);
            save.Click += async (s, e) =>
            {
                UI.ShowError(error, null);
                var ch = new JsonObject();
                string h = handle.Text.Trim().TrimStart('@').ToLowerInvariant();
                if (name.Text.Trim() != c.Name) ch["name"] = name.Text.Trim();
                if (desc.Text.Trim() != c.Description) ch["description"] = desc.Text.Trim();
                if (badge != c.Badge) ch["badge"] = badge;
                if (h != (c.Handle ?? ""))
                {
                    if (h.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(h, "^[a-z0-9_]{3,24}$")) { UI.ShowError(error, Loc.T("err.handle_format")); return; }
                    ch["handle"] = h.Length == 0 ? null : h;
                }
                if (ch.Count == 0) return;
                if (name.Text.Trim().Length == 0) { UI.ShowError(error, Loc.T("err.group_name")); return; }
                save.IsEnabled = false;
                string err = await SocialApi.UpdateCommunity(c.Id, ch);
                save.IsEnabled = true;
                if (err != null) { UI.ShowError(error, err); return; }
                _toast(Loc.T("profile.saved"));
                await ShowCommunity();
            };
            sp.Children.Add(save);
            return UI.Card(sp);
        }

        async Task ChangeCommunityImage(Community c, string kind, int maxSide)
        {
            var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp" };
            if (dlg.ShowDialog() != true) return;
            if (new FileInfo(dlg.FileName).Length > 25 * 1024 * 1024) { _toast(Loc.T("err.too_big")); return; }
            _toast(Loc.T("profile.uploading"));
            string url = await SocialApi.UploadImage(dlg.FileName, "community-" + kind, maxSide);
            if (url == null) { _toast(Loc.T("err.upload")); return; }
            string err = await SocialApi.UpdateCommunity(c.Id, new JsonObject { [kind == "avatar" ? "avatar_url" : "banner_url"] = url });
            _toast(err ?? Loc.T("profile.saved"));
            await ShowCommunity();
        }
    }
}
