using System.Text;
using UnityEngine;

/// <summary>
/// 单位调试悬浮文字：非围墙/非野兽单位头顶实时显示 [ID|坐标|HP|攻击力]。
/// 由 UnitView.ConfigureFromUnitPrefab() 挂载（野兽路径不挂），全局开关
/// PlaybackControlPanelController.ShowUnitStats 控制显隐。
///
/// 性能：全局关闭或单位死亡时 TextMesh SetActive(false)，零渲染开销；
/// 开启时 0.5s 节流 + hp/pos/ap 脏检查（变化才重建文本），不逐帧拼字符串。
/// </summary>
public class UnitDebugOverlay : MonoBehaviour
{
    const float REFRESH_INTERVAL = 0.5f;   // 文本重建节流（秒）
    const float TEXT_ABOVE_HPBAR = 1.0f;   // 血条上方悬浮高度（明确浮在血条之上）
    const float MAP_OFFSET_X = 20f;        // 世界坐标反推格子坐标（与 CellToWorld 一致）
    const float MAP_OFFSET_Z = 15.5f;
    const int FONT_SIZE = 20;
    const float CHAR_SIZE = 0.1f;          // 对标 TradeBadge：charSize×fontSize≈2.0，3D 相机下肉眼可见

    UnitState _state;
    Transform _hpFill;      // 血条（文本悬浮基准，Awake 时缓存）
    Transform _textGo;
    TextMesh _textMesh;
    readonly StringBuilder _sb = new StringBuilder(96);

    // 脏检查缓存（初始 NaN/MinValue，保证首帧必刷新）
    float _lastPosX = float.NaN, _lastPosZ = float.NaN;
    int _lastHp = int.MinValue, _lastAp = int.MinValue;
    float _nextRefresh;

    void Awake()
    {
        var v = GetComponent<UnitView>();
        if (v == null || v.state == null) { enabled = false; return; }
        _state = v.state;
        _hpFill = transform.Find("HpFill");
        // 围墙(5)/野兽(>=11)：绝对不创建、不渲染任何调试文本（野兽路径本不会挂此组件，围墙在此过滤）
        if (_state.type == 5 || _state.IsBeast) { enabled = false; return; }
    }

    void Update()
    {
        // 全局关闭 → 隐藏（仅一条布尔判断，零渲染开销）
        if (!PlaybackControlPanelController.ShowUnitStats) { SetTextActive(false); return; }
        // 单位已死 → 隐藏
        if (_state.dead || _state.dying) { SetTextActive(false); return; }

        EnsureText();
        if (_textGo == null) return;
        _textGo.gameObject.SetActive(true);

        // 脏检查 + 节流：位置/血量/攻击力变化，或距上次刷新满 0.5s，才重建文本
        float now = Time.unscaledTime;
        bool dirty = _state.pos.x != _lastPosX || _state.pos.z != _lastPosZ
                  || _state.hp != _lastHp || _state.ap != _lastAp;
        if (now >= _nextRefresh || dirty)
        {
            _nextRefresh = now + REFRESH_INTERVAL;
            _lastPosX = _state.pos.x; _lastPosZ = _state.pos.z;
            _lastHp = _state.hp; _lastAp = _state.ap;
            RebuildText();
        }

        // 跟随血条高度（血条 Y 一般固定，保险起见对齐一次）
        FollowHpBarY();
    }

    /// <summary>懒创建世界空间 TextMesh（首次需要显示时才建，避免闲置单位白白持有对象）。</summary>
    void EnsureText()
    {
        if (_textGo != null) return;
        var go = new GameObject("DebugOverlay");
        go.transform.SetParent(transform, false);
        _textGo = go.transform;
        _textMesh = go.AddComponent<TextMesh>();
        var font = UiFonts.Get();
        _textMesh.font = font;
        _textMesh.fontSize = FONT_SIZE;
        _textMesh.characterSize = CHAR_SIZE;
        _textMesh.anchor = TextAnchor.MiddleCenter;
        _textMesh.alignment = TextAlignment.Center;
        _textMesh.color = Color.black;
        // 关键修复：Dynamic 字体的贴图不会自动同步到 MeshRenderer → 3D 文本隐形。
        // 与 TradeBadge 一致：显式赋 font.material + 抬高 sortingOrder 避免被透明物遮挡。
        var mr = _textMesh.GetComponent<MeshRenderer>();
        if (mr != null && font != null && font.material != null)
        {
            mr.sharedMaterial = font.material;
            mr.sortingOrder = 100;
        }
        // 复用 TradeBadge 同款 Billboard，文字始终面向相机
        go.AddComponent<Billboard>();
        FollowHpBarY();
    }

    void RebuildText()
    {
        if (_textMesh == null) return;
        // 格子坐标：state.pos 是格子中心世界坐标，反推回格子。
        // 基地(type=4)占 2×2，state.pos 是其 2×2 中心（UnitWorldPos 锚点=左上角格，占地 x..x+1, y-1..y），
        // 左上角格 = (floor(pos.x+20), floor(pos.z+15.5)+1)。
        int gx, gy;
        if (_state.type == 4)
        {
            gx = Mathf.FloorToInt(_state.pos.x + MAP_OFFSET_X);
            gy = Mathf.FloorToInt(_state.pos.z + MAP_OFFSET_Z) + 1;   // 左上角格
        }
        else
        {
            gx = Mathf.RoundToInt(_state.pos.x + MAP_OFFSET_X);
            gy = Mathf.RoundToInt(_state.pos.z + MAP_OFFSET_Z);
        }
        _sb.Length = 0;
        _sb.Append("ID: ").Append(_state.id)
           .Append(" | Pos: (").Append(gx).Append(", ").Append(gy).Append(")")
           .Append(" | HP: ").Append(_state.hp)
           .Append(" | ATK: ").Append(_state.ap);
        _textMesh.text = _sb.ToString();
        // WebGL：legacy TextMesh 赋 Dynamic 字体后不主动请求字形 → 显式请求（TradeBadge 同款做法）
        if (_textMesh.font != null)
            _textMesh.font.RequestCharactersInTexture(_textMesh.text, _textMesh.fontSize, _textMesh.fontStyle);
    }

    /// <summary>文字悬浮在血条正上方（HpFill 子节点的 localPosition.y 即血条高度）。</summary>
    void FollowHpBarY()
    {
        if (_textGo == null) return;
        float y = (_hpFill != null ? _hpFill.localPosition.y : 3f) + TEXT_ABOVE_HPBAR;
        var p = _textGo.localPosition;
        if (Mathf.Abs(p.y - y) > 0.001f)
            _textGo.localPosition = new Vector3(0, y, 0);
    }

    void SetTextActive(bool active)
    {
        if (_textGo != null && _textGo.gameObject.activeSelf != active)
            _textGo.gameObject.SetActive(active);
    }
}
