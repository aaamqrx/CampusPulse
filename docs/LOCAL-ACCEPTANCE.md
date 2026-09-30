# 2026-09-30 本机打包前验收

基线：公开 `main` 提交 `8f52264`；本轮修复及验收记录已推送并回读源码提交 `c557257d5b5a74b1e85f23697387cd422c0c698d`。Windows 11 x64，Build 26200。真实密码只由用户在本机维护。

## 已执行

- 只读后台：Running、LocalSystem、延迟 Auto；三个开关均为 true，已保存凭据；系统防睡眠请求存在。
- 正式 WPF：显示公网验证通过、后台运行、自启开启、防睡眠生效；三个开关与后台一致，密码控件为遮蔽输入。使用 Windows UI Automation 读取白名单控件，未读取账号或密码值；尚未验证全部托盘菜单和视觉缩放。
- 窗口生命周期：首轮及补诊断重跑均在 UIA Resize 调用抛 InvalidOperationException，退出码 1；原失败保存在 `ui-window-failed-resize.json`，不作为产品缺陷结论。改用原生 SetWindowPos 并核对实际尺寸后，22:08:30 至 22:08:38 退出码 0、8 个断言通过：正式窗口、最小尺寸、最小化、关闭收起、恢复可见、配置/凭据字节不变、界面进程退出后同一后台持续 Running。证据 `ui-window-validation.json`。恢复可见由 Win32 完成，不能代替真实托盘菜单或视觉裁切验收。
- 真实托盘/视觉验收：用户确认最小尺寸滚动可用、关闭并双击托盘、右键“打开”、右键“退出界面（后台继续）”四步全部正常，已退出界面。22:16:59 `verify-ui-manual.ps1 -Elevate` 退出码 0，确认测试界面已退出、同一后台 PID 继续 Running、配置和加密凭据字节不变。证据用户现场反馈、`ui-manual-baseline.json`、`ui-manual-after.json`。这是本机当前缩放/尺寸的一次交互验收，其他 DPI 或机器未验证。
- SEC-01 文件及通道访问：通过本机已执行范围。受保护 ACL、当前未提升令牌的三份文件/管道拒绝已有证据。用户授权后，22:32 `validate-other-user.ps1 -Execute -Elevate` 退出码 0：新普通本地账号身份核对、三份固定文件打开拒绝、固定本机管道连接拒绝五断言均 true；未读取文件内容/发送控制命令，账号按 SID 核对后已删除，后台 PID、配置及凭据字节不变。采用本机 network logon 身份模拟，不创建交互会话/用户配置文件。首次描述超过系统 48 字符限制，绑定失败且未创建账号，保留失败证据；修正描述后通过。证据 `other-user-before-description-fix.json`、`other-user-validation.json`。
- 经用户明确授权，`validate-local.ps1 -Execute -Elevate` 的 8 个原生断言通过：暂停重连保留防睡眠；关闭自启变为 Manual 且服务继续 Running；重新开启为延迟 Auto；关闭无人值守释放请求；重连暂停时仍可单独防睡眠；停止服务释放请求；暂停状态重启后配置和凭据存在性保留；凭据密文字节不变。最后三个开关恢复原值 true，服务 Running。
- 新增假数据检查发现保存失败回滚缺陷：配置被占用时，回滚配置再次失败，阻断旧凭据恢复。修复为跳过未变内容，并确保尝试凭据回滚。增加密码更换/清除与无明文持久化检查。最终四项目 Release 构建 0 警告、0 错误，29/29 离线检查通过。
- 有限恢复策略：Windows 会重复最后一个失败动作；原安装器列表末尾仍为重启。改为 `restart/5000/restart/15000/restart/60000//0`，最终为 NONE。独立测试服务从未启动，系统 API 回读为 86400 秒重置、三个重启及 NONE，之后已删除。21:06 已应用到现有临时后台并通过一次异常结束恢复，详见下文；安装器尚未重新编译或执行。
- 公开源码：15 个路径白名单暂存，差异检查、明显秘密签名及历史路径扫描通过，提交 `c557257` 推送后远端 `main` 回读一致。仅发布源码与脱敏记录，未上传本机证据或安装包。

## 证据位置与限制

