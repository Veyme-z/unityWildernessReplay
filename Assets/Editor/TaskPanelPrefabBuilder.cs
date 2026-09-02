#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 一次性/可重复编辑器工具：生成推理类 + 长上下文任务面板 Prefab（右上角，PriceChart 上方不重叠）。
///  - 程序化构建 TaskPanelReasoning.prefab / TaskPanelLongContext.prefab（独立 Canvas）
///  - 把两个 prefab 按 GUID 接线进当前场景的 PrefabRefs 组件
/// 依赖：TaskPanelController / PrefabRefs 脚本已编译（先改脚本再跑本工具）。
/// </summary>
public static class TaskPanelPrefabBuilder
{
    const string REASONING_PATH = "Assets/Prefabs/UI/TaskPanelReasoning.prefab";
    const string LONGCTX_PATH = "Assets/Prefabs/UI/TaskPanelLongContext.prefab";

    static readonly Color BG_COLOR     = new Color(0.102f, 0.102f, 0.118f, 0.85f);   // #1A1A1E α0.85（同现有面板）
    static readonly Color TITLE_COLOR  = new Color(0.96f, 0.72f, 0.22f);              // 金黄
    static readonly Color BODY_COLOR   = new Color(0.75f, 0.73f, 0.68f);              // 灰白

    [MenuItem("Tools/WildernessReplay/Build Task Panel Prefabs")]
    public static void Build()
    {
        // 右上角堆叠（PriceChart 在 (-10,-490) 340×240，两个面板在其上方，避免重叠）：
        //   推理类 (-10,-10) 300×190（官方消息最长 190 字 → body ~150px）；长上下文 (-10,-210) 300×270（民间传闻最长 293 字 → body ~230px）
        var reasoning = BuildPanel("TaskPanelReasoning", TaskPanelKind.Reasoning, "【推理类任务】",
            215, new Vector2(-10, -10), new Vector2(300, 190), "暂无官方消息", REASONING_PATH);
        var longctx = BuildPanel("TaskPanelLongContext", TaskPanelKind.LongContext, "【长上下文任务】",
            216, new Vector2(-10, -210), new Vector2(300, 270), "暂无民间传闻", LONGCTX_PATH);
        if (reasoning == null || longctx == null) return;

        WireScene(reasoning, longctx);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[TaskPanelPrefabBuilder] 完成：TaskPanelReasoning + TaskPanelLongContext prefab 已生成并接线到场景 PrefabRefs。");
    }

    static GameObject BuildPanel(string rootName, TaskPanelKind kind, string titleText, int sortingOrder,
        Vector2 pos, Vector2 size, string emptyText, string path)
    {
        // 根：独立 Canvas（ScreenSpaceOverlay，与其余 UI prefab 同栈）
        var root = new GameObject(rootName);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();

        // 面板底（右上角，锚 (1,1) pivot (1,1)，负 x/y 偏移）
        var panel = new GameObject("Panel");
        panel.transform.SetParent(root.transform, false);
        var prt = panel.AddComponent<RectTransform>();
        prt.anchorMin = new Vector2(1, 1);
        prt.anchorMax = new Vector2(1, 1);
        prt.pivot = new Vector2(1, 1);
        prt.anchoredPosition = pos;
        prt.sizeDelta = size;
        var img = panel.AddComponent<Image>();
        img.color = BG_COLOR;
        img.raycastTarget = false;

        // 标题 + 正文
        var title = MakeText(panel.transform, "Title", titleText, 14,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, -8),
            new Vector2(size.x - 20, 22), TITLE_COLOR);
        var body = MakeText(panel.transform, "Body", emptyText, 13,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, -34),
            new Vector2(size.x - 20, size.y - 44), BODY_COLOR);
        body.horizontalOverflow = HorizontalWrapMode.Wrap;
        body.verticalOverflow = VerticalWrapMode.Overflow;

        // 挂控制器并连线（private serialized 字段走 SerializedObject）
        var ctrl = root.AddComponent<TaskPanelController>();
        var so = new SerializedObject(ctrl);
        so.FindProperty("_kind").intValue = (int)kind;
        so.FindProperty("_title").objectReferenceValue = title;
        so.FindProperty("_body").objectReferenceValue = body;
        so.ApplyModifiedPropertiesWithoutUndo();

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        if (prefab == null) { Debug.LogError("[TaskPanelPrefabBuilder] 保存失败 " + path); return null; }
        Debug.Log("[TaskPanelPrefabBuilder] 已生成 " + path);
        return prefab;
    }

    static Text MakeText(Transform parent, string name, string text, int fontSize,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size, Color color)
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
        t.font = UiFonts.Get();
        t.fontSize = fontSize;
        t.alignment = TextAnchor.UpperLeft;
        t.color = color;
        t.raycastTarget = false;
        return t;
    }

    static void WireScene(GameObject reasoning, GameObject longctx)
    {
        var refs = Object.FindObjectOfType<PrefabRefs>();
        if (refs == null)
        {
            Debug.LogError("[TaskPanelPrefabBuilder] 场景无 PrefabRefs，无法接线任务面板 prefab。");
            return;
        }
        refs.taskPanelReasoningPrefab = reasoning;
        refs.taskPanelLongContextPrefab = longctx;
        EditorSceneManager.MarkSceneDirty(refs.gameObject.scene);
        EditorSceneManager.SaveScene(refs.gameObject.scene);
        Debug.Log("[TaskPanelPrefabBuilder] 已接线场景 PrefabRefs：taskPanelReasoningPrefab / taskPanelLongContextPrefab。");
    }
}
#endif
