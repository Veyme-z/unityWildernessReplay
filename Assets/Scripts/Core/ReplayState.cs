using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>回放中一个单位的实时状态（数据层，视图由 UnitView 承载）</summary>
public class UnitState
{
    public long id;
    public string teamId = "";
    public string teamType = "";    // challenger / defender
    public int type;                // 4=基地 30=加特林/31=电磁狙击炮/32=火箭发射台（武器工事，兼容旧3=防御塔） 5=墙 6=工人 7=开拓者 11-14=野兽
    public int hp = 1;
    public int maxHp = 1;
    public int ap;
    public int level;               // 等级：武器工事=攻击等级(1~5)；英雄=经验等级(0~6)
    public bool stun;
    public string name = "";
    public List<ReplayCommand> commands = new List<ReplayCommand>();
    public List<ReplayItem> backpacks = new List<ReplayItem>();

    public Vector3 pos;             // 当前世界坐标（插值后）
    public Vector3 targetPos;       // 目标世界坐标（格子中心）
    public Vector3 moveFrom, moveTo;
    public float moveStart;
    public bool moving;

    public float animScale = 1f;    // 出生/死亡动画
    public bool dying;
    public bool dead;
    public float dieAt;

    public UnitView view;

    public bool IsBeast { get { return type >= 11 && type <= 14; } }   // 野兽 11~14；武器工事 30/31/32 不算野兽
    public bool IsTower { get { return type == 3 || type == 30 || type == 31 || type == 32; } }
    public bool IsBuilding { get { return IsTower || type == 4 || type == 5; } }
    public string DisplayName { get { return string.IsNullOrEmpty(name) ? "单位" + id : name; } }
}

/// <summary>队伍统计（UI 面板用）</summary>
public class TeamStat
{
    public string teamId = "";
    public string teamName = "";
    public string type = "";
    public int gold;
    public int score;
    public int tasks;
    public string taskText = "暂无任务";
    public int taskCorrect;     // 答对题数（对应 replay completeTaskCount）
    public int taskWrong;       // 答错题数（对应 replay invalidTaskCount）
    public bool hasActiveTask;  // 当前是否已接取任务（task.taskType 非空）
    public int baseHp = -1;
    public int task1Done, task1Failed, task1Total;   // 自进化类1 allTaskInfo [完成, 失败, 总数]
    public int task2Done, task2Failed, task2Total;   // 自进化类2
}

/// <summary>表现层宿主接口：状态引擎把"事件"汇报给播放器，由播放器做特效/UI。
/// Log 有两个重载：2 参数（无队伍信息）和 3 参数（带队伍类型）</summary>
public interface IReplayHost
{
    void Log(string type, string text);
    void Log(string type, string text, string teamType);
    void Toast(string text);
    void OnDamage(UnitState from, UnitState to, int dmg);
    void OnSpawn(UnitState u);
    void OnDie(UnitState u);
    void OnCommand(UnitState u, ReplayCommand c);
    void OnTalk(UnitState u, string text);
    void OnNews(string text);
    void OnPhaseChange(int day, bool isNight);
    void OnResourceDepleted(int x, int y, string resName);
}

/// <summary>
/// 状态引擎：每回合快照 diff → 推断出生/死亡/伤害/移动/指令/资源/新闻
/// 与 Web 原型 diffRound() 逻辑一一对应
/// </summary>
public class StateEngine
{
    public readonly Dictionary<long, UnitState> units = new Dictionary<long, UnitState>();
    public readonly Dictionary<string, TeamStat> teams = new Dictionary<string, TeamStat>();
    public IReplayHost host;
    public int currentRound;
    public int mapW = 41, mapH = 32;

    /// <summary>当前回合资源点类型（"x,y" → 石头/铁/铜），供 collect 徽标查询。</summary>
    public readonly Dictionary<string, string> resourceNames = new Dictionary<string, string>();

    public static readonly string[] TYPE_NAMES =
    { "空地","未知地形","水域","防御塔","基地","围墙","工人","开拓者",
      "任务官","小贩","武器商店","机器人","机器人","机器人","机器人" };

