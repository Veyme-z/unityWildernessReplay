using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// 任务卡片全局管理器：挂在 ReplayEntry 同一 GameObject（跟随其 DontDestroyOnLoad）。
/// 每帧从当前回合快照（player.data.rounds[cur-1].teams[].task / roles）判定每个队伍开拓者
/// （roleType==7）头顶卡片的 4 态（Intro/Working/Success/Fail），创建/更新/销毁 TaskCardBadge。
///
/// 关键设计：不监听 executeTask / submitAnswer 命令事件，而是按"数据回合"做跳变检测：
///   - 成功 = 上一回合有任务未完成，本回合 isTaskComplete=true
///   - 失败 = 上一回合有任务未完成，本回合任务消失（超时/移除）
/// 跳变检测读的是 player.data.rounds[cur-2]（数据里的上一回合），而非上一帧快照——
/// 因此拖动进度条 / JumpTo 到任意回合时，都按目标回合的数据重建，不会把"任务在别的回合
/// 消失"误判成"当前回合失败"（旧实现存上一帧快照，Seek 后帧≠回合，导致开拓者站着
/// 没任务时也弹失败框）。
///
/// 数据源说明：任务/角色读原始当前回合快照（StateEngine.teams 是 TeamStat 字典，不含原始
/// task/roles，故用 player.data.rounds[cur-1]）；开拓者视图从 engine.units[roleId].view 取。
/// task 的 "pos != null"（原始 replay 里无任务时为 null）在 C# 模型里用
/// "task.taskType 非空" 作为等价代理。
/// </summary>
public class TaskBadgeManager : MonoBehaviour
{
    ReplayPlayer _player;

    // key = 开拓者 roleId（ReplayRole.id 为 long）
    readonly Dictionary<long, TaskCardBadge> _activeBadges = new Dictionary<long, TaskCardBadge>();
    int _lastCur = -1;  // 上一帧 cur：检测 Seek（非逐帧跳变）用

    struct TaskSnapshot
    {
        public bool hasPos;      // 有任务（taskType 非空）
        public bool isComplete;  // isTaskComplete
        public int roundCost;    // roundCost
    }

    // ═══ 共享任务视频（全局）═══
    // working/success/fail 各一个隐藏 VideoPlayer，游戏开始即 Prepare + 循环播放进共享 RT。
    // 卡片对应状态直接显示共享 RT——立即可用，不等各自 Prepare（首次视频解码初始化 + 播放期渲染负载下
    // Prepare 需数秒，Intro/Working 阶段太短根本等不起；WebGL 走网络加载更要提前就绪）。
    // 关键：URL 必须用 TaskCardBadge.VideoUrl()（WebGL 相对正斜杠路径）——绝不能 Path.Combine，
    // Windows 下会拼出 "StreamingAssets\TaskVideos\xxx.mp4" 反斜杠 URL，浏览器无法播放视频。
    static readonly Dictionary<string, VideoPlayer> s_sharedPlayers = new Dictionary<string, VideoPlayer>();
    static readonly Dictionary<string, RenderTexture> s_sharedRTs = new Dictionary<string, RenderTexture>();
    static readonly Dictionary<string, double> s_sharedLengths = new Dictionary<string, double>();

    /// <summary>防多实例：同一帧若场景存在其它 TaskBadgeManager（编译/域重载后偶发叠加），
    /// 多余实例自我销毁，避免各自维护字典、往同一开拓者 transform 下重复创建卡片导致叠卡。
    /// 每帧扫一次：Destroy 延迟生效前可能短暂重复，因此每帧都让后出现的自毁。</summary>
    void Awake()
    {
        var all = FindObjectsOfType<TaskBadgeManager>();
        for (int i = 0; i < all.Length; i++)
            if (all[i] != this) { Destroy(this); return; }
        // 仅 working 是视频（Intro/Success/Fail 均为 Resources 静态图），全局共享播放器只建它一个
        EnsureSharedVideo(TaskCardBadge.WORKING_VIDEO);
    }

    /// <summary>创建/复用某个任务视频的全局共享播放器（隐藏对象）：Prepare 完成即循环播放进共享 RT。
    /// 随播放/暂停冻结（见 Update）。卡片对应状态显示 GetSharedVideoRT()，即开即用。
    /// WebGL 两个关键：① `audioOutputMode=None`（静音 → 浏览器 autoplay 策略放行，视频才真的播放）；
    /// ② prepareCompleted 在 WebGL 上可能不可靠，Update 里用 isPrepared 轮询兜底建立 RT/开播。</summary>
    static void EnsureSharedVideo(string file)
    {
        VideoPlayer existing;
        if (s_sharedPlayers.TryGetValue(file, out existing) && existing != null) return;
        var go = new GameObject("TaskSharedVideo_" + file);
        go.hideFlags = HideFlags.HideAndDontSave;
        var vp = go.AddComponent<VideoPlayer>();
        vp.source = VideoSource.Url;
        vp.url = TaskCardBadge.VideoUrl(file);   // WebGL 安全相对正斜杠 URL（勿用 Path.Combine）
        vp.isLooping = true;
        vp.playOnAwake = false;
        vp.renderMode = VideoRenderMode.RenderTexture;
        vp.audioOutputMode = VideoAudioOutputMode.None;   // 关键：静音，浏览器 autoplay 才放行（WebGL）
        vp.prepareCompleted += v =>
        {
            RenderTexture rt;
            if (!s_sharedRTs.TryGetValue(file, out rt) || rt == null)
            {
                rt = new RenderTexture(Mathf.Max(2, (int)v.width), Mathf.Max(2, (int)v.height), 0);
                s_sharedRTs[file] = rt;
            }
            v.targetTexture = rt;
            s_sharedLengths[file] = v.length;
            v.Play();
        };
        s_sharedPlayers[file] = vp;
        vp.Prepare();
    }

