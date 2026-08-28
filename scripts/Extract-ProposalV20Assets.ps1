[CmdletBinding()]
param(
    [Parameter()]
    [string]$SourcePath = 'C:\Users\SebastianRuiz\Downloads\plantilla_interactiva_cotizaciones_digital_tech_v20.html',

    [Parameter()]
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$resolvedSource = (Resolve-Path -LiteralPath $SourcePath).Path
$resolvedProject = (Resolve-Path -LiteralPath $ProjectRoot).Path
$lines = Get-Content -LiteralPath $resolvedSource
$requiredAssets = @(
    'cert_partner_sec',
    'cert_partner_mw',
    'cert_mct2',
    'badge_cyber',
    'badge_ea',
    'badge_azure'
)

$imageLine = -1
for ($index = 0; $index -lt $lines.Count; $index++) {
    if ($lines[$index].StartsWith('var DT_IMAGE_B64=', [StringComparison]::Ordinal)) {
        $imageLine = $index
        break
    }
}

if ($imageLine -lt 0) {
    throw 'No fue posible localizar los recursos embebidos de la plantilla V20.'
}

$jsonText = $lines[$imageLine].Substring('var DT_IMAGE_B64='.Length).TrimEnd(';')
$imageCatalog = $jsonText | ConvertFrom-Json
$assetDirectory = Join-Path $resolvedProject 'wwwroot\img\proposals\v17'
[System.IO.Directory]::CreateDirectory($assetDirectory) | Out-Null

foreach ($name in $requiredAssets) {
    $asset = $imageCatalog.PSObject.Properties[$name].Value
    if ($null -eq $asset) {
        throw "La plantilla V20 no contiene el recurso requerido: $name."
    }

    $assetPath = Join-Path $assetDirectory ($name + '.jpg')
    [System.IO.File]::WriteAllBytes($assetPath, [Convert]::FromBase64String([string]$asset.d))
}

Write-Host "Recursos V20 extraídos: $($requiredAssets.Count) en $assetDirectory"
Write-Host 'No se modificaron valores, modelos económicos ni el flujo de la calculadora.'
