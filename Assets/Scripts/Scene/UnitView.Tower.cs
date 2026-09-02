// UnitView 的防御塔视觉子模块（Partial Class）
// 职责：塔视觉包装实例化/替换、塔攻击表现触发/复位
// 字段声明与主流程见 UnitView.cs

using System.Collections;
using UnityEngine;

public partial class UnitView
{
    int _towerVisualLevel = -1;   // 当前已加载的塔视觉等级（武器工事升级时换 _2/_3 模型）

    /// <summary>防御塔视觉：隐藏旧 Visual，按武器等级加载 Tower_{Type}_{Level}_{Faction} 包装 Prefab 到 VisualRoot。</summary>
    void SetupTowerVisual()
    {
        bool isDefender = state.teamType == "defender";
        string faction = isDefender ? "Red" : "Blue";

        // 关闭旧 Visual（旧 KayKit 塔模型），由新塔视觉替代内部视觉
        var visual = transform.Find("Visual");
        if (visual != null) visual.gameObject.SetActive(false);

        // 视觉宿主：优先复用 Tower.prefab 中的 VisualRoot，否则运行时创建
        Transform visualRoot = transform.Find("VisualRoot");
        if (visualRoot == null)
        {
            var vr = new GameObject("VisualRoot");
            vr.transform.SetParent(transform, false);
            visualRoot = vr.transform;
        }

        // 按武器等级选模型：level 1/2/3 → _1/_2/_3；4~5 用最高级 _3（素材包塔模型只有 1~3 级）
        int lvl = Mathf.Clamp(state != null ? state.level : 1, 1, 3);
        string type = TowerVisualController.ResolveTowerType(this);
        string path = "Prefabs/Buildings/CubeTowers/Tower_" + type + "_" + lvl + "_" + faction;
        var prefab = Resources.Load<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogWarning("[UnitView] 未找到防御塔视觉包装 " + path);
            return;
        }

        // 换级时先销毁旧包装（首次 _towerVisual 为 null）
        if (_towerVisual != null)
        {
            Destroy(_towerVisual.gameObject);
            _towerVisual = null;
        }

        var inst = Object.Instantiate(prefab, visualRoot);
        inst.name = "TowerVisual_" + type + "_" + lvl;
        _towerVisual = inst.GetComponent<TowerVisualController>();
        if (_towerVisual == null) _towerVisual = inst.AddComponent<TowerVisualController>();
        _towerVisual.Setup(this, faction);
        _towerVisualLevel = lvl;
    }

    /// <summary>每帧检测武器等级变化（升级券生效/回合推进）→ 换对应等级塔模型。照 WallOrientation 逐帧比对 state.level。</summary>
    void RefreshTowerLevelVisual()
    {
        if (state == null) return;
        int lvl = Mathf.Clamp(state.level, 1, 3);
        if (_towerVisualLevel == -1 && _towerVisual == null) return;  // 初始加载失败时不每帧重试刷警告
        if (lvl == _towerVisualLevel) return;
        SetupTowerVisual();
    }

    /// <summary>塔视觉包装是否已就绪（火箭等需要视觉包装才能表现攻击到达）。</summary>
    public bool IsTowerVisualReady
    {
        get { return _towerVisual != null && _towerVisual.IsSetup; }
    }

    /// <summary>触发防御塔攻击表现（炮塔转向 + 后坐力 + 塔原生特效），目标为世界坐标。</summary>
    public void TriggerTowerAttack(Vector3 targetWorldPos)
    {
        if (_towerVisual == null || !_towerVisual.IsSetup) return;
        _towerVisual.Fire(targetWorldPos);
    }

    /// <summary>触发武器工事多目标攻击表现（加特林 N 落点），落点为世界坐标数组。</summary>
    public void TriggerTowerAttackMulti(Vector3[] targetWorldPositions)
    {
        if (_towerVisual != null && _towerVisual.IsSetup)
            _towerVisual.Fire(targetWorldPositions);
    }

    /// <summary>清除防御塔攻击表现（Seek 跳转后调用）。</summary>
    public void ResetTowerAttack()
    {
        if (_towerVisual != null)
            _towerVisual.ResetAttack();
    }
}
