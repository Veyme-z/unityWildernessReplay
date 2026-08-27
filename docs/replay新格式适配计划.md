# replay 新格式（v6.2）适配计划

> 状态：**待办**（尚未动手改代码）
> 依据：[replay格式文档.md](replay格式文档.md)（已按 v6.2 更新） + 新格式样例 `Assets/StreamingAssets/replay (6).txt`
> 更新：2026-08-27

---

## 一、背景与现状

### 1.1 两份数据文件

| 文件 | 格式 | 说明 |
|------|------|------|
| `Assets/StreamingAssets/replay.txt`（8-21，正式加载） | **旧格式** | `tower`/`mapType:3`、`taskOfficer`、无 `buildZones`、attack `targetPos` 单对象、`finish.glodNum` |
| `Assets/StreamingAssets/replay (6).txt`（8-27，untracked） | **新格式（文档所描述）** | roleType **30/31/32** 武器工事（各约 2285 处）、`buildZones`、`round.npc` 六 NPC、attack `targetPos` **数组**（6136 处）、动作 `nothing`、新购买物品名、`finish.goldNum` |

### 1.2 结论

当前代码跑**旧文件正常**，跑**新文件会出问题**（塔不显示、塔攻击指向 (0,0)、伤害来源漏判、物品徽标显示英文 key 等）。要兼容新格式需按下文逐项修改。

---

## 二、改动点清单（按优先级）

### A. 武器工事类型 3 → 30/31/32（加特林/电磁狙击炮/火箭发射台）【必须改】

新数据角色类型：`30`=加特林、`31`=电磁狙击炮、`32`=火箭发射台（均为武器工事，替换旧 `3`=防御塔）。所有 `type == 3` 的塔判定需兼容 30/31/32，否则新数据下塔无对应 Prefab → 生成空占位不可见。

| 涉及文件:行 | 现状 | 需改 |
|---|---|---|
| `Scene/UnitView.cs:55-65` `UNIT_PREFABS` | 仅 `{3, "Prefabs/Buildings/Tower"}` | 加 `{30/31/32 → "Prefabs/Buildings/Tower"}`，或按类型拆三种塔模型 |
| `Scene/UnitView.cs:179` `isBuilding` | 判 3/4/5/10 | 加 30/31/32 |
| `Scene/UnitView.cs:229` | `state.type == 3 → SetupTowerVisual()` | 改 30/31/32 |
| `Scene/UnitView.Hp.cs:119` | 塔血条按塔视觉包围盒 | 改 30/31/32 |
| `Scene/UnitViewSprite.cs:87` | `type == 4 || type == 3` | 加 30/31/32 |
| `Core/ReplayState.cs:34` `IsBuilding` | 判 3/4/5 | 加 30/31/32 |
| `Core/ReplayState.cs:245` 建造日志 | 判 5/3 | 加 30/31/32 |
| `Core/ReplayPlayer.cs:132`（Seek 复位塔） | `u.type == 3` | 改 30/31/32 |
| `Core/ReplayPlayer.cs:231`（OnDamage 塔不发 Beam） | `from.type != 3` | 改 30/31/32 |
| `Core/ReplayPlayer.cs:273`（attack 特效塔分支） | `u.type != 3` | 改 30/31/32 |
| `Core/ReplayPlayer.cs:296`（TriggerTowerAttack） | `u.type == 3` | 改 30/31/32 |
| `UI/PlaybackControlPanelController.cs:252`（塔计数统计） | `u.type==3` | 改 30/31/32 |
| `Scene/TowerVisualController.cs` `ResolveTowerType()` | 固定返回 `"Minigun"` | **可选**：30→Minigun / 31→RPG / 32→Flamethrower 区分三种塔 |

### B. attack 的 `targetPos` 变为坐标数组【必须改】

