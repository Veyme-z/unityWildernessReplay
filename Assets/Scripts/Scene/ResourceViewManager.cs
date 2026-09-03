using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 资源矿点可视化 v3：KayKit Rock_1_A FBX 直加载 + 物理 .mat 材质。
/// 矿石模型来自 SceneBuilder.OreRockModel（AssetDatabase 预加载的 FBX 引用）。
/// </summary>
public class ResourceViewManager
{
    static readonly Dictionary<string, string> ORE_MAT = new Dictionary<string, string>
    {
        { "石头", "Materials/Mat_Ore_Stone" },
        { "铁",   "Materials/Mat_Ore_Iron" },
        { "铜",   "Materials/Mat_Ore_Copper" },
    };

    readonly Dictionary<string, GameObject> _active = new Dictionary<string, GameObject>();
    readonly Stack<GameObject> _pool = new Stack<GameObject>();
    readonly Transform _root;
    readonly StateEngine _engine;

    public ResourceViewManager(Transform parent, StateEngine engine)
    {
        _root = new GameObject("Resources").transform;
        _root.SetParent(parent);
        _engine = engine;
    }

    public void ApplyFrame(List<ReplayResource> resources)
    {
        var seen = new HashSet<string>();

        foreach (var res in resources)
        {
            if (res.resNum < 0) continue;

            string key = res.x + "," + res.y;
            seen.Add(key);

            if (_active.TryGetValue(key, out var go))
            {
                UpdateLabel(go, res.resNum);
                continue;
            }

            go = GetOrCreate(res);
            _active[key] = go;
        }

        var toRemove = new List<string>();
        foreach (var kv in _active)
        {
            if (!seen.Contains(kv.Key))
                toRemove.Add(kv.Key);
        }
        foreach (var key in toRemove)
        {
            var go = _active[key];
            go.SetActive(false);
            _pool.Push(go);
            _active.Remove(key);
        }
    }

    GameObject GetOrCreate(ReplayResource res)
    {
        GameObject go;
        if (_pool.Count > 0)
        {
            go = _pool.Pop();
            go.SetActive(true);
        }
        else
        {
            go = new GameObject("Ore_" + res.resName);
            go.transform.SetParent(_root);

            // 矿石模型：KayKit Rock_1_A FBX 直加载（非 Prefab）
            var rockSource = SceneBuilder.OreRockModel;
            if (rockSource != null)
            {
                var model = Object.Instantiate(rockSource, go.transform);
                model.name = "Model";
                model.transform.localPosition = Vector3.zero;
                // FBX 原生 Z-up，X=-90 修正为 Y-up；保持原始 localScale(100,100,100)
                model.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

                // 移除碰撞体
                var cols = model.GetComponentsInChildren<Collider>();
                foreach (var c in cols) Object.Destroy(c);

                // 应用物理 .mat 材质
                string matPath;
                if (ORE_MAT.TryGetValue(res.resName, out matPath))
                {
                    var oreMat = Resources.Load<Material>(matPath);
                    if (oreMat != null)
                    {
                        var rends = model.GetComponentsInChildren<Renderer>();
                        foreach (var r in rends) r.sharedMaterial = oreMat;
                    }
                }
            }
            else
            {
                // Fallback: 有色球体
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = "Model_Fallback";
                sphere.transform.SetParent(go.transform, false);
                sphere.transform.localScale = Vector3.one;
                var col = sphere.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);
                string matPath;
                if (ORE_MAT.TryGetValue(res.resName, out matPath))
                {
                    var oreMat = Resources.Load<Material>(matPath);
                    if (oreMat != null) sphere.GetComponent<Renderer>().sharedMaterial = oreMat;
                }
            }

            // 数量标签
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            labelGo.transform.localPosition = new Vector3(0, 0.55f, 0);
            var tm = labelGo.AddComponent<TextMesh>();
            tm.fontSize = 100;
            tm.characterSize = 0.06f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
            tm.font = FxFactory.BuiltinFont();
            // 动态字体材质不会自动挂到 MeshRenderer → 否则字形渲染成白色豆腐块/隐形（WebGL 尤其）。
            var lmr = tm.GetComponent<MeshRenderer>();
            if (lmr != null && tm.font != null && tm.font.material != null)
                lmr.sharedMaterial = tm.font.material;
            labelGo.AddComponent<Billboard>();
        }

        // 位置
        var wp = _engine.CellToWorld(res.x, res.y);
        go.transform.position = wp + new Vector3(0, 0.15f, 0);

        // FBX scale=100 时宽 ~0.58m；容器 scale=1.7 → 约 1m 占满一格
        float seed = (res.x * 7 + res.y * 13) % 100 / 100f;
        float s = 1.50f + seed * 0.40f;
        go.transform.localScale = new Vector3(s, s * 0.70f, s);

        // 仅 Y 轴旋转, X/Z 锁死
        go.transform.rotation = Quaternion.Euler(0f, seed * 360f, 0f);

        UpdateLabel(go, res.resNum);
        return go;
    }

    void UpdateLabel(GameObject go, int num)
    {
        var tm = go.GetComponentInChildren<TextMesh>();
        if (tm == null) return;
        tm.text = num > 0 ? num.ToString() : "";
        // WebGL: TextMesh 换字后需请求字形并同步 font.material，否则白块/隐形
        if (tm.font != null)
        {
            tm.font.RequestCharactersInTexture(tm.text, tm.fontSize, tm.fontStyle);
            var mr = tm.GetComponent<MeshRenderer>();
            if (mr != null && tm.font.material != null) mr.sharedMaterial = tm.font.material;
        }
    }

    public void Clear()
    {
        foreach (var kv in _active)
        {
            kv.Value.SetActive(false);
            _pool.Push(kv.Value);
        }
        _active.Clear();
    }
}
