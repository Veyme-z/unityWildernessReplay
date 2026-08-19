# HUD / UI 结构审计

> 结论先行：HUD **不是纯代码生成，也不是场景里摆好的 Canvas**，而是 **「场景里的 `PrefabRefs` 组件按 GUID 连线 4 个 UI Prefab → 各 Controller.Create() 实例化 → 运行时由代码回填文本/颜色」** 的实现。**纯代码兜底 `CreateFromCode()` 已全部删除**，Prefab 缺失时 `Create()` 直接 `LogError` 并返回 null。

---

## 0. 关键事实

- 启动场景 = `Assets/unknow.unity`（EditorBuildSettings 为空，编辑器当前打开哪个就起哪个）。
- 场景根节点 `PrefabRefs`（`MonoBehaviour`）序列化了 4 个 UI Prefab 的 GUID；`PrefabRefs.Instance` 单例在 `Awake()` 里 `FindObjectOfType` 抓到它。
- 4 个 UI Prefab 真实路径（**不在 Resources 下**，靠 GUID 引用，不是 `Resources.Load`）：

| Prefab | 路径 | 实例化入口 |
|---|---|---|
| HUD | `Assets/Prefabs/UI/HudPanel.prefab` | `HudController.Create` |
| 事件日志 | `Assets/Prefabs/UI/EventLogPanel.prefab` | `EventLogPanelController.Create` |
| 底部面板 | `Assets/Prefabs/UI/PlaybackControlPanel.prefab` | `PlaybackControlPanelController.Create` |
| 结算 | `Assets/Prefabs/UI/SettlementPanel.prefab` | `SettlementPanelController.Create` |

- `PrefabRefs` 里 `baseBuilding/tower/wall/worker/pioneer/officer/vendor` 等字段均为 `{fileID:0}`（空），单位/建筑走 `UnitView` 的 `Resources.Load`，**与 UI 无关**。
- 每个 Prefab 根都是一个**独立 Canvas**（ScreenSpaceOverlay，排序 200/210/220/500），互不嵌套。

---

## 1. 运行时 Hierarchy 简图

```
HudPanel(Canvas, 200)                     ── 顶部
└─ TopPanel(Image 底)
   ├─ DayLabel(Text)   ── 1. DAY / 天数
   ├─ PhaseLabel(Text) ── 2. 白天/黑夜
   └─ RoundLabel(Text) ── 昼夜内回合 "回合 40/80"

EventLogCanvas/…Panel(Canvas, 210)        ── 左侧日志（非本次重点）
└─ Panel ─ ScrollView ─ Content(Text)

PlaybackControlPanel(Canvas, 220)         ── 底部
├─ TeamBar(HLG) ─ RedCard ─ RN/RH/RG/RS/RTw/RWl/RMm/RTk/RBg
│               └ BlueCard ─ BN/BH/BG/BS/BTw/BWl/BMm/BTk/BBg
│            ── 3. 左队  4. 右队（名称/基地/金币/积分/塔/墙/人数/任务/背包，纯文本）
├─ TimelineBar ─ RT(Text) + Slider       ── 9. "40 / 1300 回合" + 时间轴
└─ ControlBar(HLG, 680) ─ Play/Restart/Sp1/Sp2/CamGlobal/CamA/CamB/CamFree/Btn_ShowStats
                 + [动态] Btn_ModeManual/Btn_ModeAuto + DirectorStatus
                                              ── 10. 播放控制 + 镜头切换 + 调试显示

SettlementPanel(Canvas, 500)              ── 结算（游戏结束才出现）
└─ Bg(全屏黑) + Panel ─ Title/Score/RedResult/BlueResult/RestartBtn
```

**逐项归类（都是运行时实例化）：**

| # | 信息 | 归属 | 来源 |
|---|---|---|---|
| 1 | DAY/天数 | `HudPanel/TopPanel/DayLabel` | Prefab |
| 2 | 白天/黑夜 | `…/PhaseLabel` | Prefab |
| 3/4 | 左右队信息 | `TeamBar/RedCard\|BlueCard` 下 RN…/BN… | Prefab |
| 5 | 金币 | RG/BG | Prefab |
| 6 | 总积分 | RS/BS | Prefab |
| 7 | 基地 HP | RH/BH | Prefab |
| 8 | **角色数量** | RMm/BMm（Sync 填"人数 n/3"） | Prefab |
| 9 | 任务/背包摘要 | RTk/RBg、BTk/BBg（Sync 填"任务 对x错y"/"背包 …"） | Prefab |
| 10 | 播放进度/总回合 | RT + Slider | Prefab |
| 11 | 播放控制+镜头按钮 | ControlBar（含 CamFree「自由」） | Prefab + 代码动态加 2 个导演按钮 |

