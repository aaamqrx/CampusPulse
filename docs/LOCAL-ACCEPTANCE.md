# 2026-09-30 本机打包前验收

基线：公开 `main` 提交 `8f52264`；本轮修复及验收记录已推送并回读源码提交 `c557257d5b5a74b1e85f23697387cd422c0c698d`。Windows 11 x64，Build 26200。真实密码只由用户在本机维护。

## 已执行

- 只读后台：Running、LocalSystem、延迟 Auto；三个开关均为 true，已保存凭据；系统防睡眠请求存在。
- 正式 WPF：显示公网验证通过、后台运行、自启开启、防睡眠生效；三个开关与后台一致，密码控件为遮蔽输入。使用 Windows UI Automation 读取白名单控件，未读取账号或密码值；尚未验证全部托盘菜单和视觉缩放。
- SEC-01 部分：数据目录只允许 SYSTEM/Administrators；三个数据文件继承该受保护目录权限；程序目录仅允许普通 Users 读取/执行。当前未提升令牌打开配置、凭据、事件文件及连接管道均被拒绝。另一普通本地账号未执行。
- 经用户明确授权，`validate-local.ps1 -Execute -Elevate` 的 8 个原生断言通过：暂停重连保留防睡眠；关闭自启变为 Manual 且服务继续 Running；重新开启为延迟 Auto；关闭无人值守释放请求；重连暂停时仍可单独防睡眠；停止服务释放请求；暂停状态重启后配置和凭据存在性保留；凭据密文字节不变。最后三个开关恢复原值 true，服务 Running。
- 新增假数据检查发现保存失败回滚缺陷：配置被占用时，回滚配置再次失败，阻断旧凭据恢复。修复为跳过未变内容，并确保尝试凭据回滚。增加密码更换/清除与无明文持久化检查。最终四项目 Release 构建 0 警告、0 错误，29/29 离线检查通过。
- 有限恢复策略：Windows 会重复最后一个失败动作；原安装器列表末尾仍为重启。改为 `restart/5000/restart/15000/restart/60000//0`，最终为 NONE。独立测试服务从未启动，系统 API 回读为 86400 秒重置、三个重启及 NONE，之后已删除。安装器与临时 Install 脚本已更新；尚未重新编译安装器，未进行真实崩溃恢复。
- 公开源码：15 个路径白名单暂存，差异检查、明显秘密签名及历史路径扫描通过，提交 `c557257` 推送后远端 `main` 回读一致。仅发布源码与脱敏记录，未上传本机证据或安装包。

## 证据位置与限制

证据保存在忽略目录 `.local/acceptance-20260930/`，不提交：`inspection.json`、`unelevated-access.json`、`ui-inspection.json`、`switch-validation.json`、`offline-before-fix*.txt`、`offline-after-fix.txt`、`check-dev*.txt`、`recovery-actions.json`。

最近 80 条后台事件已回读，最早为 09-30 10:32:57（北京时间），11:39:49 后有两项公网探测成功记录；没有夜间断网及认证时间线。用户接受的一晚跨夜结果仍按现场反馈记录，不补成事件佐证或多夜验证。

上述原生后台验收针对已安装的 09-29 临时副本。新 SecureStore 修复仅在本地源码构建和假数据测试中通过，未部署至该运行副本；其安装器动作也未执行。不得用此页把 M4/M5 整体标为完成。

## 正在准备的现场项

