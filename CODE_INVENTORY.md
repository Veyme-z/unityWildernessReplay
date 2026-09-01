# CODE_INVENTORY — WildernessReplay 可维护性盘点

> 只读盘点产物，2026-08-13 生成，**2026-08-19 已随「删除 CreateFromCode 兜底 + 移除 UI emoji + 删 Legacy 旧资产」清理同步更新**（§1 行数、§3.6、§4 假设5、§5、§6、§7）；**2026-08-20 已随「UnitView.cs 拆 Partial Class」同步更新**（§1 行数、§3.4/§3.7/§3.8、§4 假设8/10、§6、§7）。
> 范围：`Assets/Scripts/**`（32 个）+ `Assets/Editor/TowerPrefabBuilder.cs`（1 个）= **33 个项目脚本**。
> 第三方（KayKit / Low_Poly_Forest / Robots / CubeTowerDefense / Raygeas Shared Assets / ProjectAssets）不审计。

---

## 1. 文件清单

行数为实际文件行数（`wc -l`）。"被谁引用/引用谁"只列项目内直接、可编译的引用关系，不列反射/字符串查找。

| 路径 | 类型 | 行数 | 一句话职责 | 被谁引用 | 引用谁 | 风险标签 |
|---|---|---|---|---|---|---|
| Scripts/ReplayEntry.cs | MonoBehaviour | 294 | 入口：加载 replay→组装相机/灯光/场景/播放器/全部 UI | 无（RuntimeInitializeOnLoadMethod 自举） | ReplayParser、SceneBuilder、ReplayPlayer、CameraManager、DayNightController、4 个 UI Controller | 双轨（自动创建 vs 场景挂载） |
| Scripts/Core/ReplayPlayer.cs | MonoBehaviour | 605 | 主控：回合推进/变速/Seek/插值/特效调度/结算/交易徽标 | ReplayEntry、4 个 UI Controller、UnitView/NpcFacing/TowerVisual（FindObjectOfType） | StateEngine、ReplayCameraRig、ResourceViewManager、EventLogPanelController、FxFactory、TradeBadge、SettlementPanelController、CameraManager | GodFile、坐标（硬编码徽标换算）、依赖 UI（结算面板） |
| Scripts/Core/ReplayState.cs | static+class | 463 | StateEngine：Diff/出生死亡伤害推断/CellToWorld + UnitState/TeamStat/IReplayHost | ReplayPlayer、ResourceViewManager、CameraManager、HudController、NpcFacingController | ReplayModels | GodFile、坐标 |
| Scripts/Core/ReplayParser.cs | static | 262 | JSONL 解析（容错） | ReplayEntry | MiniJson、ReplayModels | — |
| Scripts/Core/ReplayModels.cs | class(纯数据) | 131 | replay 数据模型 | ReplayParser、ReplayState、ReplayPlayer | — | 疑似死代码（多字段未消费） |
| Scripts/Core/PrefabRefs.cs | MonoBehaviour | 155 | Prefab 引用单例（Inspector→Resources 双轨） | UI Controller 的 Create | Resources | 双轨、疑似死代码（GetUnitPrefab/Has* 未用） |
| Scripts/Core/MiniJson.cs | static | 172 | 零依赖 JSON 解析器 | ReplayParser | — | — |
| Scripts/Scene/UnitView.cs | MonoBehaviour（partial 主文件） | 341 | 单位表现核心：字段/Create/Configure*/LateUpdate 调度/SetHp/SetStun/CalibrateBaseScale | ReplayPlayer、TeamColorApplicator、NpcFacingController | TowerVisualController、MatLib、Pickable、TeamColorApplicator、NpcFacingController、UnitDebugOverlay（挂载）+ 4 个 partial | 序列化脆弱（Find 按名）、重复实现（EstimateHeight/Width vs MeasureSize） |
| Scripts/Scene/UnitView.Anim.cs | MonoBehaviour（partial） | 172 | 动画子模块：SetupRobotAnimator/Worker 覆盖装配、UpdateAnimationState 每帧同步、攻击/采集/死亡触发、AnimatorSpeed 倍速 | UnitView.cs（partial） | ReplayPlayer（AnimatorSpeed 静态字段） | — |
| Scripts/Scene/UnitView.Hp.cs | MonoBehaviour（partial） | 172 | 血条子模块：UpgradeHpTo3D/EnsureRing/ApplyRingColor/GetSharedHpFillMat/CreateHpCube/EstimateHeight/Width、全局共享材质 | UnitView.cs（partial） | MatLib、TowerVisualController（VisualHeight/Width） | 重复实现（EstimateHeight/Width vs TowerVisualController.MeasureSize） |
| Scripts/Scene/UnitView.Lod.cs | MonoBehaviour（partial） | 119 | 野兽距离 LOD 子模块：UpdateLod/SetLodStatic、LOD_RANGE 等 public static 调参、烘焙网格缓存 s_lodMeshCache | UnitView.cs（partial） | — | — |
| Scripts/Scene/UnitView.Tower.cs | MonoBehaviour（partial） | 58 | 防御塔视觉子模块：SetupTowerVisual 包装实例化、TriggerTowerAttack/ResetTowerAttack | UnitView.cs（partial） | TowerVisualController | — |
| Scripts/Scene/SceneBuilder.cs | static | 490 | 地形：草地/森林/围栏/水面/NPC站位 | ReplayEntry | MatLib、UnitViewSprite、Resources | GodFile、EditorOnly混入Runtime、坐标 |
| Scripts/Scene/TowerVisualController.cs | MonoBehaviour | 563 | 防御塔视觉：炮塔转向/后坐力/粒子/Tracer/命中环 | UnitView | MatLib、ReplayPlayer | 重复实现（Smooth01）、疑似死代码（Flamethrower/RPG 未加载） |
| Scripts/Scene/CameraManager.cs | MonoBehaviour | 483 | 自动导播：SmoothDamp+事件特写+震屏+景深(反射) | ReplayEntry、PlaybackControlPanelController、ReplayPlayer、ReplayCameraRig | StateEngine、ReplayCameraRig | 疑似死代码（景深反射可能无包失效） |
| Scripts/Scene/ReplayCameraRig.cs | MonoBehaviour | 412 | 相机机位：Global/TeamA/TeamB/Free | ReplayEntry、PlaybackControlPanelController | CameraManager | 坐标（硬编码 CellToWorld）、疑似死代码（Focus 空、globalPositionOverride 覆盖 FitMap） |
| Scripts/Scene/DayNightController.cs | MonoBehaviour | 191 | 昼夜四阶段光照 | ReplayEntry | ReplayPlayer、Light、Camera | 昼夜、重复实现（Smooth01） |
| Scripts/Scene/ResourceViewManager.cs | class | 167 | 矿石可视化：FBX 直载+材质+数量标签 | ReplayPlayer | SceneBuilder.OreRockModel、StateEngine、MatLib、FxFactory | — |
| Scripts/Scene/MatLib.cs | static | 166 | 材质池+程序化贴图（圆环/圆角面板） | UnitView、SceneBuilder、FxFactory、TowerVisualController、TeamColorApplicator、UnitViewSprite | — | 重复实现（Smooth01 3参版） |
| Scripts/Scene/NpcFacingController.cs | MonoBehaviour | 126 | NPC 转向（距离检测来访者） | UnitView、SceneBuilder | ReplayPlayer、StateEngine | 坐标（硬编码 20/15.5） |
| Scripts/Scene/UnitViewSprite.cs | static | 124 | Sprite 扫描/颜色工具 | SceneBuilder（仅 fallback） | MatLib | 疑似死代码（TryGetSprite 未用） |
| Scripts/Scene/TeamColorApplicator.cs | MonoBehaviour | 44 | 阵营色（现只染 SelRing，与 UnitView.ApplyRingColor 重复） | UnitView | MatLib | 重复实现、疑似死代码 |
| Scripts/Scene/Pickable.cs | MonoBehaviour | 7 | 拾取标记 + view 字段 | UnitView（仅赋值） | — | 疑似死代码（view 从未被读） |
| Scripts/Scene/Billboard.cs | MonoBehaviour | 12 | 面朝相机 | FxFactory、ResourceViewManager、TradeBadge、SceneBuilder | — | — |
| Scripts/FX/FxFactory.cs | static+4个MB | 269 | 光束/光环/气泡/伤害数字 | ReplayPlayer、TradeBadge、ResourceViewManager | MatLib、Billboard | — |
| Scripts/FX/TradeBadge.cs | MonoBehaviour | 224 | 交易徽标（头顶弹字） | ReplayPlayer | FxFactory、Billboard | — |
| Scripts/UI/HudController.cs | MonoBehaviour | 87 | 顶部天数/昼夜/回合面板 | ReplayEntry | PrefabRefs、StateEngine | 昼夜 |
| Scripts/UI/PlaybackControlPanelController.cs | MonoBehaviour | 285 | 底部双队+时间轴+控制按钮（prefab 驱动；AddDirectorUI 动态加手动/自动按钮，WireCallbacks 按名接线；静态 ShowUnitStats 全局调试开关） | ReplayEntry、UnitDebugOverlay（读静态开关） | PrefabRefs、ReplayPlayer、CameraManager、ReplayCameraRig | 依赖 prefab（缺 prefab 时 Create 报错返回 null） |
| Scripts/UI/EventLogPanelController.cs | MonoBehaviour | 75 | 左侧事件日志 | ReplayEntry、ReplayPlayer | PrefabRefs | 依赖 prefab |
| Scripts/UI/SettlementPanelController.cs | MonoBehaviour | 72 | 结算画面 | ReplayPlayer | PrefabRefs | 依赖 prefab |
| Scripts/UI/TaskPanelController.cs | MonoBehaviour | ~90 | 推理类/长上下文任务面板（prefab 驱动，实时读 round.news 世界新闻，向前扫描最近一条） | ReplayEntry | PrefabRefs、ReplayPlayer | 依赖 prefab |
| Scripts/UI/UnitDebugOverlay.cs | MonoBehaviour | 139 | 单位头顶调试悬浮文字（[ID\|坐标\|HP\|攻击力]；围墙/野兽内部过滤；受 PlaybackControlPanelController.ShowUnitStats 全局开关） | UnitView（ConfigureFromUnitPrefab 挂载） | UiFonts、Billboard、PlaybackControlPanelController（静态开关） | 依赖全局静态开关（默认 false，未显示时零渲染） |
| Editor/TowerPrefabBuilder.cs | static | 124 | 编辑器工具：生成 6 个塔视觉包装 Prefab | 菜单 Tools/WildernessReplay | AssetDatabase、TowerVisualController | EditorOnly（正常） |
| Editor/TaskPanelPrefabBuilder.cs | static | ~160 | 编辑器工具：生成推理类/长上下文任务面板 Prefab + 接线场景 PrefabRefs | 菜单 Tools/WildernessReplay | AssetDatabase、TaskPanelController、PrefabRefs | EditorOnly（正常） |

