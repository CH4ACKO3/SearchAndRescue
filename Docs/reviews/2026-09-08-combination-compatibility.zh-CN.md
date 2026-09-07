# Search and Rescue 多模组组合兼容性审查

日期：2026-09-08。源码基线：`bf65804519fcbc9058d82be527efb78033e889ce`，About 版本 `0.1.0-alpha.3`。

后续用户决定：兼容面板保留一对一展示，不实施本文的组合面板建议。组合处理的实际改动及验证范围见[实现记录](2026-09-08-combination-fixes.zh-CN.md)；下文保留审查时的状态。

本轮完成源码路径审查、历史证据核对、上游用药规则核对及现有离线回归。没有进行新的多模组游戏长跑，也没有修改生产代码或第三方模组。GABS 当前唯一配置为已停止的 Rim Alert 测试配置；它不是本报告的 SAR 组合测试环境。

“常见组合”按医疗、物流、医院、工作权限等实际功能搭配选取，未做创意工坊订阅组合频率统计。下列每组均隐含 SAR、Harmony 和对应依赖。

## 1. 核心逻辑与组合边界

当前 SAR 已经具备组合兼容的基础，不是每对模组各自维护一套调度器：

1. `CareAdmission` 将手动标记与自动医疗模式统一为准入决策。`MarkedOnly`、自动紧急治疗、`AllTending` 的患者范围不同；自动模式还接纳符合殖民地责任关系的倒地患者撤离。敌方俘虏仍需明确标记。
2. `MedicalCarePlan` 生成普通药品和第三方器材需求；`FindTreatmentOptions` 生成具体干预、药堆、数量与路线候选。
3. 协调器通过加权匹配分配医生、护士、搬运者；主任务、物流、待命分别记账。俘虏、紧急治疗、撤离、稳定后补治在同一生命周期中衔接，并非每个患者都必须完成固定流水线。
4. `CompatibilityRegistry` 统一登记 Treatment / Transport / Capture / Facility；`PatientOwnership` 识别正在执行的外部 Job、玩家强制队列、实际携带、医疗设施和 Trauma Team 的 Lord 所有权。
5. `MedicalResourceLedger` 管理药堆数量声明和患者持久引用；SM、CE、PUAH 的搬运或补货补丁保护这些资源。载具库存先通过原生 VF 取货接口转交，CE Driver 再使用可直接取得的药品。
6. 机器人患者优先进入原生零件包扎或机械维修路径；响应者资格另行检查工作权限、征召、能源、WVC 模式和勤工状态。

主要入口：[协调器](../../Source/SearchAndRescue/Scheduling/SearchAndRescueCoordinator.cs)、[治疗与选药](../../Source/SearchAndRescue/Compatibility/Compatibility.cs)、[物资账本](../../Source/SearchAndRescue/Medical/MedicalLogistics.cs)、[所有权](../../Source/SearchAndRescue/Scheduling/PatientOwnership.cs)、[动态补丁](../../Source/SearchAndRescue/Compatibility/DynamicCompatibilityPatches.cs)。

## 2. 本轮发现

### A. 已确认：兼容性状态没有组合规则

`CompatibilityCatalogEntry.CurrentLevel` 只查询本模组是否启用、适配器是否就绪。CYM、SM、Pharmacist 同时存在时，各行仍可能显示“兼容”。枚举虽有 `Incompatible`，当前表没有组合条件驱动它。

