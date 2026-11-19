// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MizuEngine
{
    /// <summary>Агент Muzule: имя, значок, характер (инструкции), модель и что ему можно.</summary>
    internal sealed class MuzuleAgent
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Glyph { get; set; } = "";
        public string Color { get; set; } = "purple";
        public string Instructions { get; set; } = "";
        public string Model { get; set; }           // null — модель по умолчанию из настроек
        public bool Control { get; set; } = true;   // может менять настройки MizuEngine
        public bool Web { get; set; }               // поиск в интернете (платный у Anthropic)

        [JsonIgnore] public bool BuiltIn => Id == MuzuleStore.MizuId;
    }

    /// <summary>Переписка с агентом. Messages — ровно в формате Claude API (так её можно сразу отправлять).</summary>
    internal sealed class MuzuleChat
    {
        public string Id { get; set; }
        public string AgentId { get; set; }
        public string Title { get; set; }
        public string Model { get; set; }
        public DateTime Updated { get; set; }
        public bool Spotlight { get; set; }         // начат в быстром окне (Ctrl+Alt+M)
        public long InTokens { get; set; }
        public long OutTokens { get; set; }
        public JsonArray Messages { get; set; } = new JsonArray();
    }

    /// <summary>Строка списка чатов (без самих сообщений — их читаем, только когда чат открывают).</summary>
    internal sealed class MuzuleChatInfo
    {
        public string Id { get; set; }
        public string AgentId { get; set; }
        public string Title { get; set; }
        public DateTime Updated { get; set; }
        public bool Spotlight { get; set; }
    }

    /// <summary>
    /// Всё Muzule на этом ПК (%AppData%\MizuEngine\muzule): ключ Claude API (зашифрован DPAPI — прочитать может только
    /// этот пользователь Windows), агенты и история чатов. На наш сервер ничего не отправляется.
    /// </summary>
    internal static class MuzuleStore
    {
        public const string MizuId = "mizu";
        const int MaxChats = 80;

        public static readonly (string Id, string Short, string Key)[] Models =
        {
            ("claude-opus-5-5", "Opus 5.5", "muzule.model.opus"),
            ("claude-sonnet-5-5", "Sonnet 5.5", "muzule.model.sonnet"),
            ("claude-haiku-5-5", "Haiku 5.5", "muzule.model.haiku"),
        };
        public const string DefaultModel = "claude-sonnet-5-5";

        public static string ValidModel(string m) => Models.Any(x => x.Id == m) ? m : DefaultModel;
        public static string ModelShort(string m) => Models.FirstOrDefault(x => x.Id == m).Short ?? "Sonnet 5.5";

        /// <summary>Значки и цвета, из которых выбирают вид своего агента.</summary>
        public static readonly string[] Glyphs = { "", "", "", "", "", "", "", "", "", "", "", "" };
        public static readonly string[] Colors = { "purple", "blue", "teal", "green", "orange", "pink", "red", "gray" };

        public static (System.Windows.Media.Color a, System.Windows.Media.Color b) ColorOf(string name) => name switch
        {
            "blue" => UI.Blue, "teal" => UI.Teal, "green" => UI.Green, "orange" => UI.Orange,
            "pink" => UI.Pink, "red" => UI.Red, "gray" => UI.Gray, _ => UI.Purple
        };

        static string Dir => Path.Combine(Settings.Folder, "muzule");
        static string ChatsDir => Path.Combine(Dir, "chats");
        static string KeyFile => Path.Combine(Dir, "key.bin");
        static string AgentsFile => Path.Combine(Dir, "agents.json");
        static string IndexFile => Path.Combine(Dir, "index.json");

        static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = false };

        public static event Action ChatsChanged;
        public static event Action AgentsChanged;

        // ================== ключ ==================

        static string _key;
        static bool _keyRead;

        public static bool HasKey => !string.IsNullOrEmpty(Key);

        /// <summary>Ключ Claude API (расшифровывается один раз и держится в памяти).</summary>
        public static string Key
        {
            get
            {
                if (_keyRead) return _key;
                _keyRead = true;
                try { if (File.Exists(KeyFile)) _key = Encoding.UTF8.GetString(Dpapi.Unprotect(File.ReadAllBytes(KeyFile))); }
                catch (Exception e) { Settings.Log(e); _key = null; }
                return _key;
            }
        }

        public static void SaveKey(string key)
        {
            key = key?.Trim();
            Directory.CreateDirectory(Dir);
            File.WriteAllBytes(KeyFile, Dpapi.Protect(Encoding.UTF8.GetBytes(key ?? "")));
            _key = key;
            _keyRead = true;
        }

        public static void DeleteKey()
        {
            try { if (File.Exists(KeyFile)) File.Delete(KeyFile); } catch (Exception e) { Settings.Log(e); }
            _key = null;
            _keyRead = true;
        }

        /// <summary>Ключ для показа: начало и последние 4 символа.</summary>
        public static string MaskedKey
        {
            get
            {
                var k = Key;
                if (string.IsNullOrEmpty(k)) return "";
                return k.Length <= 14 ? "••••" : k.Substring(0, 10) + "…" + k.Substring(k.Length - 4);
            }
        }

        // ================== агенты ==================

        static List<MuzuleAgent> _agents;

        public static MuzuleAgent Mizu() => new MuzuleAgent
        {
            Id = MizuId, Name = "Mizu", Glyph = "", Color = "purple", Control = true, Web = false, Instructions = ""
        };

        public static List<MuzuleAgent> Agents
        {
            get
            {
                if (_agents != null) return _agents;
                try { if (File.Exists(AgentsFile)) _agents = JsonSerializer.Deserialize<List<MuzuleAgent>>(File.ReadAllText(AgentsFile)); }
                catch (Exception e) { Settings.Log(e); }
                _agents ??= new List<MuzuleAgent>();
                _agents.RemoveAll(a => a == null || string.IsNullOrEmpty(a.Id));
                // Встроенный Mizu всегда есть и всегда первый
                var mizu = _agents.FirstOrDefault(a => a.Id == MizuId);
                if (mizu == null) mizu = Mizu(); else _agents.Remove(mizu);
                mizu.Name = "Mizu";
                _agents.Insert(0, mizu);
                foreach (var a in _agents)
                {
                    if (string.IsNullOrWhiteSpace(a.Name)) a.Name = "Agent";
                    if (a.Model != null) a.Model = ValidModel(a.Model);
                    if (!Glyphs.Contains(a.Glyph)) a.Glyph = "";
                    if (!Colors.Contains(a.Color)) a.Color = "purple";
                    a.Instructions ??= "";
                }
                return _agents;
            }
        }

        public static MuzuleAgent Agent(string id) => Agents.FirstOrDefault(a => a.Id == id) ?? Agents[0];

        public static void SaveAgent(MuzuleAgent a)
        {
            if (string.IsNullOrEmpty(a.Id)) a.Id = Guid.NewGuid().ToString("N").Substring(0, 12);
            int i = Agents.FindIndex(x => x.Id == a.Id);
            if (i >= 0) Agents[i] = a; else Agents.Add(a);
            WriteAgents();
        }

        public static void DeleteAgent(string id)
        {
            if (id == MizuId) return;
            Agents.RemoveAll(a => a.Id == id);
            WriteAgents();
        }

        static void WriteAgents()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(AgentsFile, JsonSerializer.Serialize(_agents, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception e) { Settings.Log(e); }
            AgentsChanged?.Invoke();
        }

        // ================== чаты ==================

        static List<MuzuleChatInfo> _index;

        public static List<MuzuleChatInfo> Chats
        {
            get
            {
                if (_index != null) return _index;
                try { if (File.Exists(IndexFile)) _index = JsonSerializer.Deserialize<List<MuzuleChatInfo>>(File.ReadAllText(IndexFile)); }
                catch (Exception e) { Settings.Log(e); }
                _index ??= new List<MuzuleChatInfo>();
                _index.RemoveAll(c => c == null || string.IsNullOrEmpty(c.Id) || !File.Exists(ChatFile(c.Id)));
                _index.Sort((x, y) => y.Updated.CompareTo(x.Updated));
                return _index;
            }
        }

        static string ChatFile(string id)
        {
            // id создаём сами (буквы и цифры), но на всякий случай не даём выйти из папки
            foreach (char c in Path.GetInvalidFileNameChars()) id = id.Replace(c, '_');
            return Path.Combine(ChatsDir, id + ".json");
        }

        // Открытые сейчас чаты: вкладка и быстрое окно должны работать с одним и тем же объектом, а не с двумя копиями
        static readonly List<MuzuleChat> _open = new List<MuzuleChat>();

        static MuzuleChat Remember(MuzuleChat c)
        {
            _open.Remove(c);
            _open.Add(c);
            while (_open.Count > 12) _open.RemoveAt(0);
            return c;
        }

        public static MuzuleChat NewChat(string agentId, bool spotlight = false) => Remember(new MuzuleChat
        {
            Id = DateTime.UtcNow.ToString("yyyyMMddHHmmss") + Guid.NewGuid().ToString("N").Substring(0, 6),
            AgentId = agentId ?? MizuId,
            Updated = DateTime.Now,
            Spotlight = spotlight
        });

        public static MuzuleChat LoadChat(string id)
        {
            var live = Muzule.RunOf(id)?.Chat ?? _open.FirstOrDefault(x => x.Id == id);
            if (live != null) return Remember(live);
            try
            {
                string f = ChatFile(id);
                if (!File.Exists(f)) return null;
                var c = JsonSerializer.Deserialize<MuzuleChat>(File.ReadAllText(f));
                if (c == null) return null;
                c.Messages ??= new JsonArray();
                return Remember(c);
            }
            catch (Exception e) { Settings.Log(e); return null; }
        }

        /// <summary>Сохраняет чат (пустые не сохраняем) и обновляет список. Старые чаты сверх 80 удаляются.</summary>
        public static void SaveChat(MuzuleChat c)
        {
            if (c == null || c.Messages.Count == 0) return;
            try
            {
                Directory.CreateDirectory(ChatsDir);
                c.Updated = DateTime.Now;
                File.WriteAllText(ChatFile(c.Id), JsonSerializer.Serialize(c, Json));
                var info = Chats.FirstOrDefault(x => x.Id == c.Id);
                if (info == null) { info = new MuzuleChatInfo { Id = c.Id }; Chats.Insert(0, info); }
                info.AgentId = c.AgentId;
                info.Title = c.Title;
                info.Updated = c.Updated;
                info.Spotlight = c.Spotlight;
                Chats.Sort((x, y) => y.Updated.CompareTo(x.Updated));
                while (Chats.Count > MaxChats)
                {
                    var old = Chats[Chats.Count - 1];
                    Chats.RemoveAt(Chats.Count - 1);
                    try { File.Delete(ChatFile(old.Id)); } catch { }
                }
                WriteIndex();
            }
            catch (Exception e) { Settings.Log(e); }
            ChatsChanged?.Invoke();
        }

        public static void DeleteChat(string id)
        {
            _open.RemoveAll(x => x.Id == id);
            try { File.Delete(ChatFile(id)); } catch { }
            Chats.RemoveAll(x => x.Id == id);
            WriteIndex();
            ChatsChanged?.Invoke();
        }

        public static void DeleteAllChats()
        {
            _open.Clear();
            foreach (var c in Chats.ToList()) { try { File.Delete(ChatFile(c.Id)); } catch { } }
            Chats.Clear();
            WriteIndex();
            ChatsChanged?.Invoke();
        }

        static void WriteIndex()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(IndexFile, JsonSerializer.Serialize(_index, Json));
            }
            catch (Exception e) { Settings.Log(e); }
        }

        /// <summary>Заголовок чата из первого сообщения.</summary>
        public static string TitleFrom(string text)
        {
            text = (text ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
            while (text.Contains("  ")) text = text.Replace("  ", " ");
            return text.Length <= 48 ? text : text.Substring(0, 46).TrimEnd() + "…";
        }
    }
}
