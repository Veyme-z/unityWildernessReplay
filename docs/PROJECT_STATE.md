# WildernessReplay 项目状态

> **用途**：供新会话的 AI 快速理解项目全貌。原则：说清是什么、在哪改，不堆细节。
> **最后更新**：2026-08-10

---

## 一、项目是什么

Unity 2022.3.62f3c1 **Built-in RP** 回放播放器。加载 JSONL replay 文件，在 41×32 地图上以 3D 可视化两队对战。

- 引擎版本：**2022.3.62f3c1**，管线：**Built-in**（不是 URP）
- 数据流：`JSONL → ReplayParser → StateEngine → ReplayPlayer → UnitView`
- 代码在 `Assets/Scripts/`，约 20 个 .cs 文件
- 资源在 `Assets/Resources/`，第三方素材在 `Assets/KayKit_*/`

---

## 二、核心架构（按需阅读）

### 数据层（只读不改）
- **ReplayModels.cs** — 所有数据模型（ReplayRole, ReplayRound, ReplayNews 等）
- **ReplayParser.cs** — JSONL 解析器
- **ReplayState.cs** — 状态引擎：Diff 驱动出生/死亡/移动，WorldPos 坐标转换
- `docs/replay格式文档.md` — JSONL 字段说明（**需要理解数据结构时才读**）

### 播放层（只读不改）
- **ReplayPlayer.cs** — MonoBehaviour 主控：回合推进、插值（smoothstep）、IReplayHost 事件回调、结算
- **ReplayCameraRig.cs** — 手动模式：1/2/3 快捷机位 + 35°电影俯角
- **CameraManager.cs** — 自动模式：SmoothDamp 跟随开拓者 + 事件特写 + 景深
- **ReplayEntry.cs** — 入口：`[RuntimeInitializeOnLoadMethod]` 自动启动
- `docs/CameraManager实现文档.md` — **需要改相机系统时才读**

### 表现层（高频修改区）
- **UnitView.cs** — 单位 3D 表现核心（~640 行）：
  - `Create()` → Prefab 实例化 → 3 条配置路径
  - `LateUpdate()` → 位置/转向/动画状态同步
  - 静态工具：Sprite 扫描、颜色计算
- **SceneBuilder.cs** — 地图搭建：背景图 + 草地裙边 + 中立 NPC 站位
- **TeamColorApplicator.cs** — MaterialPropertyBlock 队伍染色
- **Pickable.cs / Billboard.cs** — 点击拾取 / 面向相机（独立 .cs，GUID 可靠）
- **FxFactory.cs** — 气泡/光束/光环特效
- **MatLib.cs** — 材质缓存

### Prefab 引用
- **PrefabRefs.cs** — Inspector 拖入 prefab 引用 + Resources fallback。场景中需有 PrefabRefs GameObject。
- **需要添加新单位类型时**：改 `UnitView.UNIT_PREFABS` 字典 + `PrefabRefs.cs` 字段

### UI
- **HudController.cs / EventLogPanelController.cs / PlaybackControlPanelController.cs / SettlementPanelController.cs**
- Prefab 在 `Assets/Resources/Prefabs/UI/`，通过 `Resources.Load` + `PrefabRefs` fallback 加载

---

## 三、3D 资源与 Prefab 清单

### 角色 Prefab（Humanoid）

```
Assets/Resources/Prefabs/Units/
├── Worker.prefab      # type 6 — Barbarian + Axe 武器 + TeamColorApplicator
├── Pioneer.prefab     # type 7 — Rogue + Sword 武器 + TeamColorApplicator
├── OfficerNPC.prefab  # tile 9 — Ranger（面朝 135°）
└── VendorNPC.prefab   # tile 8 — Mage（面朝 -45°）
```

### 野兽 Prefab（Humanoid）

```
Assets/Resources/Prefabs/Beasts/
├── Beast_11.prefab  # Skeleton_Minion
├── Beast_12.prefab  # Skeleton_Mage
├── Beast_13.prefab  # Skeleton_Warrior
└── Beast_14.prefab  # Skeleton_Rogue（BOSS，红色自发光）
```

### 建筑 Prefab（静态，双色切换）

```
Assets/Resources/Prefabs/Buildings/
├── Base.prefab   # type 4 — Visual/Model_Red + Model_Blue
├── Tower.prefab  # type 3 — 同上
└── Wall.prefab   # type 5 — 无队伍色
```

