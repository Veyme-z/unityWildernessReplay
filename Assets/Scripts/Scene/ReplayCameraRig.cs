using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 电影级导演相机系统：Global / TeamA / TeamB / Free 之间平滑切换。
/// Free 模式：左键平移 / Alt+左键(或Ctrl+左键)旋转 / 滚轮向鼠标位置缩放。
/// 快捷键：1=全局  2=A队  3=B队  4=自由
/// </summary>
public class ReplayCameraRig : MonoBehaviour
{
    public enum CameraMode { Global, TeamA, TeamB, Free }

    [Header("调试模式")]
    public bool enableAutoLock = true;

    [Header("运镜平滑度")]
    public float smoothSpeed = 3f;

    // ==================== Global ====================
    [Header("全局视角")]
    public Vector3 globalPositionOverride = new Vector3(0, 28, -40);
    public float globalHeight = 16f;
    [Range(20f, 80f)] public float globalPitch = 40f;
    [Range(20f, 60f)] public float globalFOV = 35f;

    // ==================== Team A / B ====================
    [Header("特写视角")]
    public float closeHeight = 14f;
    [Range(30f, 80f)] public float closePitch = 55f;
    [Header("手动机位")]
    public Vector3 teamAPos = new Vector3(-5.5f, 5.5f, -10f);
    public Vector3 teamARot = new Vector3(25f, 0f, 0f);
    public Vector3 teamBPos = new Vector3(5.6f, 6.5f, -15f);
    public Vector3 teamBRot = new Vector3(25f, 0f, 0f);

    // ==================== Free ====================
    [Header("Free 自由相机")]
    [Range(25f, 45f)] public float freeMinPitch = 25f;
    [Range(25f, 45f)] public float freeMaxPitch = 45f;
    public float freeMinDistance = 8f;
    public float freeMaxDistance = 45f;
    public float freePanSpeed = 6f;
    public float freeRotateSpeed = 3f;
    public float freeZoomSpeed = 1.5f;
    public float freeSmoothSpeed = 8f;

    // —— 内部状态 ——
    CameraMode _mode = CameraMode.Global;
    Vector3 _desiredPosition;
    Quaternion _desiredRotation;
    Vector3 _globalTarget;
    Vector3 _teamATarget;
    Vector3 _teamBTarget;
    bool _initialized;

    // Free 模式状态
    Vector3 _freePivot;
    float _freeYaw, _freePitch, _freeDistance;
    Vector3 _desiredPivot;
    float _desiredYaw, _desiredPitch, _desiredDistance;

    // 视野地面矩形允许的最大边界：48×40，居中于地图原点
    static readonly Vector3 VIS_MIN = new Vector3(-38f, 0f, -20f);
    static readonly Vector3 VIS_MAX = new Vector3(38f, 0f, 30f);

    // ——— 生命周期 ———
    void Awake()
    {
        _desiredPosition = transform.position;
        _desiredRotation = transform.rotation;
    }

    void Start()
    {
        _globalTarget = Vector3.zero;
        var cam = GetComponent<Camera>();
        if (cam != null) { cam.orthographic = false; cam.fieldOfView = globalFOV; }

        _teamATarget = CellToWorld(10, 24) + new Vector3(0.5f, 0, 0.5f);
        _teamBTarget = CellToWorld(30, 10) + new Vector3(0.5f, 0, 0.5f);

        _initialized = true;
        SetCameraMode("global");
    }

    // ——— 公开 API ———
    public void SetCameraMode(string mode)
    {
        if (CameraManager.Instance != null && CameraManager.Instance.IsAuto)
            CameraManager.Instance.OnPlayerCameraInput();

        switch (mode)
        {
            case "global": _mode = CameraMode.Global; break;
            case "teamA":  _mode = CameraMode.TeamA;  break;
            case "teamB":  _mode = CameraMode.TeamB;  break;
            case "free":
                EnterFreeMode();
                return;
            default:
                Debug.LogWarning("[ReplayCameraRig] 未知镜头模式: " + mode);
                return;
        }
        UpdateDesiredTransform();
    }

    public void Focus(Vector3 t, float distance) { }
    public void FitMap(int mapW, int mapH, float margin = 0.6f)
    {
        _globalTarget = new Vector3(0, 0, -mapH * 0.25f);
        if (_mode == CameraMode.Global) UpdateDesiredTransform();
    }

    // ==================== Free 模式 ====================

