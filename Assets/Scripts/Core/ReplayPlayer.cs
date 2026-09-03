using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 回放播放器（MonoBehaviour）：回合推进 / 变速 / 跳转 / 单位插值 / 特效调度 / 拾取。
/// </summary>
public class ReplayPlayer : MonoBehaviour, IReplayHost
{
    public static readonly float[] SPEEDS = { 0.5f, 1f, 2f, 5f };   // 0→0.5x 1→1x 2→2x 3→5x（面板 Sp1 在 1x↔0.5x、Sp2 在 2x↔5x 间循环）

    [Header("播放参数")]
    public float baseRoundDuration = 0.5f;   // 1x 速度下每回合秒数

    const int DIZZY_FREEZE_ROUNDS = 5;       // 眩晕法宝冻结回合数（特效持续时长）

    public ReplayData data;
    public StateEngine engine = new StateEngine();
    public ReplayCameraRig camRig;
    public Transform unitsRoot;
    ResourceViewManager _resourceView;
    EventLogPanelController _eventLog;

    public int cur { get; private set; }
    public bool playing { get; private set; }
    public int speedIndex { get; private set; } = 1;   // 默认 1x

    float _acc;
    float RoundDur { get { return baseRoundDuration / SPEEDS[speedIndex]; } }

    /// <summary>当前回合各单位执行的动作（unitId → action），供 NpcFacingController 查询。</summary>
    public Dictionary<long, string> roundActions = new Dictionary<long, string>();

    /// <summary>当前回合内的插值进度（0~1），供 DayNightController 等系统使用。</summary>
    public float RoundProgress { get { return Mathf.Clamp01(_acc / RoundDur); } }
    /// <summary>连续回合浮点值（0-indexed）：cur - 1 + RoundProgress，消除回合边界跳变。</summary>
    public float RoundFloat { get { return (cur - 1) + RoundProgress; } }

    public int TotalRounds { get { return data != null ? data.rounds.Count : 0; } }

    // ---------- 初始化 ----------
    public void SetEventLog(EventLogPanelController log) { _eventLog = log; }

    public void Setup(ReplayData d, ReplayCameraRig cam)
    {
        data = d;
        camRig = cam;
        engine.host = this;

        unitsRoot = new GameObject("Units").transform;

        // 资源矿点视图（放到 Map 根节点下，与 Units 同级）
        var mapRoot = GameObject.Find("Map");
        _resourceView = new ResourceViewManager(mapRoot != null ? mapRoot.transform : unitsRoot, engine);

        // 相机聚焦地图中心 + 自动取景填满屏幕
        var m = data.start.map;
        camRig.Focus(new Vector3(0, 0, 0), 60f);
        camRig.FitMap(m.width, m.height, 0.92f);

        // 白天光照
        RenderSettings.ambientLight = new Color(0.55f, 0.62f, 0.7f);
        RenderSettings.fog = false;

        engine.Init(data.start);
        // 预载第 1 回合（静默，无特效）
        if (data.rounds.Count > 0)
        {
            cur = 1;
            roundActions.Clear();
            engine.Diff(null, data.rounds[0], false);
        }
        RefreshResources();
    }

    public void SetPlaying(bool p)
    {
        playing = p;
        FxFactory.SetGlobalPause(!p);
    }
    public void TogglePlay() { SetPlaying(!playing); }

    public void SetSpeed(int idx)
    {
        speedIndex = Mathf.Clamp(idx, 0, SPEEDS.Length - 1);
    }

