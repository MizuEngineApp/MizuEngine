// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace MizuEngine
{
    /// <summary>
    /// Находит все открытые окна (сверху вниз по z-порядку) и их рамки.
    /// Snow = true — на это окно может падать снег. Остальные окна только перекрывают снег под собой.
    /// </summary>
    internal sealed class WindowTracker
    {
        public struct Win
        {
            public IntPtr Hwnd;
            public Native.RECT Rect;
            public bool Snow;
        }

        readonly List<Win> _list = new List<Win>();
        readonly StringBuilder _sb = new StringBuilder(256);
        readonly Native.EnumWindowsProc _callback;
        IntPtr _own;
        int _screenW, _screenH;

        public WindowTracker() { _callback = OnWindow; }

        public List<Win> GetWindows(IntPtr own, int screenW, int screenH)
        {
            _own = own; _screenW = screenW; _screenH = screenH;
            _list.Clear();
            Native.EnumWindows(_callback, IntPtr.Zero);
            return new List<Win>(_list);
        }

        bool OnWindow(IntPtr h, IntPtr _)
        {
            if (h == _own || !Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
            if (IsCloaked(h)) return true;

            long ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
            if ((ex & Native.WS_EX_TOOLWINDOW) != 0) return true;
            if ((ex & Native.WS_EX_TRANSPARENT) != 0) return true;   // чужие оверлеи, сквозь которые кликают
            if (Native.GetWindowTextLength(h) == 0) return true;

            string cls = ClassOf(h);
            if (IsShellClass(cls)) return true;

            if (!GetBounds(h, out var r)) return true;
            if (r.Width < 100 || r.Height < 50) return true;
            if (r.Right <= 0 || r.Left >= _screenW || r.Bottom <= 0 || r.Top >= _screenH) return true;

            // Развёрнутое окно (верх у края экрана): снег на нём не копим, но оно закрывает окна под собой.
            bool snow = r.Top > 4 && r.Top < _screenH - 20;
            _list.Add(new Win { Hwnd = h, Rect = r, Snow = snow });
            return true;
        }

        /// <summary>Игра или видео в полноэкранном режиме — тогда снег прячем.</summary>
        public bool ForegroundIsFullscreen(IntPtr own, int screenW, int screenH)
        {
            IntPtr h = Native.GetForegroundWindow();
            if (h == IntPtr.Zero || h == own) return false;
            if (IsShellClass(ClassOf(h))) return false;
            if (!Native.GetWindowRect(h, out var r)) return false;
            return r.Left <= 0 && r.Top <= 0 && r.Right >= screenW && r.Bottom >= screenH;
        }

        static bool IsShellClass(string cls) =>
            cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd";

        string ClassOf(IntPtr h)
        {
            _sb.Clear();
            Native.GetClassName(h, _sb, _sb.Capacity);
            return _sb.ToString();
        }

        static bool GetBounds(IntPtr h, out Native.RECT r)
        {
            // Настоящая видимая рамка окна (без невидимых теней по краям в Win10/11)
            if (Native.DwmGetWindowAttribute(h, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf<Native.RECT>()) == 0)
                return true;
            return Native.GetWindowRect(h, out r);
        }

        static bool IsCloaked(IntPtr h) =>
            Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out int v, sizeof(int)) == 0 && v != 0;
    }
}