- PWR-03：通过。用户确认拔下充电器并接回，网线不动。只读采样 19 条：12:52:54 进入电池供电，12:52:56 起防睡眠状态和系统请求均释放，12:53:09 插电后均恢复；后台持续 Running，前后电源计划一致。证据 `power-result.json`。
- SYS-04 锁屏部分：用户确认按 Win+L 锁屏约 20 秒并解锁；12:56:26 至 12:57:27 的 30 个只读样本均显示后台 Running，电源计划不变。证据 `lock-result.json` 及用户现场反馈。未单独观察屏幕自动关闭或托盘菜单操作。
- BOOT-01/03 自动启动部分：用户确认已重启；After 回读系统启动 19:29:49、当前账户最早交互会话 19:30:11、服务进程启动 19:32:20，NewBootObserved=true，Running/延迟 Auto。三个开关 true、凭据存在、防睡眠和公网状态均正常。StartedBeforeInteractiveLogon=false，不能认定登录前启动；已向用户询问实际等待/登录方式，尚未回复。证据 `boot-after.json`、`inspection-after-boot.json`。不要把延迟自启配置当成登录前证据。
- SYS-05 S0 现代待机：通过一次。用户确认主动睡眠并唤醒；系统事件 506/507 分别为 13:03:12/13:03:50。后台持续可用，13:05:27 自动检查、13:05:28 公网验证成功，未发送人工 check/reconnect。电源计划不变；13:08:09 已恢复无人值守，三个开关均 true、防睡眠生效、凭据密文不变。证据 `sleep-result.json`、`sleep-network-proof.json`、`restored-after-sleep.json`。传统 S3 与休眠本机不支持/未启用，不修改电源配置去启用。
- SYS-06：现有临时服务无恢复策略。新版自包含 Service 已发布至忽略目录，尚未部署；`validate-recovery.ps1` 已通过语法检查和只显示计划的默认入口，待用户批准后先暂停认证、刷新已标记的临时服务、配置有限恢复，再结束进程一次，验证请求释放和系统拉起，最后恢复三个开关及凭据不变。当前尚未执行故障测试，不得对开启认证的后台反复制造崩溃。
- BOOT-02/05、SEC-01 另一普通用户、SEC-02 完整诊断导出、PWR-01 超过空闲睡眠时长与 PWR-02 崩溃释放仍未完整执行。

本机原插电空闲睡眠时限为 300 秒，电池为 180 秒；未改电源计划。已准备 `watch-local.ps1 -Scenario Idle -IdleSeconds 300 -DurationSeconds 540 -Elevate`，使用真实最后输入时间验证连续空闲超过 330 秒、请求持续及没有待机事件。首次 UAC 被取消，未生成 ready/样本，因此该次未执行，未自动重试。

## 可执行入口

在项目目录运行。首版受限通道需要管理员；`-Elevate` 通过本机 UAC，不会将密码传入命令。

- `powershell -NoProfile -File scripts/inspect-local.ps1 -Elevate`：只读后台、权限和近期事件。
- `powershell -NoProfile -File scripts/inspect-ui.ps1 -Elevate`：只读正式界面白名单控件。
- `powershell -NoProfile -File scripts/validate-local.ps1`：仅显示操作计划，不改状态。
- `powershell -NoProfile -File scripts/validate-local.ps1 -Execute -Elevate`：只有获得开关/后台重启授权后使用；已授权本轮执行并恢复。
- `powershell -NoProfile -File scripts/validate-recovery.ps1`：只显示具体故障测试范围。
- `powershell -NoProfile -File scripts/validate-recovery.ps1 -Execute -Elevate`：须取得故障测试授权；当前尚未执行。
- `powershell -NoProfile -File scripts/watch-local.ps1 -Scenario Power -Elevate`：异步只读监测；派发成功不代表通过，应检查 ready 和 result 文件。
- `powershell -NoProfile -File scripts/watch-local.ps1 -Scenario Sleep -Elevate`：用户先关闭无人值守；比对系统睡眠/恢复事件与唤醒后自动检查，结束后恢复原无人值守设置。首次现场运行采用系统事件与独立后续状态回读共同验收。
- `powershell -NoProfile -File scripts/inspect-boot.ps1 -Phase Before -Elevate`：只读保存重启前基线，不重启电脑。
- `powershell -NoProfile -File scripts/inspect-boot.ps1 -Phase After -Elevate`：用户重启后回读新启动、服务启动及最早交互登录时间。
- `powershell -NoProfile -File scripts/check-dev.ps1`：四项目源码构建及离线检查。

原生项有缺口时保持打包门槛未完成。后续先记录现场结果与适用范围，修复部署验收完成后才重建安装包，再做安装、升级、卸载，最后公开并回读 Release。历史预览包不作为最终产物。

当前接力点：重启后的只读回读已完成，自启成功但登录前启动未证实；正在等待用户说明实际登录过程，以及批准修复版部署/一次故障测试。当前后台仍为 09-29 副本，三个开关 true，公网在线、防睡眠生效。原始/脱敏证据均留在忽略目录，不上传。空闲值守监测的 UAC 取消不作为项目故障，不自动重试。
