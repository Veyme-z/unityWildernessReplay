using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 入口：启动时按优先级加载 replay 文件并组装播放器/UI/相机。
/// 加载顺序：
///   1. 挂到组件上的 debugReplay（TextAsset，Editor 里方便测试）
///   2. Application.persistentDataPath/replay.jsonl（运行时替换真实数据）
///   3. StreamingAssets/replay.txt（新格式真实 replay）
///   4. StreamingAssets/demo_replay.jsonl（内置演示）
/// 用法：把本脚本挂到场景任意 GameObject 上，或自动创建（见 EnsureInScene）。
/// </summary>
public class ReplayEntry : MonoBehaviour
{
    [Tooltip("(可选) Editor 测试用：直接把 replay 文本资产拖进来")]
    public TextAsset debugReplay;

    static ReplayEntry _instance;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    /// <summary>
    /// 自动启动：进 Play 模式后无需手动挂脚本，
    /// 会自动创建 ReplayEntry 并加载 replay。
    /// 若场景里已有手动挂好的 ReplayEntry 则跳过。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoBoot()
    {
        if (FindObjectOfType<ReplayEntry>() != null) return;
        var go = new GameObject("ReplayEntry (auto)");
        go.AddComponent<ReplayEntry>();
        DontDestroyOnLoad(go);
    }

    void Start()
    {
        EnsureInScene();   // 确保 EventSystem 存在（按钮可用）
        StartCoroutine(Load());
    }

    IEnumerator Load()
    {
        string text = null;
        string srcName = null;

        if (debugReplay != null)
        {
            text = debugReplay.text;
            srcName = "debugReplay";
        }
        else
        {
            // 真实数据优先：persistentDataPath/replay.jsonl
            string p = Path.Combine(Application.persistentDataPath, "replay.jsonl");
            if (File.Exists(p))
            {
                text = File.ReadAllText(p);
                srcName = p;
            }
        }

        if (text == null)
        {
            string rPath = Path.Combine(Application.streamingAssetsPath, "replay.txt");
            if (File.Exists(rPath))
            {
                text = File.ReadAllText(rPath);
                srcName = "replay.txt";
            }
        }

        if (text == null)
        {
            string sPath = Path.Combine(Application.streamingAssetsPath, "demo_replay.jsonl");
            if (File.Exists(sPath))
            {
                text = File.ReadAllText(sPath);
                srcName = "demo_replay.jsonl";
            }
        }

        if (text == null)
        {
            Debug.LogError("[ReplayEntry] 找不到 replay 文件。请把 replay.jsonl 放到 " + Application.persistentDataPath
                + "，或确保 StreamingAssets/replay.txt 存在");
            yield break;
        }

        ReplayData data;
        try
        {
            data = ReplayParser.Parse(text);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[ReplayEntry] 解析失败: " + e.Message);
            yield break;
        }

        // ---- 相机（场景已有 MainCamera 则复用） ----
        Camera cam = Camera.main;
        GameObject camGo;
        if (cam == null)
        {
            camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
        }
        else camGo = cam.gameObject;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.55f, 0.74f, 0.87f);
        cam.fieldOfView = 50;
        cam.nearClipPlane = 0.5f;
        cam.farClipPlane = 1200f;
        var rig = camGo.GetComponent<ReplayCameraRig>();
        if (rig == null) rig = camGo.AddComponent<ReplayCameraRig>();

        // ---- 智能导播相机管理器 ----
        var camMgrGo = new GameObject("CameraManager");
        var camMgr = camMgrGo.AddComponent<CameraManager>();
        camMgr.mainCam = cam;
        camMgr.mainRig = rig;

        // ---- 灯光 ----
        var lightGo = new GameObject("Sun");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        lightGo.transform.rotation = Quaternion.Euler(55, -25, 0);
        light.intensity = 1f;
        light.color = new Color(1f, 0.96f, 0.9f);

        // ---- 静态场景 ----
        SceneBuilder.Build(data.start.map);

        // ---- 播放器 ----
        var playerGo = new GameObject("ReplayPlayer");
        var player = playerGo.AddComponent<ReplayPlayer>();

        player.Setup(data, rig);

        // 注入 CameraManager 引用
        camMgr.Init(player);

        // ---- 顶部状态面板 ----
        HudController.Create(player);

        // ---- 左侧事件日志面板 ----
        var eventLog = EventLogPanelController.Create(player);
        player.SetEventLog(eventLog);

        // ---- 底部双队面板 + 时间轴 + 播放控制 ----
        PlaybackControlPanelController.Create(player);

        // ---- 固定相机初始视角（ReplayCameraRig 接管后负责平滑运镜）----
        camGo.transform.position = new Vector3(0f, 40f, -8f);
        camGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        player.SetPlaying(true);
        Debug.Log("[ReplayEntry] 已加载 " + srcName + "：" + data.rounds.Count + " 回合 · "
            + data.start.map.width + "×" + data.start.map.height + " 地图");
    }

    /// <summary>确保场景有 EventSystem（UGUI 按钮必需）</summary>
    static void EnsureInScene()
    {
        if (EventSystem.current != null) return;
        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
    }

    /// <summary>便捷方法：任何脚本调用后自动创建入口（可选）</summary>
    public static ReplayEntry Ensure()
    {
        var e = FindObjectOfType<ReplayEntry>();
        if (e != null) return e;
        var go = new GameObject("ReplayEntry");
        return go.AddComponent<ReplayEntry>();
    }
}
