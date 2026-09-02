# WildernessReplay 项目状态

> **用途**：供新会话的 AI 快速理解项目全貌。原则：说清是什么、在哪改，不堆细节。
> **最后更新**：2026-09-02
---

## 一、项目是什么

Unity 2022.3.62f3c1 **Built-in RP** 回放播放器。加载 JSONL replay 文件，在 41×32 地图上以 3D 可视化两队对战。

- 数据流：`JSONL → ReplayParser → StateEngine → ReplayPlayer → UnitView`
- 代码在 `Assets/Scripts/`，第三方素材在 `Assets/KayKit_*/` `Assets/Low_Poly_Forest_*/`

---

## 二、核心文件清单（按职责）

### 数据 & 播放
| 文件 | 职责 |
|------|------|
| `Core/ReplayModels.cs` | 数据模型 |
| `Core/ReplayParser.cs` | JSONL 解析 |
| `Core/ReplayState.cs` | 状态引擎：Diff + WorldPos 坐标转换 |
| `Core/ReplayPlayer.cs` | 主控：回合推进、smoothstep 插值、事件回调；野兽(11-14)登场屏蔽出生光环（防多回合机器人陆续登场时周期性闪现白圈），Tracer 弹道/建筑光环等其余特效不受影响 |
| `Core/ReplayEntry.cs` | 入口：`[RuntimeInitializeOnLoadMethod]` 自动启动；WebGL 用 `UnityWebRequest` 加载 replay（`RelativeStreamingUrl` 相对路径 + `LoadWebText` try/catch 兜底 demo） |

### 场景 & 表现
| 文件 | 职责 |
|------|------|
| `Scene/SceneBuilder.cs` | **3D 地形搭建**：草地网格、森林边界、围墙、水面、NPC 站位。**性能：场景静态景物合批**（`StaticBatchAll` 用 `Mesh.CombineMeshes` 按材质分组合并草/树/围栏，2356 渲染器→14 合成网格）+ 材质共享（`GetFixedMaterial`/`GetStandardMat`/`_waterMat` 缓存） |
| `Scene/UnitView.cs` + 5 partial（2026-08-20 拆 4 个 + 08-24 加 Aura） | **单位表现核心**（Partial Class）：主文件 `UnitView.cs` 字段/Create/Configure*/LateUpdate 调度/SetHp/SetStun/CalibrateBaseScale；`UnitView.Anim.cs` 动画装配与触发（SetupRobotAnimator/UpdateAnimation/TriggerAttack/TriggerDeath/AnimatorSpeed 倍速）；`UnitView.Hp.cs` 血条与光环（UpgradeHpTo3D/EnsureRing/GetSharedHpFillMat/Estimate* + `HP_BAR_STYLES` 配置表）；`UnitView.Lod.cs` 野兽距离 LOD（UpdateLod/SetLodStatic/LOD_RANGE 等 public static 调参）；`UnitView.Tower.cs` 塔视觉（SetupTowerVisual/TriggerTowerAttack）；`UnitView.Aura.cs` 夜晚角色光环（SetupNightAura/UpdateNightAura）。**性能优化**：SetHp/SetStun/UpdateAnimation 值缓存自门控（仅 HP/眩晕/生死变化时刷新材质、写旋转、设 Animator.speed），LateUpdate 空闲跳过插值，静态 ReplayPlayer 缓存；野兽阴影/入场粒子已在 Prefab 资产源头根治；**野兽距离 LOD**（远→共享烘焙静态网格+GPU 实例化+关 Animator，近→骨骼动画）；血条全局共享材质实例化 |
| `Scene/UnitViewSprite.cs` | **静态工具**：Sprite 扫描、颜色计算（从 UnitView 拆出） |
| `Scene/ResourceViewManager.cs` | **矿石系统**：3D 球体 + 物理 .mat 材质 |
| `Scene/TeamColorApplicator.cs` | **阵营标识**：仅控制脚底 SelRing 颜色（已废除全身染色） |
| `Scene/DayNightController.cs` | **昼夜系统 v2**：四阶段 `LightingProfile` (Day/Dusk/Night/Dawn)，从 `ReplayPlayer.RoundFloat` 连续回合 → `Mathf.Repeat` → 阶段判定 → `LightingProfile.Lerp` 插值 |
| `Scene/NpcFacingController.cs` | **NPC 转向**：切比雪夫距离来访者检测 + 命令优先级 (executeTask/submitAnswer/sell) + Smooth01 八方向水平旋转；与 FBX/骨骼解耦 |
| `Scene/TowerVisualController.cs` | **防御塔视觉**（type=3）：炮塔转向 attack 目标 + 两阶段程序化后坐力 + Muzzle 粒子/闪光 + 阵营配色 Tracer/命中圆环；统一 Minigun 塔模型；暂停冻结/Seek 复位；LateUpdate 空闲快速路径（无活跃效果时只对齐待机朝向并提前返回，降低同屏开销） |
| `Scene/MatLib.cs` | 材质缓存池 + 程序化圆环贴图（Sprites/Default shader） |
| `FX/FxFactory.cs` | 世界空间特效：伤害数字/弹道/光环/气泡 + **CFXR AoE 特效**（`PlayBombEffect`/`PlayDizzyEffect`，统一 `Resources.Load("FX/...")`） |
| `Scene/Pickable.cs` `Scene/Billboard.cs` | 点击拾取 / 面向相机 |
| `Scene/ReplayCameraRig.cs` | 相机系统：1/2/3/4 快捷机位 (Global/TeamA/TeamB/Free)；Free 模式左键平移+右键旋转+滚轮锚点缩放 |
| `Scene/CameraManager.cs` | 自动导播：SmoothDamp + 事件特写 + 震屏 |
| `FX/TradeBadge.cs` | 交易/使用徽标：World Space Billboard + 弹出淡出；Vendor/Shop 独立参数；角色使用道具 `ShowUse`（「使用 xx」）；背景框按全宽/半宽自适应 |
| `FX/TaskCardBadge.cs` + `FX/TaskBadgeManager.cs` | **开拓者任务卡片**：程序化 Quad 底板 + TextMesh 文字，4 态 Intro/Working/Success/Fail，**仅 Working 是视频，其余状态是静态图片（各展示 2 回合）**；暂停冻结动画计时 + 视频同步；Billboard 面向相机；共享 Sprites/Default 材质 + MPB 改色（GPU Instancing）；**渲染层**：Intro=**claim.png**（Resources/Sprites，至少 2 回合）；Working=**TaskBadgeManager 全局共享 working 视频 RT**（游戏开始即就绪循环，Working 起始帧即显示，无中间加载态）；Success/Fail=**unlock_success.png / unlock_fail.png 静态图**（Resources 同步加载，`_resultStartCur` 记录进入回合，`cur-start>=2` 后淡出销毁，round-based 速度无关）；**CARD_SCALE=4（卡片 2 倍放大）**；**金黄描边**（BORDER=0.05 稍大 Quad 垫底，`ApplyAlpha` 淡入淡出时随背景同步）；`_mpb.Clear()` 清纹理 + 立即补回 `_Color` 防暂停全透明；资源加载失败降级纯色+文字（working 蓝「破解中」/ Success 绿「✓ 通过」/ Fail 红「× 失败」）；**性能**：共享 working 仅在"有卡在 Intro（预卷）或 Working"时播放，空闲暂停省解码（WebGL 并发解码是卡顿源）；**WebGL**：URL 用 `TaskCardBadge.VideoUrl()`（相对正斜杠、勿 Path.Combine）、`audioOutputMode=None`（静音放行 autoplay）、`isPrepared` 轮询兜底、**视频必须是 H.264（avc1）**（mp4v 浏览器不认） |
| `FX/TaskBadgeManager.cs` | 任务卡片全局管理器（挂在 ReplayEntry）：每帧从 `rounds[cur-1].teams[].task` 读快照、与 `rounds[cur-2]`（数据上一回合）做跳变检测判定状态（成功/失败）；拖动进度条/Seek 时**先全清再按目标回合数据重建**（杜绝「开拓者站着却残留失败框」）；血条上方 +0.5 净空、整体 2× 放大（世界坐标定位不受父节点缩放影响）；Awake 多实例自毁 + 创建前查父节点已有卡复用（防叠卡） |

### UI
`HudController.cs` `EventLogPanelController.cs` `PlaybackControlPanelController.cs` `SettlementPanelController.cs` + `UnitDebugOverlay.cs`

4 个面板均由场景 `PrefabRefs` 按 GUID 引用对应 prefab 驱动（`Create()` 缺 prefab 直接 `LogError`，**纯代码兜底 `CreateFromCode` 已全部删除**）。字体运行时统一替换为 `NotoSansSC`（CJK，**无 emoji 字形** → 项目 UI 全部用纯中文文本，不用 emoji）。`UnitDebugOverlay.cs` 是单位头顶调试悬浮文字（`[ID|Pos|HP|ATK]`），由 UnitView 挂载、受 `PlaybackControlPanelController.ShowUnitStats`（底部面板「显示」按钮）全局开关控制。

### 音频（BGM，2026-08-21 新增）
| 文件 | 职责 |
|------|------|
| `Audio/BgmController.cs` | **BGM 系统**：白天播 `bgm_day` / 夜晚播 `bgm_night`，双 AudioSource 按**回合**推进 CrossFade（正常 2 回合 / Seek 跳变 0.3 回合，速度无关）；夜晚阶段判定 `Mathf.Repeat(roundFloat,130) >= 75`（130 回合/周期，75~78 回合完成白天→夜晚过渡，夜晚音乐最迟第二天第 3 回合切回白天）；读取 `BgmAudioConfig` 起始偏移 + 选段循环；暂停 `AudioListener.pause` 冻结；音量档循环；WebGL 首次输入解锁 Autoplay；由 `ReplayEntry.Awake` 挂载 + `DontDestroyOnLoad` |
| `Audio/BgmAudioConfig.cs` | 起始偏移配置 ScriptableObject（`dayStartTime`/`nightStartTime`，资产在 `Resources/Audio/BGM/BgmAudioConfig.asset`） |
| `Audio/Editor/BgmAudioTool.cs` | 编辑器「BGM 选段工具」（菜单 Window → BGM 选段工具）：试听/拖进度条选段/设起始偏移/保存 |

BGM 素材在 `Assets/Resources/Audio/BGM/`（`bgm_day`、`bgm_night`，`.ogg`/`.wav`/`.mp3` 均可，**运行时按文件名（不含扩展名）加载**）。替换与选段方法见第五节。

---

## 三、3D 资源与 Prefab

### 角色
```
Resources/Prefabs/Units/
├── Worker.prefab     # type 6 — Barbarian
├── Pioneer.prefab    # type 7 — Rogue
├── OfficerNPC.prefab # type 8 — Ranger
└── VendorNPC.prefab  # type 9 — Mage
```

### 野兽
```
Resources/Prefabs/Beasts/
├── Beast_11.prefab  # Bot Robot (小型)
├── Beast_12.prefab  # Boxy Robot (中型)
├── Beast_13.prefab  # Gripper Robot (大型；原 Tanker 形象移至 Beast_14 作 Boss)
├── Beast_14.prefab  # Tanker Robot (BOSS，原第三种形象；原 Metal Robot BOSS 已淘汰)
```
层级：Beast_XX → Visual → RobotAdjust (scale/Y/yaw) → Robot (Nested Prefab)
原 Skeleton 节点保留但 disable。动画通过 `AnimatorOverrideController` 将 Skeleton_AnimatorController 参数映射到 Robot clips。

**资产源头已根治（2026-08-19，2026-08-24 Gripper 补充）**：5 个底层 Robot prefab（Bot/Boxy/Tanker/Metal/Gripper）的全部 Renderer 均 `shadowCastingMode=Off` + `receiveShadows=false`（`m_CastShadows`/`m_ReceiveShadows=0`）；Beast_11 底层 `Bot Robot.prefab` 的入场粒子 `FX Hex`（playOnAwake 白圈）已彻底删除（Boxy/Tanker/Metal/Gripper 本就无粒子）。运行时无任何阴影/粒子补救代码。
**2026-08-24 形象调整**：Beast_13 改用 `Gripper Robot.prefab`；Beast_14（Boss）改用原第三种 `Tanker Robot.prefab`；原 Boss `Metal Robot.prefab` 不再被引用（源文件保留）。Boss(type 14) 豁免距离 LOD 静态化（`UnitView.Lod.cs` `UpdateLod()`），动画始终独立播放。

