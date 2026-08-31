using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 防御塔 (roleType=3) 视觉控制器：接管已转换的 Cube Tower Defense 塔模型。
///
/// 本组件挂在「视觉包装 Prefab」(Resources/Prefabs/Buildings/CubeTowers/Tower_{Type}_{Faction})
/// 的根节点上，序列化字段全部在 Prefab Inspector 里配置，Setup() 只读取、不覆盖这些值。
/// 运行时由 UnitView 把对应包装 Prefab 实例化到 Tower.prefab 的 VisualRoot 下。
///
/// 职责：
///   - 炮塔头面向 attack.targetPos，只旋转炮塔节点（不旋转底座/UnitView 根）
///   - 待机/攻击结束/Seek 复位后回到 idleYawOffset（默认 180°）待机朝向
///   - 两阶段程序化后坐力（快速后退 + 平滑恢复）+ 枪口粒子/闪光
///   - 攻击目标连线（Tracer，从真实枪口到 targetPos）+ 命中闪光圆环，只由真实 Replay attack 事件触发
///   - 不自动循环攻击、不修改伤害/攻击范围/Replay 状态
///
/// 武器工事视觉按类型：30 加特林→Minigun / 31 电磁狙击炮→RPG / 32 火箭发射台→Flamethrower
/// （红方 Tower_X_Red / 蓝方 Tower_X_Blue）。
/// </summary>
public class TowerVisualController : MonoBehaviour
{
    // 塔类型 → 炮塔节点名（可在嵌套层级内任意深度，递归查找）
    static readonly Dictionary<string, string> TURRET_NODES = new Dictionary<string, string>
    {
        { "Minigun", "Minigun" },
        { "RPG", "Rpg" },
        { "Flamethrower", "Flamethrower" },
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

    // 枪口无独立节点时，用炮塔前向延伸的距离（世界单位）
    const float MUZZLE_FALLBACK_DIST = 0.7f;
    // 电磁狙击炮：枪口粒子寿命放大倍数（延长闪光可见时长）
    const float RAILGUN_MUZZLE_LIFETIME_MULT = 2.5f;
    // 命中圆环阶段：快速淡入 + 保持较亮（剩余时间用于扩大淡出）
    const float HIT_FADE_IN = 0.05f;
    const float HIT_HOLD = 0.10f;

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

    // 攻击目标可视化（Tracer + 命中闪光）。支持多目标（加特林 N 弹道）：用池复用 LineRenderer/Quad，避免每回合 new。
    Color _tracerColor;
    readonly List<LineRenderer> _tracerPool = new List<LineRenderer>();
    readonly List<TracerFx> _activeTracers = new List<TracerFx>();
    readonly List<Transform> _hitRingPool = new List<Transform>();
    readonly List<HitRingFx> _activeHitRings = new List<HitRingFx>();

    class TracerFx
    {
        public LineRenderer lr;
        public float t;      // 剩余（1→0）
        public float dur;
        public Color color;
    }
    class HitRingFx
    {
        public Transform tr;
        public MeshRenderer rend;
        public float t;      // 经过时间（0→hitRingDuration）
    }

    string _towerType = "";
    string _faction = "";
    bool _setup;

    public string TowerType { get { return _towerType; } }
    public bool IsSetup { get { return _setup; } }
    public Transform Turret { get { return _turret; } }

    /// <summary>武器工事类型 → 塔视觉类型：30 加特林=Minigun / 31 电磁狙击炮=RPG / 32 火箭发射台=Flamethrower（旧塔 3 兜底 Minigun）。</summary>
    public static string ResolveTowerType(UnitView view)
    {
        int t = view != null && view.state != null ? view.state.type : 3;
        if (t == 31) return "RPG";
        if (t == 32) return "Flamethrower";
        return "Minigun";
    }

    /// <summary>初始化：读取 Inspector 序列化值摆放视觉，解析炮塔/枪口节点。不覆盖 Inspector 值。</summary>
    public void Setup(UnitView view, string faction)
    {
        _view = view;
        _player = Object.FindObjectOfType<ReplayPlayer>();
        if (_player != null) _lastRound = _player.cur;

        _towerType = ResolveTowerType(view);
        _faction = faction;

        // 本节点就是视觉包装 Prefab 的根。
        // 尊重用户在 Prefab 根上直接设置的 scale（与角色/机器人一致，可直接编辑 Prefab 缩放控制大小），
        // visualScale 作为其上的统一基础倍率（默认 1.6 保持原观感）。
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

        // 电磁狙击炮：延长枪口特效粒子寿命，闪光更明显（Constant 模式存在 constant；曲线模式用 curveMultiplier）
        if (_towerType == "RPG")
        {
            foreach (var ps in _muzzleParticles)
            {
                if (ps == null) continue;
                var main = ps.main;
                var lt = main.startLifetime;
                if (lt.mode == ParticleSystemCurveMode.Constant)
                    lt.constant *= RAILGUN_MUZZLE_LIFETIME_MULT;
                else if (lt.mode == ParticleSystemCurveMode.TwoConstants)
                {
                    lt.constantMin *= RAILGUN_MUZZLE_LIFETIME_MULT;
                    lt.constantMax *= RAILGUN_MUZZLE_LIFETIME_MULT;
                }
                else lt.curveMultiplier *= RAILGUN_MUZZLE_LIFETIME_MULT;
                main.startLifetime = lt;
            }
        }

        // 源塔 Muzzle 节点默认被禁用，激活以便枪口特效可用
        if (_muzzlePoint != null) _muzzlePoint.gameObject.SetActive(true);

        ApplyIdle();
        _setup = true;
    }

    /// <summary>回到待机朝向（180°），同时复位后坐力。</summary>
    void ApplyIdle()
    {
        if (_turret == null) return;
        _turret.localPosition = _turretBaseLocalPos;
        _turret.localRotation = _turretBaseLocalRot * Quaternion.Euler(0f, idleYawOffset, 0f);
    }

    /// <summary>触发一次攻击表现：单目标（只由真实 Replay attack 事件调用）。</summary>
    public void Fire(Vector3 targetWorldPos) { Fire(new Vector3[] { targetWorldPos }); }

    /// <summary>
    /// 触发一次攻击表现（多目标）：转向主目标 + 后坐力 + 枪口特效 + 按塔类型画弹道。
    /// 30 加特林(Minigun)=N 条弹道；31 电磁狙击炮(RPG)=单发穿透粗激光；32 火箭发射台(Flamethrower)=无弹道（落点爆炸由 ReplayPlayer 触发）。
    /// </summary>
    public void Fire(Vector3[] targetWorldPositions)
    {
        if (!_setup || _turret == null) return;
        if (targetWorldPositions == null || targetWorldPositions.Length == 0) return;
        Vector3 primary = targetWorldPositions[0];

        FireMuzzleOnly(primary);

        if (_towerType == "Flamethrower") return;          // 火箭发射台：无弹道（爆炸特效由 ReplayPlayer 播放）

        if (_towerType == "RPG")                           // 电磁狙击炮：枪口闪光 + 落点电流电击（CFXR Electrified）
        {
            HitAt(primary);
            return;
        }

        foreach (var wp in targetWorldPositions)           // 加特林：N 条弹道 + N 个命中闪光
        {
            SpawnTracer(wp);
            SpawnHitRing(wp);
        }
    }

    /// <summary>塔开火：炮塔转向目标 + 后坐力 + 枪口粒子/闪光，但**不下发目标命中效果**（留给飞行弹体到达时）。</summary>
    public void FireMuzzleOnly(Vector3 targetWorldPos)
    {
        if (!_setup || _turret == null) return;

        Vector3 fullDir = targetWorldPos - _turret.position;
        Vector3 dir = fullDir;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = _turret.forward;
        _aimWorldDir = dir.normalized;
        // 完整 3D 方向：保留高度差，供炮塔上下俯仰跟随目标
        _aimWorldDir3D = fullDir.sqrMagnitude < 0.0001f ? _aimWorldDir : fullDir.normalized;
        _hasAim = true;
        _aimT = aimHoldDuration;

        // 两阶段后坐力：从当前状态自然重新触发（位置恒为 base + offset，不累计漂移）
        _recoilKicking = true;
        _recoilT = 0f;

        // 播放一次枪口粒子（电磁炮 RPG 除外：枪口特效改由电球在枪口充能承担，避免和 CFXR 混搭）
        if (_towerType != "RPG")
        {
            foreach (var ps in _muzzleParticles)
                if (ps != null) ps.Play();
            if (_muzzleParticles.Length > 0)
            {
                _particlesFired = true;
                _fireTime = Time.time;
            }
            SpawnMuzzleFlash();
        }
    }

    /// <summary>目标命中效果（命中环 + CFXR 电流电击），飞行弹体到达目标时调用。电流按阵营染色（红=淡红/蓝=淡蓝）。</summary>
    public void HitAt(Vector3 targetWorldPos)
    {
        if (!_setup) return;
        SpawnHitRing(targetWorldPos);
        FxFactory.PlayElectricHit(targetWorldPos, FxFactory.FactionElectricColor(_faction));
    }

    /// <summary>清除攻击状态（Seek 跳转后调用）：清空转向/后坐力/粒子/闪光/Tracer/命中闪光，复位到待机 180°。</summary>
    public void ResetAttack()
    {
        _hasAim = false;
        _aimT = 0f;
        _aimWorldDir = Vector3.forward;
        _aimWorldDir3D = Vector3.forward;
        _recoilKicking = false;
        _recoilT = 0f;
        _particlesFired = false;
        ApplyIdle();
        foreach (var ps in _muzzleParticles)
            if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (_flashLight != null)
        {
            _flashLight.intensity = 0f;
            _flashLight.gameObject.SetActive(false);
        }
        _flashT = 0f;
        ClearTracer();
        ClearHitRing();
    }

    /// <summary>真实枪口世界坐标（Tracer 起点）。</summary>
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

    // ---------- 攻击目标可视化 ----------

    void GetTracerStyle(out Color c, out float sw, out float ew, out float dur)
    {
        c = FactionColor();
        if (_towerType == "RPG")
        {
            // 电磁狙击炮：单发穿透激光——粗、亮、持续时间长（能量 = 25×等级，等级越高略粗）
            float lvl = _view != null && _view.state != null ? Mathf.Clamp(_view.state.level, 1, 5) : 1f;
            sw = 0.18f + 0.02f * lvl;
            ew = 0.10f + 0.02f * lvl;
            dur = 0.6f;
        }
        else
        {
            // 加特林：细、快弹道（每颗子弹 20 伤害，N 颗）
            sw = 0.07f;
            ew = 0.04f;
            dur = 0.15f;
        }
    }

    /// <summary>阵营特效颜色：防守方红 / 进攻方蓝（与 TeamColorApplicator 的霓虹色一致）。</summary>
    Color FactionColor()
    {
        return _faction == "Blue" ? new Color(0f, 0.478f, 1f) : new Color(1f, 0.176f, 0.333f);
    }

    void SpawnTracer(Vector3 targetWorldPos)
    {
        float sw, ew, dur;
        GetTracerStyle(out _tracerColor, out sw, out ew, out dur);

        LineRenderer lr;
        if (_tracerPool.Count > 0)
        {
            lr = _tracerPool[0];
            _tracerPool.RemoveAt(0);
            lr.sharedMaterial = MatLib.Get(_tracerColor);
        }
        else
        {
            var go = new GameObject("TowerTracer");
            lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.sharedMaterial = MatLib.Get(_tracerColor);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
        }

        Vector3 from = MuzzleWorldPosition();
        Vector3 to = targetWorldPos + Vector3.up * 0.35f;
        lr.SetPosition(0, from);
        lr.SetPosition(1, to);
        lr.startWidth = sw;
        lr.endWidth = ew;
        lr.startColor = _tracerColor;
        lr.endColor = new Color(_tracerColor.r, _tracerColor.g, _tracerColor.b, 0.25f);
        lr.gameObject.SetActive(true);
        _activeTracers.Add(new TracerFx { lr = lr, t = 1f, dur = dur, color = _tracerColor });
    }

    void ClearTracer()
    {
        for (int i = _activeTracers.Count - 1; i >= 0; i--)
        {
            var fx = _activeTracers[i];
            fx.lr.gameObject.SetActive(false);
            _tracerPool.Add(fx.lr);
            _activeTracers.RemoveAt(i);
        }
    }

    void SpawnHitRing(Vector3 targetWorldPos)
    {
        Transform rt;
        MeshRenderer rend;
        if (_hitRingPool.Count > 0)
        {
            rt = _hitRingPool[0];
            _hitRingPool.RemoveAt(0);
            rend = rt.GetComponent<MeshRenderer>();
            rt.gameObject.SetActive(true);
        }
        else
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "TowerHitRing";
            rend = go.GetComponent<MeshRenderer>();
            // 独立材质实例：烘焙成色的圆环贴图，不污染 MatLib 共享材质池
            var mat = new Material(MatLib.Shader2D);
            mat.mainTexture = MatLib.CreateRingTex(_tracerColor, 64);
            mat.color = Color.white;
            rend.sharedMaterial = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            rt = go.transform;
        }

        rt.position = targetWorldPos + Vector3.up * 0.05f;
        rt.rotation = Quaternion.Euler(90f, 0f, 0f);
        rt.localScale = new Vector3(0.25f, 0.25f, 1f);
        _activeHitRings.Add(new HitRingFx { tr = rt, rend = rend, t = 0f }); // 经过时间（0 → hitRingDuration）
    }

