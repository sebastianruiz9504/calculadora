param(
    [string]$DataverseUrl = "",
    [string]$TenantId = "",
    [string]$ClientId = "",
    [string]$ClientSecret = "",
    [string]$SchemaPath = "$PSScriptRoot\contracts-dataverse-schema.json",
    [string]$ContractTemplatePath = "",
    [string]$ActTemplatePath = "",
    [int]$LanguageCode = 3082,
    [switch]$SkipPublish,
    [switch]$SkipSeed
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

if (-not (Test-Path -LiteralPath $SchemaPath)) {
    throw "No se encontro el esquema de contratos: $SchemaPath"
}

$schema = Get-Content -LiteralPath $SchemaPath -Raw | ConvertFrom-Json
$DataverseUrl = $DataverseUrl.TrimEnd("/")
if ([string]::IsNullOrWhiteSpace($DataverseUrl)) {
    throw "DataverseUrl es obligatorio."
}

function Get-AccessToken {
    if (-not [string]::IsNullOrWhiteSpace($TenantId) -and
        -not [string]::IsNullOrWhiteSpace($ClientId) -and
        -not [string]::IsNullOrWhiteSpace($ClientSecret)) {
        $token = Invoke-RestMethod -Method Post `
            -Uri "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token" `
            -TimeoutSec 60 `
            -ContentType "application/x-www-form-urlencoded" `
            -Body @{
                client_id = $ClientId
                client_secret = $ClientSecret
                scope = "$DataverseUrl/.default"
                grant_type = "client_credentials"
            }
        return $token.access_token
    }

    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        throw "Configura TenantId/ClientId/ClientSecret o inicia sesion con Azure CLI."
    }

    $token = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
    if ([string]::IsNullOrWhiteSpace($token)) {
        throw "No fue posible obtener token para Dataverse."
    }

    return $token
}

$script:AccessToken = Get-AccessToken

function Invoke-DvRequest {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Path,
        [object]$Body = $null,
        [switch]$AllowNotFound
    )

    $uri = if ($Path.StartsWith("http", [System.StringComparison]::OrdinalIgnoreCase)) {
        $Path
    } else {
        "$DataverseUrl/api/data/v9.2/$($Path.TrimStart('/'))"
    }
    $headers = @{
        Authorization = "Bearer $script:AccessToken"
        Accept = "application/json"
        "OData-MaxVersion" = "4.0"
        "OData-Version" = "4.0"
    }

    try {
        if ($null -eq $Body) {
            return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -TimeoutSec 120
        }

        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -ContentType "application/json" -Body ($Body | ConvertTo-Json -Depth 40) -TimeoutSec 120
    }
    catch {
        $response = $_.Exception.Response
        if ($AllowNotFound -and $response -and [int]$response.StatusCode -eq 404) {
            return $null
        }

        if ($response) {
            $reader = $null
            try {
                $reader = New-Object System.IO.StreamReader($response.GetResponseStream())
                $detail = $reader.ReadToEnd()
                if (-not [string]::IsNullOrWhiteSpace($detail)) {
                    Write-Host $detail -ForegroundColor DarkYellow
                }
            } catch { } finally { if ($reader) { $reader.Dispose() } }
        }
        throw
    }
}

function New-DvLabel([string]$Text) {
    return @{
        "@odata.type" = "Microsoft.Dynamics.CRM.Label"
        LocalizedLabels = @(@{
            "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
            Label = $Text
            LanguageCode = $LanguageCode
        })
    }
}

function New-DvRequiredLevel([bool]$Required) {
    return @{
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
        if ($_.Length -le 1) { $_.ToUpperInvariant() }
        else { $_.Substring(0, 1).ToUpperInvariant() + $_.Substring(1) }
    }) -join ""
    return "$prefix`_$name"
}

function New-OptionMetadata([string]$Label, [int]$Value) {
    return @{
        "@odata.type" = "Microsoft.Dynamics.CRM.OptionMetadata"
        Value = $Value
        Label = New-DvLabel $Label
    }
}

function Get-Options([string]$Reference) {
    $property = $schema.optionSets.PSObject.Properties[$Reference]
    if ($null -eq $property) { throw "No existe el option set $Reference en el esquema." }
    return @($property.Value)
}

