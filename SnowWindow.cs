// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MizuEngine
{
    /// <summary>
    /// Прозрачное окно поверх всего экрана для погоды и искр. Сквозь него можно кликать, его нет в Alt+Tab и на панели задач.
    /// </summary>
    internal sealed class SnowWindow : Window
    {
        public SnowLayer Layer { get; } = new SnowLayer();

        public SnowWindow()
        {
            // Окно на весь экран обязано пропускать клики насквозь. Надёжно это работает только у «слоистого» окна WPF
            // (AllowsTransparency): у окна, которое рисует видеокарта, WPF сам снимает нужный стиль — и экран перестаёт кликаться.
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            Title = "MizuEngine Effects";
            Content = Layer;

            SourceInitialized += (s, e) =>
            {
                IntPtr h = new WindowInteropHelper(this).Handle;
                long ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
                ex |= Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                Native.SetWindowLongPtr(h, Native.GWL_EXSTYLE, new IntPtr(ex));
                HwndSource.FromHwnd(h).AddHook(Hook);
                Layer.OwnHwnd = h;
                FitToScreen();

                // Каждую секунду проверяем, что окно по-прежнему пропускает клики (на случай, если стиль кто-то снял)
                var guard = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                guard.Tick += (o, a) =>
                {
                    long cur = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
                    if ((cur & ClickThrough) != ClickThrough)
                        Native.SetWindowLongPtr(h, Native.GWL_EXSTYLE, new IntPtr(cur | ClickThrough));
                };
                guard.Start();
            };

            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s, e) => Dispatcher.BeginInvoke(new Action(FitToScreen));
            DpiChanged += (s, e) => FitToScreen();
            // Док резервирует место внизу экрана — сугроб должен лечь на его верхний край
            SystemParameters.StaticPropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SystemParameters.WorkArea)) Dispatcher.BeginInvoke(new Action(FitToScreen));
            };
        }

        const long ClickThrough = Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED;

        /// <summary>Три уровня защиты от «невидимой стены»: окно всегда прозрачно для мыши.</summary>
        static IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == 0x0084)   // WM_NCHITTEST: «здесь никого нет»
            {
                handled = true;
                return new IntPtr(-1);   // HTTRANSPARENT
            }
            if (msg == 0x007C && (int)w.ToInt64() == Native.GWL_EXSTYLE)   // WM_STYLECHANGING: не даём снять «прозрачность для кликов»
            {
                int newStyle = Marshal.ReadInt32(l, 4);   // STYLESTRUCT.styleNew
                Marshal.WriteInt32(l, 4, newStyle | (int)ClickThrough);
            }
            return IntPtr.Zero;
        }

        void FitToScreen()
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            int w = Native.GetSystemMetrics(0);   // SM_CXSCREEN
            int h = Native.GetSystemMetrics(1);   // SM_CYSCREEN
            Left = 0;
            Top = 0;
            Width = w / dpi.DpiScaleX;
            // На 1 пиксель ниже экрана: иначе Wallpaper Engine считает окно полноэкранной игрой и останавливает обои (чёрный экран)
            Height = (h - 1) / dpi.DpiScaleY;
            Layer.SetScreen(w, h - 1, dpi.DpiScaleX);
        }
    }
}
