[CmdletBinding()]
param(
    [switch]$Apply,
    [switch]$Enable,
    [string]$ExportPath = ""
)

$ErrorActionPreference = 'Stop'
$environmentName = 'Default-cab7ea42-4a21-4548-952f-fcde81f2bdd6'
$environmentUrl = 'https://orgc79ca19c.crm2.dynamics.com'
$flowName = 'Copiers MTO V2 - Enviar reporte firmado'
$templateFlowId = 'e8b74a32-4cd7-444d-b16f-96e145790612'
$flowBase = "https://api.flow.microsoft.com/providers/Microsoft.ProcessSimple/environments/$environmentName"
$dvConnection = 'shared_commondataserviceforapps'
$outlookConnection = 'shared_office365'
$mainSelect = 'dtc_copiersmtov2id,dtc_workflowstate,dtc_emailstate,dtc_emailtosnapshot,dtc_emailsubjectsnapshot,dtc_emailhtmlbodysnapshot,dtc_reportevidencekey,dtc_reportfilename,dtc_reportsha256,dtc_attachmentcount,dtc_attachmentmanifestjson,dtc_emailoutboxkey'
$evidenceSelect = 'dtc_copiersmtoevidenciav2id,dtc_evidencekey,_dtc_signedmto_value,dtc_purpose,dtc_sequence,dtc_originalfilename,dtc_contenttype,dtc_bytelength,dtc_sha256,dtc_securitystate'
$main = "first(body('Read_pending_row')?['value'])"
$recordId = "@triggerOutputs()?['body/dtc_copiersmtov2id']"
$secure = @{ secureData = @{ properties = @('inputs', 'outputs') } }

function New-ConnectorAction($operation, $parameters, $after = @{}, $connection = $dvConnection) {
    return [ordered]@{
        type = 'OpenApiConnection'
        inputs = [ordered]@{
            parameters = $parameters
            host = @{ apiId = "/providers/Microsoft.PowerApps/apis/$connection"; connectionName = $connection; operationId = $operation }
            authentication = "@parameters('`$authentication')"
        }
        runAfter = $after
        runtimeConfiguration = $secure
    }
}
function New-StateAction($state, $code, $message, $after = @{}) {
    $parameters = [ordered]@{
        entityName = 'dtc_copiersmtov2s'; recordId = $recordId
        'item/dtc_emailstate' = $state
        'item/dtc_lasterrorcode' = $code
        'item/dtc_lasterrorsafemessage' = $message
    }
    return New-ConnectorAction 'UpdateRecord' $parameters $after
}
function New-SetInvalid($after = @{}) {
    return @{ type = 'SetVariable'; inputs = @{ name = 'EvidenceValid'; value = $false }; runAfter = $after }
}
function New-Download($id, $after = @{}) {
    return New-ConnectorAction 'GetEntityFileImageFieldContentWithOrganization' @{
        organization = $environmentUrl; entityName = 'dtc_copiersmtoevidenciav2s'
        recordId = $id; fileImageFieldName = 'dtc_filecontent'
    } $after
}
function New-ListEvidence($filter, $after = @{}) {
    return New-ConnectorAction 'ListRecords' @{
        entityName = 'dtc_copiersmtoevidenciav2s'; '$select' = $evidenceSelect; '$filter' = $filter; '$top' = 2
    } $after
}

$manifestSchema = @{
    type = 'array'; maxItems = 8
    items = @{
        type = 'object'
        required = @('sequence','evidenceKey','fileName','contentType','size','sha256','purpose','securityState')
        properties = @{
            sequence = @{ type = 'integer'; minimum = 1; maximum = 8 }
            evidenceKey = @{ type = 'string'; minLength = 64; maxLength = 64 }
            fileName = @{ type = 'string'; maxLength = 260 }
            contentType = @{ type = 'string'; enum = @('image/jpeg','image/png') }
            size = @{ type = 'integer'; minimum = 1; maximum = 8388608 }
            sha256 = @{ type = 'string'; minLength = 64; maxLength = 64 }
            purpose = @{ type = 'string'; enum = @('CustomerAttachment') }
            securityState = @{ type = 'string'; enum = @('ScanPassed') }
        }
    }
}
$reportFilter = "dtc_evidencekey eq '@{replace($main`?['dtc_reportevidencekey'],decodeUriComponent('%27'),concat(decodeUriComponent('%27'),decodeUriComponent('%27')))}' and _dtc_signedmto_value eq @{triggerOutputs()?['body/dtc_copiersmtov2id']} and dtc_purpose eq 827270001"
$imageFilter = "dtc_evidencekey eq '@{replace(items('For_each_customer_attachment')?['evidenceKey'],decodeUriComponent('%27'),concat(decodeUriComponent('%27'),decodeUriComponent('%27')))}' and _dtc_signedmto_value eq @{triggerOutputs()?['body/dtc_copiersmtov2id']} and dtc_purpose eq 827270003 and dtc_securitystate eq 827270002"
$image = "first(body('Read_customer_attachment')?['value'])"
$report = "first(body('Read_signed_report')?['value'])"
$item = "items('For_each_customer_attachment')"

