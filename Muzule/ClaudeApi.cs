// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace MizuEngine
{
    /// <summary>Один ответ Claude: блоки (текст, вызовы инструментов, поиск), почему остановился, сколько токенов.</summary>
    internal sealed class ClaudeTurn
    {
        public JsonArray Content = new JsonArray();
        public string StopReason;           // end_turn, tool_use, max_tokens, pause_turn, refusal…
        public int InputTokens, OutputTokens;
        public string Error;                // ключ перевода muzule.err.* (null — всё хорошо)
        public string ErrorDetail;          // текст ошибки от Anthropic (для подсказки)
    }

    /// <summary>
    /// Claude API (Anthropic): запрос к /v1/messages с потоковым ответом (SSE).
    /// Ключ — свой у каждого пользователя, запросы идут напрямую с его ПК в Anthropic, через наш сервер ничего не проходит.
    /// </summary>
    internal static class ClaudeApi
    {
        public const string Endpoint = "https://api.anthropic.com/v1/messages";
        public const string ModelsUrl = "https://api.anthropic.com/v1/models";
        public const string Version = "2026-10-21";
        public const string ConsoleUrl = "https://platform.claude.com/settings/keys";

        // Без общего таймаута: длинный ответ может идти минуту и больше. Остановка — через CancellationToken.
        static readonly HttpClient Http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        static HttpRequestMessage Request(HttpMethod method, string url, string key)
        {
            var req = new HttpRequestMessage(method, url);
            req.Headers.TryAddWithoutValidation("x-api-key", key);
            req.Headers.TryAddWithoutValidation("anthropic-version", Version);
            return req;
        }

        /// <summary>Проверка ключа без траты денег: список моделей бесплатный. null — ключ рабочий, иначе ключ ошибки.</summary>
        public static async Task<string> CheckKeyAsync(string key, CancellationToken ct = default)
        {
            using var req = Request(HttpMethod.Get, ModelsUrl, key);
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(20));
                using var resp = await Http.SendAsync(req, cts.Token).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode) return null;
                string text = "";
                try { text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false); } catch { }
                var t = new ClaudeTurn();
                SetHttpError(t, (int)resp.StatusCode, text);
                return t.Error;
            }
            catch (Exception) { return "muzule.err.network"; }
        }

        /// <summary>
        /// Отправляет запрос и читает ответ по кусочкам. onText — новый кусочек текста (для «печати»),
        /// onTool — Claude начал вызывать инструмент (имя). Колбэки зовутся из фонового потока.
        /// </summary>
        public static async Task<ClaudeTurn> StreamAsync(string key, JsonObject body, Action<string> onText, Action<string> onTool, CancellationToken ct)
        {
            var turn = new ClaudeTurn();
            body["stream"] = true;
            using var req = Request(HttpMethod.Post, Endpoint, key);
            req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

            HttpResponseMessage resp;
            try { resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { turn.Error = "muzule.err.stopped"; return turn; }
            catch (Exception ex) { turn.Error = "muzule.err.network"; turn.ErrorDetail = ex.Message; return turn; }

            using (resp)
            {
                if (!resp.IsSuccessStatusCode)
                {
                    string text = "";
                    try { text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false); } catch { }
                    SetHttpError(turn, (int)resp.StatusCode, text);
                    return turn;
                }
                var parser = new Parser(turn, onText, onTool);
                try
                {
                    using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    string line;
                    while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
                    {
                        if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                        string data = line.Substring(5).Trim();
                        if (data.Length == 0) continue;
                        if (!parser.Feed(data)) break;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { turn.Error = "muzule.err.stopped"; }
                catch (Exception ex) { turn.Error ??= "muzule.err.network"; turn.ErrorDetail ??= ex.Message; }
                parser.Finish();
                if (turn.Error == null && turn.StopReason == null) turn.Error = "muzule.err.network";   // поток оборвался на полуслове
            }
            return turn;
        }

        // ================== ошибки ==================

        static void SetHttpError(ClaudeTurn t, int status, string body)
        {
            string type = null, msg = null;
            try
            {
                var j = JsonNode.Parse(body);
                type = (string)j?["error"]?["type"];
                msg = (string)j?["error"]?["message"];
            }
            catch { }
            t.ErrorDetail = msg;
            t.Error = type != null ? Map(type) : status switch
            {
                401 => "muzule.err.key",
                402 => "muzule.err.billing",
                403 => "muzule.err.permission",
                404 => "muzule.err.model",
                413 => "muzule.err.too_long",
                429 => "muzule.err.rate",
                529 => "muzule.err.overloaded",
                >= 500 => "muzule.err.server",
                _ => "muzule.err.request"
            };
            // На счёте Anthropic кончились деньги — это приходит как обычная ошибка запроса
            if (msg != null && msg.IndexOf("credit balance", StringComparison.OrdinalIgnoreCase) >= 0) t.Error = "muzule.err.billing";
            if (msg != null && msg.IndexOf("web search", StringComparison.OrdinalIgnoreCase) >= 0 && t.Error == "muzule.err.request") t.Error = "muzule.err.web";
            if (msg != null && (msg.IndexOf("prompt is too long", StringComparison.OrdinalIgnoreCase) >= 0 || msg.IndexOf("context window", StringComparison.OrdinalIgnoreCase) >= 0)) t.Error = "muzule.err.too_long";
        }

        static string Map(string type) => type switch
        {
            "authentication_error" => "muzule.err.key",
            "permission_error" => "muzule.err.permission",
            "not_found_error" => "muzule.err.model",
            "rate_limit_error" => "muzule.err.rate",
            "overloaded_error" => "muzule.err.overloaded",
            "billing_error" => "muzule.err.billing",
            "request_too_large" => "muzule.err.too_long",
            "timeout_error" => "muzule.err.network",
            "api_error" => "muzule.err.server",
            _ => "muzule.err.request"
        };

        // ================== разбор потока ==================

        /// <summary>Собирает блоки ответа из событий потока: текст по кусочкам, JSON инструментов по кусочкам, цитаты поиска.</summary>
        sealed class Parser
        {
            readonly ClaudeTurn _t;
            readonly Action<string> _text, _tool;
            readonly SortedDictionary<int, JsonObject> _blocks = new SortedDictionary<int, JsonObject>();
            readonly Dictionary<int, StringBuilder> _buf = new Dictionary<int, StringBuilder>();
            readonly HashSet<int> _closed = new HashSet<int>();

            public Parser(ClaudeTurn t, Action<string> text, Action<string> tool) { _t = t; _text = text; _tool = tool; }

            /// <summary>false — поток закончился (message_stop или ошибка).</summary>
            public bool Feed(string data)
            {
                JsonNode j;
                try { j = JsonNode.Parse(data); } catch { return true; }
                if (j == null) return true;
                switch ((string)j["type"])
                {
                    case "message_start":
                    {
                        var u = j["message"]?["usage"];
                        _t.InputTokens = Int(u?["input_tokens"]) + Int(u?["cache_read_input_tokens"]) + Int(u?["cache_creation_input_tokens"]);
                        _t.OutputTokens = Int(u?["output_tokens"]);
                        break;
                    }
                    case "content_block_start":
                    {
                        int i = Int(j["index"]);
                        if (j["content_block"]?.DeepClone() is not JsonObject b) break;
                        _blocks[i] = b;
                        _buf[i] = new StringBuilder();
                        string bt = (string)b["type"];
                        if (bt == "text") _buf[i].Append((string)b["text"] ?? "");
                        else if (bt == "tool_use" || bt == "server_tool_use") _tool?.Invoke((string)b["name"] ?? "");
                        break;
                    }
                    case "content_block_delta":
                    {
                        int i = Int(j["index"]);
                        if (!_blocks.TryGetValue(i, out var b)) break;
                        var d = j["delta"];
                        switch ((string)d?["type"])
                        {
                            case "text_delta":
                                string s = (string)d["text"] ?? "";
                                _buf[i].Append(s);
                                if (s.Length > 0) _text?.Invoke(s);
                                break;
                            case "input_json_delta":
                                _buf[i].Append((string)d["partial_json"] ?? "");
                                break;
                            case "citations_delta":
                                var c = d["citation"]?.DeepClone();
                                if (c != null)
                                {
                                    if (b["citations"] is not JsonArray arr) { arr = new JsonArray(); b["citations"] = arr; }
                                    arr.Add(c);
                                }
                                break;
                            case "thinking_delta":
                                _buf[i].Append((string)d["thinking"] ?? "");
                                break;
                            case "signature_delta":
                                b["signature"] = ((string)b["signature"] ?? "") + ((string)d["signature"] ?? "");
                                break;
                        }
                        break;
                    }
                    case "content_block_stop":
                        Close(Int(j["index"]));
                        break;
                    case "message_delta":
                    {
                        string sr = (string)j["delta"]?["stop_reason"];
                        if (sr != null) _t.StopReason = sr;
                        var ot = j["usage"]?["output_tokens"];
                        if (ot != null) _t.OutputTokens = Int(ot);
                        break;
                    }
                    case "message_stop":
                        return false;
                    case "error":
                        _t.Error = Map((string)j["error"]?["type"] ?? "api_error");
                        _t.ErrorDetail = (string)j["error"]?["message"];
                        return false;
                }
                return true;
            }

            void Close(int i)
            {
                if (!_blocks.TryGetValue(i, out var b) || !_closed.Add(i)) return;
                string s = _buf[i].ToString();
                switch ((string)b["type"])
                {
                    case "text":
                        b["text"] = s;
                        break;
                    case "tool_use":
                    case "server_tool_use":
                        JsonNode input = null;
                        if (s.Trim().Length > 0) { try { input = JsonNode.Parse(s); } catch { } }
                        b["input"] = input ?? b["input"]?.DeepClone() ?? new JsonObject();
                        break;
                    case "thinking":
                        b["thinking"] = s;
                        break;
                }
            }

            /// <summary>Готовый список блоков по порядку. Пустой текст API обратно не принимает — выкидываем.</summary>
            public void Finish()
            {
                foreach (var i in new List<int>(_blocks.Keys)) Close(i);
                foreach (var kv in _blocks)
                {
                    var b = kv.Value;
                    if ((string)b["type"] == "text")
                    {
                        if (string.IsNullOrEmpty((string)b["text"])) continue;
                        if (b["citations"] is not JsonArray cites || cites.Count == 0) b.Remove("citations");
                    }
                    _t.Content.Add(b);
                }
            }

            static int Int(JsonNode n)
            {
                try { return n == null ? 0 : n.GetValue<int>(); } catch { return 0; }
            }
        }
    }
}
