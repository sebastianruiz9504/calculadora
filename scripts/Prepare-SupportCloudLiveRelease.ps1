#requires -Version 7.5
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishRoot,
    [Parameter(Mandatory)][string]$ArtifactRoot,
    [Parameter(Mandatory)][uri]$ScmBaseUrl,
    [Parameter(Mandatory)][string]$SubscriptionId,
    [string]$SourceRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ValidationEvidencePath
)

$ErrorActionPreference = 'Stop'
$baselineCommit = 'c9a4244e31bbddad7749ce18068123b4e7544eaa'
$baselineDeployment = '740aae7050b14d318e17580bf931d8b4'
$baselineDllHash = '57F926CD51C39A9700AE18215FC611C9626F68D561C53F13136C93787389C46A'
$baselineManifestHash = '4CEF5A6EA04A508A369EE17561877DE6D44DDDC5A3F70B05C3E7E4888BA7D551'
$manifestName = 'CotizadorInterno.Web.staticwebassets.endpoints.json'
$assetName = 'wwwroot/js/support-cloud-surveys.js'
$names = @('CotizadorInterno.Web.dll', 'CotizadorInterno.Web.pdb', $manifestName,
    $assetName, ($assetName + '.br'), ($assetName + '.gz'))
$runtimeNames = @('CotizadorInterno.Web.deps.json', 'CotizadorInterno.Web.runtimeconfig.json')
$changedAssetFiles = @($names | Where-Object { $_.StartsWith('wwwroot/') } | ForEach-Object { $_.Substring(8) })

if ($ScmBaseUrl.Scheme -ne 'https' -or $ScmBaseUrl.UserInfo -or $ScmBaseUrl.Query -or $ScmBaseUrl.Fragment) {
    throw 'Supply an HTTPS SCM base URL without credentials, query or fragment.'
}
$scm = $ScmBaseUrl.AbsoluteUri.TrimEnd('/')
$SourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
$PublishRoot = (Resolve-Path -LiteralPath $PublishRoot).Path
$ArtifactRoot = [IO.Path]::GetFullPath($ArtifactRoot)
$sourceCommit = (git -C $SourceRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the candidate commit.' }
$sourceStatus = git -C $SourceRoot status --porcelain
if ($LASTEXITCODE -ne 0 -or $sourceStatus) { throw 'Source must be committed and clean before packaging.' }
git -C $SourceRoot merge-base --is-ancestor $baselineCommit $sourceCommit
if ($LASTEXITCODE -ne 0) { throw 'Candidate must descend from the active c9a4244 production commit.' }
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $PublishRoot 'CotizadorInterno.Web.dll')).ProductVersion
if ($version -ne ('1.0.0+' + $sourceCommit)) { throw 'Published DLL does not identify the frozen source commit.' }
foreach ($name in ($names + $runtimeNames)) {
    if (-not (Test-Path -LiteralPath (Join-Path $PublishRoot $name) -PathType Leaf)) { throw ('Missing published file: ' + $name) }
}
if ((Get-FileHash -LiteralPath (Join-Path $SourceRoot $assetName)).Hash -ne
    (Get-FileHash -LiteralPath (Join-Path $PublishRoot $assetName)).Hash) {
    throw 'Published survey JavaScript differs from the frozen source.'
}

# Each run owns fresh directories. Existing evidence is never overwritten or deleted.
$releaseRoot = Join-Path $ArtifactRoot ($sourceCommit.Substring(0, 8) + '-support-cloud-live')
if (Test-Path -LiteralPath $releaseRoot) { throw ('Release directory already exists: ' + $releaseRoot) }
$packageRoot = Join-Path $releaseRoot 'package'
$baselineRoot = Join-Path $releaseRoot 'baseline'
$rollbackRoot = Join-Path $releaseRoot 'rollback-package'
New-Item -ItemType Directory -Path $packageRoot, $baselineRoot, $rollbackRoot -Force | Out-Null

