# Windows 桌面歌词首版

日期：2026-10-13。首版已实现并在当前 Windows / iPhone / YouTube Music 组合上运行。真实手机状态驱动《夜的第七章》 / Jay Chou 的歌词查询，LRCLIB 返回 43 行同步歌词，原生悬浮窗口已显示当前行和下一行。

手机负责播放与音频输出；桌面负责歌词来源、匹配、时间轴、显示设置和基本播放控制。程序从 AMS 读取媒体状态，并通过可写的 Remote Command 特性发送播放、暂停、上一首和下一首操作；命令是否被当前播放器执行仍取决于 iPhone 与播放器的支持情况。

## 运行

在项目根目录的 WSL 终端运行：

```sh
python3 desktop/run.py
```

也可用 Windows 的 Python 启动同一个文件。需要 64 位 Windows PowerShell 5.1、系统 WPF / .NET Framework、可用的 Windows BLE 和已建立蓝牙关系的 iPhone。当前机器具备这些条件；无需安装额外 Python 包或 .NET SDK。

首次启动尝试选择名称为 `iPhone` 的设备；之后优先选择上次保存的设备 ID。显示名不同可以指定：

```sh
python3 desktop/run.py --device-name 'iPhone 17'
```

若没有自动连接，在控制窗口点击“刷新”，选择手机，再点击“连接”。连接错误或断开后需要手动重新连接。本版没有自动重连。

关闭控制窗口会隐藏到系统托盘，歌词悬浮窗、查询和蓝牙订阅继续运行；单击托盘图标或双击悬浮歌词可重新打开控制窗口。右键托盘图标并选择“退出 Lyric Connector”才会取消查询与蓝牙订阅并关闭程序。暂时不要同时运行多个桌面实例或 AMS 探针，以免多个客户端同时选择同一属性读取。

## 使用

- 精确歌词查询命中且标题、歌手、时长符合时自动载入。没有精确结果时显示候选，由用户确认；不会自动选择搜索结果第一条。
- YouTube Music 的视频标题与频道名可在桌面的检索框中修正；修改这些框只改变歌词检索，不改变手机播放状态。
- “导入 LRC”读取 UTF-8 的 `.lrc` / `.txt`，并为当前曲目保存映射。纯文本歌词没有时间轴，可以在“查看歌词文本”窗口阅读；不会伪造同步进度。
- 悬浮窗显示当前行、下一行和短进度条；播放控制图标在鼠标悬停时显示，离开后延迟收起。未开启鼠标穿透时可以拖动，右下角可以调整大小。
- 字体、字号、背景透明度、置顶和鼠标穿透均在桌面控制。`Ctrl + Alt + L` 切换穿透；如果快捷键被其他程序占用，可直接在控制窗口取消穿透。
- 桌面设置可显示或隐藏悬浮歌词文字。背景透明度可设为 0；程序保留独立的低至不可见鼠标命中层，并延迟收起播放控件，方便将指针移到按钮上。开启鼠标穿透时需先关闭穿透才能操作。
- 当前行和下一行可分别设置字体颜色，输入 `#RRGGBB` 后点击“应用”。设置随窗口其他显示选项保存。
- 歌词偏移以 0.25 秒调整。正值让歌词提前，负值让歌词延后。本版偏移为全局设置，尚未为每首歌分别保存。

设置与歌词映射保存在 Windows 的 `%LOCALAPPDATA%\LyricConnector`：`settings.json` 保存显示设置，`lyrics` 保存以曲目元数据签名区分的 JSON。标题、歌手、专辑以及按秒取整的时长构成签名；AMS 不提供统一目录歌曲 ID，所以这不是跨播放器的全局歌曲身份。

## 混合标题识别

桌面在查询前提取视频标题中的歌名与歌手，清理 KTV、字幕、Official MV、OST、Cover 等附加内容，识别《歌名》、明确的歌手与歌名分隔，以及拼音标题中的中文别名。以下实际标题格式已纳入定向检查：

