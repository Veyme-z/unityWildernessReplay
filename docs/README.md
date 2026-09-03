# 项目开发文档

> 最后更新：2026-08-24
> 配套文档索引见本文末尾

---

## 一、项目简介

一句话：**一个 3D 回放播放器**，用来可视化展示对局录像。

比赛是两支队伍在同一张 41×32 格子的地图上对战——白天采集资源、建造防御工事，晚上抵御野兽进攻。比赛结束后系统会输出一份 `.jsonl` 格式的录像文件（`replay.txt`），本项目的工作就是把这个录像**用 3D 画面回放出来**，包括地形、角色、建筑、昼夜变化、战斗特效等。

技术栈：**Unity 2022.3.62f3c1**，Built-in Render Pipeline，纯 C# 脚本，无第三方框架。

### 开发工具说明

本项目借助 **Unity MCP**（Model Context Protocol）进行 AI 辅助开发。配置可参考教程：

> 【Unity-AI开发篇】| Unity-MCP最新指南：让AI接管游戏开发
> https://blog.csdn.net/zhangay1998/article/details/158918650


---

## 二、快速开始

### 2.1 环境准备

1. **注册 Unity 账号并激活 License**
   - 访问 https://id.unity.com 注册账号
   - 选 **Unity Personal**（免费版）→ 登录账号激活
   - **必须先完成这一步**，否则无法下载编辑器和打开项目

2. 安装 **Unity Hub**注意不是团结引擎

3. 通过 Unity Hub 安装 **Unity 2022.3.62f3c1**
   - Unity Hub → **Installs** → **Install Editor**
   - 如果列表里没有这个精确版本，可以在（https://unity.com/download）手动选中下载
   - 模块勾选：**WebGL Build Support**（构建网页版必需）；其他默认即可

4. 用 Unity Hub 打开本项目文件夹（`Open → 选项目根目录`），首次打开会导入所有资源，**需要等几分钟**，耐心等待

### 2.2 打开场景并运行

1. Unity 打开后，在 **Project 窗口**（左下角）导航到 `Assets/`，找到 `unknow.unity`（这是唯一的场景文件）
2. **双击** `unknow.unity` 打开场景
3. 点击顶部的 **▶ Play 按钮**，即可看到 3D 回放画面
4. 底部面板有播放/暂停/倍速/进度条等控制按钮

> **注意**：如果打开后看到一片空白或报错，检查 Console 窗口（Window → General → Console）的红色错误信息。最常见的原因是 Unity 版本不对或资源导入不完整（删掉项目根目录的 `Library/` 文件夹重新打开即可）。

### 2.3 构建 WebGL 网页版

**推荐用一键构建脚本**（自动配好 WebGL 关键设置 + 页面样式补丁，详见 [WebGL构建脚本说明.md](WebGL构建脚本说明.md)）：

1. 菜单 **Tools → WildernessReplay → Build WebGL**
   - 首次若当前还不是 WebGL 平台，脚本会先切换平台 → 编辑器重载 → 弹窗提示重载后**再点一次**同一菜单
2. 构建输出到 `Builds/WildernessReplay_WebGL_<日期>/`（同一天重复构建会清空重写），Console 打印结果与包大小
3. 也可以点 **Tools → WildernessReplay → 仅应用 WebGL 构建设置** 固化设置后，再用原生 **File → Build Settings** 手动构建

构建产物是一个文件夹，结构如下：

```
Builds/WildernessReplay_WebGL_<日期>/
├── index.html          ← 入口页面（构建时已补丁：全窗画布 + 加载进度条样式）
├── Build/
│   ├── xxx.data.unityweb    ← 游戏资源数据（贴图/模型/音频，最大；注意 GitHub 单文件 100MB 上限）
│   ├── xxx.framework.js.unityweb ← Unity 引擎运行时
│   ├── xxx.loader.js        ← 加载脚本
│   └── xxx.wasm.unityweb    ← WebAssembly 代码
├── StreamingAssets/     ← replay.txt、任务视频（WebGL 下是独立文件，不走 System.IO）
├── TemplateData/        ← 模板样式
└── README_使用说明.txt
```