第三方但位于 Assets 下、**不审计**：`Assets/Raygeas/Shared Assets/Scripts/*`（6 个，环境素材包自带的 PlayerController/Interactive 等）。

---

## 2. 运行时对象图

启动场景 `Assets/unknow.unity`（EditorBuildSettings 为空，跑当前打开的场景）。场景内已有 `PrefabRefs` 组件（4 个 UI GUID + unitBasePrefab GUID 已序列化，见 §5）。

### 启动 → 第一帧

```
[RuntimeInitializeOnLoadMethod(AfterSceneLoad)] ReplayEntry.AutoBoot
  └─ 场景已有 ReplayEntry 则跳过；否则 new GameObject + AddComponent<ReplayEntry>
ReplayEntry.Start → EnsureInScene(EventSystem) → StartCoroutine(Load)
Load():
  1. 选 replay 文本（debugReplay → persistentDataPath/replay.jsonl → StreamingAssets/replay.txt → demo_replay.jsonl）
  2. ReplayParser.Parse(text) → ReplayData
  3. 相机：复用/新建 MainCamera + ReplayCameraRig（Awake 记录位姿）
  4. new CameraManager（Awake 单例，Start 里 InitDepthOfField 反射）
  5. new Sun（方向光）
  6. SceneBuilder.Build(map) → Map 根 + GrassGrid + ForestSkirt + PerimeterFence + 水面/NPC 站位
  7. new ReplayPlayer → Setup(data, rig)：建 Units 根、ResourceViewManager、camRig.Focus/FitMap、engine.Init + 预载第1回合
  8. camMgr.Init(player)
  9. new DayNightController（Awake 单例，Start 找 Sun/Camera.main）
  10. HudController.Create / EventLogPanelController.Create / PlaybackControlPanelController.Create / TaskPanelController.Create(Reasoning) / TaskPanelController.Create(LongContext)
  11. 相机固定初始视角 → player.SetPlaying(true)
```

