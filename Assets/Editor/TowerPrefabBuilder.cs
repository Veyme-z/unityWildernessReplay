#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 一次性/可重复编辑器工具：生成防御塔视觉包装 Prefab。
///  - 备份旧 Tower.prefab → Legacy/Tower_Legacy.prefab
///  - 修改 Tower.prefab：停用旧 Visual + 添加 VisualRoot
///  - 生成 6 个 Resources 视觉包装 Prefab（嵌套引用已转换的 ProjectAssets 塔，不复制 FBX/贴图）
/// </summary>
public static class TowerPrefabBuilder
{
    const string TOWER_PATH = "Assets/Resources/Prefabs/Buildings/Tower.prefab";
    const string LEGACY_PATH = "Assets/Resources/Prefabs/Buildings/Legacy/Tower_Legacy.prefab";
    const string CUBE_TOWERS_DIR = "Assets/Resources/Prefabs/Buildings/CubeTowers";
    const string SRC_DIR = "Assets/ProjectAssets/CubeTowerDefense_BuiltIn/Resources/Prefabs/Towers";

    static readonly Dictionary<string, string> TURRET_NODES = new Dictionary<string, string>
    {
        { "Flamethrower", "Flamethrower" },
        { "Minigun", "Minigun" },
        { "RPG", "Rpg" },
    };

    [MenuItem("Tools/WildernessReplay/Build Tower Visual Prefabs")]
    public static void Build()
    {
        EnsureFolder("Assets/Resources/Prefabs/Buildings", "Legacy");
        EnsureFolder("Assets/Resources/Prefabs/Buildings", "CubeTowers");

        // 1) 备份旧塔（已存在则跳过，避免覆盖）
        if (!AssetDatabase.LoadAssetAtPath<GameObject>(LEGACY_PATH))
            AssetDatabase.CopyAsset(TOWER_PATH, LEGACY_PATH);

        // 2) 修改 Tower.prefab：停用旧 Visual + 添加 VisualRoot
        ModifyTowerPrefab();

        // 3) 生成 6 个视觉包装 Prefab
        string[] types = { "Flamethrower", "Minigun", "RPG" };
        string[] factions = { "Red", "Blue" };
        foreach (var type in types)
            foreach (var faction in factions)
                BuildWrapper(type, faction);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[TowerPrefabBuilder] 完成：6 个视觉包装 Prefab + Tower.prefab 已更新");
    }

    static void ModifyTowerPrefab()
    {
        var contents = PrefabUtility.LoadPrefabContents(TOWER_PATH);
        var visual = contents.transform.Find("Visual");
        if (visual != null) visual.gameObject.SetActive(false);

        if (contents.transform.Find("VisualRoot") == null)
        {
            var vr = new GameObject("VisualRoot");
            vr.transform.SetParent(contents.transform, false);
            vr.transform.localPosition = Vector3.zero;
            vr.transform.localRotation = Quaternion.identity;
            vr.transform.localScale = Vector3.one;
        }
        PrefabUtility.SaveAsPrefabAsset(contents, TOWER_PATH);
        PrefabUtility.UnloadPrefabContents(contents);
    }

    static void BuildWrapper(string type, string faction)
    {
        string srcPath = SRC_DIR + "/Tower_" + type + "_" + faction + ".prefab";
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(srcPath);
        if (src == null) { Debug.LogError("[TowerPrefabBuilder] 缺少源 prefab " + srcPath); return; }

        var wrapper = new GameObject("Tower_" + type + "_" + faction);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(src, wrapper.transform);
        inst.name = "Model";

        var tvc = wrapper.AddComponent<TowerVisualController>();
        string turretName;
        tvc.turretPivot = TURRET_NODES.TryGetValue(type, out turretName) ? FindChild(inst.transform, turretName) : null;
        tvc.muzzleTransform = FindChild(inst.transform, "Muzzle");
        tvc.visualScale = 1.6f;
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

        bool hasTurret = tvc.turretPivot != null;
        bool hasMuzzle = tvc.muzzleTransform != null;

        string dest = CUBE_TOWERS_DIR + "/Tower_" + type + "_" + faction + ".prefab";
        PrefabUtility.SaveAsPrefabAsset(wrapper, dest);
        Object.DestroyImmediate(wrapper);
        Debug.Log("[TowerPrefabBuilder] " + dest + " (turret=" + hasTurret + ", muzzle=" + hasMuzzle + ")");
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