**部署与访问**：

- 用任意 HTTP 服务器托管**整个文件夹**（index.html + Build/ + StreamingAssets/ + TemplateData/ 都要有）
  > ⚠️ **不能直接双击 `index.html` 打开**，浏览器 CORS 会阻止加载 `.unityweb`，必须通过 HTTP 服务访问。
- 包内回放：`http://服务器/`（读 `StreamingAssets/replay.txt`）
- 远程回放：`http://服务器/?replay=http://回放地址/replay.txt`（走 URL 参数，无需重新打包）
- 改 replay 走远程参数时，换 replay 只需换 URL，不用每次重新 Build；否则需要重新构建部署

**改页面样式**（标题 / 加载动画 / 进度条配色等）：不要手动改构建产物里的 `index.html`（下次构建会覆盖），
去改构建脚本 `Assets/Editor/WildernessReplayWebGLBuilder.cs` 的 `PatchIndexHtml` 方法后重新构建即可。
逐项对应关系与注意事项见 [WebGL构建脚本说明.md](WebGL构建脚本说明.md)。

> 注：`Builds/` 目录已被 `.gitignore` 排除，构建产物不会提交进 git。

### 2.4 替换 replay 文件

replay 文件放在 `Assets/StreamingAssets/replay.txt`，是整个回放的数据源。

> 完整字段定义见 [replay格式文档.md](replay格式文档.md)


### 2.5 Unity 编辑器界面速览

如果你是第一次用 Unity，这几个窗口最重要：

| 窗口 | 位置 | 作用 |
|------|------|------|
| **Scene** | 左上 | 场景的 3D 编辑视图，可以用鼠标拖动查看 |
| **Game** | Scene 旁边的标签页 | Play 时的实际运行画面 |
| **Hierarchy** | 左侧 | 当前场景里所有物体的树形列表 |
| **Project** | 左下 | 项目所有文件（脚本、Prefab、材质等） |
| **Inspector** | 右侧 | 选中某个物体后显示它的所有组件和属性 |
| **Console** | 底部标签页 | 日志/报错信息，调试必备 |

**常用操作**：
- **选中物体**：在 Hierarchy 或 Scene 里点击
- **移动/旋转/缩放**：顶部工具栏的 W/E/R 快捷键
- **Play/Pause**：顶部 ▶ 按钮，或 Ctrl+P
- **保存场景**：Ctrl+S

---

## 三、项目目录结构

```
unityWildernessReplay/
├── Assets/
│   ├── Scripts/                    ← 【核心】所有 C# 代码
│   │   ├── Core/                   ← 数据层：解析 replay、状态引擎、播放控制
│   │   ├── Scene/                  ← 表现场景：地形搭建、单位 3D 表现、昼夜系统
│   │   ├── FX/                     ← 特效：伤害数字、弹道、任务卡片、交易徽标
│   │   ├── UI/                     ← UI 面板：HUD、事件日志、播放控制、结算
│   │   ├── Audio/                  ← BGM 系统
│   │   └── ReplayEntry.cs          ← 【入口】程序启动点，最先执行的代码
│   ├── Resources/                  ← 运行时加载的资源（Prefab、材质、音频等）
│   │   ├── Prefabs/Units/          ← 角色 Prefab（工人、开拓者等）
│   │   ├── Prefabs/Beasts/         ← 野兽 Prefab（4 种机器人）
│   │   ├── Prefabs/Buildings/      ← 建筑 Prefab（基地、塔、围墙、商店）
│   │   ├── Prefabs/Environment/    ← 环境 Prefab（草地、树木、围栏）
│   │   ├── Prefabs/Ores/           ← 矿石 Prefab
│   │   ├── Audio/BGM/              ← BGM 音频文件
│   │   ├── Animations/             ← 动画控制器
│   │   ├── FX/                     ← 特效 Prefab（爆炸、魔法阵等）
│   │   ├── Fonts/                  ← 中文字体（NotoSansSC）
│   │   └── Materials/              ← 材质文件
│   ├── Prefabs/UI/                 ← UI Prefab（4 个面板，不在 Resources 下）
│   ├── StreamingAssets/            ← replay.txt 放这里（WebGL 必须走此路径）
│   ├── ProjectAssets/              ← 防御塔源素材（URP 转 Built-in 后的）
│   ├── Docs/                       ← 文档（就是你正在看的这些）
│   └── [第三方素材包]/             ← KayKit、Robots Pack 等美术资源
├── docs/                           ← 项目文档
└── README.md
```

