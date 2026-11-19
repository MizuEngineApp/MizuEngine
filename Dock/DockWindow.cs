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
    /// <summary>
    /// Стекло под доком: размывает то, что за доком. С 0.10.2 само окно размером ровно с док (чуть меньше),
    /// а не во всю ширину экрана: даже если Windows рисует размытие на всё окно, оно не вылезает за док
    /// и не перекрывает приложения.
    /// </summary>
    internal sealed class DockBlur : Window
    {
        IntPtr _h;
        Native.RECT _px;          // где стоит стекло, пиксели экрана — двигаем окно, только если это правда изменилось
        int _radiusPx = -1;
        int _tint = 0x40201410;

        const uint SWP_NOZORDER = 0x4;
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out Native.RECT r);

        public DockBlur()
        {
            Glass.Setup(this, clickThrough: false);
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            Title = "MizuEngine Dock Blur";
            Left = -32000; Top = -32000; Width = 1; Height = 1;   // настоящий размер ставит SetPill

            SourceInitialized += (s, e) =>
            {
                _h = new WindowInteropHelper(this).Handle;
                DockNative.EnableAcrylic(_h, _tint);
                Apply();
            };
            // Стекло переехало на монитор с другим масштабом — Windows сам меняет размер окна, ставим его обратно ровно под док
            DpiChanged += (s, e) => Dispatcher.BeginInvoke(new Action(Apply));
        }

        public IntPtr Handle => _h;

        /// <summary>DWM перезапустился (сбой видеодрайвера, смена темы) — включаем размытие заново.</summary>
        public void Reapply()
        {
            if (_h == IntPtr.Zero) return;
            DockNative.EnableAcrylic(_h, _tint);
            Apply();
        }

        /// <summary>Чем прозрачнее док, тем слабее тёмный оттенок стекла.</summary>
        public void SetTransparency(int t)
        {
            double g = 1 - Math.Clamp(t, 0, 100) / 100.0;
            _tint = ((int)(0x08 + 0x70 * g) << 24) | 0x201410;   // формат 0xAABBGGRR
            if (_h != IntPtr.Zero) DockNative.EnableAcrylic(_h, _tint);
        }

        /// <summary>Ставит стекло ровно под «пилюлю» дока. pill — в координатах окна дока (DIP).</summary>
        public void SetPill(IntPtr dock, Visual dockVisual, Rect pill, double radius)
        {
            if (_h == IntPtr.Zero || dock == IntPtr.Zero) return;
            var target = default(Native.RECT);
            int rad = 0;
            if (!pill.IsEmpty && pill.Width > 4 && pill.Height > 4 && GetWindowRect(dock, out var w))
            {
                var dpi = VisualTreeHelper.GetDpi(dockVisual);
                double r = radius * dpi.DpiScaleX;
                // Отступ 0.3·радиуса: прямоугольник стекла целиком помещается внутри скруглённого дока,
                // даже если Windows не обрежет углы регионом
                int inset = Math.Max(2, (int)Math.Ceiling(r * 0.3));
                target.Left = w.Left + (int)Math.Round(pill.Left * dpi.DpiScaleX) + inset;
                target.Top = w.Top + (int)Math.Round(pill.Top * dpi.DpiScaleY) + inset;
                target.Right = w.Left + (int)Math.Round(pill.Right * dpi.DpiScaleX) - inset;
                target.Bottom = w.Top + (int)Math.Round(pill.Bottom * dpi.DpiScaleY) - inset;
                rad = (int)Math.Round(Math.Max(0, r - inset) * 2);
            }
            if (target.Left == _px.Left && target.Top == _px.Top && target.Right == _px.Right && target.Bottom == _px.Bottom && rad == _radiusPx) return;
            _px = target;
            _radiusPx = rad;
            Apply();
        }

        void Apply()
        {
            if (_h == IntPtr.Zero) return;
            int w = _px.Right - _px.Left, h = _px.Bottom - _px.Top;
            if (w <= 0 || h <= 0)
            {
                DockNative.SetWindowRgn(_h, DockNative.CreateRectRgn(0, 0, 0, 0), true);   // дока не видно — стекла тоже
                return;
            }
            DockNative.SetWindowPos(_h, IntPtr.Zero, _px.Left, _px.Top, w, h, SWP_NOZORDER | DockNative.SWP_NOACTIVATE);
            DockNative.SetWindowRgn(_h, DockNative.CreateRoundRectRgn(0, 0, w + 1, h + 1, _radiusPx, _radiusPx), true);
        }
    }

    /// <summary>
    /// Док вместо панели задач: «Пуск», закреплённые и открытые приложения, трей, сеть/звук и часы.
    /// Работает как панель задач Windows: порядок значков не прыгает, точка под открытыми приложениями,
    /// меню по правой кнопке, значки можно перетаскивать. А выглядит и анимируется как док macOS.
    /// Один такой док — на одном мониторе (Mon); все доки вместе — DockSet.
    /// Живёт в своём потоке (Shell): эффекты рабочего стола и главное окно его не тормозят.
    /// </summary>
    internal sealed class DockWindow : Window
    {
        readonly Settings _s;
        readonly DockSet _owner;
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

        /// <summary>Монитор этого дока (null — мониторы не нашлись, док по размеру главного экрана).</summary>
        public MonitorInfo Mon { get; private set; }
        public bool IsPrimary => Mon == null || Mon.Primary;

        public DockWindow(Settings settings, DockSet owner)
        {
            _s = settings;
            _owner = owner;
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
            _onLoc = () => Dispatcher.BeginInvoke(new Action(() => { if (_enabled) Refresh(); }));
            _onTray = () => Dispatcher.BeginInvoke(new Action(UpdateTrayCount));
            Loc.Changed += _onLoc;
            TrayHost.Changed += _onTray;
            // Док переехал на монитор с другим масштабом — Windows меняет размер окна сам; ставим ровно по низу монитора
            DpiChanged += (o, e) => Dispatcher.BeginInvoke(new Action(() => { if (_enabled) { Place(); UpdateShape(); if (_appbar) SetAppBarPos(); } }));
        }

        readonly Action _onLoc, _onTray;

        /// <summary>Монитор поменялся (разрешение, масштаб, положение) — переставляем док.</summary>
        public void SetMonitor(MonitorInfo m)
        {
            bool moved = Mon == null || m == null || !Same(Mon.Bounds, m.Bounds) || Mon.Dpi != m.Dpi;
            Mon = m;
            if (_enabled && moved) { Place(); if (_appbar) SetAppBarPos(); }
        }

        /// <summary>Монитор отключили или док на нём выключили — окно больше не нужно.</summary>
        public void CloseAll()
        {
            Loc.Changed -= _onLoc;
            TrayHost.Changed -= _onTray;
            _timer.Stop();
            _soon.Stop();
            try { _trayPopup?.Close(); } catch { }
            try { _blur.Close(); } catch { }
            try { Close(); } catch { }
        }

        public void RefreshNow() { if (_enabled) Refresh(); }

        // ================== включение / выключение (можно звать из любого потока) ==================

        public void SetEnabled(bool on)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => SetEnabled(on))); return; }
            if (on == _enabled) { if (on) ApplySettings(); return; }
            _enabled = on;
            if (on)
            {
                _view.Items.Clear();
                new WindowInteropHelper(this).EnsureHandle();   // окно ставим в пикселях своего монитора ещё до показа
                Place();
                Show();
                Place();
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
                UnregisterAppBar();
                _blur.Hide();
                Hide();
            }
            // Панель задач Windows, трей и анимации — общие для всех доков, ими управляет DockSet
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
            if (!_enabled) return;

            Place();
            if (_s.DockBlur && !_fsHidden) { _blur.Show(); UpdateShape(); }
            else _blur.Hide();

            if (_s.DockAutohide) UnregisterAppBar();   // при автоскрытии окна занимают весь экран, как на Mac
            else if (!_appbar) RegisterAppBar();
            else SetAppBarPos();

            // Трей внутри дока (общий для всех доков) включает DockSet; здесь только прячем панельку значков
            if (!(_s.DockClock && _s.DockTray)) _trayPopup?.Dismiss();
            Refresh();
        }

        /// <summary>Окно дока — полоса по низу своего монитора (в пикселях: у мониторов может быть разный масштаб).</summary>
        void Place()
        {
            double winH = _s.DockSize * _s.DockMagnify + 2 * DockView.Pad + DockView.BottomMargin + DockView.LabelSpace + DockView.BounceSpace;
            if (Mon == null || _hwnd == IntPtr.Zero)
            {
                double w = SystemParameters.PrimaryScreenWidth, h = SystemParameters.PrimaryScreenHeight;
                Left = 0; Top = h - winH; Width = w; Height = winH;   // стекло под доком ставится по форме дока (UpdateShape)
                return;
            }
            var b = Mon.Bounds;
            int hpx = (int)Math.Ceiling(winH * Mon.Scale);
            DockNative.SetWindowPos(_hwnd, IntPtr.Zero, b.Left, b.Bottom - hpx, b.Width, hpx, SWP_NOZORDER | DockNative.SWP_NOACTIVATE);
        }

        const uint SWP_NOZORDER = 0x4;

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
            if (_s.DockBlur) _blur.SetPill(_hwnd, this, _view.Pill, _view.PillRadius);
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

            // В полноэкранных играх и видео док прячется (только тот, на чьём мониторе игра)
            bool fs = Mon != null
                ? _fullscreen.ForegroundCovers(_hwnd, Mon.Bounds)
                : _fullscreen.ForegroundIsFullscreen(_hwnd, Native.GetSystemMetrics(0), Native.GetSystemMetrics(1));
            if (fs != _fsHidden)
            {
                _fsHidden = fs;
                if (fs) { Hide(); _blur.Hide(); _trayPopup?.Dismiss(); }
                else { if (_s.DockBlur) _blur.Show(); Show(); }
            }
            if (IsPrimary) Taskbar.EnsureHidden();   // панели задач Windows прячет один док, а не каждый
            if (_s.DockBlur && _blur.Handle != IntPtr.Zero && _hwnd != IntPtr.Zero)   // стекло всегда прямо под доком
                DockNative.SetWindowPos(_blur.Handle, _hwnd, 0, 0, 0, 0, DockNative.SWP_NOMOVE | DockNative.SWP_NOSIZE | DockNative.SWP_NOACTIVATE);

            var wins = DockApps.EnumRunning();
            // Окна этого монитора (и мониторов без своего дока — для главного дока)
            var here = new HashSet<IntPtr>();
            foreach (var w in wins) if (_owner == null || _owner.OwnerOf(w.Hwnd) == this) here.Add(w.Hwnd);
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
                List<IntPtr> away = null;
                foreach (var w in wins)
                    if (!claimed.Contains(w.Hwnd) && DockApps.Matches(pin, w))
                    {
                        claimed.Add(w.Hwnd);
                        if (here.Contains(w.Hwnd)) item.Windows.Add(w.Hwnd);
                        else (away ??= new List<IntPtr>()).Add(w.Hwnd);   // окно открыто на другом мониторе
                        item.Sample ??= w;
                    }
                // Закреплённое приложение открыто только на другом мониторе: точки здесь нет, но клик переключает на его окно, а не запускает второе
                if (item.Windows.Count == 0 && away != null) { item.Windows.AddRange(away); item.Away = true; }
                specs.Add(item);
            }

            // 2) Открытые приложения — в том порядке, в котором их открывали (значки не прыгают при переключении окон)
            var groups = new Dictionary<string, DockItem>();
            foreach (var w in wins)
            {
                if (claimed.Contains(w.Hwnd) || !here.Contains(w.Hwnd)) continue;   // открытые — только в доке своего монитора
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
                    bool wasRunning = ex.Windows.Count > 0 && !ex.Away;
                    ex.Windows = sp.Windows;
                    ex.Away = sp.Away;
                    ex.Name = sp.Name;
                    ex.Launch = sp.Launch;
                    ex.Pinned = sp.Pinned;
                    ex.Pin = sp.Pin;
                    ex.Sample = sp.Sample ?? ex.Sample;
                    if (sp.Attention && !ex.Attention) _view.Nudge(ex);
                    ex.Attention = sp.Attention;
                    ex.PresenceTarget = 1;
                    if (ex.IconPath != sp.IconPath && sp.IconPath != null && ex.Icon == null) LoadIcon(ex, sp.IconPath);
                    if (!wasRunning && ex.Windows.Count > 0 && !ex.Away && ex.Bounce >= 0) _view.Wake();
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
            var it = _view.Items.Find(x => x.Kind == DockKind.App && x.PresenceTarget > 0 && !x.Away && x.Windows.Contains(hwnd));
            if (it == null)
            {
                // Окно только что появилось (или переехало с другого монитора) и ещё не в доке — ищем значок его приложения
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
                    OpenStart();
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

        /// <summary>Куда сворачивается окно: в этот значок, а если окно на другом мониторе — в значок дока того монитора.</summary>
        Rect IconForWindow(IntPtr h, DockItem it) => it.Away && _owner != null ? _owner.IconFor(h) : IconOnScreen(it);

        /// <summary>
        /// Кнопка «Пуск». Windows 11 открывает меню «Пуск» на главном мониторе (у панели задач второго монитора нет кнопки,
        /// которую можно нажать программно), поэтому из дока любого монитора «Пуск» открывается на главном.
        /// Повторное нажатие закрывает меню.
        /// </summary>
        void OpenStart() => DockNative.PressKeys(DockNative.VK_LWIN);

        /// <summary>Середина дока в пикселях экрана — запасная цель для «джинна», если значка окна в доке нет.</summary>
        public Rect PillOnScreen()
        {
            if (!_enabled || _hwnd == IntPtr.Zero) return Rect.Empty;
            var pill = _view.Pill;
            if (pill.IsEmpty || pill.Width < 4) return Rect.Empty;
            try
            {
                double s = _s.DockSize;
                var c = new Point(pill.X + pill.Width / 2, pill.Y + pill.Height / 2);
                Point a = _view.PointToScreen(new Point(c.X - s / 2, c.Y - s / 2)), b = _view.PointToScreen(new Point(c.X + s / 2, c.Y + s / 2));
                return new Rect(a, b);
            }
            catch (InvalidOperationException) { return Rect.Empty; }
        }

        /// <summary>
        /// После сбоя Windows (перезапуск Проводника, видеодрайвера, выход из сна): заново резервируем место,
        /// включаем стекло, ставим окно на место и поднимаем над окнами.
        /// </summary>
        public void Recover()
        {
            if (!_enabled) return;
            try
            {
                Place();
                if (_appbar) { UnregisterAppBar(); }
                if (!_s.DockAutohide) RegisterAppBar();
                if (_hwnd != IntPtr.Zero && _shellHook) { DockNative.DeregisterShellHookWindow(_hwnd); _shellHook = DockNative.RegisterShellHookWindow(_hwnd); }
                _blur.Reapply();
                _fsHidden = false;
                Show();
                if (_s.DockBlur) _blur.Show();
                UpdateShape();
                RaiseAbove();
                Refresh();
            }
            catch (Exception e) { Settings.Log(e); }
        }

        void MinimizeWindow(IntPtr h, DockItem it)
        {
            var target = Shell.FlyTarget(h, IconForWindow(h, it));
            if (_s.DockAnimations && !target.rect.IsEmpty) Shell.Anim.Minimize(h, target.rect, target.up);
            else WindowAnimator.SetMinimized(h, true);
        }

        void FocusWindow(IntPtr h, DockItem it)
        {
            if (Native.IsIconic(h))
            {
                var target = Shell.FlyTarget(h, IconForWindow(h, it));
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
            Point anchor, k1, k2;
            try
            {
                // В пикселях экрана: доки бывают на мониторах с разным масштабом
                anchor = _view.PointToScreen(new Point(box.X + box.Width / 2, _view.Pill.Top));
                k1 = _view.PointToScreen(box.TopLeft);
                k2 = _view.PointToScreen(box.BottomRight);
            }
            catch (InvalidOperationException) { return; }
            _trayPopup.Open(anchor, new Rect(k1, k2));
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
            RefreshAllDocks();
        }

        void Unpin(DockItem it)
        {
            if (it.Pin == null || _s.DockPins == null) return;
            _s.DockPins.Remove(it.Pin);
            _s.Save();
            RefreshAllDocks();
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
            RefreshAllDocks();
        }

        /// <summary>Закреплённые значки есть в каждом доке — после изменения обновляем все.</summary>
        void RefreshAllDocks()
        {
            if (_owner != null) _owner.RefreshAll(); else Refresh();
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
            _abSet = false;
            SetAppBarPos(true);
        }

        Native.RECT _abRect;
        bool _abSet;

        /// <summary>
        /// Резерв места внизу монитора. Ставим его, только если он правда изменился: иначе два дока на разных мониторах
        /// «перекидывались» уведомлениями ABN_POSCHANGED, Windows без конца пересчитывал рабочую область —
        /// ярлыки рабочего стола переезжали на другой монитор, а Проводник подтормаживал.
        /// </summary>
        void SetAppBarPos(bool force = false)
        {
            if (!_appbar) return;
            // Место резервируется внизу своего монитора — окна на нём разворачиваются до дока, а не под него
            double scale = Mon?.Scale ?? VisualTreeHelper.GetDpi(this).DpiScaleY;
            var b = Mon?.Bounds ?? new Native.RECT { Left = 0, Top = 0, Right = Native.GetSystemMetrics(0), Bottom = Native.GetSystemMetrics(1) };
            int reserve = (int)Math.Ceiling((_s.DockSize + 2 * DockView.Pad + DockView.BottomMargin + 4) * scale);
            var abd = DockNative.NewAppBar(_hwnd);
            abd.uEdge = DockNative.ABE_BOTTOM;
            abd.rc = new Native.RECT { Left = b.Left, Top = b.Bottom - reserve, Right = b.Right, Bottom = b.Bottom };
            if (!force && _abSet && Same(abd.rc, _abRect)) return;
            DockNative.SHAppBarMessage(DockNative.ABM_QUERYPOS, ref abd);
            abd.rc.Top = abd.rc.Bottom - reserve;
            if (!force && _abSet && Same(abd.rc, _abRect)) return;
            DockNative.SHAppBarMessage(DockNative.ABM_SETPOS, ref abd);
            _abRect = abd.rc;
            _abSet = true;
        }

        void UnregisterAppBar()
        {
            if (!_appbar) return;
            var abd = DockNative.NewAppBar(_hwnd);
            DockNative.SHAppBarMessage(DockNative.ABM_REMOVE, ref abd);
            _appbar = false;
            _abSet = false;
        }

        static readonly uint TaskbarCreatedMsg = DockNative.RegisterWindowMessage("TaskbarCreated");

        IntPtr WndProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (TaskbarCreatedMsg != 0 && (uint)msg == TaskbarCreatedMsg)
            {
                // Проводник перезапустился (или упал): панель задач появилась заново — прячем её и восстанавливаем доки
                if (IsPrimary) Dispatcher.BeginInvoke(new Action(() => _owner?.Recover()), DispatcherPriority.Background);
            }
            else if (msg == 0x031E)   // WM_DWMCOMPOSITIONCHANGED: DWM перезапустился — стекло нужно включить заново
                Dispatcher.BeginInvoke(new Action(() => _blur.Reapply()));
            else if (_appbar && _abMsg != 0 && (uint)msg == _abMsg && w.ToInt64() == 1)   // ABN_POSCHANGED
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
