// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>Выдача подписки: из доната или от админа. По ней показывается поздравление.</summary>
    internal sealed class PlusGrant
    {
        public long Id;
        public string Kind;      // month / forever
        public string Source;    // donation / admin
        public int Days;
        public bool Forever;
        public DateTime Until;   // UTC
    }

    /// <summary>
    /// Подписка Mizu Plus текущего аккаунта.
    /// Статус берётся с сервера (plus_status): подделать его в приложении нельзя — значок у ника видят из базы.
    /// Последний статус запоминается в настройках, чтобы функции Plus работали и без интернета.
    /// Пока открыта вкладка Mizu Plus или нажата «Оплатить», статус проверяется каждые 20 секунд — подписка
    /// появляется меньше чем через минуту после доната. В остальное время — раз в 3 минуты.
    /// </summary>
    internal static class Plus
    {
        /// <summary>Страница доната (DALink). Донаты с неё и с DonationAlerts попадают в один аккаунт.</summary>
        public const string DonateUrl = "https://dalink.to/themrrain";
        public const int PriceMonth = 130, PriceForever = 500;

        static volatile bool _active;
        /// <summary>Есть ли сейчас Plus. Читается из любого потока (эффекты живут в своём).</summary>
        public static bool IsActive => _active && !Guard.Tampered;
        public static bool Forever { get; private set; }
        public static DateTime Until { get; private set; }      // UTC
        public static string Code { get; private set; }         // MP-XXXXXX
        /// <summary>Статус уже пришёл с сервера (а не только из сохранённого).</summary>
        public static bool Known { get; private set; }

        /// <summary>Подписка появилась, закончилась или продлилась. Вызывается в главном потоке.</summary>
        public static event Action Changed;
        /// <summary>Новая выдача подписки — пора поздравлять. Вызывается в главном потоке.</summary>
        public static event Action<PlusGrant> Granted;

        static Settings _s;
        static DispatcherTimer _timer;
        static DateTime _hotUntil, _lastRefresh;
        static bool _busy;
        static readonly HashSet<long> _shown = new HashSet<long>();

        static DateTime _checkedAt = DateTime.MinValue;   // когда сервер последний раз подтвердил статус (UTC)
        const double OfflineDays = 3;                       // столько Plus работает без интернета, дальше нужен сервер

        public static void Init(Settings s)
        {
            _s = s;
            // Сохранённый статус — до ответа сервера. Верим ему, только если на нём «печать» программы
            // (ручная правка settings.json Plus не включает) и сервер подтверждал его не раньше чем 3 дня назад.
            var parts = !string.IsNullOrEmpty(s.PlusUser) ? Guard.Unseal(s.PlusSeal)?.Split('|') : null;
            if (parts != null && parts.Length == 4 && parts[0] == s.PlusUser
                && long.TryParse(parts[2], out long untilTicks) && long.TryParse(parts[3], out long checkedTicks))
            {
                Forever = parts[1] == "1";
                Until = new DateTime(Math.Clamp(untilTicks, DateTime.MinValue.Ticks, DateTime.MaxValue.Ticks), DateTimeKind.Utc);
                _checkedAt = new DateTime(Math.Clamp(checkedTicks, DateTime.MinValue.Ticks, DateTime.MaxValue.Ticks), DateTimeKind.Utc);
                bool fresh = _checkedAt <= DateTime.UtcNow.AddMinutes(5) && (DateTime.UtcNow - _checkedAt).TotalDays <= OfflineDays;
                _active = fresh && (Forever || Until > DateTime.UtcNow);
            }
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _timer.Tick += async (o, e) => await Tick();
            _timer.Start();
            Supa.SessionChanged += OnSession;
        }

        static void OnSession()
        {
            // Сменили аккаунт: у другого аккаунта своя подписка
            if (Supa.Me != _s.PlusUser)
            {
                Code = null;
                Set(false, false, DateTime.MinValue, null);
            }
            _ = RefreshAsync();
        }

        /// <summary>Пользователь, скорее всего, сейчас оплачивает — следующие 30 минут проверяем часто.</summary>
        public static void MarkHot()
        {
            _hotUntil = DateTime.UtcNow.AddMinutes(30);
            if (DateTime.UtcNow - _lastRefresh > TimeSpan.FromSeconds(15)) _ = RefreshAsync();
        }

        static async Task Tick()
        {
            // Месячная подписка закончилась, пока программа работала
            if (_active && !Forever && Until <= DateTime.UtcNow) Set(false, false, Until, _s.PlusUser);
            // Слишком долго без связи с сервером — сохранённому статусу больше не верим
            if (_active && (DateTime.UtcNow - _checkedAt).TotalDays > OfflineDays) Set(false, Forever, Until, null);
            if (!Supa.SignedIn) return;
            bool hot = DateTime.UtcNow < _hotUntil;
            if (hot || DateTime.UtcNow - _lastRefresh >= TimeSpan.FromMinutes(3)) await RefreshAsync();
        }

        /// <summary>Спросить сервер: есть ли Plus, код для доната и новые выдачи.</summary>
        public static async Task RefreshAsync()
        {
            if (_busy || !Supa.SignedIn) return;
            _busy = true;
            try
            {
                string who = Supa.Me;
                var (j, err) = await Supa.Call("plus_status", new JsonObject());
                _lastRefresh = DateTime.UtcNow;
                if (err != null || j == null || who != Supa.Me) return;   // нет связи или пока ждали — сменили аккаунт

                Code = (string)j["code"];
                bool forever = (bool?)j["forever"] ?? false;
                DateTime until = SocialApi.Utc(j["until"]);
                bool active = (bool?)j["active"] ?? false;
                Known = true;
                Set(active, forever, until, who, fromServer: true);

                if (j["grants"] is JsonArray grants)
                    foreach (var g in grants)
                    {
                        long id = (long?)g?["id"] ?? 0;
                        if (id == 0 || !_shown.Add(id)) continue;
                        var grant = new PlusGrant
                        {
                            Id = id,
                            Kind = (string)g["kind"] ?? "month",
                            Source = (string)g["source"] ?? "donation",
                            Days = (int?)g["days"] ?? 0,
                            Forever = (bool?)g["forever"] ?? false,
                            Until = SocialApi.Utc(g["until"])
                        };
                        try { Granted?.Invoke(grant); } catch (Exception e) { Settings.Log(e); }
                        _ = Supa.Call("plus_ack", new JsonObject { ["p_id"] = id });
                    }
            }
            catch (Exception e) { Settings.Log(e); }
            finally { _busy = false; }
        }

        static void Set(bool active, bool forever, DateTime until, string user, bool fromServer = false)
        {
            bool changed = active != _active || forever != Forever || until != Until;
            _active = active;
            Forever = forever;
            Until = until;

            // Сохраняем с «печатью»; подтверждение сервера обновляем не чаще раза в 6 часов (чтобы не писать файл каждые 3 минуты)
            bool resealTime = fromServer && (DateTime.UtcNow - _checkedAt).TotalHours >= 6;
            if (fromServer && resealTime) _checkedAt = DateTime.UtcNow;
            if (user != null && (_s.PlusUser != user || _s.PlusForever != forever || _s.PlusUntil != until || resealTime || string.IsNullOrEmpty(_s.PlusSeal)))
            {
                if (fromServer && _checkedAt == DateTime.MinValue) _checkedAt = DateTime.UtcNow;
                _s.PlusUser = user;
                _s.PlusForever = forever;
                _s.PlusUntil = until;
                _s.PlusSeal = Guard.Seal($"{user}|{(forever ? 1 : 0)}|{until.Ticks}|{_checkedAt.Ticks}");
                _s.Save();
            }

            // Мой профиль в памяти — тоже с новой подпиской (значок у ника появится без перезагрузки)
            var me = SocialApi.MyProfile;
            if (me != null) { me.PlusForever = forever; me.PlusUntil = until; }

            if (changed) Changed?.Invoke();
        }

        /// <summary>Открыть страницу доната в браузере.</summary>
        public static bool OpenDonate()
        {
            MarkHot();
            try { Process.Start(new ProcessStartInfo(DonateUrl) { UseShellExecute = true }); return true; }
            catch (Exception e) { Settings.Log(e); return false; }
        }

        /// <summary>«до 18 декабря 2026» или «навсегда».</summary>
        public static string UntilText(bool forever, DateTime until) =>
            forever ? Loc.T("plus.forever_short")
                    : string.Format(Loc.T("plus.until"), until.ToLocalTime().ToString("d MMMM yyyy"));
    }
}
