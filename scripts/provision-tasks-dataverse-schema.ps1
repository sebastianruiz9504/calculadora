param(
    [string]$SchemaPath = "scripts/tasks-dataverse-schema.json",
    [string]$BaseUrl = "",
    [string]$TenantId = "",
    [string]$ClientId = "",
    [string]$ClientSecret = "",
    [switch]$SkipPacPublish
)

$ErrorActionPreference = "Stop"

function Get-JsonConfigValue {
    param([object]$Root, [string]$Path)
    $current = $Root
    foreach ($part in $Path.Split(":")) {
        if ($null -eq $current) { return "" }
        $property = $current.PSObject.Properties[$part]
        if ($null -eq $property) { return "" }
        $current = $property.Value
    }
    if ($null -eq $current) { return "" }
    return [string]$current
}

function Get-UserSecretMap {
    $map = @{}
    $lines = & dotnet user-secrets list 2>$null
    foreach ($line in $lines) {
        $idx = $line.IndexOf(" = ")
        if ($idx -le 0) { continue }
        $key = $line.Substring(0, $idx).Trim()
        $value = $line.Substring($idx + 3).Trim()
        if ($key) { $map[$key] = $value }
    }
    return $map
}

function First-NonEmpty {
    foreach ($value in $args) {
        if (![string]::IsNullOrWhiteSpace([string]$value)) { return [string]$value }
    }
    return ""
}

function New-Label {
    param([string]$Text)
    return @{
        "@odata.type" = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(
            @{
                "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                "Label" = $Text
                "LanguageCode" = 3082
            }
        )
    }
}

function New-Value {
    param([string]$Value)
    return @{ "Value" = $Value }
}

function New-RequiredLevel {
    param([bool]$Required)
    return @{
        "Value" = $(if ($Required) { "ApplicationRequired" } else { "None" })
        "CanBeChanged" = $true
        "ManagedPropertyLogicalName" = "canmodifyrequirementlevelsettings"
    }
}

function Convert-ToSchemaName {
    param([string]$LogicalName)
    $prefix, $rest = $LogicalName.Split("_", 2)
    $textInfo = [System.Globalization.CultureInfo]::InvariantCulture.TextInfo
    $schemaRest = ($rest -split "_" | ForEach-Object { $textInfo.ToTitleCase($_.ToLowerInvariant()) }) -join ""
    return "$prefix`_$schemaRest"
}

function Invoke-Dataverse {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body = $null,
        [switch]$AllowNotFound
    )

    $uri = if ($Path.StartsWith("http")) { $Path } else { "$script:BaseUrl$Path" }
    $headers = @{
        "Authorization" = "Bearer $script:AccessToken"
        "Accept" = "application/json"
        "OData-MaxVersion" = "4.0"
        "OData-Version" = "4.0"
    }
    if ($null -eq $Body) {
        try {
            return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers
        } catch {
            if ($AllowNotFound -and $_.Exception.Response.StatusCode.value__ -eq 404) { return $null }
            throw
        }
    }

    $json = $Body | ConvertTo-Json -Depth 40
    try {
        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -ContentType "application/json" -Body $json
    } catch {
        if ($AllowNotFound -and $_.Exception.Response.StatusCode.value__ -eq 404) { return $null }
        throw
    }
}

