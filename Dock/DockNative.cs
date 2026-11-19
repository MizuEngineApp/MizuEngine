// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MizuEngine
{
    /// <summary>Вызовы WinAPI для дока: панель задач, окна, процессы, иконки, размытие.</summary>
    internal static class DockNative
    {
        // ---------- окна ----------
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")] public static extern bool RedrawWindow(IntPtr h, IntPtr rect, IntPtr rgn, uint flags);
        [DllImport("user32.dll")] public static extern int SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int l, int t, int r, int b);

        public const int SW_HIDE = 0, SW_SHOWNA = 8, SW_MINIMIZE = 6, SW_RESTORE = 9;
        public const uint GW_OWNER = 4;
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;

        // ---------- процессы ----------
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
        static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);

        public static string ExePath(uint pid)
        {
            IntPtr h = OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION — работает и для чужих процессов
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }

        /// <summary>
        /// Код приложения Microsoft Store / MSIX (AUMID) — по нему Windows группирует окна на панели задач.
        /// У обычных программ его нет — тогда null.
        /// </summary>
        public static string AumidOf(uint pid)
        {
            IntPtr h = OpenProcess(0x1000, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                uint len = 512;
                var sb = new StringBuilder((int)len);
                return GetApplicationUserModelId(h, ref len, sb) == 0 && sb.Length > 0 ? sb.ToString() : null;
            }
            catch (EntryPointNotFoundException) { return null; }
            finally { CloseHandle(h); }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int GetApplicationUserModelId(IntPtr process, ref uint length, StringBuilder aumid);

        /// <summary>
        /// Настоящая панель задач Windows. Просто FindWindow("Shell_TrayWnd") не подходит:
        /// трей дока создаёт своё окно с таким же именем — пропускаем окна своей программы.
        /// </summary>
        public static IntPtr ExplorerTray()
        {
            uint own = (uint)Environment.ProcessId;
            IntPtr h = IntPtr.Zero;
            while ((h = FindWindowEx(IntPtr.Zero, h, "Shell_TrayWnd", null)) != IntPtr.Zero)
            {
                GetWindowThreadProcessId(h, out uint pid);
                if (pid != own) return h;
            }
            return IntPtr.Zero;
        }

        /// <summary>Открыто ли меню «Пуск» (или поиск) — тогда кнопка «Пуск» в доке его просто закрывает.</summary>
        public static bool StartIsOpen()
        {
            try
            {
                IntPtr fg = Native.GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;
                GetWindowThreadProcessId(fg, out uint pid);
                string exe = ExePath(pid);
                if (string.IsNullOrEmpty(exe)) return false;
                string name = System.IO.Path.GetFileName(exe);
                return name.Equals("StartMenuExperienceHost.exe", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("SearchHost.exe", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("ShellExperienceHost.exe", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("SearchApp.exe", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>Заголовок окна без отправки сообщения самому окну: не зависает, даже если программа «не отвечает».</summary>
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int InternalGetWindowText(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool SendNotifyMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(uint pid);
        [DllImport("user32.dll")] public static extern bool RegisterShellHookWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool DeregisterShellHookWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);

        public const uint WM_CLOSE = 0x0010, WM_COPYDATA = 0x004A;
        public const uint SMTO_ABORTIFHUNG = 0x0002;
        public const uint GA_ROOTOWNER = 3;
        public const long WS_EX_APPWINDOW = 0x00040000;

        // ---------- мониторы ----------
        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO { public int cbSize; public Native.RECT rcMonitor, rcWork; public uint dwFlags; }
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromRect(ref Native.RECT r, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);

        /// <summary>Рабочая область монитора (без дока и панели задач), в которой лежит прямоугольник.</summary>
        public static Native.RECT WorkAreaOf(Native.RECT r)
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            IntPtr mon = MonitorFromRect(ref r, 2);   // MONITOR_DEFAULTTONEAREST
            return GetMonitorInfo(mon, ref mi) ? mi.rcWork : r;
        }

        // ---------- события окон (свернули/развернули — в любой программе) ----------
        public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
        public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016, EVENT_SYSTEM_MINIMIZEEND = 0x0017;

        // ---------- миниатюры окон ----------
        [DllImport("dwmapi.dll")] public static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out SIZE size);
        public const int DWM_TNP_RECTSOURCE = 2;

        // ---------- клавиши ----------
        public const byte VK_LWIN = 0x5B, VK_MENU = 0x12, VK_LBUTTON = 0x01, VK_RBUTTON = 0x02;

        public static void PressKeys(params byte[] keys)
        {
            foreach (var k in keys) keybd_event(k, 0, 0, UIntPtr.Zero);
            for (int i = keys.Length - 1; i >= 0; i--) keybd_event(keys[i], 0, 2, UIntPtr.Zero);   // 2 = KEYEVENTF_KEYUP
        }

        // ---------- AppBar (резерв места внизу экрана) ----------
        [StructLayout(LayoutKind.Sequential)]
        public struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public Native.RECT rc;
            public IntPtr lParam;
        }

        [DllImport("shell32.dll")] public static extern UIntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);

        public const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3, ABM_GETSTATE = 4, ABM_SETSTATE = 10;
        public const int ABS_AUTOHIDE = 1, ABS_ALWAYSONTOP = 2;
        public const uint ABE_BOTTOM = 3;

        public static APPBARDATA NewAppBar(IntPtr h) =>
            new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = h };

        // ---------- размытие (акрил) ----------
        [StructLayout(LayoutKind.Sequential)]
        struct AccentPolicy { public int AccentState, AccentFlags, GradientColor, AnimationId; }

        [StructLayout(LayoutKind.Sequential)]
        struct WindowCompositionAttributeData { public int Attribute; public IntPtr Data; public int SizeOfData; }

        [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr h, ref WindowCompositionAttributeData d);

        /// <summary>Акриловое размытие всего, что под окном. tintAbgr — цвет оттенка в формате 0xAABBGGRR.</summary>
        public static void EnableAcrylic(IntPtr h, int tintAbgr)
        {
            var accent = new AccentPolicy { AccentState = 4, AccentFlags = 2, GradientColor = tintAbgr };
            int size = Marshal.SizeOf<AccentPolicy>();
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WindowCompositionAttributeData { Attribute = 19, Data = ptr, SizeOfData = size };   // 19 = WCA_ACCENT_POLICY
                SetWindowCompositionAttribute(h, ref data);
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }

        // ---------- иконки высокого разрешения ----------
        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr hbitmap);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int cx, cy; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        public static extern void SHCreateItemFromParsingName(string path, IntPtr bindCtx,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        [DllImport("gdi32.dll")] public static extern int GetObject(IntPtr h, int size, out BITMAP bm);
        [DllImport("gdi32.dll")] public static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, [Out] byte[] bits, ref BITMAPINFOHEADER bi, uint usage);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    }
}
