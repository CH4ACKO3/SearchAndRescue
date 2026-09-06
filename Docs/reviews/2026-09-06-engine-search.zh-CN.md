# 实机搜索与完成时间评估

已实现真实 RimWorld 进程池、随机场景生成、逐轮治疗采集、TPE 参数建议及独立种子验证。工具说明见 [实机入口](../../Tools/SchedulerOptimizer/ENGINE.zh-CN.md)。

本机 8 核 / 16 线程，约 64GB RAM。两个隐藏 batchmode worker 分别使用独立 Config/Saves/日志目录，保留图形初始化。`-nographics` 因纹理图集 NullReferenceException 未通过；并行测试未采用该模式。worker 只省略 Gabs 桥接模组，其余临床模组保持一致。实测运行中约 1.3–1.4GB 工作集/worker，数据为当时采样，不能视为所有模组配置的上限。

逐帧 Gabs 调用推进单个 3600-tick 场景约 18–30 秒；常驻进程批量执行真实 DoSingleTick 后，两个场景并行连同读档约 3.3–3.6 秒完成。第一轮冷启动另需初始化时间。完整治疗结束的暖评估约 4–6 秒/场景，并行执行。

短窗口存在实测偏差：第一轮 3600 tick 评分偏好的候选，在完整疗程中拖慢了另一个场景。因此改为生产 NeedsAnyFieldTreatment 全部清空、相关治疗 Job 结束、维持 180 tick 后完成。24000 tick 为上限；超时和死亡分别记录，完成时间计入得分。ScoringVersion=2 与旧得分分开使用。

第二轮搜索共 8 个 trial（包含基线），2 个训练种子、2 个未参与选拔的新种子。所有比较均重载各自初始存档。训练基线复跑最大得分差 0.293，601 的时间差为 30 tick，因此报告不宣称引擎完全确定性。

| 种子 | 基线终止 tick | 候选终止 tick | 结果 |
| --- | ---: | ---: | --- |
| 601（训练） | 5940 | 5760 | 完成，候选略快 |
| 602（训练） | 8880 | 8880 | 完成，持平 |
| 603（保留） | 4950 | 4950 | 完成，持平 |
| 604（保留） | 9420 | 9420 | 完成，持平 |

上述 tick 为每轮经过时间，包含 180 tick 完成确认。所有列出的场景死亡、错误与采样到的治疗所有权冲突均为 0。另用 600-tick 上限验证超时分支：6 个患者仍有需求，结果 timeout、CompletionTick=-1，未被错误当作完成。

候选：MedicineDetourTolerance=1.60593、TreatmentSwitchReluctance=0.35660、TreatmentBeforeTransportPriority=1.86634。训练场景略有提升，保留场景未显示额外收益。维持默认参数；未把小样本结果推广到实际游玩或全部兼容模组。

本轮实际覆盖普通血肉伤员、伤口/失血、原版治疗与床位运输、当前医疗相关模组。尚需扩展感染、器材链、突发新伤员、睡眠、人手不足、机械体和更多种子；死亡分支未单独构造实机回归。

原始结果：`artifacts/engine-search/20260906-completion2`；短窗口对照：`artifacts/engine-search/20260906-pilot2`；超时验证：`artifacts/engine-search/timeout-check`。worker 场景及日志保存在工作区 `work/sar-engine-workers`，Python 环境在 `work/engine-search-venv`。
