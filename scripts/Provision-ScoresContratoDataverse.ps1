param(
    [string]$DataverseUrl = "https://orgc79ca19c.crm2.dynamics.com",
    [string]$TenantId = "",
    [string]$ClientId = "",
    [string]$ClientSecret = "",
    [string]$ConnectionString = $env:ConnectionStrings__Dataverse,
    [string]$EntityLogicalName = "cr07a_contractrecord1",
    [string]$EntitySetName = "cr07a_contractrecord1s",
    [string]$IdField = "cr07a_contractrecord1id",
    [string]$TargetContractField = "cr07a_contrato",
    [string]$SourceContractField = "cr07a_tipodecontrato",
    [string]$AdditionalField = "cr07a_adicionales",
    [string]$DescriptionField = "cr07a_aprovisionamientodetallelargo",
    [string]$LegacyDescriptionField = "cr07a_description",
    [datetime]$StartCreatedOn = "2026-01-01T00:00:00Z",
    [datetime]$EndCreatedOn = "2027-01-01T00:00:00Z",
    [int]$LanguageCode = 3082,
    [bool]$OverwriteExisting = $true,
    [switch]$DryRun,
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

$NewBusinessValue = 645250000
$RenewalValue = 645250001
$DataverseUrl = $DataverseUrl.TrimEnd("/")

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    Write-Host "Azure CLI no esta disponible; se intentara autenticacion con client credentials."
}

function Resolve-ConnectionStringSettings {
    if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
        return
    }

    foreach ($part in $ConnectionString -split ";") {
        $idx = $part.IndexOf("=")
        if ($idx -le 0) {
            continue
        }

        $key = $part.Substring(0, $idx).Trim()
        $value = $part.Substring($idx + 1).Trim()
        if ([string]::IsNullOrWhiteSpace($value)) {
            continue
        }

        switch -Regex ($key) {
            "^Url$" {
                if ([string]::IsNullOrWhiteSpace($DataverseUrl) -or $DataverseUrl -eq "https://orgc79ca19c.crm2.dynamics.com") {
                    if ($value -ne "https://...") {
                        $script:ResolvedDataverseUrl = $value.TrimEnd("/")
                    }
                }
            }
            "^TenantId$" {
                if ([string]::IsNullOrWhiteSpace($TenantId)) {
                    $script:ResolvedTenantId = $value
                }
            }
            "^ClientId$" {
                if ([string]::IsNullOrWhiteSpace($ClientId)) {
                    $script:ResolvedClientId = $value
                }
            }
            "^ClientSecret$" {
                if ([string]::IsNullOrWhiteSpace($ClientSecret)) {
                    $script:ResolvedClientSecret = $value
                }
            }
        }
    }
}

$script:ResolvedDataverseUrl = $DataverseUrl
$script:ResolvedTenantId = $TenantId
$script:ResolvedClientId = $ClientId
$script:ResolvedClientSecret = $ClientSecret
Resolve-ConnectionStringSettings

$DataverseUrl = $script:ResolvedDataverseUrl.TrimEnd("/")

function Get-AccessTokenWithClientCredentials {
    if ([string]::IsNullOrWhiteSpace($script:ResolvedTenantId) -or
        [string]::IsNullOrWhiteSpace($script:ResolvedClientId) -or
        [string]::IsNullOrWhiteSpace($script:ResolvedClientSecret)) {
        return $null
    }

    $tokenUrl = "https://login.microsoftonline.com/$($script:ResolvedTenantId)/oauth2/v2.0/token"
    $body = @{
        client_id = $script:ResolvedClientId
        client_secret = $script:ResolvedClientSecret
        grant_type = "client_credentials"
        scope = "$DataverseUrl/.default"
    }

    try {
        $response = Invoke-RestMethod -Method Post -Uri $tokenUrl -Body $body -ContentType "application/x-www-form-urlencoded"
        return $response.access_token
    }
    catch {
        Write-Host "No fue posible autenticar con client credentials; se intentara Azure CLI si esta disponible."
        return $null
    }
}

