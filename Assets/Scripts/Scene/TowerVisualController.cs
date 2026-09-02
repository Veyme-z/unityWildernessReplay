using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 防御塔 (roleType=3/30/31/32) 视觉控制器：SciFi 科幻塔视觉（挂包装 Prefab 根，序列化字段在 Inspector 配置，Setup 只读不覆盖）。
/// 职责：炮塔转向攻击目标/待机 180°、两阶段后坐力、枪口粒子/闪光、按武器类型用素材包原生特效（加特林弹道/激光/火箭）。
/// 本类按职责拆 Partial（每文件 < 300 行）：.Aim=Fire+LateUpdate 调度；.Laser=激光多光束；.Rocket=火箭直飞；.Fx=弹道/命中环。
/// </summary>
public partial class TowerVisualController : MonoBehaviour
{
    // 塔类型 → 炮塔节点名（可在嵌套层级内任意深度，递归查找）
    static readonly Dictionary<string, string> TURRET_NODES = new Dictionary<string, string>
    {
        { "Minigun", "Horizontal" },
        { "AntiAir", "Horizontal" },
        { "Laser", "Horizontal" },
        { "Rocket", "Horizontal" },
    };

    [Header("视觉摆放（本包装 Prefab 根）")]
    [Tooltip("基础缩放倍率（与 Prefab 根 Transform 的 scale 相乘；可直接改 Prefab 根 scale 控制大小）")]
    public float visualScale = 1.6f;
    [Tooltip("Y 轴偏移")]
    public float yOffset = 0f;
    [Tooltip("整体朝向修正（水平 yaw，度）")]
    public float forwardYawOffset = 0f;

    [Header("炮塔")]
    [Tooltip("炮塔可旋转节点（留空则按塔类型名自动查找）")]
    public Transform turretPivot;
    [Tooltip("枪口节点（留空则用炮塔前向延伸作为枪口）")]
    public Transform muzzleTransform;
    [Tooltip("待机朝向（水平 yaw，度）")]
    public float idleYawOffset = 180f;
    [Tooltip("转向速度（度/秒）")]
    public float turnSpeed = 540f;
    [Tooltip("炮塔头部俯仰上限（度）：攻击时上下跟随目标高度，限制在此范围内")]
    public float pitchLimit = 70f;
    [Tooltip("后坐力最大后退距离（炮塔局部 Z，枪口反方向）")]
    public float recoilDistance = 0.12f;

    [Header("时间参数（秒，可在 Inspector 调整）")]
    [Tooltip("攻击后炮塔保持瞄准目标时长")]
    public float aimHoldDuration = 1.0f;
    [Tooltip("后坐力快速后退阶段时长")]
    public float recoilKickDuration = 0.05f;
    [Tooltip("后坐力平滑恢复阶段时长")]
    public float recoilRecoverDuration = 0.23f;
    [Tooltip("枪口 Point Light 显示时长（≤0.2s）")]
    public float muzzleLightDuration = 0.16f;
    [Tooltip("Muzzle 粒子发射时长")]
    public float particleDuration = 0.45f;
    [Tooltip("命中圆环总时长")]
    public float hitRingDuration = 0.40f;

    [Header("SciFi 塔原生特效（按塔类型配置）")]
    [Tooltip("火箭塔：发射口节点数组（Rocket1_LOC/Rocket2_LOC）；激光光束由 Setup 自动按 LaserBeam* 前缀收集（Laser_2/3 多束）")]
    public Transform[] rocketLaunchers;

    // 枪口无独立节点时，用炮塔前向延伸的距离（世界单位）
    const float MUZZLE_FALLBACK_DIST = 0.7f;
    // 电磁狙击炮/激光塔：枪口粒子寿命放大倍数（延长闪光可见时长）
    const float RAILGUN_MUZZLE_LIFETIME_MULT = 2.5f;

    UnitView _view;
    ReplayPlayer _player;

    Transform _turret;
    Vector3 _turretBaseLocalPos;
    Quaternion _turretBaseLocalRot;
    Transform _muzzlePoint;
    ParticleSystem[] _muzzleParticles = new ParticleSystem[0];
    Light _flashLight;
    float _flashT;

