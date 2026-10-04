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
- **晚间源码/记录同步：通过**。仅暂存 README、四份状态/设计文档及两个验收脚本共 7 个路径，差异检查和明显秘密签名扫描通过；提交 `24e7ea889250242bb5663dbe623e39d59e685152`，推送 `origin main` 退出码 0，远端回读一致，工作区干净。本轮只公开源码和脱敏状态，未上传本机证据、安装包或 Release；未执行待批准的故障测试。

## 2026-09-30 21:06：修复版部署及一次后台故障恢复（`0.1.0-preview.1`，源码基线 `14e530f`）

- **授权及范围**：用户在已说明的暂停认证、刷新临时后台、一次结束进程、恢复原设置的具体方案后指示“下一步”。执行 `powershell -NoProfile -File scripts/validate-recovery.ps1 -Execute -Elevate`，未关闭网络、重启电脑或发送人工 check/reconnect，没有读取明文密码。
- **部署/SYS-06/PWR-02：通过所执行部分**。21:05:59 至 21:06:20，命令退出码 0，10 个断言通过，Failure=null、Restored=true。暂停认证后刷新固定的 CampusPulse-Validation 副本，系统 API 回读 24 小时重置、5/15/60 秒重启后 NONE；核对服务进程路径后只结束一次，电源请求释放，约 5 秒后 SCM 自动以新 PID 拉起。重启后保留暂停及凭据存在性，重新持有电源请求；Service.exe、Service.dll、Core.dll 哈希均匹配新副本。最后原三个开关恢复，凭据密文字节未变。证据：忽略目录 `.local/acceptance-20260930/recovery-validation.json`、`refresh-fix.txt`。
- **边界**：当前临时运行后台已更新为本轮修复版；此前日间证据仍针对旧副本。不将一次进程终止写成连续四次崩溃测试，不宣称安装器恢复动作已执行。本轮未新增真实手动认证、跨夜或安装生命周期；最终安装包和 Release 尚未重建/发布。持续空闲防睡眠等剩余项目见 `LOCAL-ACCEPTANCE.md`。
- **窗口验收准备，原生结果待回读**：新增 `validate-ui-window.ps1`，PowerShell 解析检查通过。派发 `-Elevate` 后，截至 21:20 存在系统 consent 提示、无 `ui-window-validation.json`，不能记为通过或产品失败。脚本只操作自己启动的界面窗口，核对尺寸/最小化/关闭收起及后台连续运行，未授权其保存设置、停止后台或触发认证；真实托盘菜单及视觉裁切仍单独验收。已向用户询问是否重新开始此前取消的只读空闲监测，未获得答复前不开始。
- **公开源码同步：通过**。仅暂存六份状态/设计文档及窗口验收脚本共七个路径，差异检查、PowerShell 解析及明显秘密签名扫描通过。提交 `e3fbff47fa546c3650f4367c03aa3aa9288c184f`，推送 `origin main` 退出码 0，`ls-remote` 回读同一哈希，工作区当时干净；未上传本机证据、凭据或安装包。本条上传记录另行同步，最终状态以 Git HEAD/远端回读为准。

## 2026-09-30 21:34：窗口调用失败与持续空闲监测（`0.1.0-preview.1`，基线 `9dd0b53`）

- **窗口检查：部分执行，退出码 1**。用户确认此前 UAC 提示，21:34:13 打开脚本自己的正式窗口成功；随后调整尺寸前发生 `System.Management.Automation.MethodInvocationException`，尚无具体调用定位。清理仅结束脚本自己的界面进程，确认同一后台 PID 仍 Running。尺寸、最小化、关闭收起及托盘尚未通过；不将自动化调用错误直接标为产品缺陷。证据 `ui-window-validation.json`。检查脚本补充阶段、行号及根异常类型，均不读取账号密码值。
- **PWR-01：监测已开始，结果待回读**。用户明确表示“现在进行”，派发 `watch-local.ps1 -Scenario Idle -IdleSeconds 300 -DurationSeconds 540 -Elevate`。ready 为 21:35:03，首批样本确认插电、后台 Running、三个开关 true、防睡眠状态/请求有效。已提示保持充电器与网线连接、约 6 分钟不操作电脑；未修改电源计划，不触发人工认证。证据 `idle-ready.txt`、`idle-samples.jsonl`，完成后回读 `idle-result.json`，不能把派发退出码 0 当作验收通过。
- **PWR-01 回读：当前断言未通过，原因待分层核对**。21:41:37 结果 Failure=null、SampleCount=196、AllServiceRunning/PlanUnchanged/IdleReached/ContinuousPluggedInPowerRequest 均 true，最终连续空闲 331.235 秒；StandbyEntryCount=1。系统回读 21:41:06 Kernel-Power 506，原因 Idle Timeout，因此不计为通过。现代待机的 Screen Off 与实际 Sleep 是不同阶段，已查微软官方说明，准备 `inspect-sleep-report.ps1` 生成系统分阶段报告，尚未取得报告，不能先认定产品失效或改判通过。用户已被告知监测结束、可以操作电脑。窗口排查重新派发后仍等新 UAC 确认，后续结果待回读；未改电源计划或开关。

## 2026-09-30 22:01–22:13：系统报告核对与窗口原生验收（`0.1.0-preview.1`，基线 `3c4ae3d`）

