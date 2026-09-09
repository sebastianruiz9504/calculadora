#Requires -Version 7.5
[CmdletBinding()]
param(
    [switch]$Apply,
    [string]$ExpectedLastModifiedTime = '',
    [string]$ExpectedSnapshotSha256 = '',
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
$environmentName = 'Default-cab7ea42-4a21-4548-952f-fcde81f2bdd6'
$flowId = '66e7671c-17d4-4f82-b0f3-59070c0a00b8'
$flowDisplayName = 'Copiers MTO V2 - Enviar reporte firmado'
$expectedSender = 'sruiz@digitaltechcolombia.com'
$expectedOutlookConnection = 'shared-office365-087e1413-772f-4e38-8ee3-9b0eb77c0d31'
$requiredCc = 'Germanruiz@digitaltechcolombia.com;soportecopiers@digitaltechcolombia.com'
$flowUri = "https://api.flow.microsoft.com/providers/Microsoft.ProcessSimple/environments/$environmentName/flows/$($flowId)?api-version=2016-11-01"
$runsUri = "https://api.flow.microsoft.com/providers/Microsoft.ProcessSimple/environments/$environmentName/flows/$flowId/runs?api-version=2016-11-01&`$top=100"

function Copy-CopiersJson($Value) {
    return ConvertFrom-Json -InputObject (ConvertTo-Json -InputObject $Value -Depth 100 -Compress) -AsHashtable -DateKind String
}

function ConvertTo-CopiersCanonicalValue($Value) {
    if ($Value -is [System.Collections.IDictionary]) {
        $result = [ordered]@{}
        foreach ($key in @($Value.Keys | Sort-Object -CaseSensitive)) {
            $result[$key] = ConvertTo-CopiersCanonicalValue $Value[$key]
        }
        return $result
    }
    if ($Value -is [array]) {
        # Avoid pipeline flattening or dropping null elements in JSON arrays.
        $items = [System.Collections.Generic.List[object]]::new()
        foreach ($entry in $Value) { $items.Add((ConvertTo-CopiersCanonicalValue $entry)) }
        return ,$items.ToArray()
    }
    return $Value
}

function Get-CopiersJsonHash($Value) {
    $json = ConvertTo-Json -InputObject (ConvertTo-CopiersCanonicalValue $Value) -Depth 100 -Compress
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($json)))
}

function Find-CopiersSendAction($Node, [string]$Path = 'definition') {
    if ($Node -is [System.Collections.IDictionary]) {
        if ($Node.inputs.host.operationId -eq 'SendEmailV2') {
            [pscustomobject]@{ Path = $Path; Action = $Node }
        }
        foreach ($key in $Node.Keys) { Find-CopiersSendAction $Node[$key] "$Path/$key" }
    }
    elseif ($Node -is [array]) {
        for ($index = 0; $index -lt $Node.Count; $index++) { Find-CopiersSendAction $Node[$index] "$Path/$index" }
    }
}

function Get-CopiersEditableSnapshot($Flow) {
    return [ordered]@{
        displayName = $Flow.properties.displayName
        state = $Flow.properties.state
        definition = $Flow.properties.definition
        connectionReferences = $Flow.properties.connectionReferences
    }
}