### 动画 Controller

```
Assets/Resources/Animations/
├── Adventurer_AnimatorController.controller  # Worker/Pioneer/NPC 用
└── Skeleton_AnimatorController.controller    # Beast 用
```

结构相同：**Idle ↔ Walk**（isMoving bool）、**Attack**（onAttack trigger）、**Death**（onDeath trigger）

### 第三方素材

```
Assets/KayKit_Skeletons_1.1_FREE/       # 4 个骷髅兵 (Minion/Mage/Warrior/Rogue)
Assets/KayKit_Adventurers_2.0_FREE/     # 6 个冒险者 (Barbarian/Rogue/Rogue_Hooded/Knight/Ranger/Mage)
Assets/KayKit_Medieval_Hexagon_Pack_1.0_FREE/  # 中世纪建筑 (red/blue/neutral)
```

**所有 FBX 均已设为 Humanoid 类型**，loopTime=true，Avatar 已绑定。

---

## 四、关键设计决策

### UnitView 创建路由

```
Create(state, parent)
├── types 3-9  → UNIT_PREFABS dict → Resources.Load → ConfigureFromUnitPrefab()
├── types 11-14 → Beast_XX.prefab → ConfigureFromBeastPrefab()
└── 其他       → new GameObject → Build()（Sprite/Box fallback）
```

### 移动与动画（2026-08-10 修复）

1. **位置插值**：`ReplayPlayer.Update()` 用 smoothstep 在 moveFrom→moveTo 之间插值 `state.pos`
2. **动画驱动**：`UnitView.LateUpdate()` 读 `state.moving` 做主驱动力 + 帧间位移兜底
3. **`applyRootMotion = false`**：所有 Prefab 的 Animator 均已禁用，代码完全控制 transform
4. **动画速度**：`UnitView.AnimatorSpeed` 静态字段由 ReplayPlayer 每帧同步 `SPEEDS[speedIndex]`

### 建筑队伍颜色

`ConfigureFromUnitPrefab()` 内：`defender` → 激活 Model_Blue，`challenger` → 激活 Model_Red。不再有单独的 `ConfigureFromBuildingPrefab()` 方法（已删除）。

### 相机手动位置

- 模式 2（A队）：`pos=(-5.5, 5.5, -10)` `rot=(25, 0, 0)`
- 模式 3（B队）：`pos=(5.6, 6.5, -15)` `rot=(25, 0, 0)`

---

## 五、常见修改场景指南

| 想做什么 | 需要改的文件 | 复杂度 |
|---------|-------------|:---:|
| 换单个角色模型外观 | 重新生成对应 Prefab（同路径同名），代码不改 | 低 |
| 添加新单位类型 | `UnitView.UNIT_PREFABS` + `PrefabRefs.cs` + 新 Prefab | 中 |
| 换整套素材包 | 全部 Prefab 重建 + FBX Import Settings + AnimatorController clip 引用 | 高 |
| 调整相机机位 | `ReplayCameraRig.cs` Inspector 值 / `teamAPos` `teamBPos` | 低 |
| 调整移动速度/插值 | `ReplayPlayer.cs` 的 `baseRoundDuration` / `RoundDur` | 低 |
| 调整动画过渡 | 编辑 AnimatorController（Unity Editor 操作） | 中 |
| 改队伍颜色 | `TeamColorApplicator.cs` | 低 |
| 改 NPC 站位/朝向 | `SceneBuilder.cs` tile 8/9 处理 + `UnitView.cs` `ConfigureFromUnitPrefab` | 低 |

---

## 六、GUID / Prefab 创建须知

### 核心教训
- **每个自定义 MonoBehaviour 必须在独立 .cs 文件中**（Pickable/Billboard 已从 UnitView.cs 拆分）
- Prefab 创建必须用 `manage_prefabs create_from_gameobject`（场景法），不能用 `execute_code` + `SaveAsPrefabAsset`
- PrefabRefs 是场景对象，Unity 重启后可能丢失；确保场景已保存或 `ReplayEntry.AutoBoot` 自动创建

### CodeDom 限制
`execute_code` 默认编译器不支持：`var`、`?.`、`??`、元组、局部函数。类型必须全限定（`UnityEngine.Object` 而非 `Object`）。

