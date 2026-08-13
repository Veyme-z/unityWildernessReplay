using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// RaygeasV2 纯视觉环境：Unity Terrain + Water Plane。
/// 使用 map.data==2 生成水域水深，Terrain 平滑岸线，Water Shader 深度混合。
/// 不参与碰撞、移动、寻路。
/// </summary>
public static class RaygeasEnvironmentV2
{
    // ── 可调参数 ──
    public const float landY       = 0f;      // 陆地世界 Y
    public const float waterY      = -0.02f;  // 水面世界 Y
    public const float bedY        = -0.20f;  // 水底世界 Y
    public const float shoreWidth  = 0.35f;   // 岸线平滑宽度（逻辑格）
    public const float waterExpansion = 0.30f;// 水域掩码向外扩张（逻辑格）

    const int HeightmapRes = 257; // 2^8+1, 每像素 ≈ 0.16 格

    static GameObject _terrainGo;
    static GameObject _waterPlaneGo;
    static HashSet<(int,int)> _testRegion;

    // ── 公开接口 ──

    public static void Build(Transform parent, int[] mapData, int mapW, int mapH)
    {
        Clear();

        // 选最大连通水域做原型
        _testRegion = GetLargestWaterRegion(mapData, mapW, mapH);
        if (_testRegion == null || _testRegion.Count == 0)
        {
            Debug.Log("[RaygeasV2] No water cells found, skipped");
            return;
        }

        float ox = (mapW - 1) * 0.5f;
        float oz = (mapH - 1) * 0.5f;

        // 1. 距离场（grid 空间，每个 cell 角点采样）
        var distField = ComputeDistanceField(_testRegion, mapW, mapH);

        // 2. 构建 heightmap
        float heightRange = landY - bedY; // 0.20
        float landHm = (landY + heightRange) / heightRange; // normalized to [0,1]
        float bedHm  = 0f;
        // 简化：Terrain root Y 偏移使 bedY → heightmap 0, landY → heightmap 1
        // Terrain size.y = heightRange, Terrain pos.y = bedY
        float terrainBaseY = bedY;

        float[,] heights = new float[HeightmapRes, HeightmapRes];
        for (int hz = 0; hz < HeightmapRes; hz++)
        {
            for (int hx = 0; hx < HeightmapRes; hx++)
            {
                // heightmap → grid coordinate
                float gx = (float)hx / (HeightmapRes - 1) * mapW;
                float gz = (float)hz / (HeightmapRes - 1) * mapH;
                float dist = SampleBilinear(distField, mapW, mapH, gx, gz);
                float effective = dist - waterExpansion;
                float t = Mathf.Clamp01(effective / shoreWidth);
                heights[hz, hx] = Mathf.Lerp(bedHm, landHm, t);
            }
        }

        // 3. 创建 Terrain
        var td = new TerrainData();
        td.name = "RaygeasV2_TerrainData";
        td.heightmapResolution = HeightmapRes;
        td.size = new Vector3(mapW, heightRange, mapH);
        td.SetHeights(0, 0, heights);

        // 基础草地层
        var grassLayer = CreateProceduralGrassLayer();
        td.terrainLayers = new TerrainLayer[] { grassLayer };

        // 绘制 alphamap（全草）
        int amRes = td.alphamapResolution;
        float[,,] alpha = new float[amRes, amRes, 1];
        for (int z = 0; z < amRes; z++)
            for (int x = 0; x < amRes; x++)
                alpha[z, x, 0] = 1f;
        td.SetAlphamaps(0, 0, alpha);

        _terrainGo = Terrain.CreateTerrainGameObject(td);
        _terrainGo.name = "RaygeasV2_Terrain";
        _terrainGo.transform.SetParent(parent);

        // 对齐地图世界边界：左下角 = (-ox-0.5, bedY, -oz-0.5)
        _terrainGo.transform.position = new Vector3(-ox - 0.5f, terrainBaseY, -oz - 0.5f);

        // 禁用 TerrainCollider
        var tc = _terrainGo.GetComponent<TerrainCollider>();
        if (tc != null) tc.enabled = false;

        // 4. 创建 Water Plane（覆盖整个地图区域，由 Terrain 高度自然遮挡）
        var waterPlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        waterPlane.name = "RaygeasV2_WaterPlane";
        waterPlane.transform.SetParent(parent);
        // Plane 原生 10×10，缩放至地图尺寸
        waterPlane.transform.localScale = new Vector3(mapW * 0.1f, 1f, mapH * 0.1f);
        waterPlane.transform.position = new Vector3(0, waterY, 0);
        var wr = waterPlane.GetComponent<MeshRenderer>();
        var waterMat = Resources.Load<Material>("Materials/Mat_GrasslandsWater");
        if (waterMat != null) wr.sharedMaterial = waterMat;
        wr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        wr.receiveShadows = true;
        var wc = waterPlane.GetComponent<Collider>();
        if (wc != null) Object.Destroy(wc);
        _waterPlaneGo = waterPlane;

        // 5. 边界地面（Terrain 外侧，供树木/围栏站立）
        BuildBorderGround(parent, mapW, mapH, ox, oz);

        Debug.Log($"[RaygeasV2] Built Terrain {mapW}x{mapH} @ ({_terrainGo.transform.position}), "
            + $"water region: {_testRegion.Count} cells, shoreWidth={shoreWidth}, "
            + $"landY={landY} waterY={waterY} bedY={bedY}");
    }