$imageActions = [ordered]@{
    Read_customer_attachment = (New-ListEvidence $imageFilter)
    Validate_customer_attachment = @{
        type = 'If'; runAfter = @{ Read_customer_attachment = @('Succeeded') }
        expression = "@and(equals(length(body('Read_customer_attachment')?['value']),1),equals($image`?['dtc_evidencekey'],$item`?['evidenceKey']),equals($image`?['dtc_sequence'],$item`?['sequence']),equals($image`?['dtc_originalfilename'],$item`?['fileName']),equals($item`?['fileName'],concat('adjunto-',formatNumber($item`?['sequence'],'000'),if(equals($item`?['contentType'],'image/png'),'.png','.jpg'))),equals($image`?['dtc_contenttype'],$item`?['contentType']),equals($image`?['dtc_bytelength'],$item`?['size']),equals($image`?['dtc_sha256'],$item`?['sha256']),equals($item`?['purpose'],'CustomerAttachment'),equals($item`?['securityState'],'ScanPassed'),not(contains(variables('EvidenceKeys'),$item`?['evidenceKey'])),not(contains(variables('AttachmentSequences'),string($item`?['sequence']))))"
        actions = [ordered]@{
            Download_customer_attachment = (New-Download "@$image`?['dtc_copiersmtoevidenciav2id']")
            Reread_customer_attachment = (New-ListEvidence $imageFilter @{ Download_customer_attachment = @('Succeeded') })
            Validate_stable_customer_attachment = @{
                type = 'If'; runAfter = @{ Reread_customer_attachment = @('Succeeded') }
                expression = "@and(equals(length(body('Reread_customer_attachment')?['value']),1),equals($image`?['@odata.etag'],first(body('Reread_customer_attachment')?['value'])?['@odata.etag']))"
                actions = [ordered]@{
                    Append_customer_attachment = @{
                        type = 'AppendToArrayVariable'; runAfter = @{}
                        inputs = @{ name = 'MailAttachments'; value = @{ Name = "@$item`?['fileName']"; ContentBytes = "@body('Download_customer_attachment')" } }
                    }
                    Remember_evidence_key = @{
                        type = 'AppendToArrayVariable'; runAfter = @{ Append_customer_attachment = @('Succeeded') }
                        inputs = @{ name = 'EvidenceKeys'; value = "@$item`?['evidenceKey']" }
                    }
                    Remember_sequence = @{
                        type = 'AppendToArrayVariable'; runAfter = @{ Remember_evidence_key = @('Succeeded') }
                        inputs = @{ name = 'AttachmentSequences'; value = "@string($item`?['sequence'])" }
                    }
                    Add_customer_attachment_size = @{
                        type = 'IncrementVariable'; runAfter = @{ Remember_sequence = @('Succeeded') }
                        inputs = @{ name = 'EncodedBytes'; value = "@add(mul(4,div(add(int($item`?['size']),2),3)),2048)" }
                    }
                }
                else = @{ actions = @{ Reject_changed_attachment = (New-SetInvalid) } }
            }
        }
        else = @{ actions = @{ Reject_customer_attachment = (New-SetInvalid) } }
    }
}
$prepareActions = [ordered]@{
    Parse_customer_manifest = @{
        type = 'ParseJson'; runAfter = @{}
        inputs = @{ content = "@$main`?['dtc_attachmentmanifestjson']"; schema = $manifestSchema }
        runtimeConfiguration = @{ secureData = @{ properties = @('inputs') } }
    }
    Set_email_size_baseline = @{
        type = 'SetVariable'; runAfter = @{ Parse_customer_manifest = @('Succeeded') }
        inputs = @{ name = 'EncodedBytes'; value = "@add(66560,mul(4,add(add(length(coalesce($main`?['dtc_emailtosnapshot'],'')),length(coalesce($main`?['dtc_emailsubjectsnapshot'],''))),length(coalesce($main`?['dtc_emailhtmlbodysnapshot'],'')))))" }
    }
    Read_signed_report = (New-ListEvidence $reportFilter @{ Set_email_size_baseline = @('Succeeded') })
    Validate_signed_report = @{
        type = 'If'; runAfter = @{ Read_signed_report = @('Succeeded') }
        expression = "@and(equals(length(body('Read_signed_report')?['value']),1),equals($report`?['dtc_contenttype'],'application/pdf'),equals($report`?['dtc_originalfilename'],$main`?['dtc_reportfilename']),equals($report`?['dtc_sha256'],$main`?['dtc_reportsha256']),greater(int(coalesce($report`?['dtc_bytelength'],0)),0),lessOrEquals(int(coalesce($report`?['dtc_bytelength'],0)),12582912),equals(length(body('Parse_customer_manifest')),int(coalesce($main`?['dtc_attachmentcount'],-1))),not(empty($main`?['dtc_emailtosnapshot'])),not(empty($main`?['dtc_emailsubjectsnapshot'])))"
        actions = [ordered]@{
            Download_signed_report = (New-Download "@$report`?['dtc_copiersmtoevidenciav2id']")
            Reread_signed_report = (New-ListEvidence $reportFilter @{ Download_signed_report = @('Succeeded') })
            Validate_stable_signed_report = @{
                type = 'If'; runAfter = @{ Reread_signed_report = @('Succeeded') }
                expression = "@and(equals(length(body('Reread_signed_report')?['value']),1),equals($report`?['@odata.etag'],first(body('Reread_signed_report')?['value'])?['@odata.etag']))"
                actions = [ordered]@{
                    Append_signed_report = @{
                        type = 'AppendToArrayVariable'; runAfter = @{}
                        inputs = @{ name = 'MailAttachments'; value = @{ Name = "@$main`?['dtc_reportfilename']"; ContentBytes = "@body('Download_signed_report')" } }
                    }
                    Add_signed_report_size = @{
                        type = 'IncrementVariable'; runAfter = @{ Append_signed_report = @('Succeeded') }
                        inputs = @{ name = 'EncodedBytes'; value = "@add(mul(4,div(add(int($report`?['dtc_bytelength']),2),3)),2048)" }
                    }
                    For_each_customer_attachment = @{
                        type = 'Foreach'; foreach = "@body('Parse_customer_manifest')"
                        runAfter = @{ Add_signed_report_size = @('Succeeded') }
                        runtimeConfiguration = @{ concurrency = @{ repetitions = 1 } }
                        actions = $imageActions
                    }
                }
                else = @{ actions = @{ Reject_changed_report = (New-SetInvalid) } }
            }
        }
        else = @{ actions = @{ Reject_signed_report = (New-SetInvalid) } }
    }
}
$sendAction = New-ConnectorAction 'SendEmailV2' @{
    'emailMessage/To' = "@$main`?['dtc_emailtosnapshot']"
    'emailMessage/Cc' = 'Germanruiz@digitaltechcolombia.com;soportecopiers@digitaltechcolombia.com'
    'emailMessage/Subject' = "@$main`?['dtc_emailsubjectsnapshot']"
    'emailMessage/Body' = "@$main`?['dtc_emailhtmlbodysnapshot']"
    'emailMessage/Attachments' = "@variables('MailAttachments')"
    'emailMessage/Importance' = 'Normal'
} @{} $outlookConnection
$sendAction.inputs.retryPolicy = @{ type = 'none' }

