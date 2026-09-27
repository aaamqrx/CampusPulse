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

## 2026-09-27：预览代码与离线构建（0.1.0-preview.1）

- **环境**：Windows x64；从微软安装脚本把 .NET SDK 10.0.203 安装到已忽略的 `.tools/dotnet/`，`dotnet --version` 返回 `10.0.203`。旧 `.tools/dotnet-sdk.zip` 未用作安装或校验依据。
- **初次构建失败**：系统 dotnet 无 SDK；本地 SDK 初次运行因沙箱无法读取用户 NuGet.Config 而失败。经受控权限运行后发现并修复 `SocketOptionName.UnicastInterface`、`PipeAccessRights.None`、WPF `ColorConverter` 三处编译错误。失败记录保留，不计作通过。
- **离线模拟测试通过**：`.tools/dotnet/dotnet.exe run --project tests/CampusPulse.Tests -c Release`，6/6 通过。覆盖 NET-02 单次模拟登录、NET-05 退避边界、NET-06 保守拒绝分类、NET-09 门户身份拦截、NET-10 部分连通阻止认证、NET-12 账号与密码编码。所有 HTTP 均由进程内假响应处理器接管；未连接真实校园认证接口。证据：测试项目及构建日志。
- **构建与自包含发布通过**：`scripts/build.ps1 -DotNetPath .tools/dotnet/dotnet.exe -SkipInstaller` 退出码 0；Core、Service、App、Tests 的 Release 构建和上述离线测试通过，App/Service 自包含 win-x64 文件存在。证据：`artifacts/logs/build-20260927-185832-611.log` 与 `artifacts/build-manifest.json`（两者不提交）。后续单独重建 Core 为 0 警告、0 错误。边界：没有原生运行服务或界面，没有安装包。
- **安装器工具尝试未完成**：已下载的 Inno Setup 安装程序签名状态 `Valid`，签名主体 `Pyrsys B.V.`；静默安装到 `.tools/inno/` 超时停滞，已停止此次启动的两个安装器进程。`ISCC.exe` 未出现，未执行 Inno 编译或产品安装。后续须重新准备编译器并验证结果。
- **安装器工具恢复与完整构建通过**：按 Inno Setup 官方便携参数 `/PORTABLE=1 /CURRENTUSER` 安装编译器至 `.tools/inno/`，`ISCC.exe` 存在；运行 `scripts/build.ps1 -DotNetPath .tools/dotnet/dotnet.exe -InnoCompilerPath .tools/inno/ISCC.exe` 退出码 0。四项目 Release 构建均为 0 警告、0 错误；6/6 模拟测试、自包含发布、Inno 编译均通过。证据：`artifacts/logs/build-20260927-190405-763.log`、`artifacts/build-manifest.json`。安装包 `artifacts/installer/CampusPulse-Setup-0.1.0-preview.1.exe` 为 79,265,682 字节；SHA-256 为 `de3eaa93fca727518a83b0d4571581eacd263c8c2a7917de5f63b730b98d90c6`，与清单和 `sha256.txt` 一致。边界：尚未验证安装包运行。
- **产品安装尝试未通过**：先只读检查本机没有 `CampusPulse` 服务、`Program Files\CampusPulse` 或 `ProgramData\CampusPulse`。用 `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART` 启动本地安装包后约 45 秒未返回，亦未出现服务、数据目录或安装日志；已停止该次启动的两个安装器进程，退出码为 `-1`（人工终止）。随后 `whoami /groups` 显示当前令牌为 Medium Mandatory Level，Administrators 组为 deny only；这与需要交互式提权的安装器停在 UAC 前相符，但尚未直接看到 UAC 界面。因此安装、服务运行、升级卸载仍标记未通过；没有测试真实认证。
- **发布边界**：本地没有 `.git` 仓库；未创建、推送或回读 GitHub 仓库及 Release。真实账号认证、受控恢复、跨夜、安装/升级/卸载均未执行。
- **本地版本控制准备**：在此前没有 `.git` 的目录初始化 `main` 分支；以路径白名单暂存 37 个源码、文档、脚本与项目元数据文件。`git diff --cached --check` 无空白错误；暂存文件名检查未发现 `.local/`、`.tools/`、`artifacts/`、`bin/`、`obj/` 或密钥文件；明显私钥/令牌/长密码字面量模式扫描未命中。当前沙箱账号与目录所有者不同，Git 操作使用单次 `-c safe.directory=E:/Projects/01_CampusPulse`，未修改全局安全目录。使用 GitHub 已连接账号的 ID 格式 no-reply 地址创建本地提交 `b63b30c`；未创建或上传远端仓库。
- **补充离线测试及重建通过**：在本地提交 `799eceb` 后增加 NET-01 双探测已联网跳过认证、NET-08 伪 HTTP 200 不判联网、SYS-03 路径变化时阻止提交凭据。`.tools/dotnet/dotnet.exe run --project tests/CampusPulse.Tests -c Release` 返回 9/9 通过；再次运行完整 `scripts/build.ps1` 退出码 0，构建、9 项模拟测试、自包含发布、Inno 编译均通过。证据：`artifacts/logs/build-20260927-191414-086.log` 和 `artifacts/build-manifest.json`。重建安装包 79,254,977 字节，SHA-256 `deb90f0d2d8f0a5b2d91af79362cc9e936882faded366408d61e83c699b199b0`，与清单及 `sha256.txt` 一致；旧安装包哈希已作废。本次没有再执行产品安装，先前安装失败边界保持不变。

