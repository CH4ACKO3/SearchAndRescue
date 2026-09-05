# Search and Rescue 0.1.0-alpha.1 兼容实测矩阵

日期：2026-09-04

这份文件区分三类证据，避免把“代码里识别了”误写成“已经完整实测”：

- **组合长跑**：在真实快速测试地图中与 SAR 同时运行，并执行批量伤员/响应者场景。
- **启动实测**：单独或在声明组合中激活，确认 Def、Harmony 补丁和初始化无新增错误；不代表覆盖对方所有游戏内容。
- **静态适配**：核对程序集、Def 或公开接口并实现防冲突，但本机没有可启用本体，发布前仍需社区实测。

## 组合长跑

最终医疗物流回归使用 12 名已标记伤员、3 名医生、6 名搬运者，并保留额外未标记伤员覆盖
Smart Medicine 自己的自动工作路径；最终编译件在超高速下连续运行到 150,000 tick。组合包含 Work Tab、
Common Sense、Mech Work Tab、WVC Work Modes、Nurse Job、Search and Destroy、Smart Medicine、
Combat Extended、More Injuries、Allies are Helpful、No One Left Behind、Stay in bed、Grievous
Wounds、Sensible Bed Ownership、VFE Medical，以及 SAR。最终 pending/active/logistics/standby 均为 0，
未再出现 SAR 预留失败、`Stabilize` 单 tick Job 循环或扫描目标与实际 Job 不一致。

同轮性能记录覆盖 119,627 个游戏 tick：MapComponent 平均 4.58 µs/tick；137 次调度重建平均
1.396 ms、峰值 20.338 ms。峰值发生在一次大型边评分，常态调度摊销约 1.60 µs/tick。

## 已在本机逐项启动

| 类别 | 模组 |
|---|---|
| 工作与 AI | Work Tab、Common Sense、Mech Work Tab、WVC Work Modes、Nurse Job、Search and Destroy、Allies are Helpful、No One Left Behind、[MOMO] Stay in bed、Priority Treatment Ressurected |
| 医疗 | Smart Medicine、Choose Your Medicine（独立配置）、Combat Extended、More Injuries、CE + More Injuries、EPOE-Forked、Medical System Expansion 2、Death Rattle Continued、[RH2] First Aid、Hemogen Pack - Emergency transfusion、Grievous Wounds |
| 搬运、俘虏与设施 | Move the Patient、[RH2] Arrest Here!、[RH2] CASEVAC、Pick Up And Haul、Vehicle Framework、Hospitality、Sensible Bed Ownership、Vanilla Furniture Expanded - Medical Module + Vanilla Expanded Framework |
| 设置迁移 | 1trickPwnyta's Defaults；用干净的临时设置启动后恢复原设置，避免其持久化 Def 快照把未启用模组误报为缺失 |

## 仅静态适配／本机缺少本体

Pharmacist: Represcribed、Medical Tab 本体、Treat Dying First、Stabilize Bleeding、Smarter Capture
Them、Emergency Transfusions、MedPod、Dubs Rimkit、Trauma Team Complete、Yokai Village。

官方 DLC 中 Royalty、Ideology、Biotech、Odyssey 随组合启动；本机未安装 Anomaly，因此 Anomaly
目前只有静态路径审查。完整行为边界见 `COMPATIBILITY.zh-CN.md`。

## 创意工坊复查后未新增专用补丁的项目

Work Manager、Tim's Auto Priorities、AutoPriorities、Automated Work Assignment、Dynamic Work
Schedules 等只重写或周期性
设置 WorkType 数值。SAR 的 Field Rescue 是普通 WorkType，并在每次候选筛选时读取当前优先级，
因此这些模组原则上不需要任务层 Harmony 补丁；如果它们关闭 Field Rescue，SAR 会按玩家最终设置
不分配该 pawn。涉及扩展 WorkGiver 细分优先级时，以已经实测的 Work Tab 路径为准。

Enhanced Work Tab 是另一套 1.6 工作栏替换实现，并明确与 Fluffy Work Tab 互斥。它能显示自定义
WorkType，但自己的 1–9 级细分优先级接口尚未在本机取得并实测，因此 Alpha 只承诺读取其最终写回
的原版工作类型优先级，不承诺读取它的时段/区域/子工作覆盖。

## 第三方模组之间的已知组合问题

- Smart Medicine 会按设置从其他殖民者库存选药；当前 CE `Stabilize` Driver 的对应取药分支无法可靠
  完成，会在原版工作树中反复返回零效果 Job。SAR 现在同时门控 Smart Medicine 的扫描和 Job 构造：
  优先换用医生自带的合规药品，没有安全药源时跳过该自动 Job，已标记患者则由 SAR 补包/送药。
- Choose Your Medicine 自身声明应作为 Smart Medicine / Pharmacist 的替代方案，逐项启动测试也采用了
  独立配置；不把这组第三方固有互斥误写成 SAR 的兼容失败。
- Enhanced Work Tab 自身声明与其他完整 Work Tab 替换模组互斥；测试 SAR 时应只启用其中一套。
- 较早一轮 120,000 tick 组合长跑在所有医疗任务收敛后出现过一次 CE 的 disabled
  `AimingDelayFactor` 一致性日志，未带 SAR 调用栈、也不涉及病患或医疗 Job，记录为第三方/测试人口
  的非医疗观察项，不计入 SAR 回归失败；最终编译件 150,000 tick 复跑没有再次出现。
