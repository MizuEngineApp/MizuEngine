// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shell;

namespace MizuEngine
{
    /// <summary>
    /// 4 темы: жидкое стекло, синяя, тёмная, светлая.
    /// Цвета кладутся в ресурсы приложения, XAML берёт их через DynamicResource — поэтому тема меняется на лету.
    /// </summary>
    internal static class Themes
    {
        public static readonly string[] All = { "glass", "blue", "dark", "light" };

        /// <summary>Живые темы Mizu Plus: фон медленно переливается (рисует MainWindow.LiveBackground).</summary>
        public static readonly string[] Live = { "live_aurora", "live_nebula", "live_ocean", "live_sunset", "live_sakura", "live_mint" };

        /// <summary>Цвета живых тем: фон (2 цвета) и 3 светящихся «облака», которые плывут по окну.</summary>
        public static readonly Dictionary<string, (string[] bg, string[] blobs, string accent)> LiveColors = new Dictionary<string, (string[], string[], string)>
        {
            ["live_aurora"] = (new[] { "#FF06121F", "#FF0A1E2E" }, new[] { "#FF2EE6A8", "#FF2E8BFF", "#FF8B5CF6" }, "#FF3DE0B0"),
            ["live_nebula"] = (new[] { "#FF0E0820", "#FF1A0B33" }, new[] { "#FF8B5CF6", "#FFE056FD", "#FF3B82F6" }, "#FFB794FF"),
            ["live_ocean"] = (new[] { "#FF04142A", "#FF062544" }, new[] { "#FF22D3EE", "#FF1D4FE0", "#FF2EE6A8" }, "#FF38C8F0"),
            ["live_sunset"] = (new[] { "#FF1F0A16", "#FF2E0F12" }, new[] { "#FFFF6A3D", "#FFFF3D7F", "#FFFFC93D" }, "#FFFF8A5C"),
            ["live_sakura"] = (new[] { "#FF1E0F1C", "#FF2A1226" }, new[] { "#FFFF8FB8", "#FFC97BFF", "#FFFFC2D8" }, "#FFFF8FB8"),
            ["live_mint"] = (new[] { "#FF07181A", "#FF0B2522" }, new[] { "#FF3DDC84", "#FF22D3EE", "#FFB8F5A0" }, "#FF45E0A0"),
        };

        public static bool IsLive(string name) => name != null && LiveColors.ContainsKey(name);
        /// <summary>Тема только для Mizu Plus (живая или из видео).</summary>
        public static bool IsPlusTheme(string name) => name == "video" || IsLive(name);

        // Видео-тема (Mizu Plus): фон — MP4 пользователя (рисует MainWindow), панели — затемнённое стекло
        public static string VideoFile;

        public static Brush LivePreview(string name)
        {
            var (bg, blobs, _) = LiveColors[name];
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            g.GradientStops.Add(new GradientStop(C(blobs[0]), 0));
            g.GradientStops.Add(new GradientStop(C(bg[1]), 0.5));
            g.GradientStops.Add(new GradientStop(C(blobs[1]), 1));
            g.Freeze();
            return g;
        }

        // Настоящее размытие фона (Acrylic) есть только в Windows 11 22H2+
        static bool Win11 => Environment.OSVersion.Version.Build >= 22621;

        // Фон окна каждой темы (для стекла — вариант без размытия, он же превью)
        static readonly Dictionary<string, string[]> WinBg = new Dictionary<string, string[]>
        {
            ["glass"] = new[] { "#FF6A7B99", "#FF3A4763", "#FF7B8AA6" },
            ["blue"] = new[] { "#FF0B2D86", "#FF061642", "#FF0F49C4" },
            ["dark"] = new[] { "#FF171A21", "#FF0C0E12" },
            ["light"] = new[] { "#FFF5F8FC", "#FFE4EBF5" },
        };

        public static Brush Preview(string name) => G(WinBg[name]);

        // Своя тема: фон — картинка пользователя, акцент — выбранный цвет
        public static string CustomImage;
        public static string CustomAccent = "#FF4A97FF";

        /// <summary>Тема, которая сейчас на самом деле включена (с учётом подписки и файлов).</summary>
        public static string Current { get; private set; } = "blue";

        /// <summary>Тема применена — например, надпись «MizuEngine» перекрашивает свой перелив.</summary>
        public static event Action Changed;

