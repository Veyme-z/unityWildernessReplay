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
        // WebGL 性能：锁定 60fps + 关闭垂直同步，防止浏览器无限制渲染超高帧率导致 CPU 满载发热
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;

        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;

        // BGM 系统（内部自处理 WebGL Autoplay 解锁 + 暂停冻结 + 昼夜 CrossFade）
        gameObject.AddComponent<BgmController>();

        // 任务卡片系统（每帧扫当前回合 teams[].task 判定状态，见 TaskBadgeManager）
        gameObject.AddComponent<TaskBadgeManager>();

        // 装甲车任务点驱动（「自进化类2」任务完成 → 卡车开到小贩前面）
        gameObject.AddComponent<MissionVehicleDriver>();

        // 回放全屏剧情视频：入夜首回合→ufo.mp4、任务点1领取→plane.mp4（数据驱动，见 ReplayCinematic）
        gameObject.AddComponent<ReplayCinematic>();
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

#if UNITY_WEBGL && !UNITY_EDITOR
        // === [新增] WebGL：支持 ?replay=URL 从远程加载回放 ===
        // 有参数时优先远程，失败不静默回退到本地（避免拿旧回放冒充成功）；
        // 无参数时走下方原有本地加载逻辑。
        string remoteUrl = GetReplayUrlFromQuery();
        if (!string.IsNullOrEmpty(remoteUrl))
        {
            yield return LoadRemoteText(remoteUrl, got =>
            {
                text = got;
                srcName = "?replay=" + remoteUrl;
            });
            if (string.IsNullOrEmpty(text))
            {
                Debug.LogError("[ReplayEntry] 远程回放加载失败：?replay=" + remoteUrl
                    + "。请确认该地址可访问（HTTP 200、非空）；跨源时服务器需带 Access-Control-Allow-Origin 头。");
                yield break;
            }
        }
#endif

        if (text == null && debugReplay != null)
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

            // ---- 左上角任务面板（推理类 + 长上下文，实时显示世界新闻） ----
            TaskPanelController.Create(player, TaskPanelKind.Reasoning);
            TaskPanelController.Create(player, TaskPanelKind.LongContext);

            // ---- 资源价格折线图卡片（右上角，任务面板下方） ----
            PriceChartCard.Create(player);

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
    /// WebGL：从当前页面 URL 查询参数 ?replay= 读取回放地址。
    /// 支持 http(s):// 绝对地址，也支持相对路径（相对当前页面解析）。
    /// </summary>
    static string GetReplayUrlFromQuery()
    {
        string path = QueryValue(Application.absoluteURL, "replay");
        if (string.IsNullOrEmpty(path)) return null;
        if (path.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase))
            return path;
        System.Uri baseUri;
        if (System.Uri.TryCreate(Application.absoluteURL, System.UriKind.Absolute, out baseUri))
            return new System.Uri(baseUri, path).ToString();
        return path;
    }

    /// <summary>
    /// 解析 URL 查询参数。
    /// </summary>
    static string QueryValue(string url, string key)
    {
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(key)) return null;
        int queryIndex = url.IndexOf('?');
        if (queryIndex < 0 || queryIndex == url.Length - 1) return null;
        string query = url.Substring(queryIndex + 1);
        int hashIndex = query.IndexOf('#');
        if (hashIndex >= 0) query = query.Substring(0, hashIndex);

        string[] pairs = query.Split('&');
        foreach (string pair in pairs)
        {
            int equalIndex = pair.IndexOf('=');
            if (equalIndex <= 0) continue;
            string pairKey = System.Uri.UnescapeDataString(pair.Substring(0, equalIndex));
            if (!string.Equals(pairKey, key, System.StringComparison.OrdinalIgnoreCase)) continue;
            return System.Uri.UnescapeDataString(pair.Substring(equalIndex + 1));
        }
        return null;
    }

    /// <summary>
    /// WebGL：从远程 URL 拉取回放文本。带超时 + 缓存破坏参数（防浏览器缓存旧回放）。
    /// 同步部分（构造 + 发起）包 try/catch，失败仅记日志（onGot 不回调）。
    /// </summary>
    IEnumerator LoadRemoteText(string url, System.Action<string> onGot)
    {
        string sep = url.Contains("?") ? "&" : "?";
        string requestUrl = url + sep + "t=" + System.DateTime.UtcNow.Ticks;

        UnityWebRequest req = null;
        UnityWebRequestAsyncOperation op = null;
        try
        {
            req = UnityWebRequest.Get(requestUrl);
            req.timeout = 60;
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
                Debug.LogError("[ReplayEntry] 远程回放读取失败: " + url
                    + " error=" + req.error + " code=" + req.responseCode);
            }
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
