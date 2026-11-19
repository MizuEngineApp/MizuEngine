// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MizuEngine
{
    /// <summary>
    /// Инструменты, которыми агент управляет MizuEngine: эффекты, тема, док, курсоры, виджеты, рамки окон,
    /// Dynamic Island, звуки, сборки, обои Wallpaper Engine и разделы окна.
    /// Всё выполняется в главном потоке программы — как если бы человек сам нажал переключатель.
    /// Аккаунт, пароль, оплату, чаты и файлы агент не трогает.
    /// </summary>
    internal static class MuzuleTools
    {
        static Settings S => Muzule.S;
        static Effects Fx => Muzule.Fx;

        public static readonly string[] Pages = { "home", "effects", "dock", "wallpaper_engine", "live_wallpapers", "live_backgrounds", "presets", "customization", "animations", "profile", "chats", "plus", "settings", "muzule" };

        static string[] EffectNames => Settings.Weathers.Where(w => w != "custom" || File.Exists(S.CustomParticle)).ToArray();
        static string[] Themes_ => global::MizuEngine.Themes.All.Concat(global::MizuEngine.Themes.Live)
            .Concat(File.Exists(S.CustomThemeImage) ? new[] { "custom" } : Array.Empty<string>())
            .Concat(File.Exists(S.VideoTheme) ? new[] { "video" } : Array.Empty<string>()).ToArray();
        static string[] Cursors => new[] { "default" }.Concat(CursorPacks.Packs).Concat(CursorPacks.PlusPacks).ToArray();

        // ================== описание для Claude ==================

        /// <summary>Список инструментов для запроса. Агенту без права управления — только поиск (если включён).</summary>
        public static JsonArray Definitions(MuzuleAgent a)
        {
            var list = new JsonArray();
            if (a.Control)
            {
                list.Add(Tool("get_mizu_state", "Get the current MizuEngine settings (effects, theme, dock, cursor, widgets, window style, Dynamic Island, sounds) and all allowed option values. Call it before changing something when you need current values.", Obj()));
                list.Add(Tool("set_effects", "Desktop effects drawn over the wallpaper and windows. Set only the fields you want to change.", Obj(
                    ("enabled", Bool("Effects on/off")),
                    ("effect", Enum("Which effect. snow, rain, sakura, fireflies, blizzard, leaves, roses, confetti, bubbles, butterflies, pollen, embers, stars (with shooting stars), aurora, matrix, garland (Christmas lights), hearts, glitter, crystals, lanterns, meteors, fluff; custom = user's own PNG particles", EffectNames)),
                    ("density", Int("Amount of particles, 50 (light) … 260 (medium) … 600 (heavy) … 1500 (blizzard)", 50, 1500)),
                    ("snow_on_windows", Bool("Snow piles up on top of windows")),
                    ("ground_drift", Bool("Snowdrift above the taskbar/dock")),
                    ("cursor_scrape", Bool("Cursor brushes snow away")),
                    ("cursor_trail", Bool("Sparkle trail behind the cursor")),
                    ("cursor_react", Bool("Particles react to the cursor (leaves fly away, bubbles pop…)")))));
                list.Add(Tool("set_theme", "Color theme of the MizuEngine window. glass = liquid glass, blue, dark, light; live_* = animated live themes; custom = user's picture; video = user's video.", Obj(
                    ("theme", Enum("Theme name", Themes_))), "theme"));
                list.Add(Tool("set_dock", "macOS-style dock that replaces the Windows taskbar. Set only the fields you want to change.", Obj(
                    ("enabled", Bool("Dock on (the Windows taskbar is hidden) / off (taskbar comes back)")),
                    ("size", Int("Icon size in px", 32, 80)),
                    ("magnify", Num("Magnification under the cursor, 1.0 = none", 1.0, 2.2)),
                    ("transparency", Int("Glass transparency, %", 0, 100)),
                    ("autohide", Bool("Hide the dock until the mouse reaches the bottom edge")),
                    ("clock", Bool("Clock in the dock")),
                    ("blur", Bool("Blurred glass background")),
                    ("animations", Bool("Genie minimize/restore animations")),
                    ("tray", Bool("Tray icons inside the dock")))));
                list.Add(Tool("set_cursor", "Mouse cursor set drawn by MizuEngine. default = standard Windows cursors.", Obj(
                    ("pack", Enum("Cursor set: mac, snow (white), neon, sakura, galaxy, gold; Plus sets: mizu, ice, rainbow, lava, mint, midnight, cherry, aqua", Cursors)),
                    ("size", Enum("Size in px", new[] { "32", "48", "64" })))));
                list.Add(Tool("set_overlays", "FPS counter and desktop widgets. Set only the fields you want to change.", Obj(
                    ("fps", Bool("FPS counter on top of games (exclusive fullscreen is not supported)")),
                    ("fps_detailed", Bool("Also show CPU/GPU load in the FPS counter")),
                    ("widget_system", Bool("System widget: CPU, GPU, RAM")),
                    ("widget_clock", Bool("Clock widget")),
                    ("widget_weather", Bool("Weather widget")),
                    ("weather_city", Str("City for the weather widget, empty = automatic")),
                    ("widgets_on_top", Bool("Widgets stay on top of windows")))));
                list.Add(Tool("set_window_style", "Window borders and corners (Windows 11 only).", Obj(
                    ("border_mode", Enum("off, accent (theme color), color (border_color), rainbow", new[] { "off", "accent", "color", "rainbow" })),
                    ("border_color", Str("Border color as #RRGGBB, used with border_mode=color")),
                    ("corners", Enum("default, round, square", new[] { "default", "round", "square" })))));
                list.Add(Tool("set_island", "Dynamic Island: black capsule at the top of the screen (music, notifications).", Obj(
                    ("enabled", Bool("Island on/off")),
                    ("media", Bool("Show music from any player")),
                    ("notify", Bool("Show MizuEngine message notifications")),
                    ("hide_idle", Bool("Hide when nothing is happening")))));
                list.Add(Tool("set_ui_sounds", "Soft click sounds of the MizuEngine interface.", Obj(
                    ("enabled", Bool("Sounds on/off")),
                    ("pack", Enum("Sound set", UiSounds.Packs)),
                    ("volume", Int("Volume, %", 0, 100)))));
                list.Add(Tool("presets", "Presets (saved sets of all settings). list = show saved presets; apply = apply a preset by name; save = save current settings as a new preset.", Obj(
                    ("action", Enum("What to do", new[] { "list", "apply", "save" })),
                    ("name", Str("Preset name for apply/save"))), "action"));
                list.Add(Tool("random_wallpaper", "Apply a random wallpaper from Wallpaper Engine (if it is installed and has wallpapers).", Obj()));
                list.Add(Tool("open_page", "Open the MizuEngine window on a section, so the user can see or fine-tune something themselves.", Obj(
                    ("page", Enum("Section", Pages))), "page"));
            }
            if (a.Web)
                list.Add(new JsonObject { ["type"] = "web_search_20250305", ["name"] = "web_search", ["max_uses"] = 5 });
            return list;
        }

        static JsonObject Tool(string name, string desc, JsonObject schema, params string[] required)
        {
            if (required.Length > 0) schema["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray());
            return new JsonObject { ["name"] = name, ["description"] = desc, ["input_schema"] = schema };
        }

        static JsonObject Obj(params (string name, JsonObject prop)[] props)
        {
            var p = new JsonObject();
            foreach (var (n, v) in props) p[n] = v;
            return new JsonObject { ["type"] = "object", ["properties"] = p };
        }

        static JsonObject Bool(string d) => new JsonObject { ["type"] = "boolean", ["description"] = d };
        static JsonObject Str(string d) => new JsonObject { ["type"] = "string", ["description"] = d };
        static JsonObject Int(string d, int min, int max) => new JsonObject { ["type"] = "integer", ["description"] = d, ["minimum"] = min, ["maximum"] = max };
        static JsonObject Num(string d, double min, double max) => new JsonObject { ["type"] = "number", ["description"] = d, ["minimum"] = min, ["maximum"] = max };
        static JsonObject Enum(string d, IEnumerable<string> values) =>
            new JsonObject { ["type"] = "string", ["description"] = d, ["enum"] = new JsonArray(values.Select(v => (JsonNode)v).ToArray()) };

        // ================== выполнение ==================

        /// <summary>Выполняет инструмент. Возвращает текст для Claude и успех. Звать в главном потоке.</summary>
        public static (string result, bool ok) Execute(string name, JsonNode input)
        {
            try
            {
                var i = input as JsonObject ?? new JsonObject();
                string r = name switch
                {
                    "get_mizu_state" => State().ToJsonString(),
                    "set_effects" => SetEffects(i),
                    "set_theme" => SetTheme(i),
                    "set_dock" => SetDock(i),
                    "set_cursor" => SetCursor(i),
                    "set_overlays" => SetOverlays(i),
                    "set_window_style" => SetWindowStyle(i),
                    "set_island" => SetIsland(i),
                    "set_ui_sounds" => SetSounds(i),
                    "presets" => DoPresets(i),
                    "random_wallpaper" => RandomWallpaper(),
                    "open_page" => OpenPage(i),
                    _ => throw new ToolError("Unknown tool: " + name)
                };
                return (r, true);
            }
            catch (ToolError e) { return ("Error: " + e.Message, false); }
            catch (Exception e) { Settings.Log(e); return ("Error: " + e.Message, false); }
        }

        sealed class ToolError : Exception { public ToolError(string m) : base(m) { } }

        // --- чтение значений из ввода Claude ---

        static bool? B(JsonObject i, string k)
        {
            if (i[k] is not JsonValue v) return null;
            if (v.TryGetValue(out bool b)) return b;
            if (v.TryGetValue(out string s) && bool.TryParse(s, out b)) return b;
            return null;
        }

        static double? D(JsonObject i, string k)
        {
            if (i[k] is not JsonValue v) return null;
            if (v.TryGetValue(out double d)) return d;
            if (v.TryGetValue(out string s) && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) return d;
            return null;
        }

        static string Sv(JsonObject i, string k)
        {
            if (i[k] is not JsonValue v) return null;
            if (v.TryGetValue(out string s)) return s;
            if (v.TryGetValue(out double d)) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return null;
        }

        static string Pick(string value, string[] allowed, string what)
        {
            if (value == null) return null;
            var m = allowed.FirstOrDefault(a => string.Equals(a, value.Trim(), StringComparison.OrdinalIgnoreCase));
            if (m == null) throw new ToolError($"Unknown {what} '{value}'. Allowed: {string.Join(", ", allowed)}");
            return m;
        }

        static void Changed()
        {
            S.Save();
            MizuHotkeys.NotifyChanged();   // открытое окно обновит свои переключатели
        }

        // --- состояние ---

        static JsonObject State() => new JsonObject
        {
            ["effects"] = new JsonObject
            {
                ["enabled"] = S.EffectsOn, ["effect"] = S.Weather, ["density"] = S.Flakes,
                ["snow_on_windows"] = S.SnowOnWindows, ["ground_drift"] = S.GroundDrift, ["cursor_scrape"] = S.CursorScrape,
                ["cursor_trail"] = S.CursorTrail, ["cursor_react"] = S.CursorReact
            },
            ["theme"] = S.Theme,
            ["dock"] = new JsonObject
            {
                ["enabled"] = S.DockEnabled, ["size"] = S.DockSize, ["magnify"] = S.DockMagnify, ["transparency"] = S.DockTransparency,
                ["autohide"] = S.DockAutohide, ["clock"] = S.DockClock, ["blur"] = S.DockBlur, ["animations"] = S.DockAnimations, ["tray"] = S.DockTray
            },
            ["cursor"] = new JsonObject { ["pack"] = string.IsNullOrEmpty(S.CursorPack) ? "default" : S.CursorPack, ["size"] = S.CursorSize },
            ["overlays"] = new JsonObject
            {
                ["fps"] = S.FpsOn, ["fps_detailed"] = S.FpsDetailed, ["widget_system"] = S.WidgetSystem, ["widget_clock"] = S.WidgetClock,
                ["widget_weather"] = S.WidgetWeather, ["weather_city"] = S.WeatherCity ?? "", ["widgets_on_top"] = S.WidgetsOnTop
            },
            ["window_style"] = new JsonObject { ["border_mode"] = S.BorderMode, ["border_color"] = S.BorderColor, ["corners"] = S.Corners },
            ["island"] = new JsonObject { ["enabled"] = S.IslandOn, ["media"] = S.IslandMedia, ["notify"] = S.IslandNotify, ["hide_idle"] = S.IslandHideIdle },
            ["ui_sounds"] = new JsonObject { ["enabled"] = S.Sounds, ["pack"] = S.SoundPack, ["volume"] = S.UiVolume },
            ["mizu_plus"] = Plus.IsActive,
            ["windows_11"] = Environment.OSVersion.Version.Build >= 22000,
            ["app_language"] = Loc.Code,
            ["options"] = new JsonObject
            {
                ["effects"] = new JsonArray(EffectNames.Select(x => (JsonNode)x).ToArray()),
                ["themes"] = new JsonArray(Themes_.Select(x => (JsonNode)x).ToArray()),
                ["cursor_packs"] = new JsonArray(Cursors.Select(x => (JsonNode)x).ToArray()),
                ["sound_packs"] = new JsonArray(UiSounds.Packs.Select(x => (JsonNode)x).ToArray())
            }
        };

        // --- эффекты ---

        static string SetEffects(JsonObject i)
        {
            var done = new List<string>();
            var en = B(i, "enabled");
            if (en.HasValue) { S.EffectsOn = en.Value; done.Add("enabled=" + en.Value); }
            string w = Pick(Sv(i, "effect"), EffectNames, "effect");
            if (w != null)
            {
                if (Settings.IsPlusWeather(w) && !Plus.IsActive) throw new ToolError("This effect needs Mizu Plus");
                S.Weather = w;
                done.Add("effect=" + w);
                if (!en.HasValue && !S.EffectsOn) { S.EffectsOn = true; done.Add("enabled=true"); }   // выбрали эффект — значит, его хотят видеть
            }
            var dn = D(i, "density");
            if (dn.HasValue) { S.Flakes = (int)Math.Clamp(Math.Round(dn.Value), 50, 1500); done.Add("density=" + S.Flakes); }
            Flag(i, "snow_on_windows", v => S.SnowOnWindows = v, done);
            Flag(i, "ground_drift", v => S.GroundDrift = v, done);
            Flag(i, "cursor_scrape", v => S.CursorScrape = v, done);
            Flag(i, "cursor_trail", v => S.CursorTrail = v, done);
            Flag(i, "cursor_react", v => S.CursorReact = v, done);
            if (done.Count == 0) return "Nothing to change";
            var fx = Fx; var s = S;
            fx.Run(() => s.ApplyTo(fx.Snow.Layer));
            Changed();
            return "OK: " + string.Join(", ", done);
        }

        static void Flag(JsonObject i, string key, Action<bool> set, List<string> done)
        {
            var v = B(i, key);
            if (!v.HasValue) return;
            set(v.Value);
            done.Add(key + "=" + v.Value);
        }

        // --- тема ---

        static string SetTheme(JsonObject i)
        {
            string t = Pick(Sv(i, "theme"), Themes_, "theme") ?? throw new ToolError("theme is required");
            if (global::MizuEngine.Themes.IsPlusTheme(t) && !Plus.IsActive) throw new ToolError("This theme needs Mizu Plus");
            S.Theme = t;
            S.FirstRunDone = true;
            S.Save();
            var main = MainWindow.Current;
            if (main != null) main.MuzuleReapplyTheme();
            else global::MizuEngine.Themes.Apply(t, null);
            return "OK: theme=" + t;
        }

        // --- док ---

        static string SetDock(JsonObject i)
        {
            var done = new List<string>();
            Flag(i, "enabled", v => S.DockEnabled = v, done);
            var size = D(i, "size");
            if (size.HasValue) { S.DockSize = (int)Math.Clamp(Math.Round(size.Value), 32, 80); done.Add("size=" + S.DockSize); }
            var mag = D(i, "magnify");
            if (mag.HasValue) { S.DockMagnify = Math.Round(Math.Clamp(mag.Value, 1.0, 2.2), 2); done.Add("magnify=" + S.DockMagnify); }
            var tr = D(i, "transparency");
            if (tr.HasValue) { S.DockTransparency = (int)Math.Clamp(Math.Round(tr.Value), 0, 100); done.Add("transparency=" + S.DockTransparency); }
            Flag(i, "autohide", v => S.DockAutohide = v, done);
            Flag(i, "clock", v => S.DockClock = v, done);
            Flag(i, "blur", v => S.DockBlur = v, done);
            Flag(i, "animations", v => S.DockAnimations = v, done);
            Flag(i, "tray", v => S.DockTray = v, done);
            if (done.Count == 0) return "Nothing to change";
            // SetEnabled сам переходит в поток дока; если док и так включён — просто перечитывает настройки
            Fx.Dock.SetEnabled(S.DockEnabled);   // сам вызывает Shell.SyncAnimations (анимации «джинн»)
            Changed();
            return "OK: " + string.Join(", ", done);
        }

        // --- курсор ---

        static string SetCursor(JsonObject i)
        {
            var done = new List<string>();
            string p = Pick(Sv(i, "pack"), Cursors, "cursor pack");
            if (p != null)
            {
                if (CursorPacks.IsPlusPack(p) && !Plus.IsActive) throw new ToolError("This cursor set needs Mizu Plus");
                S.CursorPack = p == "default" ? "" : p;
                done.Add("pack=" + p);
            }
            var size = D(i, "size");
            if (size.HasValue)
            {
                int sz = (int)size.Value;
                if (sz != 32 && sz != 48 && sz != 64) throw new ToolError("size must be 32, 48 or 64");
                S.CursorSize = sz;
                done.Add("size=" + sz);
            }
            if (done.Count == 0) return "Nothing to change";
            CursorPacks.Apply(S);
            Changed();
            return "OK: " + string.Join(", ", done);
        }

        // --- FPS и виджеты ---

        static string SetOverlays(JsonObject i)
        {
            var done = new List<string>();
            Flag(i, "fps", v => S.FpsOn = v, done);
            Flag(i, "fps_detailed", v => S.FpsDetailed = v, done);
            Flag(i, "widget_system", v => S.WidgetSystem = v, done);
            Flag(i, "widget_clock", v => S.WidgetClock = v, done);
            Flag(i, "widget_weather", v => S.WidgetWeather = v, done);
            Flag(i, "widgets_on_top", v => S.WidgetsOnTop = v, done);
            string city = Sv(i, "weather_city");
            if (city != null) { S.WeatherCity = city.Trim(); done.Add("weather_city=" + S.WeatherCity); }
            if (done.Count == 0) return "Nothing to change";
            bool showWidgets = B(i, "widget_system") == true || B(i, "widget_clock") == true || B(i, "widget_weather") == true;
            var fx = Fx;
            fx.Run(() =>
            {
                if (showWidgets) fx.Overlays.WidgetsHidden = false;   // виджеты могли спрятать горячей клавишей
                fx.Overlays.Apply();
            });
            Changed();
            string note = B(i, "fps") == true ? " (the FPS counter needs administrator rights to count frames; if it shows 0, the user can restart MizuEngine as administrator on the Customization page)" : "";
            return "OK: " + string.Join(", ", done) + note;
        }

        // --- рамки и углы окон ---

        static string SetWindowStyle(JsonObject i)
        {
            var done = new List<string>();
            string mode = Pick(Sv(i, "border_mode"), new[] { "off", "accent", "color", "rainbow" }, "border mode");
            if (mode != null) { S.BorderMode = mode; done.Add("border_mode=" + mode); }
            string col = Sv(i, "border_color");
            if (col != null)
            {
                var c = UI.ParseColor(col.Trim().StartsWith("#") ? col.Trim() : "#" + col.Trim()) ?? throw new ToolError("border_color must be #RRGGBB");
                S.BorderColor = $"#FF{c.R:X2}{c.G:X2}{c.B:X2}";
                if (mode == null) S.BorderMode = "color";
                done.Add("border_color=" + S.BorderColor);
            }
            string corners = Pick(Sv(i, "corners"), new[] { "default", "round", "square" }, "corners");
            if (corners != null) { S.Corners = corners; done.Add("corners=" + corners); }
            if (done.Count == 0) return "Nothing to change";
            var fx = Fx;
            fx.Run(() => fx.Overlays.Apply());
            Changed();
            bool win11 = Environment.OSVersion.Version.Build >= 22000;
            return "OK: " + string.Join(", ", done) + (win11 ? "" : " (note: borders and corners work only on Windows 11, this PC has Windows 10)");
        }

        // --- Dynamic Island ---

        static string SetIsland(JsonObject i)
        {
            var done = new List<string>();
            Flag(i, "enabled", v => S.IslandOn = v, done);
            Flag(i, "media", v => S.IslandMedia = v, done);
            Flag(i, "notify", v => S.IslandNotify = v, done);
            Flag(i, "hide_idle", v => S.IslandHideIdle = v, done);
            if (done.Count == 0) return "Nothing to change";
            global::MizuEngine.Shell.Island?.Apply();
            Changed();
            return "OK: " + string.Join(", ", done);
        }

        // --- звуки интерфейса ---

        static string SetSounds(JsonObject i)
        {
            var done = new List<string>();
            Flag(i, "enabled", v => S.Sounds = v, done);
            string p = Pick(Sv(i, "pack"), UiSounds.Packs, "sound pack");
            if (p != null) { S.SoundPack = p; done.Add("pack=" + p); }
            var vol = D(i, "volume");
            if (vol.HasValue) { S.UiVolume = (int)Math.Clamp(Math.Round(vol.Value), 0, 100); done.Add("volume=" + S.UiVolume); }
            if (done.Count == 0) return "Nothing to change";
            UiSounds.On = S.Sounds;
            UiSounds.Pack = S.SoundPack;
            UiSounds.Volume = S.UiVolume;
            if (S.Sounds) UiSounds.Play(S.SoundPack);   // сразу слышно, как звучит
            Changed();
            return "OK: " + string.Join(", ", done);
        }

        // --- сборки ---

        static string DoPresets(JsonObject i)
        {
            string action = Pick(Sv(i, "action"), new[] { "list", "apply", "save" }, "action") ?? "list";
            string name = Sv(i, "name")?.Trim();
            switch (action)
            {
                case "save":
                {
                    if (string.IsNullOrEmpty(name)) name = "Muzule " + DateTime.Now.ToString("dd.MM HH:mm");
                    if (name.Length > 60) name = name.Substring(0, 60);
                    var p = Presets.SaveCurrent(S, name);
                    return "OK: saved preset '" + (p?.Name ?? name) + "'";
                }
                case "apply":
                {
                    if (string.IsNullOrEmpty(name)) throw new ToolError("name is required");
                    var all = Presets.List();
                    var p = all.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
                            ?? all.FirstOrDefault(x => x.Name != null && x.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (p == null) throw new ToolError("Preset not found. Saved presets: " + string.Join(", ", all.Select(x => x.Name)));
                    Presets.Apply(p, S);
                    S.Clamp();
                    S.Save();
                    ApplyEverything();
                    return "OK: applied preset '" + p.Name + "'";
                }
                default:
                {
                    var all = Presets.List();
                    if (all.Count == 0) return "No saved presets yet";
                    var arr = new JsonArray();
                    foreach (var p in all.Take(40))
                        arr.Add(new JsonObject { ["name"] = p.Name, ["theme"] = p.Theme, ["effect"] = p.Weather, ["created"] = p.Created.ToString("yyyy-MM-dd") });
                    return arr.ToJsonString();
                }
            }
        }

        /// <summary>Перечитать все настройки (после сборки): окно открыто — оно сделает это само и обновит переключатели.</summary>
        static void ApplyEverything()
        {
            var main = MainWindow.Current;
            if (main != null) { main.MuzuleReapplyAll(); return; }
            global::MizuEngine.Themes.CustomImage = S.CustomThemeImage;
            global::MizuEngine.Themes.CustomAccent = S.CustomAccent;
            global::MizuEngine.Themes.VideoFile = S.VideoTheme;
            global::MizuEngine.Themes.Apply(S.Theme, null);
            CursorPacks.Apply(S);
            var fx = Fx; var s = S;
            fx.Run(() =>
            {
                s.ApplyTo(fx.Snow.Layer);
                fx.Dock.ApplySettings();
                try { fx.Overlays.Apply(); } catch (Exception e) { Settings.Log(e); }
            });
        }

        // --- обои и окно ---

        static string RandomWallpaper()
        {
            if (WallpaperEngine.FindExe(out _) == null) return "Error: Wallpaper Engine is not installed on this PC";
            Task.Run(() => { try { WallpaperEngine.ApplyRandom(); } catch (Exception e) { Settings.Log(e); } });
            return "OK: asked Wallpaper Engine to apply a random wallpaper";
        }

        static string OpenPage(JsonObject i)
        {
            string p = Pick(Sv(i, "page"), Pages, "page") ?? "home";
            string nav = p switch
            {
                "effects" => "NavSnow", "dock" => "NavDock", "wallpaper_engine" => "NavWe", "live_wallpapers" => "NavWallpapers",
                "live_backgrounds" => "NavLiveBg", "presets" => "NavPresets", "customization" => "NavCustom", "animations" => "NavAnim",
                "profile" => "NavProfile", "chats" => "NavChats", "plus" => "NavPlus", "settings" => "NavSettings", "muzule" => "NavMuzule",
                _ => "NavHome"
            };
            Muzule.OpenPage(nav);
            return "OK: opened " + p;
        }
    }
}