---

## 四、代码架构：数据流

整个项目的数据流是一条**单向管道**，理解了这个就理解了 80% 的架构：

```
replay.txt (JSONL 文件)
    │
    ▼
ReplayParser.cs          ← 读文件，逐行解析 JSON，生成 ReplayData 对象
    │
    ▼
ReplayState.cs           ← 状态引擎：对比相邻回合的差异(Diff)，计算每个单位的位置/血量变化
    │
    ▼
ReplayPlayer.cs          ← 主控制器：按时间推进回合，驱动所有单位更新，触发事件回调
    │
    ▼
UnitView.cs (+ 4个partial)  ← 单位的 3D 表现：模型、动画、血条、特效
SceneBuilder.cs          ← 场景搭建：地形、森林、围栏
DayNightController.cs    ← 昼夜灯光变化
BgmController.cs         ← 背景音乐
FxFactory.cs             ← 战斗特效（弹道、爆炸等）
    │
    ▼
UI 面板 (HudController / PlaybackControlPanelController / ...)  ← 界面显示
```

**简单理解**：`Core/` 负责"数据是什么"，`Scene/` + `FX/` + `Audio/` 负责"画面上看到什么"，`UI/` 负责"界面上显示什么"。

---

## 五、各模块简述

### 5.1 数据层（`Scripts/Core/`）

| 文件 | 做什么 | 一句话说明 |
|------|--------|-----------|
| `ReplayModels.cs` | 数据模型 | 定义 replay 数据的 C# 类（回合、队伍、角色等） |
| `ReplayParser.cs` | 解析器 | 读 JSONL 文件 → 生成 `ReplayData` 对象 |
| `ReplayState.cs` | 状态引擎 | 对比相邻回合差异，计算单位移动/血量变化，坐标转换 |
| `ReplayPlayer.cs` | 主控 | 时间轴推进、回合调度、事件分发（攻击/建造/死亡等） |
| `ReplayEntry.cs` | 入口 | 程序启动点，加载 replay 文件，初始化所有系统 |

> 详细字段说明见 [PROJECT_STATE.md](PROJECT_STATE.md) 第二节

### 5.2 场景表现（`Scripts/Scene/`）

| 文件 | 做什么 |
|------|--------|
| `SceneBuilder.cs` | 搭建 3D 地形（草地、森林、围栏、水面、NPC） |
| `UnitView.cs` + 4 个 partial | **单位表现核心**：模型创建、动画、血条、野兽 LOD、塔视觉 |
| `DayNightController.cs` | 昼夜灯光变化（四阶段：白天→黄昏→夜晚→黎明） |
| `NpcFacingController.cs` | NPC 面向来访者的转向逻辑 |
| `TowerVisualController.cs` | 防御塔炮塔转向、后坐力、枪口闪光、Tracer 弹道 |
| `ResourceViewManager.cs` | 矿石 3D 表现 |
| `TeamColorApplicator.cs` | 阵营光圈颜色 |
| `ReplayCameraRig.cs` | 相机系统（全局/红方/蓝方/自由 四种模式） |
| `CameraManager.cs` | 自动导播（事件特写、震屏） |
| `MatLib.cs` | 材质缓存池 |
| `Pickable.cs` / `Billboard.cs` | 点击拾取 / 面向相机 |


