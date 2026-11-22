// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace MizuEngine
{
    /// <summary>
    /// Страница «Сборки» (0.11.5): создать сборку из всего текущего оформления (с обложкой), мои сборки карточками —
    /// применить (с выбором разделов), опубликовать в «Лучшие сборки», экспорт, отправить другу; сборки от MizuEngine;
    /// своя тема из картинки и свои частицы.
    /// </summary>
    internal sealed class PresetsPage
    {
        readonly Grid _host;
        readonly Settings _s;
        readonly Action<string> _toast;
        readonly Action _reapply;                 // перечитать тему, эффекты, док (своя тема, частицы)
        readonly Action _applyEffects;            // после применения сборки: всё оформление целиком (MainWindow.ApplyBuildEffects)
        readonly Action<PresetInfo> _sendToFriend;
        readonly Action<long> _openShared;        // открыть опубликованную сборку в «Лучших сборках»

        string _newCover;                         // выбранная обложка новой сборки

        static readonly string[] Accents = { "#FF4A97FF", "#FF7C5CFF", "#FFE056FD", "#FFFF5C8A", "#FFFF8A3D", "#FFFFC93D", "#FF3DDC84", "#FF22D3EE", "#FFFFFFFF" };

        public PresetsPage(Grid host, Settings s, Action<string> toast, Action reapply, Action applyEffects, Action<PresetInfo> sendToFriend, Action<long> openShared)
        {
            _host = host;
            _s = s;
            _toast = toast;
            _reapply = reapply;
            _applyEffects = applyEffects;
            _sendToFriend = sendToFriend;
            _openShared = openShared;
        }

        StackPanel _mine;

        public void Show()
        {
            _host.Children.Clear();
            var root = new StackPanel { MaxWidth = 1060, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 14, 20) };
            root.Children.Add(UI.H1("nav.presets"));
            var sub = UI.L("builds.sub", 14, sub: true, wrap: true);
            sub.Margin = new Thickness(0, 6, 0, 0);
            sub.MaxWidth = 720;
            sub.HorizontalAlignment = HorizontalAlignment.Left;
            root.Children.Add(sub);

            root.Children.Add(CreateCard());

            var tools = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            tools.Children.Add(UI.Glass("", "presets.import", (o, e) => Import(), 9));
            var reset = UI.Glass("", "presets.default_btn", (o, e) => ResetToDefault(), 9);
            reset.Margin = new Thickness(8, 0, 0, 0);
            reset.ToolTip = Loc.T("presets.default.d");
            tools.Children.Add(reset);
            var top = UI.Glass("", "builds.open_top", (o, e) => _openShared?.Invoke(0), 9);
            top.Margin = new Thickness(8, 0, 0, 0);
            tools.Children.Add(top);
            root.Children.Add(tools);

            // ---- мои сборки ----
            var h2 = UI.L("presets.mine", 18, true);
            h2.Margin = new Thickness(0, 28, 0, 12);
            root.Children.Add(h2);
            _mine = new StackPanel();
            root.Children.Add(_mine);
            FillMine();

            // ---- сборки от MizuEngine (часть — только для Mizu Plus) ----
            if (Supa.Configured)
            {
                var oh = UI.Row(UI.L("presets.official", 18, true), UI.PlusAfter(18, 8));
                oh.Margin = new Thickness(0, 18, 0, 10);
                root.Children.Add(oh);
                var official = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
                official.Children.Add(UI.Loading());
                root.Children.Add(official);
                _ = PlusPage.FillOfficial(official, false, _s, _toast, _applyEffects);
            }

            // ---- своё оформление: тема из картинки и свои частицы ----
            var h3 = UI.L("builds.own_style", 18, true);
            h3.Margin = new Thickness(0, 28, 0, 0);
            root.Children.Add(h3);
            var own = new WrapPanel();
            var theme = ThemeCard(); theme.Width = 470; theme.Margin = new Thickness(0, 14, 14, 0);
            var part = ParticleCard(); part.Width = 470; part.Margin = new Thickness(0, 14, 14, 0);
            own.Children.Add(theme);
            own.Children.Add(part);
            root.Children.Add(own);

            _host.Children.Add(UI.Scroll(root));
        }

        // ================== создать сборку ==================

        Border CreateCard()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());

            // Слева — обложка (по умолчанию значок MizuEngine), нажатие — выбрать свою
            var coverHost = new Grid { Width = 196, Height = 150, Cursor = System.Windows.Input.Cursors.Hand, VerticalAlignment = VerticalAlignment.Top };
            var hint = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 4, 9, 5), Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)) };
            hint.Child = new TextBlock { Text = Loc.T("builds.cover_pick"), Foreground = Brushes.White, FontSize = 11.5, FontWeight = FontWeights.SemiBold };
            void PaintCover()
            {
                coverHost.Children.Clear();
                coverHost.Children.Add(BuildUi.Cover(150, new CornerRadius(18), _newCover, decode: 420));
                coverHost.Children.Add(hint);
            }
            PaintCover();
            coverHost.MouseLeftButtonUp += (o, e) =>
            {
                var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp" };
                if (dlg.ShowDialog() != true) return;
                _newCover = dlg.FileName;
                PaintCover();
            };
            coverHost.MouseRightButtonUp += (o, e) => { _newCover = null; PaintCover(); };
            coverHost.ToolTip = Loc.T("builds.cover_tip");
            g.Children.Add(coverHost);

            var right = new StackPanel { Margin = new Thickness(20, 0, 0, 0) };
            right.Children.Add(UI.L("builds.create", 18, true));
            var d = UI.L("builds.create_d", 12.5, sub: true, wrap: true);
            d.Margin = new Thickness(0, 4, 0, 0);
            right.Children.Add(d);

            // Что войдёт прямо сейчас
            var sum = Presets.CurrentSummary(_s);
            var chips = BuildUi.Chips(sum, 24);
            chips.Margin = new Thickness(0, 10, 0, 0);
            right.Children.Add(chips);

            var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = UI.Field("", 60);
            name.SetResourceReference(FrameworkElement.TagProperty, "presets.name");
            row.Children.Add(name);
            var create = UI.Primary("builds.create_btn", null);
            create.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(create, 1);
            row.Children.Add(create);
            right.Children.Add(row);

            var note = UI.L("builds.personal_note", 11.5, sub: true, wrap: true);
            note.Margin = new Thickness(2, 8, 0, 0);
            note.Opacity = 0.85;
            right.Children.Add(note);

            async Task Create()
            {
                create.IsEnabled = false;
                string n = string.IsNullOrWhiteSpace(name.Text) ? Loc.T("builds.default_name") + " " + DateTime.Now.ToString("d MMM") : name.Text.Trim();
                string cover = _newCover;
                var p = await Task.Run(() => Presets.CreateFromCurrent(_s, n, cover));
                create.IsEnabled = true;
                if (p == null) { _toast(Loc.T("err.generic")); return; }
                _newCover = null;
                name.Text = "";
                PaintCover();
                _toast(Loc.T("presets.saved"));
                FillMine(p.Folder);
            }
            create.Click += async (o, e) => await Create();
            name.KeyDown += async (o, e) => { if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; await Create(); } };

            Grid.SetColumn(right, 1);
            g.Children.Add(right);
            var card = UI.Card(g, 20);
            card.MaxWidth = 980;
            card.HorizontalAlignment = HorizontalAlignment.Left;
            return card;
        }

        // ================== мои сборки ==================

        void FillMine(string highlight = null)
        {
            if (_mine == null) return;
            _mine.Children.Clear();
            var list = Presets.List();
            if (list.Count == 0)
            {
                var none = UI.L("presets.none", 14, sub: true);
                none.Margin = new Thickness(2, 0, 0, 0);
                _mine.Children.Add(none);
                return;
            }
            var wrap = new WrapPanel();
            // Сначала обычные, резервные копии — в конце
            foreach (var p in list.Where(x => !x.Backup).Concat(list.Where(x => x.Backup)))
            {
                var card = MineCard(p);
                wrap.Children.Add(card);
                if (p.Folder == highlight) Motion.Pop(card, 0.92);
            }
            _mine.Children.Add(wrap);
            Motion.Stagger(wrap);
        }

        Border MineCard(PresetInfo p)
        {
            var subtitle = new WrapPanel();
            subtitle.Children.Add(UI.T($"{p.Created:d MMM yyyy, HH:mm}", 12, sub: true));
            if (p.Backup) subtitle.Children.Add(Tag(Loc.T("builds.tag_backup"), false));
            if (p.PublishedId > 0) subtitle.Children.Add(Tag(Loc.T("builds.tag_published"), true));

            var buttons = new WrapPanel();
            var apply = UI.Primary("presets.apply", (o, e) => ApplyLocal(p));
            buttons.Children.Add(apply);
            if (p.PublishedId > 0)
            {
                var open = UI.Glass("", "builds.open", (o, e) => _openShared?.Invoke(p.PublishedId), 8);
                open.Margin = new Thickness(8, 0, 0, 0);
                buttons.Children.Add(open);
            }
            else if (!p.Backup && Supa.Configured)
            {
                var pub = UI.Glass("", "builds.publish", (o, e) => BuildUi.PublishDialog(_host, p, _toast, id => FillMine(p.Folder)), 8);
                pub.Margin = new Thickness(8, 0, 0, 0);
                buttons.Children.Add(pub);
            }
            var more = UI.Glass("", null, null, 8);
            more.Margin = new Thickness(8, 0, 0, 0);
            more.ToolTip = Loc.T("builds.more");
            more.Click += (o, e) => MoreMenu(p, more);
            buttons.Children.Add(more);

            var cover = BuildUi.Cover(150, new CornerRadius(19, 19, 0, 0), p.Cover);
            return BuildUi.Card(cover, p.Name, p.Plus, subtitle, p.Summary, buttons, click: () => ApplyLocal(p));
        }

        static Border Tag(string text, bool accent)
        {
            var t = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold };
            var b = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 1, 7, 2), Margin = new Thickness(8, 0, 0, 0), Child = t, VerticalAlignment = VerticalAlignment.Center };
            if (accent)
            {
                b.SetResourceReference(Border.BackgroundProperty, "Accent");
                t.Foreground = Brushes.White;
            }
            else
            {
                b.SetResourceReference(Border.BackgroundProperty, "HoverBg");
                t.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
            }
            return b;
        }

        void MoreMenu(PresetInfo p, Button anchor)
        {
            var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            MenuItem Item(string key, Action a) { var mi = new MenuItem { Header = Loc.T(key) }; mi.Click += (o, e) => a(); menu.Items.Add(mi); return mi; }
            Item("builds.rename", () => BuildUi.Prompt(_host, Loc.T("builds.rename"), p.Name, 60, n => { Presets.Rename(p, n); FillMine(); }));
            Item("builds.cover_change", () =>
            {
                var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp" };
                if (dlg.ShowDialog() != true) return;
                Presets.SetCover(p, dlg.FileName);
                FillMine(p.Folder);
            });
            if (p.Cover != null) Item("builds.cover_icon", () => { Presets.SetCover(p, null); FillMine(p.Folder); });
            Item("presets.export", () => Export(p));
            if (Supa.SignedIn) Item("presets.send", () => _sendToFriend(p));
            menu.Items.Add(new Separator());
            Item("presets.delete", () => BuildUi.Confirm(_host, string.Format(Loc.T("builds.delete_q"), p.Name),
                p.PublishedId > 0 ? Loc.T("builds.delete_local_d") : null, "presets.delete", () =>
                {
                    Presets.Delete(p);
                    FillMine();
                    return Task.CompletedTask;
                }));
            menu.IsOpen = true;
        }

        /// <summary>Применить свою сборку: окно с разделами → резервная копия → настройки → всё оформление перечитывается.</summary>
        void ApplyLocal(PresetInfo p)
        {
            BuildUi.ApplyDialog(_host, p.Name, p.Summary, async parts =>
            {
                await Task.Run(() => Presets.Backup(_s));
                Presets.Apply(p, _s, parts);
                _applyEffects();
                _toast(Loc.T("presets.applied"));
                FillMine();
            });
        }

        /// <summary>«Сборка по умолчанию»: фон, курсор, эффекты курсора и свои частицы возвращаются к стандартным.</summary>
        void ResetToDefault()
        {
            BuildUi.Confirm(_host, Loc.T("presets.default"), Loc.T("presets.default_confirm"), "presets.default_btn", async () =>
            {
                // Сначала сохраняем текущие настройки — если сброс не понравится, их можно вернуть одной кнопкой
                await Task.Run(() => Presets.Backup(_s));

                var d = new Settings();
                _s.Theme = d.Theme;
                _s.CustomThemeImage = null;
                _s.CustomAccent = d.CustomAccent;
                _s.CursorPack = d.CursorPack;
                _s.CursorSize = d.CursorSize;
                _s.CustomCursor = null;
                _s.CursorTrail = d.CursorTrail;
                _s.CursorScrape = d.CursorScrape;
                _s.CursorReact = d.CursorReact;
                _s.CustomParticle = null;
                if (_s.Weather == "custom") _s.Weather = d.Weather;

                _s.Save();
                CursorPacks.Apply(_s);
                _reapply();
                _toast(Loc.T("presets.default_done"));
                FillMine();
            });
        }

        // ================== своя тема и свои частицы ==================

        Border ThemeCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(UI.L("presets.theme", 16, true));
            sp.Children.Add(Wrap(UI.L("presets.theme.d", 13, sub: true, wrap: true), 4));

            var preview = new Border { Width = 240, Height = 135, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, BorderThickness = new Thickness(1) };
            preview.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            preview.SetResourceReference(Border.BackgroundProperty, "TrackOff");
            var img = BuildFiles.Thumb(_s.CustomThemeImage, 480);
            if (img != null) preview.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
            sp.Children.Add(preview);

            string pickedImage = _s.CustomThemeImage;
            string pickedAccent = _s.CustomAccent ?? Accents[0];

            var choose = UI.Glass("\uEB9F", "presets.pick_image", (o, e) =>
            {
                var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp" };
                if (dlg.ShowDialog() != true) return;
                pickedImage = dlg.FileName;
                var im = BuildFiles.Thumb(pickedImage, 480);
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
            var img = BuildFiles.Thumb(_s.CustomParticle, 128);
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

        // ================== файлы ==================

        void Export(PresetInfo p)
        {
            var dlg = new SaveFileDialog { Filter = "MizuEngine preset|*.mizupreset", FileName = MakeSafe(p.Name) + ".mizupreset" };
            if (dlg.ShowDialog() != true) return;
            string file = dlg.FileName;
            _ = Task.Run(() => Presets.Export(p, file)).ContinueWith(t =>
                _toast(t.Result != null ? Loc.T("presets.exported") : Loc.T("err.generic")), TaskScheduler.FromCurrentSynchronizationContext());
        }

        void Import()
        {
            var dlg = new OpenFileDialog { Filter = "MizuEngine preset|*.mizupreset;" + Legacy.PresetExt };
            if (dlg.ShowDialog() != true) return;
            string file = dlg.FileName;
            _ = Task.Run(() => Presets.Import(file)).ContinueWith(t =>
            {
                _toast(t.Result != null ? Loc.T("presets.imported") : Loc.T("presets.bad_file"));
                FillMine(t.Result?.Folder);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        internal static string MakeSafe(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "preset" : name;
        }

        static FrameworkElement Wrap(FrameworkElement e, double top, double left = 0) { e.Margin = new Thickness(left, top, 0, 0); return e; }
    }
}
