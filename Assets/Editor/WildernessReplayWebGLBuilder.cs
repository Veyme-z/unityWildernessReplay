using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// WildernessReplay 的 WebGL 一键构建。
///
/// 用法（菜单）：
///   Tools → WildernessReplay → Build WebGL
/// 首次使用若当前不是 WebGL 平台，脚本会先切换平台（编辑器会重载），
/// 重载完成后再次点击该菜单即可出包。
/// 也可以先点「仅应用 WebGL 构建设置」，再用 Unity 原生 Build Settings 构建。
///
/// 自动固化的关键设置（避免踩坑）：
/// - dataCaching = false     ：防止 IndexedDB 缓存残缺 .data 导致进度条卡死（本项目经典问题）
/// - Gzip + decompressionFallback：包体积小，且任意静态服务器（python -m http.server 等）都能直接跑
/// - insecureHttpOption = AlwaysAllowed：允许拉取 http:// 内网回放
/// - stripEngineCode = false ：关掉引擎代码裁剪，防止运行时组件缺失
/// - maximumMemorySize = 2048：稳定申请 wasm 内存
///
/// 页面样式（index.html）通过 PatchIndexHtml 在构建后修改：
/// 标题、全窗画布、加载卡片、进度条配色、资源 URL 加版本戳防缓存。
/// 想改样式直接编辑下面的 PatchIndexHtml 里的 CSS 字符串即可。
/// </summary>
public static class WildernessReplayWebGLBuilder
{
    const string MenuRoot = "Tools/WildernessReplay";

    [MenuItem(MenuRoot + "/Build WebGL")]
    public static void BuildWebGL()
    {
        // 1. 固化 WebGL 构建设置（持久化到 ProjectSettings，之后用 GUI 构建也生效）
        ApplyWebGLSettings();

        // 2. 确保当前是 WebGL 平台（切换会触发域重载）
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
        {
            bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);
            if (switched)
            {
                EditorUtility.DisplayDialog("WildernessReplay 构建",
                    "已切换到 WebGL 平台，编辑器即将重载。\n\n重载完成后请再次点击：\nTools → WildernessReplay → Build WebGL",
                    "确定");
            }
            else
            {
                EditorUtility.DisplayDialog("WildernessReplay 构建",
                    "切换到 WebGL 失败。\n请确认已安装 WebGL 构建模块：\nWindow → Package Manager → 右上角 Modules → WebGL Build Support",
                    "确定");
            }
            return; // 切换平台后编辑器会重载，直接返回
        }

        // 3. 取构建场景（用 Build Settings 里启用的场景）
        string[] scenes = GetBuildScenes();
        if (scenes == null || scenes.Length == 0)
        {
            Debug.LogError("[WildernessReplayWebGL] 没有可构建的场景，请在 Build Settings 里至少启用一个场景。");
            return;
        }

        // 4. 输出目录（按天命名，同一天重复构建会清空重写）
        string outputDir = "Builds/WildernessReplay_WebGL_" + DateTime.Now.ToString("yyyyMMdd");
        if (Directory.Exists(outputDir)) Directory.Delete(outputDir, true);
        Directory.CreateDirectory(outputDir);

