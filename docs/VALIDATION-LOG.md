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

## 2026-09-27：运营商选择与自填门户地址（基于 `9adaffb` 的工作区）

- **只读门户核对：通过，未认证**。用户截图显示“中国联通、中国移动、中国电信、校内网（无外网）”。只读请求 `http://10.62.164.14/` 返回 Dr.COM 首页；首页旧 `carrier` 字段仍写 `@lt`、`@dx`。按已保存的 `page/loadConfig` 页面标识读取浏览器加载的 `pc.js`，四个实际 `option value` 依次为 `@unicom`、`@cmcc`、`@telecom`、空值。请求只读取首页及模板，未提交账号密码。证据：忽略目录 `.local/` 的历史配置、用户截图及本次只读命令输出；动态模板未来可能变化，运行时仍须重新核对。
- **首次开发检查失败**：`powershell -NoProfile -File scripts/check-dev.ps1` 退出码 1。Core、Service 已构建，App 构建时旧演示窗口进程占用 `CampusPulse.Core.dll`，复制失败；用户随后确认演示窗口已关闭。此次失败没有计入通过，也不是源码编译错误。
- **运营商改动离线检查通过**：关闭旧窗口后同一命令退出码 0，四项目 Release 构建 0 警告、0 错误，13/13 进程内模拟测试通过。新增四选项账号后缀、模板缺失时阻止提交、错配后缀与校内网不误报公网。边界：未运行正式后台或真实认证。
- **自填地址改动离线检查通过**：再次运行 `powershell -NoProfile -File scripts/check-dev.ps1`，退出码 0，四项目 Release 构建 0 警告、0 错误，15/15 进程内模拟测试通过。新增校内私有 IPv4 门户地址输入校验及模拟自填地址的固定目标检查；所有模拟 HTTP 由测试进程内处理。证据：`src/CampusPulse.Core/PortalEndpoint.cs`、`tests/CampusPulse.Tests/Program.cs` 及本次命令输出。边界：新界面控件未在原生窗口逐项操作，服务配置迁移与权限未在系统中验收；没有真实校园登录、跨夜、安装、重新打包或 Release。
- **旧凭据绑定离线检查通过**：新增服务存储测试后运行同一命令，退出码 0，四项目 Release 构建 0 警告、0 错误，16/16 项模拟与临时目录存储检查通过。假数据测试确认旧配置加载时自动重连关闭、旧协议版本凭据不再读取，且新版凭据须同时匹配账号、运营商及门户地址。证据：`src/CampusPulse.Service/SecureStore.cs`、`tests/CampusPulse.Tests/Program.cs` 与本次命令输出。边界：临时目录使用假密码；没有系统服务身份、ACL、真实密码或 GUI 原生交互验收。
- **新版 WPF 演示启动：通过**。以 `--smoke-test` 启动当前构建的 App DLL，2 秒后进程 `Responding=True`，取得非零主窗口句柄与“CampusPulse · 校园网自动连接（演示，未联网）”标题；仅停止本次启动的进程。该模式没有连接服务或校园网络。边界：未逐项点击新控件、保存设置或检查视觉排版，不能作为正式界面和后台联动通过证据。
- **公开源码暂存检查**：按路径白名单暂存 19 个源码、测试、文档文件，`git diff --cached --check` 无空白错误，暂存路径未含 `.local/`、`.tools/`、`artifacts/`、构建输出或凭据文件。首次明显敏感模式扫描因 Git 参数写法错误未完成；修正后宽泛的 `user_password=` 规则仅命中离线测试中明确写出的 `dummy` 编码断言。单独复核该行后，私钥、GitHub/OpenAI 令牌模式和路径检查通过。模式扫描不能证明绝对无敏感数据，发布前仍需逐项审查暂存内容。
- **源码推送与回读：通过**。将源码里程碑 `8d678d0e5152249c1ee1c97fda547c3300418003` 推送到公开仓库 `aaamqrx/CampusPulse` 的 `main`；`git ls-remote origin refs/heads/main` 返回相同提交，GitHub 连接器回读新增 `src/CampusPulse.Core/PortalEndpoint.cs` 成功。没有上传 `.local/`、`.tools/`、`artifacts/` 或安装包；没有创建 Release。本条只证明源码公开，不证明系统功能或真实校园认证通过。

## 2026-09-27：运营商模板范围校验（`40d8129` 基础上的本地改动）

- **NET-09/NET-12 离线检查：通过**。Windows 11 x64，版本 `0.1.0-preview.1`；运行 `powershell -NoProfile -File scripts/check-dev.ps1`，退出码 0。Core、Service、App、Tests 四项目 Release 构建均为 0 警告、0 错误；17/17 项离线检查通过。新增模拟场景证实：别的下拉框含有所选后缀、或运营商下拉框内后缀重复时，认证在提交假密码前停止。证据：`src/CampusPulse.Core/DrComProtocol.cs`、`tests/CampusPulse.Tests/Program.cs` 及本次命令输出。测试网络由进程内假响应处理器接管；没有运行正式后台、安装程序或真实校园认证。此改动仍在本地，未宣称已推送。
- **当前门户模板只读结构核对：通过，未认证**。读取此前确认的同一 Dr.COM `pc.js` 地址，HTTP 200，响应 7094 字符；其中有 1 个 `ISP_select` 起始标签、1 个 `</select>` 和 5 个 `<option>`。此次只计算结构，没有提交账号密码，没有执行程序登录；模板未来变化仍需在运行时核对。
- **格式检查：通过**。`git -c safe.directory=E:/Projects/01_CampusPulse diff --check` 退出码 0；仅提示 Windows 工作区换行符可能在后续 Git 操作中转换。此项不等于功能验收。

