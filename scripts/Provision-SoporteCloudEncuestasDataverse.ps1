param(
    [string]$DataverseUrl = "https://orgc79ca19c.crm2.dynamics.com",
    [string]$SchemaPath = "$PSScriptRoot\soporte-cloud-encuestas-dataverse-schema.json",
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

function ConvertTo-SchemaName([string]$LogicalName) {
    $parts = $LogicalName.Split("_", [System.StringSplitOptions]::RemoveEmptyEntries)
    if ($parts.Count -lt 2) {
        return $LogicalName
    }

    $prefix = $parts[0]
    $name = ($parts | Select-Object -Skip 1 | ForEach-Object {
        if ($_.Length -le 1) { $_.ToUpperInvariant() } else { $_.Substring(0, 1).ToUpperInvariant() + $_.Substring(1) }
    }) -join ""

    return "$prefix`_$name"
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

function Resolve-Options($column) {
    $optionsRef = [string]$column.optionsRef
    if ([string]::IsNullOrWhiteSpace($optionsRef)) {
        throw "La columna Choice $($column.logicalName) no tiene optionsRef."
    }

    $options = $schema.optionSets.$optionsRef
    if ($null -eq $options) {
        throw "No existe el option set $optionsRef."
    }

    return @($options | ForEach-Object { New-OptionMetadata $_.label ([int]$_.value) })
}

function New-AttributePayload($table, $column) {
    $base = @{
        SchemaName = ConvertTo-SchemaName $column.logicalName
        DisplayName = New-DvLabel $column.displayName
        Description = New-DvLabel $column.displayName
        RequiredLevel = New-DvRequiredLevel ([bool]$column.required)
    }

    if ($column.logicalName -eq $table.primaryNameAttribute) {
        $base.IsPrimaryName = $true
    }

    switch ([string]$column.type) {
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
                Options = @(Resolve-Options $column)
            }
            return $base
        }
        "DateTime" {
            $base["@odata.type"] = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
            $base.Format = if ([string]$column.format -eq "DateOnly") { "DateOnly" } else { "DateAndTime" }
            $base.DateTimeBehavior = @{ Value = if ([string]::IsNullOrWhiteSpace([string]$column.behavior)) { "UserLocal" } else { [string]$column.behavior } }
            return $base
        }
        "Decimal" {
            $base["@odata.type"] = "Microsoft.Dynamics.CRM.DecimalAttributeMetadata"
            $base.MinValue = if ($null -eq $column.minValue) { 0 } else { [decimal]$column.minValue }
            $base.MaxValue = if ($null -eq $column.maxValue) { 1000000000 } else { [decimal]$column.maxValue }
            $base.Precision = if ($null -eq $column.precision) { 2 } else { [int]$column.precision }
            return $base
        }
        "WholeNumber" {
            $base["@odata.type"] = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
            $base.MinValue = if ($null -eq $column.minValue) { 0 } else { [int]$column.minValue }
            $base.MaxValue = if ($null -eq $column.maxValue) { 2147483647 } else { [int]$column.maxValue }
            $base.Format = "None"
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
        default {
            throw "Tipo de columna no soportado: $($column.type)"
        }
    }
}

function New-RelationshipPayload($table, $column) {
    $schemaName = "$(ConvertTo-SchemaName $table.logicalName)_$(($column.logicalName -replace '^cr07a_', ''))"
    if ([string]$column.targetTable -eq "systemuser") {
        $schemaName = "$schemaName`_systemuser"
    }

    return @{
        "@odata.type" = "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata"
        SchemaName = $schemaName
        ReferencedEntity = [string]$column.targetTable
        ReferencingEntity = [string]$table.logicalName
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
            Label = New-DvLabel $table.displayName
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

function New-DvEntity($table) {
    $primaryNameColumn = @($table.columns | Where-Object { $_.logicalName -eq $table.primaryNameAttribute })[0]
    if ($null -eq $primaryNameColumn) {
        throw "La tabla $($table.logicalName) no contiene la columna primaria $($table.primaryNameAttribute)."
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.EntityMetadata"
        SchemaName = ConvertTo-SchemaName $table.logicalName
        DisplayName = New-DvLabel $table.displayName
        DisplayCollectionName = New-DvLabel $table.displayCollectionName
        Description = New-DvLabel $table.displayName
        OwnershipType = $table.ownership
        HasActivities = $false
        HasNotes = if ($null -eq $table.hasNotes) { $false } else { [bool]$table.hasNotes }
        IsActivity = $false
        Attributes = @(
            New-AttributePayload $table $primaryNameColumn
        )
    }

    Invoke-DvRequest -Method Post -Path "EntityDefinitions" -Body $payload | Out-Null
}

function Update-EntityFlagsIfNeeded($table) {
    if ($null -eq $table.hasNotes) {
        return
    }

    $entity = [uri]::EscapeDataString("LogicalName='$($table.logicalName)'")
    $metadata = Invoke-DvRequest -Method Get -Path "EntityDefinitions($entity)?`$select=LogicalName,HasNotes" -AllowNotFound
    if ($null -eq $metadata) {
        return
    }

    $targetHasNotes = [bool]$table.hasNotes
    if ([bool]$metadata.HasNotes -eq $targetHasNotes) {
        Write-Host "  OK notas: $($table.logicalName) HasNotes=$targetHasNotes"
        return
    }

    Write-Host "  Actualizando notas: $($table.logicalName) HasNotes=$targetHasNotes"
    try {
        Invoke-DvRequest -Method Patch -Path "EntityDefinitions($entity)" -Body @{
            HasNotes = $targetHasNotes
        } | Out-Null
    }
    catch {
        Write-Warning "  No fue posible actualizar HasNotes para $($table.logicalName). Si la tabla ya existe, crea una tabla auxiliar con notas habilitadas o habilitalas desde personalizacion clasica."
    }
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

function Get-ChoiceOptionValues($table, $column) {
    $entity = [uri]::EscapeDataString("LogicalName='$($table.logicalName)'")
    $attribute = [uri]::EscapeDataString("LogicalName='$($column.logicalName)'")
    $path = "EntityDefinitions($entity)/Attributes($attribute)/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Options)"
    $metadata = Invoke-DvRequest -Method Get -Path $path -AllowNotFound
    if ($null -eq $metadata -or $null -eq $metadata.OptionSet -or $null -eq $metadata.OptionSet.Options) {
        return @()
    }

    return @($metadata.OptionSet.Options | ForEach-Object { [int]$_.Value })
}

function Add-ChoiceOptionsIfMissing($table, $column) {
    $optionsRef = [string]$column.optionsRef
    if ([string]::IsNullOrWhiteSpace($optionsRef)) {
        return
    }

    $schemaOptions = @($schema.optionSets.$optionsRef)
    if ($schemaOptions.Count -eq 0) {
        return
    }

    $existingValues = @(Get-ChoiceOptionValues $table $column)
    foreach ($option in $schemaOptions) {
        $value = [int]$option.value
        if ($existingValues -contains $value) {
            continue
        }

        Write-Host "  Agregando opcion choice: $($table.logicalName).$($column.logicalName) -> $($option.label) ($value)"
        Invoke-DvRequest -Method Post -Path "InsertOptionValue" -Body @{
            EntityLogicalName = [string]$table.logicalName
            AttributeLogicalName = [string]$column.logicalName
            Value = $value
            Label = New-DvLabel ([string]$option.label)
        } | Out-Null
    }
}

function Add-ColumnIfMissing($table, $column) {
    if (Test-AttributeExists $table.logicalName $column.logicalName) {
        Write-Host "  OK columna existente: $($table.logicalName).$($column.logicalName)"
        if ([string]$column.type -eq "Choice") {
            Add-ChoiceOptionsIfMissing $table $column
        }
        return
    }

    if ([string]$column.type -eq "Lookup") {
        Write-Host "  Creando lookup: $($table.logicalName).$($column.logicalName) -> $($column.targetTable)"
        Invoke-DvRequest -Method Post -Path "RelationshipDefinitions" -Body (New-RelationshipPayload $table $column) | Out-Null
        return
    }

    Write-Host "  Creando columna: $($table.logicalName).$($column.logicalName) ($($column.type))"
    $entity = [uri]::EscapeDataString("LogicalName='$($table.logicalName)'")
    Invoke-DvRequest -Method Post -Path "EntityDefinitions($entity)/Attributes" -Body (New-AttributePayload $table $column) | Out-Null
}

function Publish-DvCustomizations {
    for ($i = 1; $i -le 12; $i++) {
        try {
            Invoke-DvRequest -Method Post -Path "PublishAllXml" -Body @{} | Out-Null
            return
        }
        catch {
            if ($i -eq 12) { throw }
            Write-Host "  Publish ocupado; reintento $i de 12 en 20 segundos..."
            Start-Sleep -Seconds 20
        }
    }
}

Write-Host "Ambiente Dataverse: $DataverseUrl" -ForegroundColor Cyan

foreach ($table in $schema.tables) {
    Write-Host "Tabla: $($table.logicalName)" -ForegroundColor Cyan
    if (Test-EntityExists $table.logicalName) {
        Write-Host "  OK tabla existente"
    } else {
        Write-Host "  Creando tabla"
        New-DvEntity $table
        Wait-ForEntity $table.logicalName
    }
}

foreach ($table in $schema.tables) {
    Update-EntityFlagsIfNeeded $table
}

foreach ($table in $schema.tables) {
    Write-Host "Columnas: $($table.logicalName)" -ForegroundColor Cyan
    $table.columns |
        Where-Object { $_.logicalName -ne $table.primaryNameAttribute } |
        ForEach-Object { Add-ColumnIfMissing $table $_ }
}

if (-not $SkipPublish) {
    Write-Host "Publicando personalizaciones..." -ForegroundColor Cyan
    Publish-DvCustomizations
}

Write-Host "Provision de encuestas de Soporte Cloud finalizado." -ForegroundColor Green
