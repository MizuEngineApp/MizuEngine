// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;

namespace MizuEngine
{
    /// <summary>
    /// Прячет панель задач Windows, пока работает док, и возвращает её обратно.
    /// Исходная настройка сохраняется в settings.json, так что панель вернётся даже после вылета программы.
    /// </summary>
    internal static class Taskbar
    {
        /// <summary>Есть ли на мониторе наш док — тогда панель задач Windows второго монитора на нём прячем (ставит DockSet).</summary>
        public static Func<IntPtr, bool> DockOn;
        static readonly HashSet<IntPtr> _hiddenSecondary = new HashSet<IntPtr>();

        /// <summary>
        /// Панели задач на других мониторах (Shell_SecondaryTrayWnd): где стоит наш док — прячем,
        /// где дока нет — возвращаем (они остаются с автоскрытием, как и раньше).
        /// </summary>
        static void SyncSecondary()
        {
            IntPtr h = IntPtr.Zero;
            var alive = new HashSet<IntPtr>();
            while ((h = DockNative.FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
            {
                alive.Add(h);
                bool hide = DockOn?.Invoke(DockNative.MonitorFromWindow(h, 2)) == true;
                if (hide)
                {
                    // Панель не прячем совсем (тогда Windows не может открыть на этом мониторе меню «Пуск»),
                    // а даём ей пустую форму окна: она невидима и «прозрачна» для кликов, даже когда выезжает у нижнего края
                    _hiddenSecondary.Add(h);
                    if (!HasEmptyRegion(h)) SetRegion(h, true);
                    if (!Native.IsWindowVisible(h)) DockNative.ShowWindow(h, DockNative.SW_SHOWNA);
                }
                else if (_hiddenSecondary.Remove(h)) { SetRegion(h, false); DockNative.ShowWindow(h, DockNative.SW_SHOWNA); }
            }
            _hiddenSecondary.RemoveWhere(x => !alive.Contains(x));   // Explorer перезапустился — старых окон больше нет
        }

        static void ShowSecondary()
        {
            foreach (var h in _hiddenSecondary) if (DockNative.IsWindow(h)) { SetRegion(h, false); DockNative.ShowWindow(h, DockNative.SW_SHOWNA); }
            _hiddenSecondary.Clear();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetWindowRgn(IntPtr h, IntPtr rgn);

        /// <summary>Форма окна уже пустая (Проводник мог её сбросить, перерисовав панель).</summary>
        static bool HasEmptyRegion(IntPtr h)
        {
            IntPtr tmp = DockNative.CreateRectRgn(0, 0, 0, 0);
            try { return GetWindowRgn(h, tmp) == 1; }   // NULLREGION
            finally { DockNative.DeleteObject(tmp); }
        }

        static void SetRegion(IntPtr h, bool empty)
        {
            try { DockNative.SetWindowRgn(h, empty ? DockNative.CreateRectRgn(0, 0, 0, 0) : IntPtr.Zero, true); } catch { }
        }

        /// <summary>
        /// Аварийно вернуть все панели задач (MizuEngine упал, его перезапускает «сторож»):
        /// главную и на других мониторах — с нормальной формой окна.
        /// </summary>
        public static void RestoreAll(Settings s)
        {
            try
            {
                IntPtr h = IntPtr.Zero;
                while ((h = DockNative.FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
                {
                    SetRegion(h, false);
                    DockNative.ShowWindow(h, DockNative.SW_SHOWNA);
                }
                _hiddenSecondary.Clear();
                if (s.SavedTaskbarState >= 0) Restore(s);
                else
                {
                    IntPtr tray = DockNative.ExplorerTray();
                    if (tray != IntPtr.Zero && !Native.IsWindowVisible(tray)) DockNative.ShowWindow(tray, 5);
                }
            }
            catch { }
        }

        public static void Hide(Settings s)
        {
            SyncSecondary();
            IntPtr tray = DockNative.ExplorerTray();
            if (tray == IntPtr.Zero) return;

            var abd = DockNative.NewAppBar(tray);
            if (s.SavedTaskbarState < 0)
            {
                s.SavedTaskbarState = (int)DockNative.SHAppBarMessage(DockNative.ABM_GETSTATE, ref abd).ToUInt32();
                s.Save();
            }
            // Автоскрытие освобождает место внизу экрана; на втором мониторе панель останется, но будет выезжать по наведению
            abd.lParam = new IntPtr(DockNative.ABS_AUTOHIDE);
            DockNative.SHAppBarMessage(DockNative.ABM_SETSTATE, ref abd);
            DockNative.ShowWindow(tray, DockNative.SW_HIDE);
        }

        static bool _peek;
        static DateTime _awaySince = DateTime.MaxValue;

        /// <summary>Кнопка трея в доке: на время показываем настоящую панель Windows (она выезжает снизу со значками трея).</summary>
        public static void Peek()
        {
            IntPtr tray = DockNative.ExplorerTray();
            if (tray == IntPtr.Zero) return;
            _peek = true;
            _awaySince = DateTime.MaxValue;
            DockNative.ShowWindow(tray, 5);   // SW_SHOW
            // Панель с автоскрытием выезжает, когда получает фокус
            if (!DockNative.SetForegroundWindow(tray)) DockNative.PressKeys(DockNative.VK_LWIN, (byte)'T');
        }

        /// <summary>Windows иногда сама показывает панель (например, после «Пуска») — прячем снова. После «подглядывания» — когда мышь ушла.</summary>
        public static void EnsureHidden()
        {
            SyncSecondary();
            IntPtr tray = DockNative.ExplorerTray();
            if (tray == IntPtr.Zero) return;

            if (_peek)
            {
                bool nearBottom = Native.GetCursorPos(out var p) && p.Y >= Native.GetSystemMetrics(1) - 90;
                bool focused = Native.GetForegroundWindow() == tray;
                if (nearBottom || focused) { _awaySince = DateTime.MaxValue; return; }
                if (_awaySince == DateTime.MaxValue) { _awaySince = DateTime.Now; return; }
                if ((DateTime.Now - _awaySince).TotalSeconds < 1.2) return;
                _peek = false;
            }
            if (Native.IsWindowVisible(tray)) DockNative.ShowWindow(tray, DockNative.SW_HIDE);
        }

        public static void Restore(Settings s)
        {
            _peek = false;
            ShowSecondary();
            IntPtr tray = DockNative.ExplorerTray();
            if (tray != IntPtr.Zero)
            {
                var abd = DockNative.NewAppBar(tray);
                int state = s.SavedTaskbarState >= 0 ? s.SavedTaskbarState : DockNative.ABS_ALWAYSONTOP;
                abd.lParam = new IntPtr(state);
                DockNative.SHAppBarMessage(DockNative.ABM_SETSTATE, ref abd);
                DockNative.ShowWindow(tray, 5);   // SW_SHOW
                Kick(tray, state);
            }
            if (s.SavedTaskbarState >= 0)
            {
                s.SavedTaskbarState = -1;
                s.Save();
            }
        }

        /// <summary>
        /// Панель Windows 11 после скрытия иногда возвращается пустой (место есть, а значков нет).
        /// Перерисовываем её и «передёргиваем» автоскрытие — Windows заново выстраивает панель.
        /// </summary>
        static void Kick(IntPtr tray, int state)
        {
            DockNative.RedrawWindow(tray, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0100 | 0x0400);   // INVALIDATE|ERASE|ALLCHILDREN|UPDATENOW|FRAME
            if ((state & DockNative.ABS_AUTOHIDE) != 0) return;   // у пользователя и так было автоскрытие
            var abd = DockNative.NewAppBar(tray);
            abd.lParam = new IntPtr(state | DockNative.ABS_AUTOHIDE);
            DockNative.SHAppBarMessage(DockNative.ABM_SETSTATE, ref abd);
            System.Threading.Thread.Sleep(120);
            abd.lParam = new IntPtr(state);
            DockNative.SHAppBarMessage(DockNative.ABM_SETSTATE, ref abd);
            DockNative.ShowWindow(tray, 5);
        }
    }
}
