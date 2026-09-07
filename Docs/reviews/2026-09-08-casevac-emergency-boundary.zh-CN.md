# CASEVAC 自动协作与战地急救边界

日期：2026-09-08。当前工作区实现，尚未发布。

## 行为

CASEVAC 仍是独立工作类型。SAR 自动协作要求同时开启 Field Rescue 与 CASEVAC；Work Tab 下还需保留 CASEVAC 原生协助子工作与新增协调子工作。优先为未被安排治疗或搬运、可达且有目的地的患者发起原生 CASEVAC。没有此类患者后，加入已有队伍，或接管 SAR 自己的普通 Rescue/Capture 搬运。沿用原生最多四人及移动速度限制，不修改右键命令的原生召集行为。

升级保留原来的床位；目前限定接管者距离搬运者不超过 8 格、搬运者距离床位至少 12 格且接管者不更慢。交接先安全放下患者、结束旧搬运，再登记接管者。不会强行给旧搬运者开启 CASEVAC，不接管征召或玩家强制搬运，不升级救援点运输。无可用 CASEVAC 工作者时，普通救援仍然工作。

SAR 紧急治疗阶段只处理当下允许治疗的急症。非紧急包扎等待安全床位或玩家指定救援点；普通医生、右键医疗命令、外部医疗设施和机械维修保留各自流程。这是 SAR 的工作边界，不是全局禁止原版医生现场治疗。

Doctor 新增高优先级子工作 **provide emergency medical care / 执行紧急医疗**。Work Tab 下可给急救员开启它与 Field Rescue 的急救协调，关闭 Doctor 的其余子工作；普通医生保留常规治疗、感染治疗及手术等需要的工作。常规 SAR 补治分别检查人形/动物治疗子工作。无 Work Tab 也能运行，只是无法在父工作内独立分配这些子工作。

## 紧急症状覆盖

判定读取实际加载的 Hediff、阶段、出血量、免疫机制和原模组操作条件，不搜索翻译后的疾病名称。NoCare、治疗冷却、隐藏/永久伤、研究与设备权限仍有效。

| 来源 / 症状 | 现场处理边界 |
| --- | --- |
| 原版及模组的可治疗出血伤：Cut、SpontaneousBleeding、SpallFragmentCut、BoneFragmentLaceration 等 | 进入急救；CE 启用时，可稳定出血先走其原生 Stabilize。稳定流程不被拆成每伤口一个 Job。 |
| WoundInfection；具有免疫组件及致死严重度的疾病，如 Flu、Malaria | 不等待进入生命危险阶段；当前可治疗且未免疫时进入急救。达到免疫或处于治疗冷却时不重复包扎。 |
| 其他当前阶段 lifeThreatening 且可治疗的疾病 | 纳入急救；例如 More Injuries 的 HemorrhagicStroke。 |
| More Injuries：HypovolemicShock | 明确包含早期休克；其 CompTended 能在 lifeThreatening 阈值前稳定病情。输液/输血仍按已有血容量需求、研究与物资规则选择。 |
| CardiacArrest、ChokingOnBlood、HeartAttack | 保留 CPR、吸引器、除颤/肾上腺素等专用干预。不能普通包扎不会使这些急救被过滤；心脏病发作不被错误地当成 CPR 可治疗目标。 |
| ChokingOnTourniquet、TourniquetApplied | 颈部止血带正在造成窒息时，允许优先解除，即便颈部还有未治疗伤口。肢体止血带仍按其所在肢体的出血情况安全移除。 |
| BloodLoss、HypovolemicShock、Hemodilution | 保留 MI 生理盐水/血液、血原包等有界补液需求；不因存在持续恢复中的 Hediff 而无休止输血。 |
| LungCollapse、GangreneWet | 紧急撤离。肺萎陷走已有手术识别；湿性坏疽需要截肢，增加专门识别，因为截肢不表现为直接 removesHediff 配方。SAR 不自动创建手术账单。 |
| Acidosis、Coagulopathy、缺氧相关损伤 | 不能靠普通包扎处理的状态不生成空治疗任务；保留生命危险评分，以及对出血、休克、供血不足等可处理病因的既有干预。没有专门的酸中毒/凝血障碍治疗 Job 集成。 |
| Death Rattle：ClinicalDeathNoHeartbeat、ClinicalDeathAsphyxiation、LiverFailure、KidneyFailure 等 | 生命危险评分提高撤离优先级；不宣称普通包扎或 MI 的 CPR 可以直接修复 Death Rattle 器官衰竭。供氧、器官恢复和设施治疗仍由对应系统负责。 |
| Crack、Bruise、轻度 Burn、稳定骨折/脑震荡等 | 不仅因“可以治疗”就在战地补治。普通包扎等送达合理位置；MI 夹板等原生专用操作仍按其原生调度和所有权运行。 |
| EPOE-Forked 的 AI glitch / EMP 后遗症 | 本地 XML 检查显示有免疫组件的 glitch 不带致死阈值；不会仅因具有免疫组件或名称叫 critical 就加入急救。MSE2 本地 XML 未发现需要新增此类急症映射的定义。 |

