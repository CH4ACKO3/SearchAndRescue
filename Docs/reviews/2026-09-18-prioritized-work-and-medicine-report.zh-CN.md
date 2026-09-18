# Ali50：强制工作、库存送药及扩展药品报告

## 已确认并修复

SAR 的普通工作者资格检查没有排除 `mindState.priorityWork.IsPrioritized`。原版持续优先工作在两个具体任务之间仍保留该状态，当前过渡任务却可能没有 `playerForced` 标志。机械体此前有相应保护，普通殖民者缺失。现将此保护放入公共工作者控制资格检查，防止待分派和延迟唤醒在持续命令期间接管工作者；原版清除状态后重新参加匹配。

游戏探针在过渡任务上保留真实 priorityWork 状态，确认待执行补给被拒绝、延迟唤醒不替换任务、不清除玩家优先状态，清除后补给资格恢复。四个实际治疗场景及全部 129 项游戏内断言通过；离线 SchedulerSimulation 全部通过。证据见 `Docs/validation/2026-09-18-prioritized-work/`。

初次测试错误地选择了要求 `prioritizeSustains` 的 Firefighter，因原版 FightFires 没有该配置而失败。最终测试使用原版支持持续优先的 Doctor 工作。因此本次修复不能被描述为已经复现并解决了报告者的灭火问题。

## 尚未确认的两条路径

`SupplyMedicineAlternatives` 枚举地面药物和车辆货舱；普通 pawn 库存通过 `AvailableMedicines` 用于治疗及备药，并不属于自动 Supply 候选。报告中的动作可能来自治疗/备药改派或第三方调度，需要实际 job 名称、完整模组列表和存档确认，不能仅凭动画认定为多个 SAR 送药任务。

`Compatibility.AllowsMedicine` 通常委托游戏的 `MedicalCareUtility.AllowsMedicine`，因此已有第三方药物的通用入口。CYM、Smart Medicine 和 Pharmacist 会额外影响医疗策略。扩展等级如果不是按枚举数值排列，数值 Max/Min、原版效力分类等假设值得针对组合检查，但缺少报告者所用等级扩展模组及版本，暂不改变策略或宣称支持完成。查到的 Mod Medicine Patch 历史源码会扩展等级并 patch 许可方法，不等于已经确认报告者使用该版本。

继续复现需要完整模组列表、Player.log，以及最好包含伤员、所选医疗等级、可用草药/工业药和被强制工作的 pawn 的存档。当前只提交已验证的持续优先工作修复，不发布新版本。
