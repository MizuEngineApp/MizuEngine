// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.IO;
using System.Text;
using System.Windows.Interop;

namespace MizuEngine
{
    /// <summary>
    /// Для поиска ошибок: записывает в %AppData%\MizuEngine\windows.log все окна программы — где они, видны ли,
    /// пропускают ли клики. И аварийная клавиша Ctrl+Alt+Shift+N, которая мгновенно закрывает программу.
    /// </summary>
    internal static class Diagnostics
    {
        /// <summary>
        /// Пишет список окон в фоне. Раньше это делалось в главном потоке через GetWindowText — а он для своих окон
        /// шлёт сообщение потоку-хозяину и ждёт ответа без ограничения времени. Если поток-хозяин в этот момент
        /// занят (значки дока, список программ), окно MizuEngine «не отвечало» до минуты. Теперь — InternalGetWindowText
        /// (ничего не шлёт и не ждёт) и в фоновом потоке.
        /// </summary>
        public static void DumpWindows(string when) =>
            System.Threading.ThreadPool.QueueUserWorkItem(_ => DumpWindowsNow(when));

        static void DumpWindowsNow(string when)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"=== {DateTime.Now:HH:mm:ss} {when}, Windows {Environment.OSVersion.Version} ===");
                uint own = (uint)Environment.ProcessId;
                var cls = new StringBuilder(256);
                Native.EnumWindows((h, _) =>
                {
                    DockNative.GetWindowThreadProcessId(h, out uint pid);
                    if (pid != own) return true;
                    var title = new StringBuilder(256);
                    DockNative.InternalGetWindowText(h, title, title.Capacity);
                    cls.Clear();
                    Native.GetClassName(h, cls, cls.Capacity);
                    Native.GetWindowRect(h, out var r);
                    long ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
                    bool through = (ex & Native.WS_EX_TRANSPARENT) != 0 && (ex & Native.WS_EX_LAYERED) != 0;
                    sb.AppendLine($"'{title}' class={cls} visible={Native.IsWindowVisible(h)} rect=({r.Left},{r.Top})-({r.Right},{r.Bottom}) exstyle=0x{ex:X} clickThrough={through}");
                    return true;
                }, IntPtr.Zero);
                Directory.CreateDirectory(Settings.Folder);
                string file = Path.Combine(Settings.Folder, "windows.log");
                // Журнал не растёт бесконечно (было 800 КБ): больше 256 КБ — начинаем заново
                try { if (new FileInfo(file).Length > 256 * 1024) File.Delete(file); } catch { }
                File.AppendAllText(file, sb.ToString() + "\n");
            }
            catch (Exception e) { Settings.Log(e); }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);

        static HwndSource _hotkeyWindow;

        /// <summary>Ctrl+Alt+Shift+N — аварийный выход, работает всегда, даже если мышь «заблокирована».</summary>
        public static void RegisterPanicKey(Action exit)
        {
            _hotkeyWindow = new HwndSource(new HwndSourceParameters("MizuEngine hotkey") { ParentWindow = new IntPtr(-3) });   // окно только для сообщений
            _hotkeyWindow.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
            {
                if (msg == 0x0312 && w.ToInt32() == 7) { handled = true; exit(); }   // WM_HOTKEY
                return IntPtr.Zero;
            });
            RegisterHotKey(_hotkeyWindow.Handle, 7, 0x1 | 0x2 | 0x4 | 0x4000, (uint)'N');   // Alt+Ctrl+Shift, без автоповтора
        }
    }
}
