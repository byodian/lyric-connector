# AMS 纯软件直连：Windows API 缺口与最小实验

日期：2026-10-13。范围：Microsoft WinRT 公开 API/官方样例、BlueZ 官方 D-Bus 文档。此次调研未安装、广播、配对或扫描设备；未运行构建。主代理已运行宿主只读能力探针：`lowEnergySupported=true`、`centralRoleSupported=true`、`peripheralRoleSupported=true`、`radioState=On`，同时明确 `amsConnectionVerified=false`。随后使用 `GetDeviceSelectorFromPairingState(true)` 加 `DeviceInformation.FindAllAsync` 枚举已配对 LE 注册对象，返回 Count=0。这只说明当前 WinRT 查询上下文中没有可供 A2 使用的已配对 LE 对象，不证明附近没有 iPhone、经典蓝牙无配对或所有连接路径均不可行。原始结果见 [宿主能力结果](current-host-bluetooth.json)。WSL 本身没有可直接使用的 USB/蓝牙控制器，与宿主支持 BLE 不矛盾。

## 判断

**后续实测更新：A2 的定向查询路线已在 Windows / iPhone / YouTube Music 上通过。** 用户连接手机后，AMS 服务发现、`SharedReadAndWrite` 打开、完整属性读取和暂停 / 恢复 / 拖动 / 切歌通知均成功；证据见 [媒体实测](youtubemusic-ams-validation.md)。以下关于 `0x15` 的限制针对主动服务征询广播分支，不能推广为整个 Windows AMS 不可行。

**“Windows 原生 WinRT 发 AMS 服务征询广播，再等待 iPhone 连接”存在已确认的 API 限制，不能继续按现成可行路线开发。** Microsoft 明确禁止 `BluetoothLEAdvertisementPublisher` 发布 128-bit Service Solicitation UUIDs（AD type `0x15`），正是 AMS 配件广播中的服务征询类型。存在 `ServiceSolicitation128BitUuids` 常量只说明类型可被表示，不代表 Publisher 允许发送。[Publisher 限制清单](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.advertisement.bluetoothleadvertisementpublisher?view=winrt-26100)

Windows 可以做可连接的 GAP peripheral，但这通过 `GattServiceProvider` 发布本地 GATT 服务完成。不能将“广播自己提供 AMS 服务”代替“征询 iPhone 提供 AMS 服务”：两者方向不同。[GATT Server 发布服务](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-server)

这不是证明 Windows 所有蓝牙路径均不可能。A2 后续已经取得成功；入站反向 GATT、临时手机连接入口与其他蓝牙栈的实验仍未实施。

## 已确认 API、未测与阻塞

| 环节 | 状态 | 查证结果 |
| --- | --- | --- |
| 宿主控制器 peripheral role 能力 | 宿主已实测 | LE/central/peripheral 均为 true，radio On；AMS 接入仍为 false |
| WinRT 发布 AMS 服务征询 AD `0x15` | 阻塞：官方 API 限制 | Publisher 将该类型列为系统保留，禁止 App 发布 |
| Windows 接受 iPhone 连入 | API 已确认，实机未测 | `GattServiceProvider.StartAdvertising` 配合 `IsConnectable=true` 发布可连接 peripheral 广播 |
| 可连接广播附加征询字段 | 未发现公开入口 | `GattServiceProviderAdvertisingParameters` 提供 `ServiceData`，但它是本地服务 UUID 的 service-data 段，不是 solicitation 段 |
| 从入站 GATT 请求拿到对端身份 | API 已确认，实机未测 | 本地特征的 ReadRequested/WriteRequested 事件携带 Session；Session.DeviceId 是 BluetoothDeviceId |
| 入站连接上反向发现远端 AMS | 未测；官方资料未承诺该角色组合 | 标准 GATT client API 可发现/读写/订阅远端；尚不能推导它一定复用该入站 LE 连接 |
| 无手机陪伴 App、原生 Windows 完整 AMS 接入 | 未证明 | 服务征询入口受限，后续 AMS 服务可达性与安全授权也未实测 |