$definition = [ordered]@{
    '$schema' = 'https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#'
    contentVersion = '1.0.0.0'
    parameters = @{ '$authentication' = @{ defaultValue = @{}; type = 'SecureObject' }; '$connections' = @{ defaultValue = @{}; type = 'Object' } }
    triggers = @{
        When_signed_report_is_ready = @{
            type = 'OpenApiConnectionWebhook'
            inputs = @{
                parameters = @{
                    'subscriptionRequest/message' = 3; 'subscriptionRequest/entityname' = 'dtc_copiersmtov2'; 'subscriptionRequest/scope' = 4
                    'subscriptionRequest/filteringattributes' = 'dtc_workflowstate,dtc_emailstate,dtc_readyatutc'
                    'subscriptionRequest/filterexpression' = 'dtc_workflowstate eq 827270002 and dtc_emailstate eq 827270001'
                }
                host = @{ apiId = "/providers/Microsoft.PowerApps/apis/$dvConnection"; connectionName = $dvConnection; operationId = 'SubscribeWebhookTrigger' }
                authentication = "@parameters('`$authentication')"
            }
            conditions = @(@{ expression = "@and(equals(triggerOutputs()?['body/dtc_workflowstate'],827270002),equals(triggerOutputs()?['body/dtc_emailstate'],827270001))" })
            runtimeConfiguration = @{ concurrency = @{ runs = 1 }; secureData = @{ properties = @('outputs') } }
        }
    }
    actions = [ordered]@{
        Initialize_delivery_state = @{
            type = 'InitializeVariable'; runAfter = @{}
            inputs = @{ variables = @(
                @{ name = 'MailAttachments'; type = 'array'; value = @() },
                @{ name = 'EvidenceKeys'; type = 'array'; value = @() },
                @{ name = 'AttachmentSequences'; type = 'array'; value = @() },
                @{ name = 'EvidenceValid'; type = 'boolean'; value = $true },
                @{ name = 'EncodedBytes'; type = 'integer'; value = 0 }
            ) }
        }
        Read_pending_row = (New-ConnectorAction 'ListRecords' @{
            entityName = 'dtc_copiersmtov2s'; '$select' = $mainSelect; '$top' = 1
            '$filter' = "dtc_copiersmtov2id eq @{triggerOutputs()?['body/dtc_copiersmtov2id']} and dtc_workflowstate eq 827270002 and dtc_emailstate eq 827270001"
        } @{ Initialize_delivery_state = @('Succeeded') })
        Claim_only_pending_report = @{
            type = 'If'; runAfter = @{ Read_pending_row = @('Succeeded') }
            expression = "@equals(length(body('Read_pending_row')?['value']),1)"
            actions = [ordered]@{
                Mark_processing = (New-StateAction 827270002 '' '')
                Prepare_verified_attachments = @{ type = 'Scope'; runAfter = @{ Mark_processing = @('Succeeded') }; actions = $prepareActions }
                Send_only_complete_report = @{
                    type = 'If'; runAfter = @{ Prepare_verified_attachments = @('Succeeded') }
                    expression = "@and(variables('EvidenceValid'),equals(length(variables('MailAttachments')),add(1,int($main`?['dtc_attachmentcount']))),lessOrEquals(variables('EncodedBytes'),26214400))"
                    actions = [ordered]@{
                        Send_complete_report_once = $sendAction
                        Mark_sent = (New-StateAction 827270003 '' '' @{ Send_complete_report_once = @('Succeeded') })
                        Mark_send_needs_review = (New-StateAction 827270002 'EMAIL_REVIEW_REQUIRED' 'El proveedor no confirmo el envio. Revisar enviados antes de cualquier reintento; no se reenvia automaticamente.' @{ Send_complete_report_once = @('Failed','TimedOut') })
                    }
                    else = @{ actions = @{ Mark_invalid_report_failed = (New-StateAction 827270004 'EMAIL_EVIDENCE_INVALID' 'No se envio: PDF, adjuntos, manifiesto o tamano no coinciden con el reporte finalizado.') } }
                }
                Mark_preparation_failed = (New-StateAction 827270004 'EMAIL_PREPARATION_FAILED' 'No se envio: fallo la preparacion de los archivos. El reporte firmado permanece guardado.' @{ Prepare_verified_attachments = @('Failed','TimedOut') })
            }
            else = @{ actions = @{} }
        }
    }
    outputs = @{}
}

