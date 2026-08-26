// UnitView 的血条子模块（Partial Class）
// 职责：3D 血条创建/更新/变色/共享材质、阵营光环、塔 HP 定位辅助
// 字段声明与主流程见 UnitView.cs

using System.Collections.Generic;
using UnityEngine;

public partial class UnitView
{
    /// <summary>防御塔(3)血条顶部安全偏移：在 VisualHeight() 塔顶高度之上再抬高，确保清晰悬浮在炮塔正上方。</summary>
    const float TOWER_HP_TOP_PADDING = 1.1f;

    // ── 血条颜色（按阵营/类型恒定，不随血量百分比变色）──
    static readonly Color HP_COLOR_ROBOT      = new Color(1f, 0.788f, 0.302f);    // #FFC94D 机器人黄
    static readonly Color HP_COLOR_DEFENDER   = new Color(1f, 0.176f, 0.333f);    // #FF2D55 红方
    static readonly Color HP_COLOR_CHALLENGER = new Color(0f, 0.478f, 1f);        // #007AFF 蓝方
    static readonly Color HP_COLOR_NEUTRAL    = new Color(0.267f, 0.925f, 0.435f); // #44EC6F 中立绿

    /// <summary>血条颜色：机器人(11-14 野兽)统一黄色与红方区分；defender 红色；challenger 蓝色；中立单位绿色。</summary>
    Color GetHpColor()
    {
        if (state != null && state.IsBeast) return HP_COLOR_ROBOT;
        if (state != null && state.teamType == "defender") return HP_COLOR_DEFENDER;
        if (state != null && state.teamType == "challenger") return HP_COLOR_CHALLENGER;
        return HP_COLOR_NEUTRAL;
    }

    // ── 血条外观配置（按单位类型），集中管理，避免分支散落魔法数字 ──
    struct HpBarStyle
    {
        public float yOffset;   // Y = modelH + yOffset（相对模型顶部偏移）
        public float yFactor;   // Y = modelH * yFactor（按模型高度比例；>0 时优先生效）
        public float widthMul;  // 宽度倍率（基于 max(modelW, 0.3)）
        public float thick;     // 厚度（Y 方向）
        public float depth;     // 深度（Z 方向）；0 = 与厚度相同
        public float yShift;    // 额外恒定垂直偏移（世界单位），在 yFactor/yOffset 之后叠加（微调血条高低用）
    }

    static readonly Dictionary<int, HpBarStyle> HP_BAR_STYLES = new Dictionary<int, HpBarStyle>
    {
        { 3,  new HpBarStyle { yOffset = TOWER_HP_TOP_PADDING, widthMul = 1.28f, thick = 0.12f } },
        { 4,  new HpBarStyle { yOffset = 2.2f,        widthMul = 1.6f,  thick = 0.10f } },
        { 5,  new HpBarStyle { yFactor = 0.55f,       widthMul = 1f,    thick = 0.05f, depth = 0.025f, yShift = -0.5f } }, // 围墙：深度减半防过厚，血条下移 0.5
        { 7,  new HpBarStyle { yFactor = 0.65f,       widthMul = 1f,    thick = 0.05f } },
        // 野兽统一为 SciFi 模块化角色（等身高）：血条位于头顶上方、宽度适中（不再按旧模型 2.5× 放大）
        { 11, new HpBarStyle { yOffset = 0.35f,       widthMul = 0.9f,  thick = 0.08f } },
        { 12, new HpBarStyle { yOffset = 0.35f,       widthMul = 0.9f,  thick = 0.08f } },
        { 13, new HpBarStyle { yOffset = 0.35f,       widthMul = 1f,    thick = 0.08f } },
        { 14, new HpBarStyle { yOffset = 0.35f,       widthMul = 1f,    thick = 0.08f } },
    };

    static readonly HpBarStyle HP_BAR_DEFAULT = new HpBarStyle { yFactor = 0.55f, widthMul = 1f, thick = 0.05f };

    static Material s_hpFillMat;

    Transform _hpText;      // 血条上方血量数值（3D 文本）
    TextMesh _hpTextMesh;

