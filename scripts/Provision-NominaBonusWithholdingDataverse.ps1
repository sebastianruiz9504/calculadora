param(
    [string]$BaseUrl = "",
    [string]$TenantId = "",
    [string]$ClientId = "",
    [string]$ClientSecret = "",
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
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        return $map
    }

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
                LanguageCode = 3082
            },
            @{
                "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                Label = $Text
                LanguageCode = 1033
            }
        )
    }
}

function New-DvValue {
    param([string]$Value)
    return @{ Value = $Value }
}

function New-DvRequiredNone {
    return @{
        Value = "None"
        CanBeChanged = $true
        ManagedPropertyLogicalName = "canmodifyrequirementlevelsettings"
    }
}

function New-DvBooleanOption {
    param([string]$Label, [int]$Value)

    return @{
        "@odata.type" = "Microsoft.Dynamics.CRM.OptionMetadata"
        Label = New-DvLabel $Label
        Value = $Value
    }
}

function Invoke-Dataverse {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Path,
        [object]$Body = $null,
        [hashtable]$ExtraHeaders = @{},
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
    foreach ($key in $ExtraHeaders.Keys) {
        $headers[$key] = $ExtraHeaders[$key]
    }

    $jsonBody = if ($null -eq $Body) { $null } else { $Body | ConvertTo-Json -Depth 60 }
    for ($attempt = 1; $attempt -le 8; $attempt++) {
        try {
            if ($null -eq $jsonBody) {
                return Invoke-WebRequest -Uri $uri -Method $Method -Headers $headers -UseBasicParsing
            }

            return Invoke-WebRequest -Uri $uri -Method $Method -Headers $headers -ContentType "application/json; charset=utf-8" -Body $jsonBody -UseBasicParsing
        }
        catch {
            $response = $_.Exception.Response
            if ($AllowNotFound -and $response -and [int]$response.StatusCode -eq 404) {
                return $null
            }

            $statusCode = if ($response) { [int]$response.StatusCode } else { 0 }
            if ($attempt -lt 8 -and ($statusCode -eq 429 -or $statusCode -eq 502 -or $statusCode -eq 503 -or $statusCode -eq 504)) {
                $delay = [Math]::Min(60, 8 * $attempt)
                Write-Host "Dataverse devolvio $statusCode. Reintentando en $delay s..."
                Start-Sleep -Seconds $delay
                continue
            }

            $statusText = if ($response) { "$statusCode $($response.ReasonPhrase)" } else { "sin respuesta HTTP" }
            throw "Dataverse $Method $Path fallo ($statusText). $($_.Exception.Message)"
        }
    }
}

function Invoke-DataverseJson {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Path,
        [object]$Body = $null,
        [hashtable]$ExtraHeaders = @{},
        [switch]$AllowNotFound
    )

    $response = Invoke-Dataverse -Method $Method -Path $Path -Body $Body -ExtraHeaders $ExtraHeaders -AllowNotFound:$AllowNotFound
    if ($null -eq $response -or [string]::IsNullOrWhiteSpace($response.Content)) {
        return $null
    }

    return $response.Content | ConvertFrom-Json
}

function Get-AccessToken {
    if (-not [string]::IsNullOrWhiteSpace($script:ClientId) -and -not [string]::IsNullOrWhiteSpace($script:ClientSecret)) {
        $tokenResponse = Invoke-RestMethod -Method "POST" -Uri "https://login.microsoftonline.com/$script:TenantId/oauth2/v2.0/token" -ContentType "application/x-www-form-urlencoded" -Body @{
            client_id = $script:ClientId
            client_secret = $script:ClientSecret
            scope = "$script:BaseUrl/.default"
            grant_type = "client_credentials"
        }

        if (-not [string]::IsNullOrWhiteSpace($tokenResponse.access_token)) {
            return $tokenResponse.access_token
        }
    }

    if (Get-Command az -ErrorAction SilentlyContinue) {
        $token = az account get-access-token --resource $script:BaseUrl --query accessToken -o tsv
        if (-not [string]::IsNullOrWhiteSpace($token)) {
            return $token
        }
    }

    throw "No fue posible obtener token para Dataverse."
}

