using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 材质池 + 程序化贴图生成（零外部资源）。
/// 所有材质统一用 Sprites/Default（内置兼容 shader，URP/内置管线都能跑，支持透明）。
/// </summary>
public static class MatLib
{
    static Shader _shader;
    static readonly Dictionary<Color, Material> _pool = new Dictionary<Color, Material>();

    public static Texture2D whiteTex;
    public static Texture2D ringTex;   // 白色圆环（出生/选择圈）
    public static Texture2D dotTex;    // 白色圆点
    public static Texture2D panelTex;  // 圆角矩形（UI面板背景）

    public static Shader Shader2D
    {
        get
        {
            if (_shader == null)
            {
                _shader = Shader.Find("Sprites/Default");
                if (_shader == null) _shader = Shader.Find("Unlit/Texture");
            }
            return _shader;
        }
    }

    public static Material Get(Color c)
    {
        var key = new Color(Mathf.Round(c.r * 255f) / 255f, Mathf.Round(c.g * 255f) / 255f,
                            Mathf.Round(c.b * 255f) / 255f, Mathf.Round(c.a * 255f) / 255f);
        Material m;
        if (_pool.TryGetValue(key, out m)) return m;
        m = new Material(Shader2D) { color = key };
        _pool[key] = m;
        return m;
    }

    static readonly Dictionary<string, Texture2D> _texCache = new Dictionary<string, Texture2D>();

    /// <summary>从 Resources 加载贴图（不存在返回 null，失败会缓存避免重复查找）</summary>
    public static Texture2D TryLoadTexture(string path)
    {
        Texture2D t;
        if (_texCache.TryGetValue(path, out t)) return t;
        t = Resources.Load<Texture2D>(path);
        _texCache[path] = t;
        return t;
    }

    /// <summary>带贴图的材质（用于背景大地图）</summary>
    public static Material Get(Texture2D tex)
    {
        if (tex == null) return null;
        var m = new Material(Shader2D)
        {
            mainTexture = tex,
            color = Color.white
        };
        return m;
    }

    /// <summary>
    /// 运行时高清副本：去掉 mipmap（避免远景模糊），强制 Bilinear + Clamp。
    /// 不修改原资产，内存换取清晰度，对回放这种俯视场景最合适。
    /// </summary>
    public static Texture2D CreateHiResCopy(Texture2D src)
    {
        if (src == null) return null;
        try
        {
            var copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            copy.SetPixels32(src.GetPixels32());
            copy.Apply(false, true);
            copy.filterMode = FilterMode.Point;
            copy.wrapMode = TextureWrapMode.Clamp;
            copy.anisoLevel = 1;
            return copy;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[MatLib] 高清副本失败，用原图: " + e.Message);
            return src;
        }
    }

    public static void Init()
    {
        if (whiteTex != null) return;
        whiteTex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++) whiteTex.SetPixel(x, y, Color.white);
        whiteTex.Apply();
        whiteTex.wrapMode = TextureWrapMode.Clamp;

        ringTex = CreateRingTex(Color.white, 64);

        dotTex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float dx = x - 31.5f, dy = y - 31.5f;
                float a = (dx * dx + dy * dy <= 28f * 28f) ? 1f : 0f;
                dotTex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        dotTex.Apply();
        dotTex.wrapMode = TextureWrapMode.Clamp;

        // 圆角面板背景（128x128, 圆角半径 18px）
        panelTex = new Texture2D(128, 128, TextureFormat.RGBA32, false);
        float pr = 18f;
        for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++)
            {
                float a = 1f;
                if      (x < pr && y < pr)          { float d = Mathf.Sqrt((pr-x)*(pr-x) + (pr-y)*(pr-y)); a = d <= pr ? 1f : 0f; }
                else if (x >= 128-pr && y < pr)     { float d = Mathf.Sqrt((x-(127-pr))*(x-(127-pr)) + (pr-y)*(pr-y)); a = d <= pr ? 1f : 0f; }
                else if (x < pr && y >= 128-pr)     { float d = Mathf.Sqrt((pr-x)*(pr-x) + (y-(127-pr))*(y-(127-pr))); a = d <= pr ? 1f : 0f; }
                else if (x >= 128-pr && y >= 128-pr) { float d = Mathf.Sqrt((x-(127-pr))*(x-(127-pr)) + (y-(127-pr))*(y-(127-pr))); a = d <= pr ? 1f : 0f; }
                panelTex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        panelTex.Apply();
        panelTex.wrapMode = TextureWrapMode.Clamp;
    }

    /// <summary>HLSL 兼容的 smoothstep：返回 0~1 阶跃值（不同于 Mathf.SmoothStep 的插值语义）。</summary>
    static float Smooth01(float edge0, float edge1, float value)
    {
        float t = Mathf.Clamp01((value - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    /// <summary>创建带颜色烘焙的抗锯齿圆环贴图。颜色直接写入像素，不依赖 shader _Color 乘法。</summary>
    public static Texture2D CreateRingTex(Color color, int size = 128)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        float center = (size - 1) * 0.5f;
        float innerRadius = size * 0.34f;
        float outerRadius = size * 0.47f;
        float feather = 1.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);

                float innerMask = Smooth01(innerRadius - feather, innerRadius + feather, distance);
                float outerMask = 1f - Smooth01(outerRadius - feather, outerRadius + feather, distance);
                float alpha = innerMask * outerMask * color.a;

                tex.SetPixel(x, y, new Color(color.r, color.g, color.b, alpha));
            }
        }
        tex.Apply(false, false);
        return tex;
    }
}
