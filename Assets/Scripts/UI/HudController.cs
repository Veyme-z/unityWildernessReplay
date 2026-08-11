using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 顶部状态面板：天数 / 昼夜阶段 / 回合进度。
/// 挂在一个 Screen Space Overlay Canvas 下的 Panel GameObject 上。
/// </summary>
public class HudController : MonoBehaviour
{
    [Header("UI 引用（prefab 中连线或代码赋值）")]
    [SerializeField] public Text dayLabel;
    [SerializeField] public Text phaseLabel;
    [SerializeField] public Text roundLabel;
    [SerializeField] public Image panelBg;

    ReplayPlayer _player;

    // 色板
    static readonly Color BG_COLOR   = new Color(0.102f, 0.102f, 0.118f, 0.85f); // #1A1A1E
    static readonly Color WARM_DAY   = new Color(1f, 0.60f, 0.20f);   // 暖橙
    static readonly Color WARM_PHASE = new Color(1f, 0.85f, 0.55f);   // 暖金
    static readonly Color COOL_NIGHT = new Color(0.35f, 0.55f, 0.90f); // 冷蓝
    static readonly Color WHITE      = new Color(0.95f, 0.94f, 0.90f);

    Color _currentDayColor, _currentPhaseColor;
    int _lastRound = -1;
    bool _lastNight;

    public static HudController Create(ReplayPlayer player)
    {
        // 优先使用 prefab（如果有配置），否则退回纯代码创建
        var prefab = PrefabRefs.Instance.GetHudPrefab();
        if (prefab != null)
        {
            var go = Object.Instantiate(prefab);
            var ctrl = go.GetComponentInChildren<HudController>();
            if (ctrl == null) ctrl = go.AddComponent<HudController>();
            ctrl._player = player;
            ctrl._currentDayColor = WARM_DAY;
            ctrl._currentPhaseColor = WARM_PHASE;
            return ctrl;
        }
        return CreateFromCode(player);
    }

    static HudController CreateFromCode(ReplayPlayer player)
    {
        // Canvas
        var canvasGo = new GameObject("HudCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // 面板
        var panelGo = new GameObject("TopPanel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        var panelRt = panelGo.AddComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 1f);
        panelRt.anchorMax = new Vector2(0.5f, 1f);
        panelRt.pivot = new Vector2(0.5f, 1f);
        panelRt.anchoredPosition = new Vector2(0, -8);
        panelRt.sizeDelta = new Vector2(520, 52);
        var bg = panelGo.AddComponent<Image>();
        bg.color = BG_COLOR;

        // DAY 文字
        var dayGo = new GameObject("DayLabel");
        dayGo.transform.SetParent(panelGo.transform, false);
        var dayRt = dayGo.AddComponent<RectTransform>();
        dayRt.anchorMin = new Vector2(0, 0.5f);
        dayRt.anchorMax = new Vector2(0, 0.5f);
        dayRt.pivot = new Vector2(0, 0.5f);
        dayRt.anchoredPosition = new Vector2(16, 0);
        dayRt.sizeDelta = new Vector2(120, 36);
        var dayTxt = dayGo.AddComponent<Text>();
        dayTxt.text = "DAY 1";
        dayTxt.font = BuiltinFont();
        dayTxt.fontSize = 22;
        dayTxt.alignment = TextAnchor.MiddleLeft;
        dayTxt.color = WARM_DAY;

        // 昼夜阶段
        var phaseGo = new GameObject("PhaseLabel");
        phaseGo.transform.SetParent(panelGo.transform, false);
        var phaseRt = phaseGo.AddComponent<RectTransform>();
        phaseRt.anchorMin = new Vector2(0, 0.5f);
        phaseRt.anchorMax = new Vector2(0, 0.5f);
        phaseRt.pivot = new Vector2(0, 0.5f);
        phaseRt.anchoredPosition = new Vector2(148, 0);
        phaseRt.sizeDelta = new Vector2(130, 28);
        var phaseTxt = phaseGo.AddComponent<Text>();
        phaseTxt.text = "☀ 白天";
        phaseTxt.font = BuiltinFont();
        phaseTxt.fontSize = 18;
        phaseTxt.alignment = TextAnchor.MiddleLeft;
        phaseTxt.color = WARM_PHASE;

        // 回合数
        var roundGo = new GameObject("RoundLabel");
        roundGo.transform.SetParent(panelGo.transform, false);
        var roundRt = roundGo.AddComponent<RectTransform>();
        roundRt.anchorMin = new Vector2(1, 0.5f);
        roundRt.anchorMax = new Vector2(1, 0.5f);
        roundRt.pivot = new Vector2(1, 0.5f);
        roundRt.anchoredPosition = new Vector2(-16, 0);
        roundRt.sizeDelta = new Vector2(200, 28);
        var roundTxt = roundGo.AddComponent<Text>();
        roundTxt.text = "回合 1 / 80";
        roundTxt.font = BuiltinFont();
        roundTxt.fontSize = 16;
        roundTxt.alignment = TextAnchor.MiddleRight;
        roundTxt.color = WHITE;

        // 挂控制器
        var ctrl = panelGo.AddComponent<HudController>();
        ctrl.dayLabel = dayTxt;
        ctrl.phaseLabel = phaseTxt;
        ctrl.roundLabel = roundTxt;
        ctrl.panelBg = bg;
        ctrl._player = player;
        ctrl._currentDayColor = WARM_DAY;
        ctrl._currentPhaseColor = WARM_PHASE;
        return ctrl;
    }

    static Font BuiltinFont()
    {
#if UNITY_2022_1_OR_NEWER
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
        return Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
    }

    void Update()
    {
        if (_player == null || _player.data == null) return;
        int round = _player.cur;
        if (round < 1) return;

        int day   = StateEngine.DayOf(round);
        bool night = StateEngine.IsNight(round);
        int turnInPhase = ((round - 1) % 130) % 80;
        if (night) turnInPhase = ((round - 1) % 130) - 80;
        int phaseTotal = night ? 50 : 80;

        // 只在值变化时更新文本
        if (round != _lastRound || night != _lastNight)
        {
            _lastRound = round;
            _lastNight = night;

            dayLabel.text = "DAY " + day;
            phaseLabel.text = night ? "🌙 黑夜" : "☀ 白天";
            roundLabel.text = "回合 " + (turnInPhase + 1) + " / " + phaseTotal;

            // 昼夜色温过渡
            Color targetDay   = night ? COOL_NIGHT : WARM_DAY;
            Color targetPhase = night ? COOL_NIGHT : WARM_PHASE;
            _currentDayColor   = Color.Lerp(_currentDayColor,   targetDay,   0.15f);
            _currentPhaseColor = Color.Lerp(_currentPhaseColor, targetPhase, 0.15f);
            dayLabel.color   = _currentDayColor;
            phaseLabel.color = _currentPhaseColor;
        }

        // 持续做平滑过渡
        {
            Color targetDay2   = night ? COOL_NIGHT : WARM_DAY;
            Color targetPhase2 = night ? COOL_NIGHT : WARM_PHASE;
            _currentDayColor   = Color.Lerp(_currentDayColor,   targetDay2,   Time.deltaTime * 3f);
            _currentPhaseColor = Color.Lerp(_currentPhaseColor, targetPhase2, Time.deltaTime * 3f);
            dayLabel.color   = _currentDayColor;
            phaseLabel.color = _currentPhaseColor;
        }
    }
}