来源：[IsPeripheralRoleSupported](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothadapter.isperipheralrolesupported?view=winrt-26100)、[Publisher](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.advertisement.bluetoothleadvertisementpublisher?view=winrt-26100)、[GATT Server](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-server)、[AdvertisingParameters](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattserviceprovideradvertisingparameters?view=winrt-26100)、[ReadRequested.Session](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattreadrequestedeventargs.session?view=winrt-26100)、[Session.DeviceId](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattsession.deviceid?view=winrt-26100)、[GATT Client](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-client)。

官方样例 `Scenario3_ServerForeground` 提供本地服务与特征的创建、读写响应、订阅处理和广告状态回调；它是 GATT server 样例，未实现从连入的 central 反向读取媒体服务。查证时读取样例源码，不运行其任务。[Microsoft BluetoothLE 样例](https://github.com/microsoft/Windows-universal-samples/blob/main/Samples/BluetoothLE/cs/Scenario3_ServerForeground.xaml.cs)

`BluetoothLEDevice.FromIdAsync` 可以取得远端对象，但创建对象本身不保证建立连接；uncached discovery、读写或 MaintainConnection 才可能发起连接。官方说明某些请求会等待较长时间且无法取消连接过程。因此固定 iPhone 的查询应串行、限量、标记超时；不要因为外层等待超时就不断并发重试。[GATT Client 连接行为](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-client)

## 最小真实实验：先拆开验证连接与媒体服务

### A. 当前可做的宿主只读检查

1. 在 Windows 读取默认 BluetoothAdapter：是否存在、`IsLowEnergySupported`、`IsPeripheralRoleSupported`、`IsCentralRoleSupported`。记录 OS、适配器/驱动版本与 radio 状态，不切换 radio。
2. 检查所需 WinRT 类型/方法是否可用；记录设备访问状态。若只读探针运行于无 UI 进程，访问被拒绝不等于硬件不支持，应明确保留宿主权限/运行上下文这一层。[FromIdAsync 权限提示](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothledevice.fromidasync?view=winrt-26100)
3. 如宿主已经有用户明确识别的 iPhone LE 设备 ID，只针对它记录已配对/连接状态；可把该 ID 留给后续服务验证。不要搜附近全部手机，也不要以 BR/EDR 配对记录代替已确认的 LE 连接。

**停点：** peripheral=false 会阻塞该适配器上的入站广告实验；publisher 的 `0x15` 限制已由文档确定，无须试图通过 manufacturer data、ServiceData 或名称编码绕过。即使 peripheral=true，也未消除服务征询限制。

### A2. 已配对 iPhone 的定向远端 AMS 查询

这是与“桌面发征询广播”不同的独立实验，可以先做，前提是 Windows 已有用户确认的 **LE** iPhone 设备 ID。按官方方法取得 `BluetoothLEDevice`，只查询 AMS UUID，使用 uncached discovery；不启动 AdvertisementWatcher，也不发起新的配对。公开 API 允许该查询，但未保证 iPhone 在既有 Windows 配对状态下暴露 AMS，不能提前记为支持。[按 UUID 发现服务](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothledevice.getgattservicesforuuidasync?view=winrt-26100)

查询属于远端服务读取，可能由 Windows 发起或复用 LE 连接，并非纯粹读取本机缓存。记录 DeviceAccessStatus、ConnectionStatus、发现 Status/ProtocolError、服务数量和耗时；`Success` 加非空 AMS service 才算发现成功，仍须后续特征订阅才算歌词同步链路成立。若注册列表只有经典蓝牙手机或没有 LE ID，应停在“无可用既有 LE 对象”，不能把附近扫描加入此只读设备列表检查。[GATT Client 连接行为](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-client)

### B. Windows 入站连接 → 反向 GATT 的隔离实验

此实验需要用户指定 iPhone，并允许它使用一个 BLE 调试 App 或专门的极小连接 App。其作用只是验证 iPhone 作为 central 连入 Windows；不代表最终实现已满足“无需手机 App”。如果不接受该临时入口，直接记录此分支未测，不用普通 iPhone 扫描冒充替代实验。

1. Windows 发布一个新生成 UUID 的临时 GATT 服务和一个可读/可写 handshake 特征，设置 `IsDiscoverable=true`、`IsConnectable=true`。记录创建结果、AdvertisingStatusChanged 和错误；不发布假的本地 AMS。方法依前述 Microsoft Server 样例。
2. 只从指定 iPhone 的 BLE 调试 App 选择该服务并连接，发起一次 handshake 读/写。Windows 从事件 `args.Session.DeviceId` 获取对端；核对用户设备，保留此 Session。仅显示广告不算连接成功。
3. 将 `DeviceId.Id` 作为候选传入 `BluetoothLEDevice.FromIdAsync`，再执行一次 `GetGattServicesForUuidAsync(AMS_UUID, BluetoothCacheMode.Uncached)`。这是待验证的 API 组合，不是已保证互通：分别记录对象是否为空、访问状态、发现状态、服务数量和耗时。[DeviceId.Id](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothdeviceid.id?view=winrt-26100)、[按 UUID 发现服务](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothledevice.getgattservicesforuuidasync?view=winrt-26100)
4. 若发现不到 AMS，保留证据，区分“同连接反向 GATT 不通”“身份/权限不通”和“iOS 当前未发布 AMS”；单次失败不能混为一类。必要的加密/配对只针对这部手机，由系统配对 UI 完成，不自动接受未知设备。
5. 只有远端 AMS 特征发现、必要安全授权与订阅都成功，且收到与手机实际播放一致的事件，才记为媒体链路成功。随后关闭临时连接 App，测试锁屏与重连；若必须长期保持该 App 才能连接，则如实记录产品约束。
6. 停止临时广告并释放探针对象。探针只在订阅阶段写必要的配置，不发送播放、暂停、切歌等控制命令。

**此实验的价值：** 先回答原生 Windows 能不能作为 GAP peripheral 同时读取 central 的远端 GATT；即使成功，独立的“无 App 首次连接”问题仍未解决。普通 `BluetoothLEAdvertisementWatcher` 扫描 iPhone 只验证 observer 能看到广播，不验证上述角色链路。

## BlueZ 作为另一个纯软件候选

BlueZ 官方 `LEAdvertisement1` 同时提供 `Type="peripheral"` 和 `SolicitUUIDs`；后者明确对应 Service Solicitation 字段。因此 native Linux + 可用控制器在公开 API 形状上更接近 AMS 配件路径。`LEAdvertisingManager1` 提供广播注册及可用实例/控制器限制查询。[LEAdvertisement](https://github.com/bluez/bluez/blob/master/doc/org.bluez.LEAdvertisement.rst)、[LEAdvertisingManager](https://github.com/bluez/bluez/blob/master/doc/org.bluez.LEAdvertisingManager.rst)

最小实验是：确认广告实例可用 → 注册 peripheral + AMS SolicitUUIDs 广播 → 用户只在指定 iPhone 上选择该设备 → 观察对应 Device1 的 Connected/Paired/ServicesResolved → 等待远端 AMS service/characteristic 对象 → 订阅与登记 AMS 属性 → 核对真实播放事件。BlueZ 文档提供这些状态及远端特征读写/通知 API，但并未在这些文档中保证 iPhone AMS 全链路成功。[Device1 状态](https://github.com/bluez/bluez/blob/master/doc/org.bluez.Device.rst)、[远端 GATT 特征](https://github.com/bluez/bluez/blob/master/doc/org.bluez.GattCharacteristic.rst)

当前 WSL 没有蓝牙控制器，不能直接执行这个实验。另行选择能使用真实控制器的 Linux 环境，属于明确的平台选择；不是在 Windows 路线内藏一个自动备用分支。这里没有安装 BlueZ、转交 USB 设备或改变宿主蓝牙驱动。

## 本次收束

原始调研时，宿主能力已确认、目标 LE 对象缺失。后续已定向读取用户手机的 AMS 并完成控制事件验证，因此当前优先采用 A2 路线；无需为了已验证的组合实施 B 或另换 Linux / 配件。主动服务征询广播仍受 Windows API 限制，首次配对与蓝牙断开后的恢复机制仍需验收。
