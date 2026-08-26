using UnityEngine;

/// <summary>
/// 围墙方向：根据围墙所在格坐标决定横/竖摆放、镜像与拐角替换（挂在 Wall.prefab 上，回放动态生效）。
///
/// 每个防御围墙环对应一组坐标（ZONES 数组，红/蓝双基地各一）：
///   横      (hxMin..hxMax, hyTop)    —— 绕 Y 0°
///   横镜像  (hxMin..hxMax, hyBottom) —— 绕 Y 180°
///   竖      (vxRight, vyMin..vyMax)  —— 绕 Y 90°
///   竖镜像  (vxLeft, vyMin..vyMax)   —— 绕 Y 270°
///   四角    (vxLeft/vxRight, hyBottom/hyTop) —— 用 Resources 拐角 prefab（WallCorner）替换直墙，
///           旋转使 L 形外弧朝环外。
///   其余坐标  —— 默认竖（90°）
///
/// 拐角缩放完全由 prefab 自带（Resources 里 WallCorner = 0.5,0.5,0.43，与直墙同高同深），代码不改缩放。
///
/// 旋转/替换根 Transform：UnitView._lockRotation=true（围墙 type5 是建筑），LateUpdate 只重置
/// _body（"Body" 空锚点），不写根旋转，所以根的朝向与拐角子件能稳定保持。
/// </summary>
public class WallOrientation : MonoBehaviour
{
    // 防御围墙环定义（可调/可增删）
    struct WallZone
    {
        public int hxMin, hxMax;    // 横墙 x 范围
        public int hyTop, hyBottom; // 横墙两条 y 行（上/下）
        public int vxLeft, vxRight; // 竖墙两列 x（左/右）
        public int vyMin, vyMax;    // 竖墙 y 范围
    }

    static readonly WallZone[] ZONES =
    {
        // 红方基地(30,10)防御环
        new WallZone { hxMin = 29, hxMax = 32, hyTop = 12, hyBottom = 7, vxLeft = 28, vxRight = 33, vyMin = 8, vyMax = 11 },
        // 蓝方基地(10,24)防御环
        new WallZone { hxMin = 9,  hxMax = 12, hyTop = 26, hyBottom = 21, vxLeft = 8, vxRight = 13, vyMin = 22, vyMax = 25 },
    };

    // 拐角件 Resources 路径（缩放完全由 prefab 自带控制，代码不改）
    const string CORNER_RES = "Prefabs/Buildings/WallCorner";
    // 拐角沿自身水平臂方向的位置偏移（视觉上让拐角贴向横墙）。(28,12) 实测 -0.22，各角方向随旋转不同。
    const float CORNER_OFFSET_X = -0.22f;

    void Start()
    {
        var view = GetComponent<UnitView>();
        if (view == null || view.state == null) return;

        // 世界坐标 → 格坐标（与 ReplayState.CellToWorld 互逆：world = (x - 20, 0, y - 15.5)）
        Vector3 pos = view.state.pos;
        int cx = Mathf.RoundToInt(pos.x + 20f);
        int cy = Mathf.RoundToInt(pos.z + 15.5f);

        for (int i = 0; i < ZONES.Length; i++)
        {
            var z = ZONES[i];
            // 四角 → 拐角 prefab
            if ((cx == z.vxLeft || cx == z.vxRight) && (cy == z.hyBottom || cy == z.hyTop))
            {
                UseCornerPrefab(cx, cy, z);
                return;
            }
            // 四方向变体
            float rotY;
            if (cx >= z.hxMin && cx <= z.hxMax && cy == z.hyTop) rotY = 0f;
            else if (cx >= z.hxMin && cx <= z.hxMax && cy == z.hyBottom) rotY = 180f;
            else if (cx == z.vxRight && cy >= z.vyMin && cy <= z.vyMax) rotY = 90f;
            else if (cx == z.vxLeft && cy >= z.vyMin && cy <= z.vyMax) rotY = 270f;
            else continue; // 不属于该环，查下一个环
            transform.localRotation = Quaternion.Euler(0f, rotY, 0f);
            return;
        }

        // 都不属于任何环 → 默认竖
        transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
    }

    /// <summary>把直墙 Model 换成拐角 prefab，只按角旋转；缩放完全用 Resources 里 prefab 自带的（在 prefab 里调）。</summary>
    void UseCornerPrefab(int cx, int cy, WallZone z)
    {
        var cornerPrefab = Resources.Load<GameObject>(CORNER_RES);
        if (cornerPrefab == null)
        {
            Debug.LogWarning("[WallOrientation] 拐角 prefab 缺失：" + CORNER_RES);
            return;
        }

        // 旋转使 L 形贴合各自拐角（外弧朝环外）
        float rotY;
        if (cx == z.vxLeft && cy == z.hyBottom) rotY = 270f;   // 左下
        else if (cx == z.vxRight && cy == z.hyBottom) rotY = 180f; // 右下
        else if (cx == z.vxLeft && cy == z.hyTop) rotY = 0f;   // 左上
        else rotY = 90f;                                        // 右上

        // 隐藏原来的直墙视觉
        var model = transform.Find("Model");
        if (model != null) model.gameObject.SetActive(false);

        var corner = Object.Instantiate(cornerPrefab, transform);
        corner.name = "Corner";
        // 沿拐角自身水平臂方向偏移 CORNER_OFFSET_X：先按 rotY 旋转偏移到拐角局部坐标系，
        // 这样各角在世界里偏移方向不同（跟随各自臂向），但视觉效果一致（与 (28,12) 相同）。
        corner.transform.localPosition = Quaternion.Euler(0f, rotY, 0f) * new Vector3(CORNER_OFFSET_X, 0f, 0f);
        corner.transform.localRotation = Quaternion.Euler(0f, rotY, 0f);
        // 不碰缩放：直接用 Resources 里 prefab 自带的 scale（用户在 WallCorner.prefab 里自行调整）。

        // 纯装饰：关掉拐角件自带的 MeshCollider
        foreach (var col in corner.GetComponentsInChildren<Collider>())
            col.enabled = false;
    }
}
