using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Video;

/// <summary>任务卡片状态。</summary>
public enum TaskCardState
{
    Hidden,     // 不渲染
    Intro,      // 领取任务（执行任务第一个回合，claim 视频循环播放）
    Working,    // 破解中...（working 视频循环）
    Success,    // 解锁成功（unlock_success 视频，播完淡出销毁；视频失败降级纯色+弹跳）
    Fail        // 解锁失败（unlock_fail 视频，播完淡出销毁；视频失败降级纯色+抖动）
}

/// <summary>
/// 开拓者头顶任务卡片：Quad 底板 + TextMesh 文字全部程序生成。
/// 四种状态：Intro(领取任务) / Working(破解中) / Success(解锁成功) / Fail(解锁失败)。
/// 由 TaskBadgeManager 动态创建，挂在开拓者 UnitView.transform 下跟随移动，LateUpdate 面向相机。
///
/// ══ 渲染层（Phase 2 全部实现）══
///  - Intro   ：底板 MPB _MainTex ← 全局共享 claim.mp4「领取任务」视频 RT（循环播放，至少展示 2 回合）
///  - Working ：底板 MPB _MainTex ← TaskBadgeManager 全局共享 working 视频 RT（游戏开始即就绪循环播放；
///              视频自带"破解中"字样故不叠文字）
///  - Success/Fail：底板 MPB _MainTex ← 全局共享 unlock_success.mp4 / unlock_fail.mp4 视频 RT（看完一遍淡出，
///              视频自带"解锁成功/解锁失败"字样故不叠文字）
///  全局共享视频由 TaskBadgeManager.EnsureSharedVideo 在游戏开始即 Prepare + 循环播放，卡片对应状态
///  直接显示共享 RT——立即可用，无中间加载态（本地 VideoSlot 仅作共享未就绪时的回退）。
///  视频加载失败回退：Intro→task.png 图片；Working/Success/Fail→纯色+文字
///
/// 组件结构 / SetState / IsFinished / CurrentState 对外接口保持不变，仅替换内部"渲染层"。
/// </summary>
public class TaskCardBadge : MonoBehaviour
{
    static Material s_sharedBgMat;          // 所有卡片共享背景材质（MPB 改色，可 GPU Instancing 合批）
    static Texture2D s_introTex;            // Intro 状态底板贴图（Resources 懒加载缓存）

    const string RES_TASK_TEX = "Sprites/task";        // Intro 视频加载失败时的兜底图（「接受任务」，Assets/Resources/Sprites/task.png）
    public const string CLAIM_VIDEO    = "TaskVideos/claim.mp4";          // Intro 领取任务视频（执行任务第一个回合）
    public const string WORKING_VIDEO  = "TaskVideos/working.mp4";        // Working 破解中视频（StreamingAssets 下，WebGL 相对 URL）
    public const string SUCCESS_VIDEO  = "TaskVideos/unlock_success.mp4"; // Success 解锁成功视频（密码正确）
    public const string FAIL_VIDEO     = "TaskVideos/unlock_fail.mp4";    // Fail 解锁失败视频（密码错误）

    const float FADE_TIME = 0.2f;           // 淡入/淡出时长
    const float RESULT_DURATION = 1.5f;     // Success/Fail 展示时长（播完淡出销毁）
    const float BOUNCE_TIME = 0.3f;         // Success 弹跳时长
    const float SHAKE_TIME = 0.5f;          // Fail 抖动时长
    const float DOT_INTERVAL = 0.5f;        // Working 点循环周期
    const float CARD_SCALE = 4f;            // 画面整体放大倍数（底板/文字/描边/偏移全部 ×4；2026-08-25 用户反馈卡片小看不清视频，×2 → ×4）
    const float HP_CLEARANCE = 0.5f;        // 卡片底部至少高出血条 0.5（避免遮挡血条）
    const float BORDER = 0.05f;             // 金黄描边厚度（卡片单位，×CARD_SCALE=世界宽度）；让卡片在草地/图片上轮廓清晰
    const int INTRO_MIN_ROUNDS = 2;         // Intro（接受任务图片）至少展示的回合数：roundCost==0 仅接任务那回合，延长避免一闪而过

