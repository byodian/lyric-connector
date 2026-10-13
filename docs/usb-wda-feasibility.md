# iPhone-use 的 USB 原理与歌词同步适用性

调研日期：2026-10-13。只读检查的 iPhone-use 提交：`08ed5b00965025120f572a7635aef6698f05cf1a`。该提交固定 WebDriverAgent（WDA）16.14.0 / `d17782422d55ff1e5e0ceb74eb1fd509cc0c35b6`，USB 依赖 appium-ios-device 3.1.24。本次没有连接 iPhone，没有安装或运行其脚本。

## 1. 结论

**选型结论：不采用 USB / WDA 界面采集方案。** 用户已明确桌面独立获取和显示歌词、桌面控制显示方式，手机只提供歌曲名称与进度。当前实现依赖界面采集且没有专用的全局播放状态接口，不能作为本项目的状态数据源。

源码曾支持评估以下技术用法；它们均已从本项目候选中排除，后文仅保留能力与限制的分析：

- 桌面接收手机屏幕图像，只裁剪并显示歌词区域。保留原 App 的歌词画面与高亮，不必重新获取歌词文件。
- 从界面控件取得歌名、艺人及可见进度，桌面匹配 LRC 后重新渲染。进度是否可读取、精度与更新频率需要逐个 App 验证。