    bool _hasAim;
    float _aimT;
    Vector3 _aimWorldDir;
    Vector3 _aimWorldDir3D; // 保留高度差的完整方向，用于炮塔俯仰
    bool _recoilKicking;
    float _recoilT;
    float _fireTime = -999f;
    bool _particlesFired;
    int _lastRound = -1;
    bool _particleFrozen; // 粒子冻结状态缓存，避免暂停期间每帧重复调用 FreezeParticles

    string _towerType = "";
    string _faction = "";
    bool _setup;

    public string TowerType { get { return _towerType; } }
    public bool IsSetup { get { return _setup; } }
    public Transform Turret { get { return _turret; } }

    /// <summary>武器工事类型 → 塔视觉类型：30 加特林=Minigun / 31 电磁狙击炮=Laser / 32 火箭发射台=Rocket（旧塔 3 兜底 Minigun）。</summary>
    public static string ResolveTowerType(UnitView view)
    {
        int t = view != null && view.state != null ? view.state.type : 3;
        if (t == 31) return "Laser";
        if (t == 32) return "Rocket";
        return "Minigun";
    }

    /// <summary>初始化：读取 Inspector 序列化值摆放视觉，解析炮塔/枪口节点并初始化激光/火箭特效。不覆盖 Inspector 值。</summary>
    public void Setup(UnitView view, string faction)
    {
        _view = view;
        _player = Object.FindObjectOfType<ReplayPlayer>();
        if (_player != null) _lastRound = _player.cur;

        _towerType = ResolveTowerType(view);
        _faction = faction;

        // 本节点就是视觉包装 Prefab 的根：尊重用户在 Prefab 根上直接设置的 scale（可直接改 Prefab 缩放控制大小）
        Vector3 prefabScale = transform.localScale;
        transform.localScale = Vector3.Scale(prefabScale, new Vector3(visualScale, visualScale, visualScale));
        transform.localPosition = new Vector3(0f, yOffset, 0f);
        transform.localRotation = Quaternion.Euler(0f, forwardYawOffset, 0f);

        // 炮塔节点：优先用 Inspector 配置的 turretPivot，否则按塔类型名递归查找
        _turret = turretPivot;
        if (_turret == null)
        {
            string turretName;
            if (TURRET_NODES.TryGetValue(_towerType, out turretName))
                _turret = FindChild(transform, turretName);
        }
        if (_turret == null) _turret = transform;
        _turretBaseLocalPos = _turret.localPosition;
        _turretBaseLocalRot = _turret.localRotation;

        // 枪口节点 + 粒子
        SetupMuzzle();

        // SciFi 塔特效（激光/火箭）初始化
        InitLaserFx();
        InitRocketFx();

        // 源塔 Muzzle 节点默认被禁用，激活以便枪口特效可用
        if (_muzzlePoint != null) _muzzlePoint.gameObject.SetActive(true);

        ApplyIdle();
        _setup = true;
    }

