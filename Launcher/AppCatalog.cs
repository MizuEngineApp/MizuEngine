// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>Программа из меню «Пуск».</summary>
    internal sealed class AppEntry
    {
        public string Id;     // имя в shell:AppsFolder (AUMID у Store-приложений, путь/идентификатор у обычных)
        public string Name;
        public string Path => @"shell:AppsFolder\" + Id;
    }

    /// <summary>
    /// Все программы из меню «Пуск» — тот же список, что «Все приложения» в Windows (папка shell:AppsFolder):
    /// и обычные программы, и приложения из Store. Читается в отдельном STA-потоке, кэшируется в памяти.
    /// </summary>
    internal static class AppCatalog
    {
        static List<AppEntry> _apps = new List<AppEntry>();
        static Task _loading;
        static DateTime _loadedAt;

        public static IReadOnlyList<AppEntry> Apps => _apps;
        public static bool Loaded => _loadedAt != default;

        /// <summary>Список поменялся (вызывается в потоке, который позвал Refresh).</summary>
        public static event Action Changed;

        /// <summary>Перечитать список (не чаще раза в 20 секунд). Результат — в Apps и событии Changed.</summary>
        public static Task Refresh(bool force = false)
        {
            if (_loading != null && !_loading.IsCompleted) return _loading;
            if (!force && Loaded && (DateTime.UtcNow - _loadedAt).TotalSeconds < 20) return Task.CompletedTask;
            var d = Dispatcher.CurrentDispatcher;
            var tcs = new TaskCompletionSource<List<AppEntry>>();
            var t = new Thread(() =>
            {
                try { tcs.SetResult(Enumerate()); }
                catch (Exception e) { Settings.Log(e); tcs.SetResult(null); }
            }) { IsBackground = true, Name = "MizuEngine apps" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            _loading = tcs.Task.ContinueWith(r =>
            {
                var list = r.Result;
                if (list == null || list.Count == 0) return;
                d.BeginInvoke(new Action(() =>
                {
                    _loadedAt = DateTime.UtcNow;
                    bool same = list.Count == _apps.Count && list.Zip(_apps, (a, b) => a.Id == b.Id && a.Name == b.Name).All(x => x);
                    if (same) return;
                    _apps = list;
                    Changed?.Invoke();
                }));
            });
            return _loading;
        }

        public static AppEntry Find(string id) => _apps.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));

        public static void Launch(AppEntry a)
        {
            if (a == null) return;
            try { Process.Start(new ProcessStartInfo("explorer.exe", a.Path) { UseShellExecute = true }); }
            catch (Exception e) { Settings.Log(e); }
        }

        /// <summary>Значок программы (128 px, как в доке). Пока не готов — null, потом done в потоке интерфейса.</summary>
        public static ImageSource Icon(AppEntry a, Action<ImageSource> done)
        {
            var d = Dispatcher.CurrentDispatcher;
            return DockApps.IconFor(a.Path, img => d.BeginInvoke(new Action(() => done(img))));
        }

        /// <summary>Насколько программа подходит под запрос: 0 — не подходит. Начало имени важнее начала слова, а то — середины.</summary>
        public static int Score(string name, string q)
        {
            if (string.IsNullOrEmpty(q) || string.IsNullOrEmpty(name)) return 0;
            var ci = CultureInfo.CurrentCulture.CompareInfo;
            const CompareOptions o = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
            if (ci.IsPrefix(name, q, o)) return 100 - Math.Min(40, name.Length - q.Length);
            int i = ci.IndexOf(name, q, o);
            if (i > 0 && !char.IsLetterOrDigit(name[i - 1])) return 70 - Math.Min(30, i);
            if (i > 0) return 40 - Math.Min(20, i);
            // Первые буквы слов: «vsc» → Visual Studio Code
            var initials = new string(name.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries).Select(w => w[0]).ToArray());
            if (q.Length >= 2 && ci.IsPrefix(initials, q, o)) return 35;
            return 0;
        }

        // ================== чтение shell:AppsFolder ==================

        static readonly string[] SkipWords = { "uninstall", "удалить", "удаление", "деинсталл", "readme", "release notes", "website", "веб-сайт" };
        static readonly string[] SkipExt = { ".txt", ".chm", ".pdf", ".htm", ".html", ".url", ".rtf", ".ini", ".log", ".md" };

        static List<AppEntry> Enumerate()
        {
            var result = new List<AppEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            SHCreateItemFromParsingName("shell:AppsFolder", IntPtr.Zero, typeof(IShellItem).GUID, out IShellItem folder);
            if (folder == null) return result;
            try
            {
                if (folder.BindToHandler(IntPtr.Zero, BHID_EnumItems, typeof(IEnumShellItems).GUID, out IntPtr pe) != 0 || pe == IntPtr.Zero) return result;
                var en = (IEnumShellItems)Marshal.GetObjectForIUnknown(pe);
                Marshal.Release(pe);
                try
                {
                    while (en.Next(1, out IShellItem item, out uint got) == 0 && got == 1)
                    {
                        try
                        {
                            string name = Name(item, SIGDN_NORMALDISPLAY);
                            string id = Name(item, SIGDN_PARENTRELATIVEPARSING);
                            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id)) continue;
                            string low = name.ToLowerInvariant(), lowId = id.ToLowerInvariant();
                            if (SkipWords.Any(w => low.Contains(w))) continue;
                            if (SkipExt.Any(x => lowId.EndsWith(x))) continue;
                            if (lowId.StartsWith("http:") || lowId.StartsWith("https:")) continue;
                            if (!seen.Add(id)) continue;
                            result.Add(new AppEntry { Id = id, Name = name.Trim() });
                        }
                        finally { Marshal.ReleaseComObject(item); }
                    }
                }
                finally { Marshal.ReleaseComObject(en); }
            }
            finally { Marshal.ReleaseComObject(folder); }
            var cmp = StringComparer.Create(CultureInfo.CurrentCulture, true);
            result.Sort((a, b) => cmp.Compare(a.Name, b.Name));
            return result;
        }

        static string Name(IShellItem item, uint kind)
        {
            if (item.GetDisplayName(kind, out IntPtr p) != 0 || p == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUni(p); }
            finally { Marshal.FreeCoTaskMem(p); }
        }

        const uint SIGDN_NORMALDISPLAY = 0x00000000, SIGDN_PARENTRELATIVEPARSING = 0x80018001;
        static readonly Guid BHID_EnumItems = new Guid("94f60519-2850-4924-aa5a-d15e84868039");

        [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            [PreserveSig] int BindToHandler(IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid bhid, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);
            [PreserveSig] int GetParent(out IShellItem ppsi);
            [PreserveSig] int GetDisplayName(uint sigdnName, out IntPtr ppszName);
            [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            [PreserveSig] int Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IEnumShellItems
        {
            [PreserveSig] int Next(uint celt, [MarshalAs(UnmanagedType.Interface)] out IShellItem rgelt, out uint pceltFetched);
            [PreserveSig] int Skip(uint celt);
            [PreserveSig] int Reset();
            [PreserveSig] int Clone(out IEnumShellItems ppenum);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
                                                       [MarshalAs(UnmanagedType.Interface)] out IShellItem item);
    }
}
