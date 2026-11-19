// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>
    /// Куда показать уведомление о новом сообщении:
    /// окно MizuEngine открыто — карточка в правом нижнем углу окна;
    /// свёрнуто или закрыто в трей — карточка в правом нижнем углу экрана, над доком.
    /// Если человек как раз смотрит в этот чат — ничего не показываем.
    /// </summary>
    internal static class Notifier
    {
        /// <summary>Открыть главное окно (ставит Program).</summary>
        public static Action OpenMain;
        static NotifyWindow _desk;

        public static void Init() => Inbox.Arrived += OnArrived;

        static void OnArrived(InboxMessage m)
        {
            var s = ChatSounds.S;
            var main = MainWindow.Current;
            bool shown = main != null && main.IsVisible && main.WindowState != WindowState.Minimized;
            if (shown && main.IsActive && main.ActiveChatKey == m.Key) return;   // смотрит в этот чат — звук сыграет сама переписка

            if (s != null && s.NotifyDnd) return;   // «Не беспокоить»: только цифра на «Чатах»
            ChatSounds.PlayIn();
            if (s != null && !s.MsgNotify) return;
            var ui = Application.Current.Dispatcher;
            string title = string.IsNullOrEmpty(m.GroupName) ? m.Sender?.Nickname ?? "MizuEngine" : m.GroupName + " · " + (m.Sender?.Nickname ?? "");
            Shell.Run(() => Shell.Island?.Notify(title, Shown(m), m.Sender?.AvatarUrl, () => ui.BeginInvoke(new Action(() => Open(m)))));
            if (shown) main.ShowMessageCard(m);
            else
            {
                _desk ??= new NotifyWindow();
                _desk.Push(m);
            }
        }

        /// <summary>Клик по уведомлению: открыть MizuEngine прямо на этой переписке.</summary>
        public static void Open(InboxMessage m)
        {
            OpenMain?.Invoke();
            MainWindow.Current?.OpenConversation(m.FriendId, m.GroupId);
        }

        /// <summary>Что показать в уведомлении: текст сообщения или, если так настроено, просто «Новое сообщение».</summary>
        public static string Shown(InboxMessage m) => ChatSounds.S?.NotifyPreview == false ? Loc.T("notify.hidden") : Preview(m);

        /// <summary>Текст уведомления: само сообщение, а для сборки или приглашения — понятная подпись.</summary>
        public static string Preview(InboxMessage m) => m.Kind switch
        {
            "preset" => string.Format(Loc.T("notify.preset"), m.Text),
            "group_invite" => Loc.T("notify.invite"),
            "group_added" => string.Format(Loc.T("groups.you_were_added"), m.Text),
            _ => m.Text ?? ""
        };

        // ================== карточка ==================

        /// <summary>Карточка уведомления в цветах текущей темы (как фон приложения).</summary>
        public static Border Card(InboxMessage m, Action open, Action close)
        {
            var root = new Grid();

            // Подложка: тема может быть полупрозрачной (стекло) — снизу плотный слой, чтобы текст читался на любом фоне
            var under = new Border { CornerRadius = new CornerRadius(18) };
            bool lightTheme = Application.Current.TryFindResource("Text") is SolidColorBrush tb && (0.299 * tb.Color.R + 0.587 * tb.Color.G + 0.114 * tb.Color.B) / 255 < 0.5;
            under.Background = new SolidColorBrush(lightTheme ? Color.FromArgb(0xF6, 0xF7, 0xF9, 0xFC) : Color.FromArgb(0xF2, 0x12, 0x16, 0x20));
            root.Children.Add(under);
            var bg = new Border { CornerRadius = new CornerRadius(18) };
            bg.SetResourceReference(Border.BackgroundProperty, "WinBg");
            root.Children.Add(bg);
            var card = new Border { CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1) };
            card.SetResourceReference(Border.BackgroundProperty, "CardBg");
            card.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            root.Children.Add(card);

            var content = new Grid { Margin = new Thickness(14, 11, 12, 13) };
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Шапка: значок MizuEngine, «MizuEngine · сейчас» и крестик
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var close1 = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 11, Opacity = 0, Cursor = Cursors.Hand, Padding = new Thickness(6, 2, 2, 2), VerticalAlignment = VerticalAlignment.Center };
            close1.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
            close1.MouseLeftButtonUp += (s, e) => { e.Handled = true; close(); };
            DockPanel.SetDock(close1, Dock.Right);
            head.Children.Add(close1);
            var logo = new Image { Width = 16, Height = 16, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            try { logo.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/MizuEngine_logo_128.png")); } catch { }
            head.Children.Add(logo);
            var app = new TextBlock { Text = "MizuEngine  ·  " + Loc.T("notify.now"), FontSize = 11.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            app.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
            head.Children.Add(app);
            content.Children.Add(head);

            // Кто и что написал
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            body.ColumnDefinitions.Add(new ColumnDefinition());
            var avatar = Avatar(m.Sender, 40);
            avatar.VerticalAlignment = VerticalAlignment.Top;
            body.Children.Add(avatar);
            var texts = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
            string who = m.Sender?.Nickname ?? "MizuEngine";
            if (!string.IsNullOrEmpty(m.GroupName)) who = m.GroupName + "  ·  " + who;
            var name = new TextBlock { Text = who, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            texts.Children.Add(name);
            var text = new TextBlock { Text = Shown(m), FontSize = 13, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 38, Margin = new Thickness(0, 2, 0, 0) };
            text.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
            texts.Children.Add(text);
            Grid.SetColumn(texts, 1);
            body.Children.Add(texts);
            Grid.SetRow(body, 1);
            content.Children.Add(body);
            root.Children.Add(content);

            var outer = new Border
            {
                Child = root, Width = 340, Margin = new Thickness(0, 10, 0, 0), Cursor = Cursors.Hand,
                RenderTransform = new TranslateTransform(380, 0), Opacity = 0, Background = Brushes.Transparent
            };
            outer.SetResourceReference(UIElement.EffectProperty, "CardShadow");
            outer.MouseEnter += (s, e) => close1.Opacity = 1;
            outer.MouseLeave += (s, e) => close1.Opacity = 0;
            outer.MouseLeftButtonUp += (s, e) => open();
            return outer;
        }

        /// <summary>Круглая аватарка без зависимостей от главного окна (оно может быть закрыто).</summary>
        static FrameworkElement Avatar(Profile p, double size)
        {
            var g = new Grid { Width = size, Height = size };
            g.Children.Add(new Ellipse { Fill = new LinearGradientBrush(Color.FromRgb(0x6F, 0xB6, 0xFF), Color.FromRgb(0x2F, 0x6B, 0xFF), 45) });
            g.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(p?.Nickname) ? "M" : p.Nickname.Substring(0, 1).ToUpperInvariant(),
                FontSize = size * 0.42, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            });
            UI.AvatarFace(g, p?.AvatarUrl, size);
            return g;
        }

        /// <summary>Показать карточку в стопке: выезжает справа, сама исчезает через 6 секунд (пока мышь над ней — ждёт).</summary>
        public static void Push(Panel host, InboxMessage m, Action afterRemove = null)
        {
            Border card = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            void Remove()
            {
                timer.Stop();
                if (card == null || !host.Children.Contains(card)) return;
                var t = (TranslateTransform)card.RenderTransform;
                Anim.To(card, UIElement.OpacityProperty, 0, 220);
                Anim.To(t, TranslateTransform.XProperty, 380, 320, done: () => { host.Children.Remove(card); afterRemove?.Invoke(); });
            }
            card = Card(m, () => { Remove(); Open(m); }, Remove);
            card.MouseEnter += (s, e) => timer.Stop();
            card.MouseLeave += (s, e) => timer.Start();
            timer.Tick += (s, e) => Remove();

            // Не больше трёх карточек: самая старая уходит
            while (host.Children.Count >= 3) host.Children.RemoveAt(0);
            host.Children.Add(card);
            Anim.To(card, UIElement.OpacityProperty, 1, 260);
            Anim.To((TranslateTransform)card.RenderTransform, TranslateTransform.XProperty, 0, 520, ease: Anim.Spring);
            timer.Start();
        }
    }

    /// <summary>Прозрачное окно для уведомлений, когда MizuEngine свёрнут или закрыт: правый нижний угол экрана, над доком.</summary>
    internal sealed class NotifyWindow : Window
    {
        readonly StackPanel _stack = new StackPanel { Margin = new Thickness(18, 4, 18, 18), VerticalAlignment = VerticalAlignment.Bottom };

        public NotifyWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = 340 + 36;
            Title = "MizuEngine Notifications";
            Content = _stack;
            SourceInitialized += (s, e) =>
            {
                IntPtr h = new WindowInteropHelper(this).Handle;
                long ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
                Native.SetWindowLongPtr(h, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE));
            };
            SizeChanged += (s, e) => Place();
        }

        public void Push(InboxMessage m)
        {
            if (!IsVisible) Show();
            Notifier.Push(_stack, m, () => { if (_stack.Children.Count == 0) Hide(); });
            Place();
        }

        void Place()
        {
            var area = SystemParameters.WorkArea;
            var s = ChatSounds.S;
            // Док с автоскрытием не занимает место у Windows, но выезжает снизу — поднимаемся над ним
            double dock = s != null && s.DockEnabled && s.DockAutohide ? s.DockSize + 2 * DockView.Pad + DockView.BottomMargin + 6 : 0;
            Left = area.Right - ActualWidth;
            Top = area.Bottom - ActualHeight - dock;
        }
    }
}
