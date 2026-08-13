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
            t.task = new ReplayTask
            {
                taskType = MiniJson.Str(task, "taskType") ?? "",
                description = MiniJson.Str(task, "description") ?? "",
                shortcut = MiniJson.Str(task, "shortcut") ?? "",
                level = MiniJson.Str(task, "level") ?? "",
                reward = MiniJson.Int(task, "reward", 0),
                isTaskComplete = MiniJson.Bool(task, "isTaskComplete", false),
                roundCost = MiniJson.Int(task, "roundCost", 0)
            };
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
                var tp = MiniJson.Obj(cd, "targetPos");
                if (tp != null)
                {
                    cmd.hasTarget = true;
                    cmd.x = MiniJson.Int(tp, "x");
                    cmd.y = MiniJson.Int(tp, "y");
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