### 建筑
```
Resources/Prefabs/Buildings/
├── Base.prefab     # type 4 — 双色 (Model_Red/Blue)
├── Tower.prefab    # type 3 — 外层逻辑 prefab（UnitView/碰撞/血条/阵营），内部 Visual 运行时被替换
├── Wall.prefab     # type 5
└── WeaponShop.prefab # type 10 — building_barracks_yellow.fbx
```

### 任务点（2026-08-31）
```
Resources/Prefabs/
├── broken_K151ArmoredVehicle.prefab  # 韩军 K151 破损卡车（任务点初始/重生形态）
├── K151ArmoredVehicle.prefab         # 韩军 K151 完好装甲车（任务完成后的修复形态，开向小贩）
└── GoldChest.prefab                  # 金色宝箱（未来任务点）
```
- 均为包装 Prefab（根节点 + `Model` 子节点）。适配详情见 [资源问题与解决方案.md](资源问题与解决方案.md)。
- **摆放**（`SceneBuilder.BuildMissionPoint`）：地图 tile 40/42 → 宝箱、41/43 → 装甲车，位置 = 格子中心世界坐标（game 坐标 (14,14)(23,14) 宝箱、(17,17)(26,17) 装甲车）。装甲车 `VEHICLE_SCALE=0.27`（约 0.74×1.44m，车头+Z），用 `LookRotation` 使车头朝小贩(tile 9)。当前 replay.txt 已含这 4 个 marker tile。任务点根节点挂 `MissionPoint` 组件（记 gameX/Y、isVehicle）。
- **任务完成售卖**（2026-09-01，`MissionVehicleDriver` + `MissionPoint.StartSellCycle`）：任务点初始/重生形态是**破损卡车**（`broken_K151ArmoredVehicle.prefab`）；各队「自进化类2」任务（`TaskCardBadge.REPAIR_TASK_TYPE`）完成跳变时，按任务 `task.pos`（ReplayTask 已解析）定位对应卡车：**破损→在原任务点换成完好卡车**（`K151ArmoredVehicle.prefab`）→ **直线开向小贩、小贩前 `STOP_BEFORE_VENDOR=1.2m` 停下**（不调头不压到小贩）→ 卡车上显示「贩卖成功」徽标（`TradeBadge.ShowTextWorld`：挂到 scale=1 的地图根 + 世界坐标定位卡车上方 0.8m，**不继承卡车 0.27 缩放**——否则弹出/上浮动画会被放大、位置漂移；工人购买面板同款字号/黑底板自适应，1s 后销毁）→ 消失 → **原任务点重生破损车**（供下次任务）；暂停冻结。**Seek 重置**：`MissionVehicleDriver` 检测「暂停 && cur 变化」→ `MissionPoint.ResetToBroken` 取消售卖协程、销毁徽标、完好车重生破损车，杜绝跳回未完成任务回合还继续跑售卖效果。

### 防御塔（SciFiStrategyLowPoly，2026-09-02 替换 CubeTowerDefense）
- **源素材**：`Assets/SciFiStrategyLowPoly/`。唯一共享材质 `Materials/Main.mat` 是 **Built-in 目标**的 Shader Graph（非 URP），工程装了免费包 `com.unity.shadergraph` 14.0.12 后强制重导即可编译，无需烘焙/remap（修复详情见 [资源问题与解决方案.md](资源问题与解决方案.md) 第一节）。
- **武器映射**（`TowerVisualController.ResolveTowerType`）：roleType 30 加特林→**Minigun**（SciFi 转管机枪塔 `Minigun_1`，造型/语义贴切，原生 MinigunTracers 弹道）/ 31 电磁狙击炮→**Laser** / 32 火箭发射台→**Rocket**（旧 3 兜底 Minigun）。SciFi 塔层级统一为 `{Type}_1/{Type}_1_Root/Base_Mesh + Horizontal(偏航枢轴) + Vertical(俯仰枢轴)`，炮塔枢轴=`Horizontal`。
- **阵营染色**：从共享 `Main.mat` 复制出 `Assets/ProjectAssets/SciFiStrategy_BuiltIn/Materials/Main_Red.mat` / `Main_Blue.mat`，`_Color` 用 **HSV 低饱和色**（红 HSV(0°,0.5,0.7)≈砖红 / 蓝 HSV(~212°,0.5,0.7)≈钢蓝，避免荧光感）；生成器把外壳渲染器换成对应材质（VFX 材质保留），重跑会刷新 `_Color`。
- **视觉包装 Prefab（按武器等级）**：`Resources/Prefabs/Buildings/CubeTowers/Tower_{Type}_{Lv}_{Faction}.prefab`（Minigun/Laser/Rocket × 等级 1/2/3 × Red/Blue = **18 个**），生成器 `Assets/Editor/SciFiTowerPrefabBuilder.cs`（菜单 `Tools/WildernessReplay/Build SciFi Tower Visual Prefabs`）。**世界尺寸 1.4m 高 / 0.85m 底座占地**（围墙 0.825 之上）；炮塔 `Horizontal` 节点本地 scale 放大 **1.5x**（比例更自然；塔通常孤立摆放，外伸视觉可接受）；因 `UnitView.CalibrateBaseScale` 给塔施加常量缩放 0.7（量 Tower.prefab 自身宽度），生成器按「世界目标 ÷ 0.7」换算包装本地 scale；高度用整塔完整包围盒、占地只用 MeshRenderer（排除 Laser 光束 LineRenderer 会撑大 XZ）。
- **武器等级 → 模型（2026-09-02）**：回放 `ReplayRole.level`（武器工事 1~5）已解析到 `state.level`。`UnitView.SetupTowerVisual` 按 `clamp(level,1,3)` 加载 `Tower_{Type}_{Lv}_{Faction}`（4~5 级用 _3 最高模型）；`RefreshTowerLevelVisual()` 在 `LateUpdate` 逐帧比对 `state.level`（照 WallOrientation 模式），**升级券/回合推进导致等级变化时自动销毁旧包装换对应等级模型**。Laser 高等级=多光束：`Laser_1` 单束 `LaserBeam`、`Laser_2` 双束 `LaserBeam_1/2`、`Laser_3` 三束 `LaserBeam_1/2/3`，运行时 `CollectLaserBeams` 按 `LaserBeam*` 前缀自动收集、开火时全部延伸对准落点。
- **开火表现（全部用素材包原生特效，弃用旧程序化命中环/电击）**：Minigun=原生 `MinigunTracers` 枪口喷射 + `MinigunShell` 弹壳 + **到每个落点画粗弹道线**（`SpawnTracer` 加粗到 0.14，直观显示打到哪个机器人）+ **落点播原生 `Hit` 火花**（`SpawnGatlingHit`，`Hit.prefab` 已复制到 `Resources/FX/Hit` 供运行时加载）；Laser=攻击时显示原生 `LaserBeam` 光束 0.8s 并**逐帧延伸到攻击落点**（LineRenderer 终点 + End 节点用 `InverseTransformPoint` 对准目标，随炮塔转向始终指向落点；**待机默认隐藏**）；Rocket=发射原生导弹，**每枚朝落点坐标直线飞行**（发射瞬间 reparent 导弹到静态包装根、脱离旋转炮塔避免拐弯；**到达/中断归位时还原导弹原始 localScale**——否则炮塔 Horizontal 1.5x 会让导弹逐次放大成巨大导弹；全部到达或超时兜底后在落点触发爆炸 + 震屏 `FxFactory.PlayBombEffect`，暂停冻结；`ReplayPlayer` type32 不再即时爆炸、塔视觉未就绪兜底直接爆）。对应逻辑在 `TowerVisualController` 的 `ShowLaserBeam/UpdateLaserBeam/HideLaserBeam/LaunchRockets/ResetRocketMissiles/SpawnGatlingHit` + `LateUpdate`；`UnitView.Tower.cs` 已删 type31 旧电球协程（ElectricBallFly），新增 `IsTowerVisualReady`。
- 旧的 CubeTowerDefense 包装 Prefab（Minigun/RPG/Flamethrower）保留在 CubeTowers/ 但不再被加载。

### 防御塔（历史：Cube Tower Defense，已转 Built-in，2026-09-02 起被上方 SciFi 替换）
- **源素材**（URP 专用，勿改）：`Assets/CubeTowerDefense/`
- **已转换 prefab**（源塔）：`Assets/ProjectAssets/CubeTowerDefense_BuiltIn/Resources/Prefabs/Towers/`
  ```
  Tower_Flamethrower_Red/Blue.prefab
  Tower_Minigun_Red/Blue.prefab
  Tower_RPG_Red/Blue.prefab
  ```
  材质在 `.../Materials/`（Standard）、粒子在 `.../Effects/`（Particles/Standard Unlit）。
- **视觉包装 Prefab**：`Resources/Prefabs/Buildings/CubeTowers/Tower_{Type}_{Faction}.prefab`（6 个），嵌套引用上述源塔（不复制 FBX/贴图），根上挂 `TowerVisualController`。旧塔备份在 `Legacy/Tower_Legacy.prefab`。（注：2026-09-02 起运行时已改用上方 SciFi 塔，本目录 CubeTowerDefense 包装 Prefab 不再加载。）
- **节点结构**：根 → `BasePillar`(静态底座) + `Minigun`(可旋转炮塔节点)；正前方 = 局部 +Z。Minigun 有 `Muzzle` 节点（内含 8 个 `Particle System` + `Shooting` 粒子），但该节点默认 **禁用**（见 [资源问题与解决方案.md](资源问题与解决方案.md) 第六节）。

### 环境
```
Resources/Prefabs/Environment/
├── Grass_Block.prefab       # 1m×0.06m×1m 草地瓦片 (Cube + Mat_Grass_Block.mat)
├── Forest/                  # 12 个树/草/围栏/岩石 Prefab (Build 用 Resources.Load 回退)
├── Trees/  Bushes/          # 旧森林池 (未使用)
```

### 矿石
```
Resources/Prefabs/Ores/
├── Ore_Stone/Iron/Copper.prefab  # 基于 Rock_1_A FBX (注意：FBX 子节点可能未序列化，运行时用 Sphere fallback)
Resources/Materials/
├── Mat_Ore_Stone/Iron/Copper.mat  # Standard shader, 不同 Metallic/颜色
├── Mat_Grass_Block.mat
```

### 动画
```
Resources/Animations/
├── Adventurer_AnimatorController.controller   # 角色 (isMoving/onAttack/onInteract/onDeath)
└── Skeleton_AnimatorController.controller     # 骷髅 (isMoving/onAttack/onDeath)
```
参数: isMoving(Bool), onAttack(Trigger), onDeath(Trigger)。Idle↔Walk, AnyState→Attack/Death。

### Robot 动画（Beast 11-14）
- Robot 自带 Controller 的 `m_AnimatorParameters: []` 全部为空，纯 ExitTime 自动过渡，无法外部控制
- 运行时通过 `SetupRobotAnimator()` 创建 `AnimatorOverrideController(Skeleton_AnimatorController)` 替换
- 按名称模糊匹配 Robot clips → 映射到 Idle_A / Walking_A / Hit_A / Death_A
- 无匹配 clip 时用 Idle 兜底，避免 T-pose
- `_hasParams` 标志控制是否调用 SetBool/SetTrigger
- 暂停时 `_animator.speed = 0` 冻结动画

