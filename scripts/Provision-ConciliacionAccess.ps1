param(
    [string]$BaseUrl = "",
    [string]$TenantId = "",
    [string]$ClientId = "",
    [string]$ClientSecret = "",
    [string]$TargetEmail = "sruiz@digitaltechcolombia.com",
    [string]$EmployeeTableSetName = "",
    [string]$EmployeeTableLogicalName = "",
    [string]$EmployeeIdField = "",
    [string]$EmployeeNameField = "",
    [string]$EmployeeEmailField = "cr07a_correo",
    [string]$EmployeeModulesField = "",
    [int]$ModuleOptionValue = 645250022,
    [string]$ModuleLabel = "Conciliacion",
    [int]$LanguageCode = 3082,
    [switch]$SkipPublish
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
    $lines = & dotnet user-secrets list --project .\CotizadorInterno.Web.csproj 2>$null
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

function New-DvLabel {
    param([string]$Text)
    return @{
        "@odata.type" = "Microsoft.Dynamics.CRM.Label"
        LocalizedLabels = @(
            @{
                "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                Label = $Text
                LanguageCode = $LanguageCode
            }
        )
    }
}

function Invoke-Dataverse {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Path,
        [object]$Body = $null,
        [switch]$AllowNotFound
    )

    $uri = if ($Path.StartsWith("http", [System.StringComparison]::OrdinalIgnoreCase)) {
        $Path
    } else {
        "$script:BaseUrl$Path"
    }

    $headers = @{
        Authorization = "Bearer $script:AccessToken"
        Accept = "application/json"
        "OData-MaxVersion" = "4.0"
        "OData-Version" = "4.0"
    }

    try {
        if ($null -eq $Body) {
            return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers
        }

        $json = $Body | ConvertTo-Json -Depth 40
        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -ContentType "application/json" -Body $json
    }
    catch {
        $response = $_.Exception.Response
        if ($AllowNotFound -and $response -and [int]$response.StatusCode -eq 404) { return $null }
        $status = if ($response) { "$([int]$response.StatusCode) $($response.StatusCode)" } else { "sin status" }
        throw "Dataverse $Method fallo ($status) en $Path. $($_.Exception.Message)"
    }
}

function Get-DataverseRows {
    param([string]$Path)

    $rows = New-Object System.Collections.Generic.List[object]
    $next = $Path
    while (![string]::IsNullOrWhiteSpace($next)) {
        $page = Invoke-Dataverse -Method "GET" -Path $next
        foreach ($row in @($page.value)) {
            [void]$rows.Add($row)
        }

        $nextLinkProperty = $page.PSObject.Properties["@odata.nextLink"]
        $next = if ($null -ne $nextLinkProperty) { [string]$nextLinkProperty.Value } else { "" }
    }

    return $rows.ToArray()
}

function Get-ModuleOptionValues {
    $metadataTypes = @(
        "MultiSelectPicklistAttributeMetadata",
        "PicklistAttributeMetadata"
    )

    foreach ($metadataType in $metadataTypes) {
        try {
            $path = "/api/data/v9.2/EntityDefinitions(LogicalName='$EmployeeTableLogicalName')/Attributes(LogicalName='$EmployeeModulesField')/Microsoft.Dynamics.CRM.${metadataType}?`$select=LogicalName&`$expand=OptionSet(`$select=Options)"
            $result = Invoke-Dataverse -Method "GET" -Path $path
            return @($result.OptionSet.Options | ForEach-Object { [int]$_.Value })
        }
        catch {
            continue
        }
    }

    throw "No fue posible leer las opciones de $EmployeeTableLogicalName.$EmployeeModulesField."
}

function Add-ModuleOptionIfMissing {
    param([System.Collections.Generic.HashSet[int]]$ExistingValues)

    if ($ExistingValues.Contains($ModuleOptionValue)) {
        Write-Host "OK opcion existente: $ModuleLabel = $ModuleOptionValue"
        return $false
    }

    Write-Host "Agregando opcion de modulo: $ModuleLabel = $ModuleOptionValue"
    $payload = @{
        EntityLogicalName = $EmployeeTableLogicalName
        AttributeLogicalName = $EmployeeModulesField
        Value = $ModuleOptionValue
        Label = New-DvLabel $ModuleLabel
    }
    Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/InsertOptionValue" -Body $payload | Out-Null
    [void]$ExistingValues.Add($ModuleOptionValue)
    return $true
}