function New-AttributePayload($table, $column) {
    $base = @{
        SchemaName = ConvertTo-SchemaName $column.logicalName
        DisplayName = New-DvLabel $column.displayName
        Description = New-DvLabel $column.displayName
        RequiredLevel = New-DvRequiredLevel ([bool]$column.required)
    }
    if ($column.logicalName -eq $table.primaryNameAttribute) { $base.IsPrimaryName = $true }

    switch ([string]$column.type) {
        "Text" {
            $base["@odata.type"] = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            $base.MaxLength = [int]$column.maxLength
            $base.FormatName = @{ Value = "Text" }
        }
        "MultilineText" {
            $base["@odata.type"] = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
            $base.MaxLength = [int]$column.maxLength
            $base.FormatName = @{ Value = "TextArea" }
        }
        "Choice" {
            $base["@odata.type"] = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
            $base.OptionSet = @{
                "@odata.type" = "Microsoft.Dynamics.CRM.OptionSetMetadata"
                IsGlobal = $false
                OptionSetType = "Picklist"
                Options = @(Get-Options $column.optionsRef | ForEach-Object { New-OptionMetadata $_.label ([int]$_.value) })
            }
        }
        "WholeNumber" {
            $base["@odata.type"] = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
            $base.Format = "None"
            $base.MinValue = if ($null -ne $column.minValue) { [int]$column.minValue } else { -2147483648 }
            $base.MaxValue = if ($null -ne $column.maxValue) { [int]$column.maxValue } else { 2147483647 }
        }
        "DateOnly" {
            $base["@odata.type"] = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
            $base.Format = "DateOnly"
            $base.DateTimeBehavior = @{ Value = "DateOnly" }
        }
        "DateTime" {
            $base["@odata.type"] = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
            $base.Format = "DateAndTime"
            $base.DateTimeBehavior = @{ Value = "UserLocal" }
        }
        "TwoOptions" {
            $base["@odata.type"] = "Microsoft.Dynamics.CRM.BooleanAttributeMetadata"
            $base.DefaultValue = [bool]$column.defaultValue
            $base.OptionSet = @{
                TrueOption = New-OptionMetadata "Si" 1
                FalseOption = New-OptionMetadata "No" 0
            }
        }
        "File" {
            $base["@odata.type"] = "Microsoft.Dynamics.CRM.FileAttributeMetadata"
            $base.MaxSizeInKB = [int]$column.maxSizeMb * 1024
        }
        default { throw "Tipo de columna no soportado: $($column.type)" }
    }

    return $base
}

function Test-EntityExists([string]$LogicalName) {
    return $null -ne (Invoke-DvRequest -Method Get -Path "EntityDefinitions(LogicalName='$LogicalName')?`$select=LogicalName" -AllowNotFound)
}

function Test-AttributeExists([string]$Entity, [string]$Attribute) {
    return $null -ne (Invoke-DvRequest -Method Get -Path "EntityDefinitions(LogicalName='$Entity')/Attributes(LogicalName='$Attribute')?`$select=LogicalName" -AllowNotFound)
}

function New-Entity($table) {
    $primary = @($table.columns | Where-Object { $_.logicalName -eq $table.primaryNameAttribute })[0]
    if ($null -eq $primary) { throw "Falta la columna primaria de $($table.logicalName)." }
    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.EntityMetadata"
        SchemaName = ConvertTo-SchemaName $table.logicalName
        DisplayName = New-DvLabel $table.displayName
        DisplayCollectionName = New-DvLabel $table.displayCollectionName
        Description = New-DvLabel $table.description
        OwnershipType = $table.ownership
        HasActivities = $false
        HasNotes = $true
        IsActivity = $false
        Attributes = @(New-AttributePayload $table $primary)
    }
    Invoke-DvRequest -Method Post -Path "EntityDefinitions" -Body $payload | Out-Null
}

function Wait-ForEntity([string]$LogicalName) {
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        if (Test-EntityExists $LogicalName) { return }
        Start-Sleep -Seconds 4
    }
    throw "La tabla $LogicalName no estuvo disponible despues de crearla."
}

