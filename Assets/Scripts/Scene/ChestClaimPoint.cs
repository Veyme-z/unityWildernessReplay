using UnityEngine;

/// <summary>
/// 任务点1（宝箱 / 自进化类1）显示闸门：该格子空闲时显示黄旗（flag_yellow），
/// 只有当某队在该格「领取任务」（数据上该队存在 taskType=自进化类1 且 pos 指向本格的进行中任务）
/// 后才换成宝箱（GoldChest）。由 SceneBuilder.BuildChestPoint 挂到任务点根节点。
///
/// 纯数据驱动（对齐 TaskBadgeManager / MissionVehicleDriver / ReplayCinematic）：每帧读
/// rounds[cur-1] 判断本格是否正被某队做自进化类1任务，是则亮宝箱、否则亮旗——拖动进度条/跳回合
/// 也按目标回合数据正确重建。领取回合同时会触发全屏 claim 视频（ReplayCinematic），宝箱在视频
/// 遮挡期间已切好，视频播完可见。禁止改 ReplayParser / ReplayState / 伤害计算。
/// </summary>
public class ChestClaimPoint : MonoBehaviour
{
    public const string TASK_POINT1_TYPE = "自进化类1";

    public int gameX, gameY;      // 格子坐标（game 坐标系）
    public GameObject chestGo;    // 宝箱（初始隐藏，领取后显示）
    public GameObject flagGo;     // 黄旗（空闲显示）

    ReplayPlayer _player;
    bool _chestOn;                // 当前视觉状态（防每帧重复 SetActive）

    /// <summary>初始态：有旗亮旗、无旗则直接亮宝箱（兜底，保证格子上始终有标记物）。</summary>
    public void ApplyInitial()
    {
        SetChest(flagGo == null);
    }

    void Update()
    {
        if (_player == null)
        {
            _player = FindObjectOfType<ReplayPlayer>();
            if (_player == null) return;
        }
        if (_player.data == null || _player.data.rounds == null) return;

        int cur = _player.cur;
        if (cur < 1 || cur > _player.data.rounds.Count) return;
        var round = _player.data.rounds[cur - 1];
        if (round == null || round.teams == null) return;

        // 本格当前是否被某队做自进化类1任务（含完成当回合；任务从数据上消失即视为空闲）
        bool on = false;
        for (int i = 0; i < round.teams.Count; i++)
        {
            var team = round.teams[i];
            var task = team != null ? team.task : null;
            if (task == null) continue;
            if (task.taskType != TASK_POINT1_TYPE) continue;
            if (task.taskX == gameX && task.taskY == gameY) { on = true; break; }
        }
        // 兜底：无旗（素材缺失）时保持宝箱常亮
        if (flagGo == null) on = true;
        SetChest(on);
    }

    void SetChest(bool on)
    {
        if (_chestOn == on) return;
        _chestOn = on;
        if (chestGo != null) chestGo.SetActive(on);
        if (flagGo != null) flagGo.SetActive(!on);
    }
}
