using UnityEngine;

/// <summary>
/// BGM 系统（纯表现层，零耦合）：
/// - 白天播 bgm_day / 夜晚播 bgm_night，昼夜切换时双 AudioSource CrossFade
///   （按回合推进、速度无关：正常 2 回合淡入淡出；Seek 拖时间轴跳变 &gt;5 回合时瞬时快速切）
/// - 夜晚阶段判定：130 回合为一周期，周期内第 75 回合起进入夜晚（与光照入夜节奏贴合）；
///   夜晚音乐最迟在第二天第 3 回合切换回白天曲
/// - 起始播放偏移来自 BgmAudioConfig（编辑器「BGM 选段工具」可试听/选段/保存），
///   音乐从所选位置开始、播到所选片段结尾后回到该位置循环
/// - 回放暂停时 AudioListener.pause 冻结音乐；结算画面不主动换曲/打断轨道
/// - 音量档循环：音量·高 → 静音 → 音量·低 → 音量·高（供 ControlBar 按钮调用）
/// - WebGL Autoplay：首次用户输入后才开始播放
/// 只读取 ReplayPlayer.playing / RoundFloat，与 UnitView 零耦合。
/// </summary>
public class BgmController : MonoBehaviour
{
    const float NORMAL_FADE_ROUNDS = 2f;   // 正常昼夜过渡：2 回合内完成淡入淡出（与播放速度无关）
    const float SEEK_FADE_ROUNDS   = 0.3f; // Seek 拖时间轴跳变：0.3 回合瞬时切

    public enum VolumeLevel { Mute = 0, Low = 1, High = 2 }

    /// <summary>当前音量档（默认静音，选手点「音量」按钮逐档恢复），UI 每帧读标签展示。</summary>
    public static VolumeLevel CurrentVolume { get; private set; } = VolumeLevel.Mute;

    static BgmController _instance;

    // ---- 两个 CrossFade 通道 ----
    AudioSource _srcA, _srcB;
    AudioClip _dayClip, _nightClip;
    BgmAudioConfig _config;          // 起始偏移配置（Resources 下，缺省从头播）

    // ---- 运行状态 ----
    ReplayPlayer _player;        // Update 里延迟绑定（ReplayPlayer 可能晚于本组件创建）
    bool _isNightNow;            // 当前昼夜阶段
#pragma warning disable CS0414 // 编辑器/非 WebGL 下仅在 WebGL 构建里读取
    bool _audioUnlocked;         // WebGL 首次输入解锁
#pragma warning restore CS0414
    bool _started;               // 是否已启动首次播放
    float _lastRoundFloat;       // Seek 跳变检测

    // ---- CrossFade 状态 ----
    AudioSource _curSrc;         // 当前活跃通道（播放中）
    AudioSource _fadeInSrc;      // 淡入通道（目标阶段）
    AudioSource _fadeOutSrc;     // 淡出通道（旧活跃；首次播放为 null）
    float _fadeOutStartVol;      // 淡出通道起始音量
    float _crossFadeElapsed;     // 当前 CrossFade 进度
    float _crossFadeDuration;    // 当前 CrossFade 目标时长（正常 3f，快速切 0.3f）

    // ---------- 静态：音量控制（UI 按钮调用） ----------

    /// <summary>High → Mute → Low → High 循环切档。</summary>
    public static void CycleVolume()
    {
        CurrentVolume = (VolumeLevel)(((int)CurrentVolume + 1) % 3);
        if (_instance != null) _instance.ApplyVolume();
    }

    /// <summary>当前音量档的中文标签。</summary>
    public static string CurrentVolumeLabel()
    {
        switch (CurrentVolume)
        {
            case VolumeLevel.Mute: return "静音";
            case VolumeLevel.Low:  return "音量·低";
            default:               return "音量·高";
        }
    }

