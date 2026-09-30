# 2026-09-30 本机打包前验收

基线：公开 `main` 提交 `8f52264`；本轮修复及验收记录已推送并回读源码提交 `c557257d5b5a74b1e85f23697387cd422c0c698d`。Windows 11 x64，Build 26200。真实密码只由用户在本机维护。

## 已执行

- 只读后台：Running、LocalSystem、延迟 Auto；三个开关均为 true，已保存凭据；系统防睡眠请求存在。
- 正式 WPF：显示公网验证通过、后台运行、自启开启、防睡眠生效；三个开关与后台一致，密码控件为遮蔽输入。使用 Windows UI Automation 读取白名单控件，未读取账号或密码值；尚未验证全部托盘菜单和视觉缩放。
- 窗口生命周期检查部分：用户确认 UAC 后，21:34 打开正式窗口通过，随后调整尺寸前发生 MethodInvocationException；清理脚本自己的窗口后，原后台进程仍 Running。退出码 1，尺寸、最小化及关闭收起尚未验收，待进一步定位自动化调用，不能把脚本调用失败直接归为产品故障。证据 `ui-window-validation.json`。真实托盘菜单及视觉裁切另验。
- SEC-01 部分：数据目录只允许 SYSTEM/Administrators；三个数据文件继承该受保护目录权限；程序目录仅允许普通 Users 读取/执行。当前未提升令牌打开配置、凭据、事件文件及连接管道均被拒绝。另一普通本地账号未执行。
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

本机原插电空闲睡眠时限为 300 秒，电池为 180 秒；未改电源计划。首次 Idle UAC 被取消未执行；用户随后明确要求现在进行。21:35:03 至 21:41:37 取得 196 个样本，最终连续空闲 331.235 秒；全程插电、后台 Running、防睡眠状态/请求有效，电源计划未变。但 21:41:06 有 Kernel-Power 506（Idle Timeout），StandbyEntryCount=1，按当前脚本断言不能计为通过。微软将 S0 现代待机分为 Screen Off 与 Sleep，不能单凭 506 判为实际深度睡眠或产品故障；须用系统报告区分。证据 `idle-result.json`、`idle-samples.jsonl`；`inspect-sleep-report.ps1` 只读生成忽略目录内报告，不改电源计划，报告尚未回读。依据：[Modern Standby States](https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/modern-standby-states)。

## 可执行入口

在项目目录运行。首版受限通道需要管理员；`-Elevate` 通过本机 UAC，不会将密码传入命令。

- `powershell -NoProfile -File scripts/inspect-local.ps1 -Elevate`：只读后台、权限和近期事件。
- `powershell -NoProfile -File scripts/inspect-ui.ps1 -Elevate`：只读正式界面白名单控件。
- `powershell -NoProfile -File scripts/validate-ui-window.ps1 -Elevate`：仅操作脚本自己的正式窗口，不保存设置、不提交认证；完整托盘交互仍需另验。
- `powershell -NoProfile -File scripts/inspect-sleep-report.ps1 -Elevate`：只读生成系统 SleepStudy XML，留在忽略目录；报告生成不等于防睡眠验收通过。
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

当前接力点：修复版部署及一次故障恢复已通过；空闲监测已完成但现代待机阶段待报告区分，窗口自动化错误待定位，新的窗口排查 UAC 尚待确认。用户可以正常操作电脑，无需立即重复空闲/睡眠；登录前启动及其余权限/托盘验收仍未完成。原始/脱敏证据均留在忽略目录，不上传。
