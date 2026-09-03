using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 统一字体入口。两份字体分工（各一张动态图集，互不抢占，修复 WebGL 大屏下图集挤爆：
/// 2D UI 字消失 / 世界大字渲染成白色豆腐块）：
///   - Get()      → 2D UI 用 Fonts/NotoSansSC-UI（uGUI Text / Prefab 烘焙统一替换用它）
///   - GetWorld() → 3D 世界文字（TextMesh：伤害数字/交易徽标/任务卡/名牌/矿点标签）用原 Fonts/NotoSansSC-Regular
/// WebGL 没有系统 CJK 字形，内置 LegacyRuntime/Arial 会让中文变空白，故两份都打包自 Noto（Dynamic，生僻字也能出字形）。
/// </summary>
public static class UiFonts
{
    static Font _ui;
    static Font _world;
    static bool _triedUi, _triedWorld;   // 只尝试加载一次，避免重复 Resources.Load

    /// <summary>2D UI 字体（NotoSansSC-UI）。惰性加载缓存；失败 LogError 并回退内置字体。</summary>
    public static Font Get()
    {
        if (_ui == null && !_triedUi)
        {
            _triedUi = true;
            _ui = Resources.Load<Font>("Fonts/NotoSansSC-UI");
            if (_ui == null)
            {
                Debug.LogError("[UiFonts] 找不到 UI 字体 Fonts/NotoSansSC-UI，回退到内置字体。");
                _ui = BuiltinFallback();
            }
        }
        return _ui;
    }

    /// <summary>3D 世界文字（legacy TextMesh）字体 = 原 NotoSansSC-Regular。
    /// 惰性加载缓存；失败回退内置字体。成功时预热世界文字常用字符（WebGL TextMesh 不自动请求字形）。</summary>
    public static Font GetWorld()
    {
        if (_world == null && !_triedWorld)
        {
            _triedWorld = true;
            _world = Resources.Load<Font>("Fonts/NotoSansSC-Regular");
            if (_world == null)
            {
                Debug.LogError("[UiFonts] 找不到世界文字字体 Fonts/NotoSansSC-Regular，回退到内置字体。");
                _world = BuiltinFallback();
            }
            else
            {
                PrewarmWorldText(_world);
            }
        }
        return _world;
    }

    /// <summary>把 root 及其子节点所有 uGUI Text（含 inactive）统一设为 UI 字体。</summary>
    public static void Apply(Transform root)
    {
        if (root == null) return;
        Font f = Get();
        foreach (var t in root.GetComponentsInChildren<Text>(true))
            t.font = f;
    }

    static Font BuiltinFallback()
    {
#if UNITY_2022_1_OR_NEWER
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
        return Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
    }

    /// <summary>
    /// legacy TextMesh（3D 世界文字）在 WebGL 上不会主动为动态字体请求字形，中文会空白/走白块兜底。
    /// 这里把世界空间文字用到的固定字符按实际字号预热进动态图集。
    /// </summary>
    static void PrewarmWorldText(Font font)
    {
        // 覆盖所有 legacy TextMesh 的实际字号：交易徽标 60/72、气泡 110、矿点 100、伤害数字 180、血条数字 120
        const string chars = "使用贩卖了购买铜铁石药品炸弹眩晕武器围墙修复器召唤令耐久强化攻击小型中型大型首领成功修理正在通过失败接受车辆 0123456789 x-";
        int[] sizes = { 60, 72, 100, 110, 120, 180 };
        foreach (int s in sizes)
            font.RequestCharactersInTexture(chars, s, FontStyle.Normal);
    }
}
