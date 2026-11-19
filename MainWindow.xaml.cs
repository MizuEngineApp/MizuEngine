// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Path = System.Windows.Shapes.Path;

namespace MizuEngine
{
    public partial class MainWindow : Window
    {
        internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        readonly Effects _fx;
        readonly Settings _s;
        readonly DispatcherTimer _dockSave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        bool _loading = true;
        SocialPages _social;
        PresetsPage _presets;
        CustomPage _custom;
        AnimPage _animPage;
        int _navIndex;
        readonly DispatcherTimer _avatarTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(3) };
        readonly FrameworkElement[] _pages;
        readonly DispatcherTimer _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
        readonly DispatcherTimer _copyReset = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
        readonly System.Windows.Media.Effects.BlurEffect _cardBlur = new System.Windows.Media.Effects.BlurEffect { Radius = 7 };
        public bool AllowClose;
        bool _weLoaded, _closing;
        Border _weSelected;
        int _weCount = -1;
        FrameworkElement _currentPage;

        internal MainWindow(Effects fx, Settings settings)
        {
            _fx = fx;
            _s = settings;
            InitializeComponent();
            UI.Owner = this;
            Motion.Layer = FlyLayer;
            _pages = new FrameworkElement[] { PageHome, PageSnow, PageDock, PageWe, PageSoon, PageSettings, PageAbout, PageAuthor,
                                              PagePresets, PageProfile, PageFriends, PageChats, PageCommunity, PageLeaders, PageCustom, PageAnim, PageUpdates,
                                              PagePlus, PageLive, PageLiveBg, PageMuzule };
            _social = new SocialPages(PageProfile, PageFriends, PageChats, PageCommunity, ShowToast, ApplyPreset, SocialNavigate);
            _social.AttachLeaders(PageLeaders);
            SetupBrand();
            _presets = new PresetsPage(PagePresets, _s, ShowToast, ReapplyAll, p => _ = SendPresetToFriend(p));
            _custom = new CustomPage(PageCustom, _s, _fx, ShowToast);
            _animPage = new AnimPage(PageAnim, _s);
            _currentPage = PageHome;

            SourceInitialized += (o, e) =>
            {
                Themes.Apply(_s.Theme, this);
                // Своё плавное появление вместо системного: системное сначала показывает окно размытым и чуть ниже,
                // потом оно «прыгает» на место и моргает. Без него окно сразу стоит ровно и просто проявляется.
                int off = 1;
                Native.DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 3, ref off, 4);   // DWMWA_TRANSITIONS_FORCEDISABLED
            };
            Root.Opacity = 0;   // до первого кадра окно прозрачное — никакой вспышки перед анимацией
            InitPlus();   // вкладки Mizu Plus и «Живые обои», фон из видео и живые темы (MainWindow.Plus.cs)
            InitMuzule(); // вкладка Muzule — Claude и агенты (MainWindow.Muzule.cs)
            StateChanged += (o, e) =>
            {
                bool max = WindowState == WindowState.Maximized;
                Shell.Margin = new Thickness(max ? 7 : 0);   // без этого развёрнутое окно без рамки вылезает за экран
                MaxBtn.Content = max ? "" : "";
            };
            IsVisibleChanged += (o, e) => { if (IsVisible) PlayOpen(); };
            Loaded += (o, e) => MoveHighlight(NavHome, false);
            Activated += (o, e) => SetHeroFloat(_currentPage == PageHome);
            Deactivated += (o, e) => SetHeroFloat(false);
            StateChanged += (o, e) => SetHeroFloat(_currentPage == PageHome);
            NavList.SizeChanged += (o, e) => MoveHighlight(CheckedNav(), false);   // язык поменялся — пункты могли сменить высоту
            Loc.Changed += UpdateWeCount;
            Loc.Changed += UpdateCardMask;
            UpdateCardMask();
            LoadBrandIcons();
            _copyReset.Tick += (o, e) => ResetCopyPill();
            MizuHotkeys.Toggled += OnHotkeyToggled;
            Updater.Changed += OnUpdaterChanged;
            Loc.Changed += RefreshUpdateUi;
            Closed += (o, e) =>
            {
                Updater.Changed -= OnUpdaterChanged;
                Loc.Changed -= RefreshUpdateUi;
                StopGateAnim();
                // окно удаляется при закрытии — не держим ссылок на него
                Loc.Changed -= UpdateWeCount;
                Loc.Changed -= UpdateCardMask;
                _copyReset.Stop();
                MizuHotkeys.Toggled -= OnHotkeyToggled;
                Themes.Changed -= PaintBrand;
                _social.Detach();
                _avatarTimer.Stop();
            };
            _avatarTimer.Tick += async (o, e) => await RefreshAuthorPhoto();
            _toastTimer.Tick += (o, e) => HideToast();
            _dockSave.Tick += (o, e) => { _dockSave.Stop(); _s.Save(); };

            foreach (var sv in new[] { PageHome, PageSnow, PageDock, PageSettings, PageAuthor, PageAbout, PageUpdates, WeScroll, NavScroll })
                SmoothScroll.Attach(sv);

            FillThemeTiles(HomeThemes);
            FillThemeTiles(SettingsThemes);
            FillThemeTiles(FirstThemes);
            FillLanguages();
            LoadSnowControls();
            LoadDockControls();
            Autostart.IsChecked = IsAutostart();
            VersionText.Text = "v" + Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
            var badge = UI.AnimatedBadge(28, "black");   // как в профиле: чёрная галочка владельца
            badge.Margin = new Thickness(10, 4, 0, 0);
            AuthorNameRow.Children.Add(badge);
            OnUpdaterChanged();   // обновление нашлось, пока окно было закрыто — плашка сразу на месте
            InitMessages();       // цифра непрочитанных на «Чатах», уведомления в окне, звуки сообщений (MainWindow.Sounds.cs)
            InitSettingsPage();   // вкладки «Настроек» (MainWindow.Settings.cs)

