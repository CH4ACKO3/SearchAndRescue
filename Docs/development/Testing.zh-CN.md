# Search and Rescue 实机回归夹具

## 救援目的地与持有物资回归

仅在一次性测试地图或未保存的测试存档副本上运行以下开发动作。这些检查直接调用生产方法和真实 JobDriver，不复制判断规则。输出前缀为 `[SAR live regression]`，检查结果用 `PASS`/`FAIL` 表示。

1. `Run rescue destination regressions`：临时隔离床位和集合点，检查普通床位选择/送达、医疗床、集合点重复路线抑制、集合点变更和新增床位。动作内不推进 tick，完成后恢复原有床位及集合点。
2. `Prepare held-supply regression` → 精确推进 1,200 tick → `Check supply regression`：在同阵营 Pawn 背包放入 4 份药品，由真实送货 Driver 提取并送达 2 份；预期来源剩 2 份、患者旁新增 2 份、任务结束。
3. 重新载入同一原始测试存档，运行 `Prepare changed-owner supply regression` → 推进 1,200 tick → `Check supply regression`：预留后将来源物资卸到地面，预期拒绝继续拾取，来源仍为 4 份，患者旁不新增物资。

两项补给测试走实际 inventory-holder 路径。Vehicle Framework 特有的 cargo 事件/质量缓存需要另外启用该模组验证，不能由普通 Pawn 背包测试代替。不要保存测试后的地图；重新载入原始存档可清除测试生成内容。

## 所有权与任务生命周期

`Run ownership lifecycle regressions` 直接操作生产声明表、真实 Job 和 JobTracker，检查同定义 Job 回收复用、旧结束回调、阶段隔离、整组撤销，以及玩家接管后保留标记和其他伤员任务。测试会创建临时 Pawn，只应在一次性地图运行，不保存结果。

`Run external transport regressions` 检查注册运输 Job、孤立搬运清理和手动命令边界；不代替第三方模组完整游玩测试。纯规则由 `Tools/SchedulerSimulation` 直接链接生产文件，覆盖任务身份、阶段映射和工作者占用排除。

本轮结果见 [2026-09-06 所有权回归](../validation/2026-09-06-ownership-lifecycle.json)：68 项生产规则检查、原有 40 场景与 200 组随机图、38 项实机检查通过。实机补给检查含 1,200 tick 推进；未重新测量性能。

真实存档跟跑：`new20` 在原有 13 模组配置下，解除 10 人征召后运行 1,200 tick，再征召 Leah 600 tick、解除征召 600 tick。三个连续阶段合计 2,400 tick，活动主任务为 6 → 5 → 6，补给任务均为 4；22 名殖民者仍存活，12 人仍倒地，无新增运行 warning/error。多名患者出血率下降，但救援尚未全部完成。这是同一存档的连续场景验证，不是三个独立存档或完整兼容性实测。详见 [真实存档记录](../validation/2026-09-06-live-save.json)。

## 大规模战地救援

基准存档：`SAR_Test_MassCasualty_18x6x6`

该存档使用当前 16 模组测试配置，包含：

- 18 名倒地殖民者，全部标记治疗与救援；
- 6 名可执行医生，医疗技能依次为 4、7、10、13、16、19；
- 6 名只执行搬运的救援者，其中前两名也可执行狱卒工作；
- 6 个临时救治点；这些不计为永久安全救援床位；
- 工业药、草药、全血袋、生理盐水、止血剂、绷带和止血带；
- 部分患者额外具有失血性休克、心搏骤停或骨折。

## 固定复跑流程

1. 重载 `SAR_Test_MassCasualty_18x6x6`，保持暂停。
2. 精确推进 1,200 tick。
3. 执行开发动作 `Search and Rescue / Dump scheduler state`。
4. 记录存活/倒地人数、医生和搬运者当前 Job、调度器 active/logistics/pending 数量。
5. 检查从加载开始新增的 warning/error。
6. 需要观察任务结束回收时，再推进 240 tick 并重新导出状态。

首轮基线（2026-09-03）：

