// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

        /// <summary>Основной процесс: запустить сторожа.</summary>
        public static void Start()
        {
            try
            {
                try { File.Delete(Marker); } catch { }
                Process.Start(new ProcessStartInfo(Environment.ProcessPath, "--watchdog " + Environment.ProcessId)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(Environment.ProcessPath)
                });
            }
            catch (Exception e) { Settings.Log(e); }
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