    TextMesh _tm;
    MeshRenderer _bgRend;
    MaterialPropertyBlock _mpb;
    MeshRenderer _borderRend;               // 金黄描边渲染器（淡入淡出需同步其 alpha，否则结束残留黄框）
    MaterialPropertyBlock _borderMpb;
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
    GameObject _txtGo;                      // 文字对象（Intro 图片态隐藏，避免"接受任务"字叠在图上）
    int _introStartCur = -1;                // 进入 Intro 时记录的回合（用于延长展示到 INTRO_MIN_ROUNDS）

    // 视频渲染：正常路径用 TaskBadgeManager 的全局共享视频 RT（游戏开始即就绪并循环播放）——Working/结果态
    // 立即可显示，无中间加载态。本地 VideoSlot（每视频独立 VideoPlayer+RenderTexture）仅作共享视频未就绪的
    // 极端情况回退：创建后 Prepare，就绪后"渲染出帧即接管底板"，未就绪则保持当前画面。
    class VideoSlot
    {
        public string url;          // 视频资源名（TaskVideos/xxx.mp4）
        public VideoPlayer player;
        public RenderTexture rt;
        public bool prepared;       // 已 Prepare 完成（可播放）
        public bool failed;         // 加载失败（不再尝试）
    }
    readonly Dictionary<string, VideoSlot> _videos = new Dictionary<string, VideoSlot>();
    string _shownUrl;                           // 当前底板显示的视频 url（null=图片/纯色）
    float _resultDuration = RESULT_DURATION;    // Success/Fail 展示时长（视频就绪后取 max(视频时长)，看完一遍再淡出）
    bool _fallback;                             // 当前状态处于"视频加载失败 → 纯色+文字"降级
    bool _resultPreloadStarted;                 // success/fail 视频是否已开始延迟准备（working 就绪后才触发，避免三路并发抢解码）

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

        // 金黄描边：稍大一点的 Quad 垫在底板后面，四周露出边框，让卡片在草地/图片上轮廓清晰。
        // Sprites/Default 是透明混合，渲染顺序按 z（负 z 更靠前），描边放在 +z（后面）即可。
        var border = GameObject.CreatePrimitive(PrimitiveType.Quad);
        border.name = "Border";
        border.transform.SetParent(transform, false);
        border.transform.localPosition = new Vector3(0f, 0f, 0.01f * CARD_SCALE);
        border.transform.localScale = new Vector3((1f + BORDER * 2f) * CARD_SCALE, (0.6f + BORDER * 2f) * CARD_SCALE, 1f);
        var borderRend = border.GetComponent<MeshRenderer>();
        borderRend.sharedMaterial = SharedBgMat();
        borderRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        borderRend.receiveShadows = false;
        var bmpb = new MaterialPropertyBlock();
        bmpb.SetColor("_Color", Hex(0xFFD700));   // 金黄色
        borderRend.SetPropertyBlock(bmpb);
        _borderRend = borderRend;   // 淡入淡出需同步其 alpha（否则结果淡出后金边残留一小会）
        _borderMpb = bmpb;
        var bcol = border.GetComponent<Collider>();
        if (bcol != null) Destroy(bcol);

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
        _txtGo = txt.gameObject;

