#requires -Version 7.5
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PublishRoot,[Parameter(Mandatory)][string]$ArtifactRoot)
$ErrorActionPreference='Stop'
$sourceRoot=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$commit=(git -c maintenance.auto=false -c gc.auto=0 -C $sourceRoot rev-parse HEAD).Trim()
if(git -c maintenance.auto=false -c gc.auto=0 -C $sourceRoot status --porcelain){throw 'Candidate is not frozen.'}
$baseline='12002bfa52344baa804133efe6e397c1f4c704ce'
$deployment='914bc93575654c7882f65b4d228fea46'
$dllHash='E325B5A28018E33500BC85E6472287E4B1BB1FDC7E80E22418123C50065EE40E'
$manifestHash='6A944C06FD67629A9ED208238E760E58C43A8B9725945FBF3A9A60A5CAECB710'
$scm='https://calculadoradt-asduazh5e0bhhsgm.scm.eastus2-01.azurewebsites.net'
$subscription='7018b9b6-5dfc-4d91-bc4d-5f29f27553bd'
git -c maintenance.auto=false -c gc.auto=0 -C $sourceRoot merge-base --is-ancestor $baseline $commit
if($LASTEXITCODE){throw 'Candidate does not descend from production.'}
$version=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $PublishRoot 'CotizadorInterno.Web.dll')).ProductVersion
if($version -ne "1.0.0+$commit"){throw 'Publish does not identify candidate commit.'}
$releaseRoot=Join-Path $ArtifactRoot ($commit.Substring(0,8)+'-equipment-operations')
if(Test-Path -LiteralPath $releaseRoot){throw 'Release directory already exists.'}
$package=Join-Path $releaseRoot 'package';$before=Join-Path $releaseRoot 'baseline';$rollback=Join-Path $releaseRoot 'rollback'
New-Item -ItemType Directory -Path $package,$before,$rollback -Force|Out-Null
$manifest='CotizadorInterno.Web.staticwebassets.endpoints.json'
$assets=@('wwwroot/js/copiers-mto-v2.js','wwwroot/js/copiers-mto-v2-calendar.js','wwwroot/js/copiers-equipment-operations.js','wwwroot/css/copiers-equipment-operations.css')
$names=@('CotizadorInterno.Web.dll','CotizadorInterno.Web.pdb',$manifest,'appsettings.json')+@($assets|ForEach-Object{$_;"$_.br";"$_.gz"})
$newNames=@($assets|Where-Object {$_ -like '*equipment-operations*'}|ForEach-Object {$_;"$_.br";"$_.gz"})
$preserved=@('web.config','CotizadorInterno.Web.deps.json','CotizadorInterno.Web.runtimeconfig.json','wwwroot/js/dashboard.js','wwwroot/js/support-cloud-surveys.js','wwwroot/js/copiers-mto-v2-picker.js','wwwroot/js/copiers-mto-v2-drafts.js','wwwroot/css/copiers-mto-v2.css')
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
    Copy-Item -LiteralPath (Join-Path $(if($name -eq 'appsettings.json'){$before}else{$PublishRoot}) $name) -Destination $dest
    if($name -notin $newNames){$dest=Join-Path $rollback $name;New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force|Out-Null;Copy-Item -LiteralPath (Join-Path $before $name) -Destination $dest}
}
# Add only the confirmed feature section to the production configuration.
$configPath=Join-Path $package 'appsettings.json'
$baseConfig=Get-Content -Raw (Join-Path $before 'appsettings.json')|ConvertFrom-Json -AsHashtable -Depth 100
if($baseConfig.Contains('CopiersEquipmentOperations')){throw 'Feature configuration already exists. Reconcile it explicitly.'}
$coreJson=ConvertTo-Json -InputObject $baseConfig -Depth 100 -Compress
$baseConfig['CopiersEquipmentOperations']=[ordered]@{Enabled=$true;InternalClientIds=@('fd509c25-713e-f111-88b4-6045bd38ece5','52abf9ea-5ba4-f011-bbd2-6045bd382f27')}
$baseConfig|ConvertTo-Json -Depth 100|Set-Content -LiteralPath $configPath -Encoding utf8
$verifyConfig=Get-Content -Raw $configPath|ConvertFrom-Json -AsHashtable -Depth 100
$verifyConfig.Remove('CopiersEquipmentOperations')
if((ConvertTo-Json -InputObject $verifyConfig -Depth 100 -Compress) -cne $coreJson){throw 'Unrelated configuration changed.'}

$changed=@($names|Where-Object{$_ -like 'wwwroot/*'}|ForEach-Object{$_.Substring(8)})
$active=Get-Content -Raw (Join-Path $before $manifest)|ConvertFrom-Json -Depth 40
$candidate=Get-Content -Raw (Join-Path $PublishRoot $manifest)|ConvertFrom-Json -Depth 40
$keep=@($active.Endpoints|Where-Object{$_.AssetFile -notin $changed})
$keepJson=ConvertTo-Json -InputObject $keep -Depth 40 -Compress
$replacement=@($candidate.Endpoints|Where-Object{$_.AssetFile -in $changed})
if($replacement.Count -ne 40){throw 'Expected forty JS/CSS endpoint variants.'}
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
$result=[ordered]@{SourceCommit=$commit;SourceRoot=$sourceRoot;BaselineCommit=$baseline;BaselineDeploymentId=$deployment;BaselineDllSha256=$dllHash;ScmBaseUrl=$scm;SubscriptionId=$subscription;ResourceGroup='DigitalTechAppAI';AppName='calculadoradt';ZipPath=$zip;ZipSha256=(Get-FileHash $zip).Hash;RollbackZip=$undo;RollbackSha256=(Get-FileHash $undo).Hash;Files=(File-Manifest $package $names);BaselineFiles=(File-Manifest $before @($names|Where-Object{$_ -notin $newNames}));PreservedFiles=(File-Manifest $before $preserved);PreservedEndpoints=$keep.Count;UpdatedEndpoints=$replacement.Count;ConfigurationChanges=$true;ConfigurationScope='CopiersEquipmentOperations-only';RuntimeDependenciesMatchProduction=$true;CompressionVerified=$true}
$result|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $releaseRoot 'release-manifest.json') -Encoding utf8
[pscustomobject]$result|Select-Object SourceCommit,ZipPath,ZipSha256,PreservedEndpoints,UpdatedEndpoints|ConvertTo-Json