## 2026-09-27：正式后台验收前的只读检查（`40d8129` 基础上的本地改动）

- **服务状态：未安装**。`sc.exe query CampusPulse` 返回 1060；`C:\ProgramData\CampusPulse` 不存在。开发构建的 Service/App EXE 文件存在，但文件存在不能证明可运行或与系统集成。没有注册、启动或删除服务。
- **运行环境与权限**：系统 `dotnet --list-runtimes` 仅列出 .NET 6 和 8；仓库内已有 .NET 10 SDK。当前命令令牌为 Medium Mandatory Level，Administrators 组为 deny only。直接使用开发构建 EXE 进行正式服务测试之前，需要处理 .NET 10 运行时与管理员权限。此次仅只读核对，未执行正式界面联动、三个开关、真实认证或电源请求测试。
- **临时后台运行副本：文件生成通过，未运行**。在此前不存在的已忽略目录 `.local/service-validation-20260927/Service/` 中，以本地 SDK 执行 `dotnet publish` 的 `--self-contained true --no-restore` 方式成功生成后台副本，退出码 0；`CampusPulse.Service.exe`、DLL、`coreclr.dll`、`hostfxr.dll` 均存在。首次不带 `--no-restore` 的发布停在依赖还原，已人工中止，退出码 1；没有运行安装器、注册服务或认证。
- **临时界面副本：未生成**。界面 `publish --no-restore` 因缺少 `net10.0-windows/win-x64` 资产返回 NETSDK1047；仅用本地依赖的 `restore -r win-x64` 因缺少 `Microsoft.WindowsDesktop.App.Runtime.win-x64 10.0.7` 返回 NU1100。未安装运行时，也未将失败目录当成可用界面。随后重新运行 `powershell -NoProfile -File scripts/check-dev.ps1`，退出码 0，四项目 Release 构建与 17/17 项离线检查仍通过；正式界面可在后续通过仓库内 .NET 10 SDK 启动已构建 DLL，但目前未执行正式联动。

## 2026-09-28：临时服务验收入口准备（`40d8129` 基础上的本地改动）

- **脚本语法与只读状态：通过**。新增 `scripts/dev-service-validation.ps1`，固定 `CampusPulse` 服务名和专用受保护程序目录。首次语法检查因 Windows PowerShell 5.1 对无 BOM UTF-8 的读取及缺少 `if` 语句块而失败；修正后 PowerShell 解析无错误。`-Action Status` 退出码 0，报告服务未注册、临时后台副本和正式界面 DLL 存在。证据：脚本及本次命令输出。
- **权限拒绝：通过**。以当前非管理员令牌调用 `-Action Install`，在修改系统前因缺少管理员权限退出码 1；之后复查服务仍未注册，`C:\Program Files\CampusPulse-Validation` 与 `C:\ProgramData\CampusPulse` 均不存在。此项证明拒绝路径，没有证明管理员安装、服务运行、三个开关或界面联动。尚未提交真实凭据、生成安装包或上传本轮代码。

## 2026-09-28：用户完成临时后台交互检查（`40d8129` 基础上的本地改动）

- **用户操作报告**：用户确认已在正式窗口检查三个开关、后台停止与重新启动，并运行 `Remove` 清理，报告这一步通过。此次没有收到逐项截图、系统启动类型或防睡眠请求的测试输出，因此记录为用户操作报告，不把 BOOT 重启前登录、PWR 长时间保持唤醒、自动重连实际认证或权限隔离升级为独立通过。
- **清理复核：通过**。随后只读运行 `scripts/dev-service-validation.ps1 -Action Status`、查询 `Win32_Service` 与程序/数据目录，均显示 `CampusPulse` 服务、`C:\Program Files\CampusPulse-Validation` 和 `C:\ProgramData\CampusPulse` 不存在。`powercfg /requests` 在当前非管理员令牌下返回需要提升权限，故防睡眠请求的系统级复核未完成。没有运行真实认证、跨夜或产品安装器。

## 2026-09-28：当前状态复核（工作区基于 `40d8129`，含未提交改动）

- **开发检查：通过**。运行 `powershell -NoProfile -File scripts/check-dev.ps1`，退出码 0；Core、Service、App、Tests 四项目 Release 构建均为 0 警告、0 错误，17/17 项进程内离线检查通过。边界：未发布程序、未运行安装器、未注册服务、未提交真实校园认证。
- **本机服务状态：未注册**。运行 `powershell -NoProfile -File scripts/dev-service-validation.ps1 -Action Status`，退出码 0；报告 `CampusPulse` 服务未注册，临时后台副本与正式界面 DLL 均存在。未进行本次原生界面联动或防睡眠系统检查。

## 2026-09-28：追加模拟验收与用户现场反馈（工作区基于 `40d8129`，含未提交改动）

- **NET-05/06/09/11 离线检查：通过**。新增门户模板跳转拒绝提交凭据、HTTP 429 重试时间上限、并发重连序列化，以及认证拒绝状态在设置文件重新加载后保留的模拟用例。运行 `powershell -NoProfile -File scripts/check-dev.ps1`，退出码 0；四项目 Release 构建均为 0 警告、0 错误，21/21 项进程内离线检查通过。证据：`tests/CampusPulse.Tests/Program.cs` 与本次命令输出。边界：设置回读测试不等于完整服务重启测试；没有覆盖所有门户错误、实际 HTTP 超时、系统权限或真实校园认证。
- **无凭据现场操作：用户报告已完成**。用户本轮表示已完成上一轮约定的开关与后台操作；本次只读 `-Action Status` 显示 `CampusPulse` 服务未注册。未收到启动类型、`powercfg /requests` 或重启前登录的逐项输出，因此这些系统效果仍按用户反馈记录，未升级为独立验收通过。

