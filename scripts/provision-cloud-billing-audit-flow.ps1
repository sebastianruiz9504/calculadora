param(
    [string]$FlowName = "Productos Cloud - Auditoria facturacion mensual",
    [string]$SolutionUniqueName = "InventariosDigitalTech",
    [string]$ConnectionTemplateFlowName = "Siigo NC - Ajustar facturas Dataverse",
    [string]$BaseUrl = "",
    [string]$TenantId = "",
    [string]$ClientId = "",
    [string]$ClientSecret = "",
    [string]$SiigoUsername = "",
    [string]$SiigoAccessKey = "",
    [string]$SiigoPartnerId = "",
    [string]$Recipients = "sruiz@digitaltechcolombia.com;adaza@digitaltechcolombia.com",
    [int]$ReportHourBogota = 23,
    [int]$ReportMinuteBogota = 30,
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

function Find-WorkflowAction {
    param(
        [object]$Actions,
        [string]$Name
    )

    if ($null -eq $Actions) { return $null }

    foreach ($property in $Actions.PSObject.Properties) {
        if ($property.Name -eq $Name) { return $property.Value }

        $nested = Find-WorkflowAction -Actions $property.Value.actions -Name $Name
        if ($null -ne $nested) { return $nested }

        if ($null -ne $property.Value.else) {
            $nested = Find-WorkflowAction -Actions $property.Value.else.actions -Name $Name
            if ($null -ne $nested) { return $nested }
        }
    }

    return $null
}

function Get-SiigoCredentialsFromTemplateFlow {
    $template = Get-WorkflowByName $ConnectionTemplateFlowName
    if ($null -eq $template) { return $null }

    $clientData = $template.clientdata | ConvertFrom-Json
    $actions = $clientData.properties.definition.actions
    $authAction = Find-WorkflowAction -Actions $actions -Name "Autenticar_Siigo"
    if ($null -eq $authAction -or $null -eq $authAction.inputs -or $null -eq $authAction.inputs.body) {
        return $null
    }

    $partnerId = ""
    foreach ($candidateName in @("Consultar_notas_credito", "Consultar_facturas_Siigo", "Buscar_producto_activo_Siigo")) {
        $candidate = Find-WorkflowAction -Actions $actions -Name $candidateName
        if ($null -ne $candidate -and $null -ne $candidate.inputs -and $null -ne $candidate.inputs.headers) {
            $partnerProperty = $candidate.inputs.headers.PSObject.Properties["Partner-Id"]
            if ($null -ne $partnerProperty -and ![string]::IsNullOrWhiteSpace([string]$partnerProperty.Value)) {
                $partnerId = [string]$partnerProperty.Value
                break
            }
        }
    }

    return [pscustomobject]@{
        Username = [string]$authAction.inputs.body.username
        AccessKey = [string]$authAction.inputs.body.access_key
        PartnerId = $partnerId
    }
}

function New-OpenApiAction {
    param(
        [string]$OperationId,
        [object]$Parameters,
        [hashtable]$RunAfter = @{}
    )

    return [ordered]@{
        type = "OpenApiConnection"
        inputs = [ordered]@{
            parameters = $Parameters
            host = [ordered]@{
                apiId = "/providers/Microsoft.PowerApps/apis/shared_commondataserviceforapps"
                connectionName = "shared_commondataserviceforapps"
                operationId = $OperationId
            }
            authentication = "@parameters('`$authentication')"
        }
        runAfter = $RunAfter
    }
}

function New-SendEmailAction {
    param(
        [object]$Parameters,
        [hashtable]$RunAfter = @{}
    )

    return [ordered]@{
        type = "OpenApiConnection"
        inputs = [ordered]@{
            parameters = $Parameters
            host = [ordered]@{
                apiId = "/providers/Microsoft.PowerApps/apis/shared_office365"
                connectionName = "shared_office365"
                operationId = "SendEmailV2"
            }
            authentication = "@parameters('`$authentication')"
        }
        runAfter = $RunAfter
    }
}

function New-VariableAction {
    param(
        [string]$Name,
        [string]$Type = "array",
        [object]$Value = @(),
        [hashtable]$RunAfter = @{}
    )

    return [ordered]@{
        type = "InitializeVariable"
        inputs = [ordered]@{
            variables = @(
                [ordered]@{
                    name = $Name
                    type = $Type
                    value = $Value
                }
            )
        }
        runAfter = $RunAfter
    }
}

function New-AppendArrayAction {
    param(
        [string]$Name,
        [string]$Value,
        [hashtable]$RunAfter = @{}
    )

    return [ordered]@{
        type = "AppendToArrayVariable"
        inputs = [ordered]@{
            name = $Name
            value = $Value
        }
        runAfter = $RunAfter
    }
}

function New-CloudBillingAuditClientData {
    param(
        [object]$ConnectionReferences,
        [string]$ResolvedSiigoUsername,
        [string]$ResolvedSiigoAccessKey,
        [string]$ResolvedSiigoPartnerId,
        [string]$ResolvedRecipients,
        [int]$HourBogota,
        [int]$MinuteBogota
    )

    $productLine = @'
@{concat('Dia ',string(coalesce(items('Por_producto_cloud')?['cr07a_billingday'],'')),' - ',coalesce(items('Por_producto_cloud')?['cr07a_ClienteLookup']?['cr07a_nombre'],items('Por_producto_cloud')?['_cr07a_clientelookup_value@OData.Community.Display.V1.FormattedValue'],'Sin cliente'),' - ',coalesce(items('Por_producto_cloud')?['cr07a_productname'],items('Por_producto_cloud')?['cr07a_icpname'],'Sin producto'),' | Siigo ID: ',coalesce(items('Por_producto_cloud')?['cr07a_siigo_invoice_id'],'-'),if(empty(items('Por_producto_cloud')?['cr07a_error_facturacion']),'',concat(' | Error: ',items('Por_producto_cloud')?['cr07a_error_facturacion'])))}
'@

    $siigoLine = @'
@{concat(coalesce(items('Por_factura_siigo')?['name'],items('Por_factura_siigo')?['id'],'Factura'),' - NIT ',coalesce(items('Por_factura_siigo')?['customer']?['identification'],'-'),' - stamp: ',coalesce(items('Por_factura_siigo')?['stamp']?['status'],'sin estado'),' - mail: ',coalesce(items('Por_factura_siigo')?['mail']?['status'],'sin estado'))}
'@

    $emailBody = @'
@{concat('<div style="font-family:Segoe UI,Arial,sans-serif;color:#17263c;"><h2 style="margin:0 0 8px;">Auditoria mensual de facturacion Cloud</h2><p style="margin:0 0 16px;color:#526173;">Periodo: ',outputs('Inicio_mes'),' a ',outputs('Fin_mes_inclusivo'),' | Corte Bogota: ',outputs('Hoy_Bogota'),'</p><table style="border-collapse:collapse;min-width:560px;margin:12px 0 18px;"><tr><th style="text-align:left;border:1px solid #d6dee6;padding:8px;background:#eef3f8;">Indicador</th><th style="text-align:right;border:1px solid #d6dee6;padding:8px;background:#eef3f8;">Cantidad</th></tr><tr><td style="border:1px solid #d6dee6;padding:8px;">Facturas Cloud emitidas en Dataverse</td><td style="border:1px solid #d6dee6;padding:8px;text-align:right;">',string(length(coalesce(body('Listar_facturas_dataverse')?['value'],json('[]')))),'</td></tr><tr><td style="border:1px solid #d6dee6;padding:8px;">Productos Cloud con dia de facturacion revisados</td><td style="border:1px solid #d6dee6;padding:8px;text-align:right;">',string(length(coalesce(body('Listar_productos_cloud')?['value'],json('[]')))),'</td></tr><tr><td style="border:1px solid #d6dee6;padding:8px;">Errores activos en productos Cloud</td><td style="border:1px solid #d6dee6;padding:8px;text-align:right;">',string(length(variables('varErrores'))),'</td></tr><tr><td style="border:1px solid #d6dee6;padding:8px;">Productos Cloud sin emitir</td><td style="border:1px solid #d6dee6;padding:8px;text-align:right;">',string(length(variables('varSinEmitir'))),'</td></tr><tr><td style="border:1px solid #d6dee6;padding:8px;">Facturas consultadas en Siigo</td><td style="border:1px solid #d6dee6;padding:8px;text-align:right;">',string(length(coalesce(body('Consultar_facturas_Siigo')?['results'],json('[]')))),' de ',string(coalesce(body('Consultar_facturas_Siigo')?['pagination']?['total_results'],length(coalesce(body('Consultar_facturas_Siigo')?['results'],json('[]'))))),'</td></tr><tr><td style="border:1px solid #d6dee6;padding:8px;">Facturas Siigo en borrador</td><td style="border:1px solid #d6dee6;padding:8px;text-align:right;">',string(length(variables('varBorradorSiigo'))),'</td></tr><tr><td style="border:1px solid #d6dee6;padding:8px;">Facturas Siigo rechazadas</td><td style="border:1px solid #d6dee6;padding:8px;text-align:right;">',string(length(variables('varRechazadasSiigo'))),'</td></tr><tr><td style="border:1px solid #d6dee6;padding:8px;">Facturas Siigo sin correo enviado</td><td style="border:1px solid #d6dee6;padding:8px;text-align:right;">',string(length(variables('varMailPendienteSiigo'))),'</td></tr></table>',if(greater(coalesce(body('Consultar_facturas_Siigo')?['pagination']?['total_results'],0),100),'<p style="color:#9a4d00;"><strong>Nota:</strong> Siigo devolvio mas de 100 facturas; este flujo reviso la primera pagina.</p>',''),'<h3>Productos sin emitir</h3><p>',if(equals(length(variables('varSinEmitir')),0),'Sin pendientes.',join(variables('varSinEmitir'),'<br>')),'</p><h3>Errores activos</h3><p>',if(equals(length(variables('varErrores')),0),'Sin errores activos.',join(variables('varErrores'),'<br>')),'</p><h3>Revision Siigo: borrador</h3><p>',if(equals(length(variables('varBorradorSiigo')),0),'Sin facturas en borrador.',join(variables('varBorradorSiigo'),'<br>')),'</p><h3>Revision Siigo: rechazadas</h3><p>',if(equals(length(variables('varRechazadasSiigo')),0),'Sin facturas rechazadas.',join(variables('varRechazadasSiigo'),'<br>')),'</p><h3>Revision Siigo: correo pendiente</h3><p>',if(equals(length(variables('varMailPendienteSiigo')),0),'Sin pendientes de correo segun Siigo.',join(variables('varMailPendienteSiigo'),'<br>')),'</p><p style="font-size:12px;color:#607080;margin-top:18px;">Criterio: productos Cloud facturables automaticos con dia de facturacion entre 1 y el dia de corte. Un producto se marca sin emitir cuando no tiene fecha ultima factura dentro del mes. La revision Siigo usa stamp.status y mail.status de /v1/invoices.</p><p style="font-size:12px;color:#607080;">Run: ',workflow()?['run']?['name'],'</p></div>')}
'@

    $emailSubject = @'
@{concat('Auditoria facturacion Cloud - ',outputs('Inicio_mes'),' a ',outputs('Fin_mes_inclusivo'),if(or(greater(length(variables('varErrores')),0),greater(length(variables('varSinEmitir')),0),greater(length(variables('varBorradorSiigo')),0),greater(length(variables('varRechazadasSiigo')),0),greater(length(variables('varMailPendienteSiigo')),0)),' - REVISION',' - OK'))}
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
            Cada_26_fin_dia = [ordered]@{
                recurrence = [ordered]@{
                    frequency = "Month"
                    interval = 1
                    schedule = [ordered]@{
                        monthDays = @(26)
                        hours = @($HourBogota)
                        minutes = @($MinuteBogota)
                    }
                    timeZone = "SA Pacific Standard Time"
                }
                type = "Recurrence"
            }
        }
        actions = [ordered]@{
            Inicio_mes = [ordered]@{
                type = "Compose"
                inputs = "@formatDateTime(convertTimeZone(utcNow(),'UTC','SA Pacific Standard Time'),'yyyy-MM-01')"
                runAfter = @{}
            }
            Fin_mes_exclusivo = [ordered]@{
                type = "Compose"
                inputs = "@formatDateTime(addMonths(outputs('Inicio_mes'),1),'yyyy-MM-dd')"
                runAfter = @{ Inicio_mes = @("Succeeded") }
            }
            Fin_mes_inclusivo = [ordered]@{
                type = "Compose"
                inputs = "@formatDateTime(addDays(outputs('Fin_mes_exclusivo'),-1),'yyyy-MM-dd')"
                runAfter = @{ Fin_mes_exclusivo = @("Succeeded") }
            }
            Hoy_Bogota = [ordered]@{
                type = "Compose"
                inputs = "@formatDateTime(convertTimeZone(utcNow(),'UTC','SA Pacific Standard Time'),'yyyy-MM-dd')"
                runAfter = @{ Fin_mes_inclusivo = @("Succeeded") }
            }
            Dia_corte = [ordered]@{
                type = "Compose"
                inputs = "@int(formatDateTime(convertTimeZone(utcNow(),'UTC','SA Pacific Standard Time'),'dd'))"
                runAfter = @{ Hoy_Bogota = @("Succeeded") }
            }
            Listar_productos_cloud = (New-OpenApiAction -OperationId "ListRecords" -Parameters ([ordered]@{
                entityName = "cr07a_salesperformancerecords"
                '$select' = "cr07a_salesperformancerecordid,cr07a_icpname,cr07a_billingday,cr07a_facturableautomatico,cr07a_facturado,cr07a_fechaultimafactura,cr07a_error_facturacion,cr07a_siigo_invoice_id,cr07a_productname,cr07a_valorventatotalmensual"
                '$filter' = "@{concat('cr07a_facturableautomatico eq true and cr07a_billingday ne null and cr07a_billingday gt 0 and cr07a_billingday le ',outputs('Dia_corte'))}"
                '$expand' = "cr07a_ClienteLookup(`$select=cr07a_nombre,cr07a_nit,cr07a_correofacturacion)"
                '$top' = 5000
            }) -RunAfter @{ Dia_corte = @("Succeeded") })
            Listar_facturas_dataverse = (New-OpenApiAction -OperationId "ListRecords" -Parameters ([ordered]@{
                entityName = "cr07a_facturacions"
                '$select' = "cr07a_facturacionid,cr07a_name,cr07a_fechadeemision,cr07a_siigoinvoiceid,cr07a_siigoinvoicename,cr07a_vertical,cr07a_tipocontrato,cr07a_totalfactura"
                '$filter' = "@{concat('cr07a_fechadeemision ge ',outputs('Inicio_mes'),' and cr07a_fechadeemision lt ',outputs('Fin_mes_exclusivo'),' and cr07a_vertical eq 645250000')}"
                '$top' = 5000
            }) -RunAfter @{ Listar_productos_cloud = @("Succeeded") })
            Autenticar_Siigo = [ordered]@{
                type = "Http"
                inputs = [ordered]@{
                    uri = "https://api.siigo.com/auth"
                    method = "POST"
                    headers = [ordered]@{
                        "Content-Type" = "application/json"
                        "User-Agent" = "CotizadorInternoCloudAudit/1.0"
                    }
                    body = [ordered]@{
                        username = $ResolvedSiigoUsername
                        access_key = $ResolvedSiigoAccessKey
                    }
                }
                runtimeConfiguration = [ordered]@{
                    contentTransfer = [ordered]@{
                        transferMode = "Chunked"
                    }
                }
                runAfter = @{ Listar_facturas_dataverse = @("Succeeded") }
            }
            Consultar_facturas_Siigo = [ordered]@{
                type = "Http"
                inputs = [ordered]@{
                    uri = "@{concat('https://api.siigo.com/v1/invoices?date_start=',outputs('Inicio_mes'),'&date_end=',outputs('Fin_mes_inclusivo'),'&page=1&page_size=100')}"
                    method = "GET"
                    headers = [ordered]@{
                        Authorization = "Bearer @{body('Autenticar_Siigo')?['access_token']}"
                        "Partner-Id" = $ResolvedSiigoPartnerId
                    }
                }
                runtimeConfiguration = [ordered]@{
                    contentTransfer = [ordered]@{
                        transferMode = "Chunked"
                    }
                }
                runAfter = @{ Autenticar_Siigo = @("Succeeded") }
            }
            varErrores = (New-VariableAction -Name "varErrores" -RunAfter @{ Consultar_facturas_Siigo = @("Succeeded") })
            varSinEmitir = (New-VariableAction -Name "varSinEmitir" -RunAfter @{ varErrores = @("Succeeded") })
            varBorradorSiigo = (New-VariableAction -Name "varBorradorSiigo" -RunAfter @{ varSinEmitir = @("Succeeded") })
            varRechazadasSiigo = (New-VariableAction -Name "varRechazadasSiigo" -RunAfter @{ varBorradorSiigo = @("Succeeded") })
            varMailPendienteSiigo = (New-VariableAction -Name "varMailPendienteSiigo" -RunAfter @{ varRechazadasSiigo = @("Succeeded") })
            Por_producto_cloud = [ordered]@{
                type = "Foreach"
                foreach = "@coalesce(body('Listar_productos_cloud')?['value'],json('[]'))"
                actions = [ordered]@{
                    Producto_facturado_este_mes = [ordered]@{
                        type = "If"
                        expression = [ordered]@{
                            and = @(
                                [ordered]@{ equals = @("@empty(items('Por_producto_cloud')?['cr07a_fechaultimafactura'])", $false) },
                                [ordered]@{ greaterOrEquals = @("@items('Por_producto_cloud')?['cr07a_fechaultimafactura']", "@outputs('Inicio_mes')") },
                                [ordered]@{ less = @("@items('Por_producto_cloud')?['cr07a_fechaultimafactura']", "@outputs('Fin_mes_exclusivo')") }
                            )
                        }
                        actions = [ordered]@{}
                        else = [ordered]@{
                            actions = [ordered]@{
                                Agregar_sin_emitir = (New-AppendArrayAction -Name "varSinEmitir" -Value $productLine)
                            }
                        }
                        runAfter = @{}
                    }
                    Producto_con_error = [ordered]@{
                        type = "If"
                        expression = [ordered]@{
                            and = @(
                                [ordered]@{ equals = @("@empty(items('Por_producto_cloud')?['cr07a_error_facturacion'])", $false) }
                            )
                        }
                        actions = [ordered]@{
                            Agregar_error = (New-AppendArrayAction -Name "varErrores" -Value $productLine)
                        }
                        else = [ordered]@{
                            actions = [ordered]@{}
                        }
                        runAfter = @{ Producto_facturado_este_mes = @("Succeeded") }
                    }
                }
                runAfter = @{ varMailPendienteSiigo = @("Succeeded") }
                runtimeConfiguration = [ordered]@{
                    concurrency = [ordered]@{
                        repetitions = 1
                    }
                }
            }
            Por_factura_siigo = [ordered]@{
                type = "Foreach"
                foreach = "@coalesce(body('Consultar_facturas_Siigo')?['results'],json('[]'))"
                actions = [ordered]@{
                    Siigo_en_borrador = [ordered]@{
                        type = "If"
                        expression = [ordered]@{
                            and = @(
                                [ordered]@{ equals = @("@toLower(coalesce(items('Por_factura_siigo')?['stamp']?['status'],''))", "draft") }
                            )
                        }
                        actions = [ordered]@{
                            Agregar_borrador_siigo = (New-AppendArrayAction -Name "varBorradorSiigo" -Value $siigoLine)
                        }
                        else = [ordered]@{
                            actions = [ordered]@{}
                        }
                        runAfter = @{}
                    }
                    Siigo_rechazada = [ordered]@{
                        type = "If"
                        expression = [ordered]@{
                            and = @(
                                [ordered]@{ equals = @("@toLower(coalesce(items('Por_factura_siigo')?['stamp']?['status'],''))", "rejected") }
                            )
                        }
                        actions = [ordered]@{
                            Agregar_rechazada_siigo = (New-AppendArrayAction -Name "varRechazadasSiigo" -Value $siigoLine)
                        }
                        else = [ordered]@{
                            actions = [ordered]@{}
                        }
                        runAfter = @{ Siigo_en_borrador = @("Succeeded") }
                    }
                    Siigo_mail_pendiente = [ordered]@{
                        type = "If"
                        expression = [ordered]@{
                            and = @(
                                [ordered]@{ not = [ordered]@{ equals = @("@toLower(coalesce(items('Por_factura_siigo')?['mail']?['status'],''))", "sent") } }
                            )
                        }
                        actions = [ordered]@{
                            Agregar_mail_pendiente_siigo = (New-AppendArrayAction -Name "varMailPendienteSiigo" -Value $siigoLine)
                        }
                        else = [ordered]@{
                            actions = [ordered]@{}
                        }
                        runAfter = @{ Siigo_rechazada = @("Succeeded") }
                    }
                }
                runAfter = @{ Por_producto_cloud = @("Succeeded") }
                runtimeConfiguration = [ordered]@{
                    concurrency = [ordered]@{
                        repetitions = 1
                    }
                }
            }
            Cuerpo_correo = [ordered]@{
                type = "Compose"
                inputs = $emailBody
                runAfter = @{ Por_factura_siigo = @("Succeeded") }
            }
            Enviar_reporte = (New-SendEmailAction -Parameters ([ordered]@{
                "emailMessage/To" = $ResolvedRecipients
                "emailMessage/Subject" = $emailSubject
                "emailMessage/Body" = "@outputs('Cuerpo_correo')"
                "emailMessage/Importance" = "Normal"
            }) -RunAfter @{ Cuerpo_correo = @("Succeeded") })
            Notificar_error_general = (New-SendEmailAction -Parameters ([ordered]@{
                "emailMessage/To" = $ResolvedRecipients
                "emailMessage/Subject" = "Auditoria facturacion Cloud - error de flujo"
                "emailMessage/Body" = "@concat('<p>No fue posible generar la auditoria mensual de facturacion Cloud.</p><p>Run: ',workflow()?['run']?['name'],'</p><p>Revisa el historial del flujo en Power Automate.</p>')"
                "emailMessage/Importance" = "High"
            }) -RunAfter @{
                Listar_productos_cloud = @("Failed", "TimedOut")
                Listar_facturas_dataverse = @("Failed", "TimedOut")
                Autenticar_Siigo = @("Failed", "TimedOut")
                Consultar_facturas_Siigo = @("Failed", "TimedOut")
                Por_producto_cloud = @("Failed", "TimedOut")
                Por_factura_siigo = @("Failed", "TimedOut")
            })
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

function Select-PacAuthProfileForBaseUrl {
    if (-not (Get-Command pac -ErrorAction SilentlyContinue)) {
        return $false
    }

    $targetUrl = $script:BaseUrl.TrimEnd("/")
    $profiles = & pac auth list 2>$null
    foreach ($line in $profiles) {
        if ($line -notlike "*$targetUrl*") { continue }

        if ($line -match "\[(\d+)\]") {
            $index = $Matches[1]
            $selectOutput = & pac auth select --index $index 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "No se pudo seleccionar el perfil PAC para $targetUrl. Output: $selectOutput"
                return $false
            }

            return $true
        }
    }

    return $false
}

$appsettings = Get-Content -LiteralPath "appsettings.json" -Raw | ConvertFrom-Json
$secrets = Get-UserSecretMap
$requestedSiigoUsername = $SiigoUsername
$requestedSiigoAccessKey = $SiigoAccessKey
$requestedSiigoPartnerId = $SiigoPartnerId

$script:BaseUrl = First-NonEmpty $BaseUrl $secrets["Dataverse:BaseUrl"] (Get-JsonConfigValue $appsettings "Dataverse:BaseUrl")
$script:BaseUrl = $script:BaseUrl.TrimEnd("/")
$TenantId = First-NonEmpty $TenantId $secrets["Dataverse:TenantId"] (Get-JsonConfigValue $appsettings "Dataverse:TenantId") (Get-JsonConfigValue $appsettings "AzureAd:TenantId")
$ClientId = First-NonEmpty $ClientId $secrets["Dataverse:ClientId"] (Get-JsonConfigValue $appsettings "Dataverse:ClientId")
$ClientSecret = First-NonEmpty $ClientSecret $secrets["Dataverse:ClientSecret"] (Get-JsonConfigValue $appsettings "Dataverse:ClientSecret")

if ([string]::IsNullOrWhiteSpace($script:BaseUrl)) { throw "Dataverse BaseUrl is required." }
if ([string]::IsNullOrWhiteSpace($TenantId)) { throw "TenantId is required." }
if ([string]::IsNullOrWhiteSpace($ClientId)) { throw "ClientId is required." }
if ([string]::IsNullOrWhiteSpace($ClientSecret)) { throw "ClientSecret is required." }
if ([string]::IsNullOrWhiteSpace($Recipients)) { throw "Recipients is required." }

$tokenBody = @{
    client_id = $ClientId
    client_secret = $ClientSecret
    scope = "$script:BaseUrl/.default"
    grant_type = "client_credentials"
}
$token = Invoke-RestMethod -Method Post -Uri "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token" -Body $tokenBody -ContentType "application/x-www-form-urlencoded"
$script:AccessToken = $token.access_token

$connectionRefs = Get-ConnectionReferences
$siigoFromTemplate = Get-SiigoCredentialsFromTemplateFlow
$templateUsername = if ($null -ne $siigoFromTemplate) { $siigoFromTemplate.Username } else { "" }
$templateAccessKey = if ($null -ne $siigoFromTemplate) { $siigoFromTemplate.AccessKey } else { "" }
$templatePartnerId = if ($null -ne $siigoFromTemplate) { $siigoFromTemplate.PartnerId } else { "" }
$SiigoUsername = First-NonEmpty $requestedSiigoUsername $templateUsername $secrets["Siigo:Username"] (Get-JsonConfigValue $appsettings "Siigo:Username")
$SiigoAccessKey = First-NonEmpty $requestedSiigoAccessKey $templateAccessKey $secrets["Siigo:AccessKey"] (Get-JsonConfigValue $appsettings "Siigo:AccessKey")
$SiigoPartnerId = First-NonEmpty $requestedSiigoPartnerId $templatePartnerId $secrets["Siigo:PartnerId"] (Get-JsonConfigValue $appsettings "Siigo:PartnerId") "CotizadorInterno"

if ([string]::IsNullOrWhiteSpace($SiigoUsername)) { throw "SiigoUsername is required." }
if ([string]::IsNullOrWhiteSpace($SiigoAccessKey)) { throw "SiigoAccessKey is required." }

$clientData = New-CloudBillingAuditClientData `
    -ConnectionReferences $connectionRefs `
    -ResolvedSiigoUsername $SiigoUsername `
    -ResolvedSiigoAccessKey $SiigoAccessKey `
    -ResolvedSiigoPartnerId $SiigoPartnerId `
    -ResolvedRecipients $Recipients `
    -HourBogota $ReportHourBogota `
    -MinuteBogota $ReportMinuteBogota
$existing = Get-WorkflowByName $FlowName

if ($null -eq $existing) {
    $body = @{
        name = $FlowName
        description = "Envia el reporte mensual de auditoria de facturacion Cloud el dia 26 al final del dia."
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
        description = "Envia el reporte mensual de auditoria de facturacion Cloud el dia 26 al final del dia."
        modernflowtype = 0
        runas = 1
        scope = 4
        clientdata = $clientData
    } | Out-Null
}

Set-WorkflowState -WorkflowId $existing.workflowid -State 1 -Status 2
Write-Host "Activated flow $FlowName ($($existing.workflowid))"

if (-not $SkipPacSolutionComponent) {
    $selectedPacProfile = Select-PacAuthProfileForBaseUrl
    if (-not $selectedPacProfile) {
        Write-Warning "No se encontro un perfil PAC para $script:BaseUrl; se usara el perfil PAC activo."
    }

    $pacOutput = & pac solution add-solution-component --solutionUniqueName $SolutionUniqueName --component $existing.workflowid --componentType 29 --AddRequiredComponents 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "No se pudo agregar el flujo a la solucion con PAC. Output: $pacOutput"
    } else {
        $pacOutput | ForEach-Object { Write-Host $_ }
    }
}

Write-Host "Cloud billing audit flow provisioned."