| 手机标题 / 艺人 | 查询歌名 / 歌手 |
| --- | --- |
| `止戰之殤 \| KTV 動態字幕 \| … \| 原唱：周杰倫` / Capt. Lilia Iwasko | 止戰之殤 / 周杰倫 |
| `周二珂 - 《走在冷风中》` / SolarMotion20 | 走在冷风中 / 周二珂 |
| `[Meteor Garden OST] Ni Yao De Ai (你要的爱) - Penny Tai Cover …` / DARA TV | 你要的爱 / Penny Tai |

提取结果显示在标题与检索框中，悬停标题 / 歌手可查看手机原始字段。识别后的标题直接进行候选查询；歌手条件未命中时扩大为仅按歌名查询，避免频道名和不同语言的歌手名阻塞结果。候选优先显示同步歌词，再按与手机时长的差异排序。

本版采用本地规则，不使用远程模型判断歌名。没有识别依据的普通歌名保持原样；例如 `Love - Hate` / Some Band 不会仅因连字符被拆成歌手与歌名。推断结果允许用户修正。翻唱和不同版本只提供候选，不自动采用原唱歌词；选定后为手机原始曲目签名保存映射。

原生 Windows HTTP 已验证上述《止戰之殤》混合标题能够找到 20 个候选。候选数量不代表版本或音频对齐已验证。

## 网易云 AMS 字段处理

iPhone 网易云实测《傻女》时，AMS 的标题通知随播放内容变化；同一首歌的艺人字段保持为“陈慧娴/傻女”，专辑与时长也保持不变，播放位置持续前进。手机确认实际曲名是《傻女》。桌面因此按网易云的这类组合字段取出歌名和艺人，并忽略动态标题通知；艺人组合字段变化时仍会触发真正的切歌识别。健康检查收到无法解析的时长值时会跳过该次时长比较，不清除已取得的播放状态。

