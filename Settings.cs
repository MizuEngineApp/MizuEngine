// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MizuEngine
{
    /// <summary>Настройки, которые сохраняются между запусками (%AppData%\MizuEngine\settings.json).</summary>
    internal sealed class Settings
    {
        // Эффекты
        public bool EffectsOn { get; set; } = true;
        public string Weather { get; set; } = "snow";
        public int Flakes { get; set; } = 260;
        public bool SnowOnWindows { get; set; } = true;
        public bool GroundDrift { get; set; } = true;
        public bool CursorScrape { get; set; } = true;
        public bool CursorTrail { get; set; }
        public bool CursorReact { get; set; } = true;     // эффекты реагируют на курсор (листья, пузыри, бабочки, пыльца, гирлянда)
        public string CustomParticle { get; set; }       // своя картинка-частица для погоды «Свои частицы»

        // Приложение
        public string Theme { get; set; } = "blue";
        public string Language { get; set; } = "system";
        public bool FirstRunDone { get; set; }
        public string CustomThemeImage { get; set; }     // фон своей темы
        public string CustomAccent { get; set; } = "#FF4A97FF";

        // Док
        public bool DockEnabled { get; set; }
        public int DockSize { get; set; } = 46;
        public double DockMagnify { get; set; } = 1.6;
        public int DockTransparency { get; set; } = 40;
        public bool DockClock { get; set; } = true;
        public bool DockBlur { get; set; }                   // размытие под доком; у новых пользователей выключено (с 0.10.2)
        public bool DockAutohide { get; set; }
        public bool DockAnimations { get; set; } = true;
        public List<DockPin> DockPins { get; set; }          // закреплённые значки по порядку (null — ещё не переносили с панели задач Windows)
        public bool DockTray { get; set; } = true;           // значки трея внутри дока
        public bool DockAllMonitors { get; set; } = true;    // свой док на каждом мониторе (с 0.11); второй монитор подключили — док там сразу
        public List<string> DockMonitorsOff { get; set; } = new List<string>();   // мониторы (MonitorInfo.Id), где дока не нужно
        public int SavedMinAnimate { get; set; } = -1;       // системная анимация сворачивания была включена (1) — вернуть при выходе

        // Dynamic Island
        public bool IslandOn { get; set; } = true;
        public bool IslandMedia { get; set; } = true;        // музыка из любого плеера
        public bool IslandNotify { get; set; } = true;       // сообщения MizuEngine
        public bool IslandHideIdle { get; set; }             // прятать, когда ничего не происходит
        public string IslandFly { get; set; } = "up";        // up — плееры, таймеры и MizuEngine сворачиваются в Island; down — в док

        // Чаты: звуки и уведомления
        public bool MsgNotify { get; set; } = true;
        public bool MsgSounds { get; set; } = true;
        public string MsgSoundIn { get; set; } = "tg";
        public string MsgSoundOut { get; set; } = "swoosh";
        public string MsgCustomIn { get; set; }              // свой файл для входящих (.wav / .mp3)
        public string MsgCustomOut { get; set; }             // свой файл для отправки
        public int MsgVolume { get; set; } = 80;
        public Dictionary<string, long> GroupRead { get; set; } = new Dictionary<string, long>();   // группа → последнее прочитанное сообщение
        public bool NotifyPreview { get; set; } = true;      // показывать текст сообщения в уведомлении
        public bool NotifyDnd { get; set; }                  // «Не беспокоить»: без звука и всплывающих уведомлений

        // Окно
        public bool SafeRender { get; set; }                 // безопасный режим: окно без прозрачности (если интерфейс не виден)
        public bool CloseToTray { get; set; } = true;        // крестик прячет в трей (false — закрывает MizuEngine)
        // Анимации интерфейса
        public string UiAnim { get; set; } = "flight";      // flight, fade, slide, zoom, none
        public double UiAnimSpeed { get; set; } = 1.0;

        // Кастомизация
        public string CursorPack { get; set; } = "";         // "" — стандартные курсоры Windows
        public int CursorSize { get; set; } = 32;
        public string CustomCursor { get; set; }            // свой файл .cur / .ani / .png
        public bool FpsOn { get; set; }
        public bool FpsLocked { get; set; }                 // клики проходят сквозь счётчик
        public bool FpsDetailed { get; set; } = true;       // + загрузка процессора и видеокарты
        public string FpsPos { get; set; }                  // "x,y" на экране
        public bool WidgetSystem { get; set; }
        public bool WidgetClock { get; set; }
        public bool WidgetWeather { get; set; }
        public bool WidgetsOnTop { get; set; }
        public string WeatherCity { get; set; } = "";
        public string SystemPos { get; set; }
        public string ClockPos { get; set; }
        public string WeatherPos { get; set; }
        public string BorderMode { get; set; } = "off";      // off, accent, color, rainbow
        public string BorderColor { get; set; } = "#FF4A97FF";
        public string Corners { get; set; } = "default";     // default, round, square
        public bool Sounds { get; set; }
        public string SoundPack { get; set; } = "soft";      // один из UiSounds.Packs
        public int UiVolume { get; set; } = 70;              // громкость звуков интерфейса
        public bool Hotkeys { get; set; } = true;

        // Как была настроена панель задач Windows до дока (-1 — док её не трогал). Нужно, чтобы вернуть её даже после вылета.
        public int SavedTaskbarState { get; set; } = -1;

        // Mizu Plus
        public string VideoTheme { get; set; }               // MP4 — фон приложения (тема "video")
        public int VideoDim { get; set; } = 35;              // затемнение видео-фона, %
        public double LiveBgSpeed { get; set; } = 1.0;       // скорость живого фона окна (видео, GIF, готовые фоны)
        public string LiveWallpaper { get; set; }            // MP4 — живые обои на рабочем столе
        public bool LiveWallpaperOn { get; set; }
        public bool LiveAllMonitors { get; set; }
        public bool LivePauseFullscreen { get; set; } = true;
        public bool LiveSound { get; set; }                  // звук живых обоев (по умолчанию выключен)
        // Последний известный статус подписки — чтобы Plus работал и без интернета (сервер всё равно главный)
        public string PlusUser { get; set; }
        public bool PlusForever { get; set; }
        public DateTime PlusUntil { get; set; }
        public string PlusSeal { get; set; }                 // «печать» на статусе: без неё сохранённый Plus не включается

        // Muzule (Claude внутри MizuEngine, только Mizu Plus; ключ Claude API хранится отдельно, зашифрованным — muzule\key.bin)
        public string MuzuleModel { get; set; } = "claude-sonnet-5-5";   // модель по умолчанию
        public string MuzuleAgent { get; set; } = "mizu";                // последний выбранный агент
        public bool MuzuleSpotlight { get; set; } = true;                // быстрое окно по Ctrl+Alt+M
        public bool MuzuleIsland { get; set; } = true;                   // «думает…» и готовый ответ в Dynamic Island
        public bool MuzuleTyping { get; set; } = true;                   // ответ плавно «печатается»

        /// <summary>Все эффекты рабочего стола (custom — свои частицы из PNG).</summary>
        public static readonly string[] Weathers =
        {
            "snow", "rain", "sakura", "fireflies", "custom", "blizzard", "leaves", "roses", "confetti",
            "bubbles", "butterflies", "pollen", "embers", "stars", "aurora", "matrix", "garland",
            "hearts", "glitter", "crystals", "lanterns", "meteors", "fluff"
        };

        /// <summary>Эффекты только для Mizu Plus.</summary>
        public static readonly string[] PlusWeathers = { "hearts", "glitter", "crystals", "lanterns", "meteors", "fluff" };

        public static bool IsPlusWeather(string w) => Array.IndexOf(PlusWeathers, w) >= 0;

        static readonly object Lock = new object();   // сохраняют два потока: окно и эффекты

        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MizuEngine");
        static string FilePath => Path.Combine(Folder, "settings.json");

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
                    s.Clamp();
                    return s;
                }
            }
            catch { /* битый файл — берём настройки по умолчанию */ }
            return new Settings();
        }

        /// <summary>Значения из старых версий, сборок или испорченного файла приводим к допустимым.</summary>
        public void Clamp()
        {
            CursorPack = Legacy.FixCursorPack(CursorPack);
            DockSize = Math.Clamp(DockSize, 32, 80);
            DockMagnify = Math.Clamp(DockMagnify, 1.0, 2.2);
            DockTransparency = Math.Clamp(DockTransparency, 0, 100);
            DockMonitorsOff ??= new List<string>();
            Flakes = Math.Clamp(Flakes, 50, 1500);
            if (Array.IndexOf(Weathers, Weather) < 0) Weather = "snow";
            if (Weather == "custom" && !File.Exists(CustomParticle)) Weather = "snow";
            if (Theme == "custom" && !File.Exists(CustomThemeImage)) Theme = "blue";
            if (Theme == "video" && !File.Exists(VideoTheme)) Theme = "blue";
            VideoDim = Math.Clamp(VideoDim, 0, 80);
            LiveBgSpeed = double.IsFinite(LiveBgSpeed) ? Math.Clamp(LiveBgSpeed, 0.25, 2.0) : 1.0;
            if (Array.IndexOf(Motion.Styles, UiAnim) < 0) UiAnim = "flight";
            UiAnimSpeed = Math.Clamp(UiAnimSpeed, 0.5, 2.0);
            if (CursorSize != 32 && CursorSize != 48 && CursorSize != 64) CursorSize = 32;
            if (BorderMode != "off" && BorderMode != "accent" && BorderMode != "color" && BorderMode != "rainbow") BorderMode = "off";
            if (Corners != "default" && Corners != "round" && Corners != "square") Corners = "default";
            SoundPack = UiSounds.Valid(SoundPack);
            UiVolume = Math.Clamp(UiVolume, 0, 100);
            if (IslandFly != "up" && IslandFly != "down") IslandFly = "up";
            MsgVolume = Math.Clamp(MsgVolume, 0, 100);
            if (!ChatSounds.IsValid(MsgSoundIn, true, MsgCustomIn)) MsgSoundIn = "tg";
            if (!ChatSounds.IsValid(MsgSoundOut, false, MsgCustomOut)) MsgSoundOut = "swoosh";
            GroupRead ??= new Dictionary<string, long>();
        }

        static bool _frozen;

        /// <summary>«Сбросить все настройки»: удаляем settings.json и больше ничего не записываем до перезапуска.</summary>
        public static void ResetAndFreeze()
        {
            lock (Lock)
            {
                _frozen = true;
                try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
            }
        }

        public void Save()
        {
            if (_frozen) return;
            lock (Lock)
            {
                try
                {
                    Directory.CreateDirectory(Folder);
                    File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch { }
            }
        }

        public void ApplyTo(SnowLayer layer)
        {
            layer.TargetFlakes = Flakes;
            layer.SnowOnWindows = SnowOnWindows;
            layer.GroundDrift = GroundDrift;
            layer.CursorScrape = CursorScrape;
            layer.CustomParticlePath = CustomParticle;
            // Эффект Plus без подписки не включается (настройка сохраняется — вернётся вместе с Plus)
            layer.Weather = IsPlusWeather(Weather) && !Plus.IsActive ? "snow" : Weather;
            layer.Paused = !EffectsOn;
            layer.CursorTrail = CursorTrail;
            layer.CursorReact = CursorReact;
        }

        public static void Log(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(Path.Combine(Folder, "error.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
            }
            catch { }
        }
    }
}
