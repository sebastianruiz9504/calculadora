param(
    [string]$DataverseUrl = "",
    [string]$OutputDirectory = "",
    [int]$SampleTop = 1
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "artifacts\dataverse-module-tests"
}

function Read-JsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Get-JsonSetting {
    param(
        [object]$Root,
        [Parameter(Mandatory = $true)][string]$Key
    )

    if ($null -eq $Root -or [string]::IsNullOrWhiteSpace($Key)) {
        return $null
    }

    $node = $Root
    foreach ($segment in $Key.Split(":")) {
        if ($null -eq $node) {
            return $null
        }

        $property = $node.PSObject.Properties[$segment]
        if ($null -eq $property) {
            return $null
        }

        $node = $property.Value
    }

    if ($null -eq $node) {
        return $null
    }

    return [string]$node
}

function Get-UserSecrets {
    param([Parameter(Mandatory = $true)][string]$ProjectDirectory)

    $secrets = @{}
    $lines = & dotnet user-secrets list --project $ProjectDirectory 2>$null
    if ($LASTEXITCODE -ne 0) {
        return $secrets
    }

    foreach ($line in $lines) {
        if ($line -match "^(.*?)\s*=\s*(.*)$") {
            $secrets[$Matches[1].Trim()] = $Matches[2]
        }
    }

    return $secrets
}

function Get-Setting {
    param(
        [hashtable]$Secrets,
        [object]$Settings,
        [Parameter(Mandatory = $true)][string]$Key,
        [string]$Default = ""
    )

    if ($Secrets.ContainsKey($Key) -and -not [string]::IsNullOrWhiteSpace([string]$Secrets[$Key])) {
        return [string]$Secrets[$Key]
    }

    $value = Get-JsonSetting -Root $Settings -Key $Key
    if (-not [string]::IsNullOrWhiteSpace($value)) {
        return $value
    }

    return $Default
}

function ConvertTo-ODataQuotedValue {
    param([Parameter(Mandatory = $true)][string]$Value)
    return $Value.Replace("'", "''")
}

function Invoke-DataverseRequest {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Path,
        [object]$Body = $null
    )

    $uri = if ($Path.StartsWith("http", [System.StringComparison]::OrdinalIgnoreCase)) {
        $Path
    } else {
        $relative = $Path.TrimStart("/")
        if (-not $relative.StartsWith("api/data", [System.StringComparison]::OrdinalIgnoreCase)) {
            $relative = "api/data/v9.2/$relative"
        }
        "$($script:DataverseUrl)/$relative"
    }

    $headers = @{
        Authorization = "Bearer $script:AccessToken"
        Accept = "application/json"
        "OData-MaxVersion" = "4.0"
        "OData-Version" = "4.0"
    }

    $request = $null
    $response = $null
    try {
        $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), $uri)
        foreach ($header in $headers.GetEnumerator()) {
            [void]$request.Headers.TryAddWithoutValidation([string]$header.Key, [string]$header.Value)
        }

        if ($null -ne $Body) {
            $jsonBody = $Body | ConvertTo-Json -Depth 30
            $request.Content = [System.Net.Http.StringContent]::new($jsonBody, [System.Text.Encoding]::UTF8, "application/json")
        }

        $response = $script:HttpClient.SendAsync($request).GetAwaiter().GetResult()
        $content = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $json = $null
        if (-not [string]::IsNullOrWhiteSpace($content)) {
            try {
                $json = $content | ConvertFrom-Json
            } catch {
                $json = $null
            }
        }

        return [pscustomobject]@{
            Success = [bool]$response.IsSuccessStatusCode
            StatusCode = [int]$response.StatusCode
            Uri = $uri
            Content = $content
            Json = $json
            Error = if ($response.IsSuccessStatusCode) { "" } else { $content }
        }
    } catch {
        return [pscustomobject]@{
            Success = $false
            StatusCode = 0
            Uri = $uri
            Content = ""
            Json = $null
            Error = $_.Exception.Message
        }
    } finally {
        if ($null -ne $response) {
            $response.Dispose()
        }
        if ($null -ne $request) {
            $request.Dispose()
        }
    }
}

function Get-DataverseCollection {
    param([Parameter(Mandatory = $true)][string]$Path)

    $items = New-Object System.Collections.Generic.List[object]
    $next = $Path
    $page = 0
    while (-not [string]::IsNullOrWhiteSpace($next)) {
        $page++
        if ($page -gt 100) {
            throw "Se alcanzo el limite de paginas consultando Dataverse: $Path"
        }

        $response = Invoke-DataverseRequest -Method Get -Path $next
        if (-not $response.Success) {
            throw "Dataverse rechazo la consulta $Path con estado $($response.StatusCode): $($response.Error)"
        }

        if ($null -ne $response.Json -and $null -ne $response.Json.value) {
            foreach ($item in $response.Json.value) {
                $items.Add($item)
            }
        }

        $nextProperty = $response.Json.PSObject.Properties['@odata.nextLink']
        $next = if ($nextProperty) { [string]$nextProperty.Value } else { "" }
    }

    return $items
}

function Get-AttributeMap {
    param([Parameter(Mandatory = $true)][string]$LogicalName)

    if ($script:AttributeCache.ContainsKey($LogicalName)) {
        return $script:AttributeCache[$LogicalName]
    }

    $quoted = ConvertTo-ODataQuotedValue $LogicalName
    $path = "EntityDefinitions(LogicalName='$quoted')/Attributes?`$select=LogicalName,AttributeType,IsValidForCreate,IsValidForUpdate"
    $attributes = Get-DataverseCollection -Path $path
    $map = @{}
    foreach ($attribute in $attributes) {
        if (-not [string]::IsNullOrWhiteSpace([string]$attribute.LogicalName)) {
            $map[[string]$attribute.LogicalName] = $attribute
        }
    }

    $script:AttributeCache[$LogicalName] = $map
    return $map
}

