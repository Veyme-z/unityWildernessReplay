using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// 回放全屏剧情视频（挂 ReplayEntry 同一 GameObject，DontDestroyOnLoad）。
/// 数据驱动（对齐 TaskBadgeManager / MissionVehicleDriver，不监听命令事件），多个触发共用一套暂停/恢复：
///   ① 自然进入「夜晚第一个回合」 → ufo.mp4（每轮夜晚首回合都播，本局 1027 回合在 R81/211/341/471/601/731/861/991）
///   ② 任务点1领取（自进化类1、roundCost==0）→ plane.mp4
/// 任一轮若同时命中多个（本局无此情况），优先夜晚（代码先判 ①）。
///
/// 触发流程：读 rounds[cur-1]/rounds[cur-2] 做「自然跨入」检测（与 ReplayPlayer.OnRoundEntered 的相位变化
/// 口径一致：IsNight 由 false→true = 入夜首回合）；仅 player.playing 时触发（拖动/跳转 paused 不触发）：
///   1. SetPlaying(false) 暂停回放  2. 全屏黑底 + 视频（Canvas sortingOrder 1000 > 全部面板 200~500）
///   3. 播完自动 SetPlaying(true) 恢复；锁存 handledRound，恢复后同回合不重复打断，跨回合即松开。
///
/// WebGL：URL 走 TaskCardBadge.VideoUrl()（相对正斜杠）；audioOutputMode 在 WebGL 强制 None（autoplay 放行），
/// 编辑器/PC 用 Direct 出声；RenderTexture + RawImage 渲染；isPrepared 轮询兜底。Start 预热两类视频。
/// 硬约束：不改 ReplayParser / ReplayState / 伤害计算；UI 全部运行时生成，不动 .prefab/.unity/.meta。
/// </summary>
public class ReplayCinematic : MonoBehaviour
{
    // ══ 触发 → 视频 映射（如需加新触发，在 ResolveVideo() 里加判定即可）══
    public const string TASK_POINT1_TYPE = "自进化类1";          // 任务点1（宝箱，game 14,14/23,14）
    const string NIGHT_VIDEO  = "ufo.mp4";   // Assets/StreamingAssets 根目录，H.264(avc1)，每轮夜晚首回合
    const string TASK1_VIDEO  = "plane.mp4"; // 任务点1领取

    const int CANVAS_SORT = 1000;            // 高于全部 UI 面板（最高 SettlementPanel 500）
    const float PREPARE_TIMEOUT = 10f;       // Prepare 最久等待（WebGL 走网络，给足时间）
    const float DONE_GRACE = 2f;             // 播完后额外宽限（收尾，防边缘差一帧）

    ReplayPlayer _player;

    // ---- 视频（每文件一个 slot，惰性创建 + Start 预热；同名复用，多次触发不重连）----
    class Slot
    {
        public string file;
        public VideoPlayer vp;
        public RenderTexture rt;
        public bool prepared;    // Prepare 完成且已建 RT
        public bool failed;      // 加载失败（下次触发会再试一次）
    }
    readonly Dictionary<string, Slot> _slots = new Dictionary<string, Slot>();
    Slot _cur;                   // 正在播放的 slot（事件唤醒收尾用）
    bool _done;                  // 本段播放结束（loopPointReached / error / 时间兜底置位）

    // ---- 全屏 UI（懒创建，隐藏根节点）----
    GameObject _uiRoot;
    RectTransform _videoRect;
    RawImage _videoImage;
    bool _uiBuilt;

    int _handledRound = -1;      // 已触发过的回合：恢复后同回合不重复打断，跨回合即松开
    bool _active;                // 全屏播放中（回放已暂停，本帧不再检测新触发）

    // ═══════════ 生命周期 ═══════════
    void Start()
    {
        // 预热：尽早 Prepare 两类视频。领取/入夜回合到达时通常已就绪、无黑屏等待
        //（本局最早的任务点1领取在 R10、首次入夜在 R81，Start 起 Prepare 有充足时间）
        EnsureSlot(TASK1_VIDEO);
        EnsureSlot(NIGHT_VIDEO);
    }

