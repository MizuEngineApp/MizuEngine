// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>Окно под доком, которое размывает всё, что за ним (эффект стекла). Форма окна = форма дока.</summary>
    internal sealed class DockBlur : Window
    {
        IntPtr _h;
        Rect _pill;
        double _radius;
        Native.RECT _px;          // последняя форма в пикселях — менять её, только если она правда изменилась
        int _tint = 0x40201410;

        public DockBlur()
        {
            Glass.Setup(this, clickThrough: false);
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            Title = "MizuEngine Dock Blur";

            SourceInitialized += (s, e) =>
            {
                _h = new WindowInteropHelper(this).Handle;
                DockNative.EnableAcrylic(_h, _tint);
                ApplyRegion(true);
            };
        }

        public IntPtr Handle => _h;

        /// <summary>Чем прозрачнее док, тем слабее тёмный оттенок стекла.</summary>
        public void SetTransparency(int t)
        {
            double g = 1 - Math.Clamp(t, 0, 100) / 100.0;
            _tint = ((int)(0x08 + 0x70 * g) << 24) | 0x201410;   // формат 0xAABBGGRR
            if (_h != IntPtr.Zero) DockNative.EnableAcrylic(_h, _tint);
        }

        public void SetPill(Rect pill, double radius)
        {
            _pill = pill;
            _radius = radius;
            ApplyRegion(false);
        }

        void ApplyRegion(bool force)
        {
            if (_h == IntPtr.Zero) return;
            var px = DockWindow.ToPixels(this, _pill);
            if (!force && px.Left == _px.Left && px.Top == _px.Top && px.Right == _px.Right && px.Bottom == _px.Bottom) return;
            _px = px;
            DockNative.SetWindowRgn(_h, DockWindow.RoundRegion(this, _pill, _radius), true);
        }
    }

    /// <summary>
    /// Док вместо панели задач: «Пуск», закреплённые и открытые приложения, трей, сеть/звук и часы.
    /// Работает как панель задач Windows: порядок значков не прыгает, точка под открытыми приложениями,
    /// меню по правой кнопке, значки можно перетаскивать. А выглядит и анимируется как док macOS.
    /// Живёт в своём потоке (Shell): эффекты рабочего стола и главное окно его не тормозят.
    /// </summary>
    internal sealed class DockWindow : Window
    {
        readonly Settings _s;
        readonly DockView _view = new DockView();
        readonly DockBlur _blur = new DockBlur();
        readonly DispatcherTimer _timer, _soon;
        readonly WindowTracker _fullscreen = new WindowTracker();
        TrayPopup _trayPopup;

        IntPtr _hwnd;
        bool _enabled, _appbar, _fsHidden, _shellHook;
        uint _abMsg, _shellMsg;
        Native.RECT _rgnPx;
        bool _rgnPill;
        string _minuteShown;
        readonly List<string> _runOrder = new List<string>();                 // открытые (не закреплённые) приложения — в порядке появления
        readonly Dictionary<IntPtr, long> _seen = new Dictionary<IntPtr, long>(); // окна — в порядке появления (для переключения кликами)
        readonly HashSet<IntPtr> _attention = new HashSet<IntPtr>();          // окна, которые мигают (просят внимания)
        long _seenCounter;

        static bool Win11 => Environment.OSVersion.Version.Build >= 22000;

        public DockWindow(Settings settings)
        {
            _s = settings;
            Glass.Setup(this, clickThrough: false);
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            Title = "MizuEngine Dock";
            Content = _view;

            _view.ItemClicked += Activate;
            _view.ItemRightClicked += ShowMenu;
            _view.ItemDropped += OnDropped;
            _view.ShapeChanged += UpdateShape;

            SourceInitialized += (o, e) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                HwndSource.FromHwnd(_hwnd).AddHook(WndProc);
                _shellMsg = DockNative.RegisterWindowMessage("SHELLHOOK");
                UpdateShape();
            };

            // Окна открываются и закрываются — Windows сам сообщает (shell hook), а таймер раз в 1.5 с — страховка
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _timer.Tick += (o, e) => Refresh();
            _soon = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
            _soon.Tick += (o, e) => { _soon.Stop(); Refresh(); };
            // Язык меняют в главном окне (другой поток) — обновляемся в своём
            Loc.Changed += () => Dispatcher.BeginInvoke(new Action(() => { if (_enabled) Refresh(); }));
            TrayHost.Changed += () => Dispatcher.BeginInvoke(new Action(UpdateTrayCount));
        }

        // ================== включение / выключение (можно звать из любого потока) ==================

        public void SetEnabled(bool on)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => SetEnabled(on))); return; }
            if (on == _enabled) { if (on) ApplySettings(); return; }
            _enabled = on;
            if (on)
            {
                if (_s.DockPins == null)
                {
                    _s.DockPins = DockApps.ImportWindowsPins();   // первый запуск: берём закреплённое на панели задач Windows
                    _s.Save();
                }
                _view.Items.Clear();
                Place();
                Show();
                Taskbar.Hide(_s);
                if (_hwnd != IntPtr.Zero && !_shellHook) _shellHook = DockNative.RegisterShellHookWindow(_hwnd);
                ApplySettings();   // стекло, место внизу экрана и значки
                _timer.Start();
            }
            else
            {
                _timer.Stop();
                _soon.Stop();
                _trayPopup?.Dismiss();
                if (_shellHook) { DockNative.DeregisterShellHookWindow(_hwnd); _shellHook = false; }
                TrayHost.Stop();
                UnregisterAppBar();
                Taskbar.Restore(_s);
                _blur.Hide();
                Hide();
            }
            Shell.SyncAnimations();
        }

        /// <summary>Вызывается при выходе из программы: возвращает панель задач, но не меняет сохранённую настройку дока.</summary>
        public void Shutdown()
        {
            if (!Dispatcher.CheckAccess())
            {
                try { Dispatcher.Invoke(new Action(Shutdown), TimeSpan.FromSeconds(2)); } catch { }
                return;
            }
            if (_enabled) SetEnabled(false);
        }

        public bool Enabled => _enabled;

        /// <summary>Поднять док (и стекло под ним) над всеми «поверх всех» окнами.</summary>
        public void RaiseAbove()
        {
            if (!_enabled || _fsHidden || _hwnd == IntPtr.Zero) return;
            const uint f = DockNative.SWP_NOMOVE | DockNative.SWP_NOSIZE | DockNative.SWP_NOACTIVATE;
            if (_s.DockBlur && _blur.Handle != IntPtr.Zero) DockNative.SetWindowPos(_blur.Handle, new IntPtr(-1), 0, 0, 0, 0, f);
            DockNative.SetWindowPos(_hwnd, new IntPtr(-1), 0, 0, 0, 0, f);
        }

        public void ApplySettings()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(ApplySettings)); return; }
            _view.BaseSize = _s.DockSize;
            _view.Magnify = _s.DockMagnify;
            _view.Autohide = _s.DockAutohide;
            _view.SetTransparency(_s.DockTransparency);
            _blur.SetTransparency(_s.DockTransparency);
            Shell.SyncAnimations();
            if (!_enabled) return;

            Place();
            if (_s.DockBlur && !_fsHidden) { _blur.Show(); UpdateShape(); }
            else _blur.Hide();

            if (_s.DockAutohide) UnregisterAppBar();   // при автоскрытии окна занимают весь экран, как на Mac
            else if (!_appbar) RegisterAppBar();
            else SetAppBarPos();

            // Трей внутри дока: работает, пока включены док и системные значки
            if (_s.DockClock && _s.DockTray) TrayHost.Start(); else { TrayHost.Stop(); _trayPopup?.Dismiss(); }
            Refresh();
        }

        void Place()
        {
            double w = SystemParameters.PrimaryScreenWidth, h = SystemParameters.PrimaryScreenHeight;
            double winH = _s.DockSize * _s.DockMagnify + 2 * DockView.Pad + DockView.BottomMargin + DockView.LabelSpace + DockView.BounceSpace;
            Left = 0; Top = h - winH; Width = w; Height = winH;
            _blur.Left = 0; _blur.Top = h - winH; _blur.Width = w; _blur.Height = winH;
        }

        /// <summary>Подгоняет область кликов дока и стекло под текущую форму дока (только если форма правда изменилась).</summary>
        void UpdateShape()
        {
            if (_hwnd != IntPtr.Zero)
            {
                var px = ToPixels(this, _view.HitArea);
                var bpx = ToPixels(this, _view.Band);
                var lpx = ToPixels(this, _view.LabelBox);
                bool pill = _view.HitIsPill;
                if (!Same(px, _rgnPx) || !Same(bpx, _rgnBand) || !Same(lpx, _rgnLabel) || pill != _rgnPill)
                {
                    _rgnPx = px; _rgnBand = bpx; _rgnLabel = lpx;
                    _rgnPill = pill;
                    // Форма: док + торчащие значки + подпись. Над остальной полосой клики уходят окнам под доком
                    IntPtr rgn = RoundRegion(this, _view.HitArea, pill ? _view.PillRadius : 0);
                    foreach (var extra in new[] { _view.Band, _view.LabelBox })
                    {
                        if (extra.IsEmpty || extra.Width <= 0 || extra.Height <= 0) continue;
                        IntPtr add = RoundRegion(this, extra, 0);
                        CombineRgn(rgn, rgn, add, 2);   // RGN_OR
                        DockNative.DeleteObject(add);
                    }
                    DockNative.SetWindowRgn(_hwnd, rgn, false);   // окно и так перерисуется в этом кадре
                }
            }
            if (_s.DockBlur) _blur.SetPill(_view.Pill, _view.PillRadius);
        }

        Native.RECT _rgnBand, _rgnLabel;
        static bool Same(Native.RECT a, Native.RECT b) => a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern int CombineRgn(IntPtr dst, IntPtr a, IntPtr b, int mode);

        public static Native.RECT ToPixels(Visual v, Rect r)
        {
            if (r.IsEmpty) return default;
            var dpi = VisualTreeHelper.GetDpi(v);
            return new Native.RECT
            {
                Left = (int)Math.Floor(r.Left * dpi.DpiScaleX), Top = (int)Math.Floor(r.Top * dpi.DpiScaleY),
                Right = (int)Math.Ceiling(r.Right * dpi.DpiScaleX) + 1, Bottom = (int)Math.Ceiling(r.Bottom * dpi.DpiScaleY) + 1
            };
        }

        /// <summary>Скруглённый прямоугольник в пикселях экрана (Windows сам удалит регион, когда он станет не нужен).</summary>
        public static IntPtr RoundRegion(Visual v, Rect r, double radius)
        {
            if (r.IsEmpty || r.Width <= 0 || r.Height <= 0) return DockNative.CreateRectRgn(0, 0, 0, 0);
            var dpi = VisualTreeHelper.GetDpi(v);
            var px = ToPixels(v, r);
            return radius <= 0
                ? DockNative.CreateRectRgn(px.Left, px.Top, px.Right, px.Bottom)
                : DockNative.CreateRoundRectRgn(px.Left, px.Top, px.Right, px.Bottom, (int)(radius * 2 * dpi.DpiScaleX), (int)(radius * 2 * dpi.DpiScaleY));
        }

        // ================== содержимое ==================

        static string KeyOf(string k) => "app:" + (k ?? "").ToLowerInvariant();

        void Refresh()
        {
            if (!_enabled) return;

            // В полноэкранных играх и видео док прячется
            bool fs = _fullscreen.ForegroundIsFullscreen(_hwnd, Native.GetSystemMetrics(0), Native.GetSystemMetrics(1));
            if (fs != _fsHidden)
            {
                _fsHidden = fs;
                if (fs) { Hide(); _blur.Hide(); _trayPopup?.Dismiss(); }
                else { if (_s.DockBlur) _blur.Show(); Show(); }
            }
            Taskbar.EnsureHidden();
            if (_s.DockBlur && _blur.Handle != IntPtr.Zero && _hwnd != IntPtr.Zero)   // стекло всегда прямо под доком
                DockNative.SetWindowPos(_blur.Handle, _hwnd, 0, 0, 0, 0, DockNative.SWP_NOMOVE | DockNative.SWP_NOSIZE | DockNative.SWP_NOACTIVATE);

            var wins = DockApps.EnumRunning();
            foreach (var w in wins) if (!_seen.ContainsKey(w.Hwnd)) _seen[w.Hwnd] = ++_seenCounter;
            if (_seen.Count > wins.Count + 64)
            {
                var alive = new HashSet<IntPtr>(wins.Select(x => x.Hwnd));
                foreach (var dead in _seen.Keys.Where(k => !alive.Contains(k)).ToList()) _seen.Remove(dead);
            }
            _attention.RemoveWhere(h => !DockNative.IsWindow(h));

            // 1) «Пуск» и закреплённые значки — всегда на своих местах
            var specs = new List<DockItem> { new DockItem { Kind = DockKind.Start, Key = "start", Name = Loc.T("dock.start") } };
            var claimed = new HashSet<IntPtr>();
            var keys = new HashSet<string>();
            foreach (var pin in _s.DockPins ?? new List<DockPin>())
            {
                string key = KeyOf(string.IsNullOrEmpty(pin.Target) ? pin.Path : pin.Target);
                if (!keys.Add(key)) continue;
                var item = new DockItem { Kind = DockKind.App, Key = key, Name = pin.Name, Launch = pin.Path, IconPath = pin.Path, Pinned = true, Pin = pin };
                foreach (var w in wins)
                    if (!claimed.Contains(w.Hwnd) && DockApps.Matches(pin, w))
                    {
                        claimed.Add(w.Hwnd);
                        item.Windows.Add(w.Hwnd);
                        item.Sample ??= w;
                    }
                specs.Add(item);
            }

            // 2) Открытые приложения — в том порядке, в котором их открывали (значки не прыгают при переключении окон)
            var groups = new Dictionary<string, DockItem>();
            foreach (var w in wins)
            {
                if (claimed.Contains(w.Hwnd)) continue;
                string key = KeyOf(w.Key);
                if (!groups.TryGetValue(key, out var item))
                {
                    item = new DockItem
                    {
                        Kind = DockKind.App, Key = key, Sample = w,
                        Name = w.Aumid != null ? w.Title : DockApps.NameOf(w.Exe),
                        Launch = w.Aumid != null ? @"shell:AppsFolder\" + w.Aumid : w.Exe,
                        IconPath = DockApps.IconSource(w)
                    };
                    groups[key] = item;
                }
                item.Windows.Add(w.Hwnd);
            }
            foreach (var k in groups.Keys) if (!_runOrder.Contains(k)) _runOrder.Add(k);
            _runOrder.RemoveAll(k => !groups.ContainsKey(k));
            var extra = _runOrder.Select(k => groups[k]).ToList();

            foreach (var it in specs.Concat(extra))
            {
                if (it.Windows.Count > 1) it.Windows.Sort((a, b) => Seen(a).CompareTo(Seen(b)));
                it.Attention = it.Windows.Exists(h => _attention.Contains(h));
            }
            if (extra.Count > 0 && specs.Count > 1) specs.Add(new DockItem { Kind = DockKind.Separator, Key = "sep:apps" });
            specs.AddRange(extra);

            if (_s.DockClock)
            {
                specs.Add(new DockItem { Kind = DockKind.Separator, Key = "sep:sys" });
                specs.Add(new DockItem { Kind = DockKind.Tray, Key = "tray", Name = Loc.T("dock.tray") });
                specs.Add(new DockItem { Kind = DockKind.System, Key = "system", Name = Loc.T("dock.system") });
                specs.Add(new DockItem { Kind = DockKind.Clock, Key = "clock", Name = Loc.T("dock.notifications") });
            }

            if (!_view.Dragging) Merge(specs);
            UpdateTrayCount();

            string minute = DateTime.Now.ToString("HH:mm");
            if (minute != _minuteShown) { _minuteShown = minute; _view.InvalidateVisual(); }   // часы
        }

        long Seen(IntPtr h) => _seen.TryGetValue(h, out long n) ? n : long.MaxValue;

        void UpdateTrayCount()
        {
            int n = TrayHost.Running ? TrayHost.Snapshot().Count : 0;
            if (n != _view.TrayCount) { _view.TrayCount = n; _view.InvalidateVisual(); }
        }

        /// <summary>Сохраняем уже существующие элементы (чтобы их анимации не прерывались), новые плавно появляются, лишние плавно исчезают на своём месте.</summary>
        void Merge(List<DockItem> specs)
        {
            var old = _view.Items;
            var result = new List<DockItem>();
            foreach (var sp in specs)
            {
                var ex = old.Find(o => o.Key == sp.Key && !result.Contains(o));
                if (ex != null)
                {
                    bool wasRunning = ex.Windows.Count > 0;
                    ex.Windows = sp.Windows;
                    ex.Name = sp.Name;
                    ex.Launch = sp.Launch;
                    ex.Pinned = sp.Pinned;
                    ex.Pin = sp.Pin;
                    ex.Sample = sp.Sample ?? ex.Sample;
                    if (sp.Attention && !ex.Attention) _view.Nudge(ex);
                    ex.Attention = sp.Attention;
                    ex.PresenceTarget = 1;
                    if (ex.IconPath != sp.IconPath && sp.IconPath != null && ex.Icon == null) LoadIcon(ex, sp.IconPath);
                    if (!wasRunning && ex.Windows.Count > 0 && ex.Bounce >= 0) _view.Wake();
                    result.Add(ex);
                }
                else
                {
                    sp.Presence = 0;
                    LoadIcon(sp, sp.IconPath);
                    result.Add(sp);
                }
            }
            // Исчезающие значки остаются там, где были, и плавно сжимаются — соседи мягко сдвигаются
            for (int i = 0; i < old.Count; i++)
            {
                var o = old[i];
                if (result.Contains(o)) continue;
                o.PresenceTarget = 0;
                int at = 0;
                for (int k = i - 1; k >= 0; k--)
                {
                    int idx = result.IndexOf(old[k]);
                    if (idx >= 0) { at = idx + 1; break; }
                }
                result.Insert(Math.Min(at, result.Count), o);
            }
            _view.Items = result;
            _view.InvalidateVisual();
            if (result.Exists(x => x.Presence != x.PresenceTarget)) _view.Wake();
        }

        void LoadIcon(DockItem item, string path)
        {
            item.IconPath = path;
            // Иконку достаём в фоне: пока её нет, рисуется плитка с буквой
            var img = DockApps.IconFor(path, im => Dispatcher.BeginInvoke(new Action(() => { item.Icon = im; _view.InvalidateVisual(); })));
            if (img != null) item.Icon = img;
        }

        /// <summary>Значок окна в доке в пикселях экрана (куда сворачивается окно). Empty — такого значка нет.</summary>
        public Rect IconFor(IntPtr hwnd)
        {
            if (!_enabled || _fsHidden) return Rect.Empty;
            var it = _view.Items.Find(x => x.Kind == DockKind.App && x.PresenceTarget > 0 && x.Windows.Contains(hwnd));
            if (it == null)
            {
                // Окно только что появилось и ещё не в доке — ищем значок его приложения
                DockNative.GetWindowThreadProcessId(hwnd, out uint pid);
                string exe = DockNative.ExePath(pid);
                it = _view.Items.Find(x => x.Kind == DockKind.App && x.Sample != null && string.Equals(x.Sample.Exe, exe, StringComparison.OrdinalIgnoreCase));
            }
            return it == null ? Rect.Empty : IconOnScreen(it);
        }

        // ================== клики ==================

        void Activate(DockItem it)
        {
            switch (it.Kind)
            {
                case DockKind.Start:
                    DockNative.PressKeys(DockNative.VK_LWIN);
                    return;
                case DockKind.Tray:
                    ToggleTray(it);
                    return;
                case DockKind.System:
                    DockNative.PressKeys(DockNative.VK_LWIN, (byte)'A');   // быстрые настройки: Wi-Fi, звук
                    return;
                case DockKind.Clock:
                    DockNative.PressKeys(DockNative.VK_LWIN, Win11 ? (byte)'N' : (byte)'A');   // уведомления и календарь
                    return;
                case DockKind.Separator:
                    return;
            }

            if (it.Windows.Count == 0) { Launch(it); return; }

            // Как на Mac: клик по активному приложению сворачивает его в значок, по свёрнутому — разворачивает из значка.
            // Если окон несколько — каждый клик переключает на следующее.
            IntPtr fg = Native.GetForegroundWindow();
            int idx = it.Windows.IndexOf(fg);
            foreach (var h in it.Windows) _attention.Remove(h);
            it.Attention = false;

            if (idx >= 0 && !Native.IsIconic(fg))
            {
                if (it.Windows.Count == 1) MinimizeWindow(fg, it);
                else FocusWindow(it.Windows[(idx + 1) % it.Windows.Count], it);
            }
            else
            {
                // Сначала — последнее активное окно приложения
                IntPtr h = it.Windows.FirstOrDefault(w => !Native.IsIconic(w));
                FocusWindow(h != IntPtr.Zero ? h : it.Windows[0], it);
            }
            _soon.Start();
        }

        void MinimizeWindow(IntPtr h, DockItem it)
        {
            var target = Shell.FlyTarget(h, IconOnScreen(it));
            if (_s.DockAnimations && !target.rect.IsEmpty) Shell.Anim.Minimize(h, target.rect, target.up);
            else WindowAnimator.SetMinimized(h, true);
        }

        void FocusWindow(IntPtr h, DockItem it)
        {
            if (Native.IsIconic(h))
            {
                var target = Shell.FlyTarget(h, IconOnScreen(it));
                if (_s.DockAnimations && !target.rect.IsEmpty) { Shell.Anim.Restore(h, target.rect, target.up); return; }
                WindowAnimator.SetMinimized(h, false);
            }
            WindowAnimator.BringToFront(h);
        }

        void Launch(DockItem it)
        {
            if (string.IsNullOrEmpty(it.Launch)) return;
            try
            {
                if (it.Launch.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                    Process.Start(new ProcessStartInfo("explorer.exe", it.Launch) { UseShellExecute = true });
                else
                    Process.Start(new ProcessStartInfo(it.Launch) { UseShellExecute = true });
                it.Bounce = 0;
                _view.Wake();
            }
            catch (Exception e) { Settings.Log(e); }
        }

        /// <summary>Прямоугольник значка в пикселях экрана.</summary>
        Rect IconOnScreen(DockItem it)
        {
            Rect r = _view.IconRect(it);
            try
            {
                Point a = _view.PointToScreen(r.TopLeft), b = _view.PointToScreen(r.BottomRight);
                return new Rect(a, b);
            }
            catch (InvalidOperationException) { return Rect.Empty; }
        }

        // ================== трей ==================

        void ToggleTray(DockItem it)
        {
            if (!TrayHost.Running) { Taskbar.Peek(); return; }   // трей в доке выключен — показываем панель Windows
            _trayPopup ??= new TrayPopup();
            if (_trayPopup.IsOpen) { _trayPopup.Dismiss(); return; }
            Rect box = _view.ItemBox(it);
            var anchor = new Point(Left + box.X + box.Width / 2, Top + _view.Pill.Top);
            var keep = new Rect(Left + box.X, Top + box.Y, box.Width, box.Height);
            _trayPopup.Open(anchor, keep);
        }

        // ================== меню по правой кнопке ==================

        void ShowMenu(DockItem it, Point screen)
        {
            var menu = GlassMenu.Create();
            if (it.Kind == DockKind.App)
            {
                // Окна приложения — как в macOS: можно сразу выбрать нужное
                if (it.Windows.Count > 1)
                {
                    foreach (var h in it.Windows)
                    {
                        var hw = h;
                        var sb = new System.Text.StringBuilder(256);
                        DockNative.InternalGetWindowText(hw, sb, sb.Capacity);
                        string title = sb.Length > 0 ? sb.ToString() : it.Name;
                        if (title.Length > 48) title = title.Substring(0, 46) + "…";
                        GlassMenu.Item(menu, (hw == Native.GetForegroundWindow() ? "✓  " : "     ") + title, () => FocusWindow(hw, it));
                    }
                    GlassMenu.Separator(menu);
                }
                if (it.Windows.Count > 0 && !string.IsNullOrEmpty(it.Launch))
                    GlassMenu.Item(menu, Loc.T("dock.menu.new"), () => Launch(it));
                if (it.Windows.Count == 0)
                    GlassMenu.Item(menu, Loc.T("dock.menu.open"), () => Launch(it));
                GlassMenu.Separator(menu);
                if (it.Pinned) GlassMenu.Item(menu, Loc.T("dock.menu.unpin"), () => Unpin(it));
                else if (it.Sample != null) GlassMenu.Item(menu, Loc.T("dock.menu.pin"), () => PinItem(it, -1));
                string file = it.Sample?.Exe ?? (it.Launch != null && File.Exists(it.Launch) ? it.Launch : null);
                if (file != null && it.Sample?.Aumid == null)
                    GlassMenu.Item(menu, Loc.T("dock.menu.show"), () =>
                    {
                        try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + file + "\"") { UseShellExecute = true }); } catch { }
                    });
                if (it.Windows.Count > 0)
                {
                    GlassMenu.Separator(menu);
                    string close = it.Windows.Count > 1 ? string.Format(Loc.T("dock.menu.closeall"), it.Windows.Count) : Loc.T("dock.menu.close");
                    var wins = it.Windows.ToList();
                    GlassMenu.Item(menu, close, () => { foreach (var h in wins) DockNative.PostMessage(h, DockNative.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); _soon.Start(); });
                }
            }
            else
            {
                GlassMenu.Item(menu, Loc.T("dock.menu.settings"), () => Shell.OpenApp?.Invoke());
                if (it.Kind == DockKind.Tray && TrayHost.Running)
                    GlassMenu.Item(menu, Loc.T("dock.menu.wintray"), () => Taskbar.Peek());
            }

            var box = it.Kind == DockKind.App ? _view.IconRect(it) : _view.ItemBox(it);
            menu.PlacementTarget = _view;
            menu.Placement = PlacementMode.Top;
            menu.PlacementRectangle = box;
            menu.IsOpen = true;
        }

        // ================== закрепление ==================

        void PinItem(DockItem it, int at)
        {
            if (it.Sample == null) return;
            var pins = _s.DockPins ?? (_s.DockPins = new List<DockPin>());
            var pin = DockApps.PinFor(it.Sample, it.Name);
            if (pins.Exists(p => string.Equals(p.Target, pin.Target, StringComparison.OrdinalIgnoreCase))) return;
            if (at < 0 || at > pins.Count) pins.Add(pin); else pins.Insert(at, pin);
            _s.Save();
            Refresh();
        }

        void Unpin(DockItem it)
        {
            if (it.Pin == null || _s.DockPins == null) return;
            _s.DockPins.Remove(it.Pin);
            _s.Save();
            Refresh();
        }

        /// <summary>
        /// Значок перетащили. Всё, что левее разделителя, — закреплено (в этом порядке), правее — просто открытые.
        /// Утащили закреплённый значок вверх из дока — открепляем (как в macOS).
        /// </summary>
        void OnDropped(DockItem dropped, bool removed)
        {
            var items = _view.Items;
            int sep = items.FindIndex(i => i.Key == "sep:apps");
            int sys = items.FindIndex(i => i.Key == "sep:sys");
            int end = sep >= 0 ? sep : sys >= 0 ? sys : items.Count;
            var pins = new List<DockPin>();
            for (int i = 0; i < end; i++)
            {
                var it = items[i];
                if (it.Kind != DockKind.App || it.PresenceTarget == 0) continue;
                if (removed && it == dropped) continue;
                if (it.Pin != null) pins.Add(it.Pin);
                else if (it.Sample != null) pins.Add(DockApps.PinFor(it.Sample, it.Name));
            }
            _s.DockPins = pins;
            _s.Save();
            Refresh();
        }

        // ================== резерв места внизу экрана ==================

        void RegisterAppBar()
        {
            if (_appbar || _hwnd == IntPtr.Zero) return;
            _abMsg = DockNative.RegisterWindowMessage("MizuEngineDockAppBar");
            var abd = DockNative.NewAppBar(_hwnd);
            abd.uCallbackMessage = _abMsg;
            DockNative.SHAppBarMessage(DockNative.ABM_NEW, ref abd);
            _appbar = true;
            SetAppBarPos();
        }

        void SetAppBarPos()
        {
            if (!_appbar) return;
            var dpi = VisualTreeHelper.GetDpi(this);
            int sw = Native.GetSystemMetrics(0), sh = Native.GetSystemMetrics(1);
            int reserve = (int)Math.Ceiling((_s.DockSize + 2 * DockView.Pad + DockView.BottomMargin + 4) * dpi.DpiScaleY);
            var abd = DockNative.NewAppBar(_hwnd);
            abd.uEdge = DockNative.ABE_BOTTOM;
            abd.rc = new Native.RECT { Left = 0, Top = sh - reserve, Right = sw, Bottom = sh };
            DockNative.SHAppBarMessage(DockNative.ABM_QUERYPOS, ref abd);
            abd.rc.Top = abd.rc.Bottom - reserve;
            DockNative.SHAppBarMessage(DockNative.ABM_SETPOS, ref abd);
        }

        void UnregisterAppBar()
        {
            if (!_appbar) return;
            var abd = DockNative.NewAppBar(_hwnd);
            DockNative.SHAppBarMessage(DockNative.ABM_REMOVE, ref abd);
            _appbar = false;
        }

        IntPtr WndProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (_appbar && _abMsg != 0 && (uint)msg == _abMsg && w.ToInt64() == 1)   // ABN_POSCHANGED
                SetAppBarPos();
            else if (_shellMsg != 0 && (uint)msg == _shellMsg)
            {
                long code = w.ToInt64();
                if (code == 0x8006)   // HSHELL_FLASH: приложение просит внимания
                {
                    _attention.Add(l);
                    _soon.Start();
                }
                else
                {
                    if ((code & 0x7FFF) == 4) _attention.Remove(l);   // окно активировали — внимание получено
                    if (!_soon.IsEnabled) _soon.Start();              // окно открылось/закрылось/сменило заголовок
                }
            }
            return IntPtr.Zero;
        }
    }
}