    void EnterFreeMode()
    {
        // 从屏幕中心射线推导 pivot（无跳变）
        var cam = GetComponent<Camera>();
        var ray = cam != null ? cam.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0)) : default;
        var plane = new Plane(Vector3.up, Vector3.zero);
        float hit;
        if (plane.Raycast(ray, out hit))
        {
            _freePivot = ray.GetPoint(hit);
        }
        else
        {
            _freePivot = _globalTarget;
        }

        // 从当前相机位置反推 yaw/pitch/distance
        Vector3 toCam = transform.position - _freePivot;
        _freeDistance = toCam.magnitude;
        _freeDistance = Mathf.Clamp(_freeDistance, freeMinDistance, freeMaxDistance);
        if (toCam.sqrMagnitude > 0.0001f)
        {
            // yaw/pitch 代表相机朝向 pivot 的方向（非相机位置偏移）
            _freeYaw = Mathf.Atan2(-toCam.x, -toCam.z) * Mathf.Rad2Deg;
            float horiz = new Vector2(toCam.x, toCam.z).magnitude;
            _freePitch = Mathf.Atan2(toCam.y, horiz) * Mathf.Rad2Deg;
        }
        else
        {
            _freeYaw = 0f;
            _freePitch = freeMinPitch;
        }
        _freePitch = Mathf.Clamp(_freePitch, freeMinPitch, freeMaxPitch);

        // 同步 desired
        _desiredPivot = _freePivot;
        _desiredYaw = _freeYaw;
        _desiredPitch = _freePitch;
        _desiredDistance = _freeDistance;

        _mode = CameraMode.Free;
    }

    void HandleFreeInput()
    {
        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f) return;

        var cam = GetComponent<Camera>();
        if (cam == null) return;

        // 鼠标在 UI 上 → 不处理
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        // 旋转修饰键：Alt 或 Ctrl 按住 + 左键拖拽 = 旋转（规避 WebGL 浏览器右键手势劫持）
        bool rotateModifier = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)
                           || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        // —— 左键 + Alt/Ctrl 拖动：旋转 yaw + pitch ——
        if (rotateModifier && Input.GetMouseButton(0))
        {
            float dx = Input.GetAxis("Mouse X");
            float dy = Input.GetAxis("Mouse Y");
            if (Mathf.Abs(dx) > 0.001f) _desiredYaw += dx * freeRotateSpeed;
            if (Mathf.Abs(dy) > 0.001f)
            {
                _desiredPitch -= dy * freeRotateSpeed;
                _desiredPitch = Mathf.Clamp(_desiredPitch, freeMinPitch, freeMaxPitch);
            }
        }
        // —— 左键拖动（无修饰键）：平移 pivot ——
        else if (Input.GetMouseButton(0))
        {
            float dx = Input.GetAxis("Mouse X");
            float dy = Input.GetAxis("Mouse Y");
            if (Mathf.Abs(dx) > 0.001f || Mathf.Abs(dy) > 0.001f)
            {
                Vector3 right = transform.right;
                Vector3 forward = Vector3.Cross(right, Vector3.up).normalized;
                float scale = _desiredDistance * 0.003f * freePanSpeed;
                _desiredPivot -= (right * dx + forward * dy) * scale;
            }
        }

        // —— 滚轮：向鼠标位置缩放 ——
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.0001f)
        {
            float oldDist = _desiredDistance;
            float newDist = Mathf.Clamp(oldDist - scroll * freeZoomSpeed * 15f, freeMinDistance, freeMaxDistance);
            if (Mathf.Abs(newDist - oldDist) > 0.001f)
            {
                var mouseRay = cam.ScreenPointToRay(Input.mousePosition);
                var groundPlane = new Plane(Vector3.up, Vector3.zero);
                float groundHit;
                if (groundPlane.Raycast(mouseRay, out groundHit))
                {
                    Vector3 cursorPoint = mouseRay.GetPoint(groundHit);
                    float ratio = newDist / oldDist;
                    _desiredPivot = cursorPoint + (_desiredPivot - cursorPoint) * ratio;
                }
                _desiredDistance = newDist;
            }
        }

        // 可见范围夹紧：保证视野地面矩形不超出 48×40 框
        ClampVisibleArea();
    }

    void UpdateFreeTransform()
    {
        float dt = Time.unscaledDeltaTime;
        float t = 1f - Mathf.Exp(-freeSmoothSpeed * dt);

        _freePivot = Vector3.Lerp(_freePivot, _desiredPivot, t);
        _freeYaw = Mathf.LerpAngle(_freeYaw, _desiredYaw, t);
        _freePitch = Mathf.LerpAngle(_freePitch, _desiredPitch, t);
        _freeDistance = Mathf.Lerp(_freeDistance, _desiredDistance, t);

        Quaternion rot = Quaternion.Euler(_freePitch, _freeYaw, 0f);
        _desiredPosition = _freePivot - rot * Vector3.forward * _freeDistance;
        _desiredRotation = rot;
    }

    /// <summary>把视野地面矩形夹进 48×40 框：先收缩距离，再平移 pivot，使画面看不到框外。</summary>
    void ClampVisibleArea()
    {
        if (!TryGetVisibleRect(out Rect r)) return;

        // 1) 视野比框还大（通常发生在缩得太远）→ 收缩距离
        float bw = VIS_MAX.x - VIS_MIN.x; // 48
        float bh = VIS_MAX.z - VIS_MIN.z; // 40
        if (r.width > bw || r.height > bh)
        {
            float sx = r.width  <= 0f ? 1f : bw / r.width;
            float sz = r.height <= 0f ? 1f : bh / r.height;
            float s = Mathf.Min(sx, sz);
            _desiredDistance = Mathf.Max(freeMinDistance, _desiredDistance * s);
            if (!TryGetVisibleRect(out r)) return;
        }

        // 2) 平移回移，使矩形完全落在框内
        Vector3 shift = Vector3.zero;
        if (r.xMin < VIS_MIN.x) shift.x = VIS_MIN.x - r.xMin;
        else if (r.xMax > VIS_MAX.x) shift.x = VIS_MAX.x - r.xMax;
        if (r.yMin < VIS_MIN.z) shift.z = VIS_MIN.z - r.yMin;
        else if (r.yMax > VIS_MAX.z) shift.z = VIS_MAX.z - r.yMax;
        _desiredPivot += shift;
    }

    /// <summary>计算当前 desired 位姿下，视野在地面 (y=0) 的外接矩形（x→Rect.x，z→Rect.y）。</summary>
    bool TryGetVisibleRect(out Rect r)
    {
        r = new Rect();
        var cam = GetComponent<Camera>();
        if (cam == null) return false;

        Quaternion rot = Quaternion.Euler(_desiredPitch, _desiredYaw, 0f);
        Vector3 fwd = rot * Vector3.forward;
        Vector3 right = rot * Vector3.right;
        Vector3 up = rot * Vector3.up;
        Vector3 camPos = _desiredPivot - fwd * _desiredDistance;

        float halfH = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfW = halfH * cam.aspect;

        // 屏幕四角视线方向（未规范化即可，只影响 t，不影响交点）
        Vector3[] dirs = new Vector3[4]
        {
            fwd + right * halfW + up * halfH,   // 右上
            fwd - right * halfW + up * halfH,   // 左上
            fwd + right * halfW - up * halfH,   // 右下
            fwd - right * halfW - up * halfH,   // 左下
        };

        float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
        bool any = false;
        foreach (var d in dirs)
        {
            if (d.y >= -1e-4f) continue;   // 射线不向下，无法交地面
            float t = -camPos.y / d.y;
            if (t < 0f) continue;
            Vector3 hit = camPos + d * t;
            minX = Mathf.Min(minX, hit.x); maxX = Mathf.Max(maxX, hit.x);
            minZ = Mathf.Min(minZ, hit.z); maxZ = Mathf.Max(maxZ, hit.z);
            any = true;
        }
        if (!any) return false;

        r = Rect.MinMaxRect(minX, minZ, maxX, maxZ);
        return true;
    }

    // ==================== Fixed 模式 ====================

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
                if (globalPositionOverride != Vector3.zero) { useManual = true; manualPos = globalPositionOverride; manualRot = new Vector3(globalPitch, 0, 0); }
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

    // ==================== Update ====================

    void Update()
    {
        // —— 键盘快捷键 ——
        if (Input.GetKeyDown(KeyCode.Alpha1)) { NotifyManual(); SetCameraMode("global"); }
        if (Input.GetKeyDown(KeyCode.Alpha2)) { NotifyManual(); SetCameraMode("teamA"); }
        if (Input.GetKeyDown(KeyCode.Alpha3)) { NotifyManual(); SetCameraMode("teamB"); }
        if (Input.GetKeyDown(KeyCode.Alpha4)) { NotifyManual(); SetCameraMode("free"); }

        // Free 模式输入（只在 Free + 非 Auto 时处理）
        if (_mode == CameraMode.Free)
        {
            if (CameraManager.Instance == null || !CameraManager.Instance.IsAuto)
                HandleFreeInput();
        }
    }

    void NotifyManual()
    {
        if (CameraManager.Instance != null && CameraManager.Instance.IsAuto)
            CameraManager.Instance.OnPlayerCameraInput();
    }

    void LateUpdate()
    {
        if (!_initialized) return;
        if (!enableAutoLock) return;
        if (CameraManager.Instance != null && CameraManager.Instance.IsAuto) return;

        var cam = GetComponent<Camera>();
        if (cam != null) cam.orthographic = false;

        if (_mode == CameraMode.Free)
        {
            UpdateFreeTransform();
        }

        float t = 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, _desiredPosition, t);
        transform.rotation = Quaternion.Slerp(transform.rotation, _desiredRotation, t);
    }

    static Vector3 CellToWorld(int gameX, int gameY)
    {
        return new Vector3(gameX - 20f, 0f, gameY - 15.5f);
    }
}