---

## 2. Prefab 与代码的覆盖关系

| 面板 | 实例化方法 | 运行时改 RectTransform? | 运行时改文本? | 运行时改颜色? | 改 Prefab 是否被覆盖 |
|---|---|---|---|---|---|
| HUD | `HudController.Create→GetHudPrefab` | 否 | 是（Day/Phase/Round 文本） | 是（Day/Phase 颜色每帧 Lerp） | 文本/两处颜色被覆盖；布局、字号、底图色、字体保留 |
| 底部 | `PlaybackControlPanelController.Create` | 否 | 是（全部队数据+回合文本） | 部分（Play/Sp1/Sp2/Manual/Auto 按钮底 Image 色） | 队名/金币/积分/HP 文本被覆盖；队标签颜色、字号、坐标保留 |
| 结算 | `SettlementPanelController.Create` | 否 | 是（Setup 填 5 个文本） | 否 | 仅文本覆盖，样式全来自 Prefab |
| 事件日志 | `EventLogPanelController.Create` | 否 | 是（AddEventLog 拼字符串） | 否 | 仅文本覆盖 |

**冲突点（同一属性 Prefab+代码都管）：**
- `HudController`：`dayLabel.color` / `phaseLabel.color` 由代码 `_currentDayColor/_currentPhaseColor` Lerp 覆盖，Prefab 里设的 Day 橙/Phase 金只作初始值。
- `PlaybackControlPanelController.Sync()`：Play/Speed 按钮 `Image.color` 由代码覆盖（播放中蓝/黄、倍速高亮），Prefab 按钮底色被压掉。
- 其余文本颜色（红名红、蓝名蓝、金币金、积分白）**只来自 Prefab**，代码 `Sync` 只改 `.text` 不动 `.color`。

---

## 3. 数据更新链

```
ReplayTeam(ReplayParser) ─┬─ teamName   → StateEngine.teams[].teamName   → Sync → _redName/_blueName
                          ├─ goldNum    → TeamStat.gold (Diff 每回合写)   → Sync → _redGold/_blueGold
                          ├─ totalScore → TeamStat.score                  → Sync → _redScore/_blueScore
                          └─ completeTaskCount → TeamStat.tasks（仅日志用）
ReplayRole(roleType==4).health → engine.units[base].hp（非 TeamStat.baseHp）→ Sync → _redHp/_blueHp
round(ReplayPlayer.cur) ─┬─ StateEngine.DayOf   → HudController.Update → dayLabel
                         ├─ StateEngine.IsNight  → … phaseLabel
                         └─ ((round-1)%130)…     → … roundLabel "回合 40/80"
data.rounds.Count        → _totalRounds          → Sync → _roundText "40 / 1300" + Slider.max
data.finish.players[]    → ShowSettlement        → SettlementPanelController（结算时切换数据源）
```

**刷新方式：**
- `HudController.Update()`：每帧跑，但文本**只在 `round`/`night` 变化时更新**；颜色每帧 Lerp。
- `PlaybackControlPanelController.Update()`：**每帧 `Sync()`**（轮询式，非事件驱动，直接读 `engine` 现场）。
- **Seek 后刷新**：`JumpTo→Step` 不直接调 Sync，下一帧 `Update()` 自然读到新 `cur`，✅ 自动刷新。
- **Replay 重载后重置**：`Restart()` 重建 `engine`，`Sync` 读现场即复位；`_totalRounds` 不变，✅。
- **结算切换**：`ShowSettlement()` 优先用 `data.finish.players`，无 finish 时从 `engine` 推断，✅ 数据源切换。

**未展示的数据（改动时留意）：**
- `TeamStat.baseHp` 被计算但**从不显示**（面板 HP 直接读 `engine.units[base].hp`）。
- `completeTaskCount` 只在事件日志提示，`invalidTaskCount` 解析后**完全未用**。
- `TeamStat.taskText` 被拼装但**无 UI 消费**。

---

## 4. UI 技术栈

