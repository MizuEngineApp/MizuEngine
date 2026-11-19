// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace MizuEngine
{
    /// <summary>Страница «Живые обои» (Mizu Plus): своё видео MP4 на рабочем столе, за ярлыками.</summary>
    internal sealed class LivePage
    {
        readonly Grid _host;
        readonly Settings _s;
        readonly Action<string> _toast;

        public LivePage(Grid host, Settings s, Action<string> toast)
        {
            _host = host; _s = s; _toast = toast;
        }

        public void Show()
        {
            _host.Children.Clear();
            _host.Children.Add(UI.Scroll(Build()));
        }

        public void Refresh()
        {
            if (_host.IsVisible) Show();
        }

        FrameworkElement Build()
        {
            var root = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 14, 24) };
            var title = UI.Row(UI.H1("nav.wallpapers"), UI.PlusAfter(26, 12, big: true));
            root.Children.Add(title);
            var sub = UI.L("live.sub", 14, sub: true, wrap: true);
            sub.Margin = new Thickness(0, 6, 0, 0);
            root.Children.Add(sub);

            if (!Plus.IsActive)
            {
                var sp = new StackPanel();
                sp.Children.Add(UI.Row(UI.PlusBadge(34, clickable: false), Pad(UI.L("live.locked", 16, true, wrap: true), 12)));
                var d = UI.L("live.locked.d", 13, sub: true, wrap: true);
                d.Margin = new Thickness(0, 10, 0, 0);
                sp.Children.Add(d);
                var go = UI.Primary("plus.learn", (s, e) => UI.OpenPlus?.Invoke());
                go.Margin = new Thickness(0, 16, 0, 0);
                sp.Children.Add(go);
                root.Children.Add(UI.Card(sp, 22));
                return root;
            }

            // ----- видео и предпросмотр -----
            var card = new StackPanel();
            var preview = new Border { Width = 320, Height = 180, CornerRadius = new CornerRadius(14), HorizontalAlignment = HorizontalAlignment.Left, ClipToBounds = true, BorderThickness = new Thickness(1) };
            preview.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            preview.Background = Brushes.Black;
            if (File.Exists(_s.LiveWallpaper))
            {
                // Маленький предпросмотр без звука; уходит вместе со страницей (UnloadedBehavior = Close)
                var me = new MediaElement { Source = new Uri(_s.LiveWallpaper), IsMuted = true, Stretch = Stretch.UniformToFill, LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close };
                me.MediaEnded += (o, e) => { me.Position = TimeSpan.Zero; me.Play(); };
                me.Loaded += (o, e) => me.Play();
                preview.Child = new Border { CornerRadius = new CornerRadius(14), ClipToBounds = true, Child = me };
            }
            else
            {
                var hint = UI.L("live.no_video", 13, sub: true, wrap: true);
                hint.HorizontalAlignment = HorizontalAlignment.Center;
                hint.VerticalAlignment = VerticalAlignment.Center;
                hint.TextAlignment = TextAlignment.Center;
                hint.Margin = new Thickness(20);
                preview.Child = hint;
            }
            card.Children.Add(preview);

            string name = File.Exists(_s.LiveWallpaper) ? Path.GetFileName(_s.LiveWallpaper) : Loc.T("plus.no_file");
            var nameT = UI.T(name, 13.5, true);
            nameT.Margin = new Thickness(0, 10, 0, 0);
            card.Children.Add(nameT);

            var pick = UI.Primary("live.pick", (s, e) =>
            {
                var dlg = new OpenFileDialog { Filter = "MP4|*.mp4;*.m4v|Video|*.mp4;*.m4v;*.wmv;*.mov" };
                if (dlg.ShowDialog() != true) return;
                try { _s.LiveWallpaper = PlusPage.CopyLiveVideo(dlg.FileName); }
                catch (Exception ex) { Settings.Log(ex); _toast(Loc.T("err.generic")); return; }
                _s.LiveWallpaperOn = true;
                _s.Save();
                LiveWallpaper.Sync(_s);
                _toast(Loc.T(LiveWallpaper.Running ? "live.applied" : "live.failed"));
                Show();
            });
            pick.Margin = new Thickness(0, 12, 0, 0);
            card.Children.Add(pick);
            root.Children.Add(UI.Card(card, 22));

            // ----- переключатели -----
            var opts = new StackPanel();
            opts.Children.Add(Switch("live.on", _s.LiveWallpaperOn, v =>
            {
                if (v && !File.Exists(_s.LiveWallpaper)) { _toast(Loc.T("live.need_video")); Show(); return; }
                _s.LiveWallpaperOn = v;
                _s.Save();
                LiveWallpaper.Sync(_s);
                if (v && !LiveWallpaper.Running) _toast(Loc.T("live.failed"));
            }));
            opts.Children.Add(Hint("live.on.d"));
            opts.Children.Add(Switch("live.all_monitors", _s.LiveAllMonitors, v => { _s.LiveAllMonitors = v; _s.Save(); LiveWallpaper.Sync(_s); }));
            opts.Children.Add(Switch("live.pause", _s.LivePauseFullscreen, v => { _s.LivePauseFullscreen = v; _s.Save(); LiveWallpaper.Sync(_s); }));
            opts.Children.Add(Hint("live.pause.d"));
            opts.Children.Add(Switch("live.sound", _s.LiveSound, v => { _s.LiveSound = v; _s.Save(); LiveWallpaper.Sync(_s); }));
            root.Children.Add(UI.Card(opts, 14));

            var note = UI.L("live.note", 12, sub: true, wrap: true);
            note.Margin = new Thickness(2, 12, 0, 0);
            root.Children.Add(note);
            Motion.Stagger(root, 2);
            return root;
        }

        static FrameworkElement Pad(FrameworkElement e, double left) { e.Margin = new Thickness(left, 0, 0, 0); e.VerticalAlignment = VerticalAlignment.Center; return e; }

        static CheckBox Switch(string key, bool on, Action<bool> changed)
        {
            var cb = new CheckBox { Style = (Style)UI.Owner.FindResource("Switch"), IsChecked = on, Margin = new Thickness(0, 6, 0, 0) };
            cb.SetResourceReference(ContentControl.ContentProperty, key);
            cb.Click += (s, e) => changed(cb.IsChecked == true);
            return cb;
        }

        static FrameworkElement Hint(string key)
        {
            var t = UI.L(key, 12, sub: true, wrap: true);
            t.Margin = new Thickness(0, 2, 70, 6);
            return t;
        }
    }
}
