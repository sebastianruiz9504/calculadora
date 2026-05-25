param(
    [string]$DataverseUrl = "https://orgc79ca19c.crm2.dynamics.com",
    [string]$SchemaPath = "$PSScriptRoot\aguas-sda-dataverse-schema.json",
    [int]$LanguageCode = 3082,
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw "Azure CLI no esta disponible."
}

if (-not (Test-Path -LiteralPath $SchemaPath)) {
    throw "No se encontro el esquema: $SchemaPath"
}

$DataverseUrl = $DataverseUrl.TrimEnd("/")
$schema = Get-Content -LiteralPath $SchemaPath -Raw | ConvertFrom-Json
$script:AccessToken = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
if ([string]::IsNullOrWhiteSpace($script:AccessToken)) {
    throw "No fue posible obtener token para $DataverseUrl."
}

function New-DvLabel([string]$Text) {
    @{
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
    @{
        Value = if ($Required) { "ApplicationRequired" } else { "None" }
        CanBeChanged = $true
        ManagedPropertyLogicalName = "canmodifyrequirementlevelsettings"
    }
}

function ConvertTo-SchemaName([string]$LogicalName) {
    $parts = $LogicalName.Split("_", [System.StringSplitOptions]::RemoveEmptyEntries)
    if ($parts.Count -lt 2) { return $LogicalName }
    $prefix = $parts[0]
    $name = ($parts | Select-Object -Skip 1 | ForEach-Object {
        if ($_.Length -le 1) { $_.ToUpperInvariant() } else { $_.Substring(0, 1).ToUpperInvariant() + $_.Substring(1) }
    }) -join ""
    return "$prefix`_$name"
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

function Test-EntityExists([string]$LogicalName) {
    $encoded = [uri]::EscapeDataString("LogicalName='$LogicalName'")
    $result = Invoke-DvRequest -Method Get -Path "EntityDefinitions($encoded)?`$select=LogicalName" -AllowNotFound
    return $null -ne $result
}

function Test-AttributeExists([string]$EntityLogicalName, [string]$AttributeLogicalName) {
    $entity = [uri]::EscapeDataString("LogicalName='$EntityLogicalName'")
    $filter = [uri]::EscapeDataString("LogicalName eq '$AttributeLogicalName'")
    $result = Invoke-DvRequest -Method Get -Path "EntityDefinitions($entity)/Attributes?`$select=LogicalName&`$filter=$filter" -AllowNotFound
    return $null -ne $result -and $result.value.Count -gt 0
}

function New-OptionMetadata($option) {
    @{
        "@odata.type" = "Microsoft.Dynamics.CRM.OptionMetadata"
        Value = [int]$option.value
        Label = New-DvLabel $option.label
    }
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
        "WholeNumber" {
            $base["@odata.type"] = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
            $base.MinValue = 0
            $base.MaxValue = 1000000
            $base.Format = "None"
            return $base
        }
        "Decimal" {
            $base["@odata.type"] = "Microsoft.Dynamics.CRM.DecimalAttributeMetadata"
            $base.MinValue = -100000000000
            $base.MaxValue = 100000000000
            $base.Precision = 4
            return $base
        }
        "DateOnly" {
            $base["@odata.type"] = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
            $base.Format = "DateOnly"
            $base.DateTimeBehavior = @{ Value = "DateOnly" }
            return $base
        }
        "DateTime" {
            $base["@odata.type"] = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
            $base.Format = "DateAndTime"
            $base.DateTimeBehavior = @{ Value = "UserLocal" }
            return $base
        }
        "Choice" {
            $options = $schema.optionSets.PSObject.Properties[$column.optionsRef].Value
            $base["@odata.type"] = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
            $base.OptionSet = @{
                "@odata.type" = "Microsoft.Dynamics.CRM.OptionSetMetadata"
                IsGlobal = $false
                OptionSetType = "Picklist"
                Options = @($options | ForEach-Object { New-OptionMetadata $_ })
            }
            return $base
        }
        "MultiSelectChoice" {
            $options = $schema.optionSets.PSObject.Properties[$column.optionsRef].Value
            $base["@odata.type"] = "Microsoft.Dynamics.CRM.MultiSelectPicklistAttributeMetadata"
            $base.OptionSet = @{
                "@odata.type" = "Microsoft.Dynamics.CRM.OptionSetMetadata"
                IsGlobal = $false
                OptionSetType = "Picklist"
                Options = @($options | ForEach-Object { New-OptionMetadata $_ })
            }
            return $base
        }
        "TwoOptions" {
            $base["@odata.type"] = "Microsoft.Dynamics.CRM.BooleanAttributeMetadata"
            $base.DefaultValue = [bool]$column.defaultValue
            $base.OptionSet = @{
                TrueOption = New-OptionMetadata @{ label = "Si"; value = 1 }
                FalseOption = New-OptionMetadata @{ label = "No"; value = 0 }
            }
            return $base
        }
        default {
            throw "Tipo de columna no soportado: $($column.type)"
        }
    }
}

function New-RelationshipPayload($table, $column) {
    @{
        "@odata.type" = "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata"
        SchemaName = "$(ConvertTo-SchemaName $table.logicalName)_$(($column.logicalName -replace '^cr07a_', ''))"
        ReferencedEntity = $column.targetTable
        ReferencingEntity = $table.logicalName
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

function New-Entity($table) {
    $primary = $table.columns | Where-Object { $_.logicalName -eq $table.primaryNameAttribute } | Select-Object -First 1
    if (-not $primary) {
        throw "La tabla $($table.logicalName) no tiene columna primaria $($table.primaryNameAttribute)."
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.EntityMetadata"
        SchemaName = ConvertTo-SchemaName $table.logicalName
        DisplayName = New-DvLabel $table.displayName
        DisplayCollectionName = New-DvLabel $table.displayCollectionName
        Description = New-DvLabel $table.displayName
        OwnershipType = $table.ownership
        HasActivities = $false
        HasNotes = $false
        IsActivity = $false
        Attributes = @(
            New-AttributePayload $table $primary
        )
    }
    Invoke-DvRequest -Method Post -Path "EntityDefinitions" -Body $payload | Out-Null
}

function Wait-ForEntity([string]$LogicalName) {
    for ($i = 0; $i -lt 20; $i++) {
        if (Test-EntityExists $LogicalName) { return }
        Start-Sleep -Seconds 3
    }
    throw "La tabla $LogicalName no quedo disponible."
}

foreach ($table in $schema.tables) {
    if (-not (Test-EntityExists $table.logicalName)) {
        Write-Host "Creando tabla $($table.logicalName)..."
        New-Entity $table
        Wait-ForEntity $table.logicalName
    } else {
        Write-Host "Tabla existente $($table.logicalName)."
    }
}

foreach ($table in $schema.tables) {
    foreach ($column in $table.columns) {
        if ($column.logicalName -eq $table.primaryNameAttribute) { continue }
        if (Test-AttributeExists $table.logicalName $column.logicalName) {
            continue
        }

        Write-Host "Creando columna $($table.logicalName).$($column.logicalName)..."
        if ($column.type -eq "Lookup") {
            Invoke-DvRequest -Method Post -Path "RelationshipDefinitions" -Body (New-RelationshipPayload $table $column) | Out-Null
        } else {
            $entity = [uri]::EscapeDataString("LogicalName='$($table.logicalName)'")
            Invoke-DvRequest -Method Post -Path "EntityDefinitions($entity)/Attributes" -Body (New-AttributePayload $table $column) | Out-Null
        }
    }
}

if (-not $SkipPublish) {
    Write-Host "Publicando personalizaciones..."
    Invoke-DvRequest -Method Post -Path "PublishAllXml" -Body @{} | Out-Null
}

foreach ($table in $schema.tables) {
    if (-not $table.seed) { continue }
    $entitySet = "$($table.logicalName)s"
    foreach ($row in $table.seed) {
        $primaryValue = $row.($table.primaryNameAttribute)
        $filter = "$($table.primaryNameAttribute) eq '$($primaryValue -replace "'", "''")'"
        $existing = Invoke-DvRequest -Method Get -Path "$entitySet?`$select=$($table.primaryNameAttribute)&`$filter=$([uri]::EscapeDataString($filter))&`$top=1"
        if ($existing.value.Count -gt 0) { continue }
        Write-Host "Sembrando $primaryValue en $($table.logicalName)..."
        Invoke-DvRequest -Method Post -Path $entitySet -Body $row | Out-Null
    }
}

Write-Host "Esquema Aguas SDA listo."