修复后的 40 秒实时检查在另一首网易云歌曲上取得播放快照，期间没有再次进入切歌等待态。该轮歌词服务返回 429，因此没有验证在线同步歌词；播放控制也尚未单独验收。AMS 的实体属性编号和通知格式见 [Apple AMS 参考](https://developer.apple.com/library/archive/documentation/CoreBluetooth/Reference/AppleMediaService_Reference/Appendix/Appendix.html)。

## 窗口与模块

悬浮窗采用深紫背景、淡粉当前行、灰紫下一行；悬停时展示控制条，包括提前量微调、上一首、播放/暂停、下一首，以及采用 Apple Music 歌词气泡引号（quote bubble）图标的“查看歌词全文”按钮。控制窗采用浅色背景和中文系统字体。显示设置即时生效，退出时持久化。

![Lyric Connector 桌面图标](desktop-icon.png)

图标把音符、歌词行和连接节点合在一个简洁标记里，同时提供 [SVG 矢量图](../desktop/assets/lyric-connector.svg)和包含 7 种尺寸的 [Windows 图标文件](../desktop/assets/lyric-connector.ico)。控制窗口及“查看歌词文本”窗口使用同一图标。重新生成图标与预览：

```sh
python3 desktop/run.py --render-icon
```

![真实手机与歌词查询状态](desktop-control.png)

![原生两行悬浮歌词](desktop-overlay.png)

截图来自程序自己的 WPF 渲染区域，没有采集手机画面或桌面上的其他窗口。

| 文件 | 责任 |
| --- | --- |
| [Source.cs](../desktop/Source.cs) | Windows BLE、AMS 订阅、完整属性读取、状态校准、切歌过渡 |
| [Core.cs](../desktop/Core.cs) | 纯内存播放模型、单调时钟外推、LRC 解析和行定位 |
| [SongQuery.cs](../desktop/SongQuery.cs) | 纯内存视频标题清理、歌名与歌手提取 |
| [LyricsStore.cs](../desktop/LyricsStore.cs) | LRCLIB HTTP、曲目映射缓存、本地文件输入 |
| [Windows.cs](../desktop/Windows.cs) | 原生控制窗、悬浮窗、快捷键、设置、异步查询的代次检查 |
| [run.py](../desktop/run.py) | 将小型 C# 源码送入原生 Windows PowerShell 并启动 |
| [Tests.cs](../desktop/Tests.cs) | 时间轴、文件缓存、真实通知日志回放和可选网络检查 |

收到曲目元数据变化时，源立即推进曲目代次并清除旧位置与旧歌词。等待至少一秒没有元数据通知后，完整读取标题、歌手、专辑、时长和播放信息，并复读标题、歌手；读取期间出现冲突则重新采样。查询结果必须仍属于当前代次，才能更新窗口与保存映射。

稳定播放使用单调时钟以 50 ms 的 UI 节拍推进行定位；暂停冻结位置，拖动事件替换锚点。源每 15 秒读取标题与播放信息进行校准。断开后清除时间轴，不继续滚动歌词。

首次测试曾在后续 `Playback.Parse` 中出现数字格式错误；持续运行时已捕获 `0,,`，即手机暂未提供速率和位置。无效数字状态处理为“等待手机提供有效进度”：清除时间轴，保留连接，重新取得完整状态。后续真实运行已验证收到 `0,,` 后取得新快照并恢复播放状态。必需属性返回 AMS `0xA2`（属性为空）时亦等待；该错误的含义由 [Apple AMS 错误码](https://developer.apple.com/library/archive/documentation/CoreBluetooth/Reference/AppleMediaService_Reference/Specification/Specification.html) 定义。记录 `AMS_INVALID_STATE` 与返回值，便于继续定位具体手机场景。由于 AMS 多属性读取不是原子操作，极快切歌的一致性仍需持续实测。

## 验证记录

```sh
python3 desktop/run.py --test
python3 desktop/run.py --check-lyrics
python3 desktop/run.py --smoke-seconds 30
```

- 定向检查通过 85 个断言：单调时钟、暂停、拖动、旧通知、旧代次、断开、网易云组合字段解析与动态标题通知、LRC 多时间戳与偏移、无效数值（包括实际收到的 `0,,`）、UTF-8 导入、缓存往返、纯文本歌词保留、歌词颜色与悬浮歌词窗初始化、10 个混合 / 普通标题样例及真实 42 条手机日志的切歌回放。回放验证模型失效行为，不代替 BLE 采样一致性验证。
- 原生 Windows HTTP 检索《夜的第七章》 / Jay Chou 返回 13 个带时间轴的候选，确认 C# 网络和歌词反序列化路径可用。该检查不替用户选定候选。
- 原生窗口取得真实手机快照，收到播放、暂停与切歌更新。歌词完整链路的一轮 30 秒测试返回 `snapshot=True timedLyrics=True`，载入并渲染 43 行歌词，自动退出正常。标题提取更新后另一轮测试取得《世间美好与你环环相扣》的真实状态，歌词服务当时返回 503；截图展示该轮状态，不把服务错误作为歌词匹配成功。
- 曾发现关闭事件内重复调用 `Close` 的异常；改为异步清理完成后由 Dispatcher 排队关闭，复测退出正常。
- 未运行前端构建。本程序通过系统 C# 编译器载入小型原生源文件，不执行前端构建或打包流程。

当前尚未测量歌词相对实际音频的误差，不能由“窗口正常滚动”推导音频对齐达标。锁屏、AirPods 并用、蓝牙断开后重连、休眠恢复、其他播放器、逐字歌词和安装包仍未验收。LRCLIB 实测出现间歇性 503，服务恢复后可在窗口重新搜索，不进行无限重试。覆盖率与版本准确性依赖具体歌曲；视频、翻唱、Live 等版本需核对，缺失歌词时可以导入本地文件。

协议与歌词 API 依据：[Apple AMS 属性参考](https://developer.apple.com/library/archive/documentation/CoreBluetooth/Reference/AppleMediaService_Reference/Appendix/Appendix.html)、[LRCLIB API](https://lrclib.net/docs)。
