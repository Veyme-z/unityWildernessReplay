using UnityEngine;

/// <summary>
/// 电影级导演相机系统：全局 / A队特写 / B队特写 之间平滑切换。
/// 使用 Vector3.Lerp + Quaternion.Slerp 实现丝滑运镜。
/// 快捷键：1=全局  2=A队  3=B队
/// </summary>
public class ReplayCameraRig : MonoBehaviour
{
    public enum CameraMode { Global, TeamA, TeamB }

    [Header("调试模式")]
    [Tooltip("开启时自动运镜；关闭后代码不再修改相机 Transform，方便在 Play 模式手动拖拽微调机位")]
    public bool enableAutoLock = true;

    [Header("运镜平滑度")]
    public float smoothSpeed = 3f;

    [Header("全局视角")]
    [Tooltip("电影级全景镜头高度（米）")]
    public float globalHeight = 16f;
    [Tooltip("全局俯角（度），35° = 黄金 3D 全景视角")]
    [Range(20f, 80f)] public float globalPitch = 35f;
    [Tooltip("全局视野角度（度），35° = 中长焦电影镜头")]
    [Range(20f, 60f)] public float globalFOV = 35f;

    [Header("特写视角")]
    public float closeHeight = 14f;
    [Range(30f, 80f)] public float closePitch = 55f; // 度，从水平面算起的俯角

    [Header("手动机位（非零时覆盖自动计算）")]
    [Tooltip("A 队相机世界坐标，设为 (0,0,0) 则走自动计算")]
    public Vector3 teamAPos = new Vector3(-5.5f, 5.5f, -10f);
    [Tooltip("A 队相机欧拉角")]
    public Vector3 teamARot = new Vector3(25f, 0f, 0f);
    [Tooltip("B 队相机世界坐标，设为 (0,0,0) 则走自动计算")]
    public Vector3 teamBPos = new Vector3(5.6f, 6.5f, -15f);
    [Tooltip("B 队相机欧拉角")]
    public Vector3 teamBRot = new Vector3(25f, 0f, 0f);

    // —— 内部状态 ——
    CameraMode _mode = CameraMode.Global;
    Vector3 _desiredPosition;
    Quaternion _desiredRotation;
    Vector3 _globalTarget;
    Vector3 _teamATarget;
    Vector3 _teamBTarget;
    bool _initialized;

    const int MAP_W = 41;
    const int MAP_H = 32;

    // ——— 生命周期 ———
    void Awake()
    {
        // 以相机当前 transform 作为初始 desired，避免第一帧跳变
        _desiredPosition = transform.position;
        _desiredRotation = transform.rotation;
    }

    void Start()
    {
        // 计算各模式的世界坐标目标
        // 全局：地图正中央（gameX=20, gameY=15.5 → world origin）
        _globalTarget = Vector3.zero;

        // 确保主相机为透视模式 + 电影 FOV
        var cam = GetComponent<Camera>();
        if (cam != null)
        {
            cam.orthographic = false;
            cam.fieldOfView = globalFOV;
        }

        // A 队基地 (gameX=10, gameY=24)，2×2 居中偏移 +0.5
        _teamATarget = CellToWorld(10, 24) + new Vector3(0.5f, 0, 0.5f);

        // B 队基地 (gameX=30, gameY=10)，2×2 居中偏移 +0.5
        _teamBTarget = CellToWorld(30, 10) + new Vector3(0.5f, 0, 0.5f);

        _initialized = true;
        SetCameraMode("global");
    }

    // ——— 公开 API ———
    /// <summary>
    /// 切换镜头模式。参数："global" / "teamA" / "teamB"
    /// </summary>
    public void SetCameraMode(string mode)
    {
        // 通知 CameraManager：这是玩家主动操作，切回 Manual
        if (CameraManager.Instance != null && CameraManager.Instance.IsAuto)
            CameraManager.Instance.OnPlayerCameraInput();

        switch (mode)
        {
            case "global": _mode = CameraMode.Global; break;
            case "teamA":  _mode = CameraMode.TeamA;  break;
            case "teamB":  _mode = CameraMode.TeamB;  break;
            default:
                Debug.LogWarning("[ReplayCameraRig] 未知镜头模式: " + mode);
                return;
        }
        UpdateDesiredTransform();
    }

