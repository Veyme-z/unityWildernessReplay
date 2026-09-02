// TowerVisualController 攻击目标可视化（Partial Class）
// 职责：枪口到目标的弹道线（Tracer）、目标命中圆环、加特林命中火花。多目标（加特林 N 弹道）用对象池复用，
//       避免每回合 new；逐帧淡出/命中环三阶段由 Aim 的 LateUpdate 调度；OnDestroy 清理根级对象（不随塔节点销毁）。
using System.Collections.Generic;
using UnityEngine;

public partial class TowerVisualController : MonoBehaviour
{
    // 命中圆环阶段：快速淡入 + 保持较亮（剩余时间用于扩大淡出）
    const float HIT_FADE_IN = 0.05f;
    const float HIT_HOLD = 0.10f;

    // 攻击目标可视化运行态（Tracer + 命中闪光），池复用避免 GC
    Color _tracerColor;
    readonly List<LineRenderer> _tracerPool = new List<LineRenderer>();
    readonly List<TracerFx> _activeTracers = new List<TracerFx>();
    readonly List<Transform> _hitRingPool = new List<Transform>();
    readonly List<HitRingFx> _activeHitRings = new List<HitRingFx>();

    class TracerFx
    {
        public LineRenderer lr;
        public float t;      // 剩余（1→0）
        public float dur;
        public Color color;
    }
    class HitRingFx
    {
        public Transform tr;
        public MeshRenderer rend;
        public float t;      // 经过时间（0→hitRingDuration）
    }

    /// <summary>目标命中效果（命中环 + CFXR 电流电击），飞行弹体到达目标时调用。电流按阵营染色。</summary>
    public void HitAt(Vector3 targetWorldPos)
    {
        if (!_setup) return;
        SpawnHitRing(targetWorldPos);
        FxFactory.PlayElectricHit(targetWorldPos, FxFactory.FactionElectricColor(_faction));
    }

    void GetTracerStyle(out Color c, out float sw, out float ew, out float dur)
    {
        c = FactionColor();
        if (_towerType == "RPG" || _towerType == "Laser")
        {
            // 电磁狙击炮/激光塔：单发穿透激光——粗、亮、持续时间长（能量 = 25×等级，等级越高略粗）
            float lvl = _view != null && _view.state != null ? Mathf.Clamp(_view.state.level, 1, 5) : 1f;
            sw = 0.18f + 0.02f * lvl;
            ew = 0.10f + 0.02f * lvl;
            dur = 0.6f;
        }
        else
        {
            // 加特林（Minigun）：较粗、快弹道，每发明确指向一个被攻击机器人
            sw = 0.14f;
            ew = 0.04f;
            dur = 0.22f;
        }
    }

    /// <summary>阵营特效颜色：防守方红 / 进攻方蓝（与 TeamColorApplicator 霓虹色一致）。</summary>
    Color FactionColor()
    {
        return _faction == "Blue" ? new Color(0f, 0.478f, 1f) : new Color(1f, 0.176f, 0.333f);
    }

    void SpawnTracer(Vector3 targetWorldPos)
    {
        float sw, ew, dur;
        GetTracerStyle(out _tracerColor, out sw, out ew, out dur);

        LineRenderer lr;
        if (_tracerPool.Count > 0)
        {
            lr = _tracerPool[0];
            _tracerPool.RemoveAt(0);
            lr.sharedMaterial = MatLib.Get(_tracerColor);
        }
        else
        {
            var go = new GameObject("TowerTracer");
            lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.sharedMaterial = MatLib.Get(_tracerColor);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
        }

        Vector3 from = MuzzleWorldPosition();
        Vector3 to = targetWorldPos + Vector3.up * 0.35f;
        lr.SetPosition(0, from);
        lr.SetPosition(1, to);
        lr.startWidth = sw;
        lr.endWidth = ew;
        lr.startColor = _tracerColor;
        lr.endColor = new Color(_tracerColor.r, _tracerColor.g, _tracerColor.b, 0.25f);
        lr.gameObject.SetActive(true);
        _activeTracers.Add(new TracerFx { lr = lr, t = 1f, dur = dur, color = _tracerColor });
    }

    void ClearTracer()
    {
        for (int i = _activeTracers.Count - 1; i >= 0; i--)
        {
            var fx = _activeTracers[i];
            fx.lr.gameObject.SetActive(false);
            _tracerPool.Add(fx.lr);
            _activeTracers.RemoveAt(i);
        }
    }

    /// <summary>加特林命中火花：在攻击到的机器人位置播素材包原生 Hit 粒子（已复制到 Resources/FX/Hit）。</summary>
    void SpawnGatlingHit(Vector3 worldPos)
    {
        var prefab = Resources.Load<GameObject>("FX/Hit");
        if (prefab == null) return;
        var go = Object.Instantiate(prefab, worldPos + Vector3.up * 0.2f, Quaternion.identity);
        go.transform.localScale = Vector3.one * 0.6f;
        Object.Destroy(go, 0.7f);
    }

