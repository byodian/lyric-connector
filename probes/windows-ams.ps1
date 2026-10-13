param(
    [Parameter(Mandatory = $true)]
    [string] $DeviceName,
    [ValidateRange(1, 600)]
    [int] $ListenSeconds = 20
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$report = [ordered]@{
    checkedAtUtc = [DateTime]::UtcNow.ToString('o')
    status = 'pending'
    deviceName = $DeviceName
    selection = 'paired-ble-selector-and-exact-name'
    stage = 'enumeration'
    amsServiceUuid = '89D3502B-0F36-433A-8EF4-C502AD55F8DC'
    amsConnectionVerified = $false
    events = [Collections.Generic.List[object]]::new()
}

$device = $null
$services = @()
$eventRegistered = $false

function Read-AmsAttribute {
    param($Characteristic, [byte] $EntityId, [byte] $AttributeId)

    $selection = [System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions]::AsBuffer([byte[]] ($EntityId, $AttributeId))
    $write = Wait-WinRtOperation ($Characteristic.WriteValueWithResultAsync($selection, $writeOptionType::WriteWithResponse)) $writeResultType
    $record = [ordered]@{
        entityId = $EntityId
        attributeId = $AttributeId
        writeStatus = $write.Status.ToString()
        protocolError = $write.ProtocolError
    }
    if ($write.Status.ToString() -eq 'Success') {
        $read = Wait-WinRtOperation ($Characteristic.ReadValueAsync($cacheType::Uncached)) $readResultType
        $record.readStatus = $read.Status.ToString()
        $record.protocolError = $read.ProtocolError
        $record.receivedAtUtc = [DateTime]::UtcNow.ToString('o')
        $record.receivedTimestamp = [Diagnostics.Stopwatch]::GetTimestamp()
        if ($read.Status.ToString() -eq 'Success') {
            $record.value = [Text.Encoding]::UTF8.GetString([LyricConnector.Probes.NotificationCollector]::ReadAttribute($read))
        }
    }
    return $record
}
try {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    $bleType = [Windows.Devices.Bluetooth.BluetoothLEDevice, Windows.Devices.Bluetooth, ContentType = WindowsRuntime]
    $infoType = [Windows.Devices.Enumeration.DeviceInformation, Windows.Devices.Enumeration, ContentType = WindowsRuntime]
    $collectionType = [Windows.Devices.Enumeration.DeviceInformationCollection, Windows.Devices.Enumeration, ContentType = WindowsRuntime]
    $servicesType = [Windows.Devices.Bluetooth.GenericAttributeProfile.GattDeviceServicesResult, Windows.Devices.Bluetooth, ContentType = WindowsRuntime]
    $characteristicsType = [Windows.Devices.Bluetooth.GenericAttributeProfile.GattCharacteristicsResult, Windows.Devices.Bluetooth, ContentType = WindowsRuntime]
    $cacheType = [Windows.Devices.Bluetooth.BluetoothCacheMode, Windows.Devices.Bluetooth, ContentType = WindowsRuntime]
    $communicationType = [Windows.Devices.Bluetooth.GenericAttributeProfile.GattCommunicationStatus, Windows.Devices.Bluetooth, ContentType = WindowsRuntime]
    $descriptorType = [Windows.Devices.Bluetooth.GenericAttributeProfile.GattClientCharacteristicConfigurationDescriptorValue, Windows.Devices.Bluetooth, ContentType = WindowsRuntime]
    $writeResultType = [Windows.Devices.Bluetooth.GenericAttributeProfile.GattWriteResult, Windows.Devices.Bluetooth, ContentType = WindowsRuntime]
    $readResultType = [Windows.Devices.Bluetooth.GenericAttributeProfile.GattReadResult, Windows.Devices.Bluetooth, ContentType = WindowsRuntime]
    $writeOptionType = [Windows.Devices.Bluetooth.GenericAttributeProfile.GattWriteOption, Windows.Devices.Bluetooth, ContentType = WindowsRuntime]
    $openStatusType = [Windows.Devices.Bluetooth.GenericAttributeProfile.GattOpenStatus, Windows.Devices.Bluetooth, ContentType = WindowsRuntime]
    $sharingType = [Windows.Devices.Bluetooth.GenericAttributeProfile.GattSharingMode, Windows.Devices.Bluetooth, ContentType = WindowsRuntime]

    $devices = Wait-WinRtOperation ($infoType::FindAllAsync($bleType::GetDeviceSelectorFromPairingState($true))) $collectionType
    $matches = @($devices | Where-Object { $_.Name -eq $DeviceName })
    if ($matches.Count -ne 1) {
        $report.status = 'target-not-unique'
        $report.matchCount = $matches.Count
        throw 'Expected exactly one named iPhone BLE endpoint.'
    }

    $report.enumeratedPairingState = $matches[0].Pairing.IsPaired
    $report.stage = 'device-open'
    $device = Wait-WinRtOperation ($bleType::FromIdAsync($matches[0].Id)) $bleType
    if ($null -eq $device) {
        throw 'Windows did not return a BluetoothLEDevice for the target.'
    }
    $report.connectionBefore = $device.ConnectionStatus.ToString()
    $report.deviceAccess = $device.DeviceAccessInformation.CurrentStatus.ToString()
    $report.stage = 'ams-service-discovery'
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $result = Wait-WinRtOperation ($device.GetGattServicesForUuidAsync([Guid] $report.amsServiceUuid, $cacheType::Uncached)) $servicesType
    $report.discoverySeconds = [Math]::Round($timer.Elapsed.TotalSeconds, 3)
    $report.discoveryStatus = $result.Status.ToString()
    $report.protocolError = $result.ProtocolError
    $report.connectionAfter = $device.ConnectionStatus.ToString()
    $services = @($result.Services)
    $report.amsServiceCount = $services.Count
    if ($result.Status.ToString() -ne 'Success' -or $services.Count -ne 1) {
        $report.status = 'ams-unavailable'
    } else {
        $report.stage = 'ams-service-open'
        $report.sharingModeBefore = $services[0].SharingMode.ToString()
        $openStatus = Wait-WinRtOperation ($services[0].OpenAsync($sharingType::SharedReadAndWrite)) $openStatusType
        $report.openStatus = $openStatus.ToString()
        $report.sharingModeAfter = $services[0].SharingMode.ToString()
        if ($report.sharingModeAfter -ne 'SharedReadAndWrite') {
            throw 'AMS service is not open for shared access.'
        }
        $report.stage = 'ams-characteristics'
        $characteristics = Wait-WinRtOperation ($services[0].GetCharacteristicsAsync($cacheType::Uncached)) $characteristicsType
        $report.characteristicsStatus = $characteristics.Status.ToString()
        $report.characteristicsProtocolError = $characteristics.ProtocolError
        $report.characteristics = @($characteristics.Characteristics | ForEach-Object {
            [ordered]@{ uuid = $_.Uuid.ToString(); properties = $_.CharacteristicProperties.ToString() }
        })
        $report.status = 'ams-service-found'
        if ($characteristics.Status.ToString() -ne 'Success') {
            throw 'AMS characteristic discovery failed.'
        }
        $entity = @($characteristics.Characteristics | Where-Object { $_.Uuid -eq [Guid] '2F7CABCE-808D-411F-9A0C-BB92BA96C102' })[0]
        $attribute = @($characteristics.Characteristics | Where-Object { $_.Uuid -eq [Guid] 'C6B2F38C-23AB-46D8-A6AB-A3A870BBD5D7' })[0]
        $report.stage = 'entity-update-subscription'
        $frameworkPath = [Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
        $references = @(
            (Join-Path $env:windir 'System32\WinMetadata\Windows.Foundation.winmd'),
            (Join-Path $env:windir 'System32\WinMetadata\Windows.Devices.winmd'),
            (Join-Path $env:windir 'System32\WinMetadata\Windows.Storage.winmd'),
            (Join-Path $frameworkPath 'System.Runtime.WindowsRuntime.dll'),
            ([Reflection.Assembly]::Load('System.Runtime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a').Location),
            (Join-Path $frameworkPath 'System.dll'),
            (Join-Path $frameworkPath 'System.Core.dll')
        )
        $compilerParameters = [CodeDom.Compiler.CompilerParameters]::new()
        foreach ($reference in $references) {
            $compilerParameters.ReferencedAssemblies.Add($reference) | Out-Null
        }
        Add-Type -TypeDefinition $NotificationCollectorSource -CompilerParameters $compilerParameters
        $collector = [LyricConnector.Probes.NotificationCollector]::new()
        $handlerType = $entity.GetType().GetEvent('ValueChanged').EventHandlerType
        $handler = $collector.CreateHandler($handlerType)
        $registrationToken = $entity.add_ValueChanged($handler)
        $eventRegistered = $true
        $notifyStatus = Wait-WinRtOperation ($entity.WriteClientCharacteristicConfigurationDescriptorAsync($descriptorType::Notify)) $communicationType
        $report.notifyStatus = $notifyStatus.ToString()
        if ($notifyStatus.ToString() -ne 'Success') {
            throw 'Windows could not enable AMS notifications.'
        }

        $report.stage = 'attribute-registration'
        $report.registrations = [Collections.Generic.List[object]]::new()
        foreach ($registration in @([byte[]] (0, 0, 1), [byte[]] (1, 0), [byte[]] (2, 0, 1, 2, 3))) {
            $buffer = [System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions]::AsBuffer($registration)
            $write = Wait-WinRtOperation ($entity.WriteValueWithResultAsync($buffer, $writeOptionType::WriteWithResponse)) $writeResultType
            $report.registrations.Add([ordered]@{
                entityId = $registration[0]; status = $write.Status.ToString(); protocolError = $write.ProtocolError
            })
            if ($write.Status.ToString() -ne 'Success') {
                throw 'AMS attribute registration failed.'
            }
        }

        $report.stage = 'initial-attributes'
        $report.attributes = [Collections.Generic.List[object]]::new()
        foreach ($pair in @([byte[]] (0, 0), [byte[]] (0, 1), [byte[]] (1, 0), [byte[]] (2, 0), [byte[]] (2, 1), [byte[]] (2, 2), [byte[]] (2, 3))) {
            $report.attributes.Add((Read-AmsAttribute $attribute $pair[0] $pair[1]))
        }

        $report.stage = 'notification-capture'
        $report.listenSeconds = $ListenSeconds
        $report.timestampFrequency = [Diagnostics.Stopwatch]::Frequency
        $clock = [Diagnostics.Stopwatch]::StartNew()
        [Console]::Error.WriteLine("AMS_READY: listening for $ListenSeconds seconds.")
        while ($clock.Elapsed.TotalSeconds -lt $ListenSeconds) {
            $batch = $collector.Drain()
            foreach ($notification in $batch) {
                $bytes = $notification.Data
                if ($bytes.Length -lt 3) { throw 'AMS notification header is incomplete.' }
                $record = [ordered]@{
                    receivedAtUtc = $notification.ReceivedAtUtc.ToString('o')
                    receivedTimestamp = $notification.ReceivedTimestamp
                    drainedAfterSeconds = [Math]::Round($clock.Elapsed.TotalSeconds, 3)
                    entityId = $bytes[0]
                    attributeId = $bytes[1]
                    truncated = ($bytes[2] -band 1) -ne 0
                    value = [Text.Encoding]::UTF8.GetString($bytes, 3, $bytes.Length - 3)
                }
                if ($record.truncated) {
                    $record.completeAttribute = Read-AmsAttribute $attribute $bytes[0] $bytes[1]
                }
                $report.events.Add($record)
                [Console]::Error.WriteLine('AMS_EVENT: ' + ($record | ConvertTo-Json -Depth 6 -Compress))
            }
            Start-Sleep -Milliseconds 100
        }
        $report.eventCount = $report.events.Count
        $report.amsConnectionVerified = $report.events.Count -gt 0
        $report.status = if ($report.amsConnectionVerified) { 'ams-events-received' } else { 'ams-no-events' }
        $report.connectionAtEnd = $device.ConnectionStatus.ToString()
        $report.finalPlaybackInfo = Read-AmsAttribute $attribute 0 1
    }
    $report | ConvertTo-Json -Depth 8
} catch {
    $report.status = 'query-failed'
    $report.error = $_.Exception.Message
    $report | ConvertTo-Json -Depth 8
    exit 1
} finally {
    if ($eventRegistered) {
        $entity.remove_ValueChanged($registrationToken)
    }
    foreach ($service in $services) { $service.Dispose() }
    if ($null -ne $device) { $device.Dispose() }
}
