// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Threading;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>
    /// Снег и оверлеи живут в своём отдельном потоке: их анимации идут 60 кадров в секунду и не тормозят главное окно,
    /// а главное окно (переключение тем, разделов, загрузка обоев) не заставляет дёргаться снег.
    /// Док, анимации окон и Dynamic Island — в ещё одном потоке (Shell), чтобы тяжёлый кадр снега не задерживал док.
    /// Все команды из главного окна передаются сюда через Run().
    /// </summary>
    internal sealed class Effects
    {
        public SnowWindow Snow { get; private set; }
        /// <summary>Док живёт в потоке Shell; его методы сами переходят в свой поток, звать можно откуда угодно.</summary>
        public DockWindow Dock => Shell.Dock;
        public Overlays Overlays { get; private set; }
        Dispatcher _d;

        /// <summary>Читается из любого потока: на паузе ли эффекты.</summary>
        public bool Paused => Snow.Layer.Paused;

        public static Effects Start(Settings s)
        {
            var fx = new Effects();
            var ready = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                fx._d = Dispatcher.CurrentDispatcher;
                fx._d.UnhandledException += (o, e) => { Settings.Log(e.Exception); e.Handled = true; };   // ошибка не роняет программу, а пишется в error.log
                fx.Snow = new SnowWindow();
                s.ApplyTo(fx.Snow.Layer);
                fx.Overlays = new Overlays(s);
                ready.Set();

                fx.Snow.Show();
                try { fx.Overlays.Apply(); } catch (Exception e) { Settings.Log(e); }
                Dispatcher.Run();
            })
            { IsBackground = true, Name = "MizuEngine effects" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait();
            Shell.Start(s);   // док, анимации окон и Dynamic Island
            return fx;
        }

        public void Run(Action a) => _d.BeginInvoke(a);

        /// <summary>Выход: вернуть панель задач и остановить поток (ждём, пока успеет).</summary>
        public void Shutdown()
        {
            Shell.Shutdown();
            try { _d.Invoke(() => Overlays.Shutdown(), TimeSpan.FromSeconds(2)); } catch { }
            _d.InvokeShutdown();
        }
    }
}
