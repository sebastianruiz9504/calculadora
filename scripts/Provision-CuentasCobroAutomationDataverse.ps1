param(
    [string]$BaseUrl = "",
    [string]$TenantId = "",
    [string]$ClientId = "",
    [string]$ClientSecret = ""
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

function New-Label {
    param([string]$Text)
    return @{
        "@odata.type" = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(
            @{
                "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                "Label" = $Text
                "LanguageCode" = 3082
            }
        )
    }
}

function New-Value {
    param([string]$Value)
    return @{ "Value" = $Value }
}

function New-RequiredNone {
    return @{
        "Value" = "None"
        "CanBeChanged" = $true
        "ManagedPropertyLogicalName" = "canmodifyrequirementlevelsettings"
    }
}

function Invoke-Dataverse {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body = $null,
        [switch]$AllowNotFound
    )

    $uri = if ($Path.StartsWith("http", [System.StringComparison]::OrdinalIgnoreCase)) { $Path } else { "$script:BaseUrl$Path" }
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

        $json = $Body | ConvertTo-Json -Depth 40
        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -ContentType "application/json" -Body $json
    }
    catch {
        $response = $_.Exception.Response
        if ($AllowNotFound -and $response -and [int]$response.StatusCode -eq 404) { return $null }
        throw
    }
}

function Test-AttributeExists([string]$EntityLogicalName, [string]$AttributeLogicalName) {
    $result = Invoke-Dataverse -Method "GET" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')?`$select=LogicalName" -AllowNotFound
    return $null -ne $result
}

function New-StringAttribute([string]$Entity, [string]$SchemaName, [string]$Label, [int]$MaxLength = 200) {
    $logicalName = $SchemaName.ToLowerInvariant()
    if (Test-AttributeExists $Entity $logicalName) {
        Write-Host "OK columna existente: $logicalName"
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "AttributeType" = "String"
        "AttributeTypeName" = New-Value "StringType"
        "SchemaName" = $SchemaName
        "DisplayName" = New-Label $Label
        "Description" = New-Label $Label
        "RequiredLevel" = New-RequiredNone
        "MaxLength" = $MaxLength
        "FormatName" = New-Value "Text"
    }
    Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$Entity')/Attributes" -Body $payload | Out-Null
    Write-Host "Creada columna: $logicalName"
}

function New-MemoAttribute([string]$Entity, [string]$SchemaName, [string]$Label, [int]$MaxLength = 4000) {
    $logicalName = $SchemaName.ToLowerInvariant()
    if (Test-AttributeExists $Entity $logicalName) {
        Write-Host "OK columna existente: $logicalName"
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
        "AttributeType" = "Memo"
        "AttributeTypeName" = New-Value "MemoType"
        "SchemaName" = $SchemaName
        "DisplayName" = New-Label $Label
        "Description" = New-Label $Label
        "RequiredLevel" = New-RequiredNone
        "Format" = "TextArea"
        "ImeMode" = "Disabled"
        "IsLocalizable" = $false
        "MaxLength" = $MaxLength
    }
    Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$Entity')/Attributes" -Body $payload | Out-Null
    Write-Host "Creada columna: $logicalName"
}

$settings = Get-Content .\appsettings.json -Raw | ConvertFrom-Json
$secrets = Get-UserSecretMap

$BaseUrl = First-NonEmpty $BaseUrl $secrets["Dataverse:BaseUrl"] (Get-JsonConfigValue $settings "Dataverse:BaseUrl")
$TenantId = First-NonEmpty $TenantId $secrets["Dataverse:TenantId"] (Get-JsonConfigValue $settings "Dataverse:TenantId") (Get-JsonConfigValue $settings "AzureAd:TenantId")
$ClientId = First-NonEmpty $ClientId $secrets["Dataverse:ClientId"] (Get-JsonConfigValue $settings "Dataverse:ClientId")
$ClientSecret = First-NonEmpty $ClientSecret $secrets["Dataverse:ClientSecret"] (Get-JsonConfigValue $settings "Dataverse:ClientSecret")

if ([string]::IsNullOrWhiteSpace($BaseUrl) -or [string]::IsNullOrWhiteSpace($TenantId) -or [string]::IsNullOrWhiteSpace($ClientId) -or [string]::IsNullOrWhiteSpace($ClientSecret)) {
    throw "Faltan credenciales Dataverse. Configura BaseUrl, TenantId, ClientId y ClientSecret."
}

$BaseUrl = $BaseUrl.TrimEnd("/")
$token = Invoke-RestMethod -Method "POST" -Uri "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token" -Body @{
    client_id = $ClientId
    client_secret = $ClientSecret
    scope = "$BaseUrl/.default"
    grant_type = "client_credentials"
}
$script:AccessToken = $token.access_token
$script:BaseUrl = $BaseUrl

$entity = "cr07a_cuentasdecobro"
New-StringAttribute $entity "cr07a_CuentaContableCodigo" "Cuenta contable codigo" 50
New-StringAttribute $entity "cr07a_CuentaContableNombre" "Cuenta contable nombre" 200
New-StringAttribute $entity "cr07a_EstadoAutomatizacion" "Estado automatizacion" 100
New-MemoAttribute $entity "cr07a_MotivoRevision" "Motivo revision" 4000
New-StringAttribute $entity "cr07a_SiigoDocumentId" "Siigo document id" 120
New-StringAttribute $entity "cr07a_SiigoDocumentName" "Siigo document name" 120
New-StringAttribute $entity "cr07a_SiigoPaymentId" "Siigo payment id" 120
New-StringAttribute $entity "cr07a_SiigoPaymentName" "Siigo payment name" 120
New-MemoAttribute $entity "cr07a_SiigoRespuesta" "Siigo respuesta" 4000
New-MemoAttribute $entity "cr07a_SiigoPaymentResponse" "Siigo payment response" 4000

$publishPayload = @{
    ParameterXml = "<importexportxml><entities><entity>$entity</entity></entities><nodes/><securityroles/><settings/><workflows/></importexportxml>"
}
Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/PublishXml" -Body $publishPayload | Out-Null
Write-Host "Cuentas de cobro: columnas de automatizacion listas."