### 每帧 Tick（按执行顺序）

- `ReplayPlayer.Update`：推进回合（_acc）、单位插值（smoothstep `t*t*(3-2t)`）、死亡清理、RefreshResources。
- `UnitView.LateUpdate`：位置/缩放/转身/动画参数/暂停冻结。
- `TowerVisualController.LateUpdate`：炮塔转向/后坐力/粒子/闪光/Tracer/命中环；Seek 跳变检测。
- `NpcFacingController.LateUpdate`：来访者检测转向。
- `DayNightController.LateUpdate`：RoundFloat → cyclePos → LightingProfile。
- `CameraManager.LateUpdate`（仅 Auto）：SmoothDamp 三通道 + 震屏 + 景深。
- `ReplayCameraRig.LateUpdate`（非 Auto 时）：机位平滑。
- `HudController.Update` / `PlaybackControlPanelController.Update`（每帧 Sync）/ `TaskPanelController.Update`（回合变化时读 round.news）：UI 刷新。
- `Billboard.LateUpdate` / 各 FX 的 `Update`。

### Seek / Pause / Restart 三条 Reset 路径

**Seek**（`ReplayPlayer.Step(delta, withFx=false)`，由时间轴拖动 `JumpTo` 触发）：
- `TradeBadge.Cleanup()`（销毁 NPC_9_20_15 / NPC_10_25_11 下徽标）
- 清除 `roundActions`；`engine.Diff` 逐回合应用
- 所有存活单位 `u.pos=targetPos; moving=false`，销毁 dying/dead 单位视图
- 对 `type==3` 调 `u.view.ResetTowerAttack()` → `TowerVisualController.ResetAttack()`（清转向/后坐力/粒子/闪光/Tracer/命中环，复位 180°）
- 补建缺失视图；`CheckBaseDestroyed()`；`RefreshResources()`
- 另有 `TowerVisualController.LateUpdate` 的 `|cur-lastRound|>1` 二次检测兜底

**Pause**（`SetPlaying(false)`）：
- `ReplayPlayer.Update` 停止推进
- `UnitView.LateUpdate`：`_animator.speed=0`
- `TowerVisualController.LateUpdate`：`FreezeParticles(true)` + 早退
- `NpcFacingController.LateUpdate`：`_player.playing` 为假 → 不旋转
- ⚠️ `CameraManager.UpdateShake` 用 `Time.unscaledDeltaTime` → **震屏在暂停时仍衰减**（见 §4 假设4）