    void Update()
    {
        if (_player == null)
        {
            _player = FindObjectOfType<ReplayPlayer>();
            if (_player == null) return;
        }
        if (_player.data == null || _player.data.rounds == null) return;

        if (_active) return;               // 播放中：等协程收尾，不做新检测

        int cur = _player.cur;
        if (cur < 1 || cur > _player.data.rounds.Count) return;

        // 锁存：已离开上次触发的回合 → 松开，允许下次再触发
        if (_handledRound >= 0 && cur != _handledRound) _handledRound = -1;
        // 恢复播放后仍停在触发回合：等待它自然推进（避免刚恢复又立刻重复打断）
        if (_handledRound == cur) return;

        // 仅自然播放中跨入（拖动/跳转 paused 时 cur 变化但不触发）
        if (!_player.playing) return;

        string file = ResolveVideo(cur);
        if (file != null)
        {
            _handledRound = cur;
            StartCoroutine(PlayRoutine(file));
        }
    }

    void OnDestroy()
    {
        _active = false;
        foreach (var kv in _slots)
        {
            Slot s = kv.Value;
            if (s == null) continue;
            if (s.vp != null)
            {
                s.vp.Stop();
                s.vp.targetTexture = null;
            }
            if (s.rt != null)
            {
                s.rt.Release();
                Destroy(s.rt);
                s.rt = null;
            }
        }
        _slots.Clear();
    }

    // ═══════════ 触发判定 ═══════════
    /// <summary>当前回合若发生「自然跨入的剧情回合」，返回对应视频；否则 null。
    /// 多触发同回合时夜晚优先（本局数据无此类叠加）。</summary>
    string ResolveVideo(int cur)
    {
        // ① 自然进入夜晚第一个回合（相位 false→true，与 DayNightController / OnRoundEntered 同口径）
        if (cur >= 2 && StateEngine.IsNight(cur) && !StateEngine.IsNight(cur - 1))
            return NIGHT_VIDEO;
        // ② 任务点1领取（某队上一回合无自进化类1、本回合出现且 roundCost==0）
        if (IsTaskPoint1Claim(cur))
            return TASK1_VIDEO;
        return null;
    }

    /// <summary>当前回合是否发生「任务点1 领取」。</summary>
    bool IsTaskPoint1Claim(int cur)
    {
        var rounds = _player.data.rounds;
        var curR = rounds[cur - 1];
        var prevR = cur >= 2 ? rounds[cur - 2] : null;
        if (curR == null || curR.teams == null) return false;

        for (int i = 0; i < curR.teams.Count; i++)
        {
            var team = curR.teams[i];
            var task = team != null ? team.task : null;
            if (task == null) continue;
            if (task.taskType != TASK_POINT1_TYPE) continue;
            if (task.roundCost != 0) continue;                 // 领取回合唯一标志（之后逐回合递增）
            if (!HasTaskType1(prevR, team.teamId)) return true; // 上一回合该队不得已在跑同任务
        }
        return false;
    }

    static bool HasTaskType1(ReplayRound r, string teamId)
    {
        if (r == null || r.teams == null || string.IsNullOrEmpty(teamId)) return false;
        for (int i = 0; i < r.teams.Count; i++)
        {
            var t = r.teams[i];
            if (t == null || t.teamId != teamId || t.task == null) continue;
            if (t.task.taskType == TASK_POINT1_TYPE) return true;
        }
        return false;
    }

