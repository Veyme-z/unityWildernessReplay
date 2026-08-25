using UnityEngine;

/// <summary>
/// BGM 起始偏移配置：编辑器「BGM 选段工具」写入，BgmController 运行时读取。
/// 资产放在 Assets/Resources/Audio/BGM/BgmAudioConfig.asset，缺省则从头播放。
/// </summary>
[CreateAssetMenu(fileName = "BgmAudioConfig", menuName = "Audio/BGM 配置")]
public class BgmAudioConfig : ScriptableObject
{
    [Header("起始播放偏移（秒）：音乐从该位置开始，播到所选片段结尾后回到该位置循环")]

    [Tooltip("白天曲 bgm_day.ogg 起始偏移")]
    public float dayStartTime;

    [Tooltip("夜晚曲 bgm_night.ogg 起始偏移")]
    public float nightStartTime;
}
