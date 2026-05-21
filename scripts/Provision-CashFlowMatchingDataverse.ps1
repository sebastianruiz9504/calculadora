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

function New-Value { param([string]$Value) return @{ "Value" = $Value } }

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

function Test-EntityExists([string]$LogicalName) {
    $result = Invoke-Dataverse -Method "GET" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$LogicalName')?`$select=LogicalName" -AllowNotFound
    return $null -ne $result
}

function Test-AttributeExists([string]$EntityLogicalName, [string]$AttributeLogicalName) {
    $result = Invoke-Dataverse -Method "GET" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')?`$select=LogicalName" -AllowNotFound
    return $null -ne $result
}

function Wait-Entity([string]$LogicalName) {
    for ($i = 0; $i -lt 40; $i++) {
        if (Test-EntityExists $LogicalName) { return }
        Start-Sleep -Seconds 3
    }
    throw "La tabla $LogicalName no estuvo disponible despues de crearla."
}

function New-PrimaryNameAttribute {
    return @{
        "@odata.type" = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "AttributeType" = "String"
        "AttributeTypeName" = New-Value "StringType"
        "SchemaName" = "cr07a_Name"
        "DisplayName" = New-Label "Nombre"
        "Description" = New-Label "Nombre"
        "IsPrimaryName" = $true
        "RequiredLevel" = New-RequiredNone
        "MaxLength" = 200
        "FormatName" = New-Value "Text"
    }
}

function New-Table([string]$SchemaName, [string]$DisplayName, [string]$DisplayCollectionName) {
    $logicalName = $SchemaName.ToLowerInvariant()
    if (Test-EntityExists $logicalName) {
        Write-Host "OK tabla existente: $logicalName"
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.EntityMetadata"
        "Attributes" = @(New-PrimaryNameAttribute)
        "SchemaName" = $SchemaName
        "DisplayName" = New-Label $DisplayName
        "DisplayCollectionName" = New-Label $DisplayCollectionName
        "Description" = New-Label $DisplayName
        "OwnershipType" = "UserOwned"
        "IsActivity" = $false
        "HasActivities" = $false
        "HasNotes" = $true
    }
    Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/EntityDefinitions" -Body $payload | Out-Null
    Write-Host "Creada tabla: $logicalName"
    Wait-Entity $logicalName
}

