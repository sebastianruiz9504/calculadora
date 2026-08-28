param(
    [string]$DataverseUrl = "https://orgc79ca19c.crm2.dynamics.com",
    [string]$TenantId = "cab7ea42-4a21-4548-952f-fcde81f2bdd6",
    [string]$EntitySetName = "cr07a_contractrecord1s",
    [string]$IdField = "cr07a_contractrecord1id",
    [string]$AdditionalField = "cr07a_adicionales",
    [string]$ContractValueField = "cr07a_contractvalue",
    [string]$DescriptionField = "cr07a_aprovisionamientodetallelargo",
    [string]$LegacyDescriptionField = "cr07a_description",
    [datetime]$StartCreatedOn = "2026-01-01T00:00:00Z",
    [datetime]$EndCreatedOn = "2027-01-01T00:00:00Z",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$DataverseUrl = $DataverseUrl.TrimEnd("/")

function Get-AccessToken {
    $token = az account get-access-token `
        --tenant $TenantId `
        --resource $DataverseUrl `
        --query accessToken `
        -o tsv

    if ([string]::IsNullOrWhiteSpace($token)) {
        throw "No se pudo obtener token para Dataverse."
    }

    return $token
}

function Get-PropValue {
    param(
        [object]$Object,
        [string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Convert-ToDecimalOrNull {
    param([object]$Value)

    if ($null -eq $Value -or $Value -eq "") {
        return $null
    }

    try {
        return [decimal]$Value
    }
    catch {
        return $null
    }
}

function Read-PathValue {
    param(
        [object]$Root,
        [string[]]$Path
    )

    $current = $Root
    foreach ($part in $Path) {
        $current = Get-PropValue $current $part
        if ($null -eq $current) {
            return $null
        }
    }

    return $current
}

function Resolve-ContractValueFromAdditional {
    param([string]$RawAdditional)

    if ([string]::IsNullOrWhiteSpace($RawAdditional)) {
        return $null
    }

    try {
        $json = $RawAdditional | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        return $null
    }

    $candidatePaths = @(
        @("LastResult", "TotalSale"),
        @("LastResult", "totalSale"),
        @("lastResult", "TotalSale"),
        @("lastResult", "totalSale"),
        @("result", "TotalSale"),
        @("result", "totalSale"),
        @("TotalSale"),
        @("totalSale")
    )

    foreach ($path in $candidatePaths) {
        $value = Convert-ToDecimalOrNull (Read-PathValue $json $path)
        if ($null -ne $value) {
            return [math]::Round($value, 2)
        }
    }

    $sum = [decimal]0
    $foundLineValue = $false
    foreach ($linesProperty in @("Lines", "lines", "LineItems", "lineItems")) {
        $lines = Get-PropValue $json $linesProperty
        if ($null -eq $lines) {
            continue
        }

        foreach ($line in @($lines)) {
            foreach ($valueProperty in @("TotalValue", "totalValue", "VentaTotal", "ventaTotal")) {
                $lineValue = Convert-ToDecimalOrNull (Get-PropValue $line $valueProperty)
                if ($null -ne $lineValue) {
                    $sum += $lineValue
                    $foundLineValue = $true
                    break
                }
            }
        }
    }

    if ($foundLineValue) {
        return [math]::Round($sum, 2)
    }

    return $null
}

function Convert-LooseDecimalOrNull {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }

    $normalized = $Value.Trim() -replace "[^0-9,\.\-]", ""
    if ([string]::IsNullOrWhiteSpace($normalized)) {
        return $null
    }

    if ($normalized.Contains(",") -and $normalized.Contains(".")) {
        if ($normalized.LastIndexOf(",") -gt $normalized.LastIndexOf(".")) {
            $normalized = $normalized.Replace(".", "").Replace(",", ".")
        }
        else {
            $normalized = $normalized.Replace(",", "")
        }
    }
    elseif ($normalized.Contains(",")) {
        $normalized = $normalized.Replace(",", ".")
    }

    try {
        return [decimal]::Parse($normalized, [Globalization.CultureInfo]::InvariantCulture)
    }
    catch {
        return $null
    }
}

function Resolve-ContractValueFromDescription {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $null
    }

    foreach ($pattern in @(
        "(?im)^\s*Venta\s+total\s+anual\s*:\s*(?<value>[^\r\n]+)",
        "(?im)^\s*Venta\s+total\s*:\s*(?<value>[^\r\n]+)"
    )) {
        $match = [regex]::Match($Text, $pattern)
        if ($match.Success) {
            $value = Convert-LooseDecimalOrNull $match.Groups["value"].Value
            if ($null -ne $value) {
                return [math]::Round($value, 2)
            }
        }
    }

    return $null
}

$token = Get-AccessToken
$headers = @{
    Authorization = "Bearer $token"
    Accept = "application/json"
    "Content-Type" = "application/json"
}

$filter = "{0} ge {1:yyyy-MM-ddTHH:mm:ssZ} and {0} lt {2:yyyy-MM-ddTHH:mm:ssZ}" -f "createdon", $StartCreatedOn.ToUniversalTime(), $EndCreatedOn.ToUniversalTime()
$select = "$IdField,createdon,$ContractValueField,$AdditionalField,$DescriptionField,$LegacyDescriptionField"
$url = "$DataverseUrl/api/data/v9.2/$EntitySetName" +
    "?`$select=$select&`$filter=$([Uri]::EscapeDataString($filter))&`$top=5000"

$rows = @()
while (-not [string]::IsNullOrWhiteSpace($url)) {
    $response = Invoke-RestMethod -Method Get -Uri $url -Headers $headers
    $rows += @($response.value)

    $next = $response.PSObject.Properties["@odata.nextLink"]
    $url = if ($null -ne $next) { [string]$next.Value } else { $null }
}

$updated = 0
$unchanged = 0
$skipped = 0
$errors = 0

foreach ($row in $rows) {
    $recordId = Get-PropValue $row $IdField
    $contractValue = Resolve-ContractValueFromAdditional ([string](Get-PropValue $row $AdditionalField))
    if ($null -eq $contractValue) {
        $description = ([string](Get-PropValue $row $DescriptionField)) + "`n" + ([string](Get-PropValue $row $LegacyDescriptionField))
        $contractValue = Resolve-ContractValueFromDescription $description
    }
    if ($null -eq $contractValue) {
        $skipped++
        continue
    }

    $currentValue = Convert-ToDecimalOrNull (Get-PropValue $row $ContractValueField)
    if ($null -ne $currentValue -and [math]::Abs($currentValue - $contractValue) -lt [decimal]0.01) {
        $unchanged++
        continue
    }

    if ($DryRun) {
        $updated++
        continue
    }

    try {
        $body = @{ $ContractValueField = $contractValue } | ConvertTo-Json -Depth 4
        Invoke-RestMethod `
            -Method Patch `
            -Uri "$DataverseUrl/api/data/v9.2/$EntitySetName($recordId)" `
            -Headers $headers `
            -Body $body | Out-Null
        $updated++
    }
    catch {
        $errors++
        Write-Host "ERROR $recordId $($_.Exception.Message)"
    }
}

Write-Host "Leidos: $($rows.Count)"
Write-Host "Actualizados: $updated"
Write-Host "Ya iguales: $unchanged"
Write-Host "Omitidos sin total en Adicionales: $skipped"
Write-Host "Errores: $errors"
