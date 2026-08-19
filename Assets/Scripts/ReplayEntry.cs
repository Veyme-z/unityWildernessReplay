using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;

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

#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL：File API 不可用，改用 UnityWebRequest 从 StreamingAssets 拉取。
        // 使用「相对当前网页」的相对路径（协议自动跟随页面），避免 http/https 混用触发
        // "Insecure connection not allowed"。同步异常（构造/发起请求）在 LoadWebText 内捕获，
        // 异常仅记日志并兜底走 demo，绝不让异常中断游戏初始化。
        if (text == null)
        {
            string url = RelativeStreamingUrl("replay.txt");
            yield return LoadWebText(url, got =>
            {
                text = got;
                srcName = "replay.txt";
            });
        }

        if (text == null)
        {
            string url = RelativeStreamingUrl("demo_replay.jsonl");
            yield return LoadWebText(url, got =>
            {
                text = got;
                srcName = "demo_replay.jsonl";
            });
        }
#else
        // 编辑器 / Standalone：保持原样。
        if (text == null)
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
#endif

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

        try
        {
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

            // ---- 昼夜控制器（复用已有 "Sun" 方向光，不新建） ----
            var dncGo = new GameObject("DayNightController");
            dncGo.AddComponent<DayNightController>();

            // ---- 顶部状态面板 ----
            HudController.Create(player);

            // ---- 左侧事件日志面板 ----
            var eventLog = EventLogPanelController.Create(player);
            if (eventLog != null) player.SetEventLog(eventLog);

            // ---- 底部双队面板 + 时间轴 + 播放控制 ----
            PlaybackControlPanelController.Create(player);

            // ---- 右侧任务面板（占位：推理类任务 + 官方消息） ----
            TaskPanelController.Create(player);

            // ---- 固定相机初始视角（ReplayCameraRig 接管后负责平滑运镜）----
            camGo.transform.position = new Vector3(0f, 40f, -8f);
            camGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            player.SetPlaying(true);
            Debug.Log("[ReplayEntry] 已加载 " + srcName + "：" + data.rounds.Count + " 回合 · "
                + data.start.map.width + "×" + data.start.map.height + " 地图");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }
    }

    /// <summary>
    /// WebGL 用 UnityWebRequest 拉取文本。同步部分（构造 + 发起）包 try/catch，
    /// 捕获后仅记日志并正常结束协程（onGot 不会被调用 → 调用方走下一个兜底分支）。
    /// 注意：yield return 不能出现在带 catch 的 try 块内（C# CS1626），
    /// 所以这里先把请求建好，再在 try 外 yield 等待结果。
    /// </summary>
    IEnumerator LoadWebText(string url, System.Action<string> onGot)
    {
        UnityWebRequest req = null;
        UnityWebRequestAsyncOperation op = null;
        try
        {
            req = UnityWebRequest.Get(url);
            op = req.SendWebRequest();
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
            if (req != null) req.Dispose();
            yield break;
        }

        yield return op;

        using (req)
        {
            if (req.result == UnityWebRequest.Result.Success && !string.IsNullOrEmpty(req.downloadHandler.text))
            {
                onGot(req.downloadHandler.text);
            }
            else
            {
                Debug.LogError("[ReplayEntry] WebGL 读取失败: " + url + " error=" + req.error);
            }
        }
    }

    /// <summary>
    /// 把 StreamingAssets 路径归一化为「相对当前网页」的相对 URL：
    /// 去掉可能的 http(s)://host 前缀（WebGL 下 Application.streamingAssetsPath 可能返回绝对地址），
    /// 让协议自动跟随当前页面，避免 http/https 混用触发 "Insecure connection not allowed"。
    /// </summary>
    static string RelativeStreamingUrl(string fileName)
    {
        string path = Application.streamingAssetsPath;
        int schemeIdx = path.IndexOf("://", System.StringComparison.Ordinal);
        if (schemeIdx >= 0)
        {
            int hostEnd = path.IndexOf('/', schemeIdx + 3);
            path = hostEnd >= 0 ? path.Substring(hostEnd + 1) : "";
        }
        else
        {
            path = path.TrimStart('/');   // 相对路径去掉可能的开头斜杠
        }
        path = path.TrimEnd('/');
        return string.IsNullOrEmpty(path) ? fileName : path + "/" + fileName;
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
