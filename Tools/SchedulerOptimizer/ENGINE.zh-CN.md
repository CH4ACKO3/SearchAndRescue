# RimWorld 实机权重搜索

评估使用 RimWorld 的真实 tick、寻路、医疗 Job 和伤情变化。Python 使用 Optuna 的 TPE ask/evaluate/tell 接口；场景生成与评分位于 `Diagnostics/EngineBenchmarkDiagnostics.cs`，文件队列位于 `EngineBenchmarkWorker.cs`。默认权重不会自动修改。

## 隔离与并行

`Start-EngineWorkers.ps1` 默认启动 6 个进程，可设置 1–12 个。每个 worker 使用独立 `-savedatafolder`，包含 Config、Saves、日志和文件队列。所有 worker 运行期间应保持游戏与模组文件固定。用 `workers.json` 的 PID、目录和命令行确认目标后关闭测试进程。

`-NoGraphics` 加入 `-batchmode -nographics`。仅同时带 `-sar-bench-worker` 和 `-nographics` 的进程安装早期渲染适配：跳过静态图集插入/烘焙、地形图标裁剪、OnGUI，并关闭地图绘制分支。地图的电力、区域、光照、天气、组件更新和实际游戏 tick 继续执行。普通游戏不安装这些补丁。

Unity 的 Null 图形设备仍会打印 shader 不受支持的启动信息。读档或运行期间的 C# 错误不会被吞掉；加载错误输出 `.error`，运行错误和治疗所有权冲突使候选无效。适配已在纯原版和 CE 配置上开展实测，其他模组仍需分别验证。

worker 每帧用最多 20ms 连续执行 `TickManager.DoSingleTick`，然后返回常规引擎帧循环。每个 tick 有独立 Rand scope，但模组静态缓存和帧更新仍可能造成波动。无图形与有图形必须使用完全相同的初始存档作对照，不能仅凭相同 Seed 认定状态一致。

## 配置与模板

先使用独立测试运行时和数据目录。纯原版配置只启用 Harmony、Core 和 SAR；兼容配置按层加入 CE、More Injuries 等。`-Profile Vanilla` 过滤其他模组；默认 `Source` 复制来源配置并移除 RimBridge，避免 Gabs 注册冲突。脚本登记所有官方 DLC 为已知内容，防止 RimWorld 自动启用新发现的 DLC。

`New-EngineProfile.ps1 -ModDir <源码模组目录> -RuntimeDir <游戏Mods下的新目录> -SaveData <新测试数据目录>` 会复制完整运行文件（包括 Patches 和 LoadFolders.xml）、生成纯原版配置和文件哈希清单。可用 `-AdditionalMods ceteam.combatextended` 创建 CE 配置；依赖需按加载顺序一并列出。只在测试配置中启用这个独立运行时。已创建的运行时可供同一版本的多个隔离配置共用，更新前先关闭使用它的 worker。

模板必须由相同模组配置创建。可启动独立进程并传入 `-quicktest -sar-bench-worker -sar-bench-template`：地图生成后自动暂停、保存 `SAR_Engine_Template.rws` 并退出。该命令也可带 `-batchmode -nographics`。先检查模板日志和存档头部的 modIds。

worker 收到未生成的种子时，加载模板、生成场景、保存，再重新加载初始存档；后续参数复用该文件。读档前严格核对存档与进程的模组列表及顺序。场景文件名包含生成器版本，例如 `SAR_Engine_stress-v1_701_Initial.rws`，与旧版场景分开。

不同模组配置分别创建模板、worker 目录和报告。增加伤病机制时应创建新的场景版本，保留既有初始存档，避免旧种子含义改变。

## 场景

`stress-v1` 为默认压力场景：4–5 名医生，医疗技能覆盖 5–17；0–4 名专职搬运者；28、34 或 40 名倒地患者。患者有 5–8 处四肢割伤、0.55–0.88 初始失血，随机散布在中心附近。药品数量与患者数相同，另有医疗睡眠点。生成时死亡会直接报错，不通过预先杀死患者制造死亡指标。

