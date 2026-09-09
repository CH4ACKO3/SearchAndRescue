# Smart Medicine 近期分支识别

筛选范围：2026-03-09 至 2026-09-09，按 Steam 工坊条目更新时间。核对公开工坊搜索 `Smart Medicine` 的本体、修复分支与翻译结果；不宣称涵盖私有或未公开列出的分支。原始搜索页保存在工作目录 work/sar-smartmedicine-branches-20260909/browse.html。

| 条目 | 工坊 ID | 更新时间（UTC） | 处理 |
|---|---|---|---|
| MemeGoddess Continued | 3526251680 | 2026-08-15 | 独立识别 memegoddess.smartmedicine |
| PES7 Continued | 3792955548 | 2026-09-06 | 独立识别 pes7.smartmedicinecontinued |
| Uuugggg 原版 | 1309994319 | 2024-04-18 | 不在半年范围内 |
| DomB 1.5 修复分支 | 3256317028 | 2024-05-30 | 不在半年范围内 |
| 捷克语翻译 | 3792463429 | 2026-08-30 | 翻译包，不作为本体识别 |

工坊来源：https://steamcommunity.com/workshop/browse/?appid=294100&searchtext=Smart%20Medicine&browsesort=textsearch&section=items&numperpage=30 。具体条目：https://steamcommunity.com/sharedfiles/filedetails/?id=3526251680 、https://steamcommunity.com/sharedfiles/filedetails/?id=3792955548 。时间来自搜索页内 Steam 返回的 time_updated，两个本体包 ID 均与本地原包 About.xml 核对。

兼容面板将两个分支按作者分别列出，用精确包 ID 判断启用状态，共用已有适配及接口就绪检查。补充 PES7 的 loadAfter。三种语言的说明注明支持这两个续作，并提示只启用一个本体；About 与双语文档同步明确作者。两个分支沿用已有实测结论，证据见 2026-09-09-smartmedicine-argument-binding.zh-CN.md。

验证：Release 构建 0 警告、0 错误；About/语言 XML 解析及英简繁键一致性通过。这次只修改识别、加载顺序和说明，没有改变治疗流程，也没有新增未知分支探测或运行时重复本体冲突处理。未推送或发布。
