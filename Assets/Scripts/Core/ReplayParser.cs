using System;
using System.Collections.Generic;

/// <summary>
/// JSONL 解析器：每行一个 JSON（start / round / finish），末尾可有 "valid" 标记。
/// 容错设计：未知字段忽略、缺失字段给默认值，判题器格式小改动不影响播放。
/// </summary>
public static class ReplayParser
{
    public static ReplayData Parse(string text)
    {
        if (string.IsNullOrEmpty(text)) throw new Exception("replay 文件为空");
        var lines = text.Split('\n');
        var objs = new List<Dictionary<string, object>>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line == "valid") continue;
            if (line[0] != '{') continue;
            var o = MiniJson.Dict(MiniJson.Parse(line));
            if (o != null) objs.Add(o);
        }
        if (objs.Count == 0) throw new Exception("文件中没有可解析的 JSON");

        var data = new ReplayData();
        var first = objs[0];
        if (MiniJson.Str(first, "type") != "start")
            throw new Exception("第一行必须是 type=start");

        data.start = ParseStart(first);
        data.rounds = new List<ReplayRound>();
        foreach (var o in objs)
        {
            var t = MiniJson.Str(o, "type");
            if (t == "round") data.rounds.Add(ParseRound(o));
            else if (t == "finish") data.finish = ParseFinish(o);
        }
        data.rounds.Sort((a, b) => a.round.CompareTo(b.round));
        if (data.rounds.Count == 0) throw new Exception("没有 round 数据");
        return data;
    }

    static ReplayStart ParseStart(Dictionary<string, object> o)
    {
        var s = new ReplayStart { type = "start" };
        // vendorShopPriceChange.date.startDay/stopDay：小贩回收价波动窗口（推理类【官方消息】影响）
        var pc = MiniJson.Obj(o, "vendorShopPriceChange");
        var date = pc != null ? MiniJson.Obj(pc, "date") : null;
        if (date != null)
        {
            s.priceChangeStartDay = MiniJson.Int(date, "startDay", -1);
            s.priceChangeEndDay = MiniJson.Int(date, "stopDay", -1);
        }
        var m = MiniJson.Obj(o, "map");
        if (m != null)
        {
            s.map = new ReplayMap
            {
                mapName = MiniJson.Str(m, "mapName") ?? "",
                width = MiniJson.Int(m, "width", 41),
                height = MiniJson.Int(m, "height", 32)
            };
            var arr = MiniJson.Arr(m, "data");
            if (arr != null)
            {
                s.map.data = new int[arr.Count];
                for (int i = 0; i < arr.Count; i++)
                {
                    var v = arr[i];
                    if (v is long l) s.map.data[i] = (int)l;
                    else if (v is double dd) s.map.data[i] = (int)dd;
                }
            }
            else s.map.data = new int[s.map.width * s.map.height];
        }
        var teams = MiniJson.Arr(o, "teams");
        if (teams != null)
            foreach (var t in teams) s.teams.Add(ParseTeam(MiniJson.Dict(t)));
        return s;
    }

    static ReplayRound ParseRound(Dictionary<string, object> o)
    {
        var r = new ReplayRound { round = MiniJson.Int(o, "round", 0) };
        var res = MiniJson.Arr(o, "resources");
        if (res != null)
            foreach (var rr in res)
            {
                var d = MiniJson.Dict(rr);
                if (d == null) continue;
                var pos = MiniJson.Obj(d, "pos");
                r.resources.Add(new ReplayResource
                {
                    x = pos != null ? MiniJson.Int(pos, "x") : 0,
                    y = pos != null ? MiniJson.Int(pos, "y") : 0,
                    resName = MiniJson.Str(d, "resName") ?? "",
                    resNum = MiniJson.Int(d, "resNum")
                });
            }
        var npcs = MiniJson.Arr(o, "npc");
        if (npcs != null)
            foreach (var n in npcs)
            {
                var d = MiniJson.Dict(n);
                if (d == null) continue;
                var pos = MiniJson.Obj(d, "pos");
                r.npc.Add(new ReplayNpc
                {
                    x = pos != null ? MiniJson.Int(pos, "x") : 0,
                    y = pos != null ? MiniJson.Int(pos, "y") : 0,
                    roleName = MiniJson.Str(d, "roleName") ?? ""
                });
            }
        // news 对象格式：{"officialNews": "官方消息(推理类)", "folkLegends": "民间传闻(长上下文)"}
        var newsObj = MiniJson.Obj(o, "news");
        if (newsObj != null)
        {
            r.officialNews = MiniJson.Str(newsObj, "officialNews") ?? "";
            r.folkLegends = MiniJson.Str(newsObj, "folkLegends") ?? "";
        }
        // 兼容旧数组格式：news: [ {type,text} | "文本" ]
        var news = MiniJson.Arr(o, "news");
        if (news != null)
            foreach (var n in news)
            {
                if (n is string sstr)
                {
                    r.news.Add(new ReplayNews { text = sstr });
                    continue;
                }
                var d = MiniJson.Dict(n);
                if (d == null) continue;
                r.news.Add(new ReplayNews
                {
                    type = MiniJson.Str(d, "type") ?? "info",
                    text = MiniJson.Str(d, "text") ?? (MiniJson.Str(d, "content") ?? "")
                });
            }
        var vsp = MiniJson.Arr(o, "vendorShopList");
        if (vsp != null)
            foreach (var v in vsp)
            {
                var vd = MiniJson.Dict(v);
                if (vd == null) continue;
                r.vendorShopList.Add(new ReplayVendorShop
                {
                    name = MiniJson.Str(vd, "name") ?? "",
                    price = MiniJson.Int(vd, "price", 0)
                });
            }
        var teams = MiniJson.Arr(o, "teams");
        if (teams != null)
            foreach (var t in teams) r.teams.Add(ParseTeam(MiniJson.Dict(t)));
        return r;
    }

    static ReplayTeam ParseTeam(Dictionary<string, object> o)
    {
        var t = new ReplayTeam
        {
            type = MiniJson.Str(o, "type") ?? "",
            teamId = MiniJson.Str(o, "teamId") ?? "",
            teamName = MiniJson.Str(o, "teamName") ?? "",
            goldNum = MiniJson.Int(o, "goldNum", 0),
            diamondNum = MiniJson.Int(o, "diamondNum", 0),
            totalScore = MiniJson.Int(o, "totalScore", 0),
            completeTaskCount = MiniJson.Int(o, "completeTaskCount", 0),
            invalidTaskCount = MiniJson.Int(o, "invalidTaskCount", 0)
        };
        var task = MiniJson.Obj(o, "task");
        if (task != null)
        {
            var tpos = MiniJson.Obj(task, "pos");
            t.task = new ReplayTask
            {
                taskType = MiniJson.Str(task, "taskType") ?? "",
                description = MiniJson.Str(task, "description") ?? "",
                shortcut = MiniJson.Str(task, "shortcut") ?? "",
                level = MiniJson.Str(task, "level") ?? "",
                reward = MiniJson.Int(task, "reward", 0),
                isTaskComplete = MiniJson.Bool(task, "isTaskComplete", false),
                roundCost = MiniJson.Int(task, "roundCost", 0),
                taskX = tpos != null ? MiniJson.Int(tpos, "x", 0) : 0,
                taskY = tpos != null ? MiniJson.Int(tpos, "y", 0) : 0
            };
        }
        // allTaskInfo：自进化类1/2 每类三元 [已完成, 失败, 总数]（实际数据可能只有 [完成, 总数] 两项，lenient 兼容）
        var ati = MiniJson.Obj(o, "allTaskInfo");
        if (ati != null)
        {
            ParseTaskProgress(MiniJson.Arr(ati, "selfEvolutionTask1"), out t.task1Done, out t.task1Failed, out t.task1Total);
            ParseTaskProgress(MiniJson.Arr(ati, "selfEvolutionTask2"), out t.task2Done, out t.task2Failed, out t.task2Total);
        }
        var roles = MiniJson.Arr(o, "roles");
        if (roles != null)
            foreach (var r in roles)
            {
                var d = MiniJson.Dict(r);
                if (d == null) continue;
                var role = ParseRole(d);
                t.roles.Add(role);
            }
        return t;
    }

    /// <summary>解析 allTaskInfo 单类进度数组 [完成, 失败, 总数]；兼容实际数据只有 [完成, 总数] 两项的格式。</summary>
    static void ParseTaskProgress(List<object> arr, out int done, out int failed, out int total)
    {
        done = 0; failed = 0; total = 0;
        if (arr == null || arr.Count == 0) return;
        if (arr.Count >= 3) { done = ConvInt(arr[0]); failed = ConvInt(arr[1]); total = ConvInt(arr[2]); }
        else if (arr.Count == 2) { done = ConvInt(arr[0]); total = ConvInt(arr[1]); }
        else { done = ConvInt(arr[0]); }
    }

    static int ConvInt(object v)
    {
        if (v == null) return 0;
        if (v is long l) return (int)l;
        if (v is int i) return i;
        if (v is double d) return (int)d;
        int r; return int.TryParse(v.ToString(), out r) ? r : 0;
    }

    static ReplayRole ParseRole(Dictionary<string, object> d)
    {
        var role = new ReplayRole
        {
            id = MiniJson.Lng(d, "id", 0),
            roleType = MiniJson.Int(d, "roleType", MiniJson.Int(d, "mapType", 0)),
            health = MiniJson.Int(d, "health", 1),
            attackPower = MiniJson.Int(d, "attackPower", 0),
            inControl = MiniJson.Bool(d, "inControl", false),
            talk = MiniJson.Str(d, "talk"),
            roadLineType = MiniJson.Str(d, "roadLineType") ?? "",
            level = MiniJson.Int(d, "level", 0)
        };
        var pos = MiniJson.Obj(d, "pos");
        if (pos != null) { role.x = MiniJson.Int(pos, "x"); role.y = MiniJson.Int(pos, "y"); }
        var cmds = MiniJson.Arr(d, "commands");
        if (cmds != null)
            foreach (var c in cmds)
            {
                var cd = MiniJson.Dict(c);
                if (cd == null) continue;
                var cmd = new ReplayCommand
                {
                    action = MiniJson.Str(cd, "action") ?? "",
                    valid = MiniJson.Bool(cd, "valid", true),
                    queryInfo = MiniJson.Str(cd, "queryInfo") ?? "",
                    taskAnswer = MiniJson.Str(cd, "taskAnswer") ?? "",
                    targetName = MiniJson.Str(cd, "targetName") ?? ""
                };
                // targetPos 两种格式：新格式 attack 为坐标数组 [{x,y},...]（加特林 N 落点，电磁狙击炮/火箭 1 落点）；旧格式为单对象
                var tpArr = MiniJson.Arr(cd, "targetPos");
                if (tpArr != null && tpArr.Count > 0)
                {
                    cmd.hasTarget = true;
                    foreach (var tpRaw in tpArr)
                    {
                        var td = MiniJson.Dict(tpRaw);
                        if (td == null) continue;
                        cmd.targets.Add(new ReplayPoint { x = MiniJson.Int(td, "x"), y = MiniJson.Int(td, "y") });
                    }
                    if (cmd.targets.Count > 0) { cmd.x = cmd.targets[0].x; cmd.y = cmd.targets[0].y; }
                }
                else
                {
                    var tp = MiniJson.Obj(cd, "targetPos");
                    if (tp != null)
                    {
                        cmd.hasTarget = true;
                        cmd.x = MiniJson.Int(tp, "x");
                        cmd.y = MiniJson.Int(tp, "y");
                    }
                }
                var stps = MiniJson.Arr(cd, "skillTargetPos");
                if (stps != null)
                    foreach (var sp in stps)
                    {
                        var sd = MiniJson.Dict(sp);
                        if (sd == null) continue;
                        cmd.skillTargetPos.Add(new ReplayPoint
                        {
                            x = MiniJson.Int(sd, "x"),
                            y = MiniJson.Int(sd, "y")
                        });
                    }
                role.commands.Add(cmd);
            }
        var bp = MiniJson.Arr(d, "backpacks");
        if (bp != null)
            foreach (var b in bp)
            {
                // 新格式: 纯字符串 ["stone","Medicine"]
                if (b is string s)
                {
                    role.backpacks.Add(new ReplayItem { name = s, num = 1 });
                    continue;
                }
                // 旧格式: 对象 [{"name":"石头","num":3}]
                var bd = MiniJson.Dict(b);
                if (bd == null) continue;
                role.backpacks.Add(new ReplayItem
                {
                    name = MiniJson.Str(bd, "name") ?? "",
                    num = MiniJson.Int(bd, "num", 1)
                });
            }
        return role;
    }

    static ReplayFinish ParseFinish(Dictionary<string, object> o)
    {
        var f = new ReplayFinish();
        var ps = MiniJson.Arr(o, "players");
        if (ps != null)
            foreach (var p in ps)
            {
                var d = MiniJson.Dict(p);
                if (d == null) continue;
                f.players.Add(new ReplayPlayerResult
                {
                    teamId = MiniJson.Str(d, "teamId") ?? "",
                    teamName = MiniJson.Str(d, "teamName") ?? "",
                    result = MiniJson.Str(d, "result") ?? "",
                    diamondNum = MiniJson.Int(d, "diamondNum", 0),
                    goldNum = MiniJson.Int(d, "goldNum", 0),
                    totalScore = MiniJson.Int(d, "totalScore", 0)
                });
            }
        return f;
    }
}
