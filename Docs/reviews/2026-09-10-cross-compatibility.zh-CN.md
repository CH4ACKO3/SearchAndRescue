# 新增适配与既有适配交叉审查

独立子代理进行了只读逻辑审查，主代理检查原包及实际游戏组合，并实现与验证修正。无发布或推送。

## 审查发现

1. GrimWorks 提供者原先在护理模式之前返回，会绕过 `NursingOnly` 与 `NursingPreferred`。已移至普通人类搬运决策中、护理模式判断之后。CASEVAC 原有优先级保持不变。
2. GrimWorks 与 Work Tab 的子工作权限分别保存；原生 Work Tab 先建立工作列表，GrimWorks 再过滤排序。SAR 原先仅采用 GrimWorks，可能通过 SAR 入口代发被 Work Tab 禁止的治疗。现在取两者权限交集，允许时保留 GrimWorks 的最终优先级，GrimWorks 救援入口也复用此规则。
3. 新 GrimWorks 提供者不应绕过 Hardworking Tiny 白名单。现排除 Hardworking worker，保留其已有适配路径。这是静态审查后的防御收口，没有声称在游戏中复现过该条件。

独立复核确认上述子工作与护理模式修正无新的阻断问题。父工作查询也对齐 GrimWorks 原生入口，但不计作第三个已复现问题：Work Tab 已接管人类 pawn 的原生 `GetPriority`，同小时通常不会形成两个独立父权限。初版测试直接改原生 `priorities` 字段，无法改变实际 getter，因此该失败属于测试假设错误；最终改用工作管理器真实 API 禁用父项。

## 实测矩阵

| 组合 | 检查 | 结果 |
| --- | --- | --- |
| AUR + CE + PES7 Smart Medicine + GrimWorks + Hauler's Dream | 六种医疗包的实际治疗、保留剩余次数、野外急症范围 | 60 通过 |
| GrimWorks + Hauler's Dream + CE + MemeGoddess Smart Medicine + Choose Your Medicine + More Injuries + Hemogen（含 Biotech） | 逐伤口政策冲突、药物预算与重新校验、输血共享预算、设施交接及 6000 ticks 实际治疗 | 30 通过 |
| GrimWorks + Hauler's Dream + Nurse Job + CASEVAC | 实际俘虏/治疗/搬运，护理模式、CASEVAC 优先与子工作禁用 | 34 通过 |
| GrimWorks + Work Tab + Hauler's Dream | 双向子工作禁用、关闭搬运父项、完整治疗与物资保护 | 33 通过 |
| AUR + CE + MemeGoddess Smart Medicine + Work Tab + Hauler's Dream | 六种医疗包最后一次使用正确耗尽，急症范围不变 | 66 通过 |

以上均使用独立游戏进程、隔离配置和实际 JobDriver/原包接口。工作类型测试使用真实管理器 setter；程序化资源占用测试使用实际医疗资源账本。没有改玩家配置或存档。无图形殖民者栏名单辅助仅适用于明确启动的诊断进程，不跳过医疗/工作管理逻辑。

证据在 `Docs/validation/2026-09-10-cross-*.txt`。角色测试初版没有关闭随机生成角色默认启用的 CASEVAC，导致护理模式检查被原有 CASEVAC 优先规则抢先；修正测试初始权限后完整重跑通过。构建零警告零错误，既有 92 项直接检查、9 项连续治疗检查、40 个场景与 200 个随机图通过。

## 未扩大宣称的范围

- Rimkit 原包只提供三种原版药物，AUR 同时加载不会自然替换其弹药。只有第三方改写 Rimkit 药物、再叠加瞬态 claim 丢失等附加条件，才可能涉及 AUR 初始化与 Rimkit managed 标记的不对称；本次没有将此假设性组合当作已复现问题或添加补丁。
- GrimWorks 与 Work Tab 同时加载仍有两者原有的界面、优先级管理重叠。本次保证 SAR 不借入口绕过任一子工作禁用，不保证修复两个上游管理器之间所有交互。
- 未验证未知来源帐篷，也未宣称穷举全部模组组合。兼容面板保持一对一条目。