### 5.3 特效（`Scripts/FX/`）

| 文件 | 做什么 |
|------|--------|
| `FxFactory.cs` | 世界空间特效工厂（伤害数字、弹道、光环、AoE 爆炸/眩晕） |
| `TradeBadge.cs` | 交易/使用道具时头顶弹出的徽标 |
| `TaskCardBadge.cs` | 开拓者任务卡片（4 态：接受/进行中/成功/失败） |
| `TaskBadgeManager.cs` | 任务卡片全局管理器 |

### 5.4 UI（`Scripts/UI/`）

| 文件 | 做什么 |
|------|--------|
| `HudController.cs` | 顶部 HUD（天数、白天/黑夜、回合数） |
| `PlaybackControlPanelController.cs` | 底部面板（队伍数据、进度条、播放按钮、镜头切换） |
| `EventLogPanelController.cs` | 左侧事件日志 |
| `SettlementPanelController.cs` | 结算面板（游戏结束时显示） |
| `UnitDebugOverlay.cs` | 单位头顶调试文字（ID/位置/HP/ATK） |

> UI 全部由 Prefab 驱动，代码只负责回填数据。详细结构见 [HUD_UI_AUDIT.md](HUD_UI_AUDIT.md)

### 5.5 音频（`Scripts/Audio/`）

| 文件 | 做什么 |
|------|--------|
| `BgmController.cs` | 昼夜双曲 CrossFade，按回合推进切换 |
| `BgmAudioConfig.cs` | BGM 起始偏移配置 |
| `Editor/BgmAudioTool.cs` | 编辑器选段工具（Window → BGM 选段工具） |

---

## 六、当前进度

### ✅ 已完成

| 功能 | 说明 |
|------|------|
| replay 解析与回放 | JSONL 解析、状态引擎、回合推进、插值动画 |
| 3D 地形 | 草地网格、森林边界、围栏、碎草、矿石 |
| 单位表现 | 角色/野兽/建筑的 3D 模型、动画、血条 |
| 防御塔视觉 | 炮塔转向、后坐力、枪口闪光、Tracer 弹道、阵营配色 |
| 昼夜系统 | 四阶段灯光变化 |
| BGM 系统 | 昼夜双曲 CrossFade、音量档、选段工具 |
| 任务卡片 | Phase 1 程序化版（4 态动画、Seek 修复） |
| 性能优化 | 野兽距离 LOD、场景静态合批、血条实例化、日志面板批量刷新 |
| 相机系统 | 四种模式（全局/红方/蓝方/自由） |
| UI 面板 | HUD、事件日志、播放控制、结算 |
| WebGL 适配 | StreamingAssets 加载、字体预热、锁帧、物理剥离 |

### 📋 待做 / 可扩展

| 功能 | 说明 | 详细文档 |
|------|------|----------|
| 任务卡片 Phase 2 | 把程序化纯色底板换成真实图片/视频素材 | [任务卡片实现与升级方案.md](任务卡片实现与升级方案.md) 第二节 |
| 任务描述文案 | 当前任务面板显示的是硬编码假新闻，与 replay 数据无关 | 需要任务系统侧配合输出 |
| UI 样式微调 | 布局/字号/颜色均可直接改 Prefab，不碰代码 | [HUD_UI_AUDIT.md](HUD_UI_AUDIT.md) 第六节 |

---

## 七、最容易踩的坑（Top 5）

> 完整列表见 [PROJECT_STATE.md](PROJECT_STATE.md) 第七节，以下是最常踩的 5 个。

### 1. KayKit FBX 的 scale 是 100

