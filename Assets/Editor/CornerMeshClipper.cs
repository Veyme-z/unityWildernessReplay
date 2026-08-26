using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// 网格裁剪工具：沿 XZ 平面一条直线裁掉网格的一部分（顶点+三角形在边界处切开，UV/法线/切线按 t 插值）。
/// 菜单 Tools → WildernessReplay → Trim Corner Piece（打开窗口）。
/// 两种裁剪对象：
///   1. 转角件（wall_corner_B_outside）：裁弧线，另存为独立 prefab。
///   2. 直墙（wall_straight）：沿长轴裁短，直接更新 WallCorner.prefab 里的 Wall(2) 节点。
/// 裁剪线：normal = (cos a, 0, sin a)，保留 dot(normal, p) <= offset 一侧。
/// </summary>
public class CornerMeshClipper : EditorWindow
{
    enum ClipTarget { Corner, StraightWall }
    ClipTarget _target = ClipTarget.StraightWall; // 默认直墙（当前在调 Wall(2)）

    const string CORNER_SRC =
        "Assets/KayKit_Medieval_Hexagon_Pack_1.0_FREE/Assets/fbx(unity)/Prefabs/wall_corner_B_outside.prefab";
    const string CORNER_OUT =
        "Assets/KayKit_Medieval_Hexagon_Pack_1.0_FREE/Assets/fbx(unity)/Prefabs/wall_corner_B_outside_trimmed.prefab";
    const string STRAIGHT_SRC =
        "Assets/KayKit_Medieval_Hexagon_Pack_1.0_FREE/Assets/fbx(unity)/Prefabs/wall_straight.prefab";
    const string STRAIGHT_MESH_OUT =
        "Assets/Resources/Prefabs/Buildings/WallCorner_straight_shortened.asset";
    const string WALL_CORNER_PREFAB =
        "Assets/Resources/Prefabs/Buildings/WallCorner.prefab";

    float _angle = 0f;     // 裁剪线法线在 XZ 平面与 X 轴的夹角（度）。直墙沿长轴裁：0°切+X端 / 180°切-X端
    float _offset = 0.5f;  // 裁剪深度：越小裁得越多（保留 dot<=offset 一侧）。0.5 = 明显裁掉一截，好观察

    [MenuItem("Tools/WildernessReplay/Trim Corner Piece")]
    static void Open() { GetWindow<CornerMeshClipper>("Trim Corner Piece"); }

    string SrcPath { get { return _target == ClipTarget.Corner ? CORNER_SRC : STRAIGHT_SRC; } }

