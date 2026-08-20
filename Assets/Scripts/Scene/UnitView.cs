using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单位视图：数据层 UnitState 的 3D 表现。
/// 三种外观模式：
///  A. 3D 模型（野兽 type 11-14，自动从 Resources/Prefabs/Beasts/ 加载骷髅兵）
///  B. 2D 素材模式：把图片放进 Assets/Resources/Sprites/ 即自动生效
///  C. 程序化方块拼装（最终 fallback）
/// 按职责拆分为 Partial Class：血条 UnitView.Hp.cs / 动画 UnitView.Anim.cs / LOD UnitView.Lod.cs / 塔 UnitView.Tower.cs
/// </summary>
public partial class UnitView : MonoBehaviour
{
    public UnitState state;
    Transform _body;
    Transform _hpFill;
    Transform _selRing;
    float _hpY, _hpW, _hpThick = 0.05f;
    MeshRenderer _hpFillRend;
    MaterialPropertyBlock _mpb;
    Animator _animator;
    ReplayPlayer _player;
    bool _hasParams = true;
    TowerVisualController _towerVisual;

    // ── 野兽距离 LOD（远处降级为静态烘焙网格 + GPU 实例化，省 Animator CPU 与蒙皮 GPU） ──
    SkinnedMeshRenderer _skinned;   // 野兽 Robot 的活跃蒙皮渲染器
    GameObject _lodGo;              // 远处静态网格宿主
    bool _lodStatic;                // 当前是否处于静态 LOD 态
    Vector3 _lodBaseScale = Vector3.one; // 静态 LOD 网格基准缩放（1/lossyScale 补偿后，待机浮动在其上叠加）
    float _transientAnimUntil = 0f; // 远处野兽攻击/死亡时临时恢复动画的截止时间（真实时间）
    float _lastTransientEnter = -10f; // 上次进入瞬态动画的时间（冷却用，避免频繁攻击的野兽一直保持动画）

    // ── 平滑转向 ──
    Vector3 _prevPos;
    float _prevAnimScale = 1f;
    bool _wasMoving;
    bool _wasDead;
    bool? _lastStun;                    // 缓存上次眩晕状态，避免每帧重复写旋转
    int _lastHp = int.MinValue;         // 缓存上次血量，仅在 HP 变化时刷新材质
    int _lastMaxHp = int.MinValue;
    float _animSpeed = float.NaN;       // 缓存上次 Animator.speed，静止单位不再每帧赋值
    static ReplayPlayer s_cachedPlayer; // 全局缓存，避免大量单位各自 FindObjectOfType
    const float TURN_SPEED = 12f;
    bool _lockRotation; // true = 静态建筑/NPC，禁止转身
    float _baseScale = 1f; // 角色/building 的基础缩放（使宽度=1格）
    Vector3 _pivotOffset;   // 模型 pivot 修正偏移

    [Header("步幅调校")]
    public float strideCoefficient = 1.0f; // 调节这个值可以微调迈腿频率与地面的摩擦力

    // 单位类型 → Resources 路径（建筑 + 角色）
    static readonly Dictionary<int, string> UNIT_PREFABS = new Dictionary<int, string>
    {
        {3, "Prefabs/Buildings/Tower"},
        {4, "Prefabs/Buildings/Base"},
        {5, "Prefabs/Buildings/Wall"},
        {6, "Prefabs/Units/Worker"},
        {7, "Prefabs/Units/Pioneer"},
        {8, "Prefabs/Units/OfficerNPC"},
        {9, "Prefabs/Units/VendorNPC"},
        {10, "Prefabs/Buildings/WeaponShop"},
    };

