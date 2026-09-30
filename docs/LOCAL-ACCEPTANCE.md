# 2026-09-30 本机打包前验收

基线：公开 `main` 提交 `8f52264`；本轮修复及验收记录已推送并回读源码提交 `c557257d5b5a74b1e85f23697387cd422c0c698d`。Windows 11 x64，Build 26200。真实密码只由用户在本机维护。

## 已执行

- 只读后台：Running、LocalSystem、延迟 Auto；三个开关均为 true，已保存凭据；系统防睡眠请求存在。
- 正式 WPF：显示公网验证通过、后台运行、自启开启、防睡眠生效；三个开关与后台一致，密码控件为遮蔽输入。使用 Windows UI Automation 读取白名单控件，未读取账号或密码值；尚未验证全部托盘菜单和视觉缩放。
- 窗口生命周期：首轮及补诊断重跑均在 UIA Resize 调用抛 InvalidOperationException，退出码 1；原失败保存在 `ui-window-failed-resize.json`，不作为产品缺陷结论。改用原生 SetWindowPos 并核对实际尺寸后，22:08:30 至 22:08:38 退出码 0、8 个断言通过：正式窗口、最小尺寸、最小化、关闭收起、恢复可见、配置/凭据字节不变、界面进程退出后同一后台持续 Running。证据 `ui-window-validation.json`。恢复可见由 Win32 完成，不能代替真实托盘菜单或视觉裁切验收。
- 真实托盘/视觉验收：用户确认最小尺寸滚动可用、关闭并双击托盘、右键“打开”、右键“退出界面（后台继续）”四步全部正常，已退出界面。22:16:59 `verify-ui-manual.ps1 -Elevate` 退出码 0，确认测试界面已退出、同一后台 PID 继续 Running、配置和加密凭据字节不变。证据用户现场反馈、`ui-manual-baseline.json`、`ui-manual-after.json`。这是本机当前缩放/尺寸的一次交互验收，其他 DPI 或机器未验证。
- SEC-01 部分：数据目录只允许 SYSTEM/Administrators；三个数据文件继承该受保护目录权限；程序目录仅允许普通 Users 读取/执行。当前未提升令牌打开配置、凭据、事件文件及连接管道均被拒绝。另一普通本地账号未执行。已准备 `validate-other-user.ps1`，默认只显示计划；PowerShell 解析及 C# 助手在 Windows PowerShell 编译通过。执行分支会临时创建固定普通账号、随机密码仅存内存，验证文件打开/管道连接被拒绝后按 SID 核对清理；尚待用户确认修改 Windows 用户列表，不冒充已执行。
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
- BOOT-02/05、SEC-01 另一普通用户、SEC-02 完整诊断导出、PWR-01 超过空闲睡眠时长仍未完整执行。

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
- `powershell -NoProfile -File scripts/validate-other-user.ps1 -Execute -Elevate`：须先获用户对临时创建/删除普通账号的确认；当前未执行。
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

当前接力点：修复版部署、一次故障恢复、一次插电持续空闲、窗口生命周期及本机托盘/视觉操作已通过。界面已退出，后台和数据未变。另一普通用户权限脚本已准备，等待用户确认临时账号范围；另已询问是否有干净 Windows 测试环境。登录前启动及自启关闭后下一次开机/手动使用仍缺证据，安装包未重建。原始/脱敏证据均留在忽略目录，不上传。