SM/CYM 改变的是医疗许可与药品策略。RH2 First Aid 经反编译确认调用原版 FinalizeTend，因而进入相同的 DoTend 边界；CE 稳定本身要求实际出血。More Injuries 的复合 ProvideFirstAid 会继续处理小伤，因此 SAR 保持使用单次专门干预，而不使用这个复合任务串联全部伤口。

## 验证

- 编译：Release，0 警告、0 错误。
- 离线回归：92 项生产策略、9 项连续治疗、40 项调度场景及 200 个随机图通过。
- Gabs 8 模组环境：自动发起 CASEVAC、自动组队、送床和任务退出；普通搬运接管的 5 项检查通过，包括不抢手动任务、不自动授权原搬运者。
- Gabs 21 模组组合：Work Tab、Nurse Job、CE、SM、CYM、MI、血原包、RH2 First Aid、CASEVAC、Death Rattle、Common Sense、PUAH、Move the Patient、Priority Treatment、Vehicle Framework 等。24 个实际疾病定义及临床/地点/药耗/工作权限检查共 50 项通过，随后自动 CASEVAC 再次组队、送床、退出。
- 正常工作树实测：仅开启新急救子工作的医生，自主完成出血伤治疗，保留小伤未治疗且常规医生子工作仍关闭。CE 多阶段处理在约 11000 tick 的检查时完成。

测试后已停止游戏并恢复原配置：244 个文件的相对路径与 SHA-256 全部匹配，原 14 模组列表恢复。最新 DLL 已同步至本地游戏模组目录。

原始日志位于 `D:/Projects/rimworld/work/sar-next-features-20260908/`。保留初次失败记录：CASEVAC 最早夹具缺少床位阵营/有效休息需求；升级初次检查早于拾起患者；混合伤口初次测试受到 CYM 的 NoCare 默认规则干扰；自主医生首次 4000 tick 检查早于完成。后续明确设置夹具条件并验证最终效果，没有通过放宽生产边界消除这些失败。坏疽配方遗漏属于实际发现并修复的问题。

覆盖限于所列版本、条件与组合。未逐项实测所有设备治疗效果、死亡复苏结果、EPOE/MSE2 完整种族流程或跨地图运输；这些不得由此推导为全部通过。未来模组新增既不出血、也不标注生命危险/致死免疫机制的特殊急症，需要核对其真实治疗 API 后增加适配。

## 本地审查来源

- CASEVAC `2563153311/1.6/Defs/JobDefs/Jobs_Misc.xml`、`Source/Casevac/WorkGiver_Casevac.cs` 与原生 JobDriver。
- More Injuries `3348840185/1.6/Defs/HediffDefs/`；`HealthConditions/HypovolemicShock/HediffComp_Shock.cs`、`HeavyBleeding/Tourniquets/JobDriver_RemoveTourniquetBase.cs`。
- Death Rattle `2896207870/1.6/Defs/HediffDefs/Hediffs_Global_DeathRattle.xml`。
- EPOE `1949064302/1.6/Defs/HediffDefs/Hediffs_Injuries.xml`；MSE2 `2056706586` 的本地 XML。
- CYM `2937201140/1.6_Source/ChooseYourMedicine/Tendings/Harmony_Hediff.TendableNow_Postfix.cs`。
- RH2 `2563152474/1.6/Assemblies/FirstAid.dll`；CE 已有本地反编译 `CE_Utility.CanBeStabilized` 与 `JobDriver_Stabilize`。


## 演示反馈后的 CASEVAC 修复

用户报告普通 Bed 送达时重复释放预约，并质疑多人是否真正协同行走。核对本地 Casevac.dll 后确认：原生队员缓存的移除条件使用 AND，换去搬运另一患者的人可能残留；原生送达既释放队伍预约又执行 Release，且 ReleaseAllForTarget 可能波及其他预约。SAR 的加入判断还误用了团队加速后的公开速度属性。

