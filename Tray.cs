// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Windows.Forms;

namespace MizuEngine
{
    /// <summary>Иконка MizuEngine в трее: открыть окно, пауза эффектов, выход. Все настройки — в главном окне.</summary>
    internal sealed class Tray : IDisposable
    {
        readonly NotifyIcon _icon;

        public Tray(Effects fx, Settings s, Action open, Action exit)
        {
            var openItem = new ToolStripMenuItem("", null, (o, e) => open());
            var pause = new ToolStripMenuItem("") { CheckOnClick = true };
            pause.Click += (o, e) =>
            {
                s.EffectsOn = !pause.Checked;
                s.Save();
                fx.Run(() => fx.Snow.Layer.Paused = !s.EffectsOn);
            };
            var exitItem = new ToolStripMenuItem("", null, (o, e) => exit());
            // Если у кого-то окно пустое (видно только размытый фон) — отсюда включается безопасный режим
            var safe = new ToolStripMenuItem("") { CheckOnClick = true };
            safe.Click += (o, e) =>
            {
                s.SafeRender = safe.Checked;
                s.Save();
                Program.Restart();
            };

            var menu = new ContextMenuStrip();
            menu.Items.AddRange(new ToolStripItem[] { openItem, pause, safe, new ToolStripSeparator(), exitItem });
            menu.Opening += (o, e) => { pause.Checked = !s.EffectsOn; safe.Checked = s.SafeRender; };   // могли поменять в главном окне

            void UpdateTexts()
            {
                openItem.Text = Loc.T("tray.open");
                pause.Text = Loc.T("tray.pause");
                exitItem.Text = Loc.T("tray.exit");
                safe.Text = Loc.T("tray.safe");
            }
            UpdateTexts();
            Loc.Changed += UpdateTexts;

            _icon = new NotifyIcon { Icon = Logo.TrayIcon(), Text = "MizuEngine", ContextMenuStrip = menu, Visible = true };
            _icon.MouseClick += (o, e) => { if (e.Button == MouseButtons.Left) open(); };
            _icon.BalloonTipClicked += (o, e) => open();
        }

        /// <summary>Уведомление Windows от значка в трее (клик по нему открывает окно).</summary>
        public void Notify(string title, string text)
        {
            try { _icon.ShowBalloonTip(6000, title, text, ToolTipIcon.Info); } catch { }
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}