- **组件**：`UnityEngine.UI.Text`（uGUI 旧版），**无 TextMeshPro**（未引用 `TextMeshProUGUI`）。
- **Canvas**：`ScreenSpaceOverlay`，4 个独立 Canvas，sortingOrder 200/210/220/500。
- **CanvasScaler**：`ScaleWithScreenSize`，referenceResolution **1920×1080**，matchWidthOrHeight **0.5**（4 个一致）。
- **字体**：运行时由 `UiFonts.Apply()` 统一替换为打包的 `Resources/Fonts/NotoSansSC-Regular`（CJK 动态字体，**无 emoji 字形** → emoji 会渲染成空白，故项目不用 emoji）；prefab 里烘焙的 `LegacyRuntime.ttf` 仅作初始值。
- **布局**：事件日志用 `ContentSizeFitter(PreferredSize)`；底部面板的 `TeamBar`（排 RedCard/BlueCard）和 `ControlBar`（排全部按钮）用 `HorizontalLayoutGroup` 自动排布；HUD/结算仍是 `anchoredPosition`/`sizeDelta` 绝对坐标手工摆位。
- **适配**：16:9 参考分辨率 + match 0.5，超宽屏/竖屏/窗口拉伸时面板因绝对坐标会相对锚点偏移（顶部面板锚点 (0.5,1)、底部锚点 (0.5,0) 居中尚稳，但 TeamBar 内部子项用 `anchorMin=anchorMax=(0,1)` + 固定 x 偏移，窗口变窄会溢出）。

---

## 5. 视觉样式来源

| 样式 | 位置 | 说明 |
|---|---|---|
| 面板底色/透明度 | 各 Prefab `Image.color` = `#1A1A1E`(α0.85/0.95) | — |
| 红/蓝阵营色 | 队名 Text.color（红 `#F05638` 蓝 `#479EF0` 近似） | 在 Prefab Text 上 |
| 金币/积分/HP 颜色 | Prefab Text.color | 金币金黄、积分白、HP 淡红 |
| DAY/昼夜颜色 | **代码** `HudController` `WARM_DAY/COOL_NIGHT/…` | 运行时覆盖 |
| 字号 | Prefab 各 Text.fontSize（Day22/Phase18/Round16/队名14/其它12-13） | 直接改 Prefab |
| 面板宽高 | Prefab RectTransform.sizeDelta（TopPanel 520×52、TeamBar…） | 直接改 Prefab |
| 圆角/边框 | **无**（所有 Image 都是 `m_Sprite:0` 纯色矩形，无九宫格 Sprite） | 唯一 Sprite 是 `Assets/Resources/UITheme/panel_frame.png`（未被引用） |
| 图标 | **无图标 Sprite，全部纯文本**（播放/结算/事件日志的 emoji 已全部移除；旧 `Legacy/` prefab 已删除） | — |
| 背景 Sprite/图标生成 | 无程序生成 | — |

> 背景都**不是**代码程序生成的，全部是纯色 Image + 文本。`Resources/UITheme/panel_frame.png` 存在但当前未被任何 Prefab 引用。

---

## 6. 以后想改什么 → 改哪里

| 想改 | 位置 |
|---|---|
| 布局（面板大小/位置/间距） | 直接改 4 个 Prefab 的 RectTransform（**代码不覆盖布局**） |
| 队标签/金币/积分/HP 的颜色 | Prefab 对应 Text.color（代码不覆盖） |
| DAY/昼夜颜色 | `HudController` 的 `WARM_DAY/COOL_NIGHT/WARM_PHASE/COOL_NIGHT`（代码覆盖 Prefab） |
| 字号 | Prefab 对应 Text.fontSize（代码不覆盖） |
| 面板底色 | Prefab 对应 Image.color |
| 数据显示内容（文本/格式） | 各 Controller：HUD→`HudController.Update`；队数据→`PlaybackControlPanelController.Sync`；结算→`SettlementPanelController.Setup` |
| 增删镜头按钮（全局/蓝方/红方/自由） | 改 `ControlBar` 的 Button 节点（名字保持 `CamGlobal/CamA/CamB/CamFree`），`WireCallbacks` 自动接线；注意 ControlBar 需 680 宽才装得下 |
| 改显示内容/新增字段 | 在 `Sync()` 里补算并加 Text 字段（Prefab 加 Text 节点 + `[SerializeField]` 或按命名约定解析） |
| 换中文字体 | 给所有 Text 的 `m_Font` 换成自定义 Font 资产（或全局遍历设置） |

---

## 7. 推荐最小修改边界

- **只改样式（布局/字号/静态颜色/底色）**：直接编辑 `Assets/Prefabs/UI/*.prefab`，不动代码即可生效（注意别改会被代码覆盖的：Day/Phase 文本颜色、Play/Speed 按钮底色、以及所有文本内容）。
- **改显示内容/新增字段**：改 `HudController.Update` / `PlaybackControlPanelController.Sync`，并在对应 Prefab 增补 Text 节点 + `[SerializeField]` 连线。
- **`CreateFromCode` 纯代码兜底已删除**（4 个 Controller）：所有 UI 必须由 prefab 驱动，不要再引入纯代码生成面板。
- **不要**：新建第二套 Canvas；不要动 `unknow.unity` 里 `PrefabRefs` 的 4 个 UI GUID（断了 `Create()` 会 `LogError` 并返回 null，面板直接不显示，不会静默退回代码路径）。

