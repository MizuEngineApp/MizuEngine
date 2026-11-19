// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MizuEngine
{
    /// <summary>Находит установленный Wallpaper Engine и скачанные из Мастерской обои.</summary>
    internal static class WallpaperEngine
    {
        public sealed class Item
        {
            public string Title, Preview, Target;   // Target — файл, который надо передать Wallpaper Engine
        }

        /// <summary>Путь к wallpaper64.exe или null, если WE не установлен. libraries — все библиотеки Steam.</summary>
        public static string FindExe(out List<string> libraries)
        {
            libraries = new List<string>();
            var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            if (string.IsNullOrEmpty(steam)) return null;
            libraries.Add(steam.Replace('/', '\\'));

            try
            {
                // Другие диски с играми Steam перечислены в libraryfolders.vdf: "path"  "D:\\SteamLibrary"
                var vdf = Path.Combine(libraries[0], "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"(.+?)\""))
                    {
                        string p = m.Groups[1].Value.Replace(@"\\", @"\");
                        if (!libraries.Contains(p, StringComparer.OrdinalIgnoreCase)) libraries.Add(p);
                    }
            }
            catch { /* не смогли прочитать — ищем только в основной папке Steam */ }

            foreach (var lib in libraries)
            {
                var exe = Path.Combine(lib, "steamapps", "common", "wallpaper_engine", "wallpaper64.exe");
                if (File.Exists(exe)) return exe;
            }
            return null;
        }

        public static List<Item> Scan(List<string> libraries)
        {
            var list = new List<Item>();
            var opts = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

            foreach (var lib in libraries)
            {
                var dir = Path.Combine(lib, "steamapps", "workshop", "content", "431960");   // 431960 — id Wallpaper Engine в Steam
                if (!Directory.Exists(dir)) continue;

                foreach (var folder in Directory.GetDirectories(dir))
                {
                    var project = Path.Combine(folder, "project.json");
                    if (!File.Exists(project)) continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(project), opts);
                        var root = doc.RootElement;
                        string title = root.TryGetProperty("title", out var t) ? t.GetString() : null;
                        string preview = root.TryGetProperty("preview", out var p) ? p.GetString() : null;
                        // Всем типам обоев передаём project.json, как в документации Wallpaper Engine.
                        // Если передать сам .mp4, WE каждый раз заново «импортирует» видео — отсюда долгое переключение.
                        string target = project;

                        list.Add(new Item
                        {
                            Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileName(folder) : title,
                            Preview = string.IsNullOrEmpty(preview) ? null : Path.Combine(folder, preview),
                            Target = target
                        });
                    }
                    catch { /* битый project.json — пропускаем эти обои */ }
                }
            }
            return list.OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        static readonly object Gate = new object();
        // Очередь — по одной последней команде на каждый монитор (null — без указания монитора)
        static readonly Dictionary<int, string> _queued = new Dictionary<int, string>();
        static bool _busy;

        /// <summary>
        /// Просим сам Wallpaper Engine поставить обои (все типы: видео, сцены, веб). Работает в фоне.
        /// Если быстро нажать несколько обоев подряд, применятся последние — команды не копятся в очередь.
        /// </summary>
        public static void ApplyAsync(string exe, string target, Action<bool> done) => ApplyAsync(exe, target, null, done);

        /// <summary>
        /// То же, но на выбранные мониторы (номера мониторов Wallpaper Engine, см. WeIndex). null — как раньше, без номера.
        /// На каждый монитор помним только последние выбранные обои, поэтому на двух мониторах можно поставить разные.
        /// </summary>
        public static void ApplyAsync(string exe, string target, int[] monitors, Action<bool> done)
        {
            lock (Gate)
            {
                if (monitors == null || monitors.Length == 0) { _queued.Clear(); _queued[-1] = target; }
                else
                {
                    _queued.Remove(-1);
                    foreach (int m in monitors) _queued[m] = target;
                }
                if (_busy) return;
                _busy = true;
            }
            Task.Run(() =>
            {
                bool ok = true;
                while (true)
                {
                    int mon;
                    string t;
                    lock (Gate)
                    {
                        if (_queued.Count == 0) { _busy = false; break; }
                        mon = _queued.Keys.Min();
                        t = _queued[mon];
                        _queued.Remove(mon);
                    }
                    ok &= Send(exe, t, mon);
                }
                done?.Invoke(ok);
            });
        }

        /// <summary>
        /// Номер монитора для Wallpaper Engine (-monitor N, с нуля): WE считает мониторы в порядке их номеров в Windows
        /// (\\.\DISPLAY1, \\.\DISPLAY2…), а не «главный первым».
        /// </summary>
        public static int WeIndex(MonitorInfo m, List<MonitorInfo> all)
        {
            var order = all.OrderBy(x => DisplayNumber(x.Device)).ThenBy(x => x.Bounds.Left).ToList();
            return Math.Max(0, order.FindIndex(x => x.Device == m.Device));
        }

        static int DisplayNumber(string device)
        {
            var mt = Regex.Match(device ?? "", @"(\d+)\s*$");
            return mt.Success && int.TryParse(mt.Groups[1].Value, out int n) ? n : 999;
        }

        static bool Send(string exe, string target, int monitor = -1)
        {
            try
            {
                string dir = Path.GetDirectoryName(exe);
                bool run32 = Running("wallpaper32"), run64 = Running("wallpaper64");
                // Команду надо слать той же версии (32 или 64 бит), что уже запущена, иначе стартует вторая копия WE
                if (run32 && File.Exists(Path.Combine(dir, "wallpaper32.exe"))) exe = Path.Combine(dir, "wallpaper32.exe");

                if (!run32 && !run64)
                {
                    // WE не запущен: сначала запускаем его и ждём, иначе команда теряется и обои «не переключаются»
                    Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = dir });
                    for (int i = 0; i < 60 && !Running("wallpaper64") && !Running("wallpaper32"); i++) Thread.Sleep(250);
                    Thread.Sleep(2500);
                }

                string args = $"-control openWallpaper -file \"{target}\"" + (monitor >= 0 ? " -monitor " + monitor : "");
                using var p = Process.Start(new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false, WorkingDirectory = dir, CreateNoWindow = true
                });
                p?.WaitForExit(8000);   // ждём, пока WE примет команду, прежде чем слать следующую
                return true;
            }
            catch (Exception e) { Settings.Log(e); return false; }
        }

        static bool Running(string name)
        {
            var list = Process.GetProcessesByName(name);
            foreach (var p in list) p.Dispose();
            return list.Length > 0;
        }

        /// <summary>Случайные обои — для горячей клавиши.</summary>
        public static void ApplyRandom()
        {
            string exe = FindExe(out var libs);
            if (exe == null) return;
            var items = Scan(libs);
            if (items.Count == 0) return;
            ApplyAsync(exe, items[Random.Shared.Next(items.Count)].Target, null);
        }

        public static void OpenStore()
        {
            try { Process.Start(new ProcessStartInfo("steam://store/431960") { UseShellExecute = true }); }
            catch
            {
                try { Process.Start(new ProcessStartInfo("https://store.steampowered.com/app/431960") { UseShellExecute = true }); }
                catch { }
            }
        }
    }
}