现在按当前患者与床位重新计算原生队伍，仅在原生 CASEVAC toil 初始化范围内使床位释放幂等，并保留非本队的预约；加入条件读取与原模组相同的基础 TicksPerMove。原生路径跟随和加速公式保持原样。它的表现是一人携带、其他人靠拢跟随并加速，不是分列抬担架。

21 模组环境复测：4 项队伍/共享床位预约检查通过；自动组队、送床与退出通过，记录到 29 次有协助者跟随且携带者获得加速的跨格移动。日志：`work/sar-interactive-demo-20260908/Player.casevac-fix-pass.log`。此前仅检查队伍 Job 数量不足以证明实际协同行走，本次增加了位置、移动状态与基础/实际移动耗时的验证。

### 后续空引用与结束回调修复

上述第一轮检查没有覆盖中途退出，用户继续运行后仍报告 `ReservationManager.ReservedBy<TDriver>` 空引用及患者 LayDown 预约失败。进一步核对发现，原生 `ReleaseAndMakeOtherReserveBed` 在 JobTracker 已清理预约、但 CurJob 尚未清空的 finish action 中，可能给正在结束的自己重新预约。Job 随后进入对象池、def 清空，残留预约便会触发床位查询空引用或在对象重用后表现为多条错误预约。前一轮消除重复释放并不足以解决此生命周期问题。

现在结束回调只向同患者、同床位且尚未结束工作的其他队员转交预约；最后一人退出或患者已送床时不再预约。搬起前也重新检查患者预约，避免行走期间医生已预约患者后仍强行搬起。没有通过吞掉 FindBedFor 异常或全局清空预约来绕过问题。

最终 DLL SHA-256：`6B35545334531990410C74CE46320FE1F7CC5B146DD0726FEA82BE341893256D`。21 模组环境最终验证：

- 10 项预约检查全部通过，包含连续 8 次取消后对象池安全查询、队员退出时交接、最后一人退出、医生预约竞争及共享床位保护。
- 干净独立场景推进 4915 tick，自动组队、19 次实际跟随加速、存活送床、队伍退出、无残留团队床位预约、所有预约 Job 有效全部通过。
- 重新加载五患者演示起点后推进 27122 tick，未出现空引用、预约失败、重复释放或 FAIL 日志。结束存档的 50 条预约均能解析到有效 Job，CASEVAC 预约为 0。
- 日志：`work/sar-interactive-demo-20260908/Player.casevac-lifecycle-pass.log`；结束存档：`SAR_CASEVAC_Emergency_Demo_20260908_Verified`。生成演示角色留下的两条 discarded social-reference 保存警告另记，未将其称为完全无警告。

中间测试修正了随机队员速度/相对位置不满足原生加入条件的问题。把独立夹具叠加到五患者场景会竞争其他床位，切换主菜单时过早要求 playable 也曾返回尚未卸载的旧地图；最终独立测试从重启后的主菜单生成新地图，并等待 visual-ready，未把这些中间结果当作通过。

此轮结束时游戏保留开启，演示起点已重新加载并暂停，21 模组配置有意保留供用户检查；原配置备份位于 `work/sar-interactive-demo-20260908/Config.before`。此前章节的“已恢复配置”描述的是上一轮任务，不是当前交付状态。

### 无可用床位检查

无可用床位时，有可达 SAR 救援点就生成 `SAR_EvacuateToPoint`，CASEVAC 工作开启也仍走单人回退；没有救援点则不生成搬运工作。已抵达救援点不反复搬运，新床出现后可继续送床。急救资格与搬运目的地独立判断。

补齐加入/接管前的床位存活、用途、禁用和可达检查，以及结束交接时的床位有效性检查。避免在床已失效时先中断原搬运再尝试 CASEVAC。

最终 DLL `63CC73D8C83249086DEB91F6FEA9204DBCBBBD13D81A457B4A89ACC6C6DE524A`：21 模组环境 11 项目的地检查通过（包括床位全部被预约、无床无点、无床有点、床改为囚犯用途）；动态夹具在实际拾起患者后销毁床，推进 5431 tick，4 项结果检查全部通过：患者抵达救援点、退出 CASEVAC、无对象池残留预约。日志 `work/sar-interactive-demo-20260908/Player.casevac-no-bed-pass.log`。中间夹具曾试图禁止没有 Forbiddable 组件的 SleepingSpot，以及使用不支持的 ForPrisoners=false 恢复方式；已修正夹具，最终日志无这些错误。游戏重新加载原演示起点并暂停。
