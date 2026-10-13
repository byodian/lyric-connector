# Windows 安装包签名方案

调研日期：2026-10-13

本项目此前规划使用 Inno Setup 生成单用户安装程序。这里区分“Windows 默认信任的 Authenticode 签名”和“供用户另行验证来源的制品证明”：两者用途不同。

| 方案 | 是否免费 | 对 Windows 安装体验的作用 | 对本项目的适用性 |
| --- | --- | --- | --- |
| Microsoft Store 分发 MSIX | 是 | 应用通过审核并发布后，Microsoft 使用受信任证书签名；用户从 Store 安装 | **当前已选择并开始实施。**适合免费的 Store 信任签名；需要 Partner Center 账号、应用身份、MSIX 格式和 Store 审核。个人开发者新注册流程可能免注册费，但需要完成身份验证 |
| SignPath Foundation | 符合条件的开源项目免费 | 提供以 SignPath Foundation 为发布者的代码签名证书，通过受控流水线验证来源并签名 | 仓库目前已公开，但 GitHub 上没有可见的 OSI 许可证或已发布版本；项目尚未满足申请前提。申请前需由项目方决定并采用合适的 OSI 许可证、正式发布待签格式，并补齐下载说明和签名政策；不因本调研更改许可证或仓库内容 |
| Certum Open Source Code Signing | 否 | 面向开源项目的公信代码签名证书 | 当前不是免费路线；Certum 官网价目列出的 Open Source 证书起价为 €25，具体产品套装和地区价格以购买页面为准 |
| 自签名证书 | 是 | 只有先在目标设备信任该证书，Windows 才会把它视作可信；其他用户仍会看到强安全警告 | 适合本机开发测试，或由管理员统一管理证书的封闭设备，不适合一般用户下载即装 |
| GitHub Artifact Attestations / Sigstore | 可免费使用的场景有限 | 给文件附加签名来源、提交和流水线等证明，用户需用相应工具主动验证；它不是 Windows Authenticode 信任链，也不会消除 SmartScreen 警告 | 可作为供应链来源证明，不能替代安装程序签名。GitHub Free、Pro、Team 的私有仓库不支持 Artifact Attestations；私有仓库需 GitHub Enterprise Cloud |
| Azure Artifact Signing | 否，Basic 当前为约 9.99 美元/月 | 提供 Windows 公共信任证书；新发布的程序仍可能先出现 SmartScreen 提示，信誉会逐步积累 | 若保留 Inno Setup 并面向 GitHub 下载用户发布，这是比自签名更合适的可信签名路线，但有费用和地区资格限制；Azure 免费、试用或赞助订阅不受支持 |

## 建议

本项目已选择 Microsoft Store MSIX 作为第一条免费签名路线，并开始添加 Windows Application Packaging Project。下一步需要在 Partner Center 创建开发者账号并保留应用名称，随后把 Partner Center 提供的包名和 Publisher 值关联到 MSIX 包，完成 Windows 上的安装验收后生成 `.msixupload` 并提交审核。Store 只为 Store 提交并通过审核的包提供受信任签名；它不会替独立的 Inno Setup EXE 或 MSI 签名。若先采用私有受众分发，安装仅对指定用户组开放，收件人需使用对应的个人 Microsoft 账号从 Store 获取应用。提交时应选择 Private audience；如果曾以 Public audience 发布，之后不能再改为 Private audience。[MSIX Store 实施细节](msix-store-packaging.md)

如果必须继续提供可从 GitHub Releases 直接下载的 Inno Setup EXE，目前没有适用于本项目现状的免费、公开信任 Authenticode 方案。自签名只适合受控设备；GitHub Attestations / Sigstore 可以补充来源和完整性验证，但不能代替 Windows 对安装程序的信任。仓库虽然已公开，仍需先选择并采用 OSI 许可证、发布该格式的软件、补充下载说明和签名政策，之后才值得申请 SignPath Foundation；其证书发布者为 SignPath Foundation，并要求可验证流水线和逐次发布签名批准。

Inno Setup EXE 仍可用于 Windows 本机打包和内部试用；它与 Store MSIX 是两个分发工件。只有在 Partner Center 审核并发布后，Store MSIX 才获得由 Store 管理的受信任签名。在此之前，MSIX 仅用于本地打包验证，不应作为已签名的面向用户安装程序宣传。

## 官方资料

- Microsoft：[个人开发者免费注册与身份验证流程](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/open-a-developer-account)
- Microsoft：[Windows 应用代码签名选项与 Store 重新签名说明](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)；[MSIX 私有受众和可见性选项](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/visibility-options)
- Microsoft：[SmartScreen 信誉机制](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)；[Artifact Signing 价格层级](https://learn.microsoft.com/en-us/azure/artifact-signing/how-to-change-sku)；[Artifact Signing 对免费/试用 Azure 订阅的限制](https://learn.microsoft.com/en-us/azure/artifact-signing/faq)
- Microsoft：[WPF / WinForms 桌面程序使用 MSIX 打包](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/dotnet/package-app)；[Store MSIX 私有受众设置](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/visibility-options)
- SignPath Foundation：[免费开源代码签名服务](https://signpath.org/)；[开源项目资格与服务条件](https://signpath.org/terms.html)
- Certum：[代码签名证书及当前起价](https://www.certum.eu/en/code-signing-certificates/)
- GitHub：[Artifact Attestations 功能与用途](https://docs.github.com/en/actions/concepts/security/artifact-attestations)；[私有仓库计划限制](https://docs.github.com/en/enterprise-cloud@latest/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations)
- Sigstore：[无长期密钥签名和验证流程](https://docs.sigstore.dev/cosign/signing/overview/)；[Fulcio 临时证书说明](https://docs.sigstore.dev/certificate_authority/overview/)
