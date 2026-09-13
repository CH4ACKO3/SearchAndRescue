# 主分支冗余代码检查（2026-09-13）

范围：SAR 生产源码、诊断入口及近期修改的工坊发布脚本。交叉检查源码、Tools、XML 与仓库内字符串引用；检查候选方法的调用链和反射入口。本轮属于静态检查与明确冗余清理，不构成所有动态调用路径的形式化证明。

清理内容：

- Compatibility 中的 MoreInjuriesSalineJob、MoreInjuriesBloodJob 两个未读取的私有缓存。实际输血与盐水兼容仍使用原生工作提供器和驱动解析。
- 内部 MedicalResourceLedger 中没有调用路径的 RetainPatientFieldSupplyReferences、FindBestOnMap、AvailableOnMap、AvailableForRestock。AvailableOnMap 只有来自同样未被调用的 FindBestOnMap 的引用。
- PublishWorkshop 中已失效的 Success/Published/Updated 文字匹配条件。能进入该错误分支时，上传退出码必定非零；成功路径已进入远端文件校验。

保留内容：Harmony 补丁、DebugAction 方法和 RimWorld override 入口通过框架调用；治疗与转运拥有不同物资权限；匹配与提交之间的校验处理状态变化；Work Tab、GrimWorks、机械体等回退承担不同兼容边界。

验证：Release 构建（warnaserror）通过；调度回归套件通过，包括 92 项直接生产规则检查、8192 个穷举图和 1000 个随机图等；Test-Package 对既有 alpha.11 发布包的本地校验通过。本轮未重新运行游戏，也未上传新的工坊包。
