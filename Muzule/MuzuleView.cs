// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>
    /// Простой Markdown для ответов Claude: абзацы, **жирный**, *курсив*, `код`, блоки кода, списки, заголовки, цитаты, ссылки.
    /// Только ресурсы приложения (Text, SubText, Accent, HoverBg) — работает и в главном окне, и в быстром окне.
    /// </summary>
    internal static class Md
    {
        public static readonly FontFamily Mono = new FontFamily("Cascadia Mono, Consolas, Courier New");
        public static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        public static FrameworkElement Render(string md, double size = 14)
        {
            var root = new StackPanel();
            var lines = (md ?? "").Replace("\r", "").Split('\n');
            var para = new List<string>();

            void Add(FrameworkElement e, double top)
            {
                if (root.Children.Count > 0) e.Margin = new Thickness(e.Margin.Left, top, e.Margin.Right, e.Margin.Bottom);
                root.Children.Add(e);
            }
            void FlushPara()
            {
                if (para.Count == 0) return;
                Add(Paragraph(string.Join("\n", para), size), size * 0.6);
                para.Clear();
            }

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string t = line.Trim();
                if (t.StartsWith("```"))
                {
                    FlushPara();
                    var code = new StringBuilder();
                    i++;
                    while (i < lines.Length && !lines[i].Trim().StartsWith("```")) { code.Append(lines[i]).Append('\n'); i++; }
                    Add(CodeBlock(code.ToString().TrimEnd('\n'), size), size * 0.6);
                    continue;
                }
                if (t.Length == 0) { FlushPara(); continue; }
                if (t.StartsWith("#"))
                {
                    int lvl = t.TakeWhile(c => c == '#').Count();
                    if (lvl <= 6 && t.Length > lvl && t[lvl] == ' ')
                    {
                        FlushPara();
                        Add(Heading(t.Substring(lvl).Trim(), size, lvl), size * 0.9);
                        continue;
                    }
                }
                if (t == "---" || t == "***" || t == "___")
                {
                    FlushPara();
                    var hr = new Border { Height = 1, Opacity = 0.6 };
                    hr.SetResourceReference(Border.BackgroundProperty, "Divider");
                    Add(hr, size * 0.7);
                    continue;
                }
                var item = ListMarker(t);
                if (item != null)
                {
                    FlushPara();
                    int indent = line.Length - line.TrimStart().Length;
                    Add(ListItem(item.Value.marker, item.Value.text, size, Math.Min(indent / 2, 3)), size * 0.3);
                    continue;
                }
                if (t.StartsWith(">"))
                {
                    FlushPara();
                    Add(Quote(t.TrimStart('>').Trim(), size), size * 0.5);
                    continue;
                }
                para.Add(t);
            }
            FlushPara();
            return root;
        }

        static (string marker, string text)? ListMarker(string t)
        {
            if (t.Length > 2 && (t[0] == '-' || t[0] == '*' || t[0] == '•') && t[1] == ' ') return ("•", t.Substring(2));
            int d = 0;
            while (d < t.Length && d < 3 && char.IsDigit(t[d])) d++;
            if (d > 0 && d + 1 < t.Length && (t[d] == '.' || t[d] == ')') && t[d + 1] == ' ') return (t.Substring(0, d) + ".", t.Substring(d + 2));
            return null;
        }

        public static TextBlock Paragraph(string text, double size)
        {
            var tb = new TextBlock { FontSize = size, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.45 };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            AddInlines(tb.Inlines, text);
            return tb;
        }

        static FrameworkElement Heading(string text, double size, int lvl)
        {
            var tb = Paragraph(text, size + (lvl <= 1 ? 5 : lvl == 2 ? 3 : 1));
            tb.FontWeight = FontWeights.SemiBold;
            return tb;
        }

        static FrameworkElement ListItem(string marker, string text, double size, int level)
        {
            var g = new Grid { Margin = new Thickness(level * 16 + 2, 0, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(marker == "•" ? 16 : 24) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var m = new TextBlock { Text = marker, FontSize = size, LineHeight = size * 1.45 };
            m.SetResourceReference(TextBlock.ForegroundProperty, marker == "•" ? "Accent" : "SubText");
            g.Children.Add(m);
            var p = Paragraph(text, size);
            Grid.SetColumn(p, 1);
            g.Children.Add(p);
            return g;
        }

        static FrameworkElement Quote(string text, double size)
        {
            var b = new Border { BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(12, 2, 0, 2) };
            b.SetResourceReference(Border.BorderBrushProperty, "Accent");
            var p = Paragraph(text, size);
            p.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
            b.Child = p;
            return b;
        }

        static FrameworkElement CodeBlock(string code, double size)
        {
            var box = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 9, 12, 10), BorderThickness = new Thickness(1) };
            box.SetResourceReference(Border.BackgroundProperty, "HoverBg");
            box.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            var g = new Grid();
            var tb = new TextBox
            {
                Text = code, IsReadOnly = true, FontFamily = Mono, FontSize = size - 1.5, TextWrapping = TextWrapping.Wrap,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0, 0, 70, 0),
                IsTabStop = false
            };
            tb.SetResourceReference(Control.ForegroundProperty, "Text");
            tb.SetResourceReference(TextBoxBase.SelectionBrushProperty, "Accent");
            g.Children.Add(tb);
            var copy = SmallButton("", Loc.T("muzule.copy"), () =>
            {
                try { Clipboard.SetText(code); } catch { }
            });
            copy.HorizontalAlignment = HorizontalAlignment.Right;
            copy.VerticalAlignment = VerticalAlignment.Top;
            copy.Margin = new Thickness(0, -3, -4, 0);
            g.Children.Add(copy);
            box.Child = g;
            return box;
        }

        /// <summary>Разбор одной строки: **жирный**, *курсив*, `код`, [ссылка](https://…).</summary>
        static void AddInlines(InlineCollection inl, string s)
        {
            var sb = new StringBuilder();
            void Flush() { if (sb.Length > 0) { inl.Add(new Run(sb.ToString())); sb.Clear(); } }
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '\n') { Flush(); inl.Add(new LineBreak()); i++; continue; }
                if (c == '`')
                {
                    int j = s.IndexOf('`', i + 1);
                    if (j > i + 1)
                    {
                        Flush();
                        var r = new Run("\u2009" + s.Substring(i + 1, j - i - 1) + "\u2009") { FontFamily = Mono };
                        r.SetResourceReference(TextElement.BackgroundProperty, "HoverBg");
                        inl.Add(r);
                        i = j + 1;
                        continue;
                    }
                }
                if (c == '*' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    int j = s.IndexOf("**", i + 2, StringComparison.Ordinal);
                    if (j > i + 2)
                    {
                        Flush();
                        var b = new Bold();
                        AddInlines(b.Inlines, s.Substring(i + 2, j - i - 2));
                        inl.Add(b);
                        i = j + 2;
                        continue;
                    }
                }
                if (c == '*' && i + 1 < s.Length && s[i + 1] != ' ' && (i == 0 || !char.IsLetterOrDigit(s[i - 1])))
                {
                    int j = s.IndexOf('*', i + 1);
                    if (j > i + 1 && s[j - 1] != ' ')
                    {
                        Flush();
                        var it = new Italic();
                        AddInlines(it.Inlines, s.Substring(i + 1, j - i - 1));
                        inl.Add(it);
                        i = j + 1;
                        continue;
                    }
                }
                if (c == '[')
                {
                    int j = s.IndexOf("](", i + 1, StringComparison.Ordinal);
                    int k = j > i ? s.IndexOf(')', j + 2) : -1;
                    if (j > i && k > j)
                    {
                        string text = s.Substring(i + 1, j - i - 1), url = s.Substring(j + 2, k - j - 2).Trim();
                        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                        {
                            Flush();
                            inl.Add(Link(text, url));
                            i = k + 1;
                            continue;
                        }
                    }
                }
                sb.Append(c);
                i++;
            }
            Flush();
        }

        public static Hyperlink Link(string text, string url)
        {
            var h = new Hyperlink(new Run(string.IsNullOrEmpty(text) ? url : text)) { ToolTip = url, TextDecorations = null, Cursor = Cursors.Hand };
            h.SetResourceReference(TextElement.ForegroundProperty, "Accent");
            try { h.NavigateUri = new Uri(url); } catch { }
            h.RequestNavigate += (s, e) =>
            {
                e.Handled = true;
                OpenUrl(url);
            };
            return h;
        }

        public static void OpenUrl(string url)
        {
            if (!(url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception e) { Settings.Log(e); }
        }

        /// <summary>Маленькая текстовая кнопка со значком (копировать, повторить…).</summary>
        public static Border SmallButton(string glyph, string text, Action click)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            ic.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
            sp.Children.Add(ic);
            if (!string.IsNullOrEmpty(text))
            {
                var t = new TextBlock { Text = text, FontSize = 11.5, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                t.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
                sp.Children.Add(t);
            }
            var b = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(7, 3, 8, 4), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = sp };
            b.MouseEnter += (s, e) => b.SetResourceReference(Border.BackgroundProperty, "HoverBg");
            b.MouseLeave += (s, e) => b.Background = Brushes.Transparent;
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; click?.Invoke(); };
            return b;
        }

        /// <summary>Цветной скруглённый значок агента (не зависит от стилей главного окна).</summary>
        public static FrameworkElement AgentIcon(MuzuleAgent a, double size)
        {
            var c = MuzuleStore.ColorOf(a?.Color);
            var b = new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.3), VerticalAlignment = VerticalAlignment.Top,
                Background = new LinearGradientBrush(c.a, c.b, 90),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)), BorderThickness = new Thickness(0.8)
            };
            b.Child = new TextBlock
            {
                Text = a?.Glyph ?? "", FontFamily = Icons, FontSize = size * 0.5, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            return b;
        }
    }

    /// <summary>
    /// Ответ агента в ленте: текст печатается плавно, вызовы инструментов — «плашки» с крутилкой, потом галочкой.
    /// Используется и для живого ответа, и для истории.
    /// </summary>
    internal sealed class AnswerView
    {
        public readonly StackPanel Root = new StackPanel();
        /// <summary>Содержимое изменилось — лента может прокрутиться вниз.</summary>
        public Action Changed;

        readonly double _size;
        readonly bool _typing;
        readonly DispatcherTimer _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        readonly StringBuilder _pending = new StringBuilder();
        ContentControl _seg;           // текущий кусок текста (между плашками)
        string _shown = "";
        DateTime _rendered;
        FrameworkElement _dots;
        readonly List<Chip> _chips = new List<Chip>();
        readonly Dictionary<string, string> _sources = new Dictionary<string, string>();   // ссылка → заголовок (поиск в интернете)
        readonly StringBuilder _all = new StringBuilder();

        sealed class Chip { public string Name, Id; public Border Box; public TextBlock Icon; public RotateTransform Spin; public bool Done; }

        public AnswerView(double size, bool typing)
        {
            _size = size;
            _typing = typing;
            _timer.Tick += (s, e) => Reveal();
        }

        /// <summary>Весь текст ответа (для кнопки «Копировать»).</summary>
        public string AllText => (_all.ToString() + _pending).Trim();

        public bool IsEmpty => Root.Children.Count == 0 || (Root.Children.Count == 1 && _dots != null);

        // ================== живой ответ ==================

        public void ShowThinking()
        {
            if (_dots != null) return;
            _dots = Dots();
            _dots.Margin = new Thickness(0, Root.Children.Count > 0 ? 8 : 4, 0, 4);
            Root.Children.Add(_dots);
            Changed?.Invoke();
        }

        void HideThinking()
        {
            if (_dots == null) return;
            Root.Children.Remove(_dots);
            _dots = null;
        }

        public void Text(string delta)
        {
            if (string.IsNullOrEmpty(delta)) return;
            HideThinking();
            DoneSearches();
            _all.Append(delta);
            if (_seg == null) StartSegment();
            if (!_typing) { _shown += delta; Render(false); return; }
            _pending.Append(delta);
            if (!_timer.IsEnabled) _timer.Start();
        }

        /// <summary>Текст, который уже был напечатан до того, как вкладку открыли (без «печати»).</summary>
        public void Seed(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            HideThinking();
            _all.Append(text);
            if (_seg == null) StartSegment();
            _shown += text;
            Render(true);
        }

        void StartSegment()
        {
            _seg = new ContentControl { Focusable = false, IsTabStop = false };
            if (Root.Children.Count > 0) _seg.Margin = new Thickness(0, 8, 0, 0);
            _shown = "";
            Root.Children.Add(_seg);
        }

        void Reveal()
        {
            if (_pending.Length == 0) { _timer.Stop(); Render(true); return; }
            // Чем больше накопилось, тем быстрее печатаем — догоняем поток, но всегда плавно
            int n = Math.Min(_pending.Length, Math.Max(2, _pending.Length / 7));
            if (n < _pending.Length && char.IsHighSurrogate(_pending[n - 1])) n++;
            _shown += _pending.ToString(0, n);
            _pending.Remove(0, n);
            Render(_pending.Length == 0);
        }

        void Render(bool force)
        {
            if (_seg == null) return;
            var now = DateTime.UtcNow;
            if (!force && (now - _rendered).TotalMilliseconds < 45) return;   // Markdown пересобираем не чаще ~20 раз в секунду
            _rendered = now;
            _seg.Content = Md.Render(_shown, _size);
            Changed?.Invoke();
        }

        /// <summary>Всё, что ждёт «печати», — показать сразу (перед плашкой инструмента и в конце).</summary>
        void FlushText()
        {
            if (_pending.Length > 0) { _shown += _pending.ToString(); _pending.Clear(); }
            _timer.Stop();
            Render(true);
        }

        public void ToolStart(string name, string id = null, string detail = null)
        {
            HideThinking();
            FlushText();
            _seg = null;
            var chip = MakeChip(name, detail);
            chip.Id = id;
            _chips.Add(chip);
            Root.Children.Add(chip.Box);
            Changed?.Invoke();
        }

        public void ToolDone(string name, bool ok)
        {
            var c = _chips.FirstOrDefault(x => !x.Done && x.Name == name);
            if (c != null) SetDone(c, ok);
        }

        /// <summary>Ответ инструмента из истории — по его номеру.</summary>
        public void ToolResult(string id, bool ok)
        {
            var c = _chips.FirstOrDefault(x => !x.Done && x.Id == id);
            if (c != null) SetDone(c, ok);
        }

        void DoneSearches()
        {
            foreach (var c in _chips.Where(x => !x.Done && x.Name == "web_search").ToList()) SetDone(c, true);
        }

        public void Error(string message, string detail, Action fix = null, string fixText = null)
        {
            HideThinking();
            FlushText();
            _seg = null;
            foreach (var c in _chips.Where(x => !x.Done).ToList()) SetDone(c, false);
            var box = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 9, 12, 10), Margin = new Thickness(0, Root.Children.Count > 0 ? 8 : 0, 0, 0), BorderThickness = new Thickness(1) };
            box.Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0x5F, 0x57));
            box.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0x5F, 0x57));
            var sp = new StackPanel();
            var t = new TextBlock { Text = message, FontSize = 13.5, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
            t.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            sp.Children.Add(t);
            if (!string.IsNullOrWhiteSpace(detail))
            {
                var d = new TextBlock { Text = detail, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Opacity = 0.85 };
                d.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
                sp.Children.Add(d);
            }
            if (fix != null)
            {
                var b = Md.SmallButton("", fixText, fix);
                b.HorizontalAlignment = HorizontalAlignment.Left;
                b.Margin = new Thickness(-7, 6, 0, 0);
                sp.Children.Add(b);
            }
            box.Child = sp;
            Root.Children.Add(box);
            Changed?.Invoke();
        }

        public void Note(string message)
        {
            var t = new TextBlock { Text = message, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), FontStyle = FontStyles.Italic };
            t.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
            Root.Children.Add(t);
            Changed?.Invoke();
        }

        /// <summary>Ответ закончен: допечатать, закрыть плашки, показать источники и кнопку «Копировать».</summary>
        public void Finish(bool copyButton = true)
        {
            HideThinking();
            FlushText();
            _seg = null;
            foreach (var c in _chips.Where(x => !x.Done).ToList()) SetDone(c, true, neutral: true);
            if (_sources.Count > 0) Root.Children.Add(Sources());
            if (copyButton && AllText.Length > 0)
            {
                string text = AllText;
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-7, 6, 0, 0) };
                Border copy = null;
                copy = Md.SmallButton("", Loc.T("muzule.copy"), () =>
                {
                    try { Clipboard.SetText(text); } catch { }
                    if (copy?.Child is StackPanel sp && sp.Children.Count > 1 && sp.Children[1] is TextBlock tt) tt.Text = Loc.T("muzule.copied");
                });
                row.Children.Add(copy);
                Root.Children.Add(row);
            }
            Changed?.Invoke();
        }

        // ================== история ==================

        public void AddText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _all.Append(text).Append("\n\n");
            var seg = new ContentControl { Focusable = false, IsTabStop = false, Content = Md.Render(text, _size) };
            if (Root.Children.Count > 0) seg.Margin = new Thickness(0, 8, 0, 0);
            Root.Children.Add(seg);
        }

        public void AddCitations(JsonArray cites)
        {
            if (cites == null) return;
            foreach (var c in cites)
            {
                string url = (string)c?["url"];
                if (string.IsNullOrEmpty(url) || _sources.ContainsKey(url)) continue;
                _sources[url] = (string)c["title"] ?? url;
            }
        }

        // ================== части ==================

        Chip MakeChip(string name, string detail)
        {
            var c = new Chip { Name = name };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            c.Icon = new TextBlock { Text = "", FontFamily = Md.Icons, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5) };
            c.Icon.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            var rot = new RotateTransform();
            c.Icon.RenderTransform = rot;
            c.Spin = rot;
            // Крутится, только пока плашка на экране: бесконечная анимация вне окна зря будила бы видеокарту
            void Start()
            {
                if (c.Done) return;
                var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever };
                Timeline.SetDesiredFrameRate(spin, 30);
                rot.BeginAnimation(RotateTransform.AngleProperty, spin);
            }
            c.Icon.Loaded += (s, e) => Start();
            c.Icon.Unloaded += (s, e) => rot.BeginAnimation(RotateTransform.AngleProperty, null);
            sp.Children.Add(c.Icon);
            string label = Muzule.ToolLabel(name);
            if (!string.IsNullOrEmpty(detail)) label += ": " + detail;
            var t = new TextBlock { Text = label, FontSize = 12.5, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 420 };
            t.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
            sp.Children.Add(t);
            c.Box = new Border
            {
                CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 5, 12, 6), BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, Root.Children.Count > 0 ? 8 : 0, 0, 0), Child = sp
            };
            c.Box.SetResourceReference(Border.BackgroundProperty, "HoverBg");
            c.Box.SetResourceReference(Border.BorderBrushProperty, "CardRim");
            return c;
        }

        static void SetDone(Chip c, bool ok, bool neutral = false)
        {
            c.Done = true;
            c.Spin?.BeginAnimation(RotateTransform.AngleProperty, null);
            c.Icon.RenderTransform = Transform.Identity;
            c.Icon.Text = ok ? "" : "";   // галочка / крестик
            if (neutral) c.Icon.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
            else if (ok) c.Icon.Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0xC7, 0x59));
            else c.Icon.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x5F, 0x57));
        }

        FrameworkElement Sources()
        {
            var wrap = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            var h = new TextBlock { Text = Loc.T("muzule.sources"), FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
            h.SetResourceReference(TextBlock.ForegroundProperty, "SubText");
            wrap.Children.Add(h);
            foreach (var kv in _sources.Take(6))
            {
                var tb = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 1) };
                string host = kv.Key;
                try { host = new Uri(kv.Key).Host.Replace("www.", ""); } catch { }
                tb.Inlines.Add(Md.Link(kv.Value, kv.Key));
                var hr = new Run("  ·  " + host);
                hr.SetResourceReference(TextElement.ForegroundProperty, "SubText");
                tb.Inlines.Add(hr);
                wrap.Children.Add(tb);
            }
            return wrap;
        }

        static FrameworkElement Dots()
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Height = 18 };
            for (int i = 0; i < 3; i++)
            {
                var e = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 5, 0), Opacity = 0.25, VerticalAlignment = VerticalAlignment.Center };
                e.SetResourceReference(Shape.FillProperty, "Accent");
                int k = i;
                // Точки «дышат», только пока видны (убрали из ленты — анимация останавливается)
                e.Loaded += (s, ev) =>
                {
                    var a = new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(420))
                    {
                        AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(k * 160),
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                    };
                    Timeline.SetDesiredFrameRate(a, 30);
                    e.BeginAnimation(UIElement.OpacityProperty, a);
                };
                e.Unloaded += (s, ev) => e.BeginAnimation(UIElement.OpacityProperty, null);
                sp.Children.Add(e);
            }
            return sp;
        }

        // ================== лента целиком ==================

        /// <summary>
        /// Рисует историю чата в ленту. Возвращает последний ответ агента, если переписка закончилась на нём
        /// (к нему продолжит печататься живой ответ, когда вкладку открыли посреди работы агента).
        /// </summary>
        public static AnswerView RenderHistory(Panel feed, MuzuleChat chat, MuzuleAgent agent, double size, Func<FrameworkElement, AnswerView, FrameworkElement> wrapAnswer, Func<string, FrameworkElement> userBubble)
        {
            AnswerView cur = null;
            void Close() { cur?.Finish(); cur = null; }
            foreach (var node in chat.Messages)
            {
                if (node is not JsonObject m) continue;
                string role = (string)m["role"];
                if (role == "user")
                {
                    if (m["content"] is JsonArray arr)
                    {
                        var text = new StringBuilder();
                        foreach (var b in arr)
                        {
                            string bt = (string)b?["type"];
                            if (bt == "text") text.Append((string)b["text"]).Append('\n');
                            else if (bt == "tool_result") cur?.ToolResult((string)b["tool_use_id"], b["is_error"] is JsonValue v && v.TryGetValue(out bool err) ? !err : true);
                        }
                        if (text.Length > 0) { Close(); feed.Children.Add(userBubble(text.ToString().Trim())); }
                    }
                    else
                    {
                        Close();
                        feed.Children.Add(userBubble((string)m["content"] ?? ""));
                    }
                    continue;
                }
                if (role != "assistant" || m["content"] is not JsonArray content) continue;
                if (cur == null)
                {
                    cur = new AnswerView(size, false);
                    feed.Children.Add(wrapAnswer(cur.Root, cur));
                }
                foreach (var b in content)
                {
                    switch ((string)b?["type"])
                    {
                        case "text":
                            cur.AddText((string)b["text"]);
                            cur.AddCitations(b["citations"] as JsonArray);
                            break;
                        case "tool_use":
                            cur.ToolStart((string)b["name"], (string)b["id"]);
                            break;
                        case "server_tool_use":
                            cur.ToolStart((string)b["name"], (string)b["id"], (string)b["input"]?["query"]);
                            break;
                        case "web_search_tool_result":
                            cur.ToolResult((string)b["tool_use_id"], b["content"] is JsonArray);
                            break;
                    }
                }
            }
            return cur;
        }
    }
}
