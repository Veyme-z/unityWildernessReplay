// UnitView 的距离 LOD 子模块（Partial Class）
// 职责：远处野兽静态化（烘焙网格 + GPU 实例化）、瞬态动画窗口、静态待机浮动、LOD 调参
// 字段声明与主流程见 UnitView.cs

using System.Collections.Generic;
using UnityEngine;

public partial class UnitView
{
    /// <summary>距离 LOD 调参（public static，运行时可用 execute_code / 调试工具直接改值，无需重编译）：
    /// LOD_RANGE=相机 XZ 距离阈值，调大→更多野兽动画(CPU↑)，调小→更少；受相机位置影响大，建议保持 30。
    /// LodTransientCooldown=远处野兽攻击瞬态冷却秒数，调小→攻击动作更频繁(CPU↑)，调大→更稀疏。
    /// LodTransientWindow=每次攻击瞬态动画持续秒数，调大→动作更完整(并发↑)。
    /// LodIdleBobAmplitude/LodIdleSwayAmplitude=静态待机浮动上下幅度/缩放幅度，纯视觉、CPU≈0。</summary>
    public static float LOD_RANGE = 30f;
    public static float LodTransientCooldown = 2.5f;
    public static float LodTransientWindow = 1f;
    public static float LodIdleBobAmplitude = 0.03f;
    public static float LodIdleSwayAmplitude = 0.012f;
    static readonly Dictionary<int, Mesh> s_lodMeshCache = new Dictionary<int, Mesh>(); // 每类型共享一份烘焙网格
    static Camera s_camera;         // 复用 Camera.main 缓存

    /// <summary>每帧野兽距离 LOD 切换（LateUpdate 子模块：判定距离 + 切换静态/动画 + 待机浮动）。</summary>
    void UpdateLod()
    {
        // BOSS(14) 数量稀少（全局仅 ~120 条 spawn、同时在场个位数），豁免距离 LOD：
        // 动画始终独立播放（不被静态化冻结、不受瞬态冷却限制），多几副骨骼的开销可忽略。
        if (state != null && state.type == 14) return;

        // SciFi 模块化角色为多蒙皮模型，LOD 静态化只能烘焙单个部件会导致显示不全（缺头/缺武器）、腿不动，
        // 因此一律跳过距离 LOD，始终播放完整动画。
        if (_sciFiVisual) return;

        if (_skinned != null && _animator != null)
        {
            if (s_camera == null) s_camera = Camera.main;
            if (s_camera != null)
            {
                // 用相机 XZ 水平距离（相机固定高度不参与，平移/缩放时响应自然）
                Vector3 camPos = s_camera.transform.position;
                Vector3 delta = new Vector3(camPos.x - transform.position.x, 0f, camPos.z - transform.position.z);
                float d2 = delta.sqrMagnitude;
                // 滞回区间：静态化用 LOD_RANGE，恢复动画用 0.85*LOD_RANGE，避免边界来回切换闪烁
                bool far = _lodStatic
                    ? d2 >= LOD_RANGE * 0.85f * LOD_RANGE * 0.85f
                    : d2 >= LOD_RANGE * LOD_RANGE;
                // 攻击/死亡瞬态窗口内保持动画（远处野兽攻击时也能看到动作，窗口结束自动回静态）
                if (far && Time.time < _transientAnimUntil) far = false;
                if (far != _lodStatic) SetLodStatic(far);

                // 远处静态机器人轻微待机浮动：呼吸式上下浮动 + 缩放摆动，避免死板雕像。
                // 每只相位按 id 错开，视觉更自然；暂停时冻结。成本 ≈ 每只 2 次 Sin，可忽略。
                if (_lodStatic && _lodGo != null)
                {
                    bool replayPlaying = _player == null || _player.playing;
                    if (replayPlaying)
                    {
                        float ph = (float)(state.id % 997) * 0.618f;   // 每只错开相位
                        float t = Time.time % 100f;                    // 包裹避免大数精度问题
                        float bob = Mathf.Sin(t * 2.4f + ph) * LodIdleBobAmplitude;
                        _lodGo.transform.localPosition = new Vector3(0f, bob, 0f);
                        float s = 1f + Mathf.Sin(t * 1.8f + ph * 1.3f) * LodIdleSwayAmplitude;
                        _lodGo.transform.localScale = new Vector3(_lodBaseScale.x * s, _lodBaseScale.y * s, _lodBaseScale.z * s);
                    }
                    else
                    {
                        _lodGo.transform.localPosition = Vector3.zero;
                        _lodGo.transform.localScale = _lodBaseScale;
                    }
                }
            }
        }
    }

    /// <summary>野兽距离 LOD 切换：静态态 = 禁用 Animator + 蒙皮，改渲共享烘焙网格（GPU 实例化）。</summary>
    void SetLodStatic(bool toStatic)
    {
        _lodStatic = toStatic;
        if (toStatic)
        {
            // 共享材质开启实例化（幂等；蒙皮渲染器不受影响，仍正常渲染）
            var mat = _skinned.sharedMaterial;
            if (mat != null) mat.enableInstancing = true;

            // 共享网格：每野兽类型只烘焙一次（姿势取第一只进入远处状态的当时的姿势）
            Mesh sharedMesh;
            if (!s_lodMeshCache.TryGetValue(state.type, out sharedMesh) || sharedMesh == null)
            {
                sharedMesh = new Mesh();
                _skinned.BakeMesh(sharedMesh);
                s_lodMeshCache[state.type] = sharedMesh;
            }

            if (_lodGo == null)
            {
                _lodGo = new GameObject("LodMesh");
                // 挂在 Robot 同一 transform 下、零偏移
                _lodGo.transform.SetParent(_skinned.transform, false);
                var mf = _lodGo.AddComponent<MeshFilter>();
                mf.sharedMesh = sharedMesh;
                var mr = _lodGo.AddComponent<MeshRenderer>();
                mr.sharedMaterials = _skinned.sharedMaterials;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            // BakeMesh 烘焙在「除以渲染器 lossyScale」的世界比例空间：必须把 LOD 渲染器 lossyScale 补偿回 1，
            // 否则在野兽根节点缩放(0.4)下会渲染得比骨骼版小 1/0.4≈2.5 倍（机器人变小的 bug）。
            // 注意：不能除以 state.animScale —— 野兽的 "Body" 节点是空节点、不在 Robot 变换链里，
            // animScale(出生缩放 0→1) 不影响 Robot.lossyScale；若在出生瞬间转静态会被过度补偿成极小网格（远处隐形的 bug）。
            var lossy = _skinned.transform.lossyScale;
            _lodGo.transform.localScale = new Vector3(
                lossy.x > 0.0001f ? 1f / lossy.x : 1f,
                lossy.y > 0.0001f ? 1f / lossy.y : 1f,
                lossy.z > 0.0001f ? 1f / lossy.z : 1f);
            _lodBaseScale = _lodGo.transform.localScale;
            _lodGo.SetActive(true);
            _skinned.enabled = false;
            _animator.enabled = false;
        }
        else
        {
            _skinned.enabled = true;
            _animator.enabled = true;
            if (_lodGo != null) _lodGo.SetActive(false);
        }
    }
}