# Read existing authenticated connections; never serialize credentials or trigger callback URLs.
$flowToken = az account get-access-token --resource https://service.flow.microsoft.com/ --query accessToken -o tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($flowToken)) { throw 'Existing Azure CLI Flow authentication is unavailable.' }
$headers = @{ Authorization = "Bearer $flowToken" }
$template = Invoke-RestMethod -Uri "$flowBase/flows/$($templateFlowId)?api-version=2016-11-01" -Headers $headers
$refs = [ordered]@{}
foreach ($connection in @($dvConnection,$outlookConnection)) {
    $reference = $template.properties.connectionReferences.$connection
    if (-not $reference.connectionName) { throw "Legacy Copiers connection not found: $connection" }
    $refs[$connection] = @{ connectionName = $reference.connectionName; source = 'Embedded'; id = $reference.id }
}
$powerAppsToken = az account get-access-token --resource https://service.powerapps.com/ --query accessToken -o tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($powerAppsToken)) { throw 'Existing Azure CLI PowerApps authentication is unavailable.' }
$connectionUrl = "https://api.powerapps.com/providers/Microsoft.PowerApps/connections?api-version=2016-11-01&`$filter=environment%20eq%20%27$environmentName%27"
$connections = Invoke-RestMethod -Uri $connectionUrl -Headers @{ Authorization = "Bearer $powerAppsToken" }
$caller = az account show --query user.name -o tsv
foreach ($connection in @($dvConnection,$outlookConnection)) {
    $actual = @($connections.value | Where-Object name -eq $refs[$connection].connectionName)
    if ($actual.Count -ne 1 -or $actual[0].properties.accountName -ne $caller -or $actual[0].properties.statuses.status -notcontains 'Connected') {
        throw "Connection $connection is not a connected caller-owned existing Copiers connection."
    }
}
$existingFlows = Invoke-RestMethod -Uri "$flowBase/flows?api-version=2016-11-01" -Headers $headers
$existing = @($existingFlows.value | Where-Object {$_.properties.displayName -eq $flowName})
if ($existing.Count -gt 1) { throw 'Multiple V2 flows exist; refusing an ambiguous mutation.' }
$payload = @{ properties = @{ displayName = $flowName; state = 'Stopped'; definition = $definition; connectionReferences = $refs } }
$definitionJson = $definition | ConvertTo-Json -Depth 100
if ($definitionJson -match 'dtc_latitude|dtc_longitude|dtc_accuracy|dtc_location|dtc_internalnotes|cr07a_mantenimiento') {
    throw 'Definition contains an internal-location or legacy-table reference.'
}

