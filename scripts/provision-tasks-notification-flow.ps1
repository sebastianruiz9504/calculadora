param(
    [string]$FlowName = "Tareas - Notificar asignacion",
    [string]$SolutionUniqueName = "InventariosDigitalTech",
    [string]$ConnectionTemplateFlowName = "Hardware - Notificar pago a proveedor",
    [string]$BaseUrl = "",
    [string]$TenantId = "",
    [string]$ClientId = "",
    [string]$ClientSecret = "",
    [switch]$SkipPacSolutionComponent
)

$ErrorActionPreference = "Stop"

function Get-JsonConfigValue {
    param([object]$Root, [string]$Path)
    $current = $Root
    foreach ($part in $Path.Split(":")) {
        if ($null -eq $current) { return "" }
        $property = $current.PSObject.Properties[$part]
        if ($null -eq $property) { return "" }
        $current = $property.Value
    }
    if ($null -eq $current) { return "" }
    return [string]$current
}

function Get-UserSecretMap {
    $map = @{}
    $lines = & dotnet user-secrets list 2>$null
    foreach ($line in $lines) {
        $idx = $line.IndexOf(" = ")
        if ($idx -le 0) { continue }
        $key = $line.Substring(0, $idx).Trim()
        $value = $line.Substring($idx + 3).Trim()
        if ($key) { $map[$key] = $value }
    }
    return $map
}

function First-NonEmpty {
    foreach ($value in $args) {
        if (![string]::IsNullOrWhiteSpace([string]$value)) { return [string]$value }
    }
    return ""
}

function ConvertTo-JsonBody {
    param([object]$Body)
    return $Body | ConvertTo-Json -Depth 100 -Compress
}

function Invoke-Dataverse {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body = $null,
        [switch]$AllowNotFound
    )

    $uri = if ($Path.StartsWith("http")) { $Path } else { "$script:BaseUrl$Path" }
    $headers = @{
        "Authorization" = "Bearer $script:AccessToken"
        "Accept" = "application/json"
        "OData-MaxVersion" = "4.0"
        "OData-Version" = "4.0"
    }

    try {
        if ($null -eq $Body) {
            return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers
        }

        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -ContentType "application/json" -Body (ConvertTo-JsonBody $Body)
    } catch {
        if ($AllowNotFound -and $_.Exception.Response.StatusCode.value__ -eq 404) { return $null }
        throw
    }
}

function Invoke-DataverseRaw {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body = $null
    )

    $uri = if ($Path.StartsWith("http")) { $Path } else { "$script:BaseUrl$Path" }
    $headers = @{
        "Authorization" = "Bearer $script:AccessToken"
        "Accept" = "application/json"
        "OData-MaxVersion" = "4.0"
        "OData-Version" = "4.0"
    }

    if ($null -eq $Body) {
        return Invoke-WebRequest -Method $Method -Uri $uri -Headers $headers
    }

    return Invoke-WebRequest -Method $Method -Uri $uri -Headers $headers -ContentType "application/json" -Body (ConvertTo-JsonBody $Body)
}

function Invoke-DataverseRawWithToken {
    param(
        [string]$Method,
        [string]$Path,
        [string]$AccessToken,
        [object]$Body = $null
    )

    $uri = if ($Path.StartsWith("http")) { $Path } else { "$script:BaseUrl$Path" }
    $headers = @{
        "Authorization" = "Bearer $AccessToken"
        "Accept" = "application/json"
        "OData-MaxVersion" = "4.0"
        "OData-Version" = "4.0"
    }

    if ($null -eq $Body) {
        return Invoke-WebRequest -Method $Method -Uri $uri -Headers $headers
    }

    return Invoke-WebRequest -Method $Method -Uri $uri -Headers $headers -ContentType "application/json" -Body (ConvertTo-JsonBody $Body)
}

function Get-AzureCliDataverseAccessToken {
    try {
        $tokenJson = & az account get-access-token --resource $script:BaseUrl 2>$null
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($tokenJson)) { return "" }
        $token = $tokenJson | ConvertFrom-Json
        return [string]$token.accessToken
    } catch {
        return ""
    }
}

function Get-WorkflowByName {
    param([string]$Name)
    $escaped = $Name.Replace("'", "''")
    $filter = [Uri]::EscapeDataString("name eq '$escaped'")
    $select = "workflowid,name,statecode,statuscode,clientdata"
    $result = Invoke-Dataverse -Method "GET" -Path "/api/data/v9.2/workflows?`$select=$select&`$filter=$filter&`$top=1"
    if ($result.value.Count -eq 0) { return $null }
    return $result.value[0]
}

