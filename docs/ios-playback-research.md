# iPhone 播放状态读取边界调研

调研日期：2026-10-13。范围：公开 iOS API、Apple Media Service（AMS）、Apple Notification Center Service（ANCS）与 Mac AirPlay；未做实机实验。当前项目未发现本地 AGENTS.md；按会话指令仅写 docs 文档，WSL 下未运行构建。

## 结论

普通 iOS 陪伴 App 不能把 `MPNowPlayingInfoCenter` 当成读取网易云、QQ 音乐、Spotify 等其他 App 播放状态的公共入口；其文档合同是设置本 App 播放内容的信息。`systemMusicPlayer` 的“system”指 Music App，不是所有播放器。这个结论来自接口的官方职责边界，并非断言任何私有实现永远不可访问。[MPNowPlayingInfoCenter](https://developer.apple.com/documentation/mediaplayer/mpnowplayinginfocenter)、[systemMusicPlayer](https://developer.apple.com/documentation/mediaplayer/mpmusicplayercontroller/systemmusicplayer)

AMS 提供适合外部配件的播放元数据与时间信息，值得先做可行性验证；能否用指定桌面蓝牙栈直接连通 iPhone，仍需验证连接角色、权限和驱动。已公开的厂商示例证明的是开发板配件路径，不证明任意桌面原生 API 都能照搬。[Apple AMS Introduction](https://developer.apple.com/library/archive/documentation/CoreBluetooth/Reference/AppleMediaService_Reference/Introduction/Introduction.html)、[Nordic Peripheral AMS client](https://nrfconnectdocs.nordicsemi.com/ncs/latest/nrf/samples/bluetooth/peripheral_ams_client/README.html)

## iOS 公共 API

| 接口 | 官方职责 | 本项目判断 |
| --- | --- | --- |
| `MPMusicPlayerController.systemMusicPlayer` | 与 Music App 共享当前曲目、播放状态等 | Apple Music 专用候选；不读取网易云/QQ/Spotify |
| `applicationMusicPlayer` / `applicationQueuePlayer` | 在自身 App 播放，不感知 Music App 的当前曲目 | 只能用于自行接管播放 |
| `MPNowPlayingInfoCenter` | 向锁屏、控制中心、配件发布本 App 的播放信息 | 不能据此承诺跨 App 监听 |
| `MusicKit.SystemMusicPlayer` | 控制 Music App 的状态 | Apple Music 专用候选 |

表格来源：[MPMusicPlayerController](https://developer.apple.com/documentation/mediaplayer/mpmusicplayercontroller)、[MPNowPlayingInfoCenter](https://developer.apple.com/documentation/mediaplayer/mpnowplayinginfocenter)、[MusicKit SystemMusicPlayer](https://developer.apple.com/documentation/musickit/systemmusicplayer)。

MediaPlayer 的 `currentPlaybackTime` 表示当前播放头位置；MusicKit 的 `playbackTime` 也提供当前条目的秒数。这些时间属于对应播放器，不能据此扩大为全局时间读取。MusicKit 请求访问用户音乐数据需用户授权。[currentPlaybackTime](https://developer.apple.com/documentation/mediaplayer/mpmediaplayback/currentplaybacktime)、[playbackTime](https://developer.apple.com/documentation/musickit/musicplayer/playbacktime)、[MusicAuthorization](https://developer.apple.com/documentation/musickit/musicauthorization)

还要单独解决陪伴 App 的后台存活：Music App 可继续播放不等于观察者可无限执行。普通 App 进入后台后会挂起，后台能力仅对相应业务事件提供执行机会，不能把前台轮询加 WebSocket 默认视作锁屏后长期可靠。[Preparing your UI to run in the background](https://developer.apple.com/documentation/uikit/preparing-your-ui-to-run-in-the-background)

## AMS：协议与实现可达性

Apple 将 AMS 定义为 iOS 设备通过 BLE 向配件提供的服务，依赖 GATT，没有文档要求把音频输出切换到该配件。由此可推断适合“手机/耳机出声，桌面显示”设计，但 AirPods 并存和锁屏稳定性仍需实测。[AMS Introduction](https://developer.apple.com/library/archive/documentation/CoreBluetooth/Reference/AppleMediaService_Reference/Introduction/Introduction.html)

协议的 Player 表示当前活动媒体 App；Track 提供当前曲目信息。客户端订阅 Entity Update 后再登记所需属性；AMS 可能随时发布或撤销，应处理 Service Changed；不能承诺服务永远在线。[AMS Specification](https://developer.apple.com/library/archive/documentation/CoreBluetooth/Reference/AppleMediaService_Reference/Specification/Specification.html)

Track 可取歌手、专辑、标题、时长。Player PlaybackInfo 是 `state,rate,elapsed`；elapsed 是发送时的秒数，并非逐秒通知。桌面可按接收时刻外推：`position = elapsed + (now - receivedAt) × rate`。暂停、拖动和换曲后的事件完整性与时延仍应按播放器实测。协议没有歌词字段。[AMS Reference](https://developer.apple.com/library/archive/documentation/CoreBluetooth/Reference/AppleMediaService_Reference/Appendix/Appendix.html)

### 连接角色是首个技术门槛

GAP central/peripheral 与 GATT client/server 是两组不同角色，不应混用。Nordic 官方示例是 **配件广播为 GAP peripheral，iPhone 主动连接；配件同时是 GATT client，读取 iPhone 的 AMS server**。示例让用户从 iOS 蓝牙设置选择开发板并配对，开发板通过 UART 打印媒体详情。这支持 USB 串口桥候选，而不是“扫描 iPhone 后连接”已可行的结论。[Nordic Peripheral AMS client](https://nrfconnectdocs.nordicsemi.com/ncs/latest/nrf/samples/bluetooth/peripheral_ams_client/README.html)

Adafruit 自身 ESP32-S3 示例使用包含 AMS UUID 的 `SolicitServicesAdvertisement` 进行广播，等待 iPhone 连入，再执行配对并访问远端 AMS。其自有 CircuitPython 库还暴露 `elapsed_time` 与 `playback_rate`。这些是厂商实现证据，不能扩展成 Apple 对所有桌面 OS 的保证。[ESP32-S3 示例代码](https://learn.adafruit.com/esp32-s3-ble-ios-media-controller/code-the-media-controller)、[Adafruit AMS library 使用说明](https://learn.adafruit.com/now-playing-bluetooth-apple-media-service-display/apple-music-service)

因此桌面直连验证必须覆盖：能否正确发送服务征询广播、接受 iPhone 作为 central 连入、在同一连接访问远端 GATT server、完成配对及持久绑定、订阅并恢复服务。只有普通 BLE 扫描和 GATT client API，尚不足以证明整条链路可用。若平台限制这些角色组合，可另选开发板 → USB 串口 → 桌面的实现；这是明确的另一架构，需单独选定，而不是悄悄留作内部备用分支。

### 已知与待验证边界

| 问题 | 状态 | 证据边界 |
| --- | --- | --- |
| 歌名/歌手/时长/时间锚点 | 协议支持 | AMS 定义；App 提供值的完整性需测试 |
| 配对与绑定 | 厂商示例有实现 | Apple AMS 归档未写出完整安全合同；按实际 ATT/SMP 行为验证 |
| Apple Music | 厂商示例有验证流程 | 指定 iOS 与当前版本仍需回归 |
| Spotify | Adafruit 文档列为适用 App | 并非所有版本、字段与场景的测试保证 |
| 网易云/QQ 音乐 | 未验证 | 本次未找到官方 App 级 AMS 兼容承诺 |
| iPhone 锁屏、AirPods 并存 | 未验证 | 协议无音频路由依赖，不等于场景经过验证 |
| 任意桌面直连 iPhone | 未验证 | 必须验证 GAP peripheral + 远端 GATT client 组合 |

本表的协议依据为前述 Apple 文档，硬件实现依据为前述 Nordic/Adafruit 文档；“未验证”指本次没有相应一手保证或实机结果。

## ANCS 与 AirPlay

ANCS 的合同是访问 iOS 通知中心通知，支持到达/修改/移除事件与通知属性，且特征访问需要授权。锁屏媒体控件不是它的播放器时间轴合同；不应用 ANCS 代替 AMS 获取持续进度。[ANCS Introduction](https://developer.apple.com/library/archive/documentation/CoreBluetooth/Reference/AppleNotificationCenterServiceSpecification/Introduction/Introduction.html)、[ANCS Specification](https://developer.apple.com/library/archive/documentation/CoreBluetooth/Reference/AppleNotificationCenterServiceSpecification/Specification/Specification.html)

Mac AirPlay Receiver 是官方可用的音频输出或画面镜像路线：用户在 iPhone 选择 Mac 作为播放目标，Mac 打开接收器并设置允许用户。标准要求包括 iOS 14/iPhone 7 起，macOS Monterey 12 起及相应支持机型。它涉及音频路由或屏幕镜像，官方用户指南未给出供第三方桌面歌词窗口读取时间轴的接口；不能把开启接收器直接等同于拿到可编程元数据。[AirPlay to Mac 要求](https://support.apple.com/en-hk/108046)、[iPhone AirPlay 操作](https://support.apple.com/guide/iphone/stream-video-and-audio-from-your-iphone-iphd668e80e6/ios)

## 建议的实机验收

先验收播放状态链路，再开始桌面歌词 UI。针对 Apple Music、网易云、QQ 音乐、Spotify，各跑同一清单：曲目元数据、暂停恢复、拖动、切歌、锁屏 30 分钟、蓝牙断线恢复、AirPods 输出。记录手机与 App 版本、硬件/桌面 OS、接收事件时间、原始字段与可见歌词误差。不要仅凭歌名通知就宣告时间同步完成。

首个验收目标是拿到可靠的曲目及时间锚点；其次确定目标 App 的覆盖范围和锁屏持续性。AMS 只负责状态，歌词获取、版本匹配和逐字时间轴由独立方案解决。
