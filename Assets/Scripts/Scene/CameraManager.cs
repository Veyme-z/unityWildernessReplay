using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 电影级单镜头导播系统（Option C）。
/// Manual：玩家控制 1/2/3 机位。
/// Auto：news 事件驱动推拉变焦 + 景深 + 震屏。
/// 玩家操作始终优先——任意机位键立即切回 Manual。
/// </summary>
public class CameraManager : MonoBehaviour
{
    public enum CameraSpectatorMode { Manual, Auto }

    // ══════════════════════════════════════
    // 公开状态
    // ══════════════════════════════════════
    public CameraSpectatorMode currentMode = CameraSpectatorMode.Manual;
    public bool IsAuto => currentMode == CameraSpectatorMode.Auto;

    [Header("主相机")]
    public Camera mainCam;
    public ReplayCameraRig mainRig;

    // ══════════════════════════════════════
    // 三通道独立阻尼
    // ══════════════════════════════════════
    [Header("阻尼时间（秒）")]
    public float zoomSmoothTime = 0.45f;
    public float positionSmoothTime = 0.65f;
    public float rotationSmoothTime = 0.4f;

    // ══════════════════════════════════════
    // 电影镜头参数
    // ══════════════════════════════════════
    [Header("电影镜头")]
    [Tooltip("特写近景高度（米）")]
    public float closeUpHeight = 4f;
    [Tooltip("全局远景高度（米）")]
    public float defaultHeight = 9f;
    [Tooltip("特写俯角（度）")]
    [Range(30f, 90f)] public float closeUpPitch = 30f;
    [Tooltip("远景俯角（度）")]
    [Range(30f, 90f)] public float defaultPitch = 45f;
    [Tooltip("事件特写持续（秒）")]
    public float eventHoldDuration = 3f;
    [Tooltip("事件后延迟回全景（秒）")]
    public float returnDelay = 3f;
    [Tooltip("默认跟随红方开拓者 ID")]
    public long pioneerRedId = 10010;
    [Tooltip("默认跟随蓝方开拓者 ID")]
    public long pioneerBlueId = 20010;

    [Header("构图参数")]
    [Tooltip("相机围绕目标的偏航角。0=正南 45=东南对角线 90=正东")]
    [Range(0f, 360f)] public float defaultYaw = 45f;

    [Header("景深（需要 Post-Processing Layer）")]
    [Tooltip("特写时焦点距离")]
    public float focusDistanceCloseUp = 8f;
    [Tooltip("特写时背景模糊孔径")]
    [Range(0.1f, 32f)] public float apertureCloseUp = 8f;
    [Tooltip("远景时焦点距离")]
    public float focusDistanceDefault = 30f;
    [Tooltip("远景时孔径（小=全清）")]
    [Range(0.1f, 32f)] public float apertureDefault = 1f;

    // ══════════════════════════════════════
    // 目标状态
    // ══════════════════════════════════════
    float _targetHeight;
    Vector3 _targetPositionFlat;
    float _targetPitch;
    bool _eventActive;
    float _eventTimer;

    // ══════════════════════════════════════
    // SmoothDamp 速度缓存
    // ══════════════════════════════════════
    Vector3 _posVelocity;
    float _zoomVelocity;

    // ══════════════════════════════════════
    // 震动（二次方衰减）
    // ══════════════════════════════════════
    float _shakeStrength;
    float _shakeTimer;
    float _shakeDuration;
    Vector3 _shakeOffset;

    // ══════════════════════════════════════
    // 景深（运行时反射，无包也能编译）
    // ══════════════════════════════════════
    System.Object _dofSettings;       // DepthOfField 实例（反射持有）
    System.Object _dofVolume;         // PostProcessVolume 的 profile
    System.Reflection.PropertyInfo _dofFocusProp, _dofApertureProp, _dofEnabledProp;
    bool _hasDof;
    float _targetFocusDistance;
    float _targetAperture;
    float _focusDistanceVel;
    float _apertureVel;

    // ══════════════════════════════════════
    // 引用
    // ══════════════════════════════════════
    StateEngine _engine;
    ReplayPlayer _player;

    public static CameraManager Instance { get; private set; }

    // ══════════════════════════════════════
    // 生命周期
    // ══════════════════════════════════════
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        if (mainCam == null) mainCam = Camera.main;
        if (mainCam != null && mainRig == null) mainRig = mainCam.GetComponent<ReplayCameraRig>();