function Test-AttributeExists {
    param([string]$LogicalName)

    $result = Invoke-DataverseJson -Method "GET" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$script:EntityName')/Attributes(LogicalName='$LogicalName')?`$select=LogicalName" -AllowNotFound
    return $null -ne $result
}

function Ensure-MoneyAttribute {
    param([string]$LogicalName, [string]$SchemaName, [string]$Label)

    if (Test-AttributeExists $LogicalName) {
        Write-Host "OK columna existente: $LogicalName"
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.MoneyAttributeMetadata"
        AttributeType = "Money"
        AttributeTypeName = New-DvValue "MoneyType"
        SchemaName = $SchemaName
        DisplayName = New-DvLabel $Label
        Description = New-DvLabel $Label
        RequiredLevel = New-DvRequiredNone
        ImeMode = "Disabled"
        MinValue = 0
        MaxValue = 100000000000
        PrecisionSource = 2
    }

    Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$script:EntityName')/Attributes" -Body $payload | Out-Null
    Write-Host "Creada columna: $LogicalName"
}

function Ensure-DecimalAttribute {
    param([string]$LogicalName, [string]$SchemaName, [string]$Label)

    if (Test-AttributeExists $LogicalName) {
        Write-Host "OK columna existente: $LogicalName"
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.DecimalAttributeMetadata"
        AttributeType = "Decimal"
        AttributeTypeName = New-DvValue "DecimalType"
        SchemaName = $SchemaName
        DisplayName = New-DvLabel $Label
        Description = New-DvLabel $Label
        RequiredLevel = New-DvRequiredNone
        MinValue = 0
        MaxValue = 100
        Precision = 4
    }

    Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$script:EntityName')/Attributes" -Body $payload | Out-Null
    Write-Host "Creada columna: $LogicalName"
}

function Ensure-BooleanAttribute {
    param([string]$LogicalName, [string]$SchemaName, [string]$Label)

    if (Test-AttributeExists $LogicalName) {
        Write-Host "OK columna existente: $LogicalName"
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.BooleanAttributeMetadata"
        AttributeType = "Boolean"
        AttributeTypeName = New-DvValue "BooleanType"
        SchemaName = $SchemaName
        DisplayName = New-DvLabel $Label
        Description = New-DvLabel $Label
        RequiredLevel = New-DvRequiredNone
        DefaultValue = $false
        OptionSet = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.BooleanOptionSetMetadata"
            TrueOption = New-DvBooleanOption "Si" 1
            FalseOption = New-DvBooleanOption "No" 0
            OptionSetType = "Boolean"
        }
    }

    Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$script:EntityName')/Attributes" -Body $payload | Out-Null
    Write-Host "Creada columna: $LogicalName"
}

$settings = Get-Content .\appsettings.json -Raw | ConvertFrom-Json
$secrets = Get-UserSecretMap

$BaseUrl = First-NonEmpty $BaseUrl $secrets["Dataverse:BaseUrl"] (Get-JsonConfigValue $settings "Dataverse:BaseUrl")
$TenantId = First-NonEmpty $TenantId $secrets["Dataverse:TenantId"] (Get-JsonConfigValue $settings "Dataverse:TenantId") (Get-JsonConfigValue $settings "AzureAd:TenantId")
$ClientId = First-NonEmpty $ClientId $secrets["Dataverse:ClientId"] (Get-JsonConfigValue $settings "Dataverse:ClientId")
$ClientSecret = First-NonEmpty $ClientSecret $secrets["Dataverse:ClientSecret"] (Get-JsonConfigValue $settings "Dataverse:ClientSecret")

if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
    throw "Falta Dataverse:BaseUrl."
}

