// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Text;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace MizuEngine
{
    /// <summary>Тихие звуки нажатий и звуки сообщений в чатах MizuEngine. Звуки синтезируются в памяти — без файлов.</summary>
    internal static class UiSounds
    {
        public static bool On;
        public static string Pack = "soft";
        /// <summary>Громкость звуков интерфейса, 0–100 (отдельно от звуков сообщений).</summary>
        public static int Volume = 70;

        /// <summary>12 наборов звуков нажатий, в духе macOS и iPhone.</summary>
        public static readonly string[] Packs = { "soft", "tap", "glass", "drop", "pop", "bubble", "wood", "keys", "tink", "chime", "water", "crisp" };

        static readonly Dictionary<string, SoundPlayer> Players = new Dictionary<string, SoundPlayer>();
        static bool _hooked;

        /// <summary>Подписка на нажатия всех кнопок, переключателей и вкладок программы (один раз).</summary>
        public static void Init()
        {
            if (_hooked) return;
            _hooked = true;
            EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent, new RoutedEventHandler((s, e) => { if (On) Play(Pack); }));
        }

        public static void Play(string pack)
        {
            // Играем в отдельном потоке звуков (Social/ChatSounds.cs → SoundThread): окно никогда не ждёт звук
            if (!SoundThread.CheckAccess()) { SoundThread.Run(() => Play(pack)); return; }
            try
            {
                int vol = Math.Clamp(Volume, 0, 100);
                if (vol == 0 && IsUi(pack)) return;
                // У SoundPlayer нет громкости — громкость «запекаем» в сам звук (и храним готовый вариант)
                string key = pack + "|" + vol;
                if (!Players.TryGetValue(key, out var p))
                {
                    foreach (var k in new List<string>(Players.Keys))
                        if (k.StartsWith(pack + "|") && k != key) { Players[k].Dispose(); Players.Remove(k); }
                    p = new SoundPlayer(new MemoryStream(Make(pack, IsUi(pack) ? vol / 70.0 : 1)));
                    p.Load();
                    Players[key] = p;
                }
                p.Play();   // играет в фоне, окно не ждёт
            }
            catch (Exception e) { Settings.Log(e); }
        }

        static bool IsUi(string pack) => Array.IndexOf(Packs, pack) >= 0;

        public static string Valid(string pack) => IsUi(pack) ? pack : "soft";

        static byte[] Make(string pack, double gain = 1)
        {
            const int rate = 44100;
            double len = pack switch
            {
                "glass" => 0.2, "drop" => 0.13, "msg_send" => 0.2, "msg_in" => 0.5, "plus_grant" => 2.2,
                "pop" => 0.09, "bubble" => 0.16, "wood" => 0.12, "keys" => 0.07, "tink" => 0.35, "chime" => 0.45, "water" => 0.22, "crisp" => 0.05, "tap" => 0.05,
                _ => 0.08
            };
            var rnd = new Random(7);
            double air = 0;
            int n = (int)(rate * len);
            var samples = new short[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / rate, v;
                switch (pack)
                {
                    case "glass":   // звон стекла: два чистых тона
                        v = (Math.Sin(2 * Math.PI * 2093 * t) + 0.55 * Math.Sin(2 * Math.PI * 3136 * t)) * Math.Exp(-t * 26) * 0.55;
                        break;
                    case "drop":    // капля: тон быстро падает вниз
                        double f = 400 + 1100 * Math.Exp(-t * 22);
                        phase += 2 * Math.PI * f / rate;
                        v = Math.Sin(phase) * Math.Exp(-t * 30);
                        break;
                    case "msg_send":   // отправка: короткий «вжух» вверх и мягкий хлопок, как в Telegram
                    {
                        double fs = 480 + 1050 * (1 - Math.Exp(-t * 26));
                        phase += 2 * Math.PI * fs / rate;
                        air += 0.12 * (rnd.NextDouble() * 2 - 1 - air);           // мягкий шум воздуха
                        double body = Math.Sin(phase) * (1 - Math.Exp(-t * 400)) * Math.Exp(-t * 24);
                        v = body * 0.75 + air * 1.2 * Math.Exp(-t * 34);
                        break;
                    }
                    case "msg_in":     // входящее: две светлые нотки «дин-дон»
                    {
                        double n1 = (Math.Sin(2 * Math.PI * 1175 * t) + 0.25 * Math.Sin(2 * Math.PI * 2350 * t)) * Math.Exp(-t * 16);
                        double t2 = t - 0.11;
                        double n2 = t2 < 0 ? 0 : (Math.Sin(2 * Math.PI * 1568 * t2) + 0.25 * Math.Sin(2 * Math.PI * 3136 * t2)) * Math.Exp(-t2 * 9) * Math.Min(1, t2 / 0.003);
                        v = (n1 + n2) * 0.6;
                        break;
                    }
                    case "plus_grant":   // выдача Mizu Plus: светлое арпеджио «до-ми-соль-до» и мерцающий «звон» сверху
                    {
                        double[] notes = { 1046.5, 1318.5, 1568.0, 2093.0 };
                        v = 0;
                        for (int k = 0; k < notes.Length; k++)
                        {
                            double tk = t - k * 0.11;
                            if (tk < 0) continue;
                            double env = Math.Min(1, tk / 0.004) * Math.Exp(-tk * (k == 3 ? 2.2 : 5.5));
                            v += (Math.Sin(2 * Math.PI * notes[k] * tk) + 0.35 * Math.Sin(2 * Math.PI * notes[k] * 2 * tk) + 0.12 * Math.Sin(2 * Math.PI * notes[k] * 3 * tk)) * env * 0.42;
                        }
                        double ts = t - 0.42;   // блёстки: быстрые высокие нотки, тают
                        if (ts > 0)
                        {
                            double sp = (Math.Sin(2 * Math.PI * 3136 * ts) * Math.Max(0, Math.Sin(2 * Math.PI * 11 * ts)) + 0.6 * Math.Sin(2 * Math.PI * 4186 * ts) * Math.Max(0, Math.Sin(2 * Math.PI * 7 * ts + 1)));
                            v += sp * Math.Exp(-ts * 2.4) * 0.16;
                        }
                        break;
                    }
                    case "tap":     // клавиатура iPhone: короткий сухой «тык»
                        air += 0.5 * (rnd.NextDouble() * 2 - 1 - air);
                        v = (air * 0.8 + 0.5 * Math.Sin(2 * Math.PI * 1900 * t)) * Math.Exp(-t * 160);
                        break;
                    case "pop":     // «поп»: тон быстро уходит вверх
                    {
                        double fp = 300 + 900 * (1 - Math.Exp(-t * 60));
                        phase += 2 * Math.PI * fp / rate;
                        v = Math.Sin(phase) * Math.Exp(-t * 48);
                        break;
                    }
                    case "bubble":  // пузырёк: тон плывёт вверх с покачиванием
                    {
                        double fb = 520 + 700 * t / 0.16 + 60 * Math.Sin(2 * Math.PI * 38 * t);
                        phase += 2 * Math.PI * fb / rate;
                        v = Math.Sin(phase) * Math.Sin(Math.PI * Math.Min(1, t / 0.16)) * 0.9;
                        break;
                    }
                    case "wood":    // деревянный стук: низкие негармоничные тона
                        v = (Math.Sin(2 * Math.PI * 520 * t) + 0.6 * Math.Sin(2 * Math.PI * 1310 * t) + 0.3 * Math.Sin(2 * Math.PI * 2380 * t)) * Math.Exp(-t * 55) * 0.6;
                        break;
                    case "keys":    // механическая клавиатура: щелчок и короткий «стук»
                        air += 0.7 * (rnd.NextDouble() * 2 - 1 - air);
                        v = air * Math.Exp(-t * 260) + 0.6 * Math.Sin(2 * Math.PI * 240 * t) * Math.Exp(-t * 90);
                        break;
                    case "tink":    // как «Tink» в macOS: высокий металлический звон
                        v = (Math.Sin(2 * Math.PI * 2637 * t) + 0.4 * Math.Sin(2 * Math.PI * 5274 * t) + 0.2 * Math.Sin(2 * Math.PI * 3951 * t)) * Math.Exp(-t * 14) * 0.45;
                        break;
                    case "chime":   // колокольчик: две ноты подряд
                    {
                        double c1 = Math.Sin(2 * Math.PI * 1568 * t) * Math.Exp(-t * 10);
                        double tc = t - 0.07;
                        double c2 = tc < 0 ? 0 : Math.Sin(2 * Math.PI * 2093 * tc) * Math.Exp(-tc * 9) * Math.Min(1, tc / 0.003);
                        v = (c1 + c2) * 0.45;
                        break;
                    }
                    case "water":   // капля в воду: тон падает и «булькает»
                    {
                        double fw = 1500 * Math.Exp(-t * 14) + 250;
                        phase += 2 * Math.PI * fw / rate;
                        v = Math.Sin(phase) * Math.Exp(-t * 18) * (0.8 + 0.2 * Math.Sin(2 * Math.PI * 60 * t));
                        break;
                    }
                    case "crisp":   // чёткий щелчок трекпада MacBook
                        v = (Math.Sin(2 * Math.PI * 3200 * t) * 0.6 + Math.Sin(2 * Math.PI * 160 * t) * 0.8) * Math.Exp(-t * 220);
                        break;
                    default:        // мягкий щелчок
                        v = (Math.Sin(2 * Math.PI * 880 * t) + 0.3 * Math.Sin(2 * Math.PI * 1760 * t)) * Math.Exp(-t * 65);
                        break;
                }
                double attack = Math.Min(1, t / 0.002);   // без щелчка в начале
                samples[i] = (short)(Math.Clamp(v * attack * gain * 0.32, -1, 1) * 32767);
            }

            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + n * 2); w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(n * 2);
            foreach (var s in samples) w.Write(s);
            w.Flush();
            return ms.ToArray();
        }
    }
}
