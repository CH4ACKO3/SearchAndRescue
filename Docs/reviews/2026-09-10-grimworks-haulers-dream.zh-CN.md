# GrimWorks 与 Hauler's Dream 兼容检查

检查对象为 2026-09-10 下载的工坊原包，而非仅凭页面描述判断。

- GrimWorks: Work Manager，工坊 3761759348，包名 `grimworks.workmanager`；`GW_WorkManager.dll` SHA256 `05BDC85342CDB260802FD88C408DACDE96FB4AFEAE5AA353682E94BF9EC550ED`。
- Hauler's Dream 1.24.0，工坊 3742459652，包名 `giwaffed.haulersdream`；`HaulersDream.dll` SHA256 `D55644ACBE064F4F6A37C82205F48EEAF61264100C92AC568BDCB612FE8DCC2D`，Core DLL `D835EF8E730BDD721B4A6978F85C4E0B61BEBFE8109247CC67CFDF190652F299`。

## GrimWorks

反编译原包确认，其 `WorkGiverPriorityRegistry.GetEffectivePriority(Pawn, WorkGiverDef)` 保存和读取独立子工作优先级。SAR 原先只读取 Work Tab 子工作或原版父工作，因此无法可靠尊重 GrimWorks 中关闭的子工作。

现通过反射读取 GrimWorks 的实际子工作优先级，同时遵守父工作关闭与角色禁用条件。未将 GrimWorks 冒充成 Work Tab；原有 Work Tab API 路径保留。紧急医疗、普通治疗、SAR 子工作及原有特殊医疗子工作检查均使用该适配。

GrimWorks 将原版 `DoctorRescue` 移到 `GW_WorkManager_Rescue`。新增 SAR 的独立 GrimWorks 搬运入口：同时开启 Field Rescue 和原生 Rescue 子工作即可转移伤员，无需 Hauling。禁用原生 Rescue 子工作会撤销该提供者；若玩家同时授权其他搬运提供者，仍按它们自身权限工作。GrimWorks 自身关闭 Rescue 类型、将工作移回 Doctor 时，此独立入口不接管。

Nurse 负责其原本的喂食等工作，不等同于 SAR 的医疗授权，也未将它误认成独立 Nurse Job 模组。Surgery 仍保留在 GrimWorks 自己的工作类型下。没有扩展护士的止血能力。

## Hauler's Dream

单独开启时，现场止血、搬运、入床和常规治疗流程通过。未发现它导致先前那种没有治疗效果的无限循环；该循环已有 SAR 自身修复。

另发现原包 `InventorySurplus.SurplusOf` 会将 SAR 已占用的背包药物算作多余物资。原生卸货 JobDriver 及批量物资计算都会使用此判断。游戏内以实际医疗资源账本占用药物，再调用原包计算接口，确认占用前后仍判为 surplus；基线 19 项检查中该项失败。

新增对 `SurplusOf` 对应重载的保护：SAR 已占用或保留给患者的物资暂不算作 surplus，释放后恢复 Hauler's Dream 原生判断。保护作用于临时资源占用，不永久改变物品标签或库存策略。没有替换 Hauler's Dream 的普通搬运或共享库存算法。

## 验证与范围

`Tools/Run-CaptureProbe.ps1` 使用独立 quicktest 游戏进程与配置。SAR + GrimWorks + Hauler's Dream 共 29 项通过，覆盖实际俘虏、野外只止血、通过 GrimWorks Rescue 搬运（Hauling 为 0）、床上处理剩余伤口，以及子工作关闭、6/7/8 级优先级、Nurse 不越权、药物占用与释放。医疗执行阶段无异常。

无图形测试启动时，GrimWorks 查询殖民者栏布局会卡在零宽度界面上。诊断模式仅在显式 `-quicktest -sar-capture-probe -nographics` 下，以当前地图殖民者名单替代该 UI 排序查询；未跳过医疗或工作管理代码。该测试辅助不影响普通游戏。启动 shader 提示不计为医疗执行异常。

证据：`Docs/validation/2026-09-10-haulers-dream-baseline.txt`、`2026-09-10-grimworks-haulers-dream.txt`。Release 编译零警告零错误；XML 可解析；既有 92 项直接检查、9 项连续治疗检查、40 个场景、200 个随机图通过。

另在不加载 GrimWorks、Hauler's Dream，仅加载 Work Tab 的游戏配置中通过 15 项完整流程检查，证据为 `2026-09-10-grimworks-haulers-absent.txt`。此回归发现并修复了适配初版在 Hauler's Dream 缺席时未跳过空补丁目标的问题；最终组合 29 项和缺席配置 15 项均在补全 `Prepare` 检查后重新通过。

兼容面板保持一对一条目，新增两者说明。尚未发布。报告者未给出帐篷所属模组，因此没有将任何具体帐篷实现标记为已验证，也不能断言报告者整套配置的唯一原因。
