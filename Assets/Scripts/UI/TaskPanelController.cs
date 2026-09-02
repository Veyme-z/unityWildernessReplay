using UnityEngine;
using UnityEngine.UI;

/// <summary>任务面板类别。</summary>
public enum TaskPanelKind
{
    Reasoning,    // 推理类任务 — 世界新闻【官方消息】
    LongContext   // 长上下文任务 — 世界新闻【民间传闻】
}

/// <summary>
/// 推理类 / 长上下文任务面板 — Prefab 驱动，实时读取当前回合的世界新闻。
/// 推理类显示【官方消息】、长上下文显示【民间传闻】，数据源均为 ReplayRound.news。
///
/// 刷新策略：随 replay 播放推进更新；拖动进度条 / Seek 到任意回合时，
/// 从目标回合向前扫描最近一条匹配本面板类别的新闻显示（非新闻回合不闪"暂无"）。
/// 当前所有 replay 数据 news 为空时显示占位文案。
///
/// Prefab 是真源（场景 PrefabRefs.taskPanelReasoningPrefab / taskPanelLongContextPrefab 按 GUID 引用）；
/// 缺失时 Create() 报错并返回 null（与其余 UI Controller 一致，无纯代码兜底）。
/// </summary>
public class TaskPanelController : MonoBehaviour
{
    [SerializeField] TaskPanelKind _kind;
    [SerializeField] Text _title;
    [SerializeField] Text _body;

    ReplayPlayer _player;
    int _lastRound = -1;

    public static TaskPanelController Create(ReplayPlayer player, TaskPanelKind kind)
    {
        // prefab 是真源：场景 PrefabRefs 按 GUID 引用，缺失即报错（不再有纯代码兜底）
        var prefab = PrefabRefs.Instance.GetTaskPanelPrefab(kind);
        if (prefab == null)
        {
            Debug.LogError("[TaskPanelController] 缺少 TaskPanel prefab（请检查场景 PrefabRefs."
                + (kind == TaskPanelKind.Reasoning ? "taskPanelReasoningPrefab" : "taskPanelLongContextPrefab")
                + "），任务面板未创建。");
            return null;
        }
        var go = Object.Instantiate(prefab);
        UiFonts.Apply(go.transform);   // 覆盖 prefab 里烘焙的旧字体
        var ctrl = go.GetComponentInChildren<TaskPanelController>();
        if (ctrl == null) ctrl = go.AddComponent<TaskPanelController>();
        ctrl._kind = kind;
        ctrl._player = player;
        return ctrl;
    }

    void Update()
    {
        if (_player == null || _player.data == null) return;
        int round = _player.cur;
        if (round < 1 || round > _player.data.rounds.Count) return;
        if (round == _lastRound) return;   // 回合未变不刷新
        _lastRound = round;
        if (_body != null) _body.text = FindLatestNews(round);
    }

    /// <summary>从当前回合向前扫描，返回最近一条匹配本面板类别的新闻正文；没有则返回占位文案。
    /// 新格式直接读 news.officialNews（推理类）/ news.folkLegends（长上下文）；
    /// 旧数组格式 news[] 按关键词兜底。</summary>
    string FindLatestNews(int round)
    {
        for (int r = round; r >= 1; r--)
        {
            var rr = _player.data.rounds[r - 1];
            if (rr == null) continue;
            // 新对象格式：官方消息 / 民间传闻
            string text = _kind == TaskPanelKind.Reasoning ? rr.officialNews : rr.folkLegends;
            if (!string.IsNullOrEmpty(text)) return text;
            // 兼容旧数组格式
            if (rr.news != null)
                for (int i = rr.news.Count - 1; i >= 0; i--)
                {
                    var n = rr.news[i];
                    if (n == null || string.IsNullOrEmpty(n.text)) continue;
                    if (Matches(n)) return n.text;
                }
        }
        return _kind == TaskPanelKind.Reasoning ? "暂无官方消息" : "暂无民间传闻";
    }

    /// <summary>类别匹配（仅旧数组格式用）：官方消息→推理类；民间传闻→长上下文（type 或 text 含关键词即命中）。</summary>
    bool Matches(ReplayNews n)
    {
        string type = n.type ?? "";
        string text = n.text ?? "";
        bool isReasoning = type.IndexOf("官方", System.StringComparison.Ordinal) >= 0
            || text.IndexOf("官方消息", System.StringComparison.Ordinal) >= 0;
        bool isLongContext = type.IndexOf("民间", System.StringComparison.Ordinal) >= 0
            || type.IndexOf("传闻", System.StringComparison.Ordinal) >= 0
            || text.IndexOf("民间传闻", System.StringComparison.Ordinal) >= 0;
        return _kind == TaskPanelKind.Reasoning ? isReasoning : isLongContext;
    }
}
