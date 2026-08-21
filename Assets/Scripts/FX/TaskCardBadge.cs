using UnityEngine;

/// <summary>任务卡片状态。</summary>
public enum TaskCardState
{
    Hidden,     // 不渲染
    Intro,      // 灰色 "接受任务"
    Working,    // 蓝色 "破解中..."（点数循环）
    Success,    // 绿色 "✓ 通过"（弹跳，播完淡出销毁）
    Fail        // 红色 "× 失败"（抖动，播完淡出销毁）
}

/// <summary>
/// 开拓者头顶任务卡片（Phase 1 无素材版）：Quad 底板 + TextMesh 文字全部程序生成。
/// 四种状态：Intro(接受任务) / Working(破解中) / Success(✓ 通过) / Fail(× 失败)。
/// 由 TaskBadgeManager 动态创建，挂在开拓者 UnitView.transform 下跟随移动，LateUpdate 面向相机。
///
/// ══ Phase 2 升级点（本阶段仅预留说明，不实现）══
///  1. 底板 Quad 的 material.mainTexture ← RenderTexture ← VideoPlayer
///     （Working/Success/Fail 播放视频，素材路径 Assets/StreamingAssets/TaskVideos/{taskType}_{stage}.mp4）
///  2. Intro 状态用 UnityWebRequestTexture 加载 png 作为底板贴图
///  3. taskType → 素材前缀映射表（_taskType 字段已预留，未匹配时默认 "generic"）
///  组件结构 / SetState / IsFinished / CurrentState 对外接口保持不变，仅替换内部"渲染层"。
/// </summary>
public class TaskCardBadge : MonoBehaviour
{
    static Material s_sharedBgMat;          // 所有卡片共享背景材质（MPB 改色，可 GPU Instancing 合批）

    const float FADE_TIME = 0.2f;           // 淡入/淡出时长
    const float RESULT_DURATION = 1.5f;     // Success/Fail 展示时长（播完淡出销毁）
    const float BOUNCE_TIME = 0.3f;         // Success 弹跳时长
    const float SHAKE_TIME = 0.5f;          // Fail 抖动时长
    const float DOT_INTERVAL = 0.5f;        // Working 点循环周期
    const float CARD_SCALE = 2f;            // 画面整体放大倍数（底板/文字/偏移全部 ×2）
    const float HP_CLEARANCE = 0.5f;        // 卡片底部至少高出血条 0.5（避免遮挡血条）

    TextMesh _tm;
    MeshRenderer _bgRend;
    MaterialPropertyBlock _mpb;
    ReplayPlayer _player;                   // 暂停冻结用（读 playing）
    string _taskType = "generic";           // Phase 2：素材前缀映射用

    TaskCardState _state = TaskCardState.Hidden;
    float _elapsed;                         // 当前状态已持续时间（暂停时不累加）
    bool _fadingIn;                         // 正在淡入（0→1）
    bool _fadingOut;                        // 正在淡出（1→0）
    bool _isFinished;                       // Success/Fail 播完且已隐藏 → true
    Color _stateColor = Color.white;        // 当前状态背景色（RGB；alpha 由淡入淡出控制）
    Vector3 _baseLocalPos;                  // 抖动动画基准位置
    int _lastDots = -1;                     // Working 已显示的点数（避免每帧重建字符串）

    public bool IsFinished { get { return _isFinished; } }
    public TaskCardState CurrentState { get { return _state; } }

    /// <summary>创建卡片并挂到开拓者头顶。高度 = 血条世界高度 + 0.5 净空 + 底板世界半高，
    /// 保证卡片底沿在世界空间精确高于血条 0.5（不受父节点缩放影响）。</summary>
    public static TaskCardBadge Create(Transform parent, TaskCardState initialState, string taskType, ReplayPlayer player)
    {
        var go = new GameObject("TaskCardBadge");
        go.transform.SetParent(parent, false);
        go.transform.position = CardAnchorWorld(parent);
        var badge = go.AddComponent<TaskCardBadge>();
        badge._player = player;
        badge.SetState(initialState, taskType);
        return badge;
    }

