<# Creates local, unsigned beta packages. Public stable releases use the signed release workflow. #>
param(
    [ValidatePattern('^\d+\.\d+\.\d+-[0-9A-Za-z.-]+$')]
    [string]$Version = '1.0.0-beta.1',
    [ValidateSet('win-x64','win-arm64')]
    [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskPublish = Join-Path $taskRoot "dist/publish/$Version/$Runtime"
$taskPackages = Join-Path $taskRoot "dist/releases/$Version/$Runtime"
$taskVpk = Join-Path $taskRoot 'tools/bin/vpk.exe'
if (-not (Test-Path -LiteralPath $taskVpk)) {
    throw 'Install the pinned packager first: dotnet tool install vpk --version 1.2.161 --tool-path tools/bin'
}
New-Item -ItemType Directory -Force $taskPublish,$taskPackages | Out-Null
Push-Location $taskRoot
try {
    $taskPython = Join-Path $taskRoot 'tools/.venv/Scripts/python.exe'
    if (-not (Test-Path -LiteralPath $taskPython)) { $taskPython = 'python' }
    & $taskPython tools/verify_starter.py
    if ($LASTEXITCODE -ne 0) { throw 'Starter content validation failed' }
    dotnet publish src/PrettyDesk.App -c Release -r $Runtime --self-contained true `
        -p:RestoreLockedMode=true -p:PublishReadyToRun=true -p:PublishTrimmed=false -p:PublishSingleFile=false `
        -p:AcceptanceTesting=false "-p:MinVerVersionOverride=$Version" -o $taskPublish
    if ($LASTEXITCODE -ne 0) { throw 'Locked self-contained publish failed' }
    $taskAssemblyBytes = [IO.File]::ReadAllBytes((Join-Path $taskPublish 'PrettyDesk.dll'))
    $taskAssemblyText = [Text.Encoding]::UTF8.GetString($taskAssemblyBytes)
    if ($taskAssemblyText.Contains('AcceptanceHarness') -or $taskAssemblyText.Contains('AcceptanceDataRoot')) {
        throw 'Acceptance-only code unexpectedly appeared in the distributable assembly'
    }
    & $taskVpk pack --packId PrettyDeskApp --packTitle PrettyDesk --packAuthors 'PrettyDesk contributors' `
        --packVersion $Version --packDir $taskPublish --mainExe PrettyDesk.exe `
        --icon src/PrettyDesk.App/Assets/PrettyDesk.ico --runtime $Runtime --channel "$Runtime-beta" `
        --outputDir $taskPackages --shortcuts StartMenuRoot
    if ($LASTEXITCODE -ne 0) { throw 'Installer packaging failed' }
    $taskArtifacts = Get-ChildItem -LiteralPath $taskPackages -File | Where-Object Extension -in '.exe','.zip','.nupkg'
    $taskArtifacts | ForEach-Object {
        [pscustomobject]@{ File=$_.Name; Bytes=$_.Length; SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskPackages 'SHA256.json')
    @"
PrettyDesk $Version — $Runtime

Install: double-click the Setup.exe file in this folder.
Portable: extract the Portable.zip into a folder, then open PrettyDesk.exe inside it.
Keep the portable executable with its DLLs and content folder. No separate .NET installation is required.

This is an unsigned local beta. Windows may show a SmartScreen warning.
Bundled wallpapers work offline. Remote game wallpaper downloads require the content host, which is not configured yet.
Settings and wallpaper backups use %LOCALAPPDATA%\PrettyDesk.
The installer uses per-user installation and creates a Start-menu shortcut.
Release QA status: docs/REVIEW_2026-10-01.md in the source project.
"@ | Set-Content -LiteralPath (Join-Path $taskPackages 'README.txt')
    Write-Host "Created unsigned beta packages in $taskPackages"
    $taskArtifacts | Select-Object Name,Length
} finally { Pop-Location }
