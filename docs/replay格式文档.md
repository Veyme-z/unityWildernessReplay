# replay 数据结构分析文档

## 概述

回放文件为 **JSONL** 格式（每行一个独立的 JSON 对象），用于记录对局的完整回放数据。包含 4 种类型的事件记录：

| 类型 `type` | 说明                             | 出现位置              |
| ----------- | -------------------------------- | --------------------- |
| `"start"`   | 游戏开局信息，定义地图和初始角色 | 第 1 行（仅 1 行）    |
| `"round"`   | 每回合快照数据                   | 第 2 ~ N-1 行（多条） |
| `"finish"`  | 游戏结束结算                     | 倒数第 2 行           |
| `"valid"` / `"invalid"` | 对局有效性标记（纯文本行，非 JSON） | 最后 1 行（仅 1 行） |

---

## 一、类型 `start`

### 1.1 顶层结构

| 字段    | 类型       | 说明                                                         |
| ------- | ---------- | ------------------------------------------------------------ |
| `type`  | `string`   | 固定值 `"start"`                                             |
| `map`   | `object`   | 地图配置信息                                                 |
| `roles` | `object`   | 初始角色配置，key 为角色名（如 `"tower"`、`"base"`、`"pioneer"`、wall、blackBear、skeletonMage、deathWarrior、taskOfficer、vendor、weaponShop），value 为该角色的属性对象，3个NPC的血量为0就行 |
| `teams` | `object[]` | 队伍列表，每个队伍包含 `roles`、`task`、`allTaskInfo` 等，结构见 [2.3 teams](#24-teams-队伍) |

### 1.2 `map` 对象

| 字段      | 类型        | 说明                                                         |
| --------- | ----------- | ------------------------------------------------------------ |
| `mapName` | `string`    | 地图名称                                                     |
| `width`   | `integer`   | 地图宽度（格子数）                                           |
| `height`  | `integer`   | 地图高度（格子数）                                           |
| `data`    | `integer[]` | 一维数组，长度 = `width × height`，行主序存储每个格子的瓦片类型编号 |

### 1.3 `data` 瓦片类型编号

| 编号 | 名称                                              | 说明           |
| ---- | ------------------------------------------------- | -------------- |
| `0`  | 空地                                              | 可通行空白格子 |
| `3`  | 防御塔 (tower)                                    | 防御建筑       |
| `4`  | 基地 (station)                                    | 出生点/大本营  |
| `5`  | 围墙（wall）                                      | 围墙           |
| `6`  | 工人（worker）                                    | 工人           |
| `7`  | 开拓者 (pioneer)                                  | 开拓者单位     |
| 8    | 任务官taskOfficer                                 |                |
| 9    | 小贩vendor                                        |                |
| 10   | 武器商店weaponShop                                |                |
| `11` | 黑熊 (blackBear) --对应judger的smallBeast         | 野兽怪物       |
| `12` | 骷髅法师 (skeletonMage) --对应judger的midlleBeast | 野兽怪物       |
| `13` | 死亡战士 (deathWarrior) --对应judger的largeBeast  | 野兽怪物       |
| `14` | 骑兵 (cavalry) --对应judger的bossBeast            | 野兽怪物       |

### 1.4 `start` 完整 JSON 样例

```json
{
  "type": "start",
  "map": {
    "mapName": "attack_map",
    "width": 41,
    "height": 32,
    "data": [0,0,0,1,1,1,0,2,2,0,0,0,5,0,1,1,0,3,0,0,0,0,0,1,1,0,0,2,2,2,1,1,0,0,0,0,0,1,1,1,1,  ...]
  },
  "roles": {
    "tower": {
      "mapType": 3,
      "health": 600,
      "attackPower": 20,
    },
    "pioneer": {
      "mapType": 7,
      "health": 200,
      "attackPower": 0,
    }
  },
  "teams": [
    {
      "type": "challenger",
      "teamId": "3886",
      "teamName": "队队队",
      "llmResp": null,
      "prompt": null,
      "playerReq": null,
      "systemResp": null,
      "goldNum": 0,
      "totalScore": 0,
      "completeTaskCount": 0,
      "invalidTaskCount": 0,
      "task": {
        "taskType": "",
        "description": "",
        "shortcut": "",
        "level": "",
        "answer": "",
        "reward": 0,
        "isTaskComplete": false,
        "taskStatus": [],
        "roundCost": 0,
        "pos": null
      },
      "allTaskInfo": {
        "ragTask": [0],
        "dbTask": [0, 0, 0],
        "codeReviewTask": [0, 0],
        "codeGenerateTask": [0, 0],
        "osTask": [0, 0, 0]
      },
      "roles": [
        {
          "id": 10010,
          "pos": {"x": 34, "y": 29},
          "roleType": 7,
          "health": 200,
          "attackPower": 0,
          "inControl": false,
          "roadLineType": "",
          "level": 1,
          "talk": null,
          "commands": [],
          "taskPlayer": false
        }
      ]
    },
    {
      "type": "defender",
      "teamId": "3980",
      "teamName": "国一wallE斯国一",
      "llmResp": null,
      "prompt": null,
      "playerReq": null,
      "systemResp": null,
      "goldNum": 0,
      "totalScore": 0,
      "completeTaskCount": 0,
      "invalidTaskCount": 0,
      "task": {
        "taskType": "",
        "description": "",
        "shortcut": "",
        "level": "",
        "answer": "",
        "reward": 0,
        "isTaskComplete": false,
        "taskStatus": [],
        "roundCost": 0,
        "pos": null
      },
      "allTaskInfo": {
        "ragTask": [0],
        "dbTask": [0, 0, 0],
        "codeReviewTask": [0, 0],
        "codeGenerateTask": [0, 0],
        "osTask": [0, 0, 0]
      },
      "roles": [
        {
          "id": 20010,
          "pos": {"x": 1, "y": 2},
          "roleType": 7,
          "health": 200,
          "attackPower": 0,
          "inControl": false,
          "roadLineType": "",
          "level": 1,
          "talk": null,
          "commands": [],
          "taskPlayer": false
        },
        {
          "id": 31001,
          "pos": {"x": 1, "y": 2},
          "roleType": 11,
          "health": 200,
          "attackPower": 0,
          "inControl": false,
          "roadLineType": "",
          "level": 1,
          "talk": null,
          "commands": [],
          "taskPlayer": false
        }
      ]
    }
  ]
}
```

---

## 二、类型 `round`

### 2.1 顶层结构

| 字段        | 类型       | 说明                    |
| ----------- | ---------- | ----------------------- |
| `type`      | `string`   | 固定值 `"round"`        |
| `round`     | `integer`  | 当前回合编号，从 1 递增 |
| `resources` | `object[]` | 地图上的资源点列表      |
| `news`      | `object[]` | 本回合新闻/事件通知列表 |
| `teams`     | `object[]` | 参与对局的队伍列表      |

### 2.2 `resources` 资源点

| 字段      | 类型     | 说明                               |
| --------- | -------- | ---------------------------------- |
| `pos`     | `object` | 坐标 `{"x": number, "y": number}`  |
| `resName` | `string` | 资源名称：`"石头"`、`"铁"`、`"铜"` |

### 2.3 `news` 新闻/事件

| 字段 | 类型       | 说明                                                         |
| ---- | ---------- | ------------------------------------------------------------ |
| news | `object[]` | 新闻事件列表，记录关键事件（击杀、任务完成等），通常为 JSON 对象数组 |

### 2.4 `teams` 队伍

| 字段                | 类型              | 说明                                                        |
| ------------------- | ----------------- | ----------------------------------------------------------- |
| `type`              | `string`          | 队伍类型：`"challenger"`（挑战者）或 `"defender"`（防守方） |
| `teamId`            | `string`          | 队伍唯一 ID                                                 |
| `teamName`          | `string`          | 队伍名称                                                    |
| `llmResp`           | `string` / `null` | LLM 模型原始响应                                            |
| `prompt`            | `string` / `null` | 发给模型的 prompt                                           |
| `playerReq`         | `string` / `null` | 玩家向系统发出的请求                                        |
| `systemResp`        | `string` / `null` | 系统返回给玩家的响应                                        |
| `goldNum`           | `integer`         | 当前金币数量                                                |
| `totalScore`        | `integer`         | 累计总分                                                    |
| `completeTaskCount` | `integer`         | 已完成任务数量                                              |
| `invalidTaskCount`  | `integer`         | 无效/失败任务数量                                           |
| `task`              | `object`          | 当前任务信息                                                |
| `allTaskInfo`       | `object`          | 所有任务的进度信息                                          |
| `roles`             | `object[]`        | 该队伍的角色/单位列表，需要包括攻击不同阵营的野兽           |

### 2.5 `task` 任务对象

| 字段             | 类型              | 说明                                         |
| ---------------- | ----------------- | -------------------------------------------- |
| `taskType`       | `string`          | 任务类型，如 `"问题检视类"`                  |
| `description`    | `string`          | 任务详细描述                                 |
| `shortcut`       | `string`          | 任务简述/快捷提示                            |
| `level`          | `string`          | 任务难度等级，如 `"l3"`                      |
| `answer`         | `string`          | 正确答案（JSON 字符串）                      |
| `reward`         | `integer`         | 任务奖励分数                                 |
| `isTaskComplete` | `boolean`         | 任务是否已完成                               |
| `taskStatus`     | `array`           | 任务状态列表                                 |
| `roundCost`      | `integer`         | 已消耗回合数                                 |
| `pos`            | `object` / `null` | 任务所在坐标 `{"x", "y"}`，无任务时为 `null` |

### 2.6 `allTaskInfo` 任务进度（待定）

| 字段               | 类型         | 说明                            |
| ------------------ | ------------ | ------------------------------- |
| `ragTask`          | `integer[]`  | RAG 任务进度                    |
| `dbTask`           | `integer[3]` | 数据库任务进度                  |
| `codeReviewTask`   | `integer[2]` | 代码审查任务进度 `[剩余, 总数]` |
| `codeGenerateTask` | `integer[2]` | 代码生成任务进度                |
| `osTask`           | `integer[3]` | OS/系统任务进度                 |

### 2.7 `roles` 角色

| 字段          | 类型              | 说明                                                         |
| ------------- | ----------------- | ------------------------------------------------------------ |
| `id`          | `integer`         | 角色唯一 ID                                                  |
| `pos`         | `object`          | 当前坐标 `{"x": number, "y": number}`                        |
| `roleType`    | `integer`         | 角色类型编号（3=防御塔，4=基地，7=开拓者， 5=围墙， 6=工人， 野兽11~14，攻击challenger的野怪放到challenger中，同理defender） |
| `health`      | `integer`         | 当前血量                                                     |
| `attackPower` | `integer`         | 攻击力                                                       |
| `inControl`   | `boolean`         | 是否被控制/眩晕                                              |
| `talk`        | `string` / `null` | 角色对话/发言内容                                            |
| `commands`    | `object[]`        | 本回合执行的动作指令列表                                     |
| `taskPlayer`  | `boolean`         | 是否为任务执行玩家                                           |
| `backpacks`   | `array`           | 背包物品列表                                                 |

### 2.8 `commands` 动作指令

| 字段             | 类型      | 说明                                    |
| ---------------- | --------- | --------------------------------------- |
| `action`         | `string`  | 动作类型，见下表                        |
| `targetName`     | `string`  | 物品类型，详见2.9章武器商店购买内容清单 |
| `targetPos`      | `object`  | 目标坐标 `{"x": number, "y": number}`   |
| `skillTargetPos` | `array`   | 技能目标位置（通常为空数组）            |
| `taskAnswer`     | `string`  | 任务回答内容（提交答案时携带）          |
| `valid`          | `boolean` | 本次动作是否有效                        |
| `queryInfo`      | `string`  | 查询/交互信息                           |

#### 动作类型

| `action`         | 说明     |
| ---------------- | -------- |
| `"move"`         | 移动     |
| `"attack"`       | 攻击     |
| `"sell"`         | 贩卖     |
| `"buy"`          | 购买     |
| `"build"`        | 建造     |
| `"executeTask"`  | 执行任务 |
| `"submitAnswer"` | 提交答案 |
| `"detect"`       | 探索     |
| `"use"`          | 使用     |
| `"drop"`         | 丢弃     |
| `"collect"`      | 采集     |

### 2.9 武器商店购买内容清单

详情请见任务书以及response.json中的举例

### 2.10 `round` 完整 JSON 样例

注：

- 1个id理论上只能出现一次，这里写了多次是为了举例不同action下的格式
- 2.8章`commands` 动作指令的所有字段都会出现在replay.txt中，没用到的key只会传key，不会传value

```json
{
  "type": "round",
  "round": 1,
  "resources": [
    { "pos": {"x": 15, "y": 8},  "resName": "石头"},
    { "pos": {"x": 25, "y": 12}, "resName": "铁"},
    { "pos": {"x": 30, "y": 20}, "resName": "铜"}
  ],
  "news": [],
  "teams": [
    {
      "type": "challenger",
      "teamId": "3886",
      "teamName": "队队队",
      "llmResp": null,
      "prompt": null,
      "playerReq": null,
      "systemResp": null,
      "goldNum": 0,
      "totalScore": 0,
      "completeTaskCount": 0,
      "invalidTaskCount": 0,
      "task": {
        "taskType": "",
        "description": "",
        "shortcut": "",
        "level": "",
        "answer": "",
        "reward": 0,
        "isTaskComplete": false,
        "taskStatus": [],
        "roundCost": 0,
        "pos": null
      },
      "allTaskInfo": {
        "ragTask": [0],
        "dbTask": [0, 0, 0],
        "codeReviewTask": [0, 0],
        "codeGenerateTask": [0, 0],
        "osTask": [0, 0, 0]
      },
      "roles": [
        {
          "id": 10010,
          "pos": {"x": 34, "y": 29},
          "roleType": 6,
          "health": 200,
          "attackPower": 0,
          "inControl": false,
          "talk": null,
          "commands": [
            {
              "action": "move",
              "targetPos": {"x": 34, "y": 28},                
              "valid": true
            }
          ],
          "taskPlayer": false,
          "backpacks": []
        },
        {
          "id": 10011,
          "pos": {"x": 34, "y": 29},
          "roleType": 7,
          "health": 200,
          "attackPower": 0,
          "inControl": false,
          "talk": null,
          "commands": [
            {
              "action": "buy",
              "targetName": "UpgradeTowerAttack/UpgradeTowerMaxHp/UpgradeWallMaxHp/UpgradeStationMaxHp/FlameBreath/FrostPotion/ThornAmulet/IronWhistle",
              "valid": true
            }
          ],
          "taskPlayer": false,
          "backpacks": []
        },
        {
          "id": 10011,
          "pos": {"x": 34, "y": 29},
          "roleType": 7,
          "health": 200,
          "attackPower": 0,
          "inControl": false,
          "talk": null,
          "commands": [
            {
              "action": "use",
              "targetName": "Medicine/SmallBeastSummonOrder/AcientTablet",
              "valid": true
            }
          ],
          "taskPlayer": false,
          "backpacks": []
        },
        {
          "id": 10011,
          "pos": {"x": 34, "y": 29},
          "roleType": 7,
          "health": 200,
          "attackPower": 0,
          "inControl": false,
          "talk": null,
          "commands": [
            {
              "action": "use",
              "targetName": "WallFixer",
              "targetPos": {
                "x": 29,
                "y": 7
              },
              "valid": true
            }
          ],
          "taskPlayer": false,
          "backpacks": []
        },
        {
          "id": 10011,
          "pos": {"x": 34, "y": 29},
          "roleType": 7,
          "health": 200,
          "attackPower": 0,
          "inControl": false,
          "talk": null,
          "commands": [
            {
              "action": "use",
              "targetName": "/DizzyWeapon/Bomb",
              "skillTargetPos": [
                  {
                    "x": 29,
                    "y": 7
                  },
                  {
                    "x": 30,
                    "y": 7
                  },
                  {
                    "x": 31,
                    "y": 7
                  },
                  {
                    "x": 29,
                    "y": 8
                  },
                  {
                    "x": 30,
                    "y": 8
                  },
                  {
                    "x": 31,
                    "y": 8
                  },
                  {
                    "x": 29,
                    "y": 9
                  },
                  {
                    "x": 30,
                    "y": 9
                  },
                  {
                    "x": 31,
                    "y": 9
                  },
              ],
              "valid": true
            }
          ],
          "taskPlayer": false,
          "backpacks": []
        },
        {
          "id": 10012,
          "pos": {"x": 34, "y": 29},
          "roleType": 6,
          "health": 200,
          "attackPower": 0,
          "inControl": false,
          "talk": null,
          "commands": [
            {
              "action": "sell",
              "targetName": "stone/iron/copper",
              "valid": true
            }
          ],
          "taskPlayer": false,
          "backpacks": []
        },
       {
          "id": 10012,
          "pos": {"x": 34, "y": 29},
          "roleType": 6,
          "health": 200,
          "attackPower": 0,
          "inControl": false,
          "talk": null,
          "commands": [
            {
              "action": "build",
              "targetName": "wall",
              "valid": true
            }
          ],
          "taskPlayer": false,
          "backpacks": []
        },
        {
          "id": 10012,
          "pos": {"x": 34, "y": 29},
          "roleType": 6,
          "health": 200,
          "attackPower": 0,
          "inControl": false,
          "talk": null,
          "commands": [
            {
              "action": "remove",
              "targetPos": {
                "x": 29,
                "y": 7
              },
              "valid": true
            }
          ],
          "taskPlayer": false,
          "backpacks": []
        },          
        {
          "id": 10012,
          "pos": {"x": 34, "y": 29},
          "roleType": 6,
          "health": 200,
          "attackPower": 0,
          "inControl": false,
          "talk": null,
          "commands": [
            {
              "action": "collect",
              "targetPos": {
                "x": 29,
                "y": 7
              },
              "valid": true
            }
          ],
          "taskPlayer": false,
          "backpacks": []
        },
        {
          "id": 10011,
          "pos": {"x": 34, "y": 29},
          "roleType": 7,
          "health": 200,
          "attackPower": 0,
          "inControl": false,
          "talk": null,
          "commands": [
            {
              "action": "executeTask",
              "valid": true
            }
          ],
          "taskPlayer": false,
          "backpacks": []
        },
        {
          "id": 10011,
          "pos": {"x": 34, "y": 29},
          "roleType": 7,
          "health": 200,
          "attackPower": 0,
          "inControl": false,
          "talk": null,
          "commands": [
            {
              "action": "submitAnswer",
              "taskAnswer": "Task answer xxx",
              "valid": true
            }
          ],
          "taskPlayer": false,
          "backpacks": []
        },
        {
          "id": 10011,
          "pos": {"x": 34, "y": 29},
          "roleType": 7,
          "health": 200,
          "attackPower": 0,
          "inControl": false,
          "talk": null,
          "commands": [
            {
              "action": "detect",
              "queryInfo": "Detect something xxx",
              "valid": true
            }
          ],
          "taskPlayer": false,
          "backpacks": []
        },
        {
          "id": 10013,
          "pos": {"x": 34, "y": 29},
          "roleType": 3,
          "health": 200,
          "attackPower": 20,
          "inControl": false,
          "talk": null,
          "commands": [
            {
              "action": "attack",
              "targetPos": {
                "x": 29,
                "y": 7
              },
              "valid": true
            }
          ],
          "taskPlayer": false,
          "backpacks": []
        }
      ]
    }
  ]
}
```

---

## 三、类型 `finish`

### 3.1 顶层结构

| 字段      | 类型       | 说明                 |
| --------- | ---------- | -------------------- |
| `type`    | `string`   | 固定值 `"finish"`    |
| `players` | `object[]` | 各队伍的最终结算数据 |

### 3.2 `players` 元素

| 字段         | 类型      | 说明                               |
| ------------ | --------- | ---------------------------------- |
| `teamId`     | `string`  | 队伍 ID                            |
| `teamName`   | `string`  | 队伍名称                           |
| `result`     | `string`  | 对局结果：`"victory"` / `"defeat"` |
| `glodNum`    | `integer` | 最终金币数                         |
| `totalScore` | `integer` | 最终总分数                         |

### 3.3 `finish` 完整 JSON 样例

```json
{
  "type": "finish",
  "players": [
    {
      "teamId": "3886",
      "teamName": "队队队",
      "result": "defeat",
      "glodNum": 0,
      "totalScore": 973
    },
    {
      "teamId": "3980",
      "teamName": "国一wallE斯国一",
      "result": "victory",
      "glodNum": 4,
      "totalScore": 1490
    }
  ]
}
```

---

## 四、类型 `valid` / `invalid`

### 4.1 概述

该行为**纯文本行（非 JSON）**，固定为 `valid` 或 `invalid` 单字，作为 replay 文件的最后一行，标识本场对局是否有效。

### 4.2 生成逻辑

当对局因异常（如 SQL 评测服务不可用等）导致无效时，判题器输出 `invalid`；正常结束输出 `valid`。对应代码 `ReplayGenerator.valid(boolean)`。

### 4.3 示例

```
valid
```

或

```
invalid
```

---

## 五、数据整体结构示意

```
第 1 行:   {"type":"start", ...}       ← 游戏开始 + 地图 + 初始角色
第 2 行:   {"type":"round","round":1, ...}    ← 第 1 回合快照
第 3 行:   {"type":"round","round":2, ...}    ← 第 2 回合快照
...
第 N 行:   {"type":"round","round":K, ...}    ← 最后回合快照
第 N+1 行: {"type":"finish",...}       ← 结算
末尾行:     valid                        ← 文件结束标记
```

---

## 六、补充说明

- **坐标**：`x` 为列（左→右），`y` 为行（上→下），`(0,0)` 为左上角
- **资源**：`石头`、`铁`、`铜`
- **队伍类型**：`challenger`（挑战者）、`defender`（防守方）
- 角色的 `backpacks` 字段视需要可为空数组 `[]`
