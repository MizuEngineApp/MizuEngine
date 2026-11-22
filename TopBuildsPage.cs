// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace MizuEngine
{
    /// <summary>
    /// «Лучшие сборки» (0.11.5): вкладки «Дня / Недели / Месяца» — 20 сборок с наибольшим числом лайков за период.
    /// Сборку можно лайкнуть (любому, даже сборку Plus без подписки), применить, сохранить к себе, скачать файлом
    /// (сборки с вещами Mizu Plus — только подписчикам), открыть и обсудить в комментариях (с ответами).
    /// </summary>
    internal sealed class TopBuildsPage
    {
        readonly Grid _host;
        readonly Settings _s;
        readonly Action<string> _toast;
        readonly Action _applyEffects;
        readonly SocialPages _social;
        readonly Action _openPresets;

        string _period = "day";
        Grid _content;                // список или открытая сборка

        public TopBuildsPage(Grid host, Settings s, Action<string> toast, Action applyEffects, SocialPages social, Action openPresets)
        {
            _host = host;
            _s = s;
            _toast = toast;
            _applyEffects = applyEffects;
            _social = social;
            _openPresets = openPresets;
        }

        public void Show()
        {
            _host.Children.Clear();
            _content = new Grid();
            _host.Children.Add(_content);
            ShowList();
        }

        /// <summary>Открыть сразу конкретную сборку (из «Моих сборок» → «Открыть»).</summary>
        public void ShowBuild(long id)
        {
            _host.Children.Clear();
            _content = new Grid();
            _host.Children.Add(_content);
            _ = OpenById(id);
        }

        async Task OpenById(long id)
        {
            _content.Children.Clear();
            _content.Children.Add(UI.Loading());
            var b = await BuildsApi.One(id);
            if (b == null) { ShowList(); _toast(Loc.T("builds.gone")); return; }
            ShowDetail(b, 0);
        }

        // ================== список ==================

        void ShowList()
        {
            var root = new StackPanel { MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 14, 20) };
            root.Children.Add(UI.H1("nav.topbuilds"));
            var sub = UI.L("top.sub", 14, sub: true, wrap: true);
            sub.Margin = new Thickness(0, 6, 0, 0);
            sub.MaxWidth = 720;
            sub.HorizontalAlignment = HorizontalAlignment.Left;
            root.Children.Add(sub);

            var bar = new DockPanel { Margin = new Thickness(0, 18, 0, 16), MaxWidth = 1050, HorizontalAlignment = HorizontalAlignment.Stretch };
            var mine = UI.Glass("", "top.publish_mine", (o, e) => _openPresets?.Invoke(), 9);
            DockPanel.SetDock(mine, System.Windows.Controls.Dock.Right);
            bar.Children.Add(mine);
            var tabs = new WrapPanel();
            var list = new WrapPanel();
            foreach (var (key, period) in new[] { ("top.day", "day"), ("top.week", "week"), ("top.month", "month") })
            {
                string p = period;
                var tab = UI.Tab(key, "topbuilds", _period == p, async (o, e) => { _period = p; await Fill(list); });
                tab.Margin = new Thickness(0, 0, 8, 0);
                tabs.Children.Add(tab);
            }
            bar.Children.Add(tabs);
            root.Children.Add(bar);
            root.Children.Add(list);

            var sv = UI.Scroll(root);
            Motion.Replace(_content, sv);
            _ = Fill(list);
        }

        async Task Fill(WrapPanel list)
        {
            list.Children.Clear();
            list.Children.Add(UI.Loading());
            if (!Supa.Configured) { list.Children.Clear(); list.Children.Add(UI.L("err.not_configured", 13, sub: true)); return; }
            var (items, err) = await BuildsApi.Top(_period);
            list.Children.Clear();
            if (items == null) { list.Children.Add(UI.T(err ?? Loc.T("err.network"), 13, sub: true, wrap: true)); return; }
            if (items.Count == 0)
            {
                var empty = new StackPanel { Margin = new Thickness(2, 4, 0, 0) };
                empty.Children.Add(UI.L("top.empty", 14, sub: true, wrap: true));
                var go = UI.Primary("top.publish_first", (o, e) => _openPresets?.Invoke());
                go.Margin = new Thickness(0, 12, 0, 0);
                empty.Children.Add(go);
                list.Children.Add(empty);
                return;
            }
            int n = 0;
            foreach (var b in items) list.Children.Add(BuildCard(b, ++n));
            Motion.Stagger(list);
        }

        FrameworkElement AuthorLine(Profile a, double size = 20)
        {
            var row = UI.Row(UI.Avatar(a, size));
            var nick = UI.T(a.Nickname, 12.5, true, sub: true);
            nick.Margin = new Thickness(7, 0, 0, 0);
            nick.VerticalAlignment = VerticalAlignment.Center;
            nick.TextTrimming = TextTrimming.CharacterEllipsis;
            nick.MaxWidth = 200;
            row.Children.Add(nick);
            if (a.Verified) { var bd = UI.Badge(13, a.Badge); bd.Margin = new Thickness(5, 0, 0, 0); row.Children.Add(bd); }
            UI.Hand(row);
            row.Background = Brushes.Transparent;
            row.MouseLeftButtonUp += (s, e) => { e.Handled = true; Motion.Capture(row.Children[0] as FrameworkElement, "avatar:" + a.Id); _ = _social.OpenProfileFromOutside(a); };
            return row;
        }

        Border BuildCard(SharedBuild b, int rank)
        {
            var sub = AuthorLine(b.Author);

            var buttons = new WrapPanel();
            bool canLike = Supa.SignedIn && SocialApi.MyProfile?.Banned != true;
            buttons.Children.Add(Heart.LikeButton(b.Liked, b.Likes, on => BuildsApi.Like(b.Id, on), _toast, 16,
                (on, cnt) => { b.Liked = on; b.Likes = cnt; }, canLike));
            var comments = new Button { Style = (Style)UI.Owner.FindResource("GlassButton"), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(8, 0, 0, 0) };
            var cc = UI.T(b.Comments.ToString(), 13, true);
            cc.Margin = new Thickness(7, 0, 0, 0);
            comments.Content = UI.Row(UI.Icon("", 15), cc);
            comments.ToolTip = Loc.T("comments.title");
            comments.Click += (o, e) => ShowDetail(b, rank, scrollToComments: true);
            buttons.Children.Add(comments);
            if (b.Saves > 0)
            {
                var saves = UI.Row(UI.Icon("", 13), UI.T(b.Saves.ToString(), 12, true, sub: true));
                ((FrameworkElement)saves.Children[1]).Margin = new Thickness(5, 0, 0, 0);
                saves.Opacity = 0.8;
                saves.Margin = new Thickness(12, 0, 0, 0);
                saves.VerticalAlignment = VerticalAlignment.Center;
                saves.ToolTip = Loc.T("top.saves");
                buttons.Children.Add(saves);
            }

            var cover = BuildUi.Cover(160, new CornerRadius(19, 19, 0, 0), url: b.CoverUrl);
            return BuildUi.Card(cover, b.Name, b.Plus, sub, b.Summary, buttons, BuildUi.Rank(rank), () => ShowDetail(b, rank));
        }

        // ================== открытая сборка ==================

        void ShowDetail(SharedBuild b, int rank, bool scrollToComments = false)
        {
            var root = new StackPanel { MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 14, 24) };

            var back = UI.Glass("", "nav.topbuilds", (o, e) => ShowList(), 8);
            root.Children.Add(back);

            // Обложка
            var coverHost = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            coverHost.Children.Add(BuildUi.Cover(280, new CornerRadius(22), url: b.CoverUrl, decode: 1400));
            if (rank > 0)
            {
                var r = BuildUi.Rank(rank);
                r.HorizontalAlignment = HorizontalAlignment.Left;
                r.VerticalAlignment = VerticalAlignment.Top;
                r.Margin = new Thickness(14);
                coverHost.Children.Add(r);
            }
            root.Children.Add(coverHost);

            // Название (+ Plus) и лайк
            var head = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
            bool canLike = Supa.SignedIn && SocialApi.MyProfile?.Banned != true;
            var like = Heart.LikeButton(b.Liked, b.Likes, on => BuildsApi.Like(b.Id, on), _toast, 20, (on, cnt) => { b.Liked = on; b.Likes = cnt; }, canLike);
            like.Padding = new Thickness(16, 8, 16, 8);
            like.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(like, System.Windows.Controls.Dock.Right);
            head.Children.Add(like);
            var title = UI.T(b.Name, 26, true);
            title.TextWrapping = TextWrapping.Wrap;
            title.VerticalAlignment = VerticalAlignment.Center;
            var tr = UI.Row(title);
            if (b.Plus) { var pb = UI.PlusBadge(24, big: true); pb.Margin = new Thickness(10, 0, 0, 0); pb.VerticalAlignment = VerticalAlignment.Center; tr.Children.Add(pb); }
            head.Children.Add(tr);
            root.Children.Add(head);

            // Автор и время
            var meta = UI.Row(AuthorLine(b.Author, 24));
            var when = UI.T("  ·  " + UI.Ago(b.Created), 12.5, sub: true);
            when.VerticalAlignment = VerticalAlignment.Center;
            meta.Children.Add(when);
            meta.Margin = new Thickness(0, 8, 0, 0);
            root.Children.Add(meta);

            if (!string.IsNullOrWhiteSpace(b.Description))
            {
                var d = UI.T(b.Description, 14, wrap: true);
                d.Margin = new Thickness(0, 12, 0, 0);
                root.Children.Add(d);
            }

            // Кнопки или замок Plus
            var actions = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
            if (!Supa.SignedIn)
            {
                root.Children.Add(Notice(UI.Squircle("", UI.Blue, 30), "builds.need_login", null));
            }
            else if (!b.Unlocked)
            {
                var go = UI.Primary("plus.unlock", (o, e) => UI.OpenPlus?.Invoke());
                root.Children.Add(Notice(UI.PlusBadge(30, clickable: false), "builds.plus_only_d", go));
            }
            else
            {
                Button apply = null, save = null, download = null;
                apply = UI.Primary("presets.apply", async (o, e) => await ApplyShared(b, apply));
                actions.Children.Add(apply);
                save = UI.Glass("", "builds.save_mine", async (o, e) =>
                {
                    save.IsEnabled = false;
                    var (p, err) = await BuildsApi.SaveToMine(b);
                    save.IsEnabled = true;
                    _toast(err ?? Loc.T("builds.saved_mine"));
                    if (p != null) { b.Saves++; }
                }, 9);
                save.Margin = new Thickness(8, 0, 0, 0);
                actions.Children.Add(save);
                download = UI.Glass("", "builds.download", async (o, e) =>
                {
                    var dlg = new SaveFileDialog { Filter = "MizuEngine preset|*.mizupreset", FileName = PresetsPage.MakeSafe(b.Name) + ".mizupreset" };
                    if (dlg.ShowDialog() != true) return;
                    download.IsEnabled = false;
                    var (file, err) = await BuildsApi.Download(b);
                    download.IsEnabled = true;
                    if (file == null) { _toast(err); return; }
                    try { File.Copy(file, dlg.FileName, true); _toast(Loc.T("presets.exported")); }
                    catch (Exception ex) { Settings.Log(ex); _toast(Loc.T("err.generic")); }
                    finally { try { File.Delete(file); } catch { } }
                }, 9);
                download.Margin = new Thickness(8, 0, 0, 0);
                actions.Children.Add(download);
            }
            if (b.Mine || SocialApi.MyProfile?.CanPost == true)
            {
                var del = UI.Glass("", "builds.unpublish", (o, e) => BuildUi.Confirm(_host, Loc.T("builds.unpublish_q"), Loc.T("builds.unpublish_d"), "builds.unpublish", async () =>
                {
                    string err = await BuildsApi.Delete(b.Id, b.FilePath);
                    if (err != null) { _toast(err); return; }
                    _toast(Loc.T("builds.unpublished"));
                    ShowList();
                }), 9);
                del.Margin = new Thickness(8, 0, 0, 0);
                actions.Children.Add(del);
            }
            if (actions.Children.Count > 0) root.Children.Add(actions);

            // Счётчики
            var stats = UI.T(string.Format(Loc.T("top.stats"), b.Likes, b.Saves, Math.Max(1, b.FileSize / 1024 / 1024)), 12, sub: true);
            stats.Margin = new Thickness(2, 12, 0, 0);
            root.Children.Add(stats);

            // Что внутри
            var inside = UI.Section("builds.inside", 22);
            inside.Margin = new Thickness(16, 22, 0, 7);
            root.Children.Add(inside);
            var plus = Presets.PlusParts(b.Summary);
            var rows = BuildUi.PartsOf(b.Summary).Select(part =>
            {
                var (glyph, color) = BuildUi.PartLook(part);
                var name = UI.Row(UI.T(Loc.T("builds.part." + part), 13.5, true));
                if (plus.Contains(part)) name.Children.Add(UI.PlusAfter(13, 6));
                var detail = UI.T(BuildUi.PartDetail(b.Summary, part), 12.5, sub: true);
                return (UIElement)UI.IosRow(UI.Squircle(glyph, plus.Contains(part) ? (UI.PlusLight, UI.PlusDeep) : color, 28), name, detail);
            }).ToArray();
            if (rows.Length > 0) root.Children.Add(UI.IosGroup(rows));

            // Комментарии (с ответами)
            var ch = UI.L("comments.title", 18, true);
            ch.Margin = new Thickness(0, 26, 0, 0);
            root.Children.Add(ch);
            var comments = _social.Comments("build", b.Id, cnt => b.Comments = cnt, moderator: b.Mine);
            root.Children.Add(comments);

            var sv = UI.Scroll(root);
            Motion.Replace(_content, sv, 1);
            if (scrollToComments)
                sv.Dispatcher.BeginInvoke(new Action(() => ch.BringIntoView()), System.Windows.Threading.DispatcherPriority.Background);
        }

        static Border Notice(FrameworkElement icon, string key, Button action)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(icon);
            var sp = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(UI.L(key, 13.5, wrap: true));
            if (action != null) { action.Margin = new Thickness(0, 10, 0, 0); sp.Children.Add(action); }
            Grid.SetColumn(sp, 1);
            g.Children.Add(sp);
            var card = UI.Card(g, 16);
            return card;
        }

        /// <summary>Применить чужую сборку: скачать во временную папку → окно с разделами → резервная копия → применить.</summary>
        async Task ApplyShared(SharedBuild b, Button btn)
        {
            btn.IsEnabled = false;
            var (p, err) = await BuildsApi.Fetch(b);
            btn.IsEnabled = true;
            if (p == null) { _toast(err); return; }
            BuildUi.ApplyDialog(_host, b.Name, p.Summary, async parts =>
            {
                await Task.Run(() => Presets.Backup(_s));
                Presets.Apply(p, _s, parts);
                _applyEffects();
                _toast(Loc.T("presets.applied"));
            }, closed: () => Presets.Delete(p));   // временная копия больше не нужна
        }
    }
}
