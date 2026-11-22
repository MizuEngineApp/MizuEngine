// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MizuEngine
{
    /// <summary>Закреплённый в доке значок (хранится в settings.json, порядок — как в доке).</summary>
    internal sealed class DockPin
    {
        public string Path { get; set; }     // что запускать: ярлык .lnk, exe или shell:AppsFolder\<AUMID>
        public string Target { get; set; }   // с какими окнами связан: путь к exe или "aumid:<AUMID>"
        public string Name { get; set; }
    }

    /// <summary>
    /// Какие приложения закреплены, какие окна открыты и как выглядят их иконки.
    /// Окна отбираются по тем же правилам, что у панели задач Windows.
    /// </summary>
    internal static class DockApps
    {
        public sealed class Win
        {
            public IntPtr Hwnd;
            public string Exe, Aumid, Title;
            public uint Pid;
            /// <summary>По чему окна группируются в один значок: AUMID у приложений из Store, иначе путь к exe.</summary>
            public string Key => Aumid != null ? "aumid:" + Aumid : Exe;
        }

        static readonly object Lock = new object();   // список окон берут два потока: док и рамки окон
        static readonly Dictionary<string, ImageSource> Icons = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<uint, (string exe, string aumid)> ByPid = new Dictionary<uint, (string, string)>();
        static readonly Dictionary<IntPtr, string> FrameAumid = new Dictionary<IntPtr, string>();   // окна Store-приложений: AUMID запоминаем, пока окно живо
        static readonly StringBuilder Cls = new StringBuilder(256);
        static readonly StringBuilder TitleSb = new StringBuilder(512);

        // ================== закреплённые ==================

        /// <summary>Один раз переносим значки, закреплённые на панели задач Windows (дальше док хранит свой список).</summary>
        public static List<DockPin> ImportWindowsPins()
        {
            var list = new List<DockPin>();
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
            if (!Directory.Exists(dir)) return list;
            foreach (var lnk in Directory.GetFiles(dir, "*.lnk").OrderBy(f => File.GetCreationTimeUtc(f)))
                list.Add(new DockPin { Path = lnk, Target = LinkTarget(lnk), Name = Path.GetFileNameWithoutExtension(lnk) });
            return list;
        }

        /// <summary>Значок для открытого приложения, которое закрепляют из меню дока.</summary>
        public static DockPin PinFor(Win w, string name)
        {
            if (w.Aumid != null) return new DockPin { Path = @"shell:AppsFolder\" + w.Aumid, Target = w.Key, Name = name };
            return new DockPin { Path = w.Exe, Target = w.Exe, Name = name };
        }

        public static string LinkTarget(string lnk)
        {
            try
            {
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                return (string)shell.CreateShortcut(lnk).TargetPath ?? "";
            }
            catch { return ""; }
        }

        /// <summary>
        /// Относится ли окно к закреплённому значку. Кроме точного пути сравниваем и имена:
        /// у Discord, Telegram и других ярлык ведёт на «запускалку», а окно открывает другой exe.
        /// </summary>
        public static bool Matches(DockPin pin, Win w)
        {
            if (!string.IsNullOrEmpty(pin.Target) && string.Equals(pin.Target, w.Key, StringComparison.OrdinalIgnoreCase)) return true;
            if (w.Aumid != null || string.IsNullOrEmpty(pin.Name) || string.IsNullOrEmpty(w.Exe)) return false;
            if (!string.IsNullOrEmpty(pin.Target) && pin.Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetFileName(pin.Target), Path.GetFileName(w.Exe), StringComparison.OrdinalIgnoreCase)) return true;
            string exeName = Path.GetFileNameWithoutExtension(w.Exe);
            return string.Equals(pin.Name, exeName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(pin.Name, NameOf(w.Exe), StringComparison.OrdinalIgnoreCase);
        }

        // ================== открытые окна ==================

        /// <summary>Окна, которые Windows показала бы на панели задач (сверху вниз по z-порядку).</summary>
        public static List<Win> EnumRunning()
        {
            lock (Lock)
            {
                var list = new List<Win>();
                var alive = new HashSet<IntPtr>();
                Native.EnumWindows((h, _) =>
                {
                    if (!Native.IsWindowVisible(h)) return true;
                    long ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
                    // Правила панели задач: WS_EX_APPWINDOW показывается всегда; иначе — без владельца, не «инструмент» и не «без фокуса»
                    if ((ex & DockNative.WS_EX_APPWINDOW) == 0)
                    {
                        if (DockNative.GetWindow(h, DockNative.GW_OWNER) != IntPtr.Zero) return true;
                        if ((ex & Native.WS_EX_TOOLWINDOW) != 0 || (ex & Native.WS_EX_NOACTIVATE) != 0) return true;
                    }
                    if (Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;

                    TitleSb.Clear();
                    DockNative.InternalGetWindowText(h, TitleSb, TitleSb.Capacity);
                    if (TitleSb.Length == 0) return true;

                    Cls.Clear();
                    Native.GetClassName(h, Cls, Cls.Capacity);
                    string c = Cls.ToString();
                    if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd"
                        || c == "Windows.UI.Core.CoreWindow" || c == "MizuEngineTray") return true;

                    DockNative.GetWindowThreadProcessId(h, out uint pid);
                    // Путь к программе и её AUMID запоминаем: открывать процесс при каждом обновлении — лишняя нагрузка
                    if (!ByPid.TryGetValue(pid, out var info))
                    {
                        if (ByPid.Count > 400) ByPid.Clear();
                        info = (DockNative.ExePath(pid), DockNative.AumidOf(pid));
                        ByPid[pid] = info;
                    }
                    if (info.exe == null) return true;

                    string aumid = info.aumid;
                    if (aumid == null && c == "ApplicationFrameWindow") aumid = FrameAppId(h);   // Параметры, Калькулятор и другие из Store
                    alive.Add(h);
                    list.Add(new Win { Hwnd = h, Exe = info.exe, Aumid = aumid, Title = TitleSb.ToString(), Pid = pid });
                    return true;
                }, IntPtr.Zero);

                foreach (var dead in FrameAumid.Keys.Where(k => !alive.Contains(k)).ToList()) FrameAumid.Remove(dead);
                return list;
            }
        }

        /// <summary>Окна Store-приложений рисует ApplicationFrameHost, а само приложение живёт в дочернем окне — берём AUMID у него.</summary>
        static string FrameAppId(IntPtr frame)
        {
            DockNative.GetWindowThreadProcessId(frame, out uint framePid);
            IntPtr child = IntPtr.Zero;
            while ((child = DockNative.FindWindowEx(frame, child, "Windows.UI.Core.CoreWindow", null)) != IntPtr.Zero)
            {
                DockNative.GetWindowThreadProcessId(child, out uint pid);
                if (pid == framePid) continue;
                string id = DockNative.AumidOf(pid);
                if (id != null) { FrameAumid[frame] = id; return id; }
            }
            // Свёрнутое Store-приложение «засыпает» и отцепляется от рамки — берём то, что запомнили раньше
            return FrameAumid.TryGetValue(frame, out var known) ? known : null;
        }

        /// <summary>AUMID окна (у приложений из Store — даже если окно рисует ApplicationFrameHost). null — обычная программа.</summary>
        public static string AumidOfWindow(IntPtr h)
        {
            lock (Lock)
            {
                DockNative.GetWindowThreadProcessId(h, out uint pid);
                string id = DockNative.AumidOf(pid);
                if (id != null) return id;
                Cls.Clear();
                Native.GetClassName(h, Cls, Cls.Capacity);
                return Cls.ToString() == "ApplicationFrameWindow" ? FrameAppId(h) : null;
            }
        }

        public static string NameOf(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return "";
            lock (Names)
            {
                if (Names.TryGetValue(exe, out var n)) return n;
                try { n = FileVersionInfo.GetVersionInfo(exe).FileDescription; } catch { }
                if (string.IsNullOrWhiteSpace(n)) n = Path.GetFileNameWithoutExtension(exe);
                Names[exe] = n;
                return n;
            }
        }

        /// <summary>Откуда брать иконку окна: у приложений из Store — плитка из «Пуска», иначе — exe.</summary>
        public static string IconSource(Win w) => w.Aumid != null ? @"shell:AppsFolder\" + w.Aumid : w.Exe;

        // ================== иконки ==================

        static readonly object IconLock = new object();
        static Thread _iconThread;
        const int IconPx = 128;   // хватает для увеличенного значка даже на 4K, а памяти вчетверо меньше, чем у 256 px

        /// <summary>
        /// Иконка файла (exe, ярлыка или Store-приложения), как в Проводнике.
        /// Если её ещё нет в памяти — возвращает null и достаёт в отдельном потоке, потом вызывает done.
        /// </summary>
        public static ImageSource IconFor(string path, Action<ImageSource> done)
        {
            if (string.IsNullOrEmpty(path)) return null;
            lock (IconLock)
            {
                if (Icons.TryGetValue(path, out var cached)) return cached;
                if (_iconThread == null)
                {
                    // Поток значков — с настоящим циклом сообщений (Dispatcher), а не «ждёт в очереди молча»:
                    // Проводник создаёт в этом потоке скрытые окна, и поток, который не разбирает сообщения,
                    // подвешивал всех, кто к этим окнам обращался (так «зависало» окно MizuEngine)
                    var ready = new ManualResetEventSlim();
                    _iconThread = new Thread(() =>
                    {
                        _iconDispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                        ready.Set();
                        System.Windows.Threading.Dispatcher.Run();
                    }) { IsBackground = true, Name = "MizuEngine dock icons" };
                    _iconThread.SetApartmentState(ApartmentState.STA);   // значкам Проводника нужен STA-поток
                    _iconThread.Start();
                    ready.Wait();
                }
            }
            _iconDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                ImageSource img;
                bool known;
                lock (IconLock) known = Icons.TryGetValue(path, out img);
                if (!known)
                {
                    try { img = ShellImage(path, IconPx); } catch { img = null; }
                    lock (IconLock) Icons[path] = img;
                }
                if (img != null) try { done(img); } catch (Exception e) { Settings.Log(e); }
            }));
            return null;
        }

        static System.Windows.Threading.Dispatcher _iconDispatcher;

        static ImageSource ShellImage(string path, int size)
        {
            var iid = typeof(DockNative.IShellItemImageFactory).GUID;
            DockNative.SHCreateItemFromParsingName(path, IntPtr.Zero, iid, out var factory);
            // 4 = SIIGBF_ICONONLY (иконка, а не миниатюра содержимого)
            if (factory.GetImage(new DockNative.SIZE { cx = size, cy = size }, 4, out IntPtr hbm) != 0 || hbm == IntPtr.Zero)
                return null;
            try
            {
                DockNative.GetObject(hbm, System.Runtime.InteropServices.Marshal.SizeOf<DockNative.BITMAP>(), out var bm);
                int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
                if (w <= 0 || h <= 0) return null;

                var bi = new DockNative.BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
                var bits = new byte[w * h * 4];
                IntPtr dc = DockNative.GetDC(IntPtr.Zero);
                try { DockNative.GetDIBits(dc, hbm, 0, (uint)h, bits, ref bi, 0); }
                finally { DockNative.ReleaseDC(IntPtr.Zero, dc); }

                // Обычно Windows отдаёт цвета, уже умноженные на прозрачность; но не всегда — проверяем
                bool premultiplied = true;
                for (int i = 0; i < bits.Length && premultiplied; i += 4)
                    if (bits[i] > bits[i + 3] || bits[i + 1] > bits[i + 3] || bits[i + 2] > bits[i + 3]) premultiplied = false;

                var src = BitmapSource.Create(w, h, 96, 96, premultiplied ? PixelFormats.Pbgra32 : PixelFormats.Bgra32, null, bits, w * 4);
                src.Freeze();
                return src;
            }
            finally { DockNative.DeleteObject(hbm); }
        }
    }
}
