// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MizuEngine
{
    /// <summary>Помощники для плавных анимаций в стиле macOS.</summary>
    internal static class Anim
    {
        // Быстрый старт и очень мягкая остановка — так двигается интерфейс macOS
        public static readonly IEasingFunction Out = Frozen(new QuinticEase { EasingMode = EasingMode.EaseOut });
        public static readonly IEasingFunction Spring = Frozen(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 });

        static IEasingFunction Frozen(EasingFunctionBase e) { e.Freeze(); return e; }

        public static void To(IAnimatable target, DependencyProperty prop, double to, double ms,
                              double? from = null, double delayMs = 0, IEasingFunction ease = null, Action done = null)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = ease ?? Out,
                BeginTime = TimeSpan.FromMilliseconds(delayMs)
            };
            if (from.HasValue) a.From = from.Value;
            if (done != null) a.Completed += (s, e) => done();
            target.BeginAnimation(prop, a);
        }

        /// <summary>Мгновенно ставит значение, сбрасывая анимацию свойства.</summary>
        public static void Set(IAnimatable target, DependencyProperty prop, double value)
        {
            target.BeginAnimation(prop, null);
            ((DependencyObject)target).SetValue(prop, value);
        }
    }

    /// <summary>Плавная прокрутка колёсиком мыши вместо резких скачков.</summary>
    internal static class SmoothScroll
    {
        public static void Attach(ScrollViewer sv)
        {
            double target = 0;
            bool running = false;
            TimeSpan last = TimeSpan.Zero;

            void Step(object s, EventArgs e)
            {
                var now = ((RenderingEventArgs)e).RenderingTime;
                double dt = last == TimeSpan.Zero ? 1.0 / 60 : Math.Min(0.05, (now - last).TotalSeconds);
                last = now;
                double cur = sv.VerticalOffset;
                double next = cur + (target - cur) * (1 - Math.Exp(-dt * 14));
                if (Math.Abs(target - next) < 0.5)
                {
                    next = target;
                    running = false;
                    CompositionTarget.Rendering -= Step;
                }
                sv.ScrollToVerticalOffset(next);
            }

            sv.PreviewMouseWheel += (s, e) =>
            {
                if (sv.ScrollableHeight <= 0) return;
                if (!running) target = sv.VerticalOffset;
                target = Math.Max(0, Math.Min(sv.ScrollableHeight, target - e.Delta * 0.9));
                e.Handled = true;
                if (!running)
                {
                    running = true;
                    last = TimeSpan.Zero;
                    CompositionTarget.Rendering += Step;
                }
            };
        }
    }
}
