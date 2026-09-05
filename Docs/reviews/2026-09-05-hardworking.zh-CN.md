# 兽耳屋勤工兼容性检查

初始检查结论：部分兼容。下文记录修复前状态。后续已增加勤工适配，修复成员资格、工作权限及停工入口，并通过 Gabs 的 18 项真实框架组件权限回归。测试另外发现并修复了 Work Tab 优先级与勤工面板不一致的问题。概率工作模式保守退出 SAR；种族本体完整 Job 流程仍待验证。详见 [当前兼容说明](../compatibility/README.zh-CN.md) 和 [验证记录](../validation/2026-09-05-hardworking.json)。

检查对象：工坊 `2574995438`，本地名称 `kemomimihouse HardworkingKz`，包名 `Moo.Hardworking.Kz`，1.6 程序集 `HardworkingExtension 1.6.dll`。SHA256：`E449072B4667A870EDF56A841D331296BA9D87BF5BA4BD026D864F694AF3C1C2`。结论不自动适用于旧版勤工。

## 发现

1. **高优先级：勤工医生可能无法接手 SAR 标记患者。** `IsFieldResponder` 只让类人工作者通过工作优先级加入，动物仍读取独立成员集合；`CanToggleFieldResponder` 只允许受训救援动物加入。因而没有救援训练的勤工动物，即使启用了医疗和战地救援工作，也不能成为 SAR 队员。同时 SAR 对 `WorkGiver_Tend.HasJobOnThing/JobOnThing` 的补丁会阻止普通自动治疗接手已标记患者，而勤工恰好沿用这些原生 WorkGiver。当可用医生只有这类勤工动物时，存在标记后无人治疗的路径。手动强制治疗和取消标记是临时规避方式。
2. **中优先级：受训勤工动物走普通动物救援分支，忽略勤工工作开关。** `RescueProviderFor` 优先返回 Animal，跳过后面的工作优先级检查；SAR 也没有读取 `HardWorkerCanWork`、`curStopWork` 等勤工权限。加入 SAR 后，调度资格不能保证服从勤工的全局禁工、个人停工设置。实际抢占时机及夜间、跟随行为还需实机确认。
3. **设置与加入方式不一致。** 勤工为动物提供 `workSettings`，但 SAR 仍要求先学会动物救援并用独立队员开关加入。勤工的可征召补丁虽然可以让 `IsColonistPlayerControlled` 返回真，却不能解决上述成员资格门槛。勤工还对新增工作类型应用训练解锁限制；专用适配需要同时尊重战地救援及具体医疗/搬运工作的解锁和优先级。

## 已有边界与建议

- 作为伤员，非敌对血肉动物通过 SAR 的治疗资格检查；敌对动物不能沿用俘虏流程。这是源码支持范围，尚不代表该模组整套治疗与床位流程通过实测。
- 本次未发现足以确认直接崩溃的证据；不能据此宣称完全兼容。
- 建议增加可选勤工适配器：统一队员资格和工作设置入口，读取原生勤工工作许可，并让具有工作系统的勤工动物优先走工作者分支。不能仅放宽动物资格，否则会扩大绕过停工设置的问题。
- 实机验证应覆盖：无救援训练的勤工医生、只有勤工医生的标记患者、受训搬运者、关闭战地救援/医疗/搬运、全局及个人停工、夜间工作、跟随征召主人、玩家强制命令，以及勤工动物自身受伤。

源码定位：`Scheduling/SearchAndRescueCoordinator.cs` 的 `IsFieldResponder`、`CanToggleFieldResponder`、`WorkerControlledByScheduler`、`WorkerReadyForStageCore`；`Compatibility/Compatibility.cs` 的 `RescueProviderFor`；`Compatibility/JobSystemCompatibilityPatches.cs` 的原生治疗拦截。第三方核对点为 `Kz.JobGiver_HardworkingWork`、`Kz.HardworkingUtility`、`Kz.CompHardworking` 和 `Kz.HarmonyPatch_Drafted`，反编译文件未加入仓库。
