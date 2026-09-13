# 未初始化工作设置的只读查询修复

报告：https://pastebin.com/af1xTJVN 。原调用链为 WorkerCandidates → IsFieldResponder → FieldRescueWorkPriority → WorkTypePriority → Pawn_WorkSettings.GetPriority。

原版 GetPriority 在工作设置未初始化时会打印红字并执行初始化。SAR 现在在工作类型和子工作优先级入口检查 Initialized，对存在但未初始化的设置返回 0，在调用原版、Work Tab 或 GrimWorks 的优先级查询前退出。无 workSettings 对象的机械体原有回退逻辑继续保留。

验证：Release 构建通过，零警告零错误。独立游戏进程将医生的设置替换为未初始化对象，连续执行 100 轮父/子优先级查询和 WorkerCandidates 枚举，确认返回禁用、排除候选且未初始化对象；恢复原设置后优先级与候选资格即时恢复。随后完成既有移动伤员治疗、药品选择和中断场景，结果见 standing-results.txt。日志没有出现该初始化红字或异常。

运行目录：D:/Projects/rimworld/work/sar-worksettings-fix-20260913。
