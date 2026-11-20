// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MizuEngine
{
    /// <summary>Общее для Launchpad и Spotlight: вывести окно на передний план, снимок экрана под размытие, монитор под курсором.</summary>
    internal static class LauncherNative
    {
        /// <summary>
        /// Окно получает фокус клавиатуры, даже если его открыли не клавишей (горячий угол, клик по доку):
        /// Windows разрешает это после «нажатия» Alt — так делают все лаунчеры.
        /// </summary>
        public static void ForceForeground(Window w)
        {
            try
            {
                var h = new WindowInteropHelper(w).Handle;
                if (h == IntPtr.Zero) return;
                if (GetForegroundWindow() == h) return;
                keybd_event(0x12, 0, 0, UIntPtr.Zero);   // Alt вниз
                keybd_event(0x12, 0, 2, UIntPtr.Zero);   // Alt вверх
                SetForegroundWindow(h);
                w.Activate();
            }
            catch { }
        }

        /// <summary>Монитор под курсором: границы в пикселях.</summary>
        public static Native.RECT MonitorUnderCursor()
        {
            GetCursorPos(out var pt);
            var mon = MonitorFromPoint(pt, 2);   // MONITOR_DEFAULTTONEAREST
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfo(mon, ref mi);
            return mi.rcMonitor;
        }

        /// <summary>
        /// Снимок части экрана, уменьшенный в 4 раза (для размытого фона — мелкие детали всё равно не видны,
        /// а памяти и работы для видеокарты в 16 раз меньше).
        /// </summary>
        public static BitmapSource Capture(Native.RECT r)
        {
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            if (w <= 0 || h <= 0) return null;
            int sw = Math.Max(1, w / 4), sh = Math.Max(1, h / 4);
            IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), bmp = CreateCompatibleBitmap(screen, sw, sh);
            IntPtr old = SelectObject(mem, bmp);
            try
            {
                SetStretchBltMode(mem, 4);   // HALFTONE — без «лесенки»
                StretchBlt(mem, 0, 0, sw, sh, screen, r.Left, r.Top, w, h, 0x00CC0020 | 0x40000000);   // SRCCOPY | CAPTUREBLT
                SelectObject(mem, old);
                var raw = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                // GDI оставляет прозрачность пикселей нулевой — без перевода в Bgr32 картинка получается невидимой
                var src = new FormatConvertedBitmap(raw, PixelFormats.Bgr32, null, 0);
                var copy = new WriteableBitmap(src);   // своя копия пикселей: HBITMAP ниже удаляется
                copy.Freeze();
                return copy;
            }
            catch { return null; }
            finally
            {
                DeleteObject(bmp);
                DeleteDC(mem);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }

        /// <summary>Перевод пикселей экрана в единицы WPF для окна (с учётом масштаба монитора).</summary>
        public static Rect ToDip(Window w, Native.RECT r)
        {
            var src = PresentationSource.FromVisual(w);
            Matrix m = src?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var a = m.Transform(new Point(r.Left, r.Top));
            var b = m.Transform(new Point(r.Right, r.Bottom));
            return new Rect(a, b);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO { public int cbSize; public Native.RECT rcMonitor, rcWork; public uint dwFlags; }

        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT p, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, uint rop);
    }
}
