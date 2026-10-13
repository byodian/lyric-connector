# SignPath Foundation 资格与实施核查

核查日期：2026-10-13。仅依据 SignPath Foundation/SignPath 官方站点与文档；当前仓库状态按本轮核查提供的信息记录。

## 当前结论

仓库远端目前是公开的，这符合开源项目公开审查的实际需要；但远端没有 `LICENSE`，也没有已发布版本。SignPath Foundation 当前条款要求项目所有组件采用 OSI 批准的开源许可证（不得商业双重许可）、没有专有代码、持续维护，并且**已经以申请签名的同一种形式发布**。因此仓库目前尚不满足资格；本地未提交文件不构成已发布版本。若目标是 Inno Setup 安装程序，还需有该格式的发行版和说明其功能的下载页或 Store 页面。SignPath 没有在资格清单中把“GitHub visibility 必须是 Public”单列为硬性条件；其 GitHub Connector 文档也描述了 private repo 的技术接入情形。但私有源代码是否能获 Foundation 接纳，公开条款没有明确承诺。此项目远端已经公开，所以不需要为此改变可见性；仍须补齐许可证和目标格式发行记录。[Foundation 资格条款](https://signpath.org/terms.html) · [GitHub Connector](https://docs.signpath.io/trusted-build-systems/github)

条款还要求项目主页和下载/发行页面有 **Code signing policy**，说明“Free code signing provided by SignPath.io, certificate by SignPath Foundation”，列出作者、审查者和签名批准者角色与成员，并提供隐私说明。首次发布必须满足“already released in the form that should be signed”；条款没有说明为了获得首次签名，预先发布的同格式程序是否必须未签名，也没有公布该情形的例外流程。实际申请前应向 SignPath 确认如何处理首次发行，不应把现有本地变更当作发行版。[Foundation 资格条款](https://signpath.org/terms.html)

## 免费范围与签名身份

这不是可供任意项目使用的通用免费层：SignPath Foundation 为符合条件的开源项目提供免费的 SignPath.io 使用和代码签名证书。证书签发给 **SignPath Foundation**，它会作为签名发布者显示；证书不是发给项目作者或本仓库所有者。Foundation 代表签名项目承担发布者身份，因此保留接受或拒绝申请、暂停服务及撤销证书的权利，且不保证每个申请都获批。[Foundation 首页](https://signpath.org/) · [资格条款与证书说明](https://signpath.org/terms.html) · [Foundation 运营说明](https://signpath.org/about.html)

## 申请及签名流程

1. 先满足资格条件：为整个项目选择并公开 OSI 批准许可证，确认没有自有或第三方专有组件；准备功能说明、下载/发行页以及 Code signing policy。Foundation 要求项目已维护、已有对应形式的发行版，并要求可验证签名文件来自项目自己的源代码和脚本。
2. 通过 [SignPath Foundation 的申请入口](https://signpath.org/apply.html) 申请免费订阅。当前公开申请页提供申请入口，但没有说明审核时长、完整评审步骤或保证接受；Foundation 条款明确表示它没有必须发证的义务。
3. 获准后，在 SignPath 建立项目、签名策略和 Artifact Configuration；为 GitHub 添加 SignPath 的 `GitHub.com` Trusted Build System，链接项目，并按需要安装 SignPath GitHub App 授予仓库访问权限。
4. GitHub Actions 先上传未签名发行物为 workflow artifact，再调用 SignPath 的 `submit-signing-request` action。SignPath Connector 会检查请求来自 GitHub workflow、来源元数据由 GitHub 提供、制品已上传；Foundation 的开源项目要求签名前的 workflow jobs 全部运行在 GitHub-hosted agents 上，并要求对来源做验证。
5. 每个发行版的签名请求都必须经过人工批准。项目成员须为 SignPath 和源代码托管账号启用多因素认证；团队需明确作者、审查者、批准者职责，非 committer 的变更需经项目成员审查。条款要求明确角色，并未明文规定每个角色必须由不同自然人担任。

配置与流程依据：[资格和团队规则](https://signpath.org/terms.html) · [GitHub Trusted Build System / Action](https://docs.signpath.io/trusted-build-systems/github) · [项目、签名策略、批准和来源验证](https://docs.signpath.io/projects) · [签名请求方式](https://docs.signpath.io/signing-code)

## Inno Setup EXE 支持

SignPath 的 Artifact Configuration Reference 将 `.exe` 列为 PE 文件，并支持通过 `<authenticode-sign>` 进行 Windows Authenticode 签名。因此，**Inno Setup 生成的标准 `setup.exe` 可按单个 PE/EXE 文件申请签名**。这是从 SignPath 对 PE/EXE 的格式支持推得；其公开格式列表没有单独列出 Inno Setup 专用容器格式。单独对外层 `setup.exe` 签名只签安装程序文件本身，不能据此认为 SignPath 会解包并签署嵌在 Inno 安装器中的程序文件。需要签署的应用 `.exe` / `.dll` 应在打包前单独签名，之后再签 Inno 安装器；第三方上游二进制不得借用本项目的 Foundation 证书签名。

Foundation 还要求所有签名 PE 文件配置并强制检查元数据：Product Name 为项目名，Product Version 在同一发行构建中一致。Inno Setup 产物及应用二进制需要提供符合 Artifact Configuration 限制的版本和产品信息。[Artifact Configuration 格式与 Authenticode 支持](https://docs.signpath.io/artifact-configuration/reference) · [PE/EXE 签名示例及嵌套签名说明](https://docs.signpath.io/artifact-configuration/syntax) · [Foundation 的元数据和上游代码限制](https://signpath.org/terms.html)

## 对当前项目的落地判断

远端公开这一项已满足；当前仍缺少公开许可证和已发布的 Inno 安装程序，因此现在不能把 SignPath Foundation 当作本轮安装包的确定签名渠道。若后续走这条路线，需要先完成许可证和发布页，解决“先发布同形式版本”的申请前置条件，定义签名策略/职责与 Code signing policy 页面，再提交申请并等待接受。由于 Foundation 对下载运行的程序要求可验证的项目声誉，最终决定权仍在 Foundation；不要把审核通过或首次版本签名视为保证。

本项目当前已选择 Microsoft Store MSIX 作为第一条签名与分发路线。它能为通过 Store 审核并发布的 MSIX 提供 Store 信任签名，也支持将应用限制在指定测试受众；它不会签署独立的 Inno Setup EXE。SignPath Foundation 暂列为以后继续提供 GitHub 下载时的备选，仍须先解决许可证、对应格式的公开发行和签名政策等资格条件。当前 MSIX 实施状态、Partner Center 操作和私有受众决定见[Microsoft Store MSIX 方案](msix-store-packaging.md)。
