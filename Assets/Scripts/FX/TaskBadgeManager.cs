using System.Collections.Generic;
using System.IO;
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

    /// <summary>防多实例：同一帧若场景存在其它 TaskBadgeManager（编译/域重载后偶发叠加），
    /// 多余实例自我销毁，避免各自维护字典、往同一开拓者 transform 下重复创建卡片导致叠卡。
    /// 每帧扫一次：Destroy 延迟生效前可能短暂重复，因此每帧都让后出现的自毁。</summary>
    // 共享任务视频：working 由全局共享播放器持续循环渲染进共享 RT——卡片 Working 态直接显示该 RT，
    // 立即可用，不再等各自 Prepare（首次视频解码初始化 + 播放期渲染负载下 Prepare 需数秒，Intro/Working
    // 阶段太短根本等不起）。success/fail 只全局预热就绪（结果态出现晚，届时各自开播，不抢 working 解码）。
    static VideoPlayer s_sharedWorkingPlayer;
    static RenderTexture s_sharedWorkingRT;

    void Awake()
    {
        var all = FindObjectsOfType<TaskBadgeManager>();
        for (int i = 0; i < all.Length; i++)
            if (all[i] != this) { Destroy(this); return; }
        EnsureSharedWorkingVideo();   // 共享 working 视频：游戏开始即准备 + 循环播放，吸收首次解码初始化
        WarmUpResultVideos();         // success/fail 只后台 Prepare（就绪备用），不播放不抢解码
    }

    /// <summary>创建全局共享 working 视频（隐藏对象）：Prepare 完成即循环播放进共享 RT，
    /// 随播放/暂停冻结（见 Update）。卡片 Working 态显示 GetSharedWorkingRT()，即开即用。</summary>
    static void EnsureSharedWorkingVideo()
    {
        if (s_sharedWorkingPlayer != null) return;
        var go = new GameObject("TaskWorkingSharedVideo");
        go.hideFlags = HideFlags.HideAndDontSave;
        var vp = go.AddComponent<VideoPlayer>();
        vp.source = VideoSource.Url;
        vp.url = Path.Combine(Application.streamingAssetsPath, "TaskVideos/working.mp4");
        vp.isLooping = true;
        vp.playOnAwake = false;
        vp.renderMode = VideoRenderMode.RenderTexture;
        vp.prepareCompleted += v =>
        {
            if (s_sharedWorkingRT == null)
                s_sharedWorkingRT = new RenderTexture(Mathf.Max(2, (int)v.width), Mathf.Max(2, (int)v.height), 0);
            v.targetTexture = s_sharedWorkingRT;
            v.Play();
        };
        s_sharedWorkingPlayer = vp;
        vp.Prepare();
    }

    /// <summary>success/fail 结果视频只后台 Prepare 就绪（隐藏对象），结果态出现时卡片各自开播/显示。
    /// 先于 working 完成时的解码会话已由共享 working 预热，此处再预热结果视频的文件缓存。</summary>
    static void WarmUpResultVideos()
    {
        var go = new GameObject("TaskResultVideoWarmUp");
        go.hideFlags = HideFlags.HideAndDontSave;
        WarmUpOne(go, "TaskVideos/success.mp4");
        WarmUpOne(go, "TaskVideos/fail.mp4");
    }

    static void WarmUpOne(GameObject go, string file)
    {
        var vp = go.AddComponent<VideoPlayer>();
        vp.source = VideoSource.Url;
        vp.url = Path.Combine(Application.streamingAssetsPath, file);
        vp.playOnAwake = false;
        vp.Prepare();
    }

    /// <summary>共享 working 视频 RT（未就绪返回 null；卡片 Working 态用它做即时显示底）。</summary>
    public static RenderTexture GetSharedWorkingRT() { return s_sharedWorkingRT; }

    void Update()
    {
        if (_player == null)
        {
            _player = FindObjectOfType<ReplayPlayer>();
            if (_player == null) return;
        }

        // 共享 working 视频随播放/暂停冻结（回放暂停时视频同步冻结，恢复续播）
        if (s_sharedWorkingPlayer != null)
        {
            if (_player.playing)
            {
                if (!s_sharedWorkingPlayer.isPlaying) s_sharedWorkingPlayer.Play();
            }
            else if (s_sharedWorkingPlayer.isPlaying) s_sharedWorkingPlayer.Pause();
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