- **PWR-01 分阶段补证：此次通过**。`inspect-sleep-report.ps1 -Elevate` 22:01:54 退出码 0，生成仅保存在忽略目录的 SleepStudy XML。仅提取与此次监测重叠的系统阶段：Screen Off 21:41:06 至 21:45:21，进入原因 Video Idle Timeout，覆盖最后空闲样本，没有重叠 Sleep。`interpret-idle-report.ps1` 22:07:46 退出码 0，结合 196 样本、连续空闲 331.235 秒、后台/插电/电源请求持续及电源计划未变，得到 Passed=true。保留原监测的严格事件计数结果，不覆盖为 0；系统报告补证说明 506 并不等同实际睡眠。证据 `idle-sleepstudy-result.json`、`idle-sleepstudy.xml`、`idle-phase-verdict.json`。仅证明此次空闲，不补为跨夜防睡眠或跨设备保证。依据微软 Modern Standby States；公开不上传机器信息或其他应用历史。
- **窗口调用定位及修正后原生验收：通过已执行部分**。22:01 补诊断回读失败位于 UIA Resize，根异常 InvalidOperationException；退出码 1，后台持续运行，保留 `ui-window-failed-resize.json`。测试脚本改用原生 SetWindowPos 并验证实际几何尺寸，未修改产品源码。22:08:30 至 22:08:38 `validate-ui-window.ps1 -Elevate` 退出码 0，8 个断言全部通过：正式窗口、最小尺寸、最小化、关闭收起、后台 PID 不变、恢复可见、配置/凭据密文不变、界面进程退出后后台继续运行。恢复由 Win32 完成，未把它写成托盘菜单通过。证据 `ui-window-validation.json`。
- **真实托盘/视觉准备：未完成交互结果**。`inspect-ui.ps1 -KeepOwnWindow -Elevate` 22:13 退出码 0，白名单状态为公网验证通过、后台运行、自启与防睡眠开启，密码控件遮蔽；保留脚本打开的正式窗口供用户操作，未读取账号/密码值。已保存本机文件摘要与进程基线、准备 `verify-ui-manual.ps1`，并提示最小尺寸视觉、关闭/双击托盘、右键打开、右键退出四步；用户结果尚待回复。证据 `ui-inspection.json`、`ui-manual-baseline.json`，不上传本机摘要。相关四个脚本解析检查通过；最终安装包和 Release 未执行。
- **真实托盘/视觉及退出后回读：本机此次通过**。用户随后确认四步全部正常且已退出界面。22:16:59 `verify-ui-manual.ps1 -Elevate` 退出码 0，TestedInterfaceExited、SameBackgroundRunning、SettingsBytesUnchanged、CredentialBytesUnchanged 均 true。结合用户交互确认，本机当前缩放下的最小尺寸可用、关闭收起、托盘双击/右键打开及退出界面后台继续均通过。证据用户反馈、`ui-manual-after.json`；其他 DPI/机器未验证，未把程序结束脚本冒充真实菜单操作。
- **SEC-01 另一普通账号检查：已准备，未执行**。`validate-other-user.ps1` 默认入口只显示具体操作计划，退出码 0，PowerShell 解析通过；仅提取并编译其中 C# 身份助手，在 Windows PowerShell 编译成功，没有调用探测或创建账号。执行分支限定新建 CampusPulseAclCheck 普通账号、随机临时密码仅存内存、三份固定文件及固定本机管道的打开/连接尝试、不读内容/不发控制命令，最后按 SID 核对删除该新账号并验证数据及后台未变。因会修改 Windows 用户列表，已向用户请求确认，尚未得到答复；同时询问干净 Windows 测试环境可用性。不能记录为权限验收通过。

## 2026-09-30 22:31–22:40：普通账号权限及两次开机准备（`0.1.0-preview.1`，基线 `236a250`）

- **公开源码回读**：上一阶段提交 `236a250c04196f01185799128e5bc0e18c90a77f` 推送 `origin main` 退出码 0，`ls-remote` 与本地 HEAD 一致，工作区当时干净。仅源码和脱敏记录；未上传系统报告、文件摘要或安装包。
- **SEC-01 首次执行失败，未创建账号**。用户明确授权临时普通账号并立即清理。第一次 `validate-other-user.ps1 -Execute -Elevate` 退出码 1，ParameterBindingValidationException，TemporaryUserCreated=false；配置/凭据及后台未变。系统命令元数据确认 Description 长度上限 48，原脚本描述超限。只读确认账号不存在，缩短描述并保留 `other-user-before-description-fix.json`，不是产品权限失败。
- **SEC-01 重跑：通过**。22:32:30 同一已授权范围执行，退出码 0，Failure=null；临时普通账号创建和删除均 true，身份及三份文件/管道拒绝五断言全 true。采用固定本地身份 network logon，无用户配置文件；仅尝试打开文件/连接本机管道，没有读取内容或发送命令。配置及加密凭据字节未变、同一后台 PID Running；额外只读确认测试账号已不存在。证据 `other-user-validation.json`，密码随机生成且未输出/持久化，未涉及真实校园密码。
- **环境边界**：用户明确目前没有干净 Windows 虚拟机或其他测试电脑，干净机器安装验证暂缺，不把本机开发环境写成干净机器。不据此宣称安装生命周期或 Release 完成。
- **BOOT 两次准备：仅第一步已执行**。用户明确愿意现在配合两次重启。新 `validate-boot-cycle.ps1` 解析及默认无变更计划通过。22:38:59 Disable 执行退出码 0，保存原三个开关、凭据摘要及重启前基线，仅关闭自启，服务 Running/Manual；未由代理重启。22:40:48 只读回读 StartWithWindows/ActualStartWithWindows=false、Enabled/UnattendedMode=true、HasPassword=true、防睡眠有效、State=Online。证据 `boot-cycle-disable.json`、`boot-disabled-before.json`、`inspection-before-disabled-boot.json`。当前需用户首次重启后先回读 Stopped/Manual，再从正式界面手动启动；原自启尚未恢复，第二次重启及登录前证据未执行。

## 2026-09-30 22:49–22:53：关闭自启的开机与手动使用（`0.1.0-preview.1`，基线 `00af0c1`）

- **BOOT-02：通过本次重启**。用户回复“第一次已重启”，代理未打开窗口或启动服务前执行 `validate-boot-cycle.ps1 -Phase AfterManualBoot -Elevate`，22:49 退出码 0，NewBootObserved=true、Stopped/Manual、保存自启 false、CredentialBytesUnchanged=true。系统启动 22:46:52，用户交互会话 22:47:12，后台无进程。证据 `boot-cycle-aftermanualboot.json`、`boot-disabled-after.json`；没有把服务配置推断为实际停止。
- **BOOT-05：通过本次用户操作**。仅运行 `inspect-ui.ps1 -KeepOwnWindow -Elevate` 打开正式窗口，22:50 回读后台已停止、启动按钮可用，没有自动启动。用户点击一次“启动后台服务”，确认后台运行、自启仍关闭，另外两个开关开启。22:52:59 AfterManualStart 退出码 0，Running/Manual、Enabled/UnattendedMode=true、CredentialBytesUnchanged=true。证据 `ui-before-manual-start.json`、用户反馈、`boot-cycle-aftermanualstart.json`；不由代理代点启动或发送认证。
- **原设置恢复及第二次基线：通过**。22:53:07 `validate-boot-cycle.ps1 -Phase Restore -Execute -Elevate` 退出码 0，原三个开关均 true，后台 Running/Auto、凭据密文未变。已保存 `boot-auto-before.json`；第二次重启和登录前运行仍待用户操作，没有代理重启。
- **新界面问题：已定位，尚未改码**。服务停止时窗口仍显示 XAML 初值“尚未保存密码”，尽管凭据文件存在且密文未变。ShowDisconnected 未重置 PasswordHint，初始提示也提前声称无密码。拟改为未连接时暂无法确认凭据状态，待第二次重启后修正并执行开发检查；不要求用户再次输入真实密码。此问题保留为打包前待修项。
- **源码同步基线**：上轮六个白名单路径提交 `00af0c1dd9a53306bf96d86b94d1c53e578febc1`，推送退出码 0，远端 main 回读一致，工作区当时干净。本轮只记录原生结果；安装包及 Release 未执行。

## 2026-09-30 23:20–23:29：第二次开机、断连提示及诊断安全（`0.1.0-preview.1`，基线 `d8d84e2`）

