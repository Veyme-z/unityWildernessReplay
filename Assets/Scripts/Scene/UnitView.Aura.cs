// UnitView 的夜晚角色光环子模块（Partial Class）
// 职责：夜晚给工人/开拓者(6/7)挂常驻 Runic 魔法光环，按阵营 MPB 上色，随昼夜显隐，暂停冻结粒子
// 字段声明与主流程见 UnitView.cs；其余子模块：Anim / Hp / Lod / Tower

using CartoonFX;
using UnityEngine;

public partial class UnitView
{
    const string NIGHT_AURA_RES = "FX/CFXR3 Magic Aura A (Runic)"; // CFXR3 Runic 魔法光环（Resources 拷贝，自带 Point Light）
    const float NIGHT_AURA_NATIVE = 3.11f;             // 光环原始直径（实测模拟 bounds，2026-08-24）
    const float NIGHT_AURA_FOOT_Y = 0f;                // 根相对脚底偏移：0 = 特效以脚底/地面为中心（Runic 符文圈本就贴地）
    const float NIGHT_AURA_RATIO = 2.1f;               // 光环直径 / 角色占地（框住它）
    const float NIGHT_AURA_MIN_WORLD = 0.5f;           // 最小世界尺寸兜底
    const float NIGHT_AURA_ALPHA = 0.55f;              // 整体透明度（<1 更通透）
    const float NIGHT_AURA_WARMUP = 1f;                // 显示时预热模拟时长（秒），让符文圈粒子成型，避免 Seek 到夜晚立即暂停时法阵隐形

    static readonly Color NIGHT_AURA_DEFENDER   = new Color(3.0f, 0.6f, 0.6f, NIGHT_AURA_ALPHA);   // 红方光环
    static readonly Color NIGHT_AURA_CHALLENGER = new Color(0.6f, 0.8f, 3.2f, NIGHT_AURA_ALPHA);   // 蓝方光环

    Transform _auraRoot;
    ParticleSystem[] _auraParticles = new ParticleSystem[0];
    bool _auraVisible;   // 光环当前显隐（夜晚 true）
    bool _auraPaused;    // 上一帧回放暂停态

    ReplayPlayer CurrentPlayer()
    {
        if (s_cachedPlayer == null) s_cachedPlayer = FindObjectOfType<ReplayPlayer>();
        if (s_cachedPlayer == null) s_cachedPlayer = _player;
        return s_cachedPlayer;
    }

    /// <summary>仅工人(6)/开拓者(7)挂夜晚光环。在 ConfigureFromUnitPrefab 末尾（CalibrateBaseScale 之后）调用。</summary>
    void SetupNightAura()
    {
        if (state.type != 6 && state.type != 7) return;

        // 角色世界占地：hp 宽度（未缩放模型宽）× baseScale ≈ 校准后的目标宽度（6/7 = 1.5）
        float footprint = Mathf.Max(_hpW * _baseScale, NIGHT_AURA_MIN_WORLD);

        var prefab = Resources.Load<GameObject>(NIGHT_AURA_RES);
        if (prefab == null)
        {
            Debug.LogWarning("[UnitView] 夜晚光环 prefab 加载失败: " + NIGHT_AURA_RES);
            return;
        }

        var go = Instantiate(prefab, transform);
        go.name = "NightAura";
        _auraRoot = go.transform;
        _auraParticles = go.GetComponentsInChildren<ParticleSystem>(true);
        // 常驻光环：不允许 CFXR_Effect 在粒子结束时自动销毁（pause/Seek 时可能被误判为结束）
        foreach (var cfxr in go.GetComponentsInChildren<CFXR_Effect>(true))
            cfxr.clearBehavior = CFXR_Effect.ClearBehavior.None;
        // Instantiate 出的 GO 默认激活，先把 _auraVisible 对齐现实，SetAuraVisible(false) 才会真正 SetActive(false)
        _auraVisible = true;

        // 以角色占地为基准缩放光环，「框住」角色；根自身 lossyScale 兜底（本类单位恒为 1）
        float lossy = Mathf.Max(transform.lossyScale.x, 0.001f);
        float worldTarget = Mathf.Max(footprint * NIGHT_AURA_RATIO, NIGHT_AURA_MIN_WORLD);
        float scale = worldTarget / NIGHT_AURA_NATIVE / lossy;
        _auraRoot.localScale = Vector3.one * scale;
        // 光环底部贴合角色脚底：根上抬 = 原生底部偏移(FOOT_Y) × 缩放
        _auraRoot.localPosition = new Vector3(0f, NIGHT_AURA_FOOT_Y * scale, 0f);

        ApplyAuraColor();
        SetAuraVisible(false); // 初始白天，隐藏
    }

    void ApplyAuraColor()
    {
        if (_auraRoot == null) return;
        Color c = state.teamType == "defender" ? NIGHT_AURA_DEFENDER
                : state.teamType == "challenger" ? NIGHT_AURA_CHALLENGER
                : new Color(0.6f, 0.9f, 0.6f, NIGHT_AURA_ALPHA);
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_Color", c);
        foreach (var r in _auraRoot.GetComponentsInChildren<ParticleSystemRenderer>(true))
            r.SetPropertyBlock(mpb);
    }

    /// <summary>切换光环显隐。夜晚显示时统一重新播放（白天 SetActive(false) 粒子随组件停用清零）。</summary>
    void SetAuraVisible(bool on)
    {
        if (_auraRoot == null || _auraVisible == on) return;
        _auraVisible = on;
        _auraRoot.gameObject.SetActive(on);
        _auraPaused = false;
        if (on)
        {
            foreach (var ps in _auraParticles) ps.Play();
            // 预热：模拟一小段时间让符文圈粒子成型（否则 Seek 到夜晚立即暂停时粒子为 0，法阵看不见、只剩光）
            foreach (var ps in _auraParticles) ps.Simulate(NIGHT_AURA_WARMUP, true, true);
        }
    }

    /// <summary>LateUpdate 每帧调用：夜晚显隐 + 暂停冻结。</summary>
    void UpdateNightAura()
    {
        if (_auraRoot == null) return;
        bool night = IsNightRound();
        bool pause = !(CurrentPlayer() != null && CurrentPlayer().playing);
        SetAuraVisible(night);
        if (night && pause != _auraPaused)
        {
            _auraPaused = pause;
            foreach (var ps in _auraParticles)
            {
                if (pause) { if (ps.isPlaying) ps.Pause(); }
                else ps.Play();
            }
        }
    }

    /// <summary>与昼夜系统夜晚段一致：RoundFloat % 130 ∈ [80, 130) 为夜晚。</summary>
    bool IsNightRound()
    {
        var p = CurrentPlayer();
        if (p == null || p.data == null) return false;
        return Mathf.Repeat(p.RoundFloat, 130f) >= 80f;
    }
}
