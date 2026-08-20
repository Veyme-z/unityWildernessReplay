# UnitView.cs 依赖审计

> 只读审计（2026-08-20）。目标文件：`Assets/Scripts/Scene/UnitView.cs`（818 行）。
> 审计方法：meta GUID 反查 Prefab/Scene 引用 + 全 .cs 代码引用扫描 + 字段/API 静态盘点。

---

## 1. UnitView GUID

```
guid: 58167f24033a1d14a8d2d3f8ac9c07db
```

---

## 2. Prefab 挂载点（UnitView 组件预挂）

GUID 反查命中 **13 个 .prefab**，`.unity` 场景文件 0 个命中。

每个 Prefab 中 UnitView 组件的序列化块 dump（所有 13 个 **完全一致**）：

```yaml
m_Script: {fileID: 11500000, guid: 58167f24033a1d14a8d2d3f8ac9c07db, type: 3}
m_Name:
m_EditorClassIdentifier:
strideCoefficient: 1        # ← 唯一被序列化的字段，值恒为 1
```

| # | Prefab 路径 | SerializeField 引用 dump |
|---|-------------|--------------------------|
| 1 | `Assets/Prefabs/Units/UnitBase.prefab` | `strideCoefficient: 1`（`state` 未拖） |
| 2 | `Assets/Resources/Prefabs/Units/Worker.prefab` | `strideCoefficient: 1` |
| 3 | `Assets/Resources/Prefabs/Units/Pioneer.prefab` | `strideCoefficient: 1` |
| 4 | `Assets/Resources/Prefabs/Units/OfficerNPC.prefab` | `strideCoefficient: 1` |
| 5 | `Assets/Resources/Prefabs/Units/VendorNPC.prefab` | `strideCoefficient: 1` |
| 6 | `Assets/Resources/Prefabs/Buildings/Tower.prefab` | `strideCoefficient: 1` |
| 7 | `Assets/Resources/Prefabs/Buildings/Legacy/Tower_Legacy.prefab` | `strideCoefficient: 1` |
| 8 | `Assets/Resources/Prefabs/Buildings/Base.prefab` | `strideCoefficient: 1` |
| 9 | `Assets/Resources/Prefabs/Buildings/Wall.prefab` | `strideCoefficient: 1` |
| 10 | `Assets/Resources/Prefabs/Beasts/Beast_11.prefab` | `strideCoefficient: 1` |
| 11 | `Assets/Resources/Prefabs/Beasts/Beast_12.prefab` | `strideCoefficient: 1` |
| 12 | `Assets/Resources/Prefabs/Beasts/Beast_13.prefab` | `strideCoefficient: 1` |
| 13 | `Assets/Resources/Prefabs/Beasts/Beast_14.prefab` | `strideCoefficient: 1` |

**注意**：
- `state`（public UnitState）是序列化字段（public 非 static），但 **没有任何 Prefab 拖过它**——全部在运行时由 `UnitView.Create()` 赋值（`v.state = u`）。
- `Assets/Resources/Prefabs/Buildings/WeaponShop.prefab`（type=10）存在但 **未预挂 UnitView**：`Create()` 走 `GetComponent<UnitView>() → AddComponent<UnitView>()` 运行时补挂。

---

## 3. 代码引用清单（全部 .cs）

### 3.1 类型级引用（字段 / 参数 / 泛型）

| 位置 | 代码 | 用途 |
|------|------|------|
| `Scripts/Core/ReplayState.cs:31` | `public UnitView view;` | 数据层持有视图引用 |
| `Scripts/Core/ReplayState.cs:355` | `units[r.id].view == null` | 判断视图存在 |
| `Scripts/Core/ReplayState.cs:386` | `u.view.gameObject` 销毁 + `u.view = null` | 销毁视图 |
| `Scripts/UI/UnitDebugOverlay.cs:34` | `GetComponent<UnitView>()` | 取视图读 state |
| `Scripts/Scene/TowerVisualController.cs:70` | `UnitView _view;` | 防御塔视觉持有 |
| `Scripts/Scene/TowerVisualController.cs:110` | `ResolveTowerType(UnitView view)` | 参数（实参被忽略，恒返回 "Minigun"） |
| `Scripts/Scene/TowerVisualController.cs:116` | `Setup(UnitView view, string faction)` | 参数（`_view = view`，未读 state） |
| `Scripts/Scene/TeamColorApplicator.cs:5` | `public UnitView unitView;` | 读 `unitView.state.teamType`、`unitView.transform` |
| `Scripts/Scene/TeamColorApplicator.cs:10` | `GetComponentInParent<UnitView>()` | 兜底取视图 |
| `Scripts/Scene/NpcFacingController.cs:23` | `UnitView _view;` | 读 `_view.state.type` / `_view.state.pos` |
| `Scripts/Scene/NpcFacingController.cs:30` | `GetComponent<UnitView>()` | 取视图 |
| `Scripts/Scene/Pickable.cs:6` | `public UnitView view;` | 可拾取标记（仅持有，外部不读） |