**Restart**（`ReplayPlayer.Restart()`，结算面板回调）：
- `SetPlaying(false)` + `TradeBadge.Cleanup()`
- 销毁全部单位视图，`engine.units.Clear()`，`_resourceView.Clear()`
- `engine.Init` + 预载第1回合 + 重建视图 + `RefreshResources()` + `SetPlaying(true)`
- ⚠️ 未重建 `_settlementOverlay` 引用（由结算面板的 onRestart 回调 `Destroy(_settlementOverlay)` 处理）；未重设 `cur` 之外的 `roundActions` 已 Clear

---

## 3. 重复与冗余

### 3.1 Smooth01 / SmoothStep / Hermite

- **事实**：smoothstep 曲线 `t*t*(3f-2f*t)` 共 **4 处**，两种签名。
- **证据**：
  - `MatLib.Smooth01(float edge0, float edge1, float value)`（3 参，HLSL 阶跃语义）`MatLib.cs:130`
  - `DayNightController.Smooth01(float t)`（1 参）`DayNightController.cs:166`
  - `TowerVisualController.Smooth01(float t)`（1 参）`TowerVisualController.cs:402`
  - `ReplayPlayer.Update` 内联 `float e = t*t*(3f-2f*t)` `ReplayPlayer.cs:407`
  - 另有 `TowerVisualController.EaseOutCubic`（1 处，`TowerVisualController.cs:403`）
- **建议**：合并为单一静态工具（放 MatLib 或新建 `Easing`）。低优先级，纯样式一致性，不影响正确性。

### 3.2 格子↔世界坐标转换（多处硬编码 20/15.5）

- **事实**：同一变换 `(x-20, 0, y-15.5)`（针对 41×32 地图）出现在 **5 处**，其中 3 处硬编码 `20f`/`15.5f`。
- **证据**：
  - `StateEngine.CellToWorld`（唯一按 mapW/mapH 推导）`ReplayState.cs:91`
  - `SceneBuilder.Build` 内联 `ox/oz`（与 StateEngine 同向）`SceneBuilder.cs:37,61,112`
  - `ReplayCameraRig.CellToWorld` **硬编码** `gameX-20, gameY-15.5` `ReplayCameraRig.cs:405`
  - `ReplayPlayer.TryShowTradeBadge/TryShowShopBadge` **硬编码** `+20f / 15.5f-z` `ReplayPlayer.cs:336-339, 352-355`
  - `NpcFacingController.RefreshTarget` **硬编码** `+20f / 15.5f-z` `NpcFacingController.cs:90-91,101-102`
- **建议**：保留 StateEngine.CellToWorld 为唯一来源，其余改调 `engine.CellToWorld`（NpcFacingController/ReplayPlayer 已有 engine 引用，ReplayCameraRig 无 engine 需注入）。当前地图固定 41×32 时硬编码是**正确的**，属低价值 churn，可推迟。

### 3.3 130/80/50 魔法数（昼夜周期）

- **事实**：130=80 昼+50 夜 的周期数字散落多处，无常量集中。
- **证据**：
  - `StateEngine.DayOf`/`IsNight`（130、80）`ReplayState.cs:435-436`
  - `HudController.Update`（130、80、50）`HudController.cs:149-151`
  - `DayNightController.ResolveProfile`（130 周期 + 阶段切点 5/65/76/80/125）`DayNightController.cs:132,152-163`
- **建议**：抽 `DayNightConst` 常量类。低优先级（已有两处 `DayOf/IsNight` 作为权威入口，但 Hud/TaskPanel 各自重算）。

### 3.4 红蓝阵营色常量（两套色系 + 一处反了）

- **事实**：阵营色至少 **6 处**定义，分「霓虹」与「柔和」两套，且 `ReplayPlayer.TeamTag` 的 challenger/defender 映射**与其余全部相反**。
- **证据（霓虹系，defender=红 / challenger=蓝）**：
  - `UnitView.ApplyRingColor`：defender `(1,0.176,0.333)` / challenger `(0,0.478,1)` `UnitView.Hp.cs:124`
  - `TeamColorApplicator.ApplyTeamColor`：同 RGB，α=0.8 `TeamColorApplicator.cs:20-22`
  - `TowerVisualController.FactionColor`：Blue `(0,0.478,1)` / 红 `(1,0.176,0.333)` `TowerVisualController.cs:265`
  - `PlaybackControlPanelController.Sync`：`colRed (1,0.176,0.333)` / `colBlue (0,0.478,1)` `PlaybackControlPanelController.cs:309-310`
- **证据（柔和系，颜色不同）**：
  - `ReplayPlayer.TeamTag`：`challenger→#F05638 红 / 其他→#479EF0 蓝` **映射曾反了（已修 2026-08-13：改 defender→红 / challenger→蓝）** `ReplayPlayer.cs:200`
  - `SettlementPanelController`：红 `(0.94,0.34,0.28)` / 蓝 `(0.28,0.62,0.96)` `SettlementPanelController.cs:84,86`
- **建议**：抽 `FactionColor.Red/Blue` 常量（霓虹系为主），修 `TeamTag` 映射（见 §4 假设，P1）。

### 3.5 物品英文名→中文 映射是否多份

