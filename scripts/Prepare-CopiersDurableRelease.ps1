#requires -Version 7.5
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PublishRoot,[Parameter(Mandatory)][string]$ArtifactRoot)
$ErrorActionPreference='Stop'
$sourceRoot=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$commit=(git -c maintenance.auto=false -c gc.auto=0 -C $sourceRoot rev-parse HEAD).Trim()
if(git -c maintenance.auto=false -c gc.auto=0 -C $sourceRoot status --porcelain){throw 'Candidate is not frozen.'}
$baseline='e840ccf28cfda348dad56c106bea2509be397766'
$deployment='3be5c7fcebab49139c8cf1516e06d5ca'
$dllHash='C91D57AC0D8F7CD267C3EF812020163FA193AE3A89A7BBAD8E30711FE7821FA7'
$manifestHash='843C80C4CB8E7655CA0AB375AE869A01D20400EC6D5CD0A174F76F223AB9D6B7'
$scm='https://calculadoradt-asduazh5e0bhhsgm.scm.eastus2-01.azurewebsites.net'
$subscription='7018b9b6-5dfc-4d91-bc4d-5f29f27553bd'
git -c maintenance.auto=false -c gc.auto=0 -C $sourceRoot merge-base --is-ancestor $baseline $commit
if($LASTEXITCODE){throw 'Candidate does not descend from production.'}
$version=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $PublishRoot 'CotizadorInterno.Web.dll')).ProductVersion
if($version -ne "1.0.0+$commit"){throw 'Publish does not identify candidate commit.'}
$releaseRoot=Join-Path $ArtifactRoot ($commit.Substring(0,8)+'-first-receipt')
if(Test-Path -LiteralPath $releaseRoot){throw 'Release directory already exists.'}
$package=Join-Path $releaseRoot 'package';$before=Join-Path $releaseRoot 'baseline';$rollback=Join-Path $releaseRoot 'rollback'
New-Item -ItemType Directory -Path $package,$before,$rollback -Force|Out-Null
$manifest='CotizadorInterno.Web.staticwebassets.endpoints.json'
$assets=@('wwwroot/js/copiers-mto-v2.js')
$names=@('CotizadorInterno.Web.dll','CotizadorInterno.Web.pdb',$manifest)+@($assets|ForEach-Object{$_;"$_.br";"$_.gz"})
$newNames=@()
$preserved=@('appsettings.json','web.config','CotizadorInterno.Web.deps.json','CotizadorInterno.Web.runtimeconfig.json','wwwroot/js/dashboard.js','wwwroot/js/support-cloud-surveys.js','wwwroot/js/copiers-mto-v2-picker.js','wwwroot/js/copiers-mto-v2-drafts.js','wwwroot/js/copiers-mto-v2-calendar.js','wwwroot/js/copiers-mto-v2-calendar.js.br','wwwroot/js/copiers-mto-v2-calendar.js.gz')
$token=az account get-access-token --subscription $subscription --resource https://management.azure.com/ --query accessToken -o tsv
if($LASTEXITCODE -or !$token){throw 'No Azure session.'}
$headers=@{Authorization='Bearer '+$token}
function Assert-Baseline {
    $active=@(((Invoke-WebRequest -Uri "$scm/api/deployments" -Headers $headers).Content|ConvertFrom-Json)|Where-Object active)
    if($active.Count -ne 1 -or $active[0].id -ne $deployment -or $active[0].status -ne 4){throw 'Production changed. Rebase before deploying.'}
}
function File-Manifest($root,$paths){@(foreach($name in $paths){$p=Join-Path $root $name;[ordered]@{Name=$name;Sha256=(Get-FileHash -LiteralPath $p).Hash;Length=(Get-Item -LiteralPath $p).Length}})}
Assert-Baseline
foreach($name in (@($names|Where-Object{$_ -notin $newNames})+$preserved|Sort-Object -Unique)){
    $destination=Join-Path $before $name;New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force|Out-Null
    Invoke-WebRequest -Uri "$scm/api/vfs/site/wwwroot/$name" -Headers $headers -OutFile $destination|Out-Null
}
if((Get-FileHash (Join-Path $before 'CotizadorInterno.Web.dll')).Hash -ne $dllHash -or (Get-FileHash (Join-Path $before $manifest)).Hash -ne $manifestHash){throw 'Live baseline bytes differ.'}
foreach($name in @('CotizadorInterno.Web.deps.json','CotizadorInterno.Web.runtimeconfig.json')){
    if((Get-FileHash (Join-Path $before $name)).Hash -ne (Get-FileHash (Join-Path $PublishRoot $name)).Hash){throw "Dependency drift: $name"}
}
foreach($name in $names){
    $dest=Join-Path $package $name;New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force|Out-Null
    Copy-Item -LiteralPath (Join-Path $PublishRoot $name) -Destination $dest
    if($name -notin $newNames){$dest=Join-Path $rollback $name;New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force|Out-Null;Copy-Item -LiteralPath (Join-Path $before $name) -Destination $dest}
}
$changed=@($names|Where-Object{$_ -like 'wwwroot/*'}|ForEach-Object{$_.Substring(8)})
$active=Get-Content -Raw (Join-Path $before $manifest)|ConvertFrom-Json -Depth 40
$candidate=Get-Content -Raw (Join-Path $PublishRoot $manifest)|ConvertFrom-Json -Depth 40
$keep=@($active.Endpoints|Where-Object{$_.AssetFile -notin $changed})
$keepJson=ConvertTo-Json -InputObject $keep -Depth 40 -Compress
$replacement=@($candidate.Endpoints|Where-Object{$_.AssetFile -in $changed})
if($replacement.Count -ne 10){throw 'Expected ten JavaScript endpoint variants.'}
$active.Endpoints=$keep+$replacement
# Generated artifact, not a source/configuration edit.
$active|ConvertTo-Json -Depth 40 -Compress|Set-Content -LiteralPath (Join-Path $package $manifest) -Encoding utf8
$reread=Get-Content -Raw (Join-Path $package $manifest)|ConvertFrom-Json -Depth 40
if((ConvertTo-Json -InputObject @($reread.Endpoints|Where-Object{$_.AssetFile -notin $changed}) -Depth 40 -Compress) -cne $keepJson){throw 'Unrelated endpoints changed.'}
foreach($asset in $assets){
    $sourceHash=(Get-FileHash (Join-Path $sourceRoot $asset)).Hash
    if((Get-FileHash (Join-Path $package $asset)).Hash -ne $sourceHash){throw 'JavaScript differs from source.'}
    foreach($suffix in @('.br','.gz')){
        $inputStream=[IO.File]::OpenRead((Join-Path $package ($asset+$suffix)))
        $decoder=if($suffix -eq '.br'){[IO.Compression.BrotliStream]::new($inputStream,[IO.Compression.CompressionMode]::Decompress)}else{[IO.Compression.GZipStream]::new($inputStream,[IO.Compression.CompressionMode]::Decompress)}
        try{if([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($decoder)) -ne $sourceHash){throw 'Compressed asset mismatch.'}}finally{$decoder.Dispose();$inputStream.Dispose()}
    }
}
Assert-Baseline;$headers=$null;$token=$null
$zip=Join-Path $releaseRoot 'production.zip';$undo=Join-Path $releaseRoot 'rollback.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($package,$zip);[IO.Compression.ZipFile]::CreateFromDirectory($rollback,$undo)
$result=[ordered]@{SourceCommit=$commit;SourceRoot=$sourceRoot;BaselineCommit=$baseline;BaselineDeploymentId=$deployment;BaselineDllSha256=$dllHash;ScmBaseUrl=$scm;SubscriptionId=$subscription;ResourceGroup='DigitalTechAppAI';AppName='calculadoradt';ZipPath=$zip;ZipSha256=(Get-FileHash $zip).Hash;RollbackZip=$undo;RollbackSha256=(Get-FileHash $undo).Hash;Files=(File-Manifest $package $names);BaselineFiles=(File-Manifest $before @($names|Where-Object{$_ -notin $newNames}));PreservedFiles=(File-Manifest $before $preserved);PreservedEndpoints=$keep.Count;UpdatedEndpoints=$replacement.Count;ConfigurationChanges=$false;RuntimeDependenciesMatchProduction=$true;CompressionVerified=$true}
$result|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $releaseRoot 'release-manifest.json') -Encoding utf8
[pscustomobject]$result|Select-Object SourceCommit,ZipPath,ZipSha256,PreservedEndpoints,UpdatedEndpoints|ConvertTo-Json
