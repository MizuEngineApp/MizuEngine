// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MizuEngine
{
    /// <summary>Сборка от MizuEngine (публикует админ). Data — настройки; у закрытых сборок Plus без подписки её нет.</summary>
    internal sealed class OfficialPreset
    {
        public long Id;
        public string Name, Description, Theme, Weather, ThemeUrl, ParticleUrl;
        public bool PlusOnly;
        public DateTime Created;
        public JsonNode Data;

        public static OfficialPreset From(JsonNode j) => j == null ? null : new OfficialPreset
        {
            Id = (long?)j["id"] ?? 0,
            Name = (string)j["name"] ?? "MizuEngine",
            Description = (string)j["description"] ?? "",
            PlusOnly = (bool?)j["plus_only"] ?? false,
            Theme = (string)j["theme"] ?? (string)j["data"]?["Theme"],
            Weather = (string)j["weather"] ?? (string)j["data"]?["Weather"],
            ThemeUrl = (string)j["theme_url"],
            ParticleUrl = (string)j["particle_url"],
            Created = SocialApi.Utc(j["created_at"]),
            Data = j["data"]
        };
    }

    /// <summary>Запросы Mizu Plus: админ-выдача, обзор донатов, сборки от MizuEngine.</summary>
    internal static class PlusApi
    {
        // ---------- админ ----------

        /// <summary>kind: month (130 ₽, +30 дней), forever (500 ₽, навсегда), revoke (забрать). null — получилось.</summary>
        public static async Task<string> AdminGrant(string userId, string kind)
        {
            var (_, err) = await Supa.Call("admin_grant_plus", new JsonObject { ["p_user"] = userId, ["p_kind"] = kind });
            if (err != null) return SocialApi.MapError(err);
            if (userId == Supa.Me) await Plus.RefreshAsync();
            return null;
        }

        /// <summary>Для админ-панели: подключён ли DonationAlerts, последние донаты и выдачи.</summary>
        public static async Task<JsonNode> Overview()
        {
            var (j, err) = await Supa.Call("admin_plus_overview", new JsonObject());
            if (err != null) Settings.Log(new Exception("admin_plus_overview: " + err));
            return err == null ? j : null;
        }

        // ---------- сборки от MizuEngine ----------

        /// <summary>Все сборки (без настроек) — чтобы закрытые сборки Plus тоже было видно, с замочком.</summary>
        public static async Task<List<OfficialPreset>> Catalog()
        {
            var list = new List<OfficialPreset>();
            var (j, err) = await Supa.Call("official_catalog", new JsonObject());
            if (err != null) { Settings.Log(new Exception("official_catalog: " + err)); return null; }
            if (j is JsonArray arr) foreach (var x in arr) { var p = OfficialPreset.From(x); if (p != null) list.Add(p); }
            return list;
        }

        /// <summary>Полная сборка с настройками. null — нет доступа (сборка Plus без подписки) или нет связи.</summary>
        public static async Task<OfficialPreset> Load(long id)
        {
            var rows = await Supa.Select($"official_presets?id=eq.{id}&select=*");
            return rows != null && rows.Count > 0 ? OfficialPreset.From(rows[0]) : null;
        }

        /// <summary>Админ публикует текущие настройки как сборку от MizuEngine.</summary>
        public static async Task<string> Publish(Settings s, string name, string description, bool plusOnly)
        {
            if (string.IsNullOrWhiteSpace(name)) return Loc.T("plus.official.need_name");
            var data = Presets.Snapshot(s);
            string themeUrl = null, particleUrl = null;
            if (s.Theme == "custom" && File.Exists(s.CustomThemeImage))
            {
                themeUrl = await Supa.Upload($"{Supa.Me}/official/theme-{Guid.NewGuid():N}{Ext(s.CustomThemeImage, ".jpg")}", File.ReadAllBytes(s.CustomThemeImage), Mime(s.CustomThemeImage));
                if (themeUrl == null) return Loc.T("err.upload");
            }
            if (s.Weather == "custom" && File.Exists(s.CustomParticle))
            {
                particleUrl = await Supa.Upload($"{Supa.Me}/official/particle-{Guid.NewGuid():N}{Ext(s.CustomParticle, ".png")}", File.ReadAllBytes(s.CustomParticle), Mime(s.CustomParticle));
                if (particleUrl == null) return Loc.T("err.upload");
            }
            var row = new JsonObject
            {
                ["name"] = name.Trim(),
                ["description"] = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                ["data"] = data,
                ["theme_url"] = themeUrl,
                ["particle_url"] = particleUrl,
                ["plus_only"] = plusOnly
            };
            return SocialApi.MapError(await Supa.Insert("official_presets", row));
        }

        public static async Task<string> Delete(long id) =>
            SocialApi.MapError(await Supa.Delete("official_presets", $"id=eq.{id}"));

        /// <summary>Скачивает картинки сборки во временную папку и применяет её. null — получилось.</summary>
        public static async Task<string> Apply(OfficialPreset p, Settings s)
        {
            if (p.Data == null) return Loc.T("plus.locked");
            var (theme, particle) = await DownloadImages(p);
            Presets.ApplySnapshot(p.Data, s, theme, particle);
            return null;
        }

        /// <summary>Сохранить сборку от MizuEngine к себе в «Мои сборки».</summary>
        public static async Task<PresetInfo> SaveToMine(OfficialPreset p)
        {
            if (p.Data == null) return null;
            var (theme, particle) = await DownloadImages(p);
            return Presets.SaveSnapshot(p.Name, p.Data, theme, particle);
        }

        static async Task<(string theme, string particle)> DownloadImages(OfficialPreset p)
        {
            string dir = Path.Combine(Path.GetTempPath(), "MizuEngine-official");
            Directory.CreateDirectory(dir);
            string theme = null, particle = null;
            if (!string.IsNullOrEmpty(p.ThemeUrl))
            {
                var bytes = await Supa.Download(p.ThemeUrl);
                if (bytes != null) { theme = Path.Combine(dir, $"theme-{p.Id}.jpg"); File.WriteAllBytes(theme, bytes); }
            }
            if (!string.IsNullOrEmpty(p.ParticleUrl))
            {
                var bytes = await Supa.Download(p.ParticleUrl);
                if (bytes != null) { particle = Path.Combine(dir, $"particle-{p.Id}.png"); File.WriteAllBytes(particle, bytes); }
            }
            return (theme, particle);
        }

        static string Ext(string file, string fallback)
        {
            string e = Path.GetExtension(file)?.ToLowerInvariant();
            return string.IsNullOrEmpty(e) || e.Length > 5 ? fallback : e;
        }

        static string Mime(string file) => Path.GetExtension(file)?.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "image/jpeg"
        };
    }
}