    /// <summary>卡片锚点世界坐标：以 HpFill 血条世界中心为基准（Pioneer 在 _hpY），
    /// 在血条之上 +0.5 净空，再往上抬底板世界半高（CARD_SCALE*0.3×父缩放），
    /// 让卡片底沿在世界空间精确贴住净空线，父节点 Scale 不影响净空。</summary>
    static Vector3 CardAnchorWorld(Transform parent)
    {
        Vector3 world = parent != null ? parent.position : Vector3.zero;
        float hpWorldY = world.y;
        if (parent != null)
        {
            var hp = parent.Find("HpFill");
            if (hp != null) hpWorldY = hp.position.y;   // HpFill 世界中心 Y
        }
        float scale = parent != null ? parent.lossyScale.y : 1f;  // 父级（单位）缩放
        float cardHalfHWorld = CARD_SCALE * 0.3f * scale;         // 底板世界半高（0.6×缩放/2）
        return new Vector3(world.x, hpWorldY + HP_CLEARANCE + cardHalfHWorld, world.z);
    }

    void Awake()
    {
        _baseLocalPos = transform.localPosition;

        // 背景板（共享材质 + MaterialPropertyBlock 改色，不 new 独立材质）
        var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bg.name = "Bg";
        bg.transform.SetParent(transform, false);
        bg.transform.localScale = new Vector3(1f * CARD_SCALE, 0.6f * CARD_SCALE, 1f);
        _bgRend = bg.GetComponent<MeshRenderer>();
        _bgRend.sharedMaterial = SharedBgMat();
        _bgRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _bgRend.receiveShadows = false;
        var col = bg.GetComponent<Collider>();
        if (col != null) Destroy(col);
        _mpb = new MaterialPropertyBlock();

        // 文字（Legacy TextMesh，不用 uGUI）
        var txt = new GameObject("Txt");
        txt.transform.SetParent(transform, false);
        txt.transform.localPosition = new Vector3(0f, 0f, -0.01f * CARD_SCALE); // 略前一点避免与底板 z-fighting
        _tm = txt.AddComponent<TextMesh>();
        _tm.font = UiFonts.Get();
        _tm.fontSize = 40;
        _tm.characterSize = 0.03f * CARD_SCALE;
        _tm.anchor = TextAnchor.MiddleCenter;
        _tm.alignment = TextAlignment.Center;
        _tm.color = Color.white;
    }

    /// <summary>由 TaskBadgeManager 每帧调用。同状态幂等（不重置动画）；
    /// 正在播放 Success/Fail 时忽略稳态 Hidden（让它播完再淡出销毁），只有新任务出现才打断。</summary>
    public void SetState(TaskCardState state, string taskType)
    {
        if (!string.IsNullOrEmpty(taskType)) _taskType = taskType;

        // 正在播放 Success/Fail 结果：让它播完；仅新任务（Intro/Working）可打断结果播放。
        if (_state == TaskCardState.Success || _state == TaskCardState.Fail)
        {
            if (state == TaskCardState.Intro || state == TaskCardState.Working)
                SwitchTo(state);
            return;
        }
        SwitchTo(state);
    }

    void SwitchTo(TaskCardState state)
    {
        if (state == _state) return;
        bool wasHidden = _state == TaskCardState.Hidden;
        _state = state;
        _elapsed = 0f;
        _isFinished = false;
        _fadingOut = false;
        _fadingIn = wasHidden;              // 从 Hidden 进入 → 淡入 0.2s
        _lastDots = -1;                     // 重置 Working 点数缓存
        transform.localScale = Vector3.one;
        transform.localPosition = _baseLocalPos;
        ApplyStateVisuals(state);
    }

    void ApplyStateVisuals(TaskCardState state)
    {
        switch (state)
        {
            case TaskCardState.Intro:
                _stateColor = Hex(0x4A4A4A);
                SetText("接受任务");
                break;
            case TaskCardState.Working:
                _stateColor = Hex(0x2E86DE);
                SetText("破解中");
                break;
            case TaskCardState.Success:
                _stateColor = Hex(0x27AE60);
                SetText("✓ 通过");
                break;
            case TaskCardState.Fail:
                _stateColor = Hex(0xC0392B);
                SetText("× 失败");
                break;
        }
        ApplyAlpha(1f);
    }