$script:BaseUrl = $BaseUrl.TrimEnd("/")
$script:TenantId = $TenantId
$script:ClientId = $ClientId
$script:ClientSecret = $ClientSecret
$script:AccessToken = Get-AccessToken
$script:EntityName = First-NonEmpty (Get-JsonConfigValue $settings "Nomina:PayrollTableName") "cr07a_nomina"

$columns = @(
    @{
        Type = "Money"
        LogicalName = First-NonEmpty (Get-JsonConfigValue $settings "Nomina:PayrollNonCommissionBonusField") "cr07a_bonosnocomisionales"
        SchemaName = "cr07a_BonosNoComisionales"
        Label = "Bonos no comisionales"
    },
    @{
        Type = "Boolean"
        LogicalName = First-NonEmpty (Get-JsonConfigValue $settings "Nomina:PayrollApplyNonCommissionBonusWithholdingField") "cr07a_aplicarretencionbononocomisional"
        SchemaName = "cr07a_AplicarRetencionBonoNoComisional"
        Label = "Aplicar retencion bono no comisional"
    },
    @{
        Type = "Decimal"
        LogicalName = First-NonEmpty (Get-JsonConfigValue $settings "Nomina:PayrollNonCommissionBonusWithholdingRateField") "cr07a_porcentajeretencionbononocomisional"
        SchemaName = "cr07a_PorcentajeRetencionBonoNoComisional"
        Label = "Porcentaje retencion bono no comisional"
    },
    @{
        Type = "Money"
        LogicalName = First-NonEmpty (Get-JsonConfigValue $settings "Nomina:PayrollNonCommissionBonusWithholdingField") "cr07a_retencionbononocomisional"
        SchemaName = "cr07a_RetencionBonoNoComisional"
        Label = "Retencion bono no comisional"
    },
    @{
        Type = "Boolean"
        LogicalName = First-NonEmpty (Get-JsonConfigValue $settings "Nomina:PayrollApplyExternalWithholdingField") "cr07a_aplicarretencioncxc"
        SchemaName = "cr07a_AplicarRetencionCxc"
        Label = "Aplicar retencion cuenta de cobro"
    },
    @{
        Type = "Decimal"
        LogicalName = First-NonEmpty (Get-JsonConfigValue $settings "Nomina:PayrollExternalWithholdingRateField") "cr07a_porcentajeretencioncxc"
        SchemaName = "cr07a_PorcentajeRetencionCxc"
        Label = "Porcentaje retencion cuenta de cobro"
    }
)

Write-Host "Ambiente Dataverse: $script:BaseUrl" -ForegroundColor Cyan
Write-Host "Tabla objetivo: $script:EntityName" -ForegroundColor Cyan

foreach ($column in $columns) {
    switch ($column.Type) {
        "Money" { Ensure-MoneyAttribute $column.LogicalName $column.SchemaName $column.Label }
        "Decimal" { Ensure-DecimalAttribute $column.LogicalName $column.SchemaName $column.Label }
        "Boolean" { Ensure-BooleanAttribute $column.LogicalName $column.SchemaName $column.Label }
        default { throw "Tipo de columna no soportado: $($column.Type)" }
    }
}

if (-not $SkipPublish) {
    $publishXml = "<importexportxml><entities><entity>$script:EntityName</entity></entities><nodes/><securityroles/><settings/><workflows/></importexportxml>"
    Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/PublishXml" -Body @{ ParameterXml = $publishXml } | Out-Null
    Write-Host "Customizations de nomina publicadas."
}

foreach ($column in $columns) {
    $metadata = Invoke-DataverseJson -Method "GET" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$script:EntityName')/Attributes(LogicalName='$($column.LogicalName)')?`$select=LogicalName,SchemaName"
    Write-Host "Columna lista: $($metadata.LogicalName) ($($metadata.SchemaName))" -ForegroundColor Green
}
