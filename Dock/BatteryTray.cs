// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>
    /// Заряд устройств значками в трее Windows (работает и с обычной панелью задач Windows 10/11).
    /// На каждый прибор — свой значок: процент цифрами и полоска заряда, у проводного — вилка. Подсказка — имя и процент.
    /// </summary>
    internal sealed class BatteryTray : IDisposable
    {
        readonly Settings _s;
        readonly Dispatcher _ui;
        readonly Dictionary<DeviceBattery.Kind, NotifyIcon> _icons = new Dictionary<DeviceBattery.Kind, NotifyIcon>();
        readonly Dictionary<DeviceBattery.Kind, string> _shown = new Dictionary<DeviceBattery.Kind, string>();
        readonly Action _open;
        readonly Dictionary<DeviceBattery.Kind, string> _urls = new Dictionary<DeviceBattery.Kind, string>();

        public static BatteryTray Instance { get; private set; }

        public BatteryTray(Settings s, Dispatcher ui, Action open)
        {
            _s = s; _ui = ui; _open = open;
            Instance = this;
            DeviceBattery.Changed += () => _ui.BeginInvoke(new Action(Sync));
            Loc.Changed += Sync;
            Sync();
        }

        /// <summary>Показать/спрятать/перерисовать значки по настройкам и текущему заряду.</summary>
        public void Sync()
        {
            if (!_ui.CheckAccess()) { _ui.BeginInvoke(new Action(Sync)); return; }
            var want = _s.BattTray ? DeviceBattery.Visible(_s) : new List<DeviceBattery.Info>();
            var keep = new HashSet<DeviceBattery.Kind>();
            foreach (var d in want)
            {
                keep.Add(d.Kind);
                _urls[d.Kind] = d.NeedsApp ? d.AppUrl : null;
                string key = d.Name + ":" + d.Percent + ":" + d.Receiver + ":" + d.AppUrl + ":" + Loc.Code;
                if (!_icons.TryGetValue(d.Kind, out var ni))
                {
                    ni = new NotifyIcon();
                    var kind = d.Kind;
                    ni.MouseClick += (o, e) =>
                    {
                        if (e.Button != MouseButtons.Left) return;
                        // Нет программы бренда — клик ведёт на её страницу, иначе открывает MizuEngine
                        if (_urls.TryGetValue(kind, out var url) && url != null)
                            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
                        else _open?.Invoke();
                    };
                    _icons[d.Kind] = ni;
                }
                if (!_shown.TryGetValue(d.Kind, out var old) || old != key)
                {
                    _shown[d.Kind] = key;
                    var prev = ni.Icon;
                    ni.Icon = Draw(d);
                    prev?.Dispose();
                    string tip = DeviceBattery.KindName(d.Kind) + (d.Name.Length > 0 ? " — " + d.Name : "") + ": " +
                                 (d.NeedsApp ? string.Format(Loc.T("batt.app.tip"), string.IsNullOrEmpty(d.AppBrand) ? d.Name : d.AppBrand) : d.State);
                    ni.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
                }
                ni.Visible = true;
            }
            foreach (var k in new List<DeviceBattery.Kind>(_icons.Keys))
                if (!keep.Contains(k)) { _icons[k].Visible = false; _icons[k].Dispose(); _icons.Remove(k); _shown.Remove(k); }
            if (_icons.Count > 0) System.Threading.ThreadPool.QueueUserWorkItem(_ => { System.Threading.Thread.Sleep(2500); if (Promote()) _ui.BeginInvoke(new Action(Readd)); });
        }

        /// <summary>Заново показать значки — так панель задач сразу учитывает новое разрешение.</summary>
        void Readd()
        {
            foreach (var ni in _icons.Values) { ni.Visible = false; ni.Visible = true; }
        }

        /// <summary>
        /// Windows 11 прячет новые значки трея, и без меню «скрытых значков» их вообще не видно. Один раз выводим наши значки заряда
        /// на панель (как переключатель в «Параметры → Панель задач → Другие значки»). Если пользователь потом сам выключит — не трогаем.
        /// </summary>
        static bool Promote()
        {
            bool changed = false;
            try
            {
                string exe = Environment.ProcessPath ?? "";
                var names = new[] { DeviceBattery.KindName(DeviceBattery.Kind.Headphones), DeviceBattery.KindName(DeviceBattery.Kind.Mouse), DeviceBattery.KindName(DeviceBattery.Kind.Keyboard) };
                using var root = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings");
                if (root == null) return false;
                foreach (var id in root.GetSubKeyNames())
                {
                    using var k = root.OpenSubKey(id, true);
                    if (k == null || k.GetValue("IsPromoted") != null) continue;
                    if (!string.Equals(k.GetValue("ExecutablePath") as string, exe, StringComparison.OrdinalIgnoreCase)) continue;
                    string tip = k.GetValue("InitialTooltip") as string ?? "";
                    foreach (var n in names)
                        if (tip.StartsWith(n, StringComparison.OrdinalIgnoreCase)) { k.SetValue("IsPromoted", 1, Microsoft.Win32.RegistryValueKind.DWord); changed = true; break; }
                }
            }
            catch { }
            return changed;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        static readonly Color Outline = Color.FromArgb(200, 10, 14, 30);

        /// <summary>Белый текст или значок с тёмной обводкой — читается и на тёмной, и на прозрачной панели над светлыми обоями.</summary>
        static void OutlinedText(Graphics g, string text, FontFamily fam, FontStyle style, float px, RectangleF box, float stroke)
        {
            using var path = new GraphicsPath();
            using var fmt = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            path.AddString(text, fam, (int)style, px, box, fmt);
            using var pen = new Pen(Outline, stroke) { LineJoin = LineJoin.Round };
            g.DrawPath(pen, path);
            g.FillPath(Brushes.White, path);
        }

        static FontFamily IconFamily()
        {
            try { return new FontFamily("Segoe Fluent Icons"); } catch { }
            try { return new FontFamily("Segoe MDL2 Assets"); } catch { }
            return FontFamily.GenericSansSerif;
        }

        static Icon Draw(DeviceBattery.Info d)
        {
            int s = Math.Max(16, GetSystemMetrics(49));   // SM_CXSMICON — размер значка трея с учётом масштаба
            float stroke = Math.Max(2f, s / 7f);
            using var bmp = new Bitmap(s, s);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                if (d.Wired)
                {
                    // Значок устройства + маленькая вилка (провод) или «сигнал» (USB-приёмник)
                    using (var fam = IconFamily())
                        OutlinedText(g, DeviceBattery.Glyph(d.Kind), fam, FontStyle.Regular, s * 0.7f, new RectangleF(-s * 0.12f, -s * 0.08f, s, s), stroke);
                    float w = Math.Max(1.4f, s / 11f), x = s * 0.78f, y = s * 0.62f;
                    foreach (var (color, width) in new[] { (Outline, w + stroke * 0.8f), (Color.FromArgb(255, 120, 200, 255), w) })
                    {
                        using var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                        if (d.Receiver)
                        {
                            g.DrawArc(pen, x - s * 0.12f, y - s * 0.12f, s * 0.24f, s * 0.24f, 200, 140);
                            g.DrawArc(pen, x - s * 0.22f, y - s * 0.22f, s * 0.44f, s * 0.44f, 205, 130);
                            using var dot = new SolidBrush(color);
                            g.FillEllipse(dot, x - width * 0.8f, y + s * 0.1f - width * 0.8f, width * 1.6f, width * 1.6f);
                        }
                        else
                        {
                            g.DrawLine(pen, x - s * 0.08f, y, x - s * 0.08f, y - s * 0.13f);
                            g.DrawLine(pen, x + s * 0.08f, y, x + s * 0.08f, y - s * 0.13f);
                            g.DrawLine(pen, x - s * 0.13f, y, x + s * 0.13f, y);
                            g.DrawLine(pen, x, y, x, y + s * 0.2f);
                        }
                    }
                }
                else
                {
                    string t = d.Percent >= 100 ? "100" : d.Percent.ToString();
                    float fs = t.Length >= 3 ? s * 0.56f : s * 0.74f;
                    using (var fam = new FontFamily("Segoe UI"))
                        OutlinedText(g, t, fam, FontStyle.Bold, fs, new RectangleF(0, -s * 0.06f, s, s * 0.84f), stroke);
                    // Полоска заряда снизу: зелёная, жёлтая, красная — на тёмной подложке
                    float bh = Math.Max(2, s * 0.14f), by = s - bh - 1f;
                    using (var back = new SolidBrush(Outline)) g.FillRectangle(back, 0.5f, by - 0.5f, s - 1, bh + 1);
                    var c = d.Percent > 40 ? Color.FromArgb(255, 74, 222, 128) : d.Percent > 15 ? Color.FromArgb(255, 250, 204, 21) : Color.FromArgb(255, 248, 113, 113);
                    using (var fill = new SolidBrush(c)) g.FillRectangle(fill, 1, by, (s - 2) * Math.Clamp(d.Percent, 4, 100) / 100f, bh);
                }
            }
            IntPtr h = bmp.GetHicon();
            var icon = (Icon)Icon.FromHandle(h).Clone();
            DestroyIcon(h);
            return icon;
        }

        public void Dispose()
        {
            foreach (var ni in _icons.Values) { ni.Visible = false; ni.Dispose(); }
            _icons.Clear();
        }
    }
}