## 2026-09-28：服务重启保护与超时补测（工作区基于 `40d8129`，含未提交改动）

- **NET-06/NET-11：通过**。进程内模拟认证拒绝后重新创建并启动 `ConnectionWorker`，新实例回读阻断状态且没有再提交登录请求；另一模拟端点保持认证响应不返回，客户端在 HTTP 超时后报告 `authentication_timeout`，没有报告认证成功或重复提交。运行 `powershell -NoProfile -File scripts/check-dev.ps1`，退出码 0，四项目 Release 构建 0 警告、0 错误，23/23 项离线检查通过。证据：`tests/CampusPulse.Tests/Program.cs` 与命令输出。边界：这是进程内后台重启模拟，不是 Windows 服务管理器的真实重启；真实网络路径与校园认证未验证。
- **NET-05 修复**：后台在认证未被接受时，将响应中的 `RetryAfter` 交给有界退避计算，不再丢弃服务端给出的等待时间。通过上述构建与模拟检查；实际校园服务的限流响应仍未取得。
- **临时后台副本刷新：通过**。确认本机没有注册 `CampusPulse` 服务，检查目标目录无链接和敏感文件后，用仓库内 SDK 对 Service 执行 `dotnet publish -c Release -r win-x64 --self-contained true --no-restore` 到已忽略的 `.local/service-validation-20260927/Service/`，退出码 0，`CampusPulse.Service.exe` 存在。未注册或运行服务；此副本不属于最终安装包。

## 2026-09-28：校园有线网络路径排障与只读复核（`0.1.0-preview.1`，基于 `40d8129` 的本地改动）

