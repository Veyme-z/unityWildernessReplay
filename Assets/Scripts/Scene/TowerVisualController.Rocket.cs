// TowerVisualController 火箭塔（roleType 32 → Rocket）发射逻辑（Partial Class）
// 职责：按攻击落点数（== 火箭等级，升级后可一次打多个目标）同时发射多枚原生导弹。
//       发射口自动收集（名字 Rocket{序号}_LOC；1/2/3 级模型分别 2/4/6 个），每发射口取一枚导弹，
//       发射瞬间脱离旋转炮塔（reparent 到静态包装根）各自朝自己的落点直线飞行；到达即在各自落点爆炸，
//       全部到位后震屏并归位（还原父节点/scale/位置）。逐帧由 Aim 的 LateUpdate 调度。
using System.Collections.Generic;
using UnityEngine;

public partial class TowerVisualController : MonoBehaviour
{
    // 火箭塔：火箭远距速度上限（米/秒）、最短可见飞行时长与飞行兜底上限。
    // 任何攻击的飞行时长 = clamp(距离/速度, ROCKET_MIN_TIME, ROCKET_MAX_TIME)：
    //   近距目标被强制降速保证轨迹看得见；远距保持 25m/s 自然速度。嫌近距太慢可调小 MIN_TIME，嫌看不见可调大。
    const float ROCKET_SPEED = 25f;
    const float ROCKET_MIN_TIME = 0.6f;  // 距目标再近也至少飞这么久（否则一晃就到，尾焰来不及显现）
    const float ROCKET_MAX_TIME = 3f;
    const float ROCKET_FLIGHT_SCALE = 1f; // 飞行途中放大导弹本体（不画线条也能看清"一枚火箭在飞"），归位还原

    /// <summary>单个发射槽：其内原生导弹 + 尾焰粒子。</summary>
    class RocketSlot
    {
        public Transform missile;             // 槽内导弹（会被发射出去的那枚）
        public List<ParticleSystem> trails;   // 该导弹/发射口尾焰粒子（发射时播放）
    }

    /// <summary>一枚正在飞行中的导弹。</summary>
    class RocketFlight
    {
        public Transform missile;
        public Transform parent;              // 发射前导弹父节点（归位时还原）
        public Vector3 localScale;            // 发射前 localScale（防炮塔 1.5x 放大导致逐次变大）
        public Quaternion localRot;           // 发射前 localRotation（归位时还原）
        public Vector3 target;                // 该枚导弹的落点（世界坐标）
        public float speed;                   // 该枚速度：远距用 ROCKET_SPEED；近距降速以满足最短飞行时长
        public List<ParticleSystem> trails;
    }

    // 火箭运行态
    readonly List<RocketSlot> _rocketSlots = new List<RocketSlot>();     // 所有可用发射槽（Setup 时按等级模型收集）
    readonly List<RocketFlight> _rocketFlights = new List<RocketFlight>(); // 当前在飞导弹（各打各的落点）
    bool _rocketFlying;
    float _rocketT;

