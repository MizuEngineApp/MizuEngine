// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace MizuEngine
{
    /// <summary>
    /// Отпечаток компьютера для лимита аккаунтов: SHA-256 от данных материнской платы (SMBIOS).
    /// Не меняется при переустановке Windows. На сервер уходит только хеш — по нему ПК не узнать.
    /// </summary>
    internal static class DeviceId
    {
        static string _hash;

        public static string Hash => _hash ??= Compute();

        static string Compute()
        {
            var parts = new List<string>();
            try { ReadSmbios(parts); } catch (Exception e) { Settings.Log(e); }

            // У дешёвых плат UUID бывает пустым или одинаковым у всех — тогда добавляем ID установки Windows
            string uuid = parts.Count > 0 ? parts[0] : "";
            if (uuid.Length == 0 || BadUuids.Contains(uuid) || uuid.All(c => c == '0' || c == '-') || uuid.All(c => c == 'f' || c == '-'))
                parts.Add(MachineGuid());

            string src = Legacy.DeviceSalt + string.Join("|", parts);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(src))).ToLowerInvariant();
        }

        static readonly HashSet<string> BadUuids = new HashSet<string>
        {
            "03000200-0400-0500-0006-000700080009",
            "00020003-0004-0005-0006-000700080009",
        };

        static string MachineGuid()
        {
            try
            {
                using var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                                         .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                return (k?.GetValue("MachineGuid") as string ?? "").ToLowerInvariant();
            }
            catch { return ""; }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint GetSystemFirmwareTable(uint provider, uint tableId, byte[] buffer, uint size);

        /// <summary>parts[0] — UUID системы, дальше серийные номера и модель платы.</summary>
        static void ReadSmbios(List<string> parts)
        {
            const uint RSMB = 0x52534D42;
            uint size = GetSystemFirmwareTable(RSMB, 0, null, 0);
            if (size == 0 || size > 1 << 20) return;
            var buf = new byte[size];
            if (GetSystemFirmwareTable(RSMB, 0, buf, size) == 0) return;

            int len = BitConverter.ToInt32(buf, 4);
            int pos = 8, end = Math.Min(buf.Length, 8 + len);
            string uuid = "", sysSerial = "", boardProduct = "", boardSerial = "";

            while (pos + 4 <= end)
            {
                byte type = buf[pos], fl = buf[pos + 1];
                if (fl < 4 || pos + fl > end) break;
                int strings = pos + fl;

                if (type == 1 && fl >= 0x19)
                {
                    uuid = FormatUuid(buf, pos + 8);
                    sysSerial = Str(buf, strings, end, buf[pos + 7]);
                }
                else if (type == 2 && fl >= 8)
                {
                    boardProduct = Str(buf, strings, end, buf[pos + 5]);
                    boardSerial = Str(buf, strings, end, buf[pos + 7]);
                }
                if (type == 127) break;

                // Конец строк структуры — два нуля подряд
                int p = strings;
                while (p + 1 < end && !(buf[p] == 0 && buf[p + 1] == 0)) p++;
                pos = p + 2;
            }

            parts.Add(uuid);
            parts.Add(Clean(sysSerial));
            parts.Add(Clean(boardProduct));
            parts.Add(Clean(boardSerial));
        }

        /// <summary>UUID в SMBIOS: первые три поля записаны младшим байтом вперёд.</summary>
        static string FormatUuid(byte[] b, int o)
        {
            var g = new Guid(new ReadOnlySpan<byte>(b, o, 16));
            return g.ToString().ToLowerInvariant();
        }

        static string Str(byte[] b, int start, int end, byte index)
        {
            if (index == 0) return "";
            int p = start;
            for (int i = 1; p < end; i++)
            {
                int s = p;
                while (p < end && b[p] != 0) p++;
                if (i == index) return Encoding.ASCII.GetString(b, s, p - s).Trim();
                p++;
                if (p < end && b[p] == 0) break;   // строки кончились
            }
            return "";
        }

        /// <summary>Заглушки вроде «To be filled by O.E.M.» не помогают отличить ПК — выбрасываем их.</summary>
        static string Clean(string s)
        {
            string l = (s ?? "").Trim().ToLowerInvariant();
            if (l.Length < 3 || l.Contains("o.e.m") || l.Contains("default string") || l.Contains("not specified") ||
                l.Contains("system serial") || l.Contains("none") || l.All(c => c == '0' || c == ' ')) return "";
            return l;
        }
    }
}