    public void Step(int delta, bool withFx)
    {
        if (data == null) return;
        int target = Mathf.Clamp(cur + delta, 1, TotalRounds);
        if (target == cur) return;
        TradeBadge.Cleanup();
        roundActions.Clear();
        int step = target > cur ? 1 : -1;
        ReplayRound prev = cur >= 1 && cur <= TotalRounds ? data.rounds[cur - 1] : null;
        while (cur != target)
        {
            int nn = cur + step;
            var nrec = data.rounds[nn - 1];
            if (nrec == null) { cur = nn; continue; }
            bool fx = withFx && nn == target;
            engine.Diff(prev, nrec, fx);
            prev = nrec;
            cur = nn;
            _acc = 0;
            if (fx) OnRoundEntered(nn);
        }
        // 跳转时瞬间到位 + 清除死/濒死单位（避免幽灵贴图）
        if (!withFx)
        {
            // Seek 跳转：清理残留的瞬态 CFXR 特效（电球/落点电击/爆炸等，均为根级对象），避免白天还看到夜晚的特效
            foreach (var cfg in UnityEngine.Object.FindObjectsOfType<CartoonFX.CFXR_Effect>(true))
                if (cfg.transform.parent == null)
                    Destroy(cfg.gameObject);
            var deadIds = new List<long>();
            foreach (var u in engine.units.Values)
            {
                if (u.dying || u.dead) { deadIds.Add(u.id); continue; }
                u.pos = u.targetPos;
                u.moving = false;
                if (u.view != null) u.view.transform.position = u.pos;
            }
            foreach (var id in deadIds)
            {
                var u = engine.units[id];
                if (u.view != null) Destroy(u.view.gameObject);
                u.view = null;
                u.dead = true;
                engine.units.Remove(id);
            }

            // Seek 跳转：清除武器工事残留的攻击表现（炮塔转向/后坐力/粒子）
            foreach (var u in engine.units.Values)
                if (u.view != null && u.IsTower)
                    u.view.ResetTowerAttack();
        }

        // 为新建/复活单位补建视图
        foreach (var u in engine.units.Values)
            if (u.view == null && !u.dead && !u.dying)
                u.view = UnitView.Create(u, unitsRoot);

        // 结算以 replay 最后一轮为准：不因中途基地被毁提前弹出（判题器 finish 决定胜负）
        if (cur >= TotalRounds)
            ShowSettlement();

        RefreshResources();
    }

    public void JumpTo(int round, bool withFx) { Step(round - cur, withFx); }
    public void NextRound() { Step(1, true); }
    public void PrevRound() { Step(-1, true); }

    /// <summary>彻底重新播放</summary>
    public void Restart()
    {
        if (data == null) return;
        SetPlaying(false);
        TradeBadge.Cleanup();

        // 清空旧单位与视图 + 资源矿点
        foreach (var u in engine.units.Values)
            if (u.view != null) Destroy(u.view.gameObject);
        engine.units.Clear();
        _resourceView.Clear();

        // 重新初始化引擎 + 预载第 1 回合（静默）
        engine.Init(data.start);
        cur = 1;
        _acc = 0;
        roundActions.Clear();
        if (data.rounds.Count > 0) engine.Diff(null, data.rounds[0], false);

        // 为第 1 回合存活的单位补建视图（无声效）
        foreach (var u in engine.units.Values)
            if (u.view == null)
                u.view = UnitView.Create(u, unitsRoot);

        RefreshResources();
        SetPlaying(true);
    }

void OnRoundEntered(int n)
    {
        // 强行同步全局背景和场景光照，确保在重播或拖动进度条时背景颜色瞬间自愈刷新
        // 相位变化检测（仅用于日志；光照由 DayNightController 统一管理）
        int day = StateEngine.DayOf(n);
        bool night = StateEngine.IsNight(n);
        bool changed = n == 1
            || StateEngine.DayOf(n - 1) != day
            || StateEngine.IsNight(n - 1) != night;
        if (changed)
        {
            OnPhaseChange(day, night);
        }

        // 通知 CameraManager 当前回合的 news 事件（智能导演模式）
        if (CameraManager.Instance != null && data != null && n >= 1 && n <= data.rounds.Count)
        {
            var round = data.rounds[n - 1];
            CameraManager.Instance.OnNewRoundTick(round?.news, engine?.units);
        }
    }

    // ---------- IReplayHost ----------
    // defender=红方 / challenger=蓝方（与 TeamColorApplicator、基地 Model_Red/Blue、底部面板一致）
    static string TeamTag(string teamType) => teamType == "defender" ? "<color=#F05638>红方</color>" : "<color=#479EF0>蓝方</color>";

