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
    float _hpY, _hpW;
    MeshRenderer _hpFillRend;
    MaterialPropertyBlock _mpb;
    Animator _animator;
    ReplayPlayer _player;
    bool _hasParams = true;

    // ── 平滑转向 ──
    Vector3 _prevPos;
    float _prevAnimScale = 1f;
    bool _wasMoving;
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

    /// <summary>从 Beast Prefab 实例化后配置引用（模型已在 Prefab 中）。</summary>
    void ConfigureFromBeastPrefab()
    {
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

        var pv = GetComponentInChildren<Pickable>();
        if (pv != null) pv.view = this;

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

    /// <summary>将 Prefab 中扁平的 Quad 血条在运行时升级为 3D Cube 网格</summary>
    void UpgradeHpTo3D()
    {
        var cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        float modelH = _body != null ? EstimateHeight(_body.gameObject) : 2f;
        float modelW = _body != null ? EstimateWidth(_body.gameObject) : 0.5f;
        _hpW = Mathf.Max(modelW, 0.3f);
        // 基地/塔在模型顶部，开拓者 0.65，其余 0.55
        if (state.type == 4) { _hpY = modelH + 2f; _hpW *= 1.6f; }
        else if (state.type == 3) { _hpY = modelH + 0.5f; _hpW *= 1.6f; }
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
            _hpFill = CreateHpCube(transform, "HpFill", new Vector3(_hpW, 0.05f, 0.02f), new Color(0.267f, 0.925f, 0.435f), cubeMesh);
            _hpFillRend = _hpFill.GetComponent<MeshRenderer>();
        }
        else
        {
            var bb = _hpFill.GetComponent<Billboard>();
            if (bb != null) Destroy(bb);
            _hpFill.GetComponent<MeshFilter>().sharedMesh = cubeMesh;
            _hpFill.localScale = new Vector3(_hpW, 0.05f, 0.02f);
            if (_hpFillRend == null) _hpFillRend = _hpFill.GetComponent<MeshRenderer>();
            if (_hpFillRend != null && (_hpFillRend.sharedMaterial.name.Contains("Default") || _hpFillRend.sharedMaterial.shader.name != "Standard"))
                _hpFillRend.sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.267f, 0.925f, 0.435f) };
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
        bool hasAny = false;
        for (int i = 0; i < rs.Length; i++)
        {
            combined.Encapsulate(rs[i].bounds);
            float w = Mathf.Max(rs[i].bounds.size.x, rs[i].bounds.size.z);
            if (w > maxW) maxW = w;
            hasAny = true;
        }
        if (maxW > 0.01f) _baseScale = targetWidth / maxW;
        // 基地模型 pivot 偏移修正
        if (state.type == 4)
            _pivotOffset = new Vector3(0f, 0f, state.teamType == "defender" ? 1.0f : 1.92f);
    }

    /// <summary>更新动画状态（外部调用 — 仅负责 Trigger，isMoving 由 LateUpdate 统一管理）</summary>
    public void UpdateAnimation(bool isMoving, bool isDead)
    {
        if (_animator == null) return;
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
        if (_animator == null) return;
        try
        {
            if (_hasParams) _animator.SetTrigger("onAttack");
            else _animator.Play("Take Damage");
        }
        catch (System.Exception) { }
    }

    /// <summary>触发死亡动画</summary>
    public void TriggerDeath()
    {
        if (_animator == null) return;
        try
        {
            if (_hasParams) _animator.SetTrigger("onDeath");
            else _animator.Play("Die");
        }
        catch (System.Exception) { }
    }

    Transform CreateHpCube(Transform parent, string name, Vector3 size, Color color, Mesh cubeMesh)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.transform.localScale = size;
        go.GetComponent<MeshFilter>().sharedMesh = cubeMesh;
        var rend = go.GetComponent<MeshRenderer>();
        // Standard shader 确保 MPB 变色和 3D 光照正常
        var mat = new Material(Shader.Find("Standard"));
        mat.color = color;
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Glossiness", 0.2f);
        rend.sharedMaterial = mat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        return go.transform;
    }
    // ---------- 每帧刷新 ----------
    void LateUpdate()
    {
        if (state == null) return;

        // ── 位置 ──
        Vector3 newPos = state.pos;
        Vector3 moveDir = newPos - _prevPos;
        _prevPos = newPos;
        // Y=0.01f 贴地，X/Z 用 pivot 偏移修正使模型底面中心对齐格子中心
        transform.position = new Vector3(state.pos.x - _pivotOffset.x, 0.01f, state.pos.z - _pivotOffset.z);

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

        // ── 静止冻结：消除暂停时的抖动 ──
        if (!isMovingNow && _body != null)
        {
            _body.localPosition = Vector3.zero;
            if (!state.stun) _body.localRotation = Quaternion.identity;
        }

        // ── 动画状态同步 ──
        if (_animator != null)
        {
            try
            {
                // 暂停时冻结动画
                if (_player == null) _player = FindObjectOfType<ReplayPlayer>();
                bool replayPlaying = _player?.playing ?? true;
                if (!replayPlaying)
                {
                    _animator.speed = 0f;
                }
                else if (isMovingNow)
                {
                    float realSpeed = moveDir.magnitude / Time.deltaTime;
                    float targetAnimSpeed = realSpeed * strideCoefficient;
                    _animator.speed = Mathf.Clamp(targetAnimSpeed, 0.15f, 4.5f) * AnimatorSpeed;
                }
                else
                {
                    _animator.speed = AnimatorSpeed;
                }

                if (_hasParams && isMovingNow != _wasMoving)
                {
                    _wasMoving = isMovingNow;
                    _animator.SetBool("isMoving", isMovingNow);
                }
            }
            catch (System.Exception) { }
        }
    }

    public void SetAnimScale(float s) { state.animScale = s; }

    public void SetHp(int hp, int maxHp)
    {
        if (_hpFill == null || _hpFillRend == null) return;
        float pct = Mathf.Clamp01((float)hp / Mathf.Max(1, maxHp));
        float fillW = _hpW;
        _hpFill.localScale = new Vector3(fillW * pct, 0.05f, 0.02f);
        _hpFill.localPosition = new Vector3(-fillW * 0.5f * (1f - pct), _hpY, 0);
        Color c = pct > 0.6f ? new Color(0.267f, 0.925f, 0.435f)
              : pct > 0.3f ? new Color(1f, 0.788f, 0.302f)
              : new Color(1f, 0.231f, 0.188f);
        _mpb.SetColor("_Color", c);
        _hpFillRend.SetPropertyBlock(_mpb);
    }


    public void SetStun(bool stun)
    {
        if (_body != null)
            _body.localRotation = stun ? Quaternion.Euler(0, 0, 90) : Quaternion.identity;
    }

}