证据保存在忽略目录 `.local/acceptance-20260930/`，不提交：`inspection.json`、`unelevated-access.json`、`ui-inspection.json`、`switch-validation.json`、`offline-before-fix*.txt`、`offline-after-fix.txt`、`check-dev*.txt`、`recovery-actions.json`。

最近 80 条后台事件已回读，最早为 09-30 10:32:57（北京时间），11:39:49 后有两项公网探测成功记录；没有夜间断网及认证时间线。用户接受的一晚跨夜结果仍按现场反馈记录，不补成事件佐证或多夜验证。

日间原生验收针对 09-29 临时副本；21:06 已部署新 SecureStore 修复版，三个运行文件哈希匹配，并完成下述故障验收。安装器动作尚未执行，不得用此页把 M4/M5 整体标为完成。

## 正在准备的现场项

- PWR-03：通过。用户确认拔下充电器并接回，网线不动。只读采样 19 条：12:52:54 进入电池供电，12:52:56 起防睡眠状态和系统请求均释放，12:53:09 插电后均恢复；后台持续 Running，前后电源计划一致。证据 `power-result.json`。
- SYS-04 锁屏部分：用户确认按 Win+L 锁屏约 20 秒并解锁；12:56:26 至 12:57:27 的 30 个只读样本均显示后台 Running，电源计划不变。证据 `lock-result.json` 及用户现场反馈。未单独观察屏幕自动关闭或托盘菜单操作。
- BOOT-01/03 自动启动部分：用户确认已重启；After 回读系统启动 19:29:49、当前账户最早交互会话 19:30:11、服务进程启动 19:32:20，NewBootObserved=true，Running/延迟 Auto。三个开关 true、凭据存在、防睡眠和公网状态均正常。StartedBeforeInteractiveLogon=false，不能认定登录前启动；已向用户询问实际等待/登录方式，尚未回复。证据 `boot-after.json`、`inspection-after-boot.json`。不要把延迟自启配置当成登录前证据。
- SYS-05 S0 现代待机：通过一次。用户确认主动睡眠并唤醒；系统事件 506/507 分别为 13:03:12/13:03:50。后台持续可用，13:05:27 自动检查、13:05:28 公网验证成功，未发送人工 check/reconnect。电源计划不变；13:08:09 已恢复无人值守，三个开关均 true、防睡眠生效、凭据密文不变。证据 `sleep-result.json`、`sleep-network-proof.json`、`restored-after-sleep.json`。传统 S3 与休眠本机不支持/未启用，不修改电源配置去启用。
- SYS-06/PWR-02 异常结束部分：用户指示“下一步”后执行已说明的 `validate-recovery.ps1 -Execute -Elevate`，21:05:59 至 21:06:20，退出码 0，10 个断言通过、Restored=true。先暂停认证，再刷新固定临时服务并回读有限恢复策略；一次结束已核对的进程，系统电源请求立即释放，SCM 约 5 秒后以新 PID 自动恢复后台；暂停认证、凭据存在性及防睡眠请求均恢复。三个运行文件哈希与新版副本一致。最后三个开关恢复原值、凭据密文未变。证据 `recovery-validation.json`、`refresh-fix.txt`。只制造一次进程终止，未验证连续四次故障后的 NONE 行为；安装器配置仍未执行。
- BOOT-02/05 与登录前运行仍未完整执行；SEC-02 完整诊断导出也未完成，不将已有假密码持久化检查写成全范围通过。
- 两次重启准备：用户明确同意现在继续。`validate-boot-cycle.ps1 -Phase Disable -Execute -Elevate` 22:38:59 退出码 0，保存原三个开关/凭据摘要及启动基线，仅把自启设为 false，服务仍 Running/Manual。22:40:48 `inspect-local.ps1 -Elevate` 确认 Enabled/UnattendedMode=true、HasPassword=true、防睡眠及公网状态正常。首次重启后须先执行 AfterManualBoot，不启动服务；用户随后从正式界面手动启动，再执行 AfterManualStart 和 Restore，最后第二次重启核对登录前运行。当前等待首次重启，原自启恢复尚未执行。证据 `boot-cycle-original.json`、`boot-cycle-disable.json`、`boot-disabled-before.json`、`inspection-before-disabled-boot.json`；原首次重启证据另保存在 `initial-boot-*.json`。
- BOOT-02/05 本次：通过。用户确认第一次重启后先只读回读，系统启动 22:46:52，22:49 AfterManualBoot 退出码 0，NewBootObserved=true、Stopped/Manual、自启保存为 false、凭据字节未变。仅打开正式窗口并核对启动按钮可用，没有代理启动服务；用户点击“启动后台服务”，确认后台运行、自启仍关闭及其他两个开关开启。22:52:59 AfterManualStart 退出码 0，Running/Manual、凭据未变。22:53:07 Restore 退出码 0，原三个开关均 true，Running/Auto、凭据字节未变；已保存 `boot-auto-before.json`，待第二次重启。证据 `boot-cycle-aftermanualboot.json`、`boot-disabled-after.json`、`ui-before-manual-start.json`、用户反馈、`boot-cycle-aftermanualstart.json`、`boot-cycle-restore.json`。
- 界面新发现：后台停止且未加载过快照时，密码提示仍使用 XAML 默认“尚未保存密码”；后台文件及密文均存在，不能把该文本当作真实凭据状态。源码定位为初始 PasswordHint 及 ShowDisconnected 未刷新提示，记录待修正/构建；本次未清除凭据或要求重新输入密码。