function Assert-CopiersFlowScope($Flow) {
    if ($Flow.name -ne $flowId -or $Flow.properties.displayName -ne $flowDisplayName) { throw 'Unexpected flow identity. Nothing was changed.' }
    if ($Flow.properties.state -ne 'Started') { throw 'The exact V2 flow must already be Started. Nothing was changed.' }
    if ([string]::IsNullOrWhiteSpace($Flow.properties.lastModifiedTime)) { throw 'Missing flow version marker. Nothing was changed.' }
    if ($Flow.properties.connectionReferences.shared_office365.connectionName -ne $expectedOutlookConnection) { throw 'Outlook connection changed. Nothing was changed.' }
    if ($Flow.properties.definition.triggers.When_signed_report_is_ready.runtimeConfiguration.concurrency.runs -ne 1) { throw 'Flow serialization changed. Nothing was changed.' }
    $actions = @(Find-CopiersSendAction $Flow.properties.definition)
    if ($actions.Count -ne 1) { throw 'Expected exactly one SendEmailV2 action. Nothing was changed.' }
    $action = $actions[0].Action
    if ($action.inputs.host.connectionName -ne 'shared_office365' -or $action.inputs.host.apiId -ne '/providers/Microsoft.PowerApps/apis/shared_office365') { throw 'Unexpected sending connector. Nothing was changed.' }
    if ($action.inputs.retryPolicy.type -ne 'none') { throw 'Email retry policy changed. Nothing was changed.' }
    if (-not [string]::IsNullOrWhiteSpace($action.inputs.parameters.'emailMessage/From')) { throw 'An explicit From override exists. Nothing was changed.' }
    $cc = [string]$action.inputs.parameters.'emailMessage/Cc'
    if (-not [string]::IsNullOrWhiteSpace($cc) -and $cc -cne $requiredCc) { throw 'Unexpected existing CC recipients. Review before changing them.' }
    return $actions[0]
}

function New-CopiersCcPlan($Flow) {
    $originalSend = Assert-CopiersFlowScope $Flow
    $snapshot = Get-CopiersEditableSnapshot $Flow
    $candidate = Copy-CopiersJson $snapshot
    $candidateSend = @(Find-CopiersSendAction $candidate.definition)[0]
    $candidateSend.Action.inputs.parameters['emailMessage/Cc'] = $requiredCc

    # Removing only CC must produce identical definitions: catches accidental broad edits.
    $beforeWithoutCc = Copy-CopiersJson $snapshot
    $afterWithoutCc = Copy-CopiersJson $candidate
    foreach ($copy in @($beforeWithoutCc, $afterWithoutCc)) {
        $send = @(Find-CopiersSendAction $copy.definition)[0]
        $send.Action.inputs.parameters.Remove('emailMessage/Cc') | Out-Null
    }
    if ((Get-CopiersJsonHash $beforeWithoutCc) -cne (Get-CopiersJsonHash $afterWithoutCc)) { throw 'Refusing a change beyond the approved CC field.' }
    return [pscustomobject]@{
        Candidate = $candidate
        SnapshotSha256 = Get-CopiersJsonHash $snapshot
        CandidateSha256 = Get-CopiersJsonHash $candidate
        LastModifiedTime = $Flow.properties.lastModifiedTime
        ChangedPath = "$($originalSend.Path)/inputs/parameters/emailMessage~1Cc"
        AlreadyConfigured = ([string]$originalSend.Action.inputs.parameters.'emailMessage/Cc' -ceq $requiredCc)
    }
}

function Read-CopiersFlow($Headers) {
    $response = Invoke-WebRequest -Method Get -Uri $flowUri -Headers $Headers -MaximumRetryCount 0
    $flow = ConvertFrom-Json -InputObject $response.Content -AsHashtable -DateKind String
    $etag = [string]($response.Headers.ETag | Select-Object -First 1)
    if (-not $etag) { $etag = [string]$flow.etag }
    return [pscustomobject]@{ Flow = $flow; ETag = $etag }
}

function Assert-CopiersSenderConnection($Connections) {
    $actual = @($Connections.value | Where-Object name -eq $expectedOutlookConnection)
    if ($actual.Count -ne 1 -or $actual[0].properties.accountName -ne $expectedSender -or $actual[0].properties.statuses.status -notcontains 'Connected') {
        throw 'The existing Outlook connection is not Connected as the expected sender. Nothing was changed.'
    }
}

