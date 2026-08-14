using UnityEngine;

/// <summary>
/// 头顶交易徽标："贩卖了 xx xN" / "购买 xxx" + 深色底板 + Billboard + 弹出/上浮/淡出。
/// 复用项目已有的 Billboard 和 Time.deltaTime 暂停兼容。
/// </summary>
public class TradeBadge : MonoBehaviour
{
    static Transform FindVendor()
    {
        var go = GameObject.Find("NPC_9_20_15");
        return go != null ? go.transform : null;
    }

    static string CnName(string en)
    {
        switch (en.ToLowerInvariant())
        {
            case "copper": return "铜";
            case "iron":   return "铁";
            case "stone":  return "石";
            case "medicine": return "药品";
            case "bomb": return "炸弹";
            case "dizzyweapon": return "眩晕武器";
            case "wallfixer": return "围墙修复器";
            case "smallbeastsummonorder": return "小型野兽召唤令";
            case "middlebeastsummonorder": return "中型野兽召唤令";
            case "largebeastsummonorder": return "大型野兽召唤令";
            case "bossbeastsummonorder": return "首领野兽召唤令";
            case "upgradestationmaxhp": return "基地耐久强化";
            case "upgradewallmaxhp": return "围墙耐久强化";
            case "upgradetowermaxhp": return "防御塔耐久强化";
            case "upgradetowerattack": return "防御塔攻击强化";
            default:       return en;
        }
    }

    public static TradeBadge Show(Transform parent, string itemName, int qty,
        float yPos = 1.5f, float bgScale = 1f)
    {
        var existing = parent.GetComponentInChildren<TradeBadge>();
        if (existing != null)
        {
            existing.SetText(itemName, qty, bgScale);
            existing.Refresh();
            return existing;
        }

        var go = new GameObject("TradeBadge");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0, yPos, 0);

        var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bg.name = "Bg";
        bg.transform.SetParent(go.transform, false);
        var bgRend = bg.GetComponent<MeshRenderer>();
        bgRend.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        bgRend.sharedMaterial.color = new Color(0f, 0f, 0f, 0.5f);
        bgRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        bgRend.receiveShadows = false;
        var bgCol = bg.GetComponent<Collider>();
        if (bgCol != null) Destroy(bgCol);

        var txtGo = new GameObject("Txt");
        txtGo.transform.SetParent(go.transform, false);
        txtGo.transform.localPosition = new Vector3(0, 0, -0.01f);
        var tm = txtGo.AddComponent<TextMesh>();
        tm.font = FxFactory.BuiltinFont();
        tm.fontSize = Mathf.RoundToInt(60 * bgScale);
        tm.characterSize = 0.04f * bgScale;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = new Color(1f, 0.84f, 0.25f);

        go.AddComponent<Billboard>();

        var badge = go.AddComponent<TradeBadge>();
        badge._tm = tm;
        badge._bgRend = bgRend;
        badge._bgTransform = bg.transform;
        badge._bgScale = bgScale;
        badge.SetText(itemName, qty, bgScale);
        return badge;
    }

    TextMesh _tm;
    MeshRenderer _bgRend;
    Transform _bgTransform;
    float _bgScale = 1f;
    float _age;
    float _duration = 1.8f;
    float _fadeStart = 1.2f;
    Vector3 _basePos;

    void Start()
    {
        _basePos = transform.localPosition;
        transform.localScale = Vector3.one * 0.3f;
    }

    void SetText(string itemName, int qty, float bgScale = 1f)
    {
        if (_tm == null) return;
        if (string.IsNullOrEmpty(itemName) || qty <= 0)
        {
            _tm.text = "购买";
        }
        else if (itemName == "copper" || itemName == "iron" || itemName == "stone")
        {
            _tm.text = "贩卖了 " + CnName(itemName) + " x" + qty;
        }
        else
        {
            _tm.text = "购买 " + CnName(itemName);
        }

        Debug.Log("[TradeBadge] SetText: " + _tm.text);

        // WebGL: legacy TextMesh 赋值 Dynamic 字体后，材质贴图可能丢失导致隐形，
        // 显式请求字形并同步 MeshRenderer 的材质
        if (_tm.font != null)
        {
            _tm.font.RequestCharactersInTexture(_tm.text, _tm.fontSize, _tm.fontStyle);
            var mr = _tm.GetComponent<MeshRenderer>();
            if (mr != null)
                mr.sharedMaterial = _tm.font.material;
        }

        if (_bgTransform != null)
        {
            // 区分全宽/半宽：中文是全宽，空格/ASCII/数字是半宽（约一半宽）。
            // 之前用 len*charWidth 把半宽也当全宽，贩卖文字里 4 个半宽被多算导致偏长。
            float full = 0f, half = 0f;
            if (!string.IsNullOrEmpty(_tm.text))
            {
                foreach (char c in _tm.text)
                {
                    if (c > 0x7F) full++;   // 非 ASCII（中文等）= 全宽
                    else half++;            // ASCII（空格/字母/数字）= 半宽
                }
            }
            float charWidth = 0.35f;   // 全宽字符的世界宽度，可按实际字号微调
            float padding = 0.3f;      // 左右总留白
            float minWidth = 0.6f;
            float w = Mathf.Max(minWidth, full * charWidth + half * charWidth * 0.5f + padding) * bgScale;
            float h = 0.3f * bgScale;
            _bgTransform.localScale = new Vector3(w, h, 1f);
        }
    }

    public void Refresh()
    {
        _age = 0;
        transform.localScale = Vector3.one * 0.3f;
    }

    void Update()
    {
        _age += Time.deltaTime;
        float k = Mathf.Clamp01(_age / _duration);

        float pop = Mathf.Clamp01(_age / 0.15f);
        float s = Mathf.Lerp(0.3f, 1f, pop);
        transform.localScale = Vector3.one * s;

        transform.localPosition = _basePos + Vector3.up * (0.4f * k);

        float fadeK = (_age - _fadeStart) / (_duration - _fadeStart);
        float alpha = 1f - Mathf.Clamp01(fadeK);
        if (_tm != null) _tm.color = new Color(1f, 0.84f, 0.25f, alpha);
        if (_bgRend != null)
        {
            var c = _bgRend.sharedMaterial.color;
            _bgRend.sharedMaterial.color = new Color(c.r, c.g, c.b, 0.5f * alpha);
        }

        if (k >= 1f) Destroy(gameObject);
    }

    /// <summary>Seek / Reload 时清理所有旧徽标（Vendor + Shop）。</summary>
    public static void Cleanup()
    {
        foreach (var name in new[] { "NPC_9_20_15", "NPC_10_25_11" })
        {
            var go = GameObject.Find(name);
            if (go == null) continue;
            foreach (var badge in go.GetComponentsInChildren<TradeBadge>())
                Destroy(badge.gameObject);
        }
    }
}
