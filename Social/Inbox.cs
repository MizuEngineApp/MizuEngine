// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>Новое сообщение для уведомления.</summary>
    internal sealed class InboxMessage
    {
        public long Id, GroupId;
        public string FriendId, SenderId, Kind, Text, GroupName;
        public Profile Sender;
        public string Key => GroupId > 0 ? "g:" + GroupId : "u:" + FriendId;
    }

    /// <summary>
    /// Следит за новыми сообщениями, даже когда окно MizuEngine закрыто (программа в трее):
    /// раз в 6 секунд спрашивает сервер, считает непрочитанные (цифра на вкладке «Чаты») и сообщает о новых.
    /// Личные сообщения: «прочитано» хранит сервер. Группы: последнее прочитанное запоминаем в settings.json.
    /// </summary>
    internal static class Inbox
    {
        public static int Unread { get; private set; }
        public static event Action Changed;
        public static event Action<InboxMessage> Arrived;

        static Settings _s;
        static DispatcherTimer _timer;
        static bool _busy, _again;
        static string _me;                 // для какого аккаунта всё посчитано
        static long _lastPersonal = -1;    // самое новое личное сообщение, о котором уже знаем (-1 — ещё не спрашивали)
        static long _lastGroup = -1;
        static List<Group> _groups = new List<Group>();
        static DateTime _groupsAt = DateTime.MinValue;
        static readonly Dictionary<string, Profile> People = new Dictionary<string, Profile>();

        public static void Start(Settings s)
        {
            _s = s;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            _timer.Tick += async (o, e) => await Poll();
            _timer.Start();
            Supa.SessionChanged += () => Application.Current?.Dispatcher.BeginInvoke(new Action(() => { _me = null; Nudge(); }));
            Nudge();
        }

        /// <summary>Проверить прямо сейчас (например, после прочтения чата).</summary>
        public static void Nudge()
        {
            if (_busy) { _again = true; return; }
            _ = Poll();
        }

        static string GroupKey(long gid) => _me + ":" + gid;

        static long ReadMark(long gid) => _s != null && _s.GroupRead.TryGetValue(GroupKey(gid), out long v) ? v : -1;

        /// <summary>Группу открыли и дочитали до этого сообщения.</summary>
        public static void MarkGroupRead(long gid, long lastId)
        {
            if (_s == null || _me == null || lastId <= 0 || ReadMark(gid) >= lastId) return;
            _s.GroupRead[GroupKey(gid)] = lastId;
            _s.Save();
            Nudge();
        }

        static void SetUnread(int n)
        {
            if (n == Unread) return;
            Unread = n;
            Changed?.Invoke();
        }

        static async Task Poll()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                if (!Supa.SignedIn) { _me = null; SetUnread(0); return; }
                if (_me != Supa.Me)
                {
                    _me = Supa.Me;
                    _lastPersonal = _lastGroup = -1;   // новый аккаунт: старые сообщения не показываем уведомлениями
                    _groupsAt = DateTime.MinValue;
                }
                string account = _me, me = Supa.Q(_me);
                var fresh = new List<InboxMessage>();

                // 1) Личные: новые с прошлой проверки + сколько всего непрочитанных
                var rows = await Supa.Select(_lastPersonal < 0
                    ? $"messages?receiver=eq.{me}&select=id&order=id.desc&limit=1"
                    : $"messages?receiver=eq.{me}&id=gt.{_lastPersonal}&select=id,sender,kind,body&order=id.asc&limit=30");
                var unreadRows = await Supa.Select($"messages?receiver=eq.{me}&read=eq.false&select=id&limit=999");
                if (account != Supa.Me) return;   // аккаунт сменили, пока ждали ответа
                if (rows != null)
                {
                    if (_lastPersonal < 0) _lastPersonal = rows.Count > 0 ? (long?)rows[0]["id"] ?? 0 : 0;
                    else
                        foreach (var r in rows)
                        {
                            long id = (long?)r["id"] ?? 0;
                            if (id <= _lastPersonal) continue;
                            _lastPersonal = id;
                            fresh.Add(new InboxMessage { Id = id, FriendId = (string)r["sender"], SenderId = (string)r["sender"], Kind = (string)r["kind"] ?? "text", Text = (string)r["body"] ?? "" });
                        }
                }
                int unread = unreadRows?.Count ?? 0;

                // 2) Группы
                if ((DateTime.UtcNow - _groupsAt).TotalSeconds > 60)
                {
                    _groups = await SocialApi.MyGroups();
                    _groupsAt = DateTime.UtcNow;
                }
                if (_groups.Count > 0)
                {
                    // Группа, где мы ещё ни разу не были: вся старая история не считается непрочитанной
                    foreach (var g in _groups.Where(g => ReadMark(g.Id) < 0).ToList())
                    {
                        var last = await Supa.Select($"group_messages?group_id=eq.{g.Id}&select=id&order=id.desc&limit=1");
                        _s.GroupRead[GroupKey(g.Id)] = last != null && last.Count > 0 ? (long?)last[0]["id"] ?? 0 : 0;
                        _s.Save();
                    }
                    long from = _groups.Min(g => ReadMark(g.Id));
                    if (_lastGroup >= 0) from = Math.Min(from, _lastGroup);
                    string ids = string.Join(",", _groups.Select(g => g.Id));
                    var grows = await Supa.Select($"group_messages?group_id=in.({ids})&id=gt.{Math.Max(0, from)}&sender=neq.{me}&select=id,group_id,sender,kind,body&order=id.asc&limit=300");
                    if (account != Supa.Me) return;
                    long maxId = _lastGroup;
                    foreach (var r in grows ?? new System.Text.Json.Nodes.JsonArray())
                    {
                        long id = (long?)r["id"] ?? 0, gid = (long?)r["group_id"] ?? 0;
                        if (id > ReadMark(gid)) unread++;
                        // Уведомляем только о том, что пришло после прошлой проверки И ещё не прочитано.
                        // Раньше после пустой первой проверки отметка становилась 0, и при следующем открытии
                        // программы старые сообщения групп снова приходили уведомлениями.
                        if (_lastGroup >= 0 && id > _lastGroup && id > ReadMark(gid))
                            fresh.Add(new InboxMessage
                            {
                                Id = id, GroupId = gid, SenderId = (string)r["sender"], Kind = (string)r["kind"] ?? "text", Text = (string)r["body"] ?? "",
                                GroupName = _groups.FirstOrDefault(g => g.Id == gid)?.Name
                            });
                        maxId = Math.Max(maxId, id);
                    }
                    long newestRead = _groups.Max(g => ReadMark(g.Id));
                    _lastGroup = Math.Max(Math.Max(maxId, newestRead), Math.Max(0, _lastGroup));
                }
                else if (_lastGroup < 0) _lastGroup = 0;

                SetUnread(unread);

                // 3) Уведомления: не больше трёх за раз (после долгого перерыва не засыпаем экран карточками)
                foreach (var m in fresh.Skip(Math.Max(0, fresh.Count - 3)))
                {
                    m.Sender = await Person(m.SenderId);
                    if (account != Supa.Me) return;
                    try { Arrived?.Invoke(m); } catch (Exception e) { Settings.Log(e); }
                }
            }
            catch (Exception e) { Settings.Log(e); }
            finally
            {
                _busy = false;
                if (_again) { _again = false; _ = Poll(); }
            }
        }

        static async Task<Profile> Person(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (People.TryGetValue(id, out var p)) return p;
            p = await SocialApi.GetProfile(id);
            if (p != null) People[id] = p;
            return p;
        }
    }
}
