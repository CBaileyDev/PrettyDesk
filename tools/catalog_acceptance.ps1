param([switch]$AllCoreTests)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $taskRoot 'tools/sign_content.ps1')
$taskFixture = Join-Path $taskRoot 'TestResults/catalog-feed'
$taskTlsDir = Join-Path $env:LOCALAPPDATA 'PrettyDeskSigning/test-tls'
New-Item -ItemType Directory -Force $taskTlsDir,$taskFixture | Out-Null
$taskTlsKey = [Security.Cryptography.ECDsaCng]::new(256)
$taskRequest = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=localhost', $taskTlsKey, [Security.Cryptography.HashAlgorithmName]::SHA256)
$taskSan = [Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
$taskSan.AddDnsName('localhost')
$taskSan.AddIpAddress([Net.IPAddress]::Loopback)
$taskRequest.CertificateExtensions.Add($taskSan.Build())
$taskCert = $taskRequest.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5), [DateTimeOffset]::UtcNow.AddDays(2))
$taskCertFile = Join-Path $taskTlsDir 'localhost.pem'
$taskKeyFile = Join-Path $taskTlsDir 'localhost.key'
[IO.File]::WriteAllText($taskCertFile, $taskCert.ExportCertificatePem())
[IO.File]::WriteAllText($taskKeyFile, $taskTlsKey.ExportPkcs8PrivateKeyPem())
$taskCertHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($taskCert.RawData))
$taskTlsKey.Dispose()
$taskCert.Dispose()

$taskValid = Join-Path $taskFixture 'valid'
New-Item -ItemType Directory -Force $taskValid | Out-Null
$taskCatalog = Get-Content 'art/out/publish/catalog.json' -Raw | ConvertFrom-Json
$taskCatalog.contentBaseUrl = 'https://localhost:30443/valid/'
$taskValidCatalog = Join-Path $taskValid 'catalog.json'
$taskCatalog | ConvertTo-Json -Depth 100 | Set-Content $taskValidCatalog -Encoding utf8
& (Join-Path $taskRoot 'tools/sign_content.ps1') -CatalogPath $taskValidCatalog
foreach ($taskKind in 'tampered','unsigned') {
    $taskDestination = Join-Path $taskFixture $taskKind
    New-Item -ItemType Directory -Force $taskDestination | Out-Null
    Copy-Item $taskValidCatalog (Join-Path $taskDestination 'catalog.json') -Force
    if ($taskKind -ne 'unsigned') { Copy-Item "$taskValidCatalog.sig" (Join-Path $taskDestination 'catalog.json.sig') -Force }
    if ($taskKind -eq 'tampered') { [IO.File]::AppendAllText((Join-Path $taskDestination 'catalog.json'), ' ') }
}
$taskStarterPack = $taskCatalog.packs | Where-Object {$_.wallpapers.starter -contains $true} | Select-Object -First 1
foreach ($taskFile in @($taskStarterPack.wallpapers.variants.'16x9') + @($taskStarterPack.wallpapers.thumb)) {
    $taskAssetDestination = Join-Path (Join-Path $taskFixture 'valid') $taskFile.path
    New-Item -ItemType Directory -Force (Split-Path -Parent $taskAssetDestination) | Out-Null
    Copy-Item (Join-Path 'art/out/publish' $taskFile.path) $taskAssetDestination -Force
}
Set-Content (Join-Path $taskFixture 'bad-asset.jpg') 'deliberately corrupt test asset'
$taskPython = Join-Path $taskRoot 'tools/.venv/Scripts/python.exe'
$taskServe = Join-Path $taskRoot 'tools/serve_catalog.py'
$taskServer = Start-Process $taskPython -ArgumentList @("`"$taskServe`"",'--root',"`"$taskFixture`"",'--cert',"`"$taskCertFile`"",'--key',"`"$taskKeyFile`"") -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $taskFixture 'server.stdout.log') -RedirectStandardError (Join-Path $taskFixture 'server.stderr.log')
try {
    $env:PRETTYDESK_TEST_CATALOG_URL = 'https://localhost:30443/'
    $env:PRETTYDESK_TEST_CERT_SHA256 = $taskCertHash
    if ($AllCoreTests) {
        dotnet test --project tests/PrettyDesk.Core.Tests -c Release
    } else {
        dotnet test --project tests/PrettyDesk.Core.Tests -c Release -- --filter-method '*Real_https_feed*'
    }
    if ($LASTEXITCODE -ne 0) { throw 'Live HTTPS catalog acceptance failed' }
} finally {
    Stop-Process -Id $taskServer.Id -ErrorAction SilentlyContinue
    Remove-Item Env:PRETTYDESK_TEST_CATALOG_URL,Env:PRETTYDESK_TEST_CERT_SHA256 -ErrorAction SilentlyContinue
}