### 3.2 `Create()` 工厂调用

| 位置 | 代码 |
|------|------|
| `Scripts/Core/ReplayPlayer.cs:139` | `u.view = UnitView.Create(u, unitsRoot);` |
| `Scripts/Core/ReplayPlayer.cs:177` | `u.view = UnitView.Create(u, unitsRoot);` |
| `Scripts/Core/ReplayPlayer.cs:242` | `u.view = UnitView.Create(u, unitsRoot);` |

### 3.3 静态字段写入

| 位置 | 代码 |
|------|------|
| `Scripts/Core/ReplayPlayer.cs:462` | `UnitView.AnimatorSpeed = SPEEDS[speedIndex];` |

### 3.4 排除项（非真依赖）

- `Scripts/Scene/UnitViewSprite.cs` —— **独立 static class** `UnitViewSprite`，仅日志字符串前缀写 `"[UnitView]"`，与 UnitView 类零耦合。
- `Scripts/Scene/SceneBuilder.cs:406-408` —— 调用的是 `UnitViewSprite.FindSprite(...)`，非 UnitView。

---

## 4. 字段清单

### 4.1 序列化字段（会进 .prefab / Inspector）

| 字段 | 修饰符 | 类型 | Prefab 是否存值 |
|------|--------|------|-----------------|
| `state` | public | `UnitState` | ❌ 无任何 Prefab 拖过（运行时 Create 赋值） |
| `strideCoefficient` | public | `float` = 1.0 | ✅ 13 个 Prefab 全存 `1` |

### 4.2 非序列化实例字段（private）

| 字段 | 类型 |
|------|------|
| `_body` | `Transform` |
| `_hpFill` | `Transform` |
| `_selRing` | `Transform` |
| `_hpY, _hpW, _hpThick` | `float` |
| `_hpFillRend` | `MeshRenderer` |
| `_mpb` | `MaterialPropertyBlock` |
| `_animator` | `Animator` |
| `_player` | `ReplayPlayer` |
| `_hasParams` | `bool` |
| `_towerVisual` | `TowerVisualController` |
| `_skinned` | `SkinnedMeshRenderer` |
| `_lodGo` | `GameObject` |
| `_lodStatic` | `bool` |
| `_lodBaseScale` | `Vector3` |
| `_transientAnimUntil`, `_lastTransientEnter` | `float` |
| `_prevPos` | `Vector3` |
| `_prevAnimScale` | `float` |
| `_wasMoving`, `_wasDead`, `_lockRotation` | `bool` |
| `_lastStun` | `bool?` |
| `_lastHp`, `_lastMaxHp` | `int` |
| `_animSpeed` | `float` |
| `_baseScale` | `float` |
| `_pivotOffset` | `Vector3` |
| 常量 | `TURN_SPEED`, `TOWER_HP_TOP_PADDING` |

### 4.3 static 字段

| 字段 | 类型 | 外部访问 |
|------|------|----------|
| `LOD_RANGE` = 30 | public static float | 仅文档注释约定 execute_code 调试改值 |
| `LodTransientCooldown` / `LodTransientWindow` | public static float | 同上 |
| `LodIdleBobAmplitude` / `LodIdleSwayAmplitude` | public static float | 同上 |
| `AnimatorSpeed` = 1 | public static float | ✅ `ReplayPlayer.cs:462` 写入 |
| `s_lodMeshCache` / `s_camera` / `s_cachedPlayer` / `s_hpFillMat` | private static | 内部 |
| `UNIT_PREFABS` | private static readonly | 内部 |

---

## 5. Public API 清单 + 调用方

