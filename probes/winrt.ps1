function Wait-WinRtOperation {
    param($Operation, [Type] $ResultType, [int] $TimeoutSeconds = 15)

    $asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() |
        Where-Object {
            $_.Name -eq 'AsTask' -and
            $_.IsGenericMethodDefinition -and
            $_.GetParameters().Count -eq 1 -and
            $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
        } |
        Select-Object -First 1

    $task = $asTask.MakeGenericMethod($ResultType).Invoke($null, @($Operation))
    if (-not $task.Wait([TimeSpan]::FromSeconds($TimeoutSeconds))) {
        throw 'Windows Bluetooth operation timed out.'
    }
    return $task.Result
}
