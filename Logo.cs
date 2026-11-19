// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using D2 = System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Media;

namespace MizuEngine
{
    /// <summary>Логотип — скруглённая буква M, как на иконке приложения.</summary>
    internal static class Logo
    {
        // Средняя линия буквы: левая ножка вверх, вниз к середине, вверх, правая ножка вниз
        // Широкая M (шире, чем высота), как на иконке
        static readonly Point[] Line = { new Point(4.5, 25.5), new Point(4.5, 4.5), new Point(18, 18), new Point(31.5, 4.5), new Point(31.5, 25.5) };
        const double StrokeWidth = 9;

        /// <summary>Контур буквы: его можно и заливать, и обводить.</summary>
        public static Geometry Create()
        {
            var figure = new PathFigure { StartPoint = Line[0], IsClosed = false };
            figure.Segments.Add(new PolyLineSegment(new[] { Line[1], Line[2], Line[3], Line[4] }, true));
            var pen = new Pen(Brushes.Black, StrokeWidth)
            {
                StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round
            };
            var g = new PathGeometry(new[] { figure }).GetWidenedPathGeometry(pen).GetOutlinedPathGeometry();
            g.Freeze();
            return g;
        }

        public static System.Drawing.Icon TrayIcon()
        {
            // Иконка приложения из Assets\MizuEngine.ico, размер под масштаб экрана (16, 20, 24, 32...)
            try
            {
                using var s = typeof(Logo).Assembly.GetManifestResourceStream("MizuEngine.ico");
                if (s != null) return new System.Drawing.Icon(s, System.Windows.Forms.SystemInformation.SmallIconSize);
            }
            catch { }

            // Запасной вариант: рисуем букву сами
            using var bmp = new System.Drawing.Bitmap(32, 32);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            using (var brush = new D2.LinearGradientBrush(new System.Drawing.PointF(0, 0), new System.Drawing.PointF(32, 32),
                       System.Drawing.Color.FromArgb(168, 216, 255), System.Drawing.Color.FromArgb(30, 79, 216)))
            using (var pen = new System.Drawing.Pen(brush, 7f) { StartCap = D2.LineCap.Round, EndCap = D2.LineCap.Round, LineJoin = D2.LineJoin.Round })
            {
                g.SmoothingMode = D2.SmoothingMode.AntiAlias;
                g.DrawLines(pen, new[]
                {
                    new System.Drawing.PointF(5, 24), new System.Drawing.PointF(5, 8), new System.Drawing.PointF(16, 19),
                    new System.Drawing.PointF(27, 8), new System.Drawing.PointF(27, 24)
                });
            }
            return System.Drawing.Icon.FromHandle(bmp.GetHicon());
        }
    }
}
