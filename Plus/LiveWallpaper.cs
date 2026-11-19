// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>
    /// Живые обои (Mizu Plus): видео MP4 вместо картинки рабочего стола — за ярлыками, как у Wallpaper Engine.
    /// Окно с видео встраивается в слой обоев Windows (WorkerW). Звук выключен, видео крутится по кругу.
    /// Когда игра или другое окно развёрнуто на весь экран — видео на паузе и не тратит видеокарту.
    /// При выключении и выходе из программы возвращаются обычные обои.
    /// </summary>
    internal static class LiveWallpaper
    {
        static readonly List<Window> _windows = new List<Window>();
        static readonly List<MediaElement> _players = new List<MediaElement>();
        static DispatcherTimer _watch;
        static string _file;
        static bool _paused;

        public static bool Running => _windows.Count > 0;

        /// <summary>Включить, выключить или перезапустить по настройкам. Вызывать в главном потоке.</summary>
        public static void Sync(Settings s)
        {
            bool want = s.LiveWallpaperOn && Plus.IsActive && File.Exists(s.LiveWallpaper);
            if (!want) { Stop(); return; }
            if (Running && _file == s.LiveWallpaper && _allMonitors == s.LiveAllMonitors)
            {
                foreach (var p in _players) p.IsMuted = !s.LiveSound;
                _pauseFullscreen = s.LivePauseFullscreen;
                return;
            }
            Stop();
            Start(s);
        }

        static bool _allMonitors, _pauseFullscreen;

        static void Start(Settings s)
        {
            try
            {
                IntPtr host = FindWallpaperHost();
                if (host == IntPtr.Zero) { Settings.Log(new Exception("live wallpaper: WorkerW not found")); return; }
                _file = s.LiveWallpaper;
                _allMonitors = s.LiveAllMonitors;
                _pauseFullscreen = s.LivePauseFullscreen;

                var screens = System.Windows.Forms.Screen.AllScreens;
                bool first = true;
                foreach (var scr in screens)
                {
                    if (!scr.Primary && !_allMonitors) continue;
                    var w = MakeWindow(s, first);
                    first = false;
                    Embed(w, host, scr.Bounds);
                    _windows.Add(w);
                }

                _watch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _watch.Tick += (o, e) => Watch();
                _watch.Start();
            }
            catch (Exception e) { Settings.Log(e); Stop(); }
        }

        static Window MakeWindow(Settings s, bool withSound)
        {
            var player = new MediaElement
            {
                Source = new Uri(s.LiveWallpaper),
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Close,
                Stretch = Stretch.UniformToFill,
                IsMuted = !(s.LiveSound && withSound),   // звук — только у одного монитора
                ScrubbingEnabled = false
            };
            player.MediaEnded += (o, e) => { player.Position = TimeSpan.Zero; player.Play(); };
            player.MediaFailed += (o, e) => Settings.Log(new Exception("live wallpaper: " + e.ErrorException?.Message));
            _players.Add(player);

            var w = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                Background = Brushes.Black,
                Content = player,
                Left = -32000, Top = -32000, Width = 10, Height = 10,
                Title = "MizuEngine Live Wallpaper"
            };
            w.SourceInitialized += (o, e) =>
            {
                var hwnd = new WindowInteropHelper(w).Handle;
                // Окно-«ребёнок» без рамки, не в Alt+Tab
                int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            };
            w.Show();
            player.Play();
            return w;
        }

        static void Embed(Window w, IntPtr host, System.Drawing.Rectangle screen)
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            SetParent(hwnd, host);
            // Координаты в слое обоев считаются от его левого верхнего угла
            var pt = new POINT { X = screen.Left, Y = screen.Top };
            ScreenToClient(host, ref pt);
            SetWindowPos(hwnd, IntPtr.Zero, pt.X, pt.Y, screen.Width, screen.Height, SWP_NOACTIVATE | SWP_NOZORDER | SWP_SHOWWINDOW);
        }

        /// <summary>Игра или окно на весь экран — пауза. Свернули — снова играет.</summary>
        static void Watch()
        {
            if (!Running) return;
            bool cover = _pauseFullscreen && SomethingFullscreen();
            if (cover == _paused) return;
            _paused = cover;
            foreach (var p in _players) { if (cover) p.Pause(); else p.Play(); }
        }

        static bool SomethingFullscreen()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            var cls = new StringBuilder(64);
            GetClassName(fg, cls, cls.Capacity);
            string c = cls.ToString();
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd") return false;   // это сам рабочий стол
            if (!GetWindowRect(fg, out var r)) return false;
            var scr = System.Windows.Forms.Screen.FromHandle(fg).Bounds;
            return r.Left <= scr.Left && r.Top <= scr.Top && r.Right >= scr.Right && r.Bottom >= scr.Bottom;
        }

        public static void Stop()
        {
            if (_watch != null) { _watch.Stop(); _watch = null; }
            foreach (var p in _players) { try { p.Stop(); p.Close(); p.Source = null; } catch { } }
            _players.Clear();
            bool had = _windows.Count > 0;
            foreach (var w in _windows) { try { w.Close(); } catch { } }
            _windows.Clear();
            _file = null;
            _paused = false;
            if (had) RepaintWallpaper();
        }

        /// <summary>Обычные обои Windows перерисовываются — иначе на месте видео остаётся последний кадр.</summary>
        static void RepaintWallpaper()
        {
            try
            {
                var sb = new StringBuilder(520);
                if (SystemParametersInfo(SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0))
                    SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, sb, 0);   // та же картинка, без сохранения в настройки
            }
            catch (Exception e) { Settings.Log(e); }
        }

        /// <summary>
        /// Слой обоев: просим Проводник создать WorkerW под ярлыками (сообщение 0x052C), потом находим его.
        /// В Windows 11 24H2 WorkerW живёт внутри Progman — ищем и там.
        /// </summary>
        static IntPtr FindWallpaperHost()
        {
            IntPtr progman = FindWindow("Progman", null);
            if (progman == IntPtr.Zero) return IntPtr.Zero;
            SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(0x1), SMTO_NORMAL, 1000, out _);
            SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, SMTO_NORMAL, 1000, out _);

            IntPtr worker = IntPtr.Zero;
            EnumWindows((top, l) =>
            {
                IntPtr shell = FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (shell != IntPtr.Zero) worker = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
                return true;
            }, IntPtr.Zero);
            if (worker == IntPtr.Zero) worker = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);   // Windows 11 24H2
            return worker;
        }

        // ---------- WinAPI ----------
        const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        const uint SWP_NOACTIVATE = 0x10, SWP_NOZORDER = 0x4, SWP_SHOWWINDOW = 0x40, SMTO_NORMAL = 0;
        const uint SPI_GETDESKWALLPAPER = 0x0073, SPI_SETDESKWALLPAPER = 0x0014;

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
        [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr hwnd, ref POINT p);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SystemParametersInfo(uint action, uint param, StringBuilder pv, uint winIni);
    }
}