        // 结果视频（success/fail）提前后台 Prepare：结果态来临时立即可从头播放一遍再淡出，
        // 不等本地 Prepare（否则结果到了还要等就绪，且不能保证从头）。working/claim 走全局共享播放器（见
        // TaskBadgeManager.EnsureSharedVideo），不在此重复准备（本地 slot 仅作共享未就绪的回退）。
        EnsureVideo(SUCCESS_VIDEO).player.Prepare();
        EnsureVideo(FAIL_VIDEO).player.Prepare();
    }

    /// <summary>由 TaskBadgeManager 每帧调用。同状态幂等（不重置动画）；
    /// 正在播放 Success/Fail 时忽略稳态 Hidden（让它播完再淡出销毁），只有新任务出现才打断。</summary>
    public void SetState(TaskCardState state, string taskType)
    {
        if (!string.IsNullOrEmpty(taskType)) _taskType = taskType;

        // Intro 延长展示：数据上 roundCost==0 仅 1 回合，数据第 2 回合已变 Working，
        // 但卡片继续停留 Intro 到满 INTRO_MIN_ROUNDS 回合（图片一闪而过问题）。
        // 结果态（Success/Fail）不受延迟，可直接打断；Seek/跳回合时按新回合数据立即生效。
        if (_state == TaskCardState.Intro && state == TaskCardState.Working
            && _player != null && _player.cur - _introStartCur < INTRO_MIN_ROUNDS)
            return;

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
        // 记录 Intro 起始回合：数据上 roundCost==0 仅接任务那 1 回合，靠本字段延长展示
        if (state == TaskCardState.Intro)
            _introStartCur = _player != null ? _player.cur : -1;
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
        _fallback = false;
        switch (state)
        {
            case TaskCardState.Intro:
                // 执行任务第一个回合播放「领取任务」视频（全局共享已就绪则立即可用；未就绪保持 task.png 兜底）
                _stateColor = Color.white;
                BeginClaimVideo();
                break;
            case TaskCardState.Working:
                BeginWorkingVideo();                    // 就绪→直接上视频；未就绪→保持 Intro 图，就绪后无缝接管
                break;
            case TaskCardState.Success:
                _resultDuration = RESULT_DURATION;
                BeginResultVideo(SUCCESS_VIDEO);        // 就绪→上 success 视频；未就绪→保持工作视频/图片，就绪后接管
                break;
            case TaskCardState.Fail:
                _resultDuration = RESULT_DURATION;
                BeginResultVideo(FAIL_VIDEO);           // 同上，fail 视频
                break;
        }
        // Intro 图 / Working 视频 / Success/Fail 视频均自带字样 → 隐藏文字；
        // 仅"视频加载失败 → 纯色+文字"降级态（_fallback）显示文字。
        if (_txtGo != null) _txtGo.SetActive(_fallback);
        ApplyAlpha(1f);
    }

    /// <summary>设置底板主纹理（null 恢复纯色）。走 MaterialPropertyBlock，不污染共享材质。
    /// Sprites/Default 最终色 = tex × _Color，所以 Intro 需 _Color=白。
    /// 注意：MaterialPropertyBlock.SetTexture 不接受 null（会抛 ArgumentNullException，且没有
    /// 单属性清除 API），清除纹理必须用 _mpb.Clear() 清空全部属性 —— _Color 由 ApplyAlpha 每帧重设，不受影响。
    /// tex 可为 Texture2D（Intro 图）或 RenderTexture（Working 视频）。</summary>
    void SetMainTexture(Texture tex)
    {
        if (_bgRend == null || _mpb == null) return;
        _mpb.Clear();                                  // 清掉旧 _MainTex/_Color 等全部属性（清除纹理的唯一安全方式）
        if (tex != null) _mpb.SetTexture("_MainTex", tex);
        // 立刻补回 _Color：OnVideoPrepared 是异步回调，此时可能已暂停（Update 提前 return 不再跑
        // ApplyAlpha）——若不清 _Color 会残留全透明(0,0,0,0)，视频/图片底图在暂停时不可见。
        // _stateColor 的取值：Intro=白、Working 视频就绪后=白（tex×_Color 不 tint）、等待/降级/结果态=该态颜色。
        _mpb.SetColor("_Color", new Color(_stateColor.r, _stateColor.g, _stateColor.b, 1f));
        _bgRend.SetPropertyBlock(_mpb);
    }

    /// <summary>Intro 底板贴图：Resources 懒加载（打包进 Build，WebGL 同步可用）；缺失回退 null → 纯色版。</summary>
    static Texture2D IntroTex()
    {
        if (s_introTex == null)
            s_introTex = Resources.Load<Texture2D>(RES_TASK_TEX);
        return s_introTex;
    }

    // ══════════ 视频渲染（多 slot：working/success/fail 各一个 VideoPlayer + RenderTexture）══════════

    /// <summary>确保某视频的 slot 已创建并配置（URL/循环/事件）。不触发 Prepare。</summary>
    VideoSlot EnsureVideo(string url)
    {
        VideoSlot s;
        if (_videos.TryGetValue(url, out s)) return s;
        s = new VideoSlot { url = url };
        var vp = gameObject.AddComponent<VideoPlayer>();
        vp.source = VideoSource.Url;
        vp.url = VideoUrl(url);
        vp.isLooping = true;                       // 常驻循环（结果视频循环到淡出）
        vp.playOnAwake = false;
        vp.skipOnDrop = true;                      // WebGL 低帧率时跳过掉帧，保持实时
        vp.renderMode = VideoRenderMode.RenderTexture;
        vp.audioOutputMode = VideoAudioOutputMode.None;   // 静音：WebGL 浏览器 autoplay 策略才放行
        vp.prepareCompleted += OnVideoPrepared;
        vp.errorReceived += OnVideoError;
        vp.loopPointReached += OnVideoLoop;   // 循环兜底：个别平台 isLooping 失效时在此续播
        s.player = vp;
        _videos[url] = s;
        return s;
    }

    /// <summary>进入 Working：让 working 视频接管底板。优先用全局共享 working 视频（游戏开始即就绪并循环
    /// 播放，见 TaskBadgeManager.EnsureSharedVideo）——Working 态立即可显示，不等各自 Prepare（否则 working
    /// 视频本地 Prepare 需数秒，Intro/Working 阶段太短，Working 全程只能显示 Intro 图再直接跳结果；WebGL
    /// 走网络加载尤其必要）。共享 RT 未就绪的极端情况回退本地 slot（Intro 图兜底 + Prepare，就绪后接管）。
    /// 切换无中间态：目标视频未就绪时保持当前画面，绝不切蓝/空贴图；加载失败优雅降级纯色+文字。</summary>
    void BeginWorkingVideo()
    {
        _stateColor = Color.white;

        // 优先用全局共享 working 视频（游戏开始即就绪并循环播放，见 TaskBadgeManager.EnsureSharedVideo）：
        // Working 态立即可显示，不等各自 Prepare——否则 working 视频本地 Prepare 需数秒（播放期渲染负载下
        // 更慢），Intro/Working 阶段太短，Working 全程只能显示 Intro 图再直接跳结果。共享 RT 未就绪的
        // 极端情况回退旧逻辑（Intro 图兜底 + 本地 Prepare）。
        var shared = TaskBadgeManager.GetSharedVideoRT(WORKING_VIDEO);
        if (shared != null)
        {
            _shownUrl = WORKING_VIDEO;
            SetMainTexture(shared);
            return;
        }

        var slot = EnsureVideo(WORKING_VIDEO);
        if (slot.failed)
        {
            _fallback = true;
            SetFallbackVisual(TaskCardState.Working);   // 降级纯色蓝 + "破解中"
            return;
        }
        if (slot.prepared && slot.rt != null)
        {
            _shownUrl = WORKING_VIDEO;
            SetMainTexture(slot.rt);                    // 已就绪：直接上视频
        }
        else
        {
            if (_shownUrl == null) SetMainTexture(IntroTex());  // 未就绪且无当前画面 → Intro 图兜底（无白底）
            slot.player.Prepare();
        }
    }

    /// <summary>进入 Intro：让「领取任务」视频接管底板（逻辑同 BeginWorkingVideo，优先全局共享已就绪 RT）。
    /// 加载失败降级为 task.png 图片（不叠文字，图片自带「接受任务」字样）。</summary>
    void BeginClaimVideo()
    {
        var shared = TaskBadgeManager.GetSharedVideoRT(CLAIM_VIDEO);
        if (shared != null)
        {
            _shownUrl = CLAIM_VIDEO;
            SetMainTexture(shared);
            return;
        }
        var slot = EnsureVideo(CLAIM_VIDEO);
        if (slot.failed)
        {
            SetMainTexture(IntroTex());          // 降级：task.png 图片兜底
            return;
        }
        if (slot.prepared && slot.rt != null)
        {
            _shownUrl = CLAIM_VIDEO;
            SetMainTexture(slot.rt);
        }
        else
        {
            if (_shownUrl == null) SetMainTexture(IntroTex());  // 未就绪且无当前画面 → Intro 图兜底
            slot.player.Prepare();
        }
    }

    /// <summary>进入 Success/Fail：让结果视频接管底板。结果视频必须**从头播放一遍再淡出**（不是按回合循环）——
    /// 用卡片自己的 slot（Awake 已后台 Prepare，见 PreloadResult 注释）：就绪 → `time=0` + 开播 + 直接显示自己 RT，
    /// 播完（_resultDuration = 视频时长）淡出销毁。**不用全局共享结果播放器的 RT**——共享播放器一直在循环，
    /// `VideoPlayer.time=0` 的 seek 在播放中是延迟生效的，卡片切过去会先看到旧位置的画面（若恰好靠近开头，
    /// 表现为"前 ~1s 被播两遍"）。未就绪（极少数）保持当前画面 + Prepare，就绪后由 Update 无缝接管。</summary>
    void BeginResultVideo(string url)
    {
        var slot = EnsureVideo(url);
        if (slot.failed)
        {
            _fallback = true;
            SetFallbackVisual(_state);                  // 降级纯色 + 文字
            return;
        }
        if (slot.prepared && slot.rt != null)
        {
            slot.player.isLooping = false;              // 结果视频播一遍就停（末尾停帧，淡出销毁）
            _resultDuration = Mathf.Max(_resultDuration, (float)slot.player.length);   // 播一遍淡出
            slot.player.time = 0;                       // 从头（fresh/暂停态 seek 立即生效）
            PlayVideo(slot);                            // 从头播放（不可见）
            // 不设 _shownUrl / MainTex：保持当前画面（working 视频/Intro 图）不动，
            // 由 Update 渲染出帧（frame>=1）后无缝接管——避免刚分配的 RT 黑帧闪一下。
            // 直接创建在结果态（Seek/暂停）时无当前画面 → Intro 图兜底，避免白/空底。
            if (_shownUrl == null) SetMainTexture(IntroTex());
            return;
        }
        // 未就绪：保持当前画面（working 视频 / Intro 图）不动，就绪后由 Update 无缝接管
        if (_shownUrl == null) SetMainTexture(IntroTex());  // 无当前画面 → Intro 图兜底（无白底）
        slot.player.Prepare();
    }

    /// <summary>视频就绪回调：交给 MarkSlotPrepared（与 WebGL isPrepared 轮询共用同一套就绪逻辑）。
    /// 显示/播放由 Update 按状态驱动，本回调不做任何自动开播/换贴图（避免"就绪即抢底板"破坏当前画面）。</summary>
    void OnVideoPrepared(VideoPlayer vp)
    {
        MarkSlotPrepared(FindSlot(vp));
    }

    /// <summary>slot 就绪同步（prepareCompleted 回调 + WebGL isPrepared 轮询共用）：分配该 slot 的
    /// RenderTexture 并绑定、结果态记录时长、working 就绪后延迟准备 success/fail。</summary>
    void MarkSlotPrepared(VideoSlot slot)
    {
        if (slot == null || slot.player == null) return;
        slot.prepared = true;
        slot.failed = false;
        int vw = (int)slot.player.width;
        int vh = (int)slot.player.height;
        if (slot.rt == null || slot.rt.width != vw || slot.rt.height != vh)
        {
            if (slot.rt != null) slot.rt.Release();
            slot.rt = new RenderTexture(Mathf.Max(2, vw), Mathf.Max(2, vh), 0);
        }
        slot.player.targetTexture = slot.rt;
        if (slot.url == SUCCESS_VIDEO || slot.url == FAIL_VIDEO)
            _resultDuration = Mathf.Max(_resultDuration, (float)slot.player.length + 0.1f);   // 看完一遍结果视频再淡出

        // working 就绪后才后台准备 success/fail（结果态备用）：避免并发 Prepare 抢解码器
        if (slot.url == WORKING_VIDEO && !_resultPreloadStarted)
        {
            _resultPreloadStarted = true;
            EnsureVideo(SUCCESS_VIDEO).player.Prepare();
            EnsureVideo(FAIL_VIDEO).player.Prepare();
        }
    }

    /// <summary>轮询兜底：WebGL 上 prepareCompleted 可能不触发，改用 isPrepared 轮询同步本地 slot 就绪态。</summary>
    void SyncPreparedSlots()
    {
        foreach (var kv in _videos)
        {
            VideoSlot s = kv.Value;
            if (s == null || s.player == null || s.prepared || s.failed) continue;
            if (s.player.isPrepared) MarkSlotPrepared(s);
        }
    }

    /// <summary>视频出错：标记该 slot 失败并释放资源；若它是当前状态的目标视频 → 降级纯色+文字（不崩）。
    /// 非目标视频（如 Intro 后台预载的 working）出错仅标记，不打扰当前画面。</summary>
    void OnVideoError(VideoPlayer vp, string message)
    {
        VideoSlot slot = FindSlot(vp);
        if (slot == null) return;
        Debug.LogWarning("[TaskCardBadge] 视频加载失败: " + slot.url + " → " + message);
        slot.failed = true;
        slot.prepared = false;
        if (slot.rt != null) { slot.rt.Release(); slot.rt = null; }
        if (_shownUrl == slot.url) _shownUrl = null;
        if (slot.url == TargetDisplayUrl())
        {
            _fallback = true;
            SetFallbackVisual(_state);
        }
    }

    /// <summary>视频加载失败降级：当前状态回退纯色 + 文字（Working 蓝"破解中"/Success 绿"✓ 通过"/Fail 红"× 失败"）。</summary>
    void SetFallbackVisual(TaskCardState state)
    {
        switch (state)
        {
            case TaskCardState.Working: _stateColor = Hex(0x2E86DE); SetText("破解中"); break;
            case TaskCardState.Success: _stateColor = Hex(0x27AE60); SetText("✓ 通过"); break;
            case TaskCardState.Fail:    _stateColor = Hex(0xC0392B); SetText("× 失败"); break;
            default: return;
        }
        if (_txtGo != null) _txtGo.SetActive(true);
        SetMainTexture(null);
        ApplyAlpha(1f);
    }

    /// <summary>按暂停冻结同步视频播放：playing 时播放，暂停时 Pause。
    /// 视频时长必须跟回合走：放完一帧循环素材后（回合未结束）回到开头继续播。
    /// isLooping=true 为主，个别平台/格式失效时靠 loopPointReached 与"未在播放→回到开头重播"双兜底。
    /// 未就绪（!prepared）或已失败（failed）不干预，避免准备期空转/播放坏视频。</summary>
    void PlayVideo(VideoSlot s)
    {
        if (s == null || s.player == null || !s.prepared || s.failed) return;
        bool shouldPlay = _player == null || _player.playing;
        if (shouldPlay)
        {
            if (!s.player.isPlaying)
            {
                if (s.player.isPaused) s.player.Play();       // 仅是暂停 → 直接续播（不重头）
                else { s.player.time = 0; s.player.Play(); }  // 已放完停在结尾 → 回到开头再播（循环）
            }
        }
        else if (s.player.isPlaying) s.player.Pause();
    }

    /// <summary>循环兜底：视频播到结尾（loopPointReached）回到开头续播。
    /// 仅 working 循环续播（显示时长跟回合走）；success/fail 结果视频**播一遍就停在末尾等淡出**，
    /// 不在此重启（否则又变成循环，且末尾重启会露出开头画面）。isLooping 正常时本回调也触发，
    /// 重置到 0 即循环本身，幂等无害。</summary>
    void OnVideoLoop(VideoPlayer vp)
    {
        VideoSlot slot = FindSlot(vp);
        if (slot != null && (slot.url == SUCCESS_VIDEO || slot.url == FAIL_VIDEO)) return;   // 结果不循环
        vp.time = 0;
        bool shouldPlay = _player == null || _player.playing;
        if (shouldPlay) vp.Play();
    }

    /// <summary>当前状态应显示的视频 url（null=图片/纯色，无需视频）。</summary>
    string TargetDisplayUrl()
    {
        switch (_state)
        {
            case TaskCardState.Intro:   return CLAIM_VIDEO;
            case TaskCardState.Working: return WORKING_VIDEO;
            case TaskCardState.Success: return SUCCESS_VIDEO;
            case TaskCardState.Fail:    return FAIL_VIDEO;
            default: return null;
        }
    }

    /// <summary>由 VideoPlayer 找所属 slot（回调参数是具体 player）。</summary>
    VideoSlot FindSlot(VideoPlayer vp)
    {
        foreach (var kv in _videos)
            if (kv.Value.player == vp) return kv.Value;
        return null;
    }

    /// <summary>视频 URL：WebGL 用相对 StreamingAssets 路径（浏览器/VideoPlayer 相对当前页解析，
    /// 剥掉 http(s)://host 防协议混用；**必须用正斜杠拼接，不能 Path.Combine——Windows 下会出反斜杠，
    /// 浏览器无法播放**）；Standalone/Editor 用文件系统绝对路径。TaskBadgeManager 的共享视频预载也复用此方法。</summary>
    public static string VideoUrl(string file)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        string path = Application.streamingAssetsPath;
        int scheme = path.IndexOf("://", System.StringComparison.Ordinal);
        if (scheme >= 0)
        {
            int hostEnd = path.IndexOf('/', scheme + 3);
            path = hostEnd >= 0 ? path.Substring(hostEnd + 1) : "";
        }
        else path = path.TrimStart('/');
        path = path.TrimEnd('/');
        return string.IsNullOrEmpty(path) ? file : path + "/" + file;