    void Update()
    {
        // 暂停冻结：所有动画计时不推进（Working 点 / Success 弹跳 / Fail 抖动 / 淡入淡出全部暂停）
        if (_player != null && !_player.playing) return;

        _elapsed += Time.deltaTime;

        if (_fadingOut)
        {
            float a = 1f - Mathf.Clamp01(_elapsed / FADE_TIME);
            ApplyAlpha(a);
            if (a <= 0f)
            {
                _fadingOut = false;
                _state = TaskCardState.Hidden;
                _isFinished = true;
            }
            return;
        }

        switch (_state)
        {
            case TaskCardState.Hidden:
                return; // 不渲染
            case TaskCardState.Intro:
                break;  // 静止（仅淡入）
            case TaskCardState.Working:
                {
                    int dots = (int)(_elapsed / DOT_INTERVAL) % 4;  // 0 1 2 3 循环
                    if (dots != _lastDots) { _lastDots = dots; SetText("破解中" + new string('.', dots)); }
                    break;
                }
            case TaskCardState.Success:
                ApplyBounce();
                break;
            case TaskCardState.Fail:
                ApplyShake();
                break;
        }

        if (_fadingIn)
        {
            float a = Mathf.Clamp01(_elapsed / FADE_TIME);
            ApplyAlpha(a);
            if (a >= 1f) _fadingIn = false;
        }
        else
        {
            ApplyAlpha(1f);
        }

        // Success/Fail 播完 → 淡出 → Hidden
        if (_state == TaskCardState.Success || _state == TaskCardState.Fail)
        {
            if (_elapsed >= RESULT_DURATION)
            {
                _fadingOut = true;
                _fadingIn = false;
                _elapsed = 0f;
            }
        }
    }

    void ApplyBounce()
    {
        float t = Mathf.Clamp01(_elapsed / BOUNCE_TIME);
        float s = t < 0.5f
            ? Mathf.Lerp(1f, 1.3f, t * 2f)
            : Mathf.Lerp(1.3f, 1f, (t - 0.5f) * 2f);
        transform.localScale = Vector3.one * s;
    }

    void ApplyShake()
    {
        if (_elapsed < SHAKE_TIME)
        {
            float sx = Mathf.Sin(_elapsed * 30f) * 0.05f * CARD_SCALE;   // X 方向 ±0.05m×缩放 抖动
            transform.localPosition = _baseLocalPos + new Vector3(sx, 0f, 0f);
        }
        else
        {
            transform.localPosition = _baseLocalPos;
        }
    }

    /// <summary>同步背景板（MPB）与文字 alpha（TextMesh 用 color.a 淡出）。</summary>
    void ApplyAlpha(float a)
    {
        if (_bgRend != null)
        {
            _mpb.SetColor("_Color", new Color(_stateColor.r, _stateColor.g, _stateColor.b, a));
            _bgRend.SetPropertyBlock(_mpb);
        }
        if (_tm != null) _tm.color = new Color(1f, 1f, 1f, a);
    }

    /// <summary>设置 TextMesh 文字 + WebGL 动态字体预热（字形请求 + 材质同步，处理见 UiFonts/TradeBadge）。</summary>
    void SetText(string text)
    {
        if (_tm == null || _tm.text == text) return;
        _tm.text = text;
        if (_tm.font != null)
        {
            _tm.font.RequestCharactersInTexture(text, _tm.fontSize, _tm.fontStyle);
            var mr = _tm.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = _tm.font.material;
        }
    }

    /// <summary>Billboard：始终面向主相机（暂停时也保持面朝相机）。</summary>
    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null) return;
        transform.rotation = cam.transform.rotation;
    }

    static Color Hex(int rgb)
    {
        return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }

    /// <summary>共享背景材质：所有卡片一个材质 + MPB 改色 → 背景 Quad 走 GPU Instancing 批。</summary>
    static Material SharedBgMat()
    {
        if (s_sharedBgMat == null)
        {
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            s_sharedBgMat = new Material(shader);
            s_sharedBgMat.enableInstancing = true;  // Sprites/Default 支持 Instancing（已实测）
        }
        return s_sharedBgMat;
    }
}
