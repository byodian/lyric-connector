# 安装包运行依赖与 Python / probes 的用途

## 结论

普通用户通过 Inno Setup 安装并运行已编译的桌面程序时，不需要 Python 3，也不需要 `probes/`。Python 和这些探针属于源码启动、开发检查及蓝牙诊断工具，不是当前安装程序的运行入口。项目的打包工作流使用 .NET SDK 和 Inno Setup，不调用 Python 或 `probes/`。

## 三种用途

| 用途 | 入口与代码 | 是否属于安装后运行依赖 |
| --- | --- | --- |
| 安装后的桌面程序 | `desktop/LyricConnector.csproj` 编译出的 .NET WPF `LyricConnector.exe`，由 `desktop/Program.cs` 进入 WPF / AMS 应用代码 | 否。安装器直接启动该 EXE，不启动 Python。 |
| 从源码目录启动 | `desktop/run.py` | 仅这种启动方式需要 Python 3 和 64 位 Windows PowerShell 5.1。脚本读取 C# 源文件，将其交给 PowerShell 的 `Add-Type` 动态编译，再启动应用。它也提供测试、歌词提供方检查和图标预览等开发选项。 |
| 蓝牙诊断与采样 | `probes/windows_bluetooth.py`、配套 `.ps1` 和 `NotificationCollector.cs`；`probes/check_ams_capture.py` | 否。前一组通过 Windows PowerShell 检查 BLE 能力或采集 AMS 状态；后一脚本只分析已有的 JSON 采样。Python 文件使用标准库，没有项目内 `pip` 依赖清单。 |

源码目录的启动说明不能直接当作安装包的运行要求：根目录 README 的快速使用步骤选择了 `desktop/run.py`；安装后则由开始菜单或桌面快捷方式直接启动 `LyricConnector.exe`。

## 安装器当前包含什么

`installer/LyricConnector.iss` 的 `[Files]` 当前列出三项：`LyricConnector.exe`、`LyricConnector.exe.config` 和 `assets/lyric-connector.ico`。快捷方式直接指向该 EXE。Inno Setup 的 `[Files]` 段定义由安装器安装的文件，因此当前脚本没有把 Python、`.py`、`probes/`、探针 PowerShell 文件或采样 JSON 放入用户安装目录。[Inno Setup `[Files]` 说明](https://jrsoftware.org/ishelp/topic_filessection.htm)

桌面项目目标为 .NET Framework 4.8（`TargetFramework=net48`），并有 `App.config` 的 .NET Framework 启动声明。安装脚本检查 64 位注册表中的 .NET Framework `Release` 值至少为 `528040`；不满足时提示用户安装框架后重试。脚本没有链式下载或安装 .NET Framework，因此目标电脑必须预先具备 4.8 或兼容的较新版本。Microsoft 说明 .NET Framework 4.x 是就地更新系列，并介绍了随应用安装框架的可选部署方式；本项目目前没有配置该安装方式。[Microsoft .NET Framework 部署指南](https://learn.microsoft.com/en-us/dotnet/framework/deployment/deployment-guide-for-developers)

## 尚需验收的打包细节

桌面项目还声明了 `System.Runtime.WindowsRuntime` NuGet 引用，而 Inno Setup 文件清单只显式选择上述三项。NuGet 页面说明该包提供 Windows Runtime 互操作扩展；API 文档将 `WindowsRuntimeSystemExtensions` 标在 `System.Runtime.WindowsRuntime.dll` 中。[NuGet 包说明](https://www.nuget.org/packages/System.Runtime.WindowsRuntime/4.7.0) · [Microsoft API 文档](https://learn.microsoft.com/en-us/dotnet/api/system.windowsruntimesystemextensions?view=netframework-4.8)

本次本机发布目录实际包含 EXE、配置文件、PDB 和图标，没有单独的 `System.Runtime.WindowsRuntime.dll`；当前打包 Windows 的 .NET Framework GAC 中能找到该程序集。安装器因此依赖目标系统提供此框架程序集。正式分发前仍应在干净 Windows 环境验证该程序集可解析，并检查安装后首次启动、WPF 界面、蓝牙状态读取和歌词查询。不要仅凭源项目或安装脚本推断所有传递依赖都已进入安装包。目前工作区没有保留 `artifacts/app` 发布目录；本地 `artifacts/installer/LyricConnector-Setup-0.1.0-x64.exe` 是未签名测试产物，路径仅对应当前工作区，已被 Git 忽略，尚未完成安装和运行验收。

## 资料来源

- [Inno Setup `[Files]` section](https://jrsoftware.org/ishelp/topic_filessection.htm)
- [Microsoft .NET Framework deployment guide for developers](https://learn.microsoft.com/en-us/dotnet/framework/deployment/deployment-guide-for-developers)
- [Microsoft .NET Framework deployment](https://learn.microsoft.com/en-us/dotnet/framework/deployment/)
- [NuGet: `System.Runtime.WindowsRuntime` 4.7.0](https://www.nuget.org/packages/System.Runtime.WindowsRuntime/4.7.0)