function Get-CopiersRunSummary($Runs) {
    # A partial history cannot prove that an older active run does not exist.
    # Fail closed rather than expose or follow run-output/callback URLs.
    if ($Runs.nextLink -or $Runs.'@odata.nextLink') { throw 'Run history is paginated; cannot safely prove the flow is idle with this bounded operation.' }
    $items = @($Runs.value)
    $terminalStates = @('Succeeded', 'Failed', 'Cancelled', 'Skipped', 'TimedOut', 'Aborted', 'Faulted', 'Ignored')
    $active = @($items | Where-Object { $_.properties.status -notin $terminalStates })
    return [pscustomobject]@{ RunCount = $items.Count; ActiveRunCount = $active.Count }
}

function Read-CopiersRunSummary($Headers) {
    $runs = Invoke-RestMethod -Method Get -Uri $runsUri -Headers $Headers -MaximumRetryCount 0
    return Get-CopiersRunSummary $runs
}

function Assert-CopiersIdle($Summary) {
    if ($Summary.ActiveRunCount -ne 0) { throw 'The V2 flow has active, waiting, suspended or unknown-state runs. Wait for an idle flow; nothing was changed.' }
}

function Invoke-CopiersCcSelfTest {
    $send = @{ type = 'OpenApiConnection'; inputs = @{ host = @{ operationId = 'SendEmailV2'; connectionName = 'shared_office365'; apiId = '/providers/Microsoft.PowerApps/apis/shared_office365' }; retryPolicy = @{ type = 'none' }; parameters = @{ 'emailMessage/To' = '@snapshot'; 'emailMessage/Body' = '@body'; 'emailMessage/Attachments' = '@files' } } }
    $fixture = @{ name = $flowId; properties = @{ displayName = $flowDisplayName; state = 'Started'; lastModifiedTime = '2026-09-08T23:21:29.5397696Z'; connectionReferences = @{ shared_office365 = @{ connectionName = $expectedOutlookConnection } }; definition = @{ triggers = @{ When_signed_report_is_ready = @{ runtimeConfiguration = @{ concurrency = @{ runs = 1 } } } }; actions = @{ Scope = @{ actions = @{ Send = $send }; else = @{ actions = @{} } } } } } }
    $tests = 0
    $beforeHash = Get-CopiersJsonHash $fixture
    $plan = New-CopiersCcPlan $fixture
    if ((Get-CopiersJsonHash $fixture) -cne $beforeHash -or $plan.AlreadyConfigured) { throw 'Self-test: input was mutated or first update was not detected.' }; $tests++
    if (@(Find-CopiersSendAction $plan.Candidate.definition)[0].Action.inputs.parameters.'emailMessage/Cc' -cne $requiredCc) { throw 'Self-test: CC mismatch.' }; $tests++
    if ($plan.Candidate.definition.actions.Scope.actions.Send.inputs.parameters.'emailMessage/To' -ne '@snapshot' -or $plan.Candidate.definition.actions.Scope.actions.Send.inputs.parameters.Contains('emailMessage/From')) { throw 'Self-test: To/From changed.' }; $tests++
    $configured = Copy-CopiersJson $fixture
    $configured.properties = $plan.Candidate
    $configured.properties['lastModifiedTime'] = $fixture.properties.lastModifiedTime
    if (-not (New-CopiersCcPlan $configured).AlreadyConfigured) { throw 'Self-test: update is not idempotent.' }; $tests++
    $invalidCases = @(
        { param($f) $f.name = 'wrong-flow' },
        { param($f) $f.properties.state = 'Stopped' },
        { param($f) $f.properties.Remove('lastModifiedTime') },
        { param($f) $f.properties.connectionReferences.shared_office365.connectionName = 'wrong-connection' },
        { param($f) $f.properties.definition.triggers.When_signed_report_is_ready.runtimeConfiguration.concurrency.runs = 2 },
        { param($f) $f.properties.definition.actions.Scope.actions.Send.inputs.retryPolicy.type = 'exponential' },
        { param($f) $f.properties.definition.actions.Scope.actions.Send.inputs.parameters['emailMessage/From'] = 'other@example.test' },
        { param($f) $f.properties.definition.actions.Scope.actions.Send.inputs.parameters['emailMessage/Cc'] = 'other@example.test' },
        { param($f) $f.properties.definition.actions.Scope.else.actions['ExtraSend'] = Copy-CopiersJson $f.properties.definition.actions.Scope.actions.Send }
    )
    foreach ($invalidate in $invalidCases) {
        $copy = Copy-CopiersJson $fixture
        & $invalidate $copy | Out-Null
        $rejected = $false
        try { New-CopiersCcPlan $copy | Out-Null } catch { $rejected = $true }
        if (-not $rejected) { throw "Self-test: invalid scope accepted (case $tests)." }; $tests++
    }
    if ((Get-CopiersJsonHash @{ b = 2; a = @(1,2) }) -cne (Get-CopiersJsonHash ([ordered]@{ a = @(1,2); b = 2 }))) { throw 'Self-test: unstable structural hash.' }; $tests++
    if ((Get-CopiersJsonHash @{ a = @($null) }) -ceq (Get-CopiersJsonHash @{ a = @() })) { throw 'Self-test: null array element was dropped.' }; $tests++
    if ((Get-CopiersJsonHash @{ a = @(1,@(2,3)) }) -ceq (Get-CopiersJsonHash @{ a = @(1,2,3) })) { throw 'Self-test: nested array was flattened.' }; $tests++
    if ((Get-CopiersJsonHash @{ a = @(1,2) }) -ceq (Get-CopiersJsonHash @{ a = @(2,1) })) { throw 'Self-test: array order was lost.' }; $tests++
    $idle = Get-CopiersRunSummary @{ value = @() }
    Assert-CopiersIdle $idle
    if ($idle.RunCount -ne 0 -or $idle.ActiveRunCount -ne 0) { throw 'Self-test: empty run history was miscounted.' }; $tests++
    $activeSummary = Get-CopiersRunSummary @{ value = @('Running','Waiting','Suspended','Unexpected','Succeeded','Failed','Cancelled') | ForEach-Object { @{ properties = @{ status = $_ } } } }
    if ($activeSummary.RunCount -ne 7 -or $activeSummary.ActiveRunCount -ne 4) { throw 'Self-test: active or unknown run state was missed.' }; $tests++
    $activeRejected = $false
    try { Assert-CopiersIdle $activeSummary } catch { $activeRejected = $true }
    if (-not $activeRejected) { throw 'Self-test: active runs were accepted.' }; $tests++
    $pageRejected = $false
    try { Get-CopiersRunSummary @{ value = @(); nextLink = 'more-history' } | Out-Null } catch { $pageRejected = $true }
    if (-not $pageRejected) { throw 'Self-test: incomplete history was accepted.' }; $tests++
    [pscustomobject]@{ Mode = 'SelfTest'; Passed = $tests; CloudCalls = 0; Mutations = 0 } | ConvertTo-Json
}

