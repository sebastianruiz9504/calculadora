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

$entity = First-NonEmpty (Get-JsonConfigValue $settings "Nomina:PayrollTableName") "cr07a_nomina"

function Ensure-FileAttribute {
    param(
        [string]$LogicalName,
        [string]$SchemaName,
        [string]$DisplayName,
        [string]$Description,
        [string]$FileNameLogicalName
    )

    Write-Host "Columna archivo: $LogicalName" -ForegroundColor Cyan

    $existing = Invoke-DataverseJson -Method "GET" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$entity')/Attributes(LogicalName='$LogicalName')?`$select=LogicalName" -AllowNotFound
    if ($null -eq $existing) {
        $payload = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.FileAttributeMetadata"
            AttributeTypeName = New-DvValue "FileType"
            SchemaName = $SchemaName
            DisplayName = New-DvLabel $DisplayName
            Description = New-DvLabel $Description
            RequiredLevel = New-DvRequiredNone
            MaxSizeInKB = 131072
        }

        Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$entity')/Attributes" -Body $payload | Out-Null
        Write-Host "Creada columna: $LogicalName"
    } else {
        Write-Host "OK columna existente: $LogicalName"
    }

    $metadata = Invoke-DataverseJson -Method "GET" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$entity')/Attributes(LogicalName='$LogicalName')?`$select=LogicalName,SchemaName"
    Write-Host "Columna lista: $($metadata.LogicalName) ($($metadata.SchemaName)); nombre archivo esperado: $FileNameLogicalName" -ForegroundColor Green
}

$payrollLogicalName = First-NonEmpty (Get-JsonConfigValue $settings "Nomina:PayrollPaymentProofField") "cr07a_comprobantepago"
$payrollFileNameLogicalName = First-NonEmpty (Get-JsonConfigValue $settings "Nomina:PayrollPaymentProofFileNameField") "$($payrollLogicalName)_name"
$cxcLogicalName = First-NonEmpty (Get-JsonConfigValue $settings "Nomina:PayrollCuentaDeCobroPaymentProofField") "cr07a_comprobantepagocxc"
$cxcFileNameLogicalName = First-NonEmpty (Get-JsonConfigValue $settings "Nomina:PayrollCuentaDeCobroPaymentProofFileNameField") "$($cxcLogicalName)_name"

Write-Host "Ambiente Dataverse: $script:BaseUrl" -ForegroundColor Cyan
Write-Host "Tabla objetivo: $entity" -ForegroundColor Cyan
Ensure-FileAttribute `
    -LogicalName $payrollLogicalName `
    -SchemaName "cr07a_ComprobantePago" `
    -DisplayName "Comprobante de pago nomina" `
    -Description "Comprobante de pago de nomina" `
    -FileNameLogicalName $payrollFileNameLogicalName

Ensure-FileAttribute `
    -LogicalName $cxcLogicalName `
    -SchemaName "cr07a_ComprobantePagoCxc" `
    -DisplayName "Comprobante de pago CXC" `
    -Description "Comprobante de pago de cuenta de cobro de nomina" `
    -FileNameLogicalName $cxcFileNameLogicalName

if (-not $SkipPublish) {
    $publishXml = "<importexportxml><entities><entity>$entity</entity></entities><nodes/><securityroles/><settings/><workflows/></importexportxml>"
    Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/PublishXml" -Body @{ ParameterXml = $publishXml } | Out-Null
    Write-Host "Customizations de nomina publicadas."
}
