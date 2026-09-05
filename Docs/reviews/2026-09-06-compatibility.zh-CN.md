# 创意工坊兼容性复查

审计基线：`edb4db1`。三个 GPT-5.6 Sol / medium 子代理分别检查医疗、工作调度及未充分测试的模组，主代理复核所有权和载具接口。

后续修复：已登记 Kidnap 运输所有权、补齐 Hospitality 扫描/任务创建门控、勤工极简白名单和临产检查、PTR 睡眠设置及旧缓存过滤，并令载具移动状态未知时禁止取货。49 项开发诊断通过；具体测试范围及未覆盖的完整流程见 [验证记录](../validation/2026-09-06-compatibility-fixes.json)。下文保留修复前审计发现，不代表这些缺口仍全部存在。

本轮仅核对 SAR 源码、已安装的第三方源码/程序集、Def 和作者工坊说明。没有操作正在运行的游戏，没有新增实机测试，也没有修改运行代码。下文“确认”指静态路径证据，不等同于已经在游戏中复现。

## 优先处理

### P1：No One Left Behind 撤离伤员可能被强制放下

- 本体：`NinaGoblin.NoOneLeftBehind`，工坊 `3536586707`，1.6。
- 触发：敌军使用其 `JobGiver_Rescue` 搬起带 SAR 标记的同阵营伤员，随后运行 SAR 的周期维护。
- 证据：该模组创建原版 `Kidnap`；SAR `CompatibilityRegistry.cs:447` 的运输目录没有这个 Job，也没有其 `JobDriver_TakeAndExitMap` 基类。`SearchAndRescueCoordinator.cs:2363` 的 `CleanupOrphanedManagedCarries` 对未登记且不属于活动 SAR 救援的搬运执行强制放下。搬起伤员不会消除 Thing 标记，SAR 自己也保留这种标记。
- 影响：与文档中“不会抢夺敌军当前正在搬运的伤员”的承诺矛盾。`HasExternalOwner` 已经承认非 SAR 实际搬运，但清理阶段没有沿用相同边界。
- 修复建议：统一实际搬运所有权和孤儿清理的判定；明确区分失去 SAR 任务的遗留搬运与其他 AI 正在执行的运输，不应只靠不断扩充 Job 名称兜底。
- 回归：已标记敌军伤员被 NOLB 搬起，经过至少一次 60 tick 维护后仍保持搬运并可离图；同时保留真正 SAR 遗留搬运的清理测试。

### P2：Hospitality 宾客救援扫描绕过 SAR 标记门控

- 本体：`Orion.Hospitality`，工坊 `3509486825`。
- 触发：倒地宾客具有 SAR 运输标记，但 SAR 尚未建立预约，Hospitality 的救援扫描先运行。
- 证据：第三方 `WorkGiver_RescueDowned_Patch.HasJobOnThing_Patch` 对宾客自行检查床位、预约和身份，并跳过原方法；该分支不调用 SAR 在 `JobSystemCompatibilityPatches.cs:190` 门控的 `HealthAIUtility.CanRescueNow`。
- 影响：宾客可能先被原版 Rescue 接走，绕过 SAR 计划的阶段顺序。已存在有效预约时可能被预约挡住，因此属于窗口竞态，不是每次必现。
- 修复建议：在最终 `WorkGiver_RescueDowned.HasJobOnThing` 结果处补运输所有权门控，验证与 Hospitality 的 Harmony 顺序，保留玩家强制命令。
- 回归：分别测试标记/未标记宾客、已有/尚无预约、手动强制救援。

### P2：勤工极简模式的工作白名单未接入

- 本体：`Moo.Hardworking.Kz`，与上一轮测试相同的本地 1.6 框架。
- 触发：开启 `enableHardworkingTinyMode`，相关 SAR WorkGiver 不在 `AllowableWorkGiverNames` 中，但战地救援及对应父工作优先级大于零。
- 证据：原生 `JobGiver_HardworkingWork` 在极简模式只扫描按允许名单构造的 `TinyWorkList`。`HardworkingCompatibility.cs:44` 只读取优先级与停工/概率/作息字段，没有白名单检查；协调器可在工作边界直接 `StartJob`。
- 修复建议：在每个阶段的工作资格处读取极简模式白名单，使用该阶段实际 WorkGiverDef；不能只检查父工作类型。
- 回归：白名单允许和禁止对应阶段、切换极简模式、同一动物的其他工作仍可用。既有 18 项权限测试没有覆盖此模式。

