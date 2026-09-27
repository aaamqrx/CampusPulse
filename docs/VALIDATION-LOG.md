# CampusPulse 验证记录

本文件记录已经执行的检查。开发与验收计划见 [TESTING-AND-RELEASE.md](TESTING-AND-RELEASE.md)，未执行项目不填“通过”。

## 2026-09-26：需求调查与只读检查

- **校园门户读取**：用户给定地址可读取，页面包含 Dr.COM 与校园电信配置；进一步查看配置与网页脚本获得当前认证流程线索。结果：门户识别有证据。边界：没有使用真实账号、没有验证登录成功。
- **电源配置读取**：当时读取到插电空闲 5 分钟睡眠、休眠未启用及 Modern Standby 支持。结果：只读调查完成。边界：没有更改电源配置，没有验证防睡眠功能。
- **参考仓库阅读**：读取 README 与主要源码可见部分，形成 10 类功能对照。结果：静态参考完成。边界：没有安装或运行 Campus-Flow，没有声称其适配当前学校。
- **GitHub 账号读取**：连接返回登录账号 `aaamqrx`。结果：账号资料可读取。边界：没有新建仓库、推送源码或上传安装包。
- **开发环境调查**：系统中有 dotnet 命令，SDK 列表为空；工具准备未完成可用性验证。结果：还不能声明具备完整构建环境。

## 2026-09-27：文档与工作区盘点

- **源文件盘点**：Core 项目定义及 Contracts.cs、Service 项目定义存在；App 与 tests 目录不存在。结果：确认仅为早期骨架，未执行构建。
- **脚本盘点**：build.ps1 与 Inno Setup 脚本存在。结果：确认有草稿。边界：没有编译安装器，也没有运行安装/升级/卸载。
- **交付物盘点**：尚无本地 `.git` 目录、可用安装包或已验证发布产物。结果：GitHub 同步与安装包交付均未完成。

- **文档完整性检查：通过**。检查 README 与 docs 下全部 Markdown 的本地链接、代码围栏闭合及交接文档必要主题，未发现失效本地链接或未闭合围栏。执行方式：本机 Python 只读扫描文档；不涉及软件构建或运行。
- **交接一致性复核：通过**。独立复核 HANDOFF、README、开发设计、测试计划、公共契约、构建/安装草稿与实际目录；确认三开关、检测与重连区分、排除 UU、骨架与未完成项的描述一致。本次未重新验证历史门户、SDK 或 GitHub 连接调查。

## 2026-09-27：预览代码与离线构建（无 Git 提交）

- **环境**：Windows x64；从微软安装脚本把 .NET SDK 10.0.203 安装到已忽略的 `.tools/dotnet/`，`dotnet --version` 返回 `10.0.203`。旧 `.tools/dotnet-sdk.zip` 未用作安装或校验依据。
- **初次构建失败**：系统 dotnet 无 SDK；本地 SDK 初次运行因沙箱无法读取用户 NuGet.Config 而失败。经受控权限运行后发现并修复 `SocketOptionName.UnicastInterface`、`PipeAccessRights.None`、WPF `ColorConverter` 三处编译错误。失败记录保留，不计作通过。
- **离线模拟测试通过**：`.tools/dotnet/dotnet.exe run --project tests/CampusPulse.Tests -c Release`，6/6 通过。覆盖 NET-02 单次模拟登录、NET-05 退避边界、NET-06 保守拒绝分类、NET-09 门户身份拦截、NET-10 部分连通阻止认证、NET-12 账号与密码编码。所有 HTTP 均由进程内假响应处理器接管；未连接真实校园认证接口。证据：测试项目及构建日志。
- **构建与自包含发布通过**：`scripts/build.ps1 -DotNetPath .tools/dotnet/dotnet.exe -SkipInstaller` 退出码 0；Core、Service、App、Tests 的 Release 构建和上述离线测试通过，App/Service 自包含 win-x64 文件存在。证据：`artifacts/logs/build-20260927-185832-611.log` 与 `artifacts/build-manifest.json`（两者不提交）。后续单独重建 Core 为 0 警告、0 错误。边界：没有原生运行服务或界面，没有安装包。
- **安装器工具尝试未完成**：已下载的 Inno Setup 安装程序签名状态 `Valid`，签名主体 `Pyrsys B.V.`；静默安装到 `.tools/inno/` 超时停滞，已停止此次启动的两个安装器进程。`ISCC.exe` 未出现，未执行 Inno 编译或产品安装。后续须重新准备编译器并验证结果。
- **安装器工具恢复与完整构建通过**：按 Inno Setup 官方便携参数 `/PORTABLE=1 /CURRENTUSER` 安装编译器至 `.tools/inno/`，`ISCC.exe` 存在；运行 `scripts/build.ps1 -DotNetPath .tools/dotnet/dotnet.exe -InnoCompilerPath .tools/inno/ISCC.exe` 退出码 0。四项目 Release 构建均为 0 警告、0 错误；6/6 模拟测试、自包含发布、Inno 编译均通过。证据：`artifacts/logs/build-20260927-190405-763.log`、`artifacts/build-manifest.json`。安装包 `artifacts/installer/CampusPulse-Setup-0.1.0-preview.1.exe` 为 79,265,682 字节；SHA-256 为 `de3eaa93fca727518a83b0d4571581eacd263c8c2a7917de5f63b730b98d90c6`，与清单和 `sha256.txt` 一致。边界：尚未验证安装包运行。
- **产品安装尝试未通过**：先只读检查本机没有 `CampusPulse` 服务、`Program Files\CampusPulse` 或 `ProgramData\CampusPulse`。用 `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART` 启动本地安装包后约 45 秒未返回，亦未出现服务、数据目录或安装日志；已停止该次启动的两个安装器进程，退出码为 `-1`（人工终止）。随后 `whoami /groups` 显示当前令牌为 Medium Mandatory Level，Administrators 组为 deny only；这与需要交互式提权的安装器停在 UAC 前相符，但尚未直接看到 UAC 界面。因此安装、服务运行、升级卸载仍标记未通过；没有测试真实认证。
- **发布边界**：本地没有 `.git` 仓库；未创建、推送或回读 GitHub 仓库及 Release。真实账号认证、受控恢复、跨夜、安装/升级/卸载均未执行。
- **本地版本控制准备**：在此前没有 `.git` 的目录初始化 `main` 分支；以路径白名单暂存 37 个源码、文档、脚本与项目元数据文件。`git diff --cached --check` 无空白错误；暂存文件名检查未发现 `.local/`、`.tools/`、`artifacts/`、`bin/`、`obj/` 或密钥文件；明显私钥/令牌/长密码字面量模式扫描未命中。当前沙箱账号与目录所有者不同，Git 操作使用单次 `-c safe.directory=E:/Projects/01_CampusPulse`，未修改全局安全目录。尚未提交或上传。

## 后续记录格式

每条记录包含：日期、软件版本/提交标识（若尚未建立则注明）、测试编号、执行环境、操作或命令、预期结果、实际结果、通过/失败/未执行、脱敏证据位置及验证限制。

真实密码、原始抓包、会话、个人内网地址和完整认证请求不作为公开证据。自动化模拟测试、真实校园网络测试和 GitHub 发布检查分别记录。
