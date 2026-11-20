// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MizuEngine
{
    /// <summary>
    /// Заряд наушников, мыши и клавиатуры. Bluetooth-устройства: Windows хранит процент в свойстве устройства
    /// {104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2 (то же самое показывают «Параметры → Bluetooth»). Проводные (и через
    /// USB-приёмник) — процента нет, показываем значок с проводом. Опрос раз в минуту в фоне, без нагрузки на окно.
    /// </summary>
    internal static class DeviceBattery
    {
        public enum Kind { Headphones, Mouse, Keyboard }

        public sealed class Info
        {
            public Kind Kind;
            public string Name;
            public int Percent = -1;      // -1 — процента нет (провод или USB-приёмник)
            public bool Receiver;         // беспроводное через USB-приёмник 2.4 ГГц: Windows не знает заряд
            public bool FromApp;          // процент взят у программы бренда (служба iot_driver)
            public string AppBrand, AppUrl, AppSite;   // программы нет — предлагаем скачать (бренд, ссылка, сайт для значка)
            public int Vid, Pid;
            public bool Wired => Percent < 0;
            public bool NeedsApp => Percent < 0 && AppUrl != null;
            public string State => Percent >= 0 ? Percent + "%" : Loc.T(Receiver ? "batt.receiver" : "batt.wired");
        }

        static volatile Info[] _current = Array.Empty<Info>();
        static Timer _timer;
        static int _busy;
        static string _lastKey = "";

        /// <summary>Все найденные устройства (без учёта настроек — фильтр в Visible).</summary>
        public static IReadOnlyList<Info> All => _current;

        /// <summary>Изменился список или проценты. Вызывается из фонового потока.</summary>
        public static event Action Changed;

        static Settings _s;

        public static void Start(Settings s)
        {
            _s = s;
            if (_timer != null) return;
            _timer = new Timer(_ => Poll(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(60));
        }

        /// <summary>Проверить прямо сейчас (например, после включения настройки).</summary>
        public static void PollSoon() => ThreadPool.QueueUserWorkItem(_ => Poll());

        /// <summary>Устройства, которые нужно показывать по настройкам.</summary>
        public static List<Info> Visible(Settings s)
        {
            var r = new List<Info>();
            foreach (var i in _current)
                if ((i.Kind == Kind.Headphones && s.BattHeadphones) || (i.Kind == Kind.Mouse && s.BattMouse) || (i.Kind == Kind.Keyboard && s.BattKeyboard))
                    r.Add(i);
            return r;
        }

        public static string KindName(Kind k) => Loc.T(k == Kind.Headphones ? "batt.hp" : k == Kind.Mouse ? "batt.mouse" : "batt.kb");

        /// <summary>Значок устройства из Segoe Fluent Icons.</summary>
        public static string Glyph(Kind k) => k == Kind.Headphones ? "" : k == Kind.Mouse ? "" : "";

        static void Poll()
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                var list = Scan();
                string key = string.Join("|", list.Select(i => $"{i.Kind}:{i.Name}:{i.Percent}:{i.AppUrl}"));
                _current = list.ToArray();
                if (key != _lastKey) { _lastKey = key; Changed?.Invoke(); }
                CheckLow(list);
            }
            catch (Exception e) { Settings.Log(e); }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        // ================== предупреждение о низком заряде ==================

        static readonly Dictionary<string, int> _warned = new Dictionary<string, int>();   // устройство → 0 нет, 1 первое, 2 второе

        /// <summary>
        /// Заряд опустился до первого порога (по умолчанию 15%) — капсула Dynamic Island показывает «Мышь: осталось 15%» со звуком.
        /// До второго (5%) — ещё раз. Поставили на зарядку (выше порога на 5%) — предупреждения снова «взводятся».
        /// </summary>
        static void CheckLow(List<Info> list)
        {
            var s = _s;
            if (s == null || !s.BattWarn) return;
            foreach (var d in Visible(s))
            {
                if (d.Percent < 0) continue;
                string id = d.Kind + ":" + d.Name;
                _warned.TryGetValue(id, out int level);
                int lvl = d.Percent <= s.BattWarnLow ? 2 : d.Percent <= s.BattWarnAt ? 1 : 0;
                if (lvl == 0 && d.Percent > s.BattWarnAt + 5) { _warned[id] = 0; continue; }
                if (lvl <= level) continue;
                _warned[id] = lvl;
                string title = string.Format(Loc.T("batt.low.title"), KindName(d.Kind), d.Percent);
                string text = (string.IsNullOrEmpty(d.Name) ? "" : d.Name + " — ") + Loc.T(lvl == 2 ? "batt.low.crit" : "batt.low.text");
                Shell.Island?.Notify(title, text, "pack://application:,,,/Assets/MizuEngine_logo_128.png", () => Shell.OpenApp?.Invoke());
                if (s.BattWarnSound) ChatSounds.Preview("chime", null);
            }
        }

        /// <summary>Проверка предупреждения из настроек («Показать пример»).</summary>
        public static void TestWarning()
        {
            var s = _s;
            if (s == null) return;
            var d = Visible(s).FirstOrDefault();
            string title = string.Format(Loc.T("batt.low.title"), KindName(d?.Kind ?? Kind.Mouse), s.BattWarnAt);
            Shell.Island?.Notify(title, (d == null || string.IsNullOrEmpty(d.Name) ? "" : d.Name + " — ") + Loc.T("batt.low.text"),
                "pack://application:,,,/Assets/MizuEngine_logo_128.png", () => Shell.OpenApp?.Invoke());
            if (s.BattWarnSound) ChatSounds.Preview("chime", null);
        }

        // ================== поиск устройств ==================

        sealed class Dev
        {
            public string Id, Class, Name;
            public Guid Container;
            public int Battery = -1;
            public bool Started;
            public string Extra = "";
            public string Bus = "";    // имя, которое сообщает само устройство (например «MCHOSE G75»)
            public bool Bluetooth => Id.StartsWith("BTH", StringComparison.OrdinalIgnoreCase);
            public int Vid => Hex("VID_");
            public int Pid => Hex("PID_");
            int Hex(string tag)
            {
                int i = Id.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
                return i >= 0 && i + tag.Length + 4 <= Id.Length && int.TryParse(Id.AsSpan(i + tag.Length, 4), System.Globalization.NumberStyles.HexNumber, null, out int v) ? v : 0;
            }
        }

        static readonly Guid RootContainer = new Guid("00000000-0000-0000-ffff-ffffffffffff");

        static List<Info> Scan()
        {
            var devs = Enumerate();
            DebugDump(devs);
            var result = new List<Info>();
            var byContainer = devs.Where(d => d.Container != Guid.Empty && d.Container != RootContainer).GroupBy(d => d.Container);
            var btKinds = new HashSet<Kind>();
            var wiredKinds = new Dictionary<Kind, Info>();
            List<VendorBattery.Reading> vendor = null;
            bool vendorAsked = false;

            foreach (var g in byContainer)
            {
                var items = g.ToList();
                bool hasKb = items.Any(d => Eq(d.Class, "Keyboard") && d.Started);
                bool hasMouse = items.Any(d => Eq(d.Class, "Mouse") && d.Started);
                bool hasAudio = items.Any(d => (Eq(d.Class, "AudioEndpoint") || Eq(d.Class, "MEDIA")) && d.Started);
                if (!hasKb && !hasMouse && !hasAudio) continue;
                bool bt = items.Any(d => d.Bluetooth);
                var batt = items.Where(d => d.Battery >= 0).Select(d => d.Battery).DefaultIfEmpty(-1).Max();
                // Имя — у узла Bluetooth (его видно в «Параметрах»), иначе то, что сообщает само устройство по USB («MAD60 HE V2»)
                string name = items.FirstOrDefault(d => d.Bluetooth && !string.IsNullOrWhiteSpace(d.Name))?.Name
                              ?? items.FirstOrDefault(d => d.Bus.Length > 0 && !d.Bus.StartsWith("USB", StringComparison.OrdinalIgnoreCase))?.Bus
                              ?? items.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d.Name) && !Eq(d.Class, "HIDClass"))?.Name ?? "";
                Kind kind = Classify(name, hasKb, hasMouse, hasAudio, items);

                if (bt)
                {
                    if (batt < 0) continue;   // Bluetooth без данных о заряде — не показываем
                    btKinds.Add(kind);
                    result.Add(new Info { Kind = kind, Name = Clean(name), Percent = Math.Clamp(batt, 0, 100) });
                }
                else if (kind != Kind.Headphones || LooksLikeHeadset(name, items))
                {
                    if (wiredKinds.TryGetValue(kind, out var had) && had.Percent >= 0) continue;
                    var usb = items.FirstOrDefault(d => d.Vid != 0);
                    var info = new Info { Kind = kind, Name = Clean(name), Receiver = LooksWireless(name), Vid = usb?.Vid ?? 0, Pid = usb?.Pid ?? 0 };
                    if (info.Receiver || info.Vid == VendorBattery.RyVid)
                    {
                        // Приёмник: процент знает только программа бренда — спрашиваем её службу (один раз за опрос)
                        if (!vendorAsked) { vendorAsked = true; vendor = VendorBattery.Query(); }
                        var hit = vendor?.Where(r => r.Vid == info.Vid && r.Pid == info.Pid && r.Battery > 0 && r.Online)
                                         .OrderByDescending(r => kind == Kind.Keyboard ? r.Keyboard : r.Mouse).FirstOrDefault();
                        if (hit != null) { info.Percent = Math.Clamp(hit.Battery, 1, 100); info.FromApp = true; info.Receiver = true; }
                        else if (vendor == null && VendorBattery.AppFor(name, info.Vid) is { } app && (info.Receiver || app.site != null))
                        {
                            info.Receiver = true;
                            (info.AppBrand, info.AppUrl, info.AppSite) = app;
                        }
                    }
                    if (had == null || info.Percent >= 0 || (had.AppUrl == null && info.AppUrl != null)) wiredKinds[kind] = info;
                }
            }
            // Проводное показываем, только если такого же устройства по Bluetooth нет
            foreach (var kv in wiredKinds)
                if (!btKinds.Contains(kv.Key)) result.Add(kv.Value);

            result.Sort((a, b) => a.Kind.CompareTo(b.Kind));
            return result;
        }

        static bool LooksLikeHeadset(string name, List<Dev> items)
        {
            string all = (name + " " + string.Join(" ", items.Select(d => d.Name))).ToLowerInvariant();
            return all.Contains("headphone") || all.Contains("headset") || all.Contains("наушник") || all.Contains("гарнитур")
                   || all.Contains("earbud") || all.Contains("buds") || all.Contains("airpods");
        }

        static bool LooksWireless(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            return n.Contains("2.4g") || n.Contains("2.4 g") || n.Contains("wireless") || n.Contains("receiver") || n.Contains("dongle")
                   || n.Contains("unifying") || n.Contains("lightspeed") || n.Contains("приёмник") || n.Contains("приемник");
        }

        static Kind Classify(string name, bool kb, bool mouse, bool audio, List<Dev> items)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (audio && !kb && !mouse) return Kind.Headphones;
            if (n.Contains("mouse") || n.Contains("мыш") || n.Contains("mx master") || n.Contains("mx anywhere")) return Kind.Mouse;
            if (n.Contains("keyboard") || n.Contains("клав") || n.Contains("keys")) return Kind.Keyboard;
            if (audio) return Kind.Headphones;
            if (kb && !mouse) return Kind.Keyboard;
            if (mouse && !kb) return Kind.Mouse;
            // Игровые мыши и клавиатуры часто объявляют и то и другое. Настоящая роль — у первого интерфейса (MI_00):
            // у мыши первым идёт «мышь», у клавиатуры — «клавиатура».
            Dev first = null;
            foreach (var d in items)
                if ((Eq(d.Class, "Keyboard") || Eq(d.Class, "Mouse")) && d.Started && (first == null || Order(d.Id) < Order(first.Id))) first = d;
            return first != null && Eq(first.Class, "Mouse") ? Kind.Mouse : Kind.Keyboard;
        }

        /// <summary>Номер интерфейса и коллекции из пути HID: «…MI_01&COL03…» → 103.</summary>
        static int Order(string id)
        {
            int Num(string tag)
            {
                int i = id.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
                return i >= 0 && i + tag.Length + 2 <= id.Length && int.TryParse(id.AsSpan(i + tag.Length, 2), System.Globalization.NumberStyles.HexNumber, null, out int v) ? v : 0;
            }
            return Num("MI_") * 100 + Num("COL");
        }

        static string Clean(string s)
        {
            s = (s ?? "").Trim();
            foreach (var tail in new[] { " Hands-Free AG Audio", " Hands-Free AG", " Hands-Free", " Stereo", " Avrcp Transport" })
                if (s.EndsWith(tail, StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - tail.Length);
            return s;
        }

        static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        // ================== SetupAPI ==================

        const int DIGCF_PRESENT = 0x2, DIGCF_ALLCLASSES = 0x4;
        const int SPDRP_DEVICEDESC = 0x0, SPDRP_CLASS = 0x7, SPDRP_FRIENDLYNAME = 0xC;
        const uint DN_STARTED = 0x8;

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        struct DEVPROPKEY { public Guid fmtid; public uint pid; }

        static DEVPROPKEY KeyBattery = new DEVPROPKEY { fmtid = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), pid = 2 };
        static DEVPROPKEY KeyBusName = new DEVPROPKEY { fmtid = new Guid("540b947e-8b40-45bc-a8a2-6a0b894cbda2"), pid = 4 };
        static DEVPROPKEY KeyContainer = new DEVPROPKEY { fmtid = new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), pid = 2 };

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string enumerator, IntPtr parent, int flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiEnumDeviceInfo(IntPtr set, int index, ref SP_DEVINFO_DATA data);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref SP_DEVINFO_DATA data, StringBuilder id, int size, out int required);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set, ref SP_DEVINFO_DATA data, int property, out int regType, byte[] buffer, int size, out int required);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key, out uint type, byte[] buffer, int size, out int required, int flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiGetDevicePropertyKeys(IntPtr set, ref SP_DEVINFO_DATA data, [Out] DEVPROPKEY[] keys, int count, out int required, int flags);
        [DllImport("cfgmgr32.dll")]
        static extern int CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, int flags);

        static List<Dev> Enumerate()
        {
            var list = new List<Dev>();
            IntPtr set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return list;
            try
            {
                var buf = new byte[1024];
                var sb = new StringBuilder(512);
                for (int i = 0; ; i++)
                {
                    var data = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
                    if (!SetupDiEnumDeviceInfo(set, i, ref data)) break;
                    sb.Clear();
                    if (!SetupDiGetDeviceInstanceIdW(set, ref data, sb, sb.Capacity, out _)) continue;
                    var d = new Dev { Id = sb.ToString() };
                    d.Class = RegString(set, ref data, SPDRP_CLASS, buf) ?? "";
                    // Нас интересуют только узлы, из которых складываются наушники, мышь и клавиатура
                    bool interesting = d.Bluetooth || Eq(d.Class, "Keyboard") || Eq(d.Class, "Mouse") || Eq(d.Class, "AudioEndpoint") || Eq(d.Class, "MEDIA") || Eq(d.Class, "HIDClass");
                    if (!interesting) continue;
                    d.Name = RegString(set, ref data, SPDRP_FRIENDLYNAME, buf) ?? RegString(set, ref data, SPDRP_DEVICEDESC, buf) ?? "";
                    if (SetupDiGetDevicePropertyW(set, ref data, ref KeyContainer, out _, buf, buf.Length, out int n, 0) && n >= 16)
                        d.Container = new Guid(buf.AsSpan(0, 16));
                    if (SetupDiGetDevicePropertyW(set, ref data, ref KeyBusName, out uint bt, buf, buf.Length, out int bl, 0) && bt == 18 && bl > 2)
                        d.Bus = Encoding.Unicode.GetString(buf, 0, bl).TrimEnd('\0').Trim();
                    if (d.Bluetooth && SetupDiGetDevicePropertyW(set, ref data, ref KeyBattery, out _, buf, buf.Length, out n, 0) && n >= 1)
                        d.Battery = buf[0];
                    if (DebugOn && (d.Bluetooth || d.Container != Guid.Empty))
                    {
                        var keys = new DEVPROPKEY[256];
                        if (SetupDiGetDevicePropertyKeys(set, ref data, keys, keys.Length, out int kc, 0))
                            for (int k = 0; k < kc; k++)
                            {
                                var key = keys[k];
                                if (SetupDiGetDevicePropertyW(set, ref data, ref key, out uint t, buf, buf.Length, out int bn, 0) && t == 3 && bn == 1)
                                    d.Extra += $" [{key.fmtid}:{key.pid}={buf[0]}]";
                            }
                    }
                    d.Started = CM_Get_DevNode_Status(out uint st, out _, data.DevInst, 0) == 0 && (st & DN_STARTED) != 0;
                    list.Add(d);
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return list;
        }

        static readonly bool DebugOn = System.IO.File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "mizu-devices.debug"));

        /// <summary>Отладка: список узлов в out\devices.log (только если рядом лежит файл mizu-devices.debug).</summary>
        static void DebugDump(List<Dev> devs)
        {
            if (!DebugOn) return;
            try
            {
                var sb = new StringBuilder();
                foreach (var d in devs.OrderBy(x => x.Container))
                    sb.AppendLine($"{d.Container} | {d.Class} | {d.Name} | {d.Id} | bus={d.Bus} batt={d.Battery} started={d.Started}{d.Extra}");
                System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "devices.log"), sb.ToString());
            }
            catch { }
        }

        static string RegString(IntPtr set, ref SP_DEVINFO_DATA data, int prop, byte[] buf)
        {
            if (!SetupDiGetDeviceRegistryPropertyW(set, ref data, prop, out _, buf, buf.Length, out int n) || n < 2) return null;
            return Encoding.Unicode.GetString(buf, 0, n).TrimEnd('\0');
        }
    }
}