    // ——— 兼容旧 API（ReplayPlayer.Setup 调用） ———
    public void Focus(Vector3 t, float distance)
    {
        // 导演模式下无需外部干预，保留空实现以兼容旧调用
    }

    public void FitMap(int mapW, int mapH, float margin = 0.6f)
    {
        _globalTarget = new Vector3(0, 0, -mapH * 0.25f);
        if (_mode == CameraMode.Global)
            UpdateDesiredTransform();
    }

    // ——— 内部 ———
    void UpdateDesiredTransform()
    {
        Vector3 target;
        float height, pitchDeg;
        bool useManual = false;
        Vector3 manualPos = Vector3.zero;
        Vector3 manualRot = Vector3.zero;

        switch (_mode)
        {
            case CameraMode.Global:
                target   = _globalTarget;
                height   = globalHeight;
                pitchDeg = globalPitch;
                break;
            case CameraMode.TeamA:
                target   = _teamATarget;
                height   = closeHeight;
                pitchDeg = closePitch;
                if (teamAPos != Vector3.zero) { useManual = true; manualPos = teamAPos; manualRot = teamARot; }
                break;
            case CameraMode.TeamB:
                target   = _teamBTarget;
                height   = closeHeight;
                pitchDeg = closePitch;
                if (teamBPos != Vector3.zero) { useManual = true; manualPos = teamBPos; manualRot = teamBRot; }
                break;
            default: return;
        }

        // 手动机位：直接使用预设 Position + Rotation
        if (useManual)
        {
            _desiredPosition = manualPos;
            _desiredRotation = Quaternion.Euler(manualRot);
            return;
        }

        if (Mathf.Approximately(pitchDeg, 90f))
        {
            _desiredPosition = target + Vector3.up * height;
            _desiredRotation = Quaternion.Euler(90f, 0f, 0f);
        }
        else
        {
            float pitchRad = pitchDeg * Mathf.Deg2Rad;
            float horizDist = height / Mathf.Tan(pitchRad);
            _desiredPosition = target + new Vector3(0, height, -horizDist);
            _desiredRotation = Quaternion.LookRotation(target - _desiredPosition, Vector3.up);
        }
    }

    void Update()
    {
        // —— 键盘快捷键（玩家操作优先：通知 CameraManager 切回 Manual） ——
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            if (CameraManager.Instance != null && CameraManager.Instance.IsAuto)
                CameraManager.Instance.OnPlayerCameraInput();
            SetCameraMode("global");
        }
        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            if (CameraManager.Instance != null && CameraManager.Instance.IsAuto)
                CameraManager.Instance.OnPlayerCameraInput();
            SetCameraMode("teamA");
        }
        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            if (CameraManager.Instance != null && CameraManager.Instance.IsAuto)
                CameraManager.Instance.OnPlayerCameraInput();
            SetCameraMode("teamB");
        }
    }

    void LateUpdate()
    {
        if (!_initialized) return;
        if (!enableAutoLock) return;

        // CameraManager 在 Auto 模式时接管主相机，本脚本不再操作
        if (CameraManager.Instance != null && CameraManager.Instance.IsAuto) return;

        // 保持透视模式 + FOV
        var cam = GetComponent<Camera>();
        if (cam != null)
        {
            cam.orthographic = false;
            if (_mode == CameraMode.Global) cam.fieldOfView = globalFOV;
        }

        // 指数衰减平滑：每帧趋近目标，速度恒定无 overshoot
        float t = 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, _desiredPosition, t);
        transform.rotation = Quaternion.Slerp(transform.rotation, _desiredRotation, t);
    }

    // ——— 坐标工具 ———
    /// <summary>游戏格子坐标 → Unity 世界坐标</summary>
    static Vector3 CellToWorld(int gameX, int gameY)
    {
        float ox = (MAP_W - 1) * 0.5f;
        float oz = (MAP_H - 1) * 0.5f;
        return new Vector3(gameX - ox, 0f, gameY - oz);
    }
}
