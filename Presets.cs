// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MizuEngine
{
    internal sealed class PresetInfo
    {
        public string Folder, Name, Theme, Weather;
        public DateTime Created;
    }

    /// <summary>
    /// Сборки: снимок всех настроек + свои файлы (картинка для своей темы, картинка для своих частиц).
    /// Хранятся в %AppData%\MizuEngine\presets\, файлом .mizupreset (обычный zip) ими можно делиться.
    /// </summary>
    internal static class Presets
    {
        const string ThemeFile = "theme.jpg", ParticleFile = "particle.png", Manifest = "preset.json";
        const long MaxImportBytes = 60L * 1024 * 1024;

        public static string Root => Path.Combine(Settings.Folder, "presets");
        public static string CustomDir => Path.Combine(Settings.Folder, "custom");

        public static List<PresetInfo> List()
        {
            var list = new List<PresetInfo>();
            if (!Directory.Exists(Root)) return list;
            foreach (var dir in Directory.GetDirectories(Root))
            {
                var p = Read(dir);
                if (p != null) list.Add(p);
            }
            return list.OrderByDescending(p => p.Created).ToList();
        }

        /// <summary>Сборки без названия из старой версии показываем с новым именем программы.</summary>
        static string FixName(string n) => n == null ? "Preset" : n == Legacy.PresetDefaultName ? "MizuEngine preset" : n;

        static PresetInfo Read(string dir)
        {
            try
            {
                var j = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, Manifest)));
                return new PresetInfo
                {
                    Folder = dir,
                    Name = FixName((string)j["name"]),
                    Theme = (string)j["settings"]?["Theme"],
                    Weather = (string)j["settings"]?["Weather"],
                    Created = DateTime.TryParse((string)j["created"], out var d) ? d : Directory.GetCreationTime(dir)
                };
            }
            catch { return null; }
        }

        /// <summary>Сохраняет текущие настройки (и свои картинки, если они используются) как новую сборку.</summary>
        public static PresetInfo SaveCurrent(Settings s, string name)
        {
            string dir = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            var snap = Snapshot(s);
            if (s.Theme == "custom" && File.Exists(s.CustomThemeImage)) File.Copy(s.CustomThemeImage, Path.Combine(dir, ThemeFile), true);
            if (s.Weather == "custom" && File.Exists(s.CustomParticle)) File.Copy(s.CustomParticle, Path.Combine(dir, ParticleFile), true);
            WriteManifest(dir, name, snap);
            return Read(dir);
        }

        static void WriteManifest(string dir, string name, JsonNode snap)
        {
            var manifest = new JsonObject
            {
                ["format"] = 1,
                ["name"] = string.IsNullOrWhiteSpace(name) ? "MizuEngine preset" : name.Trim(),
                ["created"] = DateTime.Now.ToString("o"),
                ["settings"] = snap
            };
            File.WriteAllText(Path.Combine(dir, Manifest), manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        /// <summary>Снимок настроек для сборки (без своих картинок — они лежат рядом отдельными файлами).</summary>
        public static JsonObject Snapshot(Settings s) => new JsonObject
        {
            ["Theme"] = s.Theme, ["CustomAccent"] = s.CustomAccent,
            ["EffectsOn"] = s.EffectsOn, ["Weather"] = s.Weather, ["Flakes"] = s.Flakes,
            ["SnowOnWindows"] = s.SnowOnWindows, ["GroundDrift"] = s.GroundDrift, ["CursorScrape"] = s.CursorScrape, ["CursorTrail"] = s.CursorTrail, ["CursorReact"] = s.CursorReact,
            ["DockSize"] = s.DockSize, ["DockMagnify"] = s.DockMagnify, ["DockTransparency"] = s.DockTransparency,
            ["DockClock"] = s.DockClock, ["DockBlur"] = s.DockBlur, ["DockAutohide"] = s.DockAutohide,
            ["CursorPack"] = s.CursorPack == "custom" ? "" : s.CursorPack, ["CursorSize"] = s.CursorSize,
            ["BorderMode"] = s.BorderMode, ["BorderColor"] = s.BorderColor, ["Corners"] = s.Corners, ["VideoDim"] = s.VideoDim
        };

        /// <summary>Сохранить готовую сборку (например, сборку от MizuEngine) к себе в «Мои сборки».</summary>
        public static PresetInfo SaveSnapshot(string name, JsonNode snap, string themeFile, string particleFile)
        {
            string dir = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            if (File.Exists(themeFile)) File.Copy(themeFile, Path.Combine(dir, ThemeFile), true);
            if (File.Exists(particleFile)) File.Copy(particleFile, Path.Combine(dir, ParticleFile), true);
            WriteManifest(dir, name, snap.DeepClone());
            return Read(dir);
        }

        /// <summary>Применяет сборку к настройкам. Окно, эффекты и док потом перечитывают настройки.</summary>
        public static void Apply(PresetInfo p, Settings s)
        {
            var j = JsonNode.Parse(File.ReadAllText(Path.Combine(p.Folder, Manifest)))?["settings"];
            if (j == null) return;
            ApplySnapshot(j, s, Path.Combine(p.Folder, ThemeFile), Path.Combine(p.Folder, ParticleFile));
        }

        /// <summary>Применяет снимок настроек. themeFile / particleFile — картинки своей темы и своих частиц (могут отсутствовать).</summary>
        public static void ApplySnapshot(JsonNode j, Settings s, string themeFile, string particleFile)
        {
            Directory.CreateDirectory(CustomDir);

            string theme = (string)j["Theme"] ?? s.Theme;
            if (theme == "custom")
            {
                if (File.Exists(themeFile))
                {
                    s.CustomThemeImage = CopyToCustom(themeFile, "theme");
                    s.CustomAccent = (string)j["CustomAccent"] ?? s.CustomAccent;
                }
                else theme = "blue";
            }
            // Видео в сборку не кладётся (слишком большое): если своего видео нет — обычная тема
            if (theme == "video" && !File.Exists(s.VideoTheme)) theme = "blue";
            s.Theme = theme;

            string weather = (string)j["Weather"] ?? s.Weather;
            if (weather == "custom")
            {
                if (File.Exists(particleFile)) s.CustomParticle = CopyToCustom(particleFile, "particle");
                else weather = "snow";
            }
            s.Weather = weather;

            if (j["CursorPack"] != null) s.CursorPack = Legacy.FixCursorPack((string)j["CursorPack"] ?? "");
            s.CursorSize = (int?)j["CursorSize"] ?? s.CursorSize;
            s.BorderMode = (string)j["BorderMode"] ?? s.BorderMode;
            s.BorderColor = (string)j["BorderColor"] ?? s.BorderColor;
            s.Corners = (string)j["Corners"] ?? s.Corners;
            s.VideoDim = (int?)j["VideoDim"] ?? s.VideoDim;

            s.EffectsOn = (bool?)j["EffectsOn"] ?? s.EffectsOn;
            s.Flakes = (int?)j["Flakes"] ?? s.Flakes;
            s.SnowOnWindows = (bool?)j["SnowOnWindows"] ?? s.SnowOnWindows;
            s.GroundDrift = (bool?)j["GroundDrift"] ?? s.GroundDrift;
            s.CursorScrape = (bool?)j["CursorScrape"] ?? s.CursorScrape;
            s.CursorTrail = (bool?)j["CursorTrail"] ?? s.CursorTrail;
            s.CursorReact = (bool?)j["CursorReact"] ?? s.CursorReact;
            s.DockSize = (int?)j["DockSize"] ?? s.DockSize;
            s.DockMagnify = (double?)j["DockMagnify"] ?? s.DockMagnify;
            s.DockTransparency = (int?)j["DockTransparency"] ?? s.DockTransparency;
            s.DockClock = (bool?)j["DockClock"] ?? s.DockClock;
            s.DockBlur = (bool?)j["DockBlur"] ?? s.DockBlur;
            s.DockAutohide = (bool?)j["DockAutohide"] ?? s.DockAutohide;
            s.Clamp();
            s.Save();
        }

        /// <summary>Своя картинка (фон темы или частица) копируется к нам — чтобы не пропала, если исходник удалят.</summary>
        public static string CopyToCustom(string file, string kind)
        {
            Directory.CreateDirectory(CustomDir);
            string dest = Path.Combine(CustomDir, $"{kind}-{DateTime.Now.Ticks}{Path.GetExtension(file)}");
            File.Copy(file, dest, true);
            return dest;
        }

        public static string Export(PresetInfo p, string file)
        {
            try
            {
                if (File.Exists(file)) File.Delete(file);
                ZipFile.CreateFromDirectory(p.Folder, file);
                return file;
            }
            catch (Exception e) { Settings.Log(e); return null; }
        }

        /// <summary>Импорт .mizupreset. Проверяем, что это действительно сборка и что архив не огромный.</summary>
        public static PresetInfo Import(string file)
        {
            string dir = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            try
            {
                using (var zip = ZipFile.OpenRead(file))
                {
                    if (zip.GetEntry(Manifest) == null) return null;
                    if (zip.Entries.Sum(e => e.Length) > MaxImportBytes) return null;
                    Directory.CreateDirectory(dir);
                    foreach (var e in zip.Entries)
                    {
                        // Берём только известные файлы, без папок — никакой «хитрый» архив не запишет ничего лишнего
                        if (e.FullName != Manifest && e.FullName != ThemeFile && e.FullName != ParticleFile) continue;
                        e.ExtractToFile(Path.Combine(dir, e.FullName), true);
                    }
                }
                var p = Read(dir);
                if (p == null) Directory.Delete(dir, true);
                return p;
            }
            catch (Exception e)
            {
                Settings.Log(e);
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
                return null;
            }
        }

        public static void Delete(PresetInfo p)
        {
            try { Directory.Delete(p.Folder, true); } catch (Exception e) { Settings.Log(e); }
        }
    }
}