    public void Log(string type, string text) { Log(type, text, ""); }
    public void Log(string type, string text, string teamType)
    {
        // 过滤单位"移动"日志：每回合上百条野兽/工人移动刷屏，淹没建造/击杀/任务等事件。
        // 仅显示层过滤，StateEngine 判定与胜负逻辑不受影响。
        if (type == "cmd" && text.IndexOf(" 移动 ", System.StringComparison.Ordinal) >= 0)
            return;
        string prefix = string.IsNullOrEmpty(teamType) ? "" : TeamTag(teamType) + "：";
        string msg = "<b>[回合" + cur + "]</b> " + prefix + text;
        if (_eventLog != null) _eventLog.AddEventLog(msg, type);
    }
    public void Toast(string text) { }

    public void OnDamage(UnitState from, UnitState to, int dmg)
    {
        string msg = from != null
            ? from.DisplayName + " → " + to.DisplayName + "  -" + dmg
            : to.DisplayName + " 受击 -" + dmg;
        Log("damage", msg, to.teamType);
        if (from != null)
        {
            // 通用射线：武器工事(30/31/32，兼容旧3)用 Tracer、野兽(11~14)只保留攻击动画，均不生成通用 Beam
            if (!from.IsTower && !from.IsBeast)
                FxFactory.Beam(from.pos, to.pos, new Color(1f, 0.62f, 0.36f));
            if (from.view != null && from.IsBeast) from.view.TriggerAttack();
        }
    }

    public void OnSpawn(UnitState u)
    {
        Log("beast", u.DisplayName + " 出现", u.teamType);
        if (u.view == null)
        {
            u.view = UnitView.Create(u, unitsRoot);
            u.view.SetAnimScale(0f);
        }
        // 幽灵白圈排查：野兽(11~14)登场不再播放出生光环（白色扩散圆环），
        // 避免多回合机器人陆续登场时周期性闪现白圈；工人/开拓者等其余单位仍保留出生光环。
        // 防御塔命中环(Tracer)走 TowerVisualController，不受影响。
        if (!u.IsBeast)
            FxFactory.Ring(u.pos, new Color(1f, 0.79f, 0.3f, 0.9f));
    }

    public void OnDie(UnitState u)
    {
        Log("kill", u.DisplayName + " 阵亡", u.teamType);
        if (u.view != null && u.IsBeast) u.view.TriggerDeath();
        // 被摧毁特效：仅围墙 → 瓦砾炸开（其余单位不再播放爆炸）
        if (u.type == 5)
            FxFactory.PlayRubbleEffect(u.pos);
    }