    // ═══════════ 全屏播放流程 ═══════════
    IEnumerator PlayRoutine(string file)
    {
        if (_active) yield break;
        _active = true;
        _done = false;

        // 1. 暂停地图
        _player.SetPlaying(false);

        // 2. 全屏黑底立刻盖住一切（视频就绪前先遮黑）
        BuildUi();
        _uiRoot.SetActive(true);
        if (_videoImage != null) _videoImage.gameObject.SetActive(false);

        // 3. 确保该视频 slot 已配置；未就绪则等待 Prepare（超时/失败都放行恢复，绝不软锁暂停）
        Slot slot = EnsureSlot(file);
        if (slot.failed)
        {
            slot.failed = false;   // 上次失败（如 WebGL 偶发）→ 本次重试一次
            slot.prepared = false;
            slot.vp.Prepare();
        }
        if (!slot.prepared)
        {
            if (!slot.vp.isPrepared) slot.vp.Prepare();
            float deadline = Time.realtimeSinceStartup + PREPARE_TIMEOUT;
            while (!slot.prepared && !slot.failed)
            {
                if (slot.vp.isPrepared) MarkPrepared(slot);   // WebGL prepareCompleted 可能不触发，轮询兜底
                else if (Time.realtimeSinceStartup > deadline) { slot.failed = true; break; }
                yield return null;
            }
        }
        if (slot.failed || !slot.prepared || !_active)
        {
            Debug.LogWarning("[ReplayCinematic] 视频 " + file + " 未就绪，跳过全屏（自动恢复播放）");
            FinishRoutine();
            yield break;
        }

        // 4. 挂上 RT、按视频宽高比适配屏幕，从头播放
        _cur = slot;
        _done = false;
        AttachTextureAndFit(slot);
        slot.vp.time = 0;
        slot.vp.Play();
        Debug.Log("[ReplayCinematic] 全屏播放 " + file + "（回合 " + _handledRound + "）");

        // 5. 等待播完：loopPointReached / 时间兜底 / 失败 / 超时 任一即收尾
        double len = slot.vp.length;
        float waitable = (float)(len > 0.5 ? len : 6.0) + DONE_GRACE;
        float t0 = Time.realtimeSinceStartup;
        while (_active && !_done && !slot.failed)
        {
            if (!slot.vp.isPlaying && slot.vp.isPrepared && slot.vp.frame >= 1) _done = true;  // 播完自动停下
            else if (slot.vp.isPlaying && len > 0.5 && slot.vp.time >= len - 0.03) _done = true; // 个别平台 loopPointReached 不触发
            if (Time.realtimeSinceStartup - t0 > waitable) _done = true;                         // 保险丝：无论如何不无限暂停
            yield return null;
        }

        // 6. 结束：停视频、隐藏全屏、自动恢复播放
        FinishRoutine();
    }

    void FinishRoutine()
    {
        if (!_active) return;
        _active = false;
        _cur = null;
        if (_slots.Count > 0)
            foreach (var kv in _slots)
                if (kv.Value != null && kv.Value.vp != null && kv.Value.vp.isPlaying) kv.Value.vp.Pause();
        if (_uiRoot != null) _uiRoot.SetActive(false);
        if (_player != null && !_player.playing) _player.SetPlaying(true);   // 播完自动恢复
    }

    // ═══════════ 视频 slot（每文件一个 VideoPlayer，惰性创建 + 复用） ═══════════
    Slot EnsureSlot(string file)
    {
        Slot s;
        if (_slots.TryGetValue(file, out s) && s != null) return s;
        s = new Slot { file = file };
        var vp = gameObject.AddComponent<VideoPlayer>();
        vp.source = VideoSource.Url;
        vp.url = TaskCardBadge.VideoUrl(file);   // WebGL 相对正斜杠 URL（勿用 Path.Combine）
        vp.isLooping = false;                    // 播一遍即结束
        vp.playOnAwake = false;
        vp.skipOnDrop = true;                    // WebGL 低帧率跳帧保持实时
        vp.renderMode = VideoRenderMode.RenderTexture;
#if UNITY_WEBGL && !UNITY_EDITOR
        vp.audioOutputMode = VideoAudioOutputMode.None;   // WebGL：静音保证浏览器 autoplay 放行（同 working.mp4）
#else
        vp.audioOutputMode = VideoAudioOutputMode.Direct; // 编辑器/PC：正常出声
#endif
        vp.prepareCompleted += v => OnPrepared(FindSlot(v));
        vp.errorReceived += (v, msg) => OnVideoError(FindSlot(v), msg);
        vp.loopPointReached += v => OnVideoEnd(FindSlot(v));
        s.vp = vp;
        _slots[file] = s;
        vp.Prepare();   // 预热
        return s;
    }

