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

## 后续记录格式

每条记录包含：日期、软件版本/提交标识（若尚未建立则注明）、测试编号、执行环境、操作或命令、预期结果、实际结果、通过/失败/未执行、脱敏证据位置及验证限制。

真实密码、原始抓包、会话、个人内网地址和完整认证请求不作为公开证据。自动化模拟测试、真实校园网络测试和 GitHub 发布检查分别记录。
