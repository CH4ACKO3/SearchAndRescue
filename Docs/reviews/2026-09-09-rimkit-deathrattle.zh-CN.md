# Rimkit 自动包扎与 Death Rattle 撤离联动

2026-09-09，基于 alpha.5 工作区，尚未发布。

## Rimkit

原生 CompMedkit 已有 `UseKitForTendJob` 开关，默认关闭。SAR 尊重这个开关、原生 FindMedkit 可用性和患者用药策略，为佩戴者增加 `RimkitBandage` 候选。必须具有相应医疗工作权限；机械患者继续走原有维修/原生零件治疗路径。CE 可以执行稳定时，不用医疗包包扎抢占其流程。

调用真正的 `BandageOthers` Job。医疗包作为独立 Equipment 引用绑定到任务 targetC，避免把整件衣物错当作一份地图药品，也避免走到患者后换用另一个未选择的包。targetB 留给原生 Driver 从包中取出的药品。到达时重新检查佩戴、开关、次数、用药限制和可治疗伤口。

对带 SAR targetC 标记的原生任务保留 goto、取药、等待、FinalizeTend，去掉无限返回首个 toil 的跳转。每轮消耗一份后结束，由协调器重新匹配；在野外通过既有 FieldTreatmentBoundary 只处理急症。手动原生任务没有这个标记，保留原始循环。标记在存档中保留，即使瞬态调度声明尚未重建，也能识别其单轮边界。没有宣称本轮已经做存档重载实测。

开关或药源变化使候选失效时返回重算。取药后、完成前再次检查患者用药许可，避免旧候选继续完成违规治疗。医疗包补充沿用原模组；没有新增自动穿戴、补包后勤或将包内次数加入公共药堆账本。

兼容面板改为“联动”，原生 API 不可用时降为“部分兼容”。

## Death Rattle

核对原生 1.6 HediffDefs 和移除条件，增加六类不可包扎危险状态的撤离权重：无脉搏、缺氧、急性肝衰竭、急性肾衰竭、肠道衰竭和昏迷。检查原生 DeathRattle 组件及当前危险阶段，不靠本地化标签匹配。原生组件已满足移除条件时，不再保留额外权重。

无脉搏/缺氧的原生恶化速度高于器官衰竭，给予更高且有上限的撤离权重。只增加运输优先级，不虚构复苏 Job、不把这些状态加入普通包扎，也不关闭 More Injuries 已有的有效急救。原生 HeartAttack 仍依据其真实 TendableNow 和既有急症判断处理。

目的地沿用可用床位与原有救援点回退规则。不会自动添加移植账单、调用不存在的复苏任务，或宣称会挑选 Life Support 专用设备。手术和生命维持仍由对应系统处理。提高撤离优先级不是存活保证。

## 原生验证

[27 项通过](../validation/2026-09-09-rimkit-deathrattle.txt)。配置为 Harmony + Core + Rimkit + Death Rattle + SAR，独立 savedatafolder，正式结果目录 `D:/Projects/rimworld/work/sar-rimkit-deathrattle-20260909/g`。

- 原生开关默认关闭、开启后候选出现、草药限制拒绝工业医疗包、旧候选不能绕过变更、空包无候选。
- 没有瞬态声明时，带 SAR 标记的原生任务仍为四个单轮 toil；普通手动任务保留五个含循环的 toil。
- 运行 2,400 tick，SAR 实际用包处理切伤，消耗恰好一次，普通瘀伤保留未治疗，原生循环任务退出。
- 实际移除心脏后，Death Rattle 保持患者存活并产生无脉搏。患者获得更高撤离权重，经过 2,400 tick 被 SAR 活着送到玩家医疗床。恢复心脏后额外优先级立即消失。
- 实际移除肝脏后产生原生肝衰竭，其撤离权重低于无脉搏；不会成为包扎目标，恢复肝脏后额外权重消失。
- 原生流程共推进 5,280 tick，检查窗口没有新的 Error/Exception/Assert。未对其他四种状态逐个执行全程撤离。

早期夹具问题：首次使用重复测试 packageId 选中了旧运行副本，已终止并改用唯一 ID；医疗床未设置玩家阵营导致目的地验证失败，修正后通过；一次夹具在新游戏初始化结束前开始推进 tick，已增加 GameInitData 清空及初始 tick 门槛，失败批次不作为验证证据。

最终源码另加了可选 Rimkit 未加载时的空 Job 防护。Release 构建为 0 警告、0 错误；既有离线回归 92 项生产规则、9 项连续性、40 场景及 200 随机图通过。完整多模组、有无插件对照结果另附相应验证文本，不将单独模组测试外推为所有组合已通过。

最终 DLL 在未加载 Rimkit/Death Rattle 的 CE + Smart Medicine + More Injuries + Hemogen Direct + Work Tab 配置下，[43 项检查通过](../validation/2026-09-09-rimkit-absent-combination.txt)，并运行 6,000 tick 完成实际包扎，无新运行错误。这验证了可选模组缺席时的回退，不是 Rimkit 与 CE 同时加载的原生流程实测。

## 提供者来源

- Rimkit：[作者仓库](https://github.com/Dubwise56/Rimkit)，提交 `8d802bf6582e3f259e8e9e12f93d42319a1af5d7` 的 1.6 发布 DLL。测试副本省略仓库附带的旧 Harmony，使用独立启用的 Harmony 模组。DLL SHA-256：`5195694692AF8AD14D3BB04BD1F1896501D33D5A2F38552013BACE5996ECD512`。
- Death Rattle：[工坊 2896207870](https://steamcommunity.com/sharedfiles/filedetails/?id=2896207870) 的本机 1.6 文件。DLL SHA-256：`783AC2D815EBAD63E753875BF4B3D5A64EE3AE4894301C2E9F5A20BD5AD06457`。上一轮“本机未找到”的说法来自检索遗漏，不代表没有可用本体。
- 最终 SAR DLL SHA-256：`243A746AD6A369AB798611916404256F069EEF339134310E9CFB5005351A313B`。各批次运行副本另外保留文件清单。

复跑入口：`Tools/Run-RimkitDeathRattleProbe.ps1`，需要已安装对应提供者。未修改玩家正在使用的模组配置或存档，也未推送或发布。
