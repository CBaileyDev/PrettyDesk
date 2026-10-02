param([ValidateSet('Install','Uninstall','Verify')][string]$Action, [switch]$ExpectDeleted)
$ErrorActionPreference='Stop'
$taskWorkspace=Split-Path -Parent $PSScriptRoot
$taskData=Join-Path $taskWorkspace 'TestResults/uninstall-ui-data'
$taskInstall=Join-Path $taskWorkspace 'TestResults/uninstall-ui/installed'
if ($Action -eq 'Install') {
    if (Test-Path -LiteralPath $taskInstall) { throw 'Uninstall the existing isolated UI test install first.' }
    New-Item -ItemType Directory -Force $taskData | Out-Null
    Set-Content (Join-Path $taskData 'settings.json') '{"schemaVersion":1,"general":{"onboardingCompleted":true,"startWithWindows":false,"paused":true,"restoreOnExit":true},"content":{"prefetchInstalledGames":false}}'
    foreach ($taskFolder in 'packs','cache/render','user','backup','catalog') {
        $taskSentinelDir=Join-Path $taskData $taskFolder
        New-Item -ItemType Directory -Force $taskSentinelDir | Out-Null
        Set-Content (Join-Path $taskSentinelDir 'qa-sentinel.txt') 'Disposable acceptance data only.'
    }
    $taskSetup=(Get-ChildItem (Join-Path $taskWorkspace 'TestResults/uninstall-ui/feed') -Filter '*Setup.exe' | Select-Object -First 1).FullName
    $taskProcess=Start-Process $taskSetup -ArgumentList @('--silent','--installto',"`"$taskInstall`"") -WindowStyle Hidden -Wait -PassThru
    if ($taskProcess.ExitCode -ne 0) { throw "Test installation failed: $($taskProcess.ExitCode)" }
} elseif ($Action -eq 'Uninstall') {
    $taskExe=Join-Path $taskInstall 'current/PrettyDesk.exe'
    Get-Process -Name PrettyDesk -ErrorAction SilentlyContinue | Where-Object Path -eq $taskExe | Stop-Process
    $env:PRETTYDESK_ACCEPTANCE_UNINSTALL='interactive'
    try {
        $taskUninstall=Start-Process (Join-Path $taskInstall 'Update.exe') -ArgumentList @('uninstall','--silent') -WindowStyle Hidden -PassThru
        $taskUninstall.Id | Set-Content (Join-Path $taskData 'uninstall.pid')
        Write-Output 'The real uninstall prompt is ready for UI inspection.'
    } finally { Remove-Item Env:PRETTYDESK_ACCEPTANCE_UNINSTALL }
} else {
    $taskSettingsPresent=Test-Path -LiteralPath (Join-Path $taskData 'settings.json')
    if ($taskSettingsPresent -eq [bool]$ExpectDeleted) { throw 'Data retention outcome differs from the selected prompt action.' }
    $taskSentinels=@('packs','cache/render','user','backup','catalog' | Where-Object {Test-Path (Join-Path (Join-Path $taskData $_) 'qa-sentinel.txt')})
    if (($ExpectDeleted -and $taskSentinels.Count -ne 0) -or (-not $ExpectDeleted -and $taskSentinels.Count -ne 5)) { throw 'A data directory did not follow the selected deletion/retention action.' }
    $taskResult=[pscustomobject]@{ ExpectedDeleted=[bool]$ExpectDeleted; SettingsPresent=$taskSettingsPresent; SentinelsRemaining=$taskSentinels.Count; HookRan=(Test-Path (Join-Path $taskData 'uninstall-hook.txt')); InstallationRemoved=(-not (Test-Path (Join-Path $taskInstall 'current/PrettyDesk.exe'))) }
    if (-not $taskResult.HookRan -or -not $taskResult.InstallationRemoved) { throw 'Real uninstall hook or removal did not complete.' }
    $taskVerdict=if($ExpectDeleted){'delete'}else{'keep'}
    $taskResult | ConvertTo-Json | Set-Content (Join-Path $taskWorkspace "docs/evidence/2026-10-01/uninstall-ui-$taskVerdict.json")
    $taskResult
}
