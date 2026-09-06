# Maker 止血带后停滞：现场回归

2026-09-06，AllTending，用户实际 13 模组存档，More Injuries (Continued) 启用。

- 现场备份：`SAR_Maker_Repro_20260906`，tick 161176。Maker（Human77713）出血评分 3.0966；Willbord 正在取下一份止血带。
- 旧版推进至 tick 163576、163876：出血评分 0.226，已绑止血带的肢体仍进入 Tourniquet 候选。UseTourniquet 每轮空成功，医生停在 Wait_MaintainPosture，治疗连续性反复刷新到 600 tick。循环现场另存 `SAR_Maker_Loop_20260906`。
- 根因：筛选仅检查残余出血和肢体，没有排除 TourniquetApplied；MI 的 RequiresTreatment 对已绑肢体返回 false，任务可以成功结束；SAR 将第三方 Job 的 Succeeded 直接记为治疗进展。
- 修复：需求计算与任务构造共用的肢体筛选排除已绑肢体及当前不可治疗的伤口；UseTourniquet 以实际新增止血带 Hediff 数量确认进展。空成功进入现有重试退避，不刷新成功治疗连续性。
- 新版从循环存档推进 12900 tick：移除重复候选，Maker 获得撤离、普通包扎和安全拆除止血带；tick 171377、173177、176777 均由 Wolf 接续处理，出血评分最终 0.004。结果另存 `SAR_Maker_Fixed_20260906`，仍有后续医疗需求，未宣称完全康复。
- 新版从更早的原始现场再推进 2400 tick：未绑肢体的止血带操作保留，随后 Willbord 于 tick 163429 接续 UseHemostaticAgent；无重复止血带候选。
- 首次响应时间仍受人员可用性影响：现场多数医生执行 LayDown，另有精神状态和其他患者任务。此轮未调整睡眠、娱乐和工作抢占策略。
- 重载原始现场出现已有的 MI TourniquetBaseParameters 重复注册日志；任务仍可继续。该存档序列化问题尚未修复，不计为本轮通过项。
- 构建 0 警告 / 0 错误；模拟器 76 项生产规则检查、9 项连续性检查、40 个场景及 200 个随机匹配图通过。此次止血带修复的直接证据来自上述真实存档回归。

部署 DLL SHA256：`5F440C360416AF8C5AAF61C47381123BD739AE308C74652AF2261A61E6E0FA18`。
原始日志保存在工作区 `work/sar-maker-20260906`。
