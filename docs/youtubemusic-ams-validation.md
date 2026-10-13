# Windows / iPhone / YouTube Music 的 AMS 实测

日期：2026-10-13。Windows 宿主通过原生 WinRT 连接用户已建立蓝牙关系的 iPhone。WSL 仅用于运行入口与保存结果。没有使用 WDA、手机界面、截图或 OCR，没有发送播放控制命令，也没有切换音频输出。

## 已验证结论

**Windows 能通过这部 iPhone 的 AMS 读取 YouTube Music 的真实歌名、艺人、时长、播放状态、速率和进度，并收到暂停、恢复、进度跳转和切歌通知。** 桌面歌词首版已继续接通《夜的第七章》的真实状态与 43 行歌词，见 [桌面 MVP](desktop-mvp.md)；实际音频对齐、锁屏与蓝牙重连仍待验证。

第一次取得完整属性的样本：

| 字段 | 实测值 |
| --- | --- |
| 播放器 | YouTube Music |
| 歌名 | 羁绊 |
| 艺人 | 许嵩 |
| 专辑 | 一片冰心在玉壶 电视剧影视原声带 |
| 时长 | 247.881 秒 |
| 播放状态 | `1`，正在播放 |
| 速率 | `1.0` |
| 初次完整读取位置 | 94.346 秒 |
| 末次读取位置 | 141.446 秒 |

证据为 [完整读取与 45 秒采集](iphone-youtubemusic-ams-controls.json)、[第二次启动后的 60 秒采集](iphone-youtubemusic-ams-followup.json)；后者再次读取相同播放器和歌曲，位置从 33.361 秒推进到 94.981 秒。两轮各收到一次初始播放信息通知，不能把这些结果当成暂停、拖动或切歌推送已经通过。

第一次样本的桌面单调时钟间隔为 47.159366 秒，手机进度增加 47.100 秒，外推比较残差约 -59 毫秒。这个数字包含两次 GATT 查询的时延差，只验证手机状态锚点与桌面时钟的一致性；**不是歌词与实际音频的误差测量**。计算摘要见 [状态时间比较](iphone-youtubemusic-ams-summary.json)。

## 手机操作与实时通知已验证

短采集窗口只捕获初始通知，未与用户操作对齐。随后改为 300 秒持续监听，在用户操作期间收到 **42 条真实 AMS 通知**。证据见 [完整实时采集](iphone-youtubemusic-ams-live.json)、[操作验证摘要](iphone-youtubemusic-controls-summary.json)。

| 场景 | 实测证据 | 判断 |
| --- | --- | --- |
| 暂停 | `0,0.0,200.255` | 暂停状态与停止速率到达 |
| 恢复 | 约 2.882 秒后收到 `1,1.0,200.288` | 恢复状态到达，暂停期间位置基本不变 |
| 拖动进度 | 下一次播放锚点比按原速率外推的位置多约 45.550 秒 | 可以识别进度跳转并重置桌面锚点 |
| 切歌 | 6 次标题变化，包括《說好的幸福呢》、周二珂《走在冷风中》、《夜的第七章》及翻唱视频 | 标题与艺人字段会更新 |
| 新曲目开始 | 位置归零后收到 `1,1.0,0.147` 等锚点 | 可以重新定位歌词时间轴 |

这些是通知接收时间与值的证据，未记录用户触摸手机的精确时间，因此没有测出端到端操作延迟。

实测发现两个必须纳入桌面实现的要求：

- **切歌字段分批到达。** 新标题到达时曾仍带上一首的 248 秒位置，稍后才收到归零和新时长。切歌应立即使旧位置失效，并等待新曲目状态稳定；不能把通知简单合并后直接显示新歌词。
- **YouTube Music 可能返回视频元数据。** 艺人曾为频道名 `SolarMotion20` / `DARA TV`，标题也包含歌手、OST 和 Cover 文案，专辑与队列属性有时缺失。歌词匹配必须区分官方曲目与视频 / 翻唱版本，并支持桌面人工选定映射。

验证命令：

```sh
python3 probes/check_ams_capture.py docs/iphone-youtubemusic-ams-live.json
```