PWR-01 本机一次插电空闲验收：通过。原插电空闲睡眠时限 300 秒、电池 180 秒，未改电源计划。21:35:03 至 21:41:37 取得 196 个样本，最终连续空闲 331.235 秒，全程插电、后台 Running、防睡眠状态/请求有效，电源计划未变。初始脚本因 21:41:06 Kernel-Power 506 而严格判为未通过，保留原结果；后续 `inspect-sleep-report.ps1` 退出码 0，系统报告将该段明确分类为 Screen Off（21:41:06 至 21:45:21），覆盖最后样本，没有重叠 Sleep 阶段。`interpret-idle-report.ps1` 退出码 0，结合采样与报告得到 Passed=true。证明允许息屏及此次空闲期间没有实际 Sleep，不证明整夜或其他设备。证据 `idle-result.json`、`idle-sleepstudy.xml`、`idle-phase-verdict.json`；原始系统报告仅留忽略目录，公开摘要不含机器/应用历史。依据：[Modern Standby States](https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/modern-standby-states)。

## 可执行入口

在项目目录运行。首版受限通道需要管理员；`-Elevate` 通过本机 UAC，不会将密码传入命令。

- `powershell -NoProfile -File scripts/inspect-local.ps1 -Elevate`：只读后台、权限和近期事件。
- `powershell -NoProfile -File scripts/inspect-ui.ps1 -Elevate`：只读正式界面白名单控件。
- `powershell -NoProfile -File scripts/validate-ui-window.ps1 -Elevate`：仅操作脚本自己的正式窗口，不保存设置、不提交认证；完整托盘交互仍需另验。
- `powershell -NoProfile -File scripts/inspect-sleep-report.ps1 -Elevate`：只读生成系统 SleepStudy XML，留在忽略目录；报告生成不等于防睡眠验收通过。
- `powershell -NoProfile -File scripts/interpret-idle-report.ps1`：将已有系统报告与空闲采样对应，保存脱敏分阶段结论；不重新监测或修改系统。
- `powershell -NoProfile -File scripts/inspect-ui.ps1 -KeepOwnWindow -Elevate`：保留正式窗口用于用户手动验收，并保存本机基线。
- `powershell -NoProfile -File scripts/verify-ui-manual.ps1 -Elevate`：用户退出界面后回读进程及文件字节不变；交互结论仍需用户确认。
- `powershell -NoProfile -File scripts/validate-other-user.ps1`：仅显示临时账号权限检查计划，不改系统。
- `powershell -NoProfile -File scripts/validate-other-user.ps1 -Execute -Elevate`：本轮已授权执行并清理，不需重复。
- `powershell -NoProfile -File scripts/validate-boot-cycle.ps1 -Phase AfterManualBoot -Elevate`：第一次重启后先只读，不启动后台。
- `powershell -NoProfile -File scripts/validate-boot-cycle.ps1 -Phase AfterManualStart -Elevate`：用户从界面启动后台后回读。
- `powershell -NoProfile -File scripts/validate-boot-cycle.ps1 -Phase Restore -Execute -Elevate`：恢复原开关并保存第二次重启基线，用户随后在登录界面等待约 3 分钟。
- `powershell -NoProfile -File scripts/validate-local.ps1`：仅显示操作计划，不改状态。
- `powershell -NoProfile -File scripts/validate-local.ps1 -Execute -Elevate`：只有获得开关/后台重启授权后使用；已授权本轮执行并恢复。
- `powershell -NoProfile -File scripts/validate-recovery.ps1`：只显示具体故障测试范围。
- `powershell -NoProfile -File scripts/validate-recovery.ps1 -Execute -Elevate`：须取得故障测试授权；本轮已授权并执行一次，不重复制造故障。
- `powershell -NoProfile -File scripts/watch-local.ps1 -Scenario Power -Elevate`：异步只读监测；派发成功不代表通过，应检查 ready 和 result 文件。
- `powershell -NoProfile -File scripts/watch-local.ps1 -Scenario Sleep -Elevate`：用户先关闭无人值守；比对系统睡眠/恢复事件与唤醒后自动检查，结束后恢复原无人值守设置。首次现场运行采用系统事件与独立后续状态回读共同验收。
- `powershell -NoProfile -File scripts/inspect-boot.ps1 -Phase Before -Elevate`：只读保存重启前基线，不重启电脑。
- `powershell -NoProfile -File scripts/inspect-boot.ps1 -Phase After -Elevate`：用户重启后回读新启动、服务启动及最早交互登录时间。
- `powershell -NoProfile -File scripts/check-dev.ps1`：四项目源码构建及离线检查。

