using UnityEngine;

/// <summary>
/// 装甲车任务点驱动：监控各队「自进化类2」（TaskCardBadge.REPAIR_TASK_TYPE）任务的完成跳变，
/// 完成时让对应卡车（task.pos 指向的格子）开向小贩售卖。挂在 ReplayEntry 同一 GameObject。
///
/// 检测逻辑与 TaskBadgeManager 一致：读数据 rounds[cur-1] / rounds[cur-2] 做「上一回合有任务未完成、
/// 本回合完成」的跳变判定（Seek 后按目标回合数据重建，不依赖帧间连续）。每次完成都触发售卖
/// （MissionPoint.StartSellCycle：开向小贩 → 「贩卖成功」→ 消失 → 原任务点重生，供下次任务）。
/// </summary>
public class MissionVehicleDriver : MonoBehaviour
{
    static readonly Vector2 VENDOR_GAME = new Vector2(20f, 16f);   // 小贩格子坐标（tile 9，当前 replay 固定）

    ReplayPlayer _player;
    int _mapW = 41, _mapH = 32;
    int _lastCur = -1;   // 上一帧 cur：检测 Seek（暂停 && cur 变化）

    void Update()
    {
        if (_player == null)
        {
            _player = FindObjectOfType<ReplayPlayer>();
            if (_player == null) return;
            if (_player.data != null && _player.data.start != null && _player.data.start.map != null)
            {
                _mapW = _player.data.start.map.width;
                _mapH = _player.data.start.map.height;
            }
        }

        // Seek 检测（对齐 TaskBadgeManager）：暂停 && cur 变化 = 拖动进度条/跳回合。
        // 必须重置所有卡车为原任务点破损状态——否则回到「任务未完成」的回合时，
        // 进行中的售卖协程（开向小贩/贩卖成功徽标）会继续跑完，违背该回合卡车应破损的设定。
        if (_lastCur >= 0 && !_player.playing && _player.cur != _lastCur)
            ResetAllTrucks();
        _lastCur = _player.cur;

        if (_player.data == null) return;
        if (_player.cur < 1 || _player.cur > _player.data.rounds.Count) return;
        var round = _player.data.rounds[_player.cur - 1];
        if (round == null || round.teams == null) return;

        ReplayRound prevRound = null;
        if (_player.cur >= 2) prevRound = _player.data.rounds[_player.cur - 2];

        for (int i = 0; i < round.teams.Count; i++)
        {
            var task = round.teams[i].task;
            if (task == null || task.taskType != TaskCardBadge.REPAIR_TASK_TYPE) continue;
            if (!task.isTaskComplete) continue;

            // 上一回合该队有未完成的同类任务 → 本回合完成 = 完成跳变
            bool prevHas = false, prevDone = false;
            if (prevRound != null && prevRound.teams != null && i < prevRound.teams.Count)
            {
                var pt = prevRound.teams[i].task;
                if (pt != null && pt.taskType == TaskCardBadge.REPAIR_TASK_TYPE)
                {
                    prevHas = true;
                    prevDone = pt.isTaskComplete;
                }
            }
            if (prevHas && !prevDone)
                DriveTruck(task.taskX, task.taskY);
        }
    }

    void DriveTruck(int gx, int gy)
    {
        var mp = FindTruck(gx, gy);
        if (mp == null) return;
        Vector3 vendor = CellToWorld((int)VENDOR_GAME.x, (int)VENDOR_GAME.y);
        mp.StartSellCycle(vendor, _player);
        Debug.Log("[MissionVehicleDriver] 卡车 (" + gx + "," + gy + ") 任务完成，开向小贩售卖");
    }

    static MissionPoint FindTruck(int gx, int gy)
    {
        foreach (var mp in FindObjectsOfType<MissionPoint>())
        {
            if (!mp.isVehicle) continue;
            // 卡车 1×2 占两格：任务 pos 可能指向其中任一格，都命中同一辆车
            if (mp.gameX == gx && mp.gameY == gy) return mp;
            if (mp.gameX2 >= 0 && mp.gameX2 == gx && mp.gameY2 == gy) return mp;
        }
        return null;
    }

    /// <summary>Seek 后把全部任务点卡车重置为原任务点的破损卡车（取消售卖协程/销毁徽标）。</summary>
    static void ResetAllTrucks()
    {
        foreach (var mp in FindObjectsOfType<MissionPoint>())
            if (mp.isVehicle) mp.ResetToBroken();
    }

    Vector3 CellToWorld(int gx, int gy)
    {
        return new Vector3(gx - (_mapW - 1) * 0.5f, 0f, gy - (_mapH - 1) * 0.5f);
    }
}
