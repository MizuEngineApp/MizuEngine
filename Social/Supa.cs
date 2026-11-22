// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MizuEngine
{
    /// <summary>
    /// Связь с сервером Supabase: вход (почта или Google), запросы к базе, загрузка файлов.
    /// Адрес проекта и публичный ключ — в SupaConfig.cs (их выдаёт Supabase, ключ не секретный: доступ ограничен правилами в базе).
    /// </summary>
    internal static class Supa
    {
        public static bool Configured => SupaConfig.Url.Length > 0 && SupaConfig.AnonKey.Length > 0;
        static string Base => SupaConfig.Url.TrimEnd('/');

        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };

        public sealed class SessionData
        {
            public string AccessToken { get; set; }
            public string RefreshToken { get; set; }
            public DateTime ExpiresAt { get; set; }
            public string UserId { get; set; }
            // Для списка аккаунтов: как показать аккаунт без запроса к серверу
            public string Email { get; set; }
            public string Nickname { get; set; }
            public string Handle { get; set; }
            public string AvatarUrl { get; set; }
            public bool Verified { get; set; }
            public string Badge { get; set; }   // цвет галочки: blue / black / gray
        }

        sealed class Store
        {
            public string Active { get; set; }
            public List<SessionData> Accounts { get; set; } = new List<SessionData>();
        }

        static Store _store = new Store();

        public static SessionData Session { get; private set; }
        public static bool SignedIn => Session != null;
        public static string Me => Session?.UserId;
        public static event Action SessionChanged;

        /// <summary>Все аккаунты, в которые входили на этом ПК (текущий — первым).</summary>
        public static IReadOnlyList<SessionData> Accounts =>
            _store.Accounts.OrderBy(a => a.UserId == Me ? 0 : 1).ToList();

        static string StoreFile => Path.Combine(Settings.Folder, "accounts.bin");
        static string OldSessionFile => Path.Combine(Settings.Folder, "session.bin");

        static void SaveStore()
        {
            try
            {
                _store.Active = Me;
                Directory.CreateDirectory(Settings.Folder);
                File.WriteAllBytes(StoreFile, Dpapi.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_store))));
            }
            catch (Exception e) { Settings.Log(e); }
        }

        static void LoadStore()
        {
            try
            {
                if (File.Exists(StoreFile))
                    _store = JsonSerializer.Deserialize<Store>(Encoding.UTF8.GetString(Dpapi.Unprotect(File.ReadAllBytes(StoreFile)))) ?? new Store();
                else if (File.Exists(OldSessionFile))
                {
                    // Перенос со старой версии, где был только один аккаунт
                    var one = JsonSerializer.Deserialize<SessionData>(Encoding.UTF8.GetString(Dpapi.Unprotect(File.ReadAllBytes(OldSessionFile))));
                    if (one?.UserId != null) _store = new Store { Active = one.UserId, Accounts = { one } };
                    try { File.Delete(OldSessionFile); } catch { }
                }
            }
            catch (Exception e) { Settings.Log(e); _store = new Store(); }
            _store.Accounts ??= new List<SessionData>();
            _store.Accounts.RemoveAll(a => a?.UserId == null || a.RefreshToken == null);
        }

        // ================== лимит аккаунтов на ПК ==================

        /// <summary>Сколько аккаунтов можно держать открытыми на одном ПК одновременно: 3, с Mizu Plus — 5.</summary>
        public static int MaxOpenAccounts => Plus.IsActive || DevicePlus ? 5 : 3;
        /// <summary>С этого ПК есть аккаунт с Mizu Plus — сервер разрешает 4 регистрации вместо 2.</summary>
        public static bool DevicePlus { get; private set; }

        /// <summary>Сколько аккаунтов уже создано с этого ПК (по данным сервера).</summary>
        public static int DeviceUsed { get; private set; }
        public static int DeviceLimit { get; private set; } = 2;
        /// <summary>ПК владельца MizuEngine — без лимитов.</summary>
        public static bool DeviceExempt { get; private set; }
        static bool _deviceKnown;

        public static bool CanRegister => DeviceExempt || DeviceUsed < DeviceLimit;
        public static bool CanAddAccount => DeviceExempt || _store.Accounts.Count < MaxOpenAccounts;

        /// <summary>Спрашивает сервер, сколько аккаунтов создано с этого ПК.</summary>
        public static async Task RefreshDeviceAsync()
        {
            var (json, err) = await Call("device_status", new JsonObject { ["p_device"] = DeviceId.Hash });
            if (err != null || json == null) return;   // нет связи — оставляем прошлое значение, сервер всё равно проверит
            DeviceUsed = (int?)json["used"] ?? 0;
            DeviceLimit = (int?)json["limit"] ?? 2;
            DeviceExempt = (bool?)json["exempt"] ?? false;
            DevicePlus = (bool?)json["plus"] ?? false;
            _deviceKnown = true;
        }

        /// <summary>
        /// Привязывает текущий аккаунт к ПК. Старые аккаунты просто попадают в счёт.
        /// Новый аккаунт через Google/GitHub сверх лимита сервер удаляет — тогда возвращаем "device_limit".
        /// </summary>
        static async Task<string> ClaimDeviceAsync()
        {
            if (Session == null) return null;
            var (json, err) = await Call("claim_device", new JsonObject { ["p_device"] = DeviceId.Hash });
            if (err != null) { Settings.Log(new Exception("claim_device: " + err)); return null; }
            if ((string)json != "limit") return null;
            Forget(Session.UserId);
            return "device_limit";
        }

        /// <summary>Убрать аккаунт из списка на этом ПК (без запроса к серверу) и перейти на следующий.</summary>
        static void Forget(string userId)
        {
            _store.Accounts.RemoveAll(a => a.UserId == userId);
            if (Session?.UserId == userId) Session = _store.Accounts.FirstOrDefault();
            SaveStore();
        }

        /// <summary>Привязка в фоне — для уже сохранённых аккаунтов (запуск, переключение).</summary>
        static async void ClaimInBackground()
        {
            try
            {
                if (await ClaimDeviceAsync() != null) SessionChanged?.Invoke();
                await RefreshDeviceAsync();
            }
            catch (Exception e) { Settings.Log(e); }
        }

        /// <summary>Вход в аккаунт, которого ещё нет на этом ПК, когда открыто уже 3 — нельзя.</summary>
        static bool TooManyOpen(JsonNode j)
        {
            string id = (string)j?["user"]?["id"];
            return id != null && !CanAddAccount && !_store.Accounts.Any(a => a.UserId == id);
        }

        /// <summary>Закрыть сессию, которую мы не сохраняем (лишний аккаунт).</summary>
        static async Task DropTokenAsync(JsonNode j)
        {
            string temp = (string)j?["access_token"];
            if (temp == null) return;
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Post, Base + "/auth/v1/logout?scope=local");
                req.Headers.Add("apikey", SupaConfig.AnonKey);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", temp);
                using var bye = await Http.SendAsync(req);
            }
            catch { }
        }

        /// <summary>Новый вход (код, Google, GitHub): проверяем лимиты, сохраняем сессию, привязываем к ПК.</summary>
        static async Task<string> AcceptLoginAsync(JsonNode j, bool notify)
        {
            if (!_deviceKnown) await RefreshDeviceAsync();
            if (TooManyOpen(j)) { await DropTokenAsync(j); return "accounts_full"; }
            var previous = Session;
            SetSession(j, notify: false);
            string err = await ClaimDeviceAsync();
            if (err != null)
            {
                if (previous != null && _store.Accounts.Contains(previous)) Session = previous;
                SaveStore();
                SessionChanged?.Invoke();
                return err;
            }
            await RefreshDeviceAsync();
            if (notify) SessionChanged?.Invoke();
            return null;
        }

        // ================== вход ==================

        /// <summary>Восстанавливает вход с прошлого запуска.</summary>
        public static async Task<bool> RestoreAsync()
        {
            if (!Configured) return false;
            LoadStore();
            Session = _store.Accounts.FirstOrDefault(a => a.UserId == _store.Active) ?? _store.Accounts.FirstOrDefault();
            if (Session == null) return false;
            if (Session.ExpiresAt < DateTime.UtcNow.AddMinutes(2)) await RefreshAsync();
            SaveStore();
            SessionChanged?.Invoke();
            if (Session != null) ClaimInBackground();
            return Session != null;
        }

        /// <summary>Переключиться на другой сохранённый аккаунт. Код не нужен — вход в него уже был подтверждён.</summary>
        public static async Task<string> SwitchToAsync(string userId)
        {
            var acc = _store.Accounts.FirstOrDefault(a => a.UserId == userId);
            if (acc == null) return "session_expired";
            if (acc.UserId == Me) return null;
            Session = acc;
            if (Session.ExpiresAt < DateTime.UtcNow.AddMinutes(2)) await RefreshAsync();
            SaveStore();
            SessionChanged?.Invoke();
            if (Session?.UserId == userId) ClaimInBackground();
            return Session?.UserId == userId ? null : "session_expired";
        }

        /// <summary>Запоминает ник и аватарку аккаунта для списка аккаунтов.</summary>
        public static void RememberLook(string userId, string nick, string handle, string avatar, string badge)
        {
            var acc = _store.Accounts.FirstOrDefault(a => a.UserId == userId);
            if (acc == null) return;
            bool verified = badge != null;
            if (acc.Nickname == nick && acc.Handle == handle && acc.AvatarUrl == avatar && acc.Verified == verified && acc.Badge == badge) return;
            acc.Nickname = nick; acc.Handle = handle; acc.AvatarUrl = avatar; acc.Verified = verified; acc.Badge = badge;
            SaveStore();
        }

        /// <summary>
        /// Регистрация. Сервер присылает на почту 6-значный код — без него аккаунт не заработает.
        /// Возвращает null (вошли сразу), "code_sent" (ждём код) или текст ошибки.
        /// </summary>
        public static async Task<string> SignUpAsync(string email, string password, string nickname)
        {
            await RefreshDeviceAsync();
            if (!CanRegister) return "device_limit";
            if (!CanAddAccount) return "accounts_full";
            var body = new JsonObject
            {
                ["email"] = email, ["password"] = password,
                ["data"] = new JsonObject { ["nickname"] = nickname, ["device"] = DeviceId.Hash }
            };
            var res = await SendRaw(HttpMethod.Post, "/auth/v1/signup", body, auth: false);
            if (res.error != null) return res.error;
            if (res.json?["access_token"] != null) return await AcceptLoginAsync(res.json, notify: true);
            return "code_sent";
        }

        /// <summary>
        /// Вход: сначала проверяем пароль, потом всегда отправляем код на почту.
        /// Возвращает "code_sent" (код ушёл на почту), "code_sent_signup" (почта ещё не подтверждена — выслали код регистрации) или ошибку.
        /// </summary>
        public static async Task<string> SignInAsync(string email, string password)
        {
            var res = await SendRaw(HttpMethod.Post, "/auth/v1/token?grant_type=password",
                new JsonObject { ["email"] = email, ["password"] = password }, auth: false);
            if (res.error != null)
            {
                if (res.error.Contains("not confirmed", StringComparison.OrdinalIgnoreCase))
                {
                    var r = await SendRaw(HttpMethod.Post, "/auth/v1/resend", new JsonObject { ["type"] = "signup", ["email"] = email }, auth: false);
                    return r.error ?? "code_sent_signup";
                }
                return res.error;
            }

            // Пароль верный. Эту сессию не сохраняем — закрываем её и просим код с почты.
            await DropTokenAsync(res.json);
            if (!_deviceKnown) await RefreshDeviceAsync();
            if (TooManyOpen(res.json)) return "accounts_full";
            return await SendLoginCodeAsync(email);
        }

        static async Task<string> SendLoginCodeAsync(string email)
        {
            var otp = await SendRaw(HttpMethod.Post, "/auth/v1/otp",
                new JsonObject { ["email"] = email, ["create_user"] = false }, auth: false);
            return otp.error ?? "code_sent";
        }

        /// <summary>Отправить код ещё раз. kind: "signup", "login" или "recovery".</summary>
        public static async Task<string> ResendCodeAsync(string kind, string email)
        {
            if (kind == "login") { var r = await SendLoginCodeAsync(email); return r == "code_sent" ? null : r; }
            if (kind == "recovery") return await SendRecoveryAsync(email);
            return (await SendRaw(HttpMethod.Post, "/auth/v1/resend", new JsonObject { ["type"] = "signup", ["email"] = email }, auth: false)).error;
        }

        /// <summary>Проверяет код из письма и входит в аккаунт. kind: "signup", "login" или "recovery".</summary>
        public static async Task<string> VerifyCodeAsync(string kind, string email, string code)
        {
            string type = kind == "login" ? "email" : kind;
            var res = await SendRaw(HttpMethod.Post, "/auth/v1/verify",
                new JsonObject { ["type"] = type, ["email"] = email, ["token"] = code }, auth: false);
            if (res.error != null) return res.error;
            if (res.json?["access_token"] == null) return "bad_code";
            // При сбросе пароля профиль покажем только после того, как задан новый пароль
            return await AcceptLoginAsync(res.json, notify: kind != "recovery");
        }

        /// <summary>Сообщить страницам, что вход изменился.</summary>
        public static void Announce() => SessionChanged?.Invoke();

        /// <summary>«Забыли пароль?» — код для сброса на почту.</summary>
        public static async Task<string> SendRecoveryAsync(string email) =>
            (await SendRaw(HttpMethod.Post, "/auth/v1/recover", new JsonObject { ["email"] = email }, auth: false)).error;

        /// <summary>
        /// Смена пароля, шаг 1: проверяем старый пароль и отправляем код на почту аккаунта.
        /// null — код отправлен; "old_password" — старый пароль неверный.
        /// </summary>
        public static async Task<string> BeginPasswordChangeAsync(string oldPassword)
        {
            string email = Session?.Email;
            if (string.IsNullOrEmpty(email)) return "session_expired";
            var res = await SendRaw(HttpMethod.Post, "/auth/v1/token?grant_type=password",
                new JsonObject { ["email"] = email, ["password"] = oldPassword }, auth: false);
            if (res.error != null)
                return res.error.Contains("invalid", StringComparison.OrdinalIgnoreCase) ? "old_password" : res.error;
            await DropTokenAsync(res.json);   // пароль верный — эта проверочная сессия больше не нужна
            string sent = await SendLoginCodeAsync(email);
            return sent == "code_sent" ? null : sent;
        }

        /// <summary>
        /// Смена пароля, шаг 2: код из письма подтверждает, что это владелец почты, — ставим новый пароль.
        /// Сохранённый вход в приложении не меняется.
        /// </summary>
        public static async Task<string> FinishPasswordChangeAsync(string code, string newPassword)
        {
            string email = Session?.Email;
            if (string.IsNullOrEmpty(email)) return "session_expired";
            var res = await SendRaw(HttpMethod.Post, "/auth/v1/verify",
                new JsonObject { ["type"] = "email", ["email"] = email, ["token"] = code }, auth: false);
            if (res.error != null) return res.error;
            string token = (string)res.json?["access_token"];
            if (token == null) return "bad_code";
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Put, Base + "/auth/v1/user");
                req.Headers.Add("apikey", SupaConfig.AnonKey);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                req.Content = new StringContent(new JsonObject { ["password"] = newPassword }.ToJsonString(), Encoding.UTF8, "application/json");
                using var resp = await Http.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    string text = await resp.Content.ReadAsStringAsync();
                    JsonNode j = null;
                    try { j = JsonNode.Parse(text); } catch { }
                    return (string)j?["msg"] ?? (string)j?["message"] ?? (string)j?["error_description"] ?? resp.StatusCode.ToString();
                }
                return null;
            }
            catch (Exception e) { Settings.Log(e); return "network"; }
            finally { await DropTokenAsync(res.json); }
        }

        /// <summary>Новый пароль для аккаунта, в который только что вошли по коду.</summary>
        public static async Task<string> SetPasswordAsync(string password) =>
            (await Send(HttpMethod.Put, "/auth/v1/user", new JsonObject { ["password"] = password })).error;

        public static Task<string> SignInWithGoogleAsync() => SignInWithProviderAsync("google");

        public static Task<string> SignInWithGitHubAsync() => SignInWithProviderAsync("github");

        /// <summary>
        /// Вход через Google или GitHub: открываем браузер, сервис возвращает на http://localhost:53682/ с кодом,
        /// код меняем на сессию (PKCE — пароль от Google/GitHub к нам не попадает).
        /// </summary>
        public static async Task<string> SignInWithProviderAsync(string provider)
        {
            const string redirect = "http://localhost:53682/";
            string verifier = RandomString(64);
            string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

            using var listener = new HttpListener();
            listener.Prefixes.Add(redirect);
            try { listener.Start(); } catch (Exception e) { Settings.Log(e); return "port_busy"; }

            string url = $"{Base}/auth/v1/authorize?provider={provider}&redirect_to={Uri.EscapeDataString(redirect)}" +
                         $"&code_challenge={challenge}&code_challenge_method=s256";
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { return "browser"; }

            var ctxTask = listener.GetContextAsync();
            if (await Task.WhenAny(ctxTask, Task.Delay(TimeSpan.FromMinutes(3))) != ctxTask) return "timeout";
            var ctx = ctxTask.Result;
            string code = ctx.Request.QueryString["code"];
            string err = ctx.Request.QueryString["error_description"];

            byte[] page = Encoding.UTF8.GetBytes(
                "<html><head><meta charset='utf-8'><title>MizuEngine</title></head>" +
                "<body style='background:#0b1a3a;color:#fff;font-family:Segoe UI;display:flex;align-items:center;justify-content:center;height:100vh;margin:0'>" +
                "<div style='text-align:center'><h1 style='font-weight:600'>MizuEngine</h1><p>" +
                (code != null ? "Готово! Можно вернуться в приложение. / Done! You can return to the app." : "Не получилось войти. / Sign-in failed.") +
                "</p></div></body></html>");
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = page.Length;
            await ctx.Response.OutputStream.WriteAsync(page, 0, page.Length);
            ctx.Response.Close();

            if (code == null) return err ?? "cancelled";
            var res = await SendRaw(HttpMethod.Post, "/auth/v1/token?grant_type=pkce",
                new JsonObject { ["auth_code"] = code, ["code_verifier"] = verifier }, auth: false);
            if (res.error != null) return res.error;
            return await AcceptLoginAsync(res.json, notify: true);
        }

        /// <summary>Выйти из текущего аккаунта. Если на ПК есть другие аккаунты — переключаемся на следующий.</summary>
        public static async Task SignOutAsync()
        {
            string me = Me;
            try { await SendRaw(HttpMethod.Post, "/auth/v1/logout?scope=local", null, auth: true); } catch { }
            _store.Accounts.RemoveAll(a => a.UserId == me);
            Session = null;
            var next = _store.Accounts.FirstOrDefault();
            if (next != null)
            {
                Session = next;
                if (Session.ExpiresAt < DateTime.UtcNow.AddMinutes(2)) await RefreshAsync();
            }
            SaveStore();
            SessionChanged?.Invoke();
        }

        static readonly System.Threading.SemaphoreSlim RefreshLock = new System.Threading.SemaphoreSlim(1, 1);

        static async Task RefreshAsync()
        {
            await RefreshLock.WaitAsync();
            try { await RefreshCore(); }
            finally { RefreshLock.Release(); }
        }

        static async Task RefreshCore()
        {
            // Пока ждали очереди, токен мог уже обновить другой запрос
            if (Session?.RefreshToken == null || Session.ExpiresAt > DateTime.UtcNow.AddMinutes(2)) return;
            var current = Session;
            var res = await SendRaw(HttpMethod.Post, "/auth/v1/token?grant_type=refresh_token",
                new JsonObject { ["refresh_token"] = current.RefreshToken }, auth: false);
            if (res.error != null)
            {
                if (res.status == 400 || res.status == 401)
                {
                    // Вход устарел — убираем этот аккаунт из списка
                    _store.Accounts.RemoveAll(a => a.UserId == current.UserId);
                    if (Session == current) Session = null;
                    SaveStore();
                }
                return;
            }
            SetSession(res.json, notify: false);
        }

        static void SetSession(JsonNode j, bool notify = true)
        {
            string id = (string)j["user"]?["id"];
            var old = _store.Accounts.FirstOrDefault(a => a.UserId == id);
            var s = new SessionData
            {
                AccessToken = (string)j["access_token"],
                RefreshToken = (string)j["refresh_token"],
                ExpiresAt = DateTime.UtcNow.AddSeconds((double?)j["expires_in"] ?? 3600),
                UserId = id,
                Email = (string)j["user"]?["email"] ?? old?.Email,
                Nickname = old?.Nickname, Handle = old?.Handle, AvatarUrl = old?.AvatarUrl, Verified = old?.Verified ?? false, Badge = old?.Badge
            };
            if (old != null) _store.Accounts[_store.Accounts.IndexOf(old)] = s;
            else _store.Accounts.Add(s);
            Session = s;
            SaveStore();
            if (notify) SessionChanged?.Invoke();
        }

        // ================== база данных (PostgREST) ==================

        /// <summary>GET /rest/v1/{path}. Возвращает массив строк или null при ошибке.</summary>
        public static async Task<JsonArray> Select(string path)
        {
            var res = await Send(HttpMethod.Get, "/rest/v1/" + path, null);
            return res.json as JsonArray;
        }

        /// <summary>Сколько строк подходит под фильтр. Сервер пишет ответ в заголовок «Content-Range: 0-0/N».</summary>
        public static async Task<int> Count(string table, string filter)
        {
            var req = await MakeRequest(HttpMethod.Get, $"/rest/v1/{table}?select=*&limit=1&{filter}", null, true);
            req.Headers.TryAddWithoutValidation("Prefer", "count=exact");
            try
            {
                using var resp = await Http.SendAsync(req);
                // .NET не понимает этот заголовок без единицы измерения, поэтому читаем его «как есть»
                string v = null;
                if (resp.Content.Headers.NonValidated.TryGetValues("Content-Range", out var cv)) v = cv.ToString();
                else if (resp.Headers.NonValidated.TryGetValues("Content-Range", out var hv)) v = hv.ToString();
                if (v != null && int.TryParse(v.Substring(v.LastIndexOf('/') + 1).Trim(), out int n)) return n;
                if (!resp.IsSuccessStatusCode) Settings.Log(new Exception($"count {table}: {(int)resp.StatusCode} {await resp.Content.ReadAsStringAsync()}"));
            }
            catch (Exception e) { Settings.Log(e); }
            return 0;
        }

        public static async Task<string> Insert(string table, JsonObject row) =>
            (await Send(HttpMethod.Post, "/rest/v1/" + table, row)).error;

        /// <summary>Вставка с ответом сервера (новая строка — например, чтобы узнать её id).</summary>
        public static async Task<(JsonNode json, string error)> InsertReturning(string table, JsonObject row)
        {
            var r = await Send(HttpMethod.Post, "/rest/v1/" + table, row);
            return (r.json, r.error);
        }

        public static async Task<string> Update(string table, string filter, JsonObject changes) =>
            (await Send(HttpMethod.Patch, $"/rest/v1/{table}?{filter}", changes)).error;

        public static async Task<string> Delete(string table, string filter) =>
            (await Send(HttpMethod.Delete, $"/rest/v1/{table}?{filter}", null)).error;

        public static async Task<JsonNode> Rpc(string fn, JsonObject args) =>
            (await Send(HttpMethod.Post, "/rest/v1/rpc/" + fn, args)).json;

        /// <summary>Вызов функции базы с текстом ошибки (null — всё хорошо).</summary>
        public static async Task<(JsonNode json, string error)> Call(string fn, JsonObject args)
        {
            var r = await Send(HttpMethod.Post, "/rest/v1/rpc/" + fn, args);
            return (r.json, r.error);
        }

        // ================== файлы ==================

        /// <summary>Загружает файл в хранилище и возвращает его публичную ссылку.</summary>
        public static async Task<string> Upload(string objectPath, byte[] data, string contentType)
        {
            if (!SignedIn) return null;
            await EnsureFresh();
            var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}/storage/v1/object/media/{objectPath}")
            {
                Content = new ByteArrayContent(data)
            };
            req.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            req.Headers.Add("apikey", SupaConfig.AnonKey);
            req.Headers.Add("x-upsert", "true");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Session.AccessToken);
            try
            {
                using var resp = await Http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) { Settings.Log(new Exception("upload " + await resp.Content.ReadAsStringAsync())); return null; }
                return $"{Base}/storage/v1/object/public/media/{objectPath}";
            }
            catch (Exception e) { Settings.Log(e); return null; }
        }

        /// <summary>
        /// Загрузка в любое хранилище (0.11.5: закрытое «builds» для файлов сборок). Без перезаписи: имя файла всегда новое.
        /// Возвращает null — получилось, иначе текст ошибки сервера.
        /// </summary>
        public static async Task<string> UploadTo(string bucket, string objectPath, byte[] data, string contentType)
        {
            if (!SignedIn) return "not_signed_in";
            await EnsureFresh();
            var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}/storage/v1/object/{bucket}/{objectPath}") { Content = new ByteArrayContent(data) };
            req.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            req.Headers.Add("apikey", SupaConfig.AnonKey);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Session.AccessToken);
            try
            {
                // Большой файл на медленном VPN может идти дольше обычного таймаута — даём ему до 5 минут
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(5));
                using var resp = await BigHttp.SendAsync(req, cts.Token);
                if (resp.IsSuccessStatusCode) return null;
                string text = await resp.Content.ReadAsStringAsync();
                Settings.Log(new Exception($"upload {bucket}: {(int)resp.StatusCode} {text}"));
                return string.IsNullOrEmpty(text) ? resp.StatusCode.ToString() : text;
            }
            catch (Exception e) { Settings.Log(e); return "network"; }
        }

        /// <summary>Скачивание из закрытого хранилища (нужен вход; сервер сам проверяет, можно ли этому человеку).</summary>
        public static async Task<(byte[] data, int status)> DownloadPrivate(string bucket, string objectPath)
        {
            if (!SignedIn) return (null, 401);
            await EnsureFresh();
            var req = new HttpRequestMessage(HttpMethod.Get, $"{Base}/storage/v1/object/authenticated/{bucket}/{objectPath}");
            req.Headers.Add("apikey", SupaConfig.AnonKey);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Session.AccessToken);
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(5));
                using var resp = await BigHttp.SendAsync(req, cts.Token);
                if (!resp.IsSuccessStatusCode) return (null, (int)resp.StatusCode);
                return (await resp.Content.ReadAsByteArrayAsync(), 200);
            }
            catch (Exception e) { Settings.Log(e); return (null, 0); }
        }

        /// <summary>Удалить файл из хранилища (свой или, для сотрудников, любой).</summary>
        public static async Task<string> DeleteObject(string bucket, string objectPath)
        {
            if (!SignedIn) return "not_signed_in";
            await EnsureFresh();
            var req = new HttpRequestMessage(HttpMethod.Delete, $"{Base}/storage/v1/object/{bucket}/{objectPath}");
            req.Headers.Add("apikey", SupaConfig.AnonKey);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Session.AccessToken);
            try
            {
                using var resp = await Http.SendAsync(req);
                return resp.IsSuccessStatusCode ? null : await resp.Content.ReadAsStringAsync();
            }
            catch (Exception e) { Settings.Log(e); return "network"; }
        }

        /// <summary>Отдельный клиент для больших файлов сборок: без общего таймаута (время задаётся на каждый запрос).</summary>
        static readonly HttpClient BigHttp = new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

        public static async Task<byte[]> Download(string url)
        {
            try { return await Http.GetByteArrayAsync(url); }
            catch (Exception e) { Settings.Log(e); return null; }
        }

        // ================== HTTP ==================

        static async Task EnsureFresh()
        {
            if (Session != null && Session.ExpiresAt < DateTime.UtcNow.AddMinutes(1)) await RefreshAsync();
        }

        static async Task<HttpRequestMessage> MakeRequest(HttpMethod m, string path, JsonNode body, bool auth)
        {
            if (auth) await EnsureFresh();
            var req = new HttpRequestMessage(m, Base + path);
            req.Headers.Add("apikey", SupaConfig.AnonKey);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth && Session != null ? Session.AccessToken : SupaConfig.AnonKey);
            if (body != null) req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            return req;
        }

        static Task<(JsonNode json, string error, int status)> Send(HttpMethod m, string path, JsonNode body) =>
            SendRaw(m, path, body, auth: true);

        static async Task<(JsonNode json, string error, int status)> SendRaw(HttpMethod m, string path, JsonNode body, bool auth)
        {
            if (!Configured) return (null, "not_configured", 0);
            try
            {
                var req = await MakeRequest(m, path, body, auth);
                if (m == HttpMethod.Post || m == HttpMethod.Patch) req.Headers.TryAddWithoutValidation("Prefer", "return=representation");
                using var resp = await Http.SendAsync(req);
                string text = await resp.Content.ReadAsStringAsync();
                JsonNode json = null;
                if (!string.IsNullOrWhiteSpace(text)) { try { json = JsonNode.Parse(text); } catch { } }
                if (resp.IsSuccessStatusCode) return (json, null, (int)resp.StatusCode);

                // Понятный текст ошибки: из базы приходит «message», из входа — «msg» или «error_description»
                string msg = (string)json?["message"] ?? (string)json?["msg"] ?? (string)json?["error_description"] ?? (string)json?["error"] ?? text;
                return (json, string.IsNullOrEmpty(msg) ? resp.StatusCode.ToString() : msg, (int)resp.StatusCode);
            }
            catch (Exception e)
            {
                Settings.Log(e);
                return (null, "network", 0);
            }
        }

        static string RandomString(int len)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";
            var bytes = RandomNumberGenerator.GetBytes(len);
            var sb = new StringBuilder(len);
            foreach (var b in bytes) sb.Append(chars[b % chars.Length]);
            return sb.ToString();
        }

        static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        /// <summary>Безопасная подстановка текста в фильтр запроса.</summary>
        public static string Q(string s) => Uri.EscapeDataString(s ?? "");
    }

    /// <summary>Шифрование вход-токена средствами Windows: прочитать файл сможет только этот пользователь на этом ПК.</summary>
    internal static class Dpapi
    {
        [StructLayout(LayoutKind.Sequential)]
        struct BLOB { public int cbData; public IntPtr pbData; }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CryptProtectData(ref BLOB data, string desc, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref BLOB result);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CryptUnprotectData(ref BLOB data, IntPtr desc, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref BLOB result);

        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr h);

        public static byte[] Protect(byte[] data) => Run(data, true);
        public static byte[] Unprotect(byte[] data) => Run(data, false);

        static byte[] Run(byte[] data, bool protect)
        {
            var input = new BLOB { cbData = data.Length, pbData = Marshal.AllocHGlobal(data.Length) };
            var output = new BLOB();
            try
            {
                Marshal.Copy(data, 0, input.pbData, data.Length);
                bool ok = protect
                    ? CryptProtectData(ref input, "MizuEngine", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, ref output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, ref output);
                if (!ok) throw new CryptographicException("DPAPI " + Marshal.GetLastWin32Error());
                var result = new byte[output.cbData];
                Marshal.Copy(output.pbData, result, 0, output.cbData);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(input.pbData);
                if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
            }
        }
    }
}
