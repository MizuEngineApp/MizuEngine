// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>
    /// Значки трея над кнопкой «⌃» в доке — стеклянная панель в стиле macOS.
    /// Левый клик, правый клик (меню программы) и двойной клик передаются программе, как в трее Windows.
    /// </summary>
    internal sealed class TrayPopup : Window
    {
        IntPtr _h;
        readonly WrapPanel _grid = new WrapPanel();
        readonly TextBlock _tip = new TextBlock();
        readonly TextBlock _empty = new TextBlock();
        readonly Border _card;
        readonly ScaleTransform _scale = new ScaleTransform(0.94, 0.94);
        readonly DispatcherTimer _watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        Point _anchor;          // центр низа панели (над кнопкой трея), в DIP экрана
        Rect _keepZone;         // кнопка трея: нажатие по ней не закрывает панель (её закроет сам док)
        bool _wasDown;

        public bool IsOpen => IsVisible;

        public TrayPopup()
        {
            Glass.Setup(this, clickThrough: false);
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            Title = "MizuEngine Tray";

            _grid.MaxWidth = 6 * 44;
            _tip.Foreground = Brushes.White;
            _tip.Opacity = 0.8;
            _tip.FontSize = 12;
            _tip.Margin = new Thickness(6, 6, 6, 0);
            _tip.TextTrimming = TextTrimming.CharacterEllipsis;
            _tip.MaxWidth = 6 * 44 - 12;
            _empty.Foreground = Brushes.White;
            _empty.Opacity = 0.7;
            _empty.FontSize = 12.5;
            _empty.Margin = new Thickness(8, 6, 8, 6);

            var title = new TextBlock { Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 2, 6, 8) };
            title.Text = Loc.T("dock.tray");
            Loc.Changed += () => Dispatcher.BeginInvoke(new Action(() => { title.Text = Loc.T("dock.tray"); _empty.Text = Loc.T("dock.tray.empty"); }));
            _empty.Text = Loc.T("dock.tray.empty");

            var stack = new StackPanel();
            stack.Children.Add(title);
            stack.Children.Add(_grid);
            stack.Children.Add(_empty);
            stack.Children.Add(_tip);

            _card = new Border
            {
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(Color.FromArgb(0x55, 0x14, 0x18, 0x24)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 10, 10, 10),
                Child = stack,
                RenderTransform = _scale,
                RenderTransformOrigin = new Point(0.5, 1)
            };
            Content = _card;
            TextOptions.SetTextFormattingMode(_card, TextFormattingMode.Display);

            SourceInitialized += (s, e) =>
            {
                _h = new WindowInteropHelper(this).Handle;
                DockNative.EnableAcrylic(_h, unchecked((int)0x70201812));
            };
            SizeChanged += (s, e) => Place();
            _watch.Tick += (s, e) => Watch();
            TrayHost.Changed += () => Dispatcher.BeginInvoke(new Action(() => { if (IsVisible) Fill(); }));
        }

        /// <summary>Открыть над кнопкой трея. anchor — середина верхнего края кнопки, keep — сама кнопка (DIP экрана).</summary>
        public void Open(Point anchor, Rect keep)
        {
            _anchor = anchor;
            _keepZone = keep;
            Fill();
            if (!IsVisible) Show();
            Place();
            _wasDown = true;   // кнопка мыши ещё может быть нажата после клика по «⌃»
            _watch.Start();
            Opacity = 0;
            Anim.To(this, OpacityProperty, 1, 180);
            Anim.To(_scale, ScaleTransform.ScaleXProperty, 1, 320, from: 0.94);
            Anim.To(_scale, ScaleTransform.ScaleYProperty, 1, 320, from: 0.94);
        }

        public void Dismiss()
        {
            if (!IsVisible) return;
            _watch.Stop();
            Anim.To(this, OpacityProperty, 0, 140, done: () => { if (Opacity < 0.05) Hide(); });
        }

        void Place()
        {
            if (!IsVisible) return;
            double w = ActualWidth, h = ActualHeight;
            var area = SystemParameters.WorkArea;
            Left = Math.Max(area.Left + 6, Math.Min(SystemParameters.PrimaryScreenWidth - w - 6, _anchor.X - w / 2));
            Top = _anchor.Y - h - 10;
            if (_h != IntPtr.Zero)
                DockNative.SetWindowRgn(_h, DockWindow.RoundRegion(this, new Rect(0, 0, w, h), 16), true);
        }

        void Fill()
        {
            _grid.Children.Clear();
            var icons = TrayHost.Snapshot();
            _empty.Visibility = icons.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _tip.Text = "";
            foreach (var icon in icons) _grid.Children.Add(MakeButton(icon));
        }

        FrameworkElement MakeButton(TrayIcon icon)
        {
            var img = new Image { Source = icon.Icon, Width = 22, Height = 22, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var hover = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255));
            var cell = new Border
            {
                Width = 40, Height = 40, Margin = new Thickness(2), CornerRadius = new CornerRadius(10),
                Background = hover, Child = img, Cursor = Cursors.Hand
            };
            cell.MouseEnter += (s, e) =>
            {
                hover.Color = Color.FromArgb(0x30, 255, 255, 255);
                _tip.Text = FirstLine(icon.Tip);
                if (Native.GetCursorPos(out var p)) TrayHost.Hover(icon, p.X, p.Y);
            };
            cell.MouseLeave += (s, e) => { hover.Color = Color.FromArgb(0, 255, 255, 255); };
            bool afterDouble = false;
            cell.MouseLeftButtonUp += (s, e) =>
            {
                if (afterDouble) { afterDouble = false; e.Handled = true; return; }
                if (!Native.GetCursorPos(out var p)) return;
                TrayHost.Click(icon, right: false, dbl: false, p.X, p.Y);
                e.Handled = true;
            };
            cell.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2 && Native.GetCursorPos(out var p)) { afterDouble = true; TrayHost.Click(icon, right: false, dbl: true, p.X, p.Y); e.Handled = true; }
            };
            cell.MouseRightButtonUp += (s, e) =>
            {
                if (Native.GetCursorPos(out var p)) TrayHost.Click(icon, right: true, dbl: false, p.X, p.Y);
                e.Handled = true;
            };
            return cell;
        }

        static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int i = s.IndexOfAny(new[] { '\r', '\n' });
            return i < 0 ? s : s.Substring(0, i);
        }

        /// <summary>Окно не забирает фокус, поэтому «клик мимо» ловим сами: нажали кнопку мыши вне панели — закрываемся.</summary>
        void Watch()
        {
            bool down = (DockNative.GetAsyncKeyState(DockNative.VK_LBUTTON) & 0x8000) != 0 || (DockNative.GetAsyncKeyState(DockNative.VK_RBUTTON) & 0x8000) != 0;
            if (down && !_wasDown && Native.GetCursorPos(out var p))
            {
                Point c;
                try { c = PointFromScreen(new Point(p.X, p.Y)); } catch (InvalidOperationException) { _wasDown = down; return; }
                bool inside = c.X >= 0 && c.Y >= 0 && c.X <= ActualWidth && c.Y <= ActualHeight;
                double dpiX = VisualTreeHelper.GetDpi(this).DpiScaleX, dpiY = VisualTreeHelper.GetDpi(this).DpiScaleY;
                var dip = new Point(p.X / dpiX, p.Y / dpiY);
                if (!inside && !_keepZone.Contains(dip)) Dismiss();
            }
            _wasDown = down;
        }
    }
}
