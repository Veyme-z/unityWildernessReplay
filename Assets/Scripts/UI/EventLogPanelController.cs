using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 左侧事件日志面板 — 独立 Canvas，单层内容结构，RectMask2D 裁切。
/// </summary>
public class EventLogPanelController : MonoBehaviour
{
    [SerializeField] Text _text;
    [SerializeField] ScrollRect _scroll;
    readonly StringBuilder _sb = new StringBuilder();
    const int MAX_CHARS = 8000;

    static readonly Color C_MOVE   = new Color(0.65f, 0.65f, 0.68f);
    static readonly Color C_DAMAGE = new Color(0.94f, 0.42f, 0.38f);
    static readonly Color C_TASK   = new Color(0.96f, 0.72f, 0.22f);
    static readonly Color C_BUILD  = new Color(0.42f, 0.78f, 0.54f);
    static readonly Color C_BEAST  = new Color(0.70f, 0.50f, 0.88f);
    static readonly Color C_KILL   = new Color(0.96f, 0.34f, 0.28f);
    static readonly Color C_INFO   = new Color(0.75f, 0.73f, 0.68f);

    static Font BuiltinFont()
    {
#if UNITY_2022_1_OR_NEWER
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
        return Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
    }

    public static EventLogPanelController Create(ReplayPlayer player)
    {
        // 优先使用 prefab（如果有配置），否则退回纯代码创建
        var prefab = PrefabRefs.Instance.GetEventLogPrefab();
        if (prefab != null)
        {
            var go = Object.Instantiate(prefab);
            var ctrl = go.GetComponentInChildren<EventLogPanelController>();
            if (ctrl == null) ctrl = go.AddComponent<EventLogPanelController>();
            return ctrl;
        }
        Debug.LogWarning("[EventLogPanelController] EventLogPanel prefab 缺失，回退到代码创建 UI（请检查场景 PrefabRefs 或 Resources/Prefabs/UI/EventLogPanel）。");
        return CreateFromCode(player);
    }

    static EventLogPanelController CreateFromCode(ReplayPlayer player)
    {
        var font = BuiltinFont();

        // ── 独立 Canvas ──
        var canvasGo = new GameObject("EventLogCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 210;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // ── 面板（暗色背景） ──
        var panelGo = new GameObject("Panel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        var panelRt = panelGo.AddComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0, 0);
        panelRt.anchorMax = new Vector2(0, 1);
        panelRt.pivot = new Vector2(0, 0.5f);
        panelRt.anchoredPosition = new Vector2(10, 0);
        panelRt.sizeDelta = new Vector2(290, -70);
        panelGo.AddComponent<Image>().color = new Color(0.102f, 0.102f, 0.118f, 0.85f);

        // ── ScrollRect（挂 RectMask2D 裁切溢出） ──
        var srGo = new GameObject("ScrollView");
        srGo.transform.SetParent(panelGo.transform, false);
        var srRt = srGo.AddComponent<RectTransform>();
        srRt.anchorMin = Vector2.zero;
        srRt.anchorMax = Vector2.one;
        srRt.offsetMin = new Vector2(6, 6);
        srRt.offsetMax = new Vector2(-6, -6);
        srGo.AddComponent<RectMask2D>();
        var sr = srGo.AddComponent<ScrollRect>();
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.viewport = srRt;

        // ── Content（Text 直接挂在 Content 上，单层结构） ──
        var ctGo = new GameObject("Content");
        ctGo.transform.SetParent(srGo.transform, false);
        var ctRt = ctGo.AddComponent<RectTransform>();
        ctRt.anchorMin = new Vector2(0, 1);
        ctRt.anchorMax = new Vector2(1, 1);
        ctRt.pivot = new Vector2(0, 1);
        ctRt.anchoredPosition = Vector2.zero;
        ctRt.sizeDelta = new Vector2(0, 0);
        var csf = ctGo.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Text 直接挂 Content，不做独立子节点
        var txt = ctGo.AddComponent<Text>();
        txt.font = font;
        txt.fontSize = 15;
        txt.alignment = TextAnchor.UpperLeft;
        txt.horizontalOverflow = HorizontalWrapMode.Wrap;
        txt.verticalOverflow = VerticalWrapMode.Overflow;
        txt.color = C_INFO;
        txt.raycastTarget = false;
        txt.text = "📋 事件日志就绪\n等待对局事件...\n";

        sr.content = ctRt;

        var ctrl = panelGo.AddComponent<EventLogPanelController>();
        ctrl._text = txt;
        ctrl._scroll = sr;
        return ctrl;
    }

    public void AddEventLog(string message, string category)
    {
        Color c = CategoryColor(category);
        string hex = ColorUtility.ToHtmlStringRGB(c);
        _sb.Append("<color=#").Append(hex).Append(">").Append(message).Append("</color>\n");

        if (_sb.Length > MAX_CHARS)
        {
            int cut = _sb.Length - MAX_CHARS;
            int nl = _sb.ToString().IndexOf('\n', cut);
            if (nl >= 0) _sb.Remove(0, nl + 1);
        }

        _text.text = _sb.ToString();

        // 刷新布局后滚底
        Canvas.ForceUpdateCanvases();
        if (_scroll != null)
            _scroll.verticalNormalizedPosition = 0f;
    }

    static Color CategoryColor(string cat)
    {
        switch (cat)
        {
            case "kill":   return C_KILL;
            case "damage": return C_DAMAGE;
            case "task":   return C_TASK;
            case "trade":  return C_TASK;
            case "build":  return C_BUILD;
            case "beast":  return C_BEAST;
            case "move":   return C_MOVE;
            case "cmd":    return C_MOVE;
            default:       return C_INFO;
        }
    }
}