- **用户现场日志：旧后台未确认有线路径**。正式窗口多次显示“未能确认校园有线路径，请检查网线、地址及 VPN 路由”，时间覆盖 10:52 至 10:54。日志证明旧后台停在路径判断，不能证明发生过认证请求或密码提交。用户随后在正式窗口关闭并保存“自动重连校园网”；当前令牌无法读取受保护的 `C:\ProgramData\CampusPulse\settings.json`，因此本次仅将暂停状态记为用户确认，未作独立文件核验。
- **SYS-02 路由诊断：找到原因**。只读网卡与路由检查显示物理以太网有有效校园地址及到门户的接口路径，而 Mihomo TUN 占用了全局最优路由。使用指定物理接口和源地址检查到门户的路由，并以该接口绑定的 TCP 连接测试门户端口成功；未发 HTTP 认证或密码。修复 `WindowsNetworkPathResolver` 使用指定接口的 `GetBestRoute2`，忽略损坏虚拟网卡的单独枚举错误。证据：`src/CampusPulse.Core/WindowsNetworkPathResolver.cs`、忽略目录 `.local/route-diagnostic/` 与只读命令输出；本机结果不证明其他多网卡环境。
- **NET-10 DNS 诊断与修复**。系统 DNS 曾把一个固定公网探测域名解析到 `198.18.0.0/15` 假地址；通过指定以太网接口向该网卡配置的 DNS 服务器发送只读 DNS 查询，取得公网地址。新增 `BoundDnsResolver`，只解析两个固定探测域名，核对 DNS 问题与 CNAME 链并拒绝假地址；连接仍绑定物理以太网。证据：`src/CampusPulse.Core/BoundDnsResolver.cs`、`tests/CampusPulse.Tests/Program.cs` 和忽略目录中的诊断代码；未提交密码。
- **首次重建失败，之后通过**。修改路由代码后，首次 `scripts/check-dev.ps1` 因正式设置窗口占用 `CampusPulse.Core.dll` 报 `MSB3027`/`MSB3021`；用户关闭窗口后重试通过。修正分析器警告后再次运行 `powershell -NoProfile -File scripts/check-dev.ps1`，退出码 0，Core、Service、App、Tests 四项目 Release 构建均为 0 警告、0 错误，24/24 项进程内离线检查通过。新增 NET-10 的 DNS CNAME/假地址用例。此前一次 23/23 通过及失败均保留在本记录，不用最终成功覆盖失败事实。
- **当前网络只读探测：通过，未认证**。复核 `.local/route-diagnostic/Program.cs` 只调用 `CampusNetworkClient.CheckAsync` 后执行 `.tools/dotnet/dotnet.exe run --project .local/route-diagnostic -c Release -- --read-only-check`，退出码 0，确认物理以太网接口，结果为 `internet_verified`、`InternetAvailable=True`、`NeedsAuthentication=False`。门户未因无需认证而读取；此结果仅证明测试时两项固定探测通过，不能记为本软件真实登录成功、跨夜恢复或旧后台已修复。
- **临时后台副本：刷新发布成功，系统服务未安装**。执行 `.tools/dotnet/dotnet.exe publish src/CampusPulse.Service/CampusPulse.Service.csproj -c Release -r win-x64 --self-contained true --no-restore -o .local/service-validation-20260927/Service`，退出码 0；关键 Service/Core 运行文件存在，敏感文件检查数量为 0。用户确认已移除先前临时后台；`sc.exe query CampusPulse` 返回 1060，`C:\Program Files\CampusPulse-Validation` 不存在，`-Action Status` 报服务未注册。受保护的数据文件未读取、未删除。新增 `-Action Install -PreserveData` 入口，在管理员执行时先检查已有数据目录及 `Enabled=false`；当前只读语法/状态检查通过，管理员重新注册路径尚未执行。首次尝试读取设置因当前非管理员令牌被拒绝，没有将其误记为数据不存在。
- **命令环境失败与恢复**。本轮部分普通沙盒命令及 Node 入口曾因 `helper_unknown_error: setup refresh had errors` 无法启动；改用经自动审核允许的只读/构建命令后继续检查。此故障不作为项目代码失败，也不把未运行的临时服务更新记为通过。没有运行安装器、真实认证、跨夜测试或 GitHub 推送。
- **临时脚本复核与拒绝路径**。首次额外语法检查使用嵌套 PowerShell 命令时，外层提前展开 `$` 变量而退出码 1；改为直接在当前 PowerShell 会话调用解析器后返回 `PowerShell parser: OK`、退出码 0。以当前非管理员令牌调用 `-Action Install -PreserveData`，退出码 1，明确在系统修改前拒绝。`-Action Status` 退出码 0。旧 `Install` 输出仍写“未填写账号密码”，但本次保留了已有凭据；已修正为按 `-PreserveData` 分支准确提示，早期输出不作为无凭据证据。
- **新版临时服务重新注册：通过**。用户确认旧临时后台是自己移除，且已在正式窗口关闭并保存自动重连。通过 Windows UAC 以管理员令牌执行 `scripts/dev-service-validation.ps1 -Action Install -PreserveData`，退出码 0；脚本先核对无同名服务及临时程序目录、已有数据只含支持文件、版本 2 且 `Enabled=false`，随后保留数据安装并启动服务。`-Action Status` 和 `sc.exe query CampusPulse` 回读 Running、Manual；`CampusPulse.Service.exe`、Service/Core DLL 与 `.local/service-validation-20260927/Service/` 中当前发布副本的 SHA-256 分别一致。证据：脚本、忽略目录 `.local/admin-install-result.txt`、命令输出。此操作不是产品安装器验收，没有读取或输出账号密码。
- **正式后台只读联网检查：通过，未登录**。通过管理员权限连接受限本机管道，先发送 `status`，回读 `Enabled=false`、`HasPassword=true`、初始 Paused、实际自启关闭；随后只发送一次 `check`，再次回读 `Enabled=false`、`State=Online`、无错误码且 `LastCheck`/`LastSuccess` 已更新。证据：忽略目录 `.local/admin-service-readonly.ps1`、`.local/admin-service-readonly-result.txt` 与服务状态命令输出。该脚本未发送 `reconnect`、账号或密码；检测时网络已在线，不能记为本软件真实认证、断网恢复或跨夜成功。正式 WPF 窗口显示状态仍待核对。
- **正式窗口显示与关闭：通过本轮人工核对**。管理员执行 `scripts/dev-service-validation.ps1 -Action Open` 后输出“已打开正式界面”；进程查询显示主窗口标题为“CampusPulse · 校园网自动连接”、`Responding=True`，没有演示字样。用户确认窗口显示在线且自动重连开关关闭，与后台 `State=Online`、`Enabled=false` 一致。窗口关闭动作按产品设计先收起到托盘；结束本次由代理启动的管理员设置进程后，复核进程已退出，后台服务仍为 Running/Manual。证据：用户反馈、忽略目录 `.local/admin-open-result.txt`、`.local/admin-close-result.txt` 及进程/服务查询输出。此项证明本轮状态显示，不代表真实认证或完整 UI 功能已验收。
- **源码里程碑提交、推送与远端回读：通过**。验收后再次运行 `powershell -NoProfile -File scripts/check-dev.ps1`，退出码 0，四项目 Release 构建 0 警告、0 错误，24/24 离线检查通过。按 13 个源码/文档/测试文件白名单暂存；`git diff --cached --check` 通过，明显令牌和私钥签名扫描无命中，暂存路径不含 `.local/`、`.tools/`、`artifacts/` 或构建输出。提交 `b2fb7018b51d483e8816e7ea257bffa8f912d90b` 后推送 `origin main` 退出码 0；`git ls-remote origin refs/heads/main` 返回相同哈希。结果仅为公开源码发布；未上传本机数据、安装包或 Release，未完成真实登录及跨夜验收。
- **SEC-01 部分权限检查：通过**。在当前未提升的用户令牌中只尝试连接 `CampusPulse.Control.v1` 命名管道，不发送任何命令或凭据；连接立即抛出 `UnauthorizedAccessException`，退出码 0，符合仅管理员/SYSTEM 可访问的预期。此前提升后同一管道可正常回读状态并触发只读检查。此项尚未以另一个普通本地用户验证，也未逐项核对数据文件 ACL，因此 SEC-01 完整验收仍待执行。
- **CFG-05/PWR-02 短时原生电源请求：通过**。已插电时，管理员先回读后台 `Enabled=false`、`UnattendedMode=false`、`KeepingAwake=false`，`powercfg /requests` 无 CampusPulse 项。仅将无人值守开关暂时保存为开启，不改自动重连；约数秒后后台 `KeepingAwake=true` 且系统出现 CampusPulse 电源请求。随后用原配置恢复关闭，后台 `Enabled=false`、`UnattendedMode=false`、`KeepingAwake=false`，系统请求消失；脚本退出码 0，服务持续运行。证据：忽略目录 `.local/admin-power-toggle-validation.ps1`、`.local/admin-power-toggle-result.txt` 和只读状态输出。未等待超过空闲睡眠时间，未拔电、锁屏、停止或崩溃服务，故 PWR-01/02/03 不能整体标为通过。
- **开机类型差异与修正**。重新注册脚本最初一律创建 Manual 服务，而保留的数据中 `StartWithWindows=true`；后续保存无人值守配置时，后台按保存值把服务改为延迟自动启动。复核 `sc.exe qc CampusPulse` 为 `AUTO_START (DELAYED)`，注册表 `DelayedAutoStart=1`，管道回读 `StartWithWindows=true`、`ActualStartWithWindows=true`，自动重连仍 `Enabled=false`。已修正 `scripts/dev-service-validation.ps1 -Action Install -PreserveData`：重装前验证开机选项为布尔值，注册时按其设定 Manual 或延迟自动启动。修订后 PowerShell 解析、`-Action Status`、仓库结构检查均退出码 0；`scripts/check-dev.ps1` 再次完成四项目 0 警告/错误构建与 24/24 离线检查。修订脚本的管理员重装分支尚未再次执行；当前系统已校正，不能把这次状态变化记为 BOOT-02/03 完整验收。
- **次日真实认证前预检**。再次以管理员权限仅发送 `status`，回读 `Enabled=false`、`HasPassword=true`、状态 Paused、`StartWithWindows=true`、`ActualStartWithWindows=true`、`UnattendedMode=false`、`KeepingAwake=false`；`powercfg /requests` 没有 CampusPulse 项。`scripts/dev-service-validation.ps1 -Action Status` 回读服务 Running/Auto。没有读取账号密码，也没有发送 `check`、`reconnect` 或认证请求。脚本与电源验收文档提交 `e8faf4e76cc6b5cca0f1dcc5ac6aa145a36c986c` 已推送，`git ls-remote origin refs/heads/main` 返回同一哈希。明早仍须重新读取现场状态，当前预检不能替代真实认证结果。

