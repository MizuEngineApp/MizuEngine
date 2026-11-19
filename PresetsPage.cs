// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace MizuEngine
{
    /// <summary>Страница «Сборки»: сохранить настройки, своя тема из картинки, свои частицы, импорт/экспорт, отправка другу.</summary>
    internal sealed class PresetsPage
    {
        readonly Grid _host;
        readonly Settings _s;
        readonly Action<string> _toast;
        readonly Action _reapply;                 // перечитать все настройки (тема, эффекты, док)
        readonly Action<PresetInfo> _sendToFriend;

        static readonly string[] Accents = { "#FF4A97FF", "#FF7C5CFF", "#FFE056FD", "#FFFF5C8A", "#FFFF8A3D", "#FFFFC93D", "#FF3DDC84", "#FF22D3EE", "#FFFFFFFF" };

        public PresetsPage(Grid host, Settings s, Action<string> toast, Action reapply, Action<PresetInfo> sendToFriend)
        {
            _host = host;
            _s = s;
            _toast = toast;
            _reapply = reapply;
            _sendToFriend = sendToFriend;
        }

        public void Show()
        {
            _host.Children.Clear();
            var root = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 14, 20) };
            root.Children.Add(UI.H1("nav.presets"));
            var sub = UI.L("presets.sub", 14, sub: true, wrap: true);
            sub.Margin = new Thickness(0, 6, 0, 0);
            root.Children.Add(sub);

            root.Children.Add(SaveCard());
            root.Children.Add(DefaultCard());
            root.Children.Add(ThemeCard());
            root.Children.Add(ParticleCard());

            var import = UI.Glass("", "presets.import", (o, e) => Import(), 10);
            import.Margin = new Thickness(0, 16, 0, 0);
            root.Children.Add(import);

            // Сборки от MizuEngine (часть — только для Mizu Plus, они с замочком)
            if (Supa.Configured)
            {
                var oh = UI.Row(UI.L("presets.official", 16, true), UI.PlusAfter(18, 8));
                oh.Margin = new Thickness(0, 26, 0, 10);
                root.Children.Add(oh);
                var official = new StackPanel();
                official.Children.Add(UI.Loading());
                root.Children.Add(official);
                _ = PlusPage.FillOfficial(official, false, _s, _toast, _reapply);
            }

            var h2 = UI.L("presets.mine", 16, true);
            h2.Margin = new Thickness(0, 26, 0, 4);
            root.Children.Add(h2);
            var list = Presets.List();
            if (list.Count == 0)
            {
                var none = UI.L("presets.none", 14, sub: true);
                none.Margin = new Thickness(2, 8, 0, 0);
                root.Children.Add(none);
            }
            foreach (var p in list) root.Children.Add(PresetCard(p));

            _host.Children.Add(UI.Scroll(root));
        }

        Border SaveCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(UI.L("presets.save", 16, true));
            var name = UI.Field("", 60);
            name.SetResourceReference(FrameworkElement.TagProperty, "presets.name");
            name.Margin = new Thickness(0, 12, 0, 0);
            sp.Children.Add(name);
            var save = UI.Primary("presets.save_btn", (o, e) =>
            {
                var p = Presets.SaveCurrent(_s, name.Text);
                _toast(p != null ? Loc.T("presets.saved") : Loc.T("err.generic"));
                Show();
            });
            save.Margin = new Thickness(0, 12, 0, 0);
            sp.Children.Add(save);
            return UI.Card(sp, 22);
        }

        /// <summary>«Сборка по умолчанию»: фон, курсор, эффекты курсора и свои частицы возвращаются к стандартным.</summary>
        Border DefaultCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(UI.Row(UI.Squircle("\uE72C", UI.Gray, 30), Wrap(UI.L("presets.default", 16, true), 0, 10)));
            sp.Children.Add(Wrap(UI.L("presets.default.d", 13, sub: true, wrap: true), 8));
            var reset = UI.Glass("\uE72C", "presets.default_btn", (o, e) => ResetToDefault(), 10);
            reset.Margin = new Thickness(0, 14, 0, 0);
            sp.Children.Add(reset);
            return UI.Card(sp);
        }

        void ResetToDefault()
        {
            if (MessageBox.Show(Window.GetWindow(_host), Loc.T("presets.default_confirm"), "MizuEngine", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            // Сначала сохраняем текущие настройки — если сброс не понравится, их можно вернуть одной кнопкой
            Presets.SaveCurrent(_s, Loc.T("presets.backup") + " " + DateTime.Now.ToString("d MMM HH:mm"));

            var d = new Settings();
            // Фон приложения
            _s.Theme = d.Theme;
            _s.CustomThemeImage = null;
            _s.CustomAccent = d.CustomAccent;
            // Курсор
            _s.CursorPack = d.CursorPack;
            _s.CursorSize = d.CursorSize;
            _s.CustomCursor = null;
            // Эффекты курсора
            _s.CursorTrail = d.CursorTrail;
            _s.CursorScrape = d.CursorScrape;
            _s.CursorReact = d.CursorReact;
            // Свои частицы
            _s.CustomParticle = null;
            if (_s.Weather == "custom") _s.Weather = d.Weather;

            _s.Save();
            CursorPacks.Apply(_s);
            _reapply();
            _toast(Loc.T("presets.default_done"));
            Show();
        }

        Border ThemeCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(UI.L("presets.theme", 16, true));
            sp.Children.Add(Wrap(UI.L("presets.theme.d", 13, sub: true, wrap: true), 4));

            var preview = new Border { Width = 240, Height = 135, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, BorderThickness = new Thickness(1) };
            preview.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            preview.SetResourceReference(Border.BackgroundProperty, "TrackOff");
            var img = Load(_s.CustomThemeImage, 480);
            if (img != null) preview.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
            sp.Children.Add(preview);

            string pickedImage = _s.CustomThemeImage;
            string pickedAccent = _s.CustomAccent ?? Accents[0];

            var choose = UI.Glass("", "presets.pick_image", (o, e) =>
            {
                var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp" };
                if (dlg.ShowDialog() != true) return;
                pickedImage = dlg.FileName;
                var im = Load(pickedImage, 480);
                if (im != null) preview.Background = new ImageBrush(im) { Stretch = Stretch.UniformToFill };
                pickedImage = ApplyCustom(pickedImage, pickedAccent);   // сразу видно результат — и он же попадёт в сборку при сохранении
            }, 9);
            choose.Margin = new Thickness(0, 12, 0, 0);
            sp.Children.Add(choose);

            sp.Children.Add(Wrap(UI.L("presets.accent", 12, sub: true), 14));
            var swatches = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            Border selected = null;
            foreach (var hex in Accents)
            {
                string h = hex;
                var sw = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(0, 0, 10, 0), BorderThickness = new Thickness(h == pickedAccent ? 3 : 1), Cursor = System.Windows.Input.Cursors.Hand };
                sw.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h));
                sw.BorderBrush = Brushes.White;
                if (h == pickedAccent) selected = sw;
                sw.MouseLeftButtonUp += (o, e) =>
                {
                    if (selected != null) selected.BorderThickness = new Thickness(1);
                    sw.BorderThickness = new Thickness(3);
                    selected = sw;
                    pickedAccent = h;
                    if (_s.Theme == "custom" || !string.IsNullOrEmpty(pickedImage)) pickedImage = ApplyCustom(pickedImage, pickedAccent);
                };
                swatches.Children.Add(sw);
            }
            sp.Children.Add(swatches);

            var apply = UI.Primary("presets.apply_theme", (o, e) =>
            {
                if (string.IsNullOrEmpty(pickedImage) || !File.Exists(pickedImage)) { _toast(Loc.T("presets.need_image")); return; }
                pickedImage = ApplyCustom(pickedImage, pickedAccent);
                _toast(Loc.T("presets.theme_applied"));
            });
            apply.Margin = new Thickness(0, 16, 0, 0);
            sp.Children.Add(apply);
            return UI.Card(sp);
        }

        /// <summary>Включает свою тему: картинку копируем к себе (чтобы не пропала), запоминаем цвет и перекрашиваем окно.</summary>
        string ApplyCustom(string image, string accent)
        {
            if (string.IsNullOrEmpty(image) || !File.Exists(image)) return image;
            if (image != _s.CustomThemeImage) _s.CustomThemeImage = Presets.CopyToCustom(image, "theme");
            _s.CustomAccent = accent;
            _s.Theme = "custom";
            _s.FirstRunDone = true;
            _s.Save();
            _reapply();
            return _s.CustomThemeImage;   // дальше работаем с нашей копией, а не копируем картинку заново
        }

        Border ParticleCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(UI.L("presets.particles", 16, true));
            sp.Children.Add(Wrap(UI.L("presets.particles.d", 13, sub: true, wrap: true), 4));

            var preview = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            preview.SetResourceReference(Border.BackgroundProperty, "TrackOff");
            var img = Load(_s.CustomParticle, 128);
            if (img != null) preview.Child = new Image { Source = img, Margin = new Thickness(8) };
            sp.Children.Add(preview);

            var choose = UI.Primary("presets.pick_particle", (o, e) =>
            {
                var dlg = new OpenFileDialog { Filter = "PNG|*.png|Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp" };
                if (dlg.ShowDialog() != true) return;
                _s.CustomParticle = Presets.CopyToCustom(dlg.FileName, "particle");
                _s.Weather = "custom";
                _s.EffectsOn = true;
                _s.Save();
                _reapply();
                Show();
            });
            choose.Margin = new Thickness(0, 12, 0, 0);
            sp.Children.Add(choose);
            return UI.Card(sp);
        }

        Border PresetCard(PresetInfo p)
        {
            var sp = new StackPanel();
            var title = UI.Row(UI.Icon("", 16, accent: true), Wrap(UI.T(p.Name, 15, true), 0, 10));
            sp.Children.Add(title);
            string details = $"{p.Created:d MMM yyyy, HH:mm}";
            if (p.Theme != null) details += " · " + (p.Theme == "custom" ? Loc.T("presets.custom_theme") : Loc.T("theme." + p.Theme));
            if (p.Weather != null) details += " · " + (p.Weather == "custom" ? Loc.T("presets.particles") : Loc.T("w." + p.Weather));
            sp.Children.Add(Wrap(UI.T(details, 12, sub: true), 4));

            var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            var apply = UI.Primary("presets.apply", (o, e) =>
            {
                Presets.Apply(p, _s);
                _reapply();
                _toast(Loc.T("presets.applied"));
            });
            apply.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(apply);
            buttons.Children.Add(Spaced(UI.Glass("", "presets.export", (o, e) => Export(p), 8)));
            if (Supa.SignedIn) buttons.Children.Add(Spaced(UI.Glass("", "presets.send", (o, e) => _sendToFriend(p), 8)));
            buttons.Children.Add(Spaced(UI.Glass("", "presets.delete", (o, e) => { Presets.Delete(p); Show(); }, 8)));
            sp.Children.Add(buttons);
            return UI.Card(sp, 10);
        }

        void Export(PresetInfo p)
        {
            var dlg = new SaveFileDialog { Filter = "MizuEngine preset|*.mizupreset", FileName = MakeSafe(p.Name) + ".mizupreset" };
            if (dlg.ShowDialog() != true) return;
            _toast(Presets.Export(p, dlg.FileName) != null ? Loc.T("presets.exported") : Loc.T("err.generic"));
        }

        void Import()
        {
            var dlg = new OpenFileDialog { Filter = "MizuEngine preset|*.mizupreset;" + Legacy.PresetExt };
            if (dlg.ShowDialog() != true) return;
            var p = Presets.Import(dlg.FileName);
            _toast(p != null ? Loc.T("presets.imported") : Loc.T("presets.bad_file"));
            Show();
        }

        static string MakeSafe(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "preset" : name;
        }

        static FrameworkElement Wrap(FrameworkElement e, double top, double left = 0) { e.Margin = new Thickness(left, top, 0, 0); return e; }
        static FrameworkElement Spaced(FrameworkElement e) { e.Margin = new Thickness(0, 0, 8, 0); return e; }

        static ImageSource Load(string file, int width)
        {
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return null;
            try
            {
                var b = new BitmapImage();
                b.BeginInit();
                b.UriSource = new Uri(file);
                b.DecodePixelWidth = width;
                b.CacheOption = BitmapCacheOption.OnLoad;
                b.EndInit();
                b.Freeze();
                return b;
            }
            catch { return null; }
        }
    }
}