    /// <summary>
    /// 任务书 game 坐标 → Unity 世界坐标。
    /// 约定（与 replay 数据一致）：
    ///   (0,0) = 左下角（南），x 向右（东），y 向上（北）
    ///   map.data 存储顺序：row 0 = 北（game_y=mapH-1），row h-1 = 南（game_y=0）
    ///   SceneBuilder 遍历 data[y][x] 时 y 从 0→h-1 对应北→南（Z 从 +oz → -oz）
    /// 转换公式：z = gameY - (mapH-1)/2，gameY 越大（越北）→ z 越大（屏幕上方）
    /// </summary>
    public Vector3 CellToWorld(int gameX, int gameY)
    {
        float ox = (mapW - 1) * 0.5f;
        float oz = (mapH - 1) * 0.5f;
        return new Vector3(gameX - ox, 0f, gameY - oz);
    }

    /// <summary>单位世界坐标。基地(2×2)居中：锚点 (x,y) 是其左上角格（占地 x..x+1, y-1..y），
    /// 中心 = 左下角格 + (0.5, 0, -0.5) 即 +x/2、-y/2。</summary>
    public Vector3 UnitWorldPos(int x, int y, int roleType)
    {
        var wp = CellToWorld(x, y);
        if (roleType == 4) wp += new Vector3(0.5f, 0f, -0.5f);  // 基地 2×2 视觉居中
        return wp;
    }

    /// <summary>查询某格子的资源名（石头/铁/铜），非矿点返回空串。</summary>
    public string ResNameAt(int x, int y)
    {
        string n;
        return resourceNames.TryGetValue(x + "," + y, out n) ? n : "";
    }

    public void Init(ReplayStart start)
    {
        units.Clear();
        teams.Clear();
        if (start.map != null) { mapW = start.map.width; mapH = start.map.height; }
        foreach (var t in start.teams)
        {
            teams[t.teamId] = new TeamStat
            {
                teamId = t.teamId,
                teamName = t.teamName,
                type = t.type,
                gold = t.goldNum >= 0 ? t.goldNum : t.diamondNum,
                score = t.totalScore,
                tasks = t.completeTaskCount,
                taskCorrect = t.completeTaskCount,
                taskWrong = t.invalidTaskCount,
                hasActiveTask = t.task != null && !string.IsNullOrEmpty(t.task.taskType)
            };
        }
        ApplyRoles(start.teams, true);
    }

