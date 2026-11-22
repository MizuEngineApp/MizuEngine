// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.IO;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>
    /// Следит за памятью, как это делают Discord и Telegram: пока человек работает с MizuEngine — ничего не трогаем;
    /// как только программа ушла в фон (окно в трее, свёрнуто или человек 20+ секунд в другой программе) —
    /// отдаём Windows то, что сейчас не нужно. Ничего не выгружается и не выключается: страницы памяти просто
    /// уходят из «рабочего набора» (столбец «Память» в диспетчере задач) и мгновенно возвращаются, когда понадобятся.
    /// Раз в минуту пишет цифры в %AppData%\MizuEngine\mem.log — по ним видно, сколько на самом деле занято.
    /// </summary>
    internal static class MemoryKeeper
    {
        static Func<Window> _main;
        static DispatcherTimer _timer;
        static DateTime _lastTrim = DateTime.MinValue, _lastDeep = DateTime.MinValue, _lastLog = DateTime.MinValue;
        static DateTime _busySince = DateTime.UtcNow, _idleSince = DateTime.UtcNow;
        static long _wsAfterTrim, _heapAfterDeep;
        static bool _wasIdle;

        static string LogFile => Path.Combine(Settings.Folder, "mem.log");

        /// <summary>Запуск в главном потоке. main — главное окно (null, если оно закрыто в трей).</summary>
        public static void Start(Func<Window> main)
        {
            _main = main;
            _timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(5) };
            _timer.Tick += (o, e) => Tick();
            _timer.Start();
        }

        /// <summary>Окно закрыли в трей или свернули — через пару секунд (когда доиграет анимация) отдаём память.</summary>
        public static void Soon(bool deep)
        {
            var t = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(2) };
            t.Tick += (o, e) =>
            {
                t.Stop();
                if (IsBusy()) return;
                Trim(deep);
                Log(deep ? "в трее" : "свёрнуто");
            };
            t.Start();
        }

        /// <summary>Человек сейчас в MizuEngine: активно наше окно (главное, Launchpad, Spotlight, меню трея).</summary>
        static bool IsBusy()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            GetWindowThreadProcessId(fg, out uint pid);
            return pid == (uint)Environment.ProcessId;
        }

        static void Tick()
        {
            try
            {
                var now = DateTime.UtcNow;
                var w = _main?.Invoke();
                bool hidden = w == null || !w.IsVisible || w.WindowState == WindowState.Minimized;
                bool busy = IsBusy();
                if (busy) { _busySince = now; _wasIdle = false; }
                else if (!_wasIdle) { _wasIdle = true; _idleSince = now; }

                if ((now - _lastLog).TotalSeconds >= 60) Log(hidden ? (w == null ? "в трее" : "свёрнуто") : busy ? "окно активно" : "окно в фоне");

                // Работаем с программой (или только что работали) — ничего не трогаем, чтобы не было ни одного подтормаживания
                if (busy || (now - _busySince).TotalSeconds < 20) return;

                var c = Counters();
                // Окно в трее: раз в 3 минуты (если куча выросла) ещё и полная уборка с уплотнением — в фоне её не видно
                bool deep = w == null && (now - _lastDeep).TotalMinutes >= 3 && GC.GetTotalMemory(false) - _heapAfterDeep > 8L << 20;
                bool grew = c.ws - _wsAfterTrim > (hidden ? 12L << 20 : 24L << 20);
                bool due = (now - _lastTrim).TotalSeconds >= (hidden ? 30 : 60) && (grew || (now - _lastTrim).TotalMinutes >= 5);
                if (deep || due) Trim(deep);
            }
            catch (Exception e) { Settings.Log(e); }
        }

        /// <summary>Отдать Windows неиспользуемую память. deep — ещё и полная сборка мусора с уплотнением больших объектов.</summary>
        public static void Trim(bool deep)
        {
            try
            {
                if (deep)
                {
                    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect(2, GCCollectionMode.Forced, true, true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(2, GCCollectionMode.Forced, true, true);
                    _lastDeep = DateTime.UtcNow;
                    _heapAfterDeep = GC.GetTotalMemory(false);
                }
                SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
                _lastTrim = DateTime.UtcNow;
                _wsAfterTrim = Counters().ws;
            }
            catch (Exception e) { Settings.Log(e); }
        }

        /// <summary>Для второго процесса (сторожа): он только ждёт — держать ему в памяти нечего.</summary>
        public static void TrimSelf()
        {
            try
            {
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
            }
            catch { }
        }

        static void Log(string state)
        {
            try
            {
                _lastLog = DateTime.UtcNow;
                var c = Counters();
                Directory.CreateDirectory(Settings.Folder);
                try { if (File.Exists(LogFile) && new FileInfo(LogFile).Length > 128 * 1024) File.Delete(LogFile); } catch { }
                File.AppendAllText(LogFile,
                    $"{DateTime.Now:dd.MM HH:mm:ss}  {state,-13} память (как в диспетчере) {c.privWs >> 20,4} МБ · рабочий набор {c.ws >> 20,4} МБ · " +
                    $"выделено {c.priv >> 20,4} МБ · куча .NET {GC.GetTotalMemory(false) >> 20,3} МБ · потоков {System.Diagnostics.Process.GetCurrentProcess().Threads.Count}\n");
            }
            catch { }
        }

        static (long ws, long privWs, long priv) Counters()
        {
            var m = new PROCESS_MEMORY_COUNTERS_EX2 { cb = (uint)Marshal.SizeOf<PROCESS_MEMORY_COUNTERS_EX2>() };
            if (K32GetProcessMemoryInfo(GetCurrentProcess(), ref m, m.cb))
                return ((long)m.WorkingSetSize, (long)m.PrivateWorkingSetSize, (long)m.PrivateUsage);
            // Старая Windows 10 не знает расширенную структуру — берём обычную
            m.cb = (uint)Marshal.OffsetOf<PROCESS_MEMORY_COUNTERS_EX2>(nameof(PROCESS_MEMORY_COUNTERS_EX2.PrivateWorkingSetSize));
            if (K32GetProcessMemoryInfo(GetCurrentProcess(), ref m, m.cb))
                return ((long)m.WorkingSetSize, (long)m.WorkingSetSize, (long)m.PrivateUsage);
            return (0, 0, 0);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PROCESS_MEMORY_COUNTERS_EX2
        {
            public uint cb, PageFaultCount;
            public UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
                           QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage, PrivateUsage,
                           PrivateWorkingSetSize;
            public ulong SharedCommitUsage;
        }

        [DllImport("kernel32.dll")] static extern bool K32GetProcessMemoryInfo(IntPtr process, ref PROCESS_MEMORY_COUNTERS_EX2 counters, uint cb);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    }
}
