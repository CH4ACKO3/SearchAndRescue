# 0.1.0-alpha.1 发布清单

## 发布前自动检查

- `dotnet build Source/SearchAndRescue/SearchAndRescue.csproj -c Release -warnaserror`
- `dotnet format Source/SearchAndRescue.sln --verify-no-changes --severity warn`
- `dotnet run --project Tools/SchedulerSimulation/SchedulerSimulation.csproj -c Release`
- 三套语言的 Keyed key、DefInjected 文件和 XML 结构一致。
- `About/preview.png` 小于 1 MiB；`About.xml` 的 packageId、版本和唯一硬依赖正确。
- 运行 `Tools/BuildRelease.ps1`，确认暂存包不含 Source、SourceAssets、References、Tools、bin 或 obj。

## 实机冒烟

- 只启用 Harmony + Core + SAR，创建新地图，三个命令、组合命令和工作栏正常。
- 运行小型、18×6×6、无床位、医疗物流、俘虏分诊五个基准。
- 验证手动强制治疗/搬运/俘虏不会被改派；征召 pawn 不被 SAR 接管。
- 验证伤员死亡、离图、不可达、床位拆除、药堆禁用/摧毁/合并后声明被释放或重算。
- 检查从载入开始新增的 warning/error，并导出性能报告。
- 组合报错先用最小模组集复现，区分 SAR 缺陷、SAR×第三方交互和第三方模组彼此固有冲突；结果写入兼容实测矩阵。
- Smart Medicine + CE 场景必须同时保留未标记伤员，确认原生 `WorkGiver_Stabilize` 不会从第三方 pawn 库存产生零效果循环。

## 创意工坊页面

- Steam 默认语言使用 `WORKSHOP_DESCRIPTION.md`，简体中文页面使用 `WORKSHOP_DESCRIPTION.zh-CN.md`；逐项说明保存在 `COMPATIBILITY.zh-CN.md`，不要混入精简兼容性列表。
- 已发布页面为 https://steamcommunity.com/sharedfiles/filedetails/?id=3796056278 。发布前先核对线上英/中文描述，将作者在工坊手动修改的内容同步回本地；不要用旧版 About 或发布文案覆盖线上修改。`About.xml` 使用英文描述的游戏内纯文本版本。
- 标签建议：`1.6`、`Mod`、`Medical`、`Utilities`。
- 将可见性先设为“仅好友”或“隐藏”，完成订阅安装的干净复测后再公开。
- 保持线上标题 `Search and Rescue`；在简介后保留 Alpha 提示，按需补充已知限制与日志提交要求。
- 首次上传后由 RimWorld 生成 `About/PublishedFileId.txt`；不要在首次发布前手工伪造 ID。

## 不进入发布包

源码、参考图、SVG 源文件、测试工具、编译中间产物、测试存档、Player.log、个人模组配置和
任何第三方程序集均不进入 Workshop 内容目录。
