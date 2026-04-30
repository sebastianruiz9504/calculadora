param(
    [string]$DataverseUrl = "https://orgc79ca19c.crm2.dynamics.com",
    [string]$SchemaPath = "$PSScriptRoot\m365-reportes-dataverse-schema.json",
    [int]$LanguageCode = 3082,
    [switch]$SkipPublish
)

$scriptPath = Join-Path $PSScriptRoot "Provision-SoporteCloudEncuestasDataverse.ps1"
if (-not (Test-Path -LiteralPath $scriptPath)) {
    throw "No se encontro el script base de provisionamiento: $scriptPath"
}

$params = @{
    DataverseUrl = $DataverseUrl
    SchemaPath = $SchemaPath
    LanguageCode = $LanguageCode
}

if ($SkipPublish) {
    $params.SkipPublish = $true
}

& $scriptPath @params
