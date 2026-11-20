// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>
    /// Горячий угол: мышь в левый нижний угол главного монитора и задержалась там на ~0,3 с — открывается Launchpad.
    /// Не срабатывает, пока зажата кнопка мыши (что-то тащат) и когда на весь экран открыта игра или видео.
    /// </summary>
    internal static class HotCorner
    {
        static DispatcherTimer _t;
        static Settings _s;
        static int _inside;
        static bool _fired;

        public static void Start(Settings s)
        {
            _s = s;
            _t ??= new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(90) };
            _t.Tick -= Tick;
            _t.Tick += Tick;
            Apply();
        }

        public static void Apply()
        {
            if (_t == null) return;
            if (_s.LaunchpadOn && _s.LaunchpadCorner) _t.Start(); else _t.Stop();
        }

        static void Tick(object sender, EventArgs e)
        {
            if (!LauncherNative.GetCursorPos(out var p)) return;
            int w = GetSystemMetrics(0), h = GetSystemMetrics(1);   // главный монитор: (0,0)–(w,h)
            bool corner = p.X <= 1 && p.X >= -1 && p.Y >= h - 2 && p.Y <= h + 1;
            if (!corner) { _inside = 0; _fired = false; return; }
            if (_fired) return;
            if ((LauncherNative.GetAsyncKeyState(0x01) & 0x8000) != 0 || (LauncherNative.GetAsyncKeyState(0x02) & 0x8000) != 0) { _inside = 0; return; }
            if (++_inside < 3) return;
            _fired = true;
            if (Launchpad.IsOpen || FullscreenApp(w, h)) return;
            Launchpad.Toggle();
        }

        /// <summary>Окно на весь главный монитор (игра, видео) — угол не трогаем.</summary>
        static bool FullscreenApp(int w, int h)
        {
            IntPtr fg = LauncherNative.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == GetShellWindow() || fg == GetDesktopWindow()) return false;
            var cls = new System.Text.StringBuilder(64);
            Native.GetClassName(fg, cls, 64);
            string c = cls.ToString();
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd") return false;
            return Native.GetWindowRect(fg, out var r) && r.Left <= 0 && r.Top <= 0 && r.Right >= w && r.Bottom >= h;
        }

        [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
        [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
    }
}
