$ErrorActionPreference = 'Stop'
$taskWorkspace = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$taskExe = Join-Path $taskWorkspace 'TestResults/benchmark/PrettyDesk.exe'
$taskFixture = Join-Path $taskWorkspace 'TestResults/benchmark/PrettyDeskGameFixture.exe'
$taskData = Join-Path $taskWorkspace 'TestResults/benchmark-data'
Set-Content (Join-Path $taskData 'settings.json') '{"schemaVersion":1,"general":{"onboardingCompleted":true,"startWithWindows":false,"paused":false,"restoreOnExit":true},"detection":{"pollSeconds":2,"detectDelaySeconds":3,"exitGraceSeconds":10,"unknownGameHints":false},"default":{"mode":"fixed","fixedWallpaperId":"mb-01"},"content":{"prefetchInstalledGames":false},"customGames":[{"id":"qa-latency","displayName":"Latency Fixture","exeNames":["PrettyDeskGameFixture.exe"],"wallpapers":["ar-01"]}]}'
Copy-Item -LiteralPath $taskExe -Destination $taskFixture -Force
$taskApp = Start-Process $taskExe -ArgumentList '--background' -WindowStyle Hidden -PassThru
$taskApp.Id | Set-Content (Join-Path $taskData 'runtime.pid')
$taskBenchmark = Start-Process $taskExe -ArgumentList @('--acceptance-switch-benchmark',"`"$taskFixture`"") -WindowStyle Hidden -Wait -PassThru
if ($taskBenchmark.ExitCode -ne 0) { throw "Switching benchmark exited $($taskBenchmark.ExitCode); inspect isolated logs." }
Copy-Item (Join-Path $taskData 'switching.json') (Join-Path $taskWorkspace 'docs/evidence/2026-10-01/switching.json') -Force