            Loaded += (o, e) =>
            {
                SetHeroFloat(true);
                var cached = TelegramAvatar.Cached();
                if (cached != null) SetAuthorPhoto(cached);
                _ = RefreshAuthorPhoto();
                _avatarTimer.Start();
            };
            _loading = false;

            if (!_s.FirstRunDone)
            {
                FirstRun.Visibility = Visibility.Visible;
                Anim.To(FirstRun, OpacityProperty, 1, 400, from: 0);
                var sc = (ScaleTransform)FirstCard.RenderTransform;
                Anim.To(sc, ScaleTransform.ScaleXProperty, 1, 600, from: 0.92, delayMs: 80, ease: Anim.Spring);
                Anim.To(sc, ScaleTransform.ScaleYProperty, 1, 600, from: 0.92, delayMs: 80, ease: Anim.Spring);
            }
        }

        // ================== открытие / закрытие окна ==================

        void PlayOpen()
        {
            _closing = false;
            var sc = (ScaleTransform)Root.RenderTransform;
            // Только проявление, без увеличения: при масштабе текст на мгновение размывается и окно будто сдвигается
            Anim.Set(sc, ScaleTransform.ScaleXProperty, 1);
            Anim.Set(sc, ScaleTransform.ScaleYProperty, 1);
            Anim.To(Root, OpacityProperty, 1, 240, from: Root.Opacity < 0.5 ? Root.Opacity : 0);
            // Страховка: если на этом ПК анимации не идут (слабая видеокарта, удалённый рабочий стол),
            // интерфейс всё равно появится через секунду, а не останется прозрачным
            var guard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.1) };
            guard.Tick += (o, e) =>
            {
                guard.Stop();
                if (_closing || Root.Opacity > 0.98) return;
                Anim.Set(Root, OpacityProperty, 1);
                Anim.Set(sc, ScaleTransform.ScaleXProperty, 1);
                Anim.Set(sc, ScaleTransform.ScaleYProperty, 1);
            };
            guard.Start();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // Крестик плавно прячет окно в трей, а снег и док продолжают работать. Полный выход — через трей.
            // В «Настройки → Окно» можно сделать, чтобы крестик закрывал MizuEngine полностью.
            if (!AllowClose && !_s.CloseToTray)
            {
                e.Cancel = true;
                Dispatcher.BeginInvoke(new Action(Program.Quit));
                return;
            }
            if (!AllowClose)
            {
                e.Cancel = true;
                if (_closing) return;
                _closing = true;
                var sc = (ScaleTransform)Root.RenderTransform;
                Anim.To(sc, ScaleTransform.ScaleXProperty, 0.985, 180);
                Anim.To(sc, ScaleTransform.ScaleYProperty, 0.985, 180);
                // После анимации окно закрывается по-настоящему и освобождает память; из трея оно создаётся заново
                Anim.To(Root, OpacityProperty, 0, 200, done: () => { if (_closing) { AllowClose = true; Close(); } });
            }
            base.OnClosing(e);
        }

        void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        void Max_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        void Close_Click(object sender, RoutedEventArgs e) => Close();

        // ================== надпись «MizuEngine» слева сверху ==================

        LinearGradientBrush _brand;

        /// <summary>Надпись мягко переливается: градиент тонов темы медленно едет по буквам (30 кадров в секунду — почти без нагрузки).</summary>
        void SetupBrand()
        {
            _brand = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5),
                SpreadMethod = GradientSpreadMethod.Repeat, MappingMode = BrushMappingMode.RelativeToBoundingBox
            };
            for (int i = 0; i < 5; i++) _brand.GradientStops.Add(new GradientStop(Colors.White, i / 4.0));
            var move = new TranslateTransform();
            _brand.RelativeTransform = move;
            BrandText.Foreground = _brand;
            var slide = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromSeconds(4.5)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
            System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(slide, 30);
            move.BeginAnimation(TranslateTransform.XProperty, slide);
            Themes.Changed += PaintBrand;
            PaintBrand();
        }

        /// <summary>Цвета перелива — из цвета темы: светлый тон → цвет темы → глубокий тон. При смене темы плавно перетекают.</summary>
        void PaintBrand()
        {
            if (_brand == null) return;
            var accBrush = TryFindResource("Accent");
            Color acc = accBrush is SolidColorBrush sb ? sb.Color
                      : accBrush is GradientBrush gb && gb.GradientStops.Count > 0 ? gb.GradientStops[0].Color
                      : Color.FromRgb(0x4A, 0x97, 0xFF);
            Color text = TryFindResource("Text") is SolidColorBrush tb ? tb.Color : Colors.White;
            bool lightTheme = (0.299 * text.R + 0.587 * text.G + 0.114 * text.B) / 255 < 0.5;   // тёмный текст — значит, светлая тема
            Color light = UI.Mix(acc, Colors.White, lightTheme ? 0.12 : 0.6);
            Color deep = UI.Mix(acc, Color.FromRgb(0x0E, 0x2A, 0x8C), lightTheme ? 0.55 : 0.35);
            Color[] cols = { light, acc, deep, acc, light };
            for (int i = 0; i < cols.Length; i++)
            {
                var c = cols[i];
                c.A = 255;
                _brand.GradientStops[i].BeginAnimation(GradientStop.ColorProperty,
                    new System.Windows.Media.Animation.ColorAnimation(c, TimeSpan.FromMilliseconds(IsLoaded ? 600 : 1)));
            }
        }

        // ================== навигация ==================

        void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (_pages == null) return;   // срабатывает ещё во время загрузки XAML
            var nav = (RadioButton)sender;
            string n = nav.Name;
            MoveHighlight(nav, true);

            FrameworkElement page =
                n == "NavSnow" ? PageSnow :
                n == "NavDock" ? PageDock :
                n == "NavWe" ? PageWe :
                n == "NavWallpapers" ? PageLive :
                n == "NavLiveBg" ? PageLiveBg :
                n == "NavPlus" ? PagePlus :
                n == "NavSettings" ? PageSettings :
                n == "NavAbout" ? PageAbout :
                n == "NavAuthor" ? PageAuthor :
                n == "NavPresets" ? PagePresets :
                n == "NavProfile" ? PageProfile :
                n == "NavFriends" ? PageFriends :
                n == "NavChats" ? PageChats :
                n == "NavCommunity" ? PageCommunity :
                n == "NavLeaders" ? PageLeaders :
                n == "NavCustom" ? PageCustom :
                n == "NavAnim" ? PageAnim :
                n == "NavUpdates" ? PageUpdates :
                n == "NavMuzule" ? PageMuzule : (FrameworkElement)PageHome;

            int idx = NavList.Children.IndexOf(nav);
            int dir = Math.Sign(idx - _navIndex);
            _navIndex = idx;

            if (n == "NavSnow") SnowOn.IsChecked = _s.EffectsOn;   // паузу могли поставить из трея
            if (n == "NavWe" && !_weLoaded) LoadWe();
            if (n != "NavChats") _social.HideChats();
            OnPageShown(n);
            ShowPage(page, dir);
        }

        void ShowPage(FrameworkElement page, int dir)
        {
            if (page == _currentPage) return;
            var old = _currentPage;
            _currentPage = page;

            // Старая страница уходит, новая появляется — как именно, выбирается на странице «Анимации»
            old.IsHitTestVisible = false;
            Motion.Exit(old, dir, () => { if (_currentPage != old) old.Visibility = Visibility.Collapsed; old.IsHitTestVisible = true; });
            page.IsHitTestVisible = true;
            Motion.Enter(page, dir, Motion.Ms(60));
            SetHeroFloat(page == PageHome);
        }

        /// <summary>Эффекты/док переключили горячей клавишей — обновляем переключатели в окне.</summary>
        void OnHotkeyToggled()
        {
            _loading = true;
            LoadSnowControls();
            LoadDockControls();
            _loading = false;
            _custom.Refresh();
        }

        /// <summary>Плитка с N на главной медленно «парит». Анимация работает только когда главная видна — иначе зря грузит видеокарту.</summary>
        void SetHeroFloat(bool on)
        {
            var t = (TranslateTransform)Hero.RenderTransform;
            // Окно неактивно или свёрнуто — плитка не «парит»: бесконечная анимация заставляла перерисовывать
            // всё стеклянное окно каждый кадр, и на главной интерфейс подвисал
            on = on && IsActive && WindowState != WindowState.Minimized && !Themes.SafeMode;
            if (on == _heroOn) return;
            _heroOn = on;
            if (!on) { t.BeginAnimation(TranslateTransform.YProperty, null); return; }
            var a = new System.Windows.Media.Animation.DoubleAnimation(5, -7, TimeSpan.FromSeconds(3.2))
            {
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            };
            System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(a, 30);   // медленное парение — 30 кадров хватает, нагрузка вдвое меньше
            t.BeginAnimation(TranslateTransform.YProperty, a, System.Windows.Media.Animation.HandoffBehavior.SnapshotAndReplace);
        }

        bool _heroOn;

        void MoveHighlight(RadioButton nav, bool animate)
        {
            if (nav == null || !nav.IsLoaded) return;
            double y = nav.TranslatePoint(new Point(0, 0), NavGrid).Y;
            var t = (TranslateTransform)NavHighlight.RenderTransform;
            if (animate)
            {
                Anim.To(t, TranslateTransform.YProperty, y, 480);
                Anim.To(NavHighlight, HeightProperty, nav.ActualHeight, 480);
            }
            else
            {
                Anim.Set(t, TranslateTransform.YProperty, y);
                Anim.Set(NavHighlight, HeightProperty, nav.ActualHeight);
            }
        }

        RadioButton CheckedNav()
        {
            foreach (var child in NavList.Children)
                if (child is RadioButton rb && rb.IsChecked == true) return rb;
            return NavHome;
        }

        /// <summary>Содержимое страниц соцсети и сборок строится при каждом открытии — так данные всегда свежие.</summary>
        void OnPageShown(string nav)
        {
            switch (nav)
            {
                case "NavPresets": _presets.Show(); break;
                case "NavProfile": _ = Safe(PageProfile, _social.OnProfileNav); break;
                case "NavFriends": _ = Safe(PageFriends, _social.OnFriendsNav); break;
                case "NavChats": _ = Safe(PageChats, _social.ShowChats); break;
                case "NavCommunity": _ = Safe(PageCommunity, _social.ShowCommunity); break;
                case "NavLeaders": _ = Safe(PageLeaders, _social.ShowLeaders); break;
                case "NavPlus": _plusPage.Show(); break;
                case "NavWallpapers": _livePage.Show(); break;
                case "NavLiveBg": _liveBgPage.Show(); break;
                case "NavMuzule": _muzulePage.Show(); break;
                case "NavCustom": _custom.Show(); break;
                case "NavAnim": _animPage.Show(); break;
                case "NavAuthor": _ = RefreshAuthorPhoto(); break;
            }
        }

        /// <summary>Если страница упала с ошибкой — не оставляем пустоту: пишем причину на странице и в error.log.</summary>
        static async Task Safe(Grid host, Func<Task> work)
        {
            try { await work(); }
            catch (Exception ex)
            {
                Settings.Log(ex);
                host.Children.Clear();
                var sp = new StackPanel { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
                sp.Children.Add(UI.L("err.generic", 18, true));
                sp.Children.Add(UI.T(ex.GetType().Name + ": " + ex.Message, 12.5, sub: true, wrap: true));
                host.Children.Add(UI.Card(sp, 0));
            }
        }

        void SocialNavigate(string target)
        {
            if (target == "sounds")
            {
                // Кнопка 🔊 в «Чатах»: открыть «Настройки» на разделе со звуками сообщений
                NavSettings.IsChecked = true;
                ShowSettingsTab("sound");
                return;
            }
            var nav = target == "profile" ? NavProfile : target == "friends" ? NavFriends : target == "community" ? NavCommunity : NavChats;
            if (nav.IsChecked == true) OnPageShown(nav.Name);   // уже на этой странице — просто перестраиваем
            else nav.IsChecked = true;
        }

        // ================== сборки ==================

        void ApplyPreset(PresetInfo p)
        {
            Presets.Apply(p, _s);
            ReapplyAll();
            ShowToast(Loc.T("presets.applied"));
        }

        /// <summary>После сборки или своей темы: заново применяем тему, эффекты и док, обновляем переключатели.</summary>
        void ReapplyAll()
        {
            Themes.CustomImage = _s.CustomThemeImage;
            Themes.CustomAccent = _s.CustomAccent;
            Themes.VideoFile = _s.VideoTheme;
            CrossFade(() => { Themes.Apply(_s.Theme, this); UpdateBackdropLayer(); });
            CursorPacks.Apply(_s);   // в сборке может быть и курсор
            _fx.Run(() =>
            {
                _s.ApplyTo(_fx.Snow.Layer);
                _fx.Dock.ApplySettings();
                try { _fx.Overlays.Apply(); } catch (Exception e) { Settings.Log(e); }   // рамки окон из сборки
            });
            _loading = true;
            LoadSnowControls();
            LoadDockControls();
            _loading = false;
        }

        async Task SendPresetToFriend(PresetInfo p)
        {
            if (!Supa.SignedIn) return;
            var friends = await SocialApi.Friends(Supa.Me);
            var menu = new ContextMenu();
            if (friends.Count == 0) menu.Items.Add(new MenuItem { Header = Loc.T("chats.no_friends"), IsEnabled = false });
            foreach (var f in friends)
            {
                var fr = f;
                var item = new MenuItem { Header = $"{fr.Nickname}  @{fr.Handle}" };
                item.Click += async (o, e) =>
                {
                    ShowToast(Loc.T("profile.uploading"));
                    string err = await SocialApi.SendPreset(fr.Id, p);
                    ShowToast(err ?? Loc.T("presets.sent"));
                };
                menu.Items.Add(item);
            }
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.IsOpen = true;
        }

        // ================== аватарка автора ==================

        async Task RefreshAuthorPhoto()
        {
            var img = await TelegramAvatar.RefreshAsync();
            if (img != null) SetAuthorPhoto(img);
        }

        void SetAuthorPhoto(ImageSource img)
        {
            AuthorPhoto.Fill = new ImageBrush(img is BitmapSource bs ? UI.Square(bs) : img) { Stretch = Stretch.Fill };
            if (AuthorPhoto.Visibility != Visibility.Visible)
            {
                AuthorPhoto.Visibility = Visibility.Visible;
                Anim.To(AuthorPhoto, OpacityProperty, 1, 500, from: 0);
            }
        }

        void Go_Click(object sender, RoutedEventArgs e) =>
            ((RadioButton)FindName((string)((Button)sender).Tag)).IsChecked = true;

        // ================== плавная смена темы / языка ==================

        /// <summary>Делает снимок окна, применяет изменение и плавно растворяет снимок — цвета «перетекают».</summary>
        void CrossFade(Action change)
        {
            double w = Root.ActualWidth, h = Root.ActualHeight;
            if (!IsVisible || w < 1 || h < 1) { change(); return; }

            var dpi = VisualTreeHelper.GetDpi(this);
            var rtb = new RenderTargetBitmap((int)Math.Ceiling(w * dpi.DpiScaleX), (int)Math.Ceiling(h * dpi.DpiScaleY),
                96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) dc.DrawRectangle(new VisualBrush(Root), null, new Rect(0, 0, w, h));
            rtb.Render(dv);

            FadeShot.Source = rtb;
            FadeShot.Visibility = Visibility.Visible;
            change();
            Anim.To(FadeShot, OpacityProperty, 0, 520, from: 1, done: () => { FadeShot.Visibility = Visibility.Collapsed; FadeShot.Source = null; });
        }

        // ================== темы ==================

        void FillThemeTiles(Panel panel)
        {
            foreach (var theme in Themes.All)
            {
                var logo = new Path
                {
                    Data = (Geometry)FindResource("LogoM"), Fill = (Brush)FindResource("LogoBrush"),
                    Width = 26, Height = 28, Stretch = Stretch.Uniform
                };
                var swatch = new Border
                {
                    Width = 150, Height = 88, CornerRadius = new CornerRadius(12), Background = Themes.Preview(theme),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 255, 255, 255)), BorderThickness = new Thickness(1),
                    Child = logo
                };
                var name = new TextBlock { Margin = new Thickness(2, 8, 0, 0), FontWeight = FontWeights.SemiBold };
                name.SetResourceReference(TextBlock.TextProperty, "theme." + theme);

                var tile = new Button
                {
                    Style = (Style)FindResource("GlassButton"), Padding = new Thickness(10), Margin = new Thickness(0, 0, 12, 12),
                    Content = new StackPanel { Children = { swatch, name } }
                };
                tile.Click += (o, e) => PickTheme(theme);
                panel.Children.Add(tile);
            }
        }

        void PickTheme(string theme)
        {
            _s.Theme = theme;
            bool first = !_s.FirstRunDone;
            _s.FirstRunDone = true;
            _s.Save();
            CrossFade(() => { Themes.Apply(theme, this); UpdateBackdropLayer(); });

            if (first)
                Anim.To(FirstRun, OpacityProperty, 0, 350, done: () => FirstRun.Visibility = Visibility.Collapsed);
        }

        // ================== язык ==================

        void FillLanguages()
        {
            foreach (var (code, native) in Loc.Languages)
            {
                var pill = new RadioButton { Style = (Style)FindResource("Pill"), GroupName = "lang", IsChecked = _s.Language == code };
                if (code == "system") pill.SetResourceReference(ContentProperty, "lang.system");
                else pill.Content = native;   // название языка на нём самом, чтобы его нашёл тот, кто его знает
                pill.Click += (o, e) =>
                {
                    if (_s.Language == code) return;
                    _s.Language = code;
                    _s.Save();
                    CrossFade(() => Loc.Apply(code));
                };
                LangPanel.Children.Add(pill);
            }
        }

        // ================== эффекты ==================

        void LoadSnowControls()
        {
            SnowOn.IsChecked = _s.EffectsOn;
            OnWindows.IsChecked = _s.SnowOnWindows;
            Ground.IsChecked = _s.GroundDrift;
            Scrape.IsChecked = _s.CursorScrape;
            Trail.IsChecked = _s.CursorTrail;
            React.IsChecked = _s.CursorReact;
            foreach (RadioButton level in Levels.Children)
                level.IsChecked = (string)level.Tag == _s.Flakes.ToString();
            foreach (RadioButton w in WeatherPanel.Children)
                w.IsChecked = (string)w.Tag == _s.Weather;
        }

        void SnowOn_Click(object sender, RoutedEventArgs e)
        {
            _s.EffectsOn = SnowOn.IsChecked == true;
            SaveSnow();
        }

        void Weather_Click(object sender, RoutedEventArgs e)
        {
            _s.Weather = (string)((RadioButton)sender).Tag;
            SaveSnow();
        }

        void Level_Click(object sender, RoutedEventArgs e)
        {
            _s.Flakes = int.Parse((string)((RadioButton)sender).Tag);
            SaveSnow();
        }

        void SnowOption_Click(object sender, RoutedEventArgs e)
        {
            _s.SnowOnWindows = OnWindows.IsChecked == true;
            _s.GroundDrift = Ground.IsChecked == true;
            _s.CursorScrape = Scrape.IsChecked == true;
            _s.CursorTrail = Trail.IsChecked == true;
            _s.CursorReact = React.IsChecked == true;
            SaveSnow();
        }

        void Clear_Click(object sender, RoutedEventArgs e) => _fx.Run(() => _fx.Snow.Layer.ClearDrifts());

        void SaveSnow()
        {
            _s.Save();
            _fx.Run(() => _s.ApplyTo(_fx.Snow.Layer));
        }

        // ================== док ==================

        void LoadDockControls()
        {
            DockOn.IsChecked = _s.DockEnabled;
            DockClockSwitch.IsChecked = _s.DockClock;
            DockBlurSwitch.IsChecked = _s.DockBlur;
            DockAutohideSwitch.IsChecked = _s.DockAutohide;
            DockAnimSwitch.IsChecked = _s.DockAnimations;
            DockTraySwitch.IsChecked = _s.DockTray;
            DockAllMonSwitch.IsChecked = _s.DockAllMonitors;
            FillDockMonitors();
            IslandOnSwitch.IsChecked = _s.IslandOn;
            IslandMediaSwitch.IsChecked = _s.IslandMedia;
            IslandNotifySwitch.IsChecked = _s.IslandNotify;
            IslandHideSwitch.IsChecked = _s.IslandHideIdle;
            IslandFlyUp.IsChecked = _s.IslandFly == "up";
            IslandFlyDown.IsChecked = _s.IslandFly != "up";
            DockSizeSlider.Value = _s.DockSize;
            DockMagSlider.Value = _s.DockMagnify * 100;
            DockGlassSlider.Value = _s.DockTransparency;
            UpdateSliderLabels();
        }

        void UpdateSliderLabels()
        {
            DockSizeValue.Text = $"{(int)DockSizeSlider.Value} px";
            DockMagValue.Text = DockMagSlider.Value < 101 ? "—" : $"{(int)DockMagSlider.Value}%";
            DockGlassValue.Text = $"{(int)DockGlassSlider.Value}%";
        }

        void DockOn_Click(object sender, RoutedEventArgs e)
        {
            _s.DockEnabled = DockOn.IsChecked == true;
            _s.Save();
            bool on = _s.DockEnabled;
            _fx.Run(() => _fx.Dock.SetEnabled(on));
        }

        void DockSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || DockSizeValue == null) return;
            _s.DockSize = (int)Math.Round(DockSizeSlider.Value);
            _s.DockMagnify = Math.Round(DockMagSlider.Value) / 100.0;
            _s.DockTransparency = (int)Math.Round(DockGlassSlider.Value);
            UpdateSliderLabels();
            _fx.Run(() => _fx.Dock.ApplySettings());   // док меняется сразу, пока тянешь ползунок
            _dockSave.Stop();                          // а в файл сохраняем, когда отпустили
            _dockSave.Start();
        }

        void DockOption_Click(object sender, RoutedEventArgs e)
        {
            _s.DockClock = DockClockSwitch.IsChecked == true;
            _s.DockBlur = DockBlurSwitch.IsChecked == true;
            _s.DockAutohide = DockAutohideSwitch.IsChecked == true;
            _s.DockAnimations = DockAnimSwitch.IsChecked == true;
            _s.DockTray = DockTraySwitch.IsChecked == true;
            _s.Save();
            _fx.Run(() => _fx.Dock.ApplySettings());
        }

        /// <summary>Список мониторов под переключателем «Док на всех мониторах»: у каждого не главного — свой переключатель.</summary>
        void FillDockMonitors()
        {
            DockMonList.Children.Clear();
            System.Collections.Generic.List<MonitorInfo> mons;
            try { mons = Monitors.All(); } catch (Exception ex) { Settings.Log(ex); return; }
            if (mons.Count < 2)
            {
                var one = new TextBlock { Text = Loc.T("dock.allmon.one"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 70, 0) };
                one.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
                DockMonList.Children.Add(one);
                return;
            }
            int n = 0;
            foreach (var m in mons)
            {
                n++;
                string name = string.IsNullOrWhiteSpace(m.Name) ? string.Format(Loc.T("dock.allmon.mon"), n) : m.Name;
                string size = m.Bounds.Width + "×" + m.Bounds.Height;
                var sw = new CheckBox
                {
                    Style = (Style)FindResource("Switch"),
                    Content = m.Primary ? $"{name}  ·  {size}  ·  {Loc.T("dock.allmon.primary")}" : $"{name}  ·  {size}",
                    IsChecked = m.Primary || (_s.DockAllMonitors && !_s.DockMonitorsOff.Contains(m.Id)),
                    IsEnabled = !m.Primary && _s.DockAllMonitors,
                    Tag = m.Id
                };
                if (!m.Primary) sw.Click += DockMonItem_Click;
                DockMonList.Children.Add(sw);
            }
        }

        void DockMon_Click(object sender, RoutedEventArgs e)
        {
            _s.DockAllMonitors = DockAllMonSwitch.IsChecked == true;
            _s.Save();
            FillDockMonitors();
            _fx.Run(() => _fx.Dock.ApplySettings());
        }

        void DockMonItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox sw || sw.Tag is not string id) return;
            _s.DockMonitorsOff.Remove(id);
            if (sw.IsChecked != true) _s.DockMonitorsOff.Add(id);
            _s.Save();
            _fx.Run(() => _fx.Dock.ApplySettings());
        }

        void IslandOption_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _s.IslandOn = IslandOnSwitch.IsChecked == true;
            _s.IslandMedia = IslandMediaSwitch.IsChecked == true;
            _s.IslandNotify = IslandNotifySwitch.IsChecked == true;
            _s.IslandHideIdle = IslandHideSwitch.IsChecked == true;
            _s.IslandFly = IslandFlyDown.IsChecked == true ? "down" : "up";
            _s.Save();
            global::MizuEngine.Shell.Island?.Apply();   // капсула живёт в своём потоке — Apply сам туда переходит
        }

        // ================== Wallpaper Engine ==================

        void LoadWe()
        {
            _weLoaded = true;
            WeGrid.Children.Clear();
            string exe = WallpaperEngine.FindExe(out var libraries);
            WeMissing.Visibility = exe == null ? Visibility.Visible : Visibility.Collapsed;
            WeFound.Visibility = exe != null ? Visibility.Visible : Visibility.Collapsed;
            if (exe == null) return;

            var items = WallpaperEngine.Scan(libraries);
            _weCount = items.Count;
            UpdateWeCount();
            int i = 0;
            foreach (var item in items)
            {
                var tile = MakeWeTile(item, exe);
                WeGrid.Children.Add(tile);
                Anim.To(tile, OpacityProperty, 1, 400, from: 0, delayMs: Math.Min(i++, 24) * 25);   // плитки появляются волной
            }
        }

        void UpdateWeCount()
        {
            if (_weCount < 0) return;
            WeCount.Text = _weCount == 0 ? Loc.T("we.empty") : string.Format(Loc.T("we.count"), _weCount);
        }

        UIElement MakeWeTile(WallpaperEngine.Item item, string exe)
        {
            var picture = new Border
            {
                Width = 180, Height = 104, CornerRadius = new CornerRadius(10),
                Background = (Brush)FindResource("TrackOff")
            };
            // Превью грузятся в фоне — окно не подвисает, даже если обоев сотни
            string path = item.Preview;
            Task.Run(() => LoadPreview(path)).ContinueWith(t =>
            {
                if (t.Result != null) picture.Background = t.Result;
            }, TaskScheduler.FromCurrentSynchronizationContext());
            var title = new TextBlock
            {
                Text = item.Title, Width = 180, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 8, 0, 0)
            };
            var tile = new Button
            {
                Style = (Style)FindResource("GlassButton"), Padding = new Thickness(8), Margin = new Thickness(0, 0, 12, 12),
                Content = new StackPanel { Children = { picture, title } }, ToolTip = item.Title
            };
            tile.Click += (o, e) =>
            {
                // Отмечаем выбранные обои сразу, а Wallpaper Engine получает команду в фоне — окно не подвисает
                if (_weSelected != null) _weSelected.BorderThickness = new Thickness(0);
                picture.BorderThickness = new Thickness(2.5);
                picture.SetResourceReference(Border.BorderBrushProperty, "Accent");
                _weSelected = picture;
                Motion.Pop(picture, 1.04);
                ShowToast(Loc.T("we.applying"));
                WallpaperEngine.ApplyAsync(exe, item.Target, ok => Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (IsLoaded) ShowToast(Loc.T(ok ? "we.applied" : "we.failed"));
                })));
            };
            return tile;
        }

        static Brush LoadPreview(string path)
        {
            if (path == null || !File.Exists(path)) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path);
                bmp.DecodePixelWidth = 360;               // не держим в памяти полноразмерные картинки
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                var brush = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                brush.Freeze();
                return brush;
            }
            catch { return null; }   // повреждённое превью — покажем пустую плитку
        }

        void RefreshWe_Click(object sender, RoutedEventArgs e) => LoadWe();
        void BuyWe_Click(object sender, RoutedEventArgs e) => WallpaperEngine.OpenStore();

        // ================== автор ==================

        void Telegram_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo("https://t.me/MrRainthisisme") { UseShellExecute = true }); } catch { }
        }

        void Discord_Click(object sender, RoutedEventArgs e)
        {
            // У Discord нет ссылки на профиль по нику, поэтому копируем ник
            try { Clipboard.SetText("vanixxrain"); } catch { return; }   // буфер может быть занят другой программой
            ShowToast(Loc.T("toast.copied"));
        }

        // ================== пожертвования ==================

        const string CardNumber = "2200702015452159";
        const string DonationUrl = "https://dalink.to/themrrain";

        /// <summary>Полный номер карты виден на русском и английском, на остальных языках середина размыта.</summary>
        void UpdateCardMask()
        {
            bool full = Loc.Code == "ru" || Loc.Code == "en";
            CardMiddle.Effect = full ? null : _cardBlur;
        }

        /// <summary>Если положить Assets\tbank.png или Assets\donationalerts.png, вместо нарисованных значков будут картинки.</summary>
        void LoadBrandIcons()
        {
            Use("tbank.png", TBankImage, TBankVector);
            Use("donationalerts.png", DaImage, DaVector);

            static void Use(string file, Image img, UIElement vector)
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri("pack://application:,,,/Assets/" + file);
                    bmp.DecodePixelWidth = 100;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    img.Source = bmp;
                    img.Visibility = Visibility.Visible;
                    vector.Visibility = Visibility.Collapsed;
                }
                catch { }   // картинки нет — остаётся нарисованный значок
            }
        }

        void Card_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(CardNumber); } catch { return; }   // буфер может быть занят другой программой
            CopyIcon.Text = "";   // галочка
            CopyText.SetResourceReference(TextBlock.TextProperty, "donate.copied");
            Anim.To(CopyPillDone, OpacityProperty, 1, 220);
            var sc = (ScaleTransform)CopyPill.RenderTransform;
            Anim.To(sc, ScaleTransform.ScaleXProperty, 1, 450, from: 0.85, ease: Anim.Spring);
            Anim.To(sc, ScaleTransform.ScaleYProperty, 1, 450, from: 0.85, ease: Anim.Spring);
            _copyReset.Stop();
            _copyReset.Start();
        }

        void ResetCopyPill()
        {
            _copyReset.Stop();
            CopyIcon.Text = "";
            CopyText.SetResourceReference(TextBlock.TextProperty, "donate.copy");
            Anim.To(CopyPillDone, OpacityProperty, 0, 400);
        }

        void DonationAlerts_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo(DonationUrl) { UseShellExecute = true }); } catch { }
        }

        void ShowToast(string text)
        {
            ToastText.Text = text;
            var t = (TranslateTransform)Toast.RenderTransform;
            Anim.To(Toast, OpacityProperty, 1, 300);
            Anim.To(t, TranslateTransform.YProperty, 0, 500, from: 16, ease: Anim.Spring);
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        void HideToast()
        {
            _toastTimer.Stop();
            Anim.To(Toast, OpacityProperty, 0, 350);
            Anim.To((TranslateTransform)Toast.RenderTransform, TranslateTransform.YProperty, 10, 350);
        }

        // ================== обновления ==================

        bool _manualCheck;

        void OnUpdaterChanged()
        {
            RefreshUpdateUi();
            if (Updater.Status == Updater.State.Available) { if (Updater.ShouldNag) ShowGate(); }   // закрыли крестиком — до следующего запуска сама не всплывает
            else if (Updater.Status != Updater.State.Checking) HideGate();   // во время повторной проверки плашка не мигает
        }

        void RefreshUpdateUi()
        {
            var st = Updater.Status;
            bool available = st == Updater.State.Available;

            UpdStatus.Text =
                st == Updater.State.UpToDate ? Loc.T("upd.latest") :
                available ? string.Format(Loc.T("upd.available"), Updater.Short(Updater.Latest)) :
                st == Updater.State.Error ? Loc.T("upd.error") : Loc.T("upd.checking");

            string details = string.Format(Loc.T("upd.current"), Updater.Short(Updater.Current));
            if (st == Updater.State.Error) details += "\n" + Loc.T("upd.error.d");
            else if (Updater.LastCheck != default) details += " · " + string.Format(Loc.T("upd.checked"), Updater.LastCheck.ToString("HH:mm"));
            UpdDetails.Text = details;
            UpdButtons.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            GateVersions.Text = Updater.Short(Updater.Current) + "  →  " + Updater.Short(Updater.Latest);
            // Сборка для разработки (без подписи) не меняет себя сама — тогда кнопка просто скачивает exe в браузере
            string install = Loc.T(Guard.Official || Updater.AssetUrl == null ? "upd.install" : "upd.direct");
            UpdInstallBtn.Content = install;
            GateInstallBtn.Content = install;
            string notes = available ? Updater.Notes(Loc.Code) : "";
            GateNotes.Text = notes;
            GateNotesBox.Visibility = notes.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            SetGateBusy(Updater.Installing);

            // Точка внизу меню: зелёная — всё хорошо, оранжевая — вышло обновление
            StatusDot.Fill = new SolidColorBrush(available ? Color.FromRgb(0xFF, 0xA9, 0x4D) : Color.FromRgb(0x3D, 0xDC, 0x84));
            if (available) StatusText.Text = Loc.T("upd.badge");
            else StatusText.SetResourceReference(TextBlock.TextProperty, "status.ready");

            SpinArrow(st == Updater.State.Checking || _manualCheck);
        }

        async void UpdCheck_Click(object sender, RoutedEventArgs e)
        {
            if (_manualCheck) return;
            _manualCheck = true;
            SpinArrow(true);
            var started = DateTime.Now;
            var result = await Updater.CheckAsync();
            // Стрелка крутится хотя бы полсекунды — иначе при быстром ответе не видно, что проверка была
            var left = TimeSpan.FromMilliseconds(600) - (DateTime.Now - started);
            if (left > TimeSpan.Zero) await Task.Delay(left);
            _manualCheck = false;
            if (!IsLoaded) return;
            SpinArrow(Updater.Status == Updater.State.Checking);

            if (result == Updater.State.Available) ShowGate();   // нашлось — плашка с «Обновить сейчас» и «Страница на GitHub»
            else ShowToast(Loc.T(result == Updater.State.UpToDate ? "upd.latest" : "upd.error"));
        }

        void UpdOpen_Click(object sender, RoutedEventArgs e)
        {
            if (Updater.OpenRelease()) return;
            // Браузер не открылся — плашка временно уходит и вернётся при следующей проверке
            Updater.Postpone();
            ShowToast(Loc.T("upd.openfail"));
        }

        /// <summary>Крестик или «Позже» на плашке: прячем её, интерфейс снова чёткий. Напомним при следующем запуске.</summary>
        void GateClose_Click(object sender, RoutedEventArgs e)
        {
            if (Updater.Installing) return;
            Updater.Dismiss();
            HideGate();
        }

        /// <summary>«Обновить сейчас»: скачать, проверить подпись, заменить exe и перезапуститься (Updater.InstallAsync).</summary>
        async void UpdInstall_Click(object sender, RoutedEventArgs e)
        {
            if (Updater.Installing || Updater.Status != Updater.State.Available) return;
            ShowGate();
            SetGateBusy(true);
            Anim.Set(GateBar, WidthProperty, 0);
            GateProgressText.Text = Loc.T("upd.st.download");

            var res = await Updater.InstallAsync(
                (done, total) => Dispatcher.BeginInvoke(new Action(() => SetGateProgress(done, total))),
                stage => Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (stage != "download") GateProgressText.Text = Loc.T("upd.st." + stage);
                    if (stage != "download") Anim.To(GateBar, WidthProperty, GateProgress.Width, 200);
                })));

            switch (res)
            {
                case Updater.InstallResult.Restarting:
                    await Task.Delay(350);   // новая копия уже запущена и ждёт, пока мы закроемся
                    Program.Quit();
                    break;
                case Updater.InstallResult.OpenedBrowser:
                    SetGateBusy(false);
                    ShowToast(Loc.T("upd.browser"));
                    break;
                default:
                    SetGateBusy(false);
                    ShowToast(Loc.T("upd.fail"));
                    break;
            }
        }

        void SetGateBusy(bool busy)
        {
            GateButtons.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
            GateProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            UpdInstallBtn.IsEnabled = !busy;
            if (busy && string.IsNullOrEmpty(GateProgressText.Text)) GateProgressText.Text = Loc.T("upd.st.download");
        }

        void SetGateProgress(long done, long total)
        {
            if (!Updater.Installing) return;
            double mb(long b) => Math.Round(b / 1048576.0, 1);
            if (total > 0)
            {
                double f = Math.Min(1, done / (double)total);
                Anim.To(GateBar, WidthProperty, GateProgress.Width * f, 120);
                GateProgressText.Text = string.Format(Loc.T("upd.dl"), (int)(f * 100), mb(done), mb(total));
            }
            else GateProgressText.Text = Loc.T("upd.st.download") + "  " + mb(done) + " MB";
        }

        bool _spinning;

        void SpinArrow(bool on)
        {
            if (on == _spinning) return;
            _spinning = on;
            var rt = (RotateTransform)UpdArrow.RenderTransform;
            if (on)
            {
                rt.BeginAnimation(RotateTransform.AngleProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9))
                {
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
                });
            }
            else
            {
                // Плавно докручиваем до целого оборота, без рывка в исходное положение
                double a = rt.Angle % 360;
                rt.BeginAnimation(RotateTransform.AngleProperty, new System.Windows.Media.Animation.DoubleAnimation(a, 360, TimeSpan.FromMilliseconds(450))
                {
                    EasingFunction = new System.Windows.Media.Animation.QuinticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
                    FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
                });
            }
        }

        bool _gateOpen;

        void ShowGate()
        {
            if (_gateOpen && UpdateGate.Visibility == Visibility.Visible && UpdateGate.Opacity > 0.99) return;
            _gateOpen = true;
            UpdateGate.Visibility = Visibility.Visible;
            BlurBehindGate(true);
            Anim.To(UpdateGate, OpacityProperty, 1, 350, from: IsLoaded ? 0 : 1);
            var sc = (ScaleTransform)GateCard.RenderTransform;
            Anim.To(sc, ScaleTransform.ScaleXProperty, 1, 600, from: 0.9, delayMs: 60, ease: Anim.Spring);
            Anim.To(sc, ScaleTransform.ScaleYProperty, 1, 600, from: 0.9, delayMs: 60, ease: Anim.Spring);

            // Стрелка на плашке мягко «качается» вниз — как подсказка «скачай»
            var t = (TranslateTransform)GateArrow.RenderTransform;
            t.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(-3, 4, TimeSpan.FromSeconds(0.9))
            {
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            });
        }

        void HideGate()
        {
            if (UpdateGate.Visibility != Visibility.Visible) return;
            _gateOpen = false;
            BlurBehindGate(false);
            Anim.To(UpdateGate, OpacityProperty, 0, 300, done: () =>
            {
                if (_gateOpen) return;   // пока пряталась, плашку снова открыли
                UpdateGate.Visibility = Visibility.Collapsed;
                StopGateAnim();
            });
        }

        /// <summary>Пока открыта плашка обновления, меню и страница за ней мягко размыты; закрыли — снова чёткие.</summary>
        void BlurBehindGate(bool on)
        {
            foreach (var el in new UIElement[] { SideCol, MainCol })
            {
                var target = el;
                if (on)
                {
                    if (target.Effect is not System.Windows.Media.Effects.BlurEffect blur)
                    {
                        blur = new System.Windows.Media.Effects.BlurEffect
                        {
                            Radius = 0,
                            KernelType = System.Windows.Media.Effects.KernelType.Gaussian,
                            RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance
                        };
                        target.Effect = blur;
                    }
                    Anim.To(blur, System.Windows.Media.Effects.BlurEffect.RadiusProperty, 16, IsLoaded ? 380 : 1);
                }
                else if (target.Effect is System.Windows.Media.Effects.BlurEffect blur)
                {
                    // Размытие убираем совсем, когда оно дошло до нуля, — иначе видеокарта зря считает его каждый кадр
                    Anim.To(blur, System.Windows.Media.Effects.BlurEffect.RadiusProperty, 0, 300,
                        done: () => { if (!_gateOpen && target.Effect == blur) target.Effect = null; });
                }
            }
        }

        void StopGateAnim() => ((TranslateTransform)GateArrow.RenderTransform).BeginAnimation(TranslateTransform.YProperty, null);

        // ================== автозапуск ==================

        void Autostart_Click(object sender, RoutedEventArgs e)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key == null) return;
            if (Autostart.IsChecked == true) key.SetValue("MizuEngine", "\"" + Environment.ProcessPath + "\" --tray");
            else key.DeleteValue("MizuEngine", false);
        }

        static bool IsAutostart()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("MizuEngine") != null;
        }
    }
}