function Publish-Customizations {
    for ($i = 1; $i -le 12; $i++) {
        try {
            Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/PublishAllXml" -Body @{} | Out-Null
            return
        }
        catch {
            if ($i -eq 12) { throw }
            Write-Host "Publish ocupado; reintento $i de 12 en 20 segundos..."
            Start-Sleep -Seconds 20
        }
    }
}

function Get-PropertyValue {
    param([object]$Row, [string]$Name)
    if ($null -eq $Row -or [string]::IsNullOrWhiteSpace($Name)) { return $null }
    $property = $Row.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Read-ModuleValues {
    param([object]$RawValue)

    $values = New-Object System.Collections.Generic.List[int]
    if ($null -eq $RawValue) { return @() }

    if ($RawValue -is [System.Array]) {
        foreach ($item in $RawValue) {
            $parsed = 0
            if ([int]::TryParse([string]$item, [ref]$parsed) -and $parsed -gt 0) {
                [void]$values.Add($parsed)
            }
        }
    } else {
        foreach ($part in ([string]$RawValue).Split(",", [System.StringSplitOptions]::RemoveEmptyEntries)) {
            $parsed = 0
            if ([int]::TryParse($part.Trim(), [ref]$parsed) -and $parsed -gt 0) {
                [void]$values.Add($parsed)
            }
        }
    }

    return @($values | Sort-Object -Unique)
}

function ConvertTo-MultiSelectPayload {
    param([int[]]$Values)
    $normalized = @($Values | Where-Object { $_ -gt 0 } | Sort-Object -Unique)
    if ($normalized.Count -eq 0) { return $null }
    return ($normalized -join ",")
}

$appsettingsPath = Join-Path (Get-Location) "appsettings.json"
if (-not (Test-Path -LiteralPath $appsettingsPath)) {
    throw "No se encontro appsettings.json en $(Get-Location)."
}

$appsettings = Get-Content -LiteralPath $appsettingsPath -Raw | ConvertFrom-Json
$secrets = Get-UserSecretMap

$BaseUrl = First-NonEmpty $BaseUrl $env:DATAVERSE_BASE_URL $secrets["Dataverse:BaseUrl"] (Get-JsonConfigValue $appsettings "Dataverse:BaseUrl")
$TenantId = First-NonEmpty $TenantId $env:DATAVERSE_TENANT_ID $secrets["Dataverse:TenantId"] (Get-JsonConfigValue $appsettings "Dataverse:TenantId") (Get-JsonConfigValue $appsettings "AzureAd:TenantId")
$ClientId = First-NonEmpty $ClientId $env:DATAVERSE_CLIENT_ID $secrets["Dataverse:ClientId"] (Get-JsonConfigValue $appsettings "Dataverse:ClientId") (Get-JsonConfigValue $appsettings "AzureAd:ClientId")
$ClientSecret = First-NonEmpty $ClientSecret $env:DATAVERSE_CLIENT_SECRET $secrets["Dataverse:ClientSecret"] (Get-JsonConfigValue $appsettings "Dataverse:ClientSecret") $secrets["AzureAd:ClientSecret"] (Get-JsonConfigValue $appsettings "AzureAd:ClientSecret")
$EmployeeTableSetName = First-NonEmpty $EmployeeTableSetName (Get-JsonConfigValue $appsettings "Nomina:EmployeeTableSetName") "cr07a_empleados"
$EmployeeTableLogicalName = First-NonEmpty $EmployeeTableLogicalName (Get-JsonConfigValue $appsettings "Nomina:EmployeeTableName") "cr07a_empleado"
$EmployeeIdField = First-NonEmpty $EmployeeIdField (Get-JsonConfigValue $appsettings "Nomina:EmployeeIdField") "cr07a_empleadoid"
$EmployeeNameField = First-NonEmpty $EmployeeNameField (Get-JsonConfigValue $appsettings "Nomina:EmployeeNameField") "cr07a_nombrecompleto"
$EmployeeModulesField = First-NonEmpty $EmployeeModulesField (Get-JsonConfigValue $appsettings "Nomina:EmployeeModulesField") "cr07a_modulos"

if ([string]::IsNullOrWhiteSpace($BaseUrl) -or [string]::IsNullOrWhiteSpace($TenantId) -or [string]::IsNullOrWhiteSpace($ClientId) -or [string]::IsNullOrWhiteSpace($ClientSecret)) {
    throw "Faltan credenciales Dataverse. Configura Dataverse:BaseUrl, TenantId, ClientId y ClientSecret."
}

$script:BaseUrl = $BaseUrl.TrimEnd("/")
$tokenResponse = Invoke-RestMethod -Method "POST" -Uri "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token" -ContentType "application/x-www-form-urlencoded" -Body @{
    client_id = $ClientId
    client_secret = $ClientSecret
    scope = "$script:BaseUrl/.default"
    grant_type = "client_credentials"
}
$script:AccessToken = $tokenResponse.access_token
if ([string]::IsNullOrWhiteSpace($script:AccessToken)) {
    throw "No fue posible obtener token app-only para Dataverse."
}

Write-Host "Verificando opcion de modulo $ModuleLabel en $EmployeeTableLogicalName.$EmployeeModulesField" -ForegroundColor Cyan
$existingValues = [System.Collections.Generic.HashSet[int]]::new()
Get-ModuleOptionValues | ForEach-Object { [void]$existingValues.Add([int]$_) }
$optionCreated = Add-ModuleOptionIfMissing -ExistingValues $existingValues

if ($optionCreated -and -not $SkipPublish) {
    Write-Host "Publicando opcion nueva..." -ForegroundColor Cyan
    Publish-Customizations
}

$select = [string]::Join(",", (@($EmployeeIdField, $EmployeeNameField, $EmployeeEmailField, $EmployeeModulesField) | Sort-Object -Unique))
$orderBy = [uri]::EscapeDataString("$EmployeeNameField asc")
$path = "/api/data/v9.2/${EmployeeTableSetName}?`$select=$select&`$orderby=$orderBy"
Write-Host "Consultando empleados en $EmployeeTableSetName..."
$employees = @(Get-DataverseRows -Path $path)
if ($employees.Count -eq 0) {
    throw "No se encontraron empleados en $EmployeeTableSetName."
}

$targetEmailNormalized = $TargetEmail.Trim().ToLowerInvariant()
$targetFound = $false
$assigned = 0
$removed = 0
$unchanged = 0

foreach ($employee in $employees) {
    $employeeId = [string](Get-PropertyValue $employee $EmployeeIdField)
    if ([string]::IsNullOrWhiteSpace($employeeId)) { continue }

    $email = ([string](Get-PropertyValue $employee $EmployeeEmailField)).Trim().ToLowerInvariant()
    $name = [string](Get-PropertyValue $employee $EmployeeNameField)
    $currentValues = @(Read-ModuleValues (Get-PropertyValue $employee $EmployeeModulesField))
    $hasModule = $currentValues -contains $ModuleOptionValue
    $isTarget = $email -eq $targetEmailNormalized

    if ($isTarget) {
        $targetFound = $true
        if ($hasModule) {
            $unchanged++
            continue
        }

        $nextValues = @($currentValues + $ModuleOptionValue | Sort-Object -Unique)
        $payload = @{ $EmployeeModulesField = ConvertTo-MultiSelectPayload -Values $nextValues }
        Invoke-Dataverse -Method "PATCH" -Path "/api/data/v9.2/${EmployeeTableSetName}($employeeId)" -Body $payload | Out-Null
        $assigned++
        Write-Host "Asignado $ModuleLabel a $name <$email>"
        continue
    }

    if ($hasModule) {
        $nextValues = @($currentValues | Where-Object { $_ -ne $ModuleOptionValue } | Sort-Object -Unique)
        $payload = @{ $EmployeeModulesField = ConvertTo-MultiSelectPayload -Values $nextValues }
        Invoke-Dataverse -Method "PATCH" -Path "/api/data/v9.2/${EmployeeTableSetName}($employeeId)" -Body $payload | Out-Null
        $removed++
        Write-Host "Removido $ModuleLabel de $name <$email>"
    } else {
        $unchanged++
    }
}

if (-not $targetFound) {
    throw "No se encontro empleado con correo $TargetEmail. La opcion fue verificada, pero no se asigno acceso."
}

Write-Host "Acceso Conciliacion listo. Asignados: $assigned. Removidos: $removed. Sin cambios: $unchanged." -ForegroundColor Green
