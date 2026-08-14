using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 右侧任务面板 — 独立 Canvas，位于右上角。
/// 当前为占位内容：显示「推理类任务」分类，正文为【官方消息】，每 130 回合轮换一条。
/// </summary>
public class TaskPanelController : MonoBehaviour
{
    [SerializeField] Text _title;
    [SerializeField] Text _category;
    [SerializeField] Text _body;

    ReplayPlayer _player;
    int _lastDay = -1;

    // 占位官方消息（每 130 回合轮换一条）
    static readonly string[] NEWS = new string[]
    {
        "矿业管理局紧急通报：北部铁矿区昨夜发生严重矿井塌方事故，主巷道结构受损，部分作业面被掩埋。",
        "安全监察部门已下达通知：为保障矿工安全，矿区将于明日全面停工，进行巷道加固和主矿脉修复。"
    };

    static Font BuiltinFont()
    {
        return UiFonts.Get();
    }

    public static TaskPanelController Create(ReplayPlayer player)
    {
        var font = BuiltinFont();

        // ── 独立 Canvas ──
        var canvasGo = new GameObject("TaskPanelCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 210; // 与事件日志同级
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // ── 面板（右上角，紧凑尺寸） ──
        var panelGo = new GameObject("Panel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        var rt = panelGo.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(1, 1);
        rt.anchoredPosition = new Vector2(-10, -10);
        rt.sizeDelta = new Vector2(280, 240);
        panelGo.AddComponent<Image>().color = new Color(0.102f, 0.102f, 0.118f, 0.85f);

        // ── 标题（居中） ──
        var title = MakeText(panelGo.transform, "Title", "任务面板", font, 16,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -8), new Vector2(260, 24),
            Color.white, TextAnchor.MiddleCenter);

        // ── 分类：推理类任务（左对齐） ──
        var category = MakeText(panelGo.transform, "Category", "【推理类任务】", font, 14,
            new Vector2(0, 1f), new Vector2(0, 1f), new Vector2(0, 1f), new Vector2(12, -40), new Vector2(256, 20),
            new Color(0.96f, 0.72f, 0.22f), TextAnchor.UpperLeft);

        // ── 正文：官方消息（轮换） ──
        var body = MakeText(panelGo.transform, "Body", "", font, 13,
            new Vector2(0, 1f), new Vector2(0, 1f), new Vector2(0, 1f), new Vector2(12, -64), new Vector2(256, 160),
            new Color(0.75f, 0.73f, 0.68f), TextAnchor.UpperLeft);
        body.horizontalOverflow = HorizontalWrapMode.Wrap;
        body.verticalOverflow = VerticalWrapMode.Overflow;

        var ctrl = panelGo.AddComponent<TaskPanelController>();
        ctrl._title = title;
        ctrl._category = category;
        ctrl._body = body;
        ctrl._player = player;
        ctrl._lastDay = -1;
        ctrl.Sync(player);
        return ctrl;
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

    void Update()
    {
        if (_player != null) Sync(_player);
    }

    void Sync(ReplayPlayer p)
    {
        if (p == null) return;
        int day = (p.cur - 1) / 130;      // 每 130 回合 = 一天
        if (day == _lastDay) return;
        _lastDay = day;
        int idx = day % NEWS.Length;
        if (_body != null)
            _body.text = "【官方消息】\n" + NEWS[idx];
    }
}
