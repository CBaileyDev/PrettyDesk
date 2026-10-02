param([string]$CatalogPath = 'art/out/publish/catalog.json')
$ErrorActionPreference = 'Stop'
$taskKeyPath = Join-Path $env:LOCALAPPDATA 'PrettyDeskSigning/catalog.private.dpapi'
$taskProtectedKey = [IO.File]::ReadAllBytes($taskKeyPath)
$taskPlainKey = [Security.Cryptography.ProtectedData]::Unprotect($taskProtectedKey, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
try {
    $env:PRETTYDESK_CATALOG_KEY = [Convert]::ToBase64String($taskPlainKey)
    dotnet run --project tools/catalog-sign -c Release -- sign $CatalogPath --key-env PRETTYDESK_CATALOG_KEY
    if ($LASTEXITCODE -ne 0) { throw 'Catalog signing failed' }
    $taskPublic = Get-Content (Join-Path $env:LOCALAPPDATA 'PrettyDeskSigning/catalog.public.base64') -Raw
    dotnet run --project tools/catalog-sign -c Release -- verify $CatalogPath --pub $taskPublic
    if ($LASTEXITCODE -ne 0) { throw 'Catalog verification failed' }
} finally {
    Remove-Item Env:PRETTYDESK_CATALOG_KEY -ErrorAction SilentlyContinue
    [Array]::Clear($taskPlainKey, 0, $taskPlainKey.Length)
}