- **事实**：英文→中文 物品名映射**只有一处**（TradeBadge.CnName），不重复；但数据里两种命名约定并存。
- **证据**：
  - `TradeBadge.CnName`：copper→铜 / iron→铁 / stone→石 `TradeBadge.cs:15-24`（唯一英文→中文）
  - `ResourceViewManager.ORE_MAT`：中文「石头/铁/铜」→ 材质路径 `ResourceViewManager.cs:10-15`（不同用途：中文→资源）
  - 实测 replay.txt：`resName` 为**中文**（石头/铁/铜），`targetName` 为**英文**（copper）→ 两条映射各自正确，但命名约定不统一。
- **建议**：保留现状，仅建议在 ReplayParser 注释中明确「资源名=中文、物品名=英文」，避免未来新增物品时踩坑。

### 3.6 CreateFromCode 与 Prefab 路径内容分叉 —— 已消除（2026-08-19）

- **事实**：原 4 个 UI Controller（Hud/EventLog/Playback/Settlement）都有 `Create`（Prefab 优先）+ `CreateFromCode` 纯代码兜底双轨。**2026-08-19 已全部删除兜底**：`Create()` 缺 prefab 直接 `Debug.LogError` 并返回 null（ReplayEntry/ReplayPlayer 调用处已补 null 保护），不再有纯代码 UI 路径。
- **现状**：全部 6 个 UI 面板（Hud/EventLog/Playback/Settlement + 推理类/长上下文任务面板）纯 prefab 驱动；`TaskPanelController` 已由纯代码改为 prefab 驱动（实时读 `round.news`，见 3.3 移除的 130 魔法数）。
- **建议**：后续新增面板统一 prefab 驱动（PrefabRefs 序列化引用 + `UiFonts.Apply` + 按名接线）。

### 3.7 TeamColorApplicator 现在还改不影响 SelRing 的东西吗

- **事实**：**否**。现在只读写 `SelRing`（`unitView.transform.Find("SelRing")`），不碰身体/模型颜色；且与 `UnitView.ApplyRingColor` **功能重复**（两者都烘焙彩色圆环贴图到 SelRing，仅 α 0.8 vs 1.0、是否复用 Material 不同）。
- **证据**：`TeamColorApplicator.cs:14-43` 全逻辑仅 SelRing；`UnitView.ApplyRingColor` `UnitView.Hp.cs:124`；调用点 `UnitView.ConfigureFromUnitPrefab` `UnitView.cs:161`。
- **建议**：可删（UnitView 已覆盖 SelRing 染色）；若保留，需确认 Worker/Pioneer prefab 上是否还挂着该组件（删组件前用编辑器核实）。

### 3.8 未引用方法 / 未用字段 / 未加载 Prefab

**未引用的 public/internal 方法与字段（证据=搜索命中仅定义处）**：
- `ReplayEntry.Ensure()` `ReplayEntry.cs:192` — 未调用（自动启动走 AutoBoot）。
- `ReplayPlayer.Toast()` `ReplayPlayer.cs:209` — 空实现，接口成员未调用。
- `ReplayPlayer.OnSkillAreaEffect()` `ReplayPlayer.cs:310` — 空桩。
- `ReplayPlayer.roundActions` `ReplayPlayer.cs:31` — 只写不读（注释称"NpcFacingController 查询"，但 NpcFacingController 实际未读）。
- `PrefabRefs.GetUnitPrefab/HasUnitPrefab/HasHudPrefab/HasEventLogPrefab/HasPlaybackControlPrefab/HasSettlementPrefab` `PrefabRefs.cs:99-111,126-130` — `Has*` 全未用；`GetUnitPrefab` 未用（UnitView 用自带 UNIT_PREFABS 直载）。
- `PrefabRefs.unitBasePrefab` — 场景里已序列化 GUID（§5）但从不被 `GetUnitPrefab` 消费。
- `TeamStat.baseHp` `ReplayState.cs:48` — 写不读（HUD_UI_AUDIT §3 已确认）。
- `TeamStat.taskText` `ReplayState.cs:47,298` — 拼装但无 UI 消费。
- `UnitViewSprite.TryGetSprite()` `UnitViewSprite.cs:79` — 未调用（仅 `FindSprite` 在 SceneBuilder fallback 被用）。
- `Pickable.view` `Pickable.cs:6` — 赋值不读（`UnitView.cs:150,198`）。
- `ReplayPlayerResult.goldNum/diamondNum`（finish）`ReplayModels.cs:128-129` — 解析但结算只读 `result/totalScore`。
- `ReplayTeam.invalidTaskCount` `ReplayModels.cs:44`、`ReplayTask.level/roundCost` `ReplayModels.cs:94,96` — 解析未用。

**未加载的 Prefab / 资源（搜索命中仅定义/无命中）**：
- `Resources/Prefabs/Environment/Trees/*`、`Bushes/*` — 代码无任何 `Resources.Load` 引用（PROJECT_STATE 也标注「旧森林池未使用」）。
- `Resources/Prefabs/Buildings/CubeTowers/Tower_Flamethrower_*`、`Tower_RPG_*` — `ResolveTowerType` 恒返回 `Minigun`，运行时只加载 `Tower_Minigun_{Faction}`（`UnitView.Tower.cs:30`）。
- `Resources/Prefabs/Buildings/Legacy/Tower_Legacy.prefab` — 仅 TowerPrefabBuilder 备份产物。
- `Resources/UITheme/panel_frame.png` — 无引用（HUD_UI_AUDIT §5 已确认）；MatLib.panelTex 为程序化生成。
- `Resources/Prefabs/Units/UnitBase.prefab`（对应 `Assets/Prefabs/Units/UnitBase.prefab`）— 仅 PrefabRefs 字段引用，未加载。

