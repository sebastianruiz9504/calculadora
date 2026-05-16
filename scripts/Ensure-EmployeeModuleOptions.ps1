param(
    [string]$DataverseUrl = "https://orgc79ca19c.crm2.dynamics.com",
    [string]$SchemaPath = "$PSScriptRoot\employee-module-options-dataverse-schema.json",
    [string]$EmployeeTableLogicalName = "cr07a_empleado",
    [string]$EmployeeModulesField = "cr07a_modulos",
    [int]$LanguageCode = 3082,
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw "Azure CLI no esta disponible. Instala az o inicia sesion antes de ejecutar este script."
}

if (-not (Test-Path -LiteralPath $SchemaPath)) {
    throw "No se encontro el archivo de esquema: $SchemaPath"
}

$schema = Get-Content -LiteralPath $SchemaPath -Raw | ConvertFrom-Json
$DataverseUrl = $DataverseUrl.TrimEnd("/")

function Get-AccessToken {
    $token = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
    if ([string]::IsNullOrWhiteSpace($token)) {
        throw "No fue posible obtener token para $DataverseUrl. Ejecuta 'az login' con un usuario con permisos de personalizacion en Dataverse."
    }

    return $token
}

$script:AccessToken = Get-AccessToken

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

function Invoke-DvRequest {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Path,
        [object]$Body = $null
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

    if ($null -eq $Body) {
        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers
    }

    $json = $Body | ConvertTo-Json -Depth 30
    return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -ContentType "application/json" -Body $json
}

function Get-ModuleOptionValues {
    $metadataTypes = @(
        "MultiSelectPicklistAttributeMetadata",
        "PicklistAttributeMetadata"
    )

    foreach ($metadataType in $metadataTypes) {
        try {
            $path = "EntityDefinitions(LogicalName='$EmployeeTableLogicalName')/Attributes(LogicalName='$EmployeeModulesField')/Microsoft.Dynamics.CRM.${metadataType}?`$select=LogicalName&`$expand=OptionSet(`$select=Options)"
            $result = Invoke-DvRequest -Method Get -Path $path
            $options = @($result.OptionSet.Options)
            return @($options | ForEach-Object { [int]$_.Value })
        }
        catch {
            continue
        }
    }

    throw "No fue posible leer las opciones de $EmployeeTableLogicalName.$EmployeeModulesField."
}

function Add-ModuleOptionIfMissing($moduleOption, [System.Collections.Generic.HashSet[int]]$existingValues) {
    $value = [int]$moduleOption.value
    if ($existingValues.Contains($value)) {
        Write-Host "  OK opcion existente: $($moduleOption.label) = $value"
        return
    }

    Write-Host "  Agregando opcion de modulo: $($moduleOption.label) = $value"
    $payload = @{
        EntityLogicalName = $EmployeeTableLogicalName
        AttributeLogicalName = $EmployeeModulesField
        Value = $value
        Label = New-DvLabel $moduleOption.label
    }
    Invoke-DvRequest -Method Post -Path "InsertOptionValue" -Body $payload | Out-Null
    [void]$existingValues.Add($value)
}

function Publish-DvCustomizations {
    for ($i = 1; $i -le 12; $i++) {
        try {
            Invoke-DvRequest -Method Post -Path "PublishAllXml" -Body @{} | Out-Null
            return
        }
        catch {
            if ($i -eq 12) {
                throw
            }

            Write-Host "  Publish ocupado; reintento $i de 12 en 20 segundos..."
            Start-Sleep -Seconds 20
        }
    }
}

Write-Host "Opciones de modulos en $EmployeeTableLogicalName.$EmployeeModulesField" -ForegroundColor Cyan
$existingModuleValues = [System.Collections.Generic.HashSet[int]]::new()
Get-ModuleOptionValues | ForEach-Object { [void]$existingModuleValues.Add([int]$_) }
$schema.moduleOptions | ForEach-Object { Add-ModuleOptionIfMissing $_ $existingModuleValues }

if (-not $SkipPublish) {
    Write-Host "Publicando personalizaciones..." -ForegroundColor Cyan
    Publish-DvCustomizations
}

Write-Host "Opciones de modulos verificadas." -ForegroundColor Green