## 2026-09-29：新门户入口与 gzip 脚本排障（`0.1.0-preview.1`，源码提交 `93c2fe6`）

- **用户现场失败：未认证**。08:38 至 08:43 正式窗口重复显示“门户内容或认证配置不受支持，未继续提交凭据”，没有成功重连记录。用户随后关闭并保存自动重连；一度拔出网线，诊断确认当时无物理有线路径，重新接回后继续。该日志不能证明曾提交密码。
- **只读门户定位**。绑定当前物理以太网接口执行 `.local/route-diagnostic/` 的只读检查：旧 `10.62.164.14` 首页与状态路径均返回 451 字节跳转页，缺少 Dr.COM 标识，指向 `10.62.164.38/a79.htm`；仅记录目标主机、静态路径及参数名，没有记录动态参数值。用户浏览器截图也显示新地址的登录页及中国电信选项。以 `http://10.62.164.38/` 为首页时，配置和选中运营商模板校验通过，状态为 `authentication_required`，公网两项探测未通过；没有发起认证。
- **gzip 原因与修复**。新主机 `/a40.js` 返回 HTTP 200、`Content-Encoding: gzip`；原客户端把压缩字节当文本，版本解析报 `portal_parameter_missing`。新增单层 gzip 解码，继续按解压后字节数执行 512 KiB 限制，并将损坏压缩数据归类为不受支持。修复后只读解析到脚本版本 `4.2.1`。离线新增有效 gzip 和解压超限阻断两个用例；没有用真实账号或假密码访问真实接口。原始脚本与诊断程序仅存于忽略目录 `.local/`。
- **构建与离线检查**。首次 `powershell -NoProfile -File scripts/check-dev.ps1` 因正式窗口进程占用 App 的 Core DLL 报 `MSB3027`/`MSB3021`，退出码 1；用户关闭设置窗口后重跑，四项目 Release 构建均为 0 警告、0 错误，26/26 离线检查通过，退出码 0。独立的只读新门户检查确认脚本版本可解析。构建和只读检查均不等于真实登录。
- **临时后台刷新**。刷新脚本新增版本 2、`Enabled=false` 的管理员前置核对。运行 `.tools/dotnet/dotnet.exe publish src/CampusPulse.Service/CampusPulse.Service.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false --output .local/service-validation-20260927/Service`，退出码 0，源目录敏感文件数量 0。管理员执行 `scripts/dev-service-validation.ps1 -Action Refresh` 成功并保留现有数据和启动类型；`-Action Status` 回读服务 Running/Auto，安装位置 Core DLL 与新副本 SHA-256 相同。未运行安装器，未认证或执行跨夜恢复。
- **手动认证前置项**。用户当时需在正式窗口把已保存的旧门户改为 `http://10.62.164.38/` 并在本机重输密码；只读检查明确要求认证后，才人工点一次“立即重连”并以独立公网探测验收。网页显示登录表单和代码识别门户均不是登录成功证据；下条记录为完成后的验收结果。
- **真实账号单次手动认证：通过**。用户在正式 WPF 窗口将门户改为 `http://10.62.164.38/`，在本机重新输入密码，保持自动重连关闭并保存；“立即检测”后后台于 09:22:55 显示“检测到需要认证；本次仅检测，没有提交密码”。用户随后点击一次“立即重连”；管理员只读 `status` 回读 09:23:45“正在进行一次校园账号认证”，09:23:54“校园有线网络已通过两个公网探测”。另用 `.local/route-diagnostic/` 对同一物理以太网执行只读检查，结果 `internet_verified`、`InternetAvailable=True`。后台回读 `Enabled=false`、`HasPassword=true`、新门户地址及上次公网成功时间。证据：用户操作确认、受限管道脱敏事件和独立有线检查输出；未读取、输出或保存真实密码，也没有第二次认证请求。此项证明一次人工触发的真实认证和公网恢复，不能证明自动恢复、跨夜或安装包可用。
- **网卡事件低频保护：通过离线检查**。发现旧 `ConnectionWorker` 在任何网络变化事件中取消当前检查，并可能在下一次计划时间前重复检测。修正为不因事件取消在途检查，普通网络事件须等待已排定的下次检测时间；用户主动点“立即检测/重连”仍可即时执行。新增模拟事件突发检查，连续五次网络变化通知后没有提前发起网络请求。单独运行测试项目 27/27 通过。该模拟检查不能代替跨夜现场观察。
- **构建失败后复查通过**。网卡事件修正后首次 `powershell -NoProfile -File scripts/check-dev.ps1` 因设置窗口进程占用 App 的 Core DLL 报 `MSB3027`/`MSB3021`，退出码 1；只结束核对为 CampusPulse 设置窗口的进程，后台服务未停止。随后重跑该命令，Core、Service、App、Tests 的 Release 构建均为 0 警告、0 错误，27/27 离线检查通过，退出码 0。
- **再次刷新临时后台**。以当前源码重新发布自包含服务到忽略目录，管理员 `-Action Refresh` 成功；程序目录中的 Service/Core DLL SHA-256 均与新副本相同，`-Action Status` 回读 Running/Auto。刷新前脚本核对版本 2 且自动重连关闭；刷新后受限管道回读 `Enabled=false`、`HasPassword=true`、`PortalUrl=http://10.62.164.38/`，上午的 `LastSuccess` 仍在。此次刷新未发送认证，也未生成最终安装包。下一项是开启自动重连和插电无人值守后观察自然跨夜恢复；目前没有该项通过证据。
- **公开源码同步：通过**。只暂存 12 个源码、脚本和文档路径，`git diff --cached --check` 退出码 0，未包含 `.local/` 诊断文件、`.tools/`、`artifacts/` 或本机数据。提交 `93c2fe6d959522ddcbcc3d41829175db57ac872d` 并推送公开 `origin main`，`git ls-remote origin refs/heads/main` 回读同一哈希。此项仅证明源码和验收记录已同步；没有发布安装包或 GitHub Release。