| 方法签名 | 调用方 |
|----------|--------|
| `static UnitView Create(UnitState u, Transform parent)` | `ReplayPlayer.cs:139/177/242` |
| `void UpdateAnimation(bool isMoving, bool isDead)` | `ReplayPlayer.cs:528` |
| `void TriggerAttack()` | `ReplayPlayer.cs:233` |
| `void TriggerCollect()` | `ReplayPlayer.cs:286/301` |
| `void TriggerTowerAttack(Vector3 targetWorldPos)` | `ReplayPlayer.cs:276` |
| `void ResetTowerAttack()` | `ReplayPlayer.cs:133` |
| `void TriggerDeath()` | `ReplayPlayer.cs:255` |
| `void SetAnimScale(float s)` | `ReplayPlayer.cs:243` |
| `void SetHp(int hp, int maxHp)` | `ReplayPlayer.cs:524` |
| `void SetStun(bool stun)` | `ReplayPlayer.cs:525` |

**外部只读访问**（非方法）：`state` 字段被 `TeamColorApplicator` / `NpcFacingController` / `UnitDebugOverlay` 读取（`.state` / `.state.type` / `.state.pos`）；`transform` 被 `ReplayPlayer` / `TeamColorApplicator` / `UnitDebugOverlay` 使用。

**Unity 回调**：`LateUpdate()`（每帧状态同步）、`OnDestroy()` 无。

> 结论：**全部 10 个 public 方法 + 静态 `Create`/`AnimatorSpeed` 的唯一调用方都是 `ReplayPlayer.cs`**；其余组件只读 `state` 字段与 `transform`，不调任何 public 方法。

---

## 6. 结论 6 问

- **Q1 UnitView 是否被任何 Prefab 预挂？（是/否）**
  **是**。13 个 Prefab 预挂了 UnitView 组件（guid `58167f24033a1d14a8d2d3f8ac9c07db`）：UnitBase、Worker、Pioneer、OfficerNPC、VendorNPC、Tower、Tower_Legacy、Base、Wall、Beast_11~14。

- **Q2 是否有 SerializeField 字段被 Prefab 拖过引用？（是/否，列出）**
  **是**，仅 `strideCoefficient`（13 个 Prefab 全存 `=1`）。`state` 虽是序列化字段但无任何 Prefab 拖过，全部运行时由 `Create()` 赋值。

- **Q3 哪些字段因为 Prefab 引用不能迁移？**
  只有 `strideCoefficient`（13 个 Prefab 固化 `=1`，换类/改名需改全部 13 个资产）。`state` 可自由改名/迁移（Prefab 无存值，运行时赋值），但需同步改 3 处外部读者（TeamColorApplicator / NpcFacingController / UnitDebugOverlay）。

- **Q4 哪些 Public 方法签名必须保持不变？**
  全部 10 个 public 实例方法（UpdateAnimation / TriggerAttack / TriggerCollect / TriggerTowerAttack / ResetTowerAttack / TriggerDeath / SetAnimScale / SetHp / SetStun）+ 静态 `Create(UnitState, Transform)` + 静态字段 `AnimatorSpeed`——唯一调用方 `ReplayPlayer.cs`。

- **Q5 是否被继承/SendMessage/反射调用？（是/否）**
  **否**。无 `: UnitView` 继承、无 SendMessage、无 `typeof(UnitView)` 反射、无 `GetMethod`/`Invoke`；动画 Trigger（onDeath/onAttack/isMoving）走 Animator 参数，不经 SendMessage。

- **Q6 建议拆分策略：A激进 / B保守 / C仅Partial Class？一句话理由**
  **C（仅 Partial Class）**——13 个 Prefab 已把 GUID 与 `strideCoefficient` 固化在资产里，partial 拆分保持类名/命名空间/GUID/全部公开 API 不变，零资产改动、零调用方改动，按 血条/动画/距离LOD/防御塔 拆成 `UnitView.*.cs` 即可，风险最低收益最大。

---

### 附：拆分参考分组（C 方案）

- `UnitView.cs`（主类：字段、Create、Configure*、LateUpdate 框架、SetHp/SetStun）
- `UnitView.Hp.cs`（UpgradeHpTo3D / EnsureRing / CreateHpCube / GetSharedHpFillMat / 血条逻辑）
- `UnitView.Anim.cs`（SetupRobotAnimator / ApplyWorkerHitOverride / UpdateAnimation / TriggerAttack / TriggerCollect / TriggerDeath / SetAnimScale）
- `UnitView.Lod.cs`（SetLodStatic / LOD 调参常量 / s_lodMeshCache / LateUpdate 中 LOD 分支）
- `UnitView.Tower.cs`（SetupTowerVisual / TriggerTowerAttack / ResetTowerAttack / _towerVisual）
- `UnitView.Move.cs`（CalibrateBaseScale / 平滑转向 / 位置同步 / 眩晕冻结）
