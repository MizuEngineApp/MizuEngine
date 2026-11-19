// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

namespace MizuEngine
{
    /// <summary>
    /// Считает кадры игр так же, как PresentMon: слушает события Windows (ETW) о том, что программа показала кадр (Present).
    /// Работает для DirectX 9–12 и, через ядро видеодрайвера, почти для всего остального.
    /// Windows разрешает слушать эти события только с правами администратора.
    /// </summary>
    internal static class PresentCounter
    {
        const string SessionName = "MizuEngine FPS";
        static readonly Guid Dxgi = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
        static readonly Guid D3d9 = new Guid("783ACA0A-790E-4D7F-8451-AA850511C6B9");
        static readonly Guid DxgKrnl = new Guid("802EC45A-1E99-4B83-9920-87C98277BA9D");

        static readonly ConcurrentDictionary<int, int> Api = new ConcurrentDictionary<int, int>();     // DXGI / D3D9
        static readonly ConcurrentDictionary<int, int> Kernel = new ConcurrentDictionary<int, int>();  // драйвер (OpenGL, Vulkan)
        static readonly EventRecordCallback Callback = OnEvent;   // держим ссылку, иначе сборщик мусора удалит делегат

        static ulong _session, _trace;
        static IntPtr _props, _logfile, _name;
        static Thread _thread;

        public static bool Running { get; private set; }

        public static bool IsAdmin
        {
            get
            {
                try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
                catch { return false; }
            }
        }

        public static bool Start()
        {
            if (Running) return true;
            if (!IsAdmin) return false;
            try
            {
                _props = Props();
                uint err = StartTraceW(out _session, SessionName, _props);
                if (err == 183)   // ERROR_ALREADY_EXISTS — осталась сессия от прошлого запуска
                {
                    ControlTraceW(0, SessionName, _props, 1);
                    Marshal.FreeHGlobal(_props);
                    _props = Props();
                    err = StartTraceW(out _session, SessionName, _props);
                }
                if (err != 0) { Settings.Log(new Exception("ETW StartTrace " + err)); Cleanup(); return false; }

                var g = Dxgi; EnableTraceEx2(_session, ref g, 1, 4, ulong.MaxValue, 0, 0, IntPtr.Zero);
                g = D3d9; EnableTraceEx2(_session, ref g, 1, 4, ulong.MaxValue, 0, 0, IntPtr.Zero);
                g = DxgKrnl; EnableTraceEx2(_session, ref g, 1, 4, 0x1 | 0x8000000, 0, 0, IntPtr.Zero);

                // EVENT_TRACE_LOGFILEW (448 байт в 64-битной Windows)
                _logfile = Marshal.AllocHGlobal(448);
                for (int i = 0; i < 448; i += 8) Marshal.WriteInt64(_logfile, i, 0);
                _name = Marshal.StringToHGlobalUni(SessionName);
                Marshal.WriteIntPtr(_logfile, 8, _name);                                       // LoggerName
                Marshal.WriteInt32(_logfile, 28, 0x00000100 | 0x10000000);                      // REAL_TIME | EVENT_RECORD
                Marshal.WriteIntPtr(_logfile, 424, Marshal.GetFunctionPointerForDelegate(Callback));
                _trace = OpenTraceW(_logfile);
                if (_trace == ulong.MaxValue) { Settings.Log(new Exception("ETW OpenTrace " + Marshal.GetLastWin32Error())); Stop(); return false; }

                Running = true;
                _thread = new Thread(() =>
                {
                    try { ProcessTrace(new[] { _trace }, 1, IntPtr.Zero, IntPtr.Zero); }   // работает, пока сессию не остановят
                    catch (Exception e) { Settings.Log(e); }
                })
                { IsBackground = true, Name = "MizuEngine FPS" };
                _thread.Start();
                return true;
            }
            catch (Exception e) { Settings.Log(e); Stop(); return false; }
        }

        public static void Stop()
        {
            Running = false;
            try
            {
                if (_session != 0 && _props != IntPtr.Zero) ControlTraceW(_session, null, _props, 1);   // EVENT_TRACE_CONTROL_STOP
                if (_trace != 0 && _trace != ulong.MaxValue) CloseTrace(_trace);
            }
            catch { }
            _session = 0;
            _trace = 0;
            Cleanup();
        }

        static void Cleanup()
        {
            if (_props != IntPtr.Zero) { Marshal.FreeHGlobal(_props); _props = IntPtr.Zero; }
            // _logfile и _name не освобождаем, пока поток ProcessTrace может их читать; это 1 КБ на весь запуск
        }

        /// <summary>Сколько кадров показал процесс с прошлого вызова. Счётчики остальных процессов сбрасываются.</summary>
        public static int Take(int pid)
        {
            Api.TryGetValue(pid, out int a);
            Kernel.TryGetValue(pid, out int k);
            Api.Clear();
            Kernel.Clear();
            return a > 0 ? a : k;   // у игр на DirectX считаем по DXGI, иначе — по драйверу
        }

        static void OnEvent(IntPtr rec)
        {
            // EVENT_HEADER: ProcessId @12, ProviderId @24, EventDescriptor.Id @40
            int pid = Marshal.ReadInt32(rec, 12);
            int g0 = Marshal.ReadInt32(rec, 24);
            int id = (ushort)Marshal.ReadInt16(rec, 40);
            if ((g0 == unchecked((int)0xCA11C036) && id == 42) || (g0 == unchecked((int)0x783ACA0A) && id == 1))
                Api.AddOrUpdate(pid, 1, (_, n) => n + 1);
            else if (g0 == unchecked((int)0x802EC45A) && id == 184)
                Kernel.AddOrUpdate(pid, 1, (_, n) => n + 1);
        }

        /// <summary>EVENT_TRACE_PROPERTIES (120 байт) + место под имя сессии.</summary>
        static IntPtr Props()
        {
            const int size = 120 + 512;
            var p = Marshal.AllocHGlobal(size);
            for (int i = 0; i < size; i += 8) Marshal.WriteInt64(p, i, 0);
            Marshal.WriteInt32(p, 0, size);              // Wnode.BufferSize
            Marshal.WriteInt32(p, 40, 1);                // Wnode.ClientContext: время по QPC
            Marshal.WriteInt32(p, 44, 0x00020000);       // Wnode.Flags: WNODE_FLAG_TRACED_GUID
            Marshal.WriteInt32(p, 48, 64);               // BufferSize, КБ
            Marshal.WriteInt32(p, 52, 4);                // MinimumBuffers
            Marshal.WriteInt32(p, 56, 32);               // MaximumBuffers
            Marshal.WriteInt32(p, 64, 0x00000100);       // LogFileMode: EVENT_TRACE_REAL_TIME_MODE
            Marshal.WriteInt32(p, 68, 1);                // FlushTimer, с
            Marshal.WriteInt32(p, 116, 120);             // LoggerNameOffset
            return p;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate void EventRecordCallback(IntPtr eventRecord);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern uint StartTraceW(out ulong handle, string name, IntPtr props);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern uint ControlTraceW(ulong handle, string name, IntPtr props, uint code);
        [DllImport("advapi32.dll")] static extern uint EnableTraceEx2(ulong handle, ref Guid provider, uint control, byte level, ulong any, ulong all, uint timeout, IntPtr param);
        [DllImport("advapi32.dll", SetLastError = true)] static extern ulong OpenTraceW(IntPtr logfile);
        [DllImport("advapi32.dll")] static extern uint ProcessTrace(ulong[] handles, uint count, IntPtr start, IntPtr end);
        [DllImport("advapi32.dll")] static extern uint CloseTrace(ulong handle);
    }
}
