# Tag 驱动的工坊发布

工作流：`.github/workflows/release.yml`。普通 main 提交、PR 和手动运行会构建、回归测试、打包，并验证工坊上传参数。推送 `v*` 标签还会创建 GitHub Release；Steam 上传由仓库变量单独控制。

当前 Steam 上传保持关闭，登录授权和真实上传尚待验证。兼容性说明维护在 [中文清单](../compatibility/README.zh-CN.md) / [English](../compatibility/README.md)，工坊主页链接到 GitHub。

## 发布版本

1. 更新 `About/About.xml` 的 `modVersion`，在 `Docs/releases/<版本>.md` 填写中英双语变更说明。
2. 提交并推送 main，确认 Actions 构建通过，完成对应的引擎实测。
3. 创建并推送与版本完全一致的标签。例如版本为 `0.1.0-alpha.2` 时：

```powershell
git tag -a v0.1.0-alpha.2 -m 'Search and Rescue 0.1.0-alpha.2'
git push origin v0.1.0-alpha.2
```

标签版本不符或缺少变更说明会阻止发布。GitHub Release 附带 ZIP 和 SHA-256 清单；含预发布后缀的版本标记为 prerelease。Steam 使用同一构建产物，固定更新 appid `294100`、条目 `3796056278`。SteamCMD 上传内容目录和变更说明；随后双语发布器读取对应标签的 `Docs/workshop/Description.en.bbcode` 和 `Description.zh-CN.bbcode`，分别更新英文（0）和简体中文（6）。发布前回读两种语言并核对账号所有权，保留原有标题、可见性和标签，发布后再次回读确认描述一致。两份描述均附在 GitHub Release 中，并进入 SHA-256 校验。

## Steam 授权

在 GitHub Settings → Environments → `steam-workshop` 配置以下 Secrets：

- `STEAM_USERNAME`：条目所有者的 Steam 登录名。
- `STEAM_PASSWORD`：该账号的密码。
- `STEAM_REFRESH_TOKEN`：通过下面的本机登录工具完成 Steam Guard 后获得的持久登录令牌，供双语发布器使用。
- `STEAM_CONFIG_VDF_BASE64`：在本机 SteamCMD 完成 Steam Guard 验证后，`config/config.vdf` 的 Base64 内容。

凭据通过 GitHub Secrets 保存。配置文件包含登录信息，按密码保管；它的有效性及在云端的可用性需要首次授权实测。Steam Guard 要求重新验证时，在本机重新登录并更新 Secret。

准备就绪后，将仓库 Actions Variable `STEAM_PUBLISH_ENABLED` 设为 `true`。关闭时设为 `false`，构建与 GitHub Release 仍可使用。发布使用 GitHub 托管 Windows runner，原始 Steam 登录输出不会进入公开日志，登录文件只存在于临时目录。

双语发布器使用固定版本 SteamKit2 的 PublishedFile 服务，包含显式 `language` 参数。该接口属于 Steam 客户端协议适配，并非 SteamCMD 的官方 VDF 扩展；当前完成了编译、离线请求序列化和载荷验证，账号登录、写入权限与线上回读需要授权后实测。

本机生成令牌（在自己的终端输入密码和 Steam Guard，令牌文件放到仓库外）：

```powershell
dotnet run --project Tools/WorkshopPublisher -- auth "$env:TEMP/sar-steam-token.txt"
Get-Content "$env:TEMP/sar-steam-token.txt" -Raw | gh secret set STEAM_REFRESH_TOKEN --env steam-workshop
Remove-Item -LiteralPath "$env:TEMP/sar-steam-token.txt"
```

`check <产物目录>` 可在配置环境变量后验证登录和条目所有权，且不会写入描述。生产上传会先运行这项检查，成功后才上传文件；文件成功后再发布双语描述。两种语言与文件更新是独立提交，任一步失败都会使任务失败，错误消息会指出已完成的阶段。再次运行时，相同描述会跳过写入。

首次启用应选择准备发布的真实版本验证。Steam 失败时工作流失败，已创建的 GitHub Release 仍然保留；修复授权后可重新运行失败任务。确认线上条目后再决定是否重试，避免重复变更记录。手动运行工作流用于构建验证，上传由标签 push 事件触发。

## 构建与验证

CI 使用固定版本的 `Krafs.Rimworld.Ref` 和 Harmony NuGet 引用，在无游戏安装的 runner 上编译；本地常规构建继续使用游戏程序集。引用程序集仅用于编译，发布包包含 SAR 自己的 DLL 和运行资源。

```powershell
./Tools/CI/Build.ps1 -Tag v0.1.0-alpha.1 -OutputRoot artifacts/ci-check-1
./Tools/CI/PublishWorkshop.ps1 -ArtifactRoot artifacts/ci-check-1 -DryRun
./Tools/CI/Test-Package.ps1 -ArtifactRoot artifacts/ci-check-1
```

工坊主页发布前，将手工修改同步回本地两份 `Docs/workshop/Description.*.bbcode`；标签中的文件是发布来源。

输出目录须为新的发布目录。验证涵盖 XML、三种语言 Keyed 条目、生产调度规则、打包文件、版本/条目身份和 SHA-256。DryRun 在登录前结束。游戏引擎实测结果由开发者另行维护，CI 的规则回归不等同于引擎实测。

## 参考

- [SteamCMD Workshop 更新格式](https://partner.steamgames.com/doc/features/workshop/implementation#SteamCmd)
- [SteamKit 登录和服务示例](https://github.com/SteamRE/SteamKit/tree/3.3.1/Samples)
- [本地化条目更新接口](https://partner.steamgames.com/doc/api/ISteamUGC#SetItemUpdateLanguage)
- [RimWorld 参考程序集](https://github.com/krafs/RimRef)
