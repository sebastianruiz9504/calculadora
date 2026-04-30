param(
    [string]$DataverseUrl = "https://orgc79ca19c.crm2.dynamics.com",
    [string]$SchemaPath = "$PSScriptRoot\envios-dataverse-schema.json",
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

function New-DvRequiredLevel([bool]$Required) {
    return @{
        Value = if ($Required) { "ApplicationRequired" } else { "None" }
        CanBeChanged = $true
        ManagedPropertyLogicalName = "canmodifyrequirementlevelsettings"
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

        $json = $Body | ConvertTo-Json -Depth 30
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

function Test-EntityExists([string]$LogicalName) {
    $encoded = [uri]::EscapeDataString("LogicalName='$LogicalName'")
    $result = Invoke-DvRequest -Method Get -Path "EntityDefinitions($encoded)?`$select=LogicalName" -AllowNotFound
    return $null -ne $result
}

function Test-AttributeExists([string]$EntityLogicalName, [string]$AttributeLogicalName) {
    $entity = [uri]::EscapeDataString("LogicalName='$EntityLogicalName'")
    $attribute = [uri]::EscapeDataString("LogicalName='$AttributeLogicalName'")
    $result = Invoke-DvRequest -Method Get -Path "EntityDefinitions($entity)/Attributes($attribute)?`$select=LogicalName" -AllowNotFound
    return $null -ne $result
}

function New-OptionMetadata([string]$Label, [int]$Value) {
    return @{
        "@odata.type" = "Microsoft.Dynamics.CRM.OptionMetadata"
        Value = $Value
        Label = New-DvLabel $Label
    }
}

function ConvertTo-SchemaName([string]$LogicalName) {
    $parts = $LogicalName.Split("_", [System.StringSplitOptions]::RemoveEmptyEntries)
    if ($parts.Count -lt 2) {
        return $LogicalName
    }

    $prefix = $parts[0]
    $name = ($parts | Select-Object -Skip 1 | ForEach-Object {
        if ($_.Length -le 1) {
            $_.ToUpperInvariant()
        } else {
            $_.Substring(0, 1).ToUpperInvariant() + $_.Substring(1)
        }
    }) -join ""

    return "$prefix`_$name"
}

function New-AttributePayload($column) {
    $schemaName = ConvertTo-SchemaName $column.logicalName
    $base = @{
        SchemaName = $schemaName
        DisplayName = New-DvLabel $column.displayName
        Description = New-DvLabel $column.displayName
        RequiredLevel = New-DvRequiredLevel ([bool]$column.required)
    }

    if ($column.logicalName -eq $schema.table.primaryNameAttribute) {
        $base.IsPrimaryName = $true
    }

    switch ($column.type) {
        "Text" {
            $base["@odata.type"] = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            $base.MaxLength = [int]$column.maxLength
            $base.FormatName = @{ Value = "Text" }
            return $base
        }
        "MultilineText" {
            $base["@odata.type"] = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
            $base.MaxLength = [int]$column.maxLength
            $base.FormatName = @{ Value = "TextArea" }
            return $base
        }
        "Choice" {
            $base["@odata.type"] = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
            $base.OptionSet = @{
                "@odata.type" = "Microsoft.Dynamics.CRM.OptionSetMetadata"
                IsGlobal = $false
                OptionSetType = "Picklist"
                Options = @($schema.statusOptions | ForEach-Object { New-OptionMetadata $_.label ([int]$_.value) })
            }
            return $base
        }
        "DateTime" {
            $base["@odata.type"] = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
            $base.Format = "DateAndTime"
            $base.DateTimeBehavior = @{ Value = "UserLocal" }
            return $base
        }
        "Currency" {
            $base["@odata.type"] = "Microsoft.Dynamics.CRM.MoneyAttributeMetadata"
            $base.MinValue = 0
            $base.MaxValue = 1000000000000
            $base.Precision = 2
            $base.PrecisionSource = 1
            return $base
        }
        "TwoOptions" {
            $base["@odata.type"] = "Microsoft.Dynamics.CRM.BooleanAttributeMetadata"
            $base.DefaultValue = [bool]$column.defaultValue
            $base.OptionSet = @{
                TrueOption = New-OptionMetadata "Si" 1
                FalseOption = New-OptionMetadata "No" 0
            }
            return $base
        }
        "File" {
            $base["@odata.type"] = "Microsoft.Dynamics.CRM.FileAttributeMetadata"
            $base.MaxSizeInKB = [int]$column.maxSizeMb * 1024
            return $base
        }
        default {
            throw "Tipo de columna no soportado por este script: $($column.type)"
        }
    }
}

function New-RelationshipPayload($column) {
    $schemaName = "cr07a_envio_$($column.logicalName.Replace('cr07a_', ''))"
    if ($column.targetTable -eq "systemuser") {
        $schemaName = "$schemaName`_systemuser"
    }

    return @{
        "@odata.type" = "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata"
        SchemaName = $schemaName
        ReferencedEntity = $column.targetTable
        ReferencingEntity = $schema.table.logicalName
        Lookup = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.LookupAttributeMetadata"
            SchemaName = ConvertTo-SchemaName $column.logicalName
            DisplayName = New-DvLabel $column.displayName
            Description = New-DvLabel $column.displayName
            RequiredLevel = New-DvRequiredLevel ([bool]$column.required)
        }
        AssociatedMenuConfiguration = @{
            Behavior = "UseLabel"
            Group = "Details"
            Label = New-DvLabel $schema.table.displayName
            Order = 10000
        }
        CascadeConfiguration = @{
            Assign = "NoCascade"
            Delete = "RemoveLink"
            Merge = "NoCascade"
            Reparent = "NoCascade"
            Share = "NoCascade"
            Unshare = "NoCascade"
        }
    }
}

function New-EnviosEntity {
    $primaryNameColumn = $schema.columns | Where-Object { $_.logicalName -eq $schema.table.primaryNameAttribute } | Select-Object -First 1
    if (-not $primaryNameColumn) {
        throw "El esquema no contiene la columna primaria $($schema.table.primaryNameAttribute)."
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.EntityMetadata"
        SchemaName = ConvertTo-SchemaName $schema.table.logicalName
        DisplayName = New-DvLabel $schema.table.displayName
        DisplayCollectionName = New-DvLabel $schema.table.displayCollectionName
        Description = New-DvLabel "Solicitudes internas de envio con agenda, flete, recogida y acta de entrega."
        OwnershipType = $schema.table.ownership
        HasActivities = $false
        HasNotes = $false
        IsActivity = $false
        Attributes = @(
            New-AttributePayload $primaryNameColumn
        )
    }

    Invoke-DvRequest -Method Post -Path "EntityDefinitions" -Body $payload | Out-Null
}

function Wait-ForEntity([string]$LogicalName) {
    for ($i = 0; $i -lt 24; $i++) {
        if (Test-EntityExists $LogicalName) {
            return
        }

        Start-Sleep -Seconds 5
    }

    throw "La tabla $LogicalName no estuvo disponible despues de crearla."
}

function Add-AttributeIfMissing($column) {
    if (Test-AttributeExists $schema.table.logicalName $column.logicalName) {
        Write-Host "  OK columna existente: $($column.logicalName)"
        return
    }

    if ($column.type -eq "Lookup") {
        Write-Host "  Creando lookup: $($column.logicalName) -> $($column.targetTable)"
        Invoke-DvRequest -Method Post -Path "RelationshipDefinitions" -Body (New-RelationshipPayload $column) | Out-Null
        return
    }

    Write-Host "  Creando columna: $($column.logicalName) ($($column.type))"
    $entity = [uri]::EscapeDataString("LogicalName='$($schema.table.logicalName)'")
    Invoke-DvRequest -Method Post -Path "EntityDefinitions($entity)/Attributes" -Body (New-AttributePayload $column) | Out-Null
}

function Get-ModuleOptionValues {
    $metadataTypes = @(
        "MultiSelectPicklistAttributeMetadata",
        "PicklistAttributeMetadata"
    )

    foreach ($metadataType in $metadataTypes) {
        try {
            $path = "EntityDefinitions(LogicalName='cr07a_empleado')/Attributes(LogicalName='cr07a_modulos')/Microsoft.Dynamics.CRM.${metadataType}?`$select=LogicalName&`$expand=OptionSet(`$select=Options)"
            $result = Invoke-DvRequest -Method Get -Path $path
            $options = @($result.OptionSet.Options)
            return @($options | ForEach-Object { [int]$_.Value })
        }
        catch {
            continue
        }
    }

    throw "No fue posible leer las opciones de cr07a_empleado.cr07a_modulos."
}

function Add-ModuleOptionIfMissing($moduleOption, [int[]]$existingValues) {
    if ($existingValues -contains [int]$moduleOption.value) {
        Write-Host "  OK opcion existente: $($moduleOption.label) = $($moduleOption.value)"
        return
    }

    Write-Host "  Agregando opcion de modulo: $($moduleOption.label) = $($moduleOption.value)"
    $payload = @{
        EntityLogicalName = "cr07a_empleado"
        AttributeLogicalName = "cr07a_modulos"
        Value = [int]$moduleOption.value
        Label = New-DvLabel $moduleOption.label
    }
    Invoke-DvRequest -Method Post -Path "InsertOptionValue" -Body $payload | Out-Null
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

Write-Host "Ambiente Dataverse: $DataverseUrl" -ForegroundColor Cyan
Write-Host "Tabla objetivo: $($schema.table.logicalName)" -ForegroundColor Cyan

if (Test-EntityExists $schema.table.logicalName) {
    Write-Host "OK tabla existente: $($schema.table.logicalName)"
} else {
    Write-Host "Creando tabla: $($schema.table.logicalName)"
    New-EnviosEntity
    Wait-ForEntity $schema.table.logicalName
}

Write-Host "Columnas" -ForegroundColor Cyan
$schema.columns |
    Where-Object { $_.logicalName -ne $schema.table.primaryNameAttribute } |
    ForEach-Object { Add-AttributeIfMissing $_ }

Write-Host "Opciones de modulos" -ForegroundColor Cyan
$existingModuleValues = Get-ModuleOptionValues
$schema.moduleOptions | ForEach-Object { Add-ModuleOptionIfMissing $_ $existingModuleValues }

if (-not $SkipPublish) {
    Write-Host "Publicando personalizaciones..." -ForegroundColor Cyan
    Publish-DvCustomizations
}

Write-Host "Provision de Envios finalizada." -ForegroundColor Green