if ($SelfTest) {
    if ($Apply) { throw 'SelfTest and Apply cannot be combined.' }
    Invoke-CopiersCcSelfTest
    return
}
if ($Apply -and ([string]::IsNullOrWhiteSpace($ExpectedLastModifiedTime) -or $ExpectedSnapshotSha256 -notmatch '^[A-Fa-f0-9]{64}$')) {
    throw 'Apply requires ExpectedLastModifiedTime and ExpectedSnapshotSha256 copied from a fresh plan.'
}

# Existing CLI sessions only. No login, credential creation or permission changes.
$flowToken = az account get-access-token --resource https://service.flow.microsoft.com/ --query accessToken -o tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($flowToken)) { throw 'Existing Flow authentication unavailable.' }
$headers = @{ Authorization = "Bearer $flowToken" }
$initial = Read-CopiersFlow $headers
$plan = New-CopiersCcPlan $initial.Flow
$powerAppsToken = az account get-access-token --resource https://service.powerapps.com/ --query accessToken -o tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($powerAppsToken)) { throw 'Existing PowerApps authentication unavailable.' }
$connectionUri = "https://api.powerapps.com/providers/Microsoft.PowerApps/connections?api-version=2016-11-01&`$filter=environment%20eq%20%27$environmentName%27"
$connections = Invoke-RestMethod -Method Get -Uri $connectionUri -Headers @{ Authorization = "Bearer $powerAppsToken" } -MaximumRetryCount 0
Assert-CopiersSenderConnection $connections
$runSummary = Read-CopiersRunSummary $headers

