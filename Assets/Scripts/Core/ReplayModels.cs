using System.Collections.Generic;

/// <summary>replay 文件全部数据模型（对应《replay 数据结构分析文档》）</summary>
public class ReplayData
{
    public ReplayStart start;
    public List<ReplayRound> rounds = new List<ReplayRound>();
    public ReplayFinish finish;
}

public class ReplayStart
{
    public string type = "";
    public ReplayMap map;
    public List<ReplayTeam> teams = new List<ReplayTeam>();
}

public class ReplayMap
{
    public string mapName = "";
    public int width;
    public int height;
    public int[] data; // 行主序 (y*width+x)
}

public class ReplayRound
{
    public int round;
    public List<ReplayResource> resources = new List<ReplayResource>();
    public List<ReplayNpc> npc = new List<ReplayNpc>();
    public List<ReplayNews> news = new List<ReplayNews>();
    public List<ReplayTeam> teams = new List<ReplayTeam>();
}

public class ReplayTeam
{
    public string type = "";        // challenger / defender
    public string teamId = "";
    public string teamName = "";
    public int goldNum;             // round 里用
    public int diamondNum;          // start/finish 里用
    public int totalScore;
    public int completeTaskCount;
    public int invalidTaskCount;
    public ReplayTask task;
    public List<ReplayRole> roles = new List<ReplayRole>();
}

public class ReplayRole
{
    public long id;
    public int x, y;                // 格子坐标 (replay 坐标系: 左下原点, y 向上)
    public int roleType;
    public int health = 1;
    public int attackPower;
    public bool inControl;
    public string talk;
    public string roadLineType = ""; // 兵线类型 (mid/top/bottom 或空)
    public int level;               // 角色等级
    public List<ReplayCommand> commands = new List<ReplayCommand>();
    public List<ReplayItem> backpacks = new List<ReplayItem>();
}

public class ReplayCommand
{
    public string action = "";
    public bool hasTarget;
    public int x, y;                // 目标格
    public bool valid = true;
    public string queryInfo = "";
    public string taskAnswer = "";
    public string targetName = "";  // buy/sell 目标名（如 UpgradeTowerAttack, copper）
    public List<ReplayPoint> skillTargetPos = new List<ReplayPoint>(); // 范围技能目标格（AoE，如 DizzyWeapon/Bomb 的 3×3）
}

public class ReplayPoint
{
    public int x;
    public int y;
}

public class ReplayItem
{
    public string name = "";
    public int num;
}

public class ReplayTask
{
    public string taskType = "";
    public string description = "";
    public string shortcut = "";
    public string level = "";
    public int reward;
    public bool isTaskComplete;
    public int roundCost;
}

public class ReplayResource
{
    public int x, y;
    public string resName = "";
    public int resNum;
}

public class ReplayNpc
{
    public int x, y;
    public string roleName = "";
}

public class ReplayNews
{
    public string type = "";
    public string text = "";
}

public class ReplayFinish
{
    public List<ReplayPlayerResult> players = new List<ReplayPlayerResult>();
}

public class ReplayPlayerResult
{
    public string teamId = "";
    public string teamName = "";
    public string result = "";      // victory / defeat
    public int diamondNum;
    public int goldNum;
    public int totalScore;
}