所有 KayKit 素材的 FBX 根节点 `scale=(100,100,100)`。实例化后**不能直接改 localScale**，要用容器包裹再调容器的 scale。否则模型会巨大或消失。

### 2. `StaticBatchingUtility.Combine` 在本项目无效

Unity 内置的静态合批 API 在本项目中**静默无效**（不报错、不合并）。场景合批必须用 `Mesh.CombineMeshes` 手动实现。详见 `SceneBuilder.cs` 的 `StaticBatchAll` 方法。

### 3. `BakeMesh` 的坐标空间陷阱

`SkinnedMeshRenderer.BakeMesh()` 烘焙出的网格在"除以 lossyScale"的空间里。LOD 静态网格必须把 `localScale` 补偿回 `1/lossyScale`，否则机器人会变小甚至隐形。**绝不能除以 `state.animScale`**。

### 4. Robot 的 Animator Controller 零参数

所有 Robot 素材自带的 Animator Controller 参数列表为空（`m_AnimatorParameters: []`），外部无法控制。运行时必须通过 `AnimatorOverrideController` 替换，按名称模糊匹配动画 clip。

### 5. `AudioSource.isPlaying` 在暂停时恒 false

回放暂停时（`AudioListener.pause=true`），任何 `AudioSource.isPlaying` 都返回 false。BGM 的淡出逻辑**不能靠 isPlaying 判断**，必须按 clip 归属判定。

---

## 八、常见修改速查

> 完整表格见 [PROJECT_STATE.md](PROJECT_STATE.md) 第六节

| 想做什么 | 改哪里 | 难度 |
|---------|--------|:---:|
| 调血条高度/宽度 | `UnitView.Hp.cs` 的 `_hpY`/`_hpW` | 低 |
| 调野兽模型大小 | Beast Prefab 的 `RobotAdjust` Transform | 低 |
| 换野兽模型 | Beast Prefab 里删旧 Robot → 拖入新的 | 低 |
| 改血条颜色 | `UnitView.cs` 的 `SetHp()` 里的 Color 值 | 低 |
| 调塔尺寸/后坐力 | 打开 `CubeTowers/Tower_Minigun_*.prefab` 改 Inspector | 低 |
| 换 BGM | 替换 `Resources/Audio/BGM/` 下的文件（文件名不变） | 低 |
| 改 WebGL replay 路径 | `ReplayEntry.cs` 的 `Load()` 方法 | 低 |
| 加新单位类型 | `UnitView.UNIT_PREFABS` + 新建 Prefab | 中 |
| 换塔模型素材 | 生成源塔 + 重跑菜单 Tools → Build Tower Visual Prefabs | 中 |

---

## 九、replay 数据格式简述

replay 文件是 **JSONL 格式**（每行一个 JSON），结构如下：

```
第 1 行:  {"type":"start", ...}        ← 地图 + 初始角色配置
第 2 行:  {"type":"round","round":1}   ← 第 1 回合快照
第 3 行:  {"type":"round","round":2}   ← 第 2 回合快照
...
第 N 行:  {"type":"finish", ...}       ← 结算数据
末尾行:   valid                         ← 有效性标记
```

每回合的 `teams[].roles[]` 包含所有单位的位置、血量、动作指令等。`commands[]` 记录本回合执行的操作（move/attack/build/buy/sell 等）。

> 完整字段说明见 [replay格式文档.md](replay格式文档.md)

---

## 十、配套文档索引