### 第三方素材包
```
KayKit_Adventurers_2.0_FREE/          # 角色模型
KayKit_Skeletons_1.1_FREE/            # 骷髅模型
KayKit_Forest_Nature_Pack_1.0_FREE/   # 树/灌木/草/石头 (共享 forest_texture.png)
KayKit_Medieval_Hexagon_Pack_1.0_FREE/ # 建筑/城墙 (Base/Tower 用)
Low_Poly_Forest_Pack_Devilswork.Shop_v02/ # 树/围栏 (fence24, treeTall03)
Robots Ultimate Pack 01 Cute Series/      # Robot 野兽替换素材（Bot/Boxy/Tanker/Metal）
JMO Assets/Cartoon FX Remaster/           # 特效包：爆炸/魔法阵（AoE 用，已复制到 Resources/FX/ 供运行时加载）
```

---

## 四、关键设计决策

### UnitView 创建路由
```
Create(state, parent)
├── types 3-10 → UNIT_PREFABS dict → Resources.Load → ConfigureFromUnitPrefab()
├── types 11-14 → Beast_XX.prefab → ConfigureFromBeastPrefab()
└── 其他 → new GameObject → Build()
```

### 3D 地形 (SceneBuilder.Build)
- **草地网格**：41×32 块 Grass_Block.prefab (Cube 1m×0.06m, Standard shader)，棋盘格双色
- **森林边界**：外围 3 格宽，仅 KayKit 树（5 种），18% 概率，2m 间距，scale 0.5~1.0
- **木围栏**：Devilswork fence24.fbx，四周闭环，水平 rotY=0 / 竖直 rotY=90
- **碎草散布**：33% 概率，scale 0.15~0.3，KayKit Grass_1/2 FBX
- **水面跳过**：碎草和树灌不在水域生成
- **矿石**：ResourceViewManager 运行时生成 Sphere + Mat_Ore_XX.mat，Y-only 旋转
- **场景合批**：`StaticBatchAll`（BuildForestSkirt/BuildPerimeterFence/草地网格末尾调用）用 `Mesh.CombineMeshes` 手动合批——**不能用 `StaticBatchingUtility.Combine`**（本环境实测无论 mesh 是否可读、物体是否 isStatic 均不产生合并网格，静默 no-op，见 [资源问题与解决方案.md](资源问题与解决方案.md) 第三节）。做法：按材质分组 → 每组 `CombineInstance[]`（mesh + `localToWorldMatrix`）→ `CombineMeshes(comb, true, true)`（**useMatrices 必须 true**，false 时所有顶点塌缩到局部原点堆在地图中心）→ 挂 root 下合成网格 + 材质 → 禁用原物体（容器直接 `SetActive(false)`）。单组按 60k 顶点预算分块（围栏 170 段×600 顶点≈102k 必须分块）。FBX 需开 Read/Write（11 个 meta `isReadable:1`）

### UnitView 拆分（2026-08-20，Partial Class）
- `UnitView.cs` 原 818 行上帝类 → 拆为 `UnitView.cs`(341) + 4 个 partial：`UnitView.Anim.cs`(172 动画) / `UnitView.Hp.cs`(172 血条) / `UnitView.Lod.cs`(119 距离LOD) / `UnitView.Tower.cs`(58 塔视觉)。
- 纯物理搬运：类名/命名空间/GUID/全部字段声明与公开 API 签名零改动；13 个 Prefab（仅序列化 `strideCoefficient=1`）与 ReplayPlayer 等调用方零改动。
- `LateUpdate` 抽为调度序列：`UpdateAnimationState(isMovingNow, posChanged, moveDir)`（Anim.cs）+ `UpdateLod()`（Lod.cs）。

### 血条系统 (UnitView)
- **3D Cube**：`Resources.GetBuiltinResource<Mesh>("Cube.fbx")`，Standard shader
- **无底槽**：HpBar 已删除，只剩 HpFill
- **自适应大小（配置表驱动）**：`_hpW` = 模型宽度；高度/宽度倍率/厚度/深度统一查 `HP_BAR_STYLES` 配置表（UnitView.Hp.cs，按 type 一行，未配置走 `HP_BAR_DEFAULT`）。血条为长方体：长度 X=hp 百分比、厚度 Y=thick、深度 Z=depth（默认 depth=thick；围墙 type5 深度减半 0.025 防过厚）
- **阵营恒定颜色**（不再随血量百分比变色）：MaterialPropertyBlock `_Color` 按阵营/类型恒定——野兽(11-14 机器人)黄 `#FFC94D` / 红方(defender)红 `#FF2D55` / 蓝方(challenger)蓝 `#007AFF` / 中立单位(NPC/无阵营)绿 `#44EC6F`；常量与 `GetHpColor()` 在 `UnitView.Hp.cs`
- **自动补建**：UpgradeHpTo3D() 若 Prefab 无 HpFill 则创建，若 Default-Material 则替换为 Standard

### 性能优化（2026-08-19，WebGL 大量单位同屏）
- **SetHp/SetStun/UpdateAnimation 值缓存自门控**：`_lastHp/_lastMaxHp/_lastStun/_wasDead/_animSpeed` 缓存，仅数值实际变化才刷新 MPB 材质、写旋转、设 Animator.speed——静止单位每帧零开销（即使 `ReplayPlayer.Update` 每帧调用）
- **LateUpdate 空闲跳过**：`isMoving==false` 不插值；`TowerVisualController` 无活跃效果（aim/recoil/flash/particles/tracer/hitRing）时只对齐待机朝向并提前返回
- **静态缓存**：`s_cachedPlayer` 复用 ReplayPlayer，避免大量单位各自 `FindObjectOfType`
- **野兽渲染瘦身**：阴影与入场粒子在 Prefab 资产源头关闭/删除（见第三节），运行时零遍历补救
- **场景静态景物合批（2026-08-20）**：草 1615 + 森林 571 + 围栏 170 = 2356 渲染器 → `Mesh.CombineMeshes` 手动合批为 **14 个合成网格**（每个材质组 1 个，合成网格保留投影/接收阴影保持原视觉）。启用中 MeshRenderer 由 ~2797 → ~400（随单位数波动）。GPU 帧时编辑器内 21ms→12ms 量级
- **野兽距离 LOD + GPU 实例化（2026-08-20）**：夜间机器人 80~156 只时卡顿（实测第 861 回合 156 只野兽：活跃 Animator **164**、活跃 SkinnedMesh **214**，帧时 GPU 14ms / CPU 12ms）。`UnitView` 新增距离 LOD：野兽按**相机 XZ 水平距离**（阈值 30，静态化/恢复用 0.85 滞回防边界闪烁）两档切换——远（≥30）用 `SkinnedMeshRenderer.BakeMesh()` 一次性烘焙为**共享静态网格**（每种野兽类型仅烘焙一次，4 类型 = 4 网格），改渲 `MeshRenderer` + **GPU Instancing**（材质 `enableInstancing=true`，远处全部机器人仅 ~4 次 DrawCall）并**禁用 Animator + SkinnedMesh**（省动画 CPU + 蒙皮 GPU）；近（<25.5）恢复完整骨骼动画。实测 861 回合：**156 → 140 静态（89%），运行中 Animator/Skinned 仅 16**。另：`CreateHpCube` 血条改**全局共享 Standard 材质 + enableInstancing**（原每体 `new Material` 破坏合批），156 个血条 Cube 合成实例化批。冻结姿势由「第一只进入远处状态的野兽当时的姿势」提供，可接受

### 动画系统
- **步幅对齐**：`_animator.speed = Clamp(realSpeed * strideCoefficient, 0.15, 4.5) * AnimatorSpeed`
- **applyRootMotion = false**：代码完全控制 transform
- **loopTime**：已通过 SerializedObject 物理持久化（52 clips），Play 不回弹
- **canTransitionToSelf**：全部置 false（12 transitions），杜绝高频重置

### 阵营区分
- **TeamColorApplicator**：已废除全身 MPB 染色，不再修改角色贴图颜色
- **夜晚角色光环（2026-08-24）**：工人/开拓者(6/7) 夜晚常驻 `CFXR3 Magic Aura A (Runic)` 魔法符文光环（`Resources/FX/CFXR3 Magic Aura A (Runic).prefab`，项目原有拷贝，自带暖色 Point Light），MPB `_Color` 按阵营上色（红方红 `3,0.6,0.6` / 蓝方蓝 `0.6,0.8,3.2`，alpha 0.55 通透）；特效以脚底/地面为中心（`NIGHT_AURA_FOOT_Y=0`，符文圈贴地）；`Mathf.Repeat(RoundFloat,130)>=80` 判定夜晚、随昼夜显隐、暂停冻结。**两个关键修复**：① CFXR 灯光动画在 `CFXR_Effect.Update` 用 Time.deltaTime 推进、粒子暂停不冻结 → `FxFactory.SetGlobalPause` 里 `CFXR_Effect.GlobalDisableLights=paused`（暂停时灯保持当前强度冻结不闪）；② Seek 到夜晚立即暂停时粒子为 0 法阵隐形 → `SetAuraVisible` 显示时 `ps.Simulate(1s)` 预热成型，并设 `clearBehavior=None` 防 CFXR 误销毁。实现 `UnitView.Aura.cs`，参数（原生尺寸/比例/透明度/颜色）集中在文件顶部常量。曾用 Hovl Buff 光环（已弃用，Resources 副本已删；原始 `Hovl Studio/.../Buff.prefab` 仍在——勿 revert 旧序列化格式否则导入报「referenced script missing」误报，保留 Unity 重序列化新版即可）
- **建筑**：defender→Model_Red 激活，challenger→Model_Blue 激活（红蓝反了：defender 显红）
- **基地 pivot 偏移**：defender Z=-1.0, challenger Z=-1.92

### 防御塔接入（type=3，第 4 阶段：可编辑视觉包装 Prefab + 目标连线）
- **视觉包装 Prefab**：`Resources/Prefabs/Buildings/CubeTowers/Tower_{Type}_{Faction}.prefab`（6 个，嵌套引用 ProjectAssets 源塔，不复制 FBX/贴图），每个包装根挂 `TowerVisualController`，序列化字段全部在 Prefab Inspector 配置（`visualScale`/`yOffset`/`forwardYawOffset`/`idleYawOffset=180`/`turnSpeed`/`recoilDistance` + 时间参数 `aimHoldDuration`/`recoilKickDuration`/`recoilRecoverDuration`/`muzzleLightDuration`/`particleDuration`/`hitRingDuration`/`turretPivot`/`muzzleTransform`）。**Setup() 只读取、不覆盖这些值**；运行时统一加载 `Tower_Minigun_{Faction}`。
- **外层逻辑**：`Tower.prefab` 仍是 UnitView/碰撞体/血条/阵营宿主，旧 `Visual` 已停用，改为空 `VisualRoot` 节点；`SetupTowerVisual()` 运行时 `Resources.Load` 对应包装 Prefab 实例化到 `VisualRoot` 下。旧塔备份在 `Legacy/Tower_Legacy.prefab`。
- **阵营映射**：defender→`Red`、challenger→`Blue`。
- **塔类型统一**（`ResolveTowerType(UnitView)`）：固定返回 `"Minigun"`，所有防御塔（红/蓝）都加载 Minigun 塔模型，不再按 slot 区分 Flamethrower/Minigun/RPG。
- **待机朝向**：`idleYawOffset`（默认 180°）只作用于炮塔节点 Rest Rotation；攻击用世界空间 `LookRotation` 精确指向 targetPos，不受待机偏移影响（不整转 VisualRoot 以免反转攻击方向）。
- **攻击表现**：仅 `OnCommand` 的 `case "attack"` 触发（`u.view.TriggerTowerAttack(wp)`）。`Fire()` 做炮塔转向 + 两阶段后坐力（`EaseOutCubic` 快速后退 + `Smooth01` 平滑恢复）+ Muzzle 粒子/闪光 + **Tracer**（真实枪口 `MuzzleWorldPosition()` → targetPos，**按阵营配色**红/蓝，统一 Minigun 细线 0.07/0.04、0.15s 淡出）+ 命中闪光圆环（0.40s：淡入 0.05s/保持 0.10s/扩大淡出 0.25s）。旧通用激光对 type==3 在 `OnCommand` 与 `OnDamage` 均已禁用，避免新旧射线重叠。
- **暂停/Seek**：暂停冻结炮塔/后坐力/粒子/Tracer/命中闪光；Seek（`Step` `!withFx` 分支 + `LateUpdate` 跳变检测）`ResetAttack()` 清空全部并复位到 180°；塔销毁时 `OnDestroy` 清理 Tracer/命中闪光根对象。
- **血条**：`UpgradeHpTo3D()` 对 type==3 用 `TowerVisualController.VisualHeight()/VisualWidth()`（已排除 ParticleSystemRenderer，否则拖尾撑大包围盒）。