    public void Diff(ReplayRound prev, ReplayRound next, bool fx)
    {
        // 上一回合快照（id → role）
        var prevRoles = new Dictionary<long, ReplayRole>();
        var prevTeamType = new Dictionary<long, string>();
        if (prev != null)
            foreach (var t in prev.teams)
                foreach (var r in t.roles)
                {
                    prevRoles[r.id] = r;
                    prevTeamType[r.id] = t.type;
                }

        // 应用新快照（内部触发出生/死亡回调）
        ApplyRoles(next.teams, !fx);

        // 记录当前回合资源类型（供 collect 徽标查询，与 fx 无关）
        resourceNames.Clear();
        foreach (var res in next.resources)
            if (!string.IsNullOrEmpty(res.resName))
                resourceNames[res.x + "," + res.y] = res.resName;

        // 指令回调：通知 host 每个单位的本回合指令
        if (fx && host != null)
            foreach (var t in next.teams)
                foreach (var r in t.roles)
                {
                    if (r.commands == null || r.commands.Count == 0) continue;
                    var u = GetUnit(r.id);
                    if (u == null) continue;
                    foreach (var c in r.commands)
                        host.OnCommand(u, c);
                }

        // 伤害推断：血量下降 → 找指向该格子的 attack 指令
        foreach (var t in next.teams)
            foreach (var r in t.roles)
            {
                if (!prevRoles.TryGetValue(r.id, out var pr)) continue;
                if (r.health < pr.health)
                {
                    var u = GetUnit(r.id);
                    if (u == null) continue;
                    var dmg = pr.health - r.health;
                    var attacker = FindAttacker(r, next.teams);
                    if (fx && host != null) host.OnDamage(attacker, u, dmg);
                }
            }

        // ---- 推断日志（commands 为空时，通过状态变化反推事件） ----
        if (fx && host != null)
        {
            // 1. 移动：x,y 发生变化
            foreach (var t in next.teams)
                foreach (var r in t.roles)
                {
                    if (!prevRoles.TryGetValue(r.id, out var pr)) continue;
                    if (pr.x != r.x || pr.y != r.y)
                    {
                        var u = GetUnit(r.id);
                        if (u != null)
                            host.Log("cmd", u.name + " 移动 (" + pr.x + "," + pr.y + ")→(" + r.x + "," + r.y + ")", t.type);
                    }
                }

            // 2. 背包变化：采集/获得/消耗资源
            foreach (var t in next.teams)
                foreach (var r in t.roles)
                {
                    if (!prevRoles.TryGetValue(r.id, out var pr)) continue;
                    var pBp = new Dictionary<string, int>();
                    if (pr.backpacks != null) foreach (var b in pr.backpacks) { if (!pBp.ContainsKey(b.name)) pBp[b.name] = 0; pBp[b.name] += b.num; }
                    var nBp = new Dictionary<string, int>();
                    if (r.backpacks != null) foreach (var b in r.backpacks) { if (!nBp.ContainsKey(b.name)) nBp[b.name] = 0; nBp[b.name] += b.num; }
                    foreach (var kv in nBp)
                    {
                        int prevN = pBp.ContainsKey(kv.Key) ? pBp[kv.Key] : 0;
                        if (kv.Value > prevN)
                        {
                            var u = GetUnit(r.id);
                            if (u != null) host.Log("cmd", u.name + " 获得 " + ItemNameCn.Cn(kv.Key) + " x" + (kv.Value - prevN), t.type);
                        }
                        else if (kv.Value < prevN)
                        {
                            var u = GetUnit(r.id);
                            if (u != null) host.Log("cmd", u.name + " 消耗 " + ItemNameCn.Cn(kv.Key) + " x" + (prevN - kv.Value), t.type);
                        }
                    }
                    foreach (var kv in pBp)
                        if (!nBp.ContainsKey(kv.Key))
                        {
                            var u = GetUnit(r.id);
                            if (u != null) host.Log("cmd", u.name + " 消耗 " + ItemNameCn.Cn(kv.Key) + " x" + kv.Value, t.type);
                        }
                }

            // 3. 建造：新出现的围墙 (type=5) 或武器工事 (30/31/32，兼容旧 3) 记录一次
            foreach (var t in next.teams)
                foreach (var r in t.roles)
                {
                    if (prevRoles.ContainsKey(r.id)) continue;
                    if (r.roleType == 5 || IsTowerType(r.roleType))
                    {
                        var u = GetUnit(r.id);
                        if (u != null)
                            host.Log("cmd", u.name + " 建造于 (" + r.x + "," + r.y + ")", t.type);
                    }
                }

            // 4. 金币变化
            foreach (var t in next.teams)
            {
                if (prev != null)
                    foreach (var pt in prev.teams)
                    {
                        if (pt.teamId != t.teamId) continue;
                        int prevGold = pt.goldNum >= 0 ? pt.goldNum : pt.diamondNum;
                        int nowGold = t.goldNum >= 0 ? t.goldNum : prevGold;
                        if (nowGold > prevGold)
                            host.Log("cmd", t.teamName + " 金币 +" + (nowGold - prevGold) + "（贩卖/任务奖励）", t.type);
                        break;
                    }
            }

            // 5. 任务完成
            foreach (var t in next.teams)
                if (prev != null)
                    foreach (var pt in prev.teams)
                    {
                        if (pt.teamId != t.teamId) continue;
                        if (t.completeTaskCount > pt.completeTaskCount)
                            host.Log("info", "★ " + t.teamName + " 完成任务！共 " + t.completeTaskCount + " 项", t.type);
                        break;
                    }
        }

        // 说话检测：talk 字段非空且与上一回合不同 → 气泡
        if (fx && host != null)
            foreach (var t in next.teams)
                foreach (var r in t.roles)
                {
                    if (string.IsNullOrEmpty(r.talk)) continue;
                    ReplayRole pr;
                    bool isNew = !prevRoles.TryGetValue(r.id, out pr);
                    if (isNew || pr.talk != r.talk)
                    {
                        var u = GetUnit(r.id);
                        if (u != null) host.OnTalk(u, r.talk);
                    }
                }

        // 资源变化日志
        if (fx && host != null)
        {
            var pr = new Dictionary<string, ReplayResource>();
            if (prev != null)
                foreach (var res in prev.resources) pr[res.x + "," + res.y] = res;
            var nr = new Dictionary<string, ReplayResource>();
            foreach (var res in next.resources) nr[res.x + "," + res.y] = res;
            foreach (var kv in pr)
                if (!nr.ContainsKey(kv.Key))
                {
                    host.Log("info", kv.Value.resName + "矿 @(" + kv.Value.x + "," + kv.Value.y + ") 枯竭");
                    host.OnResourceDepleted(kv.Value.x, kv.Value.y, kv.Value.resName);
                }
            foreach (var kv in nr)
                if (!pr.ContainsKey(kv.Key))
                    host.Log("info", kv.Value.resName + "矿 刷新 @(" + kv.Value.x + "," + kv.Value.y + ")");
        }

        // 队伍统计 + 任务
        foreach (var t in next.teams)
        {
            if (!teams.TryGetValue(t.teamId, out var st)) continue;
            st.gold = t.goldNum >= 0 ? t.goldNum : st.gold;
            st.score = t.totalScore;
            st.tasks = t.completeTaskCount;
            st.taskCorrect = t.completeTaskCount;
            st.taskWrong = t.invalidTaskCount;
            st.task1Done = t.task1Done; st.task1Failed = t.task1Failed; st.task1Total = t.task1Total;
            st.task2Done = t.task2Done; st.task2Failed = t.task2Failed; st.task2Total = t.task2Total;
            st.hasActiveTask = t.task != null && !string.IsNullOrEmpty(t.task.taskType);
            foreach (var r in t.roles)
                if (r.roleType == 4) { st.baseHp = r.health; break; }
            if (t.task != null && !string.IsNullOrEmpty(t.task.taskType))
            {
                st.taskText = (string.IsNullOrEmpty(t.task.shortcut) ? "" : "【" + t.task.shortcut + "】")
                            + t.task.description + (t.task.isTaskComplete ? " ✅已完成" : "");
            }
            else st.taskText = "暂无任务";
        }

        // 新闻
        if (fx && host != null)
            foreach (var n in next.news)
                if (!string.IsNullOrEmpty(n.text)) host.OnNews(n.text);

        currentRound = next.round;
    }