        public static void Apply(string name, Window window)
        {
            ApplyCore(name, window);
            Changed?.Invoke();
        }

        static void ApplyCore(string name, Window window)
        {
            if (name == "custom" && !System.IO.File.Exists(CustomImage)) name = "blue";
            // Темы Mizu Plus без подписки не включаются (выбор сохраняется — вернётся вместе с Plus)
            if (IsPlusTheme(name) && !Plus.IsActive) name = "blue";
            if (name == "video" && !System.IO.File.Exists(VideoFile)) name = "blue";
            if (name != "custom" && !IsPlusTheme(name) && Array.IndexOf(All, name) < 0) name = "blue";
            Current = name;
            bool acrylic = name == "glass" && Win11 && !SafeMode;
            var r = Application.Current.Resources;

            if (name == "custom")
            {
                Brush bg;
                try
                {
                    var img = new System.Windows.Media.Imaging.BitmapImage();
                    img.BeginInit();
                    img.UriSource = new Uri(CustomImage);
                    img.DecodePixelWidth = 1600;
                    img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    img.EndInit();
                    img.Freeze();
                    bg = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
                    bg.Freeze();
                }
                catch { bg = G(WinBg["blue"]); }
                string acc = CustomAccent ?? "#FF4A97FF";
                // Поверх картинки — затемнённые стеклянные панели, чтобы текст читался на любом фоне
                Set(r, bg,
                    side: S("#66000000"), card: G("#55101622", "#33101622"), rim: G("#B0FFFFFF", "#18FFFFFF", "#50FFFFFF"),
                    shine: V("#20FFFFFF", "#00FFFFFF"), hover: S("#20FFFFFF"), navSel: G("#40FFFFFF", "#20FFFFFF"),
                    text: "#FFFFFFFF", sub: "#D0E6EEFA", accent: acc, trackOff: "#40FFFFFF", divider: "#30FFFFFF",
                    hero: G("#C0FFFFFF", "#40FFFFFF"), glow: acc, shadow: 0.35);
                Hero(r, tile: G("#70FFFFFF", "#30FFFFFF"), rim: G("#F0FFFFFF", "#30FFFFFF", "#90FFFFFF"),
                     n: G("#FFFFFFFF", "#FFDCE8FF"), ring: "#FFFFFFFF", glow: acc);
                if (window != null) Backdrop(window, false, true);
                return;
            }

            if (IsPlusTheme(name))
            {
                // Фон рисует окно (видео или переливающиеся «облака»), здесь — только тёмная подложка и стеклянные панели
                string acc = name == "video" ? (CustomAccent ?? "#FF4A97FF") : LiveColors[name].accent;
                Brush bg = name == "video" ? S("#FF000000") : G(LiveColors[name].bg);
                Set(r, bg,
                    side: S("#59000000"), card: G("#4A0E1220", "#2A0E1220"), rim: G("#A0FFFFFF", "#16FFFFFF", "#48FFFFFF"),
                    shine: V("#1CFFFFFF", "#00FFFFFF"), hover: S("#20FFFFFF"), navSel: G("#40FFFFFF", "#1CFFFFFF"),
                    text: "#FFFFFFFF", sub: "#D0E6EEFA", accent: acc, trackOff: "#40FFFFFF", divider: "#2CFFFFFF",
                    hero: G("#C0FFFFFF", "#40FFFFFF"), glow: acc, shadow: 0.35);
                Hero(r, tile: G("#70FFFFFF", "#30FFFFFF"), rim: G("#F0FFFFFF", "#30FFFFFF", "#90FFFFFF"),
                     n: G("#FFFFFFFF", "#FFDCE8FF"), ring: "#FFFFFFFF", glow: acc);
                if (window != null) Backdrop(window, false, true);
                return;
            }

            switch (name)
            {
                case "glass":
                    Set(r, acrylic ? G("#5A1B2C4A", "#3A0E1626", "#552A3F66") : G(WinBg[name]),
                        side: S("#1AFFFFFF"), card: G("#30FFFFFF", "#12FFFFFF"), rim: G("#B0FFFFFF", "#18FFFFFF", "#50FFFFFF"),
                        shine: V("#28FFFFFF", "#00FFFFFF"), hover: S("#18FFFFFF"), navSel: G("#38FFFFFF", "#18FFFFFF"),
                        text: "#FFFFFFFF", sub: "#CCE6EEFA", accent: "#FF5AA8FF", trackOff: "#33FFFFFF", divider: "#22FFFFFF",
                        hero: G("#B0FFFFFF", "#30DDE8FF", "#60FFFFFF"), glow: "#FF9CC8FF", shadow: 0.30);
                    Hero(r, tile: G("#70FFFFFF", "#26FFFFFF", "#40DCE8FF"), rim: G("#F0FFFFFF", "#30FFFFFF", "#90FFFFFF"),
                         n: G("#FFFFFFFF", "#FFDCE8FF", "#FFA9C1EE"), ring: "#FFFFFFFF", glow: "#FFCFE2FF");
                    break;
                case "blue":
                    Set(r, G(WinBg[name]),
                        side: S("#26000820"), card: G("#40407CFF", "#1A1E4FD0"), rim: G("#A07FB4FF", "#203A6FE0", "#607FB4FF"),
                        shine: V("#20FFFFFF", "#00FFFFFF"), hover: S("#1FFFFFFF"), navSel: G("#FF2F6BFF", "#FF1D4FE0"),
                        text: "#FFFFFFFF", sub: "#CCC2D6F8", accent: "#FF4C9AFF", trackOff: "#33FFFFFF", divider: "#26FFFFFF",
                        hero: G("#FF6FB6FF", "#FF2F6BFF", "#FF0B3BB0"), glow: "#FF2F7BFF", shadow: 0.35);
                    Hero(r, tile: G("#B03A6BFF", "#E0102E8C", "#C0153AA8"), rim: G("#FF9CC8FF", "#402F6BFF", "#B06FB6FF"),
                         n: G("#FFE0F2FF", "#FF7CC4FF", "#FF2F7BFF"), ring: "#FFBFE0FF", glow: "#FF3B8CFF");
                    break;
                case "dark":
                    Set(r, G(WinBg[name]),
                        side: S("#FF101217"), card: G("#FF1E222B", "#FF171A21"), rim: G("#FF343A47", "#FF22262F"),
                        shine: S("#00FFFFFF"), hover: S("#14FFFFFF"), navSel: S("#FF1F3A66"),
                        text: "#FFF3F5F9", sub: "#FF98A2B3", accent: "#FF3B82F6", trackOff: "#FF2A2F3A", divider: "#FF1E222A",
                        hero: G("#FF3A4150", "#FF14171D"), glow: "#FF3B82F6", shadow: 0.40);
                    Hero(r, tile: G("#FF2C313C", "#FF13161C", "#FF1B1F27"), rim: G("#FF6B7385", "#FF1E222A", "#FF3A4150"),
                         n: G("#FFFFFFFF", "#FFC9D2E2", "#FF8C97AD"), ring: "#FFEAF0FA", glow: "#FFB8C6E0");
                    break;
                default: // light
                    Set(r, G(WinBg[name]),
                        side: S("#B3FFFFFF"), card: G("#FFFFFFFF", "#FFF4F7FB"), rim: G("#FFFFFFFF", "#FFD9E1EC"),
                        shine: S("#00FFFFFF"), hover: S("#0F1E3A8A"), navSel: S("#FFDCE8FC"),
                        text: "#FF0F172A", sub: "#FF5B6577", accent: "#FF2563EB", trackOff: "#FFCBD5E1", divider: "#FFE2E8F0",
                        hero: G("#FFE8EEF8", "#FFB9CBE6"), glow: "#FF93B4E8", shadow: 0.12);
                    Hero(r, tile: G("#FFFFFFFF", "#FFEAF1FB", "#FFD6E2F3"), rim: G("#FFFFFFFF", "#FFC9D6EA", "#FFFFFFFF"),
                         n: G("#FF7CB8FF", "#FF3B82F6", "#FF1D4ED8"), ring: "#FF9CC2F5", glow: "#FF7AA8F0");
                    break;
            }

            if (window != null) Backdrop(window, acrylic, name != "light");
        }

