// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MizuEngine
{
    /// <summary>Значок программы в трее (Discord, Steam, антивирус и т. д.).</summary>
    internal sealed class TrayIcon
    {
        public IntPtr HWnd;
        public uint Id, CallbackMessage, Version;
        public Guid Guid;
        public string Tip = "";
        public ImageSource Icon;
        public bool Hidden;
        public long Order;
    }

    /// <summary>
    /// Трей внутри дока. Программы добавляют значки в трей, отправляя сообщение окну «Shell_TrayWnd».
    /// Мы создаём своё такое окно (невидимое) и держим его первым — так значки приходят и к нам.
    /// Каждое сообщение мы ещё и пересылаем настоящему трею Windows: после выхода из MizuEngine
    /// (или если что-то пойдёт не так) все значки остаются на месте. Так же работают RetroBar и Cairo Shell.
    /// Окно живёт в своём потоке с простым циклом сообщений: программы ждут ответа от трея, ему нельзя «задумываться».
    /// </summary>
    internal static class TrayHost
    {
        /// <summary>Значки изменились. Вызывается из потока трея.</summary>
        public static event Action Changed;

        static readonly object Lock = new object();
        static readonly List<TrayIcon> Icons = new List<TrayIcon>();
        static Thread _thread;
        static IntPtr _hwnd, _explorer;
        static WndProcFn _proc;          // делегат держим, иначе сборщик мусора его удалит, а Windows продолжит вызывать
        static long _order;
        static uint _taskbarCreated;
        static int _ticks;

        public static bool Running => _hwnd != IntPtr.Zero;

        /// <summary>Видимые значки по порядку появления.</summary>
        public static List<TrayIcon> Snapshot()
        {
            lock (Lock)
            {
                var list = Icons.FindAll(i => !i.Hidden && i.Icon != null);
                list.Sort((a, b) => a.Order.CompareTo(b.Order));
                return list;
            }
        }

        public static void Start()
        {
            if (_thread != null) return;
            _thread = new Thread(Loop) { IsBackground = true, Name = "MizuEngine tray" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        public static void Stop()
        {
            var t = _thread;
            if (t == null) return;
            if (_hwnd != IntPtr.Zero) PostMessage(_hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            try { t.Join(2000); } catch { }
            _thread = null;
        }

        // ================== окно ==================

        static void Loop()
        {
            try
            {
                IntPtr inst = GetModuleHandle(null);
                if (_proc == null) _proc = WndProc;
                IntPtr fn = Marshal.GetFunctionPointerForDelegate(_proc);
                foreach (var cls in new[] { "Shell_TrayWnd", "TrayNotifyWnd" })
                {
                    var wc = new WNDCLASSEX { cbSize = Marshal.SizeOf<WNDCLASSEX>(), style = 8, lpfnWndProc = fn, hInstance = inst, lpszClassName = cls };
                    RegisterClassEx(ref wc);   // уже зарегистрирован (повторный запуск) — не страшно
                }

                int sw = Native.GetSystemMetrics(0), sh = Native.GetSystemMetrics(1);
                _hwnd = CreateWindowEx(WS_EX_TOPMOST | WS_EX_TOOLWINDOW, "Shell_TrayWnd", "", WS_POPUP | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
                                       0, sh - 48, sw, 48, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
                if (_hwnd == IntPtr.Zero) return;
                CreateWindowEx(0, "TrayNotifyWnd", null, WS_CHILD | WS_CLIPCHILDREN | WS_CLIPSIBLINGS, 0, 0, sw, 48, _hwnd, IntPtr.Zero, inst, IntPtr.Zero);
                DockNative.SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, DockNative.SWP_NOMOVE | DockNative.SWP_NOSIZE | DockNative.SWP_NOACTIVATE);
                SetTimer(_hwnd, (UIntPtr)1, 250, IntPtr.Zero);

                // Просим все программы заново добавить свои значки (так делает Windows, когда перезапускается Проводник)
                _taskbarCreated = DockNative.RegisterWindowMessage("TaskbarCreated");
                PostMessage(HWND_BROADCAST, _taskbarCreated, IntPtr.Zero, IntPtr.Zero);

                while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
            }
            catch (Exception e) { Settings.Log(e); }
            finally
            {
                _hwnd = IntPtr.Zero;
                lock (Lock) Icons.Clear();
                RaiseChanged();
                // Наше окно исчезло — пусть программы на всякий случай ещё раз добавят значки в трей Windows
                if (_taskbarCreated != 0) PostMessage(HWND_BROADCAST, _taskbarCreated, IntPtr.Zero, IntPtr.Zero);
            }
        }

        static IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l)
        {
            try
            {
                if (h != _hwnd) return DefWindowProc(h, m, w, l);   // дочернее TrayNotifyWnd — просто для совместимости
                switch (m)
                {
                    case DockNative.WM_COPYDATA:
                    {
                        var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(l);
                        if (cds.dwData.ToInt64() == 1 && cds.lpData != IntPtr.Zero && cds.cbData >= 8 + NidSize)
                        {
                            int message = Marshal.ReadInt32(cds.lpData, 4);
                            var nid = Marshal.PtrToStructure<NOTIFYICONDATA32>(cds.lpData + 8);
                            bool ok = Handle(message, ref nid);
                            Forward(m, w, l);   // настоящий трей Windows тоже знает про значок
                            return new IntPtr(ok ? 1 : 0);
                        }
                        return Forward(m, w, l);   // резерв места (AppBar), положение значка и прочее — отвечает Проводник
                    }
                    case WM_TIMER:
                        KeepFirst();
                        if (++_ticks % 8 == 0) PruneDead();
                        return IntPtr.Zero;
                    case WM_WINDOWPOSCHANGED:
                        // Кто-то попытался показать наше окно — оно должно оставаться невидимым
                        if (Native.IsWindowVisible(h)) DockNative.ShowWindow(h, DockNative.SW_HIDE);
                        return DefWindowProc(h, m, w, l);
                    case WM_CLOSE:
                        KillTimer(h, (UIntPtr)1);
                        DestroyWindow(h);
                        return IntPtr.Zero;
                    case WM_DESTROY:
                        PostQuitMessage(0);
                        return IntPtr.Zero;
                }
                if (m == WM_ACTIVATEAPP || m == WM_COMMAND || m >= WM_USER) return Forward(m, w, l);
            }
            catch (Exception e) { Settings.Log(e); }
            return DefWindowProc(h, m, w, l);
        }

        /// <summary>Наше окно должно быть первым «Shell_TrayWnd» — Проводник иногда поднимает своё выше.</summary>
        static void KeepFirst()
        {
            if (DockNative.FindWindow("Shell_TrayWnd", null) != _hwnd)
                DockNative.SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, DockNative.SWP_NOMOVE | DockNative.SWP_NOSIZE | DockNative.SWP_NOACTIVATE);
        }

        static IntPtr Forward(uint m, IntPtr w, IntPtr l)
        {
            if (_explorer == IntPtr.Zero || !DockNative.IsWindow(_explorer)) _explorer = DockNative.ExplorerTray();
            if (_explorer == IntPtr.Zero) return IntPtr.Zero;
            if (m == WM_USER + 372) { PostMessage(_explorer, m, w, l); return IntPtr.Zero; }
            DockNative.SendMessageTimeout(_explorer, m, w, l, DockNative.SMTO_ABORTIFHUNG, 3000, out IntPtr result);
            return result;
        }

        // ================== значки ==================

        const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETFOCUS = 3, NIM_SETVERSION = 4;
        const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_STATE = 8, NIF_GUID = 0x20;
        const uint NIS_HIDDEN = 1;

        static bool Handle(int message, ref NOTIFYICONDATA32 nid)
        {
            bool ok = true, changed = false;
            var hwnd = new IntPtr(unchecked((int)nid.hWnd));
            bool byGuid = (nid.uFlags & NIF_GUID) != 0 && nid.guidItem != Guid.Empty;
            lock (Lock)
            {
                Guid g = nid.guidItem;
                uint id = nid.uID;
                var icon = byGuid ? Icons.Find(i => i.Guid == g) : Icons.Find(i => i.HWnd == hwnd && i.Id == id && i.Guid == Guid.Empty);
                switch ((uint)message)
                {
                    case NIM_ADD:
                    case NIM_MODIFY:
                        if (icon == null)
                        {
                            if (message == NIM_MODIFY && (nid.uFlags & NIF_ICON) == 0) { ok = false; break; }
                            icon = new TrayIcon { Order = ++_order };
                            Icons.Add(icon);
                        }
                        if (hwnd != IntPtr.Zero) icon.HWnd = hwnd;
                        icon.Id = nid.uID;
                        if (byGuid) icon.Guid = nid.guidItem;
                        if ((nid.uFlags & NIF_MESSAGE) != 0) icon.CallbackMessage = nid.uCallbackMessage;
                        if ((nid.uFlags & NIF_ICON) != 0) icon.Icon = ConvertIcon(nid.hIcon);
                        if ((nid.uFlags & NIF_TIP) != 0) icon.Tip = nid.szTip ?? "";
                        if ((nid.uFlags & NIF_STATE) != 0 && (nid.dwStateMask & NIS_HIDDEN) != 0) icon.Hidden = (nid.dwState & NIS_HIDDEN) != 0;
                        changed = true;
                        break;
                    case NIM_DELETE:
                        ok = icon != null;
                        if (icon != null) { Icons.Remove(icon); changed = true; }
                        break;
                    case NIM_SETVERSION:
                        if (icon != null) icon.Version = nid.uVersion;
                        break;
                    case NIM_SETFOCUS:
                        break;
                    default:
                        ok = false;
                        break;
                }
            }
            if (changed) RaiseChanged();
            return ok;
        }

        static void PruneDead()
        {
            bool changed;
            lock (Lock) changed = Icons.RemoveAll(i => i.HWnd != IntPtr.Zero && !DockNative.IsWindow(i.HWnd)) > 0;
            if (changed) RaiseChanged();
        }

        static void RaiseChanged()
        {
            try { Changed?.Invoke(); } catch (Exception e) { Settings.Log(e); }
        }

        static ImageSource ConvertIcon(uint handle)
        {
            if (handle == 0) return null;
            try
            {
                // Значок принадлежит другой программе и может исчезнуть — сразу делаем свою копию картинки
                var src = Imaging.CreateBitmapSourceFromHIcon(new IntPtr(unchecked((int)handle)), Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            catch { return null; }
        }

        // ================== нажатия ==================

        const uint WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_LBUTTONDBLCLK = 0x203,
                   WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205, WM_CONTEXTMENU = 0x7B, NIN_SELECT = 0x400;

        /// <summary>Передаём нажатие программе так же, как это делает трей Windows. x, y — пиксели экрана.</summary>
        public static void Click(TrayIcon icon, bool right, bool dbl, int x, int y)
        {
            if (icon == null || icon.HWnd == IntPtr.Zero || icon.CallbackMessage == 0) return;
            DockNative.GetWindowThreadProcessId(icon.HWnd, out uint pid);
            DockNative.AllowSetForegroundWindow(pid);   // чтобы её меню появилось поверх всего
            if (right) { Send(icon, WM_RBUTTONDOWN, x, y); Send(icon, WM_RBUTTONUP, x, y); if (icon.Version >= 4) Send(icon, WM_CONTEXTMENU, x, y); }
            else if (dbl) { Send(icon, WM_LBUTTONDBLCLK, x, y); Send(icon, WM_LBUTTONUP, x, y); }
            else { Send(icon, WM_LBUTTONDOWN, x, y); Send(icon, WM_LBUTTONUP, x, y); if (icon.Version >= 4) Send(icon, NIN_SELECT, x, y); }
        }

        /// <summary>Курсор над значком — некоторые программы обновляют подсказку только после этого.</summary>
        public static void Hover(TrayIcon icon, int x, int y)
        {
            if (icon != null && icon.HWnd != IntPtr.Zero && icon.CallbackMessage != 0) Send(icon, WM_MOUSEMOVE, x, y);
        }

        static void Send(TrayIcon icon, uint mouse, int x, int y)
        {
            IntPtr wp, lp;
            if (icon.Version >= 4)
            {
                wp = new IntPtr((y << 16) | (x & 0xFFFF));
                lp = new IntPtr(unchecked((int)((icon.Id << 16) | (mouse & 0xFFFF))));
            }
            else
            {
                wp = new IntPtr(unchecked((int)icon.Id));
                lp = new IntPtr(unchecked((int)mouse));
            }
            DockNative.SendNotifyMessage(icon.HWnd, icon.CallbackMessage, wp, lp);
        }

        // ================== WinAPI ==================

        delegate IntPtr WndProcFn(IntPtr h, uint m, IntPtr w, IntPtr l);

        static readonly int NidSize = Marshal.SizeOf<NOTIFYICONDATA32>();
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1), HWND_BROADCAST = new IntPtr(0xFFFF);
        const uint WM_DESTROY = 0x2, WM_CLOSE = 0x10, WM_ACTIVATEAPP = 0x1C, WM_WINDOWPOSCHANGED = 0x47, WM_COMMAND = 0x111, WM_TIMER = 0x113, WM_USER = 0x400;
        const uint WS_POPUP = 0x80000000, WS_CHILD = 0x40000000, WS_CLIPCHILDREN = 0x02000000, WS_CLIPSIBLINGS = 0x04000000;
        const uint WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80;

        [StructLayout(LayoutKind.Sequential)]
        struct COPYDATASTRUCT { public IntPtr dwData; public int cbData; public IntPtr lpData; }

        /// <summary>Описание значка в том виде, в каком его пересылает Windows (дескрипторы — 32-битные, одинаково для 32- и 64-битных программ).</summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct NOTIFYICONDATA32
        {
            public int cbSize;
            public uint hWnd, uID, uFlags, uCallbackMessage, hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public uint dwState, dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public uint uVersion;   // union с uTimeout
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public uint hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WNDCLASSEX
        {
            public int cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra, cbWndExtra;
            public IntPtr hInstance, hIcon, hCursor, hbrBackground;
            public string lpszMenuName, lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int ptX, ptY; }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);
        [DllImport("user32.dll", EntryPoint = "DefWindowProcW")] static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll", EntryPoint = "GetMessageW")] static extern int GetMessage(out MSG msg, IntPtr h, uint min, uint max);
        [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll", EntryPoint = "DispatchMessageW")] static extern IntPtr DispatchMessage(ref MSG msg);
        [DllImport("user32.dll")] static extern void PostQuitMessage(int code);
        [DllImport("user32.dll", EntryPoint = "PostMessageW")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern UIntPtr SetTimer(IntPtr h, UIntPtr id, uint ms, IntPtr proc);
        [DllImport("user32.dll")] static extern bool KillTimer(IntPtr h, UIntPtr id);
    }
}