    // ---------- 内部 ----------

    void ApplyRoles(List<ReplayTeam> teamList, bool silent)
    {
        var seen = new HashSet<long>();
        foreach (var t in teamList)
        {
            foreach (var r in t.roles)
            {
                // 血量归零 = 已死亡，不加入 seen，后续会被清理
                if (r.health <= 0) continue;
                seen.Add(r.id);
                bool existed = units.ContainsKey(r.id);
                bool noView = existed && units[r.id].view == null;
                bool wasDead = existed && (units[r.id].dead || units[r.id].dying);
                bool isNew = !existed || noView || wasDead;
                var u = EnsureUnit(r, t.type);
                if (isNew && host != null && !silent)
                    host.OnSpawn(u);
            }
        }
        foreach (var kv in new List<KeyValuePair<long, UnitState>>(units))
        {
            var u = kv.Value;
            if (u.dead) continue;
            if (!seen.Contains(u.id) && !u.dying)
            {
                u.dying = true;
                // 野兽播放完整死亡动画（不缩小消失），故给更长销毁时间；其余单位保持 0.45s 缩小消失
                u.dieAt = Time.time + (u.IsBeast ? 1.3f : 0.45f);
                if (host != null && !silent) host.OnDie(u);
            }
        }
    }

    UnitState EnsureUnit(ReplayRole r, string teamType)
    {
        if (units.TryGetValue(r.id, out var u))
        {
            // 复活：清除死亡/濒死标记
            if (u.dying || u.dead)
            {
                u.dying = false;
                u.dead = false;
                u.animScale = 1f;
                if (u.view != null) { GameObject.Destroy(u.view.gameObject); u.view = null; }
            }
            u.type = r.roleType;
            u.teamType = teamType;
            u.hp = r.health;
            u.ap = r.attackPower;
            u.level = r.level;
            u.stun = r.inControl;
            u.commands = r.commands;
            u.backpacks = r.backpacks;
            if (r.health > u.maxHp) u.maxHp = r.health;
            if (string.IsNullOrEmpty(u.name)) u.name = UnitName(u);
            var wp = UnitWorldPos(r.x, r.y, r.roleType);
            if (wp != u.targetPos)
            {
                u.moveFrom = u.pos;
                u.moveTo = wp;
                u.moveStart = Time.time;
                u.moving = true;
            }
            u.targetPos = wp;
            return u;
        }

        u = new UnitState
        {
            id = r.id,
            teamId = FindTeamId(teamType, r),
            teamType = teamType,
            type = r.roleType,
            hp = r.health,
            maxHp = Mathf.Max(1, r.health),
            ap = r.attackPower,
            level = r.level,
            stun = r.inControl,
            commands = r.commands,
            backpacks = r.backpacks
        };
        u.pos = UnitWorldPos(r.x, r.y, r.roleType);
        u.targetPos = u.pos;
        u.name = UnitName(u);
        units[u.id] = u;
        return u;
    }

