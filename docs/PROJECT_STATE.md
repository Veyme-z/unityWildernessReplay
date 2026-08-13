# WildernessReplay 项目状态

> **用途**：供新会话的 AI 快速理解项目全貌。原则：说清是什么、在哪改，不堆细节。
> **最后更新**：2026-08-13

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
| `Core/ReplayPlayer.cs` | 主控：回合推进、smoothstep 插值、事件回调 |
| `Core/ReplayEntry.cs` | 入口：`[RuntimeInitializeOnLoadMethod]` 自动启动 |

### 场景 & 表现
| 文件 | 职责 |
|------|------|
| `Scene/SceneBuilder.cs` | **3D 地形搭建**：草地网格、森林边界、围墙、水面、NPC 站位 |
| `Scene/UnitView.cs` | **单位表现核心**：Create/Configure/LateUpdate/动画/血条 |
| `Scene/UnitViewSprite.cs` | **静态工具**：Sprite 扫描、颜色计算（从 UnitView 拆出） |
| `Scene/ResourceViewManager.cs` | **矿石系统**：3D 球体 + 物理 .mat 材质 |
| `Scene/TeamColorApplicator.cs` | **阵营标识**：仅控制脚底 SelRing 颜色（已废除全身染色） |
| `Scene/DayNightController.cs` | **昼夜系统 v2**：四阶段 `LightingProfile` (Day/Dusk/Night/Dawn)，从 `ReplayPlayer.RoundFloat` 连续回合 → `Mathf.Repeat` → 阶段判定 → `LightingProfile.Lerp` 插值 |
| `Scene/NpcFacingController.cs` | **NPC 转向**：切比雪夫距离来访者检测 + 命令优先级 (executeTask/submitAnswer/sell) + Smooth01 八方向水平旋转；与 FBX/骨骼解耦 |
| `Scene/TowerVisualController.cs` | **防御塔视觉**（type=3）：炮塔转向 attack 目标 + 两阶段程序化后坐力 + Muzzle 粒子/闪光 + 阵营配色 Tracer/命中圆环；统一 Minigun 塔模型；暂停冻结/Seek 复位 |
| `Scene/MatLib.cs` | 材质缓存池 + 程序化圆环贴图（Sprites/Default shader） |
| `Scene/FxFactory.cs` | 气泡/光束特效 |
| `Scene/Pickable.cs` `Scene/Billboard.cs` | 点击拾取 / 面向相机 |
| `Scene/ReplayCameraRig.cs` | 相机系统：1/2/3/4 快捷机位 (Global/TeamA/TeamB/Free)；Free 模式左键平移+右键旋转+滚轮锚点缩放 |
| `Scene/CameraManager.cs` | 自动导播：SmoothDamp + 事件特写 + 震屏 |
| `FX/TradeBadge.cs` | 交易提示徽标：World Space Billboard + 弹出淡出；Vendor/Shop 独立参数 |