---

## 七、近期改动记录

| 日期 | 改动 | 文件 |
|------|------|------|
| 2026-08-10 | 🔥动画僵死Bug根除：loopTime SerializedObject物理持久化(52 clips) + canTransitionToSelf清零(12 transitions) | 4 FBX + 2 Controller |
| 2026-08-10 | 步幅对齐系统：Velocity-Based Stride Matching + 昼夜自愈 | UnitView.cs, ReplayPlayer.cs |
| 2026-08-10 | 动画修复：applyRootMotion=false、isMoving 改用 state.moving、_animator.speed 同步倍速 | UnitView.cs, ReplayPlayer.cs |
| 2026-08-10 | 代码精简：删除 5 个死方法 + 2 个死字段，UnitView 831→639 行 | UnitView.cs |
| 2026-08-10 | 相机手动位置配置：mode2=(-5.5,5.5,-10) mode3=(5.6,6.5,-15) | ReplayCameraRig.cs |
| 2026-08-10 | 建筑队伍色修复：Model_Red/Blue toggle 移入 ConfigureFromUnitPrefab | UnitView.cs |
| 2026-08-10 | 角色模型替换：Pioneer→Rogue, Officer→Ranger, Vendor→Mage | 各角色 Prefab |
| 2026-08-10 | Humanoid rig 全量 retargeting：14 个 FBX + loopTime + Avatar 绑定 | FBX Import Settings |
| 2026-08-10 | Worker/Pioneer/OfficerNPC/VendorNPC 3D 角色 Prefab 创建（含武器/染色/Avatar） | 4 个 Prefab + TeamColorApplicator.cs |
| 2026-08-09 | 相机系统：35°电影视角 + CameraManager 双模式 | ReplayCameraRig.cs, CameraManager.cs |
| 2026-08-08 | GUID 根因修复：Pickable/Billboard 拆分为独立 .cs | Pickable.cs, Billboard.cs |
| 2026-08-07 | 4 Beast Prefab + AnimatorController 重建 | Beast_11~14.prefab |
| 2026-08-07 | 3D 建筑 Prefab（Base/Tower/Wall） | Base/Tower/Wall.prefab |

---

## 八、3D 模型实操经验沉淀

> **以下是从多轮试错中沉淀的操作手册。新会话在创建/替换 Prefab 之前必须先读本节。**

### 8.1 新建一个 3D 角色 Prefab 的完整流程

**前置条件：FBX 已导入，AnimationType=Humanoid，Avatar 已 Configure，loopTime 已设。**

```
Step 1: 在场景中搭建 GameObject 层级
Step 2: 用 manage_prefabs create_from_gameobject 保存为 .prefab
Step 3: 用 manage_prefabs get_hierarchy 验证组件完整性
Step 4: 用 manage_asset delete 删除旧的，manage_asset search 获取新 GUID
Step 5: 把 Prefab 拖入 PrefabRefs Inspector 对应字段（或 Resources.Load 路径匹配）
```

**标准 Prefab 层级结构：**

```
UnitName (root)
├── UnitView 组件                    # 主控制器
├── Body (空节点)
│   └── [FBX模型实例]               # 从 FBX 拖入，含 SkinnedMeshRenderer
│       └── Animator 组件            # applyRootMotion=false
├── HpBar (Quad, Billboard)          # 黑底血条，shadowCastingMode=Off
├── HpFill (Quad, Billboard)         # 绿色填充，shadowCastingMode=Off
└── SelRing (Quad, Billboard, inactive默认)  # 选择圈，localRotation=(90,0,0)
```

### 8.2 替换已有 Prefab 的模型（不重建）

适用场景：Worker 从 Barbarian 换成 Knight，动画和结构不变。

1. 在 Unity Editor 中打开 Prefab
2. 删除 Body 下的旧 FBX 实例
3. 从 Project 窗口拖入新 FBX 到 Body 下
4. **关键：把 Animator 组件的 Avatar 换成新 FBX 的 Avatar**（点 Animator 组件 → Avatar 字段右侧圆圈 → 选新模型的 Avatar）
5. AnimatorController 不动 — Humanoid 自动重定向
6. 如果新模型带了武器骨骼，把武器子节点重新拖到 `handslot.r` 下
7. 保存 Prefab

