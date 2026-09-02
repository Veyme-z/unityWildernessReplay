// TowerVisualController 激光塔（roleType 31 → Laser）光束逻辑（Partial Class）
// 职责：收集模型里所有 LaserBeam* 光束节点（等级越高光束越多：Laser_1=1 束 / Laser_2=2 束 / Laser_3=3 束），
//       待机隐藏、攻击时全部延伸到落点并播放粒子、按计时自动隐藏；逐帧更新方法由 Aim 的 LateUpdate 调度。
using System.Collections.Generic;
using UnityEngine;

public partial class TowerVisualController : MonoBehaviour
{
    // 激光塔：攻击时激光束显示时长（秒）
    const float LASER_SHOW_DURATION = 0.8f;

    // 激光光束运行态
    readonly List<TowerBeam> _laserBeams = new List<TowerBeam>();
    Vector3 _laserTarget;
    float _laserActiveT;

    /// <summary>单束激光：根节点 + 其 LineRenderer（光束线）+ End 节点（落点粒子/光）+ 全部粒子。</summary>
    class TowerBeam
    {
        public Transform root;
        public LineRenderer line;
        public Transform end;
        public readonly List<ParticleSystem> particles = new List<ParticleSystem>();
    }

    /// <summary>初始化激光光束（Setup 调用）：收集所有 LaserBeam* 节点并待机隐藏。</summary>
    void InitLaserFx()
    {
        _laserBeams.Clear();
        CollectLaserBeams(transform);
        foreach (var b in _laserBeams)
            if (b.root != null) b.root.gameObject.SetActive(false);
    }

    /// <summary>递归收集模型里所有名字以 "LaserBeam" 开头的节点为激光束。</summary>
    void CollectLaserBeams(Transform parent)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child.name.StartsWith("LaserBeam"))
            {
                var b = new TowerBeam
                {
                    root = child,
                    line = child.GetComponentInChildren<LineRenderer>(true),
                    end = FindChild(child, "End"),
                };
                b.particles.AddRange(child.GetComponentsInChildren<ParticleSystem>(true));
                _laserBeams.Add(b);
            }
            CollectLaserBeams(child);   // 递归
        }
    }

    /// <summary>显示全部激光束，光束逐帧延伸到目标落点，播放其粒子，按 LASER_SHOW_DURATION 自动隐藏。</summary>
    void ShowLaserBeam(Vector3 targetWorldPos)
    {
        if (_laserBeams.Count == 0) return;
        _laserTarget = targetWorldPos;
        foreach (var b in _laserBeams)
        {
            if (b.root == null) continue;
            b.root.gameObject.SetActive(true);
            foreach (var ps in b.particles)
                if (ps != null) ps.Play();
        }
        UpdateLaserBeam();
        _laserActiveT = LASER_SHOW_DURATION;
    }

    /// <summary>把每束激光末端（LineRenderer 终点 + End 节点）对准当前目标，随炮塔转向始终指向落点。</summary>
    void UpdateLaserBeam()
    {
        for (int i = 0; i < _laserBeams.Count; i++)
        {
            var b = _laserBeams[i];
            if (b.root == null) continue;
            if (b.line != null)
                b.line.SetPosition(1, b.line.transform.InverseTransformPoint(_laserTarget));
            if (b.end != null)
                b.end.localPosition = b.end.parent.InverseTransformPoint(_laserTarget);
        }
    }

    /// <summary>隐藏全部激光束并停止其粒子。</summary>
    void HideLaserBeam()
    {
        for (int i = 0; i < _laserBeams.Count; i++)
        {
            var b = _laserBeams[i];
            if (b.root != null) b.root.gameObject.SetActive(false);
        }
        _laserActiveT = 0f;
    }

    /// <summary>逐帧：激光束对准落点 + 显示计时到期隐藏（Aim 的 LateUpdate 在非暂停时调用）。</summary>
    void UpdateLaserFx()
    {
        if (_laserActiveT <= 0f) return;
        UpdateLaserBeam();
        _laserActiveT -= Time.deltaTime;
        if (_laserActiveT <= 0f) HideLaserBeam();
    }
}
