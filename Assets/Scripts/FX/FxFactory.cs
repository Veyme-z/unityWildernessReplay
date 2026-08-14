using UnityEngine;

/// <summary>世界空间特效：伤害数字 / 攻击弹道 / 出生光环 / 说话气泡</summary>
public static class FxFactory
{
    public static Font BuiltinFont()
    {
        return UiFonts.Get();
    }

    /// <summary>伤害数字：上浮 + 淡出</summary>
    public static void DamageText(Vector3 pos, int dmg, Color color)
    {
        var go = new GameObject("Dmg");
        go.transform.position = pos + Vector3.up * 1.2f;
        var tm = go.AddComponent<TextMesh>();
        tm.text = "-" + dmg;
        tm.font = BuiltinFont();
        tm.fontSize = 180;
        tm.characterSize = 0.12f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = color;
        go.AddComponent<Billboard>();  // 面朝相机，俯视可见
        var f = go.AddComponent<FloatFade>();
        f.duration = 1.2f;
        f.rise = 1.0f;
        f.color = color;
    }

    /// <summary>攻击弹道：两点间光柱</summary>
    public static void Beam(Vector3 from, Vector3 to, Color color)
    {
        var go = new GameObject("Beam");
        go.transform.position = Vector3.zero;
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        // 弹道高度提到单位中部（俯视也能看到明显的连线）
        float beamY = 1.0f;
        lr.SetPosition(0, from + Vector3.up * beamY);
        lr.SetPosition(1, to + Vector3.up * beamY);
        lr.startWidth = 0.12f;
        lr.endWidth = 0.08f;
        lr.sharedMaterial = MatLib.Get(color);
        lr.startColor = color;
        lr.endColor = new Color(color.r, color.g, color.b, 0.3f);
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        var f = go.AddComponent<FadeLine>();
        f.duration = 0.7f;
    }

    /// <summary>出生/建造光环</summary>
    public static void Ring(Vector3 pos, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "Ring";
        go.transform.position = pos + new Vector3(0, 0.08f, 0);
        go.transform.localRotation = Quaternion.Euler(90, 0, 0);
        go.transform.localScale = new Vector3(0.3f, 0.3f, 1);
        var rend = go.GetComponent<MeshRenderer>();
        rend.sharedMaterial = MatLib.Get(color);
        rend.sharedMaterial.mainTexture = MatLib.ringTex;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var col = go.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);
        var f = go.AddComponent<RingFx>();
        f.duration = 0.7f;
    }

    /// <summary>说话气泡：纯文字（无背景）</summary>
    public static void Bubble(Vector3 pos, string text)
    {
        var go = new GameObject("Bubble");
        go.transform.position = pos + Vector3.up * 2.3f;

        var txt = new GameObject("Txt");
        txt.transform.SetParent(go.transform, false);
        var tm = txt.AddComponent<TextMesh>();
        tm.text = text;
        tm.font = BuiltinFont();
        tm.fontSize = 110;
        tm.characterSize = 0.075f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.white;
        txt.AddComponent<Billboard>();

        var f = go.AddComponent<FadeScale>();
        f.duration = 3f;
        f.scaleFrom = 0.8f;
    }
}

/// <summary>上浮 + 淡出（伤害数字）</summary>
public class FloatFade : MonoBehaviour
{
    public float duration = 0.9f;
    public float rise = 0.6f;
    public Color color = Color.red;
    float _t;
    Vector3 _start;
    TextMesh _tm;

    void Start()
    {
        _start = transform.position;
        _tm = GetComponent<TextMesh>();
    }
    void Update()
    {
        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t / duration);
        transform.position = _start + Vector3.up * (rise * k);
        if (_tm != null) _tm.color = new Color(color.r, color.g, color.b, 1f - k);
        if (k >= 1f) Destroy(gameObject);
    }
}

/// <summary>弹道淡出</summary>
public class FadeLine : MonoBehaviour
{
    public float duration = 0.4f;
    float _t;
    LineRenderer _lr;
    void Start() { _lr = GetComponent<LineRenderer>(); }
    void Update()
    {
        _t += Time.deltaTime;
        float k = 1f - Mathf.Clamp01(_t / duration);
        if (_lr != null) { _lr.startColor = new Color(1, 1, 1, k); _lr.endColor = new Color(1, 1, 1, k); }
        if (k <= 0f) Destroy(gameObject);
    }
}

/// <summary>光环扩散 + 淡出（propertyblock，不污染共享材质）</summary>
public class RingFx : MonoBehaviour
{
    public float duration = 0.7f;
    float _t;
    MeshRenderer _rend;
    MaterialPropertyBlock _mpb;
    void Start()
    {
        _rend = GetComponent<MeshRenderer>();
        _mpb = new MaterialPropertyBlock();
    }
    void Update()
    {
        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t / duration);
        float s = Mathf.Lerp(0.3f, 1.3f, k);
        transform.localScale = new Vector3(s, s, 1);
        if (_rend != null)
        {
            _mpb.SetColor("_Color", new Color(1, 1, 1, 1f - k));
            _rend.SetPropertyBlock(_mpb);
        }
        if (k >= 1f) Destroy(gameObject);
    }
}

/// <summary>气泡出现缩放 + 淡出</summary>
public class FadeScale : MonoBehaviour
{
    public float duration = 3f;
    public float scaleFrom = 0.8f;
    float _t;
    Vector3 _base;
    void Start() { _base = transform.localScale; }
    void Update()
    {
        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t / duration);
        float s = Mathf.Lerp(scaleFrom, 1f, Mathf.Clamp01(k * 6f));
        transform.localScale = _base * s;
        if (k >= 1f) Destroy(gameObject);
    }
}
