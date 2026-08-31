// UnitView 的防御塔视觉子模块（Partial Class）
// 职责：塔视觉包装实例化/替换、塔攻击表现触发/复位
// 字段声明与主流程见 UnitView.cs

using System.Collections;
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
        if (_towerVisual == null || !_towerVisual.IsSetup) return;
        // 电磁狙击炮(type=31)：电球从枪口飞向目标，到达后再出命中效果
        if (state != null && state.type == 31)
        {
            StartCoroutine(ElectricBallFly(targetWorldPos));
            return;
        }
        _towerVisual.Fire(targetWorldPos);
    }

    // 电磁炮电球飞行参数：CFXR Electrified 3 原生约 13 世界单位；满尺寸当"球"；速度 2（射程 7 → 时长封顶 0.8s）
    const float ELECTRIC_BALL_SCALE = 0.3f;
    const float ELECTRIC_BALL_SPEED = 10f;
    const float ELECTRIC_BALL_CHARGE_MIN = 0.02f;   // 充能起始 scale（枪口聚电）
    const float ELECTRIC_BALL_CHARGE_DUR = 0.15f;   // 充能时长（由小变大）
    const float ELECTRIC_BALL_MAX_LIFE = 1.6f;      // 兜底自毁：协程被打断（Seek/塔销毁）时电球也不会残留

    /// <summary>电磁炮电球飞行协程：枪口聚电（由小变大）→ 电球飞向目标 → 到达后出命中效果（命中环 + 电流电击）。</summary>
    private IEnumerator ElectricBallFly(Vector3 targetPos)
    {
        // 塔先开火（炮塔转向 + 后坐力）；枪口特效不用 CannonFireFX，改由电球在枪口充能承担
        if (_towerVisual != null) _towerVisual.FireMuzzleOnly(targetPos);

        var prefab = Resources.Load<GameObject>("FX/CFXR Electrified 3");
        GameObject ball = null;
        if (prefab != null)
        {
            Vector3 startPos = _towerVisual != null ? _towerVisual.MuzzleWorldPosition() : transform.position + Vector3.up;
            ball = Instantiate(prefab, startPos, Quaternion.identity);
            // 电球 Glow 按阵营染色（红=淡红 / 蓝=淡蓝）
            FxFactory.TintElectricGlow(ball, FxFactory.FactionElectricColor(state != null && state.teamType == "defender" ? "Red" : "Blue"));
            // 兜底自毁：协程靠 MoveTowards 结束才 Destroy，若被 Seek/塔销毁打断会残留循环 CFXR → 定时销毁兜底
            Destroy(ball, ELECTRIC_BALL_MAX_LIFE);
            // 充能阶段：电球在枪口由小变大（聚电），暂停冻结
            ball.transform.localScale = Vector3.one * ELECTRIC_BALL_CHARGE_MIN;
            float chargeT = 0f;
            while (chargeT < ELECTRIC_BALL_CHARGE_DUR)
            {
                if (!FxFactory.IsPaused())
                {
                    chargeT += Time.deltaTime;
                    float k = Mathf.Clamp01(chargeT / ELECTRIC_BALL_CHARGE_DUR);
                    ball.transform.localScale = Vector3.one * Mathf.Lerp(ELECTRIC_BALL_CHARGE_MIN, ELECTRIC_BALL_SCALE, k);
                }
                yield return null;
            }
            ball.transform.localScale = Vector3.one * ELECTRIC_BALL_SCALE;
        }

        if (ball != null)
        {
            Vector3 from = ball.transform.position;
            float dist = Vector3.Distance(from, targetPos);
            float dur = Mathf.Clamp(dist / ELECTRIC_BALL_SPEED, 0.2f, 0.8f);
            float t = 0f;
            while (t < dur)
            {
                if (!FxFactory.IsPaused())   // 回放暂停时电球冻结
                {
                    t += Time.deltaTime;
                    ball.transform.position = Vector3.Lerp(from, targetPos, Mathf.Clamp01(t / dur));
                }
                yield return null;
            }
            ball.transform.position = targetPos;
            if (_towerVisual != null) _towerVisual.HitAt(targetPos);
            Destroy(ball);
        }
        else
        {
            // 电球 prefab 缺失回退：直接出命中效果
            if (_towerVisual != null) _towerVisual.HitAt(targetPos);
        }
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