function Get-AccessToken {
    $token = Get-AccessTokenWithClientCredentials
    if (-not [string]::IsNullOrWhiteSpace($token)) {
        return $token
    }

    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        throw "No fue posible obtener token para $DataverseUrl. Configura ConnectionStrings__Dataverse o ejecuta az login."
    }

    $token = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
    if ([string]::IsNullOrWhiteSpace($token)) {
        throw "No fue posible obtener token para $DataverseUrl. Ejecuta az login con un usuario con permisos de personalizacion y escritura en Dataverse."
    }

    return $token
}

$script:AccessToken = Get-AccessToken

function New-DvValue([string]$Value) {
    return @{ Value = $Value }
}

function New-DvLabel([string]$Text) {
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

function New-DvRequiredNone {
    return @{
        Value = "None"
        CanBeChanged = $true
        ManagedPropertyLogicalName = "canmodifyrequirementlevelsettings"
    }
}

function New-DvOption([string]$Label, [int]$Value) {
    return @{
        "@odata.type" = "Microsoft.Dynamics.CRM.OptionMetadata"
        Value = $Value
        Label = New-DvLabel $Label
    }
}

function Invoke-DvRequest {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Path,
        [object]$Body = $null,
        [switch]$AllowNotFound
    )

    $headers = @{
        Authorization = "Bearer $script:AccessToken"
        Accept = "application/json"
        "OData-MaxVersion" = "4.0"
        "OData-Version" = "4.0"
    }

    $uri = if ($Path.StartsWith("http", [System.StringComparison]::OrdinalIgnoreCase)) {
        $Path
    } else {
        "$DataverseUrl/api/data/v9.2/$($Path.TrimStart('/'))"
    }

    try {
        if ($null -eq $Body) {
            return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers
        }

        $json = $Body | ConvertTo-Json -Depth 50
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

function Test-AttributeExists([string]$LogicalName) {
    $path = "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$LogicalName')?`$select=LogicalName"
    $result = Invoke-DvRequest -Method Get -Path $path -AllowNotFound
    return $null -ne $result
}

function Ensure-ContratoAttribute {
    if (Test-AttributeExists $TargetContractField) {
        Write-Host "OK columna existente: $TargetContractField"
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
        AttributeType = "Picklist"
        AttributeTypeName = New-DvValue "PicklistType"
        SchemaName = "cr07a_Contrato"
        DisplayName = New-DvLabel "Contrato"
        Description = New-DvLabel "Clasificacion del contrato para metricas: negocio nuevo o renovacion."
        RequiredLevel = New-DvRequiredNone
        OptionSet = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.OptionSetMetadata"
            IsGlobal = $false
            OptionSetType = "Picklist"
            Options = @(
                New-DvOption "Negocio nuevo" $NewBusinessValue
                New-DvOption "Renovacion" $RenewalValue
            )
        }
    }

    if ($DryRun) {
        Write-Host "DRY RUN crearia columna $TargetContractField en $EntityLogicalName"
        return
    }

    Invoke-DvRequest -Method Post -Path "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes" -Body $payload | Out-Null
    Write-Host "Creada columna: $TargetContractField"

    if (-not $SkipPublish) {
        Invoke-DvRequest -Method Post -Path "PublishAllXml" -Body @{} | Out-Null
        Write-Host "Publicadas personalizaciones."
    }
}

function Normalize-ContractValue($Value) {
    if ($null -eq $Value) {
        return $null
    }

    $text = "$Value".Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        return $null
    }

    $intValue = 0
    if ([int]::TryParse($text, [ref]$intValue)) {
        if ($intValue -eq $NewBusinessValue -or $intValue -eq $RenewalValue) {
            return $intValue
        }
    }

    $normalized = $text.ToLowerInvariant()
    if ($normalized -in @("negocio nuevo", "negocionuevo", "nuevo", "new business", "newbusiness")) {
        return $NewBusinessValue
    }

    if ($normalized -in @("renovacion", "renovación", "renewal", "renewals")) {
        return $RenewalValue
    }

    return $null
}

