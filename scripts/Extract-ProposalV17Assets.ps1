[CmdletBinding()]
param(
    [Parameter()]
    [string]$SourcePath = 'C:\Users\SebastianRuiz\Downloads\plantilla_interactiva_cotizaciones_digital_tech_v17.html',

    [Parameter()]
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$resolvedSource = (Resolve-Path -LiteralPath $SourcePath).Path
$resolvedProject = (Resolve-Path -LiteralPath $ProjectRoot).Path
$lines = Get-Content -LiteralPath $resolvedSource

$imageLine = -1
for ($index = 0; $index -lt $lines.Count; $index++) {
    if ($lines[$index].StartsWith('var DT_IMAGE_B64=', [StringComparison]::Ordinal)) {
        $imageLine = $index
        break
    }
}

if ($imageLine -lt 0) {
    throw 'No fue posible localizar los recursos embebidos de la plantilla V17.'
}

$jsonText = $lines[$imageLine].Substring('var DT_IMAGE_B64='.Length).TrimEnd(';')
$imageCatalog = $jsonText | ConvertFrom-Json
$assetDirectory = Join-Path $resolvedProject 'wwwroot\img\proposals\v17'
[System.IO.Directory]::CreateDirectory($assetDirectory) | Out-Null

$assetCount = 0
foreach ($property in $imageCatalog.PSObject.Properties) {
    $name = $property.Name
    $asset = $property.Value
    $assetPath = Join-Path $assetDirectory ($name + '.jpg')
    [System.IO.File]::WriteAllBytes($assetPath, [Convert]::FromBase64String([string]$asset.d))
    $assetCount++
}

Write-Host "Recursos extraídos: $assetCount en $assetDirectory"
Write-Host 'El motor adaptado se mantiene en wwwroot/js/proposal-pdf-v17.js y no se sobrescribe.'
