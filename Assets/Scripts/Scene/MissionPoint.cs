using System.Collections;
using UnityEngine;

/// <summary>
/// 任务点组件：挂在 SceneBuilder.BuildChestPoint / BuildVehiclePoint 生成的任务点（宝箱/装甲车）根节点上。
/// 记录格子坐标与类型，供 MissionVehicleDriver 按 game 坐标定位。
/// 装甲车（isVehicle）任务点初始/重生形态是「破损卡车」（broken_…prefab）；「自进化类2」任务
/// 完成时 StartSellCycle：破损→修复成完好卡车→直线开向小贩（不调头，小贩前停下）→停留片刻
/// →消失→原任务点重生破损卡车。无文字徽标，卡车出现即售卖信号。
/// </summary>
public class MissionPoint : MonoBehaviour
{
    public int gameX, gameY;   // 格子坐标（game 坐标系：左下原点，x 东 y 北）
    public int gameX2 = -1, gameY2 = -1; // 卡车 1×2 占地时另一格坐标（无则 -1）；供任务 pos 命中其中任一格
    public bool isVehicle;     // 装甲车（任务完成会修复/开走/重生）；宝箱为 false
    public bool isBroken;      // 破损卡车（任务点初始/重生形态）
    public string prefabPath;          // 破损卡车 prefab（初始+重生，如 Prefabs/broken_K151ArmoredVehicle）
    public string workingPrefabPath;   // 修复后的完好卡车 prefab（如 Prefabs/K151ArmoredVehicle）

    const float DRIVE_DURATION = 2f;   // 开到小贩的时长（秒）
    const float STOP_BEFORE_VENDOR = 1.2f; // 在小贩坐标前 N 米停下（不压到小贩）
    const float SELL_HOLD = 1f;      // 「贩卖成功」文字显示停留时长（秒），随后消失重生
    const float BADGE_Y = 0.8f;        // 「贩卖成功」徽标在卡车模型上方的高度（世界单位，卡车高约 0.46m）
    const float BADGE_SCALE = 1f;      // 徽标字号/底板（1 = 与工人购买面板同款大小）

    bool _busy;   // 正在售卖流程中（防重复触发/中途再开）
    Vector3 _originPos;      // 原任务点位置（Seek 重置 / 重生破损车用）
    Quaternion _originRot;
    Vector3 _originScale;
    TradeBadge _badge;       // 「贩卖成功」徽标引用（Seek 重置时销毁）

    /// <summary>装甲车售卖流程：破损→修复成完好→开向小贩→停留→消失→原任务点重生破损车。</summary>
    public void StartSellCycle(Vector3 vendorPos, ReplayPlayer player)
    {
        if (_busy || !isVehicle) return;
        _busy = true;
        if (isBroken)
        {
            // 修复完成：在原任务点换成完好卡车，由它开向小贩
            var working = SpawnTruck(workingPrefabPath, transform.position, transform.rotation, transform.localScale, transform.parent, name);
            if (working != null)
            {
                var wmp = working.GetComponent<MissionPoint>();
                if (wmp == null) wmp = working.AddComponent<MissionPoint>();
                wmp.gameX = gameX; wmp.gameY = gameY;
                wmp.gameX2 = gameX2; wmp.gameY2 = gameY2;
                wmp.isVehicle = true; wmp.isBroken = false;
                wmp.prefabPath = prefabPath; wmp.workingPrefabPath = workingPrefabPath;
                wmp._originPos = transform.position;   // 原任务点（Seek 重置 / 重生破损车用）
                wmp._originRot = transform.rotation;
                wmp._originScale = transform.localScale;
                wmp.StartSellCycle(vendorPos, player);   // 完好卡车继续售卖流程
            }
            Destroy(gameObject);   // 移除破损卡车
            return;
        }
        // 完好卡车：开向小贩 → 停留 → 重生破损车 → 消失
        StartCoroutine(SellRoutine(vendorPos, player));
    }