    /// <summary>共享任务视频 RT（未就绪返回 null；卡片对应状态用它做即时显示底）。</summary>
    public static RenderTexture GetSharedVideoRT(string file)
    {
        RenderTexture rt;
        return s_sharedRTs.TryGetValue(file, out rt) ? rt : null;
    }

    void Update()
    {
        if (_player == null)
        {
            _player = FindObjectOfType<ReplayPlayer>();
            if (_player == null) return;
        }

        // 统计当前显示 claim(Intro)/working(Working) 的卡片数：共享视频只在有卡片需要显示时才播放，
        // 空闲期（无任务卡片）全部暂停省解码——WebGL 并发解码正是卡顿源（对齐 LycheeMap"只播当前显示的"）。
        int claimCards = 0, workingCards = 0;
        foreach (var kv in _activeBadges)
        {
            if (kv.Value == null) continue;
            switch (kv.Value.CurrentState)
            {
                case TaskCardState.Intro: claimCards++; break;
                case TaskCardState.Working: workingCards++; break;
            }
        }

        // 共享 working 视频：① 轮询 isPrepared（WebGL 上 prepareCompleted 可能不触发）兜底建立 RT + 绑定；
        // ② 有卡片处于 Working（显示它）或 Intro（预卷，保证 Working 进入即开即用不闪黑）才播放，
        //    空闲期暂停省解码（WebGL 并发解码是卡顿源）。仅 working 是视频，只此一个共享播放器。
        foreach (var kv in s_sharedPlayers)
        {
            VideoPlayer vp = kv.Value;
            if (vp == null) continue;
            if (!vp.isPrepared) continue;
            string file = kv.Key;
            RenderTexture rt;
            if (!s_sharedRTs.TryGetValue(file, out rt) || rt == null)
            {
                rt = new RenderTexture(Mathf.Max(2, (int)vp.width), Mathf.Max(2, (int)vp.height), 0);
                s_sharedRTs[file] = rt;
                vp.targetTexture = rt;
                s_sharedLengths[file] = vp.length;
            }
            bool want = _player.playing && (workingCards > 0 || claimCards > 0);
            if (want) { if (!vp.isPlaying) vp.Play(); }
            else if (vp.isPlaying) vp.Pause();
        }

        // Seek 检测：暂停状态下 cur 发生变化（拖动进度条 / 跳回合）→ 所有结果卡片（Success/Fail）
        // 已过时，先全部清空，再由下方 team loop 按目标回合数据重建。
        // 不能只看 |cur-lastCur|>1：正常播放走 ReplayPlayer 的 while 循环，快进时 cur 也能一帧
        // 连跳多回合（此时 playing=true，不能清）。而拖动/跳转都是先 SetPlaying(false) 再 JumpTo，
        // 所以"暂停 && cur 变了"就是 seek 的唯一可靠信号。
        // 必须清空的根因：结果卡片在暂停时 Update 提前 return，1.5s 结束计时被冻结、永不淡出销毁；
        // 若不清空，开拓者已回到任务官前站着（无任务）时，残留的失败框会一直挂着。
        if (_lastCur >= 0 && !_player.playing && _player.cur != _lastCur)
            ClearAllBadges();
        _lastCur = _player.cur;

        if (_player.data == null) return;
        if (_player.cur < 1 || _player.cur > _player.data.rounds.Count) return;
        var round = _player.data.rounds[_player.cur - 1];
        if (round == null || round.teams == null) return;

        var activeTaskerIds = new HashSet<long>();  // 本帧仍是开拓者的 roleId（清理已死亡/换人卡片用）

        // 数据里的"上一回合"（JumpTo/拖动进度条时 _player.cur 可能跳变，须读数据而非存上一帧）
        ReplayRound prevRound = null;
        if (_player.cur >= 2)
            prevRound = _player.data.rounds[_player.cur - 2];

        for (int i = 0; i < round.teams.Count; i++)
        {
            var team = round.teams[i];

            var curSnap = MakeSnapshot(team.task);
            // 上一回合该队快照（队伍数可能不同：越界/无上一回合 → 视为无任务）
            var prevSnap = default(TaskSnapshot);
            if (prevRound != null && prevRound.teams != null && i < prevRound.teams.Count)
                prevSnap = MakeSnapshot(prevRound.teams[i].task);

            var target = DecideState(curSnap, prevSnap);

            var tasker = FindOpenTasker(team);
            if (tasker == null) continue;
            long roleId = tasker.id;
            activeTaskerIds.Add(roleId);

            // 开拓者视图不存在（死亡/未创建）→ 立即销毁卡片
            UnitState us;
            if (!_player.engine.units.TryGetValue(roleId, out us) || us.view == null)
            {
                DestroyBadge(roleId);
                continue;
            }

            string taskType = team.task != null ? team.task.taskType : "";

            TaskCardBadge badge;
            if (_activeBadges.TryGetValue(roleId, out badge) && badge != null)
            {
                // 目标 Hidden 且卡片当前未在播放 Success/Fail 结果 → 直接销毁。
                // （直接 SetState(Hidden) 会留下 IsFinished=false 的 Hidden 残体，
                //   不被"播完销毁"清理收集，Seek 反复进出任务段会累积。）
                if (target == TaskCardState.Hidden
                    && badge.CurrentState != TaskCardState.Success
                    && badge.CurrentState != TaskCardState.Fail)
                {
                    DestroyBadge(roleId);
                    continue;
                }
                badge.SetState(target, taskType);
            }
            else
            {
                if (target == TaskCardState.Hidden) continue;   // 隐藏态不创建卡片
                // 硬保险：父节点下已有卡片（字典丢失/多 manager 偶发叠加）则复用而非新建，杜绝叠卡
                var existing = us.view.transform.GetComponentInChildren<TaskCardBadge>(true);
                if (existing != null)
                {
                    _activeBadges[roleId] = existing;
                    existing.SetState(target, taskType);
                    continue;
                }
                _activeBadges[roleId] = TaskCardBadge.Create(us.view.transform, target, taskType, _player);
            }
        }

        // 清理：开拓者已死亡 / 角色被移除 → 销毁其卡片
        var orphans = new List<long>();
        foreach (var kv in _activeBadges)
            if (!activeTaskerIds.Contains(kv.Key)) orphans.Add(kv.Key);
        foreach (var id in orphans) DestroyBadge(id);

        // 清理播完的卡片（内部 IsFinished 且已回 Hidden）
        var done = new List<long>();
        foreach (var kv in _activeBadges)
            if (kv.Value == null || (kv.Value.IsFinished && kv.Value.CurrentState == TaskCardState.Hidden))
                done.Add(kv.Key);
        foreach (var id in done) DestroyBadge(id);
    }

