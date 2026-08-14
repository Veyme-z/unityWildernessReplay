using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 结算画面面板 — 显示胜负结果和重新观看按钮。
/// 支持 prefab 和纯代码创建两种模式。
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
        // 优先使用 prefab
        var prefab = PrefabRefs.Instance.GetSettlementPrefab();
        if (prefab != null)
        {
            var go = Object.Instantiate(prefab);
            UiFonts.Apply(go.transform);   // 覆盖 prefab 里烘焙的旧字体
            var ctrl = go.GetComponentInChildren<SettlementPanelController>();
            if (ctrl == null) ctrl = go.AddComponent<SettlementPanelController>();
            ctrl.Setup(p0Name, p0Result, p0Score, p1Name, p1Result, p1Score, onRestart);
            return ctrl;
        }
        Debug.LogWarning("[SettlementPanelController] SettlementPanel prefab 缺失，回退到代码创建 UI（请检查场景 PrefabRefs 或 Resources/Prefabs/UI/SettlementPanel）。");
        return CreateFromCode(p0Name, p0Result, p0Score, p1Name, p1Result, p1Score, onRestart);
    }

    static SettlementPanelController CreateFromCode(
        string p0Name, string p0Result, int p0Score,
        string p1Name, string p1Result, int p1Score,
        UnityAction onRestart)
    {
        // ── Canvas ──
        var canvasGo = new GameObject("SettlementCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // ── 背景 ──
        var bg = new GameObject("Bg"); bg.transform.SetParent(canvasGo.transform, false);
        var brt = bg.AddComponent<RectTransform>();
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
        bg.AddComponent<Image>().color = new Color(0, 0, 0, 0.7f);

        // ── 面板 ──
        var panel = new GameObject("Panel"); panel.transform.SetParent(canvasGo.transform, false);
        var prt = panel.AddComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.5f, 0.5f); prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(500, 320);
        panel.AddComponent<Image>().color = new Color(0.102f, 0.102f, 0.118f, 0.95f);

        string winner = p0Result == "victory" ? p0Name : (p1Result == "victory" ? p1Name : "平局");

        // ── 文本 ──
        var title = MkText(panel.transform, "🏆 " + (winner == "平局" ? "平局" : winner + " 获胜！"), 28,
                           new Vector2(0, -20), TextAnchor.UpperCenter, new Color(0.96f, 0.78f, 0.22f), 500, 44);
        var score = MkText(panel.transform, p0Name + "  " + p0Score + " 分  |  " + p1Name + "  " + p1Score + " 分",
                           20, new Vector2(0, -80), TextAnchor.UpperCenter, Color.white, 480, 36);
        var redR = MkText(panel.transform, "🔴 " + p0Name + "：" + (p0Result == "victory" ? "胜利" : "失败"),
                          16, new Vector2(-120, -130), TextAnchor.UpperLeft, new Color(0.94f, 0.34f, 0.28f), 240, 28);
        var blueR = MkText(panel.transform, "🔵 " + p1Name + "：" + (p1Result == "victory" ? "胜利" : "失败"),
                           16, new Vector2(120, -130), TextAnchor.UpperLeft, new Color(0.28f, 0.62f, 0.96f), 240, 28);

        // ── 按钮 ──
        var btnGo = new GameObject("RestartBtn"); btnGo.transform.SetParent(panel.transform, false);
        var btnRt = btnGo.AddComponent<RectTransform>();
        btnRt.anchorMin = new Vector2(0.5f, 0); btnRt.anchorMax = new Vector2(0.5f, 0);
        btnRt.pivot = new Vector2(0.5f, 0); btnRt.anchoredPosition = new Vector2(0, 30); btnRt.sizeDelta = new Vector2(160, 40);
        btnGo.AddComponent<Image>().color = new Color(0, 0.478f, 1f);
        var btn = btnGo.AddComponent<Button>();
        btn.onClick.AddListener(onRestart);
        var btnLbl = new GameObject("Lbl"); btnLbl.transform.SetParent(btnGo.transform, false);
        var lrt = btnLbl.AddComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
        var lt = btnLbl.AddComponent<Text>();
        lt.text = "重新观看"; lt.fontSize = 18; lt.alignment = TextAnchor.MiddleCenter; lt.color = Color.white;
        lt.font = BuiltinFont(); lt.raycastTarget = false;

        var ctrl = panel.AddComponent<SettlementPanelController>();
        ctrl._titleText = title;
        ctrl._scoreText = score;
        ctrl._redResultText = redR;
        ctrl._blueResultText = blueR;
        ctrl._restartBtn = btn;
        return ctrl;
    }

    /// <summary>Prefab 模式：填充数据并连线回调</summary>
    void Setup(string p0Name, string p0Result, int p0Score,
               string p1Name, string p1Result, int p1Score,
               UnityAction onRestart)
    {
        string winner = p0Result == "victory" ? p0Name : (p1Result == "victory" ? p1Name : "平局");

        if (_titleText != null)
            _titleText.text = "🏆 " + (winner == "平局" ? "平局" : winner + " 获胜！");
        if (_scoreText != null)
            _scoreText.text = p0Name + "  " + p0Score + " 分  |  " + p1Name + "  " + p1Score + " 分";
        if (_redResultText != null)
            _redResultText.text = "🔴 " + p0Name + "：" + (p0Result == "victory" ? "胜利" : "失败");
        if (_blueResultText != null)
            _blueResultText.text = "🔵 " + p1Name + "：" + (p1Result == "victory" ? "胜利" : "失败");
        if (_restartBtn != null)
            _restartBtn.onClick.AddListener(onRestart);
    }

    static Text MkText(Transform p, string txt, int sz, Vector2 pos, TextAnchor a, Color c, float w, float h)
    {
        var go = new GameObject("T"); go.transform.SetParent(p, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1); rt.anchorMax = new Vector2(0.5f, 1);
        rt.pivot = new Vector2(0.5f, 1); rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(w, h);
        var t = go.AddComponent<Text>();
        t.text = txt; t.font = BuiltinFont();
        t.fontSize = sz; t.alignment = a; t.color = c; t.raycastTarget = false;
        return t;
    }

    static Font BuiltinFont()
    {
        return UiFonts.Get();
    }
}
