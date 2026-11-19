// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MizuEngine
{
    internal enum DockKind { App, Start, Separator, Tray, System, Clock }

    internal sealed class DockItem
    {
        public DockKind Kind;
        public string Key, Name, Launch, IconPath;
        public ImageSource Icon;
        public List<IntPtr> Windows = new List<IntPtr>();
        public bool Pinned;
        public DockPin Pin;                 // закреплённый значок (null — просто открытое приложение)
        public DockApps.Win Sample;         // одно из окон приложения — из него берём путь и AUMID для закрепления
        public bool Attention;              // приложение просит внимания (мигает на панели задач) — значок прыгает, точка оранжевая
        public bool Away;                   // закреплённое приложение открыто только на другом мониторе: точки нет, клик переключает на окно

        // Анимация: Presence — насколько элемент «появился» (0..1), Size — текущий размер иконки
        public double Presence, PresenceTarget = 1, Size, X, W;
        public double Shift;                // плавный сдвиг после перестановки значков (уходит в 0)
        public double Bounce = -1;          // время прыжка при запуске, -1 — не прыгает
    }

    /// <summary>
    /// Рисует док и анимирует его: увеличение под курсором как в macOS, плавное появление и исчезновение значков,
    /// прыжок при запуске, подписи, перетаскивание значков. Кадры считаются, только пока что-то движется.
    /// </summary>
    internal sealed class DockView : FrameworkElement
    {
        public const double Pad = 9, Gap = 7, BottomMargin = 8, LabelSpace = 48, BounceSpace = 30;

        public double BaseSize = 46, Magnify = 1.6;
        public bool Autohide;
        public int TrayCount;               // сколько значков в трее (рисуется на кнопке трея)
        public List<DockItem> Items = new List<DockItem>();
        public event Action<DockItem> ItemClicked;
        public event Action<DockItem, Point> ItemRightClicked;   // точка — в пикселях экрана
        public event Action<DockItem, bool> ItemDropped;         // перетащили (true — утащили вверх из дока: открепить)
        public event Action ShapeChanged;   // док сдвинулся/изменился — обновить размытие и область кликов

        public Rect Pill { get; private set; }
        public double PillRadius => (BaseSize + 2 * Pad) * 0.3;
        /// <summary>Где док принимает клики: только сам док, а при наведении — ещё и место над ним для увеличенных значков.</summary>
        public Rect HitArea { get; private set; }
        /// <summary>Значки, которые при увеличении или прыжке торчат над доком (только они ловят клики, а не вся полоса над доком).</summary>
        public Rect Band { get; private set; } = Rect.Empty;
        /// <summary>Подпись над значком.</summary>
        public Rect LabelBox { get; private set; } = Rect.Empty;
        FormattedText _labelText;
        string _labelKey;
        public bool HitIsPill { get; private set; }
        public bool Dragging => _drag != null;

        double _hover, _mx, _label, _hide, _away;
        Rect _lastHit;
        DockItem _hot, _pressed, _labelItem, _drag;
        Point _downAt, _dragAt;
        TimeSpan _last;
        Rect _lastPill;

        static readonly Brush Catcher = Frozen(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)));   // почти прозрачное — чтобы ловить мышь
        static readonly Brush Shadow = Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)));
        Brush Fill, Shine;
        static readonly Pen Rim = Frozen(new Pen(Grad(Color.FromArgb(0xC0, 255, 255, 255), Color.FromArgb(0x28, 255, 255, 255)), 1));
        static readonly Brush TileFill = Frozen(Grad(Color.FromArgb(0x48, 255, 255, 255), Color.FromArgb(0x1C, 255, 255, 255)));
        static readonly Brush DotFill = Frozen(new SolidColorBrush(Color.FromArgb(0xF0, 0x9A, 0x9E, 0xA8)));      // серая точка, как в macOS
        static readonly Pen DotRim = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0x10, 0x14, 0x1C)), 0.8));
        static readonly Brush AttentionDot = Frozen(new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x9F, 0x0A)));
        static readonly Pen Sep = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x50, 255, 255, 255)), 1));
        static readonly Brush Hot = Frozen(new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255)));
        static readonly Brush SysBack = Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0x08, 0x0C, 0x16)));      // тёмная подложка под часами — их видно на любых обоях
        static readonly Brush LabelBg = Frozen(new SolidColorBrush(Color.FromArgb(0xEC, 0x16, 0x1B, 0x27)));
        static readonly Pen LabelRim = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x55, 255, 255, 255)), 1));
        static readonly Brush LabelShadow = Frozen(new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)));
        static readonly Brush TextShadow = Frozen(new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0)));
        const double LabelReach = 46;   // сколько места над значком занимает подпись
        static readonly Brush StartBg = Frozen(Grad(Color.FromArgb(0xFF, 0x5A, 0xB0, 0xFF), Color.FromArgb(0xFF, 0x1D, 0x4F, 0xE0)));
        static readonly Brush BadgeBg = Frozen(new SolidColorBrush(Color.FromArgb(0xFF, 0x4A, 0x97, 0xFF)));
        static readonly Brush White = Brushes.White;
        static readonly Brush Faded = Frozen(new SolidColorBrush(Color.FromArgb(0xE0, 255, 255, 255)));

        static readonly Typeface Text = new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        static readonly Typeface Glyphs = new Typeface(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        /// <summary>Прозрачность стекла: 0 — плотное, 100 — почти невидимое.</summary>
        public void SetTransparency(int t)
        {
            double g = 1 - Math.Clamp(t, 0, 100) / 100.0;
            Fill = Frozen(Grad(Color.FromArgb((byte)(0x10 + 0x58 * g), 255, 255, 255), Color.FromArgb((byte)(0x06 + 0x2A * g), 255, 255, 255)));
            Shine = Frozen(Grad(Color.FromArgb((byte)(0x10 + 0x28 * g), 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 0.45));
            InvalidateVisual();
        }

        // Кадры считаем, только пока что-то движется или мышь у дока. В покое док не нагружает процессор:
        // раз в 40 мс проверяем, не подвели ли мышь.
        bool _hooked;
        int _idle;
        readonly DispatcherTimer _wakeup = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };

        public DockView()
        {
            SetTransparency(40);
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            _wakeup.Tick += (s, e) => CheckWake();
            Wake();

            MouseLeftButtonDown += (s, e) =>
            {
                _pressed = _hot;
                _downAt = e.GetPosition(this);
                if (_pressed != null) CaptureMouse();
                Wake();
                InvalidateVisual();
            };
            MouseMove += (s, e) =>
            {
                if (_pressed == null || _drag != null || e.LeftButton != MouseButtonState.Pressed) return;
                var p = e.GetPosition(this);
                // Значки приложений можно перетаскивать: менять порядок, закреплять, утаскивать вверх (открепить)
                if (_pressed.Kind == DockKind.App && (Math.Abs(p.X - _downAt.X) > 6 || Math.Abs(p.Y - _downAt.Y) > 6))
                {
                    _drag = _pressed;
                    _dragAt = p;
                    Wake();
                }
            };
            MouseLeftButtonUp += (s, e) =>
            {
                var p = _pressed;
                var drag = _drag;
                _pressed = null;
                _drag = null;
                if (IsMouseCaptured) ReleaseMouseCapture();
                if (drag != null)
                {
                    bool removed = e.GetPosition(this).Y < Pill.Top - 70;
                    ItemDropped?.Invoke(drag, removed);
                }
                else if (p != null && p == _hot) ItemClicked?.Invoke(p);
                Wake();
                InvalidateVisual();
            };
            MouseRightButtonUp += (s, e) =>
            {
                if (_hot == null || _drag != null) return;
                Point screen;
                try { screen = PointToScreen(e.GetPosition(this)); } catch (InvalidOperationException) { return; }
                ItemRightClicked?.Invoke(_hot, screen);
                e.Handled = true;
            };
            LostMouseCapture += (s, e) =>
            {
                if (_drag != null) { var d = _drag; _drag = null; _pressed = null; ItemDropped?.Invoke(d, false); }
            };
        }

        /// <summary>Запустить покадровую анимацию (появились значки, прыжок, мышь у дока).</summary>
        public void Wake()
        {
            _idle = 0;
            if (_hooked) return;
            _hooked = true;
            _last = TimeSpan.Zero;
            _wakeup.Stop();
            CompositionTarget.Rendering += OnFrame;
        }

        void Sleep()
        {
            if (!_hooked) return;
            _hooked = false;
            CompositionTarget.Rendering -= OnFrame;
            _wakeup.Start();
        }

        void CheckWake()
        {
            if (!IsVisible || !Native.GetCursorPos(out var p)) return;
            Point c;
            try { c = PointFromScreen(new Point(p.X, p.Y)); } catch (InvalidOperationException) { return; }
            bool near = HoverZone().Contains(c) || (Autohide && c.Y >= ActualHeight - 3 && c.Y <= ActualHeight + 2 && c.X >= 0 && c.X <= ActualWidth);
            if (near) Wake();
        }

        public static bool IsIcon(DockItem it) => it.Kind == DockKind.App || it.Kind == DockKind.Start;

        double BaseWidth(DockItem it) => it.Kind switch
        {
            DockKind.Separator => 14,
            DockKind.Clock => BaseSize * 1.35 + 10,
            DockKind.System => BaseSize * 1.1 + (SystemInfoBattery.Present ? 22 : 8),
            DockKind.Tray => BaseSize * 0.62,
            _ => BaseSize
        };

        double BounceY(DockItem it)
        {
            if (it.Bounce < 0) return 0;
            double t = it.Bounce;
            return Math.Abs(Math.Sin(t * Math.PI * 2.2)) * BaseSize * 0.34 * Math.Max(0, 1 - t / 1.8);
        }

        /// <summary>Значок, который просит внимания, подпрыгивает раз в пару секунд, пока на него не посмотрят.</summary>
        public void Nudge(DockItem it)
        {
            if (it.Bounce < 0) it.Bounce = 0;
            Wake();
        }

        // ================== кадр ==================

        void OnFrame(object sender, EventArgs e)
        {
            if (!IsVisible || ActualWidth <= 0) return;
            var now = ((RenderingEventArgs)e).RenderingTime;
            if (now == _last) return;
            double dt = _last == TimeSpan.Zero ? 1.0 / 60 : Math.Min(0.05, (now - _last).TotalSeconds);
            _last = now;
            bool changed = false;

            // Где курсор (опрашиваем сами — так увеличение работает и над прозрачными промежутками)
            Point c = new Point(-1e5, -1e5);
            if (Native.GetCursorPos(out var p))
                try { c = PointFromScreen(new Point(p.X, p.Y)); } catch (InvalidOperationException) { }

            // Наведение начинается только на самом доке (как в macOS). Дальше держится, пока курсор над увеличенными значками.
            // Раньше «зоной дока» считалась вся полоса над ним — и клики по кнопкам окон над доком попадали в док.
            Rect core = new Rect(Pill.Left - 6, Pill.Top - 4, Math.Max(0, Pill.Width + 12), Math.Max(0, ActualHeight - Pill.Top + 4));
            bool inside = Pill.Width > 0 && _hide < 0.5 && (core.Contains(c) || (_hover > 0.3 && Band.Contains(c)) || _drag != null);

            // Автоскрытие: док уезжает вниз, когда мышь ушла, и выезжает, когда её подвели к нижнему краю экрана
            if (Autohide)
            {
                bool atEdge = c.Y >= ActualHeight - 3 && c.Y <= ActualHeight + 2 && c.X >= 0 && c.X <= ActualWidth;
                if (inside || atEdge || _pressed != null) _away = 0; else _away += dt;
                changed |= Approach(ref _hide, inside || atEdge || _away < 0.6 ? 0 : 1, dt, 9);
            }
            else changed |= Approach(ref _hide, 0, dt, 9);

            changed |= Approach(ref _hover, inside ? 1 : 0, dt, 9);
            if (inside)
            {
                if (_hover < 0.05) _mx = c.X;
                else changed |= Approach(ref _mx, c.X, dt, 28, 0.05);
            }

            // Перетаскивание: значок едет за курсором, соседи расступаются
            if (_drag != null)
            {
                if (c != _dragAt) { _dragAt = c; changed = true; }
                changed |= Reorder(c.X);
            }

            for (int i = Items.Count - 1; i >= 0; i--)
            {
                var it = Items[i];
                changed |= Approach(ref it.Presence, it.PresenceTarget, dt, 9);
                changed |= Approach(ref it.Shift, 0, dt, 14, 0.2);
                if (it.PresenceTarget == 0 && it.Presence < 0.004) { Items.RemoveAt(i); changed = true; continue; }
                if (it.Bounce >= 0)
                {
                    it.Bounce += dt;
                    // Прыгаем, пока приложение не открылось (но не дольше 1.8 с), и приземляемся мягко
                    if (it.Bounce > 1.8 || (!it.Attention && it.Windows.Count > 0 && it.Bounce > 0.45 && BounceY(it) < 1)) it.Bounce = -1;
                    changed = true;
                }
            }

            Layout();

            DockItem hot = null;
            if (inside && _drag == null)
                foreach (var it in Items)
                    if (it.Kind != DockKind.Separator && it.PresenceTarget > 0 && c.X >= it.X - Gap / 2 && c.X < it.X + it.W + Gap / 2)
                        hot = it;
            if (hot != _hot) { _hot = hot; changed = true; }
            if (hot != null) _labelItem = hot;
            changed |= Approach(ref _label, hot != null && _hover > 0.3 ? 1 : 0, dt, 14);

            // Ничего не меняется и мыши рядом нет — засыпаем до следующего движения
            if (changed || inside || _pressed != null) _idle = 0;
            else if (++_idle > 30) Sleep();

            if (changed)
            {
                InvalidateVisual();
                // Форма окна = сам док + торчащие над ним значки + подпись. Всё остальное над доком пропускает клики
                // к окнам под ним. Форму меняем шагами по 8 px: менять её каждый кадр дорого (раньше отсюда были лаги).
                var band = Rect.Empty;
                double iconBottom = Pill.Bottom - Pad;
                foreach (var it in Items)
                {
                    if (!IsIcon(it) || it.Presence < 0.05) continue;
                    double top = iconBottom - it.Size * it.Presence - BounceY(it);
                    if (top >= Pill.Top - 1) continue;
                    var r = new Rect(it.X + it.Shift - Gap / 2, top - 3, it.W + Gap, Pill.Top - top + 3);
                    band = band.IsEmpty ? r : Rect.Union(band, r);
                }
                var label = Rect.Empty;
                if (_label > 0.01 && _drag == null && _labelItem != null && Items.Contains(_labelItem) && !string.IsNullOrEmpty(_labelItem.Name))
                    label = LabelRect(_labelItem, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                Band = band.IsEmpty || _hide > 0.98 ? Rect.Empty : Snap(band, 8);
                LabelBox = label.IsEmpty || _hide > 0.98 ? Rect.Empty : Snap(new Rect(label.X - 2, label.Y - 2, label.Width + 4, label.Height + 6), 8);

                HitIsPill = _hover <= 0.01 && _drag == null && Band.IsEmpty && LabelBox.IsEmpty;
                Rect area;
                if (_drag != null)
                {
                    area = HoverZone();   // тащим значок — док ловит мышь на всей своей высоте
                    area = Snap(new Rect(area.X, Math.Max(0, area.Y - LabelReach), area.Width, area.Height + Math.Min(area.Y, LabelReach)), 8);
                }
                else area = Snap(core, 8);
                HitArea = _hide > 0.98 ? Rect.Empty : HitIsPill ? Pill : area;
                if (Pill != _lastPill || HitArea != _lastHit || Band != _lastBand || LabelBox != _lastLabel)
                {
                    _lastPill = Pill;
                    _lastHit = HitArea;
                    _lastBand = Band;
                    _lastLabel = LabelBox;
                    ShapeChanged?.Invoke();
                }
            }
        }

        Rect _lastBand = Rect.Empty, _lastLabel = Rect.Empty;

        /// <summary>Где подпись над значком. Подпись не выходит за края экрана — длинные названия сдвигаются внутрь.</summary>
        Rect LabelRect(DockItem it, double dpi)
        {
            string key = it.Name + "|" + dpi.ToString(CultureInfo.InvariantCulture);
            if (key != _labelKey || _labelText == null) { _labelKey = key; _labelText = Fmt(it.Name, Text, 13.5, White, dpi); }
            double iconBottom = Pill.Bottom - Pad;
            double top = IsIcon(it) ? iconBottom - it.Size - BounceY(it) : Pill.Top;
            double w = _labelText.Width + 24, h = _labelText.Height + 11;
            double lx = Math.Max(6, Math.Min(ActualWidth - w - 6, it.X + it.Shift + it.W / 2 - w / 2));
            double ly = Math.Max(2, top - h - 9 + (1 - _label) * 6);   // подпись чуть «всплывает» при появлении
            return new Rect(lx, ly, w, h);
        }

        static Rect Snap(Rect r, double step)
        {
            double l = Math.Floor(r.Left / step) * step, t = Math.Floor(r.Top / step) * step;
            double rr = Math.Ceiling(r.Right / step) * step, b = Math.Ceiling(r.Bottom / step) * step;
            return new Rect(l, t, Math.Max(0, rr - l), Math.Max(0, b - t));
        }

        /// <summary>Переставляет перетаскиваемый значок туда, где сейчас курсор (только среди значков приложений).</summary>
        bool Reorder(double x)
        {
            int from = Items.IndexOf(_drag);
            if (from < 0) return false;
            int lo = 1, hi = Items.Count - 1;   // после «Пуска» и до системной части
            int sys = Items.FindIndex(i => i.Key == "sep:sys");
            if (sys >= 0) hi = sys - 1;
            if (Items.Count > 0 && Items[0].Kind != DockKind.Start) lo = 0;

            int to = from;
            for (int i = lo; i <= hi; i++)
            {
                var it = Items[i];
                if (it == _drag || it.PresenceTarget == 0) continue;
                double mid = it.X + it.W / 2;
                if (i < from && x < mid) { to = i; break; }
                if (i > from && x > mid) to = i;
            }
            if (to == from) return false;

            var oldX = new Dictionary<DockItem, double>();
            foreach (var it in Items) oldX[it] = it.X + it.Shift;
            Items.RemoveAt(from);
            Items.Insert(to, _drag);
            Layout();
            foreach (var it in Items)
                if (it != _drag && oldX.TryGetValue(it, out double ox)) it.Shift = ox - it.X;   // соседи плавно доезжают на новое место
            return true;
        }

        /// <summary>Плавно ведёт значение к цели независимо от частоты кадров. Возвращает true, если значение изменилось.</summary>
        static bool Approach(ref double v, double target, double dt, double speed, double eps = 0.002)
        {
            if (v == target) return false;
            double next = v + (target - v) * (1 - Math.Exp(-dt * speed));
            if (Math.Abs(target - next) < eps) next = target;
            v = next;
            return true;
        }

        Rect HoverZone()
        {
            double top = Pill.Bottom - Pad - BaseSize * Magnify - 14;
            return new Rect(Pill.Left - 6, top, Pill.Width + 12, Math.Max(0, ActualHeight - top));
        }

        void Layout()
        {
            int n = Items.Count;
            double bottom = ActualHeight - BottomMargin + _hide * (BaseSize + 2 * Pad + BottomMargin + 12);
            double r = BaseSize * 2.4;   // радиус влияния курсора

            // 1) где стояли бы значки без увеличения — от этого считаем, насколько каждый ближе к курсору
            var centers = new double[n];
            double total = 0;
            for (int i = 0; i < n; i++) total += BaseWidth(Items[i]) * Items[i].Presence + (i > 0 ? Gap * Items[i].Presence : 0);
            double x = ActualWidth / 2 - total / 2;
            for (int i = 0; i < n; i++)
            {
                var it = Items[i];
                if (i > 0) x += Gap * it.Presence;
                double w = BaseWidth(it) * it.Presence;
                centers[i] = x + w / 2;
                x += w;
            }

            // 2) размеры с увеличением (плавный «колокол» вокруг курсора)
            double total2 = 0;
            for (int i = 0; i < n; i++)
            {
                var it = Items[i];
                double s = BaseSize;
                if (IsIcon(it) && _hover > 0)
                {
                    double d = Math.Abs(_mx - centers[i]);
                    double f = d < r ? Math.Pow(Math.Cos(d / r * Math.PI / 2), 2) : 0;
                    s = BaseSize * (1 + (Magnify - 1) * _hover * f);
                }
                it.Size = s;
                it.W = (IsIcon(it) ? s : BaseWidth(it)) * it.Presence;
                total2 += it.W + (i > 0 ? Gap * it.Presence : 0);
            }

            x = ActualWidth / 2 - total2 / 2;
            for (int i = 0; i < n; i++)
            {
                if (i > 0) x += Gap * Items[i].Presence;
                Items[i].X = x;
                x += Items[i].W;
            }

            double h = BaseSize + 2 * Pad;
            Pill = total2 > 0.5
                ? new Rect(ActualWidth / 2 - total2 / 2 - Pad, bottom - h, total2 + 2 * Pad, h)
                : new Rect(ActualWidth / 2, bottom, 0, 0);
        }

        // ================== отрисовка ==================

        // Текст часов создаётся раз в секунду, а не в каждом кадре
        FormattedText _time, _date, _trayGlyph, _wifi, _vol;
        string _timeKey;
        double _textDpi;

        void PrepareTexts(double dpi)
        {
            var now = DateTime.Now;
            string key = now.ToString("HH:mm d MMM", CultureInfo.CurrentUICulture);
            if (key == _timeKey && dpi == _textDpi && _time != null) return;
            _timeKey = key;
            _textDpi = dpi;
            _time = Fmt(now.ToString("HH:mm"), Text, 17, White, dpi);
            _date = Fmt(now.ToString("d MMM", CultureInfo.CurrentUICulture), Text, 11.5, Faded, dpi);
            _trayGlyph = Fmt("", Glyphs, 15, White, dpi);
            _wifi = Fmt("", Glyphs, 17, White, dpi);
            _vol = Fmt("", Glyphs, 17, White, dpi);
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (Pill.Width <= 0) return;
            if (_hover > 0.01 || _drag != null)
            {
                if (!HitArea.IsEmpty) dc.DrawRectangle(Catcher, null, HitArea);
                if (!Band.IsEmpty) dc.DrawRectangle(Catcher, null, Band);
            }

            double rad = PillRadius;
            dc.DrawRoundedRectangle(Shadow, null, new Rect(Pill.X - 1, Pill.Y + 3, Pill.Width + 2, Pill.Height), rad + 1, rad + 1);
            dc.DrawRoundedRectangle(Fill, Rim, Pill, rad, rad);
            dc.DrawRoundedRectangle(Shine, null, new Rect(Pill.X + 1, Pill.Y + 1, Pill.Width - 2, Pill.Height * 0.55), rad - 1, rad - 1);

            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            PrepareTexts(dpi);
            double iconBottom = Pill.Bottom - Pad;

            // Тёмная подложка под системной частью (трей, сеть, часы) — белый текст читается на любых обоях
            DockItem firstSys = null, lastSys = null;
            foreach (var it in Items)
                if (it.Kind == DockKind.Tray || it.Kind == DockKind.System || it.Kind == DockKind.Clock)
                {
                    if (firstSys == null) firstSys = it;
                    lastSys = it;
                }
            if (firstSys != null)
            {
                var back = new Rect(firstSys.X + firstSys.Shift - 3, Pill.Top + 5, lastSys.X + lastSys.W - firstSys.X + 6, Pill.Height - 10);
                if (back.Width > 0) dc.DrawRoundedRectangle(SysBack, null, back, rad * 0.7, rad * 0.7);
            }

            foreach (var it in Items)
            {
                if (it.Presence < 0.01 || it == _drag) continue;
                DrawItem(dc, it, it.X + it.Shift + it.W / 2, iconBottom, dpi);
            }

            // Перетаскиваемый значок — поверх остальных, под курсором
            if (_drag != null && Items.Contains(_drag))
            {
                double s = BaseSize * Math.Max(1, Magnify * 0.92);
                double cy = Math.Min(iconBottom - s / 2, _dragAt.Y);
                var rect = new Rect(_dragAt.X - s / 2, cy - s / 2, s, s);
                bool leaving = _dragAt.Y < Pill.Top - 70 && _drag.Pinned;
                dc.PushOpacity(leaving ? 0.55 : 0.95);
                if (_drag.Icon != null) dc.DrawImage(_drag.Icon, rect); else DrawGeneric(dc, rect, _drag.Name, dpi);
                dc.Pop();
            }

            // Подпись над значком под курсором
            if (_label > 0.01 && _labelItem != null && Items.Contains(_labelItem) && !string.IsNullOrEmpty(_labelItem.Name) && _drag == null)
            {
                var it = _labelItem;
                var box = LabelRect(it, dpi);
                var text = _labelText;
                double lx = box.X, ly = box.Y, w = box.Width, h = box.Height;
                dc.PushOpacity(_label);
                dc.DrawRoundedRectangle(LabelShadow, null, new Rect(lx, ly + 2, w, h), 10, 10);
                dc.DrawRoundedRectangle(LabelBg, LabelRim, new Rect(lx, ly, w, h), 10, 10);
                dc.DrawText(text, new Point(lx + 12, ly + 5.5));
                dc.Pop();
            }
        }

        void DrawItem(DrawingContext dc, DockItem it, double cx, double iconBottom, double dpi)
        {
            dc.PushOpacity(Math.Min(1, it.Presence * 1.3));
            switch (it.Kind)
            {
                case DockKind.App:
                case DockKind.Start:
                {
                    double s = it.Size * it.Presence;
                    var rect = new Rect(cx - s / 2, iconBottom - s - BounceY(it), s, s);
                    if (it == _pressed) dc.PushOpacity(0.6);
                    if (it.Kind == DockKind.Start) DrawStart(dc, rect);
                    else if (it.Icon != null) dc.DrawImage(it.Icon, rect);
                    else DrawGeneric(dc, rect, it.Name, dpi);
                    if (it == _pressed) dc.Pop();
                    // Точка под открытым приложением (оранжевая — приложение просит внимания)
                    if (it.Windows.Count > 0 && !it.Away)
                        dc.DrawEllipse(it.Attention ? AttentionDot : DotFill, DotRim, new Point(cx, Pill.Bottom - 4.2), 2.4, 2.4);
                    break;
                }
                case DockKind.Separator:
                    dc.DrawLine(Sep, new Point(cx, Pill.Top + Pad + BaseSize * 0.15), new Point(cx, Pill.Bottom - Pad - BaseSize * 0.15));
                    break;

                case DockKind.Clock:
                {
                    if (it == _hot) dc.DrawRoundedRectangle(Hot, null, ItemBox(it), 12, 12);
                    double top = Pill.Top + (Pill.Height - _time.Height - _date.Height) / 2;
                    ShadowText(dc, _time, new Point(cx - _time.Width / 2, top), White);
                    ShadowText(dc, _date, new Point(cx - _date.Width / 2, top + _time.Height - 1), Faded);
                    break;
                }
                case DockKind.Tray:
                {
                    if (it == _hot) dc.DrawRoundedRectangle(Hot, null, ItemBox(it), 10, 10);
                    ShadowText(dc, _trayGlyph, new Point(cx - _trayGlyph.Width / 2, Pill.Top + (Pill.Height - _trayGlyph.Height) / 2), White);
                    if (TrayCount > 0)
                    {
                        var n = Fmt(TrayCount > 99 ? "99" : TrayCount.ToString(CultureInfo.InvariantCulture), Text, 9.5, White, dpi);
                        double bw = Math.Max(15, n.Width + 7), bx = cx + 2, by = Pill.Top + 7;
                        dc.DrawRoundedRectangle(BadgeBg, null, new Rect(bx, by, bw, 15), 7.5, 7.5);
                        dc.DrawText(n, new Point(bx + (bw - n.Width) / 2, by + (15 - n.Height) / 2));
                    }
                    break;
                }
                case DockKind.System:
                {
                    if (it == _hot) dc.DrawRoundedRectangle(Hot, null, ItemBox(it), 12, 12);
                    double y = Pill.Top + (Pill.Height - _wifi.Height) / 2;
                    double gap = SystemInfoBattery.Present ? 12 : 5;
                    double left = cx - (SystemInfoBattery.Present ? 26 : 0);
                    ShadowText(dc, _wifi, new Point(left - _wifi.Width - gap / 2, y), White);
                    ShadowText(dc, _vol, new Point(left + gap / 2, y), White);
                    if (SystemInfoBattery.Present)
                    {
                        var pct = Fmt(SystemInfoBattery.Percent + "%", Text, 11.5, White, dpi);
                        ShadowText(dc, pct, new Point(left + gap / 2 + _vol.Width + 6, Pill.Top + (Pill.Height - pct.Height) / 2), White);
                    }
                    break;
                }
            }
            dc.Pop();
        }

        /// <summary>Белый текст с мягкой тенью — так он читается и на светлых обоях.</summary>
        static void ShadowText(DrawingContext dc, FormattedText t, Point p, Brush fg)
        {
            // Текст рисуется в момент вызова, поэтому можно на время сменить цвет и нарисовать тень тем же объектом
            t.SetForegroundBrush(TextShadow);
            dc.DrawText(t, new Point(p.X, p.Y + 1));
            t.SetForegroundBrush(fg);
            dc.DrawText(t, p);
        }

        /// <summary>Где сейчас нарисован значок (в координатах дока) — туда «улетает» сворачиваемое окно.</summary>
        public Rect IconRect(DockItem it)
        {
            double s = it.Size * it.Presence;
            return new Rect(it.X + it.W / 2 - s / 2, Pill.Bottom - Pad - s, s, s);
        }

        /// <summary>Прямоугольник элемента (для кнопки трея — над ним открывается список значков).</summary>
        public Rect ItemBox(DockItem it) => new Rect(it.X, Pill.Top + 5, it.W, Pill.Height - 10);

        static FormattedText Fmt(string s, Typeface tf, double size, Brush brush, double dpi) =>
            new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, tf, size, brush, dpi);

        static void DrawStart(DrawingContext dc, Rect r)
        {
            var tile = new Rect(r.X + r.Width * 0.06, r.Y + r.Height * 0.06, r.Width * 0.88, r.Height * 0.88);
            dc.DrawRoundedRectangle(StartBg, Rim, tile, tile.Width * 0.24, tile.Width * 0.24);
            double q = tile.Width * 0.2, g = tile.Width * 0.035;
            double x0 = tile.X + tile.Width / 2 - q - g / 2, y0 = tile.Y + tile.Height / 2 - q - g / 2;
            for (int i = 0; i < 4; i++)
                dc.DrawRoundedRectangle(White, null, new Rect(x0 + (i % 2) * (q + g), y0 + (i / 2) * (q + g), q, q), q * 0.15, q * 0.15);
        }

        static void DrawGeneric(DrawingContext dc, Rect r, string name, double dpi)
        {
            var tile = new Rect(r.X + r.Width * 0.06, r.Y + r.Height * 0.06, r.Width * 0.88, r.Height * 0.88);
            dc.DrawRoundedRectangle(TileFill, Rim, tile, tile.Width * 0.24, tile.Width * 0.24);
            string letter = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
            var t = Fmt(letter, Text, Math.Max(1, tile.Height * 0.45), White, dpi);
            dc.DrawText(t, new Point(tile.X + (tile.Width - t.Width) / 2, tile.Y + (tile.Height - t.Height) / 2));
        }

        static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

        static LinearGradientBrush Grad(Color a, Color b, double end = 1) =>
            new LinearGradientBrush(a, b, new Point(0, 0), new Point(0, end));
    }

    /// <summary>Батарея ноутбука: есть ли она и сколько процентов (обновляется раз в 30 секунд).</summary>
    internal static class SystemInfoBattery
    {
        static DateTime _at;
        static bool _present;
        static int _pct;

        public static bool Present { get { Update(); return _present; } }
        public static int Percent { get { Update(); return _pct; } }

        static void Update()
        {
            if ((DateTime.UtcNow - _at).TotalSeconds < 30) return;
            _at = DateTime.UtcNow;
            try
            {
                var ps = System.Windows.Forms.SystemInformation.PowerStatus;
                _present = ps.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery
                        && ps.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.Unknown;
                _pct = (int)Math.Round(Math.Clamp(ps.BatteryLifePercent, 0f, 1f) * 100);
            }
            catch { _present = false; }
        }
    }
}