### P2：勤工临产停工边界未接入

- 证据：原生 `JobGiver_HardworkingWork.TryIssueJobPackage` 对 `InLabor(true)` 返回 NoJob；SAR 仅调用 `GetPriority`，自身勤工资格也没有对应检查。
- 触发范围：仍未倒地且满足其他工作资格的临产或原生该条件覆盖的角色。已倒地者会被现有 SAR 检查排除。
- 修复建议：在勤工 `CanWorkNow` 复用该条件；添加可行动的临产角色与普通角色对照测试。

## 当前未发现同等级问题的项目

- WVC Work Modes：初步怀疑的直接抢占在复核后不成立。正常 Goto/SelfShutdown 任务不在 SAR 的软过渡和可抢占例行工作中；不能把缺少显式 WorkMode 检查直接判作冲突。仅保留所有节点失败后的空任务/闲逛回退窗口作为待实测疑点。

- Work Tab：当前 1.6 的反射签名和当前本地小时读取方式匹配；不能据此承诺所有设置组合都经过实测。
- Mech Work Tab：当前实现写回 `pawn.workSettings`，SAR 能读取该值。
- More Injuries：本地工坊附带的 1.6 源码与 SAR 的 Dispatcher、ExtendedJobParameters 和主要医疗 Job 所有权登记匹配，未发现确定接口缺陷；这是源码比对，不是本轮新增 CPR/输血/存档恢复实测。
- Vehicle Framework：本地 `1.6.2144` 的 `TakeFromInventory(Thing,int)`、`AddOrTransfer(Thing,int)`、`vehiclePather.Moving` 与 SAR 签名匹配；取货路径确实触发 CargoRemoved。没有发现本版本的接口错配。

载具适配另有健壮性问题：`UsesVehiclesFramework` 只检测类型；若移动状态字段/属性将来失效，`VehicleCargoSourceAvailable` 会把无法获取状态当成可取货。建议能力检测包含全部必需接口，未知状态禁止取货。这不是当前版本已经失配的证据。

## 医疗适配补充

- Priority Treatment Ressurected：本体 `TickRare` 对 `wakeUpToTend=false` 的分支把 `CurJobDef == null` 与具体睡眠 Job 相等条件用 AND 连接，条件不可成立，是上游既有设置缺陷。SAR 的 `PriorityTreatmentCompatibility.cs:133` 只读取允许进食设置，未隔离这个问题；其 override 可把休息中的医生加入匹配并中断当前任务。建议复现“不允许唤醒”的睡眠医生场景后，明确让桥接遵守该设置。这不是已经实测的新 SAR 回归。
- Nurse Job：文档的“无护士时才由医生兜底”过于绝对。`Compatibility.cs:612` 允许护士和医生同时进入候选，护理偏好是有限加分，路线和紧急度可让附近医生胜出。应写作“优先护士，医生仍参与匹配”，属于说明偏差，不是任务无法完成。

## 缺少本体的边界

- Stabilize Bleeding：本机无本体，公开描述不足以验证 JobDef/Driver，继续保留待实测说明。
- Hardworking animals 1.6：本机无本体，不能把描述中的搬运频率改动视作实际 ThinkTree 行为证明。现有“待实测”说明正确，且不属于兽耳屋勤工适配。
- Animals Logic 本地元数据确实声明 `Daniledman.HardworkingAnimals` 不兼容；它是两款第三方模组之间的声明，不是 SAR 冲突结论。

建议先验证并修复 NOLB 所有权问题，再处理 Hospitality 扫描和勤工权限边界；新的实机测试应与之前的长跑、启动和组件权限测试分开记录。