    static float TargetVolume(VolumeLevel lv)
    {
        switch (lv)
        {
            case VolumeLevel.Mute: return 0f;
            case VolumeLevel.Low:  return 0.15f;
            default:               return 0.4f;
        }
    }

    // ---------- 生命周期 ----------

    void Awake()
    {
        if (_instance != null) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);   // 跟 ReplayEntry（auto）一致，结算/切场景不打断

        _dayClip   = Resources.Load<AudioClip>("Audio/BGM/bgm_day");
        _nightClip = Resources.Load<AudioClip>("Audio/BGM/bgm_night");
        if (_dayClip == null)
            Debug.LogWarning("[BgmController] 加载失败 Audio/BGM/bgm_day（bgm_day.ogg），白天 BGM 静音，游戏正常继续");
        if (_nightClip == null)
            Debug.LogWarning("[BgmController] 加载失败 Audio/BGM/bgm_night（bgm_night.ogg），夜晚 BGM 静音，游戏正常继续");

        // 起始偏移配置（缺省为 0 → 从头播放，不报错）
        _config = Resources.Load<BgmAudioConfig>("Audio/BGM/BgmAudioConfig");

        _srcA = CreateSource();
        _srcB = CreateSource();
        _curSrc = _srcA;

#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL 浏览器 Autoplay 政策：首次用户输入后才允许 Play
        _audioUnlocked = false;
#else
        _audioUnlocked = true;
#endif
        ApplyVolume();
    }

    AudioSource CreateSource()
    {
        var src = gameObject.AddComponent<AudioSource>();
        src.loop = true;
        src.playOnAwake = false;
        src.volume = 0f;
        return src;
    }

    void Update()
    {
        if (_player == null)
        {
            _player = FindObjectOfType<ReplayPlayer>();
            if (_player == null) return;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL Autoplay 解锁：首次用户输入后才开始播放
        if (!_audioUnlocked)
        {
            if (Input.anyKeyDown || Input.touchCount > 0) _audioUnlocked = true;
            else return;   // 未解锁：不做任何播放
        }
#endif

        // 回放暂停 → 冻结音乐；恢复 → 继续（结算画面 SetPlaying(false) 同样冻结，但轨道不换不重启）
        AudioListener.pause = !(_player != null && _player.playing);

        // 首次启动：从当前阶段开始，淡入 2 回合
        if (!_started)
        {
            _started = true;
            _lastRoundFloat = _player.RoundFloat;
            _isNightNow = IsBgmNight(_lastRoundFloat);
            _crossFadeDuration = NORMAL_FADE_ROUNDS;
            StartCrossFade(_isNightNow);
        }

        // 昼夜阶段判定（周期内第 75 回合起入夜，75~78 回合完成白天→夜晚过渡）
        float roundFloat = _player.RoundFloat;
        float roundDelta = Mathf.Abs(roundFloat - _lastRoundFloat);   // 本帧回合增量（Seek 跳变时为大幅值）
        bool nightNow = IsBgmNight(roundFloat);
        if (nightNow != _isNightNow)
        {
            // 正常昼夜过渡 2 回合淡入淡出；Seek 跳变(>5回合) 0.3 回合瞬时切
            _crossFadeDuration = (roundDelta > 5f) ? SEEK_FADE_ROUNDS : NORMAL_FADE_ROUNDS;
            StartCrossFade(nightNow);
            _isNightNow = nightNow;
        }
        _lastRoundFloat = roundFloat;

        // 推进 CrossFade（按回合增量推进，速度无关；暂停/无增量时冻结）
        if (_crossFadeElapsed < _crossFadeDuration && _fadeInSrc != null)
            AdvanceCrossFade(roundDelta);

        // 选段循环：播到所选片段结尾后回到起始偏移，避免绕回整曲开头
        WrapLoopClip(_srcA);
        WrapLoopClip(_srcB);
    }

    // ---------- CrossFade ----------

    /// <summary>决定活跃/目标通道，目标通道淡入、活跃通道淡出。</summary>
    void StartCrossFade(bool toNight)
    {
        AudioClip target = toNight ? _nightClip : _dayClip;
        if (target == null) return;   // 素材缺失：保持静音，不报错

        AudioSource fadeIn, fadeOut;
        if (_srcA.clip == target)      { fadeIn = _srcA; fadeOut = _srcB; }
        else if (_srcB.clip == target) { fadeIn = _srcB; fadeOut = _srcA; }
        else                           { fadeIn = (_curSrc == _srcA) ? _srcB : _srcA; fadeOut = _curSrc; }

        fadeIn.Stop();
        fadeIn.clip = target;
        fadeIn.volume = 0f;
        fadeIn.time = Mathf.Min(StartTimeFor(fadeIn), Mathf.Max(0f, fadeIn.clip.length - 0.1f));
        fadeIn.Play();

        // 旧活跃通道按 clip 归属判定淡出（不能用 isPlaying：回放暂停时 AudioListener.pause 会让 isPlaying 恒为 false）
        _fadeInSrc = fadeIn;
        _fadeOutSrc = (fadeOut != null && fadeOut.clip != null) ? fadeOut : null;
        _fadeOutStartVol = _fadeOutSrc != null ? _fadeOutSrc.volume : 0f;
        _crossFadeElapsed = 0f;
    }

    void AdvanceCrossFade(float roundDelta)
    {
        float t = Mathf.Clamp01(_crossFadeElapsed / Mathf.Max(_crossFadeDuration, 0.001f));
        float inTarget = TargetVolume(CurrentVolume);

        _fadeInSrc.volume = Mathf.Lerp(0f, inTarget, t);
        if (_fadeOutSrc != null)
            _fadeOutSrc.volume = Mathf.Lerp(_fadeOutStartVol, 0f, t);

        _crossFadeElapsed += roundDelta;   // 按回合推进，速度无关

        if (_crossFadeElapsed >= _crossFadeDuration)
        {
            // 淡入完成：收尾（旧通道清零并停止，新通道成为活跃）
            if (_fadeOutSrc != null)
            {
                _fadeOutSrc.volume = 0f;
                _fadeOutSrc.Stop();
            }
            _curSrc = _fadeInSrc;
            _fadeInSrc = null;
            _fadeOutSrc = null;
            ApplyVolume();
        }
    }

    /// <summary>把当前音量档应用到活跃通道（CycleVolume 后 / CrossFade 结束后 / Awake 初始化后调用）。</summary>
    void ApplyVolume()
    {
        if (_curSrc != null) _curSrc.volume = TargetVolume(CurrentVolume);
    }

    // ---------- 昼夜阶段 + 选段循环 ----------

    /// <summary>BGM 昼夜判定：130 回合为一周期，周期内第 75 回合起进入夜晚（75~78 回合完成白天→夜晚过渡）。</summary>
    bool IsBgmNight(float roundFloat)
    {
        return Mathf.Repeat(roundFloat, 130f) >= 75f;
    }

    /// <summary>该通道对应的起始播放偏移（秒）；配置缺省或未对应到日/夜曲时为 0。</summary>
    float StartTimeFor(AudioSource src)
    {
        if (_config == null || src == null || src.clip == null) return 0f;
        if (src.clip == _dayClip)   return Mathf.Clamp(_config.dayStartTime, 0f, src.clip.length);
        if (src.clip == _nightClip) return Mathf.Clamp(_config.nightStartTime, 0f, src.clip.length);
        return 0f;
    }

    /// <summary>选段循环：播到所选片段结尾后回到起始偏移（loop=true 兜底，错过一帧也只绕回整曲开头、不会停）。</summary>
    void WrapLoopClip(AudioSource src)
    {
        if (src == null || src.clip == null || !src.isPlaying) return;
        float start = StartTimeFor(src);
        if (start <= 0.05f) return;          // 无偏移：整曲循环
        if (src.time >= src.clip.length - 0.15f)
            src.time = start;
    }
}