## 2026-09-30：一晚跨夜自动恢复的用户现场确认（`0.1.0-preview.1`，上次已回读源码提交 `c2802fb`）

- **用户操作与反馈：按本次范围接受**。用户明确表示“自动重连和跨夜恢复验证成功”，并说明只有这一晚在学校，要求按这一晚完成跨夜验收。场景为 2026-09-29 至 09-30 的自然跨夜运行；用户未提供具体断网、开始认证或公网恢复时刻。证据位置：本次对话的用户现场反馈；本轮没有读取后台脱敏事件、独立有线公网探测或系统电源请求，因此这些细项标为未验证。不能将本条写成两晚、无人值守电源请求持续整夜或独立检查通过。
- **验收门槛调整**。用户决定首个预览版接受这一晚作为校园实测样本，不再安排第二晚或受控认证失效测试；公开说明须写明仅 1 晚、用户现场确认及缺少事件回读。此决定不替代开机自启、三开关、凭据权限、服务生命周期、安装/升级/卸载等本机验收，也不证明多夜可靠性。
- **本轮文档维护与只读服务检查**。同步 `AGENTS.md` 当前门户、`README.md` 状态摘要、`docs/HANDOFF.md` 下一阶段与证据边界、`docs/TESTING-AND-RELEASE.md` 首个预览版跨夜标准及本记录。普通命令环境多次返回 `helper_unknown_error: setup refresh had errors`；改用经审核的只读命令回读项目文件与 Git 状态，未把工具故障写为产品故障。运行 `powershell -NoProfile -File scripts/dev-service-validation.ps1 -Action Status`，退出码 0，回读临时服务 Running/Auto、临时程序副本和正式界面 DLL 存在；这不证明跨夜认证时间线或实时开关状态。文档差异检查 `git diff --check` 退出码 0。未运行新构建、离线测试、真实认证或安装器；本轮 GitHub 同步以最终远端回读为准。
- **文档提交与首次远端回读：通过**。提升后的 Git 首次因仓库所有者是沙盒身份而拒绝 `status`；后续仅对本命令使用 `-c safe.directory=E:/Projects/01_CampusPulse`，未修改全局 Git 信任设置。暂存范围只有 `AGENTS.md`、`README.md`、`docs/HANDOFF.md`、`docs/TESTING-AND-RELEASE.md`、`docs/VALIDATION-LOG.md`，`git diff --cached --check` 退出码 0；人工检查差异未见账号、密码或认证请求。提交 `fdeac540890f870de809417d0dc6d9cb9adde4d7` 后 `git push origin main` 退出码 0，`git ls-remote origin refs/heads/main` 回读同一提交，GitHub 文件接口可读取更新后的交接页。本次仅公开文档，没有发布安装包或 Release。

## 2026-09-30：打包前本机验收及保存失败修复（`0.1.0-preview.1`，基线 `8f52264`）