`routine-v1` 保留 3 名医生、6 名伤员的常规回归，用于检查完成时间和低负荷表现。地图中原有殖民者的工作被关闭，保持新增医护与搬运者的救援容量可控。

生成器目前覆盖割伤、挫伤和失血。CE 使用真实稳定流程；MI 专用器材、感染、机械体、延迟伤员和不同地形仍需增加专门场景。不同配置的生存人数只用于本配置中的参数对比，不能直接当作模组间效果排名。

## 终止与评分

`ScoringVersion=3`，与旧版分数不混用。

- 压力场景运行完整观察窗，默认 24,000 tick，输出 `observed`。死亡后继续救治其他患者，观察窗末记录存活、死亡、仍需战地治疗和已完成治疗的人数。
- 常规场景在存活患者全部完成可执行战地治疗、且无相应治疗 Job 连续 180 tick 后结束为 `completed`；达到上限为 `timeout`。
- `CompletionTick` 记录当前有效的完成确认区间起点。治疗需求再次出现时撤销完成记录。患者死亡本身不计作完成治疗；死亡患者单独计数。
- 运输不属于治疗完成条件。24,000 tick 后的长期感染、后遗症与生存结果需要额外长期评估。

单场得分：`1000 × 存活人数 + 次级分`。次级分上限 124：已完成治疗的存活患者比例最多 100，低失血负担最多 10，短首次有效治疗等待最多 5，完成时间最多 5，少药耗最多 2，少换医和短移动距离各最多 1。

整组搜索目标：`1000 × 各场存活人数总和 + 平均次级分 + 10 × 最差场景存活率`。因此整组多救活一人始终优先于所有次级加分。死亡是压力场景的有效临床结果；引擎错误和所有权冲突是硬约束。报告保留逐场结果，以检查总体提升是否伴随某类场景退步。

失血按固定观察上限归一化，死亡后按最大负担累积。首次治疗与换医计数来自实际 Tend 回调；CE 稳定和 MI 器材操作尚未统一计入首次干预事件。移动距离是每 30 tick 位移采样。药耗是地图及 pawn 库存/手持药品的净减少量，运输本身不会扣分；这个指标适用于当前没有销毁/补充药品事件的场景，未来新增此类事件时要改为实际消耗记录。

## 运行

```powershell
./Tools/SchedulerOptimizer/Start-EngineWorkers.ps1 -GameExe 'D:/.../RimWorldWin64.exe' -SaveData 'D:/.../vanilla-source' -WorkerRoot 'D:/.../workers' -WorkerCount 6 -Profile Vanilla -NoGraphics
python Tools/SchedulerOptimizer/engine_search.py --workers D:/.../workers/worker0 D:/.../workers/worker1 D:/.../workers/worker2 D:/.../workers/worker3 D:/.../workers/worker4 D:/.../workers/worker5 --scenario stress-v1 --seeds 701 702 703 704 705 706 --holdout 707 708 --trials 8 --horizon 24000 --output artifacts/engine-search/new-run
```

Python 依赖见 `requirements-engine.txt`。输出目录必须不存在。每轮并行评估同一候选的多个场景，然后给 TPE 完整聚合结果。worker 应先到达 `SAR_EngineBench/ready`。基础设施超时为 180 秒，超时停止本次搜索；较重配置应先校准成本。

基线及重复基线估计波动；最佳候选再确认一次，再比较独立保留种子。前 4 个 trial 是启动采样，所以仅 4 个 trial 属于流程校准。小样本通过不代表可更改默认值；扩展新种子、重复次数、常规/压力混合场景和兼容配置后再决策。固定保留种子一旦用于调试，就不能一直视为未知测试集。

原始 XML、场景/配置 SHA256、参数、逐轮事件、耗时、SQLite 和报告全部保留。启动日志中的渲染警告与评估阶段临床错误分别检查。