function Resolve-ContractFromAdditional([string]$RawAdditional) {
    if ([string]::IsNullOrWhiteSpace($RawAdditional)) {
        return $null
    }

    try {
        $additional = $RawAdditional | ConvertFrom-Json
    }
    catch {
        return $null
    }

    $contractKind = Normalize-ContractValue $additional.ContractKindOptionValue
    if ($null -ne $contractKind) {
        return $contractKind
    }

    $dealType = 0
    if ($null -ne $additional.DealTypeValue -and [int]::TryParse("$($additional.DealTypeValue)", [ref]$dealType)) {
        if ($dealType -in @(2, 3, 4)) {
            return $RenewalValue
        }

        return $NewBusinessValue
    }

    return $null
}

function Resolve-ContractFromDescription([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $null
    }

    if ($Text -match "(?im)^\s*Tipo\s+contrato\s*:\s*(?<value>[^\r\n]+)") {
        $value = $Matches["value"].Trim()
        $normalized = $value.ToLowerInvariant()
        if ($normalized -match "renovaci[oó]n|contrato\s+existente|existente") {
            return $RenewalValue
        }

        if ($normalized -match "negocio\s+nuevo|nuevo") {
            return $NewBusinessValue
        }
    }

    return $null
}

function Get-ObjectPropertyValue($Object, [string]$Name) {
    if ($null -eq $Object -or [string]::IsNullOrWhiteSpace($Name)) {
        return $null
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Get-ScoreRowsForBackfill {
    $select = "$IdField,createdon,$TargetContractField,$SourceContractField,$AdditionalField,$DescriptionField,$LegacyDescriptionField"
    $start = $StartCreatedOn.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    $end = $EndCreatedOn.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    $filter = "createdon ge $start and createdon lt $end"
    $path = "$EntitySetName`?`$select=$select&`$filter=$([uri]::EscapeDataString($filter))"
    $rows = New-Object System.Collections.Generic.List[object]

    while (-not [string]::IsNullOrWhiteSpace($path)) {
        $page = Invoke-DvRequest -Method Get -Path $path
        foreach ($row in @($page.value)) {
            $rows.Add($row)
        }

        if ($page.PSObject.Properties.Name -contains "@odata.nextLink") {
            $path = $page."@odata.nextLink"
        } else {
            $path = $null
        }
    }

    return $rows
}

Ensure-ContratoAttribute

$rows = Get-ScoreRowsForBackfill
$updated = 0
$skippedExisting = 0
$skippedNoSource = 0
$skippedNoId = 0

foreach ($row in $rows) {
    $recordId = [string](Get-ObjectPropertyValue $row $IdField)
    if ([string]::IsNullOrWhiteSpace($recordId)) {
        $skippedNoId++
        continue
    }

    $existingValue = Normalize-ContractValue (Get-ObjectPropertyValue $row $TargetContractField)
    if (-not $OverwriteExisting -and $null -ne $existingValue) {
        $skippedExisting++
        continue
    }

    $targetValue = Normalize-ContractValue (Get-ObjectPropertyValue $row $SourceContractField)
    if ($null -eq $targetValue) {
        $targetValue = Resolve-ContractFromAdditional ([string](Get-ObjectPropertyValue $row $AdditionalField))
    }

    if ($null -eq $targetValue) {
        $description = ([string](Get-ObjectPropertyValue $row $DescriptionField)) + "`n" + ([string](Get-ObjectPropertyValue $row $LegacyDescriptionField))
        $targetValue = Resolve-ContractFromDescription $description
    }

    if ($null -eq $targetValue) {
        $skippedNoSource++
        continue
    }

    $payload = @{}
    $payload[$TargetContractField] = $targetValue
    if ($DryRun) {
        Write-Host "DRY RUN actualizaria $recordId => $targetValue"
    } else {
        Invoke-DvRequest -Method Patch -Path "$EntitySetName($recordId)" -Body $payload | Out-Null
    }

    $updated++
}

Write-Host "Backfill terminado. Leidos: $($rows.Count). Actualizados: $updated. Omitidos existentes: $skippedExisting. Omitidos sin fuente: $skippedNoSource. Omitidos sin id: $skippedNoId."