    void ClearHitRing()
    {
        for (int i = _activeHitRings.Count - 1; i >= 0; i--)
        {
            var fx = _activeHitRings[i];
            fx.tr.gameObject.SetActive(false);
            _hitRingPool.Add(fx.tr);
            _activeHitRings.RemoveAt(i);
        }
    }

    // ---------- 内部 ----------

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

    static float Smooth01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
    static float EaseOutCubic(float t) { t = Mathf.Clamp01(t); float u = 1f - t; return 1f - u * u * u; }

    void LateUpdate()
    {
        if (!_setup || _turret == null) return;

        // Seek 检测：大幅跳转 → 清除旧攻击状态
        if (_player != null && _player.cur != _lastRound)
        {
            bool seeked = Mathf.Abs(_player.cur - _lastRound) > 1;
            _lastRound = _player.cur;
            if (seeked) ResetAttack();
        }

        bool playing = _player == null || _player.playing;
        if (!playing)
        {
            if (!_particleFrozen) { FreezeParticles(true); _particleFrozen = true; }
            return; // 暂停：冻结炮塔/后坐力/粒子/闪光/Tracer/命中闪光
        }
        if (_particleFrozen) { FreezeParticles(false); _particleFrozen = false; }

        // ── 完全空闲快速退出：无瞄准/后坐/闪光/粒子/Tracer/命中圆环时，只做待机对齐 ──
        bool hasActive = _hasAim || _recoilKicking || _recoilT < 1f || _flashT > 0f
                         || _activeTracers.Count > 0 || _particlesFired
                         || _activeHitRings.Count > 0;
        if (!hasActive)
        {
            _recoilT = 1f;
            if (_turret.localPosition != _turretBaseLocalPos)
                _turret.localPosition = _turretBaseLocalPos;
            // 待机旋转：已到位则跳过，避免空闲塔每帧重复四元数计算
            Vector3 idleFwd = Quaternion.Euler(0f, idleYawOffset, 0f) * transform.forward;
            Quaternion idle = Quaternion.LookRotation(idleFwd, Vector3.up);
            if (Quaternion.Angle(_turret.rotation, idle) > 0.1f)
                _turret.rotation = Quaternion.RotateTowards(_turret.rotation, idle, turnSpeed * Time.deltaTime);
            return;
        }

        // 攻击瞄准保持计时：到期回到待机朝向（连续攻击时 Fire 会刷新 _aimT）
        if (_hasAim)
        {
            _aimT -= Time.deltaTime;
            if (_aimT <= 0f) _hasAim = false;
        }

        // 炮塔转向：水平 yaw 指向目标 + 上下俯仰 pitch 跟随目标高度；否则回到待机 180°
        Quaternion desired;
        if (_hasAim)
        {
            // 水平投影做 yaw（方向只有 XZ）
            Vector3 flat = new Vector3(_aimWorldDir3D.x, 0f, _aimWorldDir3D.z);
            if (flat.sqrMagnitude < 0.0001f) flat = transform.forward;
            flat.Normalize();
            Quaternion yaw = Quaternion.LookRotation(flat, Vector3.up);
            // 高度差转俯仰角（正=向下，负=向上），绕炮塔自身 X 轴
            float pitchDeg = Mathf.Asin(Mathf.Clamp(-_aimWorldDir3D.y, -1f, 1f)) * Mathf.Rad2Deg;
            pitchDeg = Mathf.Clamp(pitchDeg, -pitchLimit, pitchLimit);
            desired = yaw * Quaternion.Euler(pitchDeg, 0f, 0f);
        }
        else
        {
            Vector3 idleFwd = Quaternion.Euler(0f, idleYawOffset, 0f) * transform.forward;
            desired = Quaternion.LookRotation(idleFwd, Vector3.up);
        }
        _turret.rotation = Quaternion.RotateTowards(_turret.rotation, desired, turnSpeed * Time.deltaTime);

        // 后坐力两阶段：快速后退（EaseOutCubic）+ 平滑恢复（Smooth01），位置恒为 base+offset 不漂移
        if (_recoilKicking)
        {
            _recoilT += Time.deltaTime / recoilKickDuration;
            if (_recoilT >= 1f) { _recoilT = 0f; _recoilKicking = false; }
            _turret.localPosition = _turretBaseLocalPos + new Vector3(0f, 0f, -recoilDistance * EaseOutCubic(_recoilT));
        }
        else if (_recoilT < 1f)
        {
            _recoilT += Time.deltaTime / recoilRecoverDuration;
            _turret.localPosition = _turretBaseLocalPos + new Vector3(0f, 0f, -recoilDistance * (1f - Smooth01(_recoilT)));
        }
        else if (_turret.localPosition != _turretBaseLocalPos)
        {
            _turret.localPosition = _turretBaseLocalPos;
        }

        // 枪口粒子：发射后短暂停止发射，防止循环开火
        if (_particlesFired && Time.time > _fireTime + particleDuration)
        {
            _particlesFired = false;
            foreach (var ps in _muzzleParticles)
                if (ps != null && ps.isPlaying) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        // 枪口闪光衰减：到期禁用（不销毁，下次攻击重新 SetActive）
        if (_flashT > 0f && _flashLight != null)
        {
            _flashT -= Time.deltaTime / muzzleLightDuration;
            _flashLight.intensity = _flashT > 0f ? 3f * _flashT : 0f;
            if (_flashT <= 0f) { _flashT = 0f; _flashLight.gameObject.SetActive(false); }
        }

        // Tracer 淡出：遍历活跃弹道，各弹道独立计时（加特林多弹道各自淡出）
        for (int i = _activeTracers.Count - 1; i >= 0; i--)
        {
            var fx = _activeTracers[i];
            fx.t -= Time.deltaTime / fx.dur;
            float a = Mathf.Clamp01(fx.t);
            fx.lr.startColor = new Color(fx.color.r, fx.color.g, fx.color.b, a);
            fx.lr.endColor = new Color(fx.color.r, fx.color.g, fx.color.b, a * 0.25f);
            if (fx.t <= 0f)
            {
                fx.lr.gameObject.SetActive(false);
                _tracerPool.Add(fx.lr);
                _activeTracers.RemoveAt(i);
            }
        }

        // 命中圆环三阶段：快速淡入 → 保持较亮 → 扩大并平滑淡出（多落点各自推进）
        for (int i = _activeHitRings.Count - 1; i >= 0; i--)
        {
            var fx = _activeHitRings[i];
            fx.t += Time.deltaTime;
            float holdEnd = HIT_FADE_IN + HIT_HOLD;
            float expandDur = Mathf.Max(0.01f, hitRingDuration - holdEnd);
            float alpha, scale;
            if (fx.t < HIT_FADE_IN)
            {
                float p = fx.t / HIT_FADE_IN;
                alpha = p;
                scale = 0.25f;
            }
            else if (fx.t < holdEnd)
            {
                float p = (fx.t - HIT_FADE_IN) / HIT_HOLD;
                alpha = 1f;
                scale = Mathf.Lerp(0.25f, 0.4f, Smooth01(p));
            }
            else
            {
                float p = Mathf.Clamp01((fx.t - holdEnd) / expandDur);
                alpha = 1f - Smooth01(p);
                scale = Mathf.Lerp(0.4f, 0.7f, Smooth01(p));
            }
            fx.tr.localScale = new Vector3(scale, scale, 1f);
            if (fx.rend != null) fx.rend.sharedMaterial.color = new Color(1f, 1f, 1f, alpha);
            if (fx.t >= hitRingDuration)
            {
                fx.tr.gameObject.SetActive(false);
                _hitRingPool.Add(fx.tr);
                _activeHitRings.RemoveAt(i);
            }
        }
    }

    /// <summary>塔被销毁时清理 Tracer/命中闪光等根级对象（它们不随塔节点销毁）。</summary>
    void OnDestroy()
    {
        for (int i = _activeTracers.Count - 1; i >= 0; i--)
        {
            if (_activeTracers[i].lr != null) Object.Destroy(_activeTracers[i].lr.gameObject);
            _activeTracers.RemoveAt(i);
        }
        foreach (var lr in _tracerPool) if (lr != null) Object.Destroy(lr.gameObject);
        _tracerPool.Clear();
        for (int i = _activeHitRings.Count - 1; i >= 0; i--)
        {
            if (_activeHitRings[i].tr != null) Object.Destroy(_activeHitRings[i].tr.gameObject);
            _activeHitRings.RemoveAt(i);
        }
        foreach (var rt in _hitRingPool) if (rt != null) Object.Destroy(rt.gameObject);
        _hitRingPool.Clear();
        if (_flashLight != null) Object.Destroy(_flashLight.gameObject);
    }
}
