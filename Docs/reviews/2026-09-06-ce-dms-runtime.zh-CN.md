# CE 战备物资、DMS 与机器人实机验证

2026-09-06 · RimWorld 1.6.4871 · 25 模组组合 · [机器可读证据](../validation/2026-09-06-ce-dms.json)

## 实测结果

| 项目 | 结果与范围 |
| --- | --- |
| CE 本人战备药品 | SAR 选中本人库存，执行原生 Stabilize；目标伤口的 CE Stabilized 状态生效，原药堆 2→1，CE 随后精确补回 2 |
| CE 战备特殊器材 | WholeBloodBag、SalineBag、HemostaticAgent、Bandage、Defibrillator 的库存选择和 MI 原生任务构造通过；本轮没有逐项运行完全部特殊医疗操作 |
| CE 卸载与补货 | 配额内物资保留；最近的 SAR 保护药堆会被跳过，CE 可继续选更远的普通库存 |
| DMS 本体维修 | Lady 被识别为机械患者；协调器签发 RepairMech，运行 1200 tick 后损伤 10→2 |
| DMS 医护 | 调度图选中 Lady 为人类伤员执行 TendPatient；Bruise 实际完成包扎。Maiden 的工作许可与可用治疗边已核对 |
| DMS 敌方机械体 | 活着且倒地的敌方 Lady 被正确排除在人形俘虏命令之外 |
| DMS 特殊维修所有权 | FFF_RepairMech_Overseer、Tinker_RepairAutomatroid 运行时登记为 Treatment；Tinker 的原生工作扫描接入门控 |
| Paniel | 按本体规则完成无药原生包扎 |
| Androids ChjDroid | 选中相邻维修零件，原生处理消耗零件并移除伤口 |
| Androids ChjAndroid | 从医生库存选普通药品，完成 CE 稳定 |
| Expanded ChjSpacerDroid | 选中维修零件，原生处理消耗零件并移除伤口 |
| 保存重载 | 最终存档重新加载成功，再运行 600 tick，没有新增游戏错误 |

最后一轮累计运行 4800 tick，另有重载后的 600 tick。DMS 医护检查在任务仍运行的 1200 tick 时曾提前报未完成；再推进 2400 tick 后同一任务通过。第一轮人物生成/命名、伤口效果判定和物资可达性问题属于诊断场景，已修正后重跑。

## 本轮修复

- CE 原有补丁会在最近来源受保护时抛弃整个补货任务。现在在 CE 原生搜索期间过滤受保护来源，让其继续按自己的距离、容量与物资规则选择候选。
- 补齐 FFF 和 Tinker 两个专用维修 Job 的治疗所有权登记，并将 Tinker WorkGiver 纳入任务开始前的所有权门控。
- 新增可保存的 CE、DMS、Paniel/Androids 诊断场景；兼容性说明沿用既有章节，补入 DMS 六个主要模块并更新已验证范围。

SAR 仍可在持有者可提供物资时共享其他 pawn 的日常携带补给；CE 战备配额并非独占库存。改变这项策略会影响抢救时可用物资，本轮保持现有行为。

## 限制与上游问题

- DMS Core、Synthetic、MobileDragoon、AncientCorps、Motorized、Joint Operations 全部在此组合中加载；外骨骼维护、车辆维修/乘员流程、失活机体接管和复活等仍由本体处理，未逐个执行完整流程。
- Tinker 的 6 点带宽超出测试机械师剩余的 2 点，本场景保留未受控 Tinker 用于查看。其并发维修 Driver 与 FFF 维修 Driver 的全程竞争仍待专项测试；本轮验证登记和扫描接入。
- Paniel 完整床边维修、Androids 手术维修及其他 Expanded 种族仍待专项测试，因此保留“部分兼容”。
- 组合启动时发现三项 DMS 武器预算低于 CE 武器价格，及 Explosion_Small/Pawn_Melee_Punch_HitBuilding 音效引用缺失。详见 [DMS 审计](2026-09-06-dms.zh-CN.md)。本轮没有修改第三方模组文件。

Release 编译 0 警告、0 错误；76 项直接生产规则检查、40 项调度场景、200 组随机图通过；21 个 XML 文件可解析且翻译键无重复。

## 测试存档

游戏 Saves 目录中保留 `SAR_CE_DMS_00_Base_20260906`、`SAR_CE_DMS_01_Ready_20260906`、`SAR_CE_DMS_02_Tested_20260906`。01 包含已启动的治疗与维修任务，02 保留治疗结果及 CE 战备栏设置。

本地 `work/sar-ce-dms-20260906` 保存测试配置、原配置、模组清单和逐轮日志。游戏关闭时用其中的 `Select-TestProfile.ps1 -Profile Test` 切换测试配置；使用 `-Profile Original` 恢复原配置。调试动作的 Start/Finish 配对建议在同一次游戏运行内执行；普通存档重载可用于手动观察任务和结果。
