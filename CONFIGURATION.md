# 可配置项与待确认清单

基于基建 v1、基建 v2 和物资系统文档。**“演示默认值”不是已经确定的正式策划数值。**

## 开始游戏的强制规则

- 总人数至少 2 人，必须为偶数；1、3、5、7、9 人都不能开始。
- 开局后红 / 蓝阵营人数严格相等；超额手选阵营的成员会重新分配。
- 房间容量固定为 10 人，这是 v1 的 demo 要求；不要求必须凑满 10 人。
- 只有房主可开始游戏。实际身份校验和同步必须由未来网络层提供，不能信任客户端提交的 `isHost` 布尔值。

## 直接可改的配置

| 项目 | 当前值 | 修改位置 | 约束 / 说明 |
| --- | --- | --- | --- |
| 物资刷新间隔 X | 演示 2 分钟 | `FoundationDemo.unity` → Foundation Demo → Refresh Minutes | ≥0；0 不刷新；正式 X 尚未给出 |
| 点位数量 | 演示 12 | `Assets/Foundation/Resources/SupplyTables.json` → `dropLocations` 数组长度 | 每个点位 ID 唯一 |
| 点位坐标 | 演示平面坐标 | 同上 → `address.x/y/z` | 实际世界坐标，需落在正式地图可拾取区域 |
| 点位名称 / 编号 | `demo_point_*` | 同上 → `name/id` | 不得空 ID，不得重复 |
| 是否掉落的概率 | 演示 1.0 | 同上 → `chance` | 0–1；刷新仅检查空点位，不叠加占用点位 |
| 可掉落类型 | 演示四种全部 | 同上 → `type` 数组 | Food=0 / Water=1 / HighValue=2 / Ordinary=3；必须有对应物资 |
| 单次掉落数量 | 演示每点 1 | 同上 → `number` | 整数 ≥1；总掉落数随成功点数变化 |
| 随机数量范围 | 默认固定数量 | `SupplyPoint.rule.minCount/maxCount` | 运行时 API 支持随机范围；六表 JSON 的 `number` 映射到 min=max |
| 总表 ID / 名称 / 描述 | 演示物资 | 同上 → `supplies.id/name/desc` | ID 唯一；四种子表 `key` 必须关联到对应类型总表行 |
| 物资价值 | 演示 10–600 | 同上 → `value` | 高价值严格 >500；其余 0–500 |
| 图标 | 未配正式图标 | 同上 → `iconResource` | Unity Resources 内 Sprite 路径，无扩展名；未配置则颜色 / 文字占位 |
| 饥饿值变化 | 演示 +20 / -15 | 同上 → `foods.HPchange` | 可负数；HPchange 在原文表示饥饿，不是生命值 |
| 口渴值变化 | 演示 +25 | 同上 → `waters.MPchange` | 可负数；应用到 Hydration（补水程度） |
| 特殊效果说明 | 演示空 | 同上 → `specialDesc` | 普通策划文字；程序不会解释或执行这段文字 |
| 特殊效果实现 | 未接入 | `FoundationDemo.SpecialEffectHandler` | 回调必须返回成功才消耗物品；没有处理器就保留 |
| 可赠送标记 | 演示部分 true | 同上 → `canGift` | 元数据；不代表赠送玩法已经实现 |
| 堆叠上限 | 演示 1 / 5 | 同上 → `maxStack` | 整数 ≥1；背包空间不足时整批不拾取 |
| 移动速度 | 演示 4 米/秒 | `FirstPersonMotor.speed` | 可在运行时 Inspector 修改；正式参数待定 |
| 跳跃高度 | 演示 1.1 米 | `FirstPersonMotor.jumpHeight` | ≥0；正式参数待定 |
| 鼠标灵敏度 | 演示 0.12 | 局内 Settings；`FirstPersonMotor.sensitivity` | 面板范围 0.03–0.4，仅本次运行 |
| 视野 FOV | Unity 相机默认 | 局内 Settings；ViewCamera.fieldOfView | 面板范围 50–100，仅本次运行 |
| 音量 | 当前 AudioListener.volume | 局内 Settings | 范围 0–1；退出演示恢复原音量 |
| 是否全员准备才开局 | 默认 false | 房主 Room settings；`RoomModel.RequireAllReady` | v1 要求 true；v2允许等待阶段随时开始，因此默认 false；人数偶数限制始终有效 |

