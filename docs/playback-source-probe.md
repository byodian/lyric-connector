# 播放状态接入：第一阶段验证结果

日期：2026-10-13。目标仍是手机只提供歌曲与播放状态，桌面独立处理歌词和显示。音乐 App、目标桌面系统与是否接受 BLE 配件等待用户确认。

后续实测已推进至 Windows + iPhone + YouTube Music：歌曲、进度、暂停恢复、拖动和切歌通知均已验证，详见 [最新媒体测试](youtubemusic-ams-validation.md)。下表保留第一阶段硬件检查；能力探针的 `amsConnectionVerified=false` 仅表示该探针本身不执行媒体测试，不能覆盖后续 AMS 成功结果。

## 已完成的真实环境检查

WSL 内未发现 `/sys/class/bluetooth` 或 `/dev/bus/usb`。通过 WSL 的 `powershell.exe` 调用宿主 Windows WinRT，读取原生蓝牙适配器，而不是将 WSL 设备缺失认定为电脑没有蓝牙。

| 检查项 | 实际结果 | 解释 |
| --- | --- | --- |
| BLE 支持 | true | 宿主适配器支持低功耗蓝牙 |
| GAP central 支持 | true | 可承担 central 角色 |
| GAP peripheral 支持 | true | 可承担 peripheral 角色，尚不证明 AMS 接入成功 |
| 蓝牙无线状态 | On | 蓝牙已开启；探针没有修改该状态 |
| WinRT 已配对 BLE 枚举 | 0 | 当前该选择器未返回目标；不代表所有经典蓝牙 / USB 设备均不存在 |
| 实际 AMS 连接 | 未验证 | 未配对、未读取手机歌曲或进度 |

能力结果文件已更新为最近一次检查，此时已配对 BLE 选择器返回 1 个 `iPhone` 端点：[current-host-bluetooth.json](current-host-bluetooth.json)。复现入口为 [windows_bluetooth.py](../probes/windows_bluetooth.py)，核心 WinRT 调用在 [windows-bluetooth-capabilities.ps1](../probes/windows-bluetooth-capabilities.ps1)。

探针读取适配器属性、无线状态及本地配对 BLE 设备注册列表。没有发送广播、扫描附近设备、进行配对或改变系统设置。脚本始终输出 `amsConnectionVerified: false`，不会把硬件能力当作播放状态读取成功。

使用了 `BluetoothAdapter.GetDefaultAsync`、`IsLowEnergySupported`、`IsCentralRoleSupported`、`IsPeripheralRoleSupported`、`GetRadioAsync`，并通过 `GetDeviceSelectorFromPairingState(true)` 枚举已配对 BLE 记录。[Microsoft BluetoothAdapter 文档](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothadapter?view=winrt-26100)、[GetRadioAsync](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothadapter.getradioasync?view=winrt-19041)、[配对选择器](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothledevice.getdeviceselectorfrompairingstate?view=winrt-26100)

## Windows 纯软件路线的新增约束

官方 `BluetoothLEAdvertisementPublisher` 文档明确禁止应用发送 `0x15`（128-bit Service Solicitation UUIDs）广播字段。AMS 厂商配件样例使用的服务请求广播，不能通过这个公开 API 原样移植到 Windows。[Microsoft 广播限制](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.advertisement.bluetoothleadvertisementpublisher?view=winrt-26100)

这表明「Windows WinRT 原样实现配件的服务请求广播」存在公开 API 层面的阻塞。不能由此宣称 Windows 上所有 AMS 接入方式都不可能：已有合适 BLE 连接能否访问手机 AMS，仍是另一项需要目标设备的实验。

第一阶段状态曾为硬件通过、媒体未测。随后用户建立蓝牙关系，定向 AMS 查询、显式共享打开与事件订阅成功，不需要应用发出 `0x15` 广播。当前成功证据以 [YouTube Music 实测](youtubemusic-ams-validation.md) 为准；广播限制的分析仍见 [AMS 连接检查](ams-direct-connection-check.md)。

## 复现命令

在项目根目录的 WSL 终端运行（依赖 Python 3 和可调用的宿主 Windows PowerShell）：

```sh
python3 probes/windows_bluetooth.py
```

原生 Windows 也可使用 Python 运行同一入口：

```powershell
python probes/windows_bluetooth.py
```

输出 JSON；成功退出码为 0，无适配器为 2，WinRT 查询失败为 1。宿主环境缺失或进程超时直接报错，不以模拟成功结果代替。探针不需要安装第三方依赖，也不执行构建。

## 确认目标后的后续实验

- **AMS 路线**：确认目标系统和 iPhone；若已有合适的配对 BLE 端点，检查其 AMS 服务及通知可达性。没有目标端点时先确定可行的连接建立方式。
- **播放器官方 API 路线**：只有用户选定相应播放器后，才实施其授权和状态读取；将曲目、位置、暂停状态转为统一快照。
- **BLE 配件路线**：仅在用户选择接受配件后推进，不把外设自动纳入方案。

真实状态数据到达后，验证暂停恢复、拖动、切歌及手机不打开歌词页时的事件更新，再进入桌面歌词实现。