验证器确认基本状态、暂停恢复、进度不连续和标题变化均有证据，退出码 0；对仅有初始通知的早期样本运行会退出 1，避免把“订阅成功”当成控制事件通过。它核验真实日志，不测试歌词渲染或真实音频同步。

## 连接与实现要点

1. 从宿主已配对 BLE 选择器中定向选取目标，发现 AMS UUID；不自行发送服务请求广播。
2. 以 `SharedReadAndWrite` 显式打开 AMS GATT 服务。未显式打开时曾出现 `AccessDenied`；本次共享打开后，特征发现、配置和读取均成功。这是本次实验的修复结果，不推广为所有访问拒绝的唯一原因。
3. 注册 Entity Update 通知，再写入 Player、Queue、Track 的属性登记。
4. 使用 Entity Attribute 的属性选择和完整读取取得初始状态；遇到截断通知时亦可读取完整值。此次仅登记通知没有得到全部初始元数据，不能假定新订阅总能收到完整快照。
5. WinRT 事件与缓冲区互操作由小型 C# 适配器处理，Python / PowerShell 负责宿主接入、采样和 JSON 日志。通过现有 .NET 与系统 WinMetadata 运行，无需安装第三方包。

公开 API 依据：[GATT 服务共享打开](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattdeviceservice.openasync?view=winrt-26100)、[通知订阅](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattcharacteristic.writeclientcharacteristicconfigurationdescriptorasync?view=winrt-26100)、[AMS 属性选择与读取](https://developer.apple.com/library/archive/documentation/CoreBluetooth/Reference/AppleMediaService_Reference/Specification/Specification.html)。

此前查到的 Windows `0x15` 服务请求广播限制仍成立，但此次成功的是既有设备关系上的定向 AMS 服务访问；它不需要应用发送该广播。不能将广播分支受限扩大为 Windows 全部 AMS 路线不可行。

Windows 端点名称曾从 `iPhone 17` 显示为 `iPhone`，配对选择器返回对象的 `Pairing.IsPaired` 也出现不一致。探针记录事实并以实际服务操作结果判断连通性。产品实现还需稳定设备选择与重连机制，不能永久依赖这个显示名。

## 歌词源的独立检查

对上述《羁绊》样本，[LRCLIB](https://lrclib.net) 精确查询返回 404，按歌名与艺人搜索返回空列表。证据见 [精确查询](single-song-lyrics-check.json)、[候选搜索](single-song-lyrics-search.json)。

因此当前已证明播放状态接入可用，但 **LRCLIB 未覆盖这首样本歌曲**。桌面歌词实现需要本地 LRC 或另一个明确选定的歌词源；不自动匹配同名的其他艺人，也不伪造时间轴。这一首样本不能推导 [LRCLIB](https://lrclib.net) 的整体中文覆盖率。

第二首样本《夜的第七章》 / Jay Chou 则返回多个带时间戳的候选，其中有时长 229 秒、43 个时间行的版本，与手机 228.821 秒相差约 0.179 秒。证据见 [第二首候选查询](second-song-lyrics-search.json)。候选也包含伴奏和不同专辑标记，尚未自动选定，也未核验与实际音频的版本和时间轴一致性。

## 复现与边界

在项目根目录的 WSL 中运行：

```sh
python3 probes/windows_bluetooth.py --probe ams --device-name 'iPhone' --listen-seconds 60
```

设备名应与能力探针实际枚举结果一致。AMS 探针的 JSON 报告输出到 stdout，`AMS_READY` 和事件摘要输出到 stderr，可在采集就绪后同步执行手机端测试操作。

代码入口：[Python 宿主桥](../probes/windows_bluetooth.py)、[AMS 采样](../probes/windows-ams.ps1)、[WinRT 异步处理](../probes/winrt.ps1)、[事件与缓冲区适配](../probes/NotificationCollector.cs)。写入仅限通知配置、属性登记与属性选择，未调用 Remote Command。

仍需验证：更多控制操作与快速连续切歌的稳定性和延迟；锁屏和切 App；蓝牙断开恢复；长属性截断；桌面歌词实际音频对齐。iOS / YouTube Music 版本尚未记录。已运行原生探针及 C# 互操作适配；未运行前端构建。