        // 5. 构建
        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            target = BuildTarget.WebGL,
            locationPathName = outputDir,
            options = BuildOptions.None
        };

        Debug.Log("[WildernessReplayWebGL] 开始构建 → " + outputDir
            + "\n场景: " + string.Join(", ", scenes));
        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;
        Debug.Log("[WildernessReplayWebGL] 构建结果: " + summary.result
            + "，大小 " + (summary.totalSize / 1024f / 1024f).ToString("F1") + " MB"
            + "，耗时 " + summary.totalTime.TotalSeconds.ToString("F0") + "s"
            + "，错误 " + summary.totalErrors);

        if (summary.result == BuildResult.Succeeded)
        {
            PatchIndexHtml(outputDir);
            WriteReadme(outputDir);
            Debug.Log("[WildernessReplayWebGL] 构建成功，输出目录：" + Path.GetFullPath(outputDir));
        }
        else
        {
            Debug.LogError("[WildernessReplayWebGL] 构建失败：" + summary.result);
        }
    }

    [MenuItem(MenuRoot + "/仅应用 WebGL 构建设置")]
    public static void ApplySettingsMenu()
    {
        ApplyWebGLSettings();
        Debug.Log("[WildernessReplayWebGL] WebGL 构建设置已应用（dataCaching=false、Gzip、insecureHttpOption=AlwaysAllowed 等）。");
    }

    /// <summary>固化 WebGL 相关 PlayerSettings（写入 ProjectSettings，GUI 构建也会带上）。</summary>
    static void ApplyWebGLSettings()
    {
        PlayerSettings.WebGL.dataCaching = false;
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
        PlayerSettings.WebGL.maximumMemorySize = 2048;
        PlayerSettings.stripEngineCode = false;
        PlayerSettings.runInBackground = true;
    }

    static string[] GetBuildScenes()
    {
        var scenes = new List<string>();
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled && File.Exists(scene.path)) scenes.Add(scene.path);
        }
        return scenes.ToArray();
    }

    /// <summary>
    /// 构建完成后修改 index.html：品牌标题、全窗画布、深色加载卡片、进度条配色、
    /// 资源 URL 加版本戳（防浏览器缓存旧资源）。想自定义页面样式就改这里。
    /// 模板片段匹配不到的替换会自动跳过（不会改坏文件）。
    /// </summary>
    static void PatchIndexHtml(string outputDir)
    {
        string indexPath = Path.Combine(outputDir, "index.html");
        if (!File.Exists(indexPath)) return;

        string html = File.ReadAllText(indexPath);
        string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");

        // --- 标题 ---
        html = Regex.Replace(html, "<title>.*?</title>",
            "<title>荒野回放 · Wilderness Replay</title>", RegexOptions.Singleline);

        // --- 全窗画布 + 深色背景 + 隐藏 footer/logo + 居中加载卡片（注入 CSS） ---
        html = html.Replace("</head>",
            "    <style>\n" +
            "      html, body { margin: 0; width: 100%; height: 100%; overflow: hidden; background: #0b1020; }\n" +
            "      #unity-container.unity-desktop { width: 100vw; height: 100vh; background: #0b1020; }\n" +
            "      #unity-canvas { background: #000; }\n" +
            "      #unity-footer { display: none; }\n" +
            "      #unity-warning { position: fixed; top: 16px; left: 50%; transform: translateX(-50%); max-width: min(720px, calc(100vw - 32px)); z-index: 20; color: #ffd9a0; background: rgba(40,20,10,0.94); border: 1px solid #b8862f; border-radius: 6px; padding: 10px 16px; font: 500 13px/1.6 'Microsoft YaHei', sans-serif; }\n" +
            "      #unity-loading-bar { position: fixed; left: 50%; top: 50%; transform: translate(-50%, -50%); width: min(500px, calc(100vw - 48px)); padding: 28px 34px; background: rgba(16,22,40,0.96); border: 1px solid #2c3a5c; border-radius: 10px; box-shadow: 0 26px 80px rgba(0,0,0,0.6); text-align: center; }\n" +
            "      #unity-logo { display: none; }\n" +
            "      .wilderness-loading-title { color: #e8edf7; font: 700 24px/1.3 'Microsoft YaHei', 'Noto Sans CJK SC', sans-serif; }\n" +
            "      .wilderness-loading-sub { margin-top: 8px; color: #8fa3c8; font: 500 13px/1.6 'Microsoft YaHei', 'Noto Sans CJK SC', sans-serif; }\n" +
            "      #unity-progress-bar-empty { width: 100%; height: 8px; margin-top: 22px; background: rgba(255,255,255,0.1); border: 1px solid rgba(255,255,255,0.12); border-radius: 999px; overflow: hidden; }\n" +
            "      #unity-progress-bar-full { height: 100%; min-width: 4px; background: linear-gradient(90deg, #1e6fd9, #38c8ff); border-radius: 999px; background-image: linear-gradient(45deg, rgba(255,255,255,0.2) 25%, transparent 25%, transparent 50%, rgba(255,255,255,0.2) 50%, rgba(255,255,255,0.2) 75%, transparent 75%); background-size: 26px 26px; animation: wildernessStripes 0.9s linear infinite; transition: width 0.3s ease; }\n" +
            "      @keyframes wildernessStripes { 0% { background-position: 0 0; } 100% { background-position: 26px 0; } }\n" +
            "    </style>\n" +
            "  </head>");

        // --- 加载卡片标题（插到 unity-logo 之后） ---
        html = html.Replace("<div id=\"unity-logo\"></div>",
            "<div id=\"unity-logo\"></div>" +
            "<div class=\"wilderness-loading-title\">荒野回放</div>" +
            "<div class=\"wilderness-loading-sub\" id=\"wilderness-progress-text\">正在加载回放资源… 0%</div>");

        // --- 画布铺满窗口 ---
        html = html.Replace("canvas.style.width = \"960px\";", "canvas.style.width = \"100vw\";");
        html = html.Replace("canvas.style.height = \"600px\";", "canvas.style.height = \"100vh\";");

        // --- 加载进度：真实百分比 + 进度文字（Unity 的 progress 回调驱动） ---
        // Unity 的 progress 回调只在下载 .data 时报告 0→100%；框架/wasm 阶段无回调，
        // 所以保留已显示的百分比 + 条纹动画，保证任何阶段进度条都可见。
        html = html.Replace(
            "progressBarFull.style.width = 100 * progress + \"%\";",
            "progressBarFull.style.width = Math.max(2, Math.round(progress * 100)) + \"%\";\n" +
            "        var _wt = document.getElementById('wilderness-progress-text');\n" +
            "        if (_wt) _wt.textContent = progress >= 1 ? '正在启动引擎…' : '正在加载回放资源… ' + Math.round(progress * 100) + '%';");

        // --- 资源 URL 加版本戳（防浏览器缓存旧包；模板片段匹配不上则整体跳过，避免 buildStamp 未定义） ---
        if (html.Contains("var loaderUrl = buildUrl +"))
        {
            html = html.Replace(
                "var loaderUrl = buildUrl + ",
                "var buildStamp = \"" + stamp + "\";\n      var loaderUrl = buildUrl + ");
            html = html.Replace(".loader.js\";", ".loader.js\" + \"?v=\" + buildStamp;");
            html = html.Replace(".data.unityweb\",", ".data.unityweb\" + \"?v=\" + buildStamp,");
            html = html.Replace(".framework.js.unityweb\",", ".framework.js.unityweb\" + \"?v=\" + buildStamp,");
            html = html.Replace(".wasm.unityweb\",", ".wasm.unityweb\" + \"?v=\" + buildStamp,");
        }

        File.WriteAllText(indexPath, html);
        Debug.Log("[WildernessReplayWebGL] index.html 已补丁（标题/全窗/加载样式/cache-bust）。");
    }

    /// <summary>写一份使用说明到构建包。</summary>
    static void WriteReadme(string outputDir)
    {
        string text =
            "荒野回放 WebGL 构建包使用说明\n" +
            "==============================\n\n" +
            "不要直接双击 index.html，也不要只拷贝 Build 目录。整个文件夹原样放到 HTTP 服务器上访问。\n\n" +
            "目录必须包含：\n" +
            "- index.html\n- Build/\n- StreamingAssets/\n- TemplateData/\n\n" +
            "访问方式：\n" +
            "- 包内回放：http://服务器/（读 StreamingAssets/replay.txt）\n" +
            "- 远程回放：http://服务器/?replay=http://回放地址/replay.txt\n" +
            "- 相对回放：http://服务器/?replay=/StreamingAssets/replay.txt\n\n" +
            "回放加载不了时：\n" +
            "- 页面用域名打开、进度条卡住 → 浏览器缓存问题。先清该站点数据或换无痕；本包已关 dataCaching，重发新包后自然不再复发。\n" +
            "- 页面能开但回放不启动 → F12 Console 看 CORS 报错（Access-Control-Allow-Origin），目标回放服务器需放行你的页面 origin。\n" +
            "- 页面和回放都走 http:// → 已开启 insecureHttpOption=AlwaysAllowed，无需额外配置。\n";
        File.WriteAllText(Path.Combine(outputDir, "README_使用说明.txt"), text);
    }
}
