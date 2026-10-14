# Lyric Connector

让 iPhone 继续播放音乐，由 Windows 桌面端读取歌曲和进度、独立查询同步歌词，并显示轻量悬浮歌词。桌面也可在悬浮窗中向手机发送播放控制；手机不需要打开歌词页面或安装配套 App。

## 背景

很多人用手机或耳机听歌，却希望歌词始终显示在电脑桌面。Lyric Connector 将播放和歌词显示分开：iPhone 负责音乐与音频，桌面端负责歌词匹配、时间轴、外观和播放控制。当前实现通过蓝牙 Apple Media Service（AMS）获取媒体状态，不读取手机屏幕或音频。

## 快速使用

下面是从源码启动的步骤，需要 Windows、64 位 Windows PowerShell 5.1、Python 3、可用的蓝牙 LE，以及已与 Windows 配对的 iPhone。安装后的 WPF EXE 不需要 Python 或 `probes/`；它需要 Windows/.NET Framework 4.8 及蓝牙环境。当前本地测试安装器尚未完成安装验收，且未签名，不作为正式发行版本。

1. 在 Windows 蓝牙设置中配对 iPhone，然后在手机 YouTube Music 或 Apple Music 中开始播放。
2. 在项目根目录启动程序：Windows PowerShell 中运行 `python desktop/run.py`；WSL 中运行 `python3 desktop/run.py`（需启用 Windows 程序互操作）。
3. 如果程序未自动连接，在桌面控制窗口点击“刷新”，选择 iPhone，再点击“连接”。检查歌词候选并选择正确版本；Apple Music 曲名与歌词库中文标题不一致时，可查看扩展候选或手动修改曲名后搜索。缺少在线歌词时可导入本地 LRC。

## 支持度与开发进度

| 项目 | 状态 | 当前情况 |
| --- | --- | --- |
| Windows + iPhone + YouTube Music | 已实现并实测 | 已验证歌曲信息、进度、暂停 / 恢复、拖动进度、切歌，以及上一首 / 播放暂停 / 下一首控制事件。 |
| Windows + iPhone + Apple Music | 状态、歌词与基本控制已实测 | 已读取曲目并查询到 69 行同步歌词；悬浮窗暂停 / 恢复和下一首已验证。总时长未提供时仍显示歌词和已播放时间，比例进度条隐藏。 |
| 桌面歌词悬浮窗 | 已实现 | 支持逐行歌词及当前行卡拉 OK 扫色、短进度条、悬停播放控件、双击打开桌面设置、控制窗口驻留系统托盘、字体 / 颜色 / 透明度设置、歌词偏移和本地 LRC 导入。 |
| iPhone 网易云音乐、QQ 音乐、Spotify 等 | 后续适配目标 | 尚未逐一验证或适配；支持情况取决于应用提供的媒体状态及控制接口。 |
| macOS / Linux 桌面 | 暂不支持 | 当前桌面端依赖 Windows WinRT 蓝牙和 WPF。WSL 仅可在 Windows 互操作可用时启动 Windows 程序。 |
| Windows 安装程序与签名 | 实现中 | 已生成未签名的本地 Inno Setup 测试包 `artifacts/installer/LyricConnector-Setup-0.1.0-x64.exe`；尚未安装验收。Microsoft Store MSIX 仍是首选签名路线，Partner Center 身份、正式图标和 Store 审核待完成。 |
| 自动更新 | 尚未实现 | 暂无自动更新机制。 |

当前已完成 Windows 桌面 MVP。下一阶段重点是逐个验证网易云音乐、QQ 音乐、Spotify 等其他常用 iPhone 播放器；Apple Music 已走通通用 AMS 状态、歌词和基本控制接入。

## 使用限制

- 其他播放器即使能在 iPhone 播放，也不代表一定向 Windows 暴露 AMS 状态或接受远程控制。
- 蓝牙断开后当前版本需要手动重新连接；锁屏、耳机并用、休眠恢复等场景尚未完成验收。
- 歌词来自 [LRCLIB 公网服务](https://lrclib.net) 或本地文件，覆盖率和版本准确性因歌曲而异。Apple Music 曲名与中文歌词条目不一致时会扩大专辑 / 歌手候选，但仍需核对翻唱或现场版本；纯文本歌词没有逐行时间轴。
- 已实现的播放位置来自播放器报告，歌词相对实际音频的误差尚未完成系统测量。

## 详细文档

[完整背景、安装与操作说明、工作原理、数据处理方式、限制和适配计划](docs/README.md)。

Windows 安装包的实施进度和验收步骤见[安装包方案](docs/installation-plan.md)，源码脚本与安装版的差异见[分发运行时依赖说明](docs/distribution-runtime-dependencies.md)。当前优先采用 Microsoft Store MSIX，需由项目所有者在 Partner Center 保留应用名称并配置私有测试受众；[MSIX 实施说明](docs/msix-store-packaging.md)列出后续步骤。SignPath Foundation 的资格和流程调查见[SignPath 专项核查](docs/signpath-path.md)，其他签名选项见[签名方案调研](docs/code-signing-options.md)。

更多实现细节与测试记录见[桌面 MVP](docs/desktop-mvp.md)、[技术方案](docs/technical-plan.md)和[YouTube Music AMS 实测](docs/youtubemusic-ams-validation.md)。