### 坐标系统
- `StateEngine.CellToWorld(x,y)` → `(x-20, 0, y-15.5)`
- `SceneBuilder` 用 `oz - y` 转换 Z（与 StateEngine 同向）
- 单位位置 `transform.position = (state.pos.x, 0.01f, state.pos.z)`，Y 锁死贴地

### BGM 系统（2026-08-21）
- **挂载**：`ReplayEntry.Awake` 里 `gameObject.AddComponent<BgmController>()`，同 GO `DontDestroyOnLoad`；`BgmController` 零耦合（只读 `ReplayPlayer.playing/RoundFloat`）。
- **双通道 CrossFade 按回合推进**：两个 `AudioSource`（loop，volume=0），切换时一个淡出一个淡入。**进度按 `|RoundFloat - 上一帧|` 推进、时长用「回合」而非秒**——秒数制在 1x/2x 下换算回合差 2 倍（3 秒在 1x≈6 回合 / 2x≈12 回合，夜晚曲拖到第二天）；回合制速度无关。正常 2 回合，Seek 跳变（>5 回合）0.3 回合瞬时切。
- **昼夜判定**：`Mathf.Repeat(roundFloat, 130) >= 75`（130 回合/周期，配合 `StateEngine.DayOf/IsNight` 同周期）。75 起开始白天→夜晚过渡、78 回合完全铺满夜晚曲；夜晚→白天最迟第二天第 3 回合切回白天曲。
- **起始偏移 + 选段循环**：读 `BgmAudioConfig`（编辑器工具写），`Play()` 前 `src.time = 偏移`；`Update` 里播到 `clip.length - 0.15` 就 `src.time = 偏移`（loop=true 兜底防断音）。偏移 0 = 整曲循环。
- **音量档**：`VolumeLevel{ Mute=0 / Low=1 / High=2 }`，`TargetVolume` = 0 / 0.15 / 0.4；`CycleVolume()` 循环切换，UI 按钮每帧读 `CurrentVolumeLabel()`（静音/音量·低/音量·高）。
- **暂停冻结**：`AudioListener.pause = !(player != null && player.playing)`；**淡出通道判定不能靠 `isPlaying`**（暂停时恒 false，见「已知大坑」）。
- **WebGL Autoplay**：`#if UNITY_WEBGL && !UNITY_EDITOR` 下首次 `Input.anyKeyDown || Input.touchCount > 0` 才 `_audioUnlocked=true` 开始播放，未解锁前 Update 直接 return。

---

## 五、🔥 如何更换模型素材

### 更换野兽 Robot（Beast_11~14）

**在 Unity Editor 中操作，不需要写代码：**

1. 在 Project 窗口找到 `Assets/Resources/Prefabs/Beasts/Beast_11.prefab`，双击打开 Prefab Mode
2. 展开 `Beast_11 → Visual → RobotAdjust`，选中旧的 Robot 子节点，Delete
3. 从 `Assets/Robots Ultimate Pack 01 Cute Series/.../Prefabs/` 拖入新的 Robot Prefab 到 RobotAdjust 下
4. 调整 `RobotAdjust` 的 Transform：
   - **localScale**：模型尺寸
   - **localPosition.y**：脚底贴地偏移
   - **localRotation.y**：模型正前方修正（0° = +Z 前方）
5. 同法操作 Beast_12/13/14
6. 如果新 Robot 的 Controller 也是零参数（`m_AnimatorParameters: []`），运行时 `SetupRobotAnimator()` 会自动创建 OverrideController
7. 如果新 Robot 缺少 Die/Attack/Walk 动画，对应状态会用 Idle 兜底，不会崩溃

**涉及文件**：仅 Beast_XX.prefab，不需要改任何 `.cs` 代码

### 更换角色模型（Worker/Pioneer）

**Prefab 内直接替换 FBX 节点：**

1. 打开 `Assets/Resources/Prefabs/Units/Worker.prefab`
2. 展开 `Worker → Visual → Model`，找到旧的 SkinnedMeshRenderer 子节点（如 `Barbarian_Head` 等），Delete
3. 从新 FBX 资源拖入模型节点到 Model 下
4. 选中 `Model` 节点，在 Inspector 中更新 Animator 的 Avatar 为新的
5. 如果新 FBX 也是 Humanoid（KayKit 冒险者系列都是），`Adventurer_AnimatorController` 可直接复用
6. 如果新 FBX 是 Generic 或不同骨骼：
   - 替换 `Model` 上的 Animator Controller 为新素材自带的
   - 在 `UnitView.ConfigureFromUnitPrefab()` 或 `ConfigureFromBeastPrefab()` 中确认 `_animator` 引用和参数名兼容
   - 如参数名不同，在 `UpdateAnimation()` / `TriggerAttack()` / `TriggerDeath()` 中适配

**注意**：Worker/Pioneer 是 Humanoid + `Adventurer_AnimatorController`，有完整 isMoving/onAttack/onDeath 参数，换同包内其他角色（Barbarian→Knight 等）只需换 FBX + Avatar。

### 更换/调校防御塔（type=3）

**只调尺寸/朝向/后坐力/时间参数，不换塔**：直接打开 `Assets/Resources/Prefabs/Buildings/CubeTowers/Tower_Minigun_{Faction}.prefab`（红/蓝各一，现在只加载这两个），在 `TowerVisualController` Inspector 里改 `visualScale`/`yOffset`/`forwardYawOffset`/`idleYawOffset`(默认180)/`turnSpeed`/`recoilDistance`/`aimHoldDuration`/`recoilKickDuration`/`recoilRecoverDuration`/`muzzleLightDuration`/`particleDuration`/`hitRingDuration`，Play Mode 直接生效，**不需要改 C# 默认值**。

**想恢复三种塔区分（slot 映射）**：把 `TowerVisualController.ResolveTowerType()` 改回按 slot 计算（原来按该队 type==3 id 升序 `%3` → `{"Flamethrower","Minigun","RPG"}`），并在 `TURRET_NODES` 补回 `Flamethrower`=`Flamethrower`、`RPG`=`Rpg` 映射；需确保 `Assets/ProjectAssets/CubeTowerDefense_BuiltIn/Resources/Prefabs/Towers/` 有对应源塔，`CubeTowers/` 有对应视觉包装 Prefab（可重跑菜单 `Tools/WildernessReplay/Build Tower Visual Prefabs`）。

**换整套塔模型素材**（未来换别的塔包）：按第 2 阶段流程生成 ProjectAssets 源塔，再重跑 `Tools/WildernessReplay/Build Tower Visual Prefabs` 生成视觉包装，代码只需确认炮塔节点名 + Muzzle 节点名是否匹配 `TURRET_NODES`。

### 换模型后必须验证

- [ ] Play Mode 中 Idle 动画正常循环
- [ ] 移动时不滑行（Walk clip 匹配）
- [ ] 攻击和死亡动画触发正确
- [ ] 脚底贴地（调 RobotAdjust.y 或 Prefab root position）
- [ ] 模型正前方朝向正确（调 RobotAdjust.yaw 或 Model rotation）
- [ ] 血条在头顶上方可见
- [ ] Console 无 Animator 参数/状态相关错误
- [ ] 野兽阴影已关闭（新 Robot 模型默认 Cast/Receive Shadows 开启，需在 Prefab 的 MeshRenderer/SkinnedMeshRenderer 关闭，或按 2026-08-19 资产根治方式统一处理）
- [ ] 野兽无入场粒子（新 Robot 若带 playOnAwake 粒子需删除，避免登场闪现白圈）

### 更换 BGM / 选取播放片段（2026-08-21 新增）

**只改文件、不动代码：**

1. **替换音乐**：直接把新音频文件丢进 `Assets/Resources/Audio/BGM/`，文件名必须保持 **`bgm_day`** 和 **`bgm_night`**（扩展名随意：`.ogg`/`.wav`/`.mp3`/`.aif`，运行时按名字加载）。推荐与原文件**同名同扩展**覆盖（`.meta`/GUID 保留，引用最稳）。替换后 Unity 自动重新导入，进 Play 模式立即生效。**WebGL 构建必须重新 Build**（Resources 是构建时打包，不热更新）。
   - 素材缺失时不崩：`[BgmController] 加载失败...` 一条 Warning + 对应时段 BGM 静音，游戏照常跑。

2. **选取播放片段（选段工具）**：菜单 **Window → BGM 选段工具**
   - 下拉选曲 → `▶ 从偏移试听` / 拖**进度条**实时选段 → `🎯 当前位置设为起始` → `💾 保存配置`
   - 保存到 `Assets/Resources/Audio/BGM/BgmAudioConfig.asset`（`dayStartTime`/`nightStartTime`，单位秒）
   - 运行时：对应曲目从所选偏移开始播放，**播到所选片段结尾后回到偏移循环**（不绕回整曲开头；偏移 0 = 整曲循环）
   - 工具按文件名找素材、不挑扩展名，换 `.mp3`/`.wav` 也能用
   - 换曲后偏移按秒数自动 clamp 到新曲长度，建议换完重选一次

**涉及文件**：BGM 素材在 `Assets/Resources/Audio/BGM/`，配置在 `BgmAudioConfig.asset`，无需改任何 `.cs` 代码。

---

## 六、常见修改指南

| 想做什么 | 文件 | 复杂度 |
|---------|------|:---:|
| 调血条高度/宽度/厚度/深度 | `UnitView.Hp.cs` 顶部 `HP_BAR_STYLES` 配置表（按 type 一行：`yOffset`/`yFactor`、`widthMul`、`thick`、`depth`） | 低 |
| 调野兽模型大小/高度 | Beast Prefab 中 `Visual/RobotAdjust` 的 localScale / localPosition.y / localRotation.y | 低 |
| 换野兽 Robot | Beast Prefab 中删除旧 Robot 子节点 → 拖入新 Robot Prefab 到 RobotAdjust 下 | 低 |
| 调树大小/概率 | `SceneBuilder.cs` BuildForestSkirt() 中的 treeProb/scale | 低 |
| 调矿石大小 | `ResourceViewManager.cs` GetOrCreate() 中的 scale | 低 |
| 加新单位类型 | `UnitView.UNIT_PREFABS` + Prefab | 中 |
| 改血条颜色 | `UnitView.Hp.cs` 顶部 `HP_COLOR_ROBOT/DEFENDER/CHALLENGER/NEUTRAL` 常量或 `GetHpColor()` | 低 |
| 调夜晚角色光环（大小/透明度/颜色/位置） | `UnitView.Aura.cs` 顶部常量：`NIGHT_AURA_RATIO`（比例，调大更大）/`NIGHT_AURA_ALPHA`（透明度，<1 更淡）/`NIGHT_AURA_FOOT_Y`（垂直偏移，调大上移）+ `NIGHT_AURA_DEFENDER/CHALLENGER`（阵营色）；换特效 prefab 改 `NIGHT_AURA_RES` 并实测更新 `NIGHT_AURA_NATIVE`/`FOOT_Y` | 低 |
| 改围栏样式 | `SceneBuilder.cs` BuildPerimeterFence() 中的 fenceFbx 路径 | 低 |
| 调塔尺寸/朝向/后坐力/时间参数 | 打开 `CubeTowers/Tower_Minigun_{Faction}.prefab` 的 `TowerVisualController` Inspector 字段 | 低 |
| 调炮塔俯仰幅度 | 同上 Inspector 的 `pitchLimit`（默认 70°，攻击时头部上下跟随目标高度） | 低 |
| 调塔大小（直接改 Prefab scale） | 改 `CubeTowers/Tower_Minigun_{Faction}.prefab` 根 Transform 的 scale（与 `visualScale` 相乘，默认 1.6） | 低 |
| 切换塔模型（当前统一 Minigun） | `TowerVisualController.cs` 的 `ResolveTowerType()` / `TURRET_NODES` | 低 |
| 换塔模型素材 | 生成 ProjectAssets 源塔 + 重跑 `Tools/WildernessReplay/Build Tower Visual Prefabs`（见第五节） | 中 |
| 换炸弹/眩晕特效 | `FxFactory.cs` 顶部 `RES_BOMB`/`RES_DIZZY` 路径（或直接替换 `Resources/FX/` 下 prefab）；调大小改 `BOMB_SCALE`/`DIZZY_SCALE` | 低 |
| 改 WebGL replay 加载路径/换远程链接 | `ReplayEntry.cs` `Load()` 的 WebGL 分支：改 `RelativeStreamingUrl("replay.txt")` / `("demo_replay.jsonl")` 两处文件名；换远程完整链接需绕过 `RelativeStreamingUrl` 直接传完整 URL | 低 |
| 换 BGM / 选播放片段 | 直接替换 `Resources/Audio/BGM/` 下 `bgm_day`/`bgm_night` 文件 + `Window → BGM 选段工具` 设起始偏移（见第五节） | 低 |
| 调入夜/天亮节奏 | `BgmController.cs` `IsBgmNight()` 阈值（`>= 75`，130 回合周期；74→白天，75→开始入夜） | 低 |
| 调 CrossFade 时长 | `BgmController.cs` `NORMAL_FADE_ROUNDS`（正常 2 回合）/ `SEEK_FADE_ROUNDS`（0.3） | 低 |
| 调音量档 | `BgmController.cs` `TargetVolume()`：Mute=0 / Low=0.15 / High=0.4 | 低 |