function New-AttributePayload {
    param([object]$Column, [object]$Schema)

    $required = [bool]($Column.required -eq $true)
    $schemaName = Convert-ToSchemaName $Column.logicalName
    $common = @{
        "Description" = New-Label $Column.displayName
        "DisplayName" = New-Label $Column.displayName
        "RequiredLevel" = New-RequiredLevel $required
        "SchemaName" = $schemaName
    }

    switch ($Column.type) {
        "Text" {
            return $common + @{
                "@odata.type" = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
                "AttributeType" = "String"
                "AttributeTypeName" = New-Value "StringType"
                "FormatName" = New-Value "Text"
                "MaxLength" = [int]$Column.maxLength
            }
        }
        "MultilineText" {
            return $common + @{
                "@odata.type" = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
                "AttributeType" = "Memo"
                "AttributeTypeName" = New-Value "MemoType"
                "Format" = "TextArea"
                "ImeMode" = "Disabled"
                "IsLocalizable" = $false
                "MaxLength" = [int]$Column.maxLength
            }
        }
        "Integer" {
            return $common + @{
                "@odata.type" = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
                "AttributeType" = "Integer"
                "AttributeTypeName" = New-Value "IntegerType"
                "Format" = "None"
                "MinValue" = -2147483648
                "MaxValue" = 2147483647
                "SourceTypeMask" = 0
            }
        }
        "DateTime" {
            return $common + @{
                "@odata.type" = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
                "AttributeType" = "DateTime"
                "AttributeTypeName" = New-Value "DateTimeType"
                "Format" = $(if ($Column.format) { [string]$Column.format } else { "DateOnly" })
            }
        }
        "TwoOptions" {
            return $common + @{
                "@odata.type" = "Microsoft.Dynamics.CRM.BooleanAttributeMetadata"
                "AttributeType" = "Boolean"
                "AttributeTypeName" = New-Value "BooleanType"
                "DefaultValue" = [bool]($Column.defaultValue -eq $true)
                "OptionSet" = @{
                    "TrueOption" = @{ "Value" = 1; "Label" = New-Label "Si" }
                    "FalseOption" = @{ "Value" = 0; "Label" = New-Label "No" }
                    "OptionSetType" = "Boolean"
                }
            }
        }
        "Choice" {
            $options = $Schema.statusOptions | ForEach-Object {
                @{
                    "Value" = [int]$_.value
                    "Label" = New-Label $_.label
                }
            }
            return $common + @{
                "@odata.type" = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
                "AttributeType" = "Picklist"
                "AttributeTypeName" = New-Value "PicklistType"
                "OptionSet" = @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.OptionSetMetadata"
                    "IsGlobal" = $false
                    "OptionSetType" = "Picklist"
                    "Options" = @($options)
                }
            }
        }
        "Lookup" {
            return $common + @{
                "@odata.type" = "Microsoft.Dynamics.CRM.LookupAttributeMetadata"
                "AttributeType" = "Lookup"
                "AttributeTypeName" = New-Value "LookupType"
                "Targets" = @([string]$Column.targetTable)
            }
        }
        "File" {
            return $common + @{
                "@odata.type" = "Microsoft.Dynamics.CRM.FileAttributeMetadata"
                "AttributeTypeName" = New-Value "FileType"
                "MaxSizeInKB" = ([int]$Column.maxSizeMb * 1024)
            }
        }
        default {
            throw "Unsupported column type '$($Column.type)' for $($Column.logicalName)."
        }
    }
}

$schema = Get-Content -LiteralPath $SchemaPath -Raw | ConvertFrom-Json
$appsettings = Get-Content -LiteralPath "appsettings.json" -Raw | ConvertFrom-Json
$secrets = Get-UserSecretMap

$BaseUrl = First-NonEmpty $BaseUrl $env:DATAVERSE_BASE_URL $secrets["Dataverse:BaseUrl"] (Get-JsonConfigValue $appsettings "Dataverse:BaseUrl")
$TenantId = First-NonEmpty $TenantId $env:DATAVERSE_TENANT_ID $secrets["Dataverse:TenantId"] (Get-JsonConfigValue $appsettings "Dataverse:TenantId") (Get-JsonConfigValue $appsettings "AzureAd:TenantId")
$ClientId = First-NonEmpty $ClientId $env:DATAVERSE_CLIENT_ID $secrets["Dataverse:ClientId"] (Get-JsonConfigValue $appsettings "Dataverse:ClientId") (Get-JsonConfigValue $appsettings "AzureAd:ClientId")
$ClientSecret = First-NonEmpty $ClientSecret $env:DATAVERSE_CLIENT_SECRET $secrets["Dataverse:ClientSecret"] (Get-JsonConfigValue $appsettings "Dataverse:ClientSecret")

if ([string]::IsNullOrWhiteSpace($BaseUrl) -or [string]::IsNullOrWhiteSpace($TenantId) -or [string]::IsNullOrWhiteSpace($ClientId) -or [string]::IsNullOrWhiteSpace($ClientSecret)) {
    throw "Missing Dataverse credentials. Provide BaseUrl, TenantId, ClientId and ClientSecret parameters or user-secrets."
}

