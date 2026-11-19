// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MizuEngine
{
    /// <summary>
    /// Прозрачные окна, которые рисует видеокарта (DWM), а не процессор.
    /// Обычные прозрачные окна WPF (AllowsTransparency) каждый кадр копируют весь экран через процессор —
    /// отсюда лаги и лишние мегабайты памяти.
    /// </summary>
    internal static class Glass
    {
        // Только для окон, которые сами принимают клики (док, стекло под ним, анимация окон).
        // Для окон «сквозь которые кликают» не подходит: WPF снимает с них стиль WS_EX_LAYERED.
        public static void Setup(Window w, bool clickThrough)
        {
            w.AllowsTransparency = false;
            w.WindowStyle = WindowStyle.None;
            w.ResizeMode = ResizeMode.NoResize;
            w.Background = Brushes.Transparent;
            w.SourceInitialized += (s, e) =>
            {
                IntPtr h = new WindowInteropHelper(w).Handle;
                HwndSource.FromHwnd(h).CompositionTarget.BackgroundColor = Colors.Transparent;

                var m = new Native.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                Native.DwmExtendFrameIntoClientArea(h, ref m);   // прозрачный фон рисуется прозрачным, а не чёрным

                int v = 1;
                Native.DwmSetWindowAttribute(h, 33, ref v, 4);            // Windows 11: без скругления углов
                v = unchecked((int)0xFFFFFFFE);
                Native.DwmSetWindowAttribute(h, 34, ref v, 4);            // Windows 11: без рамки

                long ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
                ex |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                if (clickThrough) ex |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT;
                Native.SetWindowLongPtr(h, Native.GWL_EXSTYLE, new IntPtr(ex));
                if (clickThrough) Native.SetLayeredWindowAttributes(h, 0, 255, 2);   // LWA_ALPHA: окно целиком непрозрачно для DWM, но клики проходят насквозь
            };
        }

        /// <summary>Сбрасывает неиспользуемую память процесса — то, что показывает «Диспетчер задач».</summary>
        public static void TrimMemory()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Native.SetProcessWorkingSetSize(Native.GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
        }
    }
}