    // ── 边界地面：Terrain 外侧 3 格宽的四边形带 ──

    static void BuildBorderGround(Transform parent, int mapW, int mapH, float ox, float oz)
    {
        // Terrain 世界边界
        float tMinX = -ox - 0.5f, tMaxX = ox + 0.5f;
        float tMinZ = -oz - 0.5f, tMaxZ = oz + 0.5f;
        // 森林边界（+3 格）
        float bMinX = tMinX - 3f, bMaxX = tMaxX + 3f;
        float bMinZ = tMinZ - 3f, bMaxZ = tMaxZ + 3f;

        var skirtMat = new Material(Shader.Find("Standard"))
        {
            color = new Color(0.25f, 0.42f, 0.18f)
        };
        const float groundY = -0.03f;

        // 上/下/左/右 四条带
        CreateBorderStrip(parent, "Border_Top",
            (bMinX + bMaxX) * 0.5f, groundY, (tMaxZ + bMaxZ) * 0.5f,
            bMaxX - bMinX, bMaxZ - tMaxZ, skirtMat);
        CreateBorderStrip(parent, "Border_Bottom",
            (bMinX + bMaxX) * 0.5f, groundY, (bMinZ + tMinZ) * 0.5f,
            bMaxX - bMinX, tMinZ - bMinZ, skirtMat);
        CreateBorderStrip(parent, "Border_Left",
            (bMinX + tMinX) * 0.5f, groundY, (tMinZ + tMaxZ) * 0.5f,
            tMinX - bMinX, tMaxZ - tMinZ, skirtMat);
        CreateBorderStrip(parent, "Border_Right",
            (tMaxX + bMaxX) * 0.5f, groundY, (tMinZ + tMaxZ) * 0.5f,
            bMaxX - tMaxX, tMaxZ - tMinZ, skirtMat);
    }