    public static UnitView Create(UnitState u, Transform parent)
    {
        // 所有有 Prefab 的类型 → 直接实例化完整 Prefab
        string prefabPath;
        if (UNIT_PREFABS.TryGetValue(u.type, out prefabPath))
        {
            var prefab = Resources.Load<GameObject>(prefabPath);
            if (prefab != null)
            {
                var go = Object.Instantiate(prefab, parent);
                go.name = "Unit_" + u.id;
                var v = go.GetComponent<UnitView>();
                if (v == null) v = go.AddComponent<UnitView>();
                v.state = u;
                v.ConfigureFromUnitPrefab();
                return v;
            }
        }

        // 野兽类型 → 使用完整 Prefab
        if (u.type >= 11 && u.type <= 14)
        {
            var beastPrefab = Resources.Load<GameObject>("Prefabs/Beasts/Beast_" + u.type);
            if (beastPrefab != null)
            {
                var go = Object.Instantiate(beastPrefab, parent);
                go.name = "Unit_" + u.id;
                var v = go.GetComponent<UnitView>();
                if (v == null) v = go.AddComponent<UnitView>();
                v.state = u;
                v.ConfigureFromBeastPrefab();
                return v;
            }
        }

        // 未知类型：创建空占位（所有已知类型 3-14 均有 Prefab）
        Debug.LogWarning("[UnitView] 未知单位 type=" + u.type + " id=" + u.id + "，无对应 Prefab");
        var genericGo = new GameObject("Unit_" + u.id);
        genericGo.transform.SetParent(parent);
        var gv = genericGo.AddComponent<UnitView>();
        gv.state = u;
        return gv;
    }

    /// <summary>纯数据驱动回放：销毁单位身上所有物理组件（碰撞体/刚体），关闭后台物理引擎计算开销。</summary>
    void StripPhysics()
    {
        foreach (var col in GetComponentsInChildren<Collider>(true)) { Object.Destroy(col); }
        foreach (var rb in GetComponentsInChildren<Rigidbody>(true)) { Object.Destroy(rb); }
    }

    /// <summary>从 Beast Prefab 实例化后配置引用（模型已在 Prefab 中）。</summary>
    void ConfigureFromBeastPrefab()
    {
        StripPhysics();
        _lockRotation = false;
        _prevPos = transform.position;

        _body = transform.Find("Body");
        if (_body == null) _body = transform.Find("Visual");
        if (_body == null) _body = transform;

        _hpFill = transform.Find("HpFill");
        if (_hpFill != null)
        {
            _hpFillRend = _hpFill.GetComponent<MeshRenderer>();
            if (_hpFillRend != null)
            {
                _hpFillRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _hpFillRend.receiveShadows = false;
            }
        }

        _mpb = new MaterialPropertyBlock();
        _animator = GetComponentInChildren<Animator>();
        if (_animator != null)
        {
            _animator.applyRootMotion = false;
            SetupRobotAnimator();
        }

        // 距离 LOD：仅野兽启用；(false) 排除 inactive 的 Skeleton 幽灵件，取活跃 Robot 蒙皮
        _skinned = GetComponentInChildren<SkinnedMeshRenderer>(false);

        var pv = GetComponentInChildren<Pickable>();
        if (pv != null) pv.view = this;

        // 阴影/入场特效已在 Prefab 资产源头根治（野兽 prefab 渲染器关阴影、底层 Robot 模型移除 FX Hex），
        // 此处不再需要运行时遍历补救。
        UpgradeHpTo3D();
        EnsureRing();
        CalibrateBaseScale(1.5f);
        SetHp(state.hp, state.maxHp);
    }