    void OnPrepared(Slot s)
    {
        if (s != null) MarkPrepared(s);
    }

    /// <summary>就绪同步（prepareCompleted 回调 + WebGL isPrepared 轮询共用）：建 RT 并绑定。</summary>
    void MarkPrepared(Slot s)
    {
        if (s == null || s.vp == null || !s.vp.isPrepared) return;
        if (!s.prepared)
        {
            int vw = Mathf.Max(2, (int)s.vp.width);
            int vh = Mathf.Max(2, (int)s.vp.height);
            if (s.rt == null || s.rt.width != vw || s.rt.height != vh)
            {
                if (s.rt != null) s.rt.Release();
                s.rt = new RenderTexture(vw, vh, 0);
            }
            s.vp.targetTexture = s.rt;
            s.prepared = true;
            s.failed = false;
        }
    }

    void OnVideoError(Slot s, string message)
    {
        if (s == null) return;
        Debug.LogWarning("[ReplayCinematic] 视频加载失败: " + s.file + " → " + message);
        s.failed = true;
        if (s == _cur) _done = true;   // 唤醒等待，走收尾（自动恢复，不软锁）
    }

    void OnVideoEnd(Slot s)
    {
        if (s != null && s == _cur) _done = true;   // 播完
    }

    Slot FindSlot(VideoPlayer vp)
    {
        foreach (var kv in _slots)
            if (kv.Value != null && kv.Value.vp == vp) return kv.Value;
        return null;
    }

    // ═══════════ 全屏 UI（运行时生成，懒创建） ═══════════
    void BuildUi()
    {
        if (_uiBuilt) return;
        _uiBuilt = true;

        var root = new GameObject("ReplayCinematicCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasRenderer), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        _uiRoot = root;

        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = CANVAS_SORT;   // 高于全部面板（200~500）→ 全屏覆盖顶层面板

        // 全屏黑底：视频就绪前遮黑 + 吞掉点击（raycastTarget 默认 true），防误触下层播放/进度条
        var bgGo = new GameObject("Backdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        bgGo.transform.SetParent(root.transform, false);
        var bgRt = (RectTransform)bgGo.transform;
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;
        bgGo.GetComponent<Image>().color = Color.black;

        // 视频 RawImage：初始隐藏，就绪后按宽高比适配屏幕（letterbox，四周留黑）
        var vGo = new GameObject("Video", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        vGo.transform.SetParent(root.transform, false);
        _videoRect = (RectTransform)vGo.transform;
        _videoRect.anchorMin = _videoRect.anchorMax = new Vector2(0.5f, 0.5f);
        _videoRect.pivot = new Vector2(0.5f, 0.5f);
        _videoRect.sizeDelta = Vector2.zero;
        _videoImage = vGo.GetComponent<RawImage>();
        _videoImage.color = Color.white;
        _videoImage.raycastTarget = false;
        vGo.SetActive(false);
    }

    /// <summary>把视频 RT 挂到 RawImage 并按画面宽高比适配全屏（在覆盖画布的屏幕区域内等比缩放居中）。</summary>
    void AttachTextureAndFit(Slot s)
    {
        if (_videoImage == null || s == null || s.rt == null) return;
        _videoImage.texture = s.rt;

        Rect rect = ((RectTransform)_uiRoot.transform).rect;   // Overlay 画布尺寸 = 屏幕像素（未加 CanvasScaler）
        float cw = Mathf.Max(1f, rect.width);
        float ch = Mathf.Max(1f, rect.height);
        float va = (float)Mathf.Max(1, s.vp.width) / Mathf.Max(1, s.vp.height);
        float ca = cw / ch;
        float w = ca >= va ? ch * va : cw;   // 等比适配：宽或高撑满，另一轴居中留黑边
        float h = ca >= va ? ch : cw / va;
        _videoRect.sizeDelta = new Vector2(w, h);
        _videoRect.anchoredPosition = Vector2.zero;
        _videoImage.gameObject.SetActive(true);
    }
}
