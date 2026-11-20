// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MizuEngine
{
    /// <summary>
    /// Курсы валют для Spotlight: «100 usd», «100$ в рублях», «50 евро to cny», «¥300».
    /// Курсы — open.er-api.com (бесплатно, без ключа), обновляются раз в 6 часов и хранятся в %AppData%\MizuEngine\rates.json.
    /// </summary>
    internal static class Currency
    {
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        static Dictionary<string, double> _rates;   // сколько единиц валюты за 1 USD
        static DateTime _at;
        static Task _loading;
        static string FilePath => Path.Combine(Settings.Folder, "rates.json");

        // Как люди пишут валюты → код. Сравнивается с началом слова («доллар», «долларов», «доллара»).
        static readonly (string word, string code)[] Words =
        {
            ("$", "USD"), ("usd", "USD"), ("долл", "USD"), ("бакс", "USD"), ("dollar", "USD"), ("美元", "USD"),
            ("€", "EUR"), ("eur", "EUR"), ("евро", "EUR"), ("欧元", "EUR"),
            ("₽", "RUB"), ("rub", "RUB"), ("руб", "RUB"), ("р.", "RUB"), ("рубл", "RUB"), ("卢布", "RUB"),
            ("¥", "CNY"), ("cny", "CNY"), ("rmb", "CNY"), ("юан", "CNY"), ("yuan", "CNY"), ("元", "CNY"), ("人民币", "CNY"),
            ("£", "GBP"), ("gbp", "GBP"), ("фунт", "GBP"), ("pound", "GBP"),
            ("jpy", "JPY"), ("иен", "JPY"), ("йен", "JPY"), ("yen", "JPY"), ("日元", "JPY"),
            ("₩", "KRW"), ("krw", "KRW"), ("вон", "KRW"), ("won", "KRW"),
            ("₴", "UAH"), ("uah", "UAH"), ("грив", "UAH"),
            ("₸", "KZT"), ("kzt", "KZT"), ("тенге", "KZT"), ("tenge", "KZT"),
            ("byn", "BYN"), ("бел", "BYN"),
            ("₺", "TRY"), ("try", "TRY"), ("лир", "TRY"), ("lira", "TRY"),
            ("chf", "CHF"), ("франк", "CHF"), ("franc", "CHF"),
            ("hkd", "HKD"), ("港币", "HKD"), ("sgd", "SGD"), ("cad", "CAD"), ("aud", "AUD"),
            ("inr", "INR"), ("рупи", "INR"), ("₹", "INR"), ("brl", "BRL"), ("реал", "BRL"),
            ("pln", "PLN"), ("злот", "PLN"), ("czk", "CZK"), ("крон", "CZK"), ("sek", "SEK"),
            ("uzs", "UZS"), ("сум", "UZS"), ("amd", "AMD"), ("драм", "AMD"), ("gel", "GEL"), ("лари", "GEL"),
            ("aed", "AED"), ("дирх", "AED"), ("thb", "THB"), ("бат", "THB"), ("vnd", "VND"), ("донг", "VND"),
            ("btc", "BTC")
        };

        public static readonly string[] Defaults = { "RUB", "USD", "CNY", "EUR" };

        static readonly Regex Shape = new Regex(
            @"^\s*(?<pre>[$€₽¥£₩₴₸₺₹])?\s*(?<num>\d[\d\s]*?(?:[.,]\d+)?)\s*(?<k>(?:k|к|тыс\.?|thousand)(?=\s|$|[$€₽¥£]))?\s*(?<from>[^\d\s]+)?(?:\s+(?:(?:в|во|to|in|на|->|=|→)\s+)?(?<to>[^\d\s]+))?\s*$",
            RegexOptions.IgnoreCase);

        /// <summary>Разобрать запрос. null — это не про валюту.</summary>
        public static (double amount, string from, string[] to)? Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 60) return null;
            var m = Shape.Match(text.Trim());
            if (!m.Success) return null;
            string fromWord = m.Groups["pre"].Success ? m.Groups["pre"].Value : m.Groups["from"].Value;
            string toWord = m.Groups["to"].Value;
            // «100 usd rub»: если «from» пустое, а символ был перед числом — второе слово это «куда»
            string from = Code(fromWord);
            if (from == null) return null;
            string to = string.IsNullOrEmpty(toWord) ? null : Code(toWord);
            if (!string.IsNullOrEmpty(toWord) && to == null) return null;
            if (m.Groups["pre"].Success && m.Groups["from"].Success && m.Groups["from"].Value.Length > 0 && to == null)
            {
                to = Code(m.Groups["from"].Value);
                if (to == null) return null;
            }
            if (!double.TryParse(m.Groups["num"].Value.Replace(" ", "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double amount)) return null;
            if (m.Groups["k"].Success && m.Groups["k"].Value.Length > 0) amount *= 1000;
            var targets = to != null ? new[] { to } : Defaults.Where(c => c != from).Take(3).ToArray();
            return (amount, from, targets);
        }

        static string Code(string w)
        {
            w = (w ?? "").Trim().ToLowerInvariant().Trim('.', ',', '?');
            if (w.Length == 0) return null;
            if (w.Length == 3 && Regex.IsMatch(w, "^[a-z]{3}$") && _rates != null && _rates.ContainsKey(w.ToUpperInvariant())) return w.ToUpperInvariant();
            foreach (var (word, code) in Words)
                if (w.StartsWith(word, StringComparison.OrdinalIgnoreCase) || w == word) return code;
            return null;
        }

        /// <summary>Перевести. null — курсов нет (нет интернета и сохранённых).</summary>
        public static async Task<double?> Convert(double amount, string from, string to)
        {
            await Ensure();
            if (_rates == null || !_rates.TryGetValue(from, out double a) || !_rates.TryGetValue(to, out double b) || a <= 0) return null;
            return amount / a * b;
        }

        public static DateTime UpdatedAt => _at;

        static Task Ensure()
        {
            if (_rates != null && (DateTime.UtcNow - _at).TotalHours < 6) return Task.CompletedTask;
            if (_loading != null && !_loading.IsCompleted) return _loading;
            return _loading = Load();
        }

        static async Task Load()
        {
            // Сохранённые курсы — сразу (даже старые лучше, чем ничего)
            if (_rates == null)
            {
                try
                {
                    if (File.Exists(FilePath)) Read(JsonNode.Parse(await File.ReadAllTextAsync(FilePath)));
                }
                catch { }
                if (_rates != null && (DateTime.UtcNow - _at).TotalHours < 6) return;
            }
            try
            {
                string json = await Http.GetStringAsync("https://open.er-api.com/v6/latest/USD");
                var j = JsonNode.Parse(json);
                if ((string)j?["result"] != "success") return;
                Read(j);
                _at = DateTime.UtcNow;
                try { Directory.CreateDirectory(Settings.Folder); await File.WriteAllTextAsync(FilePath, json); } catch { }
            }
            catch (Exception e) { if (_rates == null) Settings.Log(e); }
        }

        static void Read(JsonNode j)
        {
            if (j?["rates"] is not JsonObject r) return;
            var d = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in r) if (kv.Value != null) d[kv.Key] = (double)kv.Value;
            if (d.Count < 10) return;
            _rates = d;
            long unix = (long?)j["time_last_update_unix"] ?? 0;
            _at = unix > 0 ? DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime : DateTime.UtcNow.AddHours(-5);
            if (_at > DateTime.UtcNow) _at = DateTime.UtcNow;
        }

        public static string Format(double v, string code)
        {
            string n = v >= 100 ? v.ToString("#,0.##", CultureInfo.CurrentCulture) : v.ToString("#,0.####", CultureInfo.CurrentCulture);
            return n + " " + code;
        }
    }
}
