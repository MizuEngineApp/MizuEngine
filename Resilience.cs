// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MizuEngine
{
    /// <summary>
    /// «Сторож»: маленький второй процесс (тот же MizuEngine.exe с ключом --watchdog), который ничего не рисует,
    /// а только ждёт, пока закроется основной. Если MizuEngine закрылся сам (не через «Выход») — значит, упал:
    /// сторож сразу возвращает панель задач, системную анимацию и курсоры, пишет причину в error.log
    /// и запускает MizuEngine заново (не чаще 3 раз за 5 минут, чтобы не зациклиться). После перезапуска
    /// Dynamic Island коротко сообщает, что был сбой и всё восстановлено.
    /// «Снять задачу» в диспетчере задач — не сбой: панель задач вернётся, но перезапуска не будет.
    /// </summary>
    internal static class Watchdog
    {
        static string Marker => Path.Combine(Settings.Folder, "clean-exit");
        static string History => Path.Combine(Settings.Folder, "crashes.txt");
        public static string HangDump => Path.Combine(Settings.Folder, "hang.dmp");
        static string HangEventName(int pid) => "MizuEngine_HangDump_" + pid;
        static EventWaitHandle _hangEvent;

        /// <summary>Основной процесс: запустить сторожа.</summary>
        public static void Start()
        {
            try
            {
                try { File.Delete(Marker); } catch { }
                // Снимок прошлого зависания старше 2 дней больше не нужен (он большой — сотни МБ)
                try { if (File.Exists(HangDump) && DateTime.Now - File.GetLastWriteTime(HangDump) > TimeSpan.FromDays(2)) File.Delete(HangDump); } catch { }
                _hangEvent = new EventWaitHandle(false, EventResetMode.AutoReset, HangEventName(Environment.ProcessId));
                Process.Start(new ProcessStartInfo(Environment.ProcessPath, "--watchdog " + Environment.ProcessId)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(Environment.ProcessPath)
                });
            }
            catch (Exception e) { Settings.Log(e); }
        }

        /// <summary>Основной процесс: окно зависло — просим сторожа снять снимок памяти (по нему видно, где именно застряли).</summary>
        public static void RequestHangDump()
        {
            try { _hangEvent?.Set(); } catch { }
        }

        /// <summary>Основной процесс: выходим сами — сторожу не надо ничего делать.</summary>
        public static void MarkCleanExit()
        {
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                File.WriteAllText(Marker, Environment.ProcessId.ToString());
            }
            catch { }
        }

        /// <summary>Процесс-сторож (MizuEngine.exe --watchdog PID).</summary>
        public static void Run(string[] args)
        {
            int i = Array.IndexOf(args, "--watchdog");
            if (i < 0 || i + 1 >= args.Length || !int.TryParse(args[i + 1], out int pid)) return;
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }

            Process p;
            try { p = Process.GetProcessById(pid); } catch { return; }
            // Основной процесс сообщил о зависании окна — снимаем снимок памяти (один раз за запуск)
            try
            {
                var hang = new EventWaitHandle(false, EventResetMode.AutoReset, HangEventName(pid));
                bool dumped = false;
                ThreadPool.RegisterWaitForSingleObject(hang, (st, timedOut) =>
                {
                    if (dumped) return;
                    dumped = true;
                    WriteDump(p, pid);
                }, null, -1, true);
            }
            catch { }
            MemoryKeeper.TrimSelf();   // сторож только ждёт — в диспетчере задач он почти ничего не весит
            try { p.WaitForExit(); } catch { return; }
            int code = 0;
            try { code = p.ExitCode; } catch { }
            Thread.Sleep(400);

            // Закрылись сами (кнопка «Выход», обновление, перезапуск) — всё в порядке
            try
            {
                if (File.Exists(Marker) && File.ReadAllText(Marker).Trim() == pid.ToString())
                {
                    File.Delete(Marker);
                    return;
                }
            }
            catch { }

            // Сбой: первым делом возвращаем Windows в обычный вид
            Settings s = null;
            try { s = Settings.Load(); } catch { }
            if (s != null)
            {
                try { Taskbar.RestoreAll(s); } catch { }
                try { SysAnim.Restore(s); } catch { }
            }
            try { CursorPacks.Restore(); } catch { }
            Settings.Log(new Exception($"MizuEngine закрылся из-за сбоя (код 0x{code:X8}) — сторож вернул панель задач"));

            if (code == 1) return;   // «Снять задачу» в диспетчере задач — человек закрыл сам, не перезапускаем
            if (!AllowRestart()) { Settings.Log(new Exception("Сбоев больше 3 за 5 минут — автоперезапуск пропущен")); return; }

            try
            {
                Process.Start(new ProcessStartInfo(Environment.ProcessPath, "--recovered --tray")
                {
                    UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(Environment.ProcessPath)
                });
            }
            catch (Exception e) { Settings.Log(e); }
        }

        /// <summary>
        /// Снимок памяти зависшего MizuEngine в %AppData%\MizuEngine\hang.dmp (старый заменяется).
        /// По нему analyze-hang.bat показывает, какой код стоял в каждом потоке в момент зависания.
        /// </summary>
        static void WriteDump(Process p, int pid)
        {
            string tmp = HangDump + ".tmp";
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                {
                    // Полная память + потоки и дескрипторы: этого хватает, чтобы увидеть стеки .NET
                    const uint type = 0x2 | 0x4 | 0x20 | 0x800 | 0x1000 | 0x20000;
                    if (!MiniDumpWriteDump(p.Handle, (uint)pid, fs.SafeFileHandle, type, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero))
                        throw new Exception("MiniDumpWriteDump: ошибка " + Marshal.GetLastWin32Error());
                }
                if (File.Exists(HangDump)) File.Delete(HangDump);
                File.Move(tmp, HangDump);
                Settings.Log(new Exception($"Снимок зависания сохранён: {HangDump} ({new FileInfo(HangDump).Length / 1048576} МБ)"));
            }
            catch (Exception e)
            {
                try { File.Delete(tmp); } catch { }
                Settings.Log(e);
            }
        }

        [DllImport("dbghelp.dll", SetLastError = true)]
        static extern bool MiniDumpWriteDump(IntPtr hProcess, uint processId, Microsoft.Win32.SafeHandles.SafeFileHandle hFile, uint dumpType,
                                             IntPtr exceptionParam, IntPtr userStreamParam, IntPtr callbackParam);

        /// <summary>Не больше 3 автоперезапусков за 5 минут.</summary>
        static bool AllowRestart()
        {
            var now = DateTime.UtcNow;
            var list = new List<DateTime>();
            try
            {
                if (File.Exists(History))
                    foreach (var line in File.ReadAllLines(History))
                        if (long.TryParse(line, out long t)) list.Add(new DateTime(t, DateTimeKind.Utc));
            }
            catch { }
            list = list.Where(t => now - t < TimeSpan.FromMinutes(5)).ToList();
            if (list.Count >= 3) return false;
            list.Add(now);
            try { File.WriteAllLines(History, list.Select(t => t.Ticks.ToString())); } catch { }
            return true;
        }
    }

    /// <summary>
    /// Следит, чтобы окно MizuEngine не «зависало». Раз в секунду отдельный поток просит главный поток отметиться.
    /// Не отметился 6 секунд — в error.log пишется «Окно не отвечает…», а сторож снимает снимок памяти (hang.dmp),
    /// по которому видно точное место зависания. Отвис — запись «Окно снова отвечает (N с)».
    /// </summary>
    internal static class HangWatch
    {
        static long _beat;
        static Dispatcher _ui;

        public static void Start(Dispatcher ui)
        {
            _ui = ui;
            _beat = Environment.TickCount64;
            new Thread(Loop) { IsBackground = true, Name = "MizuEngine hangwatch", Priority = ThreadPriority.BelowNormal }.Start();
        }

        static void Loop()
        {
            bool hung = false, dumped = false;
            long pending = 0, last = Environment.TickCount64;
            while (true)
            {
                Thread.Sleep(1000);
                long now = Environment.TickCount64;
                // Компьютер спал (или весь процесс стоял) — это не зависание окна, начинаем отсчёт заново
                if (now - last > 3000) Interlocked.Exchange(ref _beat, now);
                last = now;
                // Одна просьба за раз: пока главный поток не ответил, новые не копятся в его очереди
                if (Interlocked.Read(ref pending) == 0)
                {
                    Interlocked.Exchange(ref pending, 1);
                    try
                    {
                        _ui.BeginInvoke(DispatcherPriority.Send, new Action(() =>
                        {
                            Interlocked.Exchange(ref _beat, Environment.TickCount64);
                            Interlocked.Exchange(ref pending, 0);
                        }));
                    }
                    catch { return; }   // программа закрывается
                }
                long silent = now - Interlocked.Read(ref _beat);
                if (!hung && silent > 6000)
                {
                    hung = true;
                    Settings.Log(new Exception($"Окно не отвечает уже {silent / 1000} с"));
                }
                // Снимок памяти — только при долгом зависании (20+ с): сам снимок на несколько секунд останавливает программу,
                // и при коротких подвисаниях он лишь добавлял ещё одно «не отвечает»
                if (hung && !dumped && silent > 20000)
                {
                    dumped = true;
                    Settings.Log(new Exception($"Окно не отвечает уже {silent / 1000} с — сторож снимает снимок для поиска причины (hang.dmp)"));
                    Watchdog.RequestHangDump();
                }
                else if (hung && silent < 2000)
                {
                    hung = false;
                    Settings.Log(new Exception("Окно снова отвечает"));
                }
            }
        }
    }

    /// <summary>
    /// Переживаем сбои и «встряски» Windows: выход из сна, блокировку экрана, смену мониторов и разрешения,
    /// перезапуск Проводника (его ловит сам док по сообщению TaskbarCreated) и видеодрайвера (WM_DWMCOMPOSITIONCHANGED).
    /// После такого события док, трей, стекло, анимации и Dynamic Island восстанавливаются сами.
    /// Плюс ловим ошибки фоновых задач, чтобы они не роняли программу.
    /// </summary>
    internal static class Recovery
    {
        static Timer _soon;

        public static void Start()
        {
            // Ошибка в фоновой задаче, которую никто не дождался, — пишем в журнал, а не падаем
            TaskScheduler.UnobservedTaskException += (s, e) => { Settings.Log(e.Exception); e.SetObserved(); };

            try
            {
                SystemEvents.PowerModeChanged += (s, e) => { if (e.Mode == PowerModes.Resume) Later(3000); };
                SystemEvents.SessionSwitch += (s, e) =>
                {
                    if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.ConsoleConnect || e.Reason == SessionSwitchReason.RemoteConnect)
                        Later(1500);
                };
                SystemEvents.DisplaySettingsChanged += (s, e) => Later(2000);
            }
            catch (Exception e) { Settings.Log(e); }
        }

        /// <summary>Восстановить через паузу: Windows после сна и смены мониторов успокаивается не сразу. Повторные события склеиваются.</summary>
        static void Later(int ms)
        {
            _soon?.Dispose();
            _soon = new Timer(_ => { try { Shell.Recover(); } catch (Exception e) { Settings.Log(e); } }, null, ms, Timeout.Infinite);
        }
    }
}
