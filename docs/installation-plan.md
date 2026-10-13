# Windows 安装包方案

## 目标

交付适用于 Windows 10 / 11 x64 的单用户安装程序，让用户无需预装 Python，也不必从源码目录启动 Lyric Connector。首个安装包用于项目内部试用；完成干净系统验证后，再考虑公开分发。

## 选定方案

保留 Inno Setup 生成单文件 Setup EXE，用于本机打包和内部试用；同时将 Microsoft Store MSIX 作为首选免费签名与分发路线。应用本体从 PowerShell 动态载入 C# 源码的启动方式迁移为常规 WPF 可执行程序，继续以 .NET Framework 4.8 延续现有 WPF 与 WinRT 蓝牙互操作路径。Microsoft 官方支持使用 Windows Application Packaging Project 将 WPF 桌面程序封装为 Store MSIX。[MSIX 桌面应用指南](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/dotnet/package-app)

建议安装位置为 `%LOCALAPPDATA%\Programs\Lyric Connector`，在开始菜单创建快捷方式，并提供可选的桌面快捷方式。安装和升级不覆盖用户数据；窗口设置及歌词缓存继续存放在 `%LOCALAPPDATA%\LyricConnector`。卸载默认保留用户数据，避免误删已选歌词和配置。

安装器按当前用户权限运行（Inno Setup 的 `PrivilegesRequired=lowest`）。首个内部版本可以未签名；面向公开分发前应配置代码签名证书，对安装程序签名并验证 Windows SmartScreen 展示效果。

## 实施进度

已添加独立 WPF 项目、Inno Setup 定义、Windows 打包工作流，以及一个 Windows Application Packaging Project 和 Store 包清单。已使用 Windows .NET SDK 8.0.425 与 Inno Setup 6.7.3 生成本地未签名测试安装器 `artifacts/installer/LyricConnector-Setup-0.1.0-x64.exe`，并保存 SHA-256 校验文件。应用编译有一条未等待异步调用警告；Inno 脚本的架构标识已更新为兼容标识并成功编译。安装器尚未实际安装或在干净系统验收，且 `artifacts/` 被 Git 忽略，不会随源码提交。Store 包身份仍是占位值；图标资源目前复用 128×128 草稿图，尚未生成 MSIX。

## 实施步骤

1. 将桌面入口迁移为独立 Windows 桌面可执行文件，保留 AMS 蓝牙状态接收、歌词查询、系统托盘、配置和缓存行为；启动不再依赖 Python 或临时动态编译。
2. 核对 WPF、WinRT 蓝牙、HTTP 查询、图标资源及系统托盘依赖，并确认应用可在未安装 Python 的用户账户中启动。
3. 编写 Inno Setup 安装定义，配置当前用户安装、开始菜单快捷方式、可选桌面快捷方式、版本升级和卸载行为。
4. 在未安装开发工具的 Windows 10 / 11 x64 标准用户环境进行安装验收；覆盖首次启动、已配对 iPhone 连接、YouTube Music 状态读取、LRCLIB 查询、托盘隐藏与恢复、右键退出、升级后保留设置及卸载后保留用户数据。
5. 输出带版本号的安装程序、SHA-256 校验值和安装说明。先作为内部试用产物保存；GitHub Release 或其他公开发布需要单独安排。
6. 使用 `LyricConnector.sln` 在 Windows Visual Studio 中打开 MSIX 打包项目，安装 MSIX Packaging Tools 工作负载，将项目关联到 Partner Center 中保留的应用名，并替换清单中的包名、Publisher 和发行者显示名。
7. 生成正确尺寸的 Store 视觉资源，在 Windows 上安装测试 MSIX，验证启动、托盘、已配对 iPhone 的蓝牙状态读取、歌词网络查询、升级和卸载行为。
8. 生成 `.msixupload` 并提交 Partner Center。首发设置为 Private audience，添加指定测试者组并通过 Store 审核；Store 发布后再验证 Store 签名和私有受众安装。

当前工作流位于 `.github/workflows/windows-installer.yml`，只响应手动触发。GitHub Actions 中选择 “Windows installer artifact” 并手动运行后，会上传未签名的安装工件及 SHA-256 文件（保留 30 天）供内部验证；它不会创建 Release 或对外发布。

## 安装时的使用条件

- 目标设备为 Windows 10 / 11 x64，并具有可用的蓝牙 LE 适配器。
- 用户仍需在 Windows 蓝牙设置中与 iPhone 配对；安装程序不代替配对流程。
- 在线歌词检索需要互联网连接；离线时可使用已缓存或导入的 LRC 文件。
- 其他 iPhone 播放器的状态和控制能力仍由其 AMS 支持情况决定，安装包不会改变播放器兼容范围。

## 签名路线

本项目已选择 Microsoft Store MSIX 作为首选免费签名路线。Store 会在应用通过审核并发布后为 Store 包提供受信任签名；用户可以配置为仅向选定受众分发。该路线需要 Partner Center 开发者账号、保留应用名称、准确的包身份、Store 审核和 Windows 上的真实设备验收。现有 Inno Setup EXE 暂时保留用于内部试用，Store 不会替独立的 Inno EXE 签名。SignPath Foundation 作为备选仍受 OSI 许可证和发行资格限制，详见[签名方案调研](code-signing-options.md)与[SignPath 资格核查](signpath-path.md)。

## 验收与发布边界

本地 Inno Setup 测试安装器已生成，但尚未安装验证。接下来需在 Windows 上完成干净环境验收，确认桌面控制和悬浮歌词都可启动，关闭控制窗口后应用驻留系统托盘，托盘单击可恢复设置，托盘菜单可彻底退出，升级不会重置设置或删除歌词缓存。MSIX 仍需设置 Partner Center 身份、生成正式资源与上传包，并在 Store 审核发布后检查私有受众和签名状态。

本地测试工件为未签名的 `artifacts/installer/LyricConnector-Setup-0.1.0-x64.exe`，校验值位于同目录的 `SHA256SUMS.txt`。这是供本机试装的临时产物，尚未完成安装、启动和功能验收，不应作为公开发行版本。

## 参考资料

- [Inno Setup：PrivilegesRequired](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm) — 配置安装权限范围。
- [Microsoft：WPF 应用部署](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/app-development/deploying-a-wpf-application-wpf?redirectedfrom=MSDN&view=netframeworkdesktop-4.8) — WPF 应用部署与运行时说明。
- [Microsoft：WPF / WinForms 桌面应用 MSIX 打包](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/dotnet/package-app) — 使用 Windows Application Packaging Project 引用桌面项目。
- [Microsoft：MSIX Store 包和应用身份](https://learn.microsoft.com/en-us/windows/msix/package/packaging-uwp-apps) — Partner Center 关联、Store 上传包和 Microsoft 托管签名。
- [Microsoft：Bluetooth 应用能力](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/bluetooth) — MSIX 清单声明已配对蓝牙访问能力。
- [Microsoft：MSIX 私有受众](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/visibility-options) — 通过 Partner Center 限制测试者范围。
- [Inno Setup：SignTool](https://jrsoftware.org/ishelp/topic_setup_signtool.htm) — 配置安装程序签名。
