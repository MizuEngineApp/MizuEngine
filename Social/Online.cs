// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>
    /// Статус «в сети». Пока MizuEngine запущен (даже свёрнут в трей), программа раз в минуту сообщает серверу «я в сети»,
    /// при выходе — «не в сети». Зелёный кружок на аватарке — в сети, серый — нет.
    /// Кто видит статус, человек выбирает в «Настройки → Аккаунт»: все, подписчики, друзья или никто.
    /// Скрытый статус не рисуется вовсе — ни кружка, ни надписи.
    /// </summary>
    internal static class Online
    {
        static readonly Dictionary<string, Presence> Known = new Dictionary<string, Presence>();
        static readonly List<(string id, WeakReference<Ellipse> dot, WeakReference<TextBlock> text)> Watchers = new List<(string, WeakReference<Ellipse>, WeakReference<TextBlock>)>();
        static DispatcherTimer _ping, _refresh, _soon;
        static bool _busy;

        static readonly Brush Green = Frozen(new SolidColorBrush(Color.FromRgb(0x34, 0xC7, 0x59)));
        static readonly Brush Gray = Frozen(new SolidColorBrush(Color.FromRgb(0x8E, 0x94, 0x9E)));

        static Brush Frozen(Brush b) { b.Freeze(); return b; }

        /// <summary>Запуск программы: «я в сети» сразу и раз в минуту.</summary>
        public static void Start()
        {
            _ping = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _ping.Tick += (s, e) => Ping();
            _ping.Start();
            _refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _refresh.Tick += async (s, e) => await Refresh();
            _refresh.Start();
            _soon = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _soon.Tick += async (s, e) => { _soon.Stop(); await Refresh(); };
            Supa.SessionChanged += () => Application.Current?.Dispatcher.BeginInvoke(new Action(() => { Known.Clear(); Ping(); Soon(); }));
            Ping();
        }

        static void Ping()
        {
            if (Supa.SignedIn) _ = SocialApi.PresencePing(true);
        }

        /// <summary>Выход из программы: «не в сети» (ждём ответа не больше полутора секунд).</summary>
        public static void GoOffline()
        {
            try
            {
                if (!Supa.SignedIn) return;
                Task.Run(() => SocialApi.PresencePing(false)).Wait(TimeSpan.FromSeconds(1.5));
            }
            catch { }
        }

        // ================== кружок и надпись ==================

        /// <summary>Аватарка с кружком «в сети» в правом нижнем углу. Пока статус неизвестен или скрыт — кружка нет.</summary>
        public static FrameworkElement WithDot(FrameworkElement avatar, string userId, double size)
        {
            if (string.IsNullOrEmpty(userId)) return avatar;
            var g = new Grid { Width = size, Height = size, HorizontalAlignment = avatar.HorizontalAlignment, VerticalAlignment = avatar.VerticalAlignment, Margin = avatar.Margin };
            avatar.Margin = new Thickness(0);
            avatar.HorizontalAlignment = HorizontalAlignment.Stretch;
            avatar.VerticalAlignment = VerticalAlignment.Stretch;
            g.Children.Add(avatar);
            double d = Math.Max(9, Math.Round(size * 0.27));
            var dot = new Ellipse
            {
                Width = d, Height = d, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                StrokeThickness = Math.Max(1.5, d * 0.17), Visibility = Visibility.Collapsed, IsHitTestVisible = false,
                Margin = new Thickness(0, 0, size * 0.02, size * 0.02)
            };
            dot.Stroke = new SolidColorBrush(LightTheme() ? Color.FromRgb(0xF4, 0xF7, 0xFB) : Color.FromRgb(0x14, 0x17, 0x20));
            g.Children.Add(dot);
            Watch(userId, dot, null);
            return g;
        }

        /// <summary>Надпись «в сети» или «был(а) в сети 5 мин назад» (для шапки чата и профиля).</summary>
        public static TextBlock Status(string userId, double fontSize = 12)
        {
            var t = new TextBlock { FontSize = fontSize, Visibility = Visibility.Collapsed, TextTrimming = TextTrimming.CharacterEllipsis };
            t.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
            if (!string.IsNullOrEmpty(userId)) Watch(userId, null, t);
            return t;
        }

        static void Watch(string id, Ellipse dot, TextBlock text)
        {
            Watchers.Add((id, dot == null ? null : new WeakReference<Ellipse>(dot), text == null ? null : new WeakReference<TextBlock>(text)));
            if (Known.TryGetValue(id, out var p)) Paint(p, dot, text);
            Soon();
        }

        static void Soon()
        {
            if (_soon == null) return;
            _soon.Stop();
            _soon.Start();
        }

        static async Task Refresh()
        {
            if (_busy || !Supa.SignedIn) return;
            // Убираем тех, чьи кружки уже не на экране
            Watchers.RemoveAll(w => !(w.dot != null && w.dot.TryGetTarget(out _)) && !(w.text != null && w.text.TryGetTarget(out _)));
            if (Watchers.Count > 600) Watchers.RemoveRange(0, Watchers.Count - 600);
            // Спрашиваем только про тех, кто сейчас на экране (или только что создан)
            var ids = Watchers.Where(w => Alive(w.dot) || Alive(w.text)).Select(w => w.id).Distinct().ToList();
            if (ids.Count == 0) return;
            _busy = true;
            try
            {
                var fresh = await SocialApi.GetPresence(ids);
                foreach (var id in ids)
                {
                    if (fresh.TryGetValue(id, out var p)) Known[id] = p; else Known.Remove(id);
                }
                foreach (var w in Watchers)
                {
                    Ellipse dot = null; TextBlock text = null;
                    w.dot?.TryGetTarget(out dot);
                    w.text?.TryGetTarget(out text);
                    Paint(Known.TryGetValue(w.id, out var p) ? p : null, dot, text);
                }
            }
            catch (Exception e) { Settings.Log(e); }
            finally { _busy = false; }
        }

        static bool Alive<T>(WeakReference<T> r) where T : FrameworkElement =>
            r != null && r.TryGetTarget(out var e) && (e.IsLoaded || !e.IsInitialized || e.Parent != null);

        static void Paint(Presence p, Ellipse dot, TextBlock text)
        {
            if (dot != null)
            {
                dot.Visibility = p == null ? Visibility.Collapsed : Visibility.Visible;
                if (p != null) dot.Fill = p.Online ? Green : Gray;
                dot.ToolTip = p == null ? null : Describe(p);
            }
            if (text != null)
            {
                text.Visibility = p == null ? Visibility.Collapsed : Visibility.Visible;
                if (p == null) return;
                text.Text = Describe(p);
                if (p.Online) text.Foreground = Green;
                else text.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
            }
        }

        /// <summary>«в сети», «был(а) в сети 5 мин назад», «вчера», «давно».</summary>
        public static string Describe(Presence p)
        {
            if (p.Online) return Loc.T("online.now");
            if (p.LastSeen == default) return Loc.T("online.long_ago");
            var ago = DateTime.UtcNow - p.LastSeen;
            if (ago.TotalMinutes < 1) return Loc.T("online.just_now");
            if (ago.TotalMinutes < 60) return string.Format(Loc.T("online.minutes"), (int)ago.TotalMinutes);
            if (ago.TotalHours < 24) return string.Format(Loc.T("online.hours"), (int)ago.TotalHours);
            if (ago.TotalDays < 30) return string.Format(Loc.T("online.days"), (int)ago.TotalDays);
            return Loc.T("online.long_ago");
        }

        static bool LightTheme() =>
            Application.Current?.TryFindResource("Text") is SolidColorBrush tb && (0.299 * tb.Color.R + 0.587 * tb.Color.G + 0.114 * tb.Color.B) / 255 < 0.5;
    }
}
