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

    // 材质共享缓存：避免每个对象 new Material 破坏合批（同色立方体/同源贴图复用同一实例）
    static readonly Dictionary<Color, Material> _stdMats = new Dictionary<Color, Material>();
    static readonly Dictionary<string, Material> _fixedMats = new Dictionary<string, Material>();
    static Material _waterMat;

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
                // 水域不铺草地，留出凹陷的坑（水面在下方单独生成）
                int t = map.data[z * w + x];
                if (t == 2) continue;

                Vector3 pos = new Vector3(x - ox, -0.03f, oz - z);

                // 使用真正的 Grass_Block.prefab
                if (grassPrefab != null)
                {
                    var tile = Object.Instantiate(grassPrefab, pos, Quaternion.identity, grassRoot);
                    tile.name = "Grass_" + x + "_" + z;
                    tile.isStatic = true; // 静态标记：供 StaticBatchingUtility.Combine 运行时静态合批
                }
                else
                {
                    // 回退：直接创建 Cube
                    var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    tile.name = "Grass_" + x + "_" + z;
                    tile.transform.SetParent(grassRoot);
                    tile.transform.position = pos;
                    tile.transform.localScale = new Vector3(1.03f, 0.06f, 1.03f);
                    tile.isStatic = true; // 静态标记：供 StaticBatchingUtility.Combine 运行时静态合批
                    var c = tile.GetComponent<Collider>();
                    if (c != null) c.enabled = false;
                }

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
                    container.isStatic = true; // 静态标记：碎草是纯装饰，从不移动
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

        // 静态批处理：草地瓦片是运行时生成的，isStatic 标记不会让 WebGL 打包时自动合并——
        // 必须用 StaticBatchingUtility.Combine 在运行时把 ~1300 个 Draw Call 合并为个位数。
        StaticBatchGrass(grassRoot);

        // ── 森林边界 + 主战场内部点缀树 ──
        BuildForestSkirt(root, map, w, h, ox, oz);
        BuildPerimeterFence(root, w, h, ox, oz);

        // ── 地图数据瓦片 ──
        // 小贩(tile 9) 世界坐标：装甲车车头朝向它
        Vector3 vendorPos = new Vector3(0f, 0f, 0f);
        for (int i = 0; i < map.data.Length; i++)
            if (map.data[i] == 9) { int vx = i % w, vy = i / w; vendorPos = new Vector3(vx - ox, 0f, oz - vy); break; }

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int t = map.data[y * w + x];
                Vector3 c = new Vector3(x - ox, 0.01f, oz - y);

                if (t == 2)
                    AddWaterTile(root, "water_" + x + "_" + y, c);
                else if (t == 8 || t == 9 || t == 10)
                    BuildNeutralNpc(root, t, x, y, c);
                else if (t == 40 || t == 41 || t == 42 || t == 43)
                    BuildMissionPoint(root, t, x, y, c, vendorPos);
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
        skirtGo.transform.position = new Vector3(0, -0.13f, 0);
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

        // 静态合批：边界草地 + 树 合并为少量 Draw Call（材质已共享，FBX 已开 Read/Write）
        StaticBatchAll(forestRoot);
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

        // 静态合批：~170 段围栏合并为少量 Draw Call（材质已共享）
        StaticBatchAll(fenceRoot);
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
                rends[i].sharedMaterial = GetFixedMaterial(rends[i].sharedMaterial, tex);
        }
    }

    /// <summary>共享修正材质：同一（源材质+贴图）复用同一材质实例，围栏/树静态合批的前提。</summary>
    static Material GetFixedMaterial(Material src, Texture2D tex)
    {
        string key = (src != null ? src.name : "") + "|" + (tex != null ? tex.name : "");
        Material m;
        if (!_fixedMats.TryGetValue(key, out m))
        {
            m = new Material(src);
            m.SetColor("_Color", Color.white);
            if (tex != null) m.mainTexture = tex;
            _fixedMats[key] = m;
        }
        return m;
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
        GameObject npcGo = null;
        float labelY = 0f;   // 名牌挂载高度（相对 NPC 根节点）
        if (npcPrefab != null)
        {
            var go = Object.Instantiate(npcPrefab, root);
            go.name = "NPC_" + t + "_" + x + "_" + y;
            npcGo = go;
            // 纯数据驱动回放：NPC 是静态单位装饰，销毁物理组件（碰撞体/刚体）关闭物理引擎开销
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) Object.Destroy(col);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Object.Destroy(rb);
            // 静态 NPC 不掉血：销毁 prefab 自带的白色血条（HpFill/HpBar），避免头顶浮白色块
            foreach (var hp in go.GetComponentsInChildren<Transform>(true))
                if (hp.name == "HpFill" || hp.name == "HpBar") Object.Destroy(hp.gameObject);
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
            // 小贩模型高约 1.41，头顶 ~1.36；名牌挂在其正上方
            if (t == 9) labelY = 1.75f;
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
                npcGo = go;
                float sH = spr.bounds.size.y;
                go.transform.position = c + new Vector3(0, sH * 0.5f + 0.11f, 0);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = spr;
                sr.sortingOrder = 6;
                go.AddComponent<Billboard>();
                // Sprite 锚点在垂直中心，名牌挂在精灵顶部之上
                if (t == 9) labelY = sH * 0.5f + 0.55f;
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

        // 小贩头顶常驻名牌：黑色框 + "小贩"
        if (t == 9 && npcGo != null)
            NpcNameLabel.Attach(npcGo.transform, "小贩", labelY);
    }

    // 任务点装饰：宝箱(tile 40/42) / 装甲车(tile 41/43)，由地图 tile 驱动（同小贩/武器商店的摆放逻辑）。
    // 位置即 Build 循环的格子中心 c（y=0.01 贴地）。装甲车原生约 2.7×5.3m，VEHICLE_SCALE=0.27 → 约 0.74×1.44m（0.18×1.5）。
    const float VEHICLE_SCALE = 0.27f;

    static void BuildMissionPoint(Transform root, int t, int x, int y, Vector3 c, Vector3 vendorPos)
    {
        string path = null;
        float scale = 1f;
        if (t == 40 || t == 42) path = "Prefabs/GoldChest";                 // 宝箱
        else if (t == 41 || t == 43) { path = "Prefabs/K151ArmoredVehicle"; scale = VEHICLE_SCALE; } // 装甲车
        if (path == null) return;

        var prefab = Resources.Load<GameObject>(path);
        if (prefab == null) { Debug.LogWarning("[SceneBuilder] 缺少任务点 prefab: " + path); return; }

        var go = Object.Instantiate(prefab, root);
        go.name = "Mission_" + t + "_" + x + "_" + y;
        go.transform.position = c;                 // c 已含 0.01f 贴地
        go.transform.rotation = Quaternion.identity;
        if (scale != 1f) go.transform.localScale = new Vector3(scale, scale, scale);

        // 装甲车车头朝向小贩（水平方向，忽略 Y）；宝箱保持 +Z(北)
        if (t == 41 || t == 43)
        {
            Vector3 dir = vendorPos - c;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                go.transform.rotation = Quaternion.LookRotation(dir.normalized);
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
            // 共享材质：同色立方体复用同一实例，避免每个对象 new Material 破坏合批
            rend.sharedMaterial = GetStandardMat(color);
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = true;
        }
        var col = go.GetComponent<Collider>();
        if (col != null) col.enabled = false;
    }

    static Material GetStandardMat(Color color)
    {
        Material m;
        if (!_stdMats.TryGetValue(color, out m))
        {
            m = new Material(Shader.Find("Standard"));
            m.color = color;
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Glossiness", 0.1f);
            _stdMats[color] = m;
        }
        return m;
    }

    /// <summary>
    /// 水域瓦片：凹陷的池子（深色池底 + 低于草地顶面 0.1 的平整水面）。
    /// 草地顶面 y=0，水面 y=-0.10，池底 y=-0.12~-0.11；水面用无高光的半透明材质，避免反光/瓦片感。
    /// </summary>
    static void AddWaterTile(Transform parent, string name, Vector3 cell)
    {
        // 池底：深色薄板，作为水底（顶面 y=-0.11）
        AddStandardCube(parent, name + "_bed",
            new Vector3(cell.x, -0.115f, cell.z),
            new Vector3(1.03f, 0.01f, 1.03f),
            new Color(0.06f, 0.11f, 0.17f));

        // 水面：平整薄片（Plane 无厚度），低于草地顶面 0.1，形成凹陷
        var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
        go.name = name;
        go.transform.SetParent(parent);
        go.transform.position = new Vector3(cell.x, -0.10f, cell.z);
        go.transform.localScale = new Vector3(0.101f, 1f, 0.101f);
        var rend = go.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.sharedMaterial = MakeWaterMaterial();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
        }
        var col = go.GetComponent<Collider>();
        if (col != null) col.enabled = false;
    }

    static Material MakeWaterMaterial()
    {
        if (_waterMat != null) return _waterMat; // 共享同一水面材质，避免每个水瓦片 new Material
        var m = new Material(Shader.Find("Standard"));
        m.SetFloat("_Mode", 3f); // Transparent
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON");
        m.EnableKeyword("_ALPHABLEND_ON");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        m.SetColor("_Color", new Color(0.12f, 0.33f, 0.58f, 0.75f));
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Glossiness", 0f);                          // 无高光
        m.SetColor("_SpecColor", new Color(0f, 0f, 0f, 1f));    // 镜面黑，消除反光
        _waterMat = m;
        return m;
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

    /// <summary>
    /// 运行时手动合批：把 root 下所有 MeshRenderer 按「材质」分组，每组用 Mesh.CombineMeshes 合成一张网格，
    /// 挂到 root 上（一组一个子 MeshRenderer），再把原物体全部禁用。
    /// 注意：不能直接用 StaticBatchingUtility.Combine —— 在本项目（2022.3 WebGL 配置）下实测无效，
    /// 无论 mesh 是否可读、物体是否 isStatic，调用后 root 都不产生合并网格。Mesh.CombineMeshes 是确定性的。
    /// </summary>
    static void StaticBatchAll(Transform root)
    {
        // 收集可读静态 MeshRenderer（跳过 Skinned/Particle）
        var collected = new List<Renderer>();
        var renderers = root.GetComponentsInChildren<Renderer>(false);
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null || r is SkinnedMeshRenderer || r is ParticleSystemRenderer) continue;
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
            r.gameObject.isStatic = true;
            collected.Add(r);
        }
        if (collected.Count == 0) return;

        // 按材质分组 → 每组一个合成网格（材质不同无法合批）
        var groups = new Dictionary<Material, List<Renderer>>();
        for (int i = 0; i < collected.Count; i++)
        {
            var mat = collected[i].sharedMaterial;
            List<Renderer> list;
            if (!groups.TryGetValue(mat, out list)) { list = new List<Renderer>(); groups[mat] = list; }
            list.Add(collected[i]);
        }

        var rootGo = root.gameObject;
        var goList = new List<GameObject>();
        foreach (var kv in groups)
        {
            var rendererList = kv.Value;
            var mat = kv.Key;

            // 单网格顶点上限 ~65k（WebGL 16-bit 索引）；按 60k 预算分块
            const int kVertexBudget = 60000;
            var chunk = new List<Renderer>();
            int chunkVerts = 0;
            int chunkIndex = 0;
            for (int i = 0; i < rendererList.Count; i++)
            {
                var r = rendererList[i];
                int v = r.GetComponent<MeshFilter>().sharedMesh.vertexCount;
                if (chunk.Count > 0 && chunkVerts + v > kVertexBudget)
                {
                    BuildCombineChunk(rootGo, mat, chunk, rendererList.Count, chunkIndex++, goList);
                    chunk.Clear();
                    chunkVerts = 0;
                }
                chunk.Add(r);
                chunkVerts += v;
            }
            if (chunk.Count > 0)
                BuildCombineChunk(rootGo, mat, chunk, rendererList.Count, chunkIndex++, goList);
        }

        // 禁用被合并的原始物体（连同其容器，避免重复渲染）
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            renderers[i].enabled = false;
            var go = renderers[i].gameObject;
            if (go != null && go.transform.parent != null && go.transform.parent != root)
                go.transform.parent.gameObject.SetActive(false);
        }
    }

    /// <summary>把一组同材质渲染器合成一张网格，挂到 root 下。</summary>
    static void BuildCombineChunk(GameObject rootGo, Material mat, List<Renderer> list, int totalCount, int chunkIndex, List<GameObject> goList)
    {
        var comb = new CombineInstance[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            comb[i].mesh = list[i].GetComponent<MeshFilter>().sharedMesh;
            comb[i].transform = list[i].transform.localToWorldMatrix;
        }
        var combinedMesh = new Mesh();
        // useMatrices 必须为 true：否则 CombineInstance.transform 被忽略，所有顶点塌缩到局部原点（全堆在地图中心）。
        combinedMesh.CombineMeshes(comb, true, true);

        var go = new GameObject("Batch_" + totalCount + (chunkIndex > 0 ? "_" + chunkIndex : "") + "_" + mat.name);
        go.transform.SetParent(rootGo.transform, false);
        go.transform.position = Vector3.zero;       // 网格已是世界坐标
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        go.isStatic = true;
        go.AddComponent<MeshFilter>().sharedMesh = combinedMesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        // 保持原视觉：草地/树/围栏原本都投影且接收阴影。
        // 合批后 14 个合成网格投影，比 2356 个独立渲染器投影的阴影深度 pass 少得多。
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        mr.receiveShadows = true;
        goList.Add(go);
    }

    /// <summary>草地合批（草地瓦片 + 碎草装饰一起走 StaticBatchAll）。</summary>
    static void StaticBatchGrass(Transform grassRoot)
    {
        StaticBatchAll(grassRoot);
    }
}
