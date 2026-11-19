// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>
    /// Поток «оболочки»: док, анимация сворачивания окон и Dynamic Island.
    /// Раньше док жил в одном потоке со снегом — тяжёлый кадр эффектов задерживал док (отсюда подлагивания).
    /// Теперь у дока свой поток, и эффекты, и главное окно ему не мешают.
    /// </summary>
    internal static class Shell
    {
        public static Dispatcher D { get; private set; }
        public static Settings S { get; private set; }
        public static DockWindow Dock { get; private set; }
        public static WindowAnimator Anim { get; private set; }
        public static IslandWindow Island { get; private set; }

        /// <summary>Открыть главное окно MizuEngine (его ставит Program; вызывается из любого потока).</summary>
        public static Action OpenApp;

        static IntPtr _hook;
        static DockNative.WinEventProc _proc;   // делегат держим, иначе сборщик мусора удалит его, а Windows продолжит вызывать
        static DateTime _lastMinimize;

        public static void Start(Settings s)
        {
            S = s;
            var ready = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                D = Dispatcher.CurrentDispatcher;
                D.UnhandledException += (o, e) => { Settings.Log(e.Exception); e.Handled = true; };   // ошибка не роняет программу, а пишется в error.log
                try
                {
                    Anim = new WindowAnimator();
                    Anim.Show();   // показано всегда, но с пустой формой: не видно и не мешает кликам (без мигания при каждом запуске)
                    Dock = new DockWindow(s);
                    Island = new IslandWindow(s);
                    // Окно втянулось в Dynamic Island — его значок ждёт в капсуле; развернули — убираем
                    Anim.Finished += (hwnd, minimized, up) =>
                    {
                        if (minimized && up) Island.Park(hwnd);
                        else if (!minimized) Island.Unpark(hwnd);
                    };
                }
                catch (Exception e) { Settings.Log(e); }
                ready.Set();
                try
                {
                    Dock?.ApplySettings();
                    if (s.DockEnabled) Dock?.SetEnabled(true);
                    Island?.Apply();
                    SyncAnimations();
                }
                catch (Exception e) { Settings.Log(e); }
                Dispatcher.Run();
            })
            { IsBackground = true, Name = "MizuEngine shell" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait();
        }

        public static void Run(Action a) => D?.BeginInvoke(a);

        /// <summary>Док и Dynamic Island — выше окна анимации: воронка проходит под ними и «ныряет» в значок.</summary>
        public static void RaiseChrome()
        {
            Dock?.RaiseAbove();
            Island?.RaiseAbove();
        }

        /// <summary>Выход: вернуть панель задач и системную анимацию, остановить трей и поток.</summary>
        public static void Shutdown()
        {
            try
            {
                D?.Invoke(new Action(() =>
                {
                    try { Dock?.Shutdown(); } catch (Exception e) { Settings.Log(e); }
                    try { Island?.Shutdown(); } catch (Exception e) { Settings.Log(e); }
                    StopWatcher();
                    SysAnim.Restore(S);
                }), TimeSpan.FromSeconds(3));
            }
            catch { }
            TrayHost.Stop();
            try { D?.InvokeShutdown(); } catch { }
        }

        // ================== куда сворачиваются окна ==================

        /// <summary>
        /// Своя анимация нужна, когда включены док с анимацией или Dynamic Island с «улётом вверх».
        /// Тогда системная анимация Windows выключается, а мы ловим сворачивание окон во всех программах.
        /// </summary>
        public static void SyncAnimations()
        {
            if (D == null) return;
            if (!D.CheckAccess()) { D.BeginInvoke(new Action(SyncAnimations)); return; }
            bool dockAnim = Dock != null && Dock.Enabled && S.DockAnimations;
            bool islandAnim = Island != null && Island.FliesUp;
            if (dockAnim || islandAnim) { SysAnim.Disable(S); StartWatcher(); }
            else { StopWatcher(); SysAnim.Restore(S); }
        }

        /// <summary>
        /// Куда летит окно: музыка, таймеры и сам MizuEngine — вверх, в Dynamic Island (если так настроено),
        /// остальное — в свой значок дока. Empty — анимировать некуда.
        /// </summary>
        public static (Rect rect, bool up) FlyTarget(IntPtr hwnd, Rect dockIcon)
        {
            if (Island != null && Island.FliesUp && Island.IsIslandApp(hwnd))
            {
                var r = Island.ScreenRect();
                if (!r.IsEmpty) return (r, true);
            }
            return (dockIcon, false);
        }

        static void StartWatcher()
        {
            if (_hook != IntPtr.Zero) return;
            if (_proc == null) _proc = OnWinEvent;
            _hook = DockNative.SetWinEventHook(DockNative.EVENT_SYSTEM_MINIMIZESTART, DockNative.EVENT_SYSTEM_MINIMIZESTART, IntPtr.Zero, _proc, 0, 0, 0);
        }

        static void StopWatcher()
        {
            if (_hook == IntPtr.Zero) return;
            DockNative.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }

        /// <summary>Окно свернули кнопкой «_», сочетанием клавиш или из самой программы — тоже показываем «джинна».</summary>
        static void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            try
            {
                if (ev != DockNative.EVENT_SYSTEM_MINIMIZESTART || idObject != 0 || hwnd == IntPtr.Zero || Anim == null) return;
                if (hwnd == Anim.Current) return;   // это сворачиваем мы сами (клик по доку)
                // «Свернуть все окна» (Win+M) — анимируем только первое, остальные просто сворачиваются
                var now = DateTime.UtcNow;
                bool burst = (now - _lastMinimize).TotalMilliseconds < 150;
                _lastMinimize = now;
                if (burst) return;
                if (DockNative.GetAncestor(hwnd, 2) != hwnd) return;   // GA_ROOT: только окна верхнего уровня

                Rect dockIcon = Dock != null && Dock.Enabled && S.DockAnimations ? Dock.IconFor(hwnd) : Rect.Empty;
                var target = FlyTarget(hwnd, dockIcon);
                if (target.rect.IsEmpty) return;
                Anim.Minimize(hwnd, target.rect, target.up, alreadyMinimized: true);
            }
            catch (Exception e) { Settings.Log(e); }
        }
    }
}
