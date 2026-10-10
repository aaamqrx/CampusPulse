<p align="center"><img src="assets/icon/campuspulse.png" width="88" alt="CampusPulse 网络脉冲图标"></p>

# CampusPulse

校园有线网恢复供网后，自动认证并验证互联网是否恢复。面向 Windows 11 x64，设置窗口与后台服务分开运行。

## 下载

当前已发布版本：[v0.1.0-preview.2](https://github.com/aaamqrx/CampusPulse/releases/tag/v0.1.0-preview.2)。

[下载安装包](https://github.com/aaamqrx/CampusPulse/releases/download/v0.1.0-preview.2/CampusPulse-Setup-0.1.0-preview.2.exe) · [SHA-256 校验文件](https://github.com/aaamqrx/CampusPulse/releases/download/v0.1.0-preview.2/sha256.txt)

安装包包含运行环境，需要管理员权限，目前未签名。preview.2 安装包 SHA-256：`4480aaffd22ff601d4a089e4b9a45ae6ac7cc7fae01b743ff77e3916cc224f28`。

**preview.3 正在验收**：新增软件更新提醒、统一图标、GitHub 自动构建检查与标签草稿打包。源码已完成四项目本地构建及 55 项离线检查；新包升级与公开结果以[验证记录](docs/VALIDATION-LOG.md)为准。

## 首次使用

1. 安装后打开 CampusPulse，可选择安装位置；账号数据保存在受保护的 ProgramData 目录。
2. 填写学校门户首页、运营商、校园网账号和密码。当前默认门户为 `http://10.62.164.38/`。
3. 点击“保存设置”。每位使用者自行输入密码，密码只在本机受保护保存。
4. “立即检测”只检查网络；需要登录时可点击“立即重连”，按需尝试一次认证。
5. 勾选“自动重连校园网”并保存；持续插电无人值守时，可另选“插电无人值守模式”。

首次安装默认开启开机自启，自动重连和无人值守关闭。设置窗口需要管理员权限。

## 三个独立开关

- **开机自动运行后台**：电脑重启后启动后台，不自动弹出设置窗口；关闭后改为手动启动。
- **自动重连校园网**：持续低频检测，供网恢复后按需要认证；无需知道早晨恢复时间。
- **插电无人值守模式**：插电且后台运行时防止空闲睡眠，允许息屏和锁屏；暂停重连不关闭此功能。

## 使用注意

关闭窗口会收起到托盘；“退出界面（后台继续）”只退出设置窗口。点击“停止后台服务”会同时结束自动重连和防睡眠。

软件将校园认证成功和公网恢复分开判断，部分连通不会触发连续重复登录。校内网选项只表示校内网络可用。

preview.3 的“软件更新”在设置窗口打开时检查一次，也可手动检查；预览版可提示后续预览或稳定版本。点击“查看更新”打开本仓库发布页面，安装仍由使用者主动完成。网络失败或 GitHub 限流时可稍后重试。

当前门户使用 HTTP。本机加密保存不改变学校接口的传输方式；请只填写学校提供的门户。

## 支持范围与验证

- Windows 11 x64、有线网、与当前学校相同的 Dr.COM 页面和接口配置。支持运营商选择和校内 HTTP IPv4 门户首页输入。
- 校园电信完成过真实手动认证及公网复查；修复后台完成过一晚自然自动恢复，用户已确认日常稳定运行。
- preview.2 的本机安装生命周期、最终包复验和公开下载校验已有记录。preview.3 新增功能按本轮证据独立验收。
- 其他电脑、干净 Windows、其他运营商/学校协议、其他 DPI、卸载后重启及多夜稳定性未完成完整验证，继续作为预览版提供。

详细结果和失败历史见[验证记录](docs/VALIDATION-LOG.md)。

## 反馈

[提交问题](https://github.com/aaamqrx/CampusPulse/issues)。请提供软件版本、Windows 版本、场景和实际结果；可点击“复制事件”附上脱敏记录。请勿上传账号密码、完整抓包或带认证参数的地址。

## 开发

使用 C#、.NET 10、WPF、Windows Service 和 Inno Setup；SDK 固定为 `10.0.203`。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check-static.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check-dev.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1
```

普通提交与 PR 自动检查；预览标签从干净源码构建安装包和清单，仅创建草稿，经最终包验收后公开。离线控制台检查不安装服务或提交校园认证。

接手先读[交接指南](docs/HANDOFF.md)，行为见[开发设计](docs/DEVELOPMENT.md)，验收见[测试与发布计划](docs/TESTING-AND-RELEASE.md)。演示窗口入口为 `scripts/open-demo.ps1`，显示模拟状态。

`.local/`、`.tools/`、`artifacts/`、凭据与本机日志不提交。图标为原创矢量标记，可用 `scripts/generate-icon.py` 和 Pillow 重新生成。

## 许可证

[MIT License](LICENSE)。独立实现；[Campus-Flow](https://github.com/zuijiu888/Campus-Flow) 为功能参考，不作为兼容性或实测证明。
