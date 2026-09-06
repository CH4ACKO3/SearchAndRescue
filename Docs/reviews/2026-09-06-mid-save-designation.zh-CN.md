# 中途加入后无法标记敌人：调查

反馈：“中途加入存档无效，无法把标记放到倒地的敌人身上，会无法选中，只有开新档可以解决。”

**补充调查已找到并修复一个会造成该组症状的 Anomaly 判定错误，现已通过完整 DLC 实机回归。** 报告者是否启用了 Anomaly 尚待确认。此前不含 Anomaly 的中途加入测试通过；这不足以排除异象进度引发的问题。

## 完整 Anomaly 安装后的实机回归

用户安装 DLC 后，独立进程启用 Harmony、Core、Anomaly、SAR 测试运行时和探针。普通人类通过原版生成器创建，收容/研究组件来自官方定义；巨石进度使用 `GameComponent_Anomaly.SetLevel`，全程使用实际 `Pawn.GetGizmos`，没有注入组件或替换研究知识。

| 运行 | 同一敌人 | 巨石最高等级 | Thing / Cell 接受 | 原生 Gizmo 枚举中的 SAR 按钮 |
| --- | --- | --- | --- | --- |
| 旧 DLL，新建测试游戏 | Human37282 | 0 | 是 / 是 | 存在 |
| 旧 DLL，调用原版进度 API 后 | Human37282 | 1 | 否 / 否 | 缺失 |
| 修复 DLL，重新加载上述存档 | Human37282 | 1 | 是 / 是 | 存在 |

修复后成功添加 SAR_Treat、SAR_Rescue、SAR_Capture。额外通过原版 `MutantUtility.SetPawnAsMutantInstantly` 创建 Shambler，其 `StudiedAtHoldingPlatform=True`、SAR 普通囚犯俘虏资格为 False，收容分流保持正确。测试存档与日志位于 `work/sar-anomaly-native`，结果为 PASS，日志未发现 Exception。测试结束确认所有游戏进程已退出后，已将修复 DLL 同步到正式模组安装目录；未操作用户存档。

## 补充反馈：快捷按钮消失、敌人框选被拒绝

原版 Human 定义在 Anomaly 启用时附加 `CompHoldingPlatformTarget` 和 `CompStudiable`，后者 `minMonolithLevelForStudy=1`。SAR 原来的 `TargetEligibility.CanBeCaptured` 只要前一个组件的 `CanBeCaptured` 为真就拒绝俘虏，没有检查目标是否真的属于收容研究对象。

在生成巨石的玩法中，同一个倒地普通敌人会出现：最高巨石等级 0 时组件 CanBeCaptured 为假，SAR 允许；等级达到 1 后组件 CanBeCaptured 为真，SAR 拒绝。敌人又需要先有 SAR_Capture 才能添加治疗和救援，因此三个阶段全部拒绝，底部组合命令被隐藏，框选也无效。这个进度差异能造成“旧档不行、新档正常”的表象。关闭巨石生成的玩法也可能触发旧判定，问题本身不要求中途加入。

中立难民的治疗不依赖俘虏阶段，所以仍可添加治疗；综合快捷命令当前只会自动俘虏敌对人形，不会自动对中立难民添加俘虏标记。

修复将排除条件改为 `StudiedAtHoldingPlatform`，保留对真正收容对象的分流。所有使用 `CanBeCaptured` 的标记、任务调度和有效性检查同时受益，无需添加存档迁移数据。

首次调查时尚未安装 Anomaly DLC。探针当时在暂停的独立引擎进程中注入实际组件、临时启用相关判断，并切换进度字段；使用 fixture 的研究知识值覆盖，结束前恢复所有临时字段。该组件级回归的结果现已由上面的完整 DLC 实机测试确认；反馈者存档仍未取得。

相关探针：`Tools/HotAddProbe/CaptureGateProbe.cs`；独立记录：`work/sar-capturegate-before/after` 与 `work/sar-captureprogress-before/after`。下文保留此前不含 Anomaly 的对照证据。

## 实机证据

使用不引用 SAR 的独立 HotAddProbe 辅助模组，首先在完全禁用 SAR 的配置中创建新殖民地和倒地敌人。初始存档 metadata 不含 ch4acko3.searchandrescue，地图中没有 SearchAndRescueCoordinator。敌人 Human22390 为敌对古代人人形 pawn，倒地且可正常选中。

随后启用当前 SAR DLL，重启隔离进程，加载同一个存档：

| 检查 | 中途加入 | 从新档启用 |
| --- | --- | --- |
| 原版 Selector 能选择倒地敌人 | 通过 | 通过 |
| SAR 地图组件存在 | 通过 | 通过 |
| 俘虏工具，Thing 与 Cell 接受 | 通过 | 通过 |
| 快捷组合工具，Thing 与 Cell 接受 | 通过 | 通过 |
| 选中敌人的底部 SAR 命令 | 存在 | 存在 |
| 一次命令添加 Capture / Treat / Rescue | 通过 | 通过 |
| 保存后再次加载，三种标记保留 | 通过 | 未另测 |

未先添加俘虏标记时，独立 Treat 和 Rescue 工具均拒绝该敌人，并返回“必须先标记就地俘虏”的原因；新旧两组相同。因此这是可重现的操作条件，尚不能确认就是原反馈的原因。

日志中未发现本测试引发的 Exception、FAIL 或加载错误。配置包括 Harmony、RimWorld 1.6、Biotech、HugsLib、Work Tab、Common Sense、Search and Destroy、Nurse Job、Smart Medicine、Move the Patient、More Injuries，以及探针；后续阶段加入 SAR。未覆盖报告者未知的其他模组、Anomaly 实体和敌方机械体。

## 代码解释与剩余定位

- 原版 Map.ExposeData 会调用 FillComponents，为旧地图补建新增 MapComponent；SAR 构造函数初始化资源账本。标记筛选不依赖 fieldResponderWorkTypeMigrated 或工作开启情况。
- 快捷组合命令首先尝试 Capture，再添加 Treat/Rescue。普通敌对、倒地、血肉人形目标符合 Capture 条件，无需医生、囚犯床或研究条件才能放置标记。
- 独立治疗/救援要求敌人先有 SAR_Capture；敌对动物、原版敌方机械体、需要收容的 Anomaly 目标则适用各自排除条件。
- 底部命令通过 Pawn.GetGizmos 补丁动态产生。其他模组的 Gizmo 枚举异常可能影响显示，但本轮没有证据证明该冲突发生。
- “无法选中 pawn”“能选中但没命令”“工具拒绝标记”“标记立即消失”“标记后没人执行”需要分别记录。当前组合工具在全部阶段拒绝时会合并为 bool，缺少完整拒绝原因，这是诊断体验的局限，本轮未更改筛选规则。

需要报告者确认 Anomaly 启用状态、巨石进度，或提供 Player.log 与存档，才能确认是否为上述已修复问题。现有证据支持修正目标判定，尚无依据要求用户重开存档。

有效测试数据：工作区 `work/sar-hotadd-20260906-valid`。before/after/reload/new 的 Probe 文本与原始日志均保留；旧档标记重载副本为 SAR_HotAdd_OldRoundtrip。先前无海盗派系的错误测试样本在另一目录中隔离，未计入结论。
