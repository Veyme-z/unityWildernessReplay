#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 一次性/可重复编辑器工具：生成资源价格走势卡片 Prefab（右上角）。
/// 静态布局（标题/单位标注/图例/图表区位置）全部进 prefab，用户可直接在 prefab 调整；
/// 只有图表纹理 + 数值/天数轴标签由 PriceChartCard 运行时从 replay 读取绘制。
/// 依赖：PriceChartCard / PrefabRefs 脚本已编译。
/// </summary>
public static class PriceChartPrefabBuilder
{
    const string PATH = "Assets/Prefabs/UI/PriceChartCard.prefab";

    static readonly Color UNIT_COLOR = new Color(0.75f, 0.73f, 0.68f);
    static readonly Color STONE  = new Color(0.72f, 0.38f, 0.95f);
    static readonly Color COPPER = new Color(0.92f, 0.60f, 0.22f);
    static readonly Color IRON   = new Color(0.95f, 0.35f, 0.30f);

    [MenuItem("Tools/WildernessReplay/Build Price Chart Prefab")]
    public static void Build()
    {
        // 根 Canvas（独立，sortingOrder 211）
        var root = new GameObject("PriceChartCanvas");
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 211;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();

        var font = UiFonts.Get();

        // 面板（右上角，宽度与任务面板一致 300）
        var panel = new GameObject("Panel");
        panel.transform.SetParent(root.transform, false);
        var prt = panel.AddComponent<RectTransform>();
        prt.anchorMin = new Vector2(1, 1);
        prt.anchorMax = new Vector2(1, 1);
        prt.pivot = new Vector2(1, 1);
        prt.anchoredPosition = new Vector2(-10, -490);
        prt.sizeDelta = new Vector2(300, 240);
        var img = panel.AddComponent<Image>();
        img.color = new Color(0.102f, 0.102f, 0.118f, 0.85f);
        img.raycastTarget = false;

        // 标题
        var title = MakeText(panel.transform, "Title", "资源价格走势", font, 15,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -6), new Vector2(280, 22),
            Color.white, TextAnchor.MiddleCenter);

        // 单位标注（可在 prefab 调整位置）
        MakeText(panel.transform, "UnitY", "单位：金币", font, 13,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10, -10), new Vector2(90, 18),
            UNIT_COLOR, TextAnchor.UpperLeft);
        MakeText(panel.transform, "UnitX", "单位：天", font, 13,
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-15, 30), new Vector2(70, 18),
            UNIT_COLOR, TextAnchor.LowerRight);

        // 图表区（RawImage：运行时由代码绘制 replay 数据纹理）
        var chartGo = new GameObject("Chart");
        chartGo.transform.SetParent(panel.transform, false);
        var crt = chartGo.AddComponent<RectTransform>();
        crt.anchorMin = new Vector2(0.5f, 0.5f);
        crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.pivot = new Vector2(0.5f, 0.5f);
        crt.anchoredPosition = new Vector2(20, 8);
        crt.sizeDelta = new Vector2(250, 168);
        var raw = chartGo.AddComponent<RawImage>();
        raw.raycastTarget = false;

        // 无数据提示（默认隐藏）
        var empty = MakeText(panel.transform, "Empty", "本回放不含小贩回收价数据", font, 13,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(280, 80),
            new Color(0.7f, 0.68f, 0.62f), TextAnchor.MiddleCenter);
        empty.gameObject.SetActive(false);

        // 图例（可在 prefab 调整位置/颜色）
        MakeLegendItem(panel.transform, "石头", STONE, -40, font);
        MakeLegendItem(panel.transform, "铜", COPPER, 30, font);
        MakeLegendItem(panel.transform, "铁", IRON, 100, font);

        // 控制器挂根上并连线
        var ctrl = root.AddComponent<PriceChartCard>();
        var so = new SerializedObject(ctrl);
        so.FindProperty("_title").objectReferenceValue = title;
        so.FindProperty("_chartImage").objectReferenceValue = raw;
        so.FindProperty("_emptyLabel").objectReferenceValue = empty;
        so.ApplyModifiedPropertiesWithoutUndo();

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PATH);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        if (prefab == null) { Debug.LogError("[PriceChartPrefabBuilder] 保存失败 " + PATH); return; }

        // 接线场景 PrefabRefs
        var refs = Object.FindObjectOfType<PrefabRefs>();
        if (refs != null)
        {
            refs.priceChartPrefab = prefab;
            EditorSceneManager.MarkSceneDirty(refs.gameObject.scene);
            EditorSceneManager.SaveScene(refs.gameObject.scene);
            Debug.Log("[PriceChartPrefabBuilder] 完成：" + PATH + " + 已接线场景 PrefabRefs.priceChartPrefab。");
        }
        else Debug.LogWarning("[PriceChartPrefabBuilder] 场景无 PrefabRefs，未接线（prefab 已生成：" + PATH + "）。");
    }

    static Text MakeText(Transform parent, string name, string text, Font font, int fontSize,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size, Color color, TextAnchor align)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        var t = go.AddComponent<Text>();
        t.text = text;
        t.font = font;
        t.fontSize = fontSize;
        t.alignment = align;
        t.color = color;
        t.raycastTarget = false;
        return t;
    }

    static void MakeLegendItem(Transform parent, string label, Color color, float x, Font font)
    {
        var go = new GameObject("Legend_" + label);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, 4);
        rt.sizeDelta = new Vector2(80, 18);

        var dot = new GameObject("Dot");
        dot.transform.SetParent(go.transform, false);
        var drt = dot.AddComponent<RectTransform>();
        drt.anchorMin = new Vector2(0f, 0.5f);
        drt.anchorMax = new Vector2(0f, 0.5f);
        drt.pivot = new Vector2(0f, 0.5f);
        drt.anchoredPosition = new Vector2(0, 0);
        drt.sizeDelta = new Vector2(10, 10);
        var di = dot.AddComponent<Image>();
        di.color = color;
        di.raycastTarget = false;

        MakeText(go.transform, "L", label, font, 12,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14, 0), new Vector2(60, 18),
            new Color(0.85f, 0.85f, 0.85f), TextAnchor.MiddleLeft);
    }
}
#endif