$managementToken = az account get-access-token --subscription $SubscriptionId --resource 'https://management.azure.com/' --query accessToken -o tsv
if ($LASTEXITCODE -ne 0 -or -not $managementToken) { throw 'No existing Azure authentication is available.' }
$headers = @{ Authorization = 'Bearer ' + $managementToken }
function Assert-ActiveBaseline {
    $deployments = (Invoke-WebRequest -Uri ($scm + '/api/deployments') -Headers $headers -TimeoutSec 45).Content |
        ConvertFrom-Json -DateKind String
    $active = @($deployments | Where-Object { $_.active })
    if ($active.Count -ne 1 -or $active[0].id -ne $baselineDeployment -or $active[0].status -ne 4) {
        throw 'Active production changed; rebuild and review the release baseline.'
    }
}
Assert-ActiveBaseline
foreach ($name in ($names + $runtimeNames)) {
    $destination = Join-Path $baselineRoot $name
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Invoke-WebRequest -Uri ($scm + '/api/vfs/site/wwwroot/' + $name) -Headers $headers -OutFile $destination -TimeoutSec 60 | Out-Null
}
if ((Get-FileHash -LiteralPath (Join-Path $baselineRoot 'CotizadorInterno.Web.dll')).Hash -ne $baselineDllHash) {
    throw 'Live DLL differs from the independently verified c9a4244 baseline.'
}
if ((Get-FileHash -LiteralPath (Join-Path $baselineRoot $manifestName)).Hash -ne $baselineManifestHash) {
    throw 'Live static endpoint manifest differs from the c9a4244 baseline.'
}
foreach ($name in $runtimeNames) {
    if ((Get-FileHash -LiteralPath (Join-Path $baselineRoot $name)).Hash -ne
        (Get-FileHash -LiteralPath (Join-Path $PublishRoot $name)).Hash) {
        throw ('Runtime dependency drift is outside this delta: ' + $name)
    }
}
foreach ($name in $names) {
    $destination = Join-Path $packageRoot $name
    $rollbackDestination = Join-Path $rollbackRoot $name
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination), (Split-Path -Parent $rollbackDestination) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PublishRoot $name) -Destination $destination
    Copy-Item -LiteralPath (Join-Path $baselineRoot $name) -Destination $rollbackDestination
}

# Only replace the survey's three physical assets and their ten route/encoding entries.
# All 415 other deployed assets (including the latest Copiers release) retain exact endpoint metadata.
$activeManifest = Get-Content -Raw -LiteralPath (Join-Path $baselineRoot $manifestName) | ConvertFrom-Json -Depth 30 -DateKind String
$newManifest = Get-Content -Raw -LiteralPath (Join-Path $PublishRoot $manifestName) | ConvertFrom-Json -Depth 30 -DateKind String
if ($activeManifest.Version -ne $newManifest.Version -or $activeManifest.ManifestType -ne $newManifest.ManifestType) {
    throw 'Static manifest format changed; a narrow endpoint merge is not applicable.'
}
$baselineAssetFiles = @($activeManifest.Endpoints.AssetFile | Sort-Object -Unique)
if ($baselineAssetFiles.Count -ne 418 -or $activeManifest.Endpoints.Count -ne 1356) {
    throw 'Unexpected c9a4244 static asset inventory.'
}
$preservedEndpoints = @($activeManifest.Endpoints | Where-Object { $_.AssetFile -notin $changedAssetFiles })
$preservedJson = ConvertTo-Json -InputObject $preservedEndpoints -Depth 30 -Compress
$updatedEndpoints = @($newManifest.Endpoints | Where-Object { $_.AssetFile -in $changedAssetFiles })
if ($updatedEndpoints.Count -ne 10 -or
    (Compare-Object @($updatedEndpoints.AssetFile | Sort-Object -Unique) @($changedAssetFiles | Sort-Object))) {
    throw 'Published manifest must provide all ten survey route/encoding entries.'
}
$activeManifest.Endpoints = @($preservedEndpoints) + @($updatedEndpoints)
$activeManifest | ConvertTo-Json -Depth 30 -Compress |
    Set-Content -LiteralPath (Join-Path $packageRoot $manifestName) -Encoding utf8