## 2026-09-27：切换为功能先验、最后打包（提交 `c065a78` 后）

- **用户顺序调整**：用户要求先不通过安装包测试，在所有功能修改完成后再打包 EXE。历史本地安装包保留在已忽略的 `artifacts/` 中，不作为当前验收入口；本轮没有运行安装器。
- **无打包开发检查通过**：新增 `scripts/check-dev.ps1`；执行 `scripts/check-dev.ps1 -DotNetPath .tools/dotnet/dotnet.exe` 及文档入口 `powershell -NoProfile -File scripts/check-dev.ps1`，两次退出码均为 0。Core、Service、App、Tests 的 Release 构建均为 0 警告、0 错误，9/9 项进程内模拟测试通过。这些命令没有执行 `publish`、Inno 编译、Windows 服务安装或真实认证。证据：该脚本及本次命令输出；原生窗口/后台运行和校园网结果仍未验证。
- **WPF 演示窗口启动检查**：执行 `.tools/dotnet/dotnet.exe src/CampusPulse.App/bin/Release/net10.0-windows/CampusPulse.App.dll --smoke-test`，进程保持运行，`Get-Process` 读取到窗口标题“CampusPulse · 校园网自动连接（演示，未联网）”、非零主窗口句柄及 `Responding=True`。这只证明演示窗口在当前 Windows 会话中启动；尚未检查视觉排版、控件操作、后台服务或真实校园网。新增 `scripts/open-demo.ps1` 作为可重复打开入口。

## 2026-09-27：公开源码仓库创建与回读（首次推送 `4d2acac`）

- **上传前检查**：本地 `main` 为 `4d2acac3b69704ac2aac704814bfecc7eee18be9`；检查 39 个已跟踪文件和 Git 历史，未发现 `.local/`、`.tools/`、`artifacts/`、`bin/`、`obj/` 或明显凭据文件。工作区另有 5 个未提交的源码改动，本次首次推送未包含这些改动。边界：模式扫描不能代替对未来每次提交的审查。
- **创建与推送**：GitHub 连接器在创建前查询 `aaamqrx/CampusPulse` 返回不存在；本机 Git Credential Manager 已有 `aaamqrx` 登录，使用其现有凭据临时运行 `gh api user --jq .login`，返回 `aaamqrx`。`gh repo create aaamqrx/CampusPulse --public` 退出码 0；`git -c safe.directory=E:/Projects/01_CampusPulse push -u origin main` 退出码 0。设备验证码登录流程已按用户偏好取消，没有记录或输出凭据。
- **远端回读**：GitHub 连接器读取到仓库 `aaamqrx/CampusPulse`，仓库 ID `1390752756`、可见性 `public`、默认分支 `main`；`git ls-remote origin refs/heads/main` 返回 `4d2acac3b69704ac2aac704814bfecc7eee18be9`；连接器读取远端 `README.md` 成功。仓库地址：[aaamqrx/CampusPulse](https://github.com/aaamqrx/CampusPulse)。结果：预览源码公开上传通过；GitHub Actions 与 Release 未执行。此次没有重新构建、运行原生服务、真实认证、跨夜测试或安装生命周期测试。

## 后续记录格式

每条记录包含：日期、软件版本/提交标识（若尚未建立则注明）、测试编号、执行环境、操作或命令、预期结果、实际结果、通过/失败/未执行、脱敏证据位置及验证限制。

真实密码、原始抓包、会话、个人内网地址和完整认证请求不作为公开证据。自动化模拟测试、真实校园网络测试和 GitHub 发布检查分别记录。
