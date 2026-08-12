using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 可序列化的光照配置，包含一个时间段内方向光、环境光和背景色的完整参数。
/// </summary>
[System.Serializable]
public class LightingProfile
{
    [Header("Directional Light")]
    public Color lightColor = Color.white;
    public float lightIntensity = 1f;
    public Vector3 lightRotation = new Vector3(55f, -25f, 0f);
    public float shadowStrength = 0.5f;

    [Header("Ambient (Trilight)")]
    public Color ambientSky = new Color(0.55f, 0.62f, 0.7f);
    public Color ambientEquator = new Color(0.45f, 0.52f, 0.6f);
    public Color ambientGround = new Color(0.3f, 0.35f, 0.4f);

    [Header("Background")]
    public Color backgroundColor = new Color(0.55f, 0.74f, 0.87f);

    /// <summary>在两个 Profile 之间按 t (0~1) 平滑插值，返回新 Profile。</summary>
    public static LightingProfile Lerp(LightingProfile a, LightingProfile b, float t)
    {
        return new LightingProfile
        {
            lightColor      = Color.Lerp(a.lightColor, b.lightColor, t),
            lightIntensity  = Mathf.Lerp(a.lightIntensity, b.lightIntensity, t),
            lightRotation   = Vector3.Lerp(a.lightRotation, b.lightRotation, t),
            shadowStrength  = Mathf.Lerp(a.shadowStrength, b.shadowStrength, t),
            ambientSky      = Color.Lerp(a.ambientSky, b.ambientSky, t),
            ambientEquator  = Color.Lerp(a.ambientEquator, b.ambientEquator, t),
            ambientGround   = Color.Lerp(a.ambientGround, b.ambientGround, t),
            backgroundColor = Color.Lerp(a.backgroundColor, b.backgroundColor, t),
        };
    }
}

/// <summary>
/// 昼夜控制器 v2：四阶段光照 Profile（Day / Dusk / Night / Dawn），
/// 从 ReplayPlayer.RoundFloat 获取连续回合进度，Smooth01 过渡。
/// </summary>
public class DayNightController : MonoBehaviour
{
    public static DayNightController Instance { get; private set; }

    // ---- 缓存引用 ----
    ReplayPlayer _player;
    Light _dirLight;
    Camera _cam;
    float _prevCyclePos = -1f;

    // ==================== 四个光照 Profile ====================

    [Header("☀ Day       (cycle 5 ~ 65)")]
    public LightingProfile dayProfile = new LightingProfile
    {
        lightColor      = new Color(1.00f, 0.95f, 0.88f),
        lightIntensity  = 1.40f,
        lightRotation   = new Vector3(52f, -25f, 0f),
        shadowStrength  = 0.45f,
        ambientSky      = new Color(0.68f, 0.76f, 0.86f),
        ambientEquator  = new Color(0.68f, 0.64f, 0.55f),
        ambientGround   = new Color(0.44f, 0.42f, 0.38f),
        backgroundColor = new Color(0.52f, 0.74f, 0.90f),
    };

    [Header("🌅 Dusk      (cycle 65 ~ 80)")]
    public LightingProfile duskProfile = new LightingProfile
    {
        lightColor      = new Color(1.00f, 0.82f, 0.55f),
        lightIntensity  = 1.30f,
        lightRotation   = new Vector3(34f, -20f, 0f),
        shadowStrength  = 0.52f,
        ambientSky      = new Color(0.64f, 0.58f, 0.58f),
        ambientEquator  = new Color(0.72f, 0.58f, 0.42f),
        ambientGround   = new Color(0.46f, 0.40f, 0.34f),
        backgroundColor = new Color(0.82f, 0.60f, 0.48f),
    };

    [Header("☽ Night     (cycle 80 ~ 120)")]
    public LightingProfile nightProfile = new LightingProfile
    {
        lightColor      = new Color(0.68f, 0.78f, 1.00f),
        lightIntensity  = 0.78f,
        lightRotation   = new Vector3(45f, -25f, 0f),
        shadowStrength  = 0.20f,
        ambientSky      = new Color(0.38f, 0.44f, 0.56f),
        ambientEquator  = new Color(0.30f, 0.36f, 0.46f),
        ambientGround   = new Color(0.26f, 0.30f, 0.36f),
        backgroundColor = new Color(0.20f, 0.26f, 0.38f),
    };

    [Header("🌄 Dawn      (cycle 120 ~ 130)")]
    public LightingProfile dawnProfile = new LightingProfile
    {
        lightColor      = new Color(1.00f, 0.82f, 0.60f),
        lightIntensity  = 1.20f,
        lightRotation   = new Vector3(38f, -30f, 0f),
        shadowStrength  = 0.38f,
        ambientSky      = new Color(0.54f, 0.58f, 0.68f),
        ambientEquator  = new Color(0.62f, 0.50f, 0.40f),
        ambientGround   = new Color(0.36f, 0.32f, 0.30f),
        backgroundColor = new Color(0.66f, 0.56f, 0.66f),
    };

    // ==================== 生命周期 ====================
    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        _player = FindObjectOfType<ReplayPlayer>();
        _cam = Camera.main;

        var sunGo = GameObject.Find("Sun");
        if (sunGo != null) _dirLight = sunGo.GetComponent<Light>();

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.fog = false;
    }

    void LateUpdate()
    {
        if (_player == null || _player.data == null) return;

        float cp = Mathf.Repeat(_player.RoundFloat, 130f);
        if (Mathf.Abs(cp - _prevCyclePos) < 0.0005f) return;
        _prevCyclePos = cp;

        var profile = ResolveProfile(cp);
        ApplyProfile(profile);
    }

    // ==================== Profile 解析 ====================

    /// <summary>根据 cyclePosition 返回当前应使用的插值后 Profile。</summary>
    LightingProfile ResolveProfile(float cp)
    {
        //  0 ~  5：Dawn → Day（黎明过渡）
        //  5 ~ 65：Day（稳定白天）
        // 65 ~ 76：Day → Dusk（黄昏过渡）
        // 76 ~ 80：Dusk → Night（入夜）
        // 80 ~ 125：Night（稳定夜晚）
        // 125 ~ 130：Night → Dawn（破晓）

        if (cp < 5f)
            return LightingProfile.Lerp(dawnProfile, dayProfile, Smooth01((cp - 0f) / 5f));
        else if (cp < 65f)
            return dayProfile;
        else if (cp < 76f)
            return LightingProfile.Lerp(dayProfile, duskProfile, Smooth01((cp - 65f) / 11f));
        else if (cp < 80f)
            return LightingProfile.Lerp(duskProfile, nightProfile, Smooth01((cp - 76f) / 4f));
        else if (cp < 125f)
            return nightProfile;
        else
            return LightingProfile.Lerp(nightProfile, dawnProfile, Smooth01((cp - 125f) / 5f));
    }

    static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    // ==================== 应用光照 ====================

    void ApplyProfile(LightingProfile p)
    {
        if (_dirLight != null)
        {
            _dirLight.color = p.lightColor;
            _dirLight.intensity = p.lightIntensity;
            _dirLight.shadowStrength = p.shadowStrength;
            _dirLight.transform.rotation = Quaternion.Euler(p.lightRotation);
        }

        RenderSettings.ambientSkyColor = p.ambientSky;
        RenderSettings.ambientEquatorColor = p.ambientEquator;
        RenderSettings.ambientGroundColor = p.ambientGround;

        if (_cam != null)
            _cam.backgroundColor = p.backgroundColor;
    }
}