    public void OnCommand(UnitState u, ReplayCommand c)
    {
        roundActions[u.id] = c.action;
        if (!c.valid) return;
        var wp = engine.CellToWorld(c.x, c.y);
        string pos = "(" + c.x + "," + c.y + ")";
        string tt = u.teamType;
        switch (c.action)
        {
            case "attack":
                Log("damage", u.DisplayName + " 攻击 " + pos, tt);
                // 攻击特效：武器工事(30/31/32，兼容旧3)用 Tracer；野兽=枪口开火特效（角色朝向前方枪口处）；其余单位通用 Beam 弹道
                if (!u.IsTower)
                {
                    if (u.IsBeast)
                    {
                        // 射击方向 = 角色朝向（武器随角色转身，枪口随方向变化）
                        Vector3 fwd = u.view != null ? u.view.transform.forward : Vector3.forward;
                        fwd.y = 0f;
                        if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
                        fwd.Normalize();
                        // 枪口 = 沿射击方向在当前武器网格上实测的枪管前端；找不到再回退固定偏移
                        Vector3 muzzle;
                        if (u.view == null || !u.view.TryGetMuzzle(fwd, out muzzle))
                            muzzle = u.pos + fwd * 0.55f + Vector3.up * 1.15f;
                        FxFactory.PlayMuzzleFlash(muzzle, fwd);
                    }
                    else
                    {
                        FxFactory.Beam(u.pos, wp, new Color(1f, 0.62f, 0.36f));
                    }
                }
                // 攻击表现：武器工事按类型分派特效；野兽播放射击动画
                if (u.view != null)
                {
                    if (u.IsTower)
                    {
                        if (u.type == 30)
                        {
                            // 加特林：N 条弹道（落点数 = 等级，1~5）
                            u.view.TriggerTowerAttackMulti(AttackWorldTargets(c));
                        }
                        else if (u.type == 32)
                        {
                            // 火箭发射台：按等级/落点数齐射——每个被攻击目标一枚火箭飞向该落点，到达后各自爆炸（3×3 AoE）+ 震屏由 TowerVisualController 在火箭到达时触发
                            var rocketTargets = AttackWorldTargets(c);
                            u.view.TriggerTowerAttackMulti(rocketTargets);
                            // 塔视觉未就绪（火箭无法飞行）时兜底直接在各落点爆炸
                            if (!u.view.IsTowerVisualReady)
                            {
                                foreach (var p in rocketTargets)
                                    FxFactory.PlayBombEffect(p);
                                if (CameraManager.Instance != null)
                                    CameraManager.Instance.CameraShake(0.4f, 0.25f);
                            }
                        }
                        else
                        {
                            // 电磁狙击炮（31）等：单发穿透激光
                            u.view.TriggerTowerAttack(wp);
                        }
                    }
                    else if (u.IsBeast) u.view.TriggerAttack();
                }
                break;
            case "build":
                Log("build", u.DisplayName + " 建造 " + pos, tt);
                FxFactory.Ring(wp, new Color(0.44f, 0.88f, 0.54f, 0.9f));
                FxFactory.PlayBuildEffect(wp);
                // 建造（围墙/防御塔）成功 → 头顶"建造围墙*1"徽标 + 挥臂砍劈动画
                if (u.view != null && !string.IsNullOrEmpty(c.targetName))
                {
                    TradeBadge.ShowBuild(u.view.transform, c.targetName, 1);
                    u.view.TriggerCollect();
                }
                break;
            case "remove":
                Log("build", u.DisplayName + " 拆除 " + pos, tt);
                FxFactory.PlayBuildEffect(wp);
                break;
            case "collect":
                Log("cmd", u.DisplayName + " 采集 " + pos, tt);
                // 采集成功 → 工人头顶"采集 铁*1"徽标（仿 sell 交易徽标）+ 挥臂砍劈动画
                if (u.view != null)
                {
                    string res = engine.ResNameAt(c.x, c.y);
                    if (!string.IsNullOrEmpty(res))
                        TradeBadge.ShowCollect(u.view.transform, res, 1);
                    u.view.TriggerCollect();
                }
                break;
            case "sell":
                Log("trade", u.DisplayName + " 贩卖 " + pos, tt);
                FxFactory.Ring(wp, new Color(1f, 0.7f, 0.36f, 0.9f));
                if (c.valid) TryShowTradeBadge(u, c.targetName);
                break;
            case "buy":
                Log("trade", u.DisplayName + " 购买 " + pos, tt);
                FxFactory.Ring(wp, new Color(0.6f, 0.75f, 1f, 0.9f));
                if (c.valid) TryShowShopBadge(u, c.targetName);
                break;
            case "executeTask":
                Log("task", u.DisplayName + " 接取任务", tt);
                break;
            case "submitAnswer":
                Log("task", u.DisplayName + " 提交答案", tt);
                break;
            case "use":
                {
                    string item = string.IsNullOrEmpty(c.targetName) ? "物品" : ItemNameCn.Cn(c.targetName);
                    string msg = u.DisplayName + " 使用 " + item;
                    if (c.skillTargetPos != null && c.skillTargetPos.Count > 0)
                    {
                        msg += "（范围 " + c.skillTargetPos.Count + " 格）";
                        OnSkillAreaEffect(u, c);
                    }
                    else if (c.hasTarget)
                        msg += " → " + pos;
                    Log("cmd", msg, tt);
                    // 工人/开拓者使用道具 → 头顶"使用 xx"徽标（与小贩/商店同款）
                    if (u.type == 6 || u.type == 7)
                        TryShowUseBadge(u, c.targetName);
                    // 恢复类道具：生命药剂跟随角色移动；围墙修复包在围墙位置（不跟随）
                    string useItem = (c.targetName ?? "").ToLowerInvariant();
                    if (useItem == "medicine")
                    {
                        FxFactory.PlayHealEffect(u.pos, u.view != null ? u.view.transform : null);
                    }
                    else if (useItem == "wallfixer")
                    {
                        FxFactory.PlayHealEffect(wp, null);
                    }
                    break;
                }
            case "detect":
                Log("cmd", u.DisplayName + " 探测 " + pos, tt);
                break;
            default:
                // 移动指令不刷日志（每回合上百条野兽/工人移动，淹没重要事件）；attack 等其余指令保留
                if (c.action != "move")
                    Log("cmd", u.DisplayName + " " + c.action + " " + pos, tt);
                break;
        }
    }

