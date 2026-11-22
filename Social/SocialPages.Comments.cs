// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MizuEngine
{
    /// <summary>
    /// Комментарии (0.11.5) — общие для постов и сборок: ответы как в Telegram (изогнутая стрелка → цитата над полем ввода →
    /// в ответе видно, кому ответили, клик по цитате подсвечивает исходный комментарий) и лайки с анимацией сердечка.
    /// </summary>
    internal sealed partial class SocialPages
    {
        /// <summary>Открыть профиль человека с других страниц (например, автора сборки в «Лучших сборках»).</summary>
        internal Task OpenProfileFromOutside(Profile p) => OpenProfile(p);

        /// <summary>
        /// Список комментариев и поле ввода. kind — "post" или "build", id — пост или сборка.
        /// changed — новое число комментариев. moderator — может удалять любые комментарии здесь (например, автор сборки).
        /// </summary>
        internal FrameworkElement Comments(string kind, long id, Action<int> changed, bool moderator = false)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            var list = new StackPanel();
            sp.Children.Add(list);

            var rows = new Dictionary<long, Border>();     // id комментария → его «пузырь» (для подсветки по клику на цитату)
            Comment replyTo = null;

            // ---- плашка «Ответ для …» над полем ввода ----
            var replyHost = new ContentControl { Focusable = false };
            sp.Children.Add(replyHost);

            var input = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            input.ColumnDefinitions.Add(new ColumnDefinition());
            input.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var box = UI.Field("", 1000);
            box.SetResourceReference(FrameworkElement.TagProperty, "comments.placeholder");
            var send = new Button { Style = (Style)UI.Owner.FindResource("PrimaryButton"), Margin = new Thickness(8, 0, 0, 0) };
            send.Content = new TextBlock { Text = "", FontFamily = (FontFamily)UI.Owner.FindResource("Icons"), FontSize = 13 };
            Grid.SetColumn(send, 1);
            input.Children.Add(box);
            input.Children.Add(send);
            if (Supa.SignedIn) sp.Children.Add(input);

            void CancelReply()
            {
                replyTo = null;
                if (replyHost.Content is FrameworkElement old)
                    Motion.Exit(old, 0, () => { if (replyHost.Content == old) replyHost.Content = null; });
            }

            void StartReply(Comment c)
            {
                if (!Supa.SignedIn) return;
                replyTo = c;
                var bar = ReplyBar(c, CancelReply);
                replyHost.Content = bar;
                Motion.Enter(bar);
                box.Focus();
                box.CaretIndex = box.Text.Length;
            }

            void Highlight(long target)
            {
                if (!rows.TryGetValue(target, out var bubble)) return;
                bubble.BringIntoView();
                var flash = new Border { CornerRadius = bubble.CornerRadius, IsHitTestVisible = false, Opacity = 0 };
                flash.SetResourceReference(Border.BackgroundProperty, "Accent");
                if (bubble.Parent is Grid g)
                {
                    Grid.SetColumn(flash, Grid.GetColumn(bubble));
                    flash.Margin = bubble.Margin;
                    flash.HorizontalAlignment = HorizontalAlignment.Left;
                    flash.Width = bubble.ActualWidth;
                    flash.Height = bubble.ActualHeight;
                    flash.VerticalAlignment = VerticalAlignment.Top;
                    g.Children.Add(flash);
                    Anim.To(flash, UIElement.OpacityProperty, 0.28, 160, done: () =>
                        Anim.To(flash, UIElement.OpacityProperty, 0, 900, delayMs: 250, done: () => g.Children.Remove(flash)));
                }
            }

            async Task Fill()
            {
                var items = await SocialApi.GetComments(kind, id);
                list.Children.Clear();
                rows.Clear();
                if (items == null) { list.Children.Add(UI.L("err.network", 12.5, sub: true)); return; }
                if (items.Count == 0) { var t = UI.L("comments.empty", 12.5, sub: true); t.Margin = new Thickness(2, 0, 0, 4); list.Children.Add(t); }
                var byId = items.ToDictionary(c => c.Id);
                bool staff = SocialApi.MyProfile?.CanPost == true;
                foreach (var c in items)
                {
                    byId.TryGetValue(c.ReplyTo ?? 0, out var target);
                    var row = CommentRow(kind, c, target, c.Author.Id == Supa.Me || staff || moderator, Fill, StartReply, Highlight, out var bubble);
                    rows[c.Id] = bubble;
                    list.Children.Add(row);
                }
                Motion.Stagger(list);
                changed?.Invoke(items.Count);
            }

            async Task Send()
            {
                string text = box.Text.Trim();
                if (text.Length == 0) return;
                send.IsEnabled = false;
                string err = await SocialApi.AddComment(kind, id, text, replyTo?.Id);
                send.IsEnabled = true;
                if (err != null) { _toast(err); return; }
                box.Text = "";
                CancelReply();
                await Fill();
            }
            send.Click += async (s, e) => await Send();
            box.KeyDown += async (s, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; await Send(); }
                else if (e.Key == Key.Escape && replyTo != null) { e.Handled = true; CancelReply(); }
            };
            list.Children.Add(UI.Loading());
            _ = Fill();
            return sp;
        }

        /// <summary>Плашка над полем ввода: полоска цвета темы, «Ответ для ник», начало комментария и крестик.</summary>
        static FrameworkElement ReplyBar(Comment c, Action cancel)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var arrow = UI.Icon("", 15, accent: true);
            arrow.Margin = new Thickness(2, 0, 10, 0);
            g.Children.Add(arrow);

            var line = new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0, 1, 9, 1) };
            line.SetResourceReference(Border.BackgroundProperty, "Accent");
            Grid.SetColumn(line, 1);
            g.Children.Add(line);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var who = UI.Accent(string.Format(Loc.T("comments.reply_to"), c.Author.Nickname), 12.5);
            who.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(who);
            var snippet = UI.T(OneLine(c.Body), 12.5, sub: true);
            snippet.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(snippet);
            Grid.SetColumn(text, 2);
            g.Children.Add(text);

            var close = new Button { Style = (Style)UI.Owner.FindResource("CaptionBtn"), Width = 30, Height = 28, Content = "", ToolTip = Loc.T("comments.reply_cancel"), VerticalAlignment = VerticalAlignment.Center };
            close.Click += (s, e) => cancel();
            Grid.SetColumn(close, 3);
            g.Children.Add(close);

            var bar = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 7, 6, 7), Margin = new Thickness(0, 8, 0, 0), Child = g };
            bar.SetResourceReference(Border.BackgroundProperty, "HoverBg");
            return bar;
        }

        static string OneLine(string s)
        {
            s = (s ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            return s.Length > 140 ? s.Substring(0, 140) + "…" : s;
        }

        FrameworkElement CommentRow(string kind, Comment c, Comment target, bool canDelete, Func<Task> reload,
                                    Action<Comment> reply, Action<long> highlight, out Border bubbleOut)
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var av = UI.Avatar(c.Author, 30);
            av.VerticalAlignment = VerticalAlignment.Top;
            UI.Hand(av);
            av.MouseLeftButtonUp += (s, e) => { Motion.Capture(av, "avatar:" + c.Author.Id); _ = OpenProfile(c.Author); };
            g.Children.Add(av);

            var bubble = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 7, 12, 8), Margin = new Thickness(8, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            bubble.SetResourceReference(Border.BackgroundProperty, "HoverBg");
            var inner = new StackPanel();
            var nr = UI.Row(UI.T(c.Author.Nickname, 12.5, true));
            if (c.Author.Verified) { var b = UI.Badge(13, c.Author.Badge); b.Margin = new Thickness(5, 0, 0, 0); nr.Children.Add(b); }
            if (c.Author.IsPlus) nr.Children.Add(UI.PlusAfter(13, 5));
            var time = UI.T(UI.Ago(c.Created), 11, sub: true);
            time.Margin = new Thickness(8, 0, 0, 0);
            time.VerticalAlignment = VerticalAlignment.Center;
            nr.Children.Add(time);
            inner.Children.Add(nr);

            // Цитата: кому ответили и начало его комментария. Клик — подсветить исходный комментарий
            if (target != null)
            {
                var q = new StackPanel();
                var qWho = UI.Accent(target.Author.Nickname, 12);
                qWho.TextTrimming = TextTrimming.CharacterEllipsis;
                q.Children.Add(qWho);
                var qText = UI.T(OneLine(target.Body), 12, sub: true);
                qText.TextTrimming = TextTrimming.CharacterEllipsis;
                q.Children.Add(qText);
                var quote = new Border { CornerRadius = new CornerRadius(4, 9, 9, 4), BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(8, 4, 10, 5), Margin = new Thickness(0, 5, 0, 3), Child = q, MaxWidth = 460 };
                quote.SetResourceReference(Border.BorderBrushProperty, "Accent");
                quote.SetResourceReference(Border.BackgroundProperty, "CardBg");
                UI.Hand(quote);
                quote.ToolTip = Loc.T("comments.show_original");
                long tid = target.Id;
                quote.MouseLeftButtonUp += (s, e) => { e.Handled = true; highlight(tid); };
                inner.Children.Add(quote);
            }
            else if (c.ReplyTo.HasValue)
            {
                var gone = UI.L("comments.reply_deleted", 12, sub: true);
                gone.FontStyle = FontStyles.Italic;
                gone.Margin = new Thickness(0, 4, 0, 2);
                inner.Children.Add(gone);
            }

            var text = UI.T(c.Body, 13.5, wrap: true);
            text.Margin = new Thickness(0, 2, 0, 0);
            inner.Children.Add(text);
            bubble.Child = inner;
            Grid.SetColumn(bubble, 1);
            g.Children.Add(bubble);
            bubbleOut = bubble;

            // Справа: ответить (изогнутая стрелка), лайк, удалить
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4, 0, 0, 0) };
            bool signed = Supa.SignedIn && SocialApi.MyProfile?.Banned != true;
            if (signed)
            {
                var rb = new Button { Style = (Style)UI.Owner.FindResource("CaptionBtn"), Width = 30, Height = 28, Content = "", ToolTip = Loc.T("comments.reply"), Opacity = 0.7 };
                rb.MouseEnter += (s, e) => Anim.To(rb, UIElement.OpacityProperty, 1, 120);
                rb.MouseLeave += (s, e) => Anim.To(rb, UIElement.OpacityProperty, 0.7, 220);
                rb.Click += (s, e) => reply(c);
                actions.Children.Add(rb);
            }
            actions.Children.Add(Heart.LikeChip(c.Liked, c.Likes, on => SocialApi.LikeComment(kind, c.Id, on), _toast, 13, signed));
            if (canDelete)
            {
                var del = new Button { Style = (Style)UI.Owner.FindResource("CaptionBtn"), Width = 30, Height = 28, Content = "", ToolTip = Loc.T("comments.delete") };
                del.Click += async (s, e) =>
                {
                    string err = await SocialApi.DeleteComment(kind, c.Id);
                    if (err != null) { _toast(err); return; }
                    await reload();
                };
                actions.Children.Add(del);
            }
            Grid.SetColumn(actions, 2);
            g.Children.Add(actions);
            return g;
        }
    }
}
