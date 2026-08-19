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
        // WebGL: legacy TextMesh 不会主动为动态字体请求 CJK 字形，显式请求
        tm.font.RequestCharactersInTexture(text, tm.fontSize, tm.fontStyle);
        txt.AddComponent<Billboard>();

        var f = go.AddComponent<FadeScale>();
        f.duration = 3f;
        f.scaleFrom = 0.8f;
    }

    // ── Cartoon FX Remaster AoE 特效 ──
    // 统一从 Resources/FX 加载：改 Assets/Resources/FX/ 下的 prefab 即可（编辑器 + 打包均生效）。
    // 未来换特效只需改这两行路径（或直接替换 prefab 文件）。
    const string RES_BOMB = "FX/CFXR Explosion 1";
    const string RES_DIZZY = "FX/Hovl Freeze AOE";           // 眩晕法宝（Hovl Studio 雪/冰冻 AoE）
    const string RES_HEAL = "FX/Hovl Heal";                    // 恢复血量（Hovl Studio 治疗光环）
    const string RES_BUILD = "FX/Hovl Build Dust";             // 修筑建筑（Hovl Studio 尘土）
    const string RES_DEMOLISH = "FX/Hovl Demolish Explosion";  // 拆除建筑（Hovl Studio 爆炸）
    const string RES_RUBBLE = "FX/Hovl Rubble";                // 围墙摧毁（瓦砾炸开）

    // 3×3 覆盖：1 格 = 1 世界单位，3×3 = 3 单位。
    // 炸弹范围缩小为原来的 1/2（1.8 → 0.9）。
    const float BOMB_SCALE = 0.9f;
    const float DIZZY_SCALE = 0.4f;
    const float BOMB_DURATION = 2.5f;   // 粒子播完后自动销毁
    const float HEAL_SCALE = 0.6f;
    const float BUILD_SCALE = 0.3f;
    const float BUILD_ALPHA = 0.5f;   // 尘土透明度系数（乘到粒子 startColor.alpha）
    const float HEAL_DURATION = 2.0f;
    const float BUILD_DURATION = 1.5f;
    const float DEMOLISH_SCALE = 0.3f;
    const float DEMOLISH_DURATION = 1.5f;
    const float RUBBLE_SCALE = 1.2f;
    const float RUBBLE_DURATION = 1.5f;

    /// <summary>炸弹 AoE：实例化 Resources/FX 下的 CFXR 爆炸 prefab，放大覆盖 3×3 并自动回收。</summary>
    public static void PlayBombEffect(Vector3 center)
    {
        SpawnEffect(RES_BOMB, center, BOMB_SCALE, BOMB_DURATION);
    }

    /// <summary>眩晕 AoE：实例化 Resources/FX 下的 CFXR 魔法阵 prefab，放大覆盖 3×3，持续 durationSeconds 后自动回收。</summary>
    public static void PlayDizzyEffect(Vector3 center, float durationSeconds)
    {
        SpawnEffect(RES_DIZZY, center, DIZZY_SCALE, durationSeconds);
    }

    /// <summary>恢复血量：Hovl Studio 治疗光环，在单位位置短暂播放；follow 非空时挂到单位下跟随移动。</summary>
    public static void PlayHealEffect(Vector3 center, Transform follow = null)
    {
        SpawnEffect(RES_HEAL, center, HEAL_SCALE, HEAL_DURATION, 1f, follow);
    }

    /// <summary>修筑建筑：Hovl Studio 尘土，在建造位置短暂播放。</summary>
    public static void PlayBuildEffect(Vector3 center)
    {
        SpawnEffect(RES_BUILD, center, BUILD_SCALE, BUILD_DURATION, BUILD_ALPHA);
    }

    /// <summary>拆除建筑：Hovl Studio 爆炸，在拆除位置短暂播放。</summary>
    public static void PlayDemolishEffect(Vector3 center)
    {
        SpawnEffect(RES_DEMOLISH, center, DEMOLISH_SCALE, DEMOLISH_DURATION);
    }

    /// <summary>围墙摧毁 / 矿石消失：瓦砾炸开，在坐标位置短暂播放。</summary>
    public static void PlayRubbleEffect(Vector3 center, float scale = RUBBLE_SCALE, float duration = RUBBLE_DURATION)
    {
        SpawnEffect(RES_RUBBLE, center, scale, duration);
    }

    /// <summary>统一从 Resources 加载 + 实例化 + 缩放 + 透明度 + 定时回收；follow 非空时挂到目标下跟随。</summary>
    static void SpawnEffect(string resPath, Vector3 center, float scale, float duration, float alpha = 1f, Transform follow = null)
    {
        var prefab = Resources.Load<GameObject>(resPath);
        if (prefab == null)
        {
            Debug.LogWarning("[FxFactory] 特效 prefab 加载失败: " + resPath);
            return;
        }
        var inst = Object.Instantiate(prefab, center, Quaternion.identity);
        if (follow != null)
        {
            inst.transform.SetParent(follow, false);
            inst.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            float ls = follow.lossyScale.x;
            if (ls > 0.001f) scale /= ls;
        }
        inst.transform.localScale = Vector3.one * scale;
        if (alpha < 1f)
        {
            foreach (var ps in inst.GetComponentsInChildren<ParticleSystem>())
            {
                var m = ps.main;
                var sc = m.startColor;
                var c = sc.color;
                sc.color = new Color(c.r, c.g, c.b, c.a * alpha);
                m.startColor = sc;
            }
        }
        Object.Destroy(inst, duration);
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
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
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
