#requires -Version 7.5
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ReleaseRoot)
$ErrorActionPreference='Stop'
$release=Get-Content -Raw (Join-Path $ReleaseRoot 'release-manifest.json')|ConvertFrom-Json -Depth 40
$scm='https://calculadoradt-asduazh5e0bhhsgm.scm.eastus2-01.azurewebsites.net'
$subscription='7018b9b6-5dfc-4d91-bc4d-5f29f27553bd'
if($release.ScmBaseUrl -ne $scm -or $release.SubscriptionId -ne $subscription -or $release.BaselineDeploymentId -ne '9e70bf8222b8454ab1466d1fd1b75e84' -or $release.Files.Count -ne 9 -or $release.ConfigurationChanges){throw 'Unapproved release manifest.'}
if((Get-FileHash $release.ZipPath).Hash -ne $release.ZipSha256 -or (Get-FileHash $release.RollbackZip).Hash -ne $release.RollbackSha256){throw 'Package changed.'}
if((git -c maintenance.auto=false -c gc.auto=0 -C $release.SourceRoot rev-parse HEAD).Trim() -ne $release.SourceCommit -or (git -c maintenance.auto=false -c gc.auto=0 -C $release.SourceRoot status --porcelain)){throw 'Source changed after validation.'}
$expected=@('CotizadorInterno.Web.dll','CotizadorInterno.Web.pdb','CotizadorInterno.Web.staticwebassets.endpoints.json','wwwroot/js/copiers-mto-v2.js','wwwroot/js/copiers-mto-v2.js.br','wwwroot/js/copiers-mto-v2.js.gz','wwwroot/js/copiers-mto-v2-calendar.js','wwwroot/js/copiers-mto-v2-calendar.js.br','wwwroot/js/copiers-mto-v2-calendar.js.gz')
$archive=[IO.Compression.ZipFile]::OpenRead($release.ZipPath)
try{
    if(Compare-Object @($archive.Entries.FullName|ForEach-Object{$_.Replace('\','/')}|Sort-Object) @($expected|Sort-Object)){throw 'Unexpected ZIP contents.'}
    foreach($entry in $archive.Entries){
        $file=@($release.Files|Where-Object Name -eq $entry.FullName.Replace('\','/'));$stream=$entry.Open()
        try{if($file.Count -ne 1 -or [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) -ne $file[0].Sha256){throw 'ZIP entry changed.'}}finally{$stream.Dispose()}
    }
}finally{$archive.Dispose()}
$token=az account get-access-token --subscription $subscription --resource https://management.azure.com/ --query accessToken -o tsv
if($LASTEXITCODE -or !$token){throw 'No Azure session.'}
$headers=@{Authorization='Bearer '+$token}
$readback=Join-Path $ReleaseRoot ('readback-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $readback|Out-Null
function Active-Deployment {
    $active=@(((Invoke-WebRequest -Uri "$scm/api/deployments" -Headers $headers).Content|ConvertFrom-Json)|Where-Object active)
    if($active.Count -ne 1 -or $active[0].status -ne 4){throw 'No unique successful active deployment.'}
    return $active[0]
}
function Check-Files($files,[string]$phase){
    foreach($file in $files){
        $dest=Join-Path $readback "$phase/$($file.Name)";New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force|Out-Null
        Invoke-WebRequest -Uri "$scm/api/vfs/site/wwwroot/$($file.Name)" -Headers $headers -OutFile $dest|Out-Null
        if((Get-FileHash $dest).Hash -ne $file.Sha256){throw "Read-back mismatch: $phase/$($file.Name)"}
    }
}
$offlineUrl="$scm/api/vfs/site/wwwroot/app_offline.htm"
$marker='copiers-durable-'+$release.SourceCommit.Substring(0,8)+'-'+[Guid]::NewGuid().ToString('N')
$offlineBody="<!doctype html><html><meta charset='utf-8'><p>Estamos actualizando la aplicación. Vuelve a cargar en un momento.</p><!-- $marker --></html>"
$owned=$false
try{
    if((Active-Deployment).id -ne $release.BaselineDeploymentId){throw 'Production changed. No deployment attempted.'}
    Check-Files $release.BaselineFiles 'before';Check-Files $release.PreservedFiles 'preserved-before'
    $existing=Invoke-WebRequest -Uri $offlineUrl -Headers $headers -SkipHttpErrorCheck
    if($existing.StatusCode -ne 404){throw 'Another maintenance marker exists.'}
    if((Active-Deployment).id -ne $release.BaselineDeploymentId){throw 'Production changed during preflight.'}
    $owned=$true
    Invoke-WebRequest -Uri $offlineUrl -Method Put -Headers @{Authorization='Bearer '+$token;'If-None-Match'='*'} -ContentType 'text/html; charset=utf-8' -Body $offlineBody|Out-Null
    $probeScript="try { `$s=[IO.File]::Open('C:\home\site\wwwroot\CotizadorInterno.Web.dll',[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None); `$s.Dispose(); 'UNLOCKED'; exit 0 } catch { 'LOCKED'; exit 1 }"
    $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($probeScript))
    $unlocked=$false
    for($attempt=0;$attempt -lt 15;$attempt++){
        $probe=Invoke-RestMethod -Uri "$scm/api/command" -Method Post -Headers $headers -ContentType application/json -Body (@{command="powershell.exe -NoProfile -NonInteractive -EncodedCommand $encoded";dir='C:\home\site\wwwroot'}|ConvertTo-Json)
        if($probe.ExitCode -eq 0 -and $probe.Output -match 'UNLOCKED'){$unlocked=$true;break}
        Start-Sleep -Seconds 2
    }
    if(!$unlocked){throw 'DLL still locked. No deployment attempted.'}
    Write-Output 'Deploying frozen nine-file package once; configuration is unchanged.'
    $raw=az webapp deploy --subscription $subscription --resource-group DigitalTechAppAI --name calculadoradt --src-path $release.ZipPath --type zip --clean false --restart true --async true --timeout 900000 -o json
    if($LASTEXITCODE){throw 'Deployment command uncertain/failed. Inspect operation before retrying.'}
    $raw|Set-Content -LiteralPath (Join-Path $ReleaseRoot 'deployment-result.json') -Encoding utf8
    $operation=($raw -join [Environment]::NewLine)|ConvertFrom-Json
    if($operation.id -notmatch '^[a-fA-F0-9]{32}$' -or $operation.id -eq $release.BaselineDeploymentId){throw 'Unexpected deployment operation.'}
    $success=$false
    for($attempt=0;$attempt -lt 90;$attempt++){
        $status=(Invoke-WebRequest -Uri "$scm/api/deployments/$($operation.id)" -Headers $headers).Content|ConvertFrom-Json
        if($status.status -eq 4){$success=$true;break}
        if($status.status -eq 3){throw 'Deployment failed. Do not retry blindly.'}
        Start-Sleep -Seconds 2
    }
    if(!$success -or (Active-Deployment).id -ne $operation.id){throw 'Deployment completion not independently confirmed.'}
    Check-Files $release.Files 'after';Check-Files $release.PreservedFiles 'preserved-after'
    [ordered]@{SourceCommit=$release.SourceCommit;DeploymentId=$operation.id;ZipSha256=$release.ZipSha256;RuntimeFilesVerified=$release.Files.Count;PreservedFilesVerified=$release.PreservedFiles.Count;ConfigurationUntouched=$true;ReadbackRoot=$readback;Utc=[DateTimeOffset]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $ReleaseRoot 'deployment-verified.json') -Encoding utf8
    Get-Content (Join-Path $ReleaseRoot 'deployment-verified.json')
}finally{
    if($owned){
        $current=Invoke-WebRequest -Uri $offlineUrl -Headers $headers -SkipHttpErrorCheck
        if($current.StatusCode -eq 200 -and ([string]$current.Content).Contains($marker)){
            Invoke-WebRequest -Uri $offlineUrl -Method Delete -Headers @{Authorization='Bearer '+$token;'If-Match'='*'}|Out-Null
            Write-Output 'Removed only this deployment maintenance marker.'
        }elseif($current.StatusCode -ne 404){Write-Warning 'Maintenance marker ownership changed; not removed.'}
    }
    $headers=$null;$token=$null
}