- **第二次用户重启及失败回读**：用户确认在登录界面等待约 3 分钟。最初 inspect-boot/inspect-local 的两项只读提权调用均因 UAC 被取消退出码 1，未取得新结果；不引用旧 boot-after.json 作为本次证据，也没有修改服务。
- **BOOT-03 自动启动及解锁前运行：通过本次范围**。23:20 合并只读回读退出码 0：新开机 22:58:25、账户会话 22:58:46、后台自动启动 23:01:00；Running/Auto、原三个开关 true、HasPassword=true、防睡眠请求有效。因 Windows 提前创建会话，StartedBeforeInteractiveLogon=false。只读当前账户审计进一步回读 4800 锁定 22:58:48、4801 解锁 23:07:21；增强 inspect-boot 解析通过，23:29 执行退出码 0，StartedWhileLockedBeforeUserUnlock=true。服务在用户解锁进入桌面前已运行，不宣称早于所有账户会话；首次安装默认行为仍待安装验收。证据 `.local/acceptance-20260930/boot-auto-after.json`、`boot-unlock-events.json`、`inspection-after-auto-boot.json` 和用户反馈。全程不打开产品窗口或手动启动后台。
- **CFG-02 断连提示修正：构建通过**。XAML 初值显示正在读取，ShowDisconnected 显示暂无法确认凭据状态；只有有效快照决定已保存/未保存。`check-dev.ps1` 四项目 Release 0 警告/错误、29/29 通过、退出码 0，证据 `check-dev-password-hint-fix.txt`。新断连文字尚未原生复核，没有清除凭据或要求重输密码。
- **SEC-02 拒绝响应及请求异常/诊断文本：离线通过**。新增进程内模拟检查：假密码保存后，拒绝正文回显密码及带密码 URL；另一场景 HttpRequestException 包含完整模拟请求 URL。每场景断言只有一次假认证，扫描配置/加密凭据/事件、status 回复及正式诊断复制共用文本生成函数的导出，未发现假密码标记或认证参数。未读取/改写用户剪贴板，未访问真实校园端点；不宣称所有异常组合或原生剪贴板操作已验证。`check-dev.ps1` 四项目 Release 0 警告/错误、30/30 离线检查通过、退出码 0，证据 `check-dev-diagnostic-security.txt`，临时模拟文件已清理。
- **当前边界**：本机现场验收按记录范围完成，用户没有干净 Windows 测试环境。下一项重建安装包并实测本机安装/升级/卸载，产品数据须保护和恢复。安装包、安装生命周期与 Release 此时仍未执行。

## 2026-09-30 23:38–23:44 / 2026-10-01 00:01：新候选安装包及生命周期准备（源码 `8cefa33` 后工作区变更）

- **源码同步**：11 个源码/脚本/脱敏文档白名单提交 `8cefa33999970ea9138560b66a4ecc9d01bfc03d`，差异检查及明显秘密签名检查通过；push 退出码 0，远端 main 回读同一哈希。未上传本机证据或安装包。
- **第一次新构建**：`scripts/build.ps1` 退出码 0，四项目 Release、30/30 离线检查、App/Service 自包含发布及 Inno 编译通过。安装包 SHA-256 `0228cccb524fdb068aa780b3268b2d59cdef707757bcac89bb6d6cedba3f4e78` 与清单一致。此包随后因安装器路径保护修正被替换，仅保留在忽略目录作历史证据，不作为最终交付。
- **安装范围修正及再次构建**：安装/升级和卸载都必须先将同名服务 ImagePath 与当前安装目录精确比对；不接管不同路径服务。修改 DEVELOPMENT/安装验收条款后运行 `scripts/build.ps1`，退出码 0，四项目构建、30/30 离线检查、自包含发布和 Inno 编译通过。新候选 SHA-256 `0a534bcc407213c3aa00a8d929a14b60c4f8ec4972c265e5315f61bd4cc26ae6`，清单一致，Authenticode=NotSigned。证据 `build-service-path-guard.txt`、`artifacts/build-manifest.json`、构建日志；未宣称安装通过。
- **生命周期准备**：`validate-installation.ps1` 解析检查及默认计划退出码 0，不修改系统；新增固定路径/链接检查、备份权限验证、自动暂停认证、失败恢复和独立 Restore 入口。另以当前应用载荷编译 preview.0 测试安装器退出码 0，仅作跨版本安装器流程基线，不是历史旧代码或可分享成品。证据 `compile-upgrade-baseline.txt`，全部忽略。
- **Preflight 实际状态**：已派发 `-Phase Preflight -Execute -Elevate`，到 10-01 00:01 仍有 consent 进程，尚无 installer-preflight.json 或安装日志。正在等待 Windows UAC，不标为执行通过。用户已获得明确弹窗提醒；真实配置备份、服务移除及生命周期阶段均未开始。干净 Windows 环境仍不可用，Release 未发布。

## 2026-10-01 10:06–10:10：候选安装器安全拒绝与本机生命周期（基线 `721e8fc`）

- **Preflight：通过**。用户继续后回读此前已授权调用，10:06:51 至 10:06:53、退出码 0、7 个断言 true、Failure=null。新安装器拒绝固定临时服务的不同路径，同一后台 PID 持续运行，配置/密文不变；未创建产品安装或服务文件。证据 `installer-preflight.json`，候选 SHA-256 `0a534bcc407213c3aa00a8d929a14b60c4f8ec4972c265e5315f61bd4cc26ae6`。
- **Lifecycle：实际完成**。10:07:44 至 10:08:35，35 个断言全 true、Failure=null、OriginalDataRestored=true。父启动器 45 秒后显示 pending，随后 shell 回收为非零；不写成父调用退出码 0。子结果、实际正式服务与独立状态回读证明操作完成。先验证受保护备份，再暂停认证、核对标记后移除临时服务；全流程假凭据保持 Enabled=false。
- **安装/升级/重装**。preview.0 测试基线首次安装为 Running/Auto、重连/无人值守 false、无凭据及正确快捷方式。保存假凭据、Manual 自启和独立无人值守后，候选包升级与同版重装成功，配置和加密凭据字节不变，六份 App/Service/Core 文件哈希匹配；正式自包含界面响应、密码遮蔽。preview.0 使用本轮载荷，只验证安装器跨版本流程，不证明历史旧代码迁移。
- **卸载/再安装**。实际卸载移除服务、安装注册、产品可执行文件/快捷方式/三份数据，并释放防睡眠；无关标记文件保留且随后清理。开发原生窗口显示修正后的断连密码提示。再安装为首次默认、无旧假凭据。随后恢复原设置/密文，启动正式产品，电源计划不变。证据 `installer-lifecycle.json`、`ui-installed-final.json`、`ui-disconnected-password-fix.json` 及忽略目录安装日志。
- **独立恢复复核：通过**。`inspect-local.ps1` 新增明确 Installed/Validation 固定路径选择，解析通过；10:10:31 `-Target Installed -Elevate` 退出码 0，正式 LocalSystem 服务 Running/Auto、原三个开关 true、HasPassword=true、KeepingAwake/系统请求 true。证据 `inspection-installed-restored.json`。没有读取或输出密码；受保护备份留待发布复验后清理。
- **发布准备，未完成**。GitHub CLI 自身无登录态；复用本机对该仓库已有的 GitHub 凭据，仅在子进程环境临时使用，不输出/持久化令牌。只读 API 确认账号 aaamqrx、当前无 Release；新增清单源提交/标签状态和最终包复验阶段，解析及无修改计划通过。最终标签构建、最终包复验和 Release 尚未执行。干净 Windows、历史迁移及其他安装失败组合保持未验证，不把 M5 整体标为通过。

