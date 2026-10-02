param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$DataRoot, [int]$Runs=5, [string]$OutFile='startup.json')
$ErrorActionPreference = 'Stop'
$taskExePath = (Resolve-Path -LiteralPath $Exe).Path
$taskReceipt = Join-Path $DataRoot 'tray-ready.json'
$taskSamples = @()
$taskProcess = $null
for ($taskIndex=0; $taskIndex -lt $Runs; $taskIndex++) {
    if ($taskProcess -and -not $taskProcess.HasExited) { Stop-Process -Id $taskProcess.Id }
    Remove-Item -LiteralPath $taskReceipt -ErrorAction SilentlyContinue
    $taskTimer = [Diagnostics.Stopwatch]::StartNew()
    $taskProcess = Start-Process $taskExePath -ArgumentList '--background' -WindowStyle Hidden -PassThru
    while (-not (Test-Path $taskReceipt) -and $taskTimer.Elapsed.TotalSeconds -lt 15) { Start-Sleep -Milliseconds 10 }
    if (-not (Test-Path $taskReceipt)) { throw 'No tray-ready receipt; check whether another PrettyDesk copy is running.' }
    $taskObservedMs = $taskTimer.Elapsed.TotalMilliseconds
    $taskMeasured = Get-Content $taskReceipt -Raw | ConvertFrom-Json
    if ($taskMeasured.ProcessId -ne $taskProcess.Id) { throw 'Unexpected process wrote the receipt' }
    $taskSamples += [pscustomobject]@{ Version=$taskMeasured.DisplayVersion; ProcessId=$taskProcess.Id; InternalMilliseconds=$taskMeasured.StartupMilliseconds; ExternalMilliseconds=$taskObservedMs }
    Start-Sleep -Seconds 2
}
$taskSamples | ConvertTo-Json | Set-Content $OutFile
Write-Host "Recorded $Runs process starts; final PID $($taskProcess.Id) remains running in the tray."
