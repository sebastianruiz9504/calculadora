#requires -Version 7.4
[CmdletBinding()]
param([switch]$Apply,[switch]$Enable,[string]$ExportPath='')
$ErrorActionPreference='Stop'
$environment='Default-cab7ea42-4a21-4548-952f-fcde81f2bdd6'
$sourceId='66e7671c-17d4-4f82-b0f3-59070c0a00b8'
$displayName='Copiers Actividades V2 - Enviar acta firmada'
$base="https://api.flow.microsoft.com/providers/Microsoft.ProcessSimple/environments/$environment"
function Get-CanonicalValue($Value) {
    if ($Value -is [System.Collections.IDictionary]) {
        $ordered=[ordered]@{}
        foreach ($key in @($Value.Keys | Sort-Object -CaseSensitive)) { $ordered[$key]=Get-CanonicalValue $Value[$key] }
        return ,$ordered
    }
    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        return ,@(foreach ($item in $Value) { Get-CanonicalValue $item })
    }
    return $Value
}
function Get-CanonicalJson($Value) {
    $copy=$Value | ConvertTo-Json -Depth 100 -Compress | ConvertFrom-Json -Depth 100 -AsHashtable -DateKind String
    return ConvertTo-Json -InputObject (Get-CanonicalValue $copy) -Depth 100 -Compress
}
function Get-ConnectionContract($Flow) {
    $refs=$Flow.properties.connectionReferences | ConvertTo-Json -Depth 30 -Compress | ConvertFrom-Json -AsHashtable
    $result=[ordered]@{}
    foreach ($key in @($refs.Keys | Sort-Object -CaseSensitive)) {
        $r=$refs[$key]
        $result[$key]=@{connectionName=$r.connectionName;id=$r.id;source=$r.source}
    }
    return Get-CanonicalJson $result
}
function Assert-ActivityFlow($Flow) {
    if ($Flow.properties.displayName -ne $displayName -or $Flow.properties.state -notin @('Started','Stopped') -or
        (Get-CanonicalJson $Flow.properties.definition) -cne $expectedDefinition -or (Get-ConnectionContract $Flow) -cne $expectedConnections) {
        throw 'New activity flow definition, connection identity or state differs from the approved clone.'
    }
}
$token=az account get-access-token --resource https://service.flow.microsoft.com/ --query accessToken -o tsv
if ($LASTEXITCODE -ne 0 -or -not $token) { throw 'Existing Flow authentication unavailable.' }
$headers=@{Authorization='Bearer '+$token}
$source=Invoke-RestMethod -Uri "$base/flows/$($sourceId)?api-version=2016-11-01" -Headers $headers
if ($source.properties.state -ne 'Started' -or $source.properties.displayName -ne 'Copiers MTO V2 - Enviar reporte firmado') { throw 'The verified original MTO flow is not active.' }
$sourceJson=Get-CanonicalJson $source.properties.definition
$expectedConnections=Get-ConnectionContract $source
$sourceHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($sourceJson)))
if ($sourceJson -notmatch 'Germanruiz@digitaltechcolombia.com;soportecopiers@digitaltechcolombia.com' -or $sourceJson -notmatch 'SendEmailV2' -or $sourceJson -notmatch 'dtc_copiersmtov2') { throw 'Original delivery contract has drifted.' }
$definitionJson=$sourceJson.Replace('dtc_copiersmtoevidenciav2','dtc_copiersactivityevidencev2').Replace('dtc_copiersmtov2','dtc_copiersactivityv2').Replace('dtc_signedmto','dtc_signedactivity')
if ($definitionJson -match 'dtc_copiersmto|dtc_latitude|dtc_longitude|dtc_internalnotes|dtc_location') { throw 'New flow includes an old-table or internal-location reference.' }
$definition=$definitionJson | ConvertFrom-Json -Depth 100 -AsHashtable
$expectedDefinition=Get-CanonicalJson $definition
$refs=$source.properties.connectionReferences
if ($refs.shared_office365.connectionName -ne 'shared-office365-087e1413-772f-4e38-8ee3-9b0eb77c0d31') { throw 'Original email connection changed.' }
$allFlows=@(); $pageUrl="$base/flows?api-version=2016-11-01"
while ($pageUrl) {
    $uri=[uri]$pageUrl
    if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'api.flow.microsoft.com' -or -not $uri.AbsolutePath.StartsWith("/providers/Microsoft.ProcessSimple/environments/$environment/flows",[StringComparison]::Ordinal)) { throw 'Untrusted flow pagination URL.' }
    $page=Invoke-RestMethod -Uri $pageUrl -Headers $headers
    $allFlows+=@($page.value)
    $pageUrl=if ($page.nextLink) { [string]$page.nextLink } elseif ($page.'@odata.nextLink') { [string]$page.'@odata.nextLink' } else { '' }
}
$existing=@($allFlows | Where-Object {$_.properties.displayName -eq $displayName})
if ($existing.Count -gt 1) { throw 'Ambiguous existing activity flows.' }
if (-not $Apply) {
    @{Mode='Plan';SourceFlowId=$sourceId;SourceDefinitionSha256=$sourceHash;Existing=$existing.Count;Name=$displayName;OriginalUntouched=$true;SenderConnectionUnchanged=$true} | ConvertTo-Json
    return
}
if ($existing.Count -eq 1) {
    $created=Invoke-RestMethod -Uri "$base/flows/$($existing[0].name)?api-version=2016-11-01" -Headers $headers
    Assert-ActivityFlow $created
} else {
    $properties=@{displayName=$displayName;state='Stopped';definition=$definition;connectionReferences=$refs}
    # Never retry an uncertain create blindly. Reconcile the unique display name first.
    $created=Invoke-RestMethod -Method Post -Uri "$base/flows?api-version=2016-11-01" -Headers $headers -ContentType 'application/json' -Body (@{properties=$properties} | ConvertTo-Json -Depth 100)
}
$id=$created.name
if (-not $id) { throw 'No activity flow ID was returned; reconcile before retrying.' }
$readBack=Invoke-RestMethod -Uri "$base/flows/$($id)?api-version=2016-11-01" -Headers $headers
Assert-ActivityFlow $readBack
if ($readBack.properties.definition.triggers.When_signed_report_is_ready.inputs.parameters.'subscriptionRequest/entityname' -ne 'dtc_copiersactivityv2' -or $readBack.properties.definition.triggers.When_signed_report_is_ready.runtimeConfiguration.concurrency.runs -ne 1) { throw 'Activity delivery trigger verification failed.' }
if ($Enable -and $readBack.properties.state -ne 'Started') {
    [void](Invoke-RestMethod -Method Post -Uri "$base/flows/$id/start?api-version=2016-11-01" -Headers $headers)
    $readBack=Invoke-RestMethod -Uri "$base/flows/$($id)?api-version=2016-11-01" -Headers $headers
    Assert-ActivityFlow $readBack
    if ($readBack.properties.state -ne 'Started') { throw 'New activity flow did not start.' }
}
$originalAgain=Invoke-RestMethod -Uri "$base/flows/$($sourceId)?api-version=2016-11-01" -Headers $headers
$currentJson=Get-CanonicalJson $originalAgain.properties.definition
$currentHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($currentJson)))
if ($currentHash -ne $sourceHash -or (Get-ConnectionContract $originalAgain) -cne $expectedConnections -or $originalAgain.properties.state -ne 'Started') { throw 'The original flow changed during provisioning; reconcile before deploying app.' }
$evidence=@{FlowId=$id;Name=$displayName;State=$readBack.properties.state;OriginalFlowId=$sourceId;OriginalDefinitionSha256=$sourceHash;OriginalUntouched=$true;SenderConnectionUnchanged=$true;CustomerSendTested=$false}
if ($ExportPath) { $evidence | ConvertTo-Json -Depth 10 | Out-File -LiteralPath $ExportPath -Encoding utf8 }
$evidence | ConvertTo-Json -Depth 10
