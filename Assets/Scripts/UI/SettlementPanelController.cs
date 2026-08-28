using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 结算画面面板 — 显示胜负结果和重新观看按钮。
/// Prefab 是真源（场景 PrefabRefs.settlementPanelPrefab 按 GUID 引用）；缺失时 Create() 报错并返回 null。
/// </summary>
public class SettlementPanelController : MonoBehaviour
{
    [Header("UI 引用（prefab 中连线或代码赋值）")]
    [SerializeField] Text _titleText;
    [SerializeField] Text _scoreText;
    [SerializeField] Text _redResultText;
    [SerializeField] Text _blueResultText;
    [SerializeField] Text _redStatsText;   // 红方列：积分 + 存活天数（两行）
    [SerializeField] Text _blueStatsText;  // 蓝方列：积分 + 存活天数（两行）
    [SerializeField] Button _restartBtn;

    /// <summary>
    /// 创建结算面板。
    /// </summary>
    /// <param name="p0Name">红方名称</param>
    /// <param name="p0Result">红方结果 victory/defeat/draw</param>
    /// <param name="p0Score">红方最终积分（判题器 finish）</param>
    /// <param name="p0Days">红方存活天数</param>
    /// <param name="p1Name">蓝方名称</param>
    /// <param name="p1Result">蓝方结果</param>
    /// <param name="p1Score">蓝方最终积分</param>
    /// <param name="p1Days">蓝方存活天数</param>
    /// <param name="onRestart">重新观看回调</param>
    public static SettlementPanelController Create(
        string p0Name, string p0Result, int p0Score, int p0Days,
        string p1Name, string p1Result, int p1Score, int p1Days,
        UnityAction onRestart)
    {
        // prefab 是真源：场景 PrefabRefs 按 GUID 引用，缺失即报错（不再有纯代码兜底）
        var prefab = PrefabRefs.Instance.GetSettlementPrefab();
        if (prefab == null)
        {
            Debug.LogError("[SettlementPanelController] 缺少 SettlementPanel prefab（请检查场景 PrefabRefs.settlementPanelPrefab），结算面板未创建。");
            return null;
        }
        var go = Object.Instantiate(prefab);
        UiFonts.Apply(go.transform);   // 覆盖 prefab 里烘焙的旧字体
        var ctrl = go.GetComponentInChildren<SettlementPanelController>();
        if (ctrl == null) ctrl = go.AddComponent<SettlementPanelController>();
        ctrl.Setup(p0Name, p0Result, p0Score, p0Days, p1Name, p1Result, p1Score, p1Days, onRestart);
        return ctrl;
    }



    /// <summary>
    /// Prefab 模式：填充数据并连线回调。
    /// 文本格式由 prefab 里的模板决定（改 prefab 文本即改格式），运行时只替换 {占位符}：
    ///   {winner} 胜利方名称、{name} 当前队伍名、{result} 胜利/失败、
    ///   {score} 积分、{days} 存活天数
    /// </summary>
    void Setup(string p0Name, string p0Result, int p0Score, int p0Days,
               string p1Name, string p1Result, int p1Score, int p1Days,
               UnityAction onRestart)
    {
        string p0ResultCn = p0Result == "victory" ? "胜利" : "失败";
        string p1ResultCn = p1Result == "victory" ? "胜利" : "失败";
        string winner = p0Result == "victory" ? p0Name : (p1Result == "victory" ? p1Name : "平局");

        if (_titleText != null)
            _titleText.text = winner == "平局" ? "平局" : Fill(_titleText.text, "winner", winner);
        // 旧单行积分隐藏，改用红蓝两列（各两行：积分 + 存活天数），格式看 prefab 模板
        if (_scoreText != null) _scoreText.gameObject.SetActive(false);
        if (_redStatsText != null)
            _redStatsText.text = Fill(_redStatsText.text, "name", p0Name, "result", p0ResultCn, "score", p0Score.ToString(), "days", p0Days.ToString());
        if (_blueStatsText != null)
            _blueStatsText.text = Fill(_blueStatsText.text, "name", p1Name, "result", p1ResultCn, "score", p1Score.ToString(), "days", p1Days.ToString());
        if (_redResultText != null)
            _redResultText.text = Fill(_redResultText.text, "name", p0Name, "result", p0ResultCn, "score", p0Score.ToString(), "days", p0Days.ToString());
        if (_blueResultText != null)
            _blueResultText.text = Fill(_blueResultText.text, "name", p1Name, "result", p1ResultCn, "score", p1Score.ToString(), "days", p1Days.ToString());
        if (_restartBtn != null)
            _restartBtn.onClick.AddListener(onRestart);
    }

    /// <summary>模板替换：把模板里的 {key} 替换为对应值；无占位符则原样返回（保留 prefab 里的自定义文字）。</summary>
    static string Fill(string template, params string[] kv)
    {
        if (string.IsNullOrEmpty(template)) return template;
        string s = template;
        for (int i = 0; i + 1 < kv.Length; i += 2)
            s = s.Replace("{" + kv[i] + "}", kv[i + 1]);
        return s;
    }




}