    void SpawnHitRing(Vector3 targetWorldPos)
    {
        Transform rt;
        MeshRenderer rend;
        if (_hitRingPool.Count > 0)
        {
            rt = _hitRingPool[0];
            _hitRingPool.RemoveAt(0);
            rend = rt.GetComponent<MeshRenderer>();
            rt.gameObject.SetActive(true);
        }
        else
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "TowerHitRing";
            rend = go.GetComponent<MeshRenderer>();
            // 独立材质实例：烘焙成色的圆环贴图，不污染 MatLib 共享材质池
            var mat = new Material(MatLib.Shader2D);
            mat.mainTexture = MatLib.CreateRingTex(_tracerColor, 64);
            mat.color = Color.white;
            rend.sharedMaterial = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            rt = go.transform;
        }

        rt.position = targetWorldPos + Vector3.up * 0.05f;
        rt.rotation = Quaternion.Euler(90f, 0f, 0f);
        rt.localScale = new Vector3(0.25f, 0.25f, 1f);
        _activeHitRings.Add(new HitRingFx { tr = rt, rend = rend, t = 0f });
    }

    void ClearHitRing()
    {
        for (int i = _activeHitRings.Count - 1; i >= 0; i--)
        {
            var fx = _activeHitRings[i];
            fx.tr.gameObject.SetActive(false);
            _hitRingPool.Add(fx.tr);
            _activeHitRings.RemoveAt(i);
        }
    }

    /// <summary>逐帧：弹道线淡出，各弹道独立计时（Aim 的 LateUpdate 调度）。</summary>
    void UpdateTracersFx()
    {
        for (int i = _activeTracers.Count - 1; i >= 0; i--)
        {
            var fx = _activeTracers[i];
            fx.t -= Time.deltaTime / fx.dur;
            float a = Mathf.Clamp01(fx.t);
            fx.lr.startColor = new Color(fx.color.r, fx.color.g, fx.color.b, a);
            fx.lr.endColor = new Color(fx.color.r, fx.color.g, fx.color.b, a * 0.25f);
            if (fx.t <= 0f)
            {
                fx.lr.gameObject.SetActive(false);
                _tracerPool.Add(fx.lr);
                _activeTracers.RemoveAt(i);
            }
        }
    }

    /// <summary>逐帧：命中圆环三阶段（快速淡入 → 保持较亮 → 扩大并平滑淡出），多落点各自推进（Aim 的 LateUpdate 调度）。</summary>
    void UpdateHitRingsFx()
    {
        for (int i = _activeHitRings.Count - 1; i >= 0; i--)
        {
            var fx = _activeHitRings[i];
            fx.t += Time.deltaTime;
            float holdEnd = HIT_FADE_IN + HIT_HOLD;
            float expandDur = Mathf.Max(0.01f, hitRingDuration - holdEnd);
            float alpha, scale;
            if (fx.t < HIT_FADE_IN)
            {
                float p = fx.t / HIT_FADE_IN;
                alpha = p;
                scale = 0.25f;
            }
            else if (fx.t < holdEnd)
            {
                float p = (fx.t - HIT_FADE_IN) / HIT_HOLD;
                alpha = 1f;
                scale = Mathf.Lerp(0.25f, 0.4f, Smooth01(p));
            }
            else
            {
                float p = Mathf.Clamp01((fx.t - holdEnd) / expandDur);
                alpha = 1f - Smooth01(p);
                scale = Mathf.Lerp(0.4f, 0.7f, Smooth01(p));
            }
            fx.tr.localScale = new Vector3(scale, scale, 1f);
            if (fx.rend != null) fx.rend.sharedMaterial.color = new Color(1f, 1f, 1f, alpha);
            if (fx.t >= hitRingDuration)
            {
                fx.tr.gameObject.SetActive(false);
                _hitRingPool.Add(fx.tr);
                _activeHitRings.RemoveAt(i);
            }
        }
    }

    /// <summary>塔被销毁时清理 Tracer/命中闪光等根级对象（它们不随塔节点销毁）。</summary>
    void OnDestroy()
    {
        for (int i = _activeTracers.Count - 1; i >= 0; i--)
        {
            if (_activeTracers[i].lr != null) Object.Destroy(_activeTracers[i].lr.gameObject);
            _activeTracers.RemoveAt(i);
        }
        foreach (var lr in _tracerPool) if (lr != null) Object.Destroy(lr.gameObject);
        _tracerPool.Clear();
        for (int i = _activeHitRings.Count - 1; i >= 0; i--)
        {
            if (_activeHitRings[i].tr != null) Object.Destroy(_activeHitRings[i].tr.gameObject);
            _activeHitRings.RemoveAt(i);
        }
        foreach (var rt in _hitRingPool) if (rt != null) Object.Destroy(rt.gameObject);
        _hitRingPool.Clear();
        if (_flashLight != null) Object.Destroy(_flashLight.gameObject);
    }
}