function New-RelationshipPayload($table, $column) {
    $suffix = $column.logicalName.Replace("cr07a_", "")
    $target = $column.targetTable.Replace("cr07a_", "")
    return @{
        "@odata.type" = "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata"
        SchemaName = ConvertTo-SchemaName "cr07a_$($table.logicalName.Replace('cr07a_', ''))_${suffix}_${target}"
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
            Label = New-DvLabel $table.displayCollectionName
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

function Add-ColumnIfMissing($table, $column) {
    if (Test-AttributeExists $table.logicalName $column.logicalName) {
        Write-Host "    OK $($column.logicalName)"
        return
    }
    if ($column.type -eq "Lookup") {
        Write-Host "    Creando lookup $($column.logicalName) -> $($column.targetTable)"
        Invoke-DvRequest -Method Post -Path "RelationshipDefinitions" -Body (New-RelationshipPayload $table $column) | Out-Null
        return
    }
    Write-Host "    Creando $($column.logicalName) ($($column.type))"
    Invoke-DvRequest -Method Post -Path "EntityDefinitions(LogicalName='$($table.logicalName)')/Attributes" -Body (New-AttributePayload $table $column) | Out-Null
}

function Add-KeyIfMissing($table, $key) {
    $keys = Invoke-DvRequest -Method Get -Path "EntityDefinitions(LogicalName='$($table.logicalName)')/Keys?`$select=SchemaName"
    if (@($keys.value | Where-Object { $_.SchemaName -eq $key.schemaName }).Count -gt 0) {
        Write-Host "    OK key $($key.schemaName)"
        return
    }
    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.EntityKeyMetadata"
        SchemaName = $key.schemaName
        DisplayName = New-DvLabel $key.schemaName
        KeyAttributes = @($key.attributes)
    }
    Write-Host "    Creando key $($key.schemaName)"
    Invoke-DvRequest -Method Post -Path "EntityDefinitions(LogicalName='$($table.logicalName)')/Keys" -Body $payload | Out-Null
}

function Get-ModuleOptionValues {
    foreach ($type in @("MultiSelectPicklistAttributeMetadata", "PicklistAttributeMetadata")) {
        try {
            $path = "EntityDefinitions(LogicalName='$($schema.moduleOption.targetTable)')/Attributes(LogicalName='$($schema.moduleOption.targetField)')/Microsoft.Dynamics.CRM.${type}?`$select=LogicalName&`$expand=OptionSet(`$select=Options)"
            $metadata = Invoke-DvRequest -Method Get -Path $path
            return @($metadata.OptionSet.Options | ForEach-Object { [int]$_.Value })
        } catch { }
    }
    throw "No fue posible leer la matriz de modulos."
}

function Ensure-ModuleOption {
    $value = [int]$schema.moduleOption.value
    if ((Get-ModuleOptionValues) -contains $value) {
        Write-Host "OK opcion de modulo Contratos existente."
        return
    }
    $payload = @{
        EntityLogicalName = $schema.moduleOption.targetTable
        AttributeLogicalName = $schema.moduleOption.targetField
        Value = $value
        Label = New-DvLabel $schema.moduleOption.label
    }
    Invoke-DvRequest -Method Post -Path "InsertOptionValue" -Body $payload | Out-Null
    Write-Host "Opcion de modulo Contratos agregada."
}

function Publish-All {
    for ($attempt = 1; $attempt -le 12; $attempt++) {
        try {
            Invoke-DvRequest -Method Post -Path "PublishAllXml" -Body @{} | Out-Null
            return
        } catch {
            if ($attempt -eq 12) { throw }
            Start-Sleep -Seconds 15
        }
    }
}

function Get-EntityMetadata([string]$LogicalName) {
    return Invoke-DvRequest -Method Get -Path "EntityDefinitions(LogicalName='$LogicalName')?`$select=LogicalName,EntitySetName,PrimaryIdAttribute,PrimaryNameAttribute"
}

function Escape-OData([string]$Text) { return ($Text ?? "").Replace("'", "''") }

function Normalize-SeedText([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) { return "" }
    $normalized = $Text.Normalize([Text.NormalizationForm]::FormD)
    $builder = New-Object Text.StringBuilder
    foreach ($character in $normalized.ToCharArray()) {
        if ([Globalization.CharUnicodeInfo]::GetUnicodeCategory($character) -ne [Globalization.UnicodeCategory]::NonSpacingMark) {
            [void]$builder.Append($character)
        }
    }
    return (($builder.ToString().ToUpperInvariant() -replace '[^A-Z0-9Ñ]+', ' ').Trim() -replace '\s+', ' ')
}

function Seed-Consecutives {
    $meta = Get-EntityMetadata "cr07a_consecutivocontrato"
    $clientMeta = Get-EntityMetadata "cr07a_cliente"
    $clientsResponse = Invoke-DvRequest -Method Get -Path "$($clientMeta.EntitySetName)?`$select=$($clientMeta.PrimaryIdAttribute),cr07a_nombre&`$top=5000"
    $clients = @($clientsResponse.value)
    $rowNumber = 0
    foreach ($row in @($schema.consecutiveSeed)) {
        $rowNumber++
        $code = [string]$row[0]
        $line = [string]$row[1]
        $clientName = [string]$row[2]
        $description = [string]$row[3]
        $actaNumber = $row[4]
        $filter = [uri]::EscapeDataString("cr07a_name eq '$(Escape-OData $code)'")
        $existing = Invoke-DvRequest -Method Get -Path "$($meta.EntitySetName)?`$select=$($meta.PrimaryIdAttribute)&`$filter=$filter&`$top=1"
        if (@($existing.value).Count -gt 0) { continue }

        $year = if ($code -match '(\d{4})$') { [int]$Matches[1] } else { (Get-Date).Year }
        $isUsed = -not [string]::IsNullOrWhiteSpace($clientName)
        $payload = @{
            cr07a_name = $code
            cr07a_anio = $year
            cr07a_lineanegocio = if ([string]::IsNullOrWhiteSpace($line)) { $null } else { $line.Trim() }
            cr07a_estado = if ($isUsed) { 645260002 } else { 645260000 }
            cr07a_clientehistorico = if ($isUsed) { $clientName.Trim() } else { $null }
            cr07a_descripcion = if ([string]::IsNullOrWhiteSpace($description)) { $null } else { $description.Trim() }
            cr07a_filafuente = $rowNumber
            cr07a_usadoen = if ($isUsed) { (Get-Date).ToUniversalTime().ToString("o") } else { $null }
        }
        if ($null -ne $actaNumber) { $payload.cr07a_numeroacta = [int]$actaNumber }

        if ($isUsed) {
            $normalizedClient = Normalize-SeedText $clientName
            $match = $clients | Where-Object { (Normalize-SeedText ([string]$_.cr07a_nombre)) -eq $normalizedClient } | Select-Object -First 1
            if ($match) { $payload["cr07a_cliente@odata.bind"] = "/$($clientMeta.EntitySetName)($($match.($clientMeta.PrimaryIdAttribute)))" }
        }
        Invoke-DvRequest -Method Post -Path $meta.EntitySetName -Body $payload | Out-Null
    }
    Write-Host "Consecutivos importados/verificados: $rowNumber"
}

function Seed-Template {
    $meta = Get-EntityMetadata "cr07a_plantillacontrato"
    $name = [string]$schema.templateSeed.name
    $filter = [uri]::EscapeDataString("cr07a_name eq '$(Escape-OData $name)'")
    $existing = Invoke-DvRequest -Method Get -Path "$($meta.EntitySetName)?`$select=$($meta.PrimaryIdAttribute)&`$filter=$filter&`$top=1"
    if (@($existing.value).Count -gt 0) { return }
    $payload = @{
        cr07a_name = $name
        cr07a_tipocontrato = [int]$schema.templateSeed.type
        cr07a_version = [string]$schema.templateSeed.version
        cr07a_activa = $true
        cr07a_promptversion = [string]$schema.templateSeed.promptVersion
        cr07a_hashcontrato = [string]$schema.templateSeed.contractHash
        cr07a_hashacta = [string]$schema.templateSeed.actaHash
        cr07a_notas = "Plantilla Copiers inicial basada en el contrato marco AQJ001-2026 y el acta de entrega suministrados."
    }
    Invoke-DvRequest -Method Post -Path $meta.EntitySetName -Body $payload | Out-Null
    Write-Host "Plantilla Copiers v1.0 registrada."
}

function Set-DvFile {
    param(
        [Parameter(Mandatory = $true)][string]$EntitySetName,
        [Parameter(Mandatory = $true)][string]$RecordId,
        [Parameter(Mandatory = $true)][string]$FieldName,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string]$TargetFileName
    )
    if (-not (Test-Path -LiteralPath $FilePath -PathType Leaf)) {
        throw "No se encontro el archivo de plantilla: $FilePath"
    }
    $headers = @{
        Authorization = "Bearer $script:AccessToken"
        Accept = "application/json"
        "OData-MaxVersion" = "4.0"
        "OData-Version" = "4.0"
        "x-ms-file-name" = $TargetFileName
    }
    $uri = "$DataverseUrl/api/data/v9.2/$EntitySetName($RecordId)/$FieldName"
    Invoke-WebRequest -Method Patch -Uri $uri -Headers $headers -ContentType "application/octet-stream" -InFile $FilePath -TimeoutSec 120 | Out-Null
}