    void OnGUI()
    {
        _target = (ClipTarget)EditorGUILayout.EnumPopup("裁剪对象", _target);
        GUILayout.Space(4);
        if (_target == ClipTarget.Corner)
        {
            GUILayout.Label("裁剪 wall_corner_B_outside（转角件弧线）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("直线切刀裁掉转角件弧线凸出部分。0°=切+X侧, 90°=切+Z侧, 45°=切斜对角。", MessageType.Info);
        }
        else
        {
            GUILayout.Label("裁剪 wall_straight（缩短 Wall(2) 那段直墙）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("直线切刀沿墙长轴裁短：0°=切 +X 端, 180°=切 -X 端（墙长轴是 X）。深度越小切越多。", MessageType.Info);
        }
        GUILayout.Space(8);
        EditorGUILayout.LabelField("裁剪方向（快捷）");
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("切 +X 0°")) _angle = 0f;
        if (GUILayout.Button("切 +Z 90°")) _angle = 90f;
        if (GUILayout.Button("切 -X 180°")) _angle = 180f;
        if (GUILayout.Button("切 -Z 270°")) _angle = 270f;
        EditorGUILayout.EndHorizontal();
        _angle = EditorGUILayout.Slider("裁剪角度 °（0~360）", _angle, 0f, 360f);
        _offset = EditorGUILayout.FloatField("裁剪深度（越小切越多）", _offset);
        GUILayout.Space(8);
        if (GUILayout.Button("预览")) Preview();
        if (GUILayout.Button("保存")) Save();
    }

    void Preview()
    {
        var mesh = ClipCopy();
        if (mesh == null) return;
        Debug.Log("[CornerMeshClipper] " + (_target == ClipTarget.Corner ? "转角" : "直墙")
            + " 裁剪：verts=" + mesh.vertexCount + " tris=" + (mesh.triangles.Length / 3)
            + " bounds=" + mesh.bounds.size);

        // 直墙模式：把黄色裁剪件放到场景里 Wall(2) 的原位置/旋转/缩放，覆盖对比
        Transform anchor = _target == ClipTarget.StraightWall ? FindWall2InScene() : FindSourceInScene();
        var go = new GameObject("_clip_preview");
        if (anchor != null)
        {
            go.transform.position = anchor.position + Vector3.up * 0.15f;
            go.transform.rotation = anchor.rotation;
            go.transform.localScale = anchor.lossyScale;
        }
        var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = mesh;
        var mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(1f, 0.85f, 0.2f, 0.6f); // 黄色半透明
        mat.SetFloat("_Mode", 3f);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.renderQueue = 3000;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        Selection.activeGameObject = go;
        SceneView.lastActiveSceneView?.FrameSelected();
    }

    /// <summary>在场景里找 WallCorner 实例里的 Wall(2) 节点（用于直墙预览定位）。</summary>
    static Transform FindWall2InScene()
    {
        var all = Object.FindObjectsOfType<GameObject>();
        foreach (var go in all)
        {
            if (!go.scene.IsValid()) continue;
            if (go.name == "Wall (2)" && go.transform.Find("Model") != null)
                return go.transform;
        }
        return null;
    }

    /// <summary>在场景里找已摆好的源转角件（名字含 wall_corner_B_outside），用于转角件预览定位。</summary>
    static Transform FindSourceInScene()
    {
        var all = Object.FindObjectsOfType<GameObject>();
        foreach (var go in all)
            if (go.scene.IsValid() && go.name.IndexOf("wall_corner_B_outside", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return go.transform;
        return null;
    }

    void Save()
    {
        if (_target == ClipTarget.Corner) { SaveCorner(); return; }
        SaveStraight();
    }

    // ── 转角件：另存独立 prefab ──
    void SaveCorner()
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(CORNER_SRC);
        if (src == null) { Debug.LogError("找不到源 prefab：" + CORNER_SRC); return; }
        var mesh = ClipCopy();
        if (mesh == null) return;

        string meshAssetPath = System.IO.Path.ChangeExtension(CORNER_OUT, ".asset");
        var savedMesh = OverwriteOrCreateMeshAsset(mesh, meshAssetPath);
        if (savedMesh == null) return;

        var srcRend = src.GetComponentInChildren<MeshRenderer>();
        var srcMat = srcRend != null && srcRend.sharedMaterials.Length > 0 ? srcRend.sharedMaterials[0] : null;
        var root = new GameObject("wall_corner_B_outside_trimmed");
        root.AddComponent<MeshFilter>().sharedMesh = savedMesh;
        var mr = root.AddComponent<MeshRenderer>();
        if (srcMat != null) mr.sharedMaterial = srcMat;
        var mc = root.AddComponent<MeshCollider>();
        mc.sharedMesh = savedMesh;

        bool ok = PrefabUtility.SaveAsPrefabAsset(root, CORNER_OUT);
        Object.DestroyImmediate(root);
        Debug.Log("[CornerMeshClipper] " + (ok ? "转角件已保存：" + CORNER_OUT : "保存失败"));
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    // ── 直墙：裁短并直接更新 WallCorner.prefab 里的 Wall(2) ──
    void SaveStraight()
    {
        var mesh = ClipCopy();
        if (mesh == null) return;
        var savedMesh = OverwriteOrCreateMeshAsset(mesh, STRAIGHT_MESH_OUT);
        if (savedMesh == null) return;

        var contentsRoot = PrefabUtility.LoadPrefabContents(WALL_CORNER_PREFAB);
        var wall2 = contentsRoot.transform.Find("Wall (2)");
        if (wall2 == null)
        {
            // 回退：找有 Model 且绕 Y≈270°（竖直）的 Wall 子节点
            for (int i = 0; i < contentsRoot.transform.childCount; i++)
            {
                var c = contentsRoot.transform.GetChild(i);
                if (c.name.StartsWith("Wall") && c.Find("Model") != null
                    && Mathf.Abs(Mathf.DeltaAngle(c.localEulerAngles.y, 270f)) < 1f)
                { wall2 = c; break; }
            }
        }
        if (wall2 == null)
        {
            Debug.LogError("WallCorner.prefab 里找不到 Wall(2) 节点");
            PrefabUtility.UnloadPrefabContents(contentsRoot);
            return;
        }
        var model = wall2.Find("Model");
        var mf = model != null ? model.GetComponent<MeshFilter>() : null;
        if (mf == null)
        {
            Debug.LogError("Wall(2) 没有 Model/MeshFilter");
            PrefabUtility.UnloadPrefabContents(contentsRoot);
            return;
        }
        mf.sharedMesh = savedMesh;
        bool ok = PrefabUtility.SaveAsPrefabAsset(contentsRoot, WALL_CORNER_PREFAB);
        PrefabUtility.UnloadPrefabContents(contentsRoot);
        Debug.Log("[CornerMeshClipper] " + (ok ? "Wall(2) 网格已更新为裁剪版：" + STRAIGHT_MESH_OUT : "保存失败"));
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    /// <summary>覆盖或新建网格资产（保持 GUID 稳定）。</summary>
    static Mesh OverwriteOrCreateMeshAsset(Mesh mesh, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }
        existing.Clear();
        existing.vertices = mesh.vertices;
        existing.triangles = mesh.triangles;
        existing.uv = mesh.uv;
        existing.normals = mesh.normals;
        existing.tangents = mesh.tangents;
        existing.RecalculateBounds();
        EditorUtility.SetDirty(existing);
        AssetDatabase.SaveAssets();
        return existing;
    }

    /// <summary>按当前参数裁一份网格副本（源网格不动）。</summary>
    Mesh ClipCopy()
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(SrcPath);
        if (src == null) { Debug.LogError("找不到源 prefab：" + SrcPath); return null; }
        var srcMf = src.GetComponentInChildren<MeshFilter>();
        if (srcMf == null || srcMf.sharedMesh == null) { Debug.LogError("源 prefab 没有可读网格"); return null; }
        var srcMesh = srcMf.sharedMesh;
        var mesh = new Mesh();
        mesh.name = (_target == ClipTarget.Corner ? "wall_corner_B_outside_trimmed" : "wall_straight_shortened");
        mesh.vertices = srcMesh.vertices;
        mesh.triangles = srcMesh.triangles;
        mesh.normals = srcMesh.normals;
        mesh.uv = srcMesh.uv;
        mesh.tangents = srcMesh.tangents;
        ClipMesh(mesh, _angle * Mathf.Deg2Rad, _offset);
        // 直墙：裁剪后把 UV(U=长度轴)重新归一化到原始范围，让完整贴图映射到缩短后的墙（贴图不变形、和裁剪前对应）
        if (_target == ClipTarget.StraightWall)
            ReNormalizeLengthUV(mesh, srcMesh);
        return mesh;
    }

    /// <summary>
    /// 平面裁剪：保留 dot(normal, p) <= offset 的一侧，三角形在边界处切开。
    /// 关键：切割点的新顶点按 t 插值 UV/法线/切线，否则贴图会错乱。
    /// </summary>
    static void ClipMesh(Mesh mesh, float rad, float offset)
    {
        Vector3 n = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
        var verts = mesh.vertices;
        var uvs = mesh.uv;
        var norms = mesh.normals;
        var tans = mesh.tangents;
        var tris = mesh.triangles;
        var nv = new List<Vector3>();
        var nuv = new List<Vector2>();
        var nn = new List<Vector3>();
        var ntan = new List<Vector4>();
        var nt = new List<int>();

        for (int i = 0; i < tris.Length; i += 3)
        {
            var idx = new int[] { tris[i], tris[i + 1], tris[i + 2] };
            var pP = new List<Vector3>(); var pU = new List<Vector2>();
            var pN = new List<Vector3>(); var pT = new List<Vector4>();
            for (int k = 0; k < 3; k++)
            {
                int ia = idx[k], ib = idx[(k + 1) % 3];
                float da = Vector3.Dot(n, verts[ia]) - offset;
                float db = Vector3.Dot(n, verts[ib]) - offset;
                bool ka = da <= 0f, kb = db <= 0f;
                if (ka) { pP.Add(verts[ia]); pU.Add(uvs[ia]); pN.Add(norms[ia]); pT.Add(tans[ia]); }
                if (ka != kb)
                {
                    float t = da / (da - db);
                    pP.Add(Vector3.Lerp(verts[ia], verts[ib], t));
                    pU.Add(Vector2.Lerp(uvs[ia], uvs[ib], t));
                    pN.Add(Vector3.Lerp(norms[ia], norms[ib], t).normalized);
                    pT.Add(Vector4.Lerp(tans[ia], tans[ib], t));
                }
            }
            if (pP.Count < 3) continue;
            int baseIdx = nv.Count;
            for (int k = 0; k < pP.Count; k++) { nv.Add(pP[k]); nuv.Add(pU[k]); nn.Add(pN[k]); ntan.Add(pT[k]); }
            for (int k = 1; k + 1 < pP.Count; k++)
            {
                nt.Add(baseIdx);
                nt.Add(baseIdx + k);
                nt.Add(baseIdx + k + 1);
            }
        }

        mesh.Clear();
        mesh.vertices = nv.ToArray();
        mesh.triangles = nt.ToArray();
        mesh.uv = nuv.ToArray();
        mesh.normals = nn.ToArray();
        mesh.tangents = ntan.ToArray();
        mesh.RecalculateBounds();
    }

    /// <summary>把裁剪后网格的 U（长度轴）重新归一化到源网格的 U 范围，让完整贴图映射到缩短后的墙。</summary>
    static void ReNormalizeLengthUV(Mesh mesh, Mesh srcMesh)
    {
        var uv = mesh.uv;
        var srcUv = srcMesh.uv;
        if (uv == null || uv.Length == 0 || srcUv == null || srcUv.Length == 0) return;
        float minU = float.MaxValue, maxU = float.MinValue, srcMin = float.MaxValue, srcMax = float.MinValue;
        for (int i = 0; i < uv.Length; i++) { if (uv[i].x < minU) minU = uv[i].x; if (uv[i].x > maxU) maxU = uv[i].x; }
        for (int i = 0; i < srcUv.Length; i++) { if (srcUv[i].x < srcMin) srcMin = srcUv[i].x; if (srcUv[i].x > srcMax) srcMax = srcUv[i].x; }
        if (maxU - minU < 0.0001f) return;
        for (int i = 0; i < uv.Length; i++)
        {
            float u = uv[i].x;
            uv[i].x = srcMin + (u - minU) * (srcMax - srcMin) / (maxU - minU);
        }
        mesh.uv = uv;
    }
}