### 8.3 FBX 导入设置检查清单

每个用作角色的 FBX 必须逐项确认：

| 设置项 | 正确值 | 位置 |
|--------|--------|------|
| AnimationType | **Humanoid** | Rig 标签页 |
| Avatar Definition | CreateFromThisModel | Rig 标签页 |
| Configure 骨骼映射 | 全绿（无红色 Missing） | Rig → Configure… 按钮 |
| loopTime | **true**（Idle、Walking、Running） | Animation 标签页 → 选中 clip |
| loopTime | **false**（Death、Hit、Attack） | 同上 |
| Scale Factor | 1（KayKit 角色） / 0.01（KayKit 建筑） | Model 标签页 |

**⚠️ Humanoid 重导入会重置 loopTime**：改 FBX 从 Generic 到 Humanoid 后，所有 AnimationClip 的 loopTime 会被 Unity 重置为默认值。必须用 `AnimationUtility.GetAnimationClipSettings` / `SetAnimationClipSettings` 重新设回。

### 8.4 AnimatorController 创建要点

两个 Controller 结构一致，区别仅在于绑定的 AnimationClip 来源不同：

```
参数: isMoving(Bool), onAttack(Trigger), onDeath(Trigger)

Idle (默认状态) ←→ Walk
  过渡: hasExitTime=false, duration=0.15s, condition=isMoving(true/false)

Any State → Attack
  过渡: hasExitTime=false, condition=onAttack

Any State → Death
  过渡: hasExitTime=false, condition=onDeath
```

**编程创建时注意**（如果用 execute_code 生成 .controller）：
- CodeDom 不支持 `ModelImporterAnimationType.Humanoid` 枚举名，用数字 `(UnityEditor.ModelImporterAnimationType)3`
- `AddTransition` 在同一对状态间只能调用一次，重复调用会创建重复过渡 → 先遍历 `RemoveTransition` 再 `AddTransition`
- `AnimatorStateTransition.exitTime` 只在 `hasExitTime=true` 时生效

### 8.5 Weapon（武器）挂载

- 武器 GameObject 放在 Body/FBX实例/root/handslot.r 下
- `handslot.r` 在 KayKit Adventurer 骨骼中是右手持武器骨骼
- localPosition/localRotation 需手动调整到合适握持位置

### 8.6 TeamColorApplicator 染色

```
TeamColorApplicator（挂载在 FBX 实例或 Body 上）
├── 需要引用 unitView（自动从父级 GetComponentInParent）
├── challenger → Color(1, 0.55, 0.55) 淡红
├── defender  → Color(0.55, 0.65, 1) 淡蓝
└── 通过 MaterialPropertyBlock._Color 染色所有子 Renderer
```

### 8.7 常见坑

| 坑 | 现象 | 解法 |
|----|------|------|
| Animator.applyRootMotion 未关 | 角色抖动/瞬移/位置偏移 | `_animator.applyRootMotion = false` |
| Avatar 未绑定 | 角色 T-Pose 不动 | Animator.Avatar = FBX 自带的 Avatar |
| AnimatorController clip 引用丢失 | 动画状态为 None (Motion) | 重新拖入 clip 或编程重建 Controller |
| FBX Scale Factor 不对 | 建筑巨大如摩天楼或微小如米粒 | KayKit 建筑用 0.01，角色用 1 |
| loopTime 为 false | 角色迈一步就冻在分腿姿势上滑行到底（动画不循环） | ⚠️ 见下方 8.7.1 专项修复方案 |
| Animator 组件在错误的节点 | 动画不播放 | Animator 必须在 FBX 根节点上 |
| Prefab 的 AnimatorController 丢失 | Play 后无动画 | 确认 Controller 在 `Assets/Resources/` 下，用 `Resources.Load` 路径 |

### 8.7.1 🔥 "迈一步就僵死滑行" Bug 根因与修复（2026-08-10）

**现象**：角色进入 Walk 状态后迈出第一步就冻在分腿姿势上，像个雕像一样平移滑行到终点，双腿完全不会交替踩踏。

**两个铁证根因**：

#### 根因 1：`loopTime` 未物理持久化到磁盘