---

## 4. 隐藏 bug 假设

> 只列「读代码能成立」的项。**P0 = 0**（无数据丢失/崩溃/不可恢复项）。

### 假设 1 — finish 金币字段 glodNum/goldNum：无数据丢失
- **触发条件**：读 replay格式文档 vs 读实际 replay.txt。
- **证据**：文档 `docs/replay格式文档.md:665,678,685` 写 `glodNum`（拼写错）；实际 `replay.txt` finish 用 `goldNum`（实测）；解析器 `ReplayParser.ParseFinish` 读 `goldNum` `ReplayParser.cs:256`，与数据一致。且 finish 的 gold/diamond **结算从不显示**（只读 `result/totalScore` `ReplayPlayer.cs:497-498`）。
- **严重度**：P2（纯文档拼写错 + 字段本就用不到）。
- **验证**：运行到结算，观察分数；金币数不显示，故无法从 UI 观察到差异。改读 `docs/replay格式文档.md` 的 finish 示例即可确认。

### 假设 2 — CellToWorld 与 SceneBuilder 是否同一套变换：一致（但硬编码副本多）
- **触发条件**：换地图尺寸（非 41×32）。
- **证据**：StateEngine `z=gameY-oz`、SceneBuilder `z=oz-row` 且 `gameY=mapH-1-row` → 二者等价（§3.2）。但 `ReplayCameraRig`/`ReplayPlayer` 徽标/`NpcFacingController` 硬编码 20/15.5，换地图会错位。
- **严重度**：P2（当前地图正确）。
- **验证**：Play 中拖动时间轴，观察 NPC 朝向/交易徽标是否仍贴在 20/15.5 假设的位置（换非 41×32 地图才能触发错位）。

### 假设 3 — type==3 双射线路径：已消除，无重复
- **触发条件**：防御塔攻击事件。
- **证据**：`OnCommand case "attack"` 与 `OnDamage` 均 `if (u.type != 3 && !u.IsBeast) FxFactory.Beam(...)`，type==3 只走 `TriggerTowerAttack`→`Fire`→`SpawnTracer`。`ReplayPlayer.cs:219-221,254-258`。
- **严重度**：无（已正确）。
- **验证**：Play 到塔攻击回合，确认只有塔 Tracer 无通用 Beam。

### 假设 4 — Seek/Pause 时各表现是否都停：震屏不暂停（P2）
- **触发条件**：Auto 导播模式 + 事件特写触发震屏后按暂停。
- **证据**：`TowerVisualController`/`UnitView`/`NpcFacingController`/`TradeBadge` 均按 `playing` 或 `Time.deltaTime` 冻结 ✅；但 `CameraManager.UpdateShake` 用 `Time.unscaledDeltaTime` `CameraManager.cs:367`，且 `CameraManager.LateUpdate` 三通道 SmoothDamp 也用 `unscaledDeltaTime` `CameraManager.cs:385` → **暂停时 Auto 相机仍移动、震屏仍衰减**。
- **严重度**：P2（视觉不冻结，非崩溃）。
- **验证**：进入 Auto 模式，制造一次「袭击了基地」事件触发震屏，立刻暂停，观察相机是否继续漂移。

### 假设 5 — PrefabRefs 四个 UI 引用是否为空：非空，走 Prefab 路径
- **触发条件**：UI 创建。
- **证据**：`unknow.unity` 中 `hudPanelPrefab/eventLogPanelPrefab/playbackControlPanelPrefab/settlementPanelPrefab` 均有 GUID（`unknow.unity:347-350`）；4 个 Prefab 存在于 `Assets/Prefabs/UI/`。→ `Get*Prefab` 返回序列化引用（**不再有 `Resources.Load` 兜底**；`Create()` 缺 prefab 时 `Debug.LogError` 并返回 null，调用处已判空）。
- **严重度**：无。
- **验证**：Play，UI 出现即为 Prefab 路径；若某个 prefab 引用断掉，Console 会出现 `[xxxController] 缺少 xxx prefab` 的 LogError。

### 假设 6 — OfficerNPC/VendorNPC Animator.controller 是否为空：非空（文档已过时）
- **触发条件**：NPC 实例化。
- **证据**：实测两个 prefab `m_Controller: {fileID: 9100000, guid: 6deaed17cc256fd4bb9821a81a30b7a2}`（非空）。PROJECT_STATE §7「NPC T-Pose：m_Controller:{fileID:0}」**已过时**（§8 已记 08-11 修复）。
- **严重度**：无（代码仍安全：`UpdateAnimation` 的 SetTrigger 包 try/catch）。
- **验证**：Play，任务官/小贩应播放 Idle 而非 T-Pose。