function Get-ConnectionReferences {
    $template = Get-WorkflowByName $ConnectionTemplateFlowName
    if ($null -eq $template) {
        throw "No se encontro el flujo plantilla '$ConnectionTemplateFlowName' para reutilizar sus conexiones."
    }

    $clientData = $template.clientdata | ConvertFrom-Json
    $refs = $clientData.properties.connectionReferences
    if ($null -eq $refs.shared_commondataserviceforapps -or $null -eq $refs.shared_office365) {
        throw "El flujo plantilla no tiene conexiones shared_commondataserviceforapps y shared_office365."
    }

    return $refs
}

function New-TaskNotificationClientData {
    param([object]$ConnectionReferences)

    $emailBody = @'
<p>Hola @{coalesce(triggerOutputs()?['body/cr07a_responsablenombre'],'')},</p>
<p>Se te asigno una nueva tarea en Cotizador Interno.</p>
<table style="border-collapse:collapse;width:100%;font-family:Segoe UI,Arial,sans-serif;font-size:13px;margin:12px 0;">
<tr><td style="border:1px solid #d6dee6;padding:8px;background:#eef3f8;"><strong>Tarea</strong></td><td style="border:1px solid #d6dee6;padding:8px;">@{coalesce(triggerOutputs()?['body/cr07a_name'],'Tarea')}</td></tr>
<tr><td style="border:1px solid #d6dee6;padding:8px;background:#eef3f8;"><strong>Modulo</strong></td><td style="border:1px solid #d6dee6;padding:8px;">@{coalesce(triggerOutputs()?['body/cr07a_modulo'],'-')}</td></tr>
<tr><td style="border:1px solid #d6dee6;padding:8px;background:#eef3f8;"><strong>Tipo</strong></td><td style="border:1px solid #d6dee6;padding:8px;">@{coalesce(triggerOutputs()?['body/cr07a_tipo'],'-')}</td></tr>
<tr><td style="border:1px solid #d6dee6;padding:8px;background:#eef3f8;"><strong>Fecha limite</strong></td><td style="border:1px solid #d6dee6;padding:8px;">@{coalesce(triggerOutputs()?['body/cr07a_fechalimite@OData.Community.Display.V1.FormattedValue'], triggerOutputs()?['body/cr07a_fechalimite'], '-')}</td></tr>
<tr><td style="border:1px solid #d6dee6;padding:8px;background:#eef3f8;"><strong>Pendientes</strong></td><td style="border:1px solid #d6dee6;padding:8px;">@{coalesce(triggerOutputs()?['body/cr07a_totalpendientes'], 1)}</td></tr>
</table>
<p style="white-space:pre-line;">@{coalesce(triggerOutputs()?['body/cr07a_descripcion'],'-')}</p>
@{if(empty(triggerOutputs()?['body/cr07a_actionurl']), '', concat('<p><a href="', triggerOutputs()?['body/cr07a_actionurl'], '" style="display:inline-block;background:#195bd8;color:#ffffff;text-decoration:none;padding:10px 14px;border-radius:6px;">Abrir tarea</a></p>'))}
@{coalesce(triggerOutputs()?['body/cr07a_emailtablahtmlfull'],'')}
<p style="color:#607080;font-size:12px;margin-top:16px;">Correo generado automaticamente por el flujo de tareas.</p>
'@

    $definition = [ordered]@{
        '$schema' = "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#"
        contentVersion = "1.0.0.0"
        parameters = [ordered]@{
            '$authentication' = [ordered]@{
                defaultValue = @{}
                type = "SecureObject"
            }
            '$connections' = [ordered]@{
                defaultValue = @{}
                type = "Object"
            }
        }
        triggers = [ordered]@{
            Cuando_se_crea_tarea = [ordered]@{
                metadata = [ordered]@{
                    operationMetadataId = [guid]::NewGuid().ToString()
                }
                type = "OpenApiConnectionWebhook"
                inputs = [ordered]@{
                    parameters = [ordered]@{
                        "subscriptionRequest/message" = 1
                        "subscriptionRequest/entityname" = "cr07a_tarea"
                        "subscriptionRequest/scope" = 4
                    }
                    host = [ordered]@{
                        apiId = "/providers/Microsoft.PowerApps/apis/shared_commondataserviceforapps"
                        connectionName = "shared_commondataserviceforapps"
                        operationId = "SubscribeWebhookTrigger"
                    }
                    authentication = "@parameters('`$authentication')"
                }
            }
        }
        actions = [ordered]@{
            Condicion_correo_responsable = [ordered]@{
                runAfter = @{}
                type = "If"
                expression = [ordered]@{
                    and = @(
                        [ordered]@{
                            equals = @(
                                "@empty(triggerOutputs()?['body/cr07a_responsablecorreo'])",
                                $false
                            )
                        }
                    )
                }
                actions = [ordered]@{
                    Enviar_correo_electronico = [ordered]@{
                        runAfter = @{}
                        type = "OpenApiConnection"
                        inputs = [ordered]@{
                            parameters = [ordered]@{
                                "emailMessage/To" = "@triggerOutputs()?['body/cr07a_responsablecorreo']"
                                "emailMessage/Subject" = "Tarea pendiente - @{coalesce(triggerOutputs()?['body/cr07a_name'],'Tarea')}"
                                "emailMessage/Body" = $emailBody
                                "emailMessage/Importance" = "Normal"
                            }
                            host = [ordered]@{
                                apiId = "/providers/Microsoft.PowerApps/apis/shared_office365"
                                connectionName = "shared_office365"
                                operationId = "SendEmailV2"
                            }
                            authentication = "@parameters('`$authentication')"
                        }
                    }
                    Marcar_email_enviado = [ordered]@{
                        runAfter = [ordered]@{
                            Enviar_correo_electronico = @("Succeeded")
                        }
                        type = "OpenApiConnection"
                        inputs = [ordered]@{
                            parameters = [ordered]@{
                                entityName = "cr07a_tareas"
                                recordId = "@triggerOutputs()?['body/cr07a_tareaid']"
                                "item/cr07a_emailenviado" = $true
                                "item/cr07a_emailenviadoen" = "@utcNow()"
                                "item/cr07a_emailerror" = ""
                            }
                            host = [ordered]@{
                                apiId = "/providers/Microsoft.PowerApps/apis/shared_commondataserviceforapps"
                                connectionName = "shared_commondataserviceforapps"
                                operationId = "UpdateRecord"
                            }
                            authentication = "@parameters('`$authentication')"
                        }
                    }
                    Marcar_email_error = [ordered]@{
                        runAfter = [ordered]@{
                            Enviar_correo_electronico = @("Failed", "TimedOut")
                        }
                        type = "OpenApiConnection"
                        inputs = [ordered]@{
                            parameters = [ordered]@{
                                entityName = "cr07a_tareas"
                                recordId = "@triggerOutputs()?['body/cr07a_tareaid']"
                                "item/cr07a_emailenviado" = $false
                                "item/cr07a_emailerror" = "Power Automate no pudo enviar el correo de la tarea."
                            }
                            host = [ordered]@{
                                apiId = "/providers/Microsoft.PowerApps/apis/shared_commondataserviceforapps"
                                connectionName = "shared_commondataserviceforapps"
                                operationId = "UpdateRecord"
                            }
                            authentication = "@parameters('`$authentication')"
                        }
                    }
                }
                else = [ordered]@{
                    actions = [ordered]@{
                        Marcar_email_sin_destinatario = [ordered]@{
                            type = "OpenApiConnection"
                            inputs = [ordered]@{
                                parameters = [ordered]@{
                                    entityName = "cr07a_tareas"
                                    recordId = "@triggerOutputs()?['body/cr07a_tareaid']"
                                    "item/cr07a_emailenviado" = $false
                                    "item/cr07a_emailerror" = "La tarea no tiene correo de responsable."
                                }
                                host = [ordered]@{
                                    apiId = "/providers/Microsoft.PowerApps/apis/shared_commondataserviceforapps"
                                    connectionName = "shared_commondataserviceforapps"
                                    operationId = "UpdateRecord"
                                }
                                authentication = "@parameters('`$authentication')"
                            }
                        }
                    }
                }
            }
        }
        outputs = @{}
    }

    $clientData = [ordered]@{
        properties = [ordered]@{
            connectionReferences = $ConnectionReferences
            definition = $definition
        }
        schemaVersion = "1.0.0.0"
    }

    return ConvertTo-JsonBody $clientData
}

