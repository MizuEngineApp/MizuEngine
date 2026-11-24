// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Runtime.InteropServices;
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
        /// <summary>Все доки (на главном мониторе и, если включено, на остальных).</summary>
        public static DockSet Dock { get; private set; }
        public static WindowAnimator Anim { get; private set; }
        public static IslandWindow Island { get; private set; }

        /// <summary>Открыть главное окно MizuEngine (его ставит Program; вызывается из любого потока).</summary>
        public static Action OpenApp;

        /// <summary>Открыть/закрыть Launchpad (его ставит Program; вызывается из любого потока).</summary>
        public static Action OpenLaunchpad;

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
                    Dock = new DockSet(s);
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
                StartHealth();
                Dispatcher.Run();
            })
            { IsBackground = true, Name = "MizuEngine shell" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait();
        }

        public static void Run(Action a) => D?.BeginInvoke(a);

        // ================== самопроверка: анимации не должны пропадать ==================

        static DispatcherTimer _health;
        static int _healthTicks;

        /// <summary>
        /// Раз в 4 секунды проверяем, что анимации сворачивания на месте: перехват сворачивания включён,
        /// системная анимация Windows выключена (её мог вернуть другой экземпляр программы или сама Windows),
        /// окно анимации не «залипло». Раз в 2 минуты перехват переподключаем заново — после сна, блокировки
        /// или перезапуска Проводника Windows иногда тихо перестаёт присылать события.
        /// </summary>
        static void StartHealth()
        {
            _health = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(4) };
            _health.Tick += (o, e) =>
            {
                try
                {
                    bool want = (Dock != null && Dock.Enabled && S.DockAnimations) || (Island != null && Island.FliesUp);
                    if (!want) return;
                    _healthTicks++;
                    if (_hook == IntPtr.Zero || _healthTicks % 30 == 0) { StopWatcher(); StartWatcher(); }
                    SysAnim.Ensure(S);
                    Anim?.Unstick();
                }
                catch (Exception ex) { Settings.Log(ex); }
            };
            _health.Start();
        }

        /// <summary>Восстановление после сбоев Windows (сон, блокировка, перезапуск Проводника или видеодрайвера).</summary>
        public static void Recover()
        {
            if (D == null) return;
            if (!D.CheckAccess()) { D.BeginInvoke(new Action(Recover)); return; }
            try
            {
                Dock?.Recover();
                Anim?.Recover();
                Island?.Apply();
                StopWatcher();
                SyncAnimations();
                SysAnim.Ensure(S);
                RaiseChrome();
            }
            catch (Exception e) { Settings.Log(e); }
        }

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
        /// остальное — в свой значок дока. Dynamic Island есть только на главном мониторе, поэтому окна
        /// с других мониторов всегда уходят вниз, в док своего монитора. Empty — анимировать некуда.
        /// </summary>
        public static (Rect rect, bool up) FlyTarget(IntPtr hwnd, Rect dockIcon)
        {
            if (Island != null && Island.FliesUp && Island.IsIslandApp(hwnd) && Monitors.OnPrimary(hwnd))
            {
                var r = Island.ScreenRect();
                if (!r.IsEmpty) return (r, true);
            }
            // Значка окна в доке нет (окно только что открылось, док на миг перестраивался) — летим в середину дока,
            // чтобы анимация не пропадала
            if (dockIcon.IsEmpty && Dock != null && Dock.Enabled && S.DockAnimations) dockIcon = Dock.FallbackIcon(hwnd);
            return (dockIcon, false);
        }

        /// <summary>
        /// Свернуть окно с «джинном», не дожидаясь Windows (своя кнопка «_» у окна MizuEngine): копия окна появляется,
        /// и только потом окно сворачивается. false — оболочки нет, сворачивай обычным способом.
        /// </summary>
        public static bool MinimizeAnimated(IntPtr hwnd)
        {
            if (D == null || Anim == null || hwnd == IntPtr.Zero) return false;
            D.BeginInvoke(new Action(() =>
            {
                try
                {
                    bool want = (Dock != null && Dock.Enabled && S.DockAnimations) || (Island != null && Island.FliesUp);
                    Rect dockIcon = Dock != null && Dock.Enabled && S.DockAnimations ? Dock.IconFor(hwnd) : Rect.Empty;
                    (Rect rect, bool up) target = want ? FlyTarget(hwnd, dockIcon) : (Rect.Empty, false);
                    if (target.rect.IsEmpty) { WindowAnimator.SetMinimized(hwnd, true); return; }
                    Anim.Minimize(hwnd, target.rect, target.up, alreadyMinimized: false);
                }
                catch (Exception e) { Settings.Log(e); WindowAnimator.SetMinimized(hwnd, true); }
            }));
            return true;
        }

        static void StartWatcher()
        {
            StartMouse();
            if (_hook != IntPtr.Zero) return;
            if (_proc == null) _proc = OnWinEvent;
            _hook = DockNative.SetWinEventHook(DockNative.EVENT_SYSTEM_MINIMIZESTART, DockNative.EVENT_SYSTEM_MINIMIZESTART, IntPtr.Zero, _proc, 0, 0, 0);
        }

        static void StopWatcher()
        {
            StopMouse();
            if (_hook == IntPtr.Zero) return;
            DockNative.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }

        // ================== кнопка «_»: заранее готовим «джинна» (без вспышки и задержки) ==================

        delegate IntPtr LowLevelMouseProc(int code, IntPtr w, IntPtr l);
        [StructLayout(LayoutKind.Sequential)] struct MSLLHOOKSTRUCT { public Native.POINT pt; public uint mouseData, flags, time; public IntPtr extra; }
        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, LowLevelMouseProc fn, IntPtr mod, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Native.POINT pt);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);

        static IntPtr _mouseHook;
        static LowLevelMouseProc _mouseProc;   // держим ссылку, иначе сборщик мусора удалит делегат
        static Native.POINT _downPt;

        static void StartMouse()
        {
            if (_mouseHook != IntPtr.Zero) return;
            _mouseProc ??= OnMouse;
            _mouseHook = SetWindowsHookEx(14, _mouseProc, GetModuleHandle(null), 0);   // WH_MOUSE_LL
        }

        static void StopMouse()
        {
            if (_mouseHook == IntPtr.Zero) return;
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        /// <summary>
        /// Следим только за левой кнопкой. Нажали — проверяем (в очереди потока, не задерживая мышь), не на «_» ли,
        /// и если да — готовим копию окна. Отпустили там же — показываем копию поверх окна, раньше, чем окно свернётся.
        /// </summary>
        static IntPtr OnMouse(int code, IntPtr w, IntPtr l)
        {
            if (code >= 0)
            {
                try
                {
                    int msg = w.ToInt32();
                    if (msg == 0x0201)   // WM_LBUTTONDOWN
                    {
                        var m = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(l);
                        _downPt = m.pt;
                        D?.BeginInvoke(DispatcherPriority.Send, new Action(() => PrepareAt(m.pt)));
                    }
                    else if (msg == 0x0202 && Anim != null && Anim.Prepared != IntPtr.Zero)   // WM_LBUTTONUP
                    {
                        var m = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(l);
                        if (Math.Abs(m.pt.X - _downPt.X) <= 6 && Math.Abs(m.pt.Y - _downPt.Y) <= 6) Anim.Reveal();
                    }
                }
                catch (Exception e) { Settings.Log(e); }
            }
            return CallNextHookEx(IntPtr.Zero, code, w, l);
        }

        static void PrepareAt(Native.POINT pt)
        {
            try
            {
                if (Anim == null) return;
                IntPtr h = WindowFromPoint(pt);
                if (h == IntPtr.Zero) return;
                h = DockNative.GetAncestor(h, 2);   // GA_ROOT
                if (h == IntPtr.Zero || Native.IsIconic(h) || !Native.IsWindowVisible(h)) return;
                long lp = ((long)(pt.Y & 0xFFFF) << 16) | (uint)(pt.X & 0xFFFF);
                // Спрашиваем само окно, что под курсором; зависшее окно не ждём (SMTO_ABORTIFHUNG, 60 мс)
                if (DockNative.SendMessageTimeout(h, 0x0084, IntPtr.Zero, new IntPtr(lp), 0x0002, 60, out IntPtr hit) == IntPtr.Zero) return;
                if (hit.ToInt64() != 8) return;   // HTMINBUTTON — кнопка «_»
                Rect dockIcon = Dock != null && Dock.Enabled && S.DockAnimations ? Dock.IconFor(h) : Rect.Empty;
                var target = FlyTarget(h, dockIcon);
                if (target.rect.IsEmpty) return;
                Anim.Prepare(h, target.rect, target.up);
            }
            catch (Exception e) { Settings.Log(e); }
        }

        /// <summary>Окно свернули кнопкой «_», сочетанием клавиш или из самой программы — тоже показываем «джинна».</summary>
        static void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            try
            {
                if (ev != DockNative.EVENT_SYSTEM_MINIMIZESTART || idObject != 0 || hwnd == IntPtr.Zero || Anim == null) return;
                // Свернули кнопкой «_», а копия окна уже готова — «джинн» сразу, без вспышки и паузы
                if (Anim.Go(hwnd)) { _lastMinimize = DateTime.UtcNow; return; }
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
