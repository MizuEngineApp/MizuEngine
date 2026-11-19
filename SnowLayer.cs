// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>
    /// Погода поверх экрана: снег, дождь, сакура, светлячки, метель, листопад, лепестки роз, конфетти, пузыри,
    /// бабочки, пыльца, искры костра, звёзды, северное сияние, «цифровой дождь» и гирлянда.
    /// Сугробы на окнах, разгребание курсором и искры за курсором. Часть эффектов реагирует на курсор (CursorReact).
    /// Все координаты — в физических пикселях экрана, при отрисовке переводим их в единицы WPF.
    /// Когда показывать нечего, кадры не считаются вообще — программа не тратит процессор.
    /// </summary>
    internal sealed class SnowLayer : FrameworkElement
    {
        // ---------- настройки ----------
        public int TargetFlakes = 260;
        public bool SnowOnWindows = true;
        public bool GroundDrift = true;
        public bool CursorScrape = true;
        public bool CursorReact = true;      // листья разлетаются, пузыри лопаются, бабочки улетают, пыльца кружится
        public IntPtr OwnHwnd;

        string _weather = "snow";
        public string Weather
        {
            get => _weather;
            set
            {
                if (_weather == value) return;
                _weather = value;
                _flakes.Clear();
                _wire = null;
                if (!IsSnow) ClearDrifts();
            }
        }

        ImageSource _sprite;
        string _spritePath;
        /// <summary>Своя картинка-частица (PNG). Загружается один раз при смене файла.</summary>
        public string CustomParticlePath
        {
            set
            {
                if (value == _spritePath) return;
                _spritePath = value;
                _sprite = null;
                try
                {
                    if (!string.IsNullOrEmpty(value) && System.IO.File.Exists(value))
                    {
                        var b = new System.Windows.Media.Imaging.BitmapImage();
                        b.BeginInit();
                        b.UriSource = new Uri(value);
                        b.DecodePixelWidth = 96;
                        b.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        b.EndInit();
                        b.Freeze();
                        _sprite = b;
                    }
                }
                catch (Exception e) { Settings.Log(e); }
            }
        }

        bool _paused, _trail;
        public bool Paused { get => _paused; set { _paused = value; UpdateRunning(); } }
        public bool CursorTrail { get => _trail; set { if (value && !_trail) Native.GetCursorPos(out _lastCursor); _trail = value; UpdateRunning(); } }

        bool IsSnow => _weather == "snow" || _weather == "blizzard";

        // ---------- константы ----------
        const int Cell = 3;               // ширина «столбика» сугроба в пикселях
        const float WindowMaxH = 14f;     // макс. высота сугроба на окне
        const float GroundMaxH = 22f;     // макс. высота сугроба над панелью задач
        const int MaxFlakes = 3000;

        sealed class Flake
        {
            public double X, Y, VX, VY, R, Phase, Rot, VRot, Life;
            public double PX, PY;         // толчок от курсора (затухает)
            public bool Loose;            // кусок снега/капля, которые отскакивают с гравитацией; у звёзд — падающая звезда
            public int Tint;
            public int[] Chars;           // знаки колонки «цифрового дождя»
        }

        sealed class Drift
        {
            public IntPtr Hwnd;
            public Native.RECT Rect;
            public int Z;                 // место в z-порядке (0 — самое верхнее окно)
            public float[] H;             // высота снега по столбикам
            public float[] Cap;           // предельная высота столбика (волнистая)
            public bool[] Vis;            // видно ли этот столбик (не закрыт другим окном)
            public float MaxH;
        }

        readonly List<Flake> _flakes = new List<Flake>();
        readonly List<Flake> _sparks = new List<Flake>();
        readonly Dictionary<IntPtr, Drift> _drifts = new Dictionary<IntPtr, Drift>();
        readonly List<Drift> _order = new List<Drift>();
        readonly List<Native.RECT> _occluders = new List<Native.RECT>();
        Drift _ground;

        readonly Random _rnd = new Random();
        readonly WindowTracker _tracker = new WindowTracker();
        readonly DispatcherTimer _slow;

        int _screenW, _screenH;
        double _scale = 1;
        double _time;
        bool _fullscreenHidden, _running;
        TimeSpan _lastRender;
        Native.POINT _lastCursor;
        bool _hasCur, _curKnown;          // курсор для реагирующих эффектов: где и с какой скоростью движется
        double _cx, _cy, _cvx, _cvy;
        Geometry _wire;                   // провод гирлянды (строится один раз)
        int _frameNo;
        FormattedText[] _matrixText;      // готовые знаки «цифрового дождя»: 9 яркостей каждого
        double _matrixDip;

        // Кисти создаются один раз — так рисование не нагружает сборщик мусора
        static readonly Brush[] SnowBrushes = { B(0x90, 255, 255, 255), B(0xC8, 255, 255, 255), B(0xF0, 255, 255, 255) };
        static readonly Brush DriftBrush = B(0xF7, 0xF4, 0xF8, 0xFF);
        static readonly Pen DriftPen = P(B(0x70, 0xB4, 0xC6, 0xDC), 1);
        static readonly Pen[] RainPens = { P(B(0x55, 0xC8, 0xDC, 0xFF), 1), P(B(0x80, 0xD8, 0xE6, 0xFF), 1.3) };
        static readonly Brush[] Petals = { B(0xF0, 0xFF, 0xC4, 0xD8), B(0xF0, 0xFF, 0xB0, 0xC8), B(0xF0, 0xFF, 0xE4, 0xEE) };
        static readonly Brush FlyCore = B(0xFF, 0xFF, 0xF8, 0xB8);
        static readonly Brush[] FlyGlow = MakeLevels(0xFF, 0xF0, 0x80, 0x50);
        static readonly Brush[][] SparkLevels =
        {
            MakeLevels(0x9C, 0xDC, 0xFF, 0xFF), MakeLevels(0xFF, 0xFF, 0xFF, 0xFF), MakeLevels(0xFF, 0xD8, 0x7A, 0xFF)
        };

        // ---------- новые эффекты ----------
        static readonly Pen[] BlizzardPens = { P(B(0x80, 255, 255, 255), 1.1), P(B(0xB0, 255, 255, 255), 1.6), P(B(0xE0, 255, 255, 255), 2.2) };
        static readonly Brush[] LeafBrushes = { B(0xF0, 0xE8, 0xA3, 0x3D), B(0xF0, 0xD9, 0x57, 0x2B), B(0xF0, 0xF2, 0xC9, 0x4C), B(0xF0, 0xB5, 0x45, 0x2B), B(0xF0, 0x9C, 0x6B, 0x30) };
        static readonly Pen LeafVein = P(B(0x70, 0x5A, 0x30, 0x10), 0.08);
        static readonly Geometry LeafGeo = Frozen("M 0,-1 C 0.6,-0.55 0.55,0.5 0,1 C -0.55,0.5 -0.6,-0.55 0,-1 Z M 0,-0.85 L 0,1.15");
        static readonly Brush[] RoseBrushes = { B(0xF0, 0xC2, 0x18, 0x3B), B(0xF0, 0xE0, 0x30, 0x50), B(0xF0, 0x9E, 0x0F, 0x2E), B(0xE8, 0xF0, 0x6A, 0x84) };
        static readonly Geometry PetalGeo = Frozen("M 0,-0.75 C 0.35,-1.15 1.05,-0.7 0.75,0.05 C 0.55,0.55 0.2,0.85 0,1 C -0.2,0.85 -0.55,0.55 -0.75,0.05 C -1.05,-0.7 -0.35,-1.15 0,-0.75 Z");
        static readonly Brush[] ConfettiBrushes =
        {
            B(0xF0, 0xFF, 0x5C, 0x8A), B(0xF0, 0xFF, 0xC9, 0x3D), B(0xF0, 0x3D, 0xDC, 0x84), B(0xF0, 0x4A, 0x97, 0xFF),
            B(0xF0, 0xB3, 0x88, 0xFF), B(0xF0, 0x22, 0xD3, 0xEE), B(0xF0, 0xFF, 0x8A, 0x3D), B(0xF0, 0xFF, 0xFF, 0xFF)
        };
        static readonly Rect UnitRect = new Rect(-1, -1, 2, 2);
        static readonly Brush BubbleFill = B(0x12, 0xFF, 0xFF, 0xFF), BubbleShine = B(0xC8, 0xFF, 0xFF, 0xFF);
        static readonly Pen[] BubblePens = { P(B(0x90, 0xBF, 0xEF, 0xFF), 1.3), P(B(0x90, 0xFF, 0xC6, 0xEC), 1.3), P(B(0xA0, 0xFF, 0xFF, 0xFF), 1.2) };
        static readonly (Brush upper, Brush lower)[] WingBrushes =
        {
            (B(0xF0, 0x4F, 0xB3, 0xFF), B(0xF0, 0x1E, 0x5B, 0xD8)), (B(0xF0, 0xFF, 0xA6, 0x2B), B(0xF0, 0xE2, 0x70, 0x1A)),
            (B(0xF0, 0xFF, 0x9E, 0xC7), B(0xF0, 0xE0, 0x5A, 0x9A)), (B(0xF0, 0xFF, 0xF1, 0x9A), B(0xF0, 0xF2, 0xD0, 0x4C)),
            (B(0xF0, 0xC4, 0xA1, 0xFF), B(0xF0, 0x7C, 0x4D, 0xFF))
        };
        static readonly Brush ButterflyBody = B(0xFF, 0x2A, 0x22, 0x1E);
        static readonly Brush[][] PollenGlow = { MakeLevels(0xFF, 0xE7, 0x9A, 0x60), MakeLevels(0x9C, 0xE8, 0xFF, 0x60) };
        static readonly Brush PollenCore = B(0xFF, 0xFF, 0xFA, 0xE0);
        static readonly Brush[] EmberGlow = MakeLevels(0xFF, 0x7A, 0x1E, 0x70), EmberCore = MakeLevels(0xFF, 0xD2, 0x6A, 0xFF);
        static readonly Brush[] StarLevels = MakeLevels(0xFF, 0xFF, 0xFF, 0xFF);
        static readonly Pen[] StarPens = MakePens(StarLevels, 0.8), ShootPens = MakePens(StarLevels, 2);
        static readonly Brush[] AuroraFill = { AuroraBrush(0x3D, 0xFF, 0xB0), AuroraBrush(0x36, 0xD1, 0xDC), AuroraBrush(0x8E, 0x6B, 0xFF) };
        const double MatrixSize = 18;
        const string MatrixGlyphs = "ｱｲｳｴｵｶｷｸｹｺｻｼｽｾｿﾀﾁﾂﾃﾄﾅﾆﾇﾈﾉﾊﾋﾌﾍﾎﾏﾐﾑﾒﾓﾔﾕﾖﾗﾘﾙﾚﾛﾜﾝ0123456789Z:.=*+-<>";
        static readonly Brush[] MatrixLevels = MakeLevels(0x3D, 0xFF, 0x7A, 0xE6);
        static readonly Brush MatrixHead = B(0xFF, 0xDC, 0xFF, 0xE8);
        static readonly Color[] GarlandColors =
        {
            Color.FromRgb(0xFF, 0x4D, 0x4D), Color.FromRgb(0xFF, 0xD2, 0x4D), Color.FromRgb(0x4D, 0xFF, 0x88),
            Color.FromRgb(0x4D, 0xA6, 0xFF), Color.FromRgb(0xFF, 0x66, 0xCC)
        };
        static readonly Brush[][] GarlandGlow = Array.ConvertAll(GarlandColors, c => MakeLevels(c.R, c.G, c.B, 0x70));
        static readonly Brush[] GarlandBright = Array.ConvertAll(GarlandColors, c => B(0xFF, c.R, c.G, c.B));
        static readonly Brush[] GarlandDim = Array.ConvertAll(GarlandColors, c => B(0xB0, (byte)(c.R * 0.6), (byte)(c.G * 0.6), (byte)(c.B * 0.6)));
        static readonly Brush BulbCap = B(0xFF, 0x50, 0x55, 0x50);
        static readonly Pen WirePen = P(B(0xD0, 0x2A, 0x33, 0x2A), 1.6);

        // ---------- эффекты Mizu Plus ----------
        static readonly Geometry HeartGeo = Frozen("M 0,0.95 C -0.35,0.65 -1,0.2 -1,-0.35 C -1,-0.8 -0.62,-1 -0.35,-1 C -0.12,-1 0,-0.85 0,-0.68 C 0,-0.85 0.12,-1 0.35,-1 C 0.62,-1 1,-0.8 1,-0.35 C 1,0.2 0.35,0.65 0,0.95 Z");
        static readonly Brush[] HeartBrushes = { B(0xF0, 0xFF, 0x4D, 0x7A), B(0xF0, 0xFF, 0x7A, 0xA8), B(0xF0, 0xE0, 0x24, 0x5B), B(0xE8, 0xFF, 0xB3, 0xCC), B(0xF0, 0xB0, 0x7C, 0xFF) };
        static readonly Geometry SparkleGeo = Frozen("M 0,-1 L 0.2,-0.2 L 1,0 L 0.2,0.2 L 0,1 L -0.2,0.2 L -1,0 L -0.2,-0.2 Z");
        static readonly Brush[][] GlitterLevels = { MakeLevels(0xFF, 0xE2, 0x7A, 0xFF), MakeLevels(0xF2, 0xF5, 0xFA, 0xFF), MakeLevels(0xFF, 0x9E, 0xCF, 0xFF), MakeLevels(0xC4, 0xB5, 0xFD, 0xFF) };
        static readonly Geometry CrystalGeo = BuildCrystal();
        static readonly Pen[] CrystalPens = { P(B(0xC8, 0xFF, 0xFF, 0xFF), 0.11), P(B(0xE0, 0xDD, 0xF2, 0xFF), 0.13), P(B(0xF0, 0xFF, 0xFF, 0xFF), 0.09) };
        static readonly Brush[] LanternGlow = MakeLevels(0xFF, 0x9A, 0x2E, 0x58);
        static readonly Brush LanternBody = LanternBrush();
        static readonly Brush LanternCore = B(0xFF, 0xFF, 0xEC, 0xB0);
        static readonly Color[] MeteorColors = { Color.FromRgb(0x9B, 0xE6, 0xFF), Color.FromRgb(0xC4, 0xB5, 0xFD), Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xFF, 0xC9, 0x7A) };
        static readonly Pen[][] MeteorPens = Array.ConvertAll(MeteorColors, c => MakePens(MakeLevels(c.R, c.G, c.B, 0xFF), 2));
        static readonly Brush[][] MeteorHeads = Array.ConvertAll(MeteorColors, c => MakeLevels(c.R, c.G, c.B, 0xFF));
        static readonly Geometry FluffGeo = BuildFluff();
        static readonly Pen FluffPen = P(B(0xB8, 0xFF, 0xFF, 0xFF), 0.07);
        static readonly Brush FluffCore = B(0xE6, 0xF5, 0xEC, 0xD8);

        /// <summary>Снежинка-кристалл: 6 лучей с веточками (фигура размером 2×2 вокруг нуля).</summary>
        static Geometry BuildCrystal()
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                for (int k = 0; k < 6; k++)
                {
                    double a = k * Math.PI / 3, ca = Math.Cos(a), sa = Math.Sin(a);
                    Point Turn(double x, double y) => new Point(x * ca - y * sa, x * sa + y * ca);
                    c.BeginFigure(new Point(0, 0), false, false);
                    c.LineTo(Turn(1, 0), true, true);
                    foreach (double r in new[] { 0.42, 0.7 })
                    {
                        double l = r < 0.6 ? 0.3 : 0.2;
                        c.BeginFigure(Turn(r, 0), false, false);
                        c.LineTo(Turn(r + l * 0.7, l * 0.7), true, true);
                        c.BeginFigure(Turn(r, 0), false, false);
                        c.LineTo(Turn(r + l * 0.7, -l * 0.7), true, true);
                    }
                }
            }
            g.Freeze();
            return g;
        }

        /// <summary>Пушинка одуванчика: «зонтик» тонких лучей и ножка вниз.</summary>
        static Geometry BuildFluff()
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                for (int k = 0; k < 12; k++)
                {
                    double a = -Math.PI + k * Math.PI / 11;
                    c.BeginFigure(new Point(0, 0.2), false, false);
                    c.LineTo(new Point(Math.Cos(a), Math.Sin(a) * 0.8 - 0.2), true, true);
                }
                c.BeginFigure(new Point(0, 0.2), false, false);
                c.LineTo(new Point(0, 1.3), true, true);
            }
            g.Freeze();
            return g;
        }

        static Brush LanternBrush()
        {
            var br = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            br.GradientStops.Add(new GradientStop(Color.FromArgb(0xF0, 0xFF, 0xC2, 0x5C), 0));
            br.GradientStops.Add(new GradientStop(Color.FromArgb(0xF0, 0xFF, 0x8A, 0x2A), 0.6));
            br.GradientStops.Add(new GradientStop(Color.FromArgb(0xF0, 0xE0, 0x4A, 0x14), 1));
            br.Freeze();
            return br;
        }

        static Geometry Frozen(string data) { var g = Geometry.Parse(data); g.Freeze(); return g; }

        static Pen[] MakePens(Brush[] levels, double w) => Array.ConvertAll(levels, b => P(b, w));

        /// <summary>Штора сияния: сверху прозрачная, книзу ярче, у самого края мягко гаснет.</summary>
        static Brush AuroraBrush(byte r, byte g, byte b)
        {
            var br = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            br.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, r, g, b), 0));
            br.GradientStops.Add(new GradientStop(Color.FromArgb(0x26, r, g, b), 0.5));
            br.GradientStops.Add(new GradientStop(Color.FromArgb(0x66, r, g, b), 0.9));
            br.GradientStops.Add(new GradientStop(Color.FromArgb(0x10, r, g, b), 1));
            br.Freeze();
            return br;
        }

        static Brush B(byte a, byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromArgb(a, r, g, b)); x.Freeze(); return x; }
        static Pen P(Brush b, double w) { var p = new Pen(b, w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; p.Freeze(); return p; }

        /// <summary>8 уровней прозрачности одного цвета — для плавного угасания без PushOpacity на каждую частицу.</summary>
        static Brush[] MakeLevels(byte r, byte g, byte b, byte maxA)
        {
            var arr = new Brush[8];
            for (int i = 0; i < 8; i++) arr[i] = B((byte)(maxA * (i + 1) / 8), r, g, b);
            return arr;
        }

        public SnowLayer()
        {
            IsHitTestVisible = false;
            _slow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            _slow.Tick += (s, e) => SlowTick();
            _slow.Start();
        }

        // ================== публичные команды ==================

        public void SetScreen(int width, int height, double scale)
        {
            _screenW = width;
            _screenH = height;
            _scale = scale <= 0 ? 1 : scale;
            _drifts.Clear();
            _order.Clear();
            _occluders.Clear();
            _flakes.Clear();
            _wire = null;
            BuildGround();
            UpdateRunning();
        }

        public void ClearDrifts()
        {
            foreach (var d in _drifts.Values) Array.Clear(d.H, 0, d.H.Length);
            if (_ground != null) Array.Clear(_ground.H, 0, _ground.H.Length);
        }

        // ================== запуск / остановка кадров ==================

        void UpdateRunning()
        {
            bool run = _screenW > 0 && !_fullscreenHidden && (!_paused || _trail || _sparks.Count > 0);
            Visibility = _fullscreenHidden ? Visibility.Hidden : Visibility.Visible;
            if (run == _running) return;
            _running = run;
            _lastRender = TimeSpan.Zero;
            if (run) CompositionTarget.Rendering += OnFrame;
            else { CompositionTarget.Rendering -= OnFrame; InvalidateVisual(); }
        }

        /// <summary>Редкие проверки (12 раз в секунду): полноэкранные приложения и положение окон.</summary>
        void SlowTick()
        {
            if (_screenW == 0) return;
            bool fs = _tracker.ForegroundIsFullscreen(OwnHwnd, _screenW, _screenH);
            if (fs != _fullscreenHidden) { _fullscreenHidden = fs; UpdateRunning(); }
            if (_running && !_paused) RefreshWindows();
        }

        // ================== главный цикл ==================

        void OnFrame(object sender, EventArgs e)
        {
            var now = ((RenderingEventArgs)e).RenderingTime;
            if (now == _lastRender) return;
            double dt = _lastRender == TimeSpan.Zero ? 1.0 / 60 : (now - _lastRender).TotalSeconds;
            _lastRender = now;
            dt = Math.Min(dt, 0.05);
            _time += dt;

            if (!_paused)
            {
                Step(dt);
                if (CursorScrape && IsSnow) Scrape();
            }
            else if (_flakes.Count > 0) _flakes.Clear();

            StepSparks(dt);
            // Сияние и «цифровой дождь» рисуют много — им хватает 30 кадров в секунду
            bool heavy = !_paused && (_weather == "aurora" || _weather == "matrix");
            if (!heavy || (++_frameNo & 1) == 0) InvalidateVisual();
            if (_paused && !_trail && _sparks.Count == 0) UpdateRunning();
        }

        // ================== окна и сугробы ==================

        void BuildGround()
        {
            // Если снизу панель задач или док — сугроб лежит на их верхней кромке, иначе на низу экрана
            int bottom = (int)Math.Round(SystemParameters.WorkArea.Bottom * _scale);
            int y = bottom < _screenH - 4 ? bottom : _screenH;
            _ground = NewDrift(IntPtr.Zero, new Native.RECT { Left = 0, Top = y, Right = _screenW, Bottom = _screenH }, GroundMaxH);
            for (int i = 0; i < _ground.Vis.Length; i++) _ground.Vis[i] = true;
        }

        Drift NewDrift(IntPtr hwnd, Native.RECT rect, float maxH)
        {
            int cols = Math.Max(1, rect.Width / Cell);
            var d = new Drift
            {
                Hwnd = hwnd, Rect = rect, MaxH = maxH,
                H = new float[cols], Cap = new float[cols], Vis = new bool[cols]
            };
            // Волнистый предел высоты, чтобы сугроб был «горками», а не ровной полосой
            double p1 = _rnd.NextDouble() * 10, p2 = _rnd.NextDouble() * 10, p3 = _rnd.NextDouble() * 10;
            for (int c = 0; c < cols; c++)
            {
                double x = c * Cell;
                double n = 0.5 * Math.Sin(x / 37.0 + p1) + 0.3 * Math.Sin(x / 13.0 + p2) + 0.2 * Math.Sin(x / 71.0 + p3);
                double edge = Math.Min(c, cols - 1 - c) * Cell;
                double taper = hwnd == IntPtr.Zero ? 1 : Math.Min(1.0, (edge + 2) / 16.0);
                d.Cap[c] = (float)(maxH * (0.72 + 0.28 * n) * taper);
            }
            return d;
        }

        void RefreshWindows()
        {
            bool track = SnowOnWindows && (IsSnow || _weather == "rain");   // на окна ложится снег, а дождь о них разбивается
            var wins = track ? _tracker.GetWindows(OwnHwnd, _screenW, _screenH) : new List<WindowTracker.Win>();

            var alive = new HashSet<IntPtr>();
            _order.Clear();
            _occluders.Clear();

            for (int z = 0; z < wins.Count; z++)
            {
                var w = wins[z];
                _occluders.Add(w.Rect);
                if (!w.Snow) continue;
                alive.Add(w.Hwnd);

                if (_drifts.TryGetValue(w.Hwnd, out var d))
                {
                    bool moved = d.Rect.Left != w.Rect.Left || d.Rect.Top != w.Rect.Top || d.Rect.Right != w.Rect.Right;
                    if (moved)
                    {
                        Shed(d);                        // окно сдвинули — снег осыпается
                        var fresh = NewDrift(w.Hwnd, w.Rect, WindowMaxH);
                        _drifts[w.Hwnd] = fresh;
                        d = fresh;
                    }
                }
                else
                {
                    d = NewDrift(w.Hwnd, w.Rect, WindowMaxH);
                    _drifts[w.Hwnd] = d;
                }
                d.Z = z;
                _order.Add(d);
            }

            // Окна, которые закрыли или свернули — снег с них падает вниз
            List<IntPtr> dead = null;
            foreach (var kv in _drifts)
                if (!alive.Contains(kv.Key)) (dead ??= new List<IntPtr>()).Add(kv.Key);
            if (dead != null)
                foreach (var h in dead) { Shed(_drifts[h]); _drifts.Remove(h); }

            // Какие части верхней кромки окна не закрыты окнами повыше
            foreach (var d in _order)
            {
                int y = d.Rect.Top - 3;
                for (int c = 0; c < d.Vis.Length; c++)
                {
                    int x = d.Rect.Left + c * Cell + Cell / 2;
                    bool vis = x >= 0 && x < _screenW && y > 0;
                    for (int j = 0; j < d.Z && vis; j++)
                    {
                        var r = _occluders[j];
                        if (x >= r.Left && x < r.Right && y >= r.Top - 2 && y < r.Bottom) vis = false;
                    }
                    d.Vis[c] = vis;
                }
            }
        }

        /// <summary>Превращает сугроб в падающие комочки снега.</summary>
        void Shed(Drift d)
        {
            for (int c = 0; c < d.H.Length; c++)
            {
                float h = d.H[c];
                if (h < 0.6f || !d.Vis[c]) continue;
                int n = Math.Min(3, (int)(h / 4) + 1);
                for (int k = 0; k < n && _flakes.Count < MaxFlakes; k++)
                {
                    _flakes.Add(new Flake
                    {
                        X = d.Rect.Left + c * Cell + _rnd.NextDouble() * Cell,
                        Y = d.Rect.Top - _rnd.NextDouble() * h,
                        VX = (_rnd.NextDouble() - 0.5) * 50,
                        VY = _rnd.NextDouble() * 40,
                        R = 1.1 + _rnd.NextDouble() * 1.7,
                        Loose = true
                    });
                }
            }
        }

        // ================== физика ==================

        int Target => _weather switch
        {
            "rain" => (int)(TargetFlakes * 1.2),
            "sakura" => Math.Max(20, TargetFlakes / 3),
            "custom" => Math.Max(20, TargetFlakes / 3),
            "fireflies" => Math.Max(15, TargetFlakes / 5),
            "blizzard" => Math.Min(MaxFlakes / 2, (int)(TargetFlakes * 1.6)),
            "leaves" => Math.Max(15, TargetFlakes / 4),
            "roses" => Math.Max(15, TargetFlakes / 4),
            "confetti" => Math.Max(30, TargetFlakes / 2),
            "bubbles" => Math.Clamp(TargetFlakes / 8, 10, 150),
            "butterflies" => Math.Clamp(TargetFlakes / 25, 5, 40),
            "pollen" => Math.Max(25, (int)(TargetFlakes / 2.5)),
            "embers" => Math.Max(20, TargetFlakes / 3),
            "stars" => Math.Max(60, TargetFlakes / 2),
            "aurora" => Math.Max(40, TargetFlakes / 4),
            "matrix" => Math.Clamp(TargetFlakes / 7, 15, 140),
            "garland" => 0,                                       // лампочки ставятся один раз (BuildGarland)
            "hearts" => Math.Max(15, TargetFlakes / 4),
            "glitter" => Math.Max(40, TargetFlakes / 2),
            "crystals" => Math.Max(15, TargetFlakes / 5),
            "lanterns" => Math.Clamp(TargetFlakes / 18, 6, 45),
            "meteors" => Math.Clamp(TargetFlakes / 14, 6, 70),
            "fluff" => Math.Max(15, TargetFlakes / 6),
            _ => TargetFlakes
        };

        /// <summary>Эффекты, где частицы поднимаются снизу вверх.</summary>
        bool Rising => _weather == "bubbles" || _weather == "embers" || _weather == "lanterns" || _weather == "fluff";

        Flake NewFlake(bool anywhere)
        {
            var f = new Flake
            {
                X = _rnd.NextDouble() * _screenW,
                Y = anywhere ? _rnd.NextDouble() * _screenH : -10 - _rnd.NextDouble() * 40,
                Phase = _rnd.NextDouble() * Math.PI * 2
            };
            switch (_weather)
            {
                case "rain":
                    f.R = 10 + _rnd.NextDouble() * 14;                 // длина капли
                    f.VY = 900 + _rnd.NextDouble() * 500;
                    f.Tint = _rnd.Next(2);
                    break;
                case "custom":
                    f.R = 9 + _rnd.NextDouble() * 9;
                    f.VY = 45 + _rnd.NextDouble() * 45;
                    f.Rot = _rnd.NextDouble() * 360;
                    f.VRot = (_rnd.NextDouble() - 0.5) * 120;
                    break;
                case "sakura":
                    f.R = 4 + _rnd.NextDouble() * 3.5;
                    f.VY = 45 + _rnd.NextDouble() * 35;
                    f.Rot = _rnd.NextDouble() * 360;
                    f.VRot = (_rnd.NextDouble() - 0.5) * 160;
                    f.Tint = _rnd.Next(3);
                    break;
                case "fireflies":
                    f.Y = anywhere ? _rnd.NextDouble() * _screenH : _screenH * (0.3 + _rnd.NextDouble() * 0.7);
                    f.R = 1.6 + _rnd.NextDouble() * 1.6;
                    f.Life = 0;                                         // светлячки плавно зажигаются
                    break;
                case "blizzard":
                    f.R = 0.8 + Math.Pow(_rnd.NextDouble(), 1.8) * 2.2;
                    f.VY = 120 + _rnd.NextDouble() * 110;
                    f.VX = 380 + _rnd.NextDouble() * 260;
                    if (!anywhere && _rnd.Next(2) == 0) { f.X = -10; f.Y = _rnd.NextDouble() * _screenH; }   // метель залетает и сбоку
                    break;
                case "leaves":
                    f.R = 7 + _rnd.NextDouble() * 7;
                    f.VY = 45 + _rnd.NextDouble() * 40;
                    f.Rot = _rnd.NextDouble() * 360;
                    f.VRot = (_rnd.NextDouble() - 0.5) * 140;
                    f.Tint = _rnd.Next(LeafBrushes.Length);
                    break;
                case "roses":
                    f.R = 6 + _rnd.NextDouble() * 5;
                    f.VY = 40 + _rnd.NextDouble() * 35;
                    f.Rot = _rnd.NextDouble() * 360;
                    f.VRot = (_rnd.NextDouble() - 0.5) * 150;
                    f.Tint = _rnd.Next(RoseBrushes.Length);
                    break;
                case "confetti":
                    f.R = 7 + _rnd.NextDouble() * 6;
                    f.VY = 70 + _rnd.NextDouble() * 70;
                    f.Rot = _rnd.NextDouble() * 360;
                    f.VRot = (_rnd.NextDouble() - 0.5) * 420;
                    f.Tint = _rnd.Next(ConfettiBrushes.Length);
                    break;
                case "bubbles":
                    f.R = 6 + Math.Pow(_rnd.NextDouble(), 1.5) * 18;
                    f.VY = -(30 + _rnd.NextDouble() * 45) * (1.3 - f.R / 30);   // маленькие всплывают быстрее
                    if (!anywhere) f.Y = _screenH + f.R + _rnd.NextDouble() * 40;
                    f.Tint = _rnd.Next(BubblePens.Length);
                    break;
                case "butterflies":
                    f.R = 7 + _rnd.NextDouble() * 5;
                    f.VX = 60 + _rnd.NextDouble() * 50;                 // скорость полёта
                    f.Rot = _rnd.NextDouble() * Math.PI * 2;            // направление
                    f.Y = 60 + _rnd.NextDouble() * Math.Max(1, _screenH - 160);
                    f.X = 60 + _rnd.NextDouble() * Math.Max(1, _screenW - 120);
                    f.Tint = _rnd.Next(WingBrushes.Length);
                    f.Life = 0;
                    break;
                case "pollen":
                    f.R = 0.9 + _rnd.NextDouble() * 1.4;
                    f.Tint = _rnd.Next(2);
                    f.Life = 0;
                    if (!anywhere) f.Y = _rnd.NextDouble() * _screenH;
                    break;
                case "embers":
                    f.R = 1 + _rnd.NextDouble() * 1.8;
                    f.VY = -(50 + _rnd.NextDouble() * 110);
                    f.Life = 1;
                    f.VRot = 1 / (2.5 + _rnd.NextDouble() * 4);         // как быстро гаснет
                    f.Y = anywhere ? _screenH * (0.4 + _rnd.NextDouble() * 0.6) : _screenH + 5;
                    break;
                case "stars":
                case "aurora":
                    f.Y = _rnd.NextDouble() * _screenH * (_weather == "aurora" ? 0.55 : 1);
                    f.R = 0.5 + Math.Pow(_rnd.NextDouble(), 3) * 1.6;
                    f.VRot = 0.6 + _rnd.NextDouble() * 1.6;             // скорость мерцания
                    break;
                case "hearts":
                    f.R = 7 + _rnd.NextDouble() * 7;
                    f.VY = 40 + _rnd.NextDouble() * 35;
                    f.Tint = _rnd.Next(HeartBrushes.Length);
                    break;
                case "glitter":
                    f.R = 2.5 + _rnd.NextDouble() * 4;
                    f.VY = 25 + _rnd.NextDouble() * 35;
                    f.Rot = _rnd.NextDouble() * 360;
                    f.VRot = 0.8 + _rnd.NextDouble() * 2;               // скорость мерцания
                    f.Tint = _rnd.Next(GlitterLevels.Length);
                    break;
                case "crystals":
                    f.R = 6 + _rnd.NextDouble() * 9;
                    f.VY = 30 + _rnd.NextDouble() * 30;
                    f.Rot = _rnd.NextDouble() * 360;
                    f.VRot = (_rnd.NextDouble() - 0.5) * 50;
                    f.Tint = _rnd.Next(CrystalPens.Length);
                    break;
                case "lanterns":
                    f.R = 7 + _rnd.NextDouble() * 6;
                    f.VY = -(18 + _rnd.NextDouble() * 22);
                    if (!anywhere) f.Y = _screenH + f.R * 3 + _rnd.NextDouble() * 60;
                    break;
                case "meteors":
                {
                    double speed = 600 + _rnd.NextDouble() * 500, ang = (28 + _rnd.NextDouble() * 14) * Math.PI / 180;
                    f.VX = -Math.Cos(ang) * speed;                      // летят справа налево вниз
                    f.VY = Math.Sin(ang) * speed;
                    f.R = 80 + _rnd.NextDouble() * 160;                 // длина хвоста
                    f.Life = 0.5 + _rnd.NextDouble() * 0.5;             // яркость
                    f.Tint = _rnd.Next(MeteorColors.Length);
                    f.X = _screenW * (0.1 + _rnd.NextDouble() * 1.3);
                    if (!anywhere) f.Y = -20 - _rnd.NextDouble() * _screenH * 0.3;
                    break;
                }
                case "fluff":
                    f.R = 6 + _rnd.NextDouble() * 6;
                    f.VY = -(10 + _rnd.NextDouble() * 18);
                    if (!anywhere) f.Y = _screenH + 20 + _rnd.NextDouble() * 60;
                    break;
                case "matrix":
                    f.X = Math.Round(_rnd.NextDouble() * _screenW / MatrixSize) * MatrixSize;
                    ResetColumn(f, anywhere);
                    break;
                default:
                    f.R = 0.9 + Math.Pow(_rnd.NextDouble(), 2.2) * 2.6;
                    f.VY = 35 + f.R * 22 + _rnd.NextDouble() * 15;
                    break;
            }
            return f;
        }

        /// <summary>Колонка «цифрового дождя» начинается заново сверху: новая скорость, длина и знаки.</summary>
        void ResetColumn(Flake f, bool anywhere)
        {
            int len = 8 + _rnd.Next(18);
            f.Chars = new int[len];
            for (int i = 0; i < len; i++) f.Chars[i] = _rnd.Next(MatrixGlyphs.Length);
            f.VY = 140 + _rnd.NextDouble() * 260;
            f.Y = anywhere ? _rnd.NextDouble() * _screenH : -_rnd.NextDouble() * _screenH * 0.6;
            f.Life = 0;
        }

        /// <summary>Гирлянда вдоль верхнего края экрана: провод провисает дугами, на нём лампочки пяти цветов.</summary>
        void BuildGarland()
        {
            double top = SystemParameters.WorkArea.Top * _scale;
            int arcs = Math.Max(2, (int)Math.Round(_screenW / 380.0));
            double seg = (double)_screenW / arcs;
            double WireY(double x) => top + 4 + 34 * Math.Sin(Math.PI * ((x % seg) / seg));

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(0, WireY(0)), false, false);
                for (double x = 8; x <= _screenW; x += 8) ctx.LineTo(new Point(x, WireY(Math.Min(x, _screenW - 0.01))), true, true);
            }
            geo.Freeze();
            _wire = geo;

            int n = Math.Max(10, _screenW / 46);
            for (int i = 0; i < n; i++)
            {
                double x = (i + 0.5) * _screenW / n;
                _flakes.Add(new Flake { X = x, Y = WireY(x) + 10, R = 5.5, Tint = i % GarlandColors.Length, Phase = i });
            }
        }

        static double TurnTo(double a, double target, double maxStep)
        {
            double d = target - a;
            while (d > Math.PI) d -= Math.PI * 2;
            while (d < -Math.PI) d += Math.PI * 2;
            return a + Math.Clamp(d, -maxStep, maxStep);
        }

        /// <summary>Курсор отталкивает частицу, а если он движется — увлекает её за собой, как ветер.</summary>
        void Push(Flake f, double radius, double strength, double dt)
        {
            if (_hasCur)
            {
                double dx = f.X - _cx, dy = f.Y - _cy, d2 = dx * dx + dy * dy;
                if (d2 < radius * radius && d2 > 1)
                {
                    double d = Math.Sqrt(d2), k = 1 - d / radius;
                    f.PX += (dx / d * strength * k + _cvx * k * 0.5) * dt * 4;
                    f.PY += (dy / d * strength * k + _cvy * k * 0.5) * dt * 4;
                    f.PX = Math.Clamp(f.PX, -900, 900);
                    f.PY = Math.Clamp(f.PY, -900, 900);
                }
            }
            f.PX *= 1 - Math.Min(1, 1.8 * dt);
            f.PY *= 1 - Math.Min(1, 1.8 * dt);
        }

        /// <summary>Пузырь лопается — брызги разлетаются кольцом.</summary>
        void Pop(Flake f)
        {
            int n = 6 + (int)(f.R / 4);
            for (int k = 0; k < n && _sparks.Count < 400; k++)
            {
                double a = k * Math.PI * 2 / n + _rnd.NextDouble() * 0.4;
                _sparks.Add(new Flake
                {
                    X = f.X + Math.Cos(a) * f.R, Y = f.Y + Math.Sin(a) * f.R,
                    VX = Math.Cos(a) * (60 + _rnd.NextDouble() * 80), VY = Math.Sin(a) * (60 + _rnd.NextDouble() * 80) - 30,
                    R = 1 + _rnd.NextDouble() * 0.9, Life = 0.75, Tint = 0
                });
            }
        }

        void Step(double dt)
        {
            // Курсор: где он и как быстро движется (для эффектов, которые на него реагируют)
            Native.POINT cp = default;
            _hasCur = CursorReact && Native.GetCursorPos(out cp);
            if (_hasCur)
            {
                if (_curKnown)
                {
                    _cvx = Math.Clamp((cp.X - _cx) / dt, -4000, 4000);
                    _cvy = Math.Clamp((cp.Y - _cy) / dt, -4000, 4000);
                }
                _cx = cp.X; _cy = cp.Y; _curKnown = true;
            }
            else _curKnown = false;

            int normal = 0;
            foreach (var f in _flakes) if (!f.Loose) normal++;

            if (_weather == "garland")
            {
                if (normal == 0) BuildGarland();
            }
            else
            {
                int target = Target;
                if (normal == 0)
                    for (int i = 0; i < target; i++) _flakes.Add(NewFlake(true));
                else
                    for (int i = 0; i < Math.Min(target - normal, 12); i++) _flakes.Add(NewFlake(false));
            }

            // Падающие звёзды: примерно одна раз в две секунды
            if (_weather == "stars" && _rnd.NextDouble() < dt * 0.5 && _flakes.Count < MaxFlakes)
            {
                double dir = _rnd.Next(2) == 0 ? 1 : -1, speed = 900 + _rnd.NextDouble() * 600, ang = (18 + _rnd.NextDouble() * 20) * Math.PI / 180;
                _flakes.Add(new Flake
                {
                    X = _rnd.NextDouble() * _screenW, Y = _rnd.NextDouble() * _screenH * 0.45, Loose = true, Life = 1,
                    VX = Math.Cos(ang) * speed * dir, VY = Math.Sin(ang) * speed, R = 140 + _rnd.NextDouble() * 140
                });
            }

            double wind = Math.Sin(_time * 0.21) * 18 + Math.Sin(_time * 0.057) * 10;
            double gust = Math.Sin(_time * 0.5) * 160 + Math.Sin(_time * 1.7) * 60;

            for (int i = _flakes.Count - 1; i >= 0; i--)
            {
                var f = _flakes[i];
                double prevY = f.Y;
                bool remove = false;

                if (f.Loose)
                {
                    if (_weather == "stars")
                    {
                        f.X += f.VX * dt;
                        f.Y += f.VY * dt;
                        f.Life -= dt * 1.3;
                        remove = f.Life <= 0;
                    }
                    else
                    {
                        f.VY += 520 * dt;
                        f.VX *= 1 - 0.8 * dt;
                        f.X += f.VX * dt;
                        f.Y += f.VY * dt;
                        if (_weather == "rain") { f.Life += dt; remove = f.Life > 0.35; }
                    }
                }
                else switch (_weather)
                {
                    case "rain":
                        f.X += wind * 2.2 * dt;
                        f.Y += f.VY * dt;
                        break;
                    case "sakura":
                    case "custom":
                        f.X += (wind * 1.4 + Math.Sin(_time * 1.1 + f.Phase) * 38) * dt;
                        f.Y += f.VY * (0.75 + 0.25 * Math.Sin(_time * 2 + f.Phase)) * dt;
                        f.Rot += f.VRot * dt;
                        break;
                    case "fireflies":
                        f.Life = Math.Min(1, f.Life + dt * 0.5);
                        f.X += Math.Sin(_time * 0.7 + f.Phase) * 22 * dt + Math.Cos(_time * 0.31 + f.Phase * 2) * 14 * dt;
                        f.Y += Math.Cos(_time * 0.5 + f.Phase) * 16 * dt - 4 * dt;
                        if (f.Y < -20) f.Y = _screenH + 10;
                        break;
                    case "blizzard":
                        f.X += (f.VX + gust) * dt;
                        f.Y += (f.VY + Math.Sin(_time * 2.3 + f.Phase) * 40) * dt;
                        break;
                    case "leaves":
                    case "roses":
                        Push(f, 130, 420, dt);
                        f.X += (wind * 1.6 + Math.Sin(_time * 0.9 + f.Phase) * 55 + f.PX) * dt;
                        f.Y += (f.VY * (0.65 + 0.35 * Math.Sin(_time * 1.8 + f.Phase)) + f.PY) * dt;
                        f.Rot += f.VRot * dt;
                        break;
                    case "confetti":
                        Push(f, 120, 380, dt);
                        f.X += (wind * 1.2 + Math.Sin(_time * 1.7 + f.Phase) * 30 + f.PX) * dt;
                        f.Y += (f.VY + f.PY) * dt;
                        f.Rot += f.VRot * dt;
                        break;
                    case "bubbles":
                    {
                        Push(f, 90, 160, dt);
                        f.X += (Math.Sin(_time * 1.6 + f.Phase) * 18 + wind * 0.4 + f.PX) * dt;
                        f.Y += (f.VY + f.PY * 0.3) * dt;
                        bool hit = _hasCur && (f.X - _cx) * (f.X - _cx) + (f.Y - _cy) * (f.Y - _cy) < (f.R + 6) * (f.R + 6);
                        if (hit || (f.Y < _screenH * 0.3 && _rnd.NextDouble() < dt * 0.25)) { Pop(f); remove = true; }
                        break;
                    }
                    case "butterflies":
                    {
                        f.Life = Math.Min(1, f.Life + dt * 0.6);
                        f.VRot = Math.Clamp(f.VRot + (_rnd.NextDouble() - 0.5) * 6 * dt, -1.6, 1.6);
                        double speed = f.VX;
                        if (_hasCur)
                        {
                            double dx = f.X - _cx, dy = f.Y - _cy;
                            if (dx * dx + dy * dy < 170 * 170) { f.Rot = TurnTo(f.Rot, Math.Atan2(dy, dx), 5 * dt); speed *= 2.4; }   // улетают от курсора
                        }
                        if (f.X < 50 || f.X > _screenW - 50 || f.Y < 50 || f.Y > _screenH - 70)
                            f.Rot = TurnTo(f.Rot, Math.Atan2(_screenH / 2.0 - f.Y, _screenW / 2.0 - f.X), 2.5 * dt);
                        f.Rot += f.VRot * dt;
                        f.Phase += dt * (speed > 150 ? 24 : 13);       // взмахи крыльев
                        f.X += Math.Cos(f.Rot) * speed * dt;
                        f.Y += (Math.Sin(f.Rot) * speed + Math.Sin(f.Phase) * 14) * dt;
                        break;
                    }
                    case "pollen":
                    {
                        f.Life = Math.Min(1, f.Life + dt * 0.4);
                        double bx = Math.Sin(_time * 0.5 + f.Phase) * 14 + wind * 0.3, by = -10 + Math.Cos(_time * 0.4 + f.Phase * 1.7) * 8;
                        if (_hasCur)
                        {
                            double dx = _cx - f.X, dy = _cy - f.Y, d = Math.Sqrt(dx * dx + dy * dy);
                            if (d < 240 && d > 1)
                            {
                                double k = 1 - d / 240;   // притягивается и кружится вокруг курсора
                                f.VX += (dx / d * 260 * k - dy / d * 320 * k) * dt;
                                f.VY += (dy / d * 260 * k + dx / d * 320 * k) * dt;
                            }
                        }
                        f.VX *= 1 - Math.Min(1, 1.2 * dt);
                        f.VY *= 1 - Math.Min(1, 1.2 * dt);
                        f.X += (bx + f.VX) * dt;
                        f.Y += (by + f.VY) * dt;
                        if (f.Y < -20) f.Y = _screenH + 10;
                        break;
                    }
                    case "embers":
                        Push(f, 110, 300, dt);
                        f.Life -= dt * f.VRot;
                        f.X += (wind * 0.8 + Math.Sin(_time * 3 + f.Phase) * 25 + f.PX) * dt;
                        f.Y += (f.VY + f.PY) * dt;
                        remove = f.Life <= 0;
                        break;
                    case "stars":
                    case "aurora":
                    case "garland":
                        break;                                          // стоят на месте, только мерцают
                    case "hearts":
                        Push(f, 130, 380, dt);
                        f.X += (wind * 1.2 + Math.Sin(_time * 1.2 + f.Phase) * 40 + f.PX) * dt;
                        f.Y += (f.VY * (0.8 + 0.2 * Math.Sin(_time * 2 + f.Phase)) + f.PY) * dt;
                        f.Rot = Math.Sin(_time * 1.3 + f.Phase) * 18;     // покачивается
                        break;
                    case "glitter":
                        Push(f, 110, 300, dt);
                        f.X += (wind * 0.8 + Math.Sin(_time * 0.9 + f.Phase) * 20 + f.PX) * dt;
                        f.Y += (f.VY + f.PY) * dt;
                        f.Rot += 25 * dt;
                        break;
                    case "crystals":
                        Push(f, 140, 260, dt);
                        f.X += (wind + Math.Sin(_time * 0.7 + f.Phase) * 22 + f.PX) * dt;
                        f.Y += (f.VY + f.PY) * dt;
                        f.Rot += f.VRot * dt;
                        break;
                    case "lanterns":
                        Push(f, 120, 140, dt);
                        f.X += (Math.Sin(_time * 0.5 + f.Phase) * 14 + wind * 0.5 + f.PX) * dt;
                        f.Y += (f.VY + f.PY * 0.4) * dt;
                        break;
                    case "meteors":
                        f.X += f.VX * dt;
                        f.Y += f.VY * dt;
                        if (f.X < -280) remove = true;
                        break;
                    case "fluff":
                        Push(f, 170, 520, dt);
                        f.X += (wind * 1.4 + Math.Sin(_time * 0.8 + f.Phase) * 26 + f.PX) * dt;
                        f.Y += (f.VY + Math.Cos(_time * 0.6 + f.Phase) * 10 + f.PY) * dt;
                        f.Rot = Math.Sin(_time * 1.1 + f.Phase) * 25;
                        break;
                    case "matrix":
                        f.Y += f.VY * dt;
                        f.Life += dt;
                        if (f.Life > 0.09) { f.Life = 0; f.Chars[_rnd.Next(f.Chars.Length)] = _rnd.Next(MatrixGlyphs.Length); }
                        if (f.Y - f.Chars.Length * MatrixSize > _screenH) ResetColumn(f, false);
                        break;
                    default:
                        f.X += (wind * (0.5 + f.R * 0.25) + Math.Sin(_time * 1.3 + f.Phase) * 12) * dt;
                        f.Y += f.VY * dt;
                        break;
                }
                if (!f.Loose && _weather != "garland" && _weather != "matrix" && _weather != "meteors")
                {
                    if (f.X < -20) f.X += _screenW + 40;
                    else if (f.X > _screenW + 20) f.X -= _screenW + 40;
                }

                bool gone = f.Loose ? (f.Y > _screenH + 30 || f.X < -60 || f.X > _screenW + 60)
                          : Rising ? f.Y < -60
                          : _weather != "matrix" && f.Y > _screenH + 30;
                if (remove || gone || TryLand(f, prevY))
                {
                    _flakes[i] = _flakes[_flakes.Count - 1];   // быстрое удаление без сдвига списка
                    _flakes.RemoveAt(_flakes.Count - 1);
                }
            }

            if (IsSnow)
            {
                foreach (var d in _order) Relax(d);
                if (GroundDrift && _ground != null) Relax(_ground);
            }
        }

        bool TryLand(Flake f, double prevY)
        {
            if (!IsSnow && _weather != "rain") return false;
            if (f.Loose && _weather == "rain") return false;
            foreach (var d in _order)
                if (LandOn(d, f, prevY)) return true;
            return GroundDrift && _ground != null && LandOn(_ground, f, prevY);
        }

        bool LandOn(Drift d, Flake f, double prevY)
        {
            if (f.X < d.Rect.Left || f.X >= d.Rect.Right) return false;
            int c = (int)((f.X - d.Rect.Left) / Cell);
            if (c < 0 || c >= d.H.Length || !d.Vis[c]) return false;

            double surface = d.Rect.Top - d.H[c];
            if (prevY > surface || f.Y < surface) return false;   // не пересекли поверхность в этом кадре

            if (_weather == "rain")
            {
                // Капля разбивается на брызги
                for (int k = 0; k < 2 && _flakes.Count < MaxFlakes; k++)
                    _flakes.Add(new Flake
                    {
                        X = f.X, Y = surface - 1, Loose = true, R = 1,
                        VX = (_rnd.NextDouble() - 0.5) * 140, VY = -(60 + _rnd.NextDouble() * 90)
                    });
                return true;
            }

            float add = f.Loose ? 0.9f : (float)(f.R * 0.3);
            d.H[c] = Math.Min(d.Cap[c], d.H[c] + add);
            return true;
        }

        /// <summary>Сглаживание: слишком крутые склоны осыпаются в соседние столбики.</summary>
        static void Relax(Drift d)
        {
            var h = d.H;
            const float slope = 1.6f;
            for (int c = 0; c < h.Length - 1; c++)
            {
                float diff = h[c] - h[c + 1];
                if (diff > slope)
                {
                    float m = (diff - slope) * 0.25f;
                    h[c] -= m; h[c + 1] = Math.Min(d.Cap[c + 1], h[c + 1] + m);
                }
                else if (diff < -slope)
                {
                    float m = (-diff - slope) * 0.25f;
                    h[c + 1] -= m; h[c] = Math.Min(d.Cap[c], h[c] + m);
                }
            }
        }

        /// <summary>Курсор разгребает сугробы и раскидывает снег.</summary>
        void Scrape()
        {
            if (!Native.GetCursorPos(out var p)) return;
            const int radius = 18;

            foreach (var d in AllDrifts())
            {
                if (p.X < d.Rect.Left - radius || p.X > d.Rect.Right + radius) continue;
                if (p.Y < d.Rect.Top - d.MaxH - 6 || p.Y > d.Rect.Top + 2) continue;

                int c0 = Math.Max(0, (p.X - radius - d.Rect.Left) / Cell);
                int c1 = Math.Min(d.H.Length - 1, (p.X + radius - d.Rect.Left) / Cell);
                for (int c = c0; c <= c1; c++)
                {
                    if (!d.Vis[c] || d.H[c] < 0.3f) continue;
                    double cx = d.Rect.Left + c * Cell + Cell * 0.5;
                    double dx = (cx - p.X) / radius;
                    if (Math.Abs(dx) > 1) continue;

                    double allowed = Math.Max(0, (d.Rect.Top - p.Y) + dx * dx * d.MaxH);
                    if (d.H[c] <= allowed) continue;

                    float removed = d.H[c] - (float)allowed;
                    d.H[c] = (float)allowed;

                    int n = Math.Min(2, (int)(removed / 2) + 1);
                    for (int k = 0; k < n && _flakes.Count < MaxFlakes; k++)
                    {
                        _flakes.Add(new Flake
                        {
                            X = cx, Y = d.Rect.Top - d.H[c] - 1,
                            VX = Math.Sign(dx == 0 ? _rnd.NextDouble() - 0.5 : dx) * (60 + _rnd.NextDouble() * 120),
                            VY = -(80 + _rnd.NextDouble() * 120),
                            R = 1.0 + _rnd.NextDouble() * 1.4,
                            Loose = true
                        });
                    }
                }
            }
        }

        /// <summary>Искры, которые сыплются за курсором, когда он движется.</summary>
        void StepSparks(double dt)
        {
            if (_trail && Native.GetCursorPos(out var p))
            {
                double dx = p.X - _lastCursor.X, dy = p.Y - _lastCursor.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                int n = Math.Min(6, (int)(dist / 9));
                for (int k = 0; k < n && _sparks.Count < 400; k++)
                {
                    double t = (k + 1.0) / n;
                    _sparks.Add(new Flake
                    {
                        X = _lastCursor.X + dx * t, Y = _lastCursor.Y + dy * t,
                        VX = (_rnd.NextDouble() - 0.5) * 90, VY = (_rnd.NextDouble() - 0.7) * 90,
                        R = 1.2 + _rnd.NextDouble() * 2, Life = 1, Tint = _rnd.Next(3)
                    });
                }
                _lastCursor = p;
            }

            for (int i = _sparks.Count - 1; i >= 0; i--)
            {
                var s = _sparks[i];
                s.Life -= dt * 1.6;
                s.VY += 120 * dt;
                s.X += s.VX * dt;
                s.Y += s.VY * dt;
                if (s.Life <= 0)
                {
                    _sparks[i] = _sparks[_sparks.Count - 1];
                    _sparks.RemoveAt(_sparks.Count - 1);
                }
            }
        }

        IEnumerable<Drift> AllDrifts()
        {
            foreach (var d in _order) yield return d;
            if (GroundDrift && _ground != null) yield return _ground;
        }

        // ================== отрисовка ==================

        protected override void OnRender(DrawingContext dc)
        {
            if (_screenW == 0 || _fullscreenHidden) return;
            dc.PushTransform(new ScaleTransform(1 / _scale, 1 / _scale));

            if (!_paused)
            {
                if (IsSnow) foreach (var d in AllDrifts()) DrawDrift(dc, d);
                if (_weather == "aurora") DrawAurora(dc);
                if (_weather == "garland" && _wire != null) dc.DrawGeometry(null, WirePen, _wire);

                foreach (var f in _flakes)
                {
                    switch (_weather)
                    {
                        case "rain":
                            if (f.Loose) dc.DrawEllipse(SnowBrushes[0], null, new Point(f.X, f.Y), 1, 1);
                            else
                            {
                                double tilt = (Math.Sin(_time * 0.21) * 18 + Math.Sin(_time * 0.057) * 10) * 2.2 / f.VY;
                                dc.DrawLine(RainPens[f.Tint], new Point(f.X, f.Y), new Point(f.X - tilt * f.R, f.Y - f.R));
                            }
                            break;
                        case "sakura":
                            dc.PushTransform(new RotateTransform(f.Rot, f.X, f.Y));
                            dc.DrawEllipse(Petals[f.Tint], null, new Point(f.X, f.Y), f.R, f.R * 0.6);
                            dc.Pop();
                            break;
                        case "custom":
                            if (_sprite == null) break;
                            dc.PushTransform(new RotateTransform(f.Rot, f.X, f.Y));
                            dc.DrawImage(_sprite, new Rect(f.X - f.R, f.Y - f.R, f.R * 2, f.R * 2));
                            dc.Pop();
                            break;
                        case "fireflies":
                        {
                            double pulse = 0.5 + 0.5 * Math.Sin(_time * 2.2 + f.Phase * 3);
                            int lvl = (int)(pulse * f.Life * 7.99);
                            dc.DrawEllipse(FlyGlow[lvl], null, new Point(f.X, f.Y), f.R * 4, f.R * 4);
                            if (pulse * f.Life > 0.25) dc.DrawEllipse(FlyCore, null, new Point(f.X, f.Y), f.R, f.R);
                            break;
                        }
                        case "blizzard":
                        {
                            if (f.Loose) { dc.DrawEllipse(SnowBrushes[1], null, new Point(f.X, f.Y), f.R, f.R); break; }
                            // Снежинка в метель — короткий штрих по направлению ветра
                            double vx = f.VX + Math.Sin(_time * 0.5) * 160 + Math.Sin(_time * 1.7) * 60, vy = f.VY;
                            double len = Math.Sqrt(vx * vx + vy * vy), k = f.R * 5 / Math.Max(1, len);
                            var pen = f.R < 1.4 ? BlizzardPens[0] : f.R < 2.2 ? BlizzardPens[1] : BlizzardPens[2];
                            dc.DrawLine(pen, new Point(f.X, f.Y), new Point(f.X - vx * k, f.Y - vy * k));
                            break;
                        }
                        case "leaves":
                        {
                            double flip = Math.Sin(_time * 2.6 + f.Phase);          // лист переворачивается в полёте
                            Shape(dc, f, f.R * (0.35 + 0.65 * Math.Abs(flip)), f.R);
                            dc.DrawGeometry(LeafBrushes[f.Tint], LeafVein, LeafGeo);
                            dc.Pop();
                            break;
                        }
                        case "roses":
                        {
                            double flip = Math.Sin(_time * 2.1 + f.Phase);
                            Shape(dc, f, f.R * (0.45 + 0.55 * Math.Abs(flip)), f.R);
                            dc.DrawGeometry(RoseBrushes[f.Tint], null, PetalGeo);
                            dc.Pop();
                            break;
                        }
                        case "confetti":
                        {
                            double flip = Math.Cos(_time * 6 + f.Phase * 3);       // кувыркается
                            Shape(dc, f, f.R * 0.6, f.R * 0.32 * (0.15 + 0.85 * Math.Abs(flip)));
                            dc.DrawRectangle(ConfettiBrushes[f.Tint], null, UnitRect);
                            dc.Pop();
                            break;
                        }
                        case "bubbles":
                        {
                            var c = new Point(f.X, f.Y);
                            dc.DrawEllipse(BubbleFill, BubblePens[f.Tint], c, f.R, f.R);
                            dc.DrawEllipse(BubbleShine, null, new Point(f.X - f.R * 0.38, f.Y - f.R * 0.4), f.R * 0.26, f.R * 0.16);
                            break;
                        }
                        case "butterflies":
                            DrawButterfly(dc, f);
                            break;
                        case "pollen":
                        {
                            double tw = 0.55 + 0.45 * Math.Sin(_time * 3 + f.Phase * 4);
                            int lvl = Math.Max(0, Math.Min(7, (int)(tw * f.Life * 7.99)));
                            dc.DrawEllipse(PollenGlow[f.Tint][lvl], null, new Point(f.X, f.Y), f.R * 3.5, f.R * 3.5);
                            dc.DrawEllipse(PollenCore, null, new Point(f.X, f.Y), f.R * 0.8, f.R * 0.8);
                            break;
                        }
                        case "embers":
                        {
                            double flick = 0.7 + 0.3 * Math.Sin(_time * 18 + f.Phase * 7);
                            int lvl = Math.Max(0, Math.Min(7, (int)(f.Life * flick * 7.99)));
                            dc.DrawEllipse(EmberGlow[lvl], null, new Point(f.X, f.Y), f.R * 3.2, f.R * 3.2);
                            dc.DrawEllipse(EmberCore[lvl], null, new Point(f.X, f.Y), f.R, f.R);
                            break;
                        }
                        case "stars":
                        case "aurora":
                            if (f.Loose) DrawShootingStar(dc, f);
                            else DrawStar(dc, f);
                            break;
                        case "hearts":
                        {
                            double beat = 1 + 0.08 * Math.Sin(_time * 4 + f.Phase);
                            double flip = 0.7 + 0.3 * Math.Abs(Math.Sin(_time * 1.5 + f.Phase));
                            Shape(dc, f, f.R * beat * flip, f.R * beat);
                            dc.DrawGeometry(HeartBrushes[f.Tint], null, HeartGeo);
                            dc.Pop();
                            break;
                        }
                        case "glitter":
                        {
                            double tw = 0.5 + 0.5 * Math.Sin(_time * f.VRot * 3 + f.Phase * 5);
                            int lvl = Math.Max(0, Math.Min(7, (int)((0.25 + 0.75 * tw) * 7.99)));
                            double sz = f.R * (0.45 + 0.55 * tw);
                            Shape(dc, f, sz, sz);
                            dc.DrawGeometry(GlitterLevels[f.Tint][lvl], null, SparkleGeo);
                            dc.Pop();
                            break;
                        }
                        case "crystals":
                            Shape(dc, f, f.R, f.R);
                            dc.DrawGeometry(null, CrystalPens[f.Tint], CrystalGeo);
                            dc.Pop();
                            break;
                        case "lanterns":
                        {
                            double flick = 0.75 + 0.25 * Math.Sin(_time * 7 + f.Phase * 3) * Math.Sin(_time * 2.3 + f.Phase);
                            int lvl = Math.Max(0, Math.Min(7, (int)(flick * 7.99)));
                            Shape(dc, f, f.R, f.R);
                            dc.DrawEllipse(LanternGlow[lvl], null, new Point(0, 0), 2.6, 2.6);
                            dc.DrawRoundedRectangle(LanternBody, null, new Rect(-0.55, -0.9, 1.1, 1.5), 0.25, 0.25);
                            dc.DrawEllipse(LanternCore, null, new Point(0, 0.35), 0.28 * flick, 0.22 * flick);
                            dc.Pop();
                            break;
                        }
                        case "meteors":
                            DrawMeteor(dc, f);
                            break;
                        case "fluff":
                            Shape(dc, f, f.R, f.R);
                            dc.DrawGeometry(null, FluffPen, FluffGeo);
                            dc.DrawEllipse(FluffCore, null, new Point(0, 1.3), 0.13, 0.24);
                            dc.Pop();
                            break;
                        case "matrix":
                            DrawColumn(dc, f);
                            break;
                        case "garland":
                            DrawBulb(dc, f);
                            break;
                        default:
                            var b = f.R < 1.6 ? SnowBrushes[0] : f.R < 2.4 ? SnowBrushes[1] : SnowBrushes[2];
                            dc.DrawEllipse(b, null, new Point(f.X, f.Y), f.R, f.R);
                            break;
                    }
                }
            }

            foreach (var s in _sparks)
            {
                int lvl = Math.Max(0, Math.Min(7, (int)(s.Life * 7.99)));
                dc.DrawEllipse(SparkLevels[s.Tint][lvl], null, new Point(s.X, s.Y), s.R * s.Life + 0.4, s.R * s.Life + 0.4);
            }

            dc.Pop();
        }

        /// <summary>Сдвиг, поворот и масштаб для фигуры размером 2×2 вокруг нуля (лист, лепесток, конфетти). Не забыть dc.Pop().</summary>
        static void Shape(DrawingContext dc, Flake f, double sx, double sy)
        {
            var m = new Matrix();
            m.Scale(Math.Max(0.05, sx), Math.Max(0.05, sy));
            m.Rotate(f.Rot);
            m.Translate(f.X, f.Y);
            dc.PushTransform(new MatrixTransform(m));
        }

        void DrawButterfly(DrawingContext dc, Flake f)
        {
            if (f.Life < 0.05) return;
            double w = 0.2 + 0.8 * Math.Abs(Math.Cos(f.Phase));             // крылья складываются и раскрываются
            var m = new Matrix();
            m.Scale(f.R, f.R);
            m.Rotate(f.Rot * 180 / Math.PI + 90);
            m.Translate(f.X, f.Y);
            dc.PushTransform(new MatrixTransform(m));
            if (f.Life < 1) dc.PushOpacity(f.Life);
            var (upper, lower) = WingBrushes[f.Tint];
            dc.DrawEllipse(upper, null, new Point(-0.55 * w, -0.2), 0.62 * w, 0.5);
            dc.DrawEllipse(upper, null, new Point(0.55 * w, -0.2), 0.62 * w, 0.5);
            dc.DrawEllipse(lower, null, new Point(-0.42 * w, 0.38), 0.42 * w, 0.36);
            dc.DrawEllipse(lower, null, new Point(0.42 * w, 0.38), 0.42 * w, 0.36);
            dc.DrawEllipse(ButterflyBody, null, new Point(0, 0.05), 0.09, 0.62);
            if (f.Life < 1) dc.Pop();
            dc.Pop();
        }

        void DrawStar(DrawingContext dc, Flake f)
        {
            double tw = 0.5 + 0.5 * Math.Sin(_time * f.VRot * 2 + f.Phase * 5);
            double v = 0.3 + 0.7 * tw;
            int lvl = Math.Max(0, Math.Min(7, (int)(v * 7.99)));
            dc.DrawEllipse(StarLevels[lvl], null, new Point(f.X, f.Y), f.R, f.R);
            if (f.R > 1.2 && tw > 0.75)
            {
                double len = f.R * 4 * tw;   // яркие звёзды «сверкают» лучиками
                dc.DrawLine(StarPens[lvl], new Point(f.X - len, f.Y), new Point(f.X + len, f.Y));
                dc.DrawLine(StarPens[lvl], new Point(f.X, f.Y - len), new Point(f.X, f.Y + len));
            }
        }

        /// <summary>Метеор: цветной хвост тает к концу, на голове — яркая точка.</summary>
        void DrawMeteor(DrawingContext dc, Flake f)
        {
            double speed = Math.Sqrt(f.VX * f.VX + f.VY * f.VY);
            if (speed < 1) return;
            double ux = -f.VX / speed, uy = -f.VY / speed;
            var pens = MeteorPens[f.Tint];
            for (int k = 0; k < 4; k++)
            {
                var a = new Point(f.X + ux * f.R * k / 4, f.Y + uy * f.R * k / 4);
                var b = new Point(f.X + ux * f.R * (k + 1) / 4, f.Y + uy * f.R * (k + 1) / 4);
                int lvl = Math.Max(0, Math.Min(7, (int)((1 - k / 4.0) * f.Life * 7.99)));
                dc.DrawLine(pens[lvl], a, b);
            }
            dc.DrawEllipse(MeteorHeads[f.Tint][Math.Max(0, Math.Min(7, (int)(f.Life * 7.99)))], null, new Point(f.X, f.Y), 2.2, 2.2);
        }

        void DrawShootingStar(DrawingContext dc, Flake f)
        {
            double speed = Math.Sqrt(f.VX * f.VX + f.VY * f.VY);
            double ux = -f.VX / speed, uy = -f.VY / speed, len = f.R * Math.Max(0.2, f.Life);
            var head = new Point(f.X, f.Y);
            // Хвост тает к концу: три отрезка всё прозрачнее
            for (int k = 0; k < 3; k++)
            {
                var a = new Point(f.X + ux * len * k / 3, f.Y + uy * len * k / 3);
                var b = new Point(f.X + ux * len * (k + 1) / 3, f.Y + uy * len * (k + 1) / 3);
                int lvl = Math.Max(0, Math.Min(7, (int)((1 - k / 3.0) * f.Life * 7.99)));
                dc.DrawLine(ShootPens[lvl], a, b);
            }
            dc.DrawEllipse(StarLevels[Math.Max(0, Math.Min(7, (int)(f.Life * 7.99)))], null, head, 1.8, 1.8);
        }

        /// <summary>Северное сияние: три полупрозрачные «шторы», которые медленно колышутся у верха экрана.</summary>
        void DrawAurora(DrawingContext dc)
        {
            const int n = 40;
            for (int k = 0; k < 3; k++)
            {
                double baseY = _screenH * (0.16 + 0.06 * k), amp = _screenH * 0.05;
                var geo = new StreamGeometry();
                var bottom = new Point[n + 1];
                using (var ctx = geo.Open())
                {
                    for (int i = 0; i <= n; i++)
                    {
                        double x = (double)i / n * _screenW, u = (double)i / n;
                        double y = baseY + amp * Math.Sin(u * Math.PI * 2 * (1.2 + 0.3 * k) + _time * (0.25 + 0.07 * k) + k * 2)
                                         + amp * 0.5 * Math.Sin(u * Math.PI * 6 + _time * 0.6 + k);
                        double h = _screenH * (0.12 + 0.05 * k) * (0.65 + 0.35 * Math.Sin(u * Math.PI * 4 + _time * 0.4 + k * 1.3));
                        bottom[i] = new Point(x, y);
                        var top = new Point(x, y - h);
                        if (i == 0) ctx.BeginFigure(top, true, true); else ctx.LineTo(top, false, true);
                    }
                    for (int i = n; i >= 0; i--) ctx.LineTo(bottom[i], false, true);
                }
                geo.Freeze();
                dc.DrawGeometry(AuroraFill[k], null, geo);
            }
        }

        void DrawColumn(DrawingContext dc, Flake f)
        {
            if (_matrixText == null || _matrixDip != _scale) BuildMatrixText();
            int len = f.Chars.Length;
            for (int i = 0; i < len; i++)
            {
                double y = f.Y - i * MatrixSize;
                if (y < -MatrixSize || y > _screenH) continue;
                int level = i == 0 ? 8 : Math.Max(0, Math.Min(7, (int)((1 - (double)i / len) * 7.99)));
                dc.DrawText(_matrixText[f.Chars[i] * 9 + level], new Point(f.X, y));
            }
        }

        void BuildMatrixText()
        {
            _matrixDip = _scale;
            var face = new Typeface(new FontFamily("MS Gothic, Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            _matrixText = new FormattedText[MatrixGlyphs.Length * 9];
            for (int g = 0; g < MatrixGlyphs.Length; g++)
                for (int l = 0; l < 9; l++)
                    _matrixText[g * 9 + l] = new FormattedText(MatrixGlyphs[g].ToString(), System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, face, MatrixSize * 0.9, l == 8 ? MatrixHead : MatrixLevels[l], _scale);
        }

        void DrawBulb(DrawingContext dc, Flake f)
        {
            double b = 0.3 + 0.7 * (0.5 + 0.5 * Math.Sin(_time * 2.4 - f.Phase * 0.55));   // бегущая волна огоньков
            bool near = false;
            if (_hasCur)
            {
                double dx = f.X - _cx, dy = f.Y - _cy;
                near = dx * dx + dy * dy < 140 * 140;                      // курсор рядом — лампочка вспыхивает
                if (near) b = 1;
            }
            int lvl = Math.Max(0, Math.Min(7, (int)(b * 7.99)));
            double glow = f.R * (near ? 4.6 : 3.2) * (0.6 + 0.4 * b);
            var c = new Point(f.X, f.Y + f.R * 0.3);
            dc.DrawEllipse(GarlandGlow[f.Tint][lvl], null, c, glow, glow);
            dc.DrawEllipse(b > 0.6 ? GarlandBright[f.Tint] : GarlandDim[f.Tint], null, c, f.R * 0.72, f.R);
            dc.DrawRectangle(BulbCap, null, new Rect(f.X - 2.6, f.Y - f.R * 0.95, 5.2, 4));
        }

        void DrawDrift(DrawingContext dc, Drift d)
        {
            var geo = new StreamGeometry();
            bool any = false;
            using (var ctx = geo.Open())
            {
                int n = d.H.Length, c = 0;
                double baseY = d.Rect.Top + 1;
                while (c < n)
                {
                    while (c < n && (!d.Vis[c] || d.H[c] < 0.4f)) c++;
                    if (c >= n) break;

                    ctx.BeginFigure(new Point(d.Rect.Left + c * Cell, baseY), true, true);
                    while (c < n && d.Vis[c] && d.H[c] >= 0.4f)
                    {
                        ctx.LineTo(new Point(d.Rect.Left + c * Cell + Cell * 0.5, baseY - d.H[c] - 1), true, true);
                        c++;
                    }
                    ctx.LineTo(new Point(d.Rect.Left + c * Cell, baseY), true, false);
                    any = true;
                }
            }
            if (!any) return;
            geo.Freeze();
            dc.DrawGeometry(DriftBrush, DriftPen, geo);
        }
    }
}