function Set-WorkflowState {
    param(
        [string]$WorkflowId,
        [int]$State,
        [int]$Status
    )

    try {
        Invoke-DataverseRaw -Method "PATCH" -Path "/api/data/v9.2/workflows($WorkflowId)" -Body @{
            statecode = $State
            statuscode = $Status
        } | Out-Null
        return
    } catch {
        Write-Host "PATCH de estado con token app-only no funciono, intentando token delegado de Azure CLI..."
    }

    $delegatedToken = Get-AzureCliDataverseAccessToken
    if (![string]::IsNullOrWhiteSpace($delegatedToken)) {
        try {
            Invoke-DataverseRawWithToken -Method "PATCH" -Path "/api/data/v9.2/workflows($WorkflowId)" -AccessToken $delegatedToken -Body @{
                statecode = $State
                statuscode = $Status
            } | Out-Null
            return
        } catch {
            Write-Host "PATCH de estado con token delegado no funciono, intentando accion SetState..."
        }
    } else {
        Write-Host "Azure CLI no devolvio token delegado, intentando accion SetState..."
    }

    Invoke-DataverseRaw -Method "POST" -Path "/api/data/v9.2/SetState" -Body @{
        EntityMoniker = @{
            "@odata.id" = "workflows($WorkflowId)"
        }
        State = $State
        Status = $Status
    } | Out-Null
}