### 假设 7 — Minigun Muzzle 默认 active 与 Setup 是否匹配：Setup 强制 SetActive(true)
- **触发条件**：塔开火。
- **证据**：`Setup()` 先 `playOnAwake=false` 再 `_muzzlePoint.gameObject.SetActive(true)` `TowerVisualController.cs:160-165`，与注释一致；不依赖源塔 Muzzle 默认态。源塔 Muzzle 默认态在嵌套 ProjectAssets 内，未核实（属第三方）。
- **严重度**：无。
- **验证**：Play 到塔攻击回合，观察枪口粒子是否喷出。

### 假设 8 — UnitView 量包围盒是否跳过 ParticleSystemRenderer：**未跳过（不完整修复）**
- **触发条件**：非塔单位（基地/墙/工人/开拓者/野兽）prefab 含粒子系统时，HP 条尺寸计算。
- **证据**：`UnitView.EstimateHeight/EstimateWidth` 用 `GetComponentsInChildren<Renderer>()` **未过滤** `ParticleSystemRenderer` `UnitView.Hp.cs:15,28`；而 `TowerVisualController.MeasureSize` **已过滤** `TowerVisualController.cs:358`。即「粒子撑大包围盒」修复只覆盖了塔，未覆盖 `UnitView` 通用测量。
- **严重度**：P2（潜在，当前野兽/角色 prefab 未必带粒子）。
- **验证**：若某野兽 prefab 带 ParticleSystem，HP 条会异常宽高；否则无表象。需在 Editor 选中一个带粒子的单位观察血条。

### 假设 9 — 基地 HP 读取路径（TeamStat.baseHp vs units[base].hp）：读 units，baseHp 死字段
- **触发条件**：底部面板显示基地 HP。
- **证据**：`PlaybackControlPanelController.Sync` 用 `u.type==4 → hp=u.hp`（读 `engine.units`）`PlaybackControlPanelController.cs:318`；`TeamStat.baseHp` 只写不读 `ReplayState.cs:295`。
- **严重度**：无（行为正确，baseHp 冗余）。
- **验证**：Play，面板基地 HP 随血量变化，与日志一致。

### 假设 10 — 野兽挂在被攻击方 roles 中是否被套阵营色/SelRing：不会
- **触发条件**：野兽出现在某队 roles 里（带 teamType）。
- **证据**：`UnitView.EnsureRing` 仅 `type 6/7` 建 SelRing `UnitView.Hp.cs:92`；野兽走 `ConfigureFromBeastPrefab`（不调 TeamColorApplicator、不切 Model_Red/Blue）；`TeamColorApplicator` 只找 SelRing。→ 野兽无阵营色/SelRing，视觉中性。但野兽仍带 teamType，会影响事件日志的 `TeamTag` 前缀（见假设 3.4 的映射反转）。
- **严重度**：无（SelRing 正确不套野兽）。
- **验证**：Play 到野兽出现，确认无红/蓝光圈。

---

## 5. 与 PROJECT_STATE.md / HUD_UI_AUDIT.md 的偏差

| 类型 | 条目 | 说明 |
|---|---|---|
| 文档过时 | PROJECT_STATE §7「NPC T-Pose：Animator Controller 缺失」 | 说 `m_Controller:{fileID:0}`，实测两 prefab 均有有效 guid `6deaed17…`（08-11 已修，§8 自身也记了修复，§7 表未删） |
| 代码有、文档没写（已修） | `ReplayPlayer.TeamTag` challenger/defender 映射反转 | 两篇文档均把 defender=红、challenger=蓝 作为既定事实，`ReplayPlayer.cs:200` 曾反着写；已于 2026-08-13 改为 defender=红/challenger=蓝 |
| 文档过时（描述与实现不符） | PROJECT_STATE §二 `NpcFacingController`「命令优先级 (executeTask/submitAnswer/sell) + Smooth01 八方向水平旋转」 | 实际 `RefreshTarget` 只按切比雪夫距离 `dist==1` 检测来访者，无命令优先级、无 8 方向、无 Smooth01（用 `Quaternion.RotateTowards` 连续转）`NpcFacingController.cs:82-111`；`ReplayPlayer.roundActions` 正是为此预留但从未被读 |
| 文档过时 | PROJECT_STATE §二 `ResourceViewManager`「3D 球体 + 物理 .mat 材质」 | 实际先 FBX（OreRockModel）直载，Sphere 是 fallback；§三/§六 已更正为 FBX，§二 一句话仍写「球体」 |
| 代码有、文档没写 | `TeamStat.baseHp` 死字段 | HUD_UI_AUDIT §3 已点名「baseHp 不显示」，但 PROJECT_STATE 未把它列入死代码 |
| ~~文档过时~~（已修 08-19） | ~~HUD_UI_AUDIT §1「角色数量 ❌ 未显示」~~ | 已解决：prefab 含 `RMm/BMm`（人数正常显示），HUD_UI_AUDIT §1/§8 已更正；唯一缺人数标签的 `CreateFromCode` 兜底已删除 |
| 文档有、代码没有 | HUD_UI_AUDIT §0「单位/建筑字段均为 {fileID:0}」 | `unknow.unity:331` 显示 `unitBasePrefab` **非空**（有 GUID）；文档未误，但未说明 unitBasePrefab 非空却永不加载（`GetUnitPrefab` 未用） |
| 一致 | HUD_UI_AUDIT §5「panel_frame.png 未被引用」 | 代码零引用，已核实 ✅ |
| 一致 | PROJECT_STATE §四「坐标 (x-20,0,y-15.5)」 | 与 StateEngine.CellToWorld 一致 ✅ |