---

## 七、🔥 已知大坑

| 坑 | 说明 |
|----|------|
| **AssetDatabase 仅 Editor** | `LoadAsset<T>` 有 `#if UNITY_EDITOR` + Resources.Load 双回退。Build 需要 Resources/Prefabs/Environment/Forest/ 下的包装 Prefab |
| **红蓝阵营色反了** | defender 显红色模型，challenger 显蓝色模型 |
| **Mathf.SmoothStep ≠ HLSL smoothstep** | C# `Mathf.SmoothStep(from,to,t)` 是插值函数（以 t 为 0~1 因子在 from/to 间插值），不是 HLSL `smoothstep(edge0,edge1,x)` 的 0~1 阶跃。圆环遮罩和昼夜 Blend 必须用自定义 `Smooth01`（基于 `Clamp01` + Hermite 曲线），见 `MatLib.Smooth01()` 和 `DayNightController.Smooth01()` |
| **昼夜 130 回合/天** | `StateEngine.DayOf(n)` / `IsNight(n)` 硬编码 130 回合周期（80 白天 + 50 夜晚）。`DayNightController` 通过 `ReplayPlayer.RoundFloat`（连续浮点值）计算 `cyclePosition = Mathf.Repeat(roundFloat, 130f)`，黄昏 72-80、黎明 122-130 |
| **MainModule 是结构体** | `var m = ps.main; m.playOnAwake = false;` 这种写法有效（MainModule 属性 setter 直写原生对象），但不要对 `ps.main` 整体赋值 |
| **WebGL "Insecure connection not allowed"** | HTTP 页面下 `UnityWebRequest.Get(绝对 http://URL)` 抛 `InvalidOperationException`。`ReplayEntry.RelativeStreamingUrl()` 把 `Application.streamingAssetsPath` 归一化为「相对当前网页」路径（剥掉协议+host，协议跟随页面），`LoadWebText()` 同步段包 try/catch 兜底走 demo，异常不中断初始化 |
| **`AudioSource.isPlaying` 在 `AudioListener.pause` 时恒 false** | 回放暂停（`AudioListener.pause=true`）时任何 `AudioSource.isPlaying` 都返回 false。`BgmController` 判断「要淡出的旧通道」**不能靠 `isPlaying`**（否则暂停拖时间轴 seek 时旧曲不淡出，恢复播放双曲叠加），必须按 `clip` 归属判定、结束无条件 `Stop()`。换/调 BGM 逻辑时注意 |

> 资源 / 材质 / 模型 / 贴图 / 动画资源类坑已全部移至 [资源问题与解决方案.md](资源问题与解决方案.md)，此处只保留**代码/逻辑**类坑。

---

## 八、近期改动

> 📄 详细实现文档见 [夜间机器人卡顿优化_实现记录.md](夜间机器人卡顿优化_实现记录.md)（2026-08-20 夜间卡顿的完整改动方法、代码、关键坑）；任务卡片见 [任务卡片实现与升级方案.md](任务卡片实现与升级方案.md)（**素材更换指南**（图片/视频位置、H.264 格式要求、转码脚本）+ 当前 4 态实现 + WebGL 关键点）。