    /// <summary>
    /// AoE 道具范围视觉：Bomb（震屏 + 爆炸）/ DizzyWeapon（魔法阵），均走 Cartoon FX Remaster 特效。
    /// 纯表现层，不改变任何判定与底层数据（实际扣血/眩晕已由 replay 决定）。
    /// </summary>
    void OnSkillAreaEffect(UnitState u, ReplayCommand c)
    {
        var center = engine.CellToWorld(c.x, c.y);
        string item = (c.targetName ?? "").ToLowerInvariant();

        if (item == "bomb")
        {
            // 震屏（Auto 导演模式下由 CameraManager 应用）
            if (CameraManager.Instance != null)
                CameraManager.Instance.CameraShake(0.4f, 0.25f);
            FxFactory.PlayBombEffect(center);
        }
        else if (item == "dizzyweapon")
        {
            // 对范围内每个机器人单独播放冰冻特效（特效落在机器人的单位坐标上）
            var targets = new HashSet<string>();
            if (c.skillTargetPos != null)
                foreach (var sp in c.skillTargetPos)
                    targets.Add(sp.x + "," + sp.y);
            foreach (var bu in engine.units.Values)
            {
                if (!bu.IsBeast || bu.dead || bu.dying) continue;
                int gx = Mathf.RoundToInt(bu.pos.x + 20f);
                int gy = Mathf.RoundToInt(bu.pos.z + 15.5f);
                if (targets.Contains(gx + "," + gy))
                    FxFactory.PlayDizzyEffect(bu.pos, DIZZY_FREEZE_ROUNDS * RoundDur);
            }
        }
    }

    /// <summary>攻击落点（格子坐标，可能是数组）→ 世界坐标数组（加特林多弹道用）。</summary>
    Vector3[] AttackWorldTargets(ReplayCommand c)
    {
        if (c.targets != null && c.targets.Count > 0)
        {
            var wps = new Vector3[c.targets.Count];
            for (int i = 0; i < c.targets.Count; i++)
                wps[i] = engine.CellToWorld(c.targets[i].x, c.targets[i].y);
            return wps;
        }
        return new Vector3[] { engine.CellToWorld(c.x, c.y) };
    }

    public void OnTalk(UnitState u, string text)
    {
        Log("info", u.DisplayName + "：" + text);
        FxFactory.Bubble(u.pos, text);
    }

    public void OnNews(string text)
    {
        Log("info", "📢 " + text);
    }

    public void OnPhaseChange(int day, bool isNight)
    {
        Log("info", (isNight ? "🌙 第" : "☀ 第") + day + "天 " + (isNight ? "黑夜降临" : "天亮"));
        // 光照统一由 DayNightController 管理，此处仅记录日志
    }

    /// <summary>矿石（石头/铁/铜）消失 → 在原坐标播放瓦砾破碎特效（短时长，只爆一下）。</summary>
    public void OnResourceDepleted(int x, int y, string resName)
    {
        FxFactory.PlayRubbleEffect(engine.CellToWorld(x, y), 0.6f, 0.7f);
    }

    /// <summary>sell 有效且执行者在小贩周围一格内 → 徽标显示在执行者（worker/pioneer）头上。</summary>
    void TryShowTradeBadge(UnitState u, string targetName)
    {
        if (u.view == null) return;
        var vendorGo = GameObject.Find("NPC_9_20_15");
        if (vendorGo == null) return;

        var vp = vendorGo.transform.position;
        int vgx = Mathf.RoundToInt(vp.x + 20f);
        int vgy = Mathf.RoundToInt(15.5f - vp.z);
        int ugx = Mathf.RoundToInt(u.pos.x + 20f);
        int ugy = Mathf.RoundToInt(15.5f - u.pos.z);

        if (Mathf.Max(Mathf.Abs(ugx - vgx), Mathf.Abs(ugy - vgy)) <= 1)
            TradeBadge.Show(u.view.transform, targetName ?? "copper", 1, 1.8f);
    }

