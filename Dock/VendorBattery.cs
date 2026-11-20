// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;

namespace MizuEngine
{
    /// <summary>
    /// Заряд беспроводных мышей и клавиатур на USB-приёмнике 2.4 ГГц. Windows его не знает: приёмник передаёт заряд только
    /// своей командой. Программы AJAZZ и других брендов на той же платформе (USB VID 0x3151) ставят службу iot_driver —
    /// она сама опрашивает приёмник и отдаёт список устройств с процентом по gRPC-web на 127.0.0.1:3814 (метод
    /// driver.DriverGrpc/watchDevList). Мы просто спрашиваем у неё. Нет службы — значит, нет программы бренда:
    /// тогда показываем кнопку «Скачать программу».
    /// </summary>
    internal static class VendorBattery
    {
        /// <summary>Платформа приёмников, заряд которых умеет отдавать iot_driver.</summary>
        public const int RyVid = 0x3151;

        public sealed class Reading { public int Vid, Pid, Battery; public bool Online, Mouse, Keyboard; }

        static readonly HttpClient Http = new HttpClient(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromMilliseconds(600) })
        { Timeout = TimeSpan.FromSeconds(3) };

        /// <summary>Есть ли служба (то есть установлена программа бренда). Обновляется при каждом Query.</summary>
        public static bool ServiceFound { get; private set; }

