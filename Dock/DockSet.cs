// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>
    /// Все доки сразу: один на главном мониторе и (если включено «Док на всех мониторах») по одному на каждом другом.
    /// Закреплённые значки — в каждом доке, открытые приложения — в доке того монитора, где их окно.
    /// Окна с монитора без своего дока попадают в главный док.
    /// Панель задач Windows, трей и анимации — общие, ими управляет этот класс, а не отдельные доки.
    /// Живёт в потоке Shell; методы сами переходят в свой поток, звать можно откуда угодно.
    /// </summary>
    internal sealed class DockSet
    {
        readonly Settings _s;
        readonly List<DockWindow> _docks = new List<DockWindow>();
        readonly Dispatcher _d;
        readonly DispatcherTimer _display;
        bool _enabled;

        public DockSet(Settings s)
        {
            _s = s;
            _d = Dispatcher.CurrentDispatcher;
            // Мониторы подключили/отключили/поменяли разрешение — пересобираем доки (с паузой: Windows шлёт несколько событий подряд)
            _display = new DispatcherTimer(DispatcherPriority.Normal, _d) { Interval = TimeSpan.FromMilliseconds(900) };
            _display.Tick += (o, e) => { _display.Stop(); if (_enabled) { Sync(); ApplyShared(); } };
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (o, e) => _d.BeginInvoke(new Action(() => { _display.Stop(); _display.Start(); }));
            Taskbar.DockOn = HasDockOn;
        }

        public bool Enabled => _enabled;

        /// <summary>Док главного монитора.</summary>
        public DockWindow Primary => _docks.Find(x => x.IsPrimary) ?? (_docks.Count > 0 ? _docks[0] : null);

        // ================== включение / выключение ==================

        public void SetEnabled(bool on)
        {
            if (!_d.CheckAccess()) { _d.BeginInvoke(new Action(() => SetEnabled(on))); return; }
            if (on == _enabled) { if (on) ApplySettings(); return; }
            _enabled = on;
            if (on)
            {
                if (_s.DockPins == null)
                {
                    _s.DockPins = DockApps.ImportWindowsPins();   // первый запуск: берём закреплённое на панели задач Windows
                    _s.Save();
                }
                // Сначала прячем панель задач Windows, потом резервируем место под доки. Наоборот (как было в 0.11.0)
                // место внизу на миг отнимали оба сразу — и Windows пересаживал нижние ярлыки рабочего стола на другой монитор
                DesktopIcons.Snapshot();
                Taskbar.Hide(_s);
                Sync();
                ApplyShared();
                DesktopIcons.RestoreLater();
            }
            else
            {
                DesktopIcons.Snapshot();
                foreach (var d in _docks) d.SetEnabled(false);
                TrayHost.Stop();
                Taskbar.Restore(_s);
                DesktopIcons.RestoreLater();
            }
            Shell.SyncAnimations();
        }

        /// <summary>
        /// Восстановление после сбоя Windows: Проводник перезапустился, вышли из сна, разблокировали ПК, сменились мониторы.
        /// Панель задач прячется снова, доки заново резервируют место, трей переподключается.
        /// </summary>
        public void Recover()
        {
            if (!_d.CheckAccess()) { _d.BeginInvoke(new Action(Recover)); return; }
            if (!_enabled) return;
            try
            {
                DesktopIcons.Snapshot();
                Taskbar.Hide(_s);
                Sync();
                foreach (var d in _docks) d.Recover();
                if (_s.DockClock && _s.DockTray) { TrayHost.Stop(); TrayHost.Start(); }
                Taskbar.EnsureHidden();
                DesktopIcons.RestoreLater();
            }
            catch (Exception e) { Settings.Log(e); }
        }

        /// <summary>Запасная цель «джинна»: середина дока монитора окна (если значка этого окна в доке нет).</summary>
        public Rect FallbackIcon(IntPtr hwnd)
        {
            if (!_d.CheckAccess()) return (Rect)_d.Invoke(new Func<Rect>(() => FallbackIcon(hwnd)));
            return OwnerOf(hwnd)?.PillOnScreen() ?? Rect.Empty;
        }

        public void ApplySettings()
        {
            if (!_d.CheckAccess()) { _d.BeginInvoke(new Action(ApplySettings)); return; }
            if (_enabled) Sync();          // включили/выключили «Док на всех мониторах» или отдельный монитор
            foreach (var d in _docks) d.ApplySettings();
            if (_enabled) ApplyShared();
            Shell.SyncAnimations();
        }

        /// <summary>Общее для всех доков: трей внутри дока и прятанье панелей задач Windows.</summary>
        void ApplyShared()
        {
            if (_s.DockClock && _s.DockTray) TrayHost.Start(); else TrayHost.Stop();
            Taskbar.EnsureHidden();
        }

        /// <summary>Выход из программы: вернуть панель задач, но не менять сохранённую настройку дока.</summary>
        public void Shutdown()
        {
            if (!_d.CheckAccess())
            {
                try { _d.Invoke(new Action(Shutdown), TimeSpan.FromSeconds(2)); } catch { }
                return;
            }
            if (_enabled) SetEnabled(false);
        }

        // ================== какие мониторы получают док ==================

        /// <summary>Создаёт доки для нужных мониторов и убирает лишние (монитор отключили или его выключили в настройках).</summary>
        void Sync()
        {
            List<MonitorInfo> mons;
            try { mons = Monitors.All(); } catch (Exception e) { Settings.Log(e); mons = new List<MonitorInfo>(); }

            var want = new List<MonitorInfo>();
            foreach (var m in mons)
                if (m.Primary || (_s.DockAllMonitors && !(_s.DockMonitorsOff?.Contains(m.Id) ?? false))) want.Add(m);
            if (want.Count == 0 && mons.Count > 0) want.Add(mons[0]);

            // Док появится или пропадёт — запоминаем ярлыки рабочего стола, чтобы вернуть их, если Windows их сдвинет
            bool change = want.Count != _docks.Count || !want.TrueForAll(m => _docks.Exists(d => d.Mon != null && d.Mon.Device == m.Device));
            bool watch = change && _docks.Count > 0;
            if (watch) DesktopIcons.Snapshot();
            SyncCore(want);
            if (watch) DesktopIcons.RestoreLater();
        }

        void SyncCore(List<MonitorInfo> want)
        {
            if (want.Count == 0)
            {
                // Мониторов не нашлось (редкий сбой WinAPI) — один док, как раньше, по размеру главного экрана
                if (_docks.Count == 0) Add(null);
                else foreach (var d in _docks) if (!d.Enabled) d.SetEnabled(true);
                return;
            }

            for (int i = _docks.Count - 1; i >= 0; i--)
            {
                var d = _docks[i];
                var m = d.Mon == null ? null : want.Find(x => x.Device == d.Mon.Device);
                if (m == null)
                {
                    d.SetEnabled(false);
                    d.CloseAll();
                    _docks.RemoveAt(i);
                }
                else
                {
                    d.SetMonitor(m);
                    if (!d.Enabled) d.SetEnabled(true);
                }
            }
            foreach (var m in want)
                if (!_docks.Exists(x => x.Mon != null && x.Mon.Device == m.Device)) Add(m);

            _docks.Sort((a, b) => a.IsPrimary != b.IsPrimary ? (a.IsPrimary ? -1 : 1) : (a.Mon?.Bounds.Left ?? 0).CompareTo(b.Mon?.Bounds.Left ?? 0));
        }

        void Add(MonitorInfo m)
        {
            var d = new DockWindow(_s, this);
            if (m != null) d.SetMonitor(m);
            _docks.Add(d);
            d.SetEnabled(true);
        }

        // ================== окна и значки ==================

        /// <summary>В каком доке живёт значок окна: в доке его монитора, а если там дока нет — в главном.</summary>
        public DockWindow OwnerOf(IntPtr hwnd)
        {
            if (!_enabled || _docks.Count == 0) return null;
            if (_docks.Count == 1) return _docks[0];
            IntPtr mon = Monitors.Of(hwnd);
            return _docks.Find(x => x.Enabled && x.Mon != null && x.Mon.Handle == mon) ?? Primary;
        }

        /// <summary>Значок окна в пикселях экрана (куда сворачивается окно) — в доке его монитора. Empty — значка нет.</summary>
        public Rect IconFor(IntPtr hwnd)
        {
            if (!_d.CheckAccess()) return (Rect)_d.Invoke(new Func<Rect>(() => IconFor(hwnd)));
            return OwnerOf(hwnd)?.IconFor(hwnd) ?? Rect.Empty;
        }

        /// <summary>Поднять все доки (и стекло под ними) над окнами «поверх всех».</summary>
        public void RaiseAbove()
        {
            foreach (var d in _docks) d.RaiseAbove();
        }

        /// <summary>Закрепили/открепили значок — обновить все доки (закреплённые есть в каждом).</summary>
        public void RefreshAll()
        {
            foreach (var d in _docks) d.RefreshNow();
        }

        /// <summary>Есть ли наш док на мониторе — тогда панель задач Windows на нём прячем.</summary>
        bool HasDockOn(IntPtr hmon) => _enabled && _docks.Exists(x => x.Enabled && x.Mon != null && x.Mon.Handle == hmon);
    }
}