---

## 8. 底部播放控制面板（PlaybackControlPanel）实现明细

> 这是四个面板里逻辑最重的一个。改前先读 [PlaybackControlPanelController.cs](../Assets/Scripts/UI/PlaybackControlPanelController.cs) 顶部架构注释。

**创建链（prefab 是唯一路径）：**

```
ReplayEntry.LoadReplay() ─▶ PlaybackControlPanelController.Create(player)
  ├─ PrefabRefs.Instance.GetPlaybackControlPrefab()   // 场景 unknow.unity 按 GUID fd4fb86f… 引用 prefab
  │   ├─ null → LogError 并 return null（CreateFromCode 兜底已删除）
  │   └─ 实例化 PlaybackControlPanel(Canvas, sortingOrder 220)
  │      └─ UiFonts.Apply()     // 全部 Text → NotoSansSC（CJK 字体，无 emoji 字形 → emoji 会变空白，故不用 emoji）
  │      └─ AddDirectorUI()     // 动态加：Btn_ModeManual「手动」Btn_ModeAuto「自动」+ DirectorStatus 指示灯
  │      └─ WireCallbacks()     // 按名字查 ControlBar 接线：Play/Restart/Sp1/Sp2/CamGlobal/CamA/CamB/CamFree/Btn_ShowStats/手动/自动 + Slider
  │      └─ ResolveTextRefs()   // 按名字重解析 TeamBar/RedCard|BlueCard 下的文本引用（防序列化引用失效）
  │      └─ Sync(player)        // 立即填充一次数据
```

**运行时状态：**
- `Update()` 每帧调 `Sync()`（轮询式直读 `player.engine` 现场，非事件驱动）；拖 Slider → `SetPlaying(false)+JumpTo`，下一帧自然刷新。
- `Sync()` 写：队名/基地 HP/金币/积分/围墙/防御塔/**人数**/任务对错/背包 + 回合数 + Slider.max + Play/Speed 按钮底色。
- `Update()` 还管：Auto 模式呼吸灯（DirectorStatus）与「手动/自动」按钮高亮。
- 镜头按钮 → `ReplayCameraRig.SetCameraMode("global"/"teamA"/"teamB"/"free")`；键盘 1/2/3/4 同源（见 ReplayCameraRig.Update）。自由 = "free"（左键平移、Alt/Ctrl+左键旋转、滚轮缩放）。

**Prefab vs 代码归属（本面板）：**

| 项目 | 归属 |
|---|---|
| 布局 / 面板尺寸 / 坐标 | Prefab（代码不覆盖；ControlBar 680×50，TeamBar 与 ControlBar 用 HorizontalLayoutGroup 自动排布） |
| 静态颜色（队名红蓝/金币金/积分白）、字号、按钮标签（播放/重播/1x/2x/全局/蓝方/红方/自由） | Prefab |
| 全部文本内容 | 代码 `Sync()` / `Update()` 运行时回填 |
| Play/Sp1/Sp2/手动/自动 按钮底色 | 代码覆盖（播放中蓝/黄、倍速高亮、导播模式高亮） |
| 字体 | 代码 `UiFonts.Apply()` 统一替换为 NotoSansSC |

**近期变更（2026-08）：**
- **新增「显示」调试开关**：ControlBar 新增 `Btn_ShowStats`「显示」按钮，点击切换 `PlaybackControlPanelController.ShowUnitStats`（默认关）。开启时 [UnitDebugOverlay.cs](../Assets/Scripts/UI/UnitDebugOverlay.cs) 在非围墙(5)/非野兽(≥11)单位头顶悬浮显示 `[ID|Pos|HP|ATK]`（0.5s 节流 + 脏检查重建文本，关闭/死亡时 TextMesh 停用零渲染）。
- ControlBar 560 → **680**（新增「自由」后 10 个按钮共需 590px，避免「自动」按钮溢出边框）。
- 新增镜头按钮 **CamFree「自由」**（克隆 CamB，`WireCallbacks` 自动接线，对应键盘 4）。
- **移除全部 emoji**：`PlaybackControlPanelController`/`SettlementPanelController`/`EventLogPanelController` 代码与 `SettlementPanel.prefab`/`EventLogPanel.prefab` 全部改纯中文文本。
- **删除旧资产**：`Assets/Prefabs/UI/Legacy/`（`HudPanel_Legacy`、`PlaybackControlPanel_Legacy` 两个无引用旧 prefab）。
- **删除死代码**：Hud/EventLog/Playback/Settlement 4 个 Controller 的 `CreateFromCode()` 纯代码兜底及不再使用的 helper（Create 改缺 prefab 时报错；调用处已补 null 保护）。
