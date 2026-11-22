// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MizuEngine
{
    /// <summary>Опубликованная сборка в «Лучших сборках».</summary>
    internal sealed class SharedBuild
    {
        public long Id;
        public Profile Author;
        public string Name, Description, CoverUrl, FilePath;   // FilePath = null — скачать нельзя (сборка Plus без подписки)
        public long FileSize;
        public bool Plus;
        public JsonObject Summary;
        public DateTime Created;
        public int Likes, PeriodLikes, Comments, Saves;
        public bool Liked;

        public bool Mine => Author?.Id == Supa.Me && Supa.Me != null;
        /// <summary>Можно применить, сохранить к себе и скачать: обычная сборка, или есть Mizu Plus, или это моя.</summary>
        public bool Unlocked => !Plus || global::MizuEngine.Plus.IsActive || Mine || SocialApi.MyProfile?.CanPost == true;

        public static SharedBuild From(JsonNode j)
        {
            if (j == null) return null;
            var author = Profile.From(j["author"]);
            if (author == null) return null;
            return new SharedBuild
            {
                Id = (long?)j["id"] ?? 0,
                Author = author,
                Name = (string)j["name"] ?? "MizuEngine",
                Description = (string)j["description"],
                CoverUrl = (string)j["cover_url"],
                FilePath = (string)j["file_path"],
                FileSize = (long?)j["file_size"] ?? 0,
                Plus = (bool?)j["plus"] ?? false,
                Summary = j["summary"] as JsonObject ?? new JsonObject(),
                Created = SocialApi.Utc(j["created_at"]).ToLocalTime(),
                Likes = (int?)j["likes"] ?? 0,
                PeriodLikes = (int?)j["period_likes"] ?? 0,
                Comments = (int?)j["comments"] ?? 0,
                Saves = (int?)j["saves"] ?? 0,
                Liked = (bool?)j["liked"] ?? false
            };
        }
    }

    /// <summary>Запросы «Лучших сборок» (0.11.5, сервер — Server/v10.sql).</summary>
    internal static class BuildsApi
    {
        /// <summary>Публиковать сборки можно после стольких часов в MizuEngine (сотрудникам и админам — сразу).</summary>
        public const int Hours = 3;

        public static bool EnoughHours(Profile p) => p != null && (p.CanPost || p.UsageSeconds >= Hours * 3600L);

        public static string NotEnoughHoursText(long usageSeconds) =>
            string.Format(Loc.T("builds.need_hours"), Hours, SocialApi.HoursMinutes(usageSeconds));

        static List<SharedBuild> Parse(JsonNode j) =>
            (j as JsonArray)?.Select(SharedBuild.From).Where(b => b != null).ToList();

        /// <summary>Лучшие за период: day / week / month (20 штук). null — нет связи или сервер старый.</summary>
        public static async Task<(List<SharedBuild> list, string error)> Top(string period)
        {
            var (j, err) = await Supa.Call("builds_feed", new JsonObject { ["p_period"] = period, ["p_limit"] = 20 });
            return err != null ? (null, SocialApi.MapError(err)) : (Parse(j) ?? new List<SharedBuild>(), null);
        }

        /// <summary>Сборки человека (новые сверху).</summary>
        public static async Task<List<SharedBuild>> ByAuthor(string userId)
        {
            var (j, err) = await Supa.Call("builds_feed", new JsonObject { ["p_author"] = userId, ["p_limit"] = 50 });
            return err != null ? null : Parse(j);
        }

        public static async Task<SharedBuild> One(long id)
        {
            var (j, err) = await Supa.Call("builds_feed", new JsonObject { ["p_id"] = id, ["p_limit"] = 1 });
            return err != null ? null : Parse(j)?.FirstOrDefault();
        }

        /// <summary>
        /// Опубликовать свою сборку: упаковать (видео при необходимости сжимается), загрузить файл в закрытое хранилище,
        /// обложку — в публичное, и создать запись. status — подпись «Сжимаю видео… / Загружаю…». Возвращает (id, ошибка).
        /// </summary>
        public static async Task<(long id, string error)> Publish(PresetInfo p, string description, Action<string> status)
        {
            if (!Supa.SignedIn) return (0, Loc.T("builds.need_login"));
            var me = SocialApi.MyProfile;
            if (me != null && !EnoughHours(me)) return (0, NotEnoughHoursText(me.UsageSeconds));

            var (zip, packErr) = await BuildFiles.PackForUpload(p, status);
            if (zip == null) return (0, packErr);
            string path = $"{Supa.Me}/{Guid.NewGuid():N}.mizupreset";
            try
            {
                long size = new FileInfo(zip).Length;
                status?.Invoke(Loc.T("builds.uploading"));
                byte[] data = await File.ReadAllBytesAsync(zip);
                string upErr = await Supa.UploadTo("builds", path, data, "application/zip");
                if (upErr != null) return (0, SocialApi.MapError(upErr) == upErr ? Loc.T("err.upload") : SocialApi.MapError(upErr));

                string coverUrl = null;
                if (p.Cover != null && File.Exists(p.Cover))
                {
                    coverUrl = await SocialApi.UploadImage(p.Cover, "build-cover", 1280);
                    if (coverUrl == null) { await Supa.DeleteObject("builds", path); return (0, Loc.T("err.upload")); }
                }

                var row = new JsonObject
                {
                    ["name"] = p.Name,
                    ["description"] = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                    ["cover_url"] = coverUrl,
                    ["file_path"] = path,
                    ["file_size"] = size,
                    ["plus"] = p.Plus,
                    ["summary"] = Presets.SummaryOf(p)
                };
                var (json, err) = await Supa.InsertReturning("builds", row);
                if (err != null)
                {
                    await Supa.DeleteObject("builds", path);   // запись не создалась — файл не нужен
                    return (0, SocialApi.MapError(err));
                }
                JsonNode first = json is JsonArray arr ? arr.FirstOrDefault() : json;
                long id = (long?)first?["id"] ?? 0;
                if (id > 0) Presets.MarkPublished(p, id);
                return (id, null);
            }
            catch (Exception e) { Settings.Log(e); return (0, Loc.T("err.generic")); }
            finally { try { File.Delete(zip); } catch { } }
        }

        public static async Task<string> Like(long id, bool on) =>
            SocialApi.MapError(on ? await Supa.Insert("build_likes", new JsonObject { ["build_id"] = id, ["user_id"] = Supa.Me })
                                  : await Supa.Delete("build_likes", $"build_id=eq.{id}&user_id=eq.{Supa.Q(Supa.Me)}"));

        /// <summary>Отметить, что сборку сохранили/скачали/применили (каждый человек считается один раз).</summary>
        static async Task CountSave(long id)
        {
            try { await Supa.Insert("build_saves", new JsonObject { ["build_id"] = id, ["user_id"] = Supa.Me }); } catch { }
        }

        /// <summary>
        /// Скачать файл сборки во временную папку. Сервер сам не отдаст сборку Plus без подписки.
        /// Возвращает путь к файлу или ошибку.
        /// </summary>
        public static async Task<(string file, string error)> Download(SharedBuild b)
        {
            if (!Supa.SignedIn) return (null, Loc.T("builds.need_login"));
            if (!b.Unlocked || string.IsNullOrEmpty(b.FilePath)) return (null, Loc.T("builds.plus_only"));
            var (data, status) = await Supa.DownloadPrivate("builds", b.FilePath);
            if (data == null)
                return (null, status == 400 || status == 403 || status == 404 ? (b.Plus ? Loc.T("builds.plus_only") : Loc.T("builds.gone")) : Loc.T("err.network"));
            string dir = Path.Combine(Path.GetTempPath(), "MizuEngine-builds");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"build-{b.Id}-{Guid.NewGuid():N}.mizupreset");
            await File.WriteAllBytesAsync(file, data);
            _ = CountSave(b.Id);
            return (file, null);
        }

        /// <summary>Скачать и добавить в «Мои сборки».</summary>
        public static async Task<(PresetInfo preset, string error)> SaveToMine(SharedBuild b)
        {
            var (file, err) = await Download(b);
            if (file == null) return (null, err);
            try
            {
                var p = await Task.Run(() => Presets.Import(file, b.Name));
                return p != null ? (p, null) : (null, Loc.T("presets.bad_file"));
            }
            finally { try { File.Delete(file); } catch { } }
        }

        /// <summary>Скачать во временную папку (чтобы применить, не добавляя в «Мои сборки»). Папку потом удалить: Presets.Delete.</summary>
        public static async Task<(PresetInfo preset, string error)> Fetch(SharedBuild b)
        {
            var (file, err) = await Download(b);
            if (file == null) return (null, err);
            try
            {
                var p = await Task.Run(() => Presets.Import(file, b.Name, temp: true));
                return p != null ? (p, null) : (null, Loc.T("presets.bad_file"));
            }
            finally { try { File.Delete(file); } catch { } }
        }

        /// <summary>Убрать сборку из «Лучших сборок» (автор, админ или сотрудник). Файл тоже удаляется.</summary>
        public static async Task<string> Delete(long id, string filePath)
        {
            string err = SocialApi.MapError(await Supa.Delete("builds", $"id=eq.{id}"));
            if (err != null) return err;
            if (!string.IsNullOrEmpty(filePath)) await Supa.DeleteObject("builds", filePath);
            foreach (var p in Presets.List().Where(x => x.PublishedId == id)) Presets.MarkPublished(p, 0);
            return null;
        }
    }
}
