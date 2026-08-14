using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 统一中文字体入口：全项目 uGUI Text / TextMesh 都从这里取字体。
/// WebGL 没有系统 CJK 字形，内置 LegacyRuntime/Arial 会让中文变空白，
/// 因此统一改用打包进 Resources 的 NotoSansSC-Regular（保持 Dynamic，生僻字也能动态出字形）。
/// </summary>
public static class UiFonts
{
    static Font _font;
    static bool _tried;   // 只尝试加载一次，避免每次 Get 都重复 Resources.Load

    /// <summary>惰性加载并缓存中文字体；失败时 LogError 并回退到内置字体（不返回 null，不重复加载）。</summary>
    public static Font Get()
    {
        if (_font == null && !_tried)
        {
            _tried = true;
            _font = Resources.Load<Font>("Fonts/NotoSansSC-Regular");
            if (_font == null)
            {
                Debug.LogError("[UiFonts] 找不到中文字体 Fonts/NotoSansSC-Regular，回退到内置字体。");
#if UNITY_2022_1_OR_NEWER
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
                _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
            }
        }
        return _font;
    }

    /// <summary>把 root 及其子节点所有 uGUI Text（含 inactive）统一设为中文字体。</summary>
    public static void Apply(Transform root)
    {
        if (root == null) return;
        Font f = Get();
        foreach (var t in root.GetComponentsInChildren<Text>(true))
            t.font = f;
    }
}
