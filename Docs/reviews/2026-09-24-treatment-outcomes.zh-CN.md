# 重复空治疗：历史修复与完成判定审计

## 结论

在已发布 alpha.14 中确认了通用的假成功问题：DoTend 被 SAR 自己的急症前缀跳过时，SAR 后置回调仍把 CommittedTreatmentRounds 加一、设置 RoundEffectSeen。药物与伤口均未变化。基础配置和 Smart Medicine（PES7）+ Colony Hospital 配置都出现同一结果。

这不是玩家此次持续循环的完整复现，不能断言其存档由这个触发条件造成。普通囚犯现场止血、入床、常规补治，在两组当前配置中均成功。仍需要玩家的确切 Smart Medicine 分支、版本，以及故障时存档或日志；“治疗结果写入失败”仅是玩家转述的推断，当前没有发现序列化写入失败的证据。

## 历史问题并非同一个触发器

| 记录 | 当时触发条件 | 与本轮的联系 |
| --- | --- | --- |
| 2026-09-06 Maker 止血带 | 已绑止血带肢体仍生成候选，第三方任务空成功 | 已为 UseTourniquet 单独增加实际效果检查；通用成功推断仍存在 |
| 2026-09-10 囚犯床上补治 | 通道匹配包含 FollowupTreat，却被当作精确 Treat，误套急症过滤 | 分类修复仍在，但过滤跳过后仍发出成功通知 |
| 2026-09-09 AUR | CE 稳定与正式治疗不同，且整包消耗绕过多次使用机制 | 属于不同协议/预期问题，不能把所有类似报告归成同一 bug |
| 2026-09-21 BCD | 失血自然恢复被当作急救生效，提前结束进度条 | CP_FirstAid 已改用提交计数，但计数本身也依赖未经效果确认的 DoTend 后置通知 |

共同薄弱点是候选可执行条件、执行过滤、实际效果和调度成功记录之间缺少一致的约定。

## 当前代码路径

1. FieldTendScopePatch.Prefix 在受限治疗且没有急症伤口时返回 false，合法地阻止 DoTend 消耗药品或处理普通伤口。
2. TendUtility_SearchAndRescueCommittedRoundPatch.Postfix 不检查是否实际处理伤口，直接调用 NotifyTreatmentCommitted。Harmony 跳过原方法时后置补丁仍执行。
3. NotifyTreatmentCommitted 对匹配任务无条件设置 RoundEffectSeen、递增 CommittedTreatmentRounds；FollowupTreat 还会请求在此轮结束。
4. TreatmentProgressMade 看到 RoundEffectSeen 就判成功。普通 TendPatient 还沿用失血/严重度自然下降等通用兜底，不能仅修 postfix 就认为所有假成功都消失了。
5. FinishTreatmentRound 在成功路径刷新医生亲和、清除重试记录、要求重新调度。在持续存在候选/执行不一致时，这会维持重复派工。

另一个静态风险：FindTreatmentOptions 按患者所在位置生成包扎候选，SelectTreatmentOption 的 stage 主要参与评分，执行端 RestrictRound 则按任务阶段限制急症。输血/专用抢救需求使患者进入 Treat 时，床上普通伤口仍可能成为包扎候选。此风险本轮没有构造完整游戏复现，不当作本次玩家原因。

## Colony Hospital 原包检查

工坊 3774795066，packageId Jianyuan.ColonyHospital，2026-09-24 匿名 SteamCMD 下载当前包并反编译；原始第三方 DLL 不入库。

其 DoTend 补丁是记账 postfix，没有发现直接跳过治疗的 prefix。医院专用床过滤针对非医院患者，但 CanDesignateAsHospitalBed 明确排除囚犯床，因此不能据此认定它导致本次囚犯问题。

其记账也以 DoTend 返回作为完成信号，若 SAR 跳过一个已登记医院患者的治疗，存在误记费用的静态风险。此次场景是普通俘虏，未验证医院登记患者的记账，不宣称该风险已实测。

## 实测与测试盲区

使用与 alpha.14 发布包 SHA256 相同的生产 DLL、独立 quicktest 配置，无 BCD。

- Harmony + Core + SAR：15 PASS、0 FAIL，实际完成现场止血、俘虏入床、普通挫伤补治。
- 再加入原包 PES7 Smart Medicine 与 Colony Hospital：15 PASS、0 FAIL，同一流程完成。
- 两组沿用 CheckBedStages 的边界夹具：当前床上仅剩普通伤口，显式注册 Treat，然后真实调用 DoTend；伤口应不治疗、药物应不消耗。新增外部观测同时读取调度器记录。

两组均记录：

```
stage=Treat restricted=True untended=1->1 medicine=2->2 commits=0->1 effect=True
stage=FollowupTreat restricted=False untended=1->0 medicine=2->1 commits=0->1 effect=True
```

第一行证明假成功；第二行是正常补治对照。边界夹具直接设置阶段，不等于证明真实调度一定会派出第一行这种任务。不能将这个单次假成功观测表述为完整无限循环复现。

旧测试正确检查了执行边界、伤口和药物，却没有检查未治疗时的完成计数，因此 15 项通过不能证明调度结果记录正确。最初观测器用字段读取 activeByTarget（实际是属性），未取到 assignment；修正反射读取后重跑，入库证据均来自后者。

证据位于 Docs/validation/2026-09-24-treatment-outcomes，观测器源码位于 Tools/TreatmentOutcomeProbe。仅向隔离 runtime 的 Assemblies 复制 Audit.dll，配合 -quicktest -sar-capture-probe 运行；不要把观测器放入发布包。没有修改玩家存档或活动模组配置。

## 建议的根治范围

1. 统一按治疗阶段生成候选、开始前重检及最终伤口过滤，避免同一方案在调度端可执行、结算端却不可执行。
2. 用同步治疗前后伤口状态/治疗计时变化确认实际效果；不能只看 DoTend 返回、__runOriginal、药量变化或失血下降。替换原方法的第三方前缀也可能成功治疗，多次使用药品也可能不减少堆叠数。
3. 无效果单轮应在安全的任务边界结束，并走有限重试/退避，不刷新成功亲和。区分患者已无需治疗和仍有可治疗伤口两种情况。
4. 增加调度结果断言、故意跳过原方法的对照、替换式治疗、多次使用药品、自然恢复、已治疗疾病重新包扎等测试，并覆盖实际 JobDriver 连续运行。

本轮为审计：没有修改生产逻辑，也没有推送或发布。
