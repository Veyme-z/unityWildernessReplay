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
    [SerializeField] Button _restartBtn;

    /// <summary>
    /// 创建结算面板。
    /// </summary>
    /// <param name="p0Name">红方名称</param>
    /// <param name="p0Result">红方结果 victory/defeat/draw</param>
    /// <param name="p0Score">红方分数</param>
    /// <param name="p1Name">蓝方名称</param>
    /// <param name="p1Result">蓝方结果</param>
    /// <param name="p1Score">蓝方分数</param>
    /// <param name="onRestart">重新观看回调</param>
    public static SettlementPanelController Create(
        string p0Name, string p0Result, int p0Score,
        string p1Name, string p1Result, int p1Score,
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
        ctrl.Setup(p0Name, p0Result, p0Score, p1Name, p1Result, p1Score, onRestart);
        return ctrl;
    }



    /// <summary>Prefab 模式：填充数据并连线回调</summary>
    void Setup(string p0Name, string p0Result, int p0Score,
               string p1Name, string p1Result, int p1Score,
               UnityAction onRestart)
    {
        string winner = p0Result == "victory" ? p0Name : (p1Result == "victory" ? p1Name : "平局");

        if (_titleText != null)
            _titleText.text = winner == "平局" ? "平局" : winner + " 获胜！";
        if (_scoreText != null)
            _scoreText.text = p0Name + "  " + p0Score + " 分  |  " + p1Name + "  " + p1Score + " 分";
        if (_redResultText != null)
            _redResultText.text = p0Name + "：" + (p0Result == "victory" ? "胜利" : "失败");
        if (_blueResultText != null)
            _blueResultText.text = p1Name + "：" + (p1Result == "victory" ? "胜利" : "失败");
        if (_restartBtn != null)
            _restartBtn.onClick.AddListener(onRestart);
    }




}
