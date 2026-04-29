param(
    [string]$SchemaPath = "$PSScriptRoot\envios-dataverse-schema.json"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $SchemaPath)) {
    throw "No se encontro el archivo de esquema: $SchemaPath"
}

$schema = Get-Content -LiteralPath $SchemaPath -Raw | ConvertFrom-Json

Write-Host "Tabla Dataverse requerida" -ForegroundColor Cyan
Write-Host "  Nombre logico: $($schema.table.logicalName)"
Write-Host "  Entity set:    $($schema.table.entitySetName)"
Write-Host "  Primary name:  $($schema.table.primaryNameAttribute)"
Write-Host ""

Write-Host "Opciones para cr07a_empleado.cr07a_modulos" -ForegroundColor Cyan
$schema.moduleOptions | ForEach-Object {
    Write-Host ("  {0} = {1}" -f $_.label, $_.value)
}
Write-Host ""

Write-Host "Opciones de estado cr07a_estado" -ForegroundColor Cyan
$schema.statusOptions | ForEach-Object {
    Write-Host ("  {0} = {1}" -f $_.label, $_.value)
}
Write-Host ""

Write-Host "Columnas" -ForegroundColor Cyan
$schema.columns | ForEach-Object {
    $target = if ($_.targetTable) { " -> $($_.targetTable)" } else { "" }
    $required = if ($_.required) { "obligatoria" } else { "opcional" }
    Write-Host ("  {0,-34} {1,-16} {2}{3}" -f $_.logicalName, $_.type, $required, $target)
}

Write-Host ""
Write-Host "Este script imprime el contrato exacto que consume la app. Crea la tabla y columnas en Power Apps/Dataverse con estos nombres logicos antes de habilitar el modulo." -ForegroundColor Yellow