#else
        return Path.Combine(Application.streamingAssetsPath, file);
#endif
    }

    void Update()
    {
        SyncPreparedSlots();   // WebGL isPrepared 轮询兜底（prepareCompleted 不可靠时同步本地 slot 就绪态）

        // 暂停冻结：所有动画计时不推进（点循环 / 弹跳 / 抖动 / 淡入淡出全部暂停）
        bool paused = _player != null && !_player.playing;
        if (paused)
        {
            foreach (var kv in _videos)   // 所有视频随暂停冻结
                if (kv.Value.player != null && kv.Value.player.isPlaying) kv.Value.player.Pause();
            return;
        }

        // 视频按状态驱动（无中间态核心）：
        //  - Intro（目标=null）：working 预卷蓄帧，为 Working 直接接视频做准备；其余视频停播
        //  - Working/Success/Fail（目标=对应视频）：目标未显示时开播，渲染出帧（frame>=1）瞬间接管底板，
        //    未就绪则保持当前画面（Intro 图/working 视频）不动——绝不切蓝/白/空贴图；
        //    已显示后保证其播放并停掉其余视频（Seek 后旧结果视频等）
        string target = TargetDisplayUrl();
        if (target != null)
        {
            VideoSlot slot;
            if (_videos.TryGetValue(target, out slot) && slot.prepared && slot.rt != null && !slot.failed)
            {
                if (_shownUrl == target)
                {
                    // 已显示：保证播放（暂停后恢复）。结果视频播完（_elapsed 到时长 / 视频到末尾）即进入淡出，
                    // 不再续播/重启（isLooping=false 停在末尾，PlayVideo 会把末尾的视频 time=0 重启，露开头画面）。
                    bool isResult = _state == TaskCardState.Success || _state == TaskCardState.Fail;
                    bool resultDone = isResult && (_elapsed >= _resultDuration
                        || slot.player.time >= slot.player.length - 0.05f);
                    if (!resultDone) PlayVideo(slot);
                    foreach (var kv in _videos)                  // 其余视频停掉（结果接管后 working 等）
                        if (kv.Key != target && kv.Value.player != null && kv.Value.player.isPlaying)
                            kv.Value.player.Pause();
                }
                else if (slot.player.isPlaying && slot.player.frame >= 1)
                {
                    _shownUrl = target;                          // 已渲染出帧 → 无缝接管（前一画面保持，无空档）
                    _stateColor = Color.white;
                    SetMainTexture(slot.rt);
                }
                else
                {
                    PlayVideo(slot);                             // 尚未渲染 → 开播；当前画面保持到出帧
                    if (_shownUrl == WORKING_VIDEO)              // 若当前画面是 working 视频 → 保持其播放（勿冻结）
                    {
                        VideoSlot w;
                        if (_videos.TryGetValue(WORKING_VIDEO, out w)) PlayVideo(w);
                    }
                }
            }
        }
        else
        {
            // Intro：working 预卷蓄帧（为 Working 直接接视频做准备）；Hidden：全部停播（结果已结束）
            bool preRollWorking = _state == TaskCardState.Intro;
            foreach (var kv in _videos)
            {
                bool wantPlay = preRollWorking && kv.Key == WORKING_VIDEO;
                if (wantPlay) PlayVideo(kv.Value);
                else if (kv.Value.player != null && kv.Value.player.isPlaying) kv.Value.player.Pause();
            }
        }

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
                    // 视频模式：点循环文字不渲染（视频自带"破解中"），仅降级纯色版需要
                    if (_fallback)
                    {
                        int dots = (int)(_elapsed / DOT_INTERVAL) % 4;  // 0 1 2 3 循环
                        if (dots != _lastDots) { _lastDots = dots; SetText("破解中" + new string('.', dots)); }
                    }
                    break;
                }
            case TaskCardState.Success:
                if (_fallback) ApplyBounce();   // 视频自带动画；仅降级纯色版弹跳
                break;
            case TaskCardState.Fail:
                if (_fallback) ApplyShake();    // 视频自带动画；仅降级纯色版抖动
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

        // Success/Fail 播完（看完一遍视频）→ 淡出 → Hidden
        if (_state == TaskCardState.Success || _state == TaskCardState.Fail)
        {
            if (_elapsed >= _resultDuration)
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

    /// <summary>同步背景板（MPB）、文字（TextMesh color.a）与金黄描边（MPB）的 alpha——淡入淡出三者一致，
    /// 否则结果淡出后背景已透明而金边仍不透明，卡片结束残留一个黄色边框。</summary>
    void ApplyAlpha(float a)
    {
        if (_bgRend != null)
        {
            _mpb.SetColor("_Color", new Color(_stateColor.r, _stateColor.g, _stateColor.b, a));
            _bgRend.SetPropertyBlock(_mpb);
        }
        if (_tm != null) _tm.color = new Color(1f, 1f, 1f, a);
        if (_borderRend != null && _borderMpb != null)
        {
            _borderMpb.SetColor("_Color", new Color(1f, 215f / 255f, 0f, a));   // 金黄 #FFD700 + alpha
            _borderRend.SetPropertyBlock(_borderMpb);
        }
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

    void OnDestroy()
    {
        // 释放全部视频资源：VideoPlayer 停止 + 解绑事件 + RenderTexture Release（防内存泄漏）
        foreach (var kv in _videos)
        {
            VideoSlot s = kv.Value;
            if (s.player != null)
            {
                s.player.Stop();
                s.player.targetTexture = null;
                s.player.prepareCompleted -= OnVideoPrepared;
                s.player.errorReceived -= OnVideoError;
                s.player.loopPointReached -= OnVideoLoop;
            }
            if (s.rt != null)
            {
                s.rt.Release();
                Destroy(s.rt);
            }
        }
        _videos.Clear();
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
