// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MizuEngine
{
    /// <summary>
    /// Вкладка «Лидеры»: топ-10 по часам в MizuEngine, по дате создания аккаунта и по номеру.
    /// Простой список: первые трое — цвета золота, серебра и бронзы, внизу — карточка «Ты» с настоящим местом.
    /// </summary>
    internal sealed partial class SocialPages
    {
        Grid _leadersHost;
        string _leadersTab = "hours";

        static readonly (Color a, Color b)[] Medals =
        {
            (Color.FromRgb(0xFF, 0xE2, 0x7A), Color.FromRgb(0xF0, 0xB0, 0x00)),   // золото
            (Color.FromRgb(0xF2, 0xF5, 0xFA), Color.FromRgb(0xA4, 0xAF, 0xBF)),   // серебро
            (Color.FromRgb(0xF2, 0xC2, 0x9B), Color.FromRgb(0xC2, 0x76, 0x3E))    // бронза
        };

        public void AttachLeaders(Grid host) => _leadersHost = host;

        public Task ShowLeaders() => _leadersHost == null ? Task.CompletedTask : Guard(_leadersHost, ShowLeadersCore);

        async Task ShowLeadersCore()
        {
            if (!Gate(_leadersHost)) return;
            if (Supa.SignedIn && SocialApi.MyProfile == null) await SocialApi.LoadMe();

            var root = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 14, 24) };
            root.Children.Add(UI.Row(UI.Squircle("", UI.Orange, 40), Pad(UI.H1("leaders.title"), 14)));
            var sub = UI.L("leaders.sub", 14, sub: true, wrap: true);
            sub.Margin = new Thickness(0, 8, 0, 0);
            root.Children.Add(sub);

            var body = new StackPanel();
            var tabs = new WrapPanel { Margin = new Thickness(0, 18, 0, 6) };
            foreach (var k in new[] { "hours", "created", "number" })
            {
                string kind = k;
                tabs.Children.Add(UI.Tab("leaders.tab." + kind, "leaderstabs", _leadersTab == kind, async (s, e) =>
                {
                    if (_leadersTab == kind) return;
                    _leadersTab = kind;
                    await FillLeaders(body);
                }));
            }
            root.Children.Add(tabs);
            root.Children.Add(body);
            Motion.Replace(_leadersHost, UI.Scroll(root));
            await FillLeaders(body);
        }

        async Task FillLeaders(StackPanel body)
        {
            string kind = _leadersTab;
            if (body.Children.Count == 0) body.Children.Add(UI.Loading());
            else Motion.Dim(body);
            var list = await SocialApi.Leaders(kind);
            if (kind != _leadersTab) return;   // пока грузили, переключили вкладку
            body.Children.Clear();
            if (list == null) { body.Children.Add(Empty("err.network")); return; }
            if (list.Count == 0) { body.Children.Add(Empty("leaders.empty")); return; }

            var me = SocialApi.MyProfile;
            // Простой список: 1 — золото, 2 — серебро, 3 — бронза, дальше обычные строки
            var rows = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            for (int i = 0; i < list.Count; i++) rows.Children.Add(LeaderRow(list[i], i, kind, me));
            body.Children.Add(rows);

            if (me != null)
            {
                int place = list.FindIndex(p => p.Id == me.Id) + 1;
                if (place == 0) place = await SocialApi.PlaceOf(me, kind);
                if (kind != _leadersTab) return;
                body.Children.Add(MeCard(me, place, kind));
            }
            else
            {
                var hint = UI.L("leaders.login", 13, sub: true, wrap: true);
                hint.Margin = new Thickness(2, 18, 0, 0);
                body.Children.Add(hint);
            }
            Motion.Stagger(body);
        }

        static string LeaderValue(Profile p, string kind) => kind switch
        {
            "hours" => string.Format(Loc.T("leaders.hours"), Hours(p.UsageSeconds)),
            "created" => p.Created > DateTime.MinValue ? p.Created.ToLocalTime().ToString("d MMM yyyy") : "—",
            _ => "№" + p.Number
        };

        /// <summary>Строка лидера. У первых трёх — цвет медали: рамка, подсветка, кружок с местом и значение.</summary>
        FrameworkElement LeaderRow(Profile p, int index, string kind, Profile me)
        {
            bool medal = index < 3;
            var (ca, cb) = medal ? Medals[index] : (Colors.Transparent, Colors.Transparent);
            // На светлой теме светлое золото и серебро не читаются — текст берём из тёмного края медали
            bool lightTheme = UI.Owner.TryFindResource("Text") is SolidColorBrush tb && !UI.IsLight(tb.Color);
            var medalText = new SolidColorBrush(lightTheme ? cb : ca);

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Место: у медалистов — кружок цвета медали, у остальных просто цифра
            FrameworkElement place;
            if (medal)
                place = new Border
                {
                    Width = 30, Height = 30, CornerRadius = new CornerRadius(15), VerticalAlignment = VerticalAlignment.Center,
                    Background = new LinearGradientBrush(ca, cb, 90), BorderBrush = new SolidColorBrush(Color.FromArgb(0xB0, 255, 255, 255)), BorderThickness = new Thickness(1.2),
                    Child = new TextBlock { Text = (index + 1).ToString(), FontWeight = FontWeights.Bold, FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(0x2A, 0x22, 0x10)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                };
            else
            {
                var t = UI.T((index + 1).ToString(), 15, true, sub: true);
                t.Width = 30;
                t.TextAlignment = TextAlignment.Center;
                t.VerticalAlignment = VerticalAlignment.Center;
                place = t;
            }
            g.Children.Add(place);

            var av = UI.Avatar(p, 44);
            FrameworkElement avBox = av;
            if (medal)
                avBox = new Border { CornerRadius = new CornerRadius(26), BorderThickness = new Thickness(2.5), Padding = new Thickness(1.5), Child = av, BorderBrush = new LinearGradientBrush(ca, cb, 45) };
            avBox.Margin = new Thickness(12, 0, 0, 0);
            avBox.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(avBox, 1);
            g.Children.Add(avBox);

            var names = new StackPanel { Margin = new Thickness(12, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            var nick = UI.T(p.Nickname, 15, true);
            nick.TextTrimming = TextTrimming.CharacterEllipsis;
            var nr = UI.Row(nick);
            if (p.Verified) nr.Children.Add(Pad(UI.Badge(15, p.Badge), 6));
            if (p.IsPlus) nr.Children.Add(UI.PlusAfter(15));
            if (medal) nick.Foreground = medalText;
            else if (me != null && p.Id == me.Id) nick.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            names.Children.Add(nr);
            names.Children.Add(UI.T("@" + p.Handle, 12, sub: true));
            Grid.SetColumn(names, 2);
            g.Children.Add(names);

            var value = UI.T(LeaderValue(p, kind), 14, true);
            value.VerticalAlignment = VerticalAlignment.Center;
            if (medal) value.Foreground = medalText;
            Grid.SetColumn(value, 3);
            g.Children.Add(value);

            var card = new Border { CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(medal ? 1.5 : 1), Padding = new Thickness(14, 10, 18, 10), Margin = new Thickness(0, 0, 0, 8), Child = g };
            if (medal)
            {
                card.Background = new LinearGradientBrush(Color.FromArgb(0x40, ca.R, ca.G, ca.B), Color.FromArgb(0x10, cb.R, cb.G, cb.B), 0);
                card.BorderBrush = new SolidColorBrush(Color.FromArgb(0xA0, ca.R, ca.G, ca.B));
                card.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = cb, BlurRadius = index == 0 ? 18 : 12, ShadowDepth = 0, Opacity = index == 0 ? 0.45 : 0.3 };
            }
            else
            {
                card.SetResourceReference(Border.BackgroundProperty, "CardBg");
                card.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            }
            UI.Hand(card);
            var who = p;
            card.MouseLeftButtonDown += (s, e) => Motion.Pop(card, 0.98);
            card.MouseLeftButtonUp += (s, e) => { Motion.Capture(av, "avatar:" + who.Id); _ = OpenProfile(who); };
            return card;
        }

        /// <summary>Карточка «Ты»: самая крупная аватарка, ник, настоящее место и значение — с подсветкой цветом темы.</summary>
        FrameworkElement MeCard(Profile me, int place, string kind)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var ring = new Border { CornerRadius = new CornerRadius(56), BorderThickness = new Thickness(3), Padding = new Thickness(3), Child = UI.Avatar(me, 100), VerticalAlignment = VerticalAlignment.Center };
            ring.SetResourceReference(Border.BorderBrushProperty, "Accent");
            g.Children.Add(ring);

            var info = new StackPanel { Margin = new Thickness(20, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            var you = UI.L("leaders.you", 12, true, sub: true);
            you.Typography.Capitals = FontCapitals.AllSmallCaps;
            info.Children.Add(you);
            var nr = UI.Row(UI.T(me.Nickname, 24, true));
            if (me.Verified) nr.Children.Add(Pad(UI.AnimatedBadge(22, me.Badge), 8));
            if (me.IsPlus) nr.Children.Add(UI.PlusAfter(22, 8, big: true));
            info.Children.Add(nr);
            info.Children.Add(UI.T("@" + me.Handle, 13.5, sub: true));
            var value = UI.T(LeaderValue(me, kind), 14, true);
            value.Margin = new Thickness(0, 8, 0, 0);
            info.Children.Add(value);
            Grid.SetColumn(info, 1);
            g.Children.Add(info);

            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            var placeLabel = UI.L("leaders.place", 12, sub: true);
            placeLabel.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(placeLabel);
            var big = UI.Accent("№" + place, 40);
            big.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(big);
            Grid.SetColumn(right, 2);
            g.Children.Add(right);

            var card = new Border { CornerRadius = new CornerRadius(24), BorderThickness = new Thickness(2), Padding = new Thickness(20), Margin = new Thickness(0, 22, 0, 0), Child = g };
            card.SetResourceReference(Border.BackgroundProperty, "CardBg");
            card.SetResourceReference(Border.BorderBrushProperty, "Accent");
            var accent = UI.Owner.TryFindResource("Accent") is SolidColorBrush sb ? sb.Color : Color.FromRgb(0x4A, 0x97, 0xFF);
            card.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = accent, BlurRadius = 26, ShadowDepth = 0, Opacity = 0.55 };
            UI.Hand(card);
            card.MouseLeftButtonUp += (s, e) => _ = OpenProfile(me);
            return card;
        }
    }
}