if (-not $Apply) {
    [pscustomobject]@{ Mode = 'Plan'; DisplayName = $flowName; ExistingCount = $existing.Count; Sender = $caller; DefinitionBytes = [Text.Encoding]::UTF8.GetByteCount($definitionJson); Contract = 'outlook-send-once-v1' } | ConvertTo-Json
    return
}
if ($existing.Count -ne 0) { throw "V2 flow already exists ($($existing[0].name)); this create-only script will not overwrite it." }
# No automatic retry of creation. If response is ambiguous, list/read the exact display name before any new attempt.
$created = Invoke-RestMethod -Method Post -Uri "$flowBase/flows?api-version=2016-11-01" -Headers $headers -ContentType 'application/json' -Body ($payload | ConvertTo-Json -Depth 100)
$flowId = $created.name
if (-not $flowId) { throw 'Creation response has no flow ID. Reconcile by display name; do not rerun blindly.' }
$readBack = Invoke-RestMethod -Uri "$flowBase/flows/$($flowId)?api-version=2016-11-01" -Headers $headers
if ($readBack.properties.displayName -ne $flowName -or $readBack.properties.definition.triggers.When_signed_report_is_ready.runtimeConfiguration.concurrency.runs -ne 1) {
    throw "Flow $flowId created but verification failed. It was requested Stopped."
}
if ($Enable) {
    Invoke-RestMethod -Method Post -Uri "$flowBase/flows/$flowId/start?api-version=2016-11-01" -Headers $headers | Out-Null
    $readBack = Invoke-RestMethod -Uri "$flowBase/flows/$($flowId)?api-version=2016-11-01" -Headers $headers
    if ($readBack.properties.state -ne 'Started') { throw "Flow $flowId was created but is not Started." }
}
if ($ExportPath) {
    # Export only the allowlisted definition (no connection runtime URLs, tokens, callback URL or live row data).
    $safeExport = @{ contract = 'outlook-send-once-v1'; flowId = $flowId; displayName = $flowName; state = $readBack.properties.state; definition = $readBack.properties.definition }
    $safeExport | ConvertTo-Json -Depth 100 | Out-File -LiteralPath $ExportPath -Encoding utf8
}
[pscustomobject]@{ FlowId = $flowId; DisplayName = $flowName; State = $readBack.properties.state; Sender = $caller; Contract = 'outlook-send-once-v1'; DeliveryTested = $false } | ConvertTo-Json