$script:BaseUrl = $BaseUrl.TrimEnd("/")
$tokenResponse = Invoke-RestMethod -Method Post -Uri "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token" -ContentType "application/x-www-form-urlencoded" -Body @{
    client_id = $ClientId
    client_secret = $ClientSecret
    scope = "$script:BaseUrl/.default"
    grant_type = "client_credentials"
}
$script:AccessToken = $tokenResponse.access_token

$logicalName = [string]$schema.table.logicalName
$entity = Invoke-Dataverse -Method GET -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$logicalName')?`$select=LogicalName" -AllowNotFound
if ($null -eq $entity) {
    $primaryColumn = $schema.columns | Where-Object { $_.logicalName -eq $schema.table.primaryNameAttribute } | Select-Object -First 1
    $tablePayload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.EntityMetadata"
        "Attributes" = @(
            (New-AttributePayload $primaryColumn $schema) + @{
                "IsPrimaryName" = $true
            }
        )
        "Description" = New-Label $schema.table.displayName
        "DisplayCollectionName" = New-Label $schema.table.displayCollectionName
        "DisplayName" = New-Label $schema.table.displayName
        "HasActivities" = $false
        "HasNotes" = $false
        "IsActivity" = $false
        "OwnershipType" = $schema.table.ownership
        "SchemaName" = Convert-ToSchemaName $schema.table.logicalName
    }
    Invoke-Dataverse -Method POST -Path "/api/data/v9.2/EntityDefinitions" -Body $tablePayload | Out-Null
    Write-Host "Created table $logicalName"
} else {
    Write-Host "Table $logicalName already exists"
}

foreach ($column in $schema.columns) {
    if ($column.logicalName -eq $schema.table.primaryNameAttribute) { continue }
    $path = "/api/data/v9.2/EntityDefinitions(LogicalName='$logicalName')/Attributes(LogicalName='$($column.logicalName)')?`$select=LogicalName"
    $attribute = Invoke-Dataverse -Method GET -Path $path -AllowNotFound
    if ($null -ne $attribute) {
        Write-Host "Column $($column.logicalName) already exists"
        if ($column.type -eq "MultilineText" -and $column.maxLength) {
            $memoFilter = [Uri]::EscapeDataString("LogicalName eq '$($column.logicalName)'")
            $memoMetadata = Invoke-Dataverse -Method GET -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$logicalName')/Attributes/Microsoft.Dynamics.CRM.MemoAttributeMetadata?`$select=LogicalName,MaxLength&`$filter=$memoFilter" -AllowNotFound
            $currentMaxLength = if ($memoMetadata.value.Count -gt 0) { [int]$memoMetadata.value[0].MaxLength } else { 0 }
            if ([int]$column.maxLength -le $currentMaxLength) { continue }

            Invoke-Dataverse -Method PATCH -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$logicalName')/Attributes/Microsoft.Dynamics.CRM.MemoAttributeMetadata(LogicalName='$($column.logicalName)')" -Body @{
                "@odata.type" = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
                "MaxLength" = [int]$column.maxLength
            } | Out-Null
            Write-Host "Updated max length for $($column.logicalName) to $($column.maxLength)"
        }
        continue
    }

    $payload = New-AttributePayload $column $schema
    Invoke-Dataverse -Method POST -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$logicalName')/Attributes" -Body $payload | Out-Null
    Write-Host "Created column $($column.logicalName)"
}

foreach ($key in $schema.alternateKeys) {
    $path = "/api/data/v9.2/EntityDefinitions(LogicalName='$logicalName')/Keys(LogicalName='$($key.logicalName)')?`$select=LogicalName"
    $existingKey = Invoke-Dataverse -Method GET -Path $path -AllowNotFound
    if ($null -ne $existingKey) {
        Write-Host "Alternate key $($key.logicalName) already exists"
        continue
    }

    Invoke-Dataverse -Method POST -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$logicalName')/Keys" -Body @{
        "SchemaName" = $key.logicalName
        "DisplayName" = New-Label $key.displayName
        "KeyAttributes" = @($key.columns)
    } | Out-Null
    Write-Host "Created alternate key $($key.logicalName)"
}

Invoke-Dataverse -Method POST -Path "/api/data/v9.2/PublishXml" -Body @{
    "ParameterXml" = "<importexportxml><entities><entity>$logicalName</entity></entities></importexportxml>"
} | Out-Null

if (-not $SkipPacPublish) {
    pac solution publish
}

Write-Host "Tasks Dataverse schema provisioned."
