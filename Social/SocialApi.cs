// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MizuEngine
{
    internal sealed class Profile
    {
        public string Id, Nickname, Handle, Bio, AvatarUrl, BannerUrl, Title;
        public string Badge;                 // цвет галочки: blue / black / gray, null — нет галочки
        public string TitleBg, TitleFg;      // плашка подписи: фон #RRGGBB (null — синий градиент) и текст white/black
        public string BanReason;
        public long Number, UsageSeconds;
        public bool Verified, IsAdmin, Staff, Bot;
        public DateTime HandleChangedAt, BannedUntil, MutedUntil, Created;
        public bool PlusForever;
        public DateTime PlusUntil;
        public string OnlineVis = "everyone";   // кто видит «в сети»: everyone / followers / friends / nobody
        public string GroupAdd = "everyone";    // кто может сразу добавлять в группы: everyone / invites (только приглашения)

        /// <summary>Подписчик Mizu Plus — у ника значок Plus.</summary>
        public bool IsPlus => PlusForever || PlusUntil > DateTime.UtcNow;

        /// <summary>Может публиковать посты и создавать группы: ты (админ) и твинк (сотрудник).</summary>
        public bool CanPost => IsAdmin || Staff;
        public bool Banned => BannedUntil > DateTime.UtcNow;
        public bool Muted => Banned || MutedUntil > DateTime.UtcNow;
        /// <summary>Сколько ждать между сменами @юза: сотрудникам и админам — 5 секунд, остальным — сутки.</summary>
        public TimeSpan HandleCooldown => CanPost ? TimeSpan.FromSeconds(5) : TimeSpan.FromHours(24);

        public static Profile From(JsonNode j)
        {
            if (j == null) return null;
            return new Profile
            {
                Id = (string)j["id"],
                Number = (long?)j["number"] ?? 0,
                Nickname = (string)j["nickname"] ?? "User",
                Handle = (string)j["handle"] ?? "",
                Bio = (string)j["bio"] ?? "",
                AvatarUrl = (string)j["avatar_url"],
                BannerUrl = (string)j["banner_url"],
                Title = (string)j["title"],
                Badge = SocialApi.BadgeKind((string)j["badge"], (bool?)j["verified"] ?? false),
                TitleBg = (string)j["title_bg"],
                TitleFg = (string)j["title_fg"] ?? "white",
                BanReason = (string)j["ban_reason"],
                UsageSeconds = (long?)j["usage_seconds"] ?? 0,
                IsAdmin = (bool?)j["is_admin"] ?? false,
                Staff = (bool?)j["staff"] ?? false,
                Bot = (bool?)j["bot"] ?? false,
                HandleChangedAt = SocialApi.Utc(j["handle_changed_at"]),
                BannedUntil = SocialApi.Utc(j["banned_until"]),
                MutedUntil = SocialApi.Utc(j["muted_until"]),
                Created = SocialApi.Utc(j["created_at"]),
                PlusForever = (bool?)j["plus_forever"] ?? false,
                PlusUntil = SocialApi.Utc(j["plus_until"]),
                OnlineVis = (string)j["online_vis"] ?? "everyone",
                GroupAdd = (string)j["group_add"] ?? "everyone"
            }.Fix();
        }

        /// <summary>Галочка есть, если у неё есть цвет.</summary>
        public Profile Fix() { Verified = Badge != null; return this; }
    }

    /// <summary>В сети ли человек и когда был последний раз.</summary>
    internal sealed class Presence
    {
        public bool Online;
        public DateTime LastSeen;
    }

    /// <summary>Строка списка всех пользователей в админ-панели.</summary>
    internal sealed class AdminUser
    {
        public string Id, Email;
        public DateTime SignedUp, LastSeen;
        public bool Online;
        public Profile Profile;
    }

    /// <summary>Тема чата: фон (готовый градиент или своя картинка) и цвет своих сообщений.</summary>
    internal sealed class ChatTheme
    {
        public string Bg;       // ключ готового фона из UI.ThemeBackgrounds, "custom" — картинка, null — без фона
        public string Img;      // ссылка на свою картинку
        public string Bubble;   // цвет своих сообщений #RRGGBB, null — цвет темы приложения

        public bool IsEmpty => Bg == null && Bubble == null;

        public static ChatTheme From(JsonNode j)
        {
            if (j is not JsonObject) return new ChatTheme();
            return new ChatTheme { Bg = (string)j["bg"], Img = (string)j["img"], Bubble = (string)j["bubble"] };
        }

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            if (Bg != null) o["bg"] = Bg;
            if (Bg == "custom" && Img != null) o["img"] = Img;
            if (Bubble != null) o["bubble"] = Bubble;
            return o;
        }

        public ChatTheme Copy() => new ChatTheme { Bg = Bg, Img = Img, Bubble = Bubble };
    }

    /// <summary>Запись журнала админов.</summary>
    internal sealed class AdminLogEntry
    {
        public string AdminName, TargetName, Action;
        public long TargetGroup;
        public JsonNode Details;
        public DateTime Created;
    }

    internal sealed class FriendRequest
    {
        public long Id;
        public Profile Other;
    }

    internal sealed class ChatMessage
    {
        public long Id, GroupId;
        public string Sender, Receiver, Kind, Body;
        public JsonNode Payload;
        public DateTime Created;
        public bool Read;                              // личные сообщения: собеседник уже прочитал

        public static ChatMessage From(JsonNode j) => new ChatMessage
        {
            Read = (bool?)j["read"] ?? false,
            Id = (long?)j["id"] ?? 0,
            GroupId = (long?)j["group_id"] ?? 0,
            Sender = (string)j["sender"],
            Receiver = (string)j["receiver"],
            Kind = (string)j["kind"] ?? "text",
            Body = (string)j["body"] ?? "",
            Payload = j["payload"]?.DeepClone(),
            Created = DateTime.TryParse((string)j["created_at"], out var d) ? d.ToLocalTime() : DateTime.Now
        };
    }

    internal sealed class Post
    {
        public long Id, CommunityId;
        public string Author, Body, ImageUrl;
        public DateTime Created;
        public int Likes, Comments;
        public bool Liked;
    }

    /// <summary>Жалобы на один пост (для админ-панели): сам пост, его автор, сколько жалоб и на что.</summary>
    internal sealed class PostReport
    {
        public Post Post;
        public Profile Author;
        public int Count;
        public List<string> Reasons = new List<string>();
        public DateTime Last;
    }

    internal sealed class Comment
    {
        public long Id;
        public string Body;
        public Profile Author;
        public DateTime Created;
    }

    internal sealed class Community
    {
        public long Id;
        public string Owner, Name, Description, AvatarUrl, BannerUrl, Handle, Badge;

        public static Community From(JsonNode j) => j == null ? null : new Community
        {
            Id = (long?)j["id"] ?? 0,
            Owner = (string)j["owner"],
            Name = (string)j["name"] ?? "",
            Description = (string)j["description"] ?? "",
            AvatarUrl = (string)j["avatar_url"],
            BannerUrl = (string)j["banner_url"],
            Handle = (string)j["handle"],
            Badge = (string)j["badge"]
        };

        /// <summary>Сообщество выглядит в постах как автор: аватарка, имя и галочка.</summary>
        public Profile AsAuthor() => new Profile { Id = "community:" + Id, Nickname = Name, Handle = Handle ?? "", AvatarUrl = AvatarUrl, Badge = Badge }.Fix();
    }

    internal sealed class Group
    {
        public long Id;
        public string Owner, Name, AvatarUrl, Handle, Badge;
        public int? MaxMembers;
        public ChatTheme Theme = new ChatTheme();
        public DateTime HandleChangedAt;

        public static Group From(JsonNode j) => j == null ? null : new Group
        {
            Id = (long?)j["id"] ?? 0,
            Owner = (string)j["owner"],
            Name = (string)j["name"] ?? "",
            AvatarUrl = (string)j["avatar_url"],
            MaxMembers = (int?)j["max_members"],
            Handle = (string)j["handle"],
            Badge = (string)j["badge"],
            Theme = ChatTheme.From(j["theme"]),
            HandleChangedAt = SocialApi.Utc(j["handle_changed_at"])
        };

        /// <summary>Для аватарки группы: первая буква названия на градиенте.</summary>
        public Profile Look() => new Profile { Id = "group:" + Id, Nickname = Name, Handle = Handle ?? "", AvatarUrl = AvatarUrl, Badge = Badge }.Fix();
    }

    internal sealed class GroupMember
    {
        public Profile User;
        public bool Joined;
    }

    internal enum FriendState { None, Outgoing, Incoming, Friends }

    /// <summary>Все запросы соцсети MizuEngine: профили, подписки, друзья, сообщения.</summary>
    internal static class SocialApi
    {
        static Profile _me;

        /// <summary>Профиль того, кто сейчас вошёл. Если вошли в другой аккаунт — старый не показываем.</summary>
        public static Profile MyProfile
        {
            get => _me != null && Supa.SignedIn && _me.Id == Supa.Me ? _me : null;
            private set => _me = value;
        }

        // ---------- общее ----------

        /// <summary>Время с сервера в UTC. "infinity" и даты после 9000 года — «навсегда».</summary>
        public static DateTime Utc(JsonNode n)
        {
            string s = n == null ? null : (n is JsonValue v && v.TryGetValue<string>(out var str) ? str : null);
            if (string.IsNullOrEmpty(s)) return DateTime.MinValue;
            if (s.Contains("infinity")) return DateTime.MaxValue;
            return DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : DateTime.MinValue;
        }

        public static bool Forever(DateTime until) => until.Year >= 9000;

        /// <summary>Цвет галочки. Старые профили без цвета, но с галочкой — синяя.</summary>
        public static string BadgeKind(string badge, bool verified) =>
            badge == "blue" || badge == "black" || badge == "gray" ? badge : verified ? "blue" : null;

        // ---------- профили ----------

        public static async Task<Profile> LoadMe()
        {
            if (!Supa.SignedIn) { MyProfile = null; return null; }
            var me = await GetProfile(Supa.Me);
            MyProfile = me;
            if (me != null) Supa.RememberLook(me.Id, me.Nickname, me.Handle, me.AvatarUrl, me.Badge);
            return me;
        }

        // ---------- время в MizuEngine (серая галочка за 300+ часов) ----------

        static System.Windows.Threading.DispatcherTimer _usage;

        /// <summary>Раз в 5 минут сообщает серверу, что приложение открыто. Сервер сам считает часы и выдаёт серую галочку.</summary>
        public static void StartUsageTracking()
        {
            if (_usage != null) return;
            _usage = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
            _usage.Tick += async (s, e) => await PingUsage();
            _usage.Start();
            var first = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            first.Tick += async (s, e) => { first.Stop(); await PingUsage(); };
            first.Start();
        }

        static async Task PingUsage()
        {
            if (!Supa.SignedIn || !Supa.Configured) return;
            try { await Supa.Call("usage_ping", new JsonObject()); }
            catch (Exception e) { Settings.Log(e); }
        }

        public static async Task<Profile> GetProfile(string id)
        {
            var rows = await Supa.Select($"profiles?id=eq.{Supa.Q(id)}&select=*");
            return rows != null && rows.Count > 0 ? Profile.From(rows[0]) : null;
        }

        public static async Task<List<Profile>> Search(string query)
        {
            // Только безопасные символы: из них состоят ники и юзы
            string q = new string((query ?? "").Trim().TrimStart('@').Where(c => char.IsLetterOrDigit(c) || c == '_' || c == ' ' || c == '.').ToArray());
            if (q.Length == 0) return new List<Profile>();
            var rows = await Supa.Select($"profiles?or=(handle.ilike.*{Supa.Q(q)}*,nickname.ilike.*{Supa.Q(q)}*)&select=*&order=number.asc&limit=30");
            return rows?.Select(Profile.From).ToList() ?? new List<Profile>();
        }

        /// <summary>Сохраняет изменения своего профиля. Возвращает понятную ошибку или null.</summary>
        public static async Task<string> UpdateMe(JsonObject changes)
        {
            string err = await Supa.Update("profiles", $"id=eq.{Supa.Q(Supa.Me)}", changes);
            if (err == null) await LoadMe();
            return MapError(err);
        }

        // ---------- лидеры ----------

        /// <summary>Топ-10. kind: "hours" — больше всех часов в MizuEngine, "created" — кто раньше создал аккаунт, "number" — по номеру аккаунта.</summary>
        public static async Task<List<Profile>> Leaders(string kind)
        {
            string order = kind == "hours" ? "usage_seconds.desc,number.asc" : kind == "created" ? "created_at.asc" : "number.asc";
            var rows = await Supa.Select($"profiles?select=*&order={order}&limit=10");
            return rows?.Select(Profile.From).Where(p => p != null).ToList();
        }

        /// <summary>Настоящее место человека в рейтинге (1 — первый).</summary>
        public static async Task<int> PlaceOf(Profile p, string kind)
        {
            string filter = kind == "hours"
                ? $"or=(usage_seconds.gt.{p.UsageSeconds},and(usage_seconds.eq.{p.UsageSeconds},number.lt.{p.Number}))"
                : kind == "created"
                    ? $"created_at=lt.{Uri.EscapeDataString(p.Created.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", System.Globalization.CultureInfo.InvariantCulture))}"
                    : $"number=lt.{p.Number}";
            return await Supa.Count("profiles", filter) + 1;
        }

        // ---------- админ-панель ----------

        /// <summary>Админ меняет чужой профиль: галочку, подпись, роли, бан, мут, сброс. Всё пишется в журнал на сервере.</summary>
        public static async Task<string> AdminUpdate(string id, JsonObject changes)
        {
            string err = MapError(await Supa.Update("profiles", $"id=eq.{Supa.Q(id)}", changes));
            if (err == null && id == Supa.Me) await LoadMe();
            return err;
        }

        static string NowIso => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);

        public static async Task<List<Profile>> Punished()
        {
            string now = NowIso;
            var rows = await Supa.Select($"profiles?or=(banned_until.gt.{now},muted_until.gt.{now})&select=*&order=number.asc&limit=200");
            return rows?.Select(Profile.From).ToList() ?? new List<Profile>();
        }

        public static async Task<List<AdminLogEntry>> AdminLog()
        {
            var rows = await Supa.Select("admin_log?select=action,details,created_at,target_group,a:admin(nickname),t:target(nickname)&order=id.desc&limit=100");
            return rows?.Select(r => new AdminLogEntry
            {
                AdminName = (string)r["a"]?["nickname"] ?? "?",
                TargetName = (string)r["t"]?["nickname"],
                Action = (string)r["action"] ?? "",
                TargetGroup = (long?)r["target_group"] ?? 0,
                Details = r["details"]?.DeepClone(),
                Created = Utc(r["created_at"]).ToLocalTime()
            }).ToList() ?? new List<AdminLogEntry>();
        }

        public static async Task<List<Group>> AdminGroups(string query)
        {
            string q = new string((query ?? "").Trim().TrimStart('@').Where(c => char.IsLetterOrDigit(c) || c == '_' || c == ' ').ToArray());
            string filter = q.Length == 0 ? "" : $"or=(name.ilike.*{Supa.Q(q)}*,handle.ilike.*{Supa.Q(q)}*)&";
            var rows = await Supa.Select($"groups?{filter}select=*&order=id.desc&limit=40");
            return rows?.Select(Group.From).Where(g => g != null).ToList() ?? new List<Group>();
        }

        /// <summary>Цифры для админ-панели: люди, группы, посты, наказанные.</summary>
        public static async Task<(int users, int groups, int posts, int punished)> AdminStats()
        {
            string now = NowIso;
            var a = Supa.Count("profiles", "");
            var b = Supa.Count("groups", "");
            var c = Supa.Count("posts", "");
            var d = Supa.Count("profiles", $"or=(banned_until.gt.{now},muted_until.gt.{now})");
            await Task.WhenAll(a, b, c, d);
            return (a.Result, b.Result, c.Result, d.Result);
        }

        /// <summary>Картинку уменьшаем и сжимаем в JPEG перед загрузкой — быстрее и места меньше.</summary>
        public static async Task<string> UploadImage(string file, string kind, int maxSide)
        {
            byte[] data = await Task.Run(() => Shrink(file, maxSide));
            if (data == null) return null;
            return await Supa.Upload($"{Supa.Me}/{kind}-{DateTime.UtcNow.Ticks}.jpg", data, "image/jpeg");
        }

        static byte[] Shrink(string file, int maxSide)
        {
            try
            {
                var src = new BitmapImage();
                src.BeginInit();
                src.UriSource = new Uri(file);
                src.CacheOption = BitmapCacheOption.OnLoad;
                src.EndInit();
                double k = Math.Min(1.0, (double)maxSide / Math.Max(src.PixelWidth, src.PixelHeight));
                BitmapSource img = k < 1 ? new TransformedBitmap(src, new ScaleTransform(k, k)) : src;
                var enc = new JpegBitmapEncoder { QualityLevel = 88 };
                enc.Frames.Add(BitmapFrame.Create(img));
                using var ms = new MemoryStream();
                enc.Save(ms);
                return ms.ToArray();
            }
            catch (Exception e) { Settings.Log(e); return null; }
        }

        // ---------- посты (у себя в профиле пишут все; в сообществе — сотрудники) ----------

        /// <summary>Сколько часов в MizuEngine нужно, чтобы писать посты (сотрудникам — не нужно). Так же на сервере (guard_post).</summary>
        public const int PostHours = 3;
        /// <summary>Длина поста для обычных людей (у сотрудников — 2000).</summary>
        public const int PostMaxLen = 1000;

        /// <summary>Хватает ли часов, чтобы писать посты.</summary>
        public static bool EnoughHoursToPost(Profile p) => p != null && (p.CanPost || p.UsageSeconds >= PostHours * 3600L);

        /// <summary>«Посты можно писать после 3 ч в MizuEngine. У тебя пока 1 ч 20 мин.»</summary>
        public static string NotEnoughHoursText(long usageSeconds) =>
            string.Format(Loc.T("posts.need_hours"), PostHours, HoursMinutes(usageSeconds));

        /// <summary>«1 ч 20 мин» / «45 мин».</summary>
        static string HoursMinutes(long seconds)
        {
            long m = Math.Max(0, seconds) / 60;
            return m >= 60 ? string.Format(Loc.T("posts.h_m"), m / 60, m % 60) : string.Format(Loc.T("posts.m"), m);
        }

        public static Task<List<Post>> GetPosts(string authorId) =>
            ReadPosts($"author=eq.{Supa.Q(authorId)}&community_id=is.null");

        public static Task<List<Post>> GetCommunityPosts(long communityId) =>
            ReadPosts($"community_id=eq.{communityId}");

        static async Task<List<Post>> ReadPosts(string filter)
        {
            var rows = await Supa.Select($"posts?{filter}&select=id,author,body,image_url,created_at,community_id,post_likes(count),post_comments(count)&order=id.desc&limit=50");
            if (rows == null) return null;
            var list = rows.Select(r => new Post
            {
                Id = (long?)r["id"] ?? 0,
                Author = (string)r["author"],
                Body = (string)r["body"] ?? "",
                ImageUrl = (string)r["image_url"],
                Created = DateTime.TryParse((string)r["created_at"], out var d) ? d.ToLocalTime() : DateTime.Now,
                CommunityId = (long?)r["community_id"] ?? 0,
                Likes = CountOf(r["post_likes"]),
                Comments = CountOf(r["post_comments"])
            }).ToList();
            if (Supa.SignedIn && list.Count > 0)
            {
                string ids = string.Join(",", list.Select(x => x.Id));
                var mine = await Supa.Select($"post_likes?user_id=eq.{Supa.Q(Supa.Me)}&post_id=in.({ids})&select=post_id");
                var liked = new HashSet<long>(mine?.Select(m => (long?)m["post_id"] ?? 0) ?? Enumerable.Empty<long>());
                foreach (var x in list) x.Liked = liked.Contains(x.Id);
            }
            return list;
        }

        static int CountOf(JsonNode n) => (n as JsonArray)?.Count > 0 ? ((int?)n[0]?["count"] ?? 0) : 0;

        public static async Task<string> CreatePost(string body, string imageFile, long communityId = 0)
        {
            string url = null;
            if (imageFile != null)
            {
                url = await UploadImage(imageFile, "post", 1600);
                if (url == null) return Loc.T("err.upload");
            }
            var row = new JsonObject { ["author"] = Supa.Me, ["body"] = body ?? "", ["image_url"] = url };
            if (communityId > 0) row["community_id"] = communityId;
            return MapError(await Supa.Insert("posts", row));
        }

        // ---------- комментарии ----------

        public static async Task<List<Comment>> GetComments(long postId)
        {
            var rows = await Supa.Select($"post_comments?post_id=eq.{postId}&select=id,body,created_at,p:author(*)&order=id.asc&limit=200");
            return rows?.Select(r => new Comment
            {
                Id = (long?)r["id"] ?? 0,
                Body = (string)r["body"] ?? "",
                Author = Profile.From(r["p"]),
                Created = DateTime.TryParse((string)r["created_at"], out var d) ? d.ToLocalTime() : DateTime.Now
            }).Where(c => c.Author != null).ToList() ?? new List<Comment>();
        }

        public static async Task<string> AddComment(long postId, string body) =>
            MapError(await Supa.Insert("post_comments", new JsonObject { ["post_id"] = postId, ["body"] = body }));

        public static async Task<string> DeleteComment(long id) =>
            MapError(await Supa.Delete("post_comments", $"id=eq.{id}"));

        // ---------- сообщество ----------

        /// <summary>Сообщество MizuEngine (первое созданное). null — его ещё нет или нет сети (тогда ok = false).</summary>
        public static async Task<(Community c, bool ok)> GetCommunity()
        {
            var rows = await Supa.Select("communities?select=*&order=id.asc&limit=1");
            if (rows == null) return (null, false);
            return (rows.Count > 0 ? Community.From(rows[0]) : null, true);
        }

        public static async Task<string> CreateCommunity(string name, string description) =>
            MapError(await Supa.Insert("communities", new JsonObject { ["owner"] = Supa.Me, ["name"] = name, ["description"] = description ?? "" }));

        public static async Task<string> UpdateCommunity(long id, JsonObject changes) =>
            MapError(await Supa.Update("communities", $"id=eq.{id}", changes));

        public static Task<int> MembersCount(long id) => Supa.Count("community_members", $"community_id=eq.{id}");

        public static async Task<bool> IsMember(long id)
        {
            var rows = await Supa.Select($"community_members?community_id=eq.{id}&user_id=eq.{Supa.Q(Supa.Me)}&select=user_id");
            return rows != null && rows.Count > 0;
        }

        public static async Task<string> Join(long id) =>
            MapError(await Supa.Insert("community_members", new JsonObject { ["community_id"] = id, ["user_id"] = Supa.Me }));

        public static async Task<string> Leave(long id) =>
            MapError(await Supa.Delete("community_members", $"community_id=eq.{id}&user_id=eq.{Supa.Q(Supa.Me)}"));

        // ---------- группы ----------

        public static async Task<List<Group>> MyGroups()
        {
            // Сначала номера моих групп, потом сами группы тем же запросом, что и шапка группы (аватарка, галочка, @юз)
            var mine = await Supa.Select($"group_members?user_id=eq.{Supa.Q(Supa.Me)}&status=eq.joined&select=group_id&order=created_at.desc");
            var ids = mine?.Select(r => (long?)r["group_id"] ?? 0).Where(id => id > 0).ToList() ?? new List<long>();
            if (ids.Count == 0) return new List<Group>();
            var rows = await Supa.Select($"groups?id=in.({string.Join(",", ids)})&select=*");
            var list = rows?.Select(Group.From).Where(g => g != null).ToList() ?? new List<Group>();
            return list.OrderBy(g => ids.IndexOf(g.Id)).ToList();
        }

        public static async Task<Group> GetGroup(long id)
        {
            var rows = await Supa.Select($"groups?id=eq.{id}&select=*");
            return rows != null && rows.Count > 0 ? Group.From(rows[0]) : null;
        }

        /// <summary>Моё место в группе: "joined", "invited" или null (не участник / нет сети).</summary>
        public static async Task<string> GroupStatus(long id)
        {
            var rows = await Supa.Select($"group_members?group_id=eq.{id}&user_id=eq.{Supa.Q(Supa.Me)}&select=status");
            return rows != null && rows.Count > 0 ? (string)rows[0]["status"] : null;
        }

        public static async Task<List<GroupMember>> GroupMembers(long id)
        {
            var rows = await Supa.Select($"group_members?group_id=eq.{id}&select=status,p:user_id(*)&order=created_at.asc");
            return rows?.Select(r => new GroupMember { User = Profile.From(r["p"]), Joined = (string)r["status"] == "joined" })
                       .Where(m => m.User != null).ToList() ?? new List<GroupMember>();
        }

        public static async Task<(long id, string error)> CreateGroup(string name, int? max)
        {
            var (json, err) = await Supa.Call("create_group", new JsonObject { ["p_name"] = name, ["p_max"] = max });
            if (err != null) return (0, MapError(err));
            long id = 0;
            try { id = json?.GetValue<long>() ?? 0; } catch { }
            return (id, id > 0 ? null : Loc.T("err.network"));
        }

        public static async Task<string> Invite(long groupId, string userId) =>
            MapError((await Supa.Call("invite_to_group", new JsonObject { ["p_group"] = groupId, ["p_user"] = userId })).error);

        /// <summary>
        /// Добавить человека в группу. Владелец MizuEngine добавляет сразу всегда, владелец группы — сразу,
        /// если человек не выбрал «только по приглашению». Иначе уходит приглашение. Ответ: added/invited или ошибка.
        /// </summary>
        public static async Task<(bool added, string error)> AddToGroup(long groupId, string userId)
        {
            var (json, err) = await Supa.Call("add_to_group", new JsonObject { ["p_group"] = groupId, ["p_user"] = userId });
            if (err != null && (err.Contains("add_to_group") || err.Contains("PGRST202")))   // на сервере ещё нет v07.sql — старое приглашение
                return (false, await Invite(groupId, userId));
            if (err != null) return (false, MapError(err));
            string r = null;
            try { r = json?.GetValue<string>(); } catch { }
            return (r == "added", null);
        }

        /// <summary>Отметить на сервере, что группу дочитали до этого сообщения (для двух галочек у остальных).</summary>
        public static Task MarkGroupReadServer(long groupId, long lastId) =>
            Supa.Call("mark_group_read", new JsonObject { ["p_group"] = groupId, ["p_last"] = lastId });

        /// <summary>До какого сообщения группу прочитал хоть кто-то, кроме меня.</summary>
        public static async Task<long> GroupReadMark(long groupId)
        {
            var (json, err) = await Supa.Call("group_read_mark", new JsonObject { ["p_group"] = groupId });
            if (err != null) return 0;
            try { return json?.GetValue<long>() ?? 0; } catch { return 0; }
        }

        // ---------- в сети ----------

        /// <summary>«Я в сети» (раз в минуту, пока программа запущена) или «не в сети» (при выходе).</summary>
        public static Task PresencePing(bool online) =>
            Supa.Call("presence_ping", new JsonObject { ["p_online"] = online });

        /// <summary>Кто из людей в сети. Тех, кто скрыл статус, в ответе нет.</summary>
        public static async Task<Dictionary<string, Presence>> GetPresence(IEnumerable<string> ids)
        {
            var list = ids?.Where(i => !string.IsNullOrEmpty(i)).Distinct().Take(300).ToList();
            var res = new Dictionary<string, Presence>();
            if (list == null || list.Count == 0) return res;
            var arr = new JsonArray();
            foreach (var i in list) arr.Add(i);
            var (json, err) = await Supa.Call("get_presence", new JsonObject { ["p_ids"] = arr });
            if (err != null || json is not JsonArray rows) return res;
            foreach (var r in rows)
            {
                string id = (string)r?["user_id"];
                if (id == null) continue;
                res[id] = new Presence { Online = (bool?)r["online"] ?? false, LastSeen = Utc(r["last_seen"]) };
            }
            return res;
        }

        // ---------- все пользователи (админ) ----------

        public static async Task<List<AdminUser>> AdminUsers(string query, int offset, int limit)
        {
            var (json, err) = await Supa.Call("admin_users", new JsonObject { ["p_query"] = query ?? "", ["p_offset"] = offset, ["p_limit"] = limit });
            var res = new List<AdminUser>();
            if (err != null || json is not JsonArray rows || rows.Count == 0) return res;
            var info = rows.Select(r => new AdminUser
            {
                Id = (string)r["id"], Email = (string)r["email"], SignedUp = Utc(r["signed_up"]),
                LastSeen = Utc(r["last_seen"]), Online = (bool?)r["online"] ?? false
            }).Where(u => u.Id != null).ToList();
            var ps = await Supa.Select($"profiles?id=in.({string.Join(",", info.Select(u => Supa.Q(u.Id)))})&select=*");
            var map = ps?.Select(Profile.From).Where(p => p != null).ToDictionary(p => p.Id) ?? new Dictionary<string, Profile>();
            foreach (var u in info)
                if (map.TryGetValue(u.Id, out var p)) { u.Profile = p; res.Add(u); }
            return res;
        }

        public static async Task<string> RespondInvite(long groupId, bool accept) =>
            MapError((await Supa.Call("respond_group_invite", new JsonObject { ["p_group"] = groupId, ["p_accept"] = accept })).error);

        public static async Task<string> LeaveGroup(long groupId) =>
            MapError(await Supa.Delete("group_members", $"group_id=eq.{groupId}&user_id=eq.{Supa.Q(Supa.Me)}"));

        public static async Task<string> Kick(long groupId, string userId) =>
            MapError(await Supa.Delete("group_members", $"group_id=eq.{groupId}&user_id=eq.{Supa.Q(userId)}"));

        public static async Task<string> DeleteGroup(long groupId) =>
            MapError(await Supa.Delete("groups", $"id=eq.{groupId}"));

        /// <summary>Владелец меняет группу (название, @юз, аватарку, тему), админ — галочку.</summary>
        public static async Task<string> UpdateGroup(long groupId, JsonObject changes) =>
            MapError(await Supa.Update("groups", $"id=eq.{groupId}", changes));

        // ---------- темы чатов ----------

        public static async Task<ChatTheme> GetChatTheme(string friendId)
        {
            var (json, err) = await Supa.Call("get_chat_theme", new JsonObject { ["p_other"] = friendId });
            return err != null ? null : ChatTheme.From(json);
        }

        public static async Task<string> SetChatTheme(string friendId, ChatTheme theme) =>
            MapError((await Supa.Call("set_chat_theme", new JsonObject { ["p_other"] = friendId, ["p_theme"] = theme?.ToJson() ?? new JsonObject() })).error);

        public static async Task<List<ChatMessage>> GroupConversation(long groupId, long afterId)
        {
            var rows = await Supa.Select($"group_messages?group_id=eq.{groupId}&id=gt.{afterId}&order=id.asc&limit=200");
            return rows?.Select(ChatMessage.From).ToList() ?? new List<ChatMessage>();
        }

        public static async Task<string> SendGroupText(long groupId, string text) =>
            MapError(await Supa.Insert("group_messages", new JsonObject { ["group_id"] = groupId, ["kind"] = "text", ["body"] = text }));

        public static async Task<Profile> FindByHandle(string handle)
        {
            string h = new string((handle ?? "").Trim().TrimStart('@').ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
            if (h.Length < 3) return null;
            var rows = await Supa.Select($"profiles?handle=eq.{Supa.Q(h)}&select=*");
            return rows != null && rows.Count > 0 ? Profile.From(rows[0]) : null;
        }

        /// <summary>Кто писал мне в личку, но не в друзьях (например, бот MizuEngine с приглашениями).</summary>
        public static async Task<List<Profile>> OtherChatPartners(ICollection<string> exclude)
        {
            var rows = await Supa.Select($"messages?receiver=eq.{Supa.Q(Supa.Me)}&select=sender&order=id.desc&limit=300");
            if (rows == null) return new List<Profile>();
            var ids = rows.Select(r => (string)r["sender"]).Where(id => id != null && id != Supa.Me && !exclude.Contains(id)).Distinct().Take(30).ToList();
            if (ids.Count == 0) return new List<Profile>();
            var ps = await Supa.Select($"profiles?id=in.({string.Join(",", ids.Select(Supa.Q))})&select=*");
            return ps?.Select(Profile.From).Where(p => p != null).ToList() ?? new List<Profile>();
        }

        public static async Task<string> DeletePost(long id) =>
            MapError(await Supa.Delete("posts", $"id=eq.{id}"));

        // ---------- жалобы на посты ----------

        /// <summary>Пожаловаться на пост: reason — spam / insult / other. Повторная жалоба — «уже отправлена».</summary>
        public static async Task<string> ReportPost(long postId, string reason) =>
            MapError(await Supa.Insert("post_reports", new JsonObject { ["post_id"] = postId, ["reporter"] = Supa.Me, ["reason"] = reason }));

        /// <summary>Жалобы для админ-панели, по постам: сверху посты с самой свежей жалобой.</summary>
        public static async Task<List<PostReport>> GetPostReports()
        {
            var rows = await Supa.Select("post_reports?select=post_id,reason,created_at,post:post_id(id,author,body,created_at,a:author(*))&order=id.desc&limit=300");
            var result = new List<PostReport>();
            if (rows == null) return null;
            var byPost = new Dictionary<long, PostReport>();
            foreach (var r in rows)
            {
                var pj = r["post"];
                if (pj == null) continue;
                long id = (long?)pj["id"] ?? 0;
                if (!byPost.TryGetValue(id, out var rep))
                {
                    var author = Profile.From(pj["a"]);
                    if (author == null) continue;
                    rep = new PostReport
                    {
                        Post = new Post
                        {
                            Id = id,
                            Author = (string)pj["author"],
                            Body = (string)pj["body"] ?? "",
                            Created = DateTime.TryParse((string)pj["created_at"], out var d) ? d.ToLocalTime() : DateTime.Now
                        },
                        Author = author,
                        Last = DateTime.TryParse((string)r["created_at"], out var l) ? l.ToLocalTime() : DateTime.Now
                    };
                    byPost[id] = rep;
                    result.Add(rep);
                }
                rep.Count++;
                string reason = (string)r["reason"] ?? "other";
                if (!rep.Reasons.Contains(reason)) rep.Reasons.Add(reason);
            }
            return result;
        }

        /// <summary>Отклонить жалобы на пост (пост остаётся).</summary>
        public static async Task<string> DismissPostReports(long postId) =>
            MapError(await Supa.Delete("post_reports", $"post_id=eq.{postId}"));

        /// <summary>Сколько постов с жалобами (для цифры на вкладке).</summary>
        public static async Task<int> PostReportsCount()
        {
            var rows = await Supa.Select("post_reports?select=post_id&limit=1000");
            return rows?.Select(r => (long?)r["post_id"] ?? 0).Distinct().Count() ?? 0;
        }

        public static async Task<string> Like(long id) =>
            MapError(await Supa.Insert("post_likes", new JsonObject { ["post_id"] = id, ["user_id"] = Supa.Me }));

        public static async Task<string> Unlike(long id) =>
            MapError(await Supa.Delete("post_likes", $"post_id=eq.{id}&user_id=eq.{Supa.Q(Supa.Me)}"));

        // ---------- подписки ----------

        public static Task<int> FollowersCount(string id) => Supa.Count("follows", $"followee=eq.{Supa.Q(id)}");
        public static Task<int> FollowingCount(string id) => Supa.Count("follows", $"follower=eq.{Supa.Q(id)}");
        public static Task<int> FriendsCount(string id) =>
            Supa.Count("friend_requests", $"status=eq.accepted&or=(sender.eq.{Supa.Q(id)},receiver.eq.{Supa.Q(id)})");

        public static async Task<bool> IsFollowing(string id)
        {
            var rows = await Supa.Select($"follows?follower=eq.{Supa.Q(Supa.Me)}&followee=eq.{Supa.Q(id)}&select=followee");
            return rows != null && rows.Count > 0;
        }

        public static async Task<string> Follow(string id) =>
            MapError(await Supa.Insert("follows", new JsonObject { ["follower"] = Supa.Me, ["followee"] = id }));

        public static async Task<string> Unfollow(string id) =>
            MapError(await Supa.Delete("follows", $"follower=eq.{Supa.Q(Supa.Me)}&followee=eq.{Supa.Q(id)}"));

        public static async Task<List<Profile>> Followers(string id)
        {
            var rows = await Supa.Select($"follows?followee=eq.{Supa.Q(id)}&select=p:follower(*)&order=created_at.desc&limit=200");
            return rows?.Select(r => Profile.From(r["p"])).Where(p => p != null).ToList() ?? new List<Profile>();
        }

        public static async Task<List<Profile>> Following(string id)
        {
            var rows = await Supa.Select($"follows?follower=eq.{Supa.Q(id)}&select=p:followee(*)&order=created_at.desc&limit=200");
            return rows?.Select(r => Profile.From(r["p"])).Where(p => p != null).ToList() ?? new List<Profile>();
        }

        // ---------- друзья ----------

        public static async Task<List<Profile>> Friends(string id)
        {
            var rows = await Supa.Select($"friend_requests?status=eq.accepted&or=(sender.eq.{Supa.Q(id)},receiver.eq.{Supa.Q(id)})&select=sender,receiver,s:sender(*),r:receiver(*)&limit=500");
            var list = new List<Profile>();
            if (rows == null) return list;
            foreach (var r in rows)
                list.Add(Profile.From((string)r["sender"] == id ? r["r"] : r["s"]));
            return list.Where(p => p != null).OrderBy(p => p.Nickname, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static async Task<List<FriendRequest>> Incoming()
        {
            var rows = await Supa.Select($"friend_requests?receiver=eq.{Supa.Q(Supa.Me)}&status=eq.pending&select=id,p:sender(*)&order=created_at.desc");
            return rows?.Select(r => new FriendRequest { Id = (long?)r["id"] ?? 0, Other = Profile.From(r["p"]) }).ToList() ?? new List<FriendRequest>();
        }

        public static async Task<List<FriendRequest>> Outgoing()
        {
            var rows = await Supa.Select($"friend_requests?sender=eq.{Supa.Q(Supa.Me)}&status=eq.pending&select=id,p:receiver(*)&order=created_at.desc");
            return rows?.Select(r => new FriendRequest { Id = (long?)r["id"] ?? 0, Other = Profile.From(r["p"]) }).ToList() ?? new List<FriendRequest>();
        }

        public static async Task<(FriendState state, long requestId)> StateWith(string id)
        {
            var rows = await Supa.Select($"friend_requests?or=(and(sender.eq.{Supa.Q(Supa.Me)},receiver.eq.{Supa.Q(id)}),and(sender.eq.{Supa.Q(id)},receiver.eq.{Supa.Q(Supa.Me)}))&select=id,sender,status");
            if (rows == null || rows.Count == 0) return (FriendState.None, 0);
            var r = rows[0];
            long rid = (long?)r["id"] ?? 0;
            if ((string)r["status"] == "accepted") return (FriendState.Friends, rid);
            return ((string)r["sender"] == Supa.Me ? FriendState.Outgoing : FriendState.Incoming, rid);
        }

        /// <summary>Заявка в друзья. Если этот человек уже отправил заявку нам — просто принимаем её.</summary>
        public static async Task<string> AddFriend(string id)
        {
            var (state, rid) = await StateWith(id);
            if (state == FriendState.Incoming) return await Accept(rid);
            if (state != FriendState.None) return null;
            return MapError(await Supa.Insert("friend_requests", new JsonObject { ["sender"] = Supa.Me, ["receiver"] = id }));
        }

        public static async Task<string> Accept(long requestId) =>
            MapError(await Supa.Update("friend_requests", $"id=eq.{requestId}", new JsonObject { ["status"] = "accepted" }));

        /// <summary>Отклонить/отменить заявку или удалить из друзей.</summary>
        public static async Task<string> RemoveRequest(long requestId) =>
            MapError(await Supa.Delete("friend_requests", $"id=eq.{requestId}"));

        // ---------- сообщения ----------

        public static async Task<List<ChatMessage>> Conversation(string friendId, long afterId)
        {
            string me = Supa.Q(Supa.Me), f = Supa.Q(friendId);
            var rows = await Supa.Select($"messages?or=(and(sender.eq.{me},receiver.eq.{f}),and(sender.eq.{f},receiver.eq.{me}))&id=gt.{afterId}&order=id.asc&limit=200");
            return rows?.Select(ChatMessage.From).ToList() ?? new List<ChatMessage>();
        }

        public static async Task<Dictionary<string, int>> UnreadCounts()
        {
            var rows = await Supa.Select($"messages?receiver=eq.{Supa.Q(Supa.Me)}&read=eq.false&select=sender&limit=1000");
            var d = new Dictionary<string, int>();
            if (rows == null) return d;
            foreach (var r in rows)
            {
                string s = (string)r["sender"];
                d[s] = d.TryGetValue(s, out int n) ? n + 1 : 1;
            }
            return d;
        }

        /// <summary>Номер последнего моего сообщения, которое собеседник уже прочитал (0 — ни одного или нет сети).</summary>
        public static async Task<long> LastReadByFriend(string friendId)
        {
            var rows = await Supa.Select($"messages?sender=eq.{Supa.Q(Supa.Me)}&receiver=eq.{Supa.Q(friendId)}&read=eq.true&select=id&order=id.desc&limit=1");
            return rows != null && rows.Count > 0 ? (long?)rows[0]["id"] ?? 0 : 0;
        }

        public static Task<string> MarkRead(string friendId) =>
            Supa.Update("messages", $"sender=eq.{Supa.Q(friendId)}&receiver=eq.{Supa.Q(Supa.Me)}&read=eq.false", new JsonObject { ["read"] = true });

        public static async Task<string> SendText(string friendId, string text) =>
            MapError(await Supa.Insert("messages", new JsonObject { ["receiver"] = friendId, ["kind"] = "text", ["body"] = text }));

        public static async Task<string> SendPreset(string friendId, PresetInfo preset)
        {
            string file = Presets.Export(preset, Path.Combine(Path.GetTempPath(), $"mizuengine-{Guid.NewGuid():N}.mizupreset"));
            if (file == null) return "preset";
            string url = await Supa.Upload($"{Supa.Me}/presets/{Guid.NewGuid():N}.mizupreset", File.ReadAllBytes(file), "application/octet-stream");
            try { File.Delete(file); } catch { }
            if (url == null) return Loc.T("err.upload");
            var payload = new JsonObject { ["name"] = preset.Name, ["url"] = url };
            return MapError(await Supa.Insert("messages", new JsonObject { ["receiver"] = friendId, ["kind"] = "preset", ["body"] = preset.Name, ["payload"] = payload }));
        }

        // ---------- ошибки ----------

        /// <summary>Ошибки сервера → понятный текст на языке приложения.</summary>
        /// <summary>Число после метки в тексте ошибки: «post_cooldown:420» → 420.</summary>
        static long NumberAfter(string e, string mark)
        {
            int i = e.IndexOf(mark, StringComparison.Ordinal);
            if (i < 0) return 0;
            i += mark.Length;
            int j = i;
            while (j < e.Length && char.IsDigit(e[j])) j++;
            return long.TryParse(e.Substring(i, j - i), out var n) ? n : 0;
        }

        public static string MapError(string err)
        {
            if (err == null) return null;
            string e = err.ToLowerInvariant();
            // Лимит аккаунтов на ПК. Ошибку из правила регистрации сервер входа отдаёт как «Database error saving new user».
            if (e == "device_limit" || e.Contains("device_limit") || e.Contains("database error saving new user")) return Loc.T("err.device_limit");
            if (e == "accounts_full") return Loc.T("err.accounts_full");
            if (e == "old_password") return Loc.T("err.old_password");
            if (e.Contains("handle_cooldown")) return Loc.T("err.handle_cooldown");
            // Посты обычных людей (guard_post): мало часов, чаще раза в 10 минут, картинка, длина
            if (e.Contains("post_hours")) return NotEnoughHoursText(NumberAfter(e, "post_hours:"));
            if (e.Contains("post_cooldown"))
            {
                long sec = NumberAfter(e, "post_cooldown:");
                return string.Format(Loc.T("err.post_cooldown"), Math.Max(1, (sec + 59) / 60));
            }
            if (e.Contains("post_text_only")) return Loc.T("err.post_text_only");
            if (e.Contains("post_too_long")) return string.Format(Loc.T("err.post_too_long"), PostMaxLen);
            if (e.Contains("post_empty")) return Loc.T("err.post_empty");
            if (e.Contains("post_reports") && (e.Contains("duplicate") || e.Contains("unique"))) return Loc.T("posts.report_already");
            if (e.Contains("row-level security") && e.Contains("post_reports")) return Loc.T("err.report_own");
            if (e.Contains("nick_cooldown")) return Loc.T("err.nick_cooldown");
            if (e.Contains("handle_format")) return Loc.T("err.handle_format");
            if (e.Contains("profiles_handle_key") || (e.Contains("duplicate") && e.Contains("handle"))) return Loc.T("err.handle_taken");
            if (e.Contains("nick_len")) return Loc.T("err.nick_len");
            if (e.Contains("bio_len")) return Loc.T("err.bio_len");
            if (e.Contains("handle_taken")) return Loc.T("err.handle_taken");
            if (e.Contains("protected_account")) return Loc.T("err.protected_account");
            if (e.Contains("owner_only")) return Loc.T("err.owner_only");
            if (e.Contains("admin_only")) return Loc.T("err.admin_only");
            if (e.Contains("banned")) return Loc.T("err.banned");
            if (e.Contains("muted")) return Loc.T("err.muted");
            if (e.Contains("row-level security") && MyProfile?.Banned == true) return Loc.T("err.banned");
            if (e.Contains("row-level security") && MyProfile?.Muted == true) return Loc.T("err.muted");
            if (e.Contains("row-level security") && e.Contains("messages")) return Loc.T("err.not_friends");
            if (e.Contains("invalid login")) return Loc.T("err.login");
            if (e.Contains("already registered")) return Loc.T("err.registered");
            if (e.Contains("password") && e.Contains("6")) return Loc.T("err.password");
            if (e.Contains("email not confirmed")) return Loc.T("err.confirm");
            if (e == "network") return Loc.T("err.network");
            if (e == "not_configured") return Loc.T("err.not_configured");
            if (e.Contains("rate limit")) return Loc.T("err.rate");
            if (e.Contains("expired or is invalid") || e == "bad_code" || e.Contains("otp_expired")) return Loc.T("err.code");
            if (e.Contains("only request this after")) return Loc.T("err.wait");
            if (e.Contains("session_expired")) return Loc.T("err.session_expired");
            if (e.Contains("same_password") || e.Contains("different from the old")) return Loc.T("err.same_password");
            if (e.Contains("invalid") && e.Contains("email")) return Loc.T("err.email");
            if (e.Contains("row-level security") && e.Contains("posts")) return Loc.T("err.posts_only_ceo");
            if (e.Contains("slow_mode")) return Loc.T("err.slow_mode");
            if (e.Contains("staff_only")) return Loc.T("err.staff_only");
            if (e.Contains("group_full")) return Loc.T("err.group_full");
            if (e.Contains("already_invited")) return Loc.T("err.already_invited");
            if (e.Contains("already_member")) return Loc.T("err.already_member");
            if (e.Contains("user_not_found")) return Loc.T("groups.not_found");
            if (e.Contains("no_invite")) return Loc.T("err.no_invite");
            if (e.Contains("not_member")) return Loc.T("err.not_member");
            return err;
        }
    }
}