    /// <summary>创建血条上方的血量数值（3D 文本，面朝相机）。</summary>
    void EnsureHpText()
    {
        if (_hpText != null) return;
        var go = new GameObject("HpText");
        go.transform.SetParent(transform, false);
        _hpText = go.transform;
        _hpTextMesh = go.AddComponent<TextMesh>();
        _hpTextMesh.font = UiFonts.Get();
        _hpTextMesh.fontSize = 120;
        _hpTextMesh.characterSize = 0.06f;
        _hpTextMesh.anchor = TextAnchor.MiddleCenter;
        _hpTextMesh.alignment = TextAlignment.Center;
        _hpTextMesh.color = Color.white;
        _hpTextMesh.text = "";
        go.AddComponent<Billboard>();
        _hpText.localPosition = new Vector3(0, _hpY + 0.12f, 0);
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

    /// <summary>估算 GameObject 的水平包围盒宽度（XZ 最大值，排除武器网格，避免枪械把血条撑得过长）</summary>
    float EstimateWidth(GameObject go)
    {
        var bounds = new Bounds(go.transform.position, Vector3.zero);
        bool hasRenderer = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (IsWeaponRenderer(r)) continue;
            bounds.Encapsulate(r.bounds);
            hasRenderer = true;
        }
        return hasRenderer ? Mathf.Max(bounds.size.x, bounds.size.z) : 0.5f;
    }

    /// <summary>渲染器是否为枪械武器节点（SciFi 步枪/手枪/霰弹/狙击/动态武器）</summary>
    static bool IsWeaponRenderer(Renderer r)
    {
        if (r == null || r.gameObject == null) return false;
        string n = r.gameObject.name;
        return n == "AssaultRifle" || n == "Pistol" || n == "Shotgun" || n == "SniperRifle"
               || n == "SciFiWeapon" || n.IndexOf("Rifle", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

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
            // 野兽体型按类型校准（_baseScale≈1.15~2），若按未缩放的原生尺寸定位，血条会被埋进模型（表现为"部分机器人头上没血条"）
            if (state != null && state.IsBeast) { modelH *= _baseScale; modelW *= _baseScale; }
        }
        _hpW = Mathf.Max(modelW, 0.3f);
        // 高度/宽度/厚度/深度按单位类型查配置表（见 HP_BAR_STYLES），未配置的走默认
        HpBarStyle st;
        if (!HP_BAR_STYLES.TryGetValue(state.type, out st)) st = HP_BAR_DEFAULT;
        _hpY = st.yFactor > 0f ? modelH * st.yFactor : modelH + st.yOffset;
        _hpY += st.yShift;
        _hpW *= st.widthMul;
        _hpThick = st.thick;
        _hpDepth = st.depth > 0f ? st.depth : st.thick;
        // 销毁旧黑底 HpBar
        var oldBar = transform.Find("HpBar");
        if (oldBar != null) Destroy(oldBar.gameObject);
        // 填充条
        if (_hpFill == null)
        {
            _hpFill = CreateHpCube(transform, "HpFill", new Vector3(_hpW, _hpThick, _hpDepth), new Color(0.267f, 0.925f, 0.435f), cubeMesh);
            _hpFillRend = _hpFill.GetComponent<MeshRenderer>();
        }
        else
        {
            var bb = _hpFill.GetComponent<Billboard>();
            if (bb != null) Destroy(bb);
            _hpFill.GetComponent<MeshFilter>().sharedMesh = cubeMesh;
            _hpFill.localScale = new Vector3(_hpW, _hpThick, _hpDepth);
            if (_hpFillRend == null) _hpFillRend = _hpFill.GetComponent<MeshRenderer>();
            if (_hpFillRend != null && (_hpFillRend.sharedMaterial.name.Contains("Default") || _hpFillRend.sharedMaterial.shader.name != "Standard"))
                _hpFillRend.sharedMaterial = GetSharedHpFillMat();
        }
        _hpFill.localPosition = new Vector3(0, _hpY, 0);
        _hpFill.localRotation = Quaternion.identity;
        // 血条默认始终面朝相机（角色移动/转身时不被侧向遮挡）。围墙(type 5)例外：不加 Billboard——
        // 血条由 LateUpdate 固定为世界水平朝向（与横墙一致），既不随城墙 Y 旋转、也不随相机晃动。
        if (state.type == 5)
        {
            var bb = _hpFill.GetComponent<Billboard>();
            if (bb != null) Destroy(bb);
        }
        else if (_hpFill.GetComponent<Billboard>() == null)
        {
            _hpFill.gameObject.AddComponent<Billboard>();
        }
        if (_hpFillRend == null) _hpFillRend = _hpFill.GetComponent<MeshRenderer>();
        if (_hpFillRend != null) { _hpFillRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; _hpFillRend.receiveShadows = false; }
        if (_mpb == null) _mpb = new MaterialPropertyBlock();

        // 血量数值文本
        EnsureHpText();
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

    /// <summary>所有单位共享同一份血条材质（开启实例化 → 上百血条 Cube 合成一次实例化批，避免每体独立材质造成大量 DrawCall）。</summary>
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
}