## 当前需改代码的参数（不是 Inspector 配置）

| 参数 | 当前值 | 位置 | 来源 |
| --- | --- | --- | --- |
| 准备时间 | 10 秒 | `MatchSession.PreparationSeconds` | v2 明确规定 |
| 局内时长 | 1200 秒 | `MatchSession.MatchSeconds` | v2 明确规定 |
| 喜欢 / 厌恶数量 | 1 / 1–3 | `PlayerState` 选择方法 | v2 明确规定；两类互斥 |
| 背包总槽数 | 演示 20 | `FoundationDemo.Awake` 中 `new Inventory(20)` | 正式容量待定 |
| 快捷槽数量 | 演示 5 | `FoundationDemo.HotbarSize` 与数字键列表 | 改动必须同步 HUD 和输入绑定 |
| 拾取距离 / 按键 | 演示 3 米 / E | `FoundationDemo.TryPickup` 与 Update | 物资文档开头仍在询问拾取方式 |
| 初始饥饿 / 补水程度 | 演示 100 / 100 | `PlayerState` | 没有自然衰减规则，只有使用效果 |
| 补选厌恶数量 | 演示最少 1 | `PlayerState.CompletePreferences` | v2 流程图要求缺项自动补齐；随机数量范围未明确 |
| 名字上限 | 演示 32 字符 | `RoomModel.ValidName` 和 `FoundationHud.InputAt` | 非正式审核规则；禁控制字符，禁富文本解析 |
| 聊天条数 / 每条长度 | 演示 6 条 / 200 字符 | `RoomModel.SendChat` | v1 要求新消息顶掉最旧消息，不手动滚动；具体条数未定 |
| 小地图范围 | 演示半径 12 米 | `FoundationDemo.BuildWorld` 中 orthographicSize | 待正式地图调整 |
| UI 设计分辨率 | 1920×1080 | `FoundationHud.Initialize` | CanvasScaler 自适应，100×100 为设计像素 |

## 尚未给出的规则 / 外部依赖

- **真实联网 / Steam 接入**：当前目录、聊天和成员为本地演示；需要 Steam App ID、SDK / 联网技术选型、主机发现、同步和邀请规则。随机房间码目前不是真实 Steam 邀请码。
- **正式美术**：人物、待机动画、艺术字、物资图标、地图和正式 UI 素材均未提供；当前使用文字和简单几何占位。
- **麦克风**：`MicrophoneOpen` / `Speaking` 已有状态字段与展示；未接入录音、语音传输或讲话检测。
- **正式物资清单**：目前五种 `demo_*` 物资仅用于验证六张表结构、正负效果和价值边界。
- **水源形态**：目前演示可拾取物资；固定地点反复交互的规则没有确定。
- **赠送 / 好感 / 求婚**：没有详细交互、数值和成功失败规则，只预留元数据或效果接口。
- **衰减 / 死亡**：饥饿与口渴衰减、死亡阈值、死亡掉落 / 消失没有确定。
- **货币 / 结算 / 胜负**：未定义，不凭空执行物资兑换或决定胜负。
- **中途离线 / 房主退出 / 重连**：等待阶段成员离开只移除自己；房主退出解散房间；开始后的联网断线行为待定。
- **名字审核**：v1“需要接审核？”仍是问题；当前只有输入长度和格式验证。

## 版本冲突的处理

- 用户追加规则优先：总人数必须为偶数，开局两边相等。
- v1 全员准备 vs v2 随时开始：保留可切换选项，默认采用后续版本 v2。
- v1 成员退出栏也写了“解散房间”，与房主专属确认提示及正常成员权限冲突：实现为只有房主退出解散，成员退出仅移除自己。

运行方法与逐项验收见 `docs/foundation-v2.md`。当前环境未运行 Unity 编辑器，不能把核心模型测试当成 Unity 场景已验收。
