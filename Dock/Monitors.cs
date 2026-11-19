// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MizuEngine
{
    /// <summary>Один монитор: где он, его масштаб и понятное имя («Mi Monitor»).</summary>
    internal sealed class MonitorInfo
    {
        public IntPtr Handle;
        /// <summary>Имя устройства Windows (\\.\DISPLAY2) — по нему док узнаёт «свой» монитор после переподключения.</summary>
        public string Device;
        /// <summary>Постоянный id монитора для настроек (путь устройства монитора, не меняется при смене порядка мониторов).</summary>
        public string Id;
        /// <summary>Название монитора из EDID, например «Mi Monitor». Пусто — Windows его не знает.</summary>
        public string Name;
        public Native.RECT Bounds, Work;
        public bool Primary;
        public int Dpi = 96;
        public double Scale => Dpi / 96.0;
    }

    /// <summary>
    /// Мониторы: список, на каком мониторе окно (даже свёрнутое) и главный ли он.
    /// Свёрнутое окно Windows «уносит» в точку (-32000, -32000), поэтому монитор окна запоминаем, пока оно видно.
    /// </summary>
    internal static class Monitors
    {
        static readonly Dictionary<IntPtr, IntPtr> _lastMonitor = new Dictionary<IntPtr, IntPtr>();
        static readonly object Lock = new object();

        /// <summary>Главный монитор (там Dynamic Island и основной док).</summary>
        public static IntPtr PrimaryHandle => MonitorFromPoint(new Native.POINT { X = 0, Y = 0 }, 1);   // MONITOR_DEFAULTTOPRIMARY

        /// <summary>На каком мониторе окно. Для свёрнутого — где оно было до сворачивания.</summary>
        public static IntPtr Of(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return PrimaryHandle;
            IntPtr m;
            if (!Native.IsIconic(hwnd))
            {
                m = DockNative.MonitorFromWindow(hwnd, 2);   // MONITOR_DEFAULTTONEAREST
                lock (Lock)
                {
                    _lastMonitor[hwnd] = m;
                    if (_lastMonitor.Count > 600) Prune();
                }
                return m;
            }
            lock (Lock) { if (_lastMonitor.TryGetValue(hwnd, out m)) return m; }
            // Окно свернули ещё до запуска MizuEngine — смотрим, где оно стоит в развёрнутом виде
            var wp = new Native.WINDOWPLACEMENT { length = Marshal.SizeOf<Native.WINDOWPLACEMENT>() };
            if (Native.GetWindowPlacement(hwnd, ref wp))
            {
                var r = wp.rcNormalPosition;   // от края рабочей области главного монитора
                var work = DockNative.WorkAreaOf(new Native.RECT { Left = 0, Top = 0, Right = 1, Bottom = 1 });
                r.Left += work.Left; r.Right += work.Left; r.Top += work.Top; r.Bottom += work.Top;
                return DockNative.MonitorFromRect(ref r, 2);
            }
            return PrimaryHandle;
        }

        public static bool OnPrimary(IntPtr hwnd) => Of(hwnd) == PrimaryHandle;

        /// <summary>Прямоугольник монитора, на котором лежит прямоугольник r (пиксели экрана).</summary>
        public static Native.RECT BoundsOf(Native.RECT r)
        {
            var mi = new DockNative.MONITORINFO { cbSize = Marshal.SizeOf<DockNative.MONITORINFO>() };
            IntPtr mon = DockNative.MonitorFromRect(ref r, 2);
            return DockNative.GetMonitorInfo(mon, ref mi) ? mi.rcMonitor : r;
        }

        static void Prune()
        {
            var dead = new List<IntPtr>();
            foreach (var h in _lastMonitor.Keys) if (!DockNative.IsWindow(h)) dead.Add(h);
            foreach (var h in dead) _lastMonitor.Remove(h);
        }

        /// <summary>Все мониторы: сначала главный, остальные — слева направо.</summary>
        public static List<MonitorInfo> All()
        {
            var list = new List<MonitorInfo>();
            Dictionary<string, (string name, string path)> names = null;
            try { names = DisplayNames(); } catch { }

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hmon, IntPtr hdc, ref Native.RECT rc, IntPtr data) =>
            {
                var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                if (!GetMonitorInfo(hmon, ref mi)) return true;
                var m = new MonitorInfo
                {
                    Handle = hmon,
                    Device = mi.szDevice ?? "",
                    Bounds = mi.rcMonitor,
                    Work = mi.rcWork,
                    Primary = (mi.dwFlags & 1) != 0   // MONITORINFOF_PRIMARY
                };
                try { if (GetDpiForMonitor(hmon, 0, out uint dx, out _) == 0 && dx > 0) m.Dpi = (int)dx; } catch { }
                if (names != null && names.TryGetValue(m.Device, out var n))
                {
                    m.Name = n.name;
                    m.Id = string.IsNullOrEmpty(n.path) ? null : n.path;
                }
                m.Id ??= m.Device;
                list.Add(m);
                return true;
            }, IntPtr.Zero);

            list.Sort((a, b) => a.Primary != b.Primary ? (a.Primary ? -1 : 1) : a.Bounds.Left != b.Bounds.Left ? a.Bounds.Left.CompareTo(b.Bounds.Left) : a.Bounds.Top.CompareTo(b.Bounds.Top));
            return list;
        }

        /// <summary>Понятные имена мониторов (как в «Параметры → Дисплей») по имени устройства \\.\DISPLAYn.</summary>
        static Dictionary<string, (string name, string path)> DisplayNames()
        {
            var res = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint np, out uint nm) != 0) return res;
            var paths = new DISPLAYCONFIG_PATH_INFO[np];
            var modes = new DISPLAYCONFIG_MODE_INFO[nm];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return res;
            for (int i = 0; i < np; i++)
            {
                var src = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                src.header.type = 1;   // DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME
                src.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
                src.header.adapterId = paths[i].sourceInfo.adapterId;
                src.header.id = paths[i].sourceInfo.id;
                if (DisplayConfigGetDeviceInfo(ref src) != 0 || string.IsNullOrEmpty(src.viewGdiDeviceName)) continue;

                var tgt = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
                tgt.header.type = 2;   // DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME
                tgt.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>();
                tgt.header.adapterId = paths[i].targetInfo.adapterId;
                tgt.header.id = paths[i].targetInfo.id;
                if (DisplayConfigGetDeviceInfo(ref tgt) != 0) continue;
                if (!res.ContainsKey(src.viewGdiDeviceName))
                    res[src.viewGdiDeviceName] = (tgt.monitorFriendlyDeviceName ?? "", tgt.monitorDevicePath ?? "");
            }
            return res;
        }

        // ---------- WinAPI ----------
        delegate bool MonitorEnumProc(IntPtr hmon, IntPtr hdc, ref Native.RECT rc, IntPtr data);
        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Native.POINT p, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")] static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFOEX mi);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr mon, int type, out uint x, out uint y);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MONITORINFOEX
        {
            public int cbSize;
            public Native.RECT rcMonitor, rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }

        const uint QDC_ONLY_ACTIVE_PATHS = 2;
        [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);
        [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] DISPLAYCONFIG_PATH_INFO[] paths, ref uint numModes, [Out] DISPLAYCONFIG_MODE_INFO[] modes, IntPtr topology);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME info);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME info);

        [StructLayout(LayoutKind.Sequential)] struct LUID { public uint Low; public int High; }
        [StructLayout(LayoutKind.Sequential)] struct DISPLAYCONFIG_RATIONAL { public uint Numerator, Denominator; }
        [StructLayout(LayoutKind.Sequential)] struct DISPLAYCONFIG_PATH_SOURCE_INFO { public LUID adapterId; public uint id, modeInfoIdx, statusFlags; }
        [StructLayout(LayoutKind.Sequential)]
        struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId;
            public uint id, modeInfoIdx, outputTechnology, rotation, scaling;
            public DISPLAYCONFIG_RATIONAL refreshRate;
            public uint scanLineOrdering;
            public int targetAvailable;
            public uint statusFlags;
        }
        [StructLayout(LayoutKind.Sequential)] struct DISPLAYCONFIG_PATH_INFO { public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo; public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo; public uint flags; }
        // 64 байта: тип, id, адаптер и объединение режимов (нам не нужно — просто место под него)
        [StructLayout(LayoutKind.Sequential)] struct DISPLAYCONFIG_MODE_INFO { public uint infoType, id; public LUID adapterId; public ulong u0, u1, u2, u3, u4, u5; }
        [StructLayout(LayoutKind.Sequential)] struct DISPLAYCONFIG_DEVICE_INFO_HEADER { public uint type, size; public LUID adapterId; public uint id; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public uint flags, outputTechnology;
            public ushort edidManufactureId, edidProductCodeId;
            public uint connectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
        }
    }
}
