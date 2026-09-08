# 兼容预期处理与验证

日期：2026-09-09。基于 54befb3 / alpha.5 的未发布工作区修改。

后续已实现 [Rimkit 自动包扎与 Death Rattle 撤离联动](2026-09-09-rimkit-deathrattle.zh-CN.md)。下文的未实现范围保留为该轮完成时的记录。

## 本轮修改

- 显式治疗/救援标记不再单独建立排他权。协调器有主任务或有效待分配任务时才阻止相应外部工作；治疗中的患者继续防止被提前搬走。等待治疗的搬运者不算实际治疗能力。
- 普通医生、盟友排队任务、看守和已注册设施扫描使用同一交接门控。SAR 没有可执行分配时允许外部接手；设施实际占用后继续沿用既有外部所有权。仍敌对且标记俘虏的患者保留先俘虏约束。未删除标记或强行中断现有任务。
- Work Tab 启用时，MI 的 CPR、气道管理、除颤及医生输液/输血还检查对应原生子工作。CPR 尊重原生气道管理的回退路径；护理工作继续独立授权，不因 Doctor 关闭而被取消。只映射存在的原生自动工作，不给纯手动器材虚构子工作要求。
- 权限在候选阶段与签发已选 Job 时检查；无已选方案的 CPR 回退入口也检查。细分权限更改不会靠旧的已选候选绕过。
- 更新英/简中/繁中兼容面板及英/简中详细文档，明确 Rimkit 不自动包扎、Death Rattle 不新增专属复苏，以及 MedPod 仍使用原生选舱与舱内治疗。

## 验证

- Release 构建成功，0 警告、0 错误；现有离线回归 92 项生产规则、9 项治疗连续性、40 场景及 200 随机图通过。
- 独立 RimWorld 进程加载 Harmony、Core、Biotech、CE、Smart Medicine、More Injuries、Hemogen Direct、Work Tab 和测试版 SAR，共 9 项。
- [43 项游戏内检查通过](../validation/2026-09-09-compatibility-expectations.txt)：含显式标记无任务退让、已登记治疗保护、设施接管/释放、五项真实 Work Tab 子工作关闭/重开、旧候选拒绝，以及心脏骤停患者的实际 CPR 候选变化。
- 检查末尾运行 6,000 tick，标记患者实际接受包扎且存活，没有新的 Error/Exception/Assert。这个检查验证当前组合下调度继续工作，不代表所有外部模组流程已跑过。
- 语言 XML 解析及 diff 空白检查通过。

复跑命令在 Tools/Run-CombinationProbe.ps1 新增 `-WorkTab`。实际证据目录：`D:/Projects/rimworld/work/sar-expectations-20260909/b`。测试使用独立 savedatafolder 和唯一 packageId，没有替换玩家现用模组或存档。

## 限制

本机未找到 MedPod、Dubs Rimkit 本体。本轮设施检查仍使用临时注册的真实普通床类型，验证的是共同门控与实际卧床所有权；没有验证 MedPod 本体完整入舱、功耗或治疗。盟友检查覆盖共同门控，不是 Allies are Helpful 本体救援长跑。未新增最优选舱、联合医疗小队、Rimkit 自动包扎、夹板/镇痛自动策略或 Death Rattle 专有复苏。

本轮没有推送或发布。兼容说明的变更随下一次发布生效。
