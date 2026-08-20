// UnitView 的血条子模块（Partial Class）
// 职责：3D 血条创建/更新/变色/共享材质、阵营光环、塔 HP 定位辅助
// 字段声明与主流程见 UnitView.cs

using UnityEngine;

public partial class UnitView
{
    /// <summary>防御塔(3)血条顶部安全偏移：在 VisualHeight() 塔顶高度之上再抬高，确保清晰悬浮在炮塔正上方。</summary>
    const float TOWER_HP_TOP_PADDING = 0.9f;

    static Material s_hpFillMat;

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
