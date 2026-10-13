param(
    [ValidateRange(1, 60)]
    [int] $TimeoutSeconds = 15
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$report = [ordered]@{
    checkedAtUtc = [DateTime]::UtcNow.ToString('o')
    status = 'pending'
    host = 'native-windows'
    lowEnergySupported = $null
    centralRoleSupported = $null
    peripheralRoleSupported = $null
    radioState = $null
    pairedBleDeviceCount = $null
    pairedBleDeviceNames = @()
    amsConnectionVerified = $false
}

try {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    $adapterType = [Windows.Devices.Bluetooth.BluetoothAdapter, Windows.Devices.Bluetooth, ContentType = WindowsRuntime]
    $radioType = [Windows.Devices.Radios.Radio, Windows.System.Devices, ContentType = WindowsRuntime]
    $adapter = Wait-WinRtOperation ($adapterType::GetDefaultAsync()) $adapterType $TimeoutSeconds

    if ($null -eq $adapter) {
        $report.status = 'no-adapter'
        $report | ConvertTo-Json
        exit 2
    }

    $report.lowEnergySupported = $adapter.IsLowEnergySupported
    $report.centralRoleSupported = $adapter.IsCentralRoleSupported
    $report.peripheralRoleSupported = $adapter.IsPeripheralRoleSupported
    $radio = Wait-WinRtOperation ($adapter.GetRadioAsync()) $radioType $TimeoutSeconds
    $report.radioState = $radio.State.ToString()
    $bleType = [Windows.Devices.Bluetooth.BluetoothLEDevice, Windows.Devices.Bluetooth, ContentType = WindowsRuntime]
    $infoType = [Windows.Devices.Enumeration.DeviceInformation, Windows.Devices.Enumeration, ContentType = WindowsRuntime]
    $collectionType = [Windows.Devices.Enumeration.DeviceInformationCollection, Windows.Devices.Enumeration, ContentType = WindowsRuntime]
    $selector = $bleType::GetDeviceSelectorFromPairingState($true)
    $devices = Wait-WinRtOperation ($infoType::FindAllAsync($selector)) $collectionType $TimeoutSeconds
    $report.pairedBleDeviceCount = $devices.Count
    $report.pairedBleDeviceNames = @($devices | ForEach-Object { $_.Name })
    $report.status = 'capabilities-read'
    $report | ConvertTo-Json
} catch {
    $report.status = 'query-failed'
    $report.error = $_.Exception.Message
    $report | ConvertTo-Json
    exit 1
}
