# WebGL 构建脚本说明

> 最后更新：2026-08-28

## 一、脚本位置与作用

- 文件：`Assets/Editor/WildernessReplayWebGLBuilder.cs`
- 入口菜单：**`Tools → WildernessReplay`**
- 作用：**一键出 WebGL 构建包**，构建时自动固化关键设置、并给 `index.html` 打上页面样式补丁。

菜单有两个入口：

| 菜单项 | 作用 |
|---|---|
| `Build WebGL` | 完整一键构建（固化设置 + 构建 + 页面补丁 + 写 README） |
| `仅应用 WebGL 构建设置` | 只固化设置不动手构建，之后可用 Unity 原生 Build Settings 构建 |

## 二、自动固化的构建设置

每次构建都会写入 ProjectSettings（之后用 GUI 构建也生效）：

| 设置 | 值 | 原因 |
|---|---|---|
| `PlayerSettings.WebGL.dataCaching` | `false` | 防止 IndexedDB 缓存残缺 `.data` 导致**进度条卡一半**（本项目经典问题） |
| `PlayerSettings.WebGL.compressionFormat` | `Gzip` | 包体积小 |
| `PlayerSettings.WebGL.decompressionFallback` | `true` | 任意静态服务器都能直接跑，无需配 Content-Encoding |
| `PlayerSettings.insecureHttpOption` | `AlwaysAllowed` | 允许拉取 `http://` 内网回放 |
| `PlayerSettings.stripEngineCode` | `false` | 关引擎代码裁剪，防运行时组件缺失 |
| `PlayerSettings.WebGL.maximumMemorySize` | `2048` | 稳定申请 wasm 内存 |

## 三、使用步骤

1. **首次使用**：点 `Tools → WildernessReplay → Build WebGL`。如果当前还不是 WebGL 平台，脚本会先切换平台并弹窗提示——**编辑器会重载**，重载完成后**再点一次**同一菜单即开始构建。
2. 构建完成后输出到 `Builds/WildernessReplay_WebGL_<日期>/`（同一天重复构建会清空重写），Console 打印结果与包大小。
3. **部署**：把整个文件夹（含 `index.html` + `Build/` + `StreamingAssets/` + `TemplateData/`）原样放到 HTTP 服务器，**不要只拷贝 Build 目录，也不要双击 index.html**。
4. 访问方式：
   - 包内回放：`http://服务器/`
   - 远程回放：`http://服务器/?replay=http://回放地址/replay.txt`
   - 相对回放：`http://服务器/?replay=/StreamingAssets/replay.txt`

## 四、如何修改页面样式（重点）

构建后生成的 `index.html` 会由脚本里的 **`PatchIndexHtml`** 方法自动打补丁。**想改样式，直接改这个方法的字符串，然后重新 Build WebGL 即可**——改源码文件不生效，必须重新构建。

各部分对应位置：

| 想改什么 | 在 `PatchIndexHtml` 里改哪里 |
|---|---|
| 浏览器标签标题 | `Regex.Replace(html, "<title>...", "<title>荒野回放 · Wilderness Replay</title>", ...)` 那行 |
| 背景色 / 加载卡片 / 进度条配色 | 注入的 `<style>...</style>` 块（CSS 字符串），如 `#unity-loading-bar`、`#unity-progress-bar-full`、`background: #0b1020` 等 |
| 加载中的标题 / 副标题文字 | `<div class="wilderness-loading-title">荒野回放</div>` 和 `wilderness-loading-sub` 那两行 |
| 加载百分比文字 / 进度动效 | `wilderness-progress-text`（JS 实时更新百分比）与 `#unity-progress-bar-full` 的条纹动画（`@keyframes wildernessStripes`） |
| 画布是否铺满窗口 | `canvas.style.width = "100vw"` / `canvas.style.height = "100vh"` 两行 |
| 资源缓存版本戳 | 结尾 `buildStamp` 相关的 `.Replace(...)` 段（构建时间戳，不用动） |

**注意事项**：
- 所有替换都是"匹配到才替换，匹配不到自动跳过"，**不会改坏文件**。
- 进度条的真实百分比由 Unity 的 `progress` 回调驱动，它**只在下载 `.data` 时**报告 0→100%，框架/wasm 编译阶段没有回调；脚本已加条纹动画保证这些阶段进度条依然可见。
- 想加"全屏品牌封面 + 加载完淡出"这类更复杂的启动页，在 `PatchIndexHtml` 里参照 Lychee 的 `#lychee-splash` 写法追加即可。
- 改完一定**重新构建**，改旧构建包里的 `index.html` 会被下次构建清掉。

## 五、常见问题

- **页面用域名打开、进度条卡一半**：浏览器 IndexedDB 缓存了残缺 `.data`。换无痕窗口/清该站点数据可临时解决；构建已关 `dataCaching`，重发新包后不再复发。
- **页面能开但回放不启动**：拉 `?replay=` 跨域被 CORS 拦截，目标回放服务器需带 `Access-Control-Allow-Origin` 头放行你的页面 origin（IP 能播、域名卡，多半就是只放行了 IP origin）。
- **地址栏"与此站点的连接不安全"**：页面 HTTPS 证书不受信任（自签名/内网 CA），属服务器证书问题，与 Unity 无关。

## 六、构建产物与 git

`Builds/WildernessReplay_WebGL_*/` 已在 `.gitignore` 中排除（`/Builds/WildernessReplay_WebGL_*/`），**不会**被 `git add` 提交。
