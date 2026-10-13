# Microsoft Store MSIX 签名与分发

更新日期：2026-10-13

## 选择的路线

项目选择 Microsoft Store MSIX 作为首选免费签名路线。现有桌面程序使用 WPF 和 .NET Framework 4.8；Microsoft 的桌面应用指南支持通过 Windows Application Packaging Project 将 WPF / WinForms 应用封装为 MSIX。应用仍以完整信任桌面程序运行，并在包清单声明蓝牙设备能力，以读取已配对 iPhone 的 AMS 状态。[WPF 桌面程序 MSIX 指南](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/dotnet/package-app) · [蓝牙应用能力](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/bluetooth)

此路线把发布安装包与发行渠道改为 Microsoft Store。Store 在应用审核通过并发布后为 Store 包提供受信任的签名；本地测试包不因此自动获得公共信任。现有 Inno Setup EXE 暂时保留为内部试用工件，不由 Store 代签。[Store 包签名说明](https://learn.microsoft.com/en-us/windows/msix/package/packaging-uwp-apps)

## 已准备内容

- `LyricConnector.sln` 同时包含现有桌面应用和 Windows Application Packaging Project。
- `installer/msix/LyricConnector.Package.wapproj` 引用 x64 WPF 桌面项目，面向 Store 上传包。
- `installer/msix/Package.appxmanifest` 声明完整信任桌面入口、`runFullTrust` 和 `bluetooth` 能力。
- 清单身份仍是占位值；包名、Publisher 和 PublisherDisplayName 必须由 Partner Center 应用身份替换。
- 视觉资源目前复用 128×128 PNG 草稿；提交前需要在 Visual Studio 的包清单设计器中生成或替换为符合 Store 尺寸要求的正式资源。

## 尚需在 Windows 上完成的步骤

1. 在 Partner Center 注册开发者账号并完成微软要求的身份验证。此步骤涉及用户账户及身份资料，需要由项目所有者本人操作。
2. 在 Partner Center 保留产品名称，然后在 Visual Studio 中将打包项目关联到该 Store 应用。让 Visual Studio 写入真实的 `Identity Name`、`Identity Publisher` 和发布者显示名称；不要将清单里的占位值作为正式身份提交。[应用身份字段](https://learn.microsoft.com/en-us/windows/apps/publish/view-app-identity-details)
3. 安装 Visual Studio 的 MSIX Packaging Tools 工作负载和所需 Windows SDK，打开 `LyricConnector.sln`，确认 WPF 项目和 MSIX 打包项目均为 x64，并生成 Store 上传包。Microsoft 推荐通过 Visual Studio 生成 `.msixupload` 后提交 Partner Center。[WAP 设置](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-packaging-dot-net) · [Store 上传包流程](https://learn.microsoft.com/en-us/windows/msix/package/packaging-uwp-apps)
4. 使用测试包验收应用启动、系统托盘、Bluetooth LE / AMS、歌词网络访问、用户数据存放、升级和卸载。MSIX 清单已声明 `bluetooth`，但仍须在真实 Windows 包环境验证配对设备访问和 AMS 行为。
5. 提交 `.msixupload` 并选择 **Private audience**，添加测试者使用的个人 Microsoft 账号组；让测试者从 Store 私有链接安装。非受众用户无法查看或获取该应用。首次发布选择 Public audience 后，不能在后续提交中再改为 Private audience，所以首发前必须确定受众策略。[私有受众说明](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/visibility-options)
6. 通过 Store 审核并发布后，确认 Store 安装的包由 Microsoft 签名，再考虑是否将受众扩大为 Public。

## 状态与限制

目前只准备了打包项目和清单草案，没有生成 MSIX、登录 Partner Center、预留商店名称、提交审核或发布应用。此前已生成 Inno Setup 本地测试安装器；本文件描述的 MSIX 项目仍需在 Windows / Visual Studio 环境生成并验收。Partner Center 的身份字段、正式视觉资源、Windows 安装验收和 Store 审核仍是待办项。

清单把 Windows 10 版本 1903（10.0.18362.0）设为最低系统版本；需要在 Windows 验收时确认这与应用实际依赖、Store 要求和目标用户相符。包内应用仍需连接蓝牙已配对设备，Store 审核需通过其自身认证流程。

## 相关项目文档

- [Windows 安装包方案](installation-plan.md)
- [代码签名方案比较](code-signing-options.md)
- [SignPath Foundation 资格核查](signpath-path.md)