原生项有缺口时保持打包门槛未完成。后续先记录现场结果与适用范围，修复部署验收完成后才重建安装包，再做安装、升级、卸载，最后公开并回读 Release。历史预览包不作为最终产物。

第二次开机：用户确认在登录界面等待约 3 分钟。初次两项只读提权回读因 UAC 取消退出码 1，没有新的通过结果；合并后 23:20 回读退出码 0。系统启动 22:58:25，账户会话 22:58:46、桌面 shell 22:58:48，后台自动启动 23:01:00。进一步只读审计取得当前账户 4800 锁定 22:58:48、4801 解锁 23:07:21；23:29 增强版 inspect-boot 退出码 0，StartedWhileLockedBeforeUserUnlock=true，但 StartedBeforeInteractiveLogon=false。BOOT-03 本次自动启动及用户解锁前运行通过，不宣称早于所有账户会话；首次安装默认行为仍待安装生命周期。后台 Running/Auto，原三个开关 true、HasPassword=true、防睡眠有效；未手动启动或重启服务。证据 `boot-auto-after.json`、`boot-unlock-events.json`、`inspection-after-auto-boot.json`、用户反馈。

断连提示修正：初始 PasswordHint 改为正在读取，ShowDisconnected 明确暂无法确认凭据状态，快照到达后才显示已保存/未保存。四项目 Release 构建及 29/29 离线检查退出码 0；尚未原生复核新断连提示，不要求用户重输密码。证据 `check-dev-password-hint-fix.txt`。

SEC-02 追加离线范围：假密码保存后，分别模拟拒绝响应正文回显密码/含密码 URL，以及 HttpRequestException 携带完整模拟请求 URL。两个场景均只发一次进程内假认证；检查持久化配置/凭据/事件、status 回复和正式复制功能共用的诊断文本，未发现明文密码标记或 user_password 参数。四项目 Release 0 警告/错误、30/30 离线检查通过，退出码 0，证据 `check-dev-diagnostic-security.txt`。未操作用户剪贴板、访问真实校园接口或宣称完成所有异常组合；模拟文件清理完成。

当前接力点：本机现场项已按上述范围取得结果，不再安排第三次重启。准备重新构建安装包，再实测本机安装、升级、卸载及受保护数据恢复。用户没有干净 Windows 环境，安装门槛缺口保留；安装包尚未重建，Release 未发布。原始/脱敏证据均留在忽略目录，不上传。