    /// <summary>解析枪口节点并收集其粒子（含 Shooting 附加粒子），关自动播放；电磁炮/激光延长枪口粒子寿命。</summary>
    void SetupMuzzle()
    {
        _muzzlePoint = muzzleTransform;
        if (_muzzlePoint == null) _muzzlePoint = FindChild(transform, "Muzzle");
        var psList = new List<ParticleSystem>();
        if (_muzzlePoint != null)
            psList.AddRange(_muzzlePoint.GetComponentsInChildren<ParticleSystem>(true));
        var shooting = FindChild(transform, "Shooting");
        if (shooting != null)
        {
            var sp = shooting.GetComponent<ParticleSystem>();
            if (sp != null && !psList.Contains(sp)) psList.Add(sp);
        }
        _muzzleParticles = psList.ToArray();

        // 关闭源塔粒子的自动播放，防止出生即开火（不自动循环攻击）
        foreach (var ps in _muzzleParticles)
        {
            if (ps == null) continue;
            var main = ps.main;
            main.playOnAwake = false;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        // 电磁狙击炮/激光塔：延长枪口特效粒子寿命，闪光更明显
        if (_towerType == "RPG" || _towerType == "Laser")
        {
            foreach (var ps in _muzzleParticles)
            {
                if (ps == null) continue;
                var main = ps.main;
                var lt = main.startLifetime;
                if (lt.mode == ParticleSystemCurveMode.Constant) lt.constant *= RAILGUN_MUZZLE_LIFETIME_MULT;
                else if (lt.mode == ParticleSystemCurveMode.TwoConstants)
                {
                    lt.constantMin *= RAILGUN_MUZZLE_LIFETIME_MULT;
                    lt.constantMax *= RAILGUN_MUZZLE_LIFETIME_MULT;
                }
                else lt.curveMultiplier *= RAILGUN_MUZZLE_LIFETIME_MULT;
                main.startLifetime = lt;
            }
        }
    }

    /// <summary>回到待机朝向（180°），同时复位后坐力。</summary>
    void ApplyIdle()
    {
        if (_turret == null) return;
        _turret.localPosition = _turretBaseLocalPos;
        _turret.localRotation = _turretBaseLocalRot * Quaternion.Euler(0f, idleYawOffset, 0f);
    }

    /// <summary>真实枪口世界坐标（弹道起点）。</summary>
    public Vector3 MuzzleWorldPosition()
    {
        if (_muzzlePoint != null) return _muzzlePoint.position;
        if (_turret != null) return _turret.position + _turret.forward * MUZZLE_FALLBACK_DIST;
        return transform.position + transform.forward * MUZZLE_FALLBACK_DIST;
    }

    /// <summary>塔模型世界包围盒高度（供 HP 条定位）。</summary>
    public float VisualHeight()
    {
        return _setup ? MeasureSize(gameObject, true) : 1.5f;
    }

    /// <summary>塔模型世界包围盒宽度（XZ 最大值，供 HP 条宽度）。</summary>
    public float VisualWidth()
    {
        return _setup ? MeasureSize(gameObject, false) : 1f;
    }

    // ---------- 内部通用 helper ----------

    Transform FindChild(Transform root, string name)
    {
        foreach (Transform child in root)
        {
            if (child.name == name) return child;
            var found = FindChild(child, name);
            if (found != null) return found;
        }
        return null;
    }

    float MeasureSize(GameObject go, bool height)
    {
        var b = new Bounds(go.transform.position, Vector3.zero);
        bool any = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer) continue; // 粒子不参与包围盒（拖尾会撑大尺寸）
            b.Encapsulate(r.bounds);
            any = true;
        }
        if (!any) return height ? 1.5f : 1f;
        return height ? b.size.y : Mathf.Max(b.size.x, b.size.z);
    }

    static float Smooth01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
    static float EaseOutCubic(float t) { t = Mathf.Clamp01(t); float u = 1f - t; return 1f - u * u * u; }

    /// <summary>枪口闪光：程序化点光，攻击时在枪口短暂点亮（旧 CubeTowerDefense 塔用；SciFi 塔用原生粒子不加）。</summary>
    void SpawnMuzzleFlash()
    {
        if (_flashLight == null)
        {
            var go = new GameObject("MuzzleFlash");
            _flashLight = go.AddComponent<Light>();
            _flashLight.type = LightType.Point;
            _flashLight.range = 3f;
            _flashLight.intensity = 0f;
            _flashLight.color = FactionColor();
            _flashLight.shadows = LightShadows.None;
        }
        Transform anchor = _muzzlePoint != null ? _muzzlePoint : _turret;
        _flashLight.transform.SetParent(anchor, false);
        _flashLight.transform.localPosition = _muzzlePoint != null ? Vector3.zero : new Vector3(0f, 0f, 0.5f);
        _flashLight.gameObject.SetActive(true);
        _flashLight.intensity = 3f;
        _flashT = 1f;
    }

    /// <summary>暂停时冻结/恢复枪口粒子（避免暂停期间粒子继续播放）。</summary>
    void FreezeParticles(bool freeze)
    {
        foreach (var ps in _muzzleParticles)
        {
            if (ps == null) continue;
            if (freeze)
            {
                if (ps.isPlaying) ps.Pause();
            }
            else
            {
                if (ps.isPaused) ps.Play();
            }
        }
    }
}
