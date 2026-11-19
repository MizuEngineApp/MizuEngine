// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace MizuEngine
{
    /// <summary>
    /// Ярлыки рабочего стола остаются на своих местах. Когда док резервирует место внизу экрана (или его освобождает),
    /// Windows пересчитывает рабочую область и может «пересадить» нижние ярлыки, в том числе на другой монитор.
    /// Перед такими изменениями запоминаем, где стоит каждый ярлык, а после — возвращаем на место те, что сдвинулись.
    /// Ярлыки — это список (SysListView32) внутри Проводника, поэтому позиции читаются через память его процесса.
    /// Если в Проводнике включено «Упорядочить автоматически», Windows расставляет ярлыки сам — тогда ничего не трогаем.
    /// </summary>
    internal static class DesktopIcons
    {
        static Dictionary<string, Native.POINT> _saved;
        static int _restoreToken;

        /// <summary>
        /// Запомнить, где сейчас стоят ярлыки. Читаем в фоне и ждём не больше 0.4 с: если Проводник занят
        /// (например, только что вернул панель задач), док не должен ждать — просто пропускаем.
        /// </summary>
        public static void Snapshot()
        {
            _saved = null;
            try
            {
                var t = Task.Run(Read);
                if (t.Wait(400)) _saved = t.Result;
            }
            catch (AggregateException ae) when (ae.InnerException is TimeoutException) { }
            catch (Exception e) { Settings.Log(e); }
        }

        /// <summary>Вернуть сдвинутые ярлыки на место — через секунду и ещё раз через три (Windows двигает их не сразу).</summary>
        public static void RestoreLater()
        {
            var saved = _saved;
            if (saved == null || saved.Count == 0) return;
            int token = ++_restoreToken;
            Task.Run(async () =>
            {
                foreach (int ms in new[] { 1200, 3000 })
                {
                    await Task.Delay(ms);
                    if (token != _restoreToken) return;
                    try { Restore(saved); } catch (TimeoutException) { } catch (Exception e) { Settings.Log(e); return; }
                }
            });
        }

        // ================== список ярлыков Проводника ==================

        const uint LVM_GETITEMCOUNT = 0x1004, LVM_GETITEMPOSITION = 0x1010, LVM_SETITEMPOSITION32 = 0x1031, LVM_GETITEMTEXTW = 0x1073;
        const int GWL_STYLE = -16, LVS_AUTOARRANGE = 0x0100;

        static IntPtr FindListView()
        {
            IntPtr found = IntPtr.Zero;
            // Ярлыки живут в Progman, а если обои анимированные (Wallpaper Engine) — в одном из окон WorkerW
            foreach (var cls in new[] { "Progman", "WorkerW" })
            {
                IntPtr top = IntPtr.Zero;
                while ((top = DockNative.FindWindowEx(IntPtr.Zero, top, cls, null)) != IntPtr.Zero)
                {
                    IntPtr view = DockNative.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (view == IntPtr.Zero) continue;
                    found = DockNative.FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
                    if (found != IntPtr.Zero) return found;
                }
            }
            return found;
        }

        static Dictionary<string, Native.POINT> Read()
        {
            var res = new Dictionary<string, Native.POINT>();
            IntPtr lv = FindListView();
            if (lv == IntPtr.Zero) return res;
            if ((Native.GetWindowLongPtr(lv, GWL_STYLE).ToInt64() & LVS_AUTOARRANGE) != 0) return res;   // Windows расставляет сам
            using var mem = Remote.Open(lv);
            if (mem == null) return res;
            int count = (int)Send(lv, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
            if (count <= 0 || count > 2000) return res;
            for (int i = 0; i < count; i++)
            {
                string name = mem.ItemText(lv, i);
                if (string.IsNullOrEmpty(name)) continue;
                var pt = mem.ItemPosition(lv, i);
                if (pt == null) continue;
                // Одинаковые имена (редко) — добавляем номер, чтобы не перепутать
                string key = name;
                for (int n = 2; res.ContainsKey(key); n++) key = name + "#" + n;
                res[key] = pt.Value;
            }
            return res;
        }

        static void Restore(Dictionary<string, Native.POINT> saved)
        {
            IntPtr lv = FindListView();
            if (lv == IntPtr.Zero) return;
            if ((Native.GetWindowLongPtr(lv, GWL_STYLE).ToInt64() & LVS_AUTOARRANGE) != 0) return;
            using var mem = Remote.Open(lv);
            if (mem == null) return;
            int count = (int)Send(lv, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
            if (count <= 0 || count > 2000) return;
            var seen = new Dictionary<string, int>();
            int moved = 0;
            for (int i = 0; i < count; i++)
            {
                string name = mem.ItemText(lv, i);
                if (string.IsNullOrEmpty(name)) continue;
                seen.TryGetValue(name, out int n);
                seen[name] = ++n;
                string key = n == 1 ? name : name + "#" + n;
                if (!saved.TryGetValue(key, out var want)) continue;
                var now = mem.ItemPosition(lv, i);
                if (now == null || (now.Value.X == want.X && now.Value.Y == want.Y)) continue;
                mem.SetItemPosition(lv, i, want);
                moved++;
            }
            if (moved > 0) DockNative.RedrawWindow(lv, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0100);   // INVALIDATE | ERASE | UPDATENOW
        }

        /// <summary>Сообщение списку ярлыков. Проводник не ответил за 0.2 с — бросаем всю операцию, ничего не трогаем.</summary>
        static IntPtr Send(IntPtr h, uint msg, IntPtr w, IntPtr l)
        {
            if (DockNative.SendMessageTimeout(h, msg, w, l, DockNative.SMTO_ABORTIFHUNG, 200, out IntPtr r) == IntPtr.Zero)
                throw new TimeoutException("desktop icons: Explorer is busy");
            return r;
        }

        /// <summary>Кусочек памяти в процессе Проводника: туда список пишет позиции и имена ярлыков.</summary>
        sealed class Remote : IDisposable
        {
            IntPtr _proc, _buf;
            const int Size = 4096;

            public static Remote Open(IntPtr lv)
            {
                DockNative.GetWindowThreadProcessId(lv, out uint pid);
                if (pid == 0) return null;
                IntPtr proc = OpenProcess(0x0008 | 0x0010 | 0x0020 | 0x0400, false, pid);   // VM_OPERATION | VM_READ | VM_WRITE | QUERY_INFORMATION
                if (proc == IntPtr.Zero) return null;
                IntPtr buf = VirtualAllocEx(proc, IntPtr.Zero, (IntPtr)Size, 0x1000 | 0x2000, 0x04);   // MEM_COMMIT|RESERVE, PAGE_READWRITE
                if (buf == IntPtr.Zero) { CloseHandle(proc); return null; }
                return new Remote { _proc = proc, _buf = buf };
            }

            public Native.POINT? ItemPosition(IntPtr lv, int i)
            {
                if (Send(lv, LVM_GETITEMPOSITION, (IntPtr)i, _buf) == IntPtr.Zero) return null;
                var bytes = new byte[8];
                if (!ReadProcessMemory(_proc, _buf, bytes, (IntPtr)8, out _)) return null;
                return new Native.POINT { X = BitConverter.ToInt32(bytes, 0), Y = BitConverter.ToInt32(bytes, 4) };
            }

            public void SetItemPosition(IntPtr lv, int i, Native.POINT p)
            {
                var bytes = new byte[8];
                BitConverter.GetBytes(p.X).CopyTo(bytes, 0);
                BitConverter.GetBytes(p.Y).CopyTo(bytes, 4);
                if (!WriteProcessMemory(_proc, _buf, bytes, (IntPtr)8, out _)) return;
                Send(lv, LVM_SETITEMPOSITION32, (IntPtr)i, _buf);
            }

            /// <summary>Имя ярлыка. LVITEMW (88 байт, x64) в начале буфера, текст — сразу за ним.</summary>
            public string ItemText(IntPtr lv, int i)
            {
                const int itemSize = 88, textOff = 128, maxChars = (Size - textOff) / 2;
                var item = new byte[itemSize];
                BitConverter.GetBytes(0).CopyTo(item, 8);                                   // iSubItem
                BitConverter.GetBytes((long)(_buf + textOff)).CopyTo(item, 24);             // pszText
                BitConverter.GetBytes(maxChars).CopyTo(item, 32);                           // cchTextMax
                if (!WriteProcessMemory(_proc, _buf, item, (IntPtr)itemSize, out _)) return null;
                int len = (int)Send(lv, LVM_GETITEMTEXTW, (IntPtr)i, _buf);
                if (len <= 0) return null;
                var text = new byte[Math.Min(len, maxChars - 1) * 2];
                if (!ReadProcessMemory(_proc, _buf + textOff, text, (IntPtr)text.Length, out _)) return null;
                return Encoding.Unicode.GetString(text);
            }

            public void Dispose()
            {
                if (_buf != IntPtr.Zero) VirtualFreeEx(_proc, _buf, IntPtr.Zero, 0x8000);   // MEM_RELEASE
                if (_proc != IntPtr.Zero) CloseHandle(_proc);
                _buf = _proc = IntPtr.Zero;
            }

            [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
            [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
            [DllImport("kernel32.dll")] static extern IntPtr VirtualAllocEx(IntPtr proc, IntPtr addr, IntPtr size, uint type, uint protect);
            [DllImport("kernel32.dll")] static extern bool VirtualFreeEx(IntPtr proc, IntPtr addr, IntPtr size, uint type);
            [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr proc, IntPtr addr, byte[] buf, IntPtr size, out IntPtr read);
            [DllImport("kernel32.dll")] static extern bool WriteProcessMemory(IntPtr proc, IntPtr addr, byte[] buf, IntPtr size, out IntPtr written);
        }
    }
}