### UI
`HudController.cs` `EventLogPanelController.cs` `PlaybackControlPanelController.cs` `SettlementPanelController.cs`

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
├── Beast_13.prefab  # Tanker Robot (大型)
├── Beast_14.prefab  # Metal Robot (BOSS)
```
层级：Beast_XX → Visual → RobotAdjust (scale/Y/yaw) → Robot (Nested Prefab)
原 Skeleton 节点保留但 disable。动画通过 `AnimatorOverrideController` 将 Skeleton_AnimatorController 参数映射到 Robot clips。

### 建筑
```
Resources/Prefabs/Buildings/
├── Base.prefab     # type 4 — 双色 (Model_Red/Blue)
├── Tower.prefab    # type 3 — 外层逻辑 prefab（UnitView/碰撞/血条/阵营），内部 Visual 运行时被替换
├── Wall.prefab     # type 5
└── WeaponShop.prefab # type 10 — building_barracks_yellow.fbx
```

### 防御塔（Cube Tower Defense，已转 Built-in）
- **源素材**（URP 专用，勿改）：`Assets/CubeTowerDefense/`
- **已转换 prefab**（源塔）：`Assets/ProjectAssets/CubeTowerDefense_BuiltIn/Resources/Prefabs/Towers/`
  ```
  Tower_Flamethrower_Red/Blue.prefab
  Tower_Minigun_Red/Blue.prefab
  Tower_RPG_Red/Blue.prefab
  ```
  材质在 `.../Materials/`（Standard）、粒子在 `.../Effects/`（Particles/Standard Unlit）。
- **视觉包装 Prefab**（运行时真正加载、可编辑）：`Resources/Prefabs/Buildings/CubeTowers/Tower_{Type}_{Faction}.prefab`（6 个），嵌套引用上述源塔（不复制 FBX/贴图），根上挂 `TowerVisualController`。**运行时统一加载 `Tower_Minigun_{Faction}`**（红方 `Tower_Minigun_Red` / 蓝方 `Tower_Minigun_Blue`），Flamethrower/RPG 包装 Prefab 保留但不再加载。旧塔备份在 `Legacy/Tower_Legacy.prefab`。
- **节点结构**：根 → `BasePillar`(静态底座) + `Minigun`(可旋转炮塔节点)；正前方 = 局部 +Z。Minigun 有 `Muzzle` 节点（内含 8 个 `Particle System` + `Shooting` 粒子），但该节点默认 **禁用**（见「已知大坑」Minigun Muzzle 节点默认禁用）。

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

### 血条系统 (UnitView)
- **3D Cube**：`Resources.GetBuiltinResource<Mesh>("Cube.fbx")`，Standard shader
- **无底槽**：HpBar 已删除，只剩 HpFill
- **自适应大小**：`_hpW` = 模型宽度，Base/Tower ×1.6，高度按类型分档
- **三色变色**：MaterialPropertyBlock `_Color`：#44EC6F(>60%) / #FFC94D(30-60%) / #FF3B30(<30%)
- **自动补建**：UpgradeHpTo3D() 若 Prefab 无 HpFill 则创建，若 Default-Material 则替换为 Standard

### 动画系统
- **步幅对齐**：`_animator.speed = Clamp(realSpeed * strideCoefficient, 0.15, 4.5) * AnimatorSpeed`
- **applyRootMotion = false**：代码完全控制 transform
- **loopTime**：已通过 SerializedObject 物理持久化（52 clips），Play 不回弹
- **canTransitionToSelf**：全部置 false（12 transitions），杜绝高频重置

### 阵营区分
- **TeamColorApplicator**：已废除全身 MPB 染色，不再修改角色贴图颜色
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

---

## 六、常见修改指南

| 想做什么 | 文件 | 复杂度 |
|---------|------|:---:|
| 调血条高度/宽度 | `UnitView.cs` UpgradeHpTo3D() 中的 `_hpY`/`_hpW` 计算（野兽按 type 11-14 独立配置） | 低 |
| 调野兽模型大小/高度 | Beast Prefab 中 `Visual/RobotAdjust` 的 localScale / localPosition.y / localRotation.y | 低 |
| 换野兽 Robot | Beast Prefab 中删除旧 Robot 子节点 → 拖入新 Robot Prefab 到 RobotAdjust 下 | 低 |
| 调树大小/概率 | `SceneBuilder.cs` BuildForestSkirt() 中的 treeProb/scale | 低 |
| 调矿石大小 | `ResourceViewManager.cs` GetOrCreate() 中的 scale | 低 |
| 加新单位类型 | `UnitView.UNIT_PREFABS` + Prefab | 中 |
| 改血条颜色 | `UnitView.cs` SetHp() 中的 Color 值 | 低 |
| 改围栏样式 | `SceneBuilder.cs` BuildPerimeterFence() 中的 fenceFbx 路径 | 低 |
| 调塔尺寸/朝向/后坐力/时间参数 | 打开 `CubeTowers/Tower_Minigun_{Faction}.prefab` 的 `TowerVisualController` Inspector 字段 | 低 |
| 切换塔模型（当前统一 Minigun） | `TowerVisualController.cs` 的 `ResolveTowerType()` / `TURRET_NODES` | 低 |
| 换塔模型素材 | 生成 ProjectAssets 源塔 + 重跑 `Tools/WildernessReplay/Build Tower Visual Prefabs`（见第五节） | 中 |

---

## 七、🔥 已知大坑

| 坑 | 说明 |
|----|------|
| **KayKit FBX scale=100** | 所有 KayKit FBX 根节点 scale=(100,100,100)，rotation=(270,0,0)。实例化后不能覆盖 localScale，要用容器包裹。Devilswork 无此问题(scale=1) |
| **FBX Prefab 序列化失败** | `execute_code` + `SaveAsPrefabAsset` 无法正确保存 FBX 子节点 mesh。简单模型用 Primitive (Cube/Sphere)，复杂模型通过 AssetDatabase 直接 Instantiate |
| **AssetDatabase 仅 Editor** | `LoadAsset<T>` 有 `#if UNITY_EDITOR` + Resources.Load 双回退。Build 需要 Resources/Prefabs/Environment/Forest/ 下的包装 Prefab |
| **loopTime 持久化** | 必须 SerializedObject → m_ClipAnimations → ApplyModifiedPropertiesWithoutUndo → SaveAndReimport |
| **Sprites/Default vs Standard** | MatLib 用 Sprites/Default（2D），血条必须用 Standard（3D+MPB 变色） |
| **红蓝阵营色反了** | defender 显红色模型，challenger 显蓝色模型 |
| **Mathf.SmoothStep ≠ HLSL smoothstep** | C# `Mathf.SmoothStep(from,to,t)` 是插值函数（以 t 为 0~1 因子在 from/to 间插值），不是 HLSL `smoothstep(edge0,edge1,x)` 的 0~1 阶跃。圆环遮罩和昼夜 Blend 必须用自定义 `Smooth01`（基于 `Clamp01` + Hermite 曲线），见 `MatLib.Smooth01()` 和 `DayNightController.Smooth01()` |
| **昼夜 130 回合/天** | `StateEngine.DayOf(n)` / `IsNight(n)` 硬编码 130 回合周期（80 白天 + 50 夜晚）。`DayNightController` 通过 `ReplayPlayer.RoundFloat`（连续浮点值）计算 `cyclePosition = Mathf.Repeat(roundFloat, 130f)`，黄昏 72-80、黎明 122-130 |
| **NPC T-Pose：Animator Controller 缺失** | OfficerNPC/VendorNPC Prefab 的 Animator 虽有有效 Avatar，但 `m_Controller: {fileID: 0}`。Humanoid 模型 + 无 Controller = bind pose（双手张开）。赋 Adventurer_AnimatorController 即可复用 KayKit Idle_A。SCENE BUILDER 静态 NPC 不会走 UnitView.ConfigureFromUnitPrefab，必须在 BuildNeutralNpc 中单独添加组件 |
| **Robot Controller 零参数** | 所有 Robot 素材包的 `.controller` 都是 `m_AnimatorParameters: []`，纯 ExitTime 链式过渡，外部无法控制。必须用 `AnimatorOverrideController(Skeleton_AnimatorController)` 替换，按名称模糊匹配 Idle/Walk/Attack/Death clip。Boxy/Tanker 缺少 Die 状态，Metal Robot 最完整 | 
| **Robot Prefab 不在 Resources 下** | `Resources.Load` 无法加载。必须通过 Nested Prefab 引用（拖入 Beast Prefab 内部）或 PrefabRefs 序列化字段。不要用 `AssetDatabase.LoadAssetAtPath`（仅 Editor 可用，Build 失效） |
| **Minigun 源塔 Muzzle 节点默认禁用** | Cube Tower Defense 源 prefab 里 Minigun 的 `Muzzle` 节点 `activeSelf=false`（原游戏脚本负责开火时激活）。若直接 `Play()` 粒子，`isPlaying` 永远 false。`TowerVisualController.Setup()` 里已先设 `playOnAwake=false` 再 `_muzzlePoint.SetActive(true)` |
| **ParticleSystemRenderer 撑大包围盒** | 粒子拖尾/射击流会把 `GetComponentsInChildren<Renderer>().bounds` 撑到 9+ 单位，导致血条过宽过高。测模型尺寸必须跳过 `ParticleSystemRenderer`（`TowerVisualController.MeasureSize()` 已处理） |
| **MainModule 是结构体** | `var m = ps.main; m.playOnAwake = false;` 这种写法有效（MainModule 属性 setter 直写原生对象），但不要对 `ps.main` 整体赋值 |

---

## 八、近期改动

| 日期 | 改动 |
|------|------|
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
| 2026-08-12 | **Free 相机模式**：`ReplayCameraRig` 新增 `CameraMode.Free`；左键平移/右键旋转/滚轮向鼠标位置缩放；pivot 地图边界 clamp；4 号快捷键 + UI 🆓 按钮；暂停时可用 `unscaledDeltaTime` |
| 2026-08-12 | **TradeBadge 交易提示**：小贩 sell 和武器商店 buy 的世界空间徽标；`ReplayCommand.targetName` 解析贩卖/购买物品名；中文映射（copper→铜等）；背包 UI 队伍级聚合 |
| 2026-08-10 | 动画僵死 Bug 根除 + 步幅对齐 + 昼夜自愈 |