function New-StringAttribute([string]$Entity, [string]$SchemaName, [string]$Label, [int]$MaxLength = 200) {
    $logicalName = $SchemaName.ToLowerInvariant()
    if (Test-AttributeExists $Entity $logicalName) {
        Write-Host "  OK columna existente: $Entity.$logicalName"
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
    Write-Host "  Creada columna: $Entity.$logicalName"
}

function New-MemoAttribute([string]$Entity, [string]$SchemaName, [string]$Label, [int]$MaxLength = 4000) {
    $logicalName = $SchemaName.ToLowerInvariant()
    if (Test-AttributeExists $Entity $logicalName) {
        Write-Host "  OK columna existente: $Entity.$logicalName"
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
    Write-Host "  Creada columna: $Entity.$logicalName"
}

function New-IntegerAttribute([string]$Entity, [string]$SchemaName, [string]$Label, [int]$MinValue = 0, [int]$MaxValue = 2147483647) {
    $logicalName = $SchemaName.ToLowerInvariant()
    if (Test-AttributeExists $Entity $logicalName) {
        Write-Host "  OK columna existente: $Entity.$logicalName"
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
        "AttributeType" = "Integer"
        "AttributeTypeName" = New-Value "IntegerType"
        "SchemaName" = $SchemaName
        "DisplayName" = New-Label $Label
        "Description" = New-Label $Label
        "RequiredLevel" = New-RequiredNone
        "Format" = "None"
        "MinValue" = $MinValue
        "MaxValue" = $MaxValue
        "SourceTypeMask" = 0
    }
    Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$Entity')/Attributes" -Body $payload | Out-Null
    Write-Host "  Creada columna: $Entity.$logicalName"
}

function New-DecimalAttribute([string]$Entity, [string]$SchemaName, [string]$Label, [decimal]$MinValue = -100000000000, [decimal]$MaxValue = 100000000000, [int]$Precision = 2) {
    $logicalName = $SchemaName.ToLowerInvariant()
    if (Test-AttributeExists $Entity $logicalName) {
        Write-Host "  OK columna existente: $Entity.$logicalName"
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.DecimalAttributeMetadata"
        "AttributeType" = "Decimal"
        "AttributeTypeName" = New-Value "DecimalType"
        "SchemaName" = $SchemaName
        "DisplayName" = New-Label $Label
        "Description" = New-Label $Label
        "RequiredLevel" = New-RequiredNone
        "MinValue" = $MinValue
        "MaxValue" = $MaxValue
        "Precision" = $Precision
    }
    Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$Entity')/Attributes" -Body $payload | Out-Null
    Write-Host "  Creada columna: $Entity.$logicalName"
}

function New-DateAttribute([string]$Entity, [string]$SchemaName, [string]$Label) {
    $logicalName = $SchemaName.ToLowerInvariant()
    if (Test-AttributeExists $Entity $logicalName) {
        Write-Host "  OK columna existente: $Entity.$logicalName"
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
        "AttributeType" = "DateTime"
        "AttributeTypeName" = New-Value "DateTimeType"
        "SchemaName" = $SchemaName
        "DisplayName" = New-Label $Label
        "Description" = New-Label $Label
        "RequiredLevel" = New-RequiredNone
        "Format" = "DateOnly"
        "DateTimeBehavior" = New-Value "DateOnly"
    }
    Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$Entity')/Attributes" -Body $payload | Out-Null
    Write-Host "  Creada columna: $Entity.$logicalName"
}

function New-DateTimeAttribute([string]$Entity, [string]$SchemaName, [string]$Label) {
    $logicalName = $SchemaName.ToLowerInvariant()
    if (Test-AttributeExists $Entity $logicalName) {
        Write-Host "  OK columna existente: $Entity.$logicalName"
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
        "AttributeType" = "DateTime"
        "AttributeTypeName" = New-Value "DateTimeType"
        "SchemaName" = $SchemaName
        "DisplayName" = New-Label $Label
        "Description" = New-Label $Label
        "RequiredLevel" = New-RequiredNone
        "Format" = "DateAndTime"
    }
    Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$Entity')/Attributes" -Body $payload | Out-Null
    Write-Host "  Creada columna: $Entity.$logicalName"
}

$appsettings = Get-Content -LiteralPath "appsettings.json" -Raw | ConvertFrom-Json
$secrets = Get-UserSecretMap

$BaseUrl = First-NonEmpty $BaseUrl $env:DATAVERSE_BASE_URL $secrets["Dataverse:BaseUrl"] (Get-JsonConfigValue $appsettings "Dataverse:BaseUrl")
$TenantId = First-NonEmpty $TenantId $env:DATAVERSE_TENANT_ID $secrets["Dataverse:TenantId"] (Get-JsonConfigValue $appsettings "Dataverse:TenantId") (Get-JsonConfigValue $appsettings "AzureAd:TenantId")
$ClientId = First-NonEmpty $ClientId $env:DATAVERSE_CLIENT_ID $secrets["Dataverse:ClientId"] (Get-JsonConfigValue $appsettings "Dataverse:ClientId") (Get-JsonConfigValue $appsettings "AzureAd:ClientId")
$ClientSecret = First-NonEmpty $ClientSecret $env:DATAVERSE_CLIENT_SECRET $secrets["Dataverse:ClientSecret"] (Get-JsonConfigValue $appsettings "Dataverse:ClientSecret")

if ([string]::IsNullOrWhiteSpace($BaseUrl) -or [string]::IsNullOrWhiteSpace($TenantId) -or [string]::IsNullOrWhiteSpace($ClientId) -or [string]::IsNullOrWhiteSpace($ClientSecret)) {
    throw "Faltan credenciales Dataverse."
}

$script:BaseUrl = $BaseUrl.TrimEnd("/")
$tokenResponse = Invoke-RestMethod -Method "POST" -Uri "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token" -ContentType "application/x-www-form-urlencoded" -Body @{
    client_id = $ClientId
    client_secret = $ClientSecret
    scope = "$script:BaseUrl/.default"
    grant_type = "client_credentials"
}
$script:AccessToken = $tokenResponse.access_token

$table = "cr07a_cruceflujocaja"
New-Table "cr07a_CruceFlujoCaja" "Cruce flujo caja" "Cruces flujo caja"
New-StringAttribute $table "cr07a_Tipo" "Tipo" 100
New-StringAttribute $table "cr07a_Estado" "Estado" 100
New-IntegerAttribute $table "cr07a_Confianza" "Confianza" 0 100
New-MemoAttribute $table "cr07a_Motivo" "Motivo" 4000
New-DecimalAttribute $table "cr07a_Diferencia" "Diferencia"
New-StringAttribute $table "cr07a_MovimientoBancarioId" "Movimiento bancario id" 100
New-StringAttribute $table "cr07a_MovimientoClaveExterna" "Movimiento clave externa" 250
New-DateAttribute $table "cr07a_FechaMovimiento" "Fecha movimiento"
New-StringAttribute $table "cr07a_OrigenFlujo" "Origen flujo" 50
New-StringAttribute $table "cr07a_BancoCuentaCodigo" "Banco cuenta codigo" 50
New-StringAttribute $table "cr07a_BancoCuentaNombre" "Banco cuenta nombre" 250
New-MemoAttribute $table "cr07a_DescripcionMovimiento" "Descripcion movimiento" 4000
New-DecimalAttribute $table "cr07a_ValorEntrada" "Valor entrada"
New-StringAttribute $table "cr07a_FacturacionId" "Facturacion id" 1000
New-StringAttribute $table "cr07a_FacturaNumero" "Factura numero" 1000
New-StringAttribute $table "cr07a_Cliente" "Cliente" 1000
New-DecimalAttribute $table "cr07a_ValorFactura" "Valor factura"
New-DecimalAttribute $table "cr07a_ValorPago" "Valor pago"
New-DecimalAttribute $table "cr07a_ReteFteValor" "ReteFte valor"
New-DecimalAttribute $table "cr07a_ReteIcaValor" "ReteICA valor"
New-DecimalAttribute $table "cr07a_RteIvaValor" "RteIVA valor"
New-MemoAttribute $table "cr07a_JsonBorradorSiigo" "Json borrador Siigo" 100000
New-StringAttribute $table "cr07a_PreflightEstado" "Preflight estado" 100
New-MemoAttribute $table "cr07a_PreflightMensaje" "Preflight mensaje" 4000
New-DateTimeAttribute $table "cr07a_PreflightFecha" "Preflight fecha"
New-DecimalAttribute $table "cr07a_PreflightDebito" "Preflight debito"
New-DecimalAttribute $table "cr07a_PreflightCredito" "Preflight credito"
New-StringAttribute $table "cr07a_ClaveExterna" "Clave externa" 300
New-StringAttribute $table "cr07a_HashOrigen" "Hash origen" 100

Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/PublishAllXml" -Body @{} | Out-Null
Write-Host "Listo: esquema de cruce de flujo de caja publicado."
