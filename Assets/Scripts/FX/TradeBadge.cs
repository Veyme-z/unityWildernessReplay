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
            _tm.text = "购买 " + itemName;
        }

        if (_bgTransform != null)
        {
            float w = Mathf.Max(0.6f, _tm.text.Length * 0.15f + 0.8f) * bgScale;
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
