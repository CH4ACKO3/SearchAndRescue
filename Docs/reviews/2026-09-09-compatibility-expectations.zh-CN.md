# 兼容性与玩家预期审查

基线：54befb3，v0.1.0-alpha.5。日期：2026-09-09。

本轮逐项核对 CompatibilityCatalog、工坊兼容清单及当前实现，重点追踪特殊任务创建、权限、外部任务门控和目的地选择。属于源码审查，没有新增游戏实测；下列运行后果应按给出的场景复现。本轮仅新增本报告，没有改变兼容状态、生产代码或发布内容。

## 优先处理

### 1. Allies are Helpful：标记可能挡住本可救人的盟友

已确认：DynamicCompatibilityPatches.cs 的 AlliesAreHelpful_SearchAndRescueQueueOwnershipPatch 删除 SAR 持有对应阶段的非强制盟友排队任务。JobSystemCompatibilityPatches.cs 的 HasManagedTreatmentOrder 对显式治疗标记直接返回 true；运输标记也不要求已有实际 SAR 执行者。设施/Lord 所有权可以解除门控，但普通盟友排队任务不是这类提供者。

玩家可能期待盟友参与自动救援，实际是优先避免两方争抢。特别需要验证“显式标记 + 无可用殖民地医生 + 可用盟友”的情况：代码存在拦截盟友新任务的条件，不能仅凭此断言患者一定死亡或所有盟友路径都失效。已开始的外部任务另有所有权保护。

建议：对普通盟友增加有期限的任务接收/接管机制；没有可执行 SAR 任务时允许盟友接手，且保持手动命令和敌对俘虏边界。优先级高。

### 2. Work Tab + More Injuries：原生子工作开关不是逐项权限

已确认：Compatibility.CanPerformTreatmentIntervention 将操作分成 Doctor 或 Nursing 两类；Doctor 对应 SAR_EmergencyMedicalCare 子工作。MakeSelectedTreatmentJob 直接构造 CPR、吸引、除颤等原生 Job，不经过各自原生 WorkGiver 的 Work Tab 优先级查询。

因此启用 SAR 紧急医疗、关闭 MI 原生 CPR/除颤子工作，并不等于禁止 SAR 发起相同操作。研究、器材和部分专门资格仍检查，问题是子工作授权语义。护理路径同样不意味着读取每个第三方护理子项。

建议：明确 SAR 紧急医疗是独立授权还是继续受原生细分开关限制；如采用后者，为实际存在的原生自动 WorkGiver 建立能力映射，不能用不存在的子工作阻断仅有手动命令的急救。专项复现先测关闭 CPR/除颤、保留 SAR 急救，再反向设置。优先级高。

### 3. MedPod：已有任务避让，不等于主动选择最合适医疗舱

已确认：注册入舱 Job、实际占用的医疗舱设施以及医生/看守/患者自行入舱扫描；SAR 持有相关阶段时会门控新的入舱任务。设施所有权使用 CurrentBed，不能将文案的“已分配医疗舱”理解成仅被分配就已建立交接。

FindBestRescueBed 使用 Move the Patient 接口后回退原版床位搜索，没有专门按 MedPod 治療能力创建入舱方案。原版搜索可能选到舱，并非绝不会入舱；但没有“舱空闲且适合，就优先送舱、跳过可由舱完成的现场补治”的保证。

建议实测普通医疗床与空闲 MedPod 并存、病人可自行移动/倒地/囚犯三种情况，再加入只有治疗标记的病人。补做明确接收与失败回退，而非直接提高所有舱位优先级。优先级高。

## 特殊功能未自动接入