    static void CreateBorderStrip(Transform parent, string name,
        float cx, float cy, float cz, float w, float h, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
        go.name = name;
        go.transform.SetParent(parent);
        go.transform.position = new Vector3(cx, cy, cz);
        go.transform.localScale = new Vector3(w * 0.1f, 1f, h * 0.1f);
        var rend = go.GetComponent<Renderer>();
        rend.sharedMaterial = mat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        var col = go.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);
    }

    public static void Clear()
    {
        if (_terrainGo != null && _terrainGo) { Object.Destroy(_terrainGo); _terrainGo = null; }
        if (_waterPlaneGo != null && _waterPlaneGo) { Object.Destroy(_waterPlaneGo); _waterPlaneGo = null; }
        _testRegion = null;
    }

    // ── 水域区域 ──

    static HashSet<(int,int)> GetLargestWaterRegion(int[] mapData, int mapW, int mapH)
    {
        var visited = new bool[mapW, mapH];
        HashSet<(int,int)> largest = null;
        for (int z = 0; z < mapH; z++)
            for (int x = 0; x < mapW; x++)
            {
                if (mapData[z * mapW + x] != 2 || visited[x, z]) continue;
                var region = FloodFill(mapData, visited, mapW, mapH, x, z);
                if (largest == null || region.Count > largest.Count)
                    largest = region;
            }
        return largest;
    }

    static HashSet<(int,int)> FloodFill(int[] mapData, bool[,] visited, int mapW, int mapH, int sx, int sz)
    {
        var region = new HashSet<(int,int)>();
        var queue = new Queue<(int,int)>();
        queue.Enqueue((sx, sz));
        visited[sx, sz] = true;
        int[] dx = { -1, 1, 0, 0 }, dz = { 0, 0, -1, 1 };
        while (queue.Count > 0)
        {
            var (x, z) = queue.Dequeue();
            region.Add((x, z));
            for (int d = 0; d < 4; d++)
            {
                int nx = x + dx[d], nz = z + dz[d];
                if (nx >= 0 && nx < mapW && nz >= 0 && nz < mapH
                    && !visited[nx, nz] && mapData[nz * mapW + nx] == 2)
                {
                    visited[nx, nz] = true;
                    queue.Enqueue((nx, nz));
                }
            }
        }
        return region;
    }

    // ── 距离场 ──

    static float[,] ComputeDistanceField(HashSet<(int,int)> water, int mapW, int mapH)
    {
        // 在 cell 角点采样 (0..mapW, 0..mapH)
        var dist = new float[mapW + 1, mapH + 1];
        for (int x = 0; x <= mapW; x++)
        {
            for (int z = 0; z <= mapH; z++)
            {
                dist[x, z] = MinDistanceToWater(water, x, z);
            }
        }
        return dist;
    }

    static float MinDistanceToWater(HashSet<(int,int)> water, float gx, float gz)
    {
        // 检查是否在任何水格内部
        int ix = Mathf.FloorToInt(gx);
        int iz = Mathf.FloorToInt(gz);
        if (ix >= 0 && iz >= 0 && water.Contains((ix, iz)))
            return 0f;

        float minDist = 999f;
        foreach (var (wx, wz) in water)
        {
            // 点到矩形 (wx, wz) ~ (wx+1, wz+1) 的最短距离
            float cx = Mathf.Clamp(gx, wx, wx + 1f);
            float cz = Mathf.Clamp(gz, wz, wz + 1f);
            float d = Mathf.Sqrt((gx - cx) * (gx - cx) + (gz - cz) * (gz - cz));
            if (d < minDist) minDist = d;
        }
        return minDist;
    }

    static float SampleBilinear(float[,] dist, int mapW, int mapH, float gx, float gz)
    {
        gx = Mathf.Clamp(gx, 0f, mapW);
        gz = Mathf.Clamp(gz, 0f, mapH);
        int x0 = Mathf.FloorToInt(gx);
        int z0 = Mathf.FloorToInt(gz);
        int x1 = Mathf.Min(x0 + 1, mapW);
        int z1 = Mathf.Min(z0 + 1, mapH);
        float fx = gx - x0;
        float fz = gz - z0;
        return Mathf.Lerp(
            Mathf.Lerp(dist[x0, z0], dist[x1, z0], fx),
            Mathf.Lerp(dist[x0, z1], dist[x1, z1], fx),
            fz);
    }

    // ── TerrainLayer ──

    static TerrainLayer CreateProceduralGrassLayer()
    {
        int s = 16;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Repeat;
        var colors = new Color[s * s];
        for (int i = 0; i < colors.Length; i++)
        {
            float r = 0.22f + Random.value * 0.08f;
            float g = 0.38f + Random.value * 0.12f;
            float b = 0.14f + Random.value * 0.06f;
            colors[i] = new Color(r, g, b, 1f);
        }
        tex.SetPixels(colors);
        tex.Apply();

        var layer = new TerrainLayer();
        layer.name = "ProceduralGrass";
        layer.diffuseTexture = tex;
        layer.tileSize = new Vector2(4f, 4f);
        return layer;
    }
}
