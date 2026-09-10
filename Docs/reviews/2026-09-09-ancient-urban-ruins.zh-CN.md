# Ancient Urban Ruins 多次使用医疗包

报告：使用 AUR 医疗包时进度条结束但没有治疗效果。报告者没有提供存档、具体物品或模组列表，因此不能确认其环境是否包含 CE 或 Smart Medicine。

## 原包与发现

SteamCMD 下载工坊原包 https://steamcommunity.com/sharedfiles/filedetails/?id=3316062206 ，包 ID XMB.AncientUrbanrUins.MO。使用未修改的 1.6/Assemblies/AncientMarket_Libraray.dll，SHA256 5AD40B31C70862839C487EC6A1A1FD3FA51BF4A0777BC3E424ED31F940F004CA。反编译原包用于检查实际实现。

AUR 的 Patch_Tend.prefix 完全替换 DoTend，并通过 CompUseableCount.count 消耗次数。该 nullable 字段初始为空，只有读取 Count 属性才初始化；未查看过的物品可能被一次性消耗。基础组合与 Smart Medicine 组合中仍能完成治疗，未复现“无治疗效果”。

CE + Smart Medicine + AUR + SAR 组合中，生产选药生成 Stabilize 工作，实际完成临时止血但不标记伤口已治疗；CE 的完成动作消耗整只医疗包，不经过 AUR 的次数处理。六种医疗包均重现此行为。这是能解释类似现象的组合问题，并不能证明就是报告者的原因。

## 修复范围

按实际 CompUseableCount 组件识别多次使用药品，将 SAR 此类候选及 CE 回退入口转为 TendPatient，保留 AUR 原生治疗/扣次、SAR 急症边界及工作权限。普通药品的 CE 选择不变。

在 SAR 管理的 DoTend 调用前，对实际拾取后的药品读取 AUR Count 属性，按提供者自身规则初始化次数。没有替换 AUR DLL，也没有全局修改普通医生或手动 CE 命令的扣费行为。供应预算仍按实体物品保守计数，没有把一个包展开成可同时分配给多个医生的虚拟药品。

## 验证

独立 quicktest 进程测试 AM_AI2A、AM_HemostaticAgent、AM_FirstAidKit、AM_Salewa、AM_Grizzly、AM_AncientGrizzly，每种包含普通工作对照和 SAR 生产选药生成的工作，并实际运行原生 JobDriver。测试会允许协调器重新分配任务，最终检查伤口、急症边界、物品和剩余次数。

- 基础组合，未初始化医疗包：60 PASS，0 FAIL。
- CE + MemeGoddess Smart Medicine + AUR，未初始化医疗包：60 PASS，0 FAIL。
- 同一 CE 组合，仅剩一次使用：66 PASS，0 FAIL。
- 所有 SAR 路径均实际治疗割伤、保留挫伤；新包分别余 2/2/3/7/23/23 次，最后一次使用后物品消耗。治疗窗口内无运行时错误。
- Release 构建 0 警告、0 错误；既有 92 项生产规则、9 项连续性、40 个调度场景、200 个随机图回归通过。
- 不加载 AUR 的 CE/Smart Medicine/More Injuries/Hemogen Direct/Work Tab 组合另有 47 PASS、0 FAIL，包含 6000 tick 实际治疗，确认可选组件缺席时的回归。

证据保存在 ../validation/2026-09-09-aur-*.txt。原始进程日志与反编译文件位于工作目录 work/sar-aur-20260909。CE/AUR 无图形模式启动会产生贴图图集相关错误，未将其计入医疗执行窗口的错误数。初始 CE 场景把患者放在地图中心不可达位置，任务未进入治疗；这些失败场景不作为复现证据，最终场景使用殖民者落点并检查可达性。

测试没有修改玩家存档或启用模组列表。未验证报告者完整环境、PES7 与 AUR 组合、所有 AUR 药物、手术和多件部分使用药包的拆分继承；本轮未发布。