- **接手与环境**：读取 AGENTS/HANDOFF/VALIDATION-LOG、相关设计与验收章节；Git 工作区初始干净，`main` 与 `git ls-remote origin refs/heads/main` 均为 `8f52264e7d1cacf5fb578c99754fefa891d4233c`。普通命令环境再次报 `helper_unknown_error: setup refresh had errors`，审核允许的命令继续执行。提升后的 Git 首次报仓库身份不同；后续仅本命令加 `-c safe.directory=E:/Projects/01_CampusPulse`，未修改全局信任设置。
- **只读系统/界面/权限**：Windows 11 x64 Build 26200；`inspect-local.ps1 -Elevate` 回读 LocalSystem、Running、延迟 Auto、三个开关 true、HasPassword=true、防睡眠请求存在。`inspect-ui.ps1 -Elevate` 通过 UI Automation 白名单核对正式 WPF 公网/后台/自启/防睡眠及三个开关一致、密码控件遮蔽；未读取账号密码值。目录 ACL 仅 SYSTEM/Administrators，文件继承受保护父目录；程序目录 Users 仅 RX。当前未提升令牌打开三个数据文件及连接管道均被拒绝。另一普通本地用户及完整 UI/托盘/缩放未执行。证据：忽略目录 `.local/acceptance-20260930/inspection-initial.json`、`ui-inspection.json`、`unelevated-access.json`。首次新只读脚本因 Windows PowerShell 将无 BOM 中文错误解码而解析失败，改为 ASCII 提示后运行通过；不作为产品故障。
- **近期后台事件回读**：取得最近 80 条，最早为北京时间 10:32:57，11:39:49 后记录两项公网探测成功；夜间记录已轮转，未取得那一晚的完整认证时间线。用户已接受的一晚跨夜结论维持现场反馈等级，不补成多夜或完整后台事件验证。
- **CFG/BOOT/PWR 部分原生验收**：用户明确允许临时切换三个开关并重启后台一次。`validate-local.ps1 -Execute -Elevate` 退出码 0，8 个断言通过：暂停不关闭防睡眠、关闭自启为 Manual 且当前服务 Running、重新开启为延迟 Auto、关闭无人值守释放请求、重连暂停时防睡眠仍可生效、停止服务释放请求、暂停状态重启后配置/凭据存在性保持、凭据密文字节未变；最后完整恢复三个开关 true，Restored=true。证据 `switch-validation.json`。不证明重启电脑前登录、自启关闭后的下次开机或服务崩溃恢复。
- **CFG-04/SEC-02/03 新假数据检查与修复**：新增密码更换/清除、持久化无假密码明文及配置文件占用时的回滚检查。首次 28/29：Windows 文件占用抛 UnauthorizedAccessException，补全测试捕获类型后仍为 28/29，明确断言旧凭据未恢复。原因是配置失败后的第一次 Restore 再次被锁定文件阻断，凭据恢复没有执行。SecureStore 回滚跳过未变文件，并在 finally 中确保尝试凭据恢复。之后 29/29，退出码 0。全部是假数据和隔离临时目录，没有提交真实认证。证据 `offline-before-fix*.txt`、`offline-after-fix.txt` 和对应源码/测试。
- **完整开发检查**：首次 `scripts/check-dev.ps1` 被设置进程 62576 占用 App 的 Core DLL，MSB3027/MSB3021，退出码 1。管理员核对该 PID 的 dotnet 路径及 App DLL 命令行后仅结束设置进程，后台持续 Running；再执行 `powershell -NoProfile -File scripts/check-dev.ps1`，四项目 Release 构建均 0 警告、0 错误，29/29 离线通过，退出码 0。随后重新打开正式界面。证据 `check-dev.txt`、`ui-close.txt`、`check-dev-after-close.txt`、`ui-open.txt`；未打包或更新运行服务副本。
- **SYS-06 有限恢复策略修正，未崩溃实测**：只读发现临时服务尚无恢复动作；原安装器三项重启以重启结尾，SCM 会重复末项。独立、不启动的 `CampusPulse-RecoveryCheck` 服务验证参数：初试 `none/0` 未取得有效证据；改为 `//0` 后配置成功，但 qfailure 本地化输出省略 NONE，文本断言失败。随后用系统 QueryServiceConfig2 回读数组，确认 `86400,1,5000,1,15000,1,60000,0,0`，检查脚本退出码 0，测试服务已删除。修改安装器及临时 Install 的配置为三个重启后 NONE，并开启 failureflag；尚未重新编译安装器、未应用到现有临时后台、未制造真实后台崩溃。证据 `recovery-command.txt`、`recovery-check.txt`、`recovery-actions.json`。
- **PWR-03 拔电切换：通过一次**：用户确认拔掉充电器约 10 秒再接回，未拔网线。只读监测 19 条样本，12:52:54 进入电池，12:52:56 防睡眠状态及系统请求释放；12:53:09 插电后两者恢复，所有样本后台 Running，前后 `powercfg /query` 一致。证据 `power-result.json`、`power-samples.jsonl`；不证明长期值守或主动关机可被阻止。
- **SYS-04 锁屏部分：通过一次**：用户确认 Win+L 锁屏约 20 秒后解锁；12:56:26 至 12:57:27 的 30 样本后台全部 Running，电源计划不变。该窗口内 LastCheck 未变化，不能声称期间发生了一次自动网络请求。证据用户反馈及 `lock-result.json`；屏幕自动关闭、托盘完整交互未执行。
- **SYS-05 S0 现代待机恢复：通过一次**：`powercfg /a` 确认本机仅支持 S0 网络连接待机，S3 不可用、休眠未启用，未改电源配置。用户关闭并保存无人值守后只读确认无防睡眠请求，随后主动睡眠并唤醒。系统 Kernel-Power 506/507 为 13:03:12/13:03:50；服务保持可用，后续只读回读 LastCheck 13:05:27、LastSuccess 13:05:28、State=Online，证明唤醒后自动检测且公网通过；未发送人工 check/reconnect。前后电源计划一致。首次监测在恢复事件后结束，自动检测证明由后续独立状态回读补齐；新脚本已改为同时等待自动检查。13:08:09 恢复无人值守，三个开关 true、KeepingAwake=true、凭据密文不变。证据 `sleep-result.json`、`sleep-network-proof.json`、`restored-after-sleep.json`。
- **重启前基线：已保存，重启结果未执行**：`inspect-boot.ps1 -Phase Before -Elevate` 退出码 0；13:08:12 记录上次系统启动 09-28 21:54:34、服务 Running/Auto、服务进程启动 09-30 12:37:01。用户已表示方便配合现场检查；下一步用户自行重启并在登录界面等待约 3 分钟，回来后回读 After 和后台状态。证据 `boot-before.json`；没有代理重启电脑、创建开机任务或修改系统登录。
- **当前验证边界**：新 SecureStore 仅本地源码及假数据验证，当前运行仍为 09-29 临时副本；新恢复策略尚未在产品安装器或真实崩溃中验证。本轮未新增手动真实认证、跨夜、安装/升级/卸载或 Release。源码提交/上传待最终回读记录，不将远端旧基线写成新修改已发布。更多剩余门槛见 `LOCAL-ACCEPTANCE.md`。

