# Smart Medicine 参数绑定启动修复

用户报告 PES7 版本的 `WorkGiver_Stabilize.JobOnThing(Pawn pawn, Thing t, bool forced)` 在 SAR Bootstrap 的 Harmony.PatchAll 中报错。SAR 后置补丁按 `healer` 参数名注入，与该签名不符，导致静态初始化中断。

将 JobOnThing 的工作者参数显式绑定到位置 0，同时将 HasJobOnThing 的工作者和目标绑定到位置 0、1。治疗与药物选择逻辑保持原样。

Release 构建通过，0 警告、0 错误。在独立游戏进程中，将实际生产后置补丁安装到分别使用 healer/target 和 pawn/t 命名的 WorkGiver_Scanner 覆写方法，并调用验证，4 项通过。已有 CE、Smart Medicine Continued、More Injuries、Hemogen Direct、Work Tab 组合回归 43 项通过，包含 6000 tick 实际治疗及运行时错误检查。

上述首轮仅验证签名及 MemeGoddess Continued 组合。证据见 ../validation/2026-09-09-smartmedicine-argument-binding.txt。

## PES7 原包实测

随后按用户要求，通过 SteamCMD 下载工坊 https://steamcommunity.com/sharedfiles/filedetails/?id=3792955548 的真实原包，About 版本 1.0.2，包 ID pes7.smartmedicinecontinued。1.6/Assemblies/SmartMedicineContinued.dll 的 SHA256 为 6DB2D421F18BFCE34E438A01F42370CA2047FB66BBC5ED9611993FF94E8BD313。直接加载原包 DLL，没有重编译或修改第三方代码。

使用 GitHub 发布包 alpha.6 的 SAR DLL，在隔离进程中复现了与用户一致的 Bootstrap / Parameter "healer" not found 报错，证据见 ../validation/2026-09-09-pes7-alpha6-startup.txt。

修复版隔离进程启用 Harmony、Core、Biotech、CE、PES7、More Injuries、Hemogen Direct、Work Tab、SAR，未同时启用 MemeGoddess 版本。确认两个生产补丁安装在真实 PES7 方法上，其参数均为 pawn,t,forced。原生 WorkGiver 的 ShouldSkip、HasJobOnThing 和 JobOnThing 检查通过；启动其返回的原生 Stabilize Job 后，68 tick 内伤口出血率从 1.08 降到 0，患者存活，恰好消耗一份工业药物。此项直接调用原生扫描并执行返回任务，没有将其描述为完整原生 ThinkTree 自主调度测试。

本轮合计 55 PASS、0 FAIL，包含 6000 tick 的 SAR 实际治疗及运行时错误检查。启动日志没有静态初始化、Harmony 补丁或异常报错。证据见 ../validation/2026-09-09-pes7-live.txt。首轮 PES7 场景失血为 0.85，未达到其原生两小时失血死亡接单阈值，导致扫描断言失败；调整测试失血为 0.95 后通过，未因此改变生产逻辑。

测试使用隔离配置，没有修改玩家存档或启用模组列表；没有发布此修复。测试覆盖该启动故障及上述医疗组合，不代表 PES7 所有可选联动均已验证。