function Upload-TemplateFiles {
    if ([string]::IsNullOrWhiteSpace($ContractTemplatePath) -and [string]::IsNullOrWhiteSpace($ActTemplatePath)) { return }
    $meta = Get-EntityMetadata "cr07a_plantillacontrato"
    $name = [string]$schema.templateSeed.name
    $filter = [uri]::EscapeDataString("cr07a_name eq '$(Escape-OData $name)'")
    $row = @((Invoke-DvRequest -Method Get -Path "$($meta.EntitySetName)?`$select=$($meta.PrimaryIdAttribute),cr07a_archivocontrato_name,cr07a_archivoacta_name&`$filter=$filter&`$top=1").value)[0]
    if ($null -eq $row) { throw "No se encontro la plantilla Copiers para cargar archivos." }
    $recordId = $row.($meta.PrimaryIdAttribute)
    if (-not [string]::IsNullOrWhiteSpace($ContractTemplatePath) -and [string]::IsNullOrWhiteSpace([string]$row.cr07a_archivocontrato_name)) {
        Set-DvFile $meta.EntitySetName $recordId "cr07a_archivocontrato" $ContractTemplatePath "plantilla-contrato-copiers.pdf"
        Write-Host "Archivo fuente del contrato cargado en la plantilla."
    }
    if (-not [string]::IsNullOrWhiteSpace($ActTemplatePath) -and [string]::IsNullOrWhiteSpace([string]$row.cr07a_archivoacta_name)) {
        Set-DvFile $meta.EntitySetName $recordId "cr07a_archivoacta" $ActTemplatePath "plantilla-acta-entrega-copiers.docx"
        Write-Host "Archivo fuente del acta cargado en la plantilla."
    }
}