        /// <summary>Один запрос к службе: первый пакет потока watchDevList — полный список устройств. null — службы нет.</summary>
        public static List<Reading> Query()
        {
            _log = Debug ? new StringBuilder() : null;
            var r = TryQuery(false) ?? TryQuery(true);
            ServiceFound = r != null;
            if (_log != null)
            {
                if (r != null) foreach (var x in r) _log.AppendLine($"vid={x.Vid:X4} pid={x.Pid:X4} batt={x.Battery} online={x.Online} mouse={x.Mouse} kb={x.Keyboard}");
                try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "vendor.log"), _log.ToString()); } catch { }
            }
            return r;
        }

        static StringBuilder _log;
        static readonly bool Debug = File.Exists(Path.Combine(AppContext.BaseDirectory, "mizu-devices.debug"));

        static List<Reading> TryQuery(bool text)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var frame = new byte[5];   // пустое сообщение Empty: флаг 0 + длина 0
                var req = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:3814/driver.DriverGrpc/watchDevList");
                string type = text ? "application/grpc-web-text" : "application/grpc-web+proto";
                req.Content = text ? new StringContent(Convert.ToBase64String(frame), Encoding.ASCII) : new ByteArrayContent(frame);
                req.Content.Headers.ContentType = new MediaTypeHeaderValue(type);
                req.Headers.Add("X-Grpc-Web", "1");
                req.Headers.Accept.ParseAdd(type);
                using var resp = Http.Send(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                _log?.AppendLine($"{(text ? "text" : "bin")}: HTTP {(int)resp.StatusCode} {resp.Content.Headers.ContentType}");
                if (!resp.IsSuccessStatusCode) return null;
                using var stream = resp.Content.ReadAsStream(cts.Token);
                byte[] msg = text ? ReadTextFrame(stream, cts.Token) : ReadBinaryFrame(stream, cts.Token);
                _log?.AppendLine("frame: " + (msg == null ? "null" : BitConverter.ToString(msg, 0, Math.Min(msg.Length, 400))));
                return msg == null ? null : ParseDeviceList(msg);
            }
            catch (Exception e) { _log?.AppendLine((text ? "text" : "bin") + ": " + e.GetType().Name + " " + e.Message); return null; }
        }

        // ---------- кадры gRPC-web: [флаг 1 байт][длина 4 байта BE][сообщение] ----------

        static byte[] ReadBinaryFrame(Stream s, CancellationToken ct)
        {
            var head = new byte[5];
            while (true)
            {
                if (!ReadExact(s, head, 5, ct)) return null;
                int len = (head[1] << 24) | (head[2] << 16) | (head[3] << 8) | head[4];
                if (len < 0 || len > 4 << 20) return null;
                var body = new byte[len];
                if (!ReadExact(s, body, len, ct)) return null;
                if ((head[0] & 0x80) == 0) return body;   // данные (а не трейлеры)
            }
        }

        static bool ReadExact(Stream s, byte[] buf, int n, CancellationToken ct)
        {
            int got = 0;
            while (got < n)
            {
                int k = s.ReadAsync(buf, got, n - got, ct).GetAwaiter().GetResult();
                if (k <= 0) return false;
                got += k;
            }
            return true;
        }

        /// <summary>Текстовый вариант: base64, куски могут идти один за другим, каждый со своими «=» в конце.</summary>
        static byte[] ReadTextFrame(Stream s, CancellationToken ct)
        {
            var bytes = new List<byte>();
            var quad = new StringBuilder(4);
            var buf = new byte[4096];
            while (true)
            {
                int k = s.ReadAsync(buf, 0, buf.Length, ct).GetAwaiter().GetResult();
                if (k <= 0) return null;
                for (int i = 0; i < k; i++)
                {
                    char c = (char)buf[i];
                    if (char.IsWhiteSpace(c)) continue;
                    quad.Append(c);
                    if (quad.Length == 4) { bytes.AddRange(Convert.FromBase64String(quad.ToString())); quad.Clear(); }
                }
                // Есть ли уже целый кадр данных?
                int p = 0;
                while (bytes.Count - p >= 5)
                {
                    int len = (bytes[p + 1] << 24) | (bytes[p + 2] << 16) | (bytes[p + 3] << 8) | bytes[p + 4];
                    if (bytes.Count - p - 5 < len) break;
                    if ((bytes[p] & 0x80) == 0) return bytes.GetRange(p + 5, len).ToArray();
                    p += 5 + len;
                }
            }
        }

        // ---------- protobuf (только нужные поля) ----------
        // DeviceList { repeated DJDev devlist = 1; }
        // DJDev { Device dev = 1; DangleCommon danglecommondev = 2; }
        // Device { devtype=1; bool is24=2; string path=3; int32 id=4; uint32 battery=5; bool isonline=6; uint32 vid=7; uint32 pid=8; }
        // DangleCommon { DangleStatus keyboard=1; DangleStatus mouse=2; string path=3; uint32 keyboardId=5; uint32 mouseId=6; uint32 vid=7; uint32 pid=8; }
        // DangleStatus { Empty empty=1; Status24 status=2; }   Status24 { uint32 battery=1; bool isOnline=2; }

        static List<Reading> ParseDeviceList(byte[] m)
        {
            var list = new List<Reading>();
            foreach (var (f, v) in Fields(m))
                if (f == 1 && v is byte[] dj)
                    foreach (var (f2, v2) in Fields(dj))
                    {
                        if (f2 == 1 && v2 is byte[] dev) list.Add(ParseDevice(dev));
                        else if (f2 == 2 && v2 is byte[] dc) list.AddRange(ParseDangle(dc));
                    }
            return list;
        }

        static Reading ParseDevice(byte[] m)
        {
            var r = new Reading { Mouse = true };
            bool is24 = false;
            foreach (var (f, v) in Fields(m))
                switch (f)
                {
                    case 2: is24 = v is ulong b && b != 0; break;
                    case 5: r.Battery = v is ulong x ? (int)x : 0; break;
                    case 6: r.Online = v is ulong o && o != 0; break;
                    case 7: r.Vid = v is ulong vid ? (int)vid : 0; break;
                    case 8: r.Pid = v is ulong pid ? (int)pid : 0; break;
                }
            if (!is24) r.Battery = 0;   // по проводу заряд здесь не приходит
            return r;
        }

        static IEnumerable<Reading> ParseDangle(byte[] m)
        {
            int vid = 0, pid = 0;
            Reading kb = null, mouse = null;
            foreach (var (f, v) in Fields(m))
                switch (f)
                {
                    case 1 when v is byte[] k: kb = ParseStatus(k); kb.Keyboard = true; break;
                    case 2 when v is byte[] ms: mouse = ParseStatus(ms); mouse.Mouse = true; break;
                    case 7: vid = v is ulong a ? (int)a : 0; break;
                    case 8: pid = v is ulong b ? (int)b : 0; break;
                }
            foreach (var r in new[] { kb, mouse })
                if (r != null) { r.Vid = vid; r.Pid = pid; yield return r; }
        }

        static Reading ParseStatus(byte[] m)
        {
            var r = new Reading();
            foreach (var (f, v) in Fields(m))
                if (f == 2 && v is byte[] st)
                    foreach (var (f2, v2) in Fields(st))
                    {
                        if (f2 == 1 && v2 is ulong b) r.Battery = (int)b;
                        if (f2 == 2 && v2 is ulong o) r.Online = o != 0;
                    }
            return r;
        }

        /// <summary>Поля сообщения: varint → ulong, length-delimited → byte[]. Остальное пропускаем.</summary>
        static IEnumerable<(int, object)> Fields(byte[] m)
        {
            int p = 0;
            while (p < m.Length)
            {
                ulong key = Varint(m, ref p);
                int field = (int)(key >> 3), wire = (int)(key & 7);
                switch (wire)
                {
                    case 0: yield return (field, Varint(m, ref p)); break;
                    case 2:
                        int len = (int)Varint(m, ref p);
                        if (len < 0 || p + len > m.Length) yield break;
                        var sub = new byte[len];
                        Array.Copy(m, p, sub, 0, len);
                        p += len;
                        yield return (field, sub);
                        break;
                    case 1: p += 8; break;
                    case 5: p += 4; break;
                    default: yield break;
                }
            }
        }

        static ulong Varint(byte[] m, ref int p)
        {
            ulong r = 0;
            for (int shift = 0; p < m.Length && shift < 64; shift += 7)
            {
                byte b = m[p++];
                r |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
            }
            return r;
        }

        // ---------- программы брендов ----------

        /// <summary>Бренд по имени приёмника — для кнопки «Скачать программу». Ссылки — официальные страницы драйверов.</summary>
        public static (string brand, string url, string site)? AppFor(string name, int vid)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (n.Contains("ajazz")) return ("AJAZZ", "https://www.a-jazz.com/h-col-155.html", "a-jazz.com");
            if (vid == RyVid)
            {
                // Тот же приёмник у разных брендов: ищем программу по названию устройства
                string q = Uri.EscapeDataString((string.IsNullOrWhiteSpace(name) ? "2.4G mouse" : name.Trim()) + " driver download");
                string brand = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } w ? w[0] : "";
                return (brand, "https://www.bing.com/search?q=" + q, null);
            }
            return null;
        }
    }
}