    string FindTeamId(string teamType, ReplayRole r)
    {
        foreach (var kv in teams)
            if (kv.Value.type == teamType) return kv.Key;
        return "";
    }

    public UnitState GetUnit(long id)
    {
        UnitState u;
        return units.TryGetValue(id, out u) ? u : null;
    }

    UnitState FindAttacker(ReplayRole victim, List<ReplayTeam> teams)
    {
        foreach (var t in teams)
            foreach (var r in t.roles)
                foreach (var c in r.commands)
                {
                    if (c.action != "attack" || !c.valid || !c.hasTarget) continue;
                    // 攻击目标可能是数组（加特林多落点 / 电磁狙击炮/火箭单落点），命中判定遍历全部落点
                    bool hit = false;
                    if (c.targets != null && c.targets.Count > 0)
                    {
                        foreach (var tp in c.targets)
                            if (tp.x == victim.x && tp.y == victim.y) { hit = true; break; }
                    }
                    else hit = (c.x == victim.x && c.y == victim.y);
                    if (hit)
                    {
                        var u = GetUnit(r.id);
                        if (u != null) return u;
                    }
                }
        return null;
    }

    public static string UnitName(UnitState u)
    {
        if (!string.IsNullOrEmpty(u.name)) return u.name;
        return TypeName(u.type) + "·" + u.id;
    }

    /// <summary>类型编号 → 显示名（含武器工事 30/31/32，兼容 TYPE_NAMES 越界）。</summary>
    public static string TypeName(int type)
    {
        if (type == 30) return "加特林炮台";
        if (type == 31) return "电磁狙击炮";
        if (type == 32) return "火箭发射台";
        return (type >= 0 && type < TYPE_NAMES.Length ? TYPE_NAMES[type] : "单位");
    }

    /// <summary>是否武器工事类型：30 加特林 / 31 电磁狙击炮 / 32 火箭发射台（兼容旧 3 防御塔）。</summary>
    public static bool IsTowerType(int t) { return t == 3 || t == 30 || t == 31 || t == 32; }

    // ---------- 昼夜 ----------
    public static int DayOf(int round) { return (round - 1) / 130 + 1; }
    public static bool IsNight(int round) { return ((round - 1) % 130) >= 80; }
}
