using UnityEngine;

/// <summary>
/// Prefab 引用中心：所有 prefab 引用集中管理。
/// 用法：挂到场景中的 GameObject 上，在 Inspector 中拖入 prefab 引用。
/// 若场景中没有，会自动创建（通过 Ensure）。
///
/// 加载策略（按优先级）：
///   1. [SerializeField] 字段（Inspector 拖入）— 首选
///   2. Resources.Load 路径 — fallback
/// </summary>
public class PrefabRefs : MonoBehaviour
{
    // ═══════════════════════════════════════════════
    // 单位 Prefab
    // ═══════════════════════════════════════════════
    [Header("单位 Prefab")]
    [Tooltip("单位通用模板（含 HP条/选择圈/碰撞体，不含具体视觉）")]
    public GameObject unitBasePrefab;

    [Header("3D 野兽模型（FBX 源文件）")]
    public GameObject beastModel11;
    public GameObject beastModel12;
    public GameObject beastModel13;
    public GameObject beastModel14;

    [Header("Robot 替换模型（优先级高于 Skeleton）")]
    public GameObject robotModel11;
    public GameObject robotModel12;
    public GameObject robotModel13;
    public GameObject robotModel14;

    [Header("3D 建筑模型")]
    [Tooltip("基地 (type 4)")]
    public GameObject baseBuildingPrefab;
    [Tooltip("防御塔 (type 3)")]
    public GameObject towerBuildingPrefab;
    [Tooltip("围墙 (type 5)")]
    public GameObject wallBuildingPrefab;

    [Header("3D 角色模型")]
    [Tooltip("工人 (type 6) → Barbarian")]
    public GameObject workerPrefab;
    [Tooltip("开拓者 (type 7) → Rogue_Hooded")]
    public GameObject pioneerPrefab;
    [Tooltip("任务官 NPC → Knight")]
    public GameObject officerNpcPrefab;
    [Tooltip("小贩 NPC → Ranger")]
    public GameObject vendorNpcPrefab;

    // ═══════════════════════════════════════════════
    // UI Prefab
    // ═══════════════════════════════════════════════
    [Header("UI Prefab")]
    public GameObject hudPanelPrefab;
    public GameObject eventLogPanelPrefab;
    public GameObject playbackControlPanelPrefab;
    public GameObject settlementPanelPrefab;
    [Tooltip("推理类任务面板（右上角，世界新闻【官方消息】）")]
    public GameObject taskPanelReasoningPrefab;
    [Tooltip("长上下文任务面板（右上角，世界新闻【民间传闻】）")]
    public GameObject taskPanelLongContextPrefab;
    [Tooltip("资源价格走势卡片（右上角，折线图）")]
    public GameObject priceChartPrefab;

    // ═══════════════════════════════════════════════
    // 单例
    // ═══════════════════════════════════════════════
    static PrefabRefs _instance;

    public static PrefabRefs Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<PrefabRefs>();
                if (_instance == null)
                {
                    var go = new GameObject("PrefabRefs (auto)");
                    _instance = go.AddComponent<PrefabRefs>();
                    DontDestroyOnLoad(go);
                }
            }
            return _instance;
        }
    }

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ═══════════════════════════════════════════════
    // 便捷查询方法
    // ═══════════════════════════════════════════════

    /// <summary>单位 prefab 是否存在（非 null 即视为已配置）</summary>
    public bool HasUnitPrefab => unitBasePrefab != null;

    /// <summary>HUD 面板 prefab 是否存在</summary>
    public bool HasHudPrefab => hudPanelPrefab != null;

    /// <summary>事件日志面板 prefab 是否存在</summary>
    public bool HasEventLogPrefab => eventLogPanelPrefab != null;

    /// <summary>底部控制面板 prefab 是否存在</summary>
    public bool HasPlaybackControlPrefab => playbackControlPanelPrefab != null;

    /// <summary>结算面板 prefab 是否存在</summary>
    public bool HasSettlementPrefab => settlementPanelPrefab != null;

    /// <summary>任务面板 prefab 是否存在（按类别）</summary>
    public bool HasTaskPanelPrefab(TaskPanelKind kind) =>
        (kind == TaskPanelKind.Reasoning ? taskPanelReasoningPrefab : taskPanelLongContextPrefab) != null;

    // ═══════════════════════════════════════════════
    // Resources 路径（当 Inspector 引用为空时的 fallback）
    // ═══════════════════════════════════════════════
    const string RES_UNIT_BASE = "Prefabs/Units/UnitBase";
    const string RES_HUD = "Prefabs/UI/HudPanel";
    const string RES_EVENT_LOG = "Prefabs/UI/EventLogPanel";
    const string RES_PLAYBACK = "Prefabs/UI/PlaybackControlPanel";
    const string RES_SETTLEMENT = "Prefabs/UI/SettlementPanel";
    const string RES_TASK_REASONING = "Prefabs/UI/TaskPanelReasoning";
    const string RES_TASK_LONGCTX = "Prefabs/UI/TaskPanelLongContext";
    const string RES_PRICE_CHART = "Prefabs/UI/PriceChartCard";

    /// <summary>
    /// 获取单位 prefab：先查 Inspector 引用 → 再查 Resources。
    /// 都失败返回 null，调用方应 fallback 到代码创建。
    /// </summary>
    public GameObject GetUnitPrefab()
    {
        if (unitBasePrefab != null) return unitBasePrefab;
        return Resources.Load<GameObject>(RES_UNIT_BASE);
    }

    public GameObject GetHudPrefab()
    {
        if (hudPanelPrefab != null) return hudPanelPrefab;
        return Resources.Load<GameObject>(RES_HUD);
    }

    public GameObject GetEventLogPrefab()
    {
        if (eventLogPanelPrefab != null) return eventLogPanelPrefab;
        return Resources.Load<GameObject>(RES_EVENT_LOG);
    }

    public GameObject GetPlaybackControlPrefab()
    {
        if (playbackControlPanelPrefab != null) return playbackControlPanelPrefab;
        return Resources.Load<GameObject>(RES_PLAYBACK);
    }

    public GameObject GetSettlementPrefab()
    {
        if (settlementPanelPrefab != null) return settlementPanelPrefab;
        return Resources.Load<GameObject>(RES_SETTLEMENT);
    }

    /// <summary>按类别获取任务面板 prefab：先查 Inspector 引用 → 再查 Resources。</summary>
    public GameObject GetTaskPanelPrefab(TaskPanelKind kind)
    {
        if (kind == TaskPanelKind.Reasoning)
            return taskPanelReasoningPrefab != null ? taskPanelReasoningPrefab : Resources.Load<GameObject>(RES_TASK_REASONING);
        return taskPanelLongContextPrefab != null ? taskPanelLongContextPrefab : Resources.Load<GameObject>(RES_TASK_LONGCTX);
    }

    /// <summary>获取资源价格走势卡片 prefab：先查 Inspector 引用 → 再查 Resources。</summary>
    public GameObject GetPriceChartPrefab()
    {
        if (priceChartPrefab != null) return priceChartPrefab;
        return Resources.Load<GameObject>(RES_PRICE_CHART);
    }
}
