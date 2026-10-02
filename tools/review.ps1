<# Local review gate. Windows tests briefly change wallpaper and restore it in finally blocks.
   Run in a desktop session: pwsh -NoProfile -ExecutionPolicy Bypass -File tools/review.ps1
   Logs and a machine-readable summary stay under TestResults/review-gate. #>
param([switch]$PublishPlatforms)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskOutputDirectory = Join-Path $taskRoot 'TestResults/review-gate'
$taskChecks = [Collections.Generic.List[object]]::new()
$taskCaptureBefore = $env:PRETTYDESK_UI_CAPTURE_DIR
$taskDetectionBefore = $env:PRETTYDESK_DETECTION_REPORT
New-Item -ItemType Directory -Force $taskOutputDirectory | Out-Null

function Invoke-ReviewCheck([string]$Name, [string]$Command, [string[]]$Arguments) {
    $taskTimer = [Diagnostics.Stopwatch]::StartNew()
    $taskOutput = & $Command @Arguments 2>&1
    $taskCode = $LASTEXITCODE
    $taskText = $taskOutput | Out-String
    $taskText | Set-Content -LiteralPath (Join-Path $taskOutputDirectory "$Name.log")
    $taskChecks.Add([pscustomobject]@{
        Check = $Name
        Status = $(if ($taskCode -eq 0) { 'PASS' } else { 'FAIL' })
        ExitCode = $taskCode
        Seconds = [math]::Round($taskTimer.Elapsed.TotalSeconds, 2)
    })
    Write-Host "$Name : exit $taskCode"
    if ($taskCode -ne 0) { throw "$Name failed. See TestResults/review-gate/$Name.log" }
    return $taskText
}

Push-Location $taskRoot
try {
    $env:PRETTYDESK_UI_CAPTURE_DIR = Join-Path $taskOutputDirectory 'ui'
    $env:PRETTYDESK_DETECTION_REPORT = Join-Path $taskOutputDirectory 'detection.json'
    $taskPython = Join-Path $taskRoot 'tools/.venv/Scripts/python.exe'
    if (-not (Test-Path -LiteralPath $taskPython)) { $taskPython = 'python' }
    Invoke-ReviewCheck 'docs' $taskPython @('tools/check_docs.py') | Out-Null
    Invoke-ReviewCheck 'build' 'dotnet' @('build','PrettyDesk.sln','-c','Release','-p:RestoreLockedMode=true') | Out-Null
    foreach ($taskProject in 'Core','Presentation','Windows','App') {
        Invoke-ReviewCheck "tests-$taskProject" 'dotnet' @('test','--project',"tests/PrettyDesk.$taskProject.Tests",'-c','Release','--no-build') | Out-Null
    }
    Invoke-ReviewCheck 'format' 'dotnet' @('format','PrettyDesk.sln','--verify-no-changes','--no-restore') | Out-Null
    Invoke-ReviewCheck 'assetpipe' $taskPython @('-m','pytest','-q','tools/assetpipe') | Out-Null
    Invoke-ReviewCheck 'strings' $taskPython @('tools/strings.py','check') | Out-Null
    Invoke-ReviewCheck 'notices' $taskPython @('tools/gen_notices.py','--check') | Out-Null
    Invoke-ReviewCheck 'catalog-schema' $taskPython @('tools/validate_catalog.py') | Out-Null
    Invoke-ReviewCheck 'starter-hashes' $taskPython @('tools/verify_starter.py') | Out-Null
    Push-Location tools/assetpipe
    try { Invoke-ReviewCheck 'prompt-lint' $taskPython @('-m','assetpipe','prompts','--check') | Out-Null }
    finally { Pop-Location }
    $taskAuditText = Invoke-ReviewCheck 'nuget-audit' 'dotnet' @('list','PrettyDesk.sln','package','--vulnerable','--include-transitive','--format','json')
    $taskAudit = $taskAuditText | ConvertFrom-Json
    foreach ($taskProject in $taskAudit.projects) {
        foreach ($taskFramework in $taskProject.frameworks) {
            if ($taskFramework.topLevelPackages -or $taskFramework.transitivePackages) {
                $taskChecks[$taskChecks.Count - 1].Status = 'FAIL'
                throw 'NuGet reported vulnerable dependencies. See the nuget-audit log.'
            }
        }
    }
    if ($PublishPlatforms) {
        foreach ($taskRid in 'win-x64','win-arm64') {
            Invoke-ReviewCheck "publish-$taskRid" 'dotnet' @('publish','src/PrettyDesk.App','-c','Release','-r',$taskRid,
                '--self-contained','true','-p:RestoreLockedMode=true','-p:PublishReadyToRun=true','-o',"TestResults/review-gate/$taskRid") | Out-Null
        }
    }
} finally {
    $env:PRETTYDESK_UI_CAPTURE_DIR = $taskCaptureBefore
    $env:PRETTYDESK_DETECTION_REPORT = $taskDetectionBefore
    $taskChecks | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskOutputDirectory 'summary.json')
    Pop-Location
}
