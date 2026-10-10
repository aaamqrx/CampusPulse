# CampusPulse 接手指南与项目进度

最后更新：2026-10-11。先读本页，再按问题查相关文档；历史操作和失败证据统一保留在 [VALIDATION-LOG](VALIDATION-LOG.md)。

## 当前阶段

**preview.3 已原位升级、公开并核对下载。** 更新提醒、统一图标、普通 CI 和标签打包已经执行通过；四项目 Release 无警告/错误，55/55 离线检查通过。正式界面、更新控件、浏览器入口及程序/窗口/托盘/安装器图标已检查。版本源码为干净标签 `0a476eba1f3fac95fa928aefd94a8af6cbd4cdda`，普通 Actions `38028418549`、标签 Actions `38062333177` 均 success。

当前本机注册 preview.3，安装目录 `E:\Apps\CampusPulse`。Actions 原始 ZIP 摘要及三附件已核对，同一安装包完成 2186/2186 原位升级和 1471/1471 最终包复查；原密文、三个开关、后台状态/权限保留。首次验收工具误判日志继承权限失败并恢复旧版，修正后核对原配置/程序再重试通过；失败保留在验证记录。

[公开 preview.3](https://github.com/aaamqrx/CampusPulse/releases/tag/v0.1.0-preview.3) 的 Release id 为 `409022538`，draft=false、prerelease=true。公开安装包 SHA-256 为 `0d17c33d3374407cc5f631eb3f23ff616d2828895f2a31aaa4e8f0674f754142`。独立 Windows Actions `38064930518` 和本机匿名下载均核对三附件完整大小/摘要，与验收用 Actions 文件一致；旧两版及附件保持不变。

发布后 main `f8ef990` 仅同步验收工具、文档与检查工作流，普通 CI `38064897097` success，未移动标签或重建 App/Service。`artifacts/0.1.0-preview.3/` 保留已验收的三份 CI 原件，本地脏工作区候选移入已忽略的 `.local/preview3/local-candidate/`，不能作为最终安装包。

此前 [preview.2](https://github.com/aaamqrx/CampusPulse/releases/tag/v0.1.0-preview.2) 来自 `58bc8f0`，43/43、生命周期 2222/2222、最终包 750/750 及公开下载均有记录。用户确认日常稳定运行；10-04 一晚自然恢复有 Automatic 脱敏摘要，不推导本次反馈对应的夜数。本轮未新增校园认证或跨夜样本。

preview.1 的早晨恢复失败已修复。旧失败、两次迁移工具失败及恢复继续保留在验证记录，不能写成当前未修复缺陷。

## 已确定要求

- Windows 11 x64、校园有线网、当前 Dr.COM 电信场景；门户 `http://10.62.164.38/`，旧 `10.62.164.14` 已跳转。
- 每位使用者自行填写门户、运营商、账号和密码；通过页面及所选模板核对后才提交凭据。真实密码只在用户本机输入。
- 恢复时间未知，持续低频检查；只验收公网恢复。
- 自启、自动重连、插电无人值守独立；关闭自启改 Manual，暂停重连不关闭防睡眠。
- 检测不提交密码，主动重连按需一次；退出界面后后台可继续运行。
- 已授权公开 GitHub、安装包以及本轮原位升级和 preview.3 公开预发布，不重复询问是否公开。

## 本轮交付顺序

1. 完成源码、图标和文档，执行构建、离线检查、文档链接及有限敏感内容检查。
2. 推送并回读普通 CI；创建 `v0.1.0-preview.3`，从干净标签生成安装包、校验文件和含 App/Service 载荷哈希的清单，附到草稿。
3. 下载同一 Actions 安装包，校验提交和 SHA-256；从固定 AppId 注册项核对实际目录、精确服务路径和权限。
4. 受保护备份后短暂暂停重连并原位升级，核对新程序、密文、三个开关、启动类型、后台及正式界面，恢复原设置。失败恢复旧版并保留草稿/证据。
5. 最终包通过后公开同一草稿，回读实际下载，再更新 README 下载、交接与验证记录。旧标签/附件不移动或覆盖。

执行结果按时间追加验证记录。YAML、本地构建或文件存在不能写成远端构建/发布通过。

## 文件职责

- [README](../README.md)：普通用户下载、配置、开关、支持范围和反馈。
- [DEVELOPMENT](DEVELOPMENT.md)：行为规范、状态机、凭据与本机服务边界。
- [TESTING-AND-RELEASE](TESTING-AND-RELEASE.md)：离线、原生、升级、校园和发布验收。
- [LOCAL-ACCEPTANCE](LOCAL-ACCEPTANCE.md)：已有本机功能验收；[修复计划](RECOVERY-REPAIR-PLAN.md)保留修复历史。
- `scripts/check-dev.ps1`：四项目构建与离线控制台，不能用 `dotnet test` 替代。
- `scripts/build.ps1`：默认版本取源码常量；标签使用 `-RequireCleanTag`，产物在 `artifacts/<version>/`。
- `ReleaseUpdateChecker`：可注入假 HTTP 客户端，只由设置窗口调用，不加入 LocalSystem 后台。
- `assets/icon/`：原创 SVG、PNG、七尺寸 ICO，可从生成源重建。

旧 implementation-plan 仅供背景；临时服务脚本不能用于移除现有正式安装。

## 边界与下一步

既有开关、故障恢复、电源/睡眠、权限、窗口/托盘、开机和生命周期继续引用原证据，不泛泛重测。新版本单独验收更新、图标、构建和原位升级。

干净 Windows、其他电脑/运营商/DPI、卸载后重启、多夜稳定及完整 M5/稳定版未完成完整矩阵；安装包未签名。

当前下一步：按实际需要补充其他机器/运营商、干净 Windows 与多夜稳定性样本；正常使用出现新故障时读取脱敏摘要定位。正式数据仍在受保护的 ProgramData，私有备份从未进入仓库。最终文档单独推送回读，软件标签和安装包保持上述源码与摘要。

本轮专用备份 `C:\ProgramData\CampusPulse-Preview3Backup-20261010` 已于 2026-10-11 00:02 清理。首次清理 UAC 取消后，用户明确要求重新弹窗，管理员确认通过；738/738 核对原密文、三个开关、后台/启动类型、防睡眠、备份标记/摘要/私有权限和无链接的固定路径，随后仅删除专用备份。正式程序和 ProgramData 数据保留，Failure=null、ProfileRestored=true；证据 `.local/preview3/upgrade-cleanup.json`。原取消记录继续保留。