| 文档 | 内容 | 什么时候看 |
|------|------|-----------|
| [PROJECT_STATE.md](PROJECT_STATE.md) | 项目全貌：核心文件清单、设计决策、已知大坑、修改指南 | 需要查某个文件做什么 / 改代码前 |
| [replay格式文档.md](replay格式文档.md) | replay JSONL 的完整字段定义 | 需要理解数据结构 / 修改解析器时 |
| [任务卡片实现与升级方案.md](任务卡片实现与升级方案.md) | TaskCardBadge 的实现细节 + Phase 2 升级方案 | 做任务卡片相关需求时 |
| [夜间机器人卡顿优化_实现记录.md](夜间机器人卡顿优化_实现记录.md) | 性能优化的完整改动记录 + 参数调优指南 | 遇到性能问题 / 想调 LOD 参数时 |
| [HUD_UI_AUDIT.md](HUD_UI_AUDIT.md) | UI 面板结构审计：Prefab 与代码的关系 | 改 UI 布局/样式/新增字段时 |
| [WebGL构建脚本说明.md](WebGL构建脚本说明.md) | WebGL 一键构建 + 自动固化的设置 + index.html 页面样式怎么改 | 构建 WebGL / 调页面样式 / 包体积过大时 |
| [Agent任务开发说明.md](Agent任务开发说明.md) | 游戏任务系统的设计说明（推理类/长上下文/自进化类） | 需要理解任务机制时 |
| [任务书.md](任务书.md) | 比赛规则原文 | 需要理解游戏玩法时 |

---

## 十一、开发小贴士

1. **改代码后直接 Play 测试**，不需要额外编译步骤（Unity 自动编译）
2. **Console 窗口**，报错信息通常很明确
3. **改 Prefab 不需要改代码**：很多视觉参数（大小、颜色、位置）都在 Prefab 的 Inspector 里，直接改就行
4. **不要删除 `Assets/` 下看起来"没用"的文件夹**：很多资源通过 Nested Prefab 引用，删了会导致运行时报错
5. **WebGL 构建后必须重新部署**：Resources 是构建时打包的，不支持热更新

---

## 十二、3D 资源获取与导入

开发过程中经常需要替换或新增 3D 模型（角色、建筑、特效等）。以下是常用的资源获取渠道和导入方法。

### 12.1 资源获取渠道

#### ① Unity Asset Store（官方商店，最推荐）

> https://assetstore.unity.com

Unity 官方资源商店，资源质量有保障，大部分免费资源可以直接使用。

**使用流程**：

1. 用 Unity 账号登录 Asset Store 网站
2. 搜索需要的资源（如 "low poly robot"、"nature pack"、"cartoon effects"）
3. 找到合适的资源后，点 **"Add to My Assets"**（免费资源点 "Add to My Assets"，付费资源需要先购买）
4. 回到 Unity 编辑器 → **Window → Package Manager**
5. 左上角下拉选 **"My Assets"**（需要登录同一个 Unity 账号）
6. 找到刚添加的资源 → 点 **Download** → 下载完成后点 **Import**
7. 弹出导入对话框 → **全部勾选 → Import**，资源会出现在 `Assets/` 下

> **提示**：Asset Store 的资源通常以 `.unitypackage` 格式打包，Package Manager 导入后会自动解压到 `Assets/` 目录。

#### ② itch.io（独立开发者社区，大量免费资源）

> https://itch.io/game-assets/free/tag-3d

独立游戏开发者的资源分享平台，有很多高质量的免费 3D 模型包。

**使用流程**：

1. 浏览或搜索需要的资源类型（如 "low poly"、"stylized"、"pixel art"）
2. 下载资源包（通常是 `.zip` 压缩包）
3. 解压后根据文件类型处理（见下方 12.2 节）

#### ③ 淘宝（付费资源的平替）

如果 Asset Store 上某个资源价格较高，可以：

1. 记下资源的**英文名称**（如 "Fantasy Kingdom Pack"）
2. 去淘宝搜索该名称，经常能找到低价的资源包
3. 卖家通常会发百度网盘链接，下载后解压


### 12.2 资源导入方法

不同来源的资源格式不同，导入方式也不一样：

#### 情况一：`.unitypackage` 文件

这是 Unity 的标准打包格式，最常见。