如果要求手机锁屏、切到其他 App 后桌面依然长期自动同步，不能把 iPhone-use 当前能力直接视为满足条件。其现有 READY 检查明确拒绝锁屏，界面读取依赖当前前台视图。[READY 实现](https://github.com/zhongerxin/iPhone-use/blob/08ed5b00965025120f572a7635aef6698f05cf1a/server/iphone_use.py#L413-L428)、[界面读取实现](https://github.com/zhongerxin/iPhone-use/blob/08ed5b00965025120f572a7635aef6698f05cf1a/server/wda_controller.py#L225-L312)

## 2. USB 操作的真实链路

```mermaid
flowchart LR
    D[桌面 Python 客户端] --> H[本机 HTTP 127.0.0.1:18100]
    H --> F[Node USB 端口转发]
    F --> U[appium-ios-device / usbmux]
    U --> W[iPhone WDA HTTP 服务 :8100]
    W --> X[XCTest UI 自动化]
    X --> A[音乐 App 当前界面]
    W --> M[独立 MJPEG 图像流 :9100]
    M --> V[桌面画面接收]
```

### USB 提供传输，WDA 提供执行能力

`tooling/forward.mjs` 在本机监听 18100，调用 `utilities.connectPort(udid, 8100)` 连接手机端端口，再双向转发字节。桌面像调用本地 HTTP 服务一样调用手机上的 WDA。这个模块本身只转发连接，不负责识别媒体或获取 App 数据。[USB 转发源码](https://github.com/zhongerxin/iPhone-use/blob/08ed5b00965025120f572a7635aef6698f05cf1a/tooling/forward.mjs#L4-L36)

appium-ios-device 的维护者将其定位为通过 usbmuxd 与 iOS 设备通信的库；它还提供设备服务与测试启动工具。USB 通道能访问哪些内容，取决于手机端对应服务提供哪些接口。[appium-ios-device](https://github.com/appium/appium-ios-device)

WDA 是安装、签名并通过测试机制启动的手机端服务，链接 XCTest，能查询 UI、点击、滚动和启动 App。不能把这种开发测试通道与普通第三方 iOS App 的公共媒体权限混为一谈；USB 连接本身也不赋予读取任意 App 内存或完整歌词库的能力。[WDA 维护者说明](https://github.com/appium/WebDriverAgent)

### 两种观察方式

**控件树**：`wda_controller.py` 请求 `/source?format=xml...`，解析元素的 `label`、`name`、`value`、类型与坐标；这是 UI / accessibility 快照。只有 App 实际向自动化树暴露的信息才能直接读取，不保证包含完整歌词、当前高亮行、逐字时间戳或毫秒级进度。[控件树代码](https://github.com/zhongerxin/iPhone-use/blob/08ed5b00965025120f572a7635aef6698f05cf1a/server/wda_controller.py#L233-L272)

**屏幕流**：`screen-stream.mjs` 通过 USB 连接独立的 9100 端口，请求 MJPEG 流并转发给桌面；它传递屏幕图像，没有提取语义化歌词或转发音频的逻辑。裁剪歌词画面是可据此设计的功能，尚未在本项目实测。[屏幕流代码](https://github.com/zhongerxin/iPhone-use/blob/08ed5b00965025120f572a7635aef6698f05cf1a/tooling/screen-stream.mjs#L7-L57)

进一步检查固定 WDA 提交的 `WebDriverAgentLib` / `PrivateHeaders`，未找到 `nowplaying`、`MediaRemote`、`lyrics`、`playbackTime` 等专用媒体读取实现；`FBDebugCommands` 的 source 入口读取活动 App 快照，`FBMjpegServer` 则循环截图并编码 JPEG。这个结论限定于检查的代码，不扩大为「任何 USB 服务或未来扩展均不可能读取媒体状态」。[固定 WDA source 入口](https://github.com/appium/WebDriverAgent/blob/d17782422d55ff1e5e0ceb74eb1fd509cc0c35b6/WebDriverAgentLib/Commands/FBDebugCommands.m#L24-L82)、[固定 WDA 图像流](https://github.com/appium/WebDriverAgent/blob/d17782422d55ff1e5e0ceb74eb1fd509cc0c35b6/WebDriverAgentLib/Utilities/FBMjpegServer.m)

WDA 本身有锁定状态接口，锁屏 UI 若暴露曲目 / 时间属性也可作为另外的实验；但这既未得到持续时间轴保证，也不是 iPhone-use 当前 READY 支持的用法。因此这里区分「现有包装层拒绝锁屏」与「任何锁屏数据都绝对不可采集」。

## 3. 已评估的用法与限制

| 目标 | 复用路径 | 适用边界 |
| --- | --- | --- |
| 电脑看到手机原有滚动歌词 | MJPEG → 歌词区域裁剪 → 置顶窗口 | 手机歌词页须可见；保留页面背景 / 字体；采集时延待测 |
| 电脑用自定义字体显示歌词 | WDA 控件查询 → 解析曲目与进度 → 本地时钟 → LRC | 必须验证 App 暴露进度；歌词需另取；秒级显示会限制精度 |
| 手机锁屏 / 任意前台 App 时也自动同步 | 需要独立媒体数据来源 | 现有 WDA 包装层不足以证明；继续验证 AMS 或播放器官方 API |

以上前两项是从源码能力推导的方案，不是已有产品效果声明。

### A. 只投放歌词区域：适合快速验证体验

用户在手机打开歌词页，桌面收到图像后按用户选定区域裁剪显示。无需 OCR，也无需重建高亮时序；手机上的字体、翻译或逐字效果若已出现在画面中，就作为图像一起显示。

代价是手机要保持相应页面，切到其他 App 后画面也随之变化；固定裁剪区域会受到旋转、布局变化和弹窗影响。MJPEG 没有在这份脚本中携带歌曲播放锚点，不能承诺音画零延迟。字体和透明背景也无法像原生桌面歌词一样自由控制。这些是所选显示方式的工程限制。

此路线是图像流展示，设计上不需要切换音乐音频输出；AirPods 并用及目标 App 的画面采集可用性仍需实测。

### B. 提取曲目与进度：适合自定义桌面歌词原型

先在播放页面检查 UI 树：歌名、艺人、已播放时长、总时长、播放 / 暂停状态是否存在，以及拖动后是否及时更新。时间若只有 `mm:ss`，应按秒级观测处理，不能声称获得毫秒级真实播放位置。进度滑块若只给百分比，也需要确认它的范围、时长和读数精度。

成功后使用确定性的 App 适配器解析这些字段，转为统一播放快照；可先以 1–2 秒采样作为实验参数，记录请求耗时及控制事件发现延迟，再决定采样频率。正常推进由桌面单调时钟完成，不用每帧重新获取完整 XML。

没有控件文本时，可单独评估截图 OCR 是否满足要求。OCR 是额外的识别模块，不是 iPhone-use 已实现的功能；无完整时间信息时，不应默默推算出不存在的精度。也不能从目前可见几行歌词还原整首歌词时间轴。

持续歌词同步不需要语言模型逐帧识别或 MCP 工具往返。复用 USB / WDA 通道与状态管理思路即可，主路径用普通程序采样和解析。

### C. 单独复用 USB 传输层

如果以后编写 Apple Music 专用 iOS 伴随 App，也可借鉴端口转发，让伴随 App 提供自己的播放状态服务。此时媒体能力来自伴随 App 的 MusicKit / MediaPlayer 接口，后台存活仍需解决；把局域网改为 USB 不会自动解决后台挂起或跨 App 权限问题。相关 API 边界见 [iOS 播放状态调研](ios-playback-research.md)。

## 4. 安装与桌面平台边界

iPhone-use 的现有 setup 明确要求 macOS 和完整 Xcode 来准备、签名、部署并启动 WDA；代码在非 macOS 平台拒绝该启动流程。它不是开箱即用的 Windows USB 歌词工具。[项目 README](https://github.com/zhongerxin/iPhone-use/blob/08ed5b00965025120f572a7635aef6698f05cf1a/README.md)、[setup 平台检查](https://github.com/zhongerxin/iPhone-use/blob/08ed5b00965025120f572a7635aef6698f05cf1a/server/wda_setup.py#L691-L704)

但不能据此说 Windows / Linux 永远不能运行 WDA 自动化。Appium XCUITest Driver 12.16 官方文档给出有限支持：真机 iOS/tvOS 18+，准备 appium-ios-remotexpc 5.1+ 的平台依赖与 RemoteXPC tunnel，指定设备和系统版本，使用预安装 WDA 或外部运行的 WDA。它解决的是另一套启动 / 会话接入路径，尚未接入此项目。[Appium 非 macOS 主机指南](https://appium.github.io/appium-xcuitest-driver/12.16/guides/non-macos-hosts/)

因此 Windows 方案要分别验收「签名与首次部署」「后续启动测试 Runner」「USB HTTP / MJPEG 连接」三个阶段。只验证端口转发成功，不等于能在设备重启后重新启动 WDA。WSL 的 USB 设备可见性也不能从原生 Windows 的支持直接推导。

## 5. 当前项目决策

**排除屏幕裁剪、OCR 与 WDA UI 字段提取；优先验证 AMS 或播放器官方播放状态 API。** 本项目要解决的是结构化状态获取，桌面负责歌词内容、时间轴、渲染和显示控制。

USB 端口转发的传输思路可供后续专用状态服务参考，但当前未找到该仓库提供的通用歌曲 / 进度 USB 接口。必须先验证状态生产者，再决定 USB 是否适合作为传输；不实施 WDA 歌词窗口原型。

前面的「AMS 配件 → USB → 桌面」与这里的 USB 是不同架构：前者 USB 连接电脑和 BLE 配件；这里 USB 直接连接电脑和 iPhone，手机需运行测试 Runner。

### 后续工作

后续状态探针与验收以 [主技术方案](technical-plan.md) 为准：获取真实曲目与播放锚点，验证暂停、拖动、切歌，以及不打开手机歌词页时的独立同步能力。本文不再作为 WDA 实施计划。

本次交付为源码调研与方案更新，未运行安装、手机自动化或前端构建。
