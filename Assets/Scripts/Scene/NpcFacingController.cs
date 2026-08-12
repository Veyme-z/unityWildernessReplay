using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// NPC 转向组件：检测周围来访者，1人则面向、≥2人则默认朝向。
/// 不依赖具体 FBX、骨骼名或 KayKit 资源路径。
/// </summary>
public class NpcFacingController : MonoBehaviour
{
    [Header("Turn")]
    public Transform facingTransform;
    public float turnSpeed = 450f;
    public float forwardYawOffset = 0f;

    [Header("Default")]
    public Vector3 defaultWorldDirection = new Vector3(0f, 0f, -1f);

    [Header("NPC Type (8=Officer, 9=Vendor)")]
    public int npcType = 0;

    // Cache
    ReplayPlayer _player;
    UnitView _view;
    Vector3? _targetPos;
    Quaternion _defaultRot;
    int _lastRound = -1;

    void Start()
    {
        _view = GetComponent<UnitView>();
        _player = FindObjectOfType<ReplayPlayer>();
        if (facingTransform == null) facingTransform = transform;

        // 如果 UnitView 有 state 则用它推断类型，否则用序列化字段
        if (npcType == 0 && _view != null && _view.state != null)
            npcType = _view.state.type;

        _defaultRot = Quaternion.LookRotation(defaultWorldDirection.normalized, Vector3.up);
    }

    Vector3 NpcWorldPos
    {
        get
        {
            if (_view != null && _view.state != null) return _view.state.pos;
            return transform.position;
        }
    }

    void LateUpdate()
    {
        if (_player == null || _player.data == null) return;

        bool seeked = Mathf.Abs(_player.cur - _lastRound) > 1;
        if (_player.cur != _lastRound)
        {
            _lastRound = _player.cur;
            RefreshTarget();
        }

        Quaternion goal = _targetPos.HasValue
            ? Quaternion.LookRotation(DirectionTo(_targetPos.Value), Vector3.up)
            : _defaultRot;

        if (seeked)
        {
            // Seek 后立即到位（无论是否暂停）
            ApplyRotation(goal);
            seeked = false;
        }
        else if (_player.playing)
        {
            // 正常播放时平滑旋转
            Quaternion cur = facingTransform.rotation;
            cur = Quaternion.RotateTowards(cur, goal, turnSpeed * Time.deltaTime);
            ApplyRotation(cur);
        }
    }

    // ==================== 目标选择 ====================

    void RefreshTarget()
    {
        _targetPos = null;
        var engine = _player.engine;
        if (engine == null) return;
        if (npcType != 8 && npcType != 9) return;

        var npcPos = NpcWorldPos;
        int npcGx = Mathf.RoundToInt(npcPos.x + 20f);
        int npcGy = Mathf.RoundToInt(15.5f - npcPos.z);

        // 收集周围一格内的所有有效来访者
        var visitors = new List<UnitState>();
        foreach (var kv in engine.units)
        {
            var u = kv.Value;
            if (u.dead || u.dying) continue;
            if (u.type != 6 && u.type != 7) continue;

            int gx = Mathf.RoundToInt(u.pos.x + 20f);
            int gy = Mathf.RoundToInt(15.5f - u.pos.z);
            int dist = Mathf.Max(Mathf.Abs(gx - npcGx), Mathf.Abs(gy - npcGy));
            if (dist == 1) visitors.Add(u);
        }

        // 规则：恰好 1 人 → 面向他；≥2 人 → 默认朝向
        if (visitors.Count == 1)
            _targetPos = visitors[0].pos;
        // else: _targetPos stays null → default direction
    }

    Vector3 DirectionTo(Vector3 worldPos)
    {
        Vector3 dir = worldPos - NpcWorldPos;
        dir.y = 0f;
        return dir.sqrMagnitude > 0.0001f ? dir.normalized : facingTransform.forward;
    }

    void ApplyRotation(Quaternion rot)
    {
        if (forwardYawOffset != 0f)
            rot = rot * Quaternion.Euler(0f, forwardYawOffset, 0f);
        facingTransform.rotation = Quaternion.Euler(0f, rot.eulerAngles.y, 0f);
    }
}