上游 CYM 作者将改变选药方式的模组列为很可能不兼容，并明确举例 Pharmacist、Smart Medicine；这不等于所有配置都会崩溃，也不是调整 SAR 加载顺序就能解决。[作者说明](https://steamcommunity.com/sharedfiles/filedetails/?id=2937201140+packageid%3A+kopp.chooseyourmedicine&l=german)

建议：另建组合规则层，记录触发 packageId 集合、适配器前提、影响范围、玩家建议和证据等级。保留单项能力说明，同时显示组合注意事项；不要仅因同时启用就自动关闭玩家模组。

### B. 已确认：多套用药策略的权威来源不统一；实际违规用药尚未复现

`EffectiveMedicalCare` 优先 CYM，再 Pharmacist，最后原版；`FindTreatmentOptions` 的选药分支却优先 SM。SM 返回的候选在这一分支检查物品类型、数量和 CE 取药路线，没有统一调用 SAR 的 `AllowsMedicine`。`AvailableForTreatment` 只计算数量，不负责政策过滤。

因此 CYM + SM 的预算与候选可以来自不同策略。SM + Pharmacist 是否出现不一致，取决于当前 SM 版本如何处理 Pharmacist 和单伤覆盖，不能只凭缺少这一过滤就宣布运行失败。

另一个明确的语义差异：CYM 作者说明单个 Hediff 的手动指定可以越过普通个人用药设置；SAR 的 `EffectiveMedicalCare` / `AllowsMedicine` 始终取个人上限。加入 Medical Tab / Defaults 后尤其容易出现“界面已指定、SAR 预算仍受限”的理解偏差。[CYM 作者说明与答复](https://steamcommunity.com/sharedfiles/filedetails/?id=2937201140+packageid%3A+kopp.chooseyourmedicine&l=german)

建议：先明确单伤手动指定与个人上限的产品优先级，再让预算、候选、补给、最终 Job 共用同一策略结果。不能直接补一个过滤而忽略 SM/CYM 合法的单伤覆盖语义。

### C. 已确认：RH2 First Aid 在叠加 SM 时也会让位

当前治疗候选只在 `FirstAidJob != null && !UsesSmartMedicine && !UsesCombatExtended` 时选择 RH2 First Aid。因而 RH2 + SM 即使不启用 CE，也会选择普通 TendPatient；启用 CE 后，可稳定伤口进入 CE，其余普通处理仍不会选择 RH2。

这是明确的选择优先级，不是异常。现有兼容文案仅写“CE 未启用时使用 RH2”，不足以解释三方行为。建议将该条件纳入组合说明；若希望 RH2 作为无药急救备选，需要单独验证其伤效、消耗和任务结束边界。

### D. 已确认：输液/输血共享调度，但没有跨提供者的疗效预算

MI 盐水、MI 血袋与血原输血需求会分别加入同一计划。ET 与 Hemogen Direct 共用一个血原需求，且 ET Job 优先，因此不是两个血原模组重复登记两份相同需求。

现有主任务所有权和单包结束后重算已经限制并发治疗；本轮没有证据证明会同时给同一患者重复输血。但账本按物品/数量去重，未表达“这份盐水/血袋/血原对同一失血缺口的替代或互补效果”。不同资源的现场引用仍可能同时保留。Hemogen 需求只读取失血、种族和器材许可，没有 MI 的休克/血液稀释状态决策。

建议：增加复苏需求组与提供者效果描述，区分失血、休克和血液稀释，按已选干预扣减等效需求；保持完成一包后重新评估。先验证三套原生效果，不把盐水与血液简单当成等价物，也不直接给现有代码判定“过量输血”。

### E. 已确认：旧组合证据不能覆盖当前全部自动模式

原 150,000 tick 记录以标记伤员为主要对象；当前 `TryBuildCareAdmission` 与 `OwnsAutonomousTreatment/Transport` 已扩展未标记患者的管理范围。第三方 WorkGiver、MedPod 自行入舱、PTR 唤醒等现在可能在更多场景遇到 SAR 门控。

应分别验证三种协调模式，尤其是无可用 SAR 响应者时的软所有权退让。不能将旧模式的一次长跑升级为新模式下所有外部医疗 AI 的组合保证。

### F. 已确认的联动边界：外部医院、机器人与载具

- `TargetReadyForStage` 在外部所有权存在时整体避让；这是当前保守交接。Trauma Team 治疗时让 SAR 继续给其送药属于新功能，需要提供者明确同意、共享物资目的地和超时释放协议。
- 原生零件机器人跳过普通器材需求与任务药包；`RobotMedicalProfile.TreatmentOption` 还拒绝从其他载具持有者直接取维修资源。因此“VF 可提供医疗库存”不能扩大解释成“机器人维修零件也有完整载具物流”。
- DMS 医护能治疗人类，不代表具有全部 CE/MI 干预能力；CE 稳定候选明确排除机械族响应者。WVC 安全模式在威胁出现时停工也是既定权限行为。

## 3. 组合检查矩阵

“历史运行”仅引用旧记录的具体场景；“静态”指本轮核对当前源码；所有行都不是本轮新实机通过。

| ID | 功能组合 | 当前判断与主要检查点 | 优先级 |
| --- | --- | --- | --- |
| C01 | CE + MI + SM + Nurse Job + Work Tab | 历史 17 模组长跑含这些组件；统一治疗/护理分工已有基础。新增单伤设置、只允许草药、器材缺货与护士时段关闭的交叉场景 | 高 |
| C02 | CE + SM + Pharmacist + Medical Tab / Defaults | 静态：预算受政策限制，SM 候选由自身策略提供。需核对单伤覆盖、伤情等级变化及最终 targetB，不能仅检查有没有报错 | 高 |
| C03 | CYM + SM，或 CYM + Pharmacist；可再叠加 CE | 上游提示很可能不兼容，SAR 缺少组合提示且存在策略来源分裂。作为冲突检测样本，不作为支持组合默认模板 | 高 |
| C04 | MI + Emergency Transfusions + Hemogen Direct + Nurse Job | 静态：ET 优先，单包重算；MI 与血原仍分别计算需求。验证失血恢复、持续出血、休克及血液稀释时的切换 | 高 |
| C05 | CE + SM + PUAH + VF（含 DMS Motorized） | 部分历史 CE 配额证据，完整四方搬运竞争未覆盖。检查同一堆数量、车移动/离图、库存持有者改变、载重事件和失败后释放 | 高 |
| C06 | Trauma Team + CE + MI + Allies are Helpful | 统一注册与 Lord 所有权路径已接入，缺完整服务交接证据。检查小队到达前、治疗中、全员失能、离开及 SAR 恢复 | 高 |
| C07 | MedPod + Move the Patient + Hospitality + Sensible Bed Ownership | 静态：床位选择和设施所有权可组合；访客关系、囚犯舱位、临时床到永久床及自行入舱尚需完整流程验证 | 高 |
| C08 | PTR + Common Sense + Stay in bed + SM / MI | 旧长跑覆盖其中部分，PTR 另有专项边界记录。新增 AllTending 下未标记患者、清扫插队、强制 Shift 队列和无响应者退让 | 高 |
| C09 | DMS Core / Synthetic / Joint Operations + Mech Work Tab + WVC + CE | 25 模组历史记录覆盖部分真实维修与人类包扎。需补 FFF/Tinker 并发、时段关工、充电和威胁模式切换；CE 稳定仍由合适人类医生承担 | 中 |
| C10 | Paniel / Androids Expanded + CE + SM + VF | 历史验证部分种族原生包扎/零件处理。机器人原生 FindBestMedicine 是补丁交汇点；车内维修零件不在现有完整物流承诺内 | 中 |
| C11 | RH2 Arrest Here + CASEVAC + Smarter Capture Them + No One Left Behind | 角色注册/实际携带兜底成立。补查俘虏前后阵营变化、敌军携带开始与结束、手动覆盖及倒地复原；不能把敌军撤退直接当成 SAR 失败 | 中 |
| C12 | RH2 First Aid + SM + CE / MI | 已确认 RH2 自动选择会被 SM 或 CE 抑制。验证玩家手动 RH2 仍保持所有权、各种伤口的剩余治疗能收敛 | 中 |
| C13 | HardworkingExt + Work Tab + Nurse Job / MI | 当前为勤工单独查询工作权限，避免普通 Work Tab 覆盖；需要种族本体的持续 Job 测试，并核对止血带技能门槛、训练与作息 | 中 |
| C14 | Death Rattle + MI + MSE2 / EPOE + VFE Medical | 通用危险评分、现场急救、床边手术分层有基础；需验证无法现场解决的病况不会持续留在战地等待。新增手术决策应交给床边提供者 | 中 |

## 4. 建议实现顺序与联动方案

1. **组合诊断先行。** 使用独立 `CompatibilityCombinationRule` 数据表；最低字段包括规则 ID、全部/任一启用条件、模式条件、适配器要求、影响说明、建议和验证级别。第一批覆盖 C03、RH2 选择让位、机器人车内零件边界及自动模式覆盖缺口。界面设计可在专门实现阶段处理。
2. **统一用药决策。** 形成一次快照的策略结果，包含来源、允许类别、顺序、个人上限与单伤覆盖；将它传入需求、资源候选和 Job 构造。SM + Pharmacist 的协同策略和 CYM 的独立策略分开定义，遇不支持的组合报告清楚。
3. **临床替代关系。** 在现有需求提供者上增加替代组/效果信息，先处理 MI 与血原复苏。医生执行高技能干预，护士执行支持治疗的现有分工可以继续复用。
4. **车载救护补给。** 基于现有 VF API、声明数量及 CE 容量检查，扩展可选医疗车库存策略：救护专用储备、普通战备库存最低保留量、车辆离开后重新选源。默认规则变化需通过资源紧缺场景评估；当前 CE 配额并非独占库存。
5. **现场到医院交接。** 提供设施/外部小队的显式接收协议：确认床位、接收人、阶段和超时后转交，失败恢复 SAR。MI/MSE2 紧急手术需求可提高适合设施的目的地偏好，但应先检查床位、种族、囚犯及阵营资格。
6. **机械战地后勤。** 将机械患者需求类型和响应者能力继续分离；为原生维修零件增加独立资源提供者，再接入 VF 取货。不能让生物输血需求或普通药品策略覆盖原生维修规则。

## 5. 可执行的专项回归设计

每个用例从同一未推进的临时基准存档副本开始。保存模组 packageId、版本/程序集哈希、顺序、设置、SAR 提交和协调模式。每组先做必要依赖 + SAR 对照，再增加单个提供者和最终组合；有效组合尝试交换可交换的第三方加载顺序，不打破依赖关系。

| 用例 | 操作 | 验收标准 |
| --- | --- | --- |
| T01 政策一致性 | 轻伤与重伤各一人；草药/工业药/高级药齐全；切换个人限制、单伤覆盖及治疗模式 | 每步记录预算、候选、声明和 Job targetB；四者符合事先定义的同一用药规则 |
| T02 复苏提供者切换 | 分别只供盐水、血袋、血原，再全部供给；有/无持续出血、休克和血液稀释 | 每包记录前后 Hediff、物资消耗、执行者和新需求；无重复主治疗，无无效反复重启 |
| T03 药堆争用 | 两名医生、两名搬运者共用小药堆，启用 CE 配额、SM 补货及 PUAH；药堆放车内 | 声明不超库存；成功取货守恒、失败不复制物品；车载重正确；患者需求结束后不残留保护 |
| T04 动态失效 | T03 拾取前让车辆移动、来源卸地、道路隔断，再恢复 | 失效任务释放声明并重新选源；有合法替代资源和人员时最终继续治疗 |
| T05 外部医疗交接 | Trauma Team/盟友先后到达，治疗中失能或离开；另测 MedPod 自行/搬运入舱 | 同患者同阶段只有一个主执行者；外部退出后 SAR 恢复；玩家强制及排队任务保留 |
| T06 自动模式 | 三模式分别测试未标记患者，先禁用全部 SAR 医生，再开启；加入 PTR、MI、MedPod | 门控与准入一致；无可用 SAR 人手时自动软所有权正确退让；显式标记按其既定规则处理 |
| T07 机械权限与资源 | DMS 医护 + 人类医生 + Tinker；切时段、WVC 模式和能源；机器人零件放地面/普通背包/车内 | 禁用权限不被其他提供者复活；原生维修不误用生物药；不支持来源明确退出且无无限 Job 循环 |
| T08 敌军撤离与医院身份 | 同敌人叠加俘虏/治疗/救援，敌军搬运者介入；另测宾客与囚犯床位 | 俘虏前后所有权正确；实际外部携带被尊重；床位权限和预约在执行前重验 |

在 1,200 / 6,000 tick 记录中间状态，足够资源场景继续至预先规定的 150,000 tick 上限或收敛；资源不足场景检查可解释的等待/退让，不能一概要求 pending 为零。记录患者存活、病况变化、Job 重启频率、实际消耗、主任务/物流/待命数量、新日志。治疗有等待或持续病况时，结合临床结果判断，不把四个计数清零当成唯一成功标准。再保存重载检查所有权与资源引用恢复。

## 6. 本轮验证与历史证据

本轮运行：`dotnet run --project Tools/SchedulerSimulation/SchedulerSimulation.csproj -c Release`，退出码 0。结果：85 项直接生产规则检查、9 项完成治疗连续性检查、40 个调度/资源/生命周期场景、200 组随机图全部通过。这些测试未加载第三方程序集，不能证明 Harmony 补丁叠加或原生 JobDriver 的组合正确性。

历史证据：

- [原组合矩阵](../compatibility/TestMatrix.zh-CN.md)：包含 SAR 的 17 模组 150,000 tick 长跑，按文档原测试范围引用。
- [CE/DMS/机器人记录](2026-09-06-ce-dms-runtime.zh-CN.md)：25 模组配置，部分真实 Driver、4,800 tick 与重载后 600 tick；部分器材仅检查候选/任务构造。
- [现有实机夹具说明](../development/Testing.zh-CN.md)：所有权、持有者改变、运输目的地和性能记录入口。

尚未执行：C01–C14 针对当前提交的新实机组合回归、上述 T01–T08 专项、加载顺序变体。建议先完成 C02/C03 的政策语义与 C06/C07/C08 的自动模式交接，再开展全组合长跑。