- **源码提交、推送与回读：通过**。六个验收/临时脚本的 PowerShell 解析检查通过，临时服务 Status 回读 Running/Auto。仅白名单暂存 15 个源码、脚本及脱敏文档路径；`git diff --cached --check`、明显密钥/令牌签名扫描及 Git 历史禁止路径检查通过。提交 `c557257d5b5a74b1e85f23697387cd422c0c698d`，`git push origin main` 退出码 0，`git ls-remote origin refs/heads/main` 返回同一哈希，工作区当时干净。只发布源码，未上传 `.local/`、`.tools/`、`artifacts/`、本机数据或安装包。本条及重启时间核对脚本排除 DWM/UMFD 会话的补充随后同步，最终文档提交以远端回读为准。

## 2026-09-30 晚间：重启后自动启动回读（`0.1.0-preview.1`，源码基线 `85089aa`）

- **BOOT-01/03 自动启动部分：通过；登录前启动未证实**。用户回复“已重启”后，没有手动启动服务或设置窗口。执行 `inspect-boot.ps1 -Phase After -Elevate` 和 `inspect-local.ps1 -Elevate`，整体退出码 0。回读系统启动 19:29:49、当前账户最早交互会话 19:30:11、服务进程启动 19:32:20；新重启成立，后台 Running/延迟 Auto，StartedBeforeInteractiveLogon=false。三个开关 true、HasPassword=true、KeepingAwake=true、系统电源请求存在，State=Online、LastSuccess 为 19:42:24。已询问用户是否在登录界面等待或先进入桌面，尚未回复；不能从本条推断登录前认证或要求再做跨夜。证据 `.local/acceptance-20260930/boot-after.json`、`inspection-after-boot.json`。
- **工具过程与边界**。普通命令入口仍报 `helper_unknown_error: setup refresh had errors`，使用获准的只读命令继续。管理员检查等待 UAC/结果时，额外读 `sc.exe query/qc CampusPulse` 确认 Running、延迟 Auto、固定验证路径和 LocalSystem，没有发送启动或认证命令。部分 PowerShell 对象未在退出前完整渲染，因此以脚本写出的 JSON 和 sc 原生输出为证据，不把空输出记为无服务。
- **PWR-01 空闲监测：未执行**。只读 `powercfg /query SCHEME_CURRENT SUB_SLEEP STANDBYIDLE` 回读 AC 300 秒、DC 180 秒。新增 Idle 监测分支，核对真实最后输入时间、持续插电防睡眠请求、无待机事件及前后电源计划不变；PowerShell 语法检查通过。尝试派发时 UAC 被取消，命令退出码 1，未生成 ready 或样本；没有自动重试或修改电源计划/开关，不作为产品故障。
- **新版后台副本准备：通过，尚未部署**。执行 `.tools/dotnet/dotnet.exe publish src/CampusPulse.Service/CampusPulse.Service.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false --output .local/service-validation-20260927/Service`，退出码 0。证据 `service-publish-fix.txt`。只更新忽略目录内副本，当前系统仍运行 09-29 代码，未停止服务、提交密码或生成安装包。
- **故障验收脚本准备：未执行系统变更**。`validate-recovery.ps1` 通过 PowerShell 解析检查，默认入口退出码 0、仅显示计划：暂停认证、刷新固定临时后台、配置 5/15/60 秒重启后 NONE、一次结束已核对进程、验证系统重启及电源请求释放，再恢复设置并核对凭据密文。用户批准尚待回复，未执行 `-Execute`。此分支不能计为 SYS-06 或 PWR-02 崩溃释放通过。

## 后续记录格式

每条记录包含：日期、软件版本/提交标识（若尚未建立则注明）、测试编号、执行环境、操作或命令、预期结果、实际结果、通过/失败/未执行、脱敏证据位置及验证限制。

真实密码、原始抓包、会话、个人内网地址和完整认证请求不作为公开证据。自动化模拟测试、真实校园网络测试和 GitHub 发布检查分别记录。
