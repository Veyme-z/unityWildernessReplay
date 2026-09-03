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
| 浏览器标签标题 | `Regex.Replace(html, "<title>...", ...)` 那行 |
| 加载遮罩配色 / 全屏背景 | CSS 里 `#wr-splash` 的 `background` 渐变、`.wr-card` 卡片背景/边框色 |
| 加载卡片大标题（"荒野回放"） | 注入的 DOM：`<div class="wr-title">荒野回放</div>` |
| 阶段提示文字（加载中/启动引擎/构建场景） | `.wr-status` 元素（JS `__wrStatus` 更新）+ C# 侧 `ReplayEntry.cs` 的 `NotifyWebGLStatus(...)` 文案 |
| 进度条配色 / 条纹动效 | CSS `.wr-track` / `.wr-fill` 与 `@keyframes wrStripes` |
| 加载失败的红色提示 | JS `__wrLoadError` / C# `NotifyWebGLError(...)` 文案 |
| 画布是否铺满窗口 | `canvas.style.width = "100vw"` / `canvas.style.height = "100vh"` 两行 |
| 资源缓存版本戳 | 结尾 `buildStamp` 相关的 `.Replace(...)` 段（构建时间戳，不用动） |

**注意事项**：
- 加载遮罩是**全屏不透明**的（`#wr-splash`），会一直盖住画面，直到 C# 侧 `ReplayEntry` 把地图/角色/UI 都建好、调用 `NotifyWebGLReady()`（对应 JS `__wrGameReady`）才淡出——**所以不会闪 Unity 启动图标、也不会露出空场景**。淡出前所有加载阶段都被遮住。
- 进度条百分比由 Unity `progress` 回调驱动，它**只在下载 `.data` 时**报告 0→100%；之后引擎初始化/启动画面/建场景都没有回调，靠条纹动画 + 阶段文字撑住。
- 模板自带的 `#unity-loading-bar` 已被 `display:none` 隐藏（我们不再用它），别去改它。
- 所有替换都是"匹配到才替换，匹配不到自动跳过"，**不会改坏文件**；改完一定**重新构建**，改旧构建包里的 `index.html` 会被下次构建清掉。

## 五、常见问题

- **页面用域名打开、进度条卡一半**：浏览器 IndexedDB 缓存了残缺 `.data`。换无痕窗口/清该站点数据可临时解决；构建已关 `dataCaching`，重发新包后不再复发。
- **页面能开但回放不启动**：拉 `?replay=` 跨域被 CORS 拦截，目标回放服务器需带 `Access-Control-Allow-Origin` 头放行你的页面 origin（IP 能播、域名卡，多半就是只放行了 IP origin）。
- **地址栏"与此站点的连接不安全"**：页面 HTTPS 证书不受信任（自签名/内网 CA），属服务器证书问题，与 Unity 无关。

## 六、构建产物与 git

`Builds/WildernessReplay_WebGL_*/` 已在 `.gitignore` 中排除（`/Builds/WildernessReplay_WebGL_*/`），**不会**被 `git add` 提交。

## 七、包体积精简（GitHub 100MB 单文件上限）

`data.unityweb` 超过 GitHub 单文件 100MB 上限时，按下面思路处理（2026-08-31 已执行一次：**117M → 91M**）：

- **大贴图 Crunch 压缩（保持分辨率，画质几乎无损）**：给 `Assets/Resources/SciFiHeroPBR/Textures/` 下 9 张 4096² 贴图加了 **WebGL 平台覆写**（只在 WebGL 生效，桌面/编辑器完全不变）：`maxTextureSize=4096` + `crunchedCompression=true`，format 留 Automatic（Unity 自动选 DXT5/DXT1/法线专用格式）。对应 `.meta` 里出现 `buildTarget: WebGL` + `crunchedCompression: 1`。这些覆写已固化在 `.meta`，以后构建都会带上。
- **删除了用不到的 SRP zip**：`Assets/Resources/SciFiHeroPBR/SRP/`（URP/HDRP 管线包共 174M，本项目 Built-in 管线用不到），已从 git 删除（历史可找回）。
- **Raygeas 不在包里**：不在 `Resources/` 下且场景/脚本均未引用，构建自动排除，压缩它对包体积无帮助，只是仓库占用。
- **移出没用到的 SciFiHeroPBR 资源（Resources 下会被无条件全打包）**：运行时实际只加载 `SciFiHeroPBR/Prefabs/AssaultRifle01` 和 `SciFiHeroPBR/Materials/PBRMaskTint`。用 `AssetDatabase.GetDependencies(预制体, true)` 算出完整依赖闭包后，把 100 个未引用的文件（其他 17 个武器预制体、5 个多余材质、65 个非 `_ar` 动画变体、3 个手/霰弹/狙控制器、2 个网格、7 个演示场景，共 70M）移到 `Assets/Unused/SciFiHeroPBR/`（连同 `.meta` 一起移，GUID 保留，引用不丢）。不在 Resources 里且未被引用 → 不打进包。**注意**：移到哪都会跟着 GUID 引用被打包的，只有真正没被引用的才省得掉。

> 整体效果：`data.unityweb` **117M → 91M（Crunch）→ 81M（移出死重）**。
> 若之后想更小（更稳地避开 100MB / 加快首屏下载），把 WebGL 覆写的 `maxTextureSize` 从 4096 降到 2048 即可（预计 data → ~50-60M，回放视角下视觉差异极小）。贴图被重新导入弄丢覆写时，在 Inspector 里给 WebGL 平台勾选 Crunch 压缩即可恢复。
