# Search and Rescue 1.6 兼容性清单

更新日期：2026-09-06

## 状态说明

联动：SAR 为该模组新增了专有内容。

兼容：SAR 会读取设置、识别工作或保护任务所有权，或双方通过原版标准接口共存；不表示每个模组都经过完整实测。

部分兼容：基础功能可以共存，仍有未纳入 SAR 调度的重叠区域、缺少本体实测，或可选适配器未能正确加载。

这些状态描述 SAR 对单个模组的支持范围。多个第三方模组同时启用时，实际效果还取决于它们彼此的兼容性。游戏内兼容性表会根据模组启用状态和可选适配器的加载结果，动态显示“未启用”或“部分兼容”。

这份清单覆盖了目前已浏览和测试的创意工坊模组。若发现遗漏或实测问题，欢迎留言。

## 官方内容

**RimWorld 1.6 原版 — 兼容**
支持批量标记、俘虏→紧急治疗→救援、稳定后低优先级补治，以及医生与搬运者统一匹配。原版手动命令始终具有优先权。

**Royalty — 兼容**
皇权身份和原版医疗规则保持原样。战地手术继续由原版或手术模组处理。

**Ideology — 兼容**
殖民者、奴隶、囚犯和宾客的身份及医疗权限沿用原版规则。

**Biotech — 兼容**
殖民地工作机仆可按其工作类型参与。CE 中依赖人类医术的稳定任务分配给合适的人类医生。支持血原包输血。伤员筛选覆盖使用常规伤口与医疗流程的血肉生物。

**Anomaly — 兼容**
具有医疗权利的变异体可接受处理。需要收容平台的实体沿用收容流程。原版 TendEntity 计入外部治疗所有权。

**Odyssey — 兼容**
调度器按当前地图工作，可用于重力船地图。跨地图撤离和载具内部运输沿用对应系统。

## 医疗、急救与输血

**Combat Extended — 联动**
使用原生 Stabilize，共享药堆按数量预约，并遵守携带容量。可稳定伤口进入 CE 稳定流程，其他伤口进入普通治疗。一次 CE 稳定流程保持为原子阶段，避免逐伤口重开 Job 和重复消耗整份药。医生或患者携带的药可直接使用。第三者、驮兽和载具库存通过 SAR 补包或送药流程转换为 CE Driver 可取得的 targetB。

**More Injuries (Continued) — 联动**
支持 CPR、吸引器、除颤、肾上腺素、止血带、止血剂、绷带、生理盐水和血袋，并遵守研究、设备与特殊 Job 参数。夹板等操作沿用原模组 WorkGiver，开始后会登记为外部治疗所有权。肺萎陷等手术状况会提高撤离权重并继续进入手术流程。

**Medical System Expansion 2 — 部分兼容**
识别危险状态并提高撤离优先级。假体、子部件和需要手术的急救由原版或模组床边手术系统处理。

**EPOE-Forked — 兼容**
植入、替换和手术账单沿用 EPOE 与原版流程。生命危险且需要床边处理的目标获得更高撤离优先级。

**Smart Medicine - Continued — 兼容**
使用其选药逻辑，并保护持久引用和本轮匹配产生的软声明，防止补货 AI 在分配与取货之间移动药品。与 CE 同时启用时，SAR 同步处理 HasJobOnThing 和 Job 构造。第三方 pawn 库存中的药品会先转交医生；安全药源暂时不可用时，医生等待 SAR 补包或送药。

**Pharmacist: Represcribed — 兼容**
将伤情严重度以及殖民者、囚犯、奴隶、动物、实体和宾客分类建议用于预算、配药、CE 稳定和最终治疗。原版个人用药上限作为最高许可等级。

**Choose Your Medicine — 兼容**
读取当前分组、伤情阶段、药品顺序和单伤覆盖设置，用于预算、携带、隐式补给和最终治疗。

**Medical Tab — 兼容**
每轮计划直接读取原版个人用药字段，表格修改会立即进入下一轮计划。

**1trickPwnyta's Defaults — 兼容**
人群默认用药写入原版字段，SAR 直接读取最终结果。

**Emergency Transfusions — 联动**
使用原生单包输血 Job，支持伤员、医生、地图 pawn 或驮兽携带的血原包。

**Hemogen Pack - Emergency transfusion — 联动**
Emergency Transfusions 未启用时，由该模组提供血原应急输血流程。

**Death Rattle Continued — 兼容**
复苏流程由 Death Rattle Continued 执行。其生命危险 Hediff 通过统一的 lifeThreatening 评分提高 SAR 紧急度。

**RH2 — BCD: First Aid — 兼容**
CE 未启用时使用其原生战地急救 Job。

**RH2 — CPERS: Arrest Here! — 兼容**
使用其原地拘捕 Job。

**Dubs Rimkit — 兼容**
1.6 版本的 TendSelf 自我包扎及 BandageOthers 为他人包扎均计入外部治疗所有权。手动启动时，SAR 会释放相关声明。

**Treat Dying First — 兼容**
Treat Dying First 管理普通患者搜索，SAR 管理被标记伤员的匹配。

