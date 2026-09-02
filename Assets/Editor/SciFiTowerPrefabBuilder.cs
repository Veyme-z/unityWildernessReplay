#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 编辑器工具：用 SciFiStrategyLowPoly 的防御塔生成武器工事视觉包装 Prefab（按武器等级 1/2/3）。
///
/// 武器映射（与 TowerVisualController.ResolveTowerType 保持一致）：
///   roleType 30 加特林炮台   → Minigun（SciFi Minigun_{Lv}，原生 MinigunTracers 弹道粒子）
///   roleType 31 电磁狙击炮   → Laser（SciFi Laser_{Lv}，光束数 = 等级：Laser_1 单束 / Laser_2 双束 / Laser_3 三束）
///   roleType 32 火箭发射台   → Rocket（SciFi Rocket_{Lv}）
/// 输出 Tower_{Type}_{Lv}_{Faction}.prefab（3 类型 × 3 等级 × 2 阵营 = 18 个）。
/// 回放武器等级 1~5，4~5 级在 UnitView 里 clamp 到 _3 模型（素材包只有 1~3 级）。
///
/// 每座塔：
///   - 外壳材质按阵营染色（Main_Red/Blue，从共享 Main.mat 复制改低饱和 _Color）
///   - 非均匀缩放：XZ 占地固定、Y 拉高超过围墙
///   - 挂 TowerVisualController：炮塔枢轴 = Horizontal，枪口/火箭按塔类型接线（激光光束运行时按 LaserBeam* 前缀自动收集）
/// </summary>
public static class SciFiTowerPrefabBuilder
{
    const string CUBE_TOWERS_DIR = "Assets/Resources/Prefabs/Buildings/CubeTowers";
    const string SRC_DIR = "Assets/SciFiStrategyLowPoly/Prefabs/Towers";
    const string MAIN_MAT_PATH = "Assets/SciFiStrategyLowPoly/Materials/Main.mat";
    const string PROJECT_MATS_DIR = "Assets/ProjectAssets/SciFiStrategy_BuiltIn/Materials";

    // UnitView.CalibrateBaseScale 会给塔施加 ~0.7 常量缩放（量的是 Tower.prefab 自身宽度，与包装 prefab 无关），
    // 故世界实际尺寸 = 包装 prefab 本地尺寸 × 0.7。这里把目标世界尺寸 ÷ 0.7 换算成包装本地尺寸。
    const float UNIT_SCALE = 0.7f;
    const float TARGET_WORLD_FOOTPRINT = 0.85f;   // 世界占地直径（米，1m 格子内）
    const float TARGET_WORLD_HEIGHT = 1.4f;       // 世界高度（米，围墙 0.825 之上，明显高出）
    const float HORIZONTAL_SCALE = 1.5f;          // 炮塔（Horizontal 节点）水平放大倍数

    static readonly int[] LEVELS = { 1, 2, 3 };   // 素材包塔模型只有 1~3 级

    // 塔类型 → SciFi 源塔配置（炮塔枢轴 / 枪口节点 / 火箭发射口数组；激光光束由运行时按 LaserBeam* 前缀自动收集）
    static readonly Dictionary<string, TowerSrc> SOURCES = new Dictionary<string, TowerSrc>
    {
        // 枪口=Vertical（含原生 MinigunTracers 弹道粒子 + MinigunShell 弹壳，均关默认播放、开火时播）
        { "Minigun", new TowerSrc("Horizontal", "Vertical", null) },
        { "Laser",   new TowerSrc("Horizontal", null, null) },
        { "Rocket",  new TowerSrc("Horizontal", null, new[] { "Rocket1_LOC", "Rocket2_LOC" }) },
    };

    class TowerSrc
    {
        public string turret;
        public string muzzle;
        public string[] rocketLocs;
        public TowerSrc(string turret, string muzzle, string[] rocketLocs)
        {
            this.turret = turret;
            this.muzzle = muzzle;
            this.rocketLocs = rocketLocs;
        }
    }

    [MenuItem("Tools/WildernessReplay/Build SciFi Tower Visual Prefabs")]
    public static void Build()
    {
        EnsureFolder("Assets/Resources/Prefabs/Buildings", "CubeTowers");
        EnsureFolder("Assets/ProjectAssets", "SciFiStrategy_BuiltIn");
        EnsureFolder("Assets/ProjectAssets/SciFiStrategy_BuiltIn", "Materials");

        string[] factions = { "Red", "Blue" };
        foreach (var kv in SOURCES)
            foreach (var level in LEVELS)
                foreach (var faction in factions)
                    BuildWrapper(kv.Key, kv.Value, level, faction);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[SciFiTowerPrefabBuilder] 完成：" + (SOURCES.Count * LEVELS.Length * factions.Length) + " 个 SciFi 塔视觉包装 Prefab（含等级）");
    }