| 日期 | 改动 |
|------|------|
| 2026-09-02 | **武器工事换用 SciFiStrategyLowPoly 防御塔 + 导入报错修复**：① 修复 `SciFiStrategyLowPoly` 导入报错——`Main.mat` 引用 **Built-in 目标** Shader Graph，装免费包 `com.unity.shadergraph` 14.0.12 + 强制重导后全包 53 个模型不再粉紫（共享一材质，无需烘焙/remap）；17 个 `Animation/*.fbx` 关动画导入消除 0 帧报错；删除无引用损坏的 `CannonShell.prefab`（详见 [资源问题与解决方案.md](资源问题与解决方案.md) 第一节）。② 武器工事模型替换：roleType 30/31/32 由 CubeTowerDefense 三塔改为 **SciFi 塔（AntiAir/Laser/Rocket）**——`TowerVisualController.ResolveTowerType` 新映射 + `TURRET_NODES`→`Horizontal`（SciFi 塔偏航枢轴）；新增 `Assets/Editor/SciFiTowerPrefabBuilder.cs` 生成 6 个包装 Prefab（`CubeTowers/Tower_{Type}_{Faction}`，visualScale≈0.52 占地 0.67m 与旧塔一致，AntiAir 枪口=`MuzzleFlash`、Laser/Rocket 用 forward fallback）；开火表现：Laser 复用粗激光+落点电击分支、Rocket 无弹道（爆炸由 ReplayPlayer）、AntiAir 多 tracer。实测 Play 模式：6 座塔正常渲染（0 粉紫像素）、炮塔 180°→目标转向正常、Resources.Load 全通、编译 0 error |
| 2026-09-02 | **SciFi 塔迭代（比例/低饱和色/特效延伸）**：① 炮塔 `Horizontal` 节点放大 **1.5x**、底座占地加宽到 0.85m（世界 1.4m 高不变，比例更自然）；② 阵营色改 **HSV 低饱和**（红 HSV(0,0.5,0.7)≈砖红 / 蓝 HSV(0.59,0.5,0.7)≈钢蓝，去荧光）；③ 激光攻击时**延伸到落点**（LineRenderer/End 逐帧 `InverseTransformPoint` 追踪，随炮塔转向始终指向目标）；④ 火箭导弹**飞到落点坐标再爆炸**（爆炸+震屏移到塔视觉 `LaunchRockets` 到达时触发 `FxFactory.PlayBombEffect`，`ReplayPlayer` type32 不再即时爆炸、塔视觉未就绪兜底；暂停冻结）。实测 Play：激光末端精确落点、火箭到达后 CFXR 爆炸、低饱和色无荧光、0 粉紫 |
| 2026-09-02 | **拆分 TowerVisualController.cs（905→5 partial 文件，各 < 300 行）**：照 UnitView partial 模式按职责拆——`TowerVisualController.cs`(286: 类声明/序列化配置/共享运行态/Setup 调度/通用 helper) + `.Aim.cs`(215: Fire/开火/复位 + LateUpdate 每帧调度器) + `.Laser.cs`(105: 激光多光束) + `.Rocket.cs`(109: 火箭直飞/爆炸) + `.Fx.cs`(246: 弹道线/命中环/命中火花/OnDestroy 清理)。行为零改动；Play 实测三种塔开火/激光/火箭全部正常 |
| 2026-09-02 | **武器等级 → 塔模型（_1/_2/_3）**：生成器改为按等级建 `Tower_{Type}_{Lv}_{Faction}`（3 类型×3 等级×2 阵营=18 个）；`ReplayRole.level`（武器工事 1~5）已解析到 `state.level`，`UnitView.SetupTowerVisual` 按 `clamp(level,1,3)` 加载对应等级模型（4~5 级用 _3），`RefreshTowerLevelVisual()` 在 `LateUpdate` 逐帧比对等级（照 WallOrientation）实现**升级实时换模型**；Laser 高等级=多光束（Laser_2 双束/Laser_3 三束），运行时按 `LaserBeam*` 前缀收集、开火全部延伸对准落点。实测 Play：等级 4 Minigun 显示 _3、Laser 等级 1→2 自动换 TowerVisual_Laser_2、Laser_2 开火 2 束 |
| 2026-09-02 | **SciFi 塔修复（火箭巨大导弹 bug + 加特林弹道可见化）**：① 火箭导弹 **归位时还原原始 localScale**（`ResetRocketMissiles` 记录发射前 scale）——否则导弹挂在炮塔 Horizontal 1.5x 下，reparent 往返会**逐次放大 1.5 倍 → 数次开火后变成巨大导弹**；② 加特林(30→Minigun)开火**对每个落点画粗弹道线**（`SpawnTracer` 加粗 0.14→0.04、0.22s，直观显示打到哪个机器人）+ **落点播原生 Hit 火花**（`SpawnGatlingHit`，`Hit.prefab` 复制到 `Resources/FX/Hit`），保留原生 MinigunTracers 枪口喷射。实测 Play：导弹连发后 scale 稳定回 (1,1,1) 无巨大化、Minigun 双目标出 2 条宽弹道 + 2 个落点 Hit |
| 2026-09-01 | **装甲车任务完成 → 破损车修复成完好车 → 开向小贩售卖 → 消失重生**：`MissionVehicleDriver`（挂 ReplayEntry，监控各队「自进化类2」任务完成跳变）+ `MissionPoint.StartSellCycle`（任务点初始/重生是**破损车** `broken_K151ArmoredVehicle.prefab`，完成时在原任务点换成完好车 `K151ArmoredVehicle.prefab` → 直线开向小贩、小贩前 `STOP_BEFORE_VENDOR=1.2m` 停下不调头 2s → 卡车上显示「贩卖成功」徽标 `TradeBadge.ShowText`（工人购买面板样式，1.5s 后脱离淡出）→ 消失 → 重生破损车，暂停冻结）；`ReplayTask` 增加 `taskX/taskY` 解析（任务 `pos` 字段指向对应卡车）。实测 Play 模式：初始破损车 → 触发即换完好车（isBroken 变 false）→ 完好车停小贩前 + 贩卖成功徽标 → 重生破损车；编译 0 error |
| 2026-08-31 | **任务卡片按任务点区分显示（自进化类1 全流程 / 自进化类2 纯文字）**：`FX/TaskCardBadge.cs` 加**文字模式**（`_textMode = (_taskType == "自进化类2")`）——装甲车任务点（game 17,17/26,17）只显示 TradeBadge 风格状态文字（深色底+黄字）：接受任务→正在修理中→修理成功/失败，Success/Fail 2 回合淡出；宝箱任务点（自进化类1，game 14,14/23,14）走全流程卡片（claim 图/working 视频/unlock 图）。`TaskBadgeManager` 统计共享 working 播放时跳过文字模式卡（不显示视频，避免空转解码）。实测：自进化类2 Working='正在修理中'、Fail='修理失败'（黄字深底、MainTex=null）；自进化类1 Working=视频RT、Fail=unlock_fail 图；编译 0 error |
| 2026-08-31 | **任务卡片改"仅 Working 视频、其余静态图（各 2 回合）"**：新增 `Assets/Resources/Sprites/claim.png`（Intro 领取，1024²）、`unlock_success.png`（Success 解锁成功，2048×1024）、`unlock_fail.png`（Fail 解锁失败，2048×1024）——图片**从 StreamingAssets 移到 Resources**（`Resources.Load` 同步加载、WebGL 安全，避免异步加载中间态）；`FX/TaskCardBadge.cs`：Intro/Success/Fail 改 `LoadTex` 图片、`_shownUrl=null`，结果图 2 回合后淡出（`_resultStartCur` + `RESULT_ROUNDS=2`，round-based 速度无关）；删除 `BeginClaimVideo`/`BeginResultVideo` 及结果视频相关（`_resultDuration`/`_resultPreloadStarted`/SUCCESS/FAIL_VIDEO）。`FX/TaskBadgeManager.cs`：只建 working 一个共享播放器；共享 working 仅在"有卡在 Intro（预卷）或 Working"时播放、空闲暂停省解码。实测：Working=RenderTexture（视频）、Intro=claim、Success=unlock_success、Fail=unlock_fail；编译 0 error |
| 2026-08-31 | **K151 装甲车 + 宝箱适配 Built-in 并放入 Resources**：新增 `Resources/Prefabs/K151ArmoredVehicle.prefab`、`GoldChest.prefab`（未来任务点，包装 prefab）。根因与完整解法见 [资源问题与解决方案.md](资源问题与解决方案.md) |
| 2026-08-25 | **结果视频改为卡片自有播放器（从头播一遍再淡出）+ 卡片放大 2 倍**：① **Success/Fail 视频"前 ~1s 被播两遍"根因**：共享播放器一直在循环，`VideoPlayer.time=0` 的 seek 在播放中是**延迟生效**的（实测设 0 后立刻读回仍是旧值），卡片切到共享 RT 会先显示旧位置画面（若恰好靠近开头即"前 1s 播两遍"）。修复：`BeginResultVideo` 改用**卡片自己的 slot**——Awake 后台 `Prepare` success/fail，结果到来时 `isLooping=false`、`time=0`、从头播一遍，`_resultDuration=视频时长` 播完淡出销毁；`OnVideoLoop` 对结果视频不再重启（仅 working 循环）；Update 在结果播完（`_elapsed>=时长` 或视频到末尾）时不再续播。② 卡片 `CARD_SCALE` 2→4（底板 4×2.4、文字/描边/定位同步放大，用户反馈卡片小看不清视频）。实测：卡片底板 Bg.localScale=(4,2.4,1)；Fail 卡（r33 暂停）自有 fail 播放器 time=0 从头；编译 0 error |
| 2026-08-24 | **任务卡片金黄描边随淡出一起消失**：`FX/TaskCardBadge.cs` 的 `ApplyAlpha` 原只淡背景底板+文字，**金黄描边（Border #FFD700）没跟着淡** → 结果视频淡出后金边残留 ~0.2s 的黄色卡片。新增 `_borderRend`/`_borderMpb` 字段（Awake 存描边渲染器+MPB），`ApplyAlpha(a)` 里同步设置描边 `_Color` 的 alpha（金黄 RGB + a）。实测 `ApplyAlpha(0.5)` 后 borderAlpha 与 bgAlpha 同为 0.50，淡入淡出三者一致 |
| 2026-08-24 | **任务卡片视频改全局共享 + WebGL 视频不显示修复**：实测发现 working 视频"本地 Prepare 需 ~3-4s（播放期渲染负载下更慢），Intro/Working 阶段太短，Working 全程只显示 Intro 图再直接跳结果"；且 **WebGL 导出后视频全不显示（占位图）**（Editor 正常、URL 正斜杠且 GET 200/304）。修复：① `FX/TaskBadgeManager.cs` 建**全局共享播放器**——`EnsureSharedVideo(file)` 对 working/success/fail 各建隐藏 VideoPlayer，游戏开始即 `Prepare` + 循环播放进共享 RT（`GetSharedVideoRT`/`GetSharedVideoLength` 供卡片取用），`Update` 统一随回放暂停/播放冻结 + **`isPrepared` 轮询兜底建 RT/开播**（WebGL 上 prepareCompleted 可能不触发）；② `FX/TaskCardBadge.cs` `BeginWorkingVideo()`/`BeginResultVideo(url)` **优先显示共享 RT（立即可用无中间态）**，共享未就绪回退本地 slot（`SyncPreparedSlots` 也加 isPrepared 轮询）；`VideoUrl()` 改 `public static`；③ **WebGL 三关键**：**视频编码必须是 H.264（avc1）**——原视频是 `mp4v`（MPEG-4 Part 2）浏览器不支持，已用 Windows Media Foundation（PowerShell `_transcode.ps1`）转 H.264/AAC 并替换（原 mp4v 备份 `_task_videos_mp4v_backup/`，转后尺寸/时长不变：working 960×540 4s、success 1280×720 3.4s、fail 960×540 2.4s，Editor 实测全部 isPrepared+isPlaying+RT 非黑）；`audioOutputMode=None`（静音放行 autoplay）；`isPrepared` 轮询兜底。实测：Editor working 就绪从 ~4.4s 提前到共享 RT 即开即用（Working 起始帧即显示，r20 双卡 shown=working.mp4、MainTex=共享 RT）；本地 slot 保留为回退 |
| 2026-08-24 | **任务卡片 Success/Fail 视频替换（多 slot 无中间态）**：新增 `Assets/StreamingAssets/TaskVideos/success.mp4`（1280×720 约3.4s）/`fail.mp4`（960×540 约2.4s）；`FX/TaskCardBadge.cs` 渲染层重构为**多 VideoPlayer slot**——`_videos` 字典按 url 缓存 working/success/fail 三路独立 VideoPlayer+RenderTexture（`VideoSlot`：url/player/rt/prepared/failed），`EnsureVideo()`+`StartVideoPreload()`（**Intro 接任务即对全部视频 `Prepare()`**，working 预卷蓄帧、结果视频就绪备用）+`BeginResultVideo(url)`（**结果态：就绪→`PlayVideo` 不可见开播；未就绪→保持当前画面（working 视频/Intro 图）不动**）+`Update` 驱动（目标视频渲染出帧 `frame>=1` 瞬间换底板 `_MainTex`；已显示保证播放、其余 slot 停播）——四个状态切换**均无中间态，绝不蓝/白/空加载底**。结果视频 `isLooping=true` 循环，`_resultDuration=max(1.5s, 视频时长+0.1)` 播完一遍淡出销毁；`OnVideoError` 按状态降级纯色+文字（Working 蓝"破解中"/Success 绿"✓ 通过"/Fail 红"× 失败"）。实测：隔离卡 Success/Fail 均 shown=对应视频、MainTex=RT、视频 playing、RT 中心非黑、working 停播、按视频时长淡出；切换首帧保持上一画面无空档；**真实 replay r33 失败段**：暂停直跳→Intro 图兜底无白底，慢速播放→真实 Fail 卡 `shown=TaskVideos/fail.mp4`、fail 视频 playing（frame=26）、RT 中心非黑（0.33,0.17,0.23）、`fallback=False`；console 0 error |
| 2026-08-24 | **任务卡片 Intro→Working 无中间态切换（预载视频 + 未就绪保持图片）**：`FX/TaskCardBadge.cs` 重构——新增 `EnsureVideoPlayer()`（创建/配置一次）＋`StartVideoPreload()`（**Intro 接任务即后台 `Prepare()` 预载视频**，不显示不播放；`OnVideoPrepared` 里静默预卷开播进 RT，为 Working 蓄帧）＋`BeginWorkingVideo()`（**Working：已就绪→直接上视频；未就绪→保持 Intro 图片**，`OnVideoPrepared` 就绪瞬间无缝接管）＋Intro 用 `_vp.Stop()` 取代旧 `StopVideo()`（保留已就绪资源复用）。去掉旧"纯蓝/纯白等待态"——`_stateColor` 在 Intro/Working 全程保持白（图片/视频都是 tex×白），切换只是换 `_MainTex` 引用，**无任何颜色中间态**。`TryPlayVideo` 加 `_videoPrepared` 守卫，未就绪不空转。实测：r12 Intro=tex2d 图片+白+视频已预载（暂停跟随冻结）；Working=r14 直接 tex=RT+白+playing；隔离卡强制 Working 未就绪瞬间=图片+白（非蓝非白底）、下一帧就绪=RT+白+playing 循环中；循环跨 4.0s 回绕；console 0 error |
| 2026-08-24 | **任务卡片 Intro 图片替换 + 防叠卡加固**：新增 `Assets/Resources/Sprites/task.png`（「接受任务」Intro 底板图，1024×1024，Resources.Load 打包进 Build）；`FX/TaskCardBadge.cs` 加 `IntroTex()`/`SetMainTexture()`——Intro 状态底板 MPB `_MainTex`=task 图片、`_Color`=白（不 tint），其余状态 `_MainTex`=null 恢复纯色。`FX/TaskBadgeManager.cs` 加两道防线：① `Awake` 多实例自毁（防编译/域重载后多 manager 各建卡叠卡）；② 创建前查父节点已有 `TaskCardBadge` 则复用而非新建。实测：r12 Intro 卡 MPB_TEX=task 1024×1024 + 文字保留，r30 Working 双卡 MPB_TEX=null 恢复纯色，编译后立即 play 播放至 r165 dict/sceneCards 恒 2，编译 0 error。视频阶段规范见 [任务卡片实现与升级方案.md](任务卡片实现与升级方案.md) |
| 2026-08-24 | **任务卡片修复（Working 残留图片 + 金黄描边）**：`SetMainTexture(null)` 原用 `_mpb.SetTexture("_MainTex", null)`，MaterialPropertyBlock.SetTexture **不接受 null → 抛 ArgumentNullException**（console 刷屏，异常中断导致 `_MainTex` 从未清除 → Working/Success/Fail 底板残留 Intro 的 task.png）。改为 `_mpb.Clear()` 清空全部属性再按需 `SetTexture`。另：卡片加**金黄描边**（`BORDER=0.05`，Awake 建稍大 Border Quad 垫在 Bg 后，MPB `_Color`=0xFFD700，卡片在草地/图片上轮廓清晰）。实测：Working/Success/Fail Bg 均 hasTex=False 纯色（task.png 已清除）、Intro hasTex=True task 图片 + 金黄 Border，console 0 error |
| 2026-08-24 | **任务卡片 Intro 优化（展示 2 回合 + 去掉叠字）**：① **至少展示 2 回合**——数据上 `roundCost==0` 仅接任务那 1 回合，Intro 图片一闪而过；`TaskCardBadge` 增加 `_introStartCur`/`INTRO_MIN_ROUNDS(2)`：`SwitchTo(Intro)` 记录起始回合，`SetState` 里当前 Intro 且数据已切 Working 但 `player.cur - _introStartCur < 2` 则延迟切换继续展示图片（结果态 Success/Fail 不受延迟、Seek 即时生效）；② **Intro 不叠文字**——task.png 自带「接受任务」字样，`ApplyStateVisuals` 里 Intro 状态 `_txtGo.SetActive(false)` 隐藏文字。实测（真实任务段 r12-r14）：r12 Intro+文字隐藏、r13 数据已切 Working 仍 Intro（延迟生效）、r14 满 2 回合切 Working+文字"破解中"，console 0 error |
| 2026-08-24 | **任务卡片 Working 视频替换 + 3 处渲染 bug 修复**：新增 `Assets/StreamingAssets/TaskVideos/working.mp4`（用户素材约 1MB）；`FX/TaskCardBadge.cs` 实现 `StartWorkingVideo()`/`OnVideoPrepared`/`OnVideoError`/`StopVideo()`/`TryPlayVideo()`/`WorkingVideoUrl()`——Working 状态底板 `_MainTex`=**VideoPlayer+RenderTexture 循环播放 working.mp4**（文字隐藏，视频自带字样）；WebGL 用相对 StreamingAssets URL（剥 `http(s)://host` 防协议混用）；暂停 Pause/恢复 Play/销毁释放；加载失败降级纯色蓝+"破解中"。**3 处修复**：① `SetMainTexture` 的 `_mpb.Clear()` 会清掉 `_Color`，而 `OnVideoPrepared` 异步回调时已暂停（`Update` 提前 return 不再跑 `ApplyAlpha`）→ 视频底图全透明不可见；改为 Clear 后立即补回 `_Color`（`_stateColor`，视频态=白）。② `showText` 从 switch 前移到 switch 后判定——原在 Working 视频模式下 `_videoWorking` 还是 false，文字对象被误激活。③ `StartWorkingVideo` 先 `SetMainTexture(null)` 清旧贴图再等视频就绪，杜绝 Intro 图/视频并存过渡帧。实测：r14 Working `_MainTex`=RT+`_Color`=白+Txt 隐藏+视频 playing 两卡独立时间轴（time 0.40/1.03）且 RT 中心像素非黑（0.20,0.33,0.45）视频真实渲染；暂停冻结视频且底图可见；r12 Intro 图+金黄描边+无视频；r33 team0 Fail 纯色红+"× 失败"+视频已停；console 0 error（仅 1 条 Editor 专属 WMF 色域告警） |
| 2026-08-24 | **任务卡片白色闪帧修复 + 视频循环保障**：① **白色空白**（Intro 图→Working 视频之间）——`_stateColor` 原在 `ApplyStateVisuals(Working)` 提前转白（为视频 tint），但视频未挂上 `_MainTex`（null）×白=纯白 Quad，视频准备期间闪白。修复：`_stateColor` **保持蓝直到视频就绪**——`OnVideoPrepared` 里才转白+`SetMainTexture(_vpRT)`；`OnVideoError` 降级先转蓝再清纹理。② **视频循环**（working.mp4 4s，Working 阶段跨多回合）——三重保障：`isLooping=true` + `loopPointReached` 事件 `OnVideoLoop` 兜底（`_vp.time=0`+Play）+ `TryPlayVideo` 未播放且未暂停时 `_vp.time=0` 后 `Play()` 每帧兜底重启，**显示时长跟回合走**。实测：真实回放 r14 两张卡一张准备中=纯蓝（非白）一张就绪=RT+白；隔离卡视频 time 1.53→3.17→0.77（跨 4.0s 回绕）→3.17 全程 playing → 循环生效；暂停冻结、恢复不断档；console 0 error |
| 2026-08-24 | **夜晚角色光环**：夜晚角色与机器人难区分 → 工人/开拓者(6/7) 夜晚常驻光环（最终用 `CFXR3 Magic Aura A (Runic)`，MPB 按阵营上色、特效贴地、自带 Point Light；曾试 Hovl Buff + 自加 AuraLight 灯效，已弃用/移除）；`Mathf.Repeat(RoundFloat,130)>=80` 判定夜晚、随昼夜显隐、暂停冻结；**修复暂停闪烁**（`FxFactory.SetGlobalPause` 加 `CFXR_Effect.GlobalDisableLights=paused`，CFXR 灯光动画随暂停冻结）+ **修复 Seek 到夜晚法阵隐形**（`SetAuraVisible` 显示时 `ps.Simulate(1s)` 预热成型 + `clearBehavior=None`）；新增 `UnitView.Aura.cs` partial（`SetupNightAura`/`UpdateNightAura`）接入 `ConfigureFromUnitPrefab`/`LateUpdate`。实测：6 角色全部挂载、白天隐藏/夜晚播放/暂停冻结、暂停灯强度稳定、Seek 后符文圈可见、阵营色正确。另：血条 Y/Z 解耦 + 外观参数收敛为 `HP_BAR_STYLES` 配置表（围墙深度减半 0.025、防御塔/基地血条抬高 +0.2） |
| 2026-08-21 | **新增任务卡片实现文档 + 新版 replay 生效**：新增 [任务卡片实现与升级方案.md](任务卡片实现与升级方案.md)（Phase 1 实现细节、Phase 2 图片/视频替换方案、新旧 replay 数据对比结论）。新版 replay（1010 回合，怪物数值大幅加强）已替换为正式 `StreamingAssets/replay.txt` 并重新导入（Unity 已生成 .meta），旧版 906 回合备份为 `replay_906.txt`。**两版 75 个 JSON 键完全一致、零字段差异**，解析器/任务卡片逻辑无需任何改动 |
| 2026-08-21 | **开拓者任务卡片定位/放大 + Seek 残留失败框修复**：新增 `FX/TaskCardBadge.cs`（4 态世界空间卡片）+ `FX/TaskBadgeManager.cs`（挂在 ReplayEntry），`ReplayEntry.Awake` 加一行 `AddComponent<TaskBadgeManager>()`。① 卡片从血条上方 `HpFill` 世界 Y + 0.5 净空定位（世界坐标计算，不受父节点缩放影响）+ 底板/文字整体 2× 放大；② **拖动进度条残留失败框 bug 根治**：管理器改为读 `rounds[cur-2]`（数据上一回合）而非上一帧快照做跳变检测，且暂停状态 `cur` 变化（`OnDrag` 先 `SetPlaying(false)` 再 `JumpTo`）时**先 ClearAllBadges 再按目标回合数据重建**——否则 Fail/Success 结果卡片在暂停时 1.5s 结束计时被冻结、永不淡出销毁，开拓者回到任务官前站着仍挂着失败框。实测：r23 任务段=2 Working，r50/r500 无任务站着=0 卡片，r33 真失败=team0 Fail+team1 Working，反复拖动序列末尾无残留。Parser/StateEngine/胜负判定/UI prefab 零改动 |
| 2026-08-21 | **BGM 系统 + 选段工具**：新增 `Audio/BgmController.cs`（昼夜双曲 CrossFade、音量档、暂停冻结、WebGL Autoplay）、`Audio/BgmAudioConfig.cs`（起始偏移配置）、`Audio/Editor/BgmAudioTool.cs`（编辑器选段工具）；`ReplayEntry.Awake` 挂载 + `PlaybackControlPanelController` 加「音量」按钮。**昼夜节奏**：130 回合/周期，`Mathf.Repeat(roundFloat,130) >= 75` 入夜（75~78 回合完成白天→夜晚过渡），CrossFade **按回合推进**（正常 2 回合淡入淡出、Seek 跳变 0.3 回合瞬时切，速度无关），夜晚音乐最迟第二天第 3 回合切回白天。**选段**：音乐从配置偏移开始、播到所选片段结尾后回偏移循环。素材在 `Assets/Resources/Audio/BGM/`（按名字加载，扩展名随意）。替换/选段方法见第五节，调参见第六节。Parser/StateEngine/胜负判定零改动 |
| 2026-08-20 | **UnitView.cs 拆分为 Partial Class（方案 C，0 回归）**：818 行上帝类按职责拆 5 文件——`UnitView.cs`(341 主文件：字段/Create/Configure*/LateUpdate/SetHp/SetStun) + `UnitView.Anim.cs`(172 动画装配/触发/倍速) / `UnitView.Hp.cs`(172 血条/光环) / `UnitView.Lod.cs`(119 距离LOD) / `UnitView.Tower.cs`(58 塔视觉)。纯物理搬运：类名/命名空间/GUID/字段与公开 API 签名零改动，13 个 Prefab（仅序列化 `strideCoefficient=1`）与 ReplayPlayer 等调用方零改动；`LateUpdate` 抽 `UpdateAnimationState()`+`UpdateLod()` 调度序列。编译 0 error/0 warning；Play 完整回放 906 回合 console 0 报错 |
| 2026-08-20 | **事件日志过滤移动消息**：`ReplayPlayer.Log` 过滤「cmd + 含" 移动 "」日志（StateEngine.Diff 生成的 xx 移动 (x,y)→(x,y)）；`OnCommand` default 分支 `c.action=="move"` 不再刷日志（英文 move (x,y)）。仅显示层过滤，StateEngine/胜负判定零改动；建造/采集/贩卖/任务等事件保留。实测面板 move 类日志 0、建造等事件正常 |
| 2026-08-20 | **远处静态机器人加轻微待机浮动 + 攻击/死亡瞬态动画**：实测"全部 156 只保持骨骼动画"会让 CPU 0.05ms→11.75ms、帧时→15ms（重新卡顿，LOD 必须保留）；在 `UnitView.LateUpdate` 给静态 LodMesh 加呼吸式上下浮动+缩放摆动（按 `state.id` 相位错开、暂停冻结，每只 2 次 Sin 可忽略）。另：`TriggerAttack`/`TriggerDeath` 时远处静态野兽临时恢复骨骼动画播放动作（冷却 2.5s+窗口 1s 限制并发，实测跳转后不加冷却会让 101/140 远处野兽全进动画 → CPU 回升）；`LateUpdate` 瞬态窗口内保持动画、窗口结束自动回静态。**全部参数已改为 public static 可运行时调**（`UnitView.LOD_RANGE / LodTransientCooldown / LodTransientWindow / LodIdleBobAmplitude / LodIdleSwayAmplitude`），详见实现记录「八、参数调优指南」 |
| 2026-08-20 | **WebGL 野兽数量 LOD + 血条实例化 + 日志面板批量刷新（机器人多时卡顿根治）**：夜间机器人 80~156 只卡顿 → (1) `UnitView` 距离 LOD（野兽按相机 XZ 水平距离 30 两档切换，滞回 0.85 防闪烁）：远处野兽 `SkinnedMeshRenderer.BakeMesh()` 一次性烘焙**每类型共享静态网格**（4 类型=4 网格）→ `MeshRenderer`+GPU 实例化（材质 enableInstancing）+ **禁用 Animator/SkinnedMesh**；近处保留完整骨骼动画。实测第 861 回合 156 只野兽：**140 静态（89%），运行中 Animator/SkinnedMesh 156→16**。`CreateHpCube` 血条改全局共享 Standard 材质 → 156 血条 Cube 实例化。(2) **事件日志面板**：`EventLogPanelController.AddEventLog` 原逐条 `_text.text=全量字符串 + Canvas.ForceUpdateCanvases()`，夜间每回合 156 条野兽移动日志单帧重排上百次 → CPU 主线程 **11.2ms + 1s 级尖峰**；改为 `_dirty` 标记 + `LateUpdate` 每帧批量刷新一次 → **CPU 0.05ms（降 99%）**，日志内容与滚底功能保留。**关键坑（机器人变小→隐形 bug 已修）**：`BakeMesh` 烘焙在「除以渲染器 lossyScale」的世界比例空间，LOD 网格必须 `localScale` 补偿回 1/lossyScale（0.4 缩放下 ×2.5）；**绝不能除以 state.animScale** —— 野兽 "Body" 节点是空节点、不在 Robot 变换链里，animScale(出生 0→1) 不影响 Robot.lossyScale，出生瞬间转静态会被过度补偿成极小网格而隐形。修后 LOD 与骨骼版世界包围盒一致（type11 0.79 vs 0.88，中心坐标完全重合）。另坑：野兽 prefab 内 Skeleton 幽灵件有 Animator/SkinnedMesh 在 inactive GO 上（不运行零开销，勿误删）；`GetComponentInChildren<SkinnedMeshRenderer>(false)` 才取到活跃 Robot 蒙皮。Parser/StateEngine/胜负判定/防御塔 Tracer 命中环零改动 |
| 2026-08-20 | **WebGL 场景静态合批 + 材质共享**：`SceneBuilder.StaticBatchAll` 改用 `Mesh.CombineMeshes` 手动合批（草 1615+森林 571+围栏 170=**2356 渲染器→14 合成网格**，按材质分组、60k 顶点分块、`useMatrices=true`）；`GetFixedMaterial`/`GetStandardMat`/`MakeWaterMaterial` 材质缓存去重（避免同材质每物体 new 实例破坏合批）；11 个树/草/围栏 FBX meta 开 Read/Write。启用中 MeshRenderer 2797→约 400。**关键坑**：`StaticBatchingUtility.Combine` 本环境静默无效必须手写；`CombineMeshes` 忘传 `useMatrices=true` 会把全部景物堆到地图中心（用 bounds 验证已修复）。Parser/StateEngine/胜负判定零改动 |
| 2026-08-19 | **修复基地对齐 + 调试文字格式**：基地(type=4) 的锚点 (x,y) 实为 2×2 的**左上角格**（占地 x..x+1, y-1..y），`ReplayState.UnitWorldPos` 中心偏移从 `+0.5/+0.5` 改为 `+0.5/-0.5`（原写法使建筑偏北 1 格，中心落在基地四格外）；同时移除 `UnitView.CalibrateBaseScale` 的 `_pivotOffset` Z 偏移（实测 Base.prefab Model_Red/Blue 均 X/Z 居中）。`UnitDebugOverlay` 文字改黑色、格式 `ID: 12 | Pos: (10, 24) | HP: 100 | ATK: 15`（空格分隔、坐标无小数、ATK 0 明确显示），基地显示 2×2 **左上角格坐标**。实测：红方基地(30,10)、蓝方(10,24) 的建筑中心与四格中心重合，overlay 显示 `Pos: (30, 10)` / `(10, 24)` |
| 2026-08-19 | **单位调试悬浮文字（全局「显示」开关）**：底部面板 ControlBar 新增 `Btn_ShowStats`「显示」按钮，切换 `PlaybackControlPanelController.ShowUnitStats`（默认关，点击取反 + 琥珀色高亮）；新增 `UnitDebugOverlay.cs`（`UnitView.ConfigureFromUnitPrefab` 末尾挂载，围墙 type5/野兽≥11 内部过滤不渲染），开启时非围墙/非野兽单位头顶显示 `[ID|Pos|HP|ATK]`（0.5s 节流 + hp/pos/ap 脏检查重建文本，关闭/死亡时 TextMesh 停用零渲染） |
| 2026-08-19 | **播放面板维护**：ControlBar 560→680（新增「自由」按钮后 10 个按钮共需 590px，修复「自动」溢出边框）；新增镜头按钮 `CamFree`「自由」（对应键盘 4，`WireCallbacks` 自动接线）；TeamBar/ControlBar 改用 `HorizontalLayoutGroup` 自动排布 |
| 2026-08-19 | **移除全部 UI emoji + 清理死代码/旧资产**：`PlaybackControlPanel/SettlementPanel/EventLogPanel` 的代码与 prefab 全部改纯中文文本；删除 4 个 UI Controller 的 `CreateFromCode` 纯代码兜底及无用 helper（`Create` 缺 prefab 改 `LogError` 并返回 null，ReplayEntry/ReplayPlayer 调用处补 null 保护）；删除 `Assets/Prefabs/UI/Legacy/`（`HudPanel_Legacy`、`PlaybackControlPanel_Legacy`）；HudController 清理 `panelBg`/`BG_COLOR` 死字段 |
| 2026-08-19 | **野兽特效资产源头根治（Prefab 级）**：4 个 Beast prefab + 4 个底层 Robot prefab 全部 Renderer 关阴影（`m_CastShadows`/`m_ReceiveShadows=0`）；Beast_11 底层 `Bot Robot.prefab` 的入场粒子 `FX Hex`（playOnAwake 白圈）彻底删除；删除 UnitView 运行时补救 `DisableBeastShadows()`/`DisableBeastSpawnFx()` 及其调用，`ConfigureFromBeastPrefab()` 恢复干净。验证：野兽登场 0 阴影 / 0 粒子 / 0 FX Hex，console 0 报错，防御塔阴影不受影响 |
| 2026-08-19 | **野兽"幽灵白圈"根治（动态 Ring）**：`ReplayPlayer.OnSpawn` 的 `FxFactory.Ring` 出生光环对野兽 11-14 屏蔽（`if (!u.IsBeast)`）——多回合机器人陆续登场时周期性闪现、从小到大、暂停即消失的白圈消失；工人/开拓者/建筑出生光环与防御塔 Tracer/命中环不受影响 |
| 2026-08-19 | **WebGL 性能优化（大量单位同屏）**：UnitView `SetHp/SetStun/UpdateAnimation` 值缓存自门控（`_lastHp/_lastMaxHp/_lastStun/_wasDead/_animSpeed`，仅变化时刷新 MPB/旋转/Animator.speed）；LateUpdate `isMoving==false` 跳过插值；TowerVisualController LateUpdate 空闲快速路径；静态 `s_cachedPlayer` 缓存。Parser/StateEngine/胜负判定零改动 |
| 2026-08-19 | **移除野兽 EvilAura 光环**：野兽(11-14) 无 SelRing（SelRing 仅工人 6/开拓者 7 有），脚底实际光环为 Hovl Debuff 粒子 `EvilAura`；删除 `SetupBeastAura()` 方法、调用及 `BEAST_AURA_*` 常量 |
| 2026-08-18 | **WebGL 加载安全修复 + link.xml**：`ReplayEntry.cs` 新增 `RelativeStreamingUrl()`（剥 http(s)://host → 相对路径，避免 "Insecure connection not allowed"）+ `LoadWebText()`（try/catch 兜底 demo，异常不中断初始化）；新增 `Assets/link.xml` 保留 `UnityEngine.MeshCollider` 防 IL2CPP/WebGL 裁剪 |
| 2026-08-17 | **AoE 道具特效（Cartoon FX Remaster）**：Bomb/DizzyWeapon 接入 CFXR 特效——`ReplayPlayer.OnSkillAreaEffect` 触发 → `FxFactory.PlayBombEffect/PlayDizzyEffect`（中心世界坐标）；prefab 复制到 `Resources/FX/`（`CFXR Explosion 1`、`CFXR3 Magic Aura A (Runic)`），统一 `Resources.Load("FX/...")`（废弃 AssetDatabase，Editor/WebGL 一致）；`BOMB_SCALE`/`DIZZY_SCALE`=1.8 覆盖 3×3、`Destroy(instance,duration)` 自动回收、Bomb 附加震屏。**角色使用道具徽标**：`TradeBadge.ShowUse` 让工人/开拓者 use 道具时头顶弹「使用 xx」（背景框全宽/半宽自适应） |
| 2026-08-14 | **中文字体体系 + WebGL 修复**：新增 `UiFonts`（NotoSansSC 统一入口，uGUI Text/TextMesh 共用，Dynamic）；WebGL 下 replay 用 `UnityWebRequest` 读 StreamingAssets（不用 File API）；TradeBadge 隐形根因修复（`RequestCharactersInTexture` 预热 + `MeshRenderer.sharedMaterial = font.material` 材质同步）；物品名中文映射扩展（铜/铁/石/药品/炸弹/眩晕武器/召唤令/耐久强化…） |
| 2026-08-19 | **防御塔炮塔俯仰瞄准**：`Fire()` 保留完整 3D 目标方向（`_aimWorldDir3D`），`LateUpdate` 在水平 yaw 基础上叠加绕炮塔自身 X 轴俯仰（高度差转 `asin` 俯仰角，`pitchLimit` 默认 70° 可调）；待机仍回 180°；ResetAttack/暂停冻结同步复位 |
| 2026-08-13 | **防御塔 Prefab scale 可控大小**：`TowerVisualController.Setup()` 原先 `localScale = one * visualScale` 会覆盖 Prefab 根 Transform 的 scale，导致直接改 Prefab 缩放无效；改为 `prefabScale × visualScale`，现在改 `CubeTowers/Tower_Minigun_{Faction}.prefab` 根 scale 即可控制塔大小（与角色/机器人一致） |
| 2026-08-13 | **防御塔统一 Minigun + 阵营配色特效**：`ResolveTowerType()` 固定返回 Minigun，三塔（红/蓝）统一加载 `Tower_Minigun_{Faction}`（删 `SLOT_TYPES`/slot 映射死代码）；Tracer/命中圆环/枪口灯按阵营配色（红 `#FF2D55` / 蓝 `#007AFF`），统一 Minigun 细线 0.07/0.04、0.15s；后坐力改两阶段 `EaseOutCubic`+`Smooth01`；6 个时间参数 `[SerializeField]`（aimHold/kick/recov/light/particle/ring） |
| 2026-08-13 | **防御塔视觉续作（第 4 阶段）**：6 个可编辑视觉包装 Prefab（`CubeTowers/Tower_{Type}_{Faction}`，序列化字段迁到 Inspector，Setup 不覆盖）；待机 180°；真实枪口 Tracer + 命中闪光；`Tower.prefab` 旧 Visual 停用改 `VisualRoot`；旧通用激光对 type=3 禁用；`Assets/Editor/TowerPrefabBuilder.cs` 一键重建 |
| 2026-08-13 | **Cube Tower Defense 塔接入 roleType=3**：`TowerVisualController.cs` 接管塔视觉（炮塔转向+程序化后坐力+Muzzle 粒子/闪光、slot id 升序→塔类型、暂停冻结/Seek 复位）；`UnitView.cs` 挂载塔视觉+血条按模型包围盒；`ReplayPlayer.cs` attack 事件传 targetPos + Seek 复位。不修改 Tower.prefab / 第三方源资源 / 伤害与 Replay 状态 |
| 2026-08-11 | 3D 地形完整重构：Grass_Block.prefab + 森林边界 + 围墙 + 碎草散布 |
| 2026-08-11 | 3D 血条：Cube 替代 Quad，Standard shader，三色变色，自适应高度/宽度 |
| 2026-08-11 | 矿石系统：物理 .mat 材质(Standard+Metallic)，Y-only 旋转 |
| 2026-08-11 | 废除全身染色：TeamColorApplicator 不再修改模型贴图 |
| 2026-08-11 | 单位缩放：CalibrateBaseScale 自动适配模型宽度到 1 格 |
| 2026-08-11 | WeaponShop 添加：type 10 → building_barracks_yellow.fbx，西南朝向 |
| 2026-08-11 | UnitViewSprite.cs 拆分：Sprite 扫描/颜色工具独立为静态类 |
| 2026-08-11 | **SelRing 阵营光圈修复**：根因为 `Mathf.SmoothStep` C# 插值语义 ≠ HLSL smoothstep 阶跃；颜色改为贴图像素烘焙（不依赖 shader `_Color`）；Shader 从 `Legacy Shaders/Particles/Additive` 改为 `Sprites/Default`；新增 `MatLib.Smooth01()` + `CreateRingTex()` 抗锯齿圆环生成；光圈缩小至 0.8 倍 |
| 2026-08-11 | **昼夜系统 v2**：四阶段 `LightingProfile`；Dusk 暖金/Night 浅蓝 I=0.78/Dawn 桃色；时间 0-5 Dawn→Day / 5-65 Day / 65-76 Day→Dusk / 76-80 Dusk→Night / 80-125 Night / 125-130 Night→Dawn |
| 2026-08-11 | **NPC Idle + 转向**：修复 OfficerNPC/VendorNPC T-Pose（根因 Prefab `m_Controller:{fileID:0}` 无 Controller）；赋 `Adventurer_AnimatorController`；`NpcFacingController`（切比雪夫 + 命令优先级 + Visual 节点平滑 Y 轴旋转）；`ReplayPlayer.roundActions` |
| 2026-08-12 | **Robot 野兽替换**：Beast_11~14 改用 Robot 素材包 Nested Prefab（Bot/Boxy/Tanker/Metal）；Visual → RobotAdjust 容器（独立 scale/Y/yaw）；`SetupRobotAnimator()` 通过 `AnimatorOverrideController` 映射 Skeleton 参数到 Robot clips；暂停冻结动画（`_animator.speed=0`）；血条按类型独立配置；移除运行时 AssetDatabase 依赖 |
| 2026-08-12 | **Free 相机模式**：`ReplayCameraRig` 新增 `CameraMode.Free`；左键平移/右键旋转/滚轮向鼠标位置缩放；pivot 地图边界 clamp；4 号快捷键 + UI「自由」按钮；暂停时可用 `unscaledDeltaTime` |
| 2026-08-12 | **TradeBadge 交易提示**：小贩 sell 和武器商店 buy 的世界空间徽标；`ReplayCommand.targetName` 解析贩卖/购买物品名；中文映射（copper→铜等）；背包 UI 队伍级聚合 |
| 2026-08-10 | 动画僵死 Bug 根除 + 步幅对齐 + 昼夜自愈 |
