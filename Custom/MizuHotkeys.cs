// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Interop;

namespace MizuEngine
{
    /// <summary>Горячие клавиши Ctrl+Alt+…, работают в любой программе и в игре.</summary>
    internal static class MizuHotkeys
    {
        /// <summary>Что-то переключили клавишей — главное окно обновит свои переключатели.</summary>
        public static event Action Toggled;

        public static readonly (int id, char key, string text)[] Keys =
        {
            (101, 'E', "hk.effects"), (102, 'D', "hk.dock"), (103, 'F', "hk.fps"),
            (104, 'W', "hk.wallpaper"), (105, 'G', "hk.widgets"), (106, 'O', "hk.open"),
            (107, 'M', "hk.muzule"),  // быстрое окно Muzule (только Mizu Plus; можно выключить во вкладке Muzule)
            (108, ' ', "hk.spotlight"),   // Spotlight: Ctrl+Alt+Space (Ctrl+Space — переключение раскладки, его не трогаем)
            (109, 'L', "hk.launchpad")    // Launchpad
        };

        /// <summary>Что-то поменяли не из окна (например, агент Muzule) — открытое окно обновит свои переключатели.</summary>
        public static void NotifyChanged() => Toggled?.Invoke();

        static HwndSource _window;
        static bool _registered;
        static Effects _fx;
        static Settings _s;
        static Action _open;

        /// <summary>Как показать клавишу: пробел — словом.</summary>
        public static string KeyName(char key) => key == ' ' ? "Space" : key.ToString();

        public static void Init(Effects fx, Settings s, Action open)
        {
            _fx = fx; _s = s; _open = open;
            _window = new HwndSource(new HwndSourceParameters("MizuEngine hotkeys") { ParentWindow = new IntPtr(-3) });   // окно только для сообщений
            _window.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
            {
                if (msg == 0x0312 && w.ToInt32() >= 101 && w.ToInt32() <= 109) { handled = true; Fire(w.ToInt32()); }   // WM_HOTKEY
                return IntPtr.Zero;
            });
            Apply();
        }

        /// <summary>Включает или выключает клавиши по настройке.</summary>
        public static void Apply()
        {
            if (_window == null) return;
            if (_s.Hotkeys && !_registered)
            {
                foreach (var k in Keys)
                {
                    if (k.id == 107 && !_s.MuzuleSpotlight) continue;   // быстрый Muzule выключен — Ctrl+Alt+M свободна для других программ
                    if (k.id == 108 && !_s.SpotlightOn) continue;
                    if (k.id == 109 && !_s.LaunchpadOn) continue;
                    RegisterHotKey(_window.Handle, k.id, 0x1 | 0x2 | 0x4000, k.key);   // Alt+Ctrl, без автоповтора
                }
                _registered = true;
            }
            else if (!_s.Hotkeys && _registered)
            {
                foreach (var k in Keys) UnregisterHotKey(_window.Handle, k.id);
                _registered = false;
            }
        }

        /// <summary>Перерегистрировать клавиши (включили/выключили быстрый Muzule).</summary>
        public static void Reapply()
        {
            if (_window == null) return;
            foreach (var k in Keys) UnregisterHotKey(_window.Handle, k.id);
            _registered = false;
            Apply();
        }

        static void Fire(int id)
        {
            switch (id)
            {
                case 101:
                    _s.EffectsOn = !_s.EffectsOn;
                    _s.Save();
                    _fx.Run(() => _s.ApplyTo(_fx.Snow.Layer));
                    break;
                case 102:
                    _s.DockEnabled = !_s.DockEnabled;
                    _s.Save();
                    bool dock = _s.DockEnabled;
                    _fx.Run(() => _fx.Dock.SetEnabled(dock));
                    break;
                case 103:
                    _s.FpsOn = !_s.FpsOn;
                    _s.Save();
                    _fx.Run(() => _fx.Overlays.Apply());
                    break;
                case 104:
                    Task.Run(WallpaperEngine.ApplyRandom);
                    return;
                case 105:
                    _fx.Run(() => { _fx.Overlays.WidgetsHidden = !_fx.Overlays.WidgetsHidden; _fx.Overlays.Apply(); });
                    return;
                case 106:
                    _open();
                    return;
                case 107:
                    Muzule.Spotlight();
                    return;
                case 108:
                    Spotlight.Toggle();
                    return;
                case 109:
                    Launchpad.Toggle();
                    return;
            }
            Toggled?.Invoke();
        }

        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
    }
}
