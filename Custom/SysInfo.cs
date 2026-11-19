// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MizuEngine
{
    /// <summary>Загрузка процессора, видеокарты и памяти — как в «Диспетчере задач».</summary>
    internal sealed class SysStats : IDisposable
    {
        long _idle, _kernel, _user;
        IntPtr _query, _gpu;
        bool _gpuOk;

        public double Cpu, Gpu, Ram, RamUsedGb, RamTotalGb;

        public SysStats()
        {
            GetSystemTimes(out _idle, out _kernel, out _user);
            try
            {
                // Видеокарта: сумма загрузки 3D-движка по всем процессам (так считает «Диспетчер задач»)
                if (PdhOpenQueryW(null, IntPtr.Zero, out _query) == 0 &&
                    PdhAddEnglishCounterW(_query, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out _gpu) == 0)
                {
                    PdhCollectQueryData(_query);
                    _gpuOk = true;
                }
            }
            catch (Exception e) { Settings.Log(e); }
        }

        public void Update()
        {
            if (GetSystemTimes(out long i, out long k, out long u))
            {
                long di = i - _idle, total = (k - _kernel) + (u - _user);   // время ядра уже включает простой
                Cpu = total > 0 ? Math.Clamp((total - di) * 100.0 / total, 0, 100) : 0;
                _idle = i; _kernel = k; _user = u;
            }

            var m = new MEMORYSTATUSEX { dwLength = 64 };
            if (GlobalMemoryStatusEx(ref m))
            {
                RamTotalGb = m.ullTotalPhys / 1073741824.0;
                RamUsedGb = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
                Ram = m.dwMemoryLoad;
            }

            if (_gpuOk) Gpu = Math.Clamp(ReadGpu(), 0, 100);
        }

        double ReadGpu()
        {
            if (PdhCollectQueryData(_query) != 0) return 0;
            uint size = 0;
            uint res = PdhGetFormattedCounterArrayW(_gpu, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out uint count, IntPtr.Zero);
            if (res != PDH_MORE_DATA || size == 0) return 0;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArrayW(_gpu, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out count, buf) != 0) return 0;
                double sum = 0;
                // PDH_FMT_COUNTERVALUE_ITEM_W: имя (8) + статус (4) + выравнивание (4) + значение double (8)
                for (int n = 0; n < count; n++)
                    sum += BitConverter.Int64BitsToDouble(Marshal.ReadInt64(buf, n * 24 + 16));
                return sum;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        public void Dispose()
        {
            if (_query != IntPtr.Zero) { PdhCloseQuery(_query); _query = IntPtr.Zero; }
            _gpuOk = false;
        }

        const uint PDH_FMT_DOUBLE = 0x00000200, PDH_FMT_NOCAP100 = 0x00008000, PDH_MORE_DATA = 0x800007D2;

        [StructLayout(LayoutKind.Sequential)]
        struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhOpenQueryW(string source, IntPtr user, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr user, out IntPtr counter);
        [DllImport("pdh.dll")] static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint size, out uint count, IntPtr buffer);
        [DllImport("pdh.dll")] static extern uint PdhCloseQuery(IntPtr query);
    }

    /// <summary>Погода с open-meteo.com — бесплатно и без ключа.</summary>
    internal static class WeatherApi
    {
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        public sealed class Now
        {
            public string Place;
            public double Temp, Wind;
            public int Code;
        }

        public static async Task<Now> Get(string city)
        {
            if (string.IsNullOrWhiteSpace(city)) return null;
            try
            {
                string lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                var geo = JsonNode.Parse(await Http.GetStringAsync($"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city.Trim())}&count=1&language={lang}"));
                var r = geo?["results"]?[0];
                if (r == null) return null;
                double lat = (double)r["latitude"], lon = (double)r["longitude"];
                string url = string.Format(CultureInfo.InvariantCulture,
                    "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&current=temperature_2m,weather_code,wind_speed_10m&timezone=auto", lat, lon);
                var w = JsonNode.Parse(await Http.GetStringAsync(url));
                var c = w?["current"];
                return new Now
                {
                    Place = (string)r["name"] ?? city,
                    Temp = (double?)c?["temperature_2m"] ?? 0,
                    Wind = (double?)c?["wind_speed_10m"] ?? 0,
                    Code = (int?)c?["weather_code"] ?? 0
                };
            }
            catch (Exception e) { Settings.Log(e); return null; }
        }

        /// <summary>Код погоды WMO → значок и ключ перевода.</summary>
        public static (string glyph, string key) Describe(int code) => code switch
        {
            0 => ("☀", "wx.clear"),
            1 or 2 => ("⛅", "wx.partly"),
            3 => ("☁", "wx.cloudy"),
            45 or 48 => ("≋", "wx.fog"),
            >= 51 and <= 67 => ("☂", "wx.rain"),
            >= 71 and <= 77 => ("❄", "wx.snow"),
            >= 80 and <= 82 => ("☂", "wx.showers"),
            85 or 86 => ("❄", "wx.snow"),
            >= 95 => ("⚡", "wx.storm"),
            _ => ("☁", "wx.cloudy")
        };
    }
}