    /// <summary>确保阵营染色材质存在（从共享 Main.mat 复制，改低饱和 _Color），每次运行都会刷新 _Color，返回该材质。</summary>
    static Material EnsureFactionMaterial(string faction)
    {
        string path = PROJECT_MATS_DIR + "/Main_" + faction + ".mat";
        var src = AssetDatabase.LoadAssetAtPath<Material>(MAIN_MAT_PATH);
        if (src == null) { Debug.LogError("[SciFiTowerPrefabBuilder] 缺少源材质 " + MAIN_MAT_PATH); return null; }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(src);
            mat.name = "Main_" + faction;
            AssetDatabase.CreateAsset(mat, path);
            Debug.Log("[SciFiTowerPrefabBuilder] 创建阵营材质 " + path);
        }
        // 低饱和阵营色（HSV 压饱和度/明度，避免荧光感）：红≈砖红、蓝≈钢蓝
        Color c = faction == "Red" ? Color.HSVToRGB(0f, 0.5f, 0.7f) : Color.HSVToRGB(0.59f, 0.5f, 0.7f);
        mat.SetColor("_Color", c);
        return mat;
    }

    static void BuildWrapper(string type, TowerSrc src, int level, string faction)
    {
        string srcPath = SRC_DIR + "/" + type + "_" + level + ".prefab";
        var srcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(srcPath);
        if (srcPrefab == null) { Debug.LogError("[SciFiTowerPrefabBuilder] 缺少源 prefab " + srcPath); return; }

        var wrapper = new GameObject("Tower_" + type + "_" + level + "_" + faction);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(srcPrefab, wrapper.transform);
        inst.name = "Model";

        // 移除嵌套源里的 Animator（空动画无用，与旧塔一致）；collider 保留（与旧塔一致）
        var anims = inst.GetComponentsInChildren<Animator>(true);
        foreach (var a in anims) Object.DestroyImmediate(a);

        // 外壳按阵营染色：只替换使用 Main 材质的渲染器（VFX 材质保留）
        var factionMat = EnsureFactionMaterial(faction);
        if (factionMat != null)
        {
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
                if (r.sharedMaterial != null && r.sharedMaterial.name == "Main")
                    r.sharedMaterial = factionMat;
        }

        // 非均匀缩放：XZ 占地固定、Y 拉高超过围墙（底面贴地）。
        // 高度用全部非粒子渲染器；占地只用 MeshRenderer（排除 LineRenderer——激光束会把 XZ 撑大）。
        var hBounds = new Bounds(inst.transform.position, Vector3.zero);
        var wBounds = new Bounds(inst.transform.position, Vector3.zero);
        bool anyH = false, anyW = false;
        foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            hBounds.Encapsulate(r.bounds);
            anyH = true;
            if (r is MeshRenderer) { wBounds.Encapsulate(r.bounds); anyW = true; }
        }
        float srcW = anyW ? Mathf.Max(wBounds.size.x, wBounds.size.z) : (anyH ? Mathf.Max(hBounds.size.x, hBounds.size.z) : 1.3f);
        float srcH = anyH ? Mathf.Max(0.01f, hBounds.size.y) : 1f;
        float xz = (TARGET_WORLD_FOOTPRINT / UNIT_SCALE) / srcW;
        float y = (TARGET_WORLD_HEIGHT / UNIT_SCALE) / srcH;
        wrapper.transform.localScale = new Vector3(xz, y, xz);

        // 炮塔（Horizontal 节点）水平放大（在量完占地后再放大，避免污染底座缩放计算）
        var hzNode = FindChild(inst.transform, "Horizontal");
        if (hzNode != null)
            hzNode.localScale = new Vector3(HORIZONTAL_SCALE, 1f, HORIZONTAL_SCALE);

        // 激光塔：包装 prefab 里默认隐藏所有 LaserBeam* 节点（待机不发光；Setup 运行时也会隐藏，攻击时才显示）
        var beams = new List<Transform>();
        FindAllLaserBeams(inst.transform, beams);
        foreach (var b in beams) b.gameObject.SetActive(false);

        var tvc = wrapper.AddComponent<TowerVisualController>();
        tvc.turretPivot = FindChild(inst.transform, src.turret);
        tvc.muzzleTransform = src.muzzle != null ? FindChild(inst.transform, src.muzzle) : null;
        if (src.rocketLocs != null)
        {
            var locs = new List<Transform>();
            foreach (var n in src.rocketLocs)
            {
                var f = FindChild(inst.transform, n);
                if (f != null) locs.Add(f);
            }
            tvc.rocketLaunchers = locs.ToArray();
        }
        tvc.visualScale = 1f;   // 目标尺寸已烘焙进根 scale，visualScale 作为统一倍率
        tvc.yOffset = 0f;
        tvc.forwardYawOffset = 0f;
        tvc.idleYawOffset = 180f;
        tvc.turnSpeed = 540f;
        tvc.recoilDistance = 0.12f;
        tvc.aimHoldDuration = 1.0f;
        tvc.recoilKickDuration = 0.05f;
        tvc.recoilRecoverDuration = 0.23f;
        tvc.muzzleLightDuration = 0.16f;
        tvc.particleDuration = 0.45f;
        tvc.hitRingDuration = 0.40f;

        string dest = CUBE_TOWERS_DIR + "/Tower_" + type + "_" + level + "_" + faction + ".prefab";
        PrefabUtility.SaveAsPrefabAsset(wrapper, dest);
        Object.DestroyImmediate(wrapper);
        Debug.Log("[SciFiTowerPrefabBuilder] " + dest + " (scale=" + xz.ToString("F3") + "x" + y.ToString("F3") + ", turret=" + (tvc.turretPivot != null) + ", beams=" + beams.Count + ", rockets=" + (tvc.rocketLaunchers != null ? tvc.rocketLaunchers.Length : 0) + ")");
    }

    static void FindAllLaserBeams(Transform parent, List<Transform> result)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child.name.StartsWith("LaserBeam")) result.Add(child);
            FindAllLaserBeams(child, result);
        }
    }

    static Transform FindChild(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            var found = FindChild(child, name);
            if (found != null) return found;
        }
        return null;
    }

    static void EnsureFolder(string parent, string child)
    {
        string full = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(full))
            AssetDatabase.CreateFolder(parent, child);
    }
}
#endif