    /// <summary>初始化火箭（Setup 调用）：按等级模型收集发射口/导弹/尾焰并待机停喷。</summary>
    void InitRocketFx()
    {
        _rocketSlots.Clear();

        // 发射口：优先全模型自动扫描（Rocket{序号}_LOC，1/2/3 级塔分别 2/4/6 个发射口）。
        // Inspector 的 rocketLaunchers 只固定配了 2 个，且会与扫描结果重复（同一导弹被收集两次 → 齐射时 2 枚飞同一个导弹）。
        // 找不到（模型命名不同）时才回退用 Inspector 配置。
        var locs = new List<Transform>();
        CollectRocketLocs(transform, locs);
        if (locs.Count == 0 && rocketLaunchers != null)
            foreach (var loc in rocketLaunchers)
                if (loc != null) locs.Add(loc);
        if (locs.Count == 0) return;
        locs.Sort(RocketLocCompare);

        // 每个导弹只保留一个发射槽（去重），避免同一枚导弹被分配给多个落点
        var taken = new HashSet<Transform>();
        foreach (var loc in locs)
        {
            if (loc == null) continue;
            var missile = loc.Find("Missile");
            if (missile == null) missile = FindChild(loc, "Missile");
            if (missile == null || !taken.Add(missile)) continue;

            var trails = new List<ParticleSystem>();
            foreach (var ps in loc.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null) continue;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                trails.Add(ps);
            }
            _rocketSlots.Add(new RocketSlot { missile = missile, trails = trails });
        }
    }

    /// <summary>递归收集名字形如 Rocket{序号}_LOC 的发射口节点（覆盖 2/4/6 个发射口的各等级模型）。</summary>
    void CollectRocketLocs(Transform root, List<Transform> outLocs)
    {
        foreach (Transform child in root)
        {
            if (child == null) continue;
            if (IsRocketLoc(child.name)) outLocs.Add(child);
            CollectRocketLocs(child, outLocs);
        }
    }

    /// <summary>发射口名字判定：Rocket + 数字 + _LOC（如 Rocket1_LOC）。</summary>
    static bool IsRocketLoc(string n)
    {
        if (string.IsNullOrEmpty(n)) return false;
        int underscore = n.IndexOf("_LOC", System.StringComparison.Ordinal);
        if (underscore <= 6) return false;                    // 至少要有 "Rocket" + 1 位数字
        if (!n.StartsWith("Rocket", System.StringComparison.OrdinalIgnoreCase)) return false;
        for (int k = 6; k < underscore; k++)
            if (!char.IsDigit(n[k])) return false;
        return underscore > 6;                                 // 中间确实夹了数字
    }

    /// <summary>发射口排序：按名字中的序号（Rocket1_LOC &lt; Rocket2_LOC …）。</summary>
    static int RocketLocCompare(Transform a, Transform b)
    {
        int ia = RocketLocIndex(a != null ? a.name : "");
        int ib = RocketLocIndex(b != null ? b.name : "");
        return ia.CompareTo(ib);
    }

    static int RocketLocIndex(string n)
    {
        int us = n.IndexOf("_LOC", System.StringComparison.Ordinal);
        if (us < 0) return int.MaxValue;
        int idx = 0;
        for (int k = 6; k < us; k++)
        {
            if (n[k] < '0' || n[k] > '9') return int.MaxValue;
            idx = idx * 10 + (n[k] - '0');
        }
        return idx == 0 ? int.MaxValue : idx;
    }

    /// <summary>
    /// 齐射：按落点数组发射多枚导弹（落点数 = 升级后同时攻击的目标数），每枚导弹朝各自的落点直线飞行（尾焰粒子播放）。
    /// 导弹默认挂在炮塔(Horizontal)下的发射口里，发射瞬间 reparent 到静态包装根（记录原父/scale），避免炮塔转向把导弹拖出直线（拐弯 bug）。
    /// </summary>
    void LaunchRockets(Vector3[] targets)
    {
        if (targets == null || targets.Length == 0) return;

        // 上一波导弹未落完又开火（连续攻击）：先把它们归位，避免新旧混飞
        if (_rocketFlights.Count > 0) ResetRocketMissiles();

        // 视觉缺失（没有可飞行的导弹）兜底：直接在各落点爆炸，保证表现不丢
        if (_rocketSlots.Count == 0)
        {
            foreach (var t in targets) FxFactory.PlayBombEffect(t);
            if (CameraManager.Instance != null)
                CameraManager.Instance.CameraShake(0.4f, 0.25f);
            return;
        }

        _rocketT = 0f;
        int take = Mathf.Min(targets.Length, _rocketSlots.Count);
        for (int i = 0; i < take; i++)
        {
            var slot = _rocketSlots[i];
            if (slot == null || slot.missile == null) continue;
            var dist = Vector3.Distance(slot.missile.position, targets[i]);
            // 飞行时长 = clamp(按 25m/s 的自然时长, 最短可见时长, 超时上限)；speed = 距离 / 时长。
            // ⚠️ 不能用 max(25, dist/min)：那只有 dist>75m 才降速，近距导弹依旧一晃就到（轨迹看不见）。
            float natural = dist / ROCKET_SPEED;
            float flightTime = Mathf.Clamp(natural, ROCKET_MIN_TIME, ROCKET_MAX_TIME);
            var f = new RocketFlight
            {
                missile = slot.missile,
                parent = slot.missile.parent,
                localScale = slot.missile.localScale,
                localRot = slot.missile.localRotation,
                target = targets[i],
                speed = dist / Mathf.Max(0.001f, flightTime),
                trails = slot.trails
            };
            slot.missile.SetParent(transform, true);   // 脱离旋转炮塔，世界坐标不变
            slot.missile.localScale = f.localScale * ROCKET_FLIGHT_SCALE;  // 飞行中放大，归位时按 f.localScale 还原
            // 每枚导弹转向自己的落点（nose-forward 对准目标）：
            // 若不转向，导弹仍保持炮塔朝主目标的姿态飞行，偏离方向的那几枚尾焰轨迹会很淡（只一枚明显）
            Vector3 dir = targets[i] - slot.missile.position;
            if (dir.sqrMagnitude > 0.0001f)
                slot.missile.rotation = AimForward(dir);
            foreach (var ps in f.trails) if (ps != null) ps.Play();
            _rocketFlights.Add(f);
        }
        // 落点多于发射槽（极端情况，正常不会出现）：多出的落点即时爆炸，不漏表现
        for (int i = take; i < targets.Length; i++)
            FxFactory.PlayBombEffect(targets[i]);

        _rocketFlying = _rocketFlights.Count > 0;
    }

    /// <summary>逐帧：各导弹朝各自落点推进，到达即在落点爆炸；全部到位（或超时兜底）→ 震屏 → 停尾焰（Aim 的 LateUpdate 调度）。</summary>
    void UpdateRocketFx()
    {
        if (!_rocketFlying) return;
        _rocketT += Time.deltaTime;
        bool timedOut = _rocketT >= ROCKET_MAX_TIME;

        for (int i = _rocketFlights.Count - 1; i >= 0; i--)
        {
            var f = _rocketFlights[i];
            if (f == null || f.missile == null) { _rocketFlights.RemoveAt(i); continue; }
            float step = f.speed > 0f ? f.speed * Time.deltaTime : ROCKET_SPEED * Time.deltaTime;
            Vector3 to = f.target - f.missile.position;
            if (timedOut || to.sqrMagnitude <= step * step)
            {
                f.missile.position = f.target;
                FxFactory.PlayBombEffect(f.target);   // 该导弹到达落点 → 爆炸（ReplayPlayer 不再即时播放）
                ReturnRocketFlight(f);
                _rocketFlights.RemoveAt(i);
            }
            else
            {
                f.missile.position += to.normalized * step;
            }
        }

        if (_rocketFlights.Count == 0)
        {
            _rocketFlying = false;
            if (CameraManager.Instance != null)
                CameraManager.Instance.CameraShake(0.4f, 0.25f);   // 整波落点全部炸完 → 震屏
        }
    }

    /// <summary>单个导弹归位：reparent 回发射口并还原原始 localScale/rotation/位置、停尾焰。</summary>
    void ReturnRocketFlight(RocketFlight f)
    {
        if (f == null || f.missile == null) return;
        var parent = f.parent;
        if (parent != null && f.missile.parent != parent) f.missile.SetParent(parent, false);
        f.missile.localScale = f.localScale;         // 还原原始 scale，防炮塔放大让导弹逐次变大
        f.missile.localRotation = f.localRot;        // 还原原始姿态
        f.missile.localPosition = Vector3.zero;
        foreach (var ps in f.trails)
            if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    /// <summary>让导弹朝向落点方向（模型 +Z = 弹头朝向；避免导弹偏离自身轨迹时尾焰歪斜变淡）。</summary>
    static Quaternion AimForward(Vector3 dir)
    {
        if (Mathf.Abs(dir.y) > 0.999f)
            dir = new Vector3(0.001f, dir.y, 0.001f).normalized;
        return Quaternion.LookRotation(dir.normalized, Vector3.up);
    }

    /// <summary>火箭结束/中断（到达、Seek、连续齐射）后：取消全部在飞导弹、归位停尾焰。</summary>
    void ResetRocketMissiles()
    {
        for (int i = _rocketFlights.Count - 1; i >= 0; i--)
            ReturnRocketFlight(_rocketFlights[i]);
        _rocketFlights.Clear();
        _rocketFlying = false;
        _rocketT = 0f;
    }
}