    /// <summary>从 Unit Prefab（Worker/Pioneer/NPC）实例化后，找到子节点引用并配置队伍颜色</summary>
    void ConfigureFromUnitPrefab()
    {
        StripPhysics();
        // 建筑(3/4/5) 和 NPC(8/9) 锁死旋转；Worker(6)/Pioneer(7) 允许转身
        bool isBuilding = (state.type == 3 || state.type == 4 || state.type == 5 || state.type == 10);
        _lockRotation = isBuilding || (state.type == 8 || state.type == 9);
        _prevPos = transform.position;

        // NPC 初始朝向：任务官面朝地图右下(135°)，小贩面朝左上方(-45°)
        if (state.type == 8)
            transform.rotation = Quaternion.Euler(0f, 135f, 0f);
        else if (state.type == 9)
            transform.rotation = Quaternion.Euler(0f, -45f, 0f);

        _body = transform.Find("Body");
        if (_body == null) _body = transform.Find("Visual");
        if (_body == null) _body = transform;

        _hpFill = transform.Find("HpFill");
        if (_hpFill != null)
        {
            _hpFillRend = _hpFill.GetComponent<MeshRenderer>();
            if (_hpFillRend != null)
            {
                _hpFillRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _hpFillRend.receiveShadows = false;
            }
        }

        _mpb = new MaterialPropertyBlock();
        _animator = GetComponentInChildren<Animator>();
        if (_animator != null) _animator.applyRootMotion = false;

        // worker(type=6)：采集/建造砍劈动画用调整过的 Hit_Worker（头摆动减小、手臂摆动增大）
        if (state.type == 6) ApplyWorkerHitOverride();

        var pv = GetComponentInChildren<Pickable>();
        if (pv != null) pv.view = this;

        // 建筑队伍颜色：防守方显示红色模型，进攻方显示蓝色模型
        if (isBuilding)
        {
            bool isDefender = state.teamType == "defender";
            var visual = transform.Find("Visual");
            if (visual != null)
            {
                var modelRed = visual.Find("Model_Red");
                var modelBlue = visual.Find("Model_Blue");
                if (modelRed != null) modelRed.gameObject.SetActive(isDefender);
                if (modelBlue != null) modelBlue.gameObject.SetActive(!isDefender);
            }
        }

        // 防御塔 (type=3)：替换内部 Visual 为已转换的 Cube Tower Defense 模型
        if (state.type == 3)
        {
            SetupTowerVisual();
        }

        // 队伍颜色染色（仅 Worker/Pioneer，NPC 无 teamType 会自动跳过）
        var tca = GetComponentInChildren<TeamColorApplicator>();
        if (tca != null) { tca.unitView = this; tca.ApplyTeamColor(); }

        // NPC 转向组件：复用 Visual 节点作为旋转轴心
        if (state.type == 8 || state.type == 9)
        {
            var fc = GetComponent<NpcFacingController>();
            if (fc == null) fc = gameObject.AddComponent<NpcFacingController>();
            fc.npcType = state.type;
            var visual = transform.Find("Visual");
            if (visual != null) fc.facingTransform = visual;
        }

        UpgradeHpTo3D();
        EnsureRing();
        float targetW = state.type == 4 ? 2f : (state.type >= 6 && state.type <= 9) ? 1.5f : 1f;
        CalibrateBaseScale(targetW);
        SetHp(state.hp, state.maxHp);
        StripPhysics(); // 二次剥离：防御塔视觉包装(Tower_*_Red/Blue)是此时才实例化的，内部自带碰撞体需一并销毁

        // 调试悬浮文字（围墙/野兽在组件内部自行过滤；野兽路径不走本方法）
        gameObject.AddComponent<UnitDebugOverlay>();
    }

    /// <summary>根据模型实际尺寸计算缩放和 pivot 偏移，使模型居中并占满格子</summary>
    void CalibrateBaseScale(float targetWidth)
    {
        float maxW = 0f;
        Bounds combined = new Bounds(transform.position, Vector3.zero);
        Renderer[] rs = GetComponentsInChildren<Renderer>();
        for (int i = 0; i < rs.Length; i++)
        {
            combined.Encapsulate(rs[i].bounds);
            float w = Mathf.Max(rs[i].bounds.size.x, rs[i].bounds.size.z);
            if (w > maxW) maxW = w;
        }
        if (maxW > 0.01f) _baseScale = targetWidth / maxW;
        // 基地模型在 prefab 里已 X/Z 居中（实测 Model_Red/Blue boundsCenter=(0,0)），
        // 不再需要 pivot 偏移修正：state.pos 即 2×2 区域中心（UnitWorldPos +0.5），
        // transform.position = state.pos 即可让建筑地面中心对齐基地四格中心。
    }

