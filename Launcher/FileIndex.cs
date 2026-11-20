// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace MizuEngine
{
    /// <summary>
    /// Файлы для Spotlight: Рабочий стол, Загрузки и Документы (до 4 уровней вглубь, не больше 40 000 файлов).
    /// Список строится в фоне при первом открытии Spotlight и обновляется, если ему больше 3 минут. В памяти только пути.
    /// </summary>
    internal static class FileIndex
    {
        internal sealed class Entry { public string Path, Name; public bool Dir; public DateTime Modified; }

        static List<Entry> _all = new List<Entry>();
        static DateTime _builtAt;
        static Task _building;

        static readonly HashSet<string> SkipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "node_modules", ".git", ".svn", "bin", "obj", "__pycache__", ".vs", ".idea", "$recycle.bin", "venv", ".venv", "packages", "cache" };

        public static bool Ready => _builtAt != default;

        public static Task Refresh()
        {
            if (_building != null && !_building.IsCompleted) return _building;
            if (Ready && (DateTime.UtcNow - _builtAt).TotalMinutes < 3) return Task.CompletedTask;
            return _building = Task.Run(Build);
        }

        static IEnumerable<string> Roots()
        {
            var list = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Downloads()
            };
            return list.Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p)).Distinct(StringComparer.OrdinalIgnoreCase);
        }

        static string Downloads()
        {
            try
            {
                if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out IntPtr p) == 0 && p != IntPtr.Zero)
                {
                    try { return Marshal.PtrToStringUni(p); }
                    finally { Marshal.FreeCoTaskMem(p); }
                }
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        static void Build()
        {
            var result = new List<Entry>();
            var opts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.Temporary };
            foreach (var root in Roots())
            {
                var stack = new Stack<(string dir, int depth)>();
                stack.Push((root, 0));
                while (stack.Count > 0 && result.Count < 40000)
                {
                    var (dir, depth) = stack.Pop();
                    try
                    {
                        foreach (var fi in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", opts))
                        {
                            bool isDir = (fi.Attributes & FileAttributes.Directory) != 0;
                            if (isDir && ((fi.Attributes & FileAttributes.ReparsePoint) != 0 || SkipDirs.Contains(fi.Name))) continue;
                            if (!isDir && fi.Extension.Equals(".ini", StringComparison.OrdinalIgnoreCase) && fi.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                            result.Add(new Entry { Path = fi.FullName, Name = isDir ? fi.Name : Path.GetFileNameWithoutExtension(fi.Name) + fi.Extension, Dir = isDir, Modified = fi.LastWriteTimeUtc });
                            if (isDir && depth < 3) stack.Push((fi.FullName, depth + 1));
                        }
                    }
                    catch { }
                }
            }
            _all = result;
            _builtAt = DateTime.UtcNow;
        }

        /// <summary>Лучшие совпадения по имени: начало имени, начало слова, середина; свежие файлы чуть выше.</summary>
        public static List<Entry> Search(string q, int max)
        {
            var list = _all;
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2 || list.Count == 0) return new List<Entry>();
            q = q.Trim();
            var ci = CultureInfo.CurrentCulture.CompareInfo;
            const CompareOptions o = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
            var hits = new List<(Entry e, double score)>();
            foreach (var e in list)
            {
                int i = ci.IndexOf(e.Name, q, o);
                if (i < 0) continue;
                double s = i == 0 ? 100 : !char.IsLetterOrDigit(e.Name[i - 1]) ? 70 : 40;
                s -= Math.Min(20, e.Name.Length / 6.0);
                double days = (DateTime.UtcNow - e.Modified).TotalDays;
                s += days < 1 ? 12 : days < 7 ? 8 : days < 30 ? 4 : 0;
                if (e.Dir) s += 2;
                hits.Add((e, s));
            }
            return hits.OrderByDescending(h => h.score).Take(max).Select(h => h.e).ToList();
        }

        [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
    }
}