**导入方法**:Unity 编辑器菜单 → **Assets → Import Package → Custom Package** → 选择 `.unitypackage` 文件 → Import

导入后资源会出现在 `Assets/` 目录下（通常会创建一个以资源名命名的子文件夹）。

#### 情况二：`.fbx` 文件 + 贴图文件夹

这是通用的 3D 模型格式，itch.io 下载的资源经常是这种格式。

**导入步骤**：

1. 将 `.fbx` 文件和贴图文件夹一起复制到 `Assets/` 下的合适位置（如 `Assets/Resources/Prefabs/` 下新建一个文件夹）
2. 回到 Unity 编辑器，Unity 会自动导入并生成 `.meta` 文件
3. 在 Project 窗口找到导入的 `.fbx` 文件，**拖到 Hierarchy 或 Scene 里**即可看到模型
4. 如果模型显示为粉色/紫色 → 缺少材质或 shader 不兼容 → 需要手动创建材质并指定贴图

**FBX 导入后的常见调整**：

| 问题 | 解决方法 |
|------|---------|
| 模型太大/太小 | 选中 FBX → Inspector → Model 页签 → **Scale Factor** 调整（如 0.01 或 100） |
| 模型朝向不对 | 模型下的子节点调整 Rotation，或用空物体包裹后旋转容器 |
| 贴图没自动关联 | 手动创建 Material（右键 → Create → Material），把贴图拖到 Albedo/Base Map 槽位 |
| 动画不播放 | 检查是否有 Animator Controller；如果没有，需要创建或使用已有的 |
| 模型是 T-Pose（站立不动） | FBX 可能没有自带 Animator Controller，需要在 Animator 组件上指定一个 |

> **本项目的特殊情况**：KayKit 系列素材的 FBX 根节点 scale 是 100，导入后需要用容器包裹再调整（详见第七节"已知大坑"）。

#### 情况三：整个 Unity 项目文件夹

有些资源是以完整 Unity 项目形式分享的（包含 `Assets/`、`ProjectSettings/` 等文件夹）。

**使用方法**：

1. 用 Unity Hub 打开那个项目
2. 在 Project 窗口找到你需要的 Prefab/模型/材质
3. 右键 → **Export Package** → 勾选需要的文件 → 导出为 `.unitypackage`
4. 回到本项目 → Import Package 导入

### 12.3 资源导入后的适配

导入新资源后，通常还需要做一些适配工作才能在项目中使用：

**替换现有模型**（最常见场景）：

详细步骤见 [PROJECT_STATE.md](PROJECT_STATE.md) 第五节，AI可以通过mcp直接操作。

**添加全新单位类型**：

1. 在 `Resources/Prefabs/` 下合适的位置创建新 Prefab
2. 按照现有 Prefab 的结构搭建（参考 `UnitView.cs` 的 `Create` 方法）
3. 在代码中注册新类型（`UnitView.UNIT_PREFABS` 字典）

### 12.4 常用免费资源推荐

以下资源包在本项目中已使用或风格兼容，可以作为参考：

| 资源包 | 风格 | 用途 | 来源 |
|--------|------|------|------|
| KayKit Adventurers | 低多边形卡通 | 角色模型 | Asset Store (Free) |
| KayKit Skeletons | 低多边形卡通 | 骷髅模型（原野兽模型已弃用后续可以清理） | Asset Store (Free) |
| KayKit Forest Nature Pack | 低多边形卡通 | 树木、灌木、草地 | Asset Store (Free) |
| KayKit Medieval Hexagon Pack | 低多边形卡通 | 建筑、城墙 | Asset Store (Free) |
| Robots Ultimate Pack (Cute Series) | 卡通机器人 | 野兽替换素材 | Asset Store |
| Cartoon FX Remaster | 卡通特效 | 爆炸、魔法阵 | Asset Store (Free) |
| Low Poly Forest Pack | 低多边形 | 树木、围栏 | itch.io |
