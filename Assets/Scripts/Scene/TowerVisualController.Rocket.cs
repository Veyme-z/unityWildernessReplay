// TowerVisualController 火箭塔（roleType 32 → Rocket）发射逻辑（Partial Class）
// 职责：发射素材包原生导弹，各导弹脱离旋转炮塔（reparent 到静态包装根）朝落点直线飞行，
//       全部到达（或超时）后在落点触发爆炸 + 震屏并归位（还原父节点/scale/位置）。逐帧由 Aim 的 LateUpdate 调度。
using System.Collections.Generic;
using UnityEngine;

public partial class TowerVisualController : MonoBehaviour
{
    // 火箭塔：火箭速度（米/秒）与飞行兜底上限
    const float ROCKET_SPEED = 25f;
    const float ROCKET_MAX_TIME = 2f;

    // 火箭运行态
    readonly List<Transform> _rocketMissiles = new List<Transform>();
    readonly List<ParticleSystem> _rocketTrails = new List<ParticleSystem>();
    readonly List<Transform> _rocketParents = new List<Transform>(); // 发射前导弹各自的父节点（归位时还原）
    readonly List<Vector3> _rocketScales = new List<Vector3>();     // 发射前导弹各自的 localScale（防炮塔 1.5x 放大导致逐次变大）
    bool _rocketFlying;
    float _rocketT;
    Vector3 _rocketTarget;

    /// <summary>初始化火箭（Setup 调用）：收集发射口导弹 + 尾焰粒子并待机停喷。</summary>
    void InitRocketFx()
    {
        _rocketMissiles.Clear();
        _rocketTrails.Clear();
        if (rocketLaunchers == null || rocketLaunchers.Length == 0) return;
        foreach (var loc in rocketLaunchers)
        {
            if (loc == null) continue;
            var missile = loc.Find("Missile");
            if (missile != null) _rocketMissiles.Add(missile);
            foreach (var ps in loc.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null) continue;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _rocketTrails.Add(ps);
            }
        }
    }

    /// <summary>
    /// 发射原生火箭：各导弹朝落点坐标直线飞行（尾焰粒子播放）。导弹默认挂在炮塔(Horizontal/Vertical)下，
    /// 发射瞬间 reparent 到静态包装根（记录原父/scale），避免炮塔转向把导弹拖出直线（拐弯 bug）。
    /// </summary>
    void LaunchRockets(Vector3 targetWorldPos)
    {
        if (rocketLaunchers == null || rocketLaunchers.Length == 0) return;
        _rocketTarget = targetWorldPos;
        _rocketT = 0f;
        _rocketFlying = true;
        _rocketParents.Clear();
        _rocketScales.Clear();
        for (int i = 0; i < _rocketMissiles.Count; i++)
        {
            var m = _rocketMissiles[i];
            if (m == null) continue;
            _rocketParents.Add(m.parent);
            _rocketScales.Add(m.localScale);
            m.SetParent(transform, true);   // 脱离旋转炮塔，世界坐标不变
        }
        foreach (var ps in _rocketTrails)
            if (ps != null) ps.Play();
    }

    /// <summary>火箭结束（到达/中断）后：导弹 reparent 回原发射口并还原原始 localScale/位置、停尾焰。</summary>
    void ResetRocketMissiles()
    {
        int n = Mathf.Min(_rocketMissiles.Count, Mathf.Min(_rocketParents.Count, _rocketScales.Count));
        for (int i = 0; i < n; i++)
        {
            var m = _rocketMissiles[i];
            if (m == null) continue;
            var parent = _rocketParents[i];
            if (m.parent != parent) m.SetParent(parent, false);
            m.localScale = _rocketScales[i];   // 还原原始 scale，防炮塔放大让导弹逐次变大
            m.localPosition = Vector3.zero;
        }
        _rocketParents.Clear();
        _rocketScales.Clear();
        foreach (var ps in _rocketTrails)
            if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    /// <summary>逐帧：各导弹朝落点推进，全部到达（或超时兜底）→ 落点爆炸 + 震屏 → 归位停尾焰（Aim 的 LateUpdate 调度）。</summary>
    void UpdateRocketFx()
    {
        if (!_rocketFlying) return;
        _rocketT += Time.deltaTime;
        float step = ROCKET_SPEED * Time.deltaTime;
        bool allArrived = true;
        for (int i = 0; i < _rocketMissiles.Count; i++)
        {
            var m = _rocketMissiles[i];
            if (m == null) continue;
            Vector3 to = _rocketTarget - m.position;
            if (to.sqrMagnitude <= step * step) m.position = _rocketTarget;
            else { m.position += to.normalized * step; allArrived = false; }
        }
        if (allArrived || _rocketT >= ROCKET_MAX_TIME)
        {
            _rocketFlying = false;
            FxFactory.PlayBombEffect(_rocketTarget);   // 火箭到达落点 → 爆炸（ReplayPlayer 不再即时播放）
            if (CameraManager.Instance != null)
                CameraManager.Instance.CameraShake(0.4f, 0.25f);
            ResetRocketMissiles();
        }
    }
}
