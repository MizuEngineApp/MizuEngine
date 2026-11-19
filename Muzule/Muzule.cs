// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>
    /// Muzule — Claude внутри MizuEngine (только Mizu Plus, ключ Claude API у каждого свой).
    /// Здесь общее для вкладки и быстрого окна: запуск агентов, статус в Dynamic Island, уведомление о готовом ответе.
    /// Агент продолжает работать, даже если окно MizuEngine закрыли в трей.
    /// </summary>
    internal static class Muzule
    {
        public static Settings S { get; private set; }
        public static Effects Fx { get; private set; }
        static Action _openMain;
        static Dispatcher _ui;
        static readonly Dictionary<string, MuzuleRun> _runs = new Dictionary<string, MuzuleRun>();

        /// <summary>Какой чат человек сейчас видит (вкладка или быстрое окно) — по нему решаем, нужно ли уведомление.</summary>
        public static Func<string, bool> PageWatching, SpotWatching;

        /// <summary>Запуск начался или закончился (для кнопок «Стоп» и точек в списке чатов).</summary>
        public static event Action RunsChanged;

        public static void Init(Settings s, Effects fx, Action openMain)
        {
            S = s; Fx = fx; _openMain = openMain;
            _ui = Dispatcher.CurrentDispatcher;
            S.MuzuleModel = MuzuleStore.ValidModel(S.MuzuleModel);
        }

        /// <summary>Можно ли сейчас пользоваться: есть Plus и ключ.</summary>
        public static bool Ready => Plus.IsActive && MuzuleStore.HasKey;

        public static MuzuleRun RunOf(string chatId) => chatId != null && _runs.TryGetValue(chatId, out var r) ? r : null;
        public static bool AnyRunning => _runs.Count > 0;

        /// <summary>Отправить сообщение в чат и запустить агента. Возвращает запуск (на его события подписывается интерфейс).</summary>
        public static MuzuleRun Send(MuzuleChat chat, string text)
        {
            if (!Plus.IsActive || !MuzuleStore.HasKey || chat == null || string.IsNullOrWhiteSpace(text)) return null;
            if (_runs.ContainsKey(chat.Id)) return null;
            var run = new MuzuleRun(chat, MuzuleStore.Agent(chat.AgentId));
            _runs[chat.Id] = run;
            RunsChanged?.Invoke();
            Island(Loc.T("muzule.island.thinking"));
            run.ToolStarted += n => Island(ToolLabel(n));
            run.Text += t => IslandWriting();
            run.Finished += () => OnFinished(run);
            _ = run.RunAsync(text.Trim());
            return run;
        }

        public static void StopAll()
        {
            foreach (var r in _runs.Values.ToList())
            {
                r.Stop();
                MuzuleStore.SaveChat(r.Chat);   // при выходе из программы — сохранить всё, что агент уже успел
            }
        }

        static void OnFinished(MuzuleRun run)
        {
            _runs.Remove(run.Chat.Id);
            RunsChanged?.Invoke();
            if (_runs.Count == 0) Island(null);
            if (run.Cancelled) return;

            // Человек смотрит в этот чат — уведомление не нужно
            bool watching = (PageWatching?.Invoke(run.Chat.Id) ?? false) || (SpotWatching?.Invoke(run.Chat.Id) ?? false);
            if (watching) return;
            string text = run.Error != null ? Loc.T(run.Error) : run.LastText;
            if (string.IsNullOrWhiteSpace(text)) text = Loc.T("muzule.island.done");
            text = Plain(text);
            if (text.Length > 140) text = text.Substring(0, 138) + "…";
            var agent = MuzuleStore.Agent(run.Chat.AgentId);
            string id = run.Chat.Id;
            var ui = _ui;
            if (S.MuzuleIsland && S.IslandOn && S.IslandNotify)
                global::MizuEngine.Shell.Run(() => global::MizuEngine.Shell.Island?.Notify(agent.Name, text, null, () => ui.BeginInvoke(new Action(() => OpenChat(id)))));
            if (S.MsgSounds && !S.NotifyDnd) ChatSounds.PlayIn();
        }

        // ================== Dynamic Island ==================

        static DateTime _writingShown;

        static void Island(string status)
        {
            if (S == null) return;
            if (!S.MuzuleIsland) status = null;
            var ui = _ui;
            global::MizuEngine.Shell.Island?.Agent(status, status == null ? null : (Action)(() => ui.BeginInvoke(new Action(() => OpenPage("NavMuzule")))));
        }

        static void IslandWriting()
        {
            // Текст приходит десятками кусочков в секунду — статус меняем не чаще раза в секунду
            if ((DateTime.UtcNow - _writingShown).TotalSeconds < 1) return;
            _writingShown = DateTime.UtcNow;
            Island(Loc.T("muzule.island.writing"));
        }

        public static string ToolLabel(string name)
        {
            string key = "muzule.tool." + name;
            string t = Loc.T(key);
            return t == key ? Loc.T("muzule.tool.other") : t;
        }

        // ================== окно ==================

        public static void OpenPage(string nav)
        {
            _openMain?.Invoke();
            MainWindow.Current?.MuzuleOpenNav(nav);
        }

        public static void OpenChat(string chatId)
        {
            _openMain?.Invoke();
            MainWindow.Current?.MuzuleShowChat(chatId);
        }

        /// <summary>Быстрое окно (Ctrl+Alt+M). Без Plus или ключа — открывается вкладка Muzule с подсказкой.</summary>
        public static void Spotlight()
        {
            if (!S.MuzuleSpotlight) return;
            if (!Ready) { OpenPage("NavMuzule"); return; }
            MuzuleSpotlight.Toggle();
        }

        /// <summary>Markdown → простой текст (для уведомлений).</summary>
        public static string Plain(string md)
        {
            var sb = new StringBuilder();
            foreach (var raw in (md ?? "").Split('\n'))
            {
                string l = raw.Trim();
                if (l.StartsWith("```")) continue;
                l = l.TrimStart('#', '>', ' ');
                if (l.StartsWith("- ") || l.StartsWith("* ")) l = "• " + l.Substring(2);
                l = l.Replace("**", "").Replace("__", "").Replace("`", "");
                if (l.Length == 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(l);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Один запуск агента: сообщение человека → ответ Claude → (инструменты → ответ)… пока Claude не закончит.
    /// Все события приходят в главном потоке.
    /// </summary>
    internal sealed class MuzuleRun
    {
        const int MaxRounds = 16;          // столько раз подряд агент может вызвать инструменты в одном ответе
        const int MaxSend = 80;            // сколько последних сообщений отправлять (старые — дорого и не нужно)
        const int MaxTokens = 8000;

        public readonly MuzuleChat Chat;
        readonly MuzuleAgent _agent;
        readonly CancellationTokenSource _cts = new CancellationTokenSource();
        readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;

        public event Action<string> Text;                  // новый кусочек текста
        public event Action<string> ToolStarted;           // Claude начал вызывать инструмент
        public event Action<string, string, bool> ToolDone;   // имя, что сделано, успех
        public event Action RoundStarted;                  // новый запрос к Claude (после инструментов)
        public event Action Finished;

        public string Error, ErrorDetail, Note;            // Note — ключ перевода: ответ обрезан, отказ…
        public bool Cancelled;
        public bool Running { get; private set; }
        /// <summary>Текст текущего ответа, который ещё печатается (если вкладку открыли посреди ответа).</summary>
        public readonly StringBuilder Live = new StringBuilder();
        public string LastText = "";

        public MuzuleRun(MuzuleChat chat, MuzuleAgent agent) { Chat = chat; _agent = agent; }

        public void Stop() { try { _cts.Cancel(); } catch { } }

        public async Task RunAsync(string userText)
        {
            Running = true;
            AddUser(userText);
            if (string.IsNullOrEmpty(Chat.Title)) Chat.Title = MuzuleStore.TitleFrom(userText);
            Chat.Model = MuzuleStore.ValidModel(Chat.Model ?? _agent.Model ?? Muzule.S.MuzuleModel);
            MuzuleStore.SaveChat(Chat);
            string key = MuzuleStore.Key;
            var all = new StringBuilder();
            try
            {
                for (int round = 0; round < MaxRounds; round++)
                {
                    if (round > 0) RoundStarted?.Invoke();
                    Live.Clear();
                    var body = Body();
                    var turn = await ClaudeApi.StreamAsync(key, body,
                        t => _ui.BeginInvoke(new Action(() => { Live.Append(t); Text?.Invoke(t); })),
                        n => _ui.BeginInvoke(new Action(() => ToolStarted?.Invoke(n))),
                        _cts.Token);
                    // Кусочки текста отправлены в главный поток через BeginInvoke — даём им дойти раньше, чем закончим
                    await _ui.InvokeAsync(() => { }, DispatcherPriority.Background);

                    Chat.InTokens += turn.InputTokens;
                    Chat.OutTokens += turn.OutputTokens;

                    if (turn.Error != null)
                    {
                        Cancelled = turn.Error == "muzule.err.stopped";
                        if (!Cancelled) { Error = turn.Error; ErrorDetail = turn.ErrorDetail; }
                        // Что успело напечататься — оставляем (без недоделанных вызовов инструментов)
                        var keep = new JsonArray();
                        foreach (var b in turn.Content)
                            if ((string)b["type"] == "text") keep.Add(b.DeepClone());
                        if (keep.Count > 0) Chat.Messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = keep });
                        break;
                    }

                    if (turn.Content.Count > 0) Chat.Messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = turn.Content });
                    foreach (var b in turn.Content)
                        if ((string)b["type"] == "text") all.Append((string)b["text"]).Append('\n');

                    if (turn.StopReason == "tool_use")
                    {
                        var results = new JsonArray();
                        foreach (var b in turn.Content)
                        {
                            if ((string)b["type"] != "tool_use") continue;
                            string name = (string)b["name"];
                            var (result, ok) = MuzuleTools.Execute(name, b["input"]);
                            var r = new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = (string)b["id"], ["content"] = result };
                            if (!ok) r["is_error"] = true;
                            results.Add(r);
                            ToolDone?.Invoke(name, result, ok);
                        }
                        if (results.Count == 0) break;
                        Chat.Messages.Add(new JsonObject { ["role"] = "user", ["content"] = results });
                        continue;
                    }
                    if (turn.StopReason == "pause_turn") continue;   // долгий поиск — Claude просит продолжить
                    if (turn.StopReason == "max_tokens") Note = "muzule.note.max_tokens";
                    else if (turn.StopReason == "refusal") Note = "muzule.note.refusal";
                    break;
                }
            }
            catch (Exception e)
            {
                Settings.Log(e);
                Error = "muzule.err.generic";
                ErrorDetail = e.Message;
            }
            finally
            {
                Running = false;
                LastText = all.ToString().Trim();
                Live.Clear();
                MuzuleStore.SaveChat(Chat);
                Finished?.Invoke();
            }
        }

        /// <summary>Сообщение человека. Если последнее сообщение уже от человека (ответ прервался) — дописываем в него.</summary>
        void AddUser(string text)
        {
            var last = Chat.Messages.Count > 0 ? Chat.Messages[Chat.Messages.Count - 1] as JsonObject : null;
            if (last != null && (string)last["role"] == "user")
            {
                JsonArray content;
                if (last["content"] is JsonArray arr) content = arr;
                else
                {
                    content = new JsonArray();
                    string old = (string)last["content"];
                    if (!string.IsNullOrEmpty(old)) content.Add(new JsonObject { ["type"] = "text", ["text"] = old });
                    last["content"] = content;
                }
                content.Add(new JsonObject { ["type"] = "text", ["text"] = text });
                return;
            }
            Chat.Messages.Add(new JsonObject { ["role"] = "user", ["content"] = text });
        }

        JsonObject Body()
        {
            var body = new JsonObject
            {
                ["model"] = Chat.Model,
                ["max_tokens"] = MaxTokens,
                // Начало запроса (инструменты + system) одинаковое — Anthropic кэширует его, следующие сообщения дешевле
                ["system"] = new JsonArray(new JsonObject
                {
                    ["type"] = "text", ["text"] = SystemPrompt(),
                    ["cache_control"] = new JsonObject { ["type"] = "ephemeral" }
                }),
                ["messages"] = Trimmed()
            };
            var tools = MuzuleTools.Definitions(_agent);
            if (tools.Count > 0) body["tools"] = tools;
            return body;
        }

        /// <summary>Последние сообщения переписки. Начинаем всегда с обычного сообщения человека (не с ответа инструмента).</summary>
        JsonArray Trimmed()
        {
            var m = Chat.Messages;
            int start = Math.Max(0, m.Count - MaxSend);
            while (start < m.Count && !IsPlainUser(m[start])) start++;
            if (start >= m.Count) start = 0;
            var arr = new JsonArray();
            for (int i = start; i < m.Count; i++) arr.Add(m[i].DeepClone());
            return arr;
        }

        static bool IsPlainUser(JsonNode n)
        {
            if ((string)n?["role"] != "user") return false;
            if (n["content"] is JsonArray a)
                return a.Count > 0 && (string)a[0]?["type"] == "text";
            return true;
        }

        string SystemPrompt()
        {
            string lang = Loc.Languages.FirstOrDefault(l => l.Code == Loc.Code).Native;
            if (string.IsNullOrEmpty(lang)) lang = "English";
            var sb = new StringBuilder();
            if (_agent.BuiltIn)
            {
                sb.Append("You are Mizu, the built-in AI agent of MizuEngine, a Windows 10/11 customization app made by rainy. ");
                sb.Append("You run on Claude by Anthropic through the user's own API key, inside the app's \"Muzule\" tab or the quick Muzule window (Ctrl+Alt+M).\n\n");
                if (!string.IsNullOrWhiteSpace(_agent.Instructions))
                    sb.Append("Extra instructions from the user:\n<instructions>\n").Append(_agent.Instructions.Trim()).Append("\n</instructions>\n\n");
            }
            else
            {
                sb.Append("You are \"").Append(_agent.Name).Append("\", a custom AI agent the user created in MizuEngine (a Windows 10/11 customization app by rainy). ");
                sb.Append("You run on Claude by Anthropic through the user's own API key, inside the app's \"Muzule\" tab.\n\n");
                if (!string.IsNullOrWhiteSpace(_agent.Instructions))
                    sb.Append("The user's instructions for you:\n<instructions>\n").Append(_agent.Instructions.Trim()).Append("\n</instructions>\n\n");
            }
            sb.Append("MizuEngine features: desktop effects (snow, rain, sakura, fireflies, blizzard, leaves, roses, confetti, bubbles, butterflies, pollen, embers, stars, aurora, matrix, garland and Plus effects), ");
            sb.Append("a macOS-style dock that replaces the Windows taskbar (genie animations, tray), Dynamic Island (music, notifications), window themes (liquid glass, blue, dark, light, animated live themes, own picture or video), ");
            sb.Append("cursor sets, FPS counter, desktop widgets (system, clock, weather), window borders and corners (Windows 11), UI sounds, presets (.mizupreset), Wallpaper Engine integration, live wallpapers, ");
            sb.Append("social features (profiles, friends, chats, groups, posts, leaderboard) and the Mizu Plus subscription. The user has Mizu Plus.\n\n");
            if (_agent.Control)
            {
                sb.Append("You can change MizuEngine settings with your tools. Rules:\n");
                sb.Append("- When the user asks for a look or a mood (\"cozy winter\", \"like a Mac\"), pick fitting settings yourself and apply them right away; settings are easy to undo, so don't ask for confirmation.\n");
                sb.Append("- Call get_mizu_state when you need current values or allowed options.\n");
                sb.Append("- After changing things, say briefly what you changed.\n");
                sb.Append("- You cannot change the account, password, payments, chats or friends, run programs or touch files. If asked, explain where in the app the user can do it (you can open the section with open_page).\n");
            }
            else sb.Append("You have no tools to change MizuEngine settings; if asked, tell the user which section of the app to open.\n");
            if (_agent.Web) sb.Append("- You can search the web when the question needs fresh information.\n");
            if (!_agent.BuiltIn && _agent.Control) sb.Append("- Use the tools only when it fits your role and the user's request.\n");
            sb.Append("\nStyle: short, friendly, clear. Light Markdown only (bold, short lists, `code`); no tables, no headings in short answers. ");
            sb.Append("Reply in the user's language; the app UI language is ").Append(lang).Append(". In Russian, address the user informally (\"ты\").\n");
            sb.Append("Today is ").Append(DateTime.Now.ToString("yyyy-MM-dd")).Append(".");
            return sb.ToString();
        }
    }
}
