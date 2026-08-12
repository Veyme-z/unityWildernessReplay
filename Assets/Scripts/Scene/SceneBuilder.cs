using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 3D 地形搭建器 v4：真正 Grass_Block.prefab + KayKit 树灌散布 + 矿石 FBX 直加载。
/// </summary>
public static class SceneBuilder
{
    static T LoadAsset<T>(string path) where T : Object
    {
#if UNITY_EDITOR
        var a = AssetDatabase.LoadAssetAtPath<T>(path);
        if (a != null) return a;
#endif
        // Build 回退：从 Resources/Prefabs/Environment/Forest/ 加载同名 Prefab
        string name = System.IO.Path.GetFileNameWithoutExtension(path);
        return Resources.Load<T>("Prefabs/Environment/Forest/" + name);
    }
    static Texture2D _dayTex, _nightTex;
    static List<GameObject> _treePool;
    static List<GameObject> _grassScatter;
    static bool _forestPoolLoaded;

    /// <summary>矿石 FBX 模型引用（供 ResourceViewManager 使用）</summary>
    public static GameObject OreRockModel;

    public static Transform Build(ReplayMap map)
    {
        MatLib.Init();
        var root = new GameObject("Map").transform;
        root.position = Vector3.zero;

        int w = map.width, h = map.height;
        float ox = (w - 1) * 0.5f, oz = (h - 1) * 0.5f;

        BuildGroundSkirt(root);
        LoadForestPool();

        _dayTex = MatLib.TryLoadTexture("Sprites/background");
        if (_dayTex == null) _dayTex = MatLib.TryLoadTexture("Textures/background");
        _nightTex = MatLib.TryLoadTexture("Sprites/background_night");
        if (_nightTex == null) _nightTex = MatLib.TryLoadTexture("Textures/background_night");

        // ── 41x32 Grass_Block.prefab 网格 ──
        var grassPrefab = Resources.Load<GameObject>("Prefabs/Environment/Grass_Block");
        var grassRoot = new GameObject("GrassGrid").transform;
        grassRoot.SetParent(root);
        System.Random rng = new System.Random(12345);

        for (int x = 0; x < w; x++)
        {
            for (int z = 0; z < h; z++)
            {
                Vector3 pos = new Vector3(x - ox, -0.03f, oz - z);

                // 使用真正的 Grass_Block.prefab
                if (grassPrefab != null)
                {
                    var tile = Object.Instantiate(grassPrefab, pos, Quaternion.identity, grassRoot);
                    tile.name = "Grass_" + x + "_" + z;
                }
                else
                {
                    // 回退：直接创建 Cube
                    var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    tile.name = "Grass_" + x + "_" + z;
                    tile.transform.SetParent(grassRoot);
                    tile.transform.position = pos;
                    tile.transform.localScale = new Vector3(1.03f, 0.06f, 1.03f);
                    var c = tile.GetComponent<Collider>();
                    if (c != null) c.enabled = false;
                }

                // 水域跳过碎草
                int t = map.data[z * w + x];
                if (t == 2) continue;

                // 1/3 概率播撒 1 束碎草，大小减半
                if (rng.NextDouble() < 0.33f && _grassScatter.Count > 0)
                {
                    int idx = rng.Next(_grassScatter.Count);
                    float sx = pos.x + (float)(rng.NextDouble() * 0.7 - 0.35);
                    float sz = pos.z + (float)(rng.NextDouble() * 0.7 - 0.35);
                    float scale = 0.15f + (float)(rng.NextDouble() * 0.15f);
                    float rotY = (float)(rng.NextDouble() * 360f);
                    var container = new GameObject("Tuft_" + x + "_" + z);
                    container.transform.SetParent(grassRoot);
                    container.transform.position = new Vector3(sx, 0f, sz);
                    container.transform.rotation = Quaternion.Euler(0, rotY, 0);
                    container.transform.localScale = new Vector3(scale, scale, scale);
                    var model = Object.Instantiate(_grassScatter[idx], container.transform);
                    model.transform.localPosition = Vector3.zero;
                    model.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                }
            }
        }
        Debug.Log("[SceneBuilder] Grass: " + (w * h) + " tiles with scatter");

        // ── 森林边界 + 主战场内部点缀树 ──
        BuildForestSkirt(root, map, w, h, ox, oz);
        BuildPerimeterFence(root, w, h, ox, oz);

        // ── 地图数据瓦片 ──
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int t = map.data[y * w + x];
                Vector3 c = new Vector3(x - ox, 0.01f, oz - y);

                if (t == 2)
                    AddStandardCube(root, "water_" + x + "_" + y, c, new Vector3(1.03f, 0.24f, 1.03f),
                            new Color(0.13f, 0.38f, 0.82f));
                else if (t == 8 || t == 9 || t == 10)
                    BuildNeutralNpc(root, t, x, y, c);
                else if (t == 4 || t == 3 || t == 5 || t == 1)
                    AddStandardCube(root, "found_" + x + "_" + y, c, new Vector3(1.02f, 0.1f, 1.02f),
                            t == 1 ? new Color(0.42f, 0.42f, 0.45f) : new Color(0.35f, 0.33f, 0.40f));
            }
        }

        return root;
    }

    static void BuildGroundSkirt(Transform root)
    {
        var grassTex = MakeGrassTex(64, 64);
        var skirtGo = GameObject.CreatePrimitive(PrimitiveType.Plane);
        skirtGo.name = "Extended_Ground_Skirt";
        skirtGo.transform.SetParent(root);
        skirtGo.transform.position = new Vector3(0, -0.08f, 0);
        skirtGo.transform.localScale = new Vector3(15f, 1f, 15f);
        var skirtRend = skirtGo.GetComponent<Renderer>();
        var skirtMat = new Material(Shader.Find("Standard"))
        {
            mainTexture = grassTex,
            color = new Color(0.25f, 0.42f, 0.18f)
        };
        skirtMat.mainTextureScale = new Vector2(30f, 30f);
        skirtRend.sharedMaterial = skirtMat;
        skirtRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        skirtRend.receiveShadows = false;
        var skirtCol = skirtGo.GetComponent<Collider>();
        if (skirtCol != null) skirtCol.enabled = false;
    }

    // ── 森林：边界 3 格（18%树/25%灌/30%石）+ 主战场内部随机点缀 ──
    static void BuildForestSkirt(Transform root, ReplayMap map, int w, int h, float ox, float oz)
    {
        if (_treePool.Count == 0) return;

        var forestRoot = new GameObject("ForestSkirt").transform;
        forestRoot.SetParent(root);

        System.Random rng = new System.Random(42);
        int treeCount = 0;
        List<Vector2> treePositions = new List<Vector2>();

        int minX = -3, maxX = w + 2;
        int minZ = -3, maxZ = h + 2;

        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                bool inMain = (x >= 0 && x < w && z >= 0 && z < h);
                float worldX = x - ox;
                float worldZ = oz - z;

                // 水面跳过（不种树灌）
                if (inMain && map.data[z * w + x] == 2) continue;

                if (!inMain)
                {
                    var grassPrefab = Resources.Load<GameObject>("Prefabs/Environment/Grass_Block");
                    if (grassPrefab != null)
                    {
                        var tile = Object.Instantiate(grassPrefab,
                            new Vector3(worldX, -0.03f, worldZ), Quaternion.identity, forestRoot);
                        tile.name = "BorderGrass_" + x + "_" + z;
                    }
                }

                // 树木仅限外圈（非主战场）
                float treeProb = inMain ? 0f : 0.18f;

                float roll = (float)rng.NextDouble();
                if (roll < treeProb && _treePool.Count > 0)
                {
                    bool tooClose = false;
                    Vector2 myPos = new Vector2(worldX, worldZ);
                    for (int ti = 0; ti < treePositions.Count; ti++)
                    {
                        if ((myPos - treePositions[ti]).sqrMagnitude < (inMain ? 16f : 4f))
                        { tooClose = true; break; }
                    }
                    if (!tooClose)
                    {
                        int idx = rng.Next(_treePool.Count);
                        float rx = worldX + (float)(rng.NextDouble() * 0.6 - 0.3);
                        float rz = worldZ + (float)(rng.NextDouble() * 0.6 - 0.3);
                        float rotY = (float)(rng.NextDouble() * 360f);
                        float scale = inMain ? 0.50f + (float)(rng.NextDouble() * 0.30f)
                                           : 0.70f + (float)(rng.NextDouble() * 0.40f);
                        var container = new GameObject("Tree_" + x + "_" + z);
                        container.transform.SetParent(forestRoot);
                        container.transform.position = new Vector3(rx, 0f, rz);
                        container.transform.rotation = Quaternion.Euler(0, rotY, 0);

                        var treePrefab = _treePool[idx];
                        var model = Object.Instantiate(treePrefab, container.transform);
                        model.transform.localPosition = Vector3.zero;
                        // KayKit (scale≈100): 需要 -90°X 修正; Devilswork (scale≈1): 无需
                        if (treePrefab.transform.localScale.x > 50f)
                        {
                            container.transform.localScale = new Vector3(scale, scale, scale);
                            model.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                        }
                        else
                        {
                            // Devilswork: scale 直接控制, 修复黄色材质
                            container.transform.localScale = new Vector3(scale * 0.6f, scale * 0.6f, scale * 0.6f);
                            var tex = LoadAsset<Texture2D>(
                                "Assets/Low_Poly_Forest_Pack_Devilswork.Shop_v02/tex/treeTall.png");
                            FixDevilsworkMaterial(model, tex);
                        }
                        treePositions.Add(new Vector2(rx, rz));
                        treeCount++;
                    }
                }
            }
        }

        Debug.Log("[SceneBuilder] Forest: " + treeCount + " trees (border only)");
    }

    // ── 木围栏：外圈边界（Devilswork fence24）──
    static void BuildPerimeterFence(Transform root, int w, int h, float ox, float oz)
    {
        var fenceFbx = LoadAsset<GameObject>(
            "Assets/Low_Poly_Forest_Pack_Devilswork.Shop_v02/FBX 2013/Low_Poly_Forest_fence24.fbx");
        if (fenceFbx == null) return;

        // 修正黄色材质：加载 fence01.png 贴图
        var fenceTex = LoadAsset<Texture2D>(
            "Assets/Low_Poly_Forest_Pack_Devilswork.Shop_v02/tex/fence01.png");

        var fenceRoot = new GameObject("PerimeterFence").transform;
        fenceRoot.SetParent(root);

        int minX = -3, maxX = w + 2;
        int minZ = -3, maxZ = h + 2;
        // fence24: 1.96m 宽, 容器 scale 0.51 → 1m/段, 高 0.58m
        float segScale = 0.51f;
        int count = 0;

        // 上边 + 下边（水平走向）
        for (int x = minX; x <= maxX; x++)
        {
            PlaceFenceSegment(fenceRoot, fenceFbx, fenceTex, x - ox, oz - (minZ - 0.5f), segScale, 0f, ref count);
            PlaceFenceSegment(fenceRoot, fenceFbx, fenceTex, x - ox, oz - (maxZ + 0.5f), segScale, 0f, ref count);
        }
        // 左边 + 右边（竖直走向，Y 旋转 90°）
        for (int z = minZ; z <= maxZ; z++)
        {
            PlaceFenceSegment(fenceRoot, fenceFbx, fenceTex, (minX - 0.5f) - ox, oz - z, segScale, 90f, ref count);
            PlaceFenceSegment(fenceRoot, fenceFbx, fenceTex, (maxX + 0.5f) - ox, oz - z, segScale, 90f, ref count);
        }

        Debug.Log("[SceneBuilder] Fence: " + count + " segments (fence24 + fence01.png)");
    }

    static void PlaceFenceSegment(Transform parent, GameObject fenceFbx, Texture2D tex, float wx, float wz, float scale, float rotY, ref int count)
    {
        var container = new GameObject("Fence_" + count);
        container.transform.SetParent(parent);
        container.transform.position = new Vector3(wx, 0f, wz);
        container.transform.rotation = Quaternion.Euler(0, rotY, 0);
        container.transform.localScale = new Vector3(scale, scale, scale);
        var model = Object.Instantiate(fenceFbx, container.transform);
        model.transform.localPosition = Vector3.zero;
        FixDevilsworkMaterial(model, tex);
        count++;
    }

    // Devilswork 模型材质修正：关联贴图，去掉黄色
    static void FixDevilsworkMaterial(GameObject go, Texture2D tex)
    {
        Renderer[] rends = go.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i].sharedMaterial != null)
            {
                Material m = new Material(rends[i].sharedMaterial);
                m.SetColor("_Color", Color.white);
                if (tex != null) m.mainTexture = tex;
                rends[i].sharedMaterial = m;
            }
        }
    }

    static void LoadForestPool()
    {
        if (_forestPoolLoaded) return;
        _forestPoolLoaded = true;
        _treePool = new List<GameObject>();
        _grassScatter = new List<GameObject>();

        // 树池：KayKit 5种 + Devilswork treeTall03
        string[] trees = {
            "Assets/KayKit_Forest_Nature_Pack_1.0_FREE/Assets/fbx/Tree_1_A_Color1.fbx",
            "Assets/KayKit_Forest_Nature_Pack_1.0_FREE/Assets/fbx/Tree_2_A_Color1.fbx",
            "Assets/KayKit_Forest_Nature_Pack_1.0_FREE/Assets/fbx/Tree_3_A_Color1.fbx",
            "Assets/KayKit_Forest_Nature_Pack_1.0_FREE/Assets/fbx/Tree_4_A_Color1.fbx",
            "Assets/KayKit_Forest_Nature_Pack_1.0_FREE/Assets/fbx/Tree_Bare_2_A_Color1.fbx",
            "Assets/Low_Poly_Forest_Pack_Devilswork.Shop_v02/FBX 2013/Low_Poly_Forest_treeTall03.fbx",
        };
        foreach (string p in trees)
        {
            var go = LoadAsset<GameObject>(p);
            if (go != null) _treePool.Add(go);
        }

        string[] grasses = {
            "Assets/KayKit_Forest_Nature_Pack_1.0_FREE/Assets/fbx/Grass_1_A_Color1.fbx",
            "Assets/KayKit_Forest_Nature_Pack_1.0_FREE/Assets/fbx/Grass_1_B_Color1.fbx",
            "Assets/KayKit_Forest_Nature_Pack_1.0_FREE/Assets/fbx/Grass_2_A_Color1.fbx",
            "Assets/KayKit_Forest_Nature_Pack_1.0_FREE/Assets/fbx/Grass_2_B_Color1.fbx",
        };
        foreach (string p in grasses)
        {
            var go = LoadAsset<GameObject>(p);
            if (go != null) _grassScatter.Add(go);
        }

        // 暴露矿石 FBX 引用（Rock_1_A）
        OreRockModel = LoadAsset<GameObject>(
            "Assets/KayKit_Forest_Nature_Pack_1.0_FREE/Assets/fbx/Rock_1_A_Color1.fbx");

        Debug.Log("[SceneBuilder] Pool: " + _treePool.Count + " trees + "
            + _grassScatter.Count + " grass"
            + " | OreModel=" + (OreRockModel != null ? "OK" : "MISSING"));
    }

    static void BuildNeutralNpc(Transform root, int t, int x, int y, Vector3 c)
    {
        string npcPrefabPath = null;
        if (t == 8) npcPrefabPath = "Prefabs/Units/OfficerNPC";
        else if (t == 9) npcPrefabPath = "Prefabs/Units/VendorNPC";
        else if (t == 10) npcPrefabPath = "Prefabs/Buildings/WeaponShop";

        GameObject npcPrefab = npcPrefabPath != null ? Resources.Load<GameObject>(npcPrefabPath) : null;
        if (npcPrefab != null)
        {
            var go = Object.Instantiate(npcPrefab, root);
            go.name = "NPC_" + t + "_" + x + "_" + y;
            go.transform.position = c + new Vector3(0, 0.01f, 0);
            if (t == 8) go.transform.rotation = Quaternion.Euler(0f, 135f, 0f);
            else if (t == 9) go.transform.rotation = Quaternion.Euler(0f, -45f, 0f);
            else if (t == 10) go.transform.rotation = Quaternion.Euler(0f, 225f, 0f); // 西南

            // NPC 转向组件
            if (t == 8 || t == 9)
            {
                var fc = go.GetComponent<NpcFacingController>();
                if (fc == null) fc = go.AddComponent<NpcFacingController>();
                fc.npcType = t;
                var visual = go.transform.Find("Visual");
                if (visual != null) fc.facingTransform = visual;
            }
        }
        else
        {
            Sprite spr = null;
            if (t == 8) spr = UnitViewSprite.FindSprite("officer", "taskofficer", "8");
            else if (t == 9) spr = UnitViewSprite.FindSprite("vendor", "trader", "9");
            else spr = UnitViewSprite.FindSprite("weaponshop", "shop", "10");

            if (spr != null)
            {
                var go = new GameObject("bld_" + t + "_" + x + "_" + y);
                go.transform.SetParent(root);
                float sH = spr.bounds.size.y;
                go.transform.position = c + new Vector3(0, sH * 0.5f + 0.11f, 0);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = spr;
                sr.sortingOrder = 6;
                go.AddComponent<Billboard>();
            }
            else
            {
                Color col = t == 8 ? new Color(0.80f, 0.68f, 0.25f)
                          : t == 9 ? new Color(0.32f, 0.62f, 0.38f)
                          : new Color(0.85f, 0.32f, 0.32f);
                AddStandardCube(root, "bld_" + t + "_" + x + "_" + y, c + new Vector3(0, 0.35f, 0),
                        new Vector3(0.5f, 0.7f, 0.5f), col);
            }
        }
    }

    static void AddStandardCube(Transform parent, string name, Vector3 pos, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent);
        go.transform.position = pos;
        go.transform.localScale = scale;
        var rend = go.GetComponent<Renderer>();
        if (rend != null)
        {
            var m = new Material(Shader.Find("Standard"));
            m.color = color;
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Glossiness", 0.1f);
            rend.sharedMaterial = m;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = true;
        }
        var col = go.GetComponent<Collider>();
        if (col != null) col.enabled = false;
    }

    static Texture2D MakeGrassTex(int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float r = Mathf.PerlinNoise(x * 0.15f, y * 0.15f);
                float gBase = 0.42f + r * 0.22f;
                float rBase = 0.20f + r * 0.15f;
                float bBase = 0.10f + r * 0.10f;
                float noise = (Mathf.PerlinNoise(x * 0.5f + 100f, y * 0.5f + 100f) - 0.5f) * 0.06f;
                tex.SetPixel(x, y, new Color(rBase + noise, gBase + noise, bBase + noise, 1f));
            }
        tex.Apply(false, true);
        return tex;
    }
}
