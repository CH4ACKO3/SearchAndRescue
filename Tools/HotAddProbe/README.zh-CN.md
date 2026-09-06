# 中途加入存档：标记回归探针

这个独立辅助模组不引用 SAR 程序集，因此可在 SAR 完全未启用时创建存档，再验证加入后的同一敌人。仅在独立 `-savedatafolder` 测试配置使用；每个阶段结束后自动退出测试进程。

构建 `dotnet build Tools/HotAddProbe/HotAddProbe.csproj -c Release -warnaserror`。可用 `-p:GameManaged=... -p:HarmonyPath=...` 指定依赖位置。将 About.xml 放到辅助模组的 About/About.xml，DLL 放到 Assemblies，并在隔离配置中启用 `ch4acko3.sarhotaddprobe`。正常游戏无需启用它。

所有阶段使用同一隔离数据目录，依次运行：

1. 禁用 SAR，带 `-quicktest -sar-hotadd-phase=before` 启动。生成敌对古代人的倒地人形 pawn，验证确实敌对、倒地；保存 SAR_HotAdd_Before。
2. 启用 SAR，带 `-sar-hotadd-phase=after` 启动。自动加载旧档，检测普通选择、地图组件、四种 designator 的 Thing/Cell 接受情况、底部命令、实际三阶段标记，并保存 SAR_HotAdd_Marked。
3. 带 `-sar-hotadd-phase=reload` 启动。检查保存的标记仍存在，再取消并重新应用。
4. 备份 SAR_HotAdd_Marked 后，带 `-quicktest -sar-hotadd-phase=new` 启动，建立从开始即启用 SAR 的对照组。此阶段会重新写入 SAR_HotAdd_Marked。

结果位于隔离目录 Probe/*.txt，失败写入 FAIL 与异常。辅助模组直接调用真实 Selector、Designator、Pawn.GetGizmos 和存档加载流程；尚未覆盖鼠标拖拽事件、第三方界面布局或玩家反馈的具体存档。

quicktest 世界不一定存在海盗派系，所以使用 Faction.OfAncientsHostile 并断言 HostileTo 为真。不要将无阵营 pawn 误当敌人测试。

新增 `-quicktest -sar-hotadd-phase=capturegate-before` / `capturegate-after` 组件回归。前者要求旧 DLL 重现普通人类被收容组件误拒绝，后者要求修复 DLL 恢复按钮、Thing/Cell 接受与三阶段标记，并继续排除收容变异体。两者都验证同一个 pawn 在巨石等级 0/1 下的结果。测试临时注入真实引擎组件与 Anomaly 开关/进度，使用研究知识 fixture；本机没有完整 DLC，不能把此结果称作完整 Anomaly 实测。分别使用独立目录，测试结束自动退出。

安装完整 Anomaly 后，使用 `-quicktest -sar-hotadd-phase=anomaly-before` 配合旧 DLL：通过原生组件与进度 API 复现等级 0/1 差异，并保存 SAR_Anomaly_Progressed。待进程退出，替换为修复 DLL，用同一隔离目录运行 `-sar-hotadd-phase=anomaly-after`，加载同一个敌人，验证实际 GetGizmos、Thing/Cell 标记及原生 Shambler 分流，保存 SAR_Anomaly_Fixed。此入口不使用组件注入或研究知识 fixture。
