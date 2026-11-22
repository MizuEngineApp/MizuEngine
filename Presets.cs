// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MizuEngine
{
    internal sealed class PresetInfo
    {
        public string Folder, Name, Theme, Weather;
        public DateTime Created;
        // 0.11.5
        public int Format;
        public bool Plus, Backup;
        public List<string> Parts = new List<string>();
        public JsonObject Summary;          // что внутри — для карточки
        public string Cover;                // своя обложка (файл) или null — значок MizuEngine
        public long PublishedId;            // опубликована в «Лучших сборках» (id), 0 — нет
        public string Description;
    }

    /// <summary>
    /// Сборки (0.11.5): снимок ВСЕГО оформления MizuEngine по разделам — тема и живой фон окна, эффекты, курсор,
    /// док, Dynamic Island, обои (картинка Windows и живые обои), анимации, звуки, виджеты и FPS, рамки окон —
    /// плюс свои файлы (картинки, видео, курсор, звуки). Хранятся в %AppData%\MizuEngine\presets\(id)\,
    /// файлом .mizupreset (обычный zip) ими можно делиться. Старые сборки (формат 1) открываются как раньше.
    /// Закреплённые значки дока, позиции виджетов и город погоды в сборку НЕ входят — это личное.
    /// </summary>
    internal static class Presets
    {
        const string Manifest = "preset.json", FilesDir = "files";
        const string OldThemeFile = "theme.jpg", OldParticleFile = "particle.png";   // формат 1
        public const int MaxBackups = 3;
        const long MaxImportBytes = 300L * 1024 * 1024;          // свой файл от друга может быть с большим видео
        public const long MaxPublishBytes = 50L * 1024 * 1024;    // в «Лучшие сборки» — до 50 МБ (видео сжимается)

        public static string Root => Path.Combine(Settings.Folder, "presets");
        public static string CustomDir => Path.Combine(Settings.Folder, "custom");

        // ================== разделы сборки ==================

        /// <summary>Разделы: какие настройки в них входят. Порядок — как в окне применения.</summary>
        public static readonly (string id, string[] keys)[] Parts =
        {
            ("theme",     new[] { "Theme", "CustomAccent", "VideoDim", "LiveBgSpeed" }),
            ("effects",   new[] { "EffectsOn", "Weather", "Flakes", "SnowOnWindows", "GroundDrift", "CursorScrape", "CursorReact" }),
            ("cursor",    new[] { "CursorPack", "CursorSize", "CursorTrail" }),
            ("dock",      new[] { "DockEnabled", "DockSize", "DockMagnify", "DockTransparency", "DockClock", "DockBlur", "DockAutohide",
                                  "DockAnimations", "DockTray", "DockAllMonitors", "LaunchpadDock", "BattDock" }),
            ("island",    new[] { "IslandOn", "IslandMedia", "IslandNotify", "IslandHideIdle", "IslandFly", "IslandWidth", "IslandHeight",
                                  "IslandRound", "IslandBounce", "IslandSpring", "IslandLayer" }),
            ("wallpaper", new[] { "LiveWallpaperOn", "LiveAllMonitors", "LiveSound", "LivePauseFullscreen" }),
            ("anim",      new[] { "UiAnim", "UiAnimSpeed" }),
            ("sounds",    new[] { "Sounds", "SoundPack", "UiVolume", "MsgSounds", "MsgSoundIn", "MsgSoundOut", "MsgVolume" }),
            ("widgets",   new[] { "FpsOn", "FpsDetailed", "WidgetSystem", "WidgetClock", "WidgetWeather", "WidgetsOnTop" }),
            ("windows",   new[] { "BorderMode", "BorderColor", "Corners" }),
        };

        public static string[] PartIds => Parts.Select(p => p.id).ToArray();
        static string[] KeysOf(string part) => Parts.FirstOrDefault(p => p.id == part).keys ?? Array.Empty<string>();

        /// <summary>Свои файлы сборки: вид → какие расширения можно (только они попадут в сборку и достанутся из архива).</summary>
        static readonly Dictionary<string, string[]> FileKinds = new Dictionary<string, string[]>
        {
            ["cover"]     = new[] { ".jpg", ".png" },
            ["theme"]     = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".webp" },
            ["video"]     = new[] { ".mp4", ".m4v", ".webm", ".mov", ".wmv", ".gif" },
            ["particle"]  = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp" },
            ["cursor"]    = new[] { ".cur", ".ani", ".png" },
            ["desktop"]   = new[] { ".jpg", ".png", ".bmp" },
            ["live"]      = new[] { ".mp4", ".m4v", ".webm", ".mov", ".wmv" },
            ["sound_in"]  = new[] { ".wav", ".mp3", ".wma", ".m4a" },
            ["sound_out"] = new[] { ".wav", ".mp3", ".wma", ".m4a" },
        };

        static bool AllowedEntry(string fullName)
        {
            if (fullName == Manifest || fullName == OldThemeFile || fullName == OldParticleFile) return true;
            if (!fullName.StartsWith(FilesDir + "/", StringComparison.Ordinal)) return false;
            string name = fullName.Substring(FilesDir.Length + 1);
            if (name.Length == 0 || name.Contains('/') || name.Contains('\\') || name.Contains("..")) return false;
            string kind = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name).ToLowerInvariant();
            return FileKinds.TryGetValue(kind, out var exts) && exts.Contains(ext);
        }

        // ================== список и чтение ==================

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

        static JsonObject ReadManifest(string dir)
        {
            try { return JsonNode.Parse(File.ReadAllText(Path.Combine(dir, Manifest))) as JsonObject; }
            catch { return null; }
        }

        public static PresetInfo Read(string dir)
        {
            var j = ReadManifest(dir);
            if (j == null) return null;
            try
            {
                var set = j["settings"] as JsonObject ?? new JsonObject();
                var p = new PresetInfo
                {
                    Folder = dir,
                    Format = (int?)j["format"] ?? 1,
                    Name = FixName((string)j["name"]),
                    Description = (string)j["description"],
                    Theme = (string)set["Theme"],
                    Weather = (string)set["Weather"],
                    Created = DateTime.TryParse((string)j["created"], out var d) ? d : Directory.GetCreationTime(dir),
                    Backup = (bool?)j["backup"] ?? false,
                    PublishedId = (long?)j["published"] ?? 0
                };
                if (j["parts"] is JsonArray arr) p.Parts = arr.Select(x => (string)x).Where(x => x != null).ToList();
                else p.Parts = Parts.Where(part => part.keys.Any(k => set[k] != null)).Select(part => part.id).ToList();   // формат 1
                p.Cover = FileOf(dir, j, "cover");
                p.Summary = j["summary"] as JsonObject ?? MakeSummary(set, p.Parts, j, dir);
                p.Plus = (bool?)j["plus"] ?? IsPlus(set, p.Parts, j, dir);
                return p;
            }
            catch (Exception e) { Settings.Log(e); return null; }
        }

        /// <summary>Путь к своему файлу сборки (или null). Формат 1: theme.jpg / particle.png в корне.</summary>
        static string FileOf(string dir, JsonObject j, string kind)
        {
            string rel = (string)j?["files"]?[kind];
            if (rel == null && ((int?)j?["format"] ?? 1) < 2)
                rel = kind == "theme" ? OldThemeFile : kind == "particle" ? OldParticleFile : null;
            if (rel == null) return null;
            rel = rel.Replace('\\', '/');
            if (!AllowedEntry(rel)) return null;
            string full = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(full) ? full : null;
        }

        public static string FileOf(PresetInfo p, string kind) => FileOf(p.Folder, ReadManifest(p.Folder), kind);

        // ================== создание ==================

        /// <summary>Свои файлы текущих настроек: вид → путь. Только то, что реально используется сейчас.</summary>
        static Dictionary<string, string> CurrentFiles(Settings s)
        {
            var f = new Dictionary<string, string>();
            void Add(string kind, string file) { if (!string.IsNullOrEmpty(file) && File.Exists(file)) f[kind] = file; }
            if (s.Theme == "custom") Add("theme", s.CustomThemeImage);
            if (s.Theme == "video") Add("video", s.VideoTheme);
            if (s.Weather == "custom") Add("particle", s.CustomParticle);
            if (s.CursorPack == "custom") Add("cursor", s.CustomCursor);
            if (s.LiveWallpaperOn) Add("live", s.LiveWallpaper);
            if (s.MsgSoundIn == "custom") Add("sound_in", s.MsgCustomIn);
            if (s.MsgSoundOut == "custom") Add("sound_out", s.MsgCustomOut);
            Add("desktop", BuildFiles.CurrentDesktopWallpaper());
            return f;
        }

        /// <summary>
        /// Новая сборка из текущих настроек. cover — своя картинка-обложка (null — значок MizuEngine).
        /// Возвращает сборку или null при ошибке. Может занять пару секунд (копируются видео) — зовите из фона.
        /// </summary>
        public static PresetInfo CreateFromCurrent(Settings s, string name, string cover = null, string description = null, bool backup = false)
        {
            string dir = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(dir, FilesDir));
                var set = Snapshot(s);
                var files = new JsonObject();
                foreach (var kv in CurrentFiles(s))
                {
                    string ext = kv.Key == "desktop" ? BuildFiles.ImageExt(kv.Value) : Path.GetExtension(kv.Value).ToLowerInvariant();
                    if (!FileKinds[kv.Key].Contains(ext)) continue;
                    string dest = Path.Combine(dir, FilesDir, kv.Key + ext);
                    // Обои Windows бывают огромными PNG (4K, 20+ МБ) — такие пересжимаются в JPEG
                    if (kv.Key == "desktop") { dest = BuildFiles.CopyImage(kv.Value, dest, 12L * 1024 * 1024); if (dest == null) continue; }
                    else File.Copy(kv.Value, dest, true);
                    files[kv.Key] = $"{FilesDir}/{Path.GetFileName(dest)}";
                }
                if (!string.IsNullOrEmpty(cover) && File.Exists(cover))
                {
                    string dest = Path.Combine(dir, FilesDir, "cover.jpg");
                    if (BuildFiles.SaveCover(cover, dest)) files["cover"] = $"{FilesDir}/cover.jpg";
                }

                var parts = PartIds.Where(id => id != "wallpaper" || files["desktop"] != null || files["live"] != null).ToList();
                var j = new JsonObject
                {
                    ["format"] = 2,
                    ["app"] = typeof(Presets).Assembly.GetName().Version?.ToString(3),
                    ["name"] = string.IsNullOrWhiteSpace(name) ? "MizuEngine" : name.Trim(),
                    ["description"] = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                    ["created"] = DateTime.Now.ToString("o"),
                    ["backup"] = backup,
                    ["parts"] = new JsonArray(parts.Select(x => (JsonNode)x).ToArray()),
                    ["settings"] = set,
                    ["files"] = files
                };
                j["summary"] = MakeSummary(set, parts, j, dir);
                j["plus"] = IsPlus(set, parts, j, dir);
                Write(dir, j);
                if (backup) TrimBackups();
                return Read(dir);
            }
            catch (Exception e)
            {
                Settings.Log(e);
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
                return null;
            }
        }

        /// <summary>Что войдёт в сборку, если создать её сейчас (для карточки «Создать сборку»).</summary>
        public static JsonObject CurrentSummary(Settings s)
        {
            var set = Snapshot(s);
            var files = new JsonObject();
            foreach (var kv in CurrentFiles(s)) files[kv.Key] = kv.Value;
            var parts = PartIds.Where(id => id != "wallpaper" || files["desktop"] != null || files["live"] != null).ToList();
            return MakeSummary(set, parts, new JsonObject { ["files"] = files }, null);
        }

        static void Write(string dir, JsonObject j) =>
            File.WriteAllText(Path.Combine(dir, Manifest), j.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        /// <summary>Совместимость со старыми местами (Muzule): сохранить текущие настройки как сборку.</summary>
        public static PresetInfo SaveCurrent(Settings s, string name) => CreateFromCurrent(s, name);

        /// <summary>Резервная копия перед применением сборки или сбросом. Храним только последние MaxBackups.</summary>
        public static PresetInfo Backup(Settings s) =>
            CreateFromCurrent(s, Loc.T("presets.backup") + " " + DateTime.Now.ToString("d MMM HH:mm"), backup: true);

        static void TrimBackups()
        {
            foreach (var old in List().Where(p => p.Backup).OrderByDescending(p => p.Created).Skip(MaxBackups)) Delete(old);
        }

        /// <summary>Все настройки разделов — плоским JSON (имена как в Settings, поэтому старые сборки тоже читаются).</summary>
        public static JsonObject Snapshot(Settings s)
        {
            var o = new JsonObject();
            foreach (var part in Parts)
                foreach (var key in part.keys)
                {
                    var pi = typeof(Settings).GetProperty(key, BindingFlags.Public | BindingFlags.Instance);
                    if (pi == null) continue;
                    try { o[key] = JsonSerializer.SerializeToNode(pi.GetValue(s), pi.PropertyType); } catch { }
                }
            // «Свой» курсор или звук без файла сохранять нельзя — у другого человека ничего не появится
            if (s.CursorPack == "custom" && !File.Exists(s.CustomCursor)) o["CursorPack"] = "";
            if (s.MsgSoundIn == "custom" && !File.Exists(s.MsgCustomIn)) o["MsgSoundIn"] = "tg";
            if (s.MsgSoundOut == "custom" && !File.Exists(s.MsgCustomOut)) o["MsgSoundOut"] = "swoosh";
            return o;
        }

        static bool HasFile(JsonObject j, string dir, string kind) => dir != null ? FileOf(dir, j, kind) != null : j?["files"]?[kind] != null;

        /// <summary>Какие разделы требуют Mizu Plus: живая тема/видео, эффект Plus, курсор Plus, живые обои.</summary>
        static List<string> PlusPartList(JsonObject set, List<string> parts, JsonObject j, string dir)
        {
            var l = new List<string>();
            if (parts.Contains("theme") && Themes.IsPlusTheme((string)set["Theme"])) l.Add("theme");
            if (parts.Contains("effects") && Settings.IsPlusWeather((string)set["Weather"])) l.Add("effects");
            if (parts.Contains("cursor") && CursorPacks.IsPlusPack((string)set["CursorPack"])) l.Add("cursor");
            if (parts.Contains("wallpaper") && ((bool?)set["LiveWallpaperOn"] ?? false) && HasFile(j, dir, "live")) l.Add("wallpaper");
            return l;
        }

        static bool IsPlus(JsonObject set, List<string> parts, JsonObject j, string dir) => PlusPartList(set, parts, j, dir).Count > 0;

        /// <summary>Разделы, которым нужен Plus (из краткого описания) — для замочков в окне применения.</summary>
        public static HashSet<string> PlusParts(JsonObject summary)
        {
            var h = new HashSet<string>();
            if (summary?["plus_parts"] is JsonArray a) foreach (var x in a) if ((string)x != null) h.Add((string)x);
            return h;
        }

        /// <summary>Коротко о сборке — для карточки и для сервера (чтобы не качать файл ради описания).</summary>
        static JsonObject MakeSummary(JsonObject set, List<string> parts, JsonObject j, string dir)
        {
            bool live = ((bool?)set["LiveWallpaperOn"] ?? false) && HasFile(j, dir, "live");
            return new JsonObject
            {
                ["parts"] = new JsonArray(parts.Select(x => (JsonNode)x).ToArray()),
                ["plus_parts"] = new JsonArray(PlusPartList(set, parts, j, dir).Select(x => (JsonNode)x).ToArray()),
                ["theme"] = (string)set["Theme"],
                ["accent"] = (string)set["CustomAccent"],
                ["effects_on"] = (bool?)set["EffectsOn"] ?? true,
                ["weather"] = (string)set["Weather"],
                ["cursor"] = (string)set["CursorPack"] ?? "",
                ["cursor_size"] = (int?)set["CursorSize"] ?? 32,
                ["dock"] = (bool?)set["DockEnabled"] ?? false,
                ["island"] = (bool?)set["IslandOn"] ?? true,
                ["wallpaper"] = live ? "live" : HasFile(j, dir, "desktop") ? "image" : null,
                ["anim"] = (string)set["UiAnim"],
                ["sounds"] = (bool?)set["Sounds"] == true ? (string)set["SoundPack"] : null,
                ["border"] = (string)set["BorderMode"]
            };
        }

        // ================== применение ==================

        /// <summary>
        /// Применяет выбранные разделы сборки (parts = null — все). Свои файлы копируются к нам — сборку потом можно удалить.
        /// Окно, эффекты, док и Island после этого перечитывают настройки (MainWindow.ApplyBuildEffects).
        /// </summary>
        public static void Apply(PresetInfo p, Settings s, ICollection<string> parts = null)
        {
            var j = ReadManifest(p.Folder);
            if (j?["settings"] is not JsonObject set) return;
            var use = (parts ?? p.Parts).Where(p.Parts.Contains).ToHashSet();
            ApplyCore(set, use, kind => FileOf(p.Folder, j, kind), s);
        }

        /// <summary>Старое API (сборки от MizuEngine): снимок + картинки темы и частиц.</summary>
        public static void ApplySnapshot(JsonNode j, Settings s, string themeFile, string particleFile)
        {
            if (j is not JsonObject set) return;
            var use = Parts.Where(part => part.keys.Any(k => set[k] != null)).Select(part => part.id).ToHashSet();
            ApplyCore(set, use, kind => kind == "theme" ? themeFile : kind == "particle" ? particleFile : null, s);
        }

        static void ApplyCore(JsonObject set, HashSet<string> use, Func<string, string> file, Settings s)
        {
            Directory.CreateDirectory(CustomDir);
            foreach (var part in use)
                foreach (var key in KeysOf(part))
                    if (set[key] != null) SetProp(s, key, set[key]);

            string Existing(string kind) { var f = file(kind); return !string.IsNullOrEmpty(f) && File.Exists(f) ? f : null; }

            if (use.Contains("theme"))
            {
                if (s.Theme == "custom")
                {
                    var f = Existing("theme");
                    if (f != null) s.CustomThemeImage = CopyToCustom(f, "theme"); else if (!File.Exists(s.CustomThemeImage)) s.Theme = "blue";
                }
                else if (s.Theme == "video")
                {
                    var f = Existing("video");
                    if (f != null) s.VideoTheme = PlusPage.CopyVideo(f, "theme"); else if (!File.Exists(s.VideoTheme)) s.Theme = "blue";
                }
            }
            if (use.Contains("effects") && s.Weather == "custom")
            {
                var f = Existing("particle");
                if (f != null) s.CustomParticle = CopyToCustom(f, "particle"); else if (!File.Exists(s.CustomParticle)) s.Weather = "snow";
            }
            if (use.Contains("cursor"))
            {
                if (s.CursorPack == "custom")
                {
                    var f = Existing("cursor");
                    if (f != null) s.CustomCursor = CopyToCustom(f, "cursor"); else if (!File.Exists(s.CustomCursor)) s.CursorPack = "";
                }
                else s.CursorPack = Legacy.FixCursorPack(s.CursorPack ?? "");
            }
            if (use.Contains("wallpaper"))
            {
                var desk = Existing("desktop");
                if (desk != null) BuildFiles.SetDesktopWallpaper(CopyToCustom(desk, "wallpaper"));
                var live = Existing("live");
                if (live != null) s.LiveWallpaper = PlusPage.CopyLiveVideo(live);
                else if (s.LiveWallpaperOn && !File.Exists(s.LiveWallpaper)) s.LiveWallpaperOn = false;
            }
            if (use.Contains("sounds"))
            {
                if (s.MsgSoundIn == "custom")
                {
                    var f = Existing("sound_in");
                    string c = f != null ? ChatSounds.ImportCustom(f, true) : null;
                    if (c != null) s.MsgCustomIn = c; else if (!File.Exists(s.MsgCustomIn)) s.MsgSoundIn = "tg";
                }
                if (s.MsgSoundOut == "custom")
                {
                    var f = Existing("sound_out");
                    string c = f != null ? ChatSounds.ImportCustom(f, false) : null;
                    if (c != null) s.MsgCustomOut = c; else if (!File.Exists(s.MsgCustomOut)) s.MsgSoundOut = "swoosh";
                }
            }
            s.FirstRunDone = true;
            s.Clamp();
            s.Save();
        }

        static void SetProp(Settings s, string key, JsonNode value)
        {
            var pi = typeof(Settings).GetProperty(key, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null || !pi.CanWrite) return;
            try { pi.SetValue(s, value.Deserialize(pi.PropertyType)); }
            catch { /* значение не того типа (испорченный файл) — оставляем своё */ }
        }

        /// <summary>Свой файл (фон темы, частица, курсор, обои) копируется к нам — чтобы не пропал, если исходник удалят.</summary>
        public static string CopyToCustom(string file, string kind)
        {
            Directory.CreateDirectory(CustomDir);
            string ext = Path.GetExtension(file);
            if (string.IsNullOrEmpty(ext)) ext = BuildFiles.ImageExt(file);
            string dest = Path.Combine(CustomDir, $"{kind}-{DateTime.Now.Ticks}{ext}");
            File.Copy(file, dest, true);
            return dest;
        }

        // ================== правка, удаление, экспорт, импорт ==================

        /// <summary>Поменять что-то в описании сборки (название, обложку, отметку о публикации).</summary>
        public static void Update(PresetInfo p, Action<JsonObject> change)
        {
            var j = ReadManifest(p.Folder);
            if (j == null) return;
            change(j);
            Write(p.Folder, j);
        }

        public static void Rename(PresetInfo p, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (name.Length > 60) name = name.Substring(0, 60);
            Update(p, j => j["name"] = name);
            p.Name = name;
        }

        public static void SetDescription(PresetInfo p, string text)
        {
            text = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            if (text != null && text.Length > 300) text = text.Substring(0, 300);
            Update(p, j => j["description"] = text);
            p.Description = text;
        }

        /// <summary>Своя обложка сборки (image = null — убрать, будет значок MizuEngine).</summary>
        public static void SetCover(PresetInfo p, string image)
        {
            string dir = Path.Combine(p.Folder, FilesDir);
            Directory.CreateDirectory(dir);
            string dest = Path.Combine(dir, "cover.jpg");
            if (image == null)
            {
                try { if (File.Exists(dest)) File.Delete(dest); } catch { }
                Update(p, j => { if (j["files"] is JsonObject f) f.Remove("cover"); });
                p.Cover = null;
                return;
            }
            if (!BuildFiles.SaveCover(image, dest)) return;
            Update(p, j =>
            {
                if (j["files"] is not JsonObject f) { f = new JsonObject(); j["files"] = f; }
                f["cover"] = $"{FilesDir}/cover.jpg";
            });
            p.Cover = dest;
        }

        public static void MarkPublished(PresetInfo p, long id)
        {
            Update(p, j => j["published"] = id > 0 ? id : null);
            p.PublishedId = id;
        }

        /// <summary>Описание сборки для сервера (summary) — то же, что видно на карточке.</summary>
        public static JsonObject SummaryOf(PresetInfo p) => p.Summary?.DeepClone() as JsonObject ?? new JsonObject();

        public static long FolderSize(PresetInfo p)
        {
            try { return new DirectoryInfo(p.Folder).GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
            catch { return 0; }
        }

        public static string Export(PresetInfo p, string file)
        {
            try
            {
                if (File.Exists(file)) File.Delete(file);
                ZipFile.CreateFromDirectory(p.Folder, file, CompressionLevel.Optimal, false);
                return file;
            }
            catch (Exception e) { Settings.Log(e); return null; }
        }

        /// <summary>
        /// Импорт .mizupreset (свой файл, от друга, из «Лучших сборок»). Берём только известные файлы с разрешёнными
        /// расширениями — никакой «хитрый» архив не запишет ничего лишнего.
        /// </summary>
        public static PresetInfo Import(string file, string rename = null, bool temp = false)
        {
            // temp — во временную папку (применить чужую сборку, не добавляя её в «Мои сборки»)
            string dir = Path.Combine(temp ? Path.Combine(Path.GetTempPath(), "MizuEngine-builds") : Root, Guid.NewGuid().ToString("N"));
            try
            {
                using (var zip = ZipFile.OpenRead(file))
                {
                    if (zip.GetEntry(Manifest) == null) return null;
                    if (zip.Entries.Sum(e => e.Length) > MaxImportBytes) return null;
                    Directory.CreateDirectory(Path.Combine(dir, FilesDir));
                    foreach (var e in zip.Entries)
                    {
                        string name = e.FullName.Replace('\\', '/');
                        if (!AllowedEntry(name)) continue;
                        e.ExtractToFile(Path.Combine(dir, name.Replace('/', Path.DirectorySeparatorChar)), true);
                    }
                }
                // Чужая сборка у меня — не «опубликована мной» и не резервная копия
                var j = ReadManifest(dir);
                if (j == null) { Directory.Delete(dir, true); return null; }
                j["published"] = null;
                j["backup"] = false;
                if (!string.IsNullOrWhiteSpace(rename)) j["name"] = rename.Trim();
                Write(dir, j);
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

        /// <summary>Сохранить готовую сборку (сборку от MizuEngine) к себе в «Мои сборки».</summary>
        public static PresetInfo SaveSnapshot(string name, JsonNode snap, string themeFile, string particleFile)
        {
            string dir = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(dir, FilesDir));
                var set = snap.DeepClone() as JsonObject ?? new JsonObject();
                var files = new JsonObject();
                if (File.Exists(themeFile)) { File.Copy(themeFile, Path.Combine(dir, FilesDir, "theme.jpg"), true); files["theme"] = $"{FilesDir}/theme.jpg"; }
                if (File.Exists(particleFile)) { File.Copy(particleFile, Path.Combine(dir, FilesDir, "particle.png"), true); files["particle"] = $"{FilesDir}/particle.png"; }
                var parts = Parts.Where(part => part.keys.Any(k => set[k] != null)).Select(part => part.id).ToList();
                var j = new JsonObject
                {
                    ["format"] = 2,
                    ["name"] = string.IsNullOrWhiteSpace(name) ? "MizuEngine" : name,
                    ["created"] = DateTime.Now.ToString("o"),
                    ["parts"] = new JsonArray(parts.Select(x => (JsonNode)x).ToArray()),
                    ["settings"] = set,
                    ["files"] = files
                };
                j["summary"] = MakeSummary(set, parts, j, dir);
                j["plus"] = IsPlus(set, parts, j, dir);
                Write(dir, j);
                return Read(dir);
            }
            catch (Exception e) { Settings.Log(e); return null; }
        }
    }
}
