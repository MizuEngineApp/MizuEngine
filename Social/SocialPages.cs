// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MizuEngine
{
    /// <summary>
    /// Страницы «Профиль», «Друзья», «Чаты». Всё строится в коде из кирпичиков UI, данные — через SocialApi.
    /// </summary>
    internal sealed partial class SocialPages
    {
        readonly Grid _profileHost, _friendsHost, _chatsHost, _communityHost;
        readonly Action<string> _toast;
        readonly Action<PresetInfo> _applyPreset;
        readonly Action<string> _navigate;   // "profile" | "friends" | "chats"

        string _viewedProfile;               // null — свой профиль
        bool _adding;                        // открыт вход в ещё один аккаунт
        string _pendingProfile, _pendingFriendsOf, _pendingTab;   // что открыть при переходе на страницу из другого места
        string _friendsOf;                   // чьи списки показываем на странице «Друзья»
        string _friendsTab = "friends";
        string _chatWith;
        long _lastMessageId;
        readonly DispatcherTimer _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        readonly DispatcherTimer _searchDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        StackPanel _messages;
        ScrollViewer _messagesScroll;
        Dictionary<string, Profile> _people = new Dictionary<string, Profile>();
        bool _chatsVisible;

        public SocialPages(Grid profileHost, Grid friendsHost, Grid chatsHost, Grid communityHost, Action<string> toast, Action<PresetInfo> applyPreset, Action<string> navigate)
        {
            _profileHost = profileHost;
            _friendsHost = friendsHost;
            _chatsHost = chatsHost;
            _communityHost = communityHost;
            _toast = toast;
            _applyPreset = applyPreset;
            _navigate = navigate;
            _poll.Tick += async (s, e) => await PollMessages();
            Supa.SessionChanged += OnSession;
        }

        public void Detach()
        {
            Supa.SessionChanged -= OnSession;
            _poll.Stop();
        }

        void OnSession()
        {
            _viewedProfile = null;
            _adding = false;
            _friendsOf = _pendingFriendsOf = _pendingTab = _pendingProfile = null;
            _chatWith = null;
            _groupOpen = 0;
            _lastMessageId = 0;
            _people.Clear();
            _ = ShowProfile(null);
        }

        // ================== общие состояния ==================

        /// <summary>Любая ошибка при построении страницы: пишем в error.log и показываем причину, а не пустоту.</summary>
        static async Task Guard(Grid host, Func<Task> build)
        {
            try { await build(); }
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

        /// <summary>Сервер не настроен — показываем объяснение. Иначе ничего не трогаем: старое содержимое уйдёт с анимацией.</summary>
        static bool Gate(Grid host)
        {
            if (!Supa.Configured)
            {
                host.Children.Clear();
                var sp = new StackPanel { MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left };
                sp.Children.Add(UI.H1("social.title"));
                var c = new StackPanel();
                c.Children.Add(UI.L("social.noserver", 16, true));
                c.Children.Add(UI.L("social.noserver.d", 13, sub: true, wrap: true));
                sp.Children.Add(UI.Card(c, 22));
                host.Children.Add(sp);
                return false;
            }
            return true;
        }

        // ================== переходы между страницами ==================

        /// <summary>Пункт меню «Профиль»: свой профиль, или чужой, если на него перешли по ссылке.</summary>
        public Task OnProfileNav()
        {
            string id = _pendingProfile;
            _pendingProfile = null;
            return ShowProfile(id);
        }

        public Task OnFriendsNav()
        {
            _friendsOf = _pendingFriendsOf;
            _friendsTab = _pendingTab ?? "friends";
            _pendingFriendsOf = _pendingTab = null;
            return ShowFriends();
        }

        // ================== ПРОФИЛЬ ==================

        public Task ShowProfile(string userId) => Guard(_profileHost, () => ShowProfileCore(userId));

        async Task ShowProfileCore(string userId)
        {
            _viewedProfile = userId;
            if (!Gate(_profileHost)) return;
            if (!Supa.SignedIn || _adding) { Motion.Replace(_profileHost, UI.Scroll(LoginView())); return; }

            // Пока грузим — старая страница остаётся (чуть тусклее), чтобы не было резкой «пустоты»
            if (_profileHost.Children.Count == 0) _profileHost.Children.Add(UI.Loading());
            else Motion.Dim(_profileHost);
            if (SocialApi.MyProfile == null) await SocialApi.LoadMe();
            var p = userId == null || userId == Supa.Me ? SocialApi.MyProfile : await SocialApi.GetProfile(userId);
            if (_viewedProfile != userId) return;   // пока грузили, открыли другой профиль
            if (p == null)
            {
                Motion.Replace(_profileHost, UI.L("err.network", 14, sub: true));
                return;
            }
            _people[p.Id] = p;
            Motion.Replace(_profileHost, UI.Scroll(ProfileView(p, p.Id == Supa.Me)));
        }

        // ================== ВХОД, РЕГИСТРАЦИЯ, КОД С ПОЧТЫ ==================

        static readonly System.Text.RegularExpressions.Regex EmailRx =
            new System.Text.RegularExpressions.Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$");

        static bool ValidEmail(string s) => EmailRx.IsMatch(s ?? "");

        /// <summary>Экран входа. Внутри карточка меняется: форма → код с почты → (для сброса) новый пароль.</summary>
        FrameworkElement LoginView()
        {
            var root = new StackPanel { MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left };
            if (Supa.SignedIn)
            {
                // Добавляем ещё один аккаунт — можно вернуться назад
                var back = UI.Glass("", "common.back", async (s, e) => { _adding = false; await ShowProfile(null); }, 8);
                back.Margin = new Thickness(0, 0, 0, 12);
                root.Children.Add(back);
                root.Children.Add(UI.H1("accounts.adding"));
            }
            else root.Children.Add(UI.H1("login.title"));
            var sub = UI.L("login.sub", 14, sub: true, wrap: true);
            sub.Margin = new Thickness(0, 6, 0, 0);
            root.Children.Add(sub);

            var card = UI.Card(null, 22);
            root.Children.Add(card);
            if (Supa.SignedIn && !Supa.CanAddAccount) Swap(card, AccountsFullNote());
            else ShowLoginForm(card, false, "");
            return root;
        }

        /// <summary>На ПК уже открыто 3 аккаунта — войти в ещё один нельзя, пока не выйдешь из какого-нибудь.</summary>
        static FrameworkElement AccountsFullNote()
        {
            var sp = new StackPanel();
            sp.Children.Add(UI.L("device.full_title", 18, true));
            var t = UI.T(string.Format(Loc.T("device.full_sub"), Supa.MaxOpenAccounts), 13, sub: true, wrap: true);
            t.Margin = new Thickness(0, 6, 0, 0);
            sp.Children.Add(t);
            return sp;
        }

        static void Swap(Border card, UIElement content)
        {
            card.Child = content;
            if (content is FrameworkElement fe) Motion.Enter(fe);
        }

        void ShowLoginForm(Border card, bool register, string emailText)
        {
            var form = new StackPanel();
            var tabs = UI.Row(
                UI.Tab("login.signin", "auth", !register, null),
                UI.Tab("login.signup", "auth", register, null));
            form.Children.Add(tabs);

            var email = UI.Field(emailText, 120);
            var (passUi, password) = UI.Secret();
            var (pass2Ui, password2) = UI.Secret();
            var nick = UI.Field("", 32);
            var nickRow = UI.Labeled("profile.nick", nick);
            form.Children.Add(nickRow);
            form.Children.Add(UI.Labeled("login.email", email));
            form.Children.Add(UI.Labeled("login.password", passUi));
            var repeatRow = UI.Labeled("login.password2", pass2Ui);
            form.Children.Add(repeatRow);

            var forgot = new Button { Style = (Style)UI.Owner.FindResource("CaptionBtn"), Width = double.NaN, Height = double.NaN, Padding = new Thickness(2, 4, 2, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), Cursor = System.Windows.Input.Cursors.Hand };
            var forgotText = UI.L("login.forgot", 12.5);
            forgotText.FontFamily = System.Windows.Documents.TextElement.GetFontFamily(UI.Owner);   // CaptionBtn задаёт шрифт иконок — без этого вместо букв квадратики
            forgotText.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            forgot.Content = forgotText;
            form.Children.Add(forgot);

            var error = UI.Error();
            form.Children.Add(error);

            var go = UI.Primary(register ? "login.signup" : "login.signin", null);
            go.Margin = new Thickness(0, 16, 0, 0);
            go.IsDefault = true;
            form.Children.Add(go);

            var or = UI.L("login.or", 12, sub: true);
            or.Margin = new Thickness(2, 16, 0, 10);
            form.Children.Add(or);

            var google = new Button { Style = (Style)UI.Owner.FindResource("GlassButton"), Padding = new Thickness(16, 11, 16, 11), HorizontalAlignment = HorizontalAlignment.Left };
            var gText = UI.L("login.google", 14, true);
            gText.Margin = new Thickness(10, 0, 0, 0);
            google.Content = UI.Row(GoogleG(), gText);
            var github = new Button { Style = (Style)UI.Owner.FindResource("GlassButton"), Padding = new Thickness(16, 11, 16, 11), Margin = new Thickness(8, 0, 0, 0) };
            var ghText = UI.L("login.github", 14, true);
            ghText.Margin = new Thickness(10, 0, 0, 0);
            github.Content = UI.Row(GitHubMark(), ghText);
            var providers = new WrapPanel();
            providers.Children.Add(google);
            providers.Children.Add(github);
            form.Children.Add(providers);

            void Mode(bool reg)
            {
                register = reg;
                nickRow.Visibility = repeatRow.Visibility = reg ? Visibility.Visible : Visibility.Collapsed;
                forgot.Visibility = reg ? Visibility.Collapsed : Visibility.Visible;
                go.SetResourceReference(ContentControl.ContentProperty, reg ? "login.signup" : "login.signin");
                UI.ShowError(error, null);
            }
            // Лимит: с одного ПК — не больше 2 регистраций. Кнопка «Регистрация» серая, нажать нельзя.
            var regTab = (RadioButton)tabs.Children[1];
            var limitNote = UI.L("device.signup_limit", 12.5, sub: true, wrap: true);
            limitNote.Margin = new Thickness(2, 10, 0, 0);
            limitNote.Visibility = Visibility.Collapsed;
            form.Children.Insert(1, limitNote);
            ToolTipService.SetShowOnDisabled(regTab, true);

            void ApplyLimits()
            {
                bool can = Supa.CanRegister;
                regTab.IsEnabled = can;
                regTab.Opacity = can ? 1 : 0.4;
                regTab.ToolTip = can ? null : Loc.T("device.signup_limit");
                limitNote.Visibility = can ? Visibility.Collapsed : Visibility.Visible;
                if (!can && register)
                {
                    ((RadioButton)tabs.Children[0]).IsChecked = true;
                    Mode(false);
                }
            }

            Mode(register);
            ApplyLimits();
            ((RadioButton)tabs.Children[0]).Click += (s, e) => Mode(false);
            regTab.Click += (s, e) => { if (Supa.CanRegister) Mode(true); };
            async void CheckDevice()
            {
                await Supa.RefreshDeviceAsync();
                ApplyLimits();
                if (Supa.SignedIn && !Supa.CanAddAccount && card.Child == form) Swap(card, AccountsFullNote());
            }
            CheckDevice();
            forgot.Click += (s, e) => ShowForgot(card, email.Text.Trim());

            go.Click += async (s, e) =>
            {
                UI.ShowError(error, null);
                string mail = email.Text.Trim();
                if (!ValidEmail(mail)) { UI.ShowError(error, Loc.T("err.email")); return; }
                string pw = password();
                if (pw.Length < 6) { UI.ShowError(error, Loc.T("err.password")); return; }
                if (register && pw != password2()) { UI.ShowError(error, Loc.T("err.password_match")); return; }
                go.IsEnabled = false;
                string res = register
                    ? await Supa.SignUpAsync(mail, pw, string.IsNullOrWhiteSpace(nick.Text) ? null : nick.Text.Trim())
                    : await Supa.SignInAsync(mail, pw);
                go.IsEnabled = true;
                if (res == null) return;   // вошли сразу — страница перерисуется сама
                if (res == "code_sent") { ShowCode(card, register ? "signup" : "login", mail, register); return; }
                if (res == "code_sent_signup") { ShowCode(card, "signup", mail, false); return; }
                UI.ShowError(error, SocialApi.MapError(res));
                if (res == "device_limit" || SocialApi.MapError(res) == Loc.T("err.device_limit")) ApplyLimits();
            };
            async Task External(Button b, Func<Task<string>> signIn)
            {
                UI.ShowError(error, null);
                google.IsEnabled = github.IsEnabled = false;
                _toast(Loc.T("login.browser"));
                string err = await signIn();
                google.IsEnabled = github.IsEnabled = true;
                if (err != null) UI.ShowError(error, SocialApi.MapError(err));
            }
            google.Click += async (s, e) => await External(google, Supa.SignInWithGoogleAsync);
            github.Click += async (s, e) => await External(github, Supa.SignInWithGitHubAsync);
            Swap(card, form);
        }

        /// <summary>Окно ввода 6-значного кода из письма. kind: "signup", "login" или "recovery".</summary>
        void ShowCode(Border card, string kind, string email, bool fromRegister)
        {
            var sp = new StackPanel();
            sp.Children.Add(UI.L("code.title", 20, true));
            var hint = UI.T(string.Format(Loc.T("code.sub"), email), 13, sub: true, wrap: true);
            hint.Margin = new Thickness(0, 6, 0, 18);
            sp.Children.Add(hint);

            var (boxes, input) = CodeBoxes();
            sp.Children.Add(boxes);

            var error = UI.Error();
            sp.Children.Add(error);

            var confirm = UI.Primary("code.confirm", null);
            confirm.Margin = new Thickness(0, 18, 0, 0);
            confirm.IsDefault = true;
            sp.Children.Add(confirm);

            var resendText = UI.T("", 13, true);
            var resend = new Button { Style = (Style)UI.Owner.FindResource("GlassButton"), Padding = new Thickness(14, 9, 14, 9), Content = resendText, HorizontalAlignment = HorizontalAlignment.Left };
            var change = UI.Glass("", "code.change", null, 9);
            change.Margin = new Thickness(8, 0, 0, 0);
            var row = UI.Row(resend, change);
            row.Margin = new Thickness(0, 14, 0, 0);
            sp.Children.Add(row);

            // Повторная отправка — не чаще раза в 60 секунд
            int left = 60;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            void Tick()
            {
                resend.IsEnabled = left <= 0;
                resendText.Text = left > 0 ? $"{Loc.T("code.resend")} ({left})" : Loc.T("code.resend");
            }
            timer.Tick += (s, e) => { left--; Tick(); if (left <= 0) timer.Stop(); };
            Tick();
            timer.Start();
            sp.Unloaded += (s, e) => timer.Stop();

            bool busy = false;
            async Task Submit()
            {
                if (busy) return;
                string code = input.Text;
                if (code.Length != 6) { UI.ShowError(error, Loc.T("err.code")); return; }
                busy = true; confirm.IsEnabled = false; UI.ShowError(error, null);
                string err = await Supa.VerifyCodeAsync(kind, email, code);
                busy = false; confirm.IsEnabled = true;
                if (err != null)
                {
                    UI.ShowError(error, SocialApi.MapError(err));
                    input.Text = "";
                    input.Focus();
                    return;
                }
                timer.Stop();
                if (kind == "recovery") ShowNewPassword(card);
                // иначе вход уже выполнен — страница профиля перерисуется сама
            }

            confirm.Click += async (s, e) => await Submit();
            input.TextChanged += async (s, e) => { if (input.Text.Length == 6) await Submit(); };
            resend.Click += async (s, e) =>
            {
                resend.IsEnabled = false;
                string err = await Supa.ResendCodeAsync(kind, email);
                if (err != null) { UI.ShowError(error, SocialApi.MapError(err)); resend.IsEnabled = true; return; }
                _toast(Loc.T("code.sent"));
                left = 60; Tick(); timer.Start();
            };
            change.Click += (s, e) =>
            {
                timer.Stop();
                if (kind == "recovery") ShowForgot(card, email); else ShowLoginForm(card, fromRegister, email);
            };

            Swap(card, sp);
            sp.Loaded += (s, e) => input.Focus();
        }

        /// <summary>Шесть клеточек для цифр. Ввод идёт в невидимое поле поверх них — работает и вставка из буфера.</summary>
        static (FrameworkElement ui, TextBox input) CodeBoxes()
        {
            var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
            var cells = new StackPanel { Orientation = Orientation.Horizontal };
            var digits = new TextBlock[6];
            var frames = new Border[6];
            for (int i = 0; i < 6; i++)
            {
                digits[i] = UI.T("", 26, true);
                digits[i].HorizontalAlignment = HorizontalAlignment.Center;
                digits[i].VerticalAlignment = VerticalAlignment.Center;
                frames[i] = new Border
                {
                    Width = 52, Height = 62, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1.5),
                    Margin = new Thickness(0, 0, i == 2 ? 18 : 8, 0), Child = digits[i]
                };
                frames[i].SetResourceReference(Border.BackgroundProperty, "CardBg");
                frames[i].SetResourceReference(Border.BorderBrushProperty, "CardRim");
                cells.Children.Add(frames[i]);
            }
            grid.Children.Add(cells);

            var input = new TextBox
            {
                MaxLength = 6, Opacity = 0.01, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = Brushes.Transparent, CaretBrush = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.IBeam,
                FontSize = 26, InputScope = new System.Windows.Input.InputScope { Names = { new System.Windows.Input.InputScopeName(System.Windows.Input.InputScopeNameValue.Number) } }
            };
            System.Windows.Input.InputMethod.SetIsInputMethodEnabled(input, false);
            grid.Children.Add(input);

            void Paint()
            {
                string t = input.Text;
                for (int i = 0; i < 6; i++)
                {
                    digits[i].Text = i < t.Length ? t[i].ToString() : "";
                    bool active = input.IsKeyboardFocused && i == Math.Min(t.Length, 5);
                    frames[i].SetResourceReference(Border.BorderBrushProperty, active || i < t.Length ? "Accent" : "CardRim");
                    frames[i].BorderThickness = new Thickness(active ? 2 : 1.5);
                }
            }
            input.PreviewTextInput += (s, e) => e.Handled = !e.Text.All(char.IsDigit);
            input.TextChanged += (s, e) =>
            {
                string clean = new string(input.Text.Where(char.IsDigit).Take(6).ToArray());
                if (clean != input.Text) { input.Text = clean; input.CaretIndex = clean.Length; return; }
                input.CaretIndex = clean.Length;
                Paint();
            };
            input.GotKeyboardFocus += (s, e) => Paint();
            input.LostKeyboardFocus += (s, e) => Paint();
            Paint();
            return (grid, input);
        }

        void ShowForgot(Border card, string emailText)
        {
            var sp = new StackPanel();
            sp.Children.Add(UI.L("forgot.title", 20, true));
            var hint = UI.L("forgot.sub", 13, sub: true, wrap: true);
            hint.Margin = new Thickness(0, 6, 0, 4);
            sp.Children.Add(hint);
            var email = UI.Field(emailText, 120);
            sp.Children.Add(UI.Labeled("login.email", email));
            var error = UI.Error();
            sp.Children.Add(error);
            var send = UI.Primary("forgot.send", null);
            send.IsDefault = true;
            var back = UI.Glass("", "common.back", (s, e) => ShowLoginForm(card, false, email.Text.Trim()), 9);
            back.Margin = new Thickness(8, 0, 0, 0);
            var row = UI.Row(send, back);
            row.Margin = new Thickness(0, 16, 0, 0);
            sp.Children.Add(row);

            send.Click += async (s, e) =>
            {
                string mail = email.Text.Trim();
                if (!ValidEmail(mail)) { UI.ShowError(error, Loc.T("err.email")); return; }
                send.IsEnabled = false;
                string err = await Supa.SendRecoveryAsync(mail);
                send.IsEnabled = true;
                if (err != null) { UI.ShowError(error, SocialApi.MapError(err)); return; }
                ShowCode(card, "recovery", mail, false);
            };
            Swap(card, sp);
        }

        void ShowNewPassword(Border card)
        {
            var sp = new StackPanel();
            sp.Children.Add(UI.L("newpass.title", 20, true));
            var (p1Ui, p1) = UI.Secret();
            var (p2Ui, p2) = UI.Secret();
            sp.Children.Add(UI.Labeled("login.password", p1Ui));
            sp.Children.Add(UI.Labeled("login.password2", p2Ui));
            var error = UI.Error();
            sp.Children.Add(error);
            var save = UI.Primary("newpass.save", null);
            save.Margin = new Thickness(0, 16, 0, 0);
            save.IsDefault = true;
            sp.Children.Add(save);
            save.Click += async (s, e) =>
            {
                string pw = p1();
                if (pw.Length < 6) { UI.ShowError(error, Loc.T("err.password")); return; }
                if (pw != p2()) { UI.ShowError(error, Loc.T("err.password_match")); return; }
                save.IsEnabled = false;
                string err = await Supa.SetPasswordAsync(pw);
                save.IsEnabled = true;
                if (err != null) { UI.ShowError(error, SocialApi.MapError(err)); return; }
                _toast(Loc.T("newpass.done"));
                Supa.Announce();   // теперь показываем профиль
            };
            Swap(card, sp);
        }

        /// <summary>Значок GitHub для кнопки входа (цвет текста темы).</summary>
        static FrameworkElement GitHubMark()
        {
            try
            {
                var geo = Geometry.Parse("M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.013 8.013 0 0 0 16 8c0-4.42-3.58-8-8-8z");
                var path = new System.Windows.Shapes.Path { Data = geo, Width = 18, Height = 18, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
                path.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Text");
                return path;
            }
            catch
            {
                var t = UI.T("GH", 13, true);
                t.VerticalAlignment = VerticalAlignment.Center;
                return t;
            }
        }

        /// <summary>Разноцветная «G» вместо логотипа Google (рисуем сами — без чужих картинок).</summary>
        static FrameworkElement GoogleG()
        {
            var t = new TextBlock { Text = "G", FontSize = 18, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            b.GradientStops.Add(new GradientStop(Color.FromRgb(0xEA, 0x43, 0x35), 0));
            b.GradientStops.Add(new GradientStop(Color.FromRgb(0xFB, 0xBC, 0x05), 0.35));
            b.GradientStops.Add(new GradientStop(Color.FromRgb(0x34, 0xA8, 0x53), 0.65));
            b.GradientStops.Add(new GradientStop(Color.FromRgb(0x42, 0x85, 0xF4), 1));
            t.Foreground = b;
            return t;
        }

        FrameworkElement ProfileView(Profile p, bool isMe)
        {
            var outer = new StackPanel { Margin = new Thickness(0, 0, 14, 20) };

            if (!isMe)
            {
                var back = UI.Glass("", "social.myprofile", async (s, e) => await ShowProfile(null), 8);
                back.Margin = new Thickness(0, 0, 0, 12);
                outer.Children.Add(back);
            }

            // Две колонки: слева карточка профиля, справа аккаунты, посты и друзья.
            // Если окно узкое — правая колонка уходит под карточку.
            var (_, root, side) = TwoColumns(outer, 390);

            // ----- карточка профиля в стиле iOS: всё по центру, крупная аватарка -----
            var card = new Border { CornerRadius = new CornerRadius(28), BorderThickness = new Thickness(1) };
            card.SetResourceReference(Border.BackgroundProperty, "CardBg");
            card.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            var body = new StackPanel();
            card.Child = body;

            var bannerUi = Banner(p.BannerUrl, 150, isMe ? () => _ = ChangeImage("banner", 1600) : (Action)null);
            DecorateBanner(bannerUi, p);   // живой баннер и свой GIF/MP4 (Mizu Plus)
            body.Children.Add(bannerUi);

            // Аватарка по центру, наполовину на баннере
            var ring = new Border { CornerRadius = new CornerRadius(62), BorderThickness = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -58, 0, 0), Child = p.Bot ? ProfileAvatar(p, 112) : Online.WithDot(ProfileAvatar(p, 112), p.Id, 112) };
            ring.SetResourceReference(Border.BorderBrushProperty, "WinBg");
            ring.SetResourceReference(Border.BackgroundProperty, "WinBg");
            Motion.Hero(ring, "avatar:" + p.Id);
            if (isMe) _headerRing = ring;   // сюда прилетит аватарка при переключении аккаунта
            if (isMe)
            {
                UI.Hand(ring);
                ring.ToolTip = Loc.T("profile.avatar");
                ring.MouseLeftButtonUp += async (s, e) => await ChangeImage("avatar", 512);
            }
            body.Children.Add(WithFrame(ring, p, 120));   // рамка аватарки (Mizu Plus)

            var info = new StackPanel { Margin = new Thickness(22, 10, 22, 20) };
            var nameRow = UI.Row(UI.T(p.Nickname, 24, true));
            nameRow.HorizontalAlignment = HorizontalAlignment.Center;
            if (p.Verified) { var b = UI.AnimatedBadge(22, p.Badge); b.Margin = new Thickness(8, 1, 0, 0); nameRow.Children.Add(b); }
            if (p.IsPlus) { var pb = UI.PlusBadge(22, big: true); pb.Margin = new Thickness(8, 1, 0, 0); nameRow.Children.Add(pb); }
            info.Children.Add(nameRow);
            var handle = UI.T("@" + p.Handle, 14, sub: true);
            handle.HorizontalAlignment = HorizontalAlignment.Center;
            info.Children.Add(handle);
            if (!p.Bot)
            {
                // «в сети» / «был(а) в сети 5 мин назад» (если человек не скрыл статус)
                var online = Online.Status(p.Id, 12.5);
                online.HorizontalAlignment = HorizontalAlignment.Center;
                online.Margin = new Thickness(0, 3, 0, 0);
                info.Children.Add(online);
            }

            if (!string.IsNullOrEmpty(p.Title)) info.Children.Add(TitlePill(p));
            if (p.Banned) info.Children.Add(StatusPill("profile.banned_pill", Color.FromRgb(0xE8, 0x34, 0x2A)));
            else if (p.Muted) info.Children.Add(StatusPill("profile.muted_pill", Color.FromRgb(0xFF, 0x86, 0x0A)));
            if (!string.IsNullOrWhiteSpace(p.Bio))
            {
                var bio = UI.T(p.Bio, 14, wrap: true);
                bio.TextAlignment = TextAlignment.Center;
                bio.Margin = new Thickness(0, 12, 0, 0);
                info.Children.Add(bio);
            }
            // Владелец MizuEngine: строка наклонным серым шрифтом, как у Дурова в Telegram
            if (p.Number == 1) info.Children.Add(OfficialLine(!string.IsNullOrWhiteSpace(p.Bio)));
            // Ссылки: Telegram, Discord, YouTube, TikTok, Spotify — круглые значки, клик открывает
            if (p.Links != null && p.Links.Count > 0) info.Children.Add(ProfileLinks.Row(p.Links));
            // Сейчас слушает (если человек не скрыл)
            if (!p.Bot) info.Children.Add(NowPlayingCard(p));

            // Счётчики — три стеклянные плитки
            var stats = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 18, 0, 0) };
            var sFriends = Stat("profile.friends", () => OpenLists(p.Id, "friends"));
            var sFollowers = Stat("profile.followers", () => OpenLists(p.Id, "followers"));
            var sFollowing = Stat("profile.following", () => OpenLists(p.Id, "following"));
            stats.Children.Add(sFriends.ui); stats.Children.Add(sFollowers.ui); stats.Children.Add(sFollowing.ui);
            info.Children.Add(stats);

            var actions = new WrapPanel { Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            info.Children.Add(actions);
            body.Children.Add(info);
            root.Children.Add(card);

            // Сведения об аккаунте — группой строк, как в «Настройках» iPhone
            root.Children.Add(UI.Section("profile.about"));
            var rows = new List<UIElement>
            {
                UI.IosRow(UI.Squircle("", UI.Blue), UI.T(string.Format(Loc.T("profile.number"), p.Number), 13.5))
            };
            if (p.Verified) rows.Add(UI.IosRow(UI.Badge(28, p.Badge), UI.T(UI.BadgeTip(p.Badge), 13, wrap: true)));
            if (p.IsPlus) rows.Add(UI.IosRow(UI.PlusBadge(28, clickable: false), UI.T(Loc.T("plus.profile_row") + " · " + Plus.UntilText(p.PlusForever, p.PlusUntil), 13, wrap: true), UI.Chevron(), () => UI.OpenPlus?.Invoke()));
            if (p.CanPost) rows.Add(UI.IosRow(UI.Squircle("", UI.Orange), UI.L(p.IsAdmin ? "profile.role_admin" : "profile.role_staff", 13.5)));
            rows.Add(UI.IosRow(UI.Squircle("\uE916", UI.Purple), UI.T(string.Format(Loc.T("profile.usage"), Hours(p.UsageSeconds)), 13.5)));
            root.Children.Add(UI.IosGroup(rows.ToArray()));
            if (isMe && (p.Banned || p.Muted)) root.Children.Insert(0, PunishCard(p));

            Func<Task> refreshStats = () => FillStats(p.Id, sFriends.value, sFollowers.value, sFollowing.value);
            _ = refreshStats();

            if (isMe) BuildMyActions(actions, root, p);
            else _ = BuildOtherActions(actions, p, refreshStats);

            if (CanManage(p)) root.Children.Add(AdminPanel(p, isMe));

            // ----- правая колонка -----
            if (isMe)
            {
                side.Children.Add(UI.Section("accounts.title", 0));
                side.Children.Add(AccountsCard());
            }
            // Посты: у себя в профиле пишут все (правила — часы, 10 минут, 1000 символов — проверяет сервер)
            side.Children.Add(UI.Section("posts.title", side.Children.Count == 0 ? 0 : 18));
            side.Children.Add(Wrap(PostsBlock(() => SocialApi.GetPosts(p.Id), p, isMe && Supa.SignedIn && SocialApi.MyProfile?.Banned != true, 0)));
            side.Children.Add(UI.Section("friendsbox.title", side.Children.Count == 0 ? 0 : 18));
            side.Children.Add(FriendsBox(p));
            return outer;
        }

        /// <summary>Сетка «карточка слева + колонка справа», которая на узком окне складывается в одну колонку.</summary>
        static (Grid grid, StackPanel left, StackPanel right) TwoColumns(StackPanel outer, double leftWidth)
        {
            var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(leftWidth) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var left = new StackPanel();
            var right = new StackPanel();
            grid.Children.Add(left);
            grid.Children.Add(right);
            void Layout(double w)
            {
                if (w <= 0) return;
                grid.Width = Math.Min(w, 1120);
                bool two = w >= 820;
                grid.ColumnDefinitions[0].Width = two ? new GridLength(leftWidth) : new GridLength(1, GridUnitType.Star);
                grid.ColumnDefinitions[1].Width = two ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
                Grid.SetColumn(right, two ? 1 : 0);
                Grid.SetRow(right, two ? 0 : 1);
                right.Margin = two ? new Thickness(20, 0, 0, 0) : new Thickness(0, 14, 0, 0);
            }
            outer.SizeChanged += (s, e) => { if (e.WidthChanged) Layout(e.NewSize.Width); };
            Layout(900);   // до первого измерения окна
            outer.Children.Add(grid);
            return (grid, left, right);
        }

        /// <summary>Баннер с мягким затемнением снизу. change — кнопка «Сменить баннер».</summary>
        static FrameworkElement Banner(string url, double height, Action change)
        {
            var banner = new Border { Height = height, CornerRadius = new CornerRadius(27, 27, 0, 0) };
            if (!string.IsNullOrEmpty(url))
            {
                // Пока баннер качается — тот же градиент, что и без баннера; картинка мягко проявляется поверх
                var g0 = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                g0.GradientStops.Add(new GradientStop(Color.FromRgb(0x7F, 0xC4, 0xFF), 0));
                g0.GradientStops.Add(new GradientStop(Color.FromRgb(0x2A, 0x62, 0xF0), 0.5));
                g0.GradientStops.Add(new GradientStop(Color.FromRgb(0x7A, 0x3C, 0xE8), 1));
                banner.Background = g0;
                var pic = new Border { Height = height, CornerRadius = new CornerRadius(27, 27, 0, 0), Opacity = 0, IsHitTestVisible = false };
                pic.Background = UI.WebBrush(url, 1400, ready: () => Anim.To(pic, UIElement.OpacityProperty, 1, 260));
                banner.Child = pic;
            }
            else
            {
                var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                g.GradientStops.Add(new GradientStop(Color.FromRgb(0x7F, 0xC4, 0xFF), 0));
                g.GradientStops.Add(new GradientStop(Color.FromRgb(0x2A, 0x62, 0xF0), 0.5));
                g.GradientStops.Add(new GradientStop(Color.FromRgb(0x7A, 0x3C, 0xE8), 1));
                banner.Background = g;
            }
            var shade = new Border { Height = height, CornerRadius = new CornerRadius(27, 27, 0, 0), IsHitTestVisible = false,
                                     Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0x50, 0, 0, 0), 90) };
            var grid = new Grid();
            grid.Children.Add(banner);
            grid.Children.Add(shade);
            if (change != null)
            {
                var b = UI.Glass("", "profile.banner", (s, e) => change(), 7);
                b.HorizontalAlignment = HorizontalAlignment.Right;
                b.VerticalAlignment = VerticalAlignment.Top;
                b.Margin = new Thickness(0, 12, 12, 0);
                grid.Children.Add(b);
            }
            return grid;
        }

        static FrameworkElement TitlePill(Profile p)
        {
            var pill = UI.TitlePill(p.Title, p.TitleBg, p.TitleFg);
            pill.Margin = new Thickness(0, 10, 0, 0);
            return pill;
        }

        /// <summary>Красная/оранжевая плашка «Заблокирован» / «В муте» в профиле.</summary>
        static FrameworkElement StatusPill(string key, Color c)
        {
            var pill = new Border { CornerRadius = new CornerRadius(11), Padding = new Thickness(12, 4, 12, 5), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Center,
                                    Background = new SolidColorBrush(Color.FromArgb(0x30, c.R, c.G, c.B)), BorderBrush = new SolidColorBrush(c), BorderThickness = new Thickness(1) };
            var t = UI.L(key, 12.5, true);
            t.Foreground = new SolidColorBrush(c);
            pill.Child = t;
            return pill;
        }

        static string Hours(long seconds) => (seconds / 3600.0).ToString(seconds < 36000 ? "0.#" : "0");

        /// <summary>Содержимое в карточку iOS (скругление 20).</summary>
        static Border Wrap(UIElement content)
        {
            var card = new Border { CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1), Padding = new Thickness(16), Child = content };
            card.SetResourceReference(Border.BackgroundProperty, "CardBg");
            card.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            return card;
        }

        static void AddAction(Panel actions, FrameworkElement b)
        {
            b.Margin = new Thickness(4, 0, 4, 8);
            actions.Children.Add(b);
        }

        // ---------- аккаунты ----------

        FrameworkElement _headerRing;   // аватарка в шапке своего профиля (цель перелёта при смене аккаунта)
        bool _switching;

        Border AccountsCard()
        {
            var rows = new List<UIElement>();
            foreach (var acc in Supa.Accounts)
            {
                bool current = acc.UserId == Supa.Me;
                var look = new Profile
                {
                    Id = acc.UserId, Nickname = acc.Nickname ?? (acc.Email ?? "?").Split('@')[0],
                    Handle = acc.Handle ?? "", AvatarUrl = acc.AvatarUrl, Badge = SocialApi.BadgeKind(acc.Badge, acc.Verified)
                }.Fix();
                var av = UI.Avatar(look, 36);
                var names = new StackPanel();
                var nr = UI.Row(UI.T(look.Nickname, 14, true));
                if (look.Verified) { var b = UI.Badge(14, look.Badge); b.Margin = new Thickness(5, 0, 0, 0); nr.Children.Add(b); }
                names.Children.Add(nr);
                names.Children.Add(UI.T(string.IsNullOrEmpty(look.Handle) ? acc.Email ?? "" : "@" + look.Handle, 12, sub: true));
                UIElement mark = current ? UI.Icon("", 15, accent: true) : (UIElement)UI.Chevron();
                var id = acc.UserId;
                rows.Add(UI.IosRow(av, names, mark, current ? (Action)null : async () =>
                {
                    if (_switching) return;
                    _switching = true;
                    try
                    {
                        // Аватарка сразу летит из списка в шапку профиля и ждёт там, старый профиль мягко размывается.
                        // Когда новый аккаунт загрузится — она плавно превращается в его настоящую аватарку
                        Motion.FlyAndHold(av, _headerRing != null && _headerRing.IsVisible ? _headerRing : null, "avatar:" + id);
                        Motion.Leave(_profileHost);
                        await System.Threading.Tasks.Task.Delay(60);   // дать анимации стартовать до тяжёлой работы
                        string err = await Supa.SwitchToAsync(id);
                        if (err != null) { _toast(SocialApi.MapError(err)); await ShowProfile(null); }
                    }
                    finally { _switching = false; }
                }));
            }
            if (Supa.CanAddAccount)
                rows.Add(UI.IosRow(UI.Squircle("", UI.Gray), UI.L("accounts.add", 13.5, true), UI.Chevron(), async () => { _adding = true; await ShowProfile(null); }));
            else
            {
                // Открыто 3 аккаунта: строка серая и не нажимается
                var txt = new StackPanel();
                txt.Children.Add(UI.L("accounts.add", 13.5, true));
                txt.Children.Add(UI.T(string.Format(Loc.T("device.full_short"), Supa.MaxOpenAccounts), 12, sub: true, wrap: true));
                var row = UI.IosRow(UI.Squircle("", UI.Gray), txt);
                row.Opacity = 0.45;
                row.ToolTip = string.Format(Loc.T("device.full_sub"), Supa.MaxOpenAccounts);
                rows.Add(row);
            }
            return UI.IosGroup(rows.ToArray());
        }

        // ---------- посты: в профиле и в сообществе ----------

        /// <summary>
        /// Лента постов. canWrite — показать поле «Что нового?». communityId > 0 — посты сообщества.
        /// author — от чьего имени показывать посты (человек или сообщество).
        /// </summary>
        FrameworkElement PostsBlock(Func<Task<List<Post>>> load, Profile author, bool canWrite, long communityId)
        {
            var sp = new StackPanel();
            var list = new StackPanel();
            Func<Task> reload = null;
            reload = () => FillPosts(list, load, author, reload);

            if (canWrite)
            {
                // Сотрудники: до 2000 символов и с фото. Остальные: только текст до 1000 символов.
                bool staff = SocialApi.MyProfile?.CanPost == true;
                int max = staff ? 2000 : SocialApi.PostMaxLen;
                var text = UI.Field("", max, multiline: true);
                text.Tag = Loc.T("posts.placeholder");
                sp.Children.Add(text);

                string image = null;
                var thumb = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
                sp.Children.Add(thumb);

                var photo = UI.Glass("\uE722", "posts.photo", null, 9);
                var publish = UI.Primary("posts.publish", null);
                var row = UI.Row();
                if (staff) { row.Children.Add(photo); publish.Margin = new Thickness(8, 0, 0, 0); }
                row.Children.Add(publish);
                var counter = UI.T("0 / " + max, 12, sub: true);
                counter.Margin = new Thickness(12, 0, 0, 0);
                counter.VerticalAlignment = VerticalAlignment.Center;
                if (!staff) row.Children.Add(counter);
                row.Margin = new Thickness(0, 10, 0, 6);
                sp.Children.Add(row);

                // Подсказка под кнопкой: «мало часов», «следующий пост через …» и другие ошибки
                var note = UI.T("", 12.5, sub: true, wrap: true);
                note.Margin = new Thickness(2, 2, 0, 6);
                note.Visibility = Visibility.Collapsed;
                note.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A));
                sp.Children.Add(note);
                void Note(string msg)
                {
                    bool same = note.Visibility == Visibility.Visible && note.Text == msg;
                    note.Text = msg ?? "";
                    note.Visibility = string.IsNullOrEmpty(msg) ? Visibility.Collapsed : Visibility.Visible;
                    if (!same && note.Visibility == Visibility.Visible) Motion.Enter(note);
                }
                // Новенький: сразу говорим, что часов пока мало (сервер всё равно проверит сам)
                bool EnoughHours() => SocialApi.EnoughHoursToPost(SocialApi.MyProfile);
                text.GotKeyboardFocus += (s, e) =>
                {
                    if (!EnoughHours()) Note(SocialApi.NotEnoughHoursText(SocialApi.MyProfile?.UsageSeconds ?? 0));
                };
                text.TextChanged += (s, e) =>
                {
                    counter.Text = text.Text.Length + " / " + max;
                    if (text.Text.Length >= max) counter.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A));
                    else counter.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
                };

                photo.Click += (s, e) =>
                {
                    var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp" };
                    if (dlg.ShowDialog() != true) return;
                    image = dlg.FileName;
                    thumb.Background = UI.WebBrush(image, 160);
                    thumb.Visibility = Visibility.Visible;
                    thumb.ToolTip = Path.GetFileName(image);
                    Motion.Pop(thumb, 0.8);
                };
                thumb.MouseLeftButtonUp += (s, e) => { image = null; thumb.Visibility = Visibility.Collapsed; };
                UI.Hand(thumb);

                publish.Click += async (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(text.Text) && image == null) return;
                    if (!EnoughHours()) { Note(SocialApi.NotEnoughHoursText(SocialApi.MyProfile?.UsageSeconds ?? 0)); Motion.Pop(note, 1.05); return; }
                    publish.IsEnabled = false;
                    string err = await SocialApi.CreatePost(text.Text.Trim(), staff ? image : null, communityId);
                    publish.IsEnabled = true;
                    if (err != null) { Note(err); Motion.Pop(note, 1.05); return; }
                    Note(null);
                    text.Text = ""; image = null; thumb.Visibility = Visibility.Collapsed;
                    await reload();
                };
            }

            sp.Children.Add(list);
            list.Children.Add(UI.Loading());
            _ = reload();
            return sp;
        }

        async Task FillPosts(StackPanel list, Func<Task<List<Post>>> load, Profile author, Func<Task> reload)
        {
            var posts = await load();
            list.Children.Clear();
            if (posts == null) { list.Children.Add(UI.L("err.network", 13, sub: true)); return; }
            if (posts.Count == 0)
            {
                var empty = UI.L("posts.empty", 13, sub: true);
                empty.Margin = new Thickness(2, 10, 0, 4);
                list.Children.Add(empty);
                return;
            }
            foreach (var post in posts)
            {
                bool canDelete = post.Author == Supa.Me || SocialApi.MyProfile?.CanPost == true;   // автор, админ или сотрудник
                list.Children.Add(PostView(post, author, canDelete, reload));
            }
            Motion.Stagger(list);
        }

        FrameworkElement PostView(Post post, Profile author, bool canDelete, Func<Task> reload)
        {
            var box = new Border { CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Padding = new Thickness(16, 14, 16, 10), Margin = new Thickness(0, 12, 0, 0) };
            box.SetResourceReference(Border.BackgroundProperty, "HoverBg");
            box.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            var sp = new StackPanel();
            box.Child = sp;

            var head = new Grid();
            var who = UI.Row(UI.Avatar(author, 38));
            var names = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var nr = UI.Row(UI.T(author.Nickname, 14, true));
            if (author.Verified) { var b = UI.Badge(15, author.Badge); b.Margin = new Thickness(6, 0, 0, 0); nr.Children.Add(b); }
            if (author.IsPlus) nr.Children.Add(UI.PlusAfter(15));
            names.Children.Add(nr);
            names.Children.Add(UI.T(UI.Ago(post.Created), 11.5, sub: true));
            who.Children.Add(names);
            head.Children.Add(who);
            if (canDelete)
            {
                var del = UI.Glass("", null, async (s, e) =>
                {
                    string err = await SocialApi.DeletePost(post.Id);
                    if (err != null) { _toast(err); return; }
                    _toast(Loc.T("posts.deleted"));
                    await reload();
                }, 6);
                del.ToolTip = Loc.T("posts.delete");
                del.HorizontalAlignment = HorizontalAlignment.Right;
                del.VerticalAlignment = VerticalAlignment.Top;
                head.Children.Add(del);
            }
            // «Пожаловаться» — на чужие посты (сотрудникам не нужно: они могут сразу удалить)
            FrameworkElement reportPanel = null;
            if (!canDelete && Supa.SignedIn && post.Author != Supa.Me && SocialApi.MyProfile?.Banned != true)
            {
                Button flag = null;
                void CloseReport()
                {
                    if (reportPanel == null) return;
                    var old = reportPanel; reportPanel = null;
                    Motion.Exit(old, 0, () => sp.Children.Remove(old));
                }
                flag = UI.Glass("\uE7C1", null, (s, e) =>
                {
                    if (reportPanel != null) { CloseReport(); return; }
                    reportPanel = ReportPanel(post, () =>
                    {
                        flag.IsEnabled = false;
                        flag.ToolTip = Loc.T("posts.report_sent");
                        CloseReport();
                    });
                    sp.Children.Insert(sp.Children.IndexOf(head) + 1, reportPanel);
                    Motion.Enter(reportPanel);
                }, 6);
                flag.ToolTip = Loc.T("posts.report");
                flag.HorizontalAlignment = HorizontalAlignment.Right;
                flag.VerticalAlignment = VerticalAlignment.Top;
                head.Children.Add(flag);
            }
            sp.Children.Add(head);

            if (!string.IsNullOrWhiteSpace(post.Body))
            {
                var t = UI.T(post.Body, 14, wrap: true);
                t.Margin = new Thickness(0, 10, 0, 0);
                sp.Children.Add(t);
            }
            if (!string.IsNullOrEmpty(post.ImageUrl))
            {
                var pic = new Border { Height = 240, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 10, 0, 0) };
                pic.SetResourceReference(Border.BackgroundProperty, "CardBg");
                var photo = new Border { CornerRadius = new CornerRadius(14), Opacity = 0, IsHitTestVisible = false };
                photo.Background = UI.WebBrush(post.ImageUrl, 1100, ready: () => Anim.To(photo, UIElement.OpacityProperty, 1, 240));
                pic.Child = photo;
                UI.Hand(pic);
                pic.MouseLeftButtonUp += (s, e) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(post.ImageUrl) { UseShellExecute = true }); } catch { } };
                sp.Children.Add(pic);
            }

            // Лайк (объёмное сердечко с анимацией, 0.11.5) и комментарии
            bool canLike = Supa.SignedIn && SocialApi.MyProfile?.Banned != true;
            var likeBtn = Heart.LikeButton(post.Liked, post.Likes, async on =>
            {
                string err = on ? await SocialApi.Like(post.Id) : await SocialApi.Unlike(post.Id);
                return err;
            }, _toast, 16, (on, n) => { post.Liked = on; post.Likes = n; }, canLike);

            var ccount = UI.T(post.Comments.ToString(), 13, true);
            ccount.Margin = new Thickness(7, 0, 0, 0);
            var commentBtn = new Button { Style = (Style)UI.Owner.FindResource("GlassButton"), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(8, 0, 0, 0), Content = UI.Row(UI.Icon("\uE8F2", 15), ccount) };
            commentBtn.ToolTip = Loc.T("comments.title");

            var bar = UI.Row(likeBtn, commentBtn);
            bar.Margin = new Thickness(0, 10, 0, 0);
            sp.Children.Add(bar);

            FrameworkElement comments = null;
            commentBtn.Click += (s, e) =>
            {
                if (comments != null) { var c = comments; comments = null; Motion.Exit(c, 0, () => sp.Children.Remove(c)); return; }
                comments = Comments("post", post.Id, n => { post.Comments = n; ccount.Text = n.ToString(); });
                sp.Children.Add(comments);
                Motion.Enter(comments);
            };
            return box;
        }

        /// <summary>Панель «Пожаловаться: Спам · Оскорбления · Другое» под шапкой поста. sent — жалоба ушла.</summary>
        FrameworkElement ReportPanel(Post post, Action sent)
        {
            var box = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 10, 0, 0) };
            box.SetResourceReference(Border.BackgroundProperty, "CardBg");
            var panel = new StackPanel();
            panel.Children.Add(UI.L("posts.report_title", 13, true));
            var row = new WrapPanel { Margin = new Thickness(-8, 6, 0, 0) };
            foreach (var reason in new[] { "spam", "insult", "other" })
            {
                string r = reason;
                var b = SmallButton("posts.report." + r, false);
                b.Margin = new Thickness(8, 4, 0, 0);
                b.Click += async (s, e) =>
                {
                    foreach (var x in row.Children.OfType<Button>()) x.IsEnabled = false;
                    string err = await SocialApi.ReportPost(post.Id, r);
                    if (err != null && err != Loc.T("posts.report_already"))
                    {
                        _toast(err);
                        foreach (var x in row.Children.OfType<Button>()) x.IsEnabled = true;
                        return;
                    }
                    _toast(err ?? Loc.T("posts.report_sent"));
                    sent();
                };
                row.Children.Add(b);
            }
            panel.Children.Add(row);
            box.Child = panel;
            return box;
        }

        // ---------- друзья (превью) ----------

        Border FriendsBox(Profile p)
        {
            var sp = new StackPanel();
            var wrap = new WrapPanel();
            sp.Children.Add(wrap);
            var all = UI.IosRow(UI.Squircle("", UI.Green), UI.L("friendsbox.all", 13.5, true), UI.Chevron(), () => OpenLists(p.Id, "friends"));
            all.Margin = new Thickness(0, 6, 0, 0);
            sp.Children.Add(all);
            wrap.Children.Add(UI.Loading());
            _ = Fill();
            async Task Fill()
            {
                List<Profile> friends;
                try { friends = await SocialApi.Friends(p.Id); }
                catch (Exception ex) { Settings.Log(ex); friends = new List<Profile>(); }
                wrap.Children.Clear();
                if (friends.Count == 0)
                {
                    var t = UI.L("friendsbox.empty", 13, sub: true);
                    t.Margin = new Thickness(2, 4, 0, 4);
                    wrap.Children.Add(t);
                    return;
                }
                foreach (var f in friends.Take(8))
                {
                    var cell = new StackPanel { Width = 78, Margin = new Thickness(0, 4, 6, 6), Background = Brushes.Transparent };
                    var av = UI.Avatar(f, 52);
                    av.HorizontalAlignment = HorizontalAlignment.Center;
                    cell.Children.Add(av);
                    var n = UI.T(f.Nickname, 12, true);
                    n.TextTrimming = TextTrimming.CharacterEllipsis;
                    n.HorizontalAlignment = HorizontalAlignment.Center;
                    n.Margin = new Thickness(0, 6, 0, 0);
                    cell.Children.Add(n);
                    UI.Hand(cell);
                    cell.ToolTip = "@" + f.Handle;
                    var friend = f;
                    cell.MouseLeftButtonUp += async (s, e) =>
                    {
                        _people[friend.Id] = friend;
                        Motion.Capture(av, "avatar:" + friend.Id);
                        await ShowProfile(friend.Id);
                    };
                    wrap.Children.Add(cell);
                }
                Motion.Stagger(wrap);
            }
            return Wrap(sp);
        }

        static FrameworkElement SmallLogo()
        {
            var path = new System.Windows.Shapes.Path
            {
                Data = (Geometry)UI.Owner.FindResource("LogoM"), Fill = (Brush)UI.Owner.FindResource("LogoBrush"),
                Width = 17, Height = 14, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center
            };
            return path;
        }

        (FrameworkElement ui, TextBlock value) Stat(string key, Action click)
        {
            var value = UI.T("…", 19, true);
            value.HorizontalAlignment = HorizontalAlignment.Center;
            var label = UI.L(key, 11.5, sub: true);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            var sp = new StackPanel();
            sp.Children.Add(value);
            sp.Children.Add(label);
            var tile = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(6, 10, 6, 10), Margin = new Thickness(4, 0, 4, 0), Child = sp, Cursor = Cursors.Hand };
            tile.SetResourceReference(Border.BackgroundProperty, "HoverBg");
            tile.MouseLeftButtonDown += (s, e) => Motion.Pop(tile, 0.94);
            tile.MouseLeftButtonUp += (s, e) => click();
            return (tile, value);
        }

        static async Task FillStats(string id, TextBlock friends, TextBlock followers, TextBlock following)
        {
            var a = SocialApi.FriendsCount(id);
            var b = SocialApi.FollowersCount(id);
            var c = SocialApi.FollowingCount(id);
            await Task.WhenAll(a, b, c);
            friends.Text = a.Result.ToString();
            followers.Text = b.Result.ToString();
            following.Text = c.Result.ToString();
        }

        void BuildMyActions(Panel actions, StackPanel root, Profile p)
        {
            Border editCard = null;
            var edit = UI.Glass("", "profile.edit", (s, e) =>
            {
                if (editCard != null) { var old = editCard; editCard = null; Motion.Exit(old, 0, () => root.Children.Remove(old)); return; }
                editCard = EditPanel(p);
                root.Children.Insert(root.Children.Count > 1 ? 1 : root.Children.Count, editCard);
                Motion.Enter(editCard);
            }, 9);
            var logout = UI.Glass("", "profile.logout", async (s, e) => { await Supa.SignOutAsync(); }, 9);
            AddAction(actions, edit);
            // Ссылки и оформление — свои панели, как «Редактировать»
            Border linksCard = null, decorCard = null;
            var linksBtn = UI.Glass("\uE71B", "links.button", (s, e) =>
            {
                if (linksCard != null) { var old = linksCard; linksCard = null; Motion.Exit(old, 0, () => root.Children.Remove(old)); return; }
                linksCard = LinksPanel(p);
                root.Children.Insert(root.Children.Count > 1 ? 1 : root.Children.Count, linksCard);
                Motion.Enter(linksCard);
            }, 9);
            var decorBtn = UI.Glass("\uE790", "decor.button", (s, e) =>
            {
                if (decorCard != null) { var old = decorCard; decorCard = null; Motion.Exit(old, 0, () => root.Children.Remove(old)); return; }
                decorCard = DecorPanel(p);
                root.Children.Insert(root.Children.Count > 1 ? 1 : root.Children.Count, decorCard);
                Motion.Enter(decorCard);
            }, 9);
            AddAction(actions, linksBtn);
            AddAction(actions, decorBtn);
            if (p.IsAdmin) AddAction(actions, UI.Glass("\uE7EF", "admin.open", async (s, e) => await ShowAdminHome(), 9));
            AddAction(actions, logout);
        }

        async Task BuildOtherActions(Panel actions, Profile p, Func<Task> refreshStats)
        {
            var followTask = SocialApi.IsFollowing(p.Id);
            var stateTask = SocialApi.StateWith(p.Id);
            var dmTask = p.Bot ? Task.FromResult<(bool ok, string dmFrom, bool blocked)>((false, null, false)) : SocialApi.DmStatus(p.Id);
            await Task.WhenAll(followTask, stateTask, dmTask);
            var dm = dmTask.Result;
            Button msg = null;
            bool following = followTask.Result;
            var (state, rid) = stateTask.Result;

            var follow = new Button { Style = (Style)UI.Owner.FindResource("PrimaryButton") };
            void SetFollow() => follow.SetResourceReference(ContentControl.ContentProperty, following ? "profile.unfollow" : "profile.follow");
            SetFollow();
            follow.Click += async (s, e) =>
            {
                follow.IsEnabled = false;
                string err = following ? await SocialApi.Unfollow(p.Id) : await SocialApi.Follow(p.Id);
                follow.IsEnabled = true;
                if (err != null) { _toast(err); return; }
                following = !following;
                SetFollow();
                Motion.Pop(follow, 0.92);
                // «Подписчики и друзья»: после подписки писать может стать можно (или наоборот)
                if (msg != null && state != FriendState.Friends) { dm = await SocialApi.DmStatus(p.Id); SetMsg(); }
                await refreshStats();
            };
            AddAction(actions, follow);

            string friendKey = state switch
            {
                FriendState.Friends => "friends.remove",
                FriendState.Outgoing => "friends.cancel",
                FriendState.Incoming => "friends.accept",
                _ => "friends.add"
            };
            var friend = UI.Glass(state == FriendState.Friends ? "" : "", friendKey, null, 9);
            friend.Click += async (s, e) =>
            {
                friend.IsEnabled = false;
                string err = state switch
                {
                    FriendState.None => await SocialApi.AddFriend(p.Id),
                    FriendState.Incoming => await SocialApi.Accept(rid),
                    _ => await SocialApi.RemoveRequest(rid)
                };
                friend.IsEnabled = true;
                if (err != null) _toast(err);
                await ShowProfile(p.Id);
            };
            AddAction(actions, friend);

            // «Написать» (0.11.5): друзьям — всегда; остальным — если их настройка «Кто может мне писать» разрешает.
            // Нельзя — кнопка серая, по наведению и нажатию подсказка «rainy принимает сообщения только от друзей»
            if (!p.Bot)
            {
                msg = UI.Glass("", "profile.message", (s, e) =>
                {
                    if (state != FriendState.Friends && !dm.Item1) { _toast(DmClosedText(p, dm.Item2)); return; }
                    _chatWith = p.Id; _groupOpen = 0; _people[p.Id] = p; _navigate("chats");
                }, 9);
                SetMsg();
                AddAction(actions, msg);
            }

            void SetMsg()
            {
                if (msg == null) return;
                bool open = state == FriendState.Friends || dm.Item1;
                msg.Opacity = open ? 1 : 0.45;
                msg.ToolTip = open ? null : DmClosedText(p, dm.Item2);
            }
        }

        Border EditPanel(Profile p)
        {
            var sp = new StackPanel();
            sp.Children.Add(UI.L("profile.edit", 16, true));
            var nick = UI.Field(p.Nickname, 32);
            var handle = UI.Field(p.Handle, 24);
            var bio = UI.Field(p.Bio, 300, multiline: true);
            sp.Children.Add(UI.Labeled("profile.nick", nick));
            sp.Children.Add(UI.Labeled("profile.handle", handle));

            var left = p.HandleChangedAt + p.HandleCooldown - DateTime.UtcNow;
            var hint = p.CanPost
                ? UI.L("profile.handle.staff", 12, sub: true)
                : left > TimeSpan.Zero
                    ? UI.T(string.Format(Loc.T("profile.handle.wait"), (int)left.TotalHours, left.Minutes), 12, sub: true)
                    : UI.L("profile.handle.d", 12, sub: true);
            hint.Margin = new Thickness(2, 6, 0, 0);
            sp.Children.Add(hint);
            sp.Children.Add(UI.Labeled("profile.bio", bio));
            sp.Children.Add(NowPlayingVisibility(p));

            var error = UI.Error();
            sp.Children.Add(error);
            var save = UI.Primary("profile.save", null);
            save.Margin = new Thickness(0, 16, 0, 0);
            save.Click += async (s, e) =>
            {
                UI.ShowError(error, null);
                var ch = new JsonObject();
                string h = handle.Text.Trim().TrimStart('@').ToLowerInvariant();
                if (nick.Text.Trim() != p.Nickname) ch["nickname"] = nick.Text.Trim();
                if (h != p.Handle) ch["handle"] = h;
                if (bio.Text != p.Bio) ch["bio"] = bio.Text;
                if (ch.Count == 0) return;
                if (ch.ContainsKey("handle") && !System.Text.RegularExpressions.Regex.IsMatch(h, "^[a-z0-9_]{3,24}$")) { UI.ShowError(error, Loc.T("err.handle_format")); return; }
                if (ch.ContainsKey("nickname") && nick.Text.Trim().Length == 0) { UI.ShowError(error, Loc.T("err.nick_len")); return; }
                save.IsEnabled = false;
                string err = await SocialApi.UpdateMe(ch);
                save.IsEnabled = true;
                if (err != null) { UI.ShowError(error, err); return; }
                _toast(Loc.T("profile.saved"));
                await ShowProfile(null);
            };
            sp.Children.Add(save);
            return UI.Card(sp);
        }

        static FrameworkElement Pad(FrameworkElement e, double left) { e.Margin = new Thickness(left, 0, 0, 0); return e; }

        async Task ChangeImage(string kind, int maxSide)
        {
            var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp" };
            if (dlg.ShowDialog() != true) return;
            if (new FileInfo(dlg.FileName).Length > 25 * 1024 * 1024) { _toast(Loc.T("err.too_big")); return; }
            _toast(Loc.T("profile.uploading"));
            string url = await SocialApi.UploadImage(dlg.FileName, kind, maxSide);
            if (url == null) { _toast(Loc.T("err.upload")); return; }
            string err = await SocialApi.UpdateMe(new JsonObject { [kind == "avatar" ? "avatar_url" : "banner_url"] = url });
            _toast(err ?? Loc.T("profile.saved"));
            await ShowProfile(null);
        }

        // ================== ДРУЗЬЯ ==================

        void OpenLists(string userId, string tab)
        {
            _pendingFriendsOf = userId == Supa.Me ? null : userId;
            _pendingTab = tab;
            _navigate("friends");
        }

        public Task ShowFriends() => Guard(_friendsHost, ShowFriendsCore);

        async Task ShowFriendsCore()
        {
            if (!Gate(_friendsHost)) return;
            if (!Supa.SignedIn) { Motion.Replace(_friendsHost, NeedLogin()); return; }

            var root = new DockPanel();
            var top = new StackPanel();
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);

            if (SocialApi.MyProfile == null) await SocialApi.LoadMe();
            Profile owner = _friendsOf == null ? SocialApi.MyProfile : (_people.TryGetValue(_friendsOf, out var o) ? o : await SocialApi.GetProfile(_friendsOf));
            if (_friendsOf != null && owner != null)
            {
                var back = UI.Glass("", null, async (s, e) => { _friendsOf = null; _friendsTab = "friends"; await ShowFriends(); }, 8);
                top.Children.Add(UI.Row(back, Pad(UI.T(owner.Nickname, 24, true), 12)));
            }
            else top.Children.Add(UI.H1("nav.friends"));

            // Поиск людей
            var list = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
            if (_friendsOf == null)
            {
                var search = UI.Field("", 40);
                search.Margin = new Thickness(0, 16, 14, 0);
                search.SetResourceReference(FrameworkElement.TagProperty, "friends.search");
                top.Children.Add(search);
                _searchDelay.Tick -= SearchTick;
                _searchDelay.Tick += SearchTick;
                search.TextChanged += (s, e) => { _searchDelay.Stop(); _searchDelay.Start(); };
                _searchBox = search;
                _searchList = list;
            }

            var tabs = new WrapPanel { Margin = new Thickness(0, 16, 0, 10) };
            string[] keys = _friendsOf == null
                ? new[] { "friends", "requests", "followers", "following" }
                : new[] { "friends", "followers", "following" };
            foreach (var k in keys)
            {
                string key = k;
                tabs.Children.Add(UI.Tab("friends.tab." + key, "ftabs", _friendsTab == key, async (s, e) =>
                {
                    _friendsTab = key;
                    if (_searchBox != null) _searchBox.Text = "";
                    await FillList(list, owner?.Id ?? Supa.Me);
                }));
            }
            top.Children.Add(tabs);
            root.Children.Add(UI.Scroll(list));
            Motion.Replace(_friendsHost, root);
            await FillList(list, owner?.Id ?? Supa.Me);
        }

        TextBox _searchBox;
        StackPanel _searchList;

        async void SearchTick(object sender, EventArgs e)
        {
            _searchDelay.Stop();
            if (_searchBox == null || _searchList == null) return;
            string q = _searchBox.Text;
            if (string.IsNullOrWhiteSpace(q)) { await FillList(_searchList, Supa.Me); return; }
            _searchList.Children.Clear();
            _searchList.Children.Add(UI.Loading());
            var found = await SocialApi.Search(q);
            if (_searchBox.Text != q) return;   // пока искали, текст уже поменяли
            _searchList.Children.Clear();
            if (found.Count == 0) { _searchList.Children.Add(Empty("friends.nothing")); return; }
            foreach (var p in found) _searchList.Children.Add(PersonRow(p));
            Motion.Stagger(_searchList);
        }

        async Task FillList(StackPanel list, string ownerId)
        {
            list.Children.Clear();
            list.Children.Add(UI.Loading());
            switch (_friendsTab)
            {
                case "requests":
                {
                    var inc = await SocialApi.Incoming();
                    var outg = await SocialApi.Outgoing();
                    list.Children.Clear();
                    if (inc.Count == 0 && outg.Count == 0) { list.Children.Add(Empty("friends.no_requests")); return; }
                    foreach (var r in inc)
                    {
                        var req = r;
                        var accept = SmallButton("friends.accept", true);
                        var decline = SmallButton("friends.decline", false);
                        accept.Click += async (s, e) => { e.Handled = true; _toast(await SocialApi.Accept(req.Id) ?? Loc.T("friends.accepted")); await FillList(list, ownerId); };
                        decline.Click += async (s, e) => { e.Handled = true; await SocialApi.RemoveRequest(req.Id); await FillList(list, ownerId); };
                        list.Children.Add(UI.UserRow(req.Other, () => _ = OpenProfile(req.Other), accept, decline));
                    }
                    foreach (var r in outg)
                    {
                        var req = r;
                        var cancel = SmallButton("friends.cancel", false);
                        cancel.Click += async (s, e) => { e.Handled = true; await SocialApi.RemoveRequest(req.Id); await FillList(list, ownerId); };
                        list.Children.Add(UI.UserRow(req.Other, () => _ = OpenProfile(req.Other), UI.L("friends.sent", 12, sub: true), cancel));
                    }
                    Motion.Stagger(list);
                    return;
                }
                case "followers":
                    Show(list, await SocialApi.Followers(ownerId), "friends.no_followers");
                    return;
                case "following":
                    Show(list, await SocialApi.Following(ownerId), "friends.no_following");
                    return;
                default:
                    Show(list, await SocialApi.Friends(ownerId), "friends.no_friends");
                    return;
            }
        }

        void Show(StackPanel list, List<Profile> people, string emptyKey)
        {
            list.Children.Clear();
            if (people.Count == 0) { list.Children.Add(Empty(emptyKey)); return; }
            foreach (var p in people) list.Children.Add(PersonRow(p));
            Motion.Stagger(list);
        }

        Button PersonRow(Profile p)
        {
            Button row = null;
            row = UI.UserRow(p, () => { Motion.Capture(UI.RowAvatar(row), "avatar:" + p.Id); _ = OpenProfile(p); });
            return row;
        }

        Task OpenProfile(Profile p)
        {
            _people[p.Id] = p;
            _pendingProfile = p.Id;
            _navigate("profile");
            return Task.CompletedTask;
        }

        static Button SmallButton(string key, bool primary)
        {
            var b = new Button { Style = (Style)UI.Owner.FindResource(primary ? "PrimaryButton" : "GlassButton"), Margin = new Thickness(8, 0, 0, 0) };
            if (!primary) b.Padding = new Thickness(14, 8, 14, 8);
            b.SetResourceReference(ContentControl.ContentProperty, key);
            return b;
        }

        static FrameworkElement Empty(string key)
        {
            var t = UI.L(key, 14, sub: true, wrap: true);
            t.Margin = new Thickness(2, 12, 0, 0);
            return t;
        }

        static FrameworkElement NeedLogin()
        {
            var sp = new StackPanel { MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(UI.H1("social.title"));
            sp.Children.Add(UI.Card(UI.L("social.need_login", 14, sub: true, wrap: true), 22));
            return sp;
        }

    }
}
