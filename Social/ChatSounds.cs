// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace MizuEngine
{
    /// <summary>
    /// Звуки сообщений: 10 для входящих, 10 для отправки и свой файл (.wav / .mp3).
    /// Встроенные звуки не скачиваются, а синтезируются программой (оригинальные звуки iPhone и Telegram
    /// защищены авторским правом — у нас свои, похожие по характеру). Готовые звуки кэшируются в %AppData%\MizuEngine\sounds.
    /// </summary>
    internal static class ChatSounds
    {
        public static readonly (string Id, string Key)[] Incoming =
        {
            ("tg", "snd.tg"), ("ding", "snd.ding"), ("chime", "snd.chime"), ("bubble", "snd.bubble"), ("glass", "snd.glass"),
            ("marimba", "snd.marimba"), ("pluck", "snd.pluck"), ("crystal", "snd.crystal"), ("soft", "snd.soft"), ("retro", "snd.retro")
        };

        public static readonly (string Id, string Key)[] Outgoing =
        {
            ("swoosh", "snd.swoosh"), ("tg_out", "snd.tg_out"), ("ding_out", "snd.ding_out"), ("pop", "snd.pop"), ("tap", "snd.tap"),
            ("rise", "snd.rise"), ("drop", "snd.drop"), ("click", "snd.click"), ("sparkle", "snd.sparkle"), ("plane", "snd.plane")
        };

        public static Settings S;
        static readonly List<MediaPlayer> Playing = new List<MediaPlayer>();   // держим, пока играет (иначе сборщик мусора оборвёт звук)
        const int Version = 1;   // поменяли синтез — меняем номер, и кэш пересоздаётся

        static string Folder => Path.Combine(Settings.Folder, "sounds");

        public static bool IsValid(string id, bool incoming, string custom) =>
            id == "custom" ? !string.IsNullOrEmpty(custom) && File.Exists(custom)
                           : Array.Exists(incoming ? Incoming : Outgoing, x => x.Id == id);

        /// <summary>Новое сообщение пришло.</summary>
        public static void PlayIn() { if (S != null && S.MsgSounds) Play(S.MsgSoundIn, S.MsgCustomIn); }

        /// <summary>Сообщение отправлено.</summary>
        public static void PlayOut() { if (S != null && S.MsgSounds) Play(S.MsgSoundOut, S.MsgCustomOut); }

        /// <summary>Прослушать звук в настройках (играет, даже если звуки выключены).</summary>
        public static void Preview(string id, string custom) => Play(id, custom);

        /// <summary>Копирует свой звук в папку программы (исходный файл можно потом удалить). Возвращает новый путь или null.</summary>
        public static string ImportCustom(string file, bool incoming)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".wav" && ext != ".mp3" && ext != ".wma" && ext != ".m4a") return null;
                if (new FileInfo(file).Length > 8 * 1024 * 1024) return null;   // больше 8 МБ — это уже не звук уведомления
                string dest = Path.Combine(Folder, (incoming ? "custom_in" : "custom_out") + ext);
                foreach (var old in Directory.GetFiles(Folder, (incoming ? "custom_in" : "custom_out") + ".*")) File.Delete(old);
                File.Copy(file, dest, true);
                return dest;
            }
            catch (Exception e) { Settings.Log(e); return null; }
        }

        static void Play(string id, string custom)
        {
            var app = Application.Current;
            if (app == null) return;
            if (!app.Dispatcher.CheckAccess()) { app.Dispatcher.BeginInvoke(new Action(() => Play(id, custom))); return; }
            try
            {
                string file = id == "custom" ? custom : Cached(id);
                if (string.IsNullOrEmpty(file) || !File.Exists(file)) return;
                var p = new MediaPlayer { Volume = Math.Clamp((S?.MsgVolume ?? 80) / 100.0, 0, 1) };
                Playing.Add(p);
                void Done() { p.Close(); Playing.Remove(p); }
                p.MediaEnded += (s, e) => Done();
                p.MediaFailed += (s, e) => Done();
                p.Open(new Uri(file));
                p.Play();
                if (Playing.Count > 6) { Playing[0].Close(); Playing.RemoveAt(0); }
            }
            catch (Exception e) { Settings.Log(e); }
        }

        /// <summary>Готовый .wav встроенного звука (создаётся один раз).</summary>
        static string Cached(string id)
        {
            string dir = Path.Combine(Folder, "cache");
            string path = Path.Combine(dir, $"{id}.v{Version}.wav");
            if (File.Exists(path)) return path;
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, Wav(Synth(id)));
            return path;
        }

        // ================== синтез ==================

        const int Rate = 44100;

        static float[] Synth(string id)
        {
            switch (id)
            {
                // ---------- входящие ----------
                case "tg":       // как в Telegram: мягкий «пузырёк» в две нотки
                    return Render(0.5, t =>
                        Tone(t, 0, 987.8, 22, 2.0, 0.22) * Drop(t, 0, 0.04) +
                        Tone(t, 0.075, 1318.5, 14, 2.0, 0.18) * 0.9);
                case "ding":     // «дзинь», как у iPhone: светлый колокольчик с долгим хвостом
                    return Render(1.4, t => Bell(t, 0, 1760, 3.2) * 0.9);
                case "chime":    // три колокольчика вверх
                    return Render(1.1, t => Bell(t, 0, 1318.5, 5) * 0.6 + Bell(t, 0.09, 1661.2, 5) * 0.6 + Bell(t, 0.18, 1975.5, 4) * 0.65);
                case "bubble":   // два пузырька
                    return Render(0.35, t => Sweep(t, 0, 520, 1150, 0.06, 26) + Sweep(t, 0.11, 680, 1450, 0.06, 26) * 0.8);
                case "glass":    // звон бокала
                    return Render(0.9, t => (Tone(t, 0, 2093, 6, 1.5, 0.1) + Tone(t, 0, 3136, 8, 1.5, 0.05) * 0.5 + Tone(t, 0, 4186, 10, 1, 0) * 0.25) * 0.7);
                case "marimba":  // деревянная маримба
                    return Render(0.7, t => Fm(t, 0, 880, 4, 3.5, 9) + Fm(t, 0.1, 1318.5, 4, 3.5, 8) * 0.9);
                case "pluck":    // щипок струны
                    return Pluck(659.3, 0.8, 0.996);
                case "crystal":  // хрусталь с переливом
                    return Render(1.0, t => (Bell(t, 0, 2637, 4) + Bell(t, 0.05, 3520, 5) * 0.6) * (0.75 + 0.25 * Math.Sin(2 * Math.PI * 9 * t)) * 0.7);
                case "soft":     // мягкое «ми-ля»
                    return Render(0.7, t => Tone(t, 0, 784, 9, 0.6, 0.08) * 0.8 + Tone(t, 0.12, 1046.5, 7, 0.6, 0.08) * 0.8);
                case "retro":    // 8-бит
                    return Render(0.32, t => Square(t, 0, 987.8, 0.08, 18) + Square(t, 0.08, 1318.5, 0.2, 14));

                // ---------- отправка ----------
                case "swoosh":   // «вжух» вверх с мягким хлопком
                {
                    var rnd = new Random(7);
                    double air = 0, phase = 0;
                    return Render(0.22, t =>
                    {
                        double f = 480 + 1050 * (1 - Math.Exp(-t * 26));
                        phase += 2 * Math.PI * f / Rate;
                        air += 0.12 * (rnd.NextDouble() * 2 - 1 - air);
                        return Math.Sin(phase) * (1 - Math.Exp(-t * 400)) * Math.Exp(-t * 24) * 0.75 + air * 1.2 * Math.Exp(-t * 34);
                    });
                }
                case "tg_out":   // как в Telegram: короткий лёгкий «тик»
                    return Render(0.16, t => Tone(t, 0, 1480, 45, 1.2, 0.2) * Drop(t, 0, 0.03) * 0.9);
                case "ding_out": // маленький колокольчик
                    return Render(0.6, t => Bell(t, 0, 2349.3, 8) * 0.7);
                case "pop":      // хлопок
                    return Render(0.12, t => Sweep(t, 0, 950, 280, 0.05, 40));
                case "tap":      // тап по стеклу
                {
                    var rnd = new Random(3);
                    return Render(0.08, t => Tone(t, 0, 2200, 70, 0.5, 0) * 0.6 + (rnd.NextDouble() * 2 - 1) * Math.Exp(-t * 160) * 0.35);
                }
                case "rise":     // нота вверх
                    return Render(0.2, t => Sweep(t, 0, 520, 1560, 0.11, 18));
                case "drop":     // капля
                {
                    double phase = 0;
                    return Render(0.14, t =>
                    {
                        double f = 400 + 1100 * Math.Exp(-t * 22);
                        phase += 2 * Math.PI * f / Rate;
                        return Math.Sin(phase) * Math.Exp(-t * 30);
                    });
                }
                case "click":    // мягкий щелчок
                    return Render(0.09, t => (Math.Sin(2 * Math.PI * 880 * t) + 0.3 * Math.Sin(2 * Math.PI * 1760 * t)) * Math.Exp(-t * 60));
                case "sparkle":  // искорки
                    return Render(0.5, t => Bell(t, 0, 2637, 14) * 0.45 + Bell(t, 0.06, 3136, 14) * 0.4 + Bell(t, 0.12, 3951, 12) * 0.4);
                case "plane":    // бумажный самолётик улетает
                {
                    var rnd = new Random(11);
                    double lp = 0, phase = 0;
                    return Render(0.38, t =>
                    {
                        double cut = 0.04 + 0.35 * (t / 0.38);           // шум «светлеет» — самолётик удаляется
                        lp += cut * (rnd.NextDouble() * 2 - 1 - lp);
                        phase += 2 * Math.PI * (700 + 900 * t / 0.38) / Rate;
                        double env = Math.Sin(Math.PI * Math.Min(1, t / 0.38));
                        return (lp * 2.2 + Math.Sin(phase) * 0.18) * env;
                    });
                }
            }
            return Render(0.1, t => Math.Sin(2 * Math.PI * 880 * t) * Math.Exp(-t * 50));
        }

        /// <summary>Генерирует сэмплы: f(t) вызывается для каждого момента времени.</summary>
        static float[] Render(double seconds, Func<double, double> f)
        {
            int n = (int)(Rate * seconds);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / Rate;
                double fadeOut = Math.Min(1, (seconds - t) / 0.02);   // без щелчка в конце
                s[i] = (float)(f(t) * Math.Min(1, t / 0.002) * fadeOut);
            }
            return s;
        }

        /// <summary>Нота с атакой и затуханием; overtone — доля второй гармоники, wobble — лёгкая «живость».</summary>
        static double Tone(double t, double start, double f, double decay, double attackMs, double overtone)
        {
            double u = t - start;
            if (u < 0) return 0;
            double env = Math.Min(1, u / (attackMs / 1000 + 1e-6)) * Math.Exp(-u * decay);
            return (Math.Sin(2 * Math.PI * f * u) + overtone * Math.Sin(2 * Math.PI * f * 2 * u)) * env;
        }

        /// <summary>Лёгкое «проседание» высоты в начале ноты — звук получается округлым, как капля.</summary>
        static double Drop(double t, double start, double len)
        {
            double u = t - start;
            return u < 0 ? 0 : 1 - 0.08 * Math.Exp(-u / len);
        }

        /// <summary>Колокол: неровные обертона (как у настоящего металла), высокие гаснут быстрее.</summary>
        static double Bell(double t, double start, double f, double decay)
        {
            double u = t - start;
            if (u < 0) return 0;
            double[] ratio = { 1, 2.0, 2.76, 5.4, 8.93 };
            double[] amp = { 1, 0.45, 0.32, 0.14, 0.06 };
            double v = 0;
            for (int k = 0; k < ratio.Length; k++)
                v += amp[k] * Math.Sin(2 * Math.PI * f * ratio[k] * u) * Math.Exp(-u * decay * (1 + ratio[k] * 0.35));
            return v * Math.Min(1, u / 0.0015) * 0.55;
        }

        /// <summary>Нота с плавным скольжением частоты (пузырёк, хлопок).</summary>
        static double Sweep(double t, double start, double f0, double f1, double len, double decay)
        {
            double u = t - start;
            if (u < 0) return 0;
            // Частота скользит от f0 к f1 по кривой 2k - k² (быстро в начале, мягко в конце); фаза — её интеграл
            double glide = u <= len ? u * u / len - u * u * u / (3 * len * len) : 2 * len / 3 + (u - len);
            double phase = 2 * Math.PI * (f0 * u + (f1 - f0) * glide);
            return Math.Sin(phase) * Math.Exp(-u * decay) * 0.8;
        }

        /// <summary>FM-синтез — деревянные и металлические звуки.</summary>
        static double Fm(double t, double start, double f, double ratio, double index, double decay)
        {
            double u = t - start;
            if (u < 0) return 0;
            double env = Math.Exp(-u * decay) * Math.Min(1, u / 0.002);
            double mod = index * Math.Exp(-u * decay * 2.5) * Math.Sin(2 * Math.PI * f * ratio * u);
            return Math.Sin(2 * Math.PI * f * u + mod) * env * 0.7;
        }

        /// <summary>Мягкий «квадрат» для 8-битного звука.</summary>
        static double Square(double t, double start, double f, double len, double decay)
        {
            double u = t - start;
            if (u < 0 || u > len + 0.15) return 0;
            double v = Math.Tanh(4 * Math.Sin(2 * Math.PI * f * u));
            return v * Math.Exp(-u * decay) * 0.35;
        }

        /// <summary>Струна по алгоритму Карплуса — Стронга.</summary>
        static float[] Pluck(double f, double seconds, double damping)
        {
            int n = (int)(Rate * seconds), period = (int)(Rate / f);
            var buf = new double[period];
            var rnd = new Random(5);
            for (int i = 0; i < period; i++) buf[i] = rnd.NextDouble() * 2 - 1;
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                int a = i % period, b = (i + 1) % period;
                double v = buf[a];
                buf[a] = damping * 0.5 * (buf[a] + buf[b]);
                double fadeOut = Math.Min(1, (n - i) / (Rate * 0.03));
                s[i] = (float)(v * 0.6 * fadeOut);
            }
            return s;
        }

        static byte[] Wav(float[] samples)
        {
            // Нормализуем громкость, чтобы все звуки звучали одинаково громко
            float peak = 0.0001f;
            foreach (var v in samples) peak = Math.Max(peak, Math.Abs(v));
            double gain = 0.6 / peak;

            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            int n = samples.Length;
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + n * 2); w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(n * 2);
            foreach (var v in samples) w.Write((short)(Math.Clamp(v * gain, -1, 1) * 32767));
            w.Flush();
            return ms.ToArray();
        }
    }
}