function Add-Check {
    param(
        [Parameter(Mandatory = $true)][string]$Module,
        [Parameter(Mandatory = $true)][string]$Feature,
        [Parameter(Mandatory = $true)][string]$EntitySet,
        [string]$LogicalName = "",
        [string[]]$Fields = @(),
        [string[]]$WritableFields = @()
    )

    if ([string]::IsNullOrWhiteSpace($EntitySet)) {
        return
    }

    $script:Checks.Add([pscustomobject]@{
        Module = $Module
        Feature = $Feature
        EntitySet = $EntitySet
        LogicalName = $LogicalName
        Fields = @($Fields | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
        WritableFields = @($WritableFields | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
    })
}

function Test-DataverseModuleCheck {
    param([Parameter(Mandatory = $true)][object]$Check)

    $configuredEntitySet = [string]$Check.EntitySet
    $entitySet = $configuredEntitySet
    $logicalName = [string]$Check.LogicalName
    $mappedEntity = $null
    if (-not [string]::IsNullOrWhiteSpace($logicalName) -and $script:EntityLogicalNameMap.ContainsKey($logicalName)) {
        $mappedEntity = $script:EntityLogicalNameMap[$logicalName]
        if (-not [string]::IsNullOrWhiteSpace([string]$mappedEntity.EntitySetName)) {
            $entitySet = [string]$mappedEntity.EntitySetName
        }
    } elseif ($script:EntitySetMap.ContainsKey($configuredEntitySet)) {
        $mappedEntity = $script:EntitySetMap[$configuredEntitySet]
        if ([string]::IsNullOrWhiteSpace($logicalName)) {
            $logicalName = [string]$mappedEntity.LogicalName
        }
    }

    $metadataOk = $null -ne $mappedEntity
    if (-not [string]::IsNullOrWhiteSpace($Check.LogicalName) -and $metadataOk) {
        $metadataOk = [string]::Equals([string]$mappedEntity.LogicalName, [string]$Check.LogicalName, [System.StringComparison]::OrdinalIgnoreCase)
    }

    $primaryId = if ($mappedEntity -and -not [string]::IsNullOrWhiteSpace([string]$mappedEntity.PrimaryIdAttribute)) {
        [string]$mappedEntity.PrimaryIdAttribute
    } else {
        ""
    }

    $readPath = if ([string]::IsNullOrWhiteSpace($primaryId)) {
        "${entitySet}?`$top=$SampleTop"
    } else {
        "${entitySet}?`$select=$primaryId&`$top=$SampleTop"
    }
    $readResponse = Invoke-DataverseRequest -Method Get -Path $readPath
    $readCount = 0
    if ($readResponse.Success -and $null -ne $readResponse.Json -and $null -ne $readResponse.Json.value) {
        $readCount = @($readResponse.Json.value).Count
    }

    $missingFields = @()
    $nonWritableFields = @()
    if ($metadataOk -and -not [string]::IsNullOrWhiteSpace($logicalName)) {
        try {
            $attributeMap = Get-AttributeMap -LogicalName $logicalName
            foreach ($field in @($Check.Fields)) {
                if (-not $attributeMap.ContainsKey($field)) {
                    $missingFields += $field
                }
            }

            foreach ($field in @($Check.WritableFields)) {
                if (-not $attributeMap.ContainsKey($field)) {
                    $missingFields += $field
                    continue
                }

                $attribute = $attributeMap[$field]
                $canCreate = $false
                $canUpdate = $false
                if ($null -ne $attribute.PSObject.Properties["IsValidForCreate"]) {
                    $canCreate = [bool]$attribute.IsValidForCreate
                }
                if ($null -ne $attribute.PSObject.Properties["IsValidForUpdate"]) {
                    $canUpdate = [bool]$attribute.IsValidForUpdate
                }

                if (-not ($canCreate -or $canUpdate)) {
                    $nonWritableFields += $field
                }
            }
        } catch {
            $missingFields += "metadata: $($_.Exception.Message)"
        }
    }

    $ok = $metadataOk -and $readResponse.Success -and $missingFields.Count -eq 0 -and $nonWritableFields.Count -eq 0
    $messageParts = New-Object System.Collections.Generic.List[string]
    if (-not $metadataOk) {
        if ($mappedEntity) {
            $messageParts.Add("EntitySetName '$configuredEntitySet' existe, pero LogicalName esperado '$($Check.LogicalName)' no coincide con '$($mappedEntity.LogicalName)'.")
        } else {
            $messageParts.Add("No se encontro LogicalName '$logicalName' ni EntitySetName '$configuredEntitySet' en metadata.")
        }
    }
    if (-not $readResponse.Success) {
        $readError = [string]$readResponse.Error
        if ($readError.Length -gt 300) {
            $readError = "$($readError.Substring(0, 300))..."
        }
        $messageParts.Add("Lectura fallo con estado $($readResponse.StatusCode): $readError")
    }
    if ($missingFields.Count -gt 0) {
        $messageParts.Add("Campos faltantes: $($missingFields -join ', ')")
    }
    if ($nonWritableFields.Count -gt 0) {
        $messageParts.Add("Campos no escribibles para formularios: $($nonWritableFields -join ', ')")
    }

    return [pscustomobject]@{
        Module = $Check.Module
        Feature = $Check.Feature
        EntitySet = $configuredEntitySet
        ResolvedEntitySet = $entitySet
        LogicalName = $logicalName
        MetadataOk = $metadataOk
        ReadOk = [bool]$readResponse.Success
        ReadStatus = $readResponse.StatusCode
        ReadUri = $readResponse.Uri
        SampleRows = $readCount
        MissingFields = @($missingFields | Select-Object -Unique)
        NonWritableFields = @($nonWritableFields | Select-Object -Unique)
        Ok = $ok
        Message = ($messageParts -join " ")
    }
}

$settings = Read-JsonFile -Path (Join-Path $repoRoot "appsettings.json")
$secrets = Get-UserSecrets -ProjectDirectory $repoRoot

$script:DataverseUrl = if ([string]::IsNullOrWhiteSpace($DataverseUrl)) {
    Get-Setting -Secrets $secrets -Settings $settings -Key "Dataverse:BaseUrl"
} else {
    $DataverseUrl
}
$script:DataverseUrl = $script:DataverseUrl.TrimEnd("/")

$tenantId = Get-Setting -Secrets $secrets -Settings $settings -Key "Dataverse:TenantId"
if ([string]::IsNullOrWhiteSpace($tenantId)) {
    $tenantId = Get-Setting -Secrets $secrets -Settings $settings -Key "AzureAd:TenantId"
}
$clientId = Get-Setting -Secrets $secrets -Settings $settings -Key "Dataverse:ClientId"
if ([string]::IsNullOrWhiteSpace($clientId)) {
    $clientId = Get-Setting -Secrets $secrets -Settings $settings -Key "AzureAd:ClientId"
}
$clientSecret = Get-Setting -Secrets $secrets -Settings $settings -Key "Dataverse:ClientSecret"
if ([string]::IsNullOrWhiteSpace($clientSecret)) {
    $clientSecret = Get-Setting -Secrets $secrets -Settings $settings -Key "AzureAd:ClientSecret"
}
$authority = Get-Setting -Secrets $secrets -Settings $settings -Key "AzureAd:Instance" -Default "https://login.microsoftonline.com/"
$authority = $authority.TrimEnd("/")

if ([string]::IsNullOrWhiteSpace($script:DataverseUrl) -or [string]::IsNullOrWhiteSpace($tenantId) -or [string]::IsNullOrWhiteSpace($clientId) -or [string]::IsNullOrWhiteSpace($clientSecret)) {
    throw "Faltan credenciales app-only. Configura Dataverse:BaseUrl, TenantId, ClientId y ClientSecret en user-secrets o appsettings."
}

Write-Host "Obteniendo token app-only para Dataverse..."
$tokenResponse = Invoke-RestMethod -Method Post -Uri "$authority/$tenantId/oauth2/v2.0/token" -ContentType "application/x-www-form-urlencoded" -Body @{
    client_id = $clientId
    client_secret = $clientSecret
    grant_type = "client_credentials"
    scope = "$script:DataverseUrl/.default"
}
$script:AccessToken = [string]$tokenResponse.access_token
if ([string]::IsNullOrWhiteSpace($script:AccessToken)) {
    throw "No fue posible obtener token app-only para Dataverse."
}
$script:HttpClient = [System.Net.Http.HttpClient]::new()
$script:HttpClient.Timeout = [TimeSpan]::FromSeconds(120)

$script:AttributeCache = @{}
$script:Checks = New-Object System.Collections.Generic.List[object]

$whoAmI = Invoke-DataverseRequest -Method Get -Path "WhoAmI()"
if (-not $whoAmI.Success) {
    throw "Dataverse rechazo WhoAmI con estado $($whoAmI.StatusCode): $($whoAmI.Error)"
}

Write-Host "Cargando metadata de entidades..."
$entityDefinitions = Get-DataverseCollection -Path "EntityDefinitions?`$select=LogicalName,EntitySetName,PrimaryIdAttribute,PrimaryNameAttribute"
$script:EntitySetMap = @{}
$script:EntityLogicalNameMap = @{}
foreach ($entity in $entityDefinitions) {
    if (-not [string]::IsNullOrWhiteSpace([string]$entity.EntitySetName)) {
        $script:EntitySetMap[[string]$entity.EntitySetName] = $entity
    }
    if (-not [string]::IsNullOrWhiteSpace([string]$entity.LogicalName)) {
        $script:EntityLogicalNameMap[[string]$entity.LogicalName] = $entity
    }
}

$clientsSet = "cr07a_clientes"
$productsSet = "cr07a_preciosclouds"
$salesPerformanceSet = Get-Setting -Secrets $secrets -Settings $settings -Key "Dataverse:SalesPerformanceTableSetName" -Default "cr07a_salesperformancerecords"
$salesPerformanceLogical = "cr07a_salesperformancerecord"
$scenariosSet = Get-Setting -Secrets $secrets -Settings $settings -Key "Dataverse:ScenariosTableSetName" -Default "cr07a_negocioscomercialeses"
$scenariosLogical = Get-Setting -Secrets $secrets -Settings $settings -Key "Dataverse:ScenariosTableName" -Default "cr07a_negocioscomerciales"
$scoresSet = Get-Setting -Secrets $secrets -Settings $settings -Key "Scores:TableSetName" -Default "cr07a_contractrecord1s"
$scoresLogical = Get-Setting -Secrets $secrets -Settings $settings -Key "Scores:TableName" -Default "cr07a_contractrecord1"
$employeesSet = Get-Setting -Secrets $secrets -Settings $settings -Key "Nomina:EmployeeTableSetName" -Default "cr07a_empleados"
$employeesLogical = Get-Setting -Secrets $secrets -Settings $settings -Key "Nomina:EmployeeTableName" -Default "cr07a_empleado"
$payrollSet = Get-Setting -Secrets $secrets -Settings $settings -Key "Nomina:PayrollTableSetName" -Default "cr07a_nominas"
$payrollLogical = Get-Setting -Secrets $secrets -Settings $settings -Key "Nomina:PayrollTableName" -Default "cr07a_nomina"
$expensesSet = Get-Setting -Secrets $secrets -Settings $settings -Key "SupplierPortal:ExpensesTableSetName" -Default "cr07a_gastodelaempresas"
$expensesLogical = Get-Setting -Secrets $secrets -Settings $settings -Key "SupplierPortal:ExpensesTableName" -Default "cr07a_gastodelaempresa"
$billingSet = Get-Setting -Secrets $secrets -Settings $settings -Key "Dashboard:BillingTableSetName" -Default "cr07a_facturacions"
$billingLogical = Get-Setting -Secrets $secrets -Settings $settings -Key "Dashboard:BillingTableLogicalName" -Default "cr07a_facturacion"
$copiersLineSet = Get-Setting -Secrets $secrets -Settings $settings -Key "Dashboard:CopiersTableSetName" -Default "cr07a_productoscopiers"
$copiersLineLogical = Get-Setting -Secrets $secrets -Settings $settings -Key "Dashboard:CopiersTableLogicalName" -Default "cr07a_productoscopiers"
$lineAssignmentSet = Get-Setting -Secrets $secrets -Settings $settings -Key "Dashboard:CopiersLineEquipmentAssignmentTableSetName" -Default "cr07a_asignacionequipolineacopierses"
$lineAssignmentLogical = Get-Setting -Secrets $secrets -Settings $settings -Key "Dashboard:CopiersLineEquipmentAssignmentLogicalName" -Default "cr07a_asignacionequipolineacopiers"
$reportClientSet = Get-Setting -Secrets $secrets -Settings $settings -Key "Reportes:Client:TableSetName" -Default $clientsSet
$reportClientLogical = Get-Setting -Secrets $secrets -Settings $settings -Key "Reportes:Client:TableLogicalName" -Default "cr07a_cliente"
$ticketSet = Get-Setting -Secrets $secrets -Settings $settings -Key "Reportes:Ticket:TableSetName" -Default "cr07a_tickets"
$ticketLogical = Get-Setting -Secrets $secrets -Settings $settings -Key "Reportes:Ticket:TableLogicalName" -Default "cr07a_ticket"
$generatedReportSet = Get-Setting -Secrets $secrets -Settings $settings -Key "Reportes:GeneratedReport:TableSetName" -Default "cr07a_m365generatedreports"
$generatedReportLogical = Get-Setting -Secrets $secrets -Settings $settings -Key "Reportes:GeneratedReport:TableLogicalName" -Default "cr07a_m365generatedreport"
$reportAttachmentSet = Get-Setting -Secrets $secrets -Settings $settings -Key "Reportes:Attachment:TableSetName" -Default "cr07a_m365reportattachments"
$reportAttachmentLogical = Get-Setting -Secrets $secrets -Settings $settings -Key "Reportes:Attachment:TableLogicalName" -Default "cr07a_m365reportattachment"
$m365ConnectionSet = Get-Setting -Secrets $secrets -Settings $settings -Key "M365:Dataverse:ConnectionTableSetName" -Default "cr07a_m365tenantconnections"
$m365ConnectionLogical = Get-Setting -Secrets $secrets -Settings $settings -Key "M365:Dataverse:ConnectionTableLogicalName" -Default "cr07a_m365tenantconnection"
$m365SnapshotSet = Get-Setting -Secrets $secrets -Settings $settings -Key "M365:Dataverse:SecuritySnapshot:TableSetName" -Default "cr07a_m365securitysnapshots"
$m365SnapshotLogical = Get-Setting -Secrets $secrets -Settings $settings -Key "M365:Dataverse:SecuritySnapshot:TableLogicalName" -Default "cr07a_m365securitysnapshot"
$planRioSet = Get-Setting -Secrets $secrets -Settings $settings -Key "PlanRio:TableSetName" -Default "cr07a_planrioentrenos"
$planRioLogical = Get-Setting -Secrets $secrets -Settings $settings -Key "PlanRio:TableLogicalName" -Default "cr07a_planrioentreno"

Add-Check -Module "Calculator" -Feature "Productos y costos de cotizacion" -EntitySet $productsSet -LogicalName "cr07a_precioscloud" -Fields @("cr07a_priceableitemdescription", "cr07a_purchaseprice", "cr07a_suggestedretailprice", "cr07a_acelerador") -WritableFields @("cr07a_priceableitemdescription", "cr07a_purchaseprice", "cr07a_suggestedretailprice", "cr07a_acelerador")
Add-Check -Module "Calculator" -Feature "Clientes lookup" -EntitySet $clientsSet -LogicalName "cr07a_cliente" -Fields @("cr07a_nombre", "cr07a_nit")
Add-Check -Module "Calculator" -Feature "Escenarios guardados" -EntitySet $scenariosSet -LogicalName $scenariosLogical -Fields @("cr07a_name", "cr07a_scenarioid", "cr07a_scenarioname", "cr07a_dealtype", "cr07a_requiresproration", "cr07a_startdate", "cr07a_enddate", "cr07a_linesjson", "cr07a_lastresultjson", "cr07a_systemuserid", "cr07a_displayname", "cr07a_email") -WritableFields @("cr07a_name", "cr07a_scenarioid", "cr07a_scenarioname", "cr07a_dealtype", "cr07a_requiresproration", "cr07a_startdate", "cr07a_enddate", "cr07a_linesjson", "cr07a_lastresultjson", "cr07a_systemuserid", "cr07a_displayname", "cr07a_email")
Add-Check -Module "Renovaciones" -Feature "Tablero y actualizacion de renovaciones" -EntitySet $salesPerformanceSet -LogicalName $salesPerformanceLogical -Fields @("cr07a_icpname", "cr07a_fecharenovacion", "cr07a_quantity", "cr07a_valorventaunidadusd", "cr07a_billingday", "cr07a_sitieneiva", "cr07a_facturableautomatico", "cr07a_productline", "cr07a_contracttype", "cr07a_clientelookup", "cr07a_producto") -WritableFields @("cr07a_fecharenovacion", "cr07a_billingday", "cr07a_sitieneiva", "cr07a_facturableautomatico", "cr07a_productline", "cr07a_contracttype", "cr07a_clientelookup", "cr07a_producto")
Add-Check -Module "Puntajes" -Feature "Verificacion, cierre y movimientos" -EntitySet $scoresSet -LogicalName $scoresLogical -Fields @("cr07a_contractstartdate", "cr07a_score", "cr07a_aprovisionamientodetallelargo", "cr07a_description", "cr07a_commission", "cr07a_cliente", "cr07a_vendedor", "cr07a_oferta", "cr07a_verificado", "cr07a_esprimercontratoconelcliente", "cr07a_tipodecontrato", "cr07a_linea", "cr07a_vertical", "cr07a_adicionales") -WritableFields @("cr07a_score", "cr07a_commission", "cr07a_verificado")
Add-Check -Module "LiquidacionNominas" -Feature "Empleados base" -EntitySet $employeesSet -LogicalName $employeesLogical -Fields @("cr07a_nombrecompleto", "cr07a_sueldomensual", "cr07a_auxconectividad", "cr07a_topecomisional", "cr07a_factorcopiers", "cr07a_factorcloud", "cr07a_usuario", "cr07a_tipocontrato", "cr07a_modulos", "cr07a_correo") -WritableFields @("cr07a_modulos")
Add-Check -Module "LiquidacionNominas" -Feature "Nominas generadas" -EntitySet $payrollSet -LogicalName $payrollLogical -Fields @("cr07a_numerodenomina", "cr07a_idempleado", "cr07a_fechapago", "cr07a_sueldobase", "cr07a_auxilio", "cr07a_diasdelmes", "cr07a_diastrabajados", "cr07a_sueldobruto", "cr07a_salud", "cr07a_pension", "cr07a_montopagado") -WritableFields @("cr07a_numerodenomina", "cr07a_idempleado", "cr07a_fechapago", "cr07a_sueldobase", "cr07a_montopagado")
Add-Check -Module "RH" -Feature "Empleados" -EntitySet $employeesSet -LogicalName $employeesLogical -Fields @("cr07a_nombrecompleto", "cr07a_correo", "cr07a_usuario")
Add-Check -Module "RH/GestionHumana" -Feature "Solicitudes de vacaciones" -EntitySet "cr07a_solicituddevacacioneses" -LogicalName "cr07a_solicituddevacaciones" -Fields @("cr07a_numerodesolicitud", "cr07a_idempleado", "cr07a_fechainicio", "cr07a_fechafin", "cr07a_cantidaddedias", "cr07a_formato", "cr07a_formato_name") -WritableFields @("cr07a_numerodesolicitud", "cr07a_idempleado", "cr07a_fechainicio", "cr07a_fechafin", "cr07a_cantidaddedias")
Add-Check -Module "RH" -Feature "Incapacidades" -EntitySet "cr07a_incapacidads" -LogicalName "cr07a_incapacidad" -Fields @("cr07a_numerodeincapacidad", "cr07a_idempleado", "cr07a_fechainicio", "cr07a_fechafin", "cr07a_motivo", "cr07a_adjuntarincapacidad", "cr07a_adjuntarincapacidad_name")
Add-Check -Module "Permissions" -Feature "Permisos por empleado" -EntitySet $employeesSet -LogicalName $employeesLogical -Fields @("cr07a_nombrecompleto", "cr07a_correo", "cr07a_usuario", "cr07a_modulos") -WritableFields @("cr07a_modulos")
Add-Check -Module "PortalProveedores/PublicDataExport" -Feature "Gastos proveedores" -EntitySet $expensesSet -LogicalName $expensesLogical -Fields @("createdon")
Add-Check -Module "Dashboard/RegistroPagosClientes" -Feature "Facturacion y pagos" -EntitySet $billingSet -LogicalName $billingLogical -Fields @("cr07a_name", "cr07a_fechadeemision", "cr07a_nitempresa", "cr07a_clientenit", "cr07a_vertical", "cr07a_tipocontrato", "cr07a_fechavencimiento", "cr07a_totalfactura", "cr07a_iva", "cr07a_impuestovalor", "cr07a_publicurl", "cr07a_fechadepago", "cr07a_valorpago", "cr07a_reteica", "cr07a_reteivavalor", "cr07a_retefuentevalor", "cr07a_diferencia") -WritableFields @("cr07a_name", "cr07a_fechadeemision", "cr07a_nitempresa", "cr07a_vertical", "cr07a_tipocontrato", "cr07a_fechavencimiento", "cr07a_totalfactura", "cr07a_iva", "cr07a_impuestovalor", "cr07a_publicurl", "cr07a_fechadepago", "cr07a_valorpago", "cr07a_reteica", "cr07a_reteivavalor", "cr07a_retefuentevalor")
Add-Check -Module "Dashboard/Copiers" -Feature "Lineas de producto copiers" -EntitySet $copiersLineSet -LogicalName $copiersLineLogical -Fields @("cr07a_producto", "cr07a_cantidad", "cr07a_valorunidadantesdeiva", "cr07a_diadefacturacion", "cr07a_operacionesincluidas", "cr07a_cliente", "cr07a_valorunidadconiva", "cr07a_totalconiva") -WritableFields @("cr07a_producto", "cr07a_cantidad", "cr07a_valorunidadantesdeiva", "cr07a_diadefacturacion", "cr07a_operacionesincluidas", "cr07a_cliente")
Add-Check -Module "Dashboard/Copiers" -Feature "Asignacion equipo-linea" -EntitySet $lineAssignmentSet -LogicalName $lineAssignmentLogical -Fields @("cr07a_name", "cr07a_cliente", "cr07a_lineaproductocopiers", "cr07a_equipo") -WritableFields @("cr07a_cliente", "cr07a_lineaproductocopiers", "cr07a_equipo")
Add-Check -Module "Copiers" -Feature "Equipos" -EntitySet "cr07a_equipos" -LogicalName "cr07a_equipo" -Fields @("cr07a_nombredelequipo", "cr07a_cliente", "cr07a_serial", "cr07a_categoriadeequipo", "cr07a_referencia", "cr07a_observaciones", "cr07a_area", "cr07a_sede", "cr07a_valorcomercial", "cr07a_estadodelequipo") -WritableFields @("cr07a_cliente", "cr07a_estadodelequipo", "cr07a_valorcomercial")
Add-Check -Module "Copiers" -Feature "Movimientos de equipos" -EntitySet "cr07a_movimientosequiposes" -LogicalName "cr07a_movimientosequipos" -Fields @("cr07a_name", "cr07a_equipo", "cr07a_cliente", "cr07a_fecha")
Add-Check -Module "Copiers" -Feature "Mantenimientos" -EntitySet "cr07a_mantenimientos" -LogicalName "cr07a_mantenimiento" -Fields @("cr07a_mantenimiento1", "cr07a_iddeequipo", "cr07a_fechademantenimiento", "cr07a_descripciondelmantenimiento", "cr07a_cliente", "cr07a_actadeentregadeservicio", "cr07a_actadeentregadeservicio_name", "cr07a_tipodemantenimiento", "cr07a_estadodelmantenimiento") -WritableFields @("cr07a_mantenimiento1", "cr07a_iddeequipo", "cr07a_fechademantenimiento", "cr07a_descripciondelmantenimiento", "cr07a_cliente", "cr07a_tipodemantenimiento", "cr07a_estadodelmantenimiento")
Add-Check -Module "Copiers" -Feature "Contadores formulario" -EntitySet "cr07a_contadoreses" -LogicalName "cr07a_contadores" -Fields @("cr07a_equipo", "cr07a_fechadetomadecontador", "cr07a_maquina", "cr07a_contador", "cr07a_contadorescaner", "cr07a_paginadeestado", "cr07a_paginadeestado_name") -WritableFields @("cr07a_equipo", "cr07a_fechadetomadecontador", "cr07a_maquina", "cr07a_contador", "cr07a_contadorescaner")
Add-Check -Module "Dashboard/Copiers" -Feature "Contadores mensuales lectura" -EntitySet "cr07a_contadoresmensualesequipos" -LogicalName "cr07a_contadoresmensualesequipo" -Fields @("cr07a_dt_fechalectura", "cr07a_equipo", "cr07a_dt_contadorpaginas", "cr07a_dt_paginasescaneadas")
Add-Check -Module "Copiers/Inventario" -Feature "Suministros" -EntitySet "cr07a_suministros" -LogicalName "cr07a_suministro" -Fields @("cr07a_nombredelsuministro", "cr07a_cantidad", "cr07a_fechadecompra", "cr07a_estadodelsuministro") -WritableFields @("cr07a_cantidad", "cr07a_estadodelsuministro")
Add-Check -Module "Copiers/Inventario" -Feature "Facturas proveedor copiers" -EntitySet "cr07a_facturasproveedorescopiers" -LogicalName "cr07a_facturasproveedorescopiers" -Fields @("cr07a_name", "cr07a_suministro", "cr07a_cantidad", "cr07a_valorunitarioantesdeiva", "cr07a_aprobadoeingresado") -WritableFields @("cr07a_name", "cr07a_suministro", "cr07a_cantidad", "cr07a_valorunitarioantesdeiva", "cr07a_aprobadoeingresado")
Add-Check -Module "Copiers" -Feature "Entregas" -EntitySet "cr07a_entregas" -LogicalName "cr07a_entrega" -Fields @("cr07a_entrega1", "cr07a_iddecliente", "cr07a_iddesuministro", "cr07a_fechadeentrega", "cr07a_cantidadentregada", "cr07a_estadodeentrega", "cr07a_comprobantedeentrega", "cr07a_comprobantedeentrega_name") -WritableFields @("cr07a_entrega1", "cr07a_iddecliente", "cr07a_iddesuministro", "cr07a_fechadeentrega", "cr07a_cantidadentregada", "cr07a_estadodeentrega")
Add-Check -Module "Envios/Transportador" -Feature "Envios" -EntitySet "cr07a_envios" -LogicalName "cr07a_envio" -Fields @("cr07a_name", "cr07a_origen", "cr07a_destino", "cr07a_cliente", "cr07a_queseenvia", "cr07a_observaciones", "cr07a_quienrecibe", "cr07a_telefonorecibe", "cr07a_estado", "cr07a_fechaprogramada", "cr07a_transportador", "cr07a_valorflete", "cr07a_recogidaaprobada", "cr07a_actaentrega", "cr07a_actaentrega_name") -WritableFields @("cr07a_origen", "cr07a_destino", "cr07a_cliente", "cr07a_queseenvia", "cr07a_observaciones", "cr07a_quienrecibe", "cr07a_telefonorecibe", "cr07a_estado", "cr07a_fechaprogramada", "cr07a_transportador", "cr07a_valorflete", "cr07a_recogidaaprobada")
Add-Check -Module "SoporteCloud/Reportes" -Feature "Tickets" -EntitySet $ticketSet -LogicalName $ticketLogical -Fields @("cr07a_tituloticket", "cr07a_descripcion", "cr07a_fechacreacion", "cr07a_estado", "cr07a_tipo", "cr07a_cliente", "cr07a_categoria", "cr07a_horastomadas", "cr07a_metodo", "cr07a_solucion") -WritableFields @("cr07a_tituloticket", "cr07a_descripcion", "cr07a_fechacreacion", "cr07a_estado", "cr07a_tipo", "cr07a_cliente", "cr07a_categoria", "cr07a_horastomadas", "cr07a_metodo", "cr07a_solucion")
Add-Check -Module "SoporteCloud" -Feature "Capacitaciones" -EntitySet "cr07a_capacitacions" -LogicalName "cr07a_capacitacion" -Fields @("cr07a_temacapacitacion", "cr07a_cliente", "cr07a_fecha", "cr07a_duracionhoras", "cr07a_cantidadasistentes", "cr07a_tema", "ownerid")
Add-Check -Module "SoporteCloud/Encuestas" -Feature "Temas" -EntitySet "cr07a_capacitaciontemas" -LogicalName "cr07a_capacitaciontema" -Fields @("cr07a_name", "cr07a_descripcion", "cr07a_activo") -WritableFields @("cr07a_name", "cr07a_descripcion", "cr07a_activo")
Add-Check -Module "SoporteCloud/Encuestas" -Feature "Preguntas" -EntitySet "cr07a_capacitacionpreguntas" -LogicalName "cr07a_capacitacionpregunta" -Fields @("cr07a_name", "cr07a_tema", "cr07a_componente", "cr07a_tiporespuesta", "cr07a_pregunta", "cr07a_orden", "cr07a_puntajemaximo", "cr07a_activa") -WritableFields @("cr07a_name", "cr07a_tema", "cr07a_componente", "cr07a_tiporespuesta", "cr07a_pregunta", "cr07a_orden", "cr07a_puntajemaximo", "cr07a_activa")
Add-Check -Module "SoporteCloud/Encuestas" -Feature "Opciones" -EntitySet "cr07a_capacitacionopcions" -LogicalName "cr07a_capacitacionopcion" -Fields @("cr07a_name", "cr07a_pregunta", "cr07a_opcion", "cr07a_escorrecta", "cr07a_puntos", "cr07a_orden", "cr07a_activa") -WritableFields @("cr07a_name", "cr07a_pregunta", "cr07a_opcion", "cr07a_escorrecta", "cr07a_puntos", "cr07a_orden", "cr07a_activa")
Add-Check -Module "SoporteCloud/Encuestas" -Feature "Sesiones" -EntitySet "cr07a_capacitacionsesions" -LogicalName "cr07a_capacitacionsesion" -Fields @("cr07a_name", "cr07a_tema", "cr07a_cliente", "cr07a_fecha", "cr07a_codigo", "cr07a_estado", "cr07a_cerradaen") -WritableFields @("cr07a_name", "cr07a_tema", "cr07a_cliente", "cr07a_fecha", "cr07a_codigo", "cr07a_estado", "cr07a_cerradaen")
Add-Check -Module "SoporteCloud/EncuestasPublicas" -Feature "Participantes" -EntitySet "cr07a_capacitacionparticipantes" -LogicalName "cr07a_capacitacionparticipante" -Fields @("cr07a_name", "cr07a_sesion", "cr07a_email", "cr07a_empresa", "cr07a_puntaje", "cr07a_puntajemaximo", "cr07a_porcentaje", "cr07a_respondidaen") -WritableFields @("cr07a_name", "cr07a_sesion", "cr07a_email", "cr07a_empresa", "cr07a_puntaje", "cr07a_puntajemaximo", "cr07a_porcentaje", "cr07a_respondidaen")
Add-Check -Module "SoporteCloud/EncuestasPublicas" -Feature "Respuestas" -EntitySet "cr07a_capacitacionrespuestas" -LogicalName "cr07a_capacitacionrespuesta" -Fields @("cr07a_name", "cr07a_sesion", "cr07a_participante", "cr07a_pregunta", "cr07a_opcion", "cr07a_componente", "cr07a_puntos", "cr07a_puntajemaximo", "cr07a_correcta", "cr07a_valornumerico", "cr07a_respuestatexto", "cr07a_respondidaen") -WritableFields @("cr07a_name", "cr07a_sesion", "cr07a_participante", "cr07a_pregunta", "cr07a_opcion", "cr07a_componente", "cr07a_puntos", "cr07a_puntajemaximo", "cr07a_correcta", "cr07a_valornumerico", "cr07a_respuestatexto", "cr07a_respondidaen")
Add-Check -Module "SoporteCloud/M365" -Feature "Conexiones tenant" -EntitySet $m365ConnectionSet -LogicalName $m365ConnectionLogical -Fields @("cr07a_name", "cr07a_cliente", "cr07a_clienteidinterno", "cr07a_tenantid", "cr07a_tenanthint", "cr07a_estadoconexion", "cr07a_fechaconexion", "cr07a_permisossolicitados", "cr07a_resultadoconsentimiento", "cr07a_adminconsent", "cr07a_scopeconsentido", "cr07a_error", "cr07a_errordescripcion", "cr07a_fechaultimaprueba", "cr07a_ultimapruebaexitosa", "cr07a_resultadoultimaprueba") -WritableFields @("cr07a_name", "cr07a_cliente", "cr07a_tenantid", "cr07a_estadoconexion", "cr07a_error", "cr07a_resultadoultimaprueba")
Add-Check -Module "SoporteCloud/M365" -Feature "Snapshots seguridad" -EntitySet $m365SnapshotSet -LogicalName $m365SnapshotLogical -Fields @("cr07a_name", "cr07a_cliente", "cr07a_clienteidinterno", "cr07a_tenantid", "cr07a_periodo", "cr07a_securescoreactual", "cr07a_securescoremaximo", "cr07a_alertashigh", "cr07a_alertasmedium", "cr07a_alertaslow", "cr07a_incidentesactivos", "cr07a_incidentesresueltos", "cr07a_recomendacionestopjson", "cr07a_alertasjson", "cr07a_incidentesjson", "cr07a_fechaconsulta", "cr07a_estadoconsulta", "cr07a_errorconsulta")
Add-Check -Module "SoporteCloud/Reportes" -Feature "Reportes generados" -EntitySet $generatedReportSet -LogicalName $generatedReportLogical -Fields @("cr07a_name", "cr07a_cliente", "cr07a_clienteidinterno", "cr07a_periodo", "cr07a_htmlgenerado", "cr07a_estado", "cr07a_fechageneracion", "cr07a_promptversion", "cr07a_errores") -WritableFields @("cr07a_name", "cr07a_cliente", "cr07a_periodo", "cr07a_htmlgenerado", "cr07a_estado", "cr07a_fechageneracion", "cr07a_promptversion", "cr07a_errores")
Add-Check -Module "SoporteCloud/Reportes" -Feature "Anexos reportes" -EntitySet $reportAttachmentSet -LogicalName $reportAttachmentLogical -Fields @("cr07a_name", "cr07a_reporte", "cr07a_reporteidinterno", "cr07a_filename", "cr07a_contenttype", "cr07a_size", "cr07a_fechacarga") -WritableFields @("cr07a_name", "cr07a_reporte", "cr07a_reporteidinterno", "cr07a_filename", "cr07a_contenttype", "cr07a_size", "cr07a_fechacarga")
Add-Check -Module "Licenciamiento" -Feature "Consumo Intcomex" -EntitySet "cr07a_consumointcomexes" -LogicalName "cr07a_consumointcomex" -Fields @("cr07a_name", "cr07a_accountid", "cr07a_nombrecliente", "cr07a_vendor", "cr07a_producto", "cr07a_dias", "cr07a_mesconsumo", "cr07a_factura", "cr07a_valortotalusd", "cr07a_unidadusd", "cr07a_cantidad", "cr07a_trm", "cr07a_pesostotal", "cr07a_tipocontrato") -WritableFields @("cr07a_name", "cr07a_accountid", "cr07a_nombrecliente", "cr07a_vendor", "cr07a_producto", "cr07a_dias", "cr07a_mesconsumo", "cr07a_factura", "cr07a_valortotalusd", "cr07a_unidadusd", "cr07a_cantidad", "cr07a_trm", "cr07a_pesostotal", "cr07a_tipocontrato")
Add-Check -Module "Licenciamiento" -Feature "Account ID ICP" -EntitySet "cr07a_accountidicps" -LogicalName "cr07a_accountidicp" -Fields @("cr07a_name", "cr07a_cliente", "cr07a_grupoempresarialid", "cr07a_grupoempresarialname") -WritableFields @("cr07a_name", "cr07a_cliente")
Add-Check -Module "CruceLicenciamiento" -Feature "Mapeo cuenta-costo" -EntitySet "cr07a_licenciamientoaccountmaps" -LogicalName "cr07a_licenciamientoaccountmap" -Fields @("cr07a_name", "cr07a_sourceaccountid", "cr07a_sourceaccountname", "cr07a_sourceclientname", "cr07a_targetaccountid", "cr07a_targetaccountname", "cr07a_targetclientid", "cr07a_targetclientname", "cr07a_active", "cr07a_notes") -WritableFields @("cr07a_name", "cr07a_sourceaccountid", "cr07a_sourceaccountname", "cr07a_sourceclientname", "cr07a_targetaccountid", "cr07a_targetaccountname", "cr07a_targetclientid", "cr07a_targetclientname", "cr07a_active", "cr07a_notes")
Add-Check -Module "CuentasCobro" -Feature "Cuentas de cobro" -EntitySet "cr07a_cuentasdecobros" -LogicalName "cr07a_cuentasdecobro" -Fields @("cr07a_name", "cr07a_nombrereceptor", "cr07a_nitocedula", "cr07a_valortotal", "cr07a_retefuenteporcentaje", "cr07a_valorpago", "cr07a_rteftevalor", "cr07a_observaciones", "cr07a_fechadeemision", "cr07a_fechadepago", "cr07a_adjunto", "cr07a_adjunto_name", "cr07a_impresa") -WritableFields @("cr07a_name", "cr07a_nombrereceptor", "cr07a_nitocedula", "cr07a_valortotal", "cr07a_retefuenteporcentaje", "cr07a_valorpago", "cr07a_rteftevalor", "cr07a_observaciones", "cr07a_fechadeemision", "cr07a_fechadepago", "cr07a_impresa")
Add-Check -Module "RebatesInversiones" -Feature "Items manuales PnL" -EntitySet "cr07a_pnlmanualitems" -LogicalName "cr07a_pnlmanualitem" -Fields @("cr07a_name", "cr07a_tipo", "cr07a_fecha", "cr07a_valor") -WritableFields @("cr07a_name", "cr07a_tipo", "cr07a_fecha", "cr07a_valor")
Add-Check -Module "PlanRio" -Feature "Entrenos y registro" -EntitySet $planRioSet -LogicalName $planRioLogical -Fields @("cr07a_name", "cr07a_fecha", "cr07a_dia", "cr07a_semanaplan", "cr07a_iniciodesemana", "cr07a_fase", "cr07a_disciplina", "cr07a_sesion", "cr07a_min", "cr07a_horas", "cr07a_volumenobjetivo", "cr07a_intensidadzona", "cr07a_detalle", "cr07a_nutricionhidratacion", "cr07a_objetivo", "cr07a_estado", "cr07a_duracionreal", "cr07a_distanciareal", "cr07a_fcpromedio", "cr07a_potenciapromedio", "cr07a_notas", "cr07a_origenhoja", "cr07a_filaorigen") -WritableFields @("cr07a_duracionreal", "cr07a_distanciareal", "cr07a_fcpromedio", "cr07a_potenciapromedio", "cr07a_notas")
Add-Check -Module "Sistema" -Feature "Usuarios Dataverse" -EntitySet "systemusers" -LogicalName "systemuser" -Fields @("fullname", "internalemailaddress", "azureactivedirectoryobjectid")
Add-Check -Module "Reportes" -Feature "Clientes para reportes" -EntitySet $reportClientSet -LogicalName $reportClientLogical -Fields @("cr07a_nombre", "cr07a_nombrepersonaacargo", "cr07a_correoelectronico")

Write-Host "Ejecutando $($script:Checks.Count) chequeos de modulos..."
$results = foreach ($check in $script:Checks) {
    Test-DataverseModuleCheck -Check $check
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$jsonPath = Join-Path $OutputDirectory "$timestamp-dataverse-module-tests.json"
$csvPath = Join-Path $OutputDirectory "$timestamp-dataverse-module-tests.csv"

$summary = [pscustomobject]@{
    RanAt = (Get-Date).ToString("o")
    DataverseUrl = $script:DataverseUrl
    UserId = if ($whoAmI.Json) { [string]$whoAmI.Json.UserId } else { "" }
    Total = @($results).Count
    Passed = @($results | Where-Object { $_.Ok }).Count
    Failed = @($results | Where-Object { -not $_.Ok }).Count
    Results = @($results)
}

$summary | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
$results |
    Select-Object Module, Feature, EntitySet, ResolvedEntitySet, LogicalName, MetadataOk, ReadOk, ReadStatus, SampleRows, Ok, Message |
    Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8

Write-Host "Reporte JSON: $jsonPath"
Write-Host "Reporte CSV: $csvPath"
Write-Host "OK: $($summary.Passed) / $($summary.Total)"

$failures = @($results | Where-Object { -not $_.Ok })
if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "Fallas:"
    foreach ($failure in $failures) {
        Write-Host "- [$($failure.Module)] $($failure.Feature) ($($failure.EntitySet)): $($failure.Message)"
    }
    exit 1
}

exit 0