---

## 6. 不建议现在重构的理由

1. **UnitView 的序列化/prefab 依赖**：`_body/_hpFill/_selRing` 全靠 `transform.Find("Body"/"Visual"/"HpFill")` 按名查找，`strideCoefficient` 是 [SerializeField]；动 `UnitView` 的创建路由或字段会连锁破坏 Worker/Pioneer/NPC/Beast 多个 prefab 的运行时装配，且「改坏了只在 Play Mode 才暴露」。**2026-08-20 已用 Partial Class 完成文件拆分（类名/命名空间/GUID/字段与公开 API 零改动，13 个 Prefab 与调用方无感知）**；上述约束对「改创建路由或改字段名」仍成立。
2. **Prefab GUID 链**：`unknow.unity → 4 个 UI prefab`、`Beast_XX → Robot Nested Prefab`、`CubeTowers/Tower_* → ProjectAssets 源塔` 三层 GUID 引用。任何一条断掉都**静默回退**到代码路径或空引用，无编译报错。
3. **双相机并存**：`ReplayCameraRig`（机位/Free 输入）与 `CameraManager`（Auto 导播）同时写 `Camera.main`，Auto/Manual 切换靠 `SetSpectatorMode` + `enableAutoLock` 协调；重构任何一边都要两处一起验证。
4. **UI 双轨**：~~Prefab 与 CreateFromCode 已分叉~~（2026-08-19 已删除全部 `CreateFromCode`，UI 纯 prefab 驱动，分叉风险消除；`TaskPanel` 是唯一纯代码面板）。
5. **SceneBuilder 的 KayKit scale=100 / -90° 修正**：`if (treePrefab.transform.localScale.x > 50f)` 这类魔数 + FBX 原生旋转修正，删改很容易让树/围栏/矿石翻倒或缩放失控。
6. **阵营色常量散落**：统一常量看似无害，但柔和/霓虹两套色系已各自被 UI prefab 文本色和世界空间特效分别使用，合并可能悄悄改变视觉。

---

## 7. 建议的下一步（最多 5 条，各自可独立提交）

1. ✅ **已修 `ReplayPlayer.TeamTag` 阵营映射反转（2026-08-13）**
   - 已改：`Assets/Scripts/Core/ReplayPlayer.cs:200`，现为 defender=红、challenger=蓝（与 §3.4 其余全部一致），并加注释说明约定。
   - 验收：Play 中看事件日志，challenger 的伤害/交易日志前缀应显示「蓝方」蓝色，defender 显示「红方」。

2. ✅ **已删死代码文件 `RaygeasEnvironmentV2.cs`（2026-08-14）**
   - 已删：`Assets/Scripts/Scene/RaygeasEnvironmentV2.cs` 及其 `.meta`；全工程搜索仅文件自身命中，无 scene/prefab/asset/其它 .cs 引用。
   - 编译：删除后首次 refresh（scripts scope）因旧 `.csproj` 残留报 CS2001，改 `scope:all` 强制刷新后 `.csproj` 重生成，Console 0 error，编译通过。
   - 说明：**未进 Play Mode**，故未实际验证水面（水面走 `SceneBuilder.AddWaterTile`，与本文件无关，但未运行确认）。

3. **给 `UnitView.EstimateHeight/Width` 补上粒子过滤**
   - 改：`Assets/Scripts/Scene/UnitView.Hp.cs:15-40`，`GetComponentsInChildren<Renderer>()` 处跳过 `ParticleSystemRenderer`（与 `TowerVisualController.MeasureSize` 对齐）。
   - 禁止改：`TowerVisualController.cs`、各 prefab 的 HP 条节点。
   - 验收：带粒子的单位 HP 条不再异常宽高；塔 HP 条无回归。

4. ✅ **已随「删除 CreateFromCode 兜底」解决（2026-08-19）**
   - 原建议让代码兜底补「人数」标签；兜底已整体删除（`PlaybackControlPanelController` 不再有 `CreateFromCode`），面板一律走 prefab 路径，`RMm/BMm` 人数正常显示，该建议失效。

5. **删除确认未加载的 Prefab/资源（部分完成 2026-08-19，其余待产品确认）**
   - 已删（2026-08-19）：`Assets/Prefabs/UI/Legacy/`（`HudPanel_Legacy`、`PlaybackControlPanel_Legacy`）。
   - **保留**：`Legacy/Tower_Legacy.prefab` 是 `TowerPrefabBuilder` 的备份产物（菜单工具会生成/覆盖），非死代码，不删。
   - 待确认：`Resources/Prefabs/Environment/Trees/`、`Bushes/`、`CubeTowers/Tower_Flamethrower_*`、`Tower_RPG_*`、`Resources/UITheme/panel_frame.png`。
   - 禁止改：`CubeTowers/Tower_Minigun_Red/Blue.prefab`、`Buildings/Tower.prefab`、任何 .cs 里的 `Resources.Load` 路径。
   - 验收：删除后全局搜索无 `Environment/Trees`、`Bushes`、`Tower_Flamethrower`、`Tower_RPG`、`panel_frame` 引用；Play 全流程无 Missing 报错。