function Add-InitialUserPermissions {
    $employeeMeta = Get-EntityMetadata $schema.moduleOption.targetTable
    $nameField = "cr07a_nombrecompleto"
    $response = Invoke-DvRequest -Method Get -Path "$($employeeMeta.EntitySetName)?`$select=$($employeeMeta.PrimaryIdAttribute),$nameField,$($schema.moduleOption.targetField)&`$top=5000"
    foreach ($requestedName in @($schema.initialUsers)) {
        $normalizedRequested = Normalize-SeedText ([string]$requestedName)
        $employee = @($response.value | Where-Object { (Normalize-SeedText ([string]$_.($nameField))) -eq $normalizedRequested })[0]
        if ($null -eq $employee) {
            Write-Warning "No se encontro empleado para asignar Contratos: $requestedName"
            continue
        }
        $values = @()
        $raw = $employee.($schema.moduleOption.targetField)
        if ($null -ne $raw) {
            $values = @(([string]$raw).Split(',', [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { [int]$_.Trim() })
        }
        $values = @($values + [int]$schema.moduleOption.value | Sort-Object -Unique)
        $payload = @{ $schema.moduleOption.targetField = ($values -join ',') }
        $id = $employee.($employeeMeta.PrimaryIdAttribute)
        Invoke-DvRequest -Method Patch -Path "$($employeeMeta.EntitySetName)($id)" -Body $payload | Out-Null
        Write-Host "Permiso Contratos asignado a $requestedName."
    }
}

Write-Host "Dataverse: $DataverseUrl" -ForegroundColor Cyan
foreach ($table in @($schema.tables)) {
    Write-Host "Tabla $($table.logicalName)" -ForegroundColor Cyan
    if (-not (Test-EntityExists $table.logicalName)) {
        New-Entity $table
        Wait-ForEntity $table.logicalName
    }
    foreach ($column in @($table.columns | Where-Object { $_.logicalName -ne $table.primaryNameAttribute })) {
        Add-ColumnIfMissing $table $column
    }
}

foreach ($table in @($schema.tables)) {
    foreach ($key in @($table.keys)) { Add-KeyIfMissing $table $key }
}

Ensure-ModuleOption
if (-not $SkipPublish) {
    Write-Host "Publicando personalizaciones..." -ForegroundColor Cyan
    Publish-All
}

if (-not $SkipSeed) {
    Seed-Consecutives
    Seed-Template
    Upload-TemplateFiles
    Add-InitialUserPermissions
}

Write-Host "Provision del modulo Contratos finalizada." -ForegroundColor Green
