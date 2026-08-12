# WildernessReplay 项目状态

> **用途**：供新会话的 AI 快速理解项目全貌。原则：说清是什么、在哪改，不堆细节。
> **最后更新**：2026-08-11

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
| `Scene/MatLib.cs` | 材质缓存池 + 程序化圆环贴图（Sprites/Default shader） |
| `Scene/FxFactory.cs` | 气泡/光束特效 |
| `Scene/Pickable.cs` `Scene/Billboard.cs` | 点击拾取 / 面向相机 |
| `Scene/ReplayCameraRig.cs` | 手动相机：1/2/3 快捷机位 + 35°俯角 |
| `Scene/CameraManager.cs` | 自动相机：SmoothDamp + 事件特写 |

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
├── Beast_11~14.prefab  # Skeleton_Minion/Mage/Warrior/Rogue
```

### 建筑
```
Resources/Prefabs/Buildings/
├── Base.prefab     # type 4 — 双色 (Model_Red/Blue)
├── Tower.prefab    # type 3
├── Wall.prefab     # type 5
└── WeaponShop.prefab # type 10 — building_barracks_yellow.fbx
```

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
├── Adventurer_AnimatorController.controller
└── Skeleton_AnimatorController.controller
```
参数: isMoving(Bool), onAttack(Trigger), onDeath(Trigger)。Idle↔Walk, AnyState→Attack/Death。

### 第三方素材包
```
KayKit_Adventurers_2.0_FREE/          # 角色模型
KayKit_Skeletons_1.1_FREE/            # 骷髅模型
KayKit_Forest_Nature_Pack_1.0_FREE/   # 树/灌木/草/石头 (共享 forest_texture.png)
KayKit_Medieval_Hexagon_Pack_1.0_FREE/ # 建筑/城墙 (Base/Tower 用)
Low_Poly_Forest_Pack_Devilswork.Shop_v02/ # 树/围栏 (fence24, treeTall03)
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

### 坐标系统
- `StateEngine.CellToWorld(x,y)` → `(x-20, 0, y-15.5)`
- `SceneBuilder` 用 `oz - y` 转换 Z（与 StateEngine 同向）
- 单位位置 `transform.position = (state.pos.x, 0.01f, state.pos.z)`，Y 锁死贴地

---

## 五、常见修改指南

| 想做什么 | 文件 | 复杂度 |
|---------|------|:---:|
| 调血条高度/宽度 | `UnitView.cs` UpgradeHpTo3D() 中的 `_hpY`/`_hpW` 计算 | 低 |
| 调树大小/概率 | `SceneBuilder.cs` BuildForestSkirt() 中的 treeProb/scale | 低 |
| 调矿石大小 | `ResourceViewManager.cs` GetOrCreate() 中的 scale | 低 |
| 加新单位类型 | `UnitView.UNIT_PREFABS` + Prefab | 中 |
| 改血条颜色 | `UnitView.cs` SetHp() 中的 Color 值 | 低 |
| 改围栏样式 | `SceneBuilder.cs` BuildPerimeterFence() 中的 fenceFbx 路径 | 低 |

---

## 六、🔥 已知大坑

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

---

## 七、近期改动

| 日期 | 改动 |
|------|------|
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
| 2026-08-10 | 动画僵死 Bug 根除 + 步幅对齐 + 昼夜自愈 |