## 2026-10-01 10:17–10:28：最终标签构建、复验、备份清理与公开预发布

- **标签及最终构建：通过**。发布标签 `v0.1.0-preview.1` 指向 `dd2dc1e4ae5198a095e18ee55b2632a03ba74b2d`，推送并回读标签和 main。干净标签运行 `scripts/build.ps1`，10:17 完成、退出码 0；四项目 Release 0 警告/错误、30/30 离线检查、App/Service 自包含发布及 Inno 编译通过。清单 sourceDirty=false、sourceTags 包含标签、sourceCommit 等于标签提交。最终安装包 79,264,751 字节、NotSigned，SHA-256 `4bba4fa53113da88e110586f6fe192d97998b3fcb8bc405e2f17df3e2402a575`，校验文件一致。证据 `.local/acceptance-20260930/build-final-tag.txt`、`artifacts/build-manifest.json`；早期候选不是本次附件。
- **FinalPackage 工具失败与修正**。首次 UAC 取消，退出码 1、无系统变更。10:20:57 再试在清单核验后终止，原开关/数据未变；提权子进程实际工作目录为 `C:\Windows\system32`，Git 未指定仓库路径。私有启动器明确 Set-Location 后重试。证据 `installer-finalpackage-before-cwd-fix.json`、`final-runner-meta.json`。发布后 main 的验收脚本补 `git -C $repo`，避免依赖提权目录；此工具修正不移动标签或重新构建已发布包。
- **最终标签包安装复验：通过**。用户要求重发 UAC，10:21:58 至 10:22:14 `-Phase FinalPackage -Execute` 退出码 0，15 个断言 true、Failure=null、OriginalDataRestored=true。确认标签/清单/最终哈希，暂停自动重连，实际重装最终包；暂停配置与原凭据密文保留，六个安装运行文件及 README 与标签载荷一致，正式自包含 WPF 响应/密码遮蔽，恢复三个原开关和密文。证据 `installer-finalpackage.json`、`ui-installed-tagged-final.json`。本轮不新增人工真实认证或校园跨夜样本。
- **独立回读及备份清理：通过**。10:23:53 清理调用退出码 0，独立确认正式服务 Running/延迟 Auto、原三个开关开启、凭据存在/密文不变、防睡眠有效；核对固定受保护备份后删除，BackupRemoved=true、Failure=null。证据 `inspection-published-final.json`、`backup-cleanup.json`。未保留额外真实凭据备份到仓库或安装包。
- **GitHub 草稿及过程失败**。已有 GitHub 凭据仅用于临时子进程环境，不输出/持久化令牌；草稿创建及两份附件上传退出码 0。按 tag 查询草稿的 API 返回 404，改用有权访问的 Release 列表；Windows PowerShell 原生参数处理又使带引号的 jq 标签筛选解析失败，改为 PowerShell JSON 筛选。两次只读失败未公开或替换附件；最终草稿回读确认 2 个附件、服务器 SHA-256/字节数与本机实测包一致。证据 `release-draft-readback.json`。
- **公开及真实下载核验：通过**。公开 prerelease、latest=false，调用退出码 0；已授权公开，无额外发布确认。Release ID `400598315`，draft=false、prerelease=true；不带账号凭据的公共 API 回读通过，[实际 Release 页面](https://github.com/aaamqrx/CampusPulse/releases/tag/v0.1.0-preview.1) 可公开读取。实际下载安装包及 sha256.txt，10:28:14 校验安装包 SHA-256 和大小与最终实测包一致，校验文件内容完全一致。证据 `release-published-readback.json`、`release-public-readback.json`、`release-download-verification.json`。本次手动标签构建/发布，无 Actions 自动发布证据。
- **验收边界**：7 项安全拒绝和 35 项完整本机生命周期针对候选包，15 项复验针对最终标签包。测试 preview.0 是本轮载荷的安装器版本基线，不证明历史旧代码迁移；干净 Windows 不可用，完整 M5/稳定版未完成。安装版额外开机、卸载后重启、其他机器/DPI、更多失败组合和多夜稳定性未验证。一晚恢复维持用户现场反馈等级，不补成事件时间线。
- **发布后验收工具检查：通过**。显式 Git 仓库路径修正的 PowerShell 解析、默认无修改计划与从 system32 查 HEAD/发布标签均通过，命令退出码 0；仓库结构/忽略项、六份更新文档的本地链接和差异空白检查通过。仅同步辅助工具与脱敏文档，不重新构建或替换已经实测/发布的标签附件。main 的最终推送状态以远端回读为准，发布标签保持上述提交。

## 2026-10-02 至 2026-10-03：后台只读排查与早晨自动恢复验收失败

- **版本与结论：失败**。已安装 `0.1.0-preview.1+dd2dc1e4ae5198a095e18ee55b2632a03ba74b2d`，文档更新基线 `20a3f27`。用户在 10-03 明确反馈今早未能自动连接，当前版本未满足夜间断网后、早晨无人操作恢复公网的核心要求。09-30 一晚成功作为历史反馈保留，不能抵消本次失败；不是仅记为多夜稳定性未验证。
- **操作及执行结果**。10-02 12:18:33 和 10-03 13:09:56 管理员运行忽略目录中的只读 `read-backend.ps1`，仅向固定本机管道发送 `status`，两次子进程退出码均为 0。正式服务 Running/Auto，版本相同、自动重连开启。未发送 `check`、`reconnect` 或设置命令，未读取/导出账号密码，未改服务、电源、安装或真实凭据。
- **10-02 日志**。最近 80 条中，09:22:57 至 12:07:26 反复显示持久化账号拒绝原因；12:07:52 软件发起一次认证，12:08:03 两项公网探测通过。快照中拒绝保护已解除。日志无操作来源，不能确认为自动触发，也不能据通用拒绝提示认定欠费。
- **10-03 日志与用户失败反馈**。最近 80 条从 09:53:42 开始；至 12:58:54 继续检测并显示拒绝保护，没有“正在进行一次校园账号认证”事件。13:03:57 检测直接显示两个公网探测通过；13:09:56 快照为 Online、拒绝保护已解除。当前已联网不证明今早自动登录成功；用户报告的失败不因事后在线状态而改为通过。
- **源码排查**。`ConnectionWorker.CheckAsync` 在一次 `CredentialsRejected` 后持久化 `AuthenticationBlocked`，之后自动检测仍继续，但没有自动到期解除且不再提交凭据；失败计数只决定退避间隔，并非次数上限。`DrComProtocol` 将通用 `ret_code=1` 判为账号密码拒绝。此机制与现场保护状态相符；首次触发事件已轮转、对应原始响应未留存，不能确认夜间停网、欠费、密码错误或学校服务器限流为根因。
- **证据位置及边界**。用户在本次对话的现场反馈；`.local/diagnostics-20261002/backend-status.json`、`.local/diagnostics-20261003/backend-status.json`；对应只读脚本及源码。全部本机原始快照仅留忽略目录。日志最多 80 条且不记录认证来源，不能重构夜间首次失败时间线。此前构建、离线检查、安装生命周期及公开发布记录保留其原范围，本次未新增构建/离线测试、原生窗口运行、人工真实认证、安装操作或 GitHub 上传/Release 修改。
- **下一步及本次文档检查**。优先排查并修复自动恢复失败，补持久化首次拒绝原因及认证来源，先离线回归，再记录修复后的受影响项目实测；不要求重复证明用户已报告的失败。本次同步 README、HANDOFF、DEVELOPMENT、测试发布计划及本机验收状态；初次文档检查退出码 1：新增标题只写日期缩写，未满足检查的完整日期要求；已补全日期。差异空白检查当次通过，完整文档复查退出码 0：六份文档均有当前失败状态，26 个本地链接存在，Markdown 围栏及差异空白检查通过；仅验证文档一致性，不证明软件已修复。修复代码、新版本和跨夜复验均未执行。
## 2026-10-03 修复计划 R1：离线复现自动恢复阻断

- **版本与范围**：源码基线 `20a3f27`，原故障版本 `0.1.0-preview.1`。新增 `RECOVERY-REPAIR-PLAN.md` 并同步交接入口，按用户要求先写目标、再逐项推进。
- **操作与实际结果：预期失败已复现**。`.tools/dotnet/dotnet.exe run --project tests/CampusPulse.Tests -c Release -- --filter=RECOVERY-01` 退出码 1、0/1 通过，失败断言为模糊拒绝后的自动恢复应再次提交但未提交。进程内假门户第一次返回 `ret_code=1` 无可靠消息，模拟等待间隔到期并让门户允许认证；物理路径保持同一模拟值，旧后台仍被保护阻断。此项不是功能验收通过。
- **证据与边界**：`.local/recovery-repair-20261003/r1-before-fix.txt`、`r1-before-fix-result.json`，新增测试源码；全部使用随机临时目录、假凭据和模拟处理器。未启动后台服务、调用自启设置或真实门户；不确认昨晚首次失败响应及根因。读取已有界面进程和 SDK 版本只为编译预检，SDK 为 10.0.203。
- **下一步**：R2 保留首拒绝与真实提交流程的脱敏诊断，再执行 R3 分类/重试及旧保护兼容、R4 回归。当前未修复已安装程序、未打包或发布，真实自动认证和跨夜复验仍未执行。
## 2026-10-03 修复计划 R2–R4：诊断、恢复策略与离线回归

- **版本与范围**：基线 `20a3f27` 的未提交开发修改，候选版本 `0.1.0-preview.2-dev`；已安装及公开旧版仍为 `0.1.0-preview.1`。未将模拟结果改写成校园实测通过。
- **R2 诊断检查与过程失败**：四项目构建无警告错误。首次针对性检查 `RECOVERY-02` 退出码 1，测试先期待尚未完成 R3 的通用拒绝分类；调整阶段预期后第二次仍退出码 1，旧测试靠同一稳定状态填满事件，与新增去重行为冲突。修正测试：先断言稳定状态去重，再交替真实模拟状态使首次事件轮转，检查独立摘要仍保留。最终 `RECOVERY-02/03/04/09` 与 `SEC-02` 五项全部通过，整体退出码 0；诊断区分自动提交、主动重连、仅观察联网和预检未提交，旧缺字段/空摘要不虚构时间，假秘密扫描通过。证据 `r2-diagnostics.txt`、`r2-diagnostics-recheck.txt`、`r2-diagnostics-final.txt`、`r2-diagnostics-result.json`。
- **R3 源码与针对性检查**：通用拒绝无可靠账号限制证据时按五分钟等待继续；明确密码、支付或账号限制仍保护，旧模糊保护兼容重验，后台重构不绕过或延长既有等待。自动等待与单次提交最低间隔分别处理；主动重试返回暂时拒绝时保留已有明确保护。`--filter=RECOVERY` 当次 10/10 通过，退出码 0，证据 `r3-recovery.txt`。随后补充三项回归，覆盖明确保护保留、自动等待跨重构/原配置及密文不变、管道摘要与 16 KiB 限制。
- **R4 全量门槛：通过**。`powershell.exe -NoProfile -File scripts/check-dev.ps1` 退出码 0，SDK 10.0.203；Core、Service、App、测试四项目 Release 构建均 0 警告、0 错误，43/43 离线检查通过。证据 `r4-check-dev.txt`、`r4-check-dev-result.json`。新增模拟检查包含旧失败复现修复、诊断轮转、自动/主动来源、预检跳过、限频、暂停/部分连通、旧保护迁移和回复长度。
- **证据等级与限制**：以上文件均在忽略目录 `.local/recovery-repair-20261003/`。测试使用假凭据、进程内端点和随机临时存储；用单次自动流程及模拟已过等待期限验证策略，不能证明真实定时器耗时、后台服务生命周期、实际校园认证或跨夜。未提交真实密码、变更已安装后台、关闭网络、改变电源计划、打安装包或上传 GitHub。

## 2026-10-03 修复计划 R5：自包含候选准备（18:14 时点）

- **本地候选：生成通过**。使用 `.tools/dotnet/dotnet.exe publish` 分别构建 App 与 Service，参数 `-c Release -r win-x64 --self-contained true -p:Version=0.1.0-preview.2-dev`，退出码 0。App 475 文件，Service 226 文件；`.local/recovery-repair-20261003/candidate/candidate-manifest.json` 保存版本、基线、未提交状态及逐文件 SHA-256，构建记录 `r5-publish-candidate.txt`。
- **边界**：候选在 E 盘，仅供正式后台更新与原生复验准备。不是安装包或公开发布；不运行默认仍为 preview.1 的 `scripts/build.ps1` 覆盖历史产物。当前已安装运行文件及自然跨夜未复验，真实认证、安装生命周期和 GitHub 新版上传均未执行。
- **更新工具与文档检查**：新增固定本机 `scripts/update-recovery-candidate.ps1`，默认 Update 只打印范围，实际替换需 Execute；核对目标/非链接路径/清单，先停止正式服务并保存受保护原始数据和二进制备份，检查凭据密文及原设置，失败尝试回滚。PowerShell 语法及默认无修改计划检查退出码 0，尚未实测其系统更新/回滚分支。七份文档 29 个本地链接、Markdown 围栏与差异空白检查通过，退出码 0，证据 `r4-docs-check.json`。
- **候选原生演示及等待边界**：直接启动候选 EXE 因其 manifest 需要 UAC 而等待，没有生成窗口；已结束本次自己的等待启动器，未结束既有用户界面，未记为通过。改以仓库运行时启动同一候选 DLL `--smoke-test`，原生窗口响应通过，退出码 0，随后仅关闭自己创建的演示进程；证据 `r5-native-launch-interrupted.json`、`r5-native-dll-demo.json`。该演示不连接正式后台或校园网，不证明正式界面与后台一致。
- **管理员只读预检：等待确认，未完成**。`powershell.exe -NoProfile -File scripts/update-recovery-candidate.ps1 -Phase Preflight -Elevate` 已发起，Windows UAC 尚未完成，未取得子进程结果。未调用 Update Execute，正式服务仍为旧版、原 PID `48284`；用户原界面 PID `19808` 未触碰。此阻碍来自 Windows 受保护后台的管理员权限，非自动审批拒绝。后续先回读该操作结果再推进，不能重复并行启动或把 pending 写成成功。

## 2026-10-03 修复计划 R5：正式后台已更新，今晚跨夜验收准备完成

- **版本与范围**：源码基线 `20a3f27` 的未提交修复，运行版本 `0.1.0-preview.2-dev+20a3f2740e8aa06535e70c07ee5b8d0974924ac0`，功能代码与前述 43/43 离线检查一致。仅替换正式 `C:\Program Files\CampusPulse\Service` 下后台文件；E 盘 App 候选用于原生只读检查，未覆盖 C 盘 App、未退出用户既有界面。程序尚未迁移至 E 盘。
- **预检完成：通过**。用户完成 Windows UAC 后，`powershell.exe -NoProfile -File scripts/update-recovery-candidate.ps1 -Phase Preflight -Elevate` 在 18:18:43–18:18:45 完成，父与子进程退出码 0，234/234 核对通过、Failure=null。确认旧正式服务、Online、凭据可用及候选清单；此结果补齐上节 pending，不把等待时点回写成成功。证据 `r5-preflight.json`。
- **后台更新：通过**。用户继续询问今晚验收，按已授权逐项修复执行 `powershell.exe -NoProfile -File scripts/update-recovery-candidate.ps1 -Phase Update -Execute -Elevate`，18:20:59–18:21:08 完成，退出码 0，705/705 核对通过、BackendUpdated=true、Failure=null。验证候选及旧二进制备份、替换后逐文件哈希；新服务 Running/Auto，PID 48284→49936。原账号绑定、运营商、门户、检测间隔、三个开关及凭据密文均保留，HasPassword=true、三个开关均 true，防睡眠生效，两个校园有线路径的公网探测通过；诊断 LastSubmissionAt=null，更新核对没有发起认证。证据 `r5-update.json`。
- **受保护回滚边界**：`C:\ProgramData\CampusPulse-RecoveryBackup-20261003` 保存完整旧后台、原配置、密文及事件，ACL 仅 SYSTEM/管理员，当前保留。本次更新成功，没有实际执行回滚；工具具备回滚流程不等于回滚已实测。未复制真实数据至 E 盘候选或安装包。
- **新版原生连接：通过**。`.local/recovery-repair-20261003/verify-native-candidate.ps1 -Elevate` 在 18:23:00–18:23:03 完成，退出码 0、16/16 核对通过、Failure=null。实际启动 E 盘候选 EXE（不带 demo），版本显示正确、正式后台 Online 与窗口公网验证一致，三个开关匹配、密码输入遮蔽、防睡眠有效；配置与密文字节不变、没有新增认证提交、后台 PID 不变、用户旧界面保留。只关闭本次创建的窗口 PID 40056。证据 `r5-native-connected.json`。不是已安装 App 升级或完整原生 UI 回归。
- **今晚与未验证边界**：当前具备新版自然跨夜验收的运行准备。保持有线、插电、开盖与原开关，等待学校自然停网/恢复；明早先只读回读提交时间、Automatic 来源、认证结果及公网成功时间，结合用户是否手动登录的反馈判断。当前 Online 只证明已有网络可用，不证明本轮真实认证或自动恢复。自然跨夜、真实认证、新安装器生命周期、E 盘迁移及 GitHub 新版上传/Release 均未执行；旧版今早失败仍保留。
- **交接检查：通过**。同步 README、HANDOFF、修复计划、本机验收及测试发布状态；七份文档 30 个本地链接、Markdown 围栏及差异空白检查通过，退出码 0，证据 `r5-docs-check.json`。本机 `progress.json` 已更新，不能再使用 18:14 的待更新状态描述当前运行版本。

## 2026-10-04 修复版一晚跨夜验收成功与 R6 新包准备

- **跨夜结论：通过（用户现场反馈）**。用户在上一轮约定的 10-03 夜间至 10-04 早晨自然恢复验收后，明确反馈“验收成功”。对象是已部署的 `0.1.0-preview.2-dev` 后台；接受为本轮一晚自动恢复样本，不要求重复同一测试。公开旧 preview.1 的 10-03 失败与此前 09-30 一晚成功均保留历史范围。
- **日志只读回读：未完成**。`powershell.exe -NoProfile -File scripts/update-recovery-candidate.ps1 -Phase Inspect -Elevate` 父调用退出码 1，Windows UAC 被用户取消，未取得受保护快照。该取消不改变用户的现场成功结论；独立认证时间、操作来源和多夜稳定性未验证，不能推断对应首次拒绝响应。没有改设置、认证或后台，未通过其他路径绕过管理员确认。
- **安装位置与版本**：用户明确选定 `E:\Apps\CampusPulse`。新包版本 `0.1.0-preview.2`，移除开发显示后缀；新构建默认版本、安装器默认版本与源码一致。安装目录页始终显示；账号数据仍采用受保护 ProgramData。新增固定程序目录保护入口，安装时只设置当前程序树 ACL，不接受自定义任意路径参数；链接被拒绝。该安装权限分支尚待原生验证，跨夜成功不替代此分支检查。
- **候选打包：通过**。`powershell.exe -NoProfile -File scripts/build.ps1 -Version 0.1.0-preview.2` 退出码 0，四项目 Release 构建各 0 警告/0 错误，43/43 离线检查通过，App/Service 自包含发布及 Inno Setup 编译通过。候选 `artifacts/0.1.0-preview.2/installer/CampusPulse-Setup-0.1.0-preview.2.exe`，79,287,632 字节；版本隔离输出，不覆盖 preview.1 产物。证据 `.local/release-20261004/candidate-build.txt`、`candidate-build-result.json` 与版本目录 `build-manifest.json`。未提交工作区的候选不是干净标签包；候选存在和编译通过不等于已安装或发布。
- **当前运行及边界**：只读系统查询服务仍 Running、PID 49936，路径 C 盘；未执行新安装包、E 盘迁移、原生安装权限/卸载回归、受保护回滚、干净标签复验或 GitHub 新版上传。已请求用户先退出旧设置界面，以便替换/清理锁定文件；后台/网络保持运行。GitHub Connector 可读取账号，gh 未保存登录；可用的既有 Git 凭据仅在发布子进程中读取，不输出或持久化令牌。安装器验证前先准备范围明确、可回滚的工具。
- **迁移工具及管理员取消**：用户选定 E 盘目录并确认退出旧界面；新增 `scripts/validate-preview2-installation.ps1`，默认只打印固定范围，PowerShell 解析和默认计划退出码 0。实际 `-Phase Lifecycle -Execute -Elevate` 的 Windows UAC 被取消，父调用退出码 1，子安装验证未开始、没有 Lifecycle 结果文件。独立回读 C 盘服务仍 Running、PID 49936，E 盘目标及本轮备份均未创建；证据 `after-canceled-uac.json`。不能记为安装失败或成功，安装过程未执行；已请求重新发送管理员确认的明确授权，未自动重复提示。
- **包与文档核对：通过**。候选 SHA-256 `77eb3f7078df09aca4d50afc6a63dd425b1a28df03c81dd73bf937f391e496d5`，79,287,632 字节，NotSigned。七份文档 30 个本地链接、Markdown 围栏及差异空白检查通过，三个旧 preview.1 产物哈希不变。退出码 0，证据 `package-check.json`。一次文档补丁因未匹配完整原句而拒绝，未写入任何文件；改用真实原句后成功。
- **版本拒绝检查的自动审批与安全替代**：实际运行 `build.ps1 -Version 0.1.0-preview.1 -SkipInstaller` 的检查被自动审批拒绝，理由是旧版本失败路径可能清理应保留的 preview.1 产物；命令未执行，无进程退出码，不绕过该拒绝。将源码版本预检移到任何产物清理 try/catch 之外后，仅运行 PowerShell AST 检查，确认拒绝分支不在清理 try/catch 中且位置先于清理入口；退出码 0、静态检查通过，证据 `version-preflight-static.json`。不是实际运行旧版本路径通过。
- **GitHub 只读准备：通过**。复用已有本机 Git 凭据的临时发布子进程，Status 退出码 0，读取发布账号 `aaamqrx` 和唯一已公开 preview.1；令牌未输出/持久化。`git ls-remote` 退出码 0，main 仍 `20a3f2740e8aa06535e70c07ee5b8d0974924ac0`，旧 annotated tag ref 仍 `e313b50d1477f9af611c14296c2b92db69ab22c4`，新 preview.2 标签尚不存在。没有创建草稿或推送新版。
- **源码与工具审查：通过其有限范围**。23 个改动文件、三份新/变更 PowerShell 工具解析、差异空白及已知 GitHub 令牌/私钥模式扫描通过，退出码 0，证据 `source-review.json`。不包含完整秘密审计或新安装器原生运行；`.local/`、`.tools/`、`artifacts/` 仍忽略，没有暂存或提交真实数据。当前候选安装包可供审核，系统验证等待用户对重新发送管理员提示的回复。

## 2026-10-04 R6 候选安装生命周期、失败恢复与 E 盘迁移

- **用户重新授权**：用户明确“重新弹窗吧”，重新完成 Windows 管理员确认；此前两次 UAC 取消保持历史记录，没有把取消写成成功或绕过确认。
- **跨夜独立脱敏诊断**：13:32 安装前 status 读取 `FirstRejectionAt=2026-10-03T23:31:59.9225307+08:00`、原因 `unconfirmed_rejection`、Automatic；事件表明夜间持续约五分钟重试。`LastSubmissionAt=2026-10-04T05:31:22.6378956+08:00`、Automatic、`authentication_accepted`，结果时间 05:31:22.9034168，`RejectionResolvedAt=05:31:25.2441355`。用户反馈与自动记录相符；不推断学校实际供网时间、旧 preview.1 首次响应或多夜稳定。完整事件与摘要仍在忽略目录，不公开账号/密码。
- **第一次 Lifecycle：失败**。13:32:55–13:33:20，1435/1436 核对通过，普通卸载删除产品数据并留下开发更新的两个 PDB，工具误设保留数据/目录完全删除断言；回滚又因数据目录不存在失败。受保护原配置/密文/程序备份完整，网络未断开。证据 `installer-lifecycle-first-failed.json`；未把此失败写成迁移成功。
- **明确 Restore：通过**。修复回滚先重建管理员/SYSTEM 数据目录、先放回暂停配置和原密文，`-Phase Restore -Execute -Elevate` 13:37:17–13:37:35，724/724 核对、父调用退出码 0，Failure=null、RollbackCompleted=true、OriginalProfileRestored=true。恢复原 C 安装和修复后台、原密码/开关、防睡眠与公网；未发主动认证。
- **第二次 Lifecycle：失败并成功回滚**。13:38:42–13:39:12，1457/1458 核对。旧开发 PDB 哈希核对后删除，但卸载器主启动器退出早于自身辅助删除；工具过早判断根目录未删除。自动回滚完成、原设置恢复。证据 `installer-lifecycle-second-failed.json`；修正等待卸载器自身清理，不删除任意目录、不触碰无关文件。
- **第三次 Lifecycle：通过**。`powershell.exe -NoProfile -File scripts/validate-preview2-installation.ps1 -Phase Lifecycle -Execute -Elevate`，13:40:11–13:41:02，2222/2222 核对，Failure=null、OriginalProfileRestored=true。父启动器超过 45 秒报告 pending，最终子报告独立回读成功，不记父退出码 0。候选 SHA 与前述一致，未提交源码基线 `20a3f27`。实际 C 升级、C 卸载、E 安装/重装/卸载/重装全部执行；安装程序逐文件 SHA、ACL/管理员所有者、Users 只读、注册路径/版本、新版原生界面/遮蔽密码/三个开关及响应核对通过。旧 C 程序目录删除，固定 `E:\Apps` 新建受管理员管理；当前 Running/延迟 Auto、Online、防睡眠生效，原账号配置/开关及密码密文不变，实际认证摘要前后完全相同。
- **数据恢复与回滚边界**：普通卸载按既有设计删除三份产品数据；迁移工具从 `C:\ProgramData\CampusPulse-ReleaseBackup-20261004` 私有完整备份恢复暂停配置和密文，再启动新安装，最后恢复原设置。备份未写入项目/包/日志。现保留两份受保护备份，发布后可按固定标记及非链接路径安全清理。失败 Restore 分支已实测，不代表任意崩溃或干净机恢复。
- **未完成**：干净标签构建和最终包复验、新版 GitHub 上传与公开下载回读；干净 Windows、其他机器/DPI、多夜、卸载后重启未测试。本轮安装测试未触发真实校园认证、断开网络、重启或改变电源计划。

## 2026-10-04 R6 干净标签最终包、独立复核与公开预发布完成

- **最终干净标签构建：通过**。源码提交 `58bc8f03ba97d22a9089e918ebb0cce62f4f20ea`、annotated tag `v0.1.0-preview.2`，构建清单 sourceDirty=false、sourceTags 对应本标签。`scripts/build.ps1 -Version 0.1.0-preview.2` 退出码 0，四项目 Release 构建 0 警告/0 错误，43/43 离线检查、自包含 App/Service 及 Inno 编译通过。最终安装包 79,276,352 字节、NotSigned，SHA-256 `4480aaffd22ff601d4a089e4b9a45ae6ac7cc7fae01b743ff77e3916cc224f28`。候选包与最终包哈希不同，未用候选冒充最终包。旧三个产物与 preview.1 标签回读保持不变；证据 `final-tag-build.txt`、`final-tag-build-result.json`、版本隔离清单。
- **FinalPackage：通过**。`scripts/validate-preview2-installation.ps1 -Phase FinalPackage -Execute -Elevate` 在 13:47:38–13:47:54 完成，父调用和子记录 ProcessExitCode=0，750/750 核对、Failure=null、OriginalProfileRestored=true。复验确切 clean tag/manifest/包 SHA、E 盘重装、逐文件载荷/ACL、原设置/密文、正式原生窗口版本/三个开关/遮蔽输入/响应、Online 和防睡眠。原 Automatic 提交时间/结果摘要保持不变；本轮没有主动认证。证据 `installer-finalpackage.json` 与 `install-final-tagged-package.log`。
- **独立系统复核与临时备份清理：通过**。`verify-and-cleanup.ps1 -Execute -Elevate`，Verified=true、Failure=null；另回读 LocalSystem/Running 的固定 E 服务，二进制产品版本含 `58bc8f03ba97d22a9089e918ebb0cce62f4f20ea`，桌面/开始菜单快捷方式指向 E App，旧 C 程序目录不存在，原三个开关/密码密文、Online、ActualStartWithWindows、防睡眠均符合原配置；没有新增认证提交。核对两份本轮临时备份的固定路径、标记、私有 ACL 及整棵树无链接后，仅删除 `CampusPulse-RecoveryBackup-20261003` 与 `CampusPulse-ReleaseBackup-20261004`。正式 ProgramData/凭据和 E 程序未删除；证据 `independent-final-cleanup.json`。
- **公开源码及标签：通过**。23 个实际变动文件显式暂存，29 个文档本地链接/围栏、三份 PowerShell 解析、差异检查及已知秘密模式检查通过（有限模式检查，不是全面安全审计）；`.local/.tools/artifacts` 未提交。源码提交和新 annotated tag 已推送，远端 main 与 tag peeled commit 回读一致；旧 preview.1 tag ref 不变。发布后更新交接/验证文档及只读排查入口，标签不移动、安装包不替换；应用与服务二进制仍对应最终标签。
- **GitHub 新版发布与下载：通过**。[v0.1.0-preview.2](https://github.com/aaamqrx/CampusPulse/releases/tag/v0.1.0-preview.2)，Release id `402863864`，draft=false、prerelease=true；两份附件为安装包和 sha256.txt。上传先建立草稿并核对附件大小/digest，随后公开，已登录 API 元数据、匿名公开 Release 页面及实际下载回读通过；匿名 API 尝试因配额限流失败，不记为匿名 API 通过；下载 79,276,352 字节，SHA-256 与本机实测最终包一致，校验文本逐字节一致。已有 Git 凭据只在子进程环境中临时使用，未输出/持久化令牌。证据 `release-draft-readback.json`、`release-publish-readback.json`、`release-public-readback.json`、`release-download-verification.json`。
- **验证边界**：本次为本机受影响功能的一晚真实自动恢复、本机安装生命周期及最终包/发布回读。没有干净 Windows、其他机器/运营商/DPI、额外重启/卸载后重启、多夜稳定、任意崩溃恢复或完整 M5/稳定版证明。真实密码只存在用户本机受保护数据，没有进入 Git 或发布附件。两次迁移检查失败及回滚恢复保留前节历史。

## 2026-10-04 发布后交接与只读检查入口

- **交接与文件检查**：同步 README、HANDOFF、LOCAL-ACCEPTANCE、修复计划、TESTING 和本记录。辅助更新曾因原句匹配不同及 PowerShell JSON 的 UTF-16 编码失败；修正精确原句匹配/按 BOM 解码后完成，没有改变最终包或移动标签。最终文档链接/围栏、差异与已知秘密模式检查通过；证据 `postrelease-check.json`。不是全量秘密审计。
- **自选目录的只读入口**：修正 `inspect-local.ps1 -Target Installed` 使用固定 Inno 产品注册的 InstallLocation 核对精确 LocalSystem 服务路径，并拒绝链接祖先；Installed 输出移至 `.local/installed-inspection/`，不覆盖旧验收证据。只读入口不执行注册路径、不改设置、不提交密码。完整命令的最后 Windows UAC 被取消，父调用退出码 1，未读取受保护数据，不再重复弹窗。
- **安全替代验证：通过有限范围**：PowerShell 解析通过；从修改后的真实源码 AST 提取并执行安装注册路径选择分支（不含提权或管道调用），实际匹配 `E:\Apps\CampusPulse\Service\CampusPulse.Service.exe`，退出码 0。证据 `inspect-custom-target.json`。这不是完整管理员只读检查通过；前述独立最终系统复核和安装结果不受此取消影响。
- **发布边界**：发布后提交仅含交接/验收文档及此只读工具；没有改应用或服务二进制。最终包和标签仍来自 `58bc8f03ba97d22a9089e918ebb0cce62f4f20ea`。Git main 后续提交单独推送回读，旧/新发布标签和已公开附件保持不变。

## 后续记录格式

每条记录包含：日期、软件版本/提交标识（若尚未建立则注明）、测试编号、执行环境、操作或命令、预期结果、实际结果、通过/失败/未执行、脱敏证据位置及验证限制。

真实密码、原始抓包、会话、个人内网地址和完整认证请求不作为公开证据。自动化模拟测试、真实校园网络测试和 GitHub 发布检查分别记录。