新数据 attack 的 `targetPos` 是数组 `[{x,y},...]`（加特林传 N 个落点、电磁狙击炮/火箭台各 1 个）。当前模型只有单点 `x,y`，数组会导致：
- `hasTarget=false` → 攻击日志显示 (0,0)
- `FindAttacker` 单点比对 → 多落点伤害来源漏判
- 塔 `TriggerTowerAttack(wp)` 指向错误位置

| 涉及文件:行 | 需改 |
|---|---|
| `Core/ReplayModels.cs:64-74` `ReplayCommand` | 增加 target 列表（如 `List<ReplayPoint> targets`），保留单点 `x,y` 兼容旧数据 |
| `Core/ReplayParser.cs:198-204` | `targetPos` 解析同时支持数组（`MiniJson.Arr`）与对象 |
| `Core/ReplayState.cs:443-457` `FindAttacker` | 遍历全部落点判定命中，替代单点 `c.x/c.y` |
| `Core/ReplayPlayer.cs:265` `OnCommand` | `wp = CellToWorld(c.x,c.y)` 对数组取首点/遍历；塔 `TriggerTowerAttack(wp)` 指向真实目标 |

### C. 新动作 `nothing`【建议改】

新数据 `nothing` 动作（38 处）均 `valid:false` → `OnCommand` 提前 return 不刷日志；但 `Core/ReplayPlayer.cs:263` 在 valid 判断**之前**就 `roundActions[u.id] = c.action`。建议 switch 显式加 `case "nothing": break;` 防患（若出现 `valid:true` 的 nothing 会走 default 分支刷日志）。

### D. 购买/使用物品名变化【建议改】

新数据 buy/use 的 `targetName`：
- `UpgradeTowerAttack/UpgradeTowerMaxHp/UpgradeWallMaxHp/UpgradeStationMaxHp` → **`WeaponUpgradeVoucher`（228 处）/ `WallUpgradeVoucher` / `StationUpgradeVoucher`**
- use 新增 `WallUpgradeVoucher`（`WallFixer` 已映射 ✓）

`Core/ItemNameCn.cs` 缺这三个新名映射 → 交易/使用徽标会显示英文 key。旧名映射保留兼容旧数据。

建议映射：`weaponupgradevoucher`→武器升级券 / `wallupgradevoucher`→围墙升级券 / `stationupgradevoucher`→基地升级券（中文名自定）。

### E. start 新增 `buildZones`（v6.2）【可选】

`Core/ReplayModels.cs` `ReplayStart` 无 `buildZones` 字段（wallRing/towerZone 每队一组，限制围墙/武器工事可建区域）。解析器忽略未知键不报错，**纯回放播放器可不改**；若想可视化建造区域需加模型 + 解析。

### F. start.roles 变化（key 改名、建筑 level:1、NPC 无 health/attackPower）【无需改动】

代码不读 `start.roles`（只读 teams/map），渲染由 round roles 驱动，解析器自动忽略未知字段。

### G. round 新增 `npc` 六 NPC 列表【解析已支持 ✓，未渲染】

- `Core/ReplayModels.cs` `ReplayNpc` + `Core/ReplayParser.cs:93-106` **已解析** ✓
- 当前 NPC/商店由 `SceneBuilder` 从 **map.data 瓦片**摆放（vendor@(20,15)、weaponShop@(25,11)，与 `ReplayPlayer` 硬编码查找 `NPC_9_20_15`/`NPC_10_25_11` 一致 ✓）
- `round.npc` 里的位置（vendor@(20,16)、weaponShop@(25,20) 等）**未被任何代码消费**
- 任务点 40-43 瓦片在 map.data 存在但 SceneBuilder 未处理（见 H）
- **可选用**：以 `round.npc` 校验/摆放任务点与商店；当前不做也不崩

### H. map.data 新瓦片编号【部分可选】

