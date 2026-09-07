# Gabs 多模组组合实测

2026-09-08，通过 Gabs 启动 RimWorld 1.6.4871 rev591，使用可视游戏、一次性 quick-test 地图和正常 TickManager 推进。兼容面板未改动。

## 配置与结果

同时启用 CE、Smart Medicine、More Injuries、Hemogen Direct、Choose Your Medicine、Pick Up And Haul、Vehicle Framework、Work Tab、Common Sense、Nurse Job、Move the Patient、Priority Treatment Ressurected；加上 Harmony、Core、Biotech、HugsLib、RimBridge、SAR 共 18 项。

| 实测项目 | 结果 |
| --- | --- |
| CE + SM + CYM 随身选药、止血与配装补货 | 明确 CYM 单伤许可后，生产选择器选中医生背包工业药；原生 Stabilize 处理伤口并消耗 1 份，CE 补回 1 份。21 项检查通过。 |
| MI + Hemogen Direct + CE + SM + CYM 输血 | 两个提供者均产生需求，共享预算取最大值；同一血包只留下 MI 选项。真实 UseBloodBag 执行后血包 3→2，失血 0.65→0.2820378，任务结束。8 项检查通过。 |
| 多伤员治疗、补给、搬运 | 18 名目标伤员在 11,589 tick 观察后全部存活并处于 LayingInBed；pending/active/logistics 均为 0。 |
| 所有权、外部搬运、救援目的地 | 35 项生产边界检查通过。外部搬运中的类型识别断言不代表对应第三方模组本体全程运行。 |
| 背包物资交付和来源变更 | 6 项检查通过：4 份拆出 2 份交付；来源从背包变成地面时取消旧取物任务，原 4 份不动。 |
| Priority Treatment 唤醒与患者缓存 | 11 项检查通过，含真实模组缓存与设置开关，保留玩家强制睡眠任务。 |

合计 81 项最终有效检查通过；详见[结构化记录](../validation/2026-09-08-gabs-combinations.json)。多伤员场景名义为 18×6×6，但此前 CE 夹具的两名角色仍被征召，实际投入 4 名医生、6 名搬运者，不能当作严格隔离的 18×6×6 性能基准。SAR MapTick 平均 164.26 μs，调度重建最大 53.056 ms，仅代表该次运行。

## 夹具修正与证据范围

第一轮 CE 夹具在添加伤口前查询药品，还假定工业药不受 CYM 分类影响，产生两个失败断言。修正为先建立伤口并通过 CYM 原生单伤接口明确工业药许可；同时禁止选项为空时进入旧回退路径。重跑 21 项全部通过。新增输血夹具首次漏开 SAR 工作权限，补齐后重跑 8 项全部通过。这些都是诊断夹具修订，本次没有再改生产调度逻辑。

三个原始 Player 日志均保留，不能把早期失败行抹掉后宣称一次通过。日志扫描未发现 Exception、Error while、Could not reserve 或 10 jobs；仍有翻译数据、未启用旧探针元数据及第三方启动提示，因此不称为“零警告”。

运行期间最终 Release 构建为 0 警告、0 错误；离线回归仍通过 92 项生产规则、9 项连续性、40 场景、200 随机图。最终 DLL 哈希在结构化记录内，较前次证据的变化仅来自诊断代码。

尚未验证：150,000 tick 长跑、真实 MedPod/Trauma Team/Pharmacist、RH2 原生流程、VF 车辆登车及货舱并发。VF 本次仅验证共同加载环境。输血夹具是明确创建原生治疗任务的流程验证，不等同于自动后勤系统所有组合都已覆盖。

## 复查与恢复

原始日志、前后存档、截图、患者状态提取及测试配置保存在 `D:\Projects\rimworld\work\sar-gabs-combinations-20260908`。输血 before 存档需启用 MI Biotech 血包整合才能复现同一种物资；本轮没有把读档作为通过项。

测试后已通过 Gabs 退出游戏，恢复原先 14 项启用列表及全部 244 个配置文件，并逐文件验证与备份字节一致。安装目录保留本轮最新 SAR 构建，原安装副本在 `Runtime.before` 中。没有修改玩家原有存档。

新增开发动作 `Start/Finish combined native transfusion fixture` 只适用于一次性地图，会完成研究并生成测试角色；结束动作恢复 MI 运行时开关。CE 夹具仍使用原先的 Create/Check/Finish 三阶段动作。