$packagedManifest = Get-Content -Raw -LiteralPath (Join-Path $packageRoot $manifestName) | ConvertFrom-Json -Depth 30 -DateKind String
$readBackPreserved = @($packagedManifest.Endpoints | Where-Object { $_.AssetFile -notin $changedAssetFiles })
if ((ConvertTo-Json -InputObject $readBackPreserved -Depth 30 -Compress) -cne $preservedJson) {
    throw 'Unrelated static endpoint metadata changed.'
}
if (Compare-Object $baselineAssetFiles @($packagedManifest.Endpoints.AssetFile | Sort-Object -Unique)) {
    throw 'The packaged manifest changes the physical static asset inventory.'
}
foreach ($endpoint in $updatedEndpoints) {
    $file = Join-Path $packageRoot ('wwwroot/' + $endpoint.AssetFile)
    $length = @($endpoint.ResponseHeaders | Where-Object { $_.Name -eq 'Content-Length' })[0].Value
    if ([long]$length -ne (Get-Item -LiteralPath $file).Length) { throw ('Endpoint length mismatch: ' + $endpoint.AssetFile) }
    $etag = @($endpoint.ResponseHeaders | Where-Object { $_.Name -eq 'ETag' })[0].Value
    $hash = [Convert]::ToBase64String([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($file)))
    if ($etag -cne ('"' + $hash + '"')) { throw ('Endpoint ETag mismatch: ' + $endpoint.AssetFile) }
}
$plainBytes = [IO.File]::ReadAllBytes((Join-Path $packageRoot $assetName))
$plainHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($plainBytes))
foreach ($extension in @('.br', '.gz')) {
    $inputStream = [IO.File]::OpenRead((Join-Path $packageRoot ($assetName + $extension)))
    $decoder = if ($extension -eq '.br') {
        [IO.Compression.BrotliStream]::new($inputStream, [IO.Compression.CompressionMode]::Decompress)
    } else {
        [IO.Compression.GZipStream]::new($inputStream, [IO.Compression.CompressionMode]::Decompress)
    }
    try {
        if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($decoder)) -ne $plainHash) {
            throw ('Compressed survey asset does not match its source: ' + $extension)
        }
    } finally {
        $decoder.Dispose()
        $inputStream.Dispose()
    }
}
Assert-ActiveBaseline
$headers = $null
$managementToken = $null

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipPath = Join-Path $releaseRoot ('CotizadorInterno-' + $sourceCommit.Substring(0, 8) + '-support-cloud-live.zip')
$rollbackPath = Join-Path $releaseRoot 'rollback-c9a4244.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($packageRoot, $zipPath)
[IO.Compression.ZipFile]::CreateFromDirectory($rollbackRoot, $rollbackPath)
foreach ($archivePath in @($zipPath, $rollbackPath)) {
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $entries = @($archive.Entries.FullName | ForEach-Object { $_.Replace('\', '/') })
        if ($entries.Count -ne 6 -or (Compare-Object ($entries | Sort-Object) ($names | Sort-Object))) {
            throw 'Unexpected ZIP inventory; configurations and dependency files must remain excluded.'
        }
    } finally { $archive.Dispose() }
}
$validationEvidence = if ($ValidationEvidencePath) {
    $resolvedEvidence = (Resolve-Path -LiteralPath $ValidationEvidencePath).Path
    [ordered]@{ Path = $resolvedEvidence; Sha256 = (Get-FileHash -LiteralPath $resolvedEvidence).Hash }
} else { $null }
$releaseManifest = [ordered]@{
    SourceCommit = $sourceCommit; BaselineCommit = $baselineCommit; BaselineDeploymentId = $baselineDeployment
    ProductVersion = $version; ZipPath = $zipPath; ZipSha256 = (Get-FileHash -LiteralPath $zipPath).Hash
    Files = @($names | ForEach-Object { [ordered]@{ Name = $_; Sha256 = (Get-FileHash -LiteralPath (Join-Path $packageRoot $_)).Hash } })
    RuntimeDependenciesMatchProduction = $true; ConfigurationChanges = $false
    BaselineStaticAssets = $baselineAssetFiles.Count; PreservedStaticAssets = 415
    PreservedStaticEndpoints = $preservedEndpoints.Count; UpdatedStaticEndpoints = $updatedEndpoints.Count
    SurveyCompressionVerified = $true; ValidationEvidence = $validationEvidence
    RollbackZip = $rollbackPath; RollbackSha256 = (Get-FileHash -LiteralPath $rollbackPath).Hash
}
$releaseManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $releaseRoot 'release-manifest.json') -Encoding utf8
$releaseManifest | ConvertTo-Json -Depth 8
