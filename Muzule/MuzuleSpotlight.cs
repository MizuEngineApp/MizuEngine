// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>Быстрое окно Muzule по Ctrl+Alt+M — как Spotlight на Mac: спросил, агент сделал, окно закрылось.</summary>
    internal static class MuzuleSpotlight
    {
        static SpotlightWindow _w;

        public static void Toggle()
        {
            if (_w == null) _w = new SpotlightWindow();
            if (_w.IsVisible && _w.IsActive) _w.HideSpot();
            else _w.ShowSpot();
        }

        /// <summary>Вопрос из Spotlight (Ctrl+Alt+Space → «Спросить Muzule»): открыть быстрое окно и сразу отправить.</summary>
        public static void Ask(string text)
        {
            if (_w == null) _w = new SpotlightWindow();
            _w.ShowSpot();
            _w.AskNow(text);
        }
    }

    internal sealed class SpotlightWindow : Window
    {
        const double TextSize = 13.5;
        readonly Grid _root;
        readonly TextBox _input;
        readonly TextBlock _ph, _agentName, _model;
        readonly ContentControl _agentIcon;
        readonly StackPanel _feed;
        readonly ScrollViewer _scroll;
        readonly Border _answerArea, _under;
        MuzuleChat _chat;
        MuzuleRun _run;
        AnswerView _view;
        bool _hiding;

        public SpotlightWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = 760;
            Title = "Muzule";
            UseLayoutRounding = true;

            // Подложка как у уведомлений: плотный слой + фон темы + стеклянная карточка — читается на любом фоне
            _root = new Grid { Margin = new Thickness(30, 20, 30, 40), RenderTransformOrigin = new Point(0.5, 0.2) };
            _root.RenderTransform = new ScaleTransform(1, 1);
            _under = new Border
            {
                CornerRadius = new CornerRadius(22),
                Effect = new DropShadowEffect { BlurRadius = 34, ShadowDepth = 8, Direction = 270, Opacity = 0.45, Color = Colors.Black }
            };
            _root.Children.Add(_under);
            var bg = new Border { CornerRadius = new CornerRadius(22) };
            bg.SetResourceReference(Border.BackgroundProperty, "WinBg");
            _root.Children.Add(bg);
            var card = new Border { CornerRadius = new CornerRadius(22), BorderThickness = new Thickness(1) };
            card.SetResourceReference(Border.BackgroundProperty, "CardBg");
            card.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            _root.Children.Add(card);

            var content = new StackPanel { Margin = new Thickness(18, 14, 18, 12) };
            _root.Children.Add(content);

            // Строка ввода: значок агента, поле, модель
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition());
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _agentIcon = new ContentControl { Focusable = false, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 12, 0) };
            top.Children.Add(_agentIcon);

            var inputHost = new Grid();
            _input = new TextBox
            {
                FontSize = 18, Background = Brushes.Transparent, BorderThickness = new Thickness(0), AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(0, 4, 0, 4), FocusVisualStyle = null, MaxLength = 8000
            };
            _input.SetResourceReference(Control.ForegroundProperty, "Text");
            _input.SetResourceReference(TextBox.CaretBrushProperty, "Text");
            _input.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.SelectionBrushProperty, "Accent");
            _ph = new TextBlock { FontSize = 18, IsHitTestVisible = false, Margin = new Thickness(2, 4, 0, 0), Opacity = 0.6 };
            _ph.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
            inputHost.Children.Add(_input);
            inputHost.Children.Add(_ph);
            Grid.SetColumn(inputHost, 1);
            top.Children.Add(inputHost);
            _input.TextChanged += (s, e) => _ph.Visibility = _input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _input.PreviewKeyDown += OnInputKey;

            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 6, 0, 0) };
            _agentName = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
            _agentName.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            _model = new TextBlock { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right };
            _model.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
            right.Children.Add(_agentName);
            right.Children.Add(_model);
            Grid.SetColumn(right, 2);
            top.Children.Add(right);
            content.Children.Add(top);

            // Ответ
            _feed = new StackPanel { Margin = new Thickness(0, 4, 8, 6) };
            _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 460, Content = _feed, Focusable = false };
            SmoothScroll.Attach(_scroll);
            _answerArea = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(0, 10, 0, 0), Child = _scroll, Visibility = Visibility.Collapsed };
            _answerArea.SetResourceReference(Border.BorderBrushProperty, "Divider");
            content.Children.Add(_answerArea);

            var hint = new TextBlock { FontSize = 11, Margin = new Thickness(0, 10, 0, 0), Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis };
            hint.SetResourceReference(TextBlock.TextProperty, "muzule.spot.hint");
            hint.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
            content.Children.Add(hint);

            Content = _root;
            Deactivated += (s, e) => HideSpot();
            PreviewKeyDown += OnKey;
            Muzule.SpotWatching = id => IsVisible && _chat?.Id == id;
            SizeChanged += (s, e) => Place();
        }

        // ================== показать / спрятать ==================

        public void ShowSpot()
        {
            _hiding = false;
            // Прошлый быстрый чат продолжаем, если он был недавно; иначе — новый
            if (_chat == null || (Muzule.RunOf(_chat.Id) == null && (DateTime.Now - _chat.Updated).TotalMinutes > 15)) NewChat();
            else UpdateHeader();
            // Светлая тема — светлая подложка, тёмная — тёмная (тему могли сменить, пока окно было спрятано)
            bool light = Application.Current.TryFindResource("Text") is SolidColorBrush tb && (0.299 * tb.Color.R + 0.587 * tb.Color.G + 0.114 * tb.Color.B) / 255 < 0.5;
            _under.Background = new SolidColorBrush(light ? Color.FromArgb(0xF8, 0xF7, 0xF9, 0xFC) : Color.FromArgb(0xF4, 0x12, 0x16, 0x20));
            Place();
            _root.Opacity = 0;
            if (!IsVisible) Show();
            Activate();
            _input.Focus();
            Keyboard.Focus(_input);
            var sc = (ScaleTransform)_root.RenderTransform;
            Anim.To(_root, UIElement.OpacityProperty, 1, Motion.Off ? 1 : 180, from: 0);
            Anim.To(sc, ScaleTransform.ScaleXProperty, 1, Motion.Off ? 1 : 320, from: 0.965, ease: Anim.Spring);
            Anim.To(sc, ScaleTransform.ScaleYProperty, 1, Motion.Off ? 1 : 320, from: 0.965, ease: Anim.Spring);
        }

        public void HideSpot()
        {
            if (!IsVisible || _hiding) return;
            _hiding = true;
            Anim.To(_root, UIElement.OpacityProperty, 0, Motion.Off ? 1 : 130, done: () =>
            {
                if (_hiding) { Hide(); _hiding = false; }
            });
        }

        void Place()
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - Width) / 2;
            Top = area.Top + area.Height * 0.16;
        }

        void NewChat()
        {
            DetachRun();
            var agent = MuzuleStore.Agent(Muzule.S.MuzuleAgent);
            _chat = MuzuleStore.NewChat(agent.Id, spotlight: true);
            _chat.Model = agent.Model ?? Muzule.S.MuzuleModel;
            _feed.Children.Clear();
            _answerArea.Visibility = Visibility.Collapsed;
            UpdateHeader();
        }

        void UpdateHeader()
        {
            var agent = MuzuleStore.Agent(_chat.AgentId);
            _agentIcon.Content = Md.AgentIcon(agent, 30);
            _agentName.Text = agent.Name;
            _model.Text = MuzuleStore.ModelShort(MuzuleStore.ValidModel(_chat.Model ?? agent.Model ?? Muzule.S.MuzuleModel));
            _ph.Text = string.Format(Loc.T("muzule.spot.ph"), agent.Name);
            _ph.Visibility = _input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ================== клавиши ==================

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { e.Handled = true; HideSpot(); return; }
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key == Key.N)
            {
                e.Handled = true;
                if (Muzule.RunOf(_chat?.Id) == null) NewChat();
                return;
            }
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key == Key.O)
            {
                e.Handled = true;
                string id = _chat?.Messages.Count > 0 ? _chat.Id : null;
                HideSpot();
                if (id != null) Muzule.OpenChat(id); else Muzule.OpenPage("NavMuzule");
            }
        }

        void OnInputKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                e.Handled = true;
                Send();
            }
        }

        // ================== отправка и ответ ==================

        public void AskNow(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            _input.Text = text;
            _input.CaretIndex = _input.Text.Length;
            Send();
        }

        void Send()
        {
            string text = _input.Text.Trim();
            if (text.Length == 0 || _chat == null || Muzule.RunOf(_chat.Id) != null) return;
            if (!Muzule.Ready) { HideSpot(); Muzule.OpenPage("NavMuzule"); return; }
            var run = Muzule.Send(_chat, text);
            if (run == null) return;
            _input.Text = "";
            _answerArea.Visibility = Visibility.Visible;

            var q = new TextBlock { Text = text, FontSize = TextSize, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, _feed.Children.Count > 0 ? 16 : 0, 0, 8) };
            q.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            _feed.Children.Add(q);
            var view = new AnswerView(TextSize, Muzule.S.MuzuleTyping);
            _feed.Children.Add(view.Root);
            AttachRun(run, view);
            view.ShowThinking();
            ScrollEnd();
        }

        void AttachRun(MuzuleRun run, AnswerView view)
        {
            DetachRun();
            _run = run;
            _view = view;
            view.Changed = ScrollEnd;
            run.Text += OnText;
            run.ToolStarted += OnTool;
            run.ToolDone += OnToolDone;
            run.RoundStarted += OnRound;
            run.Finished += OnFinished;
        }

        void DetachRun()
        {
            if (_run == null) return;
            _run.Text -= OnText;
            _run.ToolStarted -= OnTool;
            _run.ToolDone -= OnToolDone;
            _run.RoundStarted -= OnRound;
            _run.Finished -= OnFinished;
            _run = null;
            if (_view != null) _view.Changed = null;
            _view = null;
        }

        void OnText(string t) => _view?.Text(t);
        void OnTool(string n) => _view?.ToolStart(n);
        void OnToolDone(string n, string r, bool ok) => _view?.ToolDone(n, ok);
        void OnRound() => _view?.ShowThinking();

        void OnFinished()
        {
            var run = _run;
            var view = _view;
            if (run == null || view == null) return;
            if (run.Error != null) view.Error(Loc.T(run.Error), run.ErrorDetail);
            if (run.Note != null) view.Note(Loc.T(run.Note));
            if (run.Cancelled) view.Note(Loc.T("muzule.stopped"));
            view.Finish();
            DetachRun();
            ScrollEnd();
        }

        void ScrollEnd() => Dispatcher.BeginInvoke(new Action(() => _scroll.ScrollToEnd()), DispatcherPriority.Background);
    }
}