$summary = [ordered]@{ Mode = 'Plan'; FlowId = $flowId; State = 'Started'; SenderUnchanged = $expectedSender; Cc = $requiredCc; LastModifiedTime = $plan.LastModifiedTime; SnapshotSha256 = $plan.SnapshotSha256; CandidateSha256 = $plan.CandidateSha256; ChangedPath = $plan.ChangedPath; AlreadyConfigured = $plan.AlreadyConfigured; HasETag = [bool]$initial.ETag; RunCount = $runSummary.RunCount; ActiveRunCount = $runSummary.ActiveRunCount; Mutations = 0 }
if (-not $Apply) { $summary | ConvertTo-Json; return }
if ($plan.LastModifiedTime -cne $ExpectedLastModifiedTime -or $plan.SnapshotSha256 -ine $ExpectedSnapshotSha256) { throw 'The approved plan is stale. Re-plan; nothing was changed.' }
if ($plan.AlreadyConfigured) { $summary.Mode = 'AlreadyConfigured'; $summary | ConvertTo-Json; return }
Assert-CopiersIdle $runSummary

# Last-modified plus full editable snapshot are checked again immediately before PATCH.
# The Flow RP currently omits ETag. This narrows the race, but is not an atomic CAS;
# serialize all other flow writers during this operation. Never send If-Match: *.
$freshRuns = Read-CopiersRunSummary $headers
Assert-CopiersIdle $freshRuns
$fresh = Read-CopiersFlow $headers
$freshPlan = New-CopiersCcPlan $fresh.Flow
if ($freshPlan.LastModifiedTime -cne $plan.LastModifiedTime -or $freshPlan.SnapshotSha256 -cne $plan.SnapshotSha256 -or $fresh.ETag -cne $initial.ETag) { throw 'Concurrent flow modification detected. Nothing was changed.' }
$patchHeaders = @{ Authorization = $headers.Authorization }
if ($fresh.ETag) { $patchHeaders['If-Match'] = $fresh.ETag }
else { Write-Warning 'Flow API returned no ETag. Last-modified/snapshot guards are checked, but all other writers must remain frozen until read-back completes.' }
$payload = @{ properties = $freshPlan.Candidate }
# UpdateFlow is PATCH in the official shared_flowmanagement connector Swagger.
# Never retry an ambiguous mutation; reconcile the exact resource by GET first.
try {
    Invoke-RestMethod -Method Patch -Uri $flowUri -Headers $patchHeaders -ContentType 'application/json' -Body (ConvertTo-Json -InputObject $payload -Depth 100 -Compress) -MaximumRetryCount 0 | Out-Null
}
catch { throw 'Flow PATCH was not confirmed. Do not retry blindly: GET this exact flow and reconcile CC, lastModified and definition hash first.' }
$readBack = Read-CopiersFlow $headers
$readBackPlan = New-CopiersCcPlan $readBack.Flow
if (-not $readBackPlan.AlreadyConfigured -or $readBackPlan.SnapshotSha256 -cne $freshPlan.CandidateSha256) { throw 'PATCH returned, but exact read-back did not match. Do not retry or restore automatically; reconcile the flow.' }
$summary.Mode = 'AppliedVerified'
$summary.LastModifiedTime = $readBackPlan.LastModifiedTime
$summary.SnapshotSha256 = $readBackPlan.SnapshotSha256
$summary.Mutations = 1
$summary | ConvertTo-Json
