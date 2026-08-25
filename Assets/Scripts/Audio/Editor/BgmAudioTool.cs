#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// BGM 选段预览工具（编辑器模式专用）：
/// - 试听 bgm_day / bgm_night，拖进度条随意选段
/// - 把当前位置设为该曲的「起始播放偏移」并保存到 Assets/Resources/Audio/BGM/BgmAudioConfig.asset
/// - BgmController 运行时读取偏移：音乐从所选位置开始，播到所选片段结尾后回到该位置循环
/// 菜单：Window → BGM 选段工具
/// </summary>
public class BgmAudioTool : EditorWindow
{
    const string DayBase    = "bgm_day";
    const string NightBase  = "bgm_night";
    const string ConfigPath = "Assets/Resources/Audio/BGM/BgmAudioConfig.asset";

    /// <summary>按基础文件名找 BGM 素材（不挑扩展名：.ogg/.wav/.mp3 均可）。</summary>
    static AudioClip LoadClipByBaseName(string baseName)
    {
        foreach (var guid in AssetDatabase.FindAssets(baseName + " t:AudioClip"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) == baseName)
                return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
        return null;
    }

    BgmAudioConfig _config;
    AudioSource _preview;   // 编辑模式下试听用（隐藏 GameObject）
    int _sel;               // 0 = 白天，1 = 夜晚

    [MenuItem("Window/BGM 选段工具")]
    static void Open()
    {
        GetWindow<BgmAudioTool>("BGM 选段工具");
    }

    void OnEnable()
    {
        _config = Resources.Load<BgmAudioConfig>("Audio/BGM/BgmAudioConfig");
        if (_config == null) _config = ScriptableObject.CreateInstance<BgmAudioConfig>();
        EditorApplication.update += OnTick;   // 播放中驱动窗口重绘，进度条实时走动
    }

    void OnDisable()
    {
        EditorApplication.update -= OnTick;
        if (_preview != null)
        {
            _preview.Stop();
            DestroyImmediate(_preview.gameObject);
            _preview = null;
        }
    }

    void OnTick()
    {
        if (_preview != null && _preview.isPlaying) Repaint();
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("BGM 选段工具", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("试听两首 BGM，拖进度条选段，把喜欢的部分设为起始播放偏移。", EditorStyles.miniLabel);
        EditorGUILayout.Space();

        int sel = EditorGUILayout.Popup("选中曲目", _sel, new[] { "bgm_day.ogg（白天·海盗冒险）", "bgm_night.ogg（夜晚·赛博朋克）" });
        if (sel != _sel)
        {
            StopPreview();
            _sel = sel;
        }

        AudioClip clip = LoadClipByBaseName(_sel == 0 ? DayBase : NightBase);
        if (clip == null)
        {
            EditorGUILayout.HelpBox("未找到素材：Assets/Resources/Audio/BGM/ 下需有 bgm_day 与 bgm_night（.ogg/.wav/.mp3 均可）", MessageType.Warning);
            return;
        }

        // 起始偏移编辑（含自动保存到配置对象）
        float maxStart = Mathf.Max(0f, clip.length - 0.5f);
        float start = _sel == 0 ? _config.dayStartTime : _config.nightStartTime;
        start = Mathf.Clamp(EditorGUILayout.FloatField("起始偏移（秒）", start), 0f, maxStart);
        if (_sel == 0) _config.dayStartTime = start; else _config.nightStartTime = start;

        EditorGUILayout.Space();

        // 预览控制
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("▶ 从偏移试听")) PlayPreview(clip, start);
        if (GUILayout.Button(_preview != null && _preview.isPlaying ? "⏸ 暂停" : "⏵ 继续")) TogglePause();
        if (GUILayout.Button("⏹ 停止")) StopPreview();
        if (GUILayout.Button("🎯 当前位置设为起始")) SetStartFromPosition(clip);
        EditorGUILayout.EndHorizontal();

        // 进度条（scrub）：拖动即选段
        float now = (_preview != null && _preview.clip == clip) ? _preview.time : 0f;
        EditorGUI.BeginChangeCheck();
        float nt = GUILayout.HorizontalSlider(now, 0f, clip.length);
        if (EditorGUI.EndChangeCheck() && _preview != null && _preview.clip == clip)
            _preview.time = nt;
        EditorGUILayout.LabelField(
            FormatTime(now) + " / " + FormatTime(clip.length)
            + (_preview != null && _preview.clip == clip && _preview.isPlaying ? "  ·  播放中" : ""),
            EditorStyles.miniLabel);

        EditorGUILayout.Space();

        // 保存 / 重置
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("💾 保存配置")) SaveConfig();
        if (GUILayout.Button("↺ 全部归零（从头播）"))
        {
            _config.dayStartTime = 0f;
            _config.nightStartTime = 0f;
            SaveConfig();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("当前配置：白天偏移 " + FormatTime(_config.dayStartTime)
            + " · 夜晚偏移 " + FormatTime(_config.nightStartTime), EditorStyles.miniLabel);
        EditorGUILayout.LabelField("提示：保存后进入 Play 模式，白天/夜晚音乐会从对应偏移开始并循环播放所选片段。", EditorStyles.miniLabel);
    }

    void PlayPreview(AudioClip clip, float start)
    {
        EnsurePreview();
        _preview.clip = clip;
        _preview.loop = true;
        _preview.time = Mathf.Clamp(start, 0f, Mathf.Max(0f, clip.length - 0.1f));
        _preview.Play();
    }

    void TogglePause()
    {
        if (_preview == null) return;
        if (_preview.isPlaying) _preview.Pause();
        else _preview.UnPause();
    }

    void StopPreview()
    {
        if (_preview != null) _preview.Stop();
    }

    void SetStartFromPosition(AudioClip clip)
    {
        float t = (_preview != null && _preview.clip == clip) ? _preview.time : 0f;
        t = Mathf.Clamp(t, 0f, Mathf.Max(0f, clip.length - 0.5f));
        if (_sel == 0) _config.dayStartTime = t; else _config.nightStartTime = t;
        SaveConfig();
    }

    void EnsurePreview()
    {
        if (_preview != null) return;
        var go = new GameObject("__BgmAudioPreview__");
        go.hideFlags = HideFlags.HideAndDontSave;   // 隐藏且不入场景保存
        _preview = go.AddComponent<AudioSource>();
    }

    void SaveConfig()
    {
        if (_config == null) _config = ScriptableObject.CreateInstance<BgmAudioConfig>();
        var existing = AssetDatabase.LoadAssetAtPath<BgmAudioConfig>(ConfigPath);
        if (existing != null)
        {
            // 把当前编辑值同步到已存在的资产实例
            existing.dayStartTime = _config.dayStartTime;
            existing.nightStartTime = _config.nightStartTime;
            _config = existing;
        }
        else
        {
            AssetDatabase.CreateAsset(_config, ConfigPath);
        }
        EditorUtility.SetDirty(_config);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[BgmAudioTool] 已保存 BGM 起始偏移：白天 " + FormatTime(_config.dayStartTime)
            + " · 夜晚 " + FormatTime(_config.nightStartTime));
    }

    static string FormatTime(float s)
    {
        s = Mathf.Max(0f, s);
        return string.Format("{0}:{1:D2}.{2}", (int)(s / 60f), (int)(s % 60f), (int)((s * 10f) % 10f));
    }
}
#endif
