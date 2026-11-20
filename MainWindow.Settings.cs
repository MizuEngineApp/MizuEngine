// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>
    /// Страница «Настройки» с вкладками: Основные, Звук, Уведомления, Аккаунт и безопасность, Окно, Данные.
    /// Смена пароля: старый пароль один раз, новый — дважды, «Сохранить», потом код из письма.
    /// </summary>
    public partial class MainWindow
    {
        FrameworkElement _setShown;

        void InitSettingsPage()
        {
            _setShown = SetGeneral;
            UiSoundsSwitch.IsChecked = _s.Sounds;
            NotifyPreviewSwitch.IsChecked = _s.NotifyPreview;
            NotifyDndSwitch.IsChecked = _s.NotifyDnd;
            NotifyIslandSwitch.IsChecked = _s.IslandNotify;
            SafeRenderSwitch.IsChecked = _s.SafeRender;
            CloseToTraySwitch.IsChecked = _s.CloseToTray;
            FillUiSoundChips();
            Loc.Changed += FillUiSoundChips;
            Supa.SessionChanged += OnSessionForSettings;
            Closed += (o, e) => { Loc.Changed -= FillUiSoundChips; Supa.SessionChanged -= OnSessionForSettings; };
        }

        void OnSessionForSettings() => Dispatcher.BeginInvoke(new Action(() => { if (_setShown == SetAccount) BuildAccountTab(); }));

        // ================== вкладки ==================

        void SetTab_Click(object sender, RoutedEventArgs e) =>
            ShowSettingsTab(sender == SetTabSound ? "sound" : sender == SetTabNotify ? "notify" : sender == SetTabAccount ? "account"
                          : sender == SetTabWindow ? "window" : sender == SetTabData ? "data" : "general");

        /// <summary>Открыть вкладку настроек (из «Чатов» кнопкой 🔊 — сразу «Звук»).</summary>
        internal void ShowSettingsTab(string tab)
        {
            FrameworkElement page = tab switch
            {
                "sound" => SetSound, "notify" => SetNotify, "account" => SetAccount, "window" => SetWindow, "data" => SetData, _ => SetGeneral
            };
            var pill = tab switch
            {
                "sound" => SetTabSound, "notify" => SetTabNotify, "account" => SetTabAccount, "window" => SetTabWindow, "data" => SetTabData, _ => SetTabGeneral
            };
            pill.IsChecked = true;
            if (page == SetAccount) BuildAccountTab();
            if (page == SetData) UpdateCacheSize();
            if (page == SetNotify) NotifyIslandSwitch.IsChecked = _s.IslandNotify;   // могли поменять на странице «Док»
            if (page == _setShown) return;

            var old = _setShown;
            _setShown = page;
            Anim.To(old, OpacityProperty, 0, 120, done: () => { if (_setShown != old) old.Visibility = Visibility.Collapsed; });
            page.Visibility = Visibility.Visible;
            var t = page.RenderTransform as TranslateTransform ?? new TranslateTransform();
            page.RenderTransform = t;
            Anim.To(page, OpacityProperty, 1, 320, from: 0, delayMs: 60);
            Anim.To(t, TranslateTransform.YProperty, 0, 420, from: 10, delayMs: 60);
            PageSettings.ScrollToTop();
        }

        // ================== звук интерфейса ==================

        void UiSounds_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _s.Sounds = UiSoundsSwitch.IsChecked == true;
            UiSounds.On = _s.Sounds;
            _s.Save();
            _custom?.Refresh();
        }

        void FillUiSoundChips()
        {
            if (UiSoundPanel == null) return;
            UiSoundPanel.Children.Clear();
            EnsureUiVolume();
            foreach (var id in UiSounds.Packs)
            {
                string pack = id;
                UiSoundPanel.Children.Add(SoundChip(Loc.T("sounds." + pack), _s.SoundPack == pack, () =>
                {
                    _s.SoundPack = pack;
                    UiSounds.Pack = pack;
                    _s.Save();
                    UiSounds.Play(pack);
                    FillUiSoundChips();
                    _custom?.Refresh();
                }));
            }
        }

        Slider _uiVol;

        /// <summary>Ползунок громкости звуков интерфейса — под наборами звуков (отдельно от громкости сообщений).</summary>
        void EnsureUiVolume()
        {
            if (_uiVol != null || UiSoundPanel.Parent is not StackPanel host) return;
            var value = new TextBlock { Text = _s.UiVolume + "%" };
            value.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
            DockPanel.SetDock(value, Dock.Right);
            var label = new TextBlock();
            label.SetResourceReference(TextBlock.TextProperty, "set.ui.volume");
            var row = new DockPanel { Margin = new Thickness(0, 16, 0, 8) };
            row.Children.Add(value);
            row.Children.Add(label);
            _uiVol = new Slider { Style = (Style)FindResource("NiceSlider"), Minimum = 0, Maximum = 100, Value = _s.UiVolume };
            var save = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            save.Tick += (o, e) => { save.Stop(); _s.Save(); UiSounds.Play(_s.SoundPack); };
            _uiVol.ValueChanged += (o, e) =>
            {
                _s.UiVolume = (int)Math.Round(_uiVol.Value);
                UiSounds.Volume = _s.UiVolume;
                value.Text = _s.UiVolume + "%";
                save.Stop(); save.Start();   // звук-пример — когда ползунок отпустили
            };
            host.Children.Add(row);
            host.Children.Add(_uiVol);
        }

        // ================== окно ==================

        void SafeRender_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _s.SafeRender = SafeRenderSwitch.IsChecked == true;
            _s.Save();
            Program.Restart();   // режим отрисовки меняется только при запуске
        }

        void WindowOption_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _s.CloseToTray = CloseToTraySwitch.IsChecked == true;
            _s.Save();
        }

        // ================== данные ==================

        void OpenDataFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Settings.Folder + "\"") { UseShellExecute = true });
            }
            catch (Exception ex) { Settings.Log(ex); }
        }

        void OpenErrorLog_Click(object sender, RoutedEventArgs e)
        {
            string log = Path.Combine(Settings.Folder, "error.log");
            if (!File.Exists(log)) { ShowToast(Loc.T("set.data.nolog")); return; }
            try { Process.Start(new ProcessStartInfo("notepad.exe", "\"" + log + "\"") { UseShellExecute = true }); }
            catch (Exception ex) { Settings.Log(ex); }
        }

        static string CacheFolder => Path.Combine(Settings.Folder, "sounds", "cache");

        void UpdateCacheSize()
        {
            long bytes = 0;
            try { if (Directory.Exists(CacheFolder)) bytes = new DirectoryInfo(CacheFolder).GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); } catch { }
            CacheSizeText.Text = string.Format(Loc.T("set.data.cache.d"), Math.Round(bytes / 1024.0 / 1024.0, 1));
        }

        void ClearCache_Click(object sender, RoutedEventArgs e)
        {
            try { if (Directory.Exists(CacheFolder)) Directory.Delete(CacheFolder, true); } catch (Exception ex) { Settings.Log(ex); }
            Glass.TrimMemory();
            UpdateCacheSize();
            ShowToast(Loc.T("set.data.cleared"));
        }

        DateTime _resetArmed = DateTime.MinValue;
        readonly DispatcherTimer _resetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };

        /// <summary>Сброс всех настроек: первое нажатие спрашивает подтверждение, второе (в течение 4 с) — сбрасывает и перезапускает.</summary>
        void ResetSettings_Click(object sender, RoutedEventArgs e)
        {
            if ((DateTime.UtcNow - _resetArmed).TotalSeconds > 4)
            {
                _resetArmed = DateTime.UtcNow;
                ResetButton.SetResourceReference(ContentControl.ContentProperty, "set.data.reset.sure");
                _resetTimer.Tick -= ResetDisarm;
                _resetTimer.Tick += ResetDisarm;
                _resetTimer.Start();
                return;
            }
            _resetTimer.Stop();
            Settings.ResetAndFreeze();   // аккаунты (accounts.bin) не трогаем — сбрасываются только настройки
            Program.Restart();
        }

        void ResetDisarm(object sender, EventArgs e)
        {
            _resetTimer.Stop();
            _resetArmed = DateTime.MinValue;
            ResetButton.SetResourceReference(ContentControl.ContentProperty, "set.data.reset.btn");
        }

        // ================== аккаунт и безопасность ==================

        void BuildAccountTab()
        {
            SetAccount.Children.Clear();
            var panelStyle = (Style)FindResource("Panel");

            if (!Supa.SignedIn)
            {
                var need = new StackPanel();
                need.Children.Add(UI.L("set.acc.login", 14, wrap: true));
                var go = UI.Primary("set.acc.goprofile", (o, e) => NavProfile.IsChecked = true);
                go.Margin = new Thickness(0, 14, 0, 0);
                need.Children.Add(go);
                SetAccount.Children.Add(new Border { Style = panelStyle, Child = need });
                return;
            }

            // Кто вошёл
            var who = Supa.Session;
            var me = SocialApi.MyProfile;
            var info = new StackPanel();
            info.Children.Add(UI.L("set.acc.title", 16, bold: true));
            string nick = me?.Nickname ?? who?.Nickname ?? "";
            string handle = me?.Handle ?? who?.Handle;
            info.Children.Add(Row(Loc.T("set.acc.nick"), nick + (string.IsNullOrEmpty(handle) ? "" : "  ·  @" + handle)));
            info.Children.Add(Row(Loc.T("set.acc.email"), who?.Email ?? "—"));
            SetAccount.Children.Add(new Border { Style = panelStyle, Child = info });

            // Конфиденциальность: кто видит «в сети» и кто может сразу добавлять в группы
            var priv = new StackPanel();
            priv.Children.Add(UI.L("set.acc.privacy", 16, bold: true));
            var onlineLabel = UI.L("set.acc.online", 13.5, wrap: true);
            onlineLabel.Margin = new Thickness(0, 12, 0, 6);
            priv.Children.Add(onlineLabel);
            var onlineRow = new WrapPanel();
            string vis = me?.OnlineVis ?? "everyone";
            foreach (var k in new[] { "everyone", "followers", "friends", "nobody" })
            {
                string key = k;
                onlineRow.Children.Add(UI.Tab("set.acc.online." + key, "onlinevis", vis == key, async (o, e) =>
                {
                    string r = await SocialApi.UpdateMe(new System.Text.Json.Nodes.JsonObject { ["online_vis"] = key });
                    ShowToast(r ?? Loc.T("set.saved"));
                }));
            }
            priv.Children.Add(onlineRow);
            var groupLabel = UI.L("set.acc.groupadd", 13.5, wrap: true);
            groupLabel.Margin = new Thickness(0, 14, 0, 6);
            priv.Children.Add(groupLabel);
            var groupRow = new WrapPanel();
            string ga = me?.GroupAdd ?? "everyone";
            foreach (var k in new[] { "everyone", "invites" })
            {
                string key = k;
                groupRow.Children.Add(UI.Tab("set.acc.groupadd." + key, "groupadd", ga == key, async (o, e) =>
                {
                    string r = await SocialApi.UpdateMe(new System.Text.Json.Nodes.JsonObject { ["group_add"] = key });
                    ShowToast(r ?? Loc.T("set.saved"));
                }));
            }
            priv.Children.Add(groupRow);
            // «Сейчас слушает» в профиле (0.11.4): кто видит трек из плеера
            var npLabel = UI.L("np.vis", 13.5, wrap: true);
            npLabel.Margin = new Thickness(0, 14, 0, 6);
            priv.Children.Add(npLabel);
            var npRow = new WrapPanel();
            string npv = me?.NpVis ?? "everyone";
            foreach (var k in new[] { "everyone", "friends", "nobody" })
            {
                string key = k;
                npRow.Children.Add(UI.Tab("np.vis." + key, "npvis", npv == key, async (o, e) =>
                {
                    string r = await SocialApi.UpdateMe(new System.Text.Json.Nodes.JsonObject { ["np_vis"] = key });
                    if (r == null) NowPlayingSync.Refresh();
                    ShowToast(r ?? Loc.T("set.saved"));
                }));
            }
            priv.Children.Add(npRow);
            var privHint = UI.L("set.acc.privacy.d", 12, sub: true, wrap: true);
            privHint.Margin = new Thickness(0, 10, 0, 0);
            priv.Children.Add(privHint);
            SetAccount.Children.Add(new Border { Style = panelStyle, Child = priv, Margin = new Thickness(0, 14, 0, 0) });

            // Смена пароля
            var pass = new StackPanel();
            pass.Children.Add(UI.L("set.acc.pass", 16, bold: true));
            pass.Children.Add(UI.L("set.acc.pass.d", 12, sub: true, wrap: true));
            var oldBox = UI.Password();
            var newBox = UI.Password();
            var repBox = UI.Password();
            var fields = new StackPanel { MaxWidth = 380, HorizontalAlignment = HorizontalAlignment.Left };
            fields.Children.Add(UI.Labeled("set.acc.old", oldBox));
            fields.Children.Add(UI.Labeled("set.acc.new", newBox));
            fields.Children.Add(UI.Labeled("set.acc.repeat", repBox));
            pass.Children.Add(fields);
            var err = UI.Error();
            pass.Children.Add(err);

            // Второй шаг — код из письма (появляется после «Сохранить»)
            var codeBox = UI.Field("", 10);
            var codeStep = new StackPanel { Visibility = Visibility.Collapsed, MaxWidth = 380, HorizontalAlignment = HorizontalAlignment.Left };
            var sentText = UI.T(string.Format(Loc.T("set.acc.sent"), who?.Email ?? ""), 12.5, sub: true, wrap: true);
            sentText.Margin = new Thickness(0, 14, 0, 0);
            codeStep.Children.Add(sentText);
            codeStep.Children.Add(UI.Labeled("set.acc.code", codeBox));

            Button save = null, confirm = null, resend = null;
            string pending = null;   // новый пароль, который ждёт подтверждения кодом

            save = UI.Primary("set.acc.save", async (o, e) =>
            {
                UI.ShowError(err, null);
                string oldP = oldBox.Password, newP = newBox.Password, rep = repBox.Password;
                if (string.IsNullOrEmpty(oldP)) { UI.ShowError(err, Loc.T("set.acc.err.old")); return; }
                if (newP.Length < 6) { UI.ShowError(err, Loc.T("err.password")); return; }
                if (newP != rep) { UI.ShowError(err, Loc.T("set.acc.err.mismatch")); return; }
                if (newP == oldP) { UI.ShowError(err, Loc.T("err.same_password")); return; }
                save.IsEnabled = false;
                string r = await Supa.BeginPasswordChangeAsync(oldP);
                save.IsEnabled = true;
                if (r != null) { UI.ShowError(err, SocialApi.MapError(r)); return; }
                pending = newP;
                fields.IsEnabled = false;
                save.Visibility = Visibility.Collapsed;
                codeStep.Visibility = Visibility.Visible;
                codeBox.Focus();
                ShowToast(Loc.T("set.acc.code_sent"));
            });
            save.Margin = new Thickness(0, 16, 0, 0);

            confirm = UI.Primary("set.acc.confirm", async (o, e) =>
            {
                UI.ShowError(err, null);
                string code = new string(codeBox.Text.Where(char.IsDigit).ToArray());
                if (code.Length < 6) { UI.ShowError(err, Loc.T("err.code")); return; }
                confirm.IsEnabled = false;
                string r = await Supa.FinishPasswordChangeAsync(code, pending);
                confirm.IsEnabled = true;
                if (r != null) { UI.ShowError(err, SocialApi.MapError(r)); return; }
                ShowToast(Loc.T("set.acc.done"));
                BuildAccountTab();
            });
            resend = UI.Primary("set.acc.resend", async (o, e) =>
            {
                resend.IsEnabled = false;
                string r = await Supa.BeginPasswordChangeAsync(oldBox.Password);
                resend.IsEnabled = true;
                if (r != null) UI.ShowError(err, SocialApi.MapError(r)); else ShowToast(Loc.T("set.acc.code_sent"));
            });
            var buttons = UI.Row(confirm, resend);
            resend.Margin = new Thickness(10, 0, 0, 0);
            buttons.Margin = new Thickness(0, 16, 0, 0);
            codeStep.Children.Add(buttons);

            pass.Children.Add(save);
            pass.Children.Add(codeStep);
            SetAccount.Children.Add(new Border { Style = panelStyle, Child = pass, Margin = new Thickness(0, 14, 0, 0) });
        }

        static FrameworkElement Row(string label, string value)
        {
            var g = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(UI.T(label, 13.5, sub: true));
            var v = UI.T(value, 13.5, bold: true);
            v.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
            return g;
        }
    }
}
