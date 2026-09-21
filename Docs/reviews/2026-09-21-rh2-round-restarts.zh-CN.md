# BCD 急救读条提前结束

FrostFlash 报告战地急救读条只走一小段便重启，伤口没有治疗效果。尚未拿到其模组列表和存档，不能确认其具体组合，但在检查中找到并复现了 SAR 与 RH2 BCD: First Aid 的一处提前结束缺陷。

## 原因与修复

`MonitorActiveTreatmentRounds` 对聚合急救驱动调用 `TreatmentProgressMade`。旧判断把失血严重度下降、出血减少等变化当作治疗完成证据。这些变化也可能由自然恢复或其他效果造成，此时 RH2 的读条尚未完成。SAR 却以 Succeeded 结束任务，下一轮还需要急救，因此可以重新分派同一患者。

本地 RH2 1.6 的 `FirstAid.JobDriver_PerformFirstAid` 在等待结束后调用原版 `Toils_Tend.FinalizeTend`，最终通过 `TendUtility.DoTend` 提交治疗。SAR 现为 `CP_FirstAid` 记录该提交回调，只在已提交治疗时判定本轮有进展。继续使用下一次监控结束聚合任务，避免在第三方驱动调用栈中直接打断。原版 TendPatient 的连续治疗策略不变。

本修复针对 `CP_FirstAid`。More Injuries 的聚合和特殊设备流程没有据此改写，也不宣称所有无效读条问题均已解决。

## 对照测试

同一版本测试探针，加载本地 RH2 1.6 原 DLL。两个人形 pawn、未处理割伤及 0.2 失血，在 RH2 实际驱动的等待阶段把失血严重度降低 0.001。伤口保持未治疗。

- 已发布 alpha.13 DLL：患者状态不变时保留任务；失血下降后立即结束，`passive blood-loss recovery does not restart RH2 first aid` 断言失败，确认旧行为。
- 修复 DLL：下降后保留原 Job ID，继续执行实际驱动，伤口获得治疗，提交次数增加，随后结束本轮。
- 修复版含 BCD 的完整 StandingTreatmentProbe 共 160 项断言通过，含四个原版站立/移动伤员包扎场景。
- Release 构建零警告，SchedulerSimulation 全部通过。

此为在游戏进程中运行的同步驱动测试：明确调用寻路到达回调，再推进真实治疗 toils；并非使用玩家存档的长时间复现。测试开发阶段修正了未启用医生工作、快照初值、随机地图站位和 InteractionCell 要求，最终固定到原生交互位置，并先验证原始快照不会提前终止。基线与最终修复比较使用同一探针。

原始结果和双方 SAR、RH2 DLL 哈希见 `Docs/validation/2026-09-21-rh2-round/`。本次仅本地提交，未发布。
