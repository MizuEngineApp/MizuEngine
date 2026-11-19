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
        public static void Hide(Settings s)
        {
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