- 1,200 tick：0 死亡，`active=6`，`logistics=6`，无新增运行警告；
- 1,443 tick：已结束的 `UseBandage` 不再残留 active claim；
- 测试结束时游戏保持暂停，不覆盖基准存档。

## 生成并重新配置一次性测试人口

从原版开发者快速测试地图开始，执行 `Build disposable alpha benchmark population`。该动作
生成 12 名响应者、18 名倒地殖民者、12 名倒地敌对人形、医疗睡眠点及一组基础药品；
所有生成内容仅用于当前一次性测试地图。随后执行任一 `Configure benchmark ...` 动作，
它会筛选实际可工作的 Pawn、隔离工作优先级和医疗技能，并应用对应标记。执行
`Remove disposable benchmark medical spots` 可以精确移除本次会话生成的医疗睡眠点，
用于无床位回归，不会删除玩家已有床位。

## 性能基准矩阵

所有预设都应从未运行过的基准存档重新加载，不要在同一局面上依次切换。配置动作会
安全释放旧的 active/pending claim、待命者和医疗器材引用，清除旧标记，然后设置固定
人数、工作优先级和新标记；它不会恢复已经流逝的时间、伤势、床位或物资状态。

| 预设动作 | 固定规模 | 主要隔离的成本 |
| --- | --- | --- |
| `Configure benchmark - small mixed (6x2x2)` | 6 伤员、2 医生、2 搬运者 | 普通殖民地规模及低负载固定开销 |
| `Configure benchmark - treatment graph (18x6)` | 18 伤员、6 医生 | 医疗需求分析、医生/患者边评分与重匹配 |
| `Configure benchmark - rescue without beds (18x6)` | 18 伤员、6 搬运者、0 永久医疗床 | 无目的床时的负向搜索、禁止无效待命和重试退避 |
| `Configure benchmark - medical logistics (12x3x6)` | 12 伤员、3 医生、6 搬运者 | 药品/器材缺口、引用堆、隐式补给任务 |
| `Configure benchmark - capture triage (12x4x4)` | 12 敌对伤员、4 医生、4 搬运者 | 俘虏→治疗→救援的跨阶段竞争 |
| `Configure mass-casualty fixture` | 18 伤员、6 医生、6 搬运者 | 完整混合压力与长期收敛 |

预设要求对应数量的倒地目标和响应者；不足时动作只报告错误，不修改局面。推荐始终从
快速测试地图先运行一次人口生成动作，以免旧存档中的失效 JobDef 或缺失模组污染结果。

每个性能样本采用同一流程：

1. 重新加载对应的原始基准存档并保持暂停；
2. 执行一个 `Configure benchmark ...` 动作；
3. 执行 `Start/reset performance profile`；
4. 精确推进 1,200 tick，导出性能报告和调度器状态；
5. 继续推进到累计 6,000 tick，再停止采样；
6. 记录新增 warning/error、死亡数，以及仍未完成的治疗/救援/补给数量。

性能报告中的 `benchmark=` 必须与所选预设一致；若显示 `unconfigured`，该结果不能进入
横向基准表。比较性能时优先使用 `usPerGameTick`，不要只比较真实时间内推进的 tick 数。

## 性能采样

性能采样默认关闭，只在开发模式下通过以下动作启用：

1. `Search and Rescue / Start/reset performance profile`：清空旧数据并开始采样；
2. 运行需要观察的战斗或固定 tick 场景；
3. `Search and Rescue / Dump performance profile`：输出当前快照并继续采样；
4. `Search and Rescue / Stop performance profile`：输出最终快照并停止采样。

报告包含所选基准名称、逐 tick 维护、护理计划、统一匹配、单条统一图边评分、运输匹配、完整重建和
Job 唤醒的调用数、平均耗时、最大耗时、总耗时及每游戏 tick 摊销耗时；同时记录统一图
和运输图的最后/最大边数、全局/请求式/嵌套重建数量，以及 dirty 重建请求数量。正常游玩
无需开启，避免性能调试自身影响测量对象。`transportNoWorkerSkips` 记录仅执行器材引用范围
维护、但因没有实际可匹配运输者而跳过床位/待命/补给任务扫描的重建次数。