$appsettings = Get-Content -LiteralPath "appsettings.json" -Raw | ConvertFrom-Json
$secrets = Get-UserSecretMap

$script:BaseUrl = First-NonEmpty $BaseUrl (Get-JsonConfigValue $appsettings "Dataverse:BaseUrl")
$script:BaseUrl = $script:BaseUrl.TrimEnd("/")
$TenantId = First-NonEmpty $TenantId $secrets["Dataverse:TenantId"] (Get-JsonConfigValue $appsettings "Dataverse:TenantId") (Get-JsonConfigValue $appsettings "AzureAd:TenantId")
$ClientId = First-NonEmpty $ClientId $secrets["Dataverse:ClientId"] (Get-JsonConfigValue $appsettings "Dataverse:ClientId")
$ClientSecret = First-NonEmpty $ClientSecret $secrets["Dataverse:ClientSecret"] (Get-JsonConfigValue $appsettings "Dataverse:ClientSecret")

if ([string]::IsNullOrWhiteSpace($script:BaseUrl)) { throw "Dataverse BaseUrl is required." }
if ([string]::IsNullOrWhiteSpace($TenantId)) { throw "TenantId is required." }
if ([string]::IsNullOrWhiteSpace($ClientId)) { throw "ClientId is required." }
if ([string]::IsNullOrWhiteSpace($ClientSecret)) { throw "ClientSecret is required." }

$tokenBody = @{
    client_id = $ClientId
    client_secret = $ClientSecret
    scope = "$script:BaseUrl/.default"
    grant_type = "client_credentials"
}
$token = Invoke-RestMethod -Method Post -Uri "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token" -Body $tokenBody -ContentType "application/x-www-form-urlencoded"
$script:AccessToken = $token.access_token

$connectionRefs = Get-ConnectionReferences
$clientData = New-TaskNotificationClientData -ConnectionReferences $connectionRefs
$existing = Get-WorkflowByName $FlowName

if ($null -eq $existing) {
    $body = @{
        name = $FlowName
        description = "Envia correo cuando se crea una fila en cr07a_tarea."
        category = 5
        type = 1
        mode = 0
        primaryentity = "none"
        modernflowtype = 0
        runas = 1
        scope = 4
        resourceid = [guid]::NewGuid().ToString()
        subprocess = $false
        ondemand = $false
        clientdata = $clientData
    }

    Invoke-DataverseRaw -Method "POST" -Path "/api/data/v9.2/workflows" -Body $body | Out-Null
    $existing = Get-WorkflowByName $FlowName
    if ($null -eq $existing) { throw "El flujo fue creado, pero no se pudo consultar despues de crearlo." }
    Write-Host "Created flow $FlowName ($($existing.workflowid))"
} else {
    Write-Host "Flow $FlowName already exists ($($existing.workflowid)); updating definition."
    if ([int]$existing.statecode -eq 1) {
        Set-WorkflowState -WorkflowId $existing.workflowid -State 0 -Status 1
    }

    Invoke-DataverseRaw -Method "PATCH" -Path "/api/data/v9.2/workflows($($existing.workflowid))" -Body @{
        name = $FlowName
        description = "Envia correo cuando se crea una fila en cr07a_tarea."
        modernflowtype = 0
        runas = 1
        scope = 4
        clientdata = $clientData
    } | Out-Null
}

Set-WorkflowState -WorkflowId $existing.workflowid -State 1 -Status 2
Write-Host "Activated flow $FlowName ($($existing.workflowid))"

if (-not $SkipPacSolutionComponent) {
    $pacOutput = & pac solution add-solution-component --solutionUniqueName $SolutionUniqueName --component $existing.workflowid --componentType 29 --AddRequiredComponents 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "No se pudo agregar el flujo a la solucion con PAC. Output: $pacOutput"
    } else {
        $pacOutput | ForEach-Object { Write-Host $_ }
    }
}

Write-Host "Tasks notification flow provisioned."