    /// <summary>buy 有效且执行者在武器商店周围一格内 → 徽标显示在执行者（worker/pioneer）头上。</summary>
    void TryShowShopBadge(UnitState u, string targetName)
    {
        if (u.view == null) return;
        var shopGo = GameObject.Find("NPC_10_25_11");
        if (shopGo == null) return;

        var sp = shopGo.transform.position;
        int sgx = Mathf.RoundToInt(sp.x + 20f);
        int sgy = Mathf.RoundToInt(15.5f - sp.z);
        int ugx = Mathf.RoundToInt(u.pos.x + 20f);
        int ugy = Mathf.RoundToInt(15.5f - u.pos.z);

        if (Mathf.Max(Mathf.Abs(ugx - sgx), Mathf.Abs(ugy - sgy)) <= 1)
            TradeBadge.Show(u.view.transform, targetName ?? "购买", 1, 1.8f);
    }

    /// <summary>工人/开拓者使用道具 → 角色头顶显示"使用 xx"徽标。</summary>
    void TryShowUseBadge(UnitState u, string targetName)
    {
        if (u.view == null) return;
        TradeBadge.ShowUse(u.view.transform, targetName ?? "物品");
    }

    // ---------- 每帧 ----------
    void Update()
    {
        if (data == null) return;

        // 同步动画速度到所有 UnitView（匹配播放倍速）
        UnitView.AnimatorSpeed = SPEEDS[speedIndex];

        // 播放推进
        if (playing)
        {
            _acc += Time.deltaTime;
            while (_acc >= RoundDur && playing)
            {
                _acc -= RoundDur;
                int nn = cur + 1;
                if (nn > TotalRounds)
                {
                    SetPlaying(false);
                    ShowSettlement();
                    break;
                }
                var prev = data.rounds[cur - 1];
                var nrec = data.rounds[nn - 1];
                roundActions.Clear();
                engine.Diff(prev, nrec, true);
                cur = nn;
                OnRoundEntered(nn);
            }
            RefreshResources();
        }

        // 单位插值 + 动画 + 血条
        float now = Time.time;
        foreach (var u in engine.units.Values)
        {
            if (u.dead) continue;
            if (u.moving)
            {
                float t = Mathf.Clamp01((now - u.moveStart) / RoundDur);
                // 线性匀速插值：回合边界速度连续（持续移动时速度不归零），配合 Animator.speed 匹配，
                // 避免每回合 smoothstep 缓入缓出导致的"走路一快一慢/滑步/腿不动"。
                u.pos = Vector3.Lerp(u.moveFrom, u.moveTo, t);
                if (t >= 1f)
                {
                    u.pos = u.moveTo;
                    u.moving = false;
                }
            }
            if (u.animScale < 1f && !u.dying)
                u.animScale = Mathf.Clamp01(u.animScale + Time.deltaTime * 3.2f);
            if (u.dying)
            {
                // 野兽播放死亡动画，不随 animScale 缩小（避免"死亡时素材变小"）；
                // 其余单位保持原有的缩小消失效果
                if (!u.IsBeast)
                    u.animScale = Mathf.Clamp01(1f - (u.dieAt - now) / 0.45f);
                if (now >= u.dieAt)
                {
                    u.dead = true;
                    if (u.view != null) Destroy(u.view.gameObject);
                    u.view = null;
                }
            }
            if (u.view != null)
            {
                if (u.dying)
                {
                    // 死亡时血条随死亡进度清零（前 0.4s 从当前血量减到 0），颜色随之变红
                    float dpct = Mathf.Clamp01((u.dieAt - now) / 0.4f);
                    u.view.SetHp(Mathf.RoundToInt(u.hp * dpct), u.maxHp);
                }
                else
                {
                    u.view.SetHp(u.hp, u.maxHp);
                }
                u.view.SetStun(u.stun);
                // 3D 野兽动画
                if (u.IsBeast)
                    u.view.UpdateAnimation(u.moving, u.dying);
            }
        }
        // 清理死亡
        var deadIds = new List<long>();
        foreach (var kv in engine.units) if (kv.Value.dead) deadIds.Add(kv.Key);
        foreach (var id in deadIds) engine.units.Remove(id);

    }