    IEnumerator SellRoutine(Vector3 vendorPos, ReplayPlayer player)
    {
        // 记录出生点（驱车前卡车仍在任务点，含朝向小贩的车头）
        Vector3 originPos = transform.position;
        Quaternion originRot = transform.rotation;
        Vector3 originScale = transform.localScale;
        Transform parent = transform.parent;
        string objName = name;
        _originPos = originPos; _originRot = originRot; _originScale = originScale;

        // 直线开向小贩，但在小贩坐标前 STOP_BEFORE_VENDOR 处停下（不压到小贩）。
        // 停点沿行进方向回退：两车按各自路径自然分开，且不旋转（初始车头已朝小贩）。
        Vector3 start = transform.position;
        Vector3 approach = vendorPos - start;
        approach.y = 0f;
        Vector3 stopPoint = approach.sqrMagnitude > 0.0001f
            ? vendorPos - approach.normalized * STOP_BEFORE_VENDOR
            : vendorPos;
        float t = 0f;
        while (t < 1f)
        {
            if (player != null && !player.playing) { yield return null; continue; }  // 暂停冻结
            t += Time.deltaTime / DRIVE_DURATION;
            t = Mathf.Clamp01(t);
            transform.position = Vector3.Lerp(start, stopPoint, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
        transform.position = stopPoint;

        // 显示「贩卖成功」（工人购买面板样式）。挂到 scale=1 的地图根 + 世界坐标定位在卡车模型上方，
        // 避免继承卡车 0.27 缩放导致徽标突然放大/位置漂移；卡车消失后徽标独立淡出
        var badge = TradeBadge.ShowTextWorld(transform.parent,
            transform.position + Vector3.up * BADGE_Y, "贩卖成功", BADGE_SCALE);
        _badge = badge;
        float wait = 0f;
        while (wait < SELL_HOLD)
        {
            if (player != null && !player.playing) { yield return null; continue; }
            wait += Time.deltaTime;
            yield return null;
        }
        if (badge != null) Destroy(badge.gameObject);   // 只显示 SELL_HOLD(1s)，不等 TradeBadge 默认 1.8s 淡完

        var newBroken = SpawnTruck(prefabPath, originPos, originRot, originScale, parent, objName);
        if (newBroken != null)
        {
            var mp = newBroken.GetComponent<MissionPoint>();
            if (mp == null) mp = newBroken.AddComponent<MissionPoint>();
            mp.gameX = gameX; mp.gameY = gameY;
            mp.gameX2 = gameX2; mp.gameY2 = gameY2;
            mp.isVehicle = true; mp.isBroken = true;
            mp.prefabPath = prefabPath; mp.workingPrefabPath = workingPrefabPath;
        }
        Destroy(gameObject);
    }

    /// <summary>Seek 跳回合时重置为原任务点的破损卡车：取消进行中的售卖协程、销毁徽标、
    /// 若是完好车则重生破损车并销毁完好车。已破损且在原位则无操作。</summary>
    public void ResetToBroken()
    {
        StopAllCoroutines();
        _busy = false;
        if (_badge != null) { Destroy(_badge.gameObject); _badge = null; }
        if (!isBroken)
        {
            var newBroken = SpawnTruck(prefabPath, _originPos, _originRot, _originScale, transform.parent, name);
            if (newBroken != null)
            {
                var mp = newBroken.GetComponent<MissionPoint>();
                if (mp == null) mp = newBroken.AddComponent<MissionPoint>();
                mp.gameX = gameX; mp.gameY = gameY;
                mp.gameX2 = gameX2; mp.gameY2 = gameY2;
                mp.isVehicle = true; mp.isBroken = true;
                mp.prefabPath = prefabPath; mp.workingPrefabPath = workingPrefabPath;
            }
            Destroy(gameObject);
        }
    }

    /// <summary>按路径生成一辆任务点卡车（不装配 MissionPoint，调用方负责）。</summary>
    static GameObject SpawnTruck(string path, Vector3 pos, Quaternion rot, Vector3 scale, Transform parent, string objName)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var prefab = Resources.Load<GameObject>(path);
        if (prefab == null) { Debug.LogWarning("[MissionPoint] 缺 prefab " + path); return null; }
        var go = Instantiate(prefab, parent);
        go.name = objName;   // 保持命名（便于调试/定位）
        go.transform.position = pos;
        go.transform.rotation = rot;
        go.transform.localScale = scale;
        return go;
    }
}
