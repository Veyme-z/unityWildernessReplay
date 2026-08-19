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

    ReplayPlayer _player;

    // 色板
    static readonly Color WARM_DAY   = new Color(1f, 0.60f, 0.20f);   // 暖橙
    static readonly Color WARM_PHASE = new Color(1f, 0.85f, 0.55f);   // 暖金
    static readonly Color COOL_NIGHT = new Color(0.35f, 0.55f, 0.90f); // 冷蓝
    static readonly Color WHITE      = new Color(0.95f, 0.94f, 0.90f);

    Color _currentDayColor, _currentPhaseColor;
    int _lastRound = -1;
    bool _lastNight;

    public static HudController Create(ReplayPlayer player)
    {
        // prefab 是真源：场景 PrefabRefs 按 GUID 引用，缺失即报错（不再有纯代码兜底）
        var prefab = PrefabRefs.Instance.GetHudPrefab();
        if (prefab != null)
        {
            var go = Object.Instantiate(prefab);
            UiFonts.Apply(go.transform);   // 覆盖 prefab 里烘焙的旧字体
            var ctrl = go.GetComponentInChildren<HudController>();
            if (ctrl == null) ctrl = go.AddComponent<HudController>();
            ctrl._player = player;
            ctrl._currentDayColor = WARM_DAY;
            ctrl._currentPhaseColor = WARM_PHASE;
            return ctrl;
        }
        Debug.LogError("[HudController] 缺少 HudPanel prefab（请检查场景 PrefabRefs.hudPanelPrefab），顶部状态面板未创建。");
        return null;
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
            phaseLabel.text = night ? "黑夜" : "白天";
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