| 瓦片 | `SceneBuilder.cs` 现状 | 需改 |
|---|---|---|
| `2` 水 | `AddWaterTile` ✓ 已处理 | 无需 |
| `23/24/25` 矿 | 矿点显示由 round.resources 驱动 ✓ | 无需 |
| `40-43` 任务点 | 行 128-132 **未处理**（掉成纯草地） | **可选**：加可视表现（旗子/光圈/任务点标记） |
| `8` taskOfficer | 新数据已无此瓦片，`BuildNeutralNpc` 的 `t==8` 分支变死代码 | 保留兼容旧数据 |
| `3` 塔瓦片 | 新数据不再出现，行 130 `t==3` pad 分支变死代码 | 保留兼容旧数据 |

### I. round roles 新增 `roadLineType` / `level`【已解析 ✓，渲染未用】

- 模型+解析器已支持（`ReplayModels.cs:58-59`、`ReplayParser.cs:179-180`）✓
- 渲染未使用：文档注明 level「供前端按 level 渲染不同围墙/武器形象」
- **可选用**：按 `level` 换围墙/武器外观（如墙升级后换模型/变色）；当前不实现也不崩

### J. allTaskInfo.codeGenerateTask 变 3 元素【无需改动】

`[0,0]` → `[0,0,0]`。代码不解析/不使用 `allTaskInfo` 字段，无影响。

### K. finish.glodNum → goldNum【无需改动】

`Core/ReplayParser.cs` `ParseFinish` 已读 `goldNum`（并保留 `diamondNum` 旧兼容）✓

### L. commands 字段恒定存在值为 null【无需改动】

Parser 容错（`MiniJson` 缺失字段给默认值）✓

---

## 三、数据层切换（非代码）

新格式要正式生效，需把 `Core/ReplayEntry.cs` 的加载路径从 `replay.txt` 切到 `replay (6).txt`，或将新文件覆盖为 `replay.txt`。

---

## 四、待办清单（勾选使用）

- [x] **A. 武器工事类型 3 → 30/31/32**（✅ 2026-08-27 已完成：全部 `type == 3` 判定改 `IsTower`；`UNIT_PREFABS` 补 30/31/32→Tower；`ResolveTowerType` 30→Minigun/31→RPG/32→Flamethrower；`TURRET_NODES` 补 Rpg/Flamethrower；`TypeName` 补中文名）
- [x] **B. attack `targetPos` 数组支持**（✅ 2026-08-27 已完成：`ReplayCommand.targets` + 解析器数组/对象双格式 + `FindAttacker` 遍历落点 + `OnCommand` 取首点）
- [ ] **C. 动作 `nothing` 显式 case**（防 valid:true 时刷日志）
- [x] **D. `ItemNameCn` 补三个升级券映射**（✅ 2026-08-27 已完成：`weaponupgradevoucher`→武器升级道具 / `wallupgradevoucher`→围墙升级道具 / `stationupgradevoucher`→基地升级道具；旧映射保留兼容旧数据）
- [ ] E. （可选）`start.buildZones` 模型 + 解析 + 可视化建造区域
- [ ] H. （可选）`SceneBuilder` 处理 map.data 任务点瓦片 40-43 的可视表现
- [ ] I. （可选）按 `level` 渲染不同围墙/武器外观
- [ ] G. （可选）用 `round.npc` 摆放/校验任务点与商店
- [ ] 数据层：`ReplayEntry` 加载路径切到新文件 / 覆盖 `replay.txt`

---

## 五、验证方法

1. 编译 0 error / 0 warning
2. Play Mode 加载 `replay (6).txt`：
   - 加特林/电磁狙击炮/火箭发射台三塔正常显示（有 Prefab、血条、阵营色）
   - 塔攻击时炮塔转向真实目标格、Tracer 指向落点，伤害日志 `A → B -dmg` 来源正确
   - 买卖/使用徽标显示中文（武器升级券/围墙升级券/基地升级券），不显示英文 key
   - `nothing` 动作不产生日志
   - 拖动进度条/Seek 无塔残留攻击表现、无 (0,0) 攻击
3. 回归旧 `replay.txt`：显示与改动前一致（旧格式单点 targetPos、tower type 3、旧物品名仍可用）