        static void Set(ResourceDictionary r, Brush winBg, Brush side, Brush card, Brush rim, Brush shine, Brush hover, Brush navSel,
                        string text, string sub, string accent, string trackOff, string divider, Brush hero, string glow, double shadow)
        {
            r["WinBg"] = winBg; r["SideBg"] = side; r["CardBg"] = card; r["CardRim"] = rim; r["CardShine"] = shine;
            r["HoverBg"] = hover; r["NavSel"] = navSel; r["Text"] = S(text); r["SubText"] = S(sub); r["Accent"] = S(accent);
            r["TrackOff"] = S(trackOff); r["Divider"] = S(divider); r["HeroBrush"] = hero;

            var glowFx = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 60, Opacity = 0.9, Color = C(glow) };
            glowFx.Freeze();
            r["HeroGlowEffect"] = glowFx;

            var shadowFx = new DropShadowEffect { ShadowDepth = 6, Direction = 270, BlurRadius = 24, Opacity = shadow, Color = Colors.Black };
            shadowFx.Freeze();
            r["CardShadow"] = shadowFx;
        }

        /// <summary>Плитка-значок с буквой N и световым кольцом на главной — под цвет темы.</summary>
        static void Hero(ResourceDictionary r, Brush tile, Brush rim, Brush n, string ring, string glow)
        {
            r["TileBg"] = tile; r["TileRim"] = rim; r["LogoFill"] = n;
            var ringBrush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            ringBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
            ringBrush.GradientStops.Add(new GradientStop(C(ring), 0.35));
            ringBrush.GradientStops.Add(new GradientStop(C(ring), 0.7));
            ringBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
            ringBrush.Freeze();
            r["RingBrush"] = ringBrush;
            var fx = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 28, Opacity = 0.95, Color = C(glow) };
            fx.Freeze();
            r["LogoGlow"] = fx;
        }

        /// <summary>
        /// Безопасный режим: окно рисуется без прозрачности и без стекла Windows. На ПК без нормального видеодрайвера,
        /// в виртуальной машине или через удалённый рабочий стол прозрачное окно бывает пустым (видно только размытый фон).
        /// Включается сам на таких ПК или вручную: «Настройки → Окно» или меню значка в трее.
        /// </summary>
        public static bool SafeMode;

        static void Backdrop(Window w, bool acrylic, bool dark)
        {
            IntPtr hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            if (SafeMode)
            {
                int none = 1;
                if (Win11) DwmSetWindowAttribute(hwnd, 38, ref none, 4);   // без системного стекла
                WindowChrome.SetWindowChrome(w, new WindowChrome
                {
                    CaptionHeight = 44, ResizeBorderThickness = new Thickness(6), CornerRadius = new CornerRadius(0),
                    UseAeroCaptionButtons = false, GlassFrameThickness = new Thickness(0)
                });
                HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = dark ? Color.FromRgb(0x10, 0x14, 0x20) : Color.FromRgb(0xF4, 0xF7, 0xFB);
                return;
            }

            int v = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 20, ref v, 4);      // DWMWA_USE_IMMERSIVE_DARK_MODE
            v = 2;
            DwmSetWindowAttribute(hwnd, 33, ref v, 4);      // DWMWA_WINDOW_CORNER_PREFERENCE = скруглённые
            if (Win11)
            {
                v = acrylic ? 3 : 1;
                DwmSetWindowAttribute(hwnd, 38, ref v, 4);  // DWMWA_SYSTEMBACKDROP_TYPE: 3 = Acrylic, 1 = нет
            }

            WindowChrome.SetWindowChrome(w, new WindowChrome
            {
                CaptionHeight = 44,
                ResizeBorderThickness = new Thickness(6),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false,
                GlassFrameThickness = acrylic ? new Thickness(-1) : new Thickness(0)
            });
            HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // ---------- помощники для цветов ----------
        static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        static Brush S(string hex)
        {
            var b = new SolidColorBrush(C(hex));
            b.Freeze();
            return b;
        }

        static Brush G(params string[] hex) => Grad(new Point(0, 0), new Point(1, 1), hex);
        static Brush V(params string[] hex) => Grad(new Point(0, 0), new Point(0, 0.6), hex);

        static Brush Grad(Point a, Point b, string[] hex)
        {
            var g = new LinearGradientBrush { StartPoint = a, EndPoint = b };
            for (int i = 0; i < hex.Length; i++)
                g.GradientStops.Add(new GradientStop(C(hex[i]), hex.Length == 1 ? 0 : (double)i / (hex.Length - 1)));
            g.Freeze();
            return g;
        }
    }
}
