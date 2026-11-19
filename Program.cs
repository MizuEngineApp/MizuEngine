// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace MizuEngine
{
    public static class Program
    {
        static Action _exit;

        /// <summary>Полный выход из программы (кнопка «Закрыть MizuEngine» на плашке обновления).</summary>
        public static void Quit() => _exit?.Invoke();

        /// <summary>Перезапуск MizuEngine (после сброса настроек или смены режима отрисовки).</summary>
        public static void Restart()
        {
            try { Process.Start(new ProcessStartInfo(Environment.ProcessPath, "--restart") { UseShellExecute = true }); }
            catch (Exception e) { Settings.Log(e); return; }
            _exit?.Invoke();
        }

        /// <summary>Перезапуск с правами администратора (нужно счётчику FPS). false — пользователь отказался.</summary>
        public static bool RestartAsAdmin()
        {
            try
            {
                Process.Start(new ProcessStartInfo(Environment.ProcessPath, "--elevated-restart") { UseShellExecute = true, Verb = "runas" });
            }
            catch { return false; }   // нажали «Нет» в окне Windows
            _exit?.Invoke();
            return true;
        }

        [STAThread]
        public static void Main(string[] args)
        {
            // «MizuEngine.exe --quit» просит запущенную копию аккуратно закрыться (так делает build.bat перед сборкой)
            using var quit = new EventWaitHandle(false, EventResetMode.AutoReset, "MizuEngine_Quit");
            if (Array.IndexOf(args, "--quit") >= 0) { quit.Set(); return; }

            // Официальная сборка проверяет свою подпись: изменённый («взломанный») exe не запускается (Security/Guard.cs)
            if (!Guard.Check())
            {
                MessageBox.Show(Guard.TamperMessage, "MizuEngine", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Повторный запуск не открывает вторую копию, а показывает окно уже запущенной (она могла быть свёрнута в трей)
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset, "MizuEngine_Show");
            using var mutex = new Mutex(true, "MizuEngine_SingleInstance", out bool first);
            bool updated = Array.IndexOf(args, "--updated") >= 0;   // запуск сразу после «Обновить сейчас»
            if (!first && (updated || Array.IndexOf(args, "--elevated-restart") >= 0 || Array.IndexOf(args, "--restart") >= 0))
            {
                // Перезапуск (от администратора, после сброса или обновления): ждём, пока старая копия закроется
                try { first = mutex.WaitOne(15000); } catch (AbandonedMutexException) { first = true; }
            }
            if (!first) { show.Set(); return; }

            // Переезд со старого названия: папка с настройками и аккаунтами, автозапуск (Legacy.cs)
            Legacy.MigrateDataFolder();
            Legacy.MigrateAutostart(MainWindow.RunKey);

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (s, e) => { Settings.Log(e.Exception); e.Handled = true; };

            var settings = Settings.Load();
            // Прошлый запуск вылетел с включённым доком, а док теперь выключен — возвращаем панель задач
            if (settings.SavedTaskbarState >= 0 && !settings.DockEnabled) Taskbar.Restore(settings);
            // …и системную анимацию сворачивания (её выключает наша анимация «джинн»)
            if (settings.SavedMinAnimate >= 0) SysAnim.Restore(settings);
            ChatSounds.S = settings;

            app.Resources["LogoM"] = Logo.Create();
            // Безопасный режим окна: вручную или сам, если Windows рисует WPF без видеокарты (иначе окно бывает пустым)
            Themes.SafeMode = settings.SafeRender || (System.Windows.Media.RenderCapability.Tier >> 16) == 0;
            Loc.Apply(settings.Language);
            Themes.CustomImage = settings.CustomThemeImage;
            Themes.CustomAccent = settings.CustomAccent;
            Themes.VideoFile = settings.VideoTheme;
            Plus.Init(settings);   // подписка Mizu Plus: сохранённый статус сразу, свежий — с сервера после входа
            Themes.Apply(settings.Theme, null);
            Motion.Style = settings.UiAnim;
            Motion.Speed = settings.UiAnimSpeed;
            UiSounds.On = settings.Sounds;
            UiSounds.Pack = settings.SoundPack;
            UiSounds.Volume = settings.UiVolume;
            UiSounds.Init();
            CursorPacks.Apply(settings);
            _ = Supa.RestoreAsync();   // вход в аккаунт с прошлого раза (в фоне)
            SocialApi.StartUsageTracking();   // учёт часов в MizuEngine (серая галочка за 300+ часов)

            var fx = Effects.Start(settings);

            // Панель задач Windows должна вернуться при любом выходе: обычном, выключении ПК или вылете
            void RestoreTaskbar() { try { Taskbar.Restore(settings); SysAnim.Restore(settings); } catch { } }
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { if (e.ExceptionObject is Exception ex) Settings.Log(ex); RestoreTaskbar(); };
            AppDomain.CurrentDomain.ProcessExit += (s, e) => { RestoreTaskbar(); CursorPacks.Restore(); };
            app.SessionEnding += (s, e) => fx.Shutdown();

            // Главное окно создаётся, только когда его открывают, и полностью удаляется при закрытии в трей —
            // так в фоне программа занимает минимум памяти
            MainWindow main = null;
            void Open()
            {
                if (main == null)
                {
                    main = new MainWindow(fx, settings);
                    main.Closed += (s, e) =>
                    {
                        main = null;
                        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(Glass.TrimMemory), DispatcherPriority.ApplicationIdle);
                    };
                }
                main.Show();
                if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
                main.Activate();
            }

            void Exit()
            {
                Online.GoOffline();   // друзья сразу видят серый кружок
                Muzule.StopAll();     // агенты Muzule прерывают запросы (что успели — сохранено)
                LiveWallpaper.Stop();
                CursorPacks.Restore();
                fx.Shutdown();
                if (main != null) { main.AllowClose = true; main.Close(); }
                app.Shutdown();
            }

            using var tray = new Tray(fx, settings, Open, Exit);
            Shell.OpenApp = () => app.Dispatcher.BeginInvoke(new Action(Open));   // «Настройки дока» в меню дока
            // Новые сообщения: цифра на «Чатах», звук и уведомление в углу окна или экрана — даже когда окно закрыто
            Notifier.OpenMain = Open;
            Notifier.Init();
            Inbox.Start(settings);
            Online.Start();   // «в сети», пока программа запущена (даже в трее)
            Diagnostics.RegisterPanicKey(Exit);
            Muzule.Init(settings, fx, Open);   // Claude и агенты (только Mizu Plus): вкладка Muzule и быстрое окно Ctrl+Alt+M
            MizuHotkeys.Init(fx, settings, Open);
            _exit = Exit;

            // Обновления: проверка при запуске и каждые 2 часа. Окно закрыто (программа в трее) — сообщаем уведомлением
            Version notified = null;
            Updater.Changed += () =>
            {
                if (Updater.Status != Updater.State.Available || main != null || Equals(notified, Updater.Latest)) return;
                notified = Updater.Latest;
                tray.Notify(Loc.T("upd.title"), string.Format(Loc.T("upd.tray"), Updater.Short(Updater.Latest)));
            };
            Updater.Start();
            if (updated) tray.Notify(Loc.T("upd.done.t"), string.Format(Loc.T("upd.done"), Updater.Short(Updater.Current)));

            // Mizu Plus: подписка появилась или закончилась — включаем/выключаем то, что работает и без окна
            Plus.Changed += () =>
            {
                if (main == null) Themes.Apply(settings.Theme, null);   // окно открыто — оно перекрасится само
                fx.Run(() => settings.ApplyTo(fx.Snow.Layer));
                CursorPacks.Apply(settings);
                LiveWallpaper.Sync(settings);
                _ = Supa.RefreshDeviceAsync();   // с Plus можно 4 регистрации с ПК
            };
            // Подписка выдана (донат или админ) — открываем окно и поздравляем
            Plus.Granted += g =>
            {
                Open();
                main?.CelebratePlus(g);
            };
            LiveWallpaper.Sync(settings);   // живые обои с прошлого раза (если Plus есть)
            ThreadPool.RegisterWaitForSingleObject(quit, (st, timedOut) => app.Dispatcher.BeginInvoke(new Action(Exit)), null, -1, true);
            ThreadPool.RegisterWaitForSingleObject(show, (st, timedOut) => app.Dispatcher.BeginInvoke(new Action(Open)), null, -1, false);

            // Через 3 и 15 секунд записываем, какие окна у программы и где они (для поиска ошибок)
            foreach (int sec in new[] { 3, 15 })
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(sec) };
                t.Tick += (s, e) => { t.Stop(); Diagnostics.DumpWindows($"{sec} s after start"); };
                t.Start();
            }

            if (Array.IndexOf(args, "--tray") < 0) Open();   // при автозапуске стартуем свёрнутыми в трей
            else app.Dispatcher.BeginInvoke(new Action(Glass.TrimMemory), DispatcherPriority.ApplicationIdle);
            app.Run();
        }
    }
}
