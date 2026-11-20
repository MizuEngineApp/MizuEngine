// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Globalization;

namespace MizuEngine
{
    /// <summary>
    /// Калькулятор для Spotlight: + − × ÷ ^ %, скобки, запятая или точка, sqrt, sin, cos, tan, log, ln, abs, pi, e.
    /// «2+2*2», «(15,5-3)/4», «2^10», «sqrt(144)», «20% от 350» → «20%*350».
    /// </summary>
    internal static class Calc
    {
        /// <summary>Посчитать, если это похоже на пример. null — это не пример (или ошибка в записи).</summary>
        public static double? TryEval(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string s = text.Trim().TrimEnd('=').Trim().ToLowerInvariant()
                .Replace('×', '*').Replace('·', '*').Replace('÷', '/').Replace('−', '-').Replace(':', '/')
                .Replace(" от ", "*").Replace(" of ", "*").Replace("х", "*");
            // Должен быть хотя бы один знак действия или функция — просто число не считаем
            bool op = false;
            for (int i = 1; i < s.Length; i++) if ("+-*/^%".IndexOf(s[i]) >= 0) { op = true; break; }
            if (!op && !(s.Contains("sqrt") || s.Contains("sin") || s.Contains("cos") || s.Contains("tan") || s.Contains("log") || s.Contains("ln(") || s.Contains("abs") || s.Contains("pi") || s.Contains("√"))) return null;
            try
            {
                var p = new Parser(s);
                double v = p.Expr();
                p.Skip();
                if (p.Pos != s.Length || double.IsNaN(v) || double.IsInfinity(v)) return null;
                return v;
            }
            catch { return null; }
        }

        public static string Format(double v)
        {
            if (Math.Abs(v) >= 1e15 || (Math.Abs(v) < 1e-6 && v != 0)) return v.ToString("G10", CultureInfo.CurrentCulture);
            return Math.Round(v, 10).ToString("#,0.##########", CultureInfo.CurrentCulture);
        }

        sealed class Parser
        {
            readonly string _s;
            public int Pos;
            public Parser(string s) { _s = s; }

            public void Skip() { while (Pos < _s.Length && char.IsWhiteSpace(_s[Pos])) Pos++; }
            bool Eat(char c) { Skip(); if (Pos < _s.Length && _s[Pos] == c) { Pos++; return true; } return false; }

            public double Expr()
            {
                double v = Term();
                while (true)
                {
                    if (Eat('+')) v += Term();
                    else if (Eat('-')) v -= Term();
                    else return v;
                }
            }

            double Term()
            {
                double v = Power();
                while (true)
                {
                    if (Eat('*')) v *= Power();
                    else if (Eat('/')) { double d = Power(); if (d == 0) throw new DivideByZeroException(); v /= d; }
                    else return v;
                }
            }

            double Power()
            {
                double b = Unary();
                if (Eat('^')) return Math.Pow(b, Power());   // 2^3^2 = 2^(3^2)
                return b;
            }

            double Unary()
            {
                if (Eat('-')) return -Unary();
                if (Eat('+')) return Unary();
                double v = Atom();
                if (Eat('%')) v /= 100;   // 20% → 0,2
                return v;
            }

            double Atom()
            {
                Skip();
                if (Eat('(')) { double v = Expr(); if (!Eat(')')) throw new FormatException(); return v; }
                if (Eat('√')) return Math.Sqrt(Unary());
                int start = Pos;
                if (Pos < _s.Length && char.IsLetter(_s[Pos]))
                {
                    while (Pos < _s.Length && char.IsLetter(_s[Pos])) Pos++;
                    string name = _s.Substring(start, Pos - start);
                    if (name == "pi" || name == "π") return Math.PI;
                    if (name == "e") return Math.E;
                    double a = Atom();
                    return name switch
                    {
                        "sqrt" => Math.Sqrt(a),
                        "sin" => Math.Sin(a * Math.PI / 180),   // градусы — так привычнее
                        "cos" => Math.Cos(a * Math.PI / 180),
                        "tan" or "tg" => Math.Tan(a * Math.PI / 180),
                        "log" or "lg" => Math.Log10(a),
                        "ln" => Math.Log(a),
                        "abs" => Math.Abs(a),
                        _ => throw new FormatException()
                    };
                }
                if (Pos < _s.Length && _s[Pos] == 'π') { Pos++; return Math.PI; }
                // Число: 1 234,5 / 1234.5 / 1,5
                while (Pos < _s.Length && (char.IsDigit(_s[Pos]) || _s[Pos] == '.' || _s[Pos] == ',' || (_s[Pos] == ' ' && Pos + 1 < _s.Length && char.IsDigit(_s[Pos + 1]) && Pos > start))) Pos++;
                string num = _s.Substring(start, Pos - start).Replace(" ", "").Replace(',', '.');
                if (num.Length == 0 || num.IndexOf('.') != num.LastIndexOf('.')) throw new FormatException();
                return double.Parse(num, CultureInfo.InvariantCulture);
            }
        }
    }
}
