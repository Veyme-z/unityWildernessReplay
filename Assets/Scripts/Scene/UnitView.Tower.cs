// UnitView 的防御塔视觉子模块（Partial Class）
// 职责：塔视觉包装实例化/替换、塔攻击表现触发/复位
// 字段声明与主流程见 UnitView.cs

using UnityEngine;

public partial class UnitView
{
    /// <summary>防御塔 (type=3)：隐藏旧 Visual，改为 Resources 中可编辑的 Cube Tower Defense 视觉包装 Prefab。</summary>
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

        // 运行时选择 Resources 中的视觉包装 Prefab（以后调尺寸直接改对应 CubeTowers Prefab）
        string type = TowerVisualController.ResolveTowerType(this);
        string path = "Prefabs/Buildings/CubeTowers/Tower_" + type + "_" + faction;
        var prefab = Resources.Load<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogWarning("[UnitView] 未找到防御塔视觉包装 " + path);
            return;
        }

        var inst = Object.Instantiate(prefab, visualRoot);
        inst.name = "TowerVisual_" + type;
        _towerVisual = inst.GetComponent<TowerVisualController>();
        if (_towerVisual == null) _towerVisual = inst.AddComponent<TowerVisualController>();
        _towerVisual.Setup(this, faction);
    }

    /// <summary>触发防御塔攻击表现（炮塔转向 + 后坐力 + 枪口特效），目标为世界坐标。</summary>
    public void TriggerTowerAttack(Vector3 targetWorldPos)
    {
        if (_towerVisual != null && _towerVisual.IsSetup)
            _towerVisual.Fire(targetWorldPos);
    }

    /// <summary>触发武器工事多目标攻击表现（加特林 N 落点：N 条弹道），落点为世界坐标数组。</summary>
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
