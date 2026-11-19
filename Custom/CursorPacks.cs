// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace MizuEngine
{
    /// <summary>
    /// Наборы курсоров. Курсоры рисуются самой программой (без чужих файлов) и ставятся системными для всей Windows.
    /// Пока программа работает — курсоры наши, при выходе возвращаются стандартные.
    /// </summary>
    internal static class CursorPacks
    {
        public static readonly string[] Packs = { "mac", "snow", "neon", "sakura", "galaxy", "gold" };
        /// <summary>Наборы только для Mizu Plus.</summary>
        public static readonly string[] PlusPacks = { "mizu", "ice", "rainbow", "lava", "mint", "midnight", "cherry", "aqua" };

        public static bool IsPlusPack(string pack) => Array.IndexOf(PlusPacks, pack) >= 0;

        const uint OCR_NORMAL = 32512, OCR_IBEAM = 32513, OCR_WAIT = 32514, OCR_HAND = 32649, OCR_APPSTARTING = 32650;
        static string Dir => System.IO.Path.Combine(Settings.Folder, "cursors");

        public static void Apply(Settings s)
        {
            try
            {
                Restore();
                if (string.IsNullOrEmpty(s.CursorPack)) return;
                if (IsPlusPack(s.CursorPack) && !Plus.IsActive) return;   // набор Plus без подписки — стандартные курсоры
                if (s.CursorPack == "custom") { ApplyCustom(s.CustomCursor, s.CursorSize); return; }
                int size = s.CursorSize;
                Set(Build(s.CursorPack, "arrow", size), OCR_NORMAL, size);
                Set(Build(s.CursorPack, "hand", size), OCR_HAND, size);
                Set(Build(s.CursorPack, "ibeam", size), OCR_IBEAM, size);
                string wait = Build(s.CursorPack, "wait", size);
                Set(wait, OCR_WAIT, size);
                Set(wait, OCR_APPSTARTING, size);
            }
            catch (Exception e) { Settings.Log(e); }
        }

        /// <summary>Вернуть стандартные курсоры Windows (из текущей схемы в настройках системы).</summary>
        public static void Restore()
        {
            try { SystemParametersInfoW(0x0057, 0, IntPtr.Zero, 0); } catch { }   // SPI_SETCURSORS
        }

        static void ApplyCustom(string file, int size)
        {
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return;
            string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
            if (ext == ".cur" || ext == ".ani")
            {
                IntPtr h = LoadCursorFromFileW(file);
                if (h != IntPtr.Zero) SetSystemCursor(h, OCR_NORMAL);
                return;
            }
            // Картинка: уменьшаем до размера курсора, «кончик» — левый верхний угол
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri(file);
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            var host = new Grid { Width = size, Height = size };
            host.Children.Add(new Image { Source = img, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top });
            Directory.CreateDirectory(Dir);
            string path = System.IO.Path.Combine(Dir, $"custom-{size}.cur");
            WriteCur(path, Render(host, size), 0, 0);
            Set(path, OCR_NORMAL, size);
        }

        static void Set(string path, uint id, int size)
        {
            if (path == null) return;
            IntPtr h = LoadImageW(IntPtr.Zero, path, 2, size, size, 0x10);   // IMAGE_CURSOR, LR_LOADFROMFILE
            if (h != IntPtr.Zero) SetSystemCursor(h, id);                    // Windows забирает курсор себе, освобождать не надо
        }

        // ================== рисование ==================

        sealed class Look
        {
            public Brush Fill, Stroke;
            public Color? Glow;
        }

        static Look Style(string pack)
        {
            Brush G(string a, string b) => new LinearGradientBrush((Color)ColorConverter.ConvertFromString(a), (Color)ColorConverter.ConvertFromString(b), 90);
            Brush S(string a) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(a));
            Color C(string a) => (Color)ColorConverter.ConvertFromString(a);
            return pack switch
            {
                "snow" => new Look { Fill = S("#FFFFFFFF"), Stroke = S("#FF1E1E1E") },
                "neon" => new Look { Fill = G("#FF8FD3FF", "#FF1D5BF0"), Stroke = S("#FFFFFFFF"), Glow = C("#FF3B8CFF") },
                "sakura" => new Look { Fill = G("#FFFFC2D8", "#FFFF5C8A"), Stroke = S("#FFFFFFFF"), Glow = C("#FFFF7AA8") },
                "galaxy" => new Look { Fill = G("#FFB894FF", "#FF3A1F9E"), Stroke = S("#FFF0E8FF"), Glow = C("#FF8A5CFF") },
                "gold" => new Look { Fill = G("#FFFFE38F", "#FFDB9A12"), Stroke = S("#FF3A2A00") },
                // ----- Mizu Plus -----
                "mizu" => new Look { Fill = G("#FFC4B5FD", "#FF6D28D9"), Stroke = S("#FFFFFFFF"), Glow = C("#FF8B5CF6") },
                "ice" => new Look { Fill = G("#FFF0FBFF", "#FF8FD8F5"), Stroke = S("#FF1E5A78"), Glow = C("#FF9BE6FF") },
                "rainbow" => new Look { Fill = Rainbow(), Stroke = S("#FFFFFFFF"), Glow = C("#FFFF7AD9") },
                "lava" => new Look { Fill = G("#FFFFD24D", "#FFE8360F"), Stroke = S("#FF2A0800"), Glow = C("#FFFF5A1F") },
                "mint" => new Look { Fill = G("#FFCFFFEA", "#FF2BC48A"), Stroke = S("#FF0B3B2A") },
                "midnight" => new Look { Fill = G("#FF3A4466", "#FF0A0E1C"), Stroke = S("#FF9CC2FF"), Glow = C("#FF4A7BFF") },
                "cherry" => new Look { Fill = G("#FFFF8FA8", "#FFB0103A"), Stroke = S("#FFFFFFFF"), Glow = C("#FFFF3D6E") },
                "aqua" => new Look { Fill = G("#FF7DF9FF", "#FF0D8BD9"), Stroke = S("#FFFFFFFF"), Glow = C("#FF22D3EE") },
                _ => new Look { Fill = S("#FF101010"), Stroke = S("#FFFFFFFF") }   // mac
            };
        }

        static Brush Rainbow()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            string[] c = { "#FFFF5C8A", "#FFFFC93D", "#FF3DDC84", "#FF22D3EE", "#FF8B5CF6" };
            for (int i = 0; i < c.Length; i++) b.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(c[i]), i / (double)(c.Length - 1)));
            return b;
        }

        /// <summary>Форма курсора в сетке 32×32 и его «кончик» (куда указывает).</summary>
        static (Geometry shape, Point hot) Shape(string role)
        {
            switch (role)
            {
                case "hand":
                {
                    Geometry g = new RectangleGeometry(new Rect(10, 2, 5, 16), 2.5, 2.5);
                    g = Geometry.Combine(g, new RectangleGeometry(new Rect(7.5, 12.5, 17, 15.5), 5.5, 5.5), GeometryCombineMode.Union, null);
                    g = Geometry.Combine(g, new RectangleGeometry(new Rect(14.6, 9.6, 4.6, 9), 2.3, 2.3), GeometryCombineMode.Union, null);
                    g = Geometry.Combine(g, new RectangleGeometry(new Rect(19.2, 11, 4.6, 8.5), 2.3, 2.3), GeometryCombineMode.Union, null);
                    g = Geometry.Combine(g, new RectangleGeometry(new Rect(4.6, 15, 6.4, 5), 2.5, 2.5), GeometryCombineMode.Union, null);
                    return (g, new Point(12.5, 2));
                }
                case "ibeam":
                    return (Geometry.Parse("M 12,4 L 20,4 L 20,6.2 L 17.1,6.2 L 17.1,25.8 L 20,25.8 L 20,28 L 12,28 L 12,25.8 L 14.9,25.8 L 14.9,6.2 L 12,6.2 Z"), new Point(16, 16));
                case "wait":
                {
                    var ring = Geometry.Combine(new EllipseGeometry(new Point(16, 16), 11, 11), new EllipseGeometry(new Point(16, 16), 6.5, 6.5), GeometryCombineMode.Exclude, null);
                    return (ring, new Point(16, 16));
                }
                default:
                    return (Geometry.Parse("M 4,2 L 4,24.5 L 9.4,19.3 L 13,27.6 L 16.8,26 L 13.3,17.9 L 20.6,17.9 Z"), new Point(4, 2));
            }
        }

        static FrameworkElement Draw(string pack, string role, int size)
        {
            var look = Style(pack);
            var (shape, _) = Shape(role);
            double k = size / 32.0;
            var g = shape.Clone();
            g.Transform = new ScaleTransform(k, k);
            var path = new System.Windows.Shapes.Path
            {
                Data = g, Fill = look.Fill, Stroke = look.Stroke, StrokeThickness = 1.5 * k, StrokeLineJoin = PenLineJoin.Round,
                Effect = new DropShadowEffect
                {
                    ShadowDepth = look.Glow.HasValue ? 0 : 1.2 * k, BlurRadius = (look.Glow.HasValue ? 6 : 3) * k,
                    Color = look.Glow ?? Colors.Black, Opacity = look.Glow.HasValue ? 0.9 : 0.45, Direction = 300
                }
            };
            var canvas = new Canvas { Width = size, Height = size };
            canvas.Children.Add(path);
            return canvas;
        }

        static BitmapSource Render(FrameworkElement e, int size)
        {
            e.Measure(new Size(size, size));
            e.Arrange(new Rect(0, 0, size, size));
            e.UpdateLayout();
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(e);
            bmp.Freeze();
            return bmp;
        }

        /// <summary>Готовит файл курсора (рисует один раз и кладёт в %AppData%\MizuEngine\cursors).</summary>
        static string Build(string pack, string role, int size)
        {
            Directory.CreateDirectory(Dir);
            string path = System.IO.Path.Combine(Dir, $"{pack}-{role}-{size}-v1.cur");
            if (File.Exists(path)) return path;
            var (_, hot) = Shape(role);
            double k = size / 32.0;
            WriteCur(path, Render(Draw(pack, role, size), size), (int)Math.Round(hot.X * k), (int)Math.Round(hot.Y * k));
            return path;
        }

        /// <summary>Картинка-превью для плиток на странице «Кастомизация».</summary>
        public static ImageSource Preview(string pack) => Render(Draw(pack, "arrow", 48), 48);

        /// <summary>Файл .cur: заголовок, 32-битная картинка снизу вверх и маска прозрачности.</summary>
        static void WriteCur(string path, BitmapSource bmp, int hotX, int hotY)
        {
            int w = bmp.PixelWidth, h = bmp.PixelHeight;
            var px = new byte[w * h * 4];
            bmp.CopyPixels(px, w * 4, 0);
            for (int i = 0; i < px.Length; i += 4)   // в файле курсора цвета хранятся без умножения на прозрачность
            {
                int a = px[i + 3];
                if (a == 0 || a == 255) continue;
                px[i] = (byte)Math.Min(255, px[i] * 255 / a);
                px[i + 1] = (byte)Math.Min(255, px[i + 1] * 255 / a);
                px[i + 2] = (byte)Math.Min(255, px[i + 2] * 255 / a);
            }
            int maskStride = (w + 31) / 32 * 4;
            int imageBytes = 40 + w * h * 4 + maskStride * h;

            using var bw = new BinaryWriter(File.Create(path));
            bw.Write((short)0); bw.Write((short)2); bw.Write((short)1);                     // ICONDIR: курсор, 1 картинка
            bw.Write((byte)(w >= 256 ? 0 : w)); bw.Write((byte)(h >= 256 ? 0 : h)); bw.Write((byte)0); bw.Write((byte)0);
            bw.Write((short)hotX); bw.Write((short)hotY); bw.Write(imageBytes); bw.Write(22);
            bw.Write(40); bw.Write(w); bw.Write(h * 2); bw.Write((short)1); bw.Write((short)32);   // BITMAPINFOHEADER
            bw.Write(0); bw.Write(w * h * 4 + maskStride * h); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
            for (int y = h - 1; y >= 0; y--) bw.Write(px, y * w * 4, w * 4);
            var row = new byte[maskStride];
            for (int y = h - 1; y >= 0; y--)
            {
                Array.Clear(row, 0, row.Length);
                for (int x = 0; x < w; x++)
                    if (px[(y * w + x) * 4 + 3] == 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
                bw.Write(row);
            }
        }

        [DllImport("user32.dll")] static extern bool SetSystemCursor(IntPtr cursor, uint id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadImageW(IntPtr inst, string name, uint type, int cx, int cy, uint load);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadCursorFromFileW(string file);
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] static extern bool SystemParametersInfoW(uint action, uint param, IntPtr pv, uint winIni);
    }
}
