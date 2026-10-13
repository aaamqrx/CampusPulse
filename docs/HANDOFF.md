# CampusPulse 接手指南与项目进度

最后更新：2026-10-13。先读本页；行为查 [DEVELOPMENT](DEVELOPMENT.md)，验收要求查 [TESTING-AND-RELEASE](TESTING-AND-RELEASE.md)，历史证据查 [VALIDATION-LOG](VALIDATION-LOG.md)。

## 当前阶段

**首个正式版 [v0.1.0](https://github.com/aaamqrx/CampusPulse/releases/tag/v0.1.0) 已公开并验证下载。** Release id `411329933`，draft=false、prerelease=false，Latest。源码标签提交 `0a97305b33956ac72c1cd406971691423ea54b12`；构建和安装 [Actions 38353484040](https://github.com/aaamqrx/CampusPulse/actions/runs/38353484040) 通过。正式版认证、服务算法、UI 和配置格式沿用 preview.3，仅晋升版本及扩展发布/验收流程。

四项目、55/55 离线检查、13/13 发布来源检查通过。候选 runner 2187/2187；最终标签同包在 Windows Server 2025 build 26100 执行 2187/2187 安装检查，涵盖全新安装、同版重装、服务重启、preview.3 升级、密文/开关/停止与 Manual 状态保留、卸载/重装和外来服务拒绝。WPF 演示/服务实际载入各自随包运行时；不冒充新的校园实测或 Windows 11 全矩阵。

安装包 79,313,741 字节，SHA-256 `8796f43c98791701c21f3cf72d029d5471ad89d3634bd8fe2ed46a0d662c23da`，未签名；公开三附件实际匿名下载与源清单、验收包的完整大小及摘要一致。旧三版 Release/附件保持不变。最终包位于 `artifacts/0.1.0/`；详细报告位于已忽略 `.local/stable/`，候选包不得作为正式附件。

## 用户确认与本机边界

- 用户于 2026-10-13 明确确认连续运行稳定、基本没有问题；接受此现场结论，不重复要求累计夜数、不虚构夜数或本轮独立日志。既有手动认证及 10-04 Automatic 自然恢复证据继续有效。
- **用户明确不要动当前电脑。** 本机仍注册 preview.3，目录 `E:\Apps\CampusPulse`；本轮没有安装/卸载、停止/重启服务、更改开关/密码/电源计划或新建系统数据备份。未验证正式版在本机运行，不能写成已升级。
- 已授权公开源码与首个正式版。以后有新发布任务仍按实际源码、CI、安装及公开下载分别核对；旧标签/附件不移动或覆盖。

## 支持范围与剩余边界

Windows 11 x64、有线校园网；本校 Dr.COM 电信已实测，门户 `http://10.62.164.38/`。其他符合现有页面、脚本和运营商参数模板的 Dr.COM 网络可尝试，尚未实测，不能因页面相似宣称通用兼容。账号密码仅由使用者在本机填写。

三开关独立：开机后台自启、自动重连、插电无人值守。关闭自启设 Manual；暂停重连不关闭防睡眠；检测不提交密码，主动重连按需一次。仅验收公网恢复，不测试 UU。

干净 Windows 11、其他电脑/运营商/DPI、卸载后真实重启和完整安装失败组合仍未全覆盖。正式版按用户确认、云端同包验收及上述披露发布，完整 M5 矩阵不标通过。安装包未签名。出现实际新故障时读取脱敏摘要定位，不泛泛重测已确认行为。

## 开发与发布入口

- `scripts/check-dev.ps1`：四项目构建与 55 项离线控制台；`scripts/check-static.ps1`：版本/脚本/文档检查。
- `scripts/test-release-verification.ps1`：13 项清单来源检查；默认拒绝无标签包，明确的未发布候选模式只用于预演。
- `.github/workflows/preview-release.yml`：名称已改为 Build verified release draft；手动分支运行只验收候选，标签运行重建并验收同包后创建对应渠道草稿。
- `scripts/validate-clean-install.ps1`：仅允许无 CampusPulse 的一次性 GitHub 托管 Windows runner；不在本机运行。
- `.github/workflows/verify-public-release.yml`：正式/预览渠道、公开三附件、大小/摘要和标签来源核对。
- 本轮完整证据与失败保留在 VALIDATION-LOG。发布后的 main 文档提交可能晚于软件标签，不因此重建已发布附件。
