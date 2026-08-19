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

    public static EventLogPanelController Create(ReplayPlayer player)
    {
        // prefab 是真源：场景 PrefabRefs 按 GUID 引用，缺失即报错（不再有纯代码兜底）
        var prefab = PrefabRefs.Instance.GetEventLogPrefab();
        if (prefab == null)
        {
            Debug.LogError("[EventLogPanelController] 缺少 EventLogPanel prefab（请检查场景 PrefabRefs.eventLogPanelPrefab），事件日志未创建。");
            return null;
        }
        var go = Object.Instantiate(prefab);
        UiFonts.Apply(go.transform);   // 覆盖 prefab 里烘焙的旧字体
        var ctrl = go.GetComponentInChildren<EventLogPanelController>();
        if (ctrl == null) ctrl = go.AddComponent<EventLogPanelController>();
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