    /// <summary>刷新当前回合的资源矿点显示</summary>
    void RefreshResources()
    {
        if (_resourceView == null || data == null) return;
        if (cur >= 1 && cur <= data.rounds.Count)
            _resourceView.ApplyFrame(data.rounds[cur - 1].resources);
    }

    // ---------- 结算画面 ----------
    GameObject _settlementOverlay;
    void ShowSettlement()
    {
        if (_settlementOverlay != null) return;

        // 红方 = defender，蓝方 = challenger（与 TeamColorApplicator / 底部面板一致）
        TeamStat red = null, blue = null;
        foreach (var kv in engine.teams)
        {
            if (kv.Value.type == "defender") red = kv.Value;
            else if (kv.Value.type == "challenger") blue = kv.Value;
        }
        if (red == null || blue == null) return;

        string p0Name = "红方", p1Name = "蓝方";
        string p0Result, p1Result;
        int p0Score, p1Score;

        // 结果 + 最终积分取判题器 finish（finish.totalScore = 最后一轮累计 + 生存天数加分等，按 teamId 对齐）；
        // 无 finish 时按基地血量推断结果、用引擎最后一轮积分兜底
        var f = data.finish;
        var resultById = new Dictionary<string, string>();
        var scoreById = new Dictionary<string, int>();
        if (f != null && f.players != null)
            foreach (var pr in f.players)
            {
                resultById[pr.teamId] = pr.result;
                scoreById[pr.teamId] = pr.totalScore;
            }

        if (resultById.ContainsKey(red.teamId) && resultById.ContainsKey(blue.teamId))
        {
            p0Result = resultById[red.teamId];
            p1Result = resultById[blue.teamId];
            p0Score  = scoreById[red.teamId];
            p1Score  = scoreById[blue.teamId];
        }
        else
        {
            int redHp = -1, blueHp = -1;
            foreach (var u in engine.units.Values)
            {
                if (u.type != 4) continue;
                if (u.teamId == red.teamId) redHp = u.hp;
                else if (u.teamId == blue.teamId) blueHp = u.hp;
            }
            p0Result = redHp <= 0 ? "defeat" : (blueHp <= 0 ? "victory" : "draw");
            p1Result = blueHp <= 0 ? "defeat" : (redHp <= 0 ? "victory" : "draw");
            p0Score = red.score;
            p1Score = blue.score;
        }

        // 存活天数：基地被毁回合所在天 - 1（基地始终存活则按最后一轮天 - 1）
        int p0Days = SurvivalDays(red.teamId);
        int p1Days = SurvivalDays(blue.teamId);

        var ctrl = SettlementPanelController.Create(
            p0Name, p0Result, p0Score, p0Days,
            p1Name, p1Result, p1Score, p1Days,
            () => { Destroy(_settlementOverlay); _settlementOverlay = null; Restart(); });
        if (ctrl != null) _settlementOverlay = ctrl.gameObject;
    }

    /// <summary>队伍存活天数：基地首次 hp≤0 的回合所在天 - 1；基地始终存活则按最后一轮天 - 1。</summary>
    int SurvivalDays(string teamId)
    {
        if (data == null || data.rounds == null || data.rounds.Count == 0) return 0;
        int lastDay = StateEngine.DayOf(data.rounds[data.rounds.Count - 1].round);
        int destroyRound = -1;
        foreach (var rd in data.rounds)
        {
            bool found = false;
            foreach (var t in rd.teams)
            {
                if (t.teamId != teamId) continue;
                foreach (var r in t.roles)
                    if (r.roleType == 4 && r.health <= 0) { destroyRound = rd.round; found = true; break; }
                if (found) break;
            }
            if (found) break;
        }
        if (destroyRound >= 0) return StateEngine.DayOf(destroyRound) - 1;
        return lastDay - 1;
    }
}
