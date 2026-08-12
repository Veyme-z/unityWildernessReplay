using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>UnitView 的 Sprite 扫描与颜色工具（静态）</summary>
public static class UnitViewSprite
{
    static readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>();
    static readonly string[] SEMANTIC_NAMES =
    { "", "", "", "tower", "base", "wall", "work", "pioneer", "", "", "",
      "beat", "beat", "beat", "beat" };

    static Dictionary<string, Sprite> _allSprites;
    static bool _scanned;

    static string Norm(string n)
    {
        if (string.IsNullOrEmpty(n)) return "";
        string r = n.ToLowerInvariant();
        if (r.EndsWith(".png")) r = r.Substring(0, r.Length - 4);
        if (r.EndsWith(".jpg")) r = r.Substring(0, r.Length - 4);
        if (r.EndsWith(".jpeg")) r = r.Substring(0, r.Length - 5);
        return r;
    }

    public static Sprite FindSprite(params string[] names)
    {
        ScanAllSprites();
        foreach (var n in names)
        {
            Sprite sp;
            if (_allSprites.TryGetValue(Norm(n), out sp) && sp != null) return sp;
        }
        return null;
    }

    static void ScanAllSprites()
    {
        if (_scanned) return;
        _scanned = true;
        _allSprites = new Dictionary<string, Sprite>();
        var texs = new List<Texture2D>();
        var sps = Resources.LoadAll<Sprite>("");
        foreach (var sp in sps)
            if (sp != null && sp.texture != null && !texs.Contains(sp.texture))
                texs.Add(sp.texture);
        var rawTexs = Resources.LoadAll<Texture2D>("");
        foreach (var t in rawTexs)
            if (t != null && !texs.Contains(t))
                texs.Add(t);
        foreach (var t in texs)
        {
            string key = Norm(t.name);
            if (key.Length == 0 || _allSprites.ContainsKey(key)) continue;
            var hi = MatLib.CreateHiResCopy(t);
            float ppu = GetAutoPPU(t.name, hi.width);
            _allSprites[key] = Sprite.Create(hi, new Rect(0, 0, hi.width, hi.height),
                                             new Vector2(0.5f, 0.5f), ppu);
        }
        var sb = new StringBuilder("找到的 Resources 图片: ");
        foreach (var k in _allSprites.Keys) sb.Append(k).Append(" ");
        Debug.Log(sb.ToString());
    }

    static float GetAutoPPU(string texName, int pixelWidth)
    {
        if (string.IsNullOrEmpty(texName) || pixelWidth <= 0) return 100f;
        string lower = texName.ToLowerInvariant();
        if (lower.Contains("background")) return pixelWidth / 41f;
        if (lower.Contains("officer")) return pixelWidth / 4f;
        if (lower.Contains("base")) return pixelWidth / 3f;
        if (lower.Contains("vendor") || lower.Contains("weaponshop") || lower.Contains("shop"))
            return pixelWidth / 3f;
        if (lower.Contains("work") || lower.Contains("pioneer"))
            return pixelWidth / 2f;
        return pixelWidth / 1.5f;
    }

    public static bool TryGetSprite(int type, string teamType)
    {
        Sprite s;
        string cacheKey = type + "|" + (teamType ?? "");
        if (_spriteCache.TryGetValue(cacheKey, out s)) return s != null;
        ScanAllSprites();
        if (_allSprites == null) return false;
        Sprite found = null;
        if (teamType == "defender" && (type == 4 || type == 3))
        {
            string blueName = (type == 4) ? "base_blue" : "tower_blue";
            _allSprites.TryGetValue(blueName, out found);
        }
        if (found == null)
        {
            string sem = (type >= 0 && type < SEMANTIC_NAMES.Length) ? SEMANTIC_NAMES[type] : "";
            if (!string.IsNullOrEmpty(sem)) _allSprites.TryGetValue(sem, out found);
        }
        if (found == null) _allSprites.TryGetValue(type.ToString(), out found);
        _spriteCache[cacheKey] = found;
        if (found == null)
            Debug.LogWarning("[UnitView] 类型 " + type + (teamType == "defender" ? "(defender)" : "")
                + " 没找到对应图片，用方块占位。");
        return found != null;
    }

    public static Color UnitColor(UnitState u)
    {
        switch (u.type)
        {
            case 11: return new Color(0.48f, 0.29f, 0.17f);
            case 12: return new Color(0.42f, 0.31f, 0.63f);
            case 13: return new Color(0.24f, 0.24f, 0.30f);
            case 14: return new Color(0.55f, 0.12f, 0.12f);
            default:
                return u.teamType == "defender" ? new Color(0.88f, 0.27f, 0.20f)
                     : u.teamType == "challenger" ? new Color(0.27f, 0.48f, 0.92f)
                     : new Color(0.6f, 0.6f, 0.6f);
        }
    }

    public static Color Lighten(Color c, float amt)
    {
        return new Color(Mathf.Min(1, c.r + amt), Mathf.Min(1, c.g + amt), Mathf.Min(1, c.b + amt), c.a);
    }
}