**Stabilize Bleeding — 部分兼容**
创意工坊条目目前已下架，已有订阅者仍可能保留文件。其手动止血 Job 与 SAR 目标存在重叠，本机缺少本体进行 JobDef 和运行验证。玩家手动命令具有优先权；与 SAR 标记叠加时列为部分兼容。

## 搬运、床位与外部救援者

**Trauma Team Complete — 兼容**
创伤小队处于治疗阶段且存在可工作、可达医护时，会在首个 Job 产生前获得客户所有权，SAR 在 Job 边界避让。其私有 ThinkTree 接入统一所有权门控。350 tick 医护看门狗接受所有指向同一客户的已注册治疗或运输 Job，包括 CE 与 More Injuries。治疗阶段结束，或全队失能、隔断后，SAR 恢复调度。创伤小队保留其成员与随身药品的调度权，它们不计入 SAR 匹配和殖民地医疗配额。

**Move the Patient — 兼容**
优先通过其患者组件选择合适医疗床，再回退到原版床位搜索。

**Allies are Helpful — 兼容**
对方直接插入的自动治疗和救援任务会经过 SAR 所有权清理。清理范围覆盖与已标记伤员重复、且由系统自动产生的任务。其他伤员继续由 Allies are Helpful 处理。

**No One Left Behind — 部分兼容**
敌军搬运者在实际携带伤员期间持有运输所有权，撤退救援由 No One Left Behind 执行。敌对搬运结束后，仍然有效的 SAR 标记会重新进入调度。

**MedPod — 兼容**
搬入和救援 Job 计入运输所有权，已分配医疗舱登记为外部设施。看守的直接扫描与患者自行入舱的 NonScanJob 会在 SAR 持有对应阶段时避让。

**RH2 — BCD: CASEVAC — 兼容**
专有救援和俘虏运输 Job 计入外部所有权。右键手动命令保留原模组行为。

**Smarter Capture Them — 兼容**
自动俘虏和运输 WorkGiver 接入统一所有权门控。玩家强制命令具有优先权。

**Pick Up And Haul — 兼容**
SAR 会保护仍被伤员持久引用或被本轮匹配软声明的战地医疗物资，使其留在当前运输链路中。卸货选择器出现异常时，补丁会恢复内部携带集合。

**Hospitality — 部分兼容**
使用原版床位、宾客和阵营关系路径。接待、收费和访客 AI 由 Hospitality 处理。大型医院场景建议进行实机验证。

**MOMO — Stay in bed — 兼容**
其可中断的最低优先级卧床 Job 会为 SAR 治疗、补给和运输任务让出执行权。

**Sensible Bed Ownership — 兼容**
SAR 每次运输前重新验证实际床位与预约，以读取最新的床位所有权。

**Vanilla Furniture Expanded - Medical Module — 兼容**
通过标准接口使用其医疗床、设施 Def 和治疗效果。

**Vehicle Framework — 联动**
停驻、属于玩家且可达的载具货舱会作为医疗物资来源，参与路线、稀缺度和软声明计算。医生可以从载具补充任务医疗包，搬运者可以取出已声明数量并直接送往伤员，CE 启用时同样适用。取货调用 VF 的公开货舱 API，触发货物移除事件并刷新载重和状态。移动中、离图、敌对和不可达载具会从候选来源中排除。伤员进入载具及车内治疗沿用载具与下游模组的流程。

## 工作栏、AI 与非人类工作者

**Nurse Job — 联动**
提供“护理优先”和“仅护理”救援模式，默认模式使用搬运工作。标记伤员的输血、输液、止血剂、绷带和止血带可由护理工作承担，医生在护士不可用时接手。CPR、吸痰、除颤及普通包扎按医疗技能匹配给医生。

**Work Tab — 兼容**
读取细分 WorkGiver 优先级。

**Mech Work Tab — 兼容**
读取机仆细分工作设置。

**WVC - Work Modes — 兼容**
读取额外机械体工作模式与优先级来源。

**Search and Destroy (Continued) — 兼容**
Search and Destroy 管理征召战斗行为，SAR 调度未征召 pawn 的战地救援工作。双方各自维护开关和 Job。

**Common Sense — 兼容**
治疗阶段完成时，SAR 会清除 Common Sense 自动插入的非强制清扫。玩家队列保持原样。

**Priority Treatment Ressurected — 兼容**
注册 RH2、CE、More Injuries 及 SAR 医疗和补给 Job，使正在执行这些工作的 pawn 保持忙碌状态。

**Yokai Village — 兼容**
非敌对血肉动物可以使用动物床和 MedicineBase 物品接受治疗与救援。俘虏阶段适用于人形目标。

**Grievous Wounds — 兼容**
新增伤口通过通用 Hediff 和出血评估进入紧急度。溢出伤害由 Grievous Wounds 计算。

兽耳屋勤工 / kemomimihouse HardworkingExt（Moo.Hardworking.Kz）— 部分兼容
在勤工面板启用已解锁的战地救援及对应医疗/搬运工作即可加入，不要求动物救援训练。概率工作模式暂不参与 SAR 自动调度。