| 模组 | 代码实际支持 | 容易产生的额外期待 | 处理建议 |
| --- | --- | --- | --- |
| Dubs Rimkit | 注册 TendSelf、Bandage、BandageOthers，保护已启动任务 | SAR 自动使用 Rimkit 特殊包扎，如先前期待 CASEVAC 自动组队 | 最接近旧 CASEVAC 的缺口；目前无对应干预候选和 Job 创建分支。可新增器材、资格、原生任务和完成条件适配 |
| More Injuries | 主动支持 CPR、吸引、除颤、肾上腺素、止血与液体等列明操作 | 所有 MI 急救、夹板、镇痛/麻醉和急诊手术都由 SAR 自动安排 | UseSplint、UseMorphine 等存在所有权注册，但不在主动干预枚举/创建路径。夹板和手术文案已有边界，不能把这种限制一律当 bug；镇痛与手术应单独设计 |
| Death Rattle Continued | 通用危险评分和可包扎急症识别 | 安装后 SAR 自动复苏所有濒死/器官衰竭状况 | 没有 Death Rattle 专有干预创建路径。FieldTreatmentBoundary 首先要求 TendableNow；不可包扎状况不能靠普通治疗解决，也不能把 MI CardiacArrest 支持外推到所有模组 |
| Trauma Team Complete | 识别服务小队治疗阶段并交接患者，修正任务看门狗 | 殖民地医生与小队混编，SAR 给小队送药、多人共同处理患者 | 当前外部所有权使 SAR 退让；不是联合调度。工坊详细说明已说明人员与药品分开，属于功能边界 |
| Vehicle Framework | 停驻己方载具的医疗库存取货 | 自动装伤员上救护车、随车治疗、跨图撤离 | 未实现，详细文案已经排除，不宜按缺陷修复 |

## 其他已检查边界

- RH2 First Aid：CE 启用时不选 RH2 自动急救；与 SM 并用但无 CE 时，躺卧患者的无药 RH2 已作为候选，带药路径仍不走 RH2。不能重复引用旧审查中“SM 完全屏蔽 RH2”的结论。当前工坊仅强调 CE 排他，遗漏带药/无药区别。
- Nurse Job：护士可做支持干预，不等于有普通伤口包扎/CPR 权限；后者仍需 Doctor/SAR 紧急医疗。详细文案基本准确，但仅看“护理联动”容易误会。
- CYM、Smart Medicine、Pharmacist：此前统一策略修复已在当前代码中，不能再把旧的个人上限覆盖问题算作当前缺陷。兼容仅覆盖 SAR 选药路径，不保证第三方彼此所有路径兼容。
- Move the Patient：实际调用床位选择组件并重新验证；没有发现旧 CASEVAC 那种仅注册任务而未调用核心接口的同类遗漏。持续床位升级仍由原模组负责。
- Emergency Transfusions / Hemogen Direct：有主动单包 Job 路径，ET 优先；不是只登记外部任务。临床阈值/所有设置一致性仍需原生对照，不以静态存在 Job 声称全部实测通过。
- Medical Tab、Defaults、Sensible Bed Ownership、VFE Medical、EPOE、Grievous Wounds、Yokai Village：承诺主要是读取标准字段/床位/伤情或种族接口，没有发现与文案等价的特殊自动行为遗漏；不代表全部内容组合实机通过。
- Work Tab、Mech Work Tab、WVC、Search and Destroy、Common Sense、PTR、Treat Dying First、PUAH、Stay in bed、Smarter Capture Them：主要是权限、任务边界或物资保护。没有联合控制征召作战或所有第三方调度器的承诺。Work Tab 的细粒度例外见上文。
- Hospitality、NOLB、Stabilize Bleeding、MSE2、勤工、Paniel、Androids 及 DMS 家族已标记部分兼容；既有车内维修、专用种族和手术边界不重复算作新发现。

## 验证顺序

先做盟友无 SAR 人手接管、Work Tab 禁用特定 MI 操作、真实 MedPod 接收三个最小对照场景，记录任务来源、患者状态、等待时间和最终目的地。随后再决定 Dubs Rimkit 自动包扎的新增联动。单纯“无报错”或“JobDef 已注册”不能作为功能验收。

证据入口：Source/SearchAndRescue/Compatibility/{CompatibilityCatalog,CompatibilityRegistry,Compatibility,DynamicCompatibilityPatches,JobSystemCompatibilityPatches}.cs；Medical/{MedicalLogistics,FieldTreatmentBoundary}.cs。历史运行范围见 2026-09-08-combination-fixes.zh-CN.md 与 2026-09-08-gabs-combinations.zh-CN.md；本报告没有将历史接口探针升级为真实设施全流程测试。