FBX 动画 Clip 的 `loopTime` 在 Unity Editor 中通过 API 设置后，**必须用 SerializedObject 直写 FBX 元数据 + `SaveAndReimport()` 物理保存**。仅调用 `importer.clipAnimations = clips` + `SaveAndReimport()` 不够 — `defaultClipAnimations` 是只读的 FBX 原始数据，Play 模式启动时 Unity 会从磁盘重新读取，把内存中的临时修改全部覆盖。

**✅ 正确的物理保存代码**：

```csharp
ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
SerializedObject so = new SerializedObject(importer);
SerializedProperty clipAnimations = so.FindProperty("m_ClipAnimations");

for (int i = 0; i < clipAnimations.arraySize; i++)
{
    SerializedProperty clip = clipAnimations.GetArrayElementAtIndex(i);
    string clipName = clip.FindPropertyRelative("name").stringValue;
    // Idle/Walking/Running/Interact/Spawn → loopTime = true
    // Death/Hit/Jump/PickUp/Throw/T-Pose → loopTime = false
    if (ShouldLoop(clipName))
    {
        clip.FindPropertyRelative("loopTime").boolValue = true;
        clip.FindPropertyRelative("loopPose").boolValue = true;
    }
}

so.ApplyModifiedPropertiesWithoutUndo();  // ← 关键！
importer.SaveAndReimport();                // ← 必须有
```

**验证方法**：`AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate)` 后再读取 loopTime，确认未回弹。

#### 根因 2：AnimatorController 的 `canTransitionToSelf` 全部为 True

Any State → Attack/Death/Hit 等过渡线如果 `canTransitionToSelf = true`，在 Walk 状态下任何参数微小抖动（或 trigger 残留）都会导致状态机**每帧重新进入 Walk 的第 0 帧**，动画永远播不完第一步。

**✅ 修复**：对所有 Transition（Any State 和普通 State 的）执行：

```csharp
foreach (var t in stateMachine.anyStateTransitions)
    t.canTransitionToSelf = false;
foreach (var s in stateMachine.states)
    foreach (var t in s.state.transitions)
        t.canTransitionToSelf = false;
EditorUtility.SetDirty(controller);
AssetDatabase.SaveAssets();
```

**受影响的资产（4 个 FBX + 2 个 Controller）**：
- `Assets/KayKit_Adventurers_2.0_FREE/Animations/fbx/Rig_Medium/Rig_Medium_General.fbx` (15 clips)
- `Assets/KayKit_Adventurers_2.0_FREE/Animations/fbx/Rig_Medium/Rig_Medium_MovementBasic.fbx` (11 clips)
- `Assets/KayKit_Skeletons_1.1_FREE/Animations/fbx/Rig_Medium/Rig_Medium_General.fbx` (15 clips)
- `Assets/KayKit_Skeletons_1.1_FREE/Animations/fbx/Rig_Medium/Rig_Medium_MovementBasic.fbx` (11 clips)
- `Assets/Resources/Animations/Adventurer_AnimatorController.controller` (7 transitions)
- `Assets/Resources/Animations/Skeleton_AnimatorController.controller` (5 transitions)

**教训**：以后任何"改了 API 但 Play 模式又回去了"的问题，都先用 SerializedObject 直写 + ForceUpdate 验证持久化。

### 8.8 野兽 Prefab 特殊处理

- Beast 走 `ConfigureFromBeastPrefab()` 路径，与 Worker/Pioneer 不同
- Beast_14（BOSS）额外需要红色自发光：`_EmissionColor = (0.6, 0, 0, 1)` 通过 MaterialPropertyBlock 设置
- Beast 使用 `Skeleton_AnimatorController`，角色使用 `Adventurer_AnimatorController`
- `_lockRotation = false`（允许转身），Worker/Pioneer 同；NPC/Building 为 true

---

## 九、相关文档索引

| 文档 | 何时读 |
|------|--------|
| [replay格式文档.md](replay格式文档.md) | 需要理解 JSONL 数据结构时 |
| [CameraManager实现文档.md](CameraManager实现文档.md) | 需要修改相机/导演系统时 |
| [任务书.md](任务书.md) | 需要理解游戏规则时 |
| [plan.md](plan.md) | 回顾原始开发计划时 |
| [WildernessReplay开发任务拆解.md](WildernessReplay开发任务拆解.md) | 查看历史任务拆解时 |
