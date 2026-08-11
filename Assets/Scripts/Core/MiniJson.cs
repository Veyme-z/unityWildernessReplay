using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// 极简 JSON 解析器（零依赖，不依赖 Newtonsoft.Json）
/// 解析结果: object = Dictionary&lt;string,object&gt; / List&lt;object&gt; / string / long / double / bool / null
/// </summary>
public static class MiniJson
{
    public static object Parse(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var p = new P(text);
        object v = p.ParseValue();
        return v;
    }

    // ---------- 类型安全取值助手 ----------
    public static Dictionary<string, object> Dict(object o) { return o as Dictionary<string, object>; }
    public static List<object> List(object o) { return o as List<object>; }

    public static string Str(Dictionary<string, object> d, string k)
    {
        if (d == null || !d.TryGetValue(k, out var v) || v == null) return null;
        return v.ToString();
    }
    public static long Lng(Dictionary<string, object> d, string k, long def = 0)
    {
        if (d == null || !d.TryGetValue(k, out var v) || v == null) return def;
        if (v is long l) return l;
        if (v is double dd) return (long)dd;
        long r; return long.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out r) ? r : def;
    }
    public static int Int(Dictionary<string, object> d, string k, int def = 0)
    {
        return (int)Lng(d, k, def);
    }
    public static bool Bool(Dictionary<string, object> d, string k, bool def = false)
    {
        if (d == null || !d.TryGetValue(k, out var v) || v == null) return def;
        if (v is bool b) return b;
        bool r; return bool.TryParse(v.ToString(), out r) ? r : def;
    }
    public static List<object> Arr(Dictionary<string, object> d, string k)
    {
        if (d == null || !d.TryGetValue(k, out var v) || v == null) return null;
        return v as List<object>;
    }
    public static Dictionary<string, object> Obj(Dictionary<string, object> d, string k)
    {
        if (d == null || !d.TryGetValue(k, out var v) || v == null) return null;
        return v as Dictionary<string, object>;
    }

    // ---------- 内部解析器 ----------
    class P
    {
        readonly string s;
        int i;
        public P(string t) { s = t; i = 0; }

        void SkipWs()
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }

        public object ParseValue()
        {
            SkipWs();
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{') return ParseObj();
            if (c == '[') return ParseArr();
            if (c == '"') return ParseStr();
            if (c == 't') { i += 4; return true; }
            if (c == 'f') { i += 5; return false; }
            if (c == 'n') { i += 4; return null; }
            return ParseNum();
        }

        Dictionary<string, object> ParseObj()
        {
            var d = new Dictionary<string, object>();
            i++; // {
            SkipWs();
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                SkipWs();
                string k = ParseStr();
                SkipWs();
                if (i < s.Length && s[i] == ':') i++;
                object v = ParseValue();
                if (k != null) d[k] = v;
                SkipWs();
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                break;
            }
            return d;
        }

        List<object> ParseArr()
        {
            var l = new List<object>();
            i++; // [
            SkipWs();
            if (i < s.Length && s[i] == ']') { i++; return l; }
            while (true)
            {
                object v = ParseValue();
                l.Add(v);
                SkipWs();
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
                break;
            }
            return l;
        }

        string ParseStr()
        {
            i++; // "
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\' && i < s.Length)
                {
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case '"': sb.Append('"'); break;
                        case 'u':
                            if (i + 4 <= s.Length)
                            {
                                int code;
                                if (int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                    sb.Append((char)code);
                                i += 4;
                            }
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        object ParseNum()
        {
            int st = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
            string t = s.Substring(st, i - st);
            if (t.Length == 0) return 0L;
            if (t.IndexOf('.') >= 0 || t.IndexOf('e') >= 0 || t.IndexOf('E') >= 0)
            {
                double d; return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 0.0;
            }
            long l; return long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out l) ? l : 0L;
        }
    }
}
