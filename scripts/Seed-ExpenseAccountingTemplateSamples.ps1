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

function Invoke-Dataverse {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body = $null,
        [switch]$ReturnRepresentation,
        [switch]$AllowNotFound
    )

    $uri = if ($Path.StartsWith("http", [System.StringComparison]::OrdinalIgnoreCase)) {
        $Path
    } else {
        "$script:BaseUrl$Path"
    }
    $headers = @{
        "Authorization" = "Bearer $script:AccessToken"
        "Accept" = "application/json"
        "OData-MaxVersion" = "4.0"
        "OData-Version" = "4.0"
    }
    if ($ReturnRepresentation) {
        $headers["Prefer"] = "return=representation"
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
        if ($AllowNotFound -and $response -and [int]$response.StatusCode -eq 404) {
            return $null
        }
        throw
    }
}

function Escape-ODataLiteral([string]$Value) {
    return ($Value ?? "").Replace("'", "''")
}

function Get-TemplateByName([string]$Name) {
    $filter = [uri]::EscapeDataString("cr07a_name eq '$(Escape-ODataLiteral $Name)'")
    $url = "/api/data/v9.2/cr07a_plantillacontablegastos?`$select=cr07a_plantillacontablegastoid,cr07a_name&`$filter=$filter&`$top=1"
    $result = Invoke-Dataverse -Method "GET" -Path $url
    return @($result.value) | Select-Object -First 1
}

function Delete-TemplateLines([string]$TemplateId) {
    $filter = [uri]::EscapeDataString("cr07a_plantillaid eq '$(Escape-ODataLiteral $TemplateId)'")
    $url = "/api/data/v9.2/cr07a_lineaplantillacontablegastos?`$select=cr07a_lineaplantillacontablegastoid&`$filter=$filter&`$top=5000"
    do {
        $result = Invoke-Dataverse -Method "GET" -Path $url
        foreach ($line in @($result.value)) {
            Invoke-Dataverse -Method "DELETE" -Path "/api/data/v9.2/cr07a_lineaplantillacontablegastos($($line.cr07a_lineaplantillacontablegastoid))" | Out-Null
        }
        $url = $result.'@odata.nextLink'
    } while ($url)
}

function Upsert-Template($Template) {
    $existing = Get-TemplateByName $Template.Name
    $payload = @{
        cr07a_name = $Template.Name
        cr07a_prioridad = [int]$Template.Priority
        cr07a_categoriavalor = 645250011
        cr07a_categorianombre = "Recurrente"
        cr07a_nitemisor = $Template.Nit
        cr07a_textocontiene = $Template.TextContains
        cr07a_tipomovimiento = "Compra"
        cr07a_activa = $true
        cr07a_requiereaprobacion = $true
        cr07a_descripcion = "Plantilla piloto. Genera lineas contables en Dataverse y queda en revision antes de cualquier envio a Siigo."
    }

    if ($null -eq $existing) {
        $created = Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/cr07a_plantillacontablegastos" -Body $payload -ReturnRepresentation
        $templateId = $created.cr07a_plantillacontablegastoid
        Write-Host "Creada plantilla: $($Template.Name)"
    } else {
        $templateId = $existing.cr07a_plantillacontablegastoid
        Invoke-Dataverse -Method "PATCH" -Path "/api/data/v9.2/cr07a_plantillacontablegastos($templateId)" -Body $payload | Out-Null
        Delete-TemplateLines $templateId
        Write-Host "Actualizada plantilla: $($Template.Name)"
    }

    foreach ($line in @($Template.Lines)) {
        $linePayload = @{
            cr07a_name = "$($Template.Name) - $($line.Order)"
            cr07a_plantillaid = $templateId
            cr07a_plantillanombre = $Template.Name
            cr07a_orden = [int]$line.Order
            cr07a_lado = $line.Side
            cr07a_cuentacodigo = $line.AccountCode
            cr07a_cuentanombre = $line.AccountName
            cr07a_formula = $line.Formula
            cr07a_porcentaje = 0
            cr07a_valorconstante = 0
            cr07a_descripcion = $line.Description
            cr07a_activa = $true
        }
        Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/cr07a_lineaplantillacontablegastos" -Body $linePayload | Out-Null
    }
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

$templates = @(
    @{
        Name = "TPL Servicios publicos energia - ENEL"
        Priority = 10
        Nit = "860063875"
        TextContains = "Enel"
        Lines = @(
            @{ Order = 1; Side = "Debito"; AccountCode = "51353001"; AccountName = "Servicios publicos - Energia electrica"; Formula = "Base"; Description = "Base del servicio de energia" },
            @{ Order = 2; Side = "Debito"; AccountCode = "24080601"; AccountName = "IVA 19%"; Formula = "Iva"; Description = "IVA descontable/registrado del servicio. El banco se completa al cruzar flujo de caja Cloud/Copiers." }
        )
    },
    @{
        Name = "TPL Servicios publicos acueducto"
        Priority = 20
        Nit = ""
        TextContains = "Acueducto"
        Lines = @(
            @{ Order = 1; Side = "Debito"; AccountCode = "51352501"; AccountName = "Servicios publicos - Acueducto y alcantarillado"; Formula = "Base"; Description = "Base del servicio de acueducto" },
            @{ Order = 2; Side = "Debito"; AccountCode = "24080601"; AccountName = "IVA 19%"; Formula = "Iva"; Description = "IVA descontable/registrado del servicio. El banco se completa al cruzar flujo de caja Cloud/Copiers." }
        )
    },
    @{
        Name = "TPL Servicios telefonia - Colombia Movil"
        Priority = 30
        Nit = "830114921"
        TextContains = "Colombia Movil"
        Lines = @(
            @{ Order = 1; Side = "Debito"; AccountCode = "51353501"; AccountName = "Servicios publicos - Telefono"; Formula = "Base"; Description = "Base del servicio de telefonia" },
            @{ Order = 2; Side = "Debito"; AccountCode = "24080601"; AccountName = "IVA 19%"; Formula = "Iva"; Description = "IVA descontable/registrado del servicio. El banco se completa al cruzar flujo de caja Cloud/Copiers." }
        )
    }
)

foreach ($template in $templates) {
    Upsert-Template $template
}

Write-Host "Listo: plantillas piloto creadas/actualizadas con aprobacion requerida."