        _targetHeight = defaultHeight;
        _targetPitch = defaultPitch;
        _targetFocusDistance = focusDistanceDefault;
        _targetAperture = apertureDefault;

        InitDepthOfField();
    }

    public void Init(ReplayPlayer player)
    {
        _player = player;
        _engine = player?.engine;
    }

    // ══════════════════════════════════════
    // 景深初始化（反射，无需 PostProcessing 包也能编译）
    // ══════════════════════════════════════
    void InitDepthOfField()
    {
        _hasDof = false;
        if (mainCam == null) return;

        try
        {
            // 检测 PostProcessLayer
            var layerType = System.Type.GetType("UnityEngine.Rendering.PostProcessing.PostProcessLayer, Unity.Postprocessing.Runtime");
            if (layerType == null) return;
            var layer = mainCam.GetComponent(layerType);
            if (layer == null) return;

            // 找或建 PostProcessVolume
            var volumeType = System.Type.GetType("UnityEngine.Rendering.PostProcessing.PostProcessVolume, Unity.Postprocessing.Runtime");
            if (volumeType == null) return;
            var volume = (MonoBehaviour)FindObjectOfType(volumeType);
            if (volume == null)
            {
                volume = (MonoBehaviour)mainCam.gameObject.AddComponent(volumeType);
                var isGlobalProp = volumeType.GetProperty("isGlobal");
                if (isGlobalProp != null) isGlobalProp.SetValue(volume, true, null);
                var weightProp = volumeType.GetProperty("weight");
                if (weightProp != null) weightProp.SetValue(volume, 1f, null);
            }

            // 获取 profile
            var profileProp = volumeType.GetProperty("sharedProfile");
            if (profileProp == null) return;
            var profile = profileProp.GetValue(volume, null);

            var profileType = System.Type.GetType("UnityEngine.Rendering.PostProcessing.PostProcessProfile, Unity.Postprocessing.Runtime");
            if (profile == null && profileType != null)
            {
                profile = ScriptableObject.CreateInstance(profileType);
                profileProp.SetValue(volume, profile, null);
            }
            if (profile == null) return;

            // 获取 DepthOfField
            var dofType = System.Type.GetType("UnityEngine.Rendering.PostProcessing.DepthOfField, Unity.Postprocessing.Runtime");
            if (dofType == null) return;

            var tryGetMethod = profileType.GetMethod("TryGetSettings");
            if (tryGetMethod == null) return;
            var tryGetGeneric = tryGetMethod.MakeGenericMethod(dofType);
            var args = new object[] { null };
            bool found = (bool)tryGetGeneric.Invoke(profile, args);
            _dofSettings = args[0];

            if (!found)
            {
                var addMethod = profileType.GetMethod("AddSettings");
                if (addMethod == null) return;
                var addGeneric = addMethod.MakeGenericMethod(dofType);
                _dofSettings = addGeneric.Invoke(profile, null);
            }

            if (_dofSettings == null) return;

            _dofEnabledProp = dofType.GetProperty("enabled");
            _dofFocusProp = dofType.GetProperty("focusDistance");
            _dofApertureProp = dofType.GetProperty("aperture");

            if (_dofEnabledProp != null)
            {
                var enabledVal = _dofEnabledProp.GetValue(_dofSettings, null);
                var enabledType = enabledVal.GetType();
                var valueProp = enabledType.GetProperty("value");
                if (valueProp != null) valueProp.SetValue(enabledVal, true, null);
            }

            if (_dofFocusProp != null)
            {
                var fdVal = _dofFocusProp.GetValue(_dofSettings, null);
                var valueProp = fdVal.GetType().GetProperty("value");
                if (valueProp != null) valueProp.SetValue(fdVal, focusDistanceDefault, null);
            }

            if (_dofApertureProp != null)
            {
                var apVal = _dofApertureProp.GetValue(_dofSettings, null);
                var valueProp = apVal.GetType().GetProperty("value");
                if (valueProp != null) valueProp.SetValue(apVal, apertureDefault, null);
            }

            _hasDof = true;
            Debug.Log("[CameraManager] 景深初始化成功");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[CameraManager] 景深不可用（缺少 Post Processing 包）: " + e.Message);
            _hasDof = false;
        }
    }

    // ══════════════════════════════════════
    // 模式切换
    // ══════════════════════════════════════
    public void SetSpectatorMode(CameraSpectatorMode mode)
    {
        if (currentMode == mode) return;
        currentMode = mode;
        _eventActive = false;
        _shakeStrength = 0f;
        _shakeOffset = Vector3.zero;

        if (mode == CameraSpectatorMode.Manual && mainRig != null)
            mainRig.enableAutoLock = true;

        // 切 Mode 时重置景深到默认
        _targetFocusDistance = focusDistanceDefault;
        _targetAperture = apertureDefault;

        Debug.Log("[CameraManager] → " + mode);
    }

    public void SetManual() => SetSpectatorMode(CameraSpectatorMode.Manual);
    public void SetAuto() => SetSpectatorMode(CameraSpectatorMode.Auto);

    public void OnPlayerCameraInput()
    {
        if (currentMode == CameraSpectatorMode.Auto)
            SetSpectatorMode(CameraSpectatorMode.Manual);
    }

    // ══════════════════════════════════════
    // 新闻事件驱动
    // ══════════════════════════════════════
    public void OnNewRoundTick(List<ReplayNews> news, Dictionary<long, UnitState> units)
    {
        if (currentMode != CameraSpectatorMode.Auto) return;

        // ── 默认：跟随两个开拓者的中点 ──
        Vector3 pioneerPos = GetPioneerMidpoint(units);
        _targetPositionFlat = pioneerPos;
        _targetHeight = defaultHeight;
        _targetPitch = defaultPitch;
        _targetFocusDistance = focusDistanceDefault;
        _targetAperture = apertureDefault;

        // ── 扫描 news 看是否有高优事件 → 拉近特写 ──
        if (news == null) return;

        foreach (var n in news)
        {
            if (n == null || string.IsNullOrEmpty(n.text)) continue;
            string txt = n.text;

            bool isHigh = txt.Contains("袭击了基地")
                       || txt.Contains("被摧毁")
                       || txt.Contains("巨兽BOSS");

            if (!isHigh) continue;

            var pos = FindRelevantPosition(units, n.type);
            _targetPositionFlat = pos;
            _targetHeight = closeUpHeight;
            _targetPitch = closeUpPitch;
            _eventActive = true;
            _eventTimer = eventHoldDuration + returnDelay;

            if (txt.Contains("袭击了基地") || txt.Contains("被摧毁"))
                CameraShake(0.5f, 0.08f);  // 降低震动强度

            _targetFocusDistance = focusDistanceCloseUp;
            _targetAperture = apertureCloseUp;

            break;
        }
    }

    /// <summary>获取红蓝开拓者的中点位置。缺失时退回全体中心</summary>
    Vector3 GetPioneerMidpoint(Dictionary<long, UnitState> units)
    {
        if (units == null) return _targetPositionFlat;

        var r = FindUnitById(pioneerRedId);
        var b = FindUnitById(pioneerBlueId);

        if (r != null && !r.dead && !r.dying && b != null && !b.dead && !b.dying)
            return (r.pos + b.pos) * 0.5f;

        if (r != null && !r.dead && !r.dying) return r.pos;
        if (b != null && !b.dead && !b.dying) return b.pos;

        return FindRelevantPosition(units, "");
    }

    /// <summary>在 units 中查找相关位置。typeFilter 为空时取所有存活单位中心</summary>
    Vector3 FindRelevantPosition(Dictionary<long, UnitState> units, string typeFilter)
    {
        if (units == null) return _targetPositionFlat;

        Vector3 sum = Vector3.zero;
        int count = 0;

        foreach (var kv in units)
        {
            var u = kv.Value;
            if (u == null || u.dead || u.dying) continue;

            // 按队伍筛选或取全体
            if (!string.IsNullOrEmpty(typeFilter) && u.teamType != typeFilter) continue;

            sum += u.pos;
            count++;
        }

        return count > 0 ? sum / count : _targetPositionFlat;
    }

    // ══════════════════════════════════════
    // 震屏
    // ══════════════════════════════════════
    public void CameraShake(float duration, float strength = 0.12f)
    {
        if (duration <= 0f) return;
        _shakeTimer = duration;
        _shakeDuration = duration;
        _shakeStrength = Mathf.Max(_shakeStrength, strength);
    }

    void UpdateShake()
    {
        if (_shakeTimer <= 0f) { _shakeOffset = Vector3.zero; return; }
        _shakeTimer -= Time.unscaledDeltaTime;

        float falloff = Mathf.Clamp01(_shakeTimer / _shakeDuration);
        float intensity = _shakeStrength * falloff * falloff;

        _shakeOffset = new Vector3(
            (Mathf.PerlinNoise(Time.unscaledTime * 30f, 0.731f) - 0.5f) * 2f * intensity,
            (Mathf.PerlinNoise(0.137f, Time.unscaledTime * 30f) - 0.5f) * 2f * intensity,
            0f);
    }

    // ══════════════════════════════════════
    // 每帧 — 三通道独立 SmoothDamp
    // ══════════════════════════════════════
    void LateUpdate()
    {
        if (currentMode != CameraSpectatorMode.Auto) return;

        float dt = Time.unscaledDeltaTime;
        float maxSpeed = Mathf.Infinity;
        UpdateShake();

        // 事件超时回退
        if (_eventActive)
        {
            _eventTimer -= Time.deltaTime;
            if (_eventTimer <= 0f)
            {
                _eventActive = false;
                _targetHeight = defaultHeight;
                _targetPitch = defaultPitch;
                _targetFocusDistance = focusDistanceDefault;
                _targetAperture = apertureDefault;
                _shakeStrength = 0f;
            }
        }

        if (mainCam == null) return;

        // ═══════════════════════════════════
        // 通道 1：变焦（高度）— 0.45s
        // ═══════════════════════════════════
        float currentH = mainCam.transform.position.y;
        float smoothedH = Mathf.SmoothDamp(currentH, _targetHeight, ref _zoomVelocity, zoomSmoothTime, maxSpeed, dt);

        // ═══════════════════════════════════
        // 通道 2：平移 — 0.65s
        // 相机围绕目标旋转 defaultYaw°，从侧面看战场，不只看背面
        // ═══════════════════════════════════
        float pitchRad = _targetPitch * Mathf.Deg2Rad;
        float horizDist = smoothedH / Mathf.Tan(pitchRad);
        float yawRad = defaultYaw * Mathf.Deg2Rad;
        Vector3 desiredPos = new Vector3(
            _targetPositionFlat.x + Mathf.Sin(yawRad) * horizDist,
            smoothedH,
            _targetPositionFlat.z - Mathf.Cos(yawRad) * horizDist);
        Vector3 smoothedPos = Vector3.SmoothDamp(mainCam.transform.position, desiredPos, ref _posVelocity, positionSmoothTime, maxSpeed, dt);
        mainCam.transform.position = smoothedPos + _shakeOffset;

        // ═══════════════════════════════════
        // 通道 3：旋转 — 0.4s。LookRotation 朝向目标（从当前实际位置）
        // ═══════════════════════════════════
        Vector3 lookDir = (_targetPositionFlat - smoothedPos).normalized;
        if (lookDir.sqrMagnitude > 0.001f)
        {
            Quaternion desiredRot = Quaternion.LookRotation(lookDir, Vector3.up);
            float t = 1f - Mathf.Exp(-dt / rotationSmoothTime);
            mainCam.transform.rotation = Quaternion.Slerp(mainCam.transform.rotation, desiredRot, t);
        }

        // ═══════════════════════════════════
        // 景深平滑过渡
        // ═══════════════════════════════════
        UpdateDepthOfField(dt);
    }

    void UpdateDepthOfField(float dt)
    {
        if (!_hasDof || _dofSettings == null) return;
        try
        {
            if (_dofFocusProp != null)
            {
                var fdVal = _dofFocusProp.GetValue(_dofSettings, null);
                var valueProp = fdVal.GetType().GetProperty("value");
                if (valueProp != null)
                {
                    float cur = (float)valueProp.GetValue(fdVal, null);
                    float fd = Mathf.SmoothDamp(cur, _targetFocusDistance, ref _focusDistanceVel, 0.5f, Mathf.Infinity, dt);
                    valueProp.SetValue(fdVal, fd, null);
                }
            }
            if (_dofApertureProp != null)
            {
                var apVal = _dofApertureProp.GetValue(_dofSettings, null);
                var valueProp = apVal.GetType().GetProperty("value");
                if (valueProp != null)
                {
                    float cur = (float)valueProp.GetValue(apVal, null);
                    float ap = Mathf.SmoothDamp(cur, _targetAperture, ref _apertureVel, 0.5f, Mathf.Infinity, dt);
                    valueProp.SetValue(apVal, ap, null);
                }
            }
        }
        catch (System.Exception) { _hasDof = false; }
    }

    // ══════════════════════════════════════
    // 工具
    // ══════════════════════════════════════
    UnitState FindUnitById(long id)
    {
        if (_engine == null) return null;
        _engine.units.TryGetValue(id, out var u);
        return u;
    }
}