    /// <summary>队伍里第一个存活的开拓者（roleType==7）。没有则返回 null。</summary>
    static ReplayRole FindOpenTasker(ReplayTeam team)
    {
        if (team == null || team.roles == null) return null;
        foreach (var r in team.roles)
            if (r != null && r.roleType == 7 && r.health > 0) return r;
        return null;
    }

    static TaskSnapshot MakeSnapshot(ReplayTask task)
    {
        bool hasPos = task != null && !string.IsNullOrEmpty(task.taskType);
        return new TaskSnapshot
        {
            hasPos = hasPos,
            isComplete = task != null && task.isTaskComplete,
            roundCost = task != null ? task.roundCost : 0
        };
    }

    /// <summary>依据"数据上一回合 → 当前回合"跳变 + 当前稳态，判定目标卡片状态。
    /// prev 是 rounds[cur-2] 里同一队伍的快照（Seek 后按目标回合数据重建，不依赖帧间连续）。</summary>
    TaskCardState DecideState(TaskSnapshot cur, TaskSnapshot prev)
    {
        // 成功：上一回合有任务未完成，本回合完成
        if (prev.hasPos && !prev.isComplete && cur.isComplete)
            return TaskCardState.Success;

        // 失败：上一回合有任务，本回合任务消失且未完成
        //（加 !prev.isComplete 防止"成功后任务立即被清掉"把成功覆盖成失败）
        if (prev.hasPos && !prev.isComplete && !cur.hasPos && !cur.isComplete)
            return TaskCardState.Fail;

        // 稳态：无任务 → 隐藏
        if (!cur.hasPos) return TaskCardState.Hidden;

        // 稳态：有任务
        if (cur.roundCost == 0) return TaskCardState.Intro;
        if (!cur.isComplete) return TaskCardState.Working;

        // 已完成任务的稳态（Success 已触发并在播放）→ 隐藏（不显示常驻卡）
        return TaskCardState.Hidden;
    }

    void DestroyBadge(long roleId)
    {
        TaskCardBadge b;
        if (_activeBadges.TryGetValue(roleId, out b))
        {
            if (b != null) Destroy(b.gameObject);
            _activeBadges.Remove(roleId);
        }
    }

    /// <summary>销毁全部卡片并清空字典（Seek 跳回合时结果卡片已过时，一次性清空，再按新回合数据重建）。</summary>
    void ClearAllBadges()
    {
        var keys = new List<long>(_activeBadges.Keys);
        foreach (var id in keys) DestroyBadge(id);
    }
}
