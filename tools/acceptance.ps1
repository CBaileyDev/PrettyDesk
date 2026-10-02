<# Two real, isolated Velopack releases. Acceptance-only commands are compiled out of public builds. #>
param([switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskResults = Join-Path $taskRoot 'TestResults/acceptance'
$taskData = Join-Path $taskRoot 'TestResults/acceptance-data'
$taskInstall = Join-Path $taskResults 'installed'
$taskFeed = Join-Path $taskResults 'feed'
$taskVpk = Join-Path $taskRoot 'tools/bin/vpk.exe'
New-Item -ItemType Directory -Force $taskData,$taskFeed | Out-Null
Set-Content (Join-Path $taskData 'settings.json') '{"schemaVersion":1,"general":{"onboardingCompleted":true,"startWithWindows":false,"paused":true,"restoreOnExit":true},"content":{"prefetchInstalledGames":false}}'

foreach ($taskVersion in '1.0.0','1.0.1') {
    $taskPublish = Join-Path $taskResults "publish-$taskVersion"
    dotnet restore src/PrettyDesk.App -r win-x64 -p:PublishReadyToRun=true -p:SelfContained=true --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed' }
    dotnet publish src/PrettyDesk.App -c Release -r win-x64 --self-contained true --no-restore `
        -p:PublishReadyToRun=true -p:PublishTrimmed=false -p:AcceptanceTesting=true `
        "-p:AcceptanceDataRoot=$taskData" "-p:MinVerVersionOverride=$taskVersion" -o $taskPublish
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    & $taskVpk pack --packId PrettyDeskAcceptance --packTitle 'PrettyDesk Acceptance Test' `
        --packAuthors 'PrettyDesk' --packVersion $taskVersion --packDir $taskPublish --mainExe PrettyDesk.exe `
        --runtime win-x64 --channel win-x64-stable --outputDir $taskFeed --shortcuts None
    if ($LASTEXITCODE -ne 0) { throw 'Velopack packing failed' }
    if ($taskVersion -eq '1.0.0') {
        $taskOldSetup = Join-Path $taskResults 'PrettyDeskAcceptance-1.0.0-Setup.exe'
        $taskSetup = Get-ChildItem $taskFeed -Filter '*Setup.exe' | Select-Object -First 1
        Copy-Item -LiteralPath $taskSetup.FullName -Destination $taskOldSetup -Force
    }
}
if ($BuildOnly) { return }
if (Test-Path -LiteralPath $taskInstall) { throw 'Acceptance install already exists. Preserve it or uninstall it before a new run.' }
$taskSetupProcess = Start-Process -FilePath $taskOldSetup -ArgumentList @('--silent','--installto',"`"$taskInstall`"") -WindowStyle Hidden -Wait -PassThru
if ($taskSetupProcess.ExitCode -ne 0) { throw "Install exited $($taskSetupProcess.ExitCode)" }
$taskExe = Join-Path $taskInstall 'current/PrettyDesk.exe'
$taskVersionProcess = Start-Process $taskExe -ArgumentList '--acceptance-version' -WindowStyle Hidden -Wait -PassThru
if ($taskVersionProcess.ExitCode -ne 0) { throw 'Installed version command failed' }
$taskBefore = Get-Content (Join-Path $taskData 'version.json') -Raw | ConvertFrom-Json
if ($taskBefore.DisplayVersion -ne '1.0.0') { throw 'Old version was not installed' }
$taskUpdateProcess = Start-Process $taskExe -ArgumentList @('--acceptance-update',"`"$taskFeed`"") -WindowStyle Hidden -PassThru
$taskDeadline = (Get-Date).AddMinutes(2)
while ((Get-Date) -lt $taskDeadline) {
    $taskReceiptPath = Join-Path $taskData 'tray-ready.json'
    if (Test-Path $taskReceiptPath) {
        $taskReceipt = Get-Content $taskReceiptPath -Raw | ConvertFrom-Json
        if ($taskReceipt.DisplayVersion -eq '1.0.1') { break }
    }
    Start-Sleep -Milliseconds 250
}
if (-not $taskReceipt -or $taskReceipt.DisplayVersion -ne '1.0.1') { throw 'Update did not restart into 1.0.1' }
[pscustomobject]@{ InstalledVersion=$taskBefore.DisplayVersion; UpdatedVersion=$taskReceipt.DisplayVersion; StartupMilliseconds=$taskReceipt.StartupMilliseconds; ProcessId=$taskReceipt.ProcessId; Signed=$false; Feed=$taskFeed } | ConvertTo-Json | Set-Content (Join-Path $taskResults 'update-result.json')
Write-Host 'Installed 1.0.0, checked and downloaded the local feed through VelopackUpdateBackend, restarted into 1.0.1.'
Write-Host 'UNSIGNED acceptance packages: no public signing claim. App is paused and uses isolated data.'