    // ---------- 每帧刷新 ----------
    void LateUpdate()
    {
        if (state == null) return;

        // ── 位置（仅变化时写入，静止单位跳过每帧 Transform 刷新） ──
        Vector3 newPos = state.pos;
        Vector3 moveDir = newPos - _prevPos;
        _prevPos = newPos;
        bool posChanged = moveDir.sqrMagnitude > 0.0001f;
        if (posChanged)
        {
            // Y=0.01f 贴地，X/Z 用 pivot 偏移修正使模型底面中心对齐格子中心
            transform.position = new Vector3(newPos.x - _pivotOffset.x, 0.01f, newPos.z - _pivotOffset.z);
        }

        // ── 身体缩放（只在变化时设置，避免与 Animator 写入冲突） ──
        float targetScale = Mathf.Max(0.001f, state.animScale);
        if (!Mathf.Approximately(_prevAnimScale, targetScale))
        {
            _prevAnimScale = targetScale;
            if (_body != null) _body.localScale = Vector3.one * targetScale * _baseScale;
        }

        // ── 平滑转身（仅动态角色，建筑/NPC 锁死） ──
        bool isMovingNow = state.moving; // 主驱动力：数据层的移动标志
        if (!_lockRotation)
        {
            moveDir.y = 0f; // 禁止低头抬头
            // 帧间位移兜底：数据层标记可能有延迟，实际位移不会骗人
            if (!isMovingNow && moveDir.sqrMagnitude > 0.0001f)
                isMovingNow = true;
            if (isMovingNow && moveDir.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(moveDir);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * TURN_SPEED);
            }
        }

        // ── 静止冻结：消除暂停时的抖动（仅未到位时写入，避免每帧刷新 Transform） ──
        if (!isMovingNow && _body != null)
        {
            if (_body.localPosition != Vector3.zero) _body.localPosition = Vector3.zero;
            if (!state.stun && _body.localRotation != Quaternion.identity)
                _body.localRotation = Quaternion.identity;
        }

        // ── 子模块：动画状态同步 + 野兽距离 LOD（实现在 UnitView.Anim.cs / UnitView.Lod.cs） ──
        UpdateAnimationState(isMovingNow, posChanged, moveDir);
        UpdateLod();
    }

    public void SetHp(int hp, int maxHp)
    {
        // 仅在血量实际变化时刷新（ReplayPlayer 每帧调用）：静止单位避免每帧写 Transform + MaterialPropertyBlock
        if (hp == _lastHp && maxHp == _lastMaxHp) return;
        _lastHp = hp;
        _lastMaxHp = maxHp;
        if (_hpFill == null || _hpFillRend == null) return;
        float pct = Mathf.Clamp01((float)hp / Mathf.Max(1, maxHp));
        float fillW = _hpW;
        _hpFill.localScale = new Vector3(fillW * pct, _hpThick, 0.02f);
        _hpFill.localPosition = new Vector3(-fillW * 0.5f * (1f - pct), _hpY, 0);
        Color c = pct > 0.6f ? new Color(0.267f, 0.925f, 0.435f)
              : pct > 0.3f ? new Color(1f, 0.788f, 0.302f)
              : new Color(1f, 0.231f, 0.188f);
        _mpb.SetColor("_Color", c);
        _hpFillRend.SetPropertyBlock(_mpb);
    }

    public void SetStun(bool stun)
    {
        // 仅在眩晕状态变化时写入旋转，避免每帧重复赋值（旋转只在变化时需要同步）
        if (_lastStun.HasValue && _lastStun.Value == stun) return;
        _lastStun = stun;
        if (_body != null)
            _body.localRotation = stun ? Quaternion.Euler(0, 0, 90) : Quaternion.identity;
    }
}
