using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 单位视图：数据层 UnitState 的 3D 表现。
/// 三种外观模式：
///  A. 3D 模型（野兽 type 11-14，自动从 Resources/Prefabs/Beasts/ 加载骷髅兵）
///  B. 2D 素材模式：把图片放进 Assets/Resources/Sprites/ 即自动生效
///  C. 程序化方块拼装（最终 fallback）
/// </summary>
public class UnitView : MonoBehaviour
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
    /// <summary>距离 LOD 调参（public static，运行时可用 execute_code / 调试工具直接改值，无需重编译）：
    /// LOD_RANGE=相机 XZ 距离阈值，调大→更多野兽动画(CPU↑)，调小→更少；受相机位置影响大，建议保持 30。
    /// LodTransientCooldown=远处野兽攻击瞬态冷却秒数，调小→攻击动作更频繁(CPU↑)，调大→更稀疏。
    /// LodTransientWindow=每次攻击瞬态动画持续秒数，调大→动作更完整(并发↑)。
    /// LodIdleBobAmplitude/LodIdleSwayAmplitude=静态待机浮动上下幅度/缩放幅度，纯视觉、CPU≈0。</summary>
    public static float LOD_RANGE = 30f;
    public static float LodTransientCooldown = 2.5f;
    public static float LodTransientWindow = 1f;
    public static float LodIdleBobAmplitude = 0.03f;
    public static float LodIdleSwayAmplitude = 0.012f;
    static readonly Dictionary<int, Mesh> s_lodMeshCache = new Dictionary<int, Mesh>(); // 每类型共享一份烘焙网格
    static Camera s_camera;         // 复用 Camera.main 缓存

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
public static float AnimatorSpeed = 1f; // 由 ReplayPlayer 同步播放倍速
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

    void SetupRobotAnimator()
    {
        if (_animator.runtimeAnimatorController == null) return;
        if (_animator.parameterCount > 0) { _hasParams = true; return; }

        var baseCtrl = Resources.Load<RuntimeAnimatorController>("Animations/Skeleton_AnimatorController");
        if (baseCtrl == null) return;
        var overrides = new AnimatorOverrideController(baseCtrl);
        var robotClips = _animator.runtimeAnimatorController.animationClips;
        if (robotClips != null && robotClips.Length > 0)
        {
            var idleClip  = FindClip(robotClips, "Idle");
            var walkClip  = FindClip(robotClips, "Walk", "Run", "Fly", "Dash");
            var atkClip   = FindClip(robotClips, "Attack", "Punch", "Slash", "Claw", "Projectile", "Slam");
            var deathClip = FindClip(robotClips, "Die", "Death");
            overrides["Idle_A"]    = idleClip ?? robotClips[0];
            overrides["Walking_A"] = walkClip ?? idleClip ?? robotClips[0];
            overrides["Hit_A"]     = atkClip  ?? idleClip ?? robotClips[0];
            overrides["Death_A"]   = deathClip ?? idleClip ?? robotClips[0];
        }
        _animator.runtimeAnimatorController = overrides;
        _hasParams = true;
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

    /// <summary>worker(type=6)：用 AnimatorOverrideController 把砍劈动画 Hit_A 替换为调整过的 Hit_Worker。</summary>
    void ApplyWorkerHitOverride()
    {
        var hitClip = Resources.Load<AnimationClip>("Animations/Hit_Worker");
        if (_animator == null || hitClip == null) return;
        if (_animator.runtimeAnimatorController == null) return;
        if (_animator.runtimeAnimatorController is AnimatorOverrideController) return;

        var overrides = new AnimatorOverrideController(_animator.runtimeAnimatorController);
        overrides["Hit_A"] = hitClip;
        _animator.runtimeAnimatorController = overrides;
    }

    /// <summary>防御塔 (type=3)：隐藏旧 Visual，改为 Resources 中可编辑的 Cube Tower Defense 视觉包装 Prefab。</summary>
    void SetupTowerVisual()
    {
        bool isDefender = state.teamType == "defender";
        string faction = isDefender ? "Red" : "Blue";

        // 关闭旧 Visual（旧 KayKit 塔模型），由新塔视觉替代内部视觉
        var visual = transform.Find("Visual");
        if (visual != null) visual.gameObject.SetActive(false);

        // 视觉宿主：优先复用 Tower.prefab 中的 VisualRoot，否则运行时创建
        Transform visualRoot = transform.Find("VisualRoot");
        if (visualRoot == null)
        {
            var vr = new GameObject("VisualRoot");
            vr.transform.SetParent(transform, false);
            visualRoot = vr.transform;
        }

        // 运行时选择 Resources 中的视觉包装 Prefab（以后调尺寸直接改对应 CubeTowers Prefab）
        string type = TowerVisualController.ResolveTowerType(this);
        string path = "Prefabs/Buildings/CubeTowers/Tower_" + type + "_" + faction;
        var prefab = Resources.Load<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogWarning("[UnitView] 未找到防御塔视觉包装 " + path);
            return;
        }

        var inst = Object.Instantiate(prefab, visualRoot);
        inst.name = "TowerVisual_" + type;
        _towerVisual = inst.GetComponent<TowerVisualController>();
        if (_towerVisual == null) _towerVisual = inst.AddComponent<TowerVisualController>();
        _towerVisual.Setup(this, faction);
    }

    /// <summary>从 clips 中按优先级匹配第一个包含关键字的动画。</summary>

    /// <summary>从 clips 中按优先级匹配第一个包含关键字的动画。</summary>
    static AnimationClip FindClip(AnimationClip[] clips, params string[] keywords)
    {
        foreach (var kw in keywords)
        {
            foreach (var c in clips)
            {
                if (c != null && c.name.IndexOf(kw, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return c;
            }
        }
        return null;
    }

    /// <summary>估算 GameObject 的包围盒高度</summary>
    float EstimateHeight(GameObject go)
    {
        var bounds = new Bounds(go.transform.position, Vector3.zero);
        bool hasRenderer = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            bounds.Encapsulate(r.bounds);
            hasRenderer = true;
        }
        return hasRenderer ? bounds.size.y : 2f;
    }

    /// <summary>估算 GameObject 的水平包围盒宽度（XZ 最大值）</summary>
    float EstimateWidth(GameObject go)
    {
        var bounds = new Bounds(go.transform.position, Vector3.zero);
        bool hasRenderer = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            bounds.Encapsulate(r.bounds);
            hasRenderer = true;
        }
        return hasRenderer ? Mathf.Max(bounds.size.x, bounds.size.z) : 0.5f;
    }

    /// <summary>防御塔(3)血条顶部安全偏移：在 VisualHeight() 塔顶高度之上再抬高，确保清晰悬浮在炮塔正上方。</summary>
    const float TOWER_HP_TOP_PADDING = 0.9f;

    /// <summary>将 Prefab 中扁平的 Quad 血条在运行时升级为 3D Cube 网格</summary>
    void UpgradeHpTo3D()
    {
        var cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        float modelH, modelW;
        if (state.type == 3 && _towerVisual != null && _towerVisual.IsSetup)
        {
            // 新塔模型按 Renderer 包围盒调整 HP 条
            modelH = _towerVisual.VisualHeight();
            modelW = _towerVisual.VisualWidth();
        }
        else
        {
            modelH = _body != null ? EstimateHeight(_body.gameObject) : 2f;
            modelW = _body != null ? EstimateWidth(_body.gameObject) : 0.5f;
        }
        _hpW = Mathf.Max(modelW, 0.3f);
        // 基地/塔在模型顶部，开拓者 0.65，其余 0.55
        if (state.type == 4) { _hpY = modelH + 2f; _hpW *= 1.6f; }
        else if (state.type == 3) { _hpY = modelH + TOWER_HP_TOP_PADDING; _hpW *= 1.28f; _hpThick = 0.06f; }
        else if (state.type == 7) _hpY = modelH * 0.65f;
        else if (state.type == 11) { _hpY = modelH + 0.4f; _hpW *= 1.3f; }
        else if (state.type == 12) { _hpY = modelH - 0.2f; _hpW *= 1.3f; }
        else if (state.type == 13) { _hpY = modelH + 1.8f; _hpW *= 2.5f; }
        else if (state.type == 14) { _hpY = modelH + 1.8f; _hpW *= 2f; }
        else _hpY = modelH * 0.55f;
        // 销毁旧黑底 HpBar
        var oldBar = transform.Find("HpBar");
        if (oldBar != null) Destroy(oldBar.gameObject);
        // 填充条
        if (_hpFill == null)
        {
            _hpFill = CreateHpCube(transform, "HpFill", new Vector3(_hpW, _hpThick, 0.02f), new Color(0.267f, 0.925f, 0.435f), cubeMesh);
            _hpFillRend = _hpFill.GetComponent<MeshRenderer>();
        }
        else
        {
            var bb = _hpFill.GetComponent<Billboard>();
            if (bb != null) Destroy(bb);
            _hpFill.GetComponent<MeshFilter>().sharedMesh = cubeMesh;
            _hpFill.localScale = new Vector3(_hpW, _hpThick, 0.02f);
            if (_hpFillRend == null) _hpFillRend = _hpFill.GetComponent<MeshRenderer>();
            if (_hpFillRend != null && (_hpFillRend.sharedMaterial.name.Contains("Default") || _hpFillRend.sharedMaterial.shader.name != "Standard"))
                _hpFillRend.sharedMaterial = GetSharedHpFillMat();
        }
        _hpFill.localPosition = new Vector3(0, _hpY, 0);
        _hpFill.localRotation = Quaternion.identity;
        if (_hpFillRend == null) _hpFillRend = _hpFill.GetComponent<MeshRenderer>();
        if (_hpFillRend != null) { _hpFillRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; _hpFillRend.receiveShadows = false; }
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
    }

    void EnsureRing()
    {
        // 仅 Worker(6) / Pioneer(7) 显示阵营光环
        if (state.type != 6 && state.type != 7) return;
        if (_selRing == null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "SelRing";
            go.transform.SetParent(transform, false);
            go.transform.localScale = new Vector3(0.8f, 0.8f, 1f);
            var rend = go.GetComponent<MeshRenderer>();
            // Sprites/Default 在这个项目中已验证 _Color 倍乘有效
            var mat = new Material(MatLib.Shader2D);
            mat.mainTexture = MatLib.ringTex;
            rend.sharedMaterial = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            _selRing = go.transform;
        }
        else
        {
            var bb = _selRing.GetComponent<Billboard>();
            if (bb != null) Destroy(bb);
        }
        _selRing.localPosition = new Vector3(0, 0.02f, 0);
        _selRing.localRotation = Quaternion.Euler(90f, 0f, 0f);
        _selRing.gameObject.SetActive(true);
        ApplyRingColor();
    }

    void ApplyRingColor()
    {
        if (_selRing == null || state == null) return;
        var sr = _selRing.GetComponent<MeshRenderer>();
        if (sr == null) return;

        Color ringColor;
        if (state.teamType == "defender")
            ringColor = new Color(1f, 0.176f, 0.333f, 1f);
        else if (state.teamType == "challenger")
            ringColor = new Color(0f, 0.478f, 1f, 1f);
        else
            return;

        // 颜色直接烘焙到贴图像素中，不依赖 shader _Color
        var coloredTex = MatLib.CreateRingTex(ringColor, 128);
        sr.sharedMaterial.mainTexture = coloredTex;
        // 重置 material.color 为白色，确保 Sprites/Default 的 tint 不影响已烘焙的颜色
        sr.sharedMaterial.color = Color.white;
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

    /// <summary>更新动画状态（外部调用 — 仅负责 Trigger，isMoving 由 LateUpdate 统一管理）</summary>
    public void UpdateAnimation(bool isMoving, bool isDead)
    {
        if (_animator == null) return;
        // 只在死亡状态发生变化时触发一次（ReplayPlayer 每帧调用，避免死亡期间重复 SetTrigger 空耗）
        if (isDead == _wasDead) return;
        _wasDead = isDead;
        try
        {
            if (isDead)
            {
                if (_hasParams) _animator.SetTrigger("onDeath");
                else _animator.Play("Die");
            }
        }
        catch (System.Exception) { }
    }

    /// <summary>触发攻击动画</summary>
    public void TriggerAttack()
    {
        // 远处静态野兽攻击时临时恢复骨骼动画（播放攻击动作，随后自动回静态）。
        // 冷却 2.5s + 窗口 1.0s（占空比 ~40%）：频繁攻击的野兽只在一部分攻击时动画，限制并发动画数，
        // 否则夜间上百只野兽同时攻击会全部进动画 → CPU 回升（实测跳转后 101/140 远处野兽动画）。
        if (_lodStatic && _skinned != null && Time.time - _lastTransientEnter > LodTransientCooldown)
        {
            _lastTransientEnter = Time.time;
            _transientAnimUntil = Time.time + LodTransientWindow;
            SetLodStatic(false);
        }
        if (_animator == null) return;
        try
        {
            if (_hasParams) _animator.SetTrigger("onAttack");
            else _animator.Play("Take Damage");
        }
        catch (System.Exception) { }
    }

    /// <summary>触发采集动作：挥臂砍劈（复用 onAttack → Hit 砍劈动画）。</summary>
    public void TriggerCollect()
    {
        TriggerAttack();
    }

    /// <summary>触发防御塔攻击表现（炮塔转向 + 后坐力 + 枪口特效），目标为世界坐标。</summary>
    public void TriggerTowerAttack(Vector3 targetWorldPos)
    {
        if (_towerVisual != null && _towerVisual.IsSetup)
            _towerVisual.Fire(targetWorldPos);
    }

    /// <summary>清除防御塔攻击表现（Seek 跳转后调用）。</summary>
    public void ResetTowerAttack()
    {
        if (_towerVisual != null)
            _towerVisual.ResetAttack();
    }

    /// <summary>触发死亡动画</summary>
    public void TriggerDeath()
    {
        // 远处静态野兽死亡时临时恢复骨骼动画，播放死亡动作后再随视图销毁
        if (_lodStatic && _skinned != null)
        {
            _transientAnimUntil = Time.time + 1.2f;
            SetLodStatic(false);
        }
        if (_animator == null) return;
        try
        {
            if (_hasParams) _animator.SetTrigger("onDeath");
            else _animator.Play("Die");
        }
        catch (System.Exception) { }
    }

    /// <summary>所有单位共享同一份血条材质（开启实例化 → 上百血条 Cube 合成一次实例化批，避免每体独立材质造成大量 DrawCall）。</summary>
    static Material s_hpFillMat;
    static Material GetSharedHpFillMat()
    {
        if (s_hpFillMat == null)
        {
            // Standard shader 确保 MPB 变色和 3D 光照正常
            s_hpFillMat = new Material(Shader.Find("Standard"));
            s_hpFillMat.color = new Color(0.267f, 0.925f, 0.435f);
            s_hpFillMat.SetFloat("_Metallic", 0f);
            s_hpFillMat.SetFloat("_Glossiness", 0.2f);
            s_hpFillMat.enableInstancing = true;
        }
        return s_hpFillMat;
    }

    Transform CreateHpCube(Transform parent, string name, Vector3 size, Color color, Mesh cubeMesh)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.transform.localScale = size;
        go.GetComponent<MeshFilter>().sharedMesh = cubeMesh;
        var rend = go.GetComponent<MeshRenderer>();
        rend.sharedMaterial = GetSharedHpFillMat();
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        return go.transform;
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

        // ── 动画状态同步 ──
        if (_animator != null)
        {
            try
            {
                // 暂停时冻结动画（ReplayPlayer 引用全局缓存，避免大量单位各自 FindObjectOfType）
                if (_player == null)
                {
                    if (s_cachedPlayer == null) s_cachedPlayer = FindObjectOfType<ReplayPlayer>();
                    _player = s_cachedPlayer;
                }
                bool replayPlaying = _player == null || _player.playing;
                float targetAnimSpeed;
                if (!replayPlaying)
                {
                    targetAnimSpeed = 0f;
                }
                else if (isMovingNow)
                {
                    float realSpeed = posChanged ? moveDir.magnitude / Time.deltaTime : 0f;
                    targetAnimSpeed = Mathf.Clamp(realSpeed * strideCoefficient, 0.15f, 4.5f) * AnimatorSpeed;
                }
                else
                {
                    targetAnimSpeed = AnimatorSpeed;
                }

                // 仅在目标速度变化时写入，静止单位不再每帧赋值 Animator.speed
                if (targetAnimSpeed != _animSpeed)
                {
                    _animSpeed = targetAnimSpeed;
                    _animator.speed = targetAnimSpeed;
                }

                if (_hasParams && isMovingNow != _wasMoving)
                {
                    _wasMoving = isMovingNow;
                    _animator.SetBool("isMoving", isMovingNow);
                }
            }
            catch (System.Exception) { }
        }

        // ── 野兽距离 LOD：远处降级为静态烘焙网格（省 Animator CPU + 蒙皮 GPU） ──
        if (_skinned != null && _animator != null)
        {
            if (s_camera == null) s_camera = Camera.main;
            if (s_camera != null)
            {
                // 用相机 XZ 水平距离（相机固定高度不参与，平移/缩放时响应自然）
                Vector3 camPos = s_camera.transform.position;
                Vector3 delta = new Vector3(camPos.x - transform.position.x, 0f, camPos.z - transform.position.z);
                float d2 = delta.sqrMagnitude;
                // 滞回区间：静态化用 LOD_RANGE，恢复动画用 0.85*LOD_RANGE，避免边界来回切换闪烁
                bool far = _lodStatic
                    ? d2 >= LOD_RANGE * 0.85f * LOD_RANGE * 0.85f
                    : d2 >= LOD_RANGE * LOD_RANGE;
                // 攻击/死亡瞬态窗口内保持动画（远处野兽攻击时也能看到动作，窗口结束自动回静态）
                if (far && Time.time < _transientAnimUntil) far = false;
                if (far != _lodStatic) SetLodStatic(far);

                // 远处静态机器人轻微待机浮动：呼吸式上下浮动 + 缩放摆动，避免死板雕像。
                // 每只相位按 id 错开，视觉更自然；暂停时冻结。成本 ≈ 每只 2 次 Sin，可忽略。
                if (_lodStatic && _lodGo != null)
                {
                    bool replayPlaying = _player == null || _player.playing;
                    if (replayPlaying)
                    {
                        float ph = (float)(state.id % 997) * 0.618f;   // 每只错开相位
                        float t = Time.time % 100f;                    // 包裹避免大数精度问题
                        float bob = Mathf.Sin(t * 2.4f + ph) * LodIdleBobAmplitude;
                        _lodGo.transform.localPosition = new Vector3(0f, bob, 0f);
                        float s = 1f + Mathf.Sin(t * 1.8f + ph * 1.3f) * LodIdleSwayAmplitude;
                        _lodGo.transform.localScale = new Vector3(_lodBaseScale.x * s, _lodBaseScale.y * s, _lodBaseScale.z * s);
                    }
                    else
                    {
                        _lodGo.transform.localPosition = Vector3.zero;
                        _lodGo.transform.localScale = _lodBaseScale;
                    }
                }
            }
        }
    }

    /// <summary>野兽距离 LOD 切换：静态态 = 禁用 Animator + 蒙皮，改渲共享烘焙网格（GPU 实例化）。</summary>
    void SetLodStatic(bool toStatic)
    {
        _lodStatic = toStatic;
        if (toStatic)
        {
            // 共享材质开启实例化（幂等；蒙皮渲染器不受影响，仍正常渲染）
            var mat = _skinned.sharedMaterial;
            if (mat != null) mat.enableInstancing = true;

            // 共享网格：每野兽类型只烘焙一次（姿势取第一只进入远处状态的当时的姿势）
            Mesh sharedMesh;
            if (!s_lodMeshCache.TryGetValue(state.type, out sharedMesh) || sharedMesh == null)
            {
                sharedMesh = new Mesh();
                _skinned.BakeMesh(sharedMesh);
                s_lodMeshCache[state.type] = sharedMesh;
            }

            if (_lodGo == null)
            {
                _lodGo = new GameObject("LodMesh");
                // 挂在 Robot 同一 transform 下、零偏移
                _lodGo.transform.SetParent(_skinned.transform, false);
                var mf = _lodGo.AddComponent<MeshFilter>();
                mf.sharedMesh = sharedMesh;
                var mr = _lodGo.AddComponent<MeshRenderer>();
                mr.sharedMaterials = _skinned.sharedMaterials;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            // BakeMesh 烘焙在「除以渲染器 lossyScale」的世界比例空间：必须把 LOD 渲染器 lossyScale 补偿回 1，
            // 否则在野兽根节点缩放(0.4)下会渲染得比骨骼版小 1/0.4≈2.5 倍（机器人变小的 bug）。
            // 注意：不能除以 state.animScale —— 野兽的 "Body" 节点是空节点、不在 Robot 变换链里，
            // animScale(出生缩放 0→1) 不影响 Robot.lossyScale；若在出生瞬间转静态会被过度补偿成极小网格（远处隐形的 bug）。
            var lossy = _skinned.transform.lossyScale;
            _lodGo.transform.localScale = new Vector3(
                lossy.x > 0.0001f ? 1f / lossy.x : 1f,
                lossy.y > 0.0001f ? 1f / lossy.y : 1f,
                lossy.z > 0.0001f ? 1f / lossy.z : 1f);
            _lodBaseScale = _lodGo.transform.localScale;
            _lodGo.SetActive(true);
            _skinned.enabled = false;
            _animator.enabled = false;
        }
        else
        {
            _skinned.enabled = true;
            _animator.enabled = true;
            if (_lodGo != null) _lodGo.SetActive(false);
        }
    }

    public void SetAnimScale(float s) { state.animScale = s; }

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
