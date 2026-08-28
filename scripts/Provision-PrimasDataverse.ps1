param(
    [string]$DataverseUrl = "https://orgc79ca19c.crm2.dynamics.com",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$LanguageCode = 3082
$EntityLogicalName = "cr07a_prima"
$EntitySchemaName = "cr07a_Prima"

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw "Azure CLI no esta disponible. Ejecuta az login antes de aprovisionar Dataverse."
}

$DataverseUrl = $DataverseUrl.TrimEnd("/")
$script:AccessToken = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
if ([string]::IsNullOrWhiteSpace($script:AccessToken)) {
    throw "No fue posible obtener token para $DataverseUrl."
}

function New-Label([string]$Text) {
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

function New-Value([string]$Value) {
    return @{ Value = $Value }
}

function New-RequiredNone {
    return @{
        Value = "None"
        CanBeChanged = $true
        ManagedPropertyLogicalName = "canmodifyrequirementlevelsettings"
    }
}

function Invoke-Dataverse {
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
            return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers
        }

        $json = $Body | ConvertTo-Json -Depth 50
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
    $result = Invoke-Dataverse -Method Get -Path "EntityDefinitions(LogicalName='$LogicalName')?`$select=LogicalName" -AllowNotFound
    return $null -ne $result
}

function Test-AttributeExists([string]$LogicalName) {
    $result = Invoke-Dataverse -Method Get -Path "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$LogicalName')?`$select=LogicalName" -AllowNotFound
    return $null -ne $result
}

function New-Entity {
    if (Test-EntityExists $EntityLogicalName) {
        Write-Host "OK tabla existente: $EntityLogicalName"
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.EntityMetadata"
        Attributes = @(
            @{
                "@odata.type" = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
                AttributeType = "String"
                AttributeTypeName = New-Value "StringType"
                Description = New-Label "Nombre de la liquidacion de prima."
                DisplayName = New-Label "Nombre"
                IsPrimaryName = $true
                RequiredLevel = New-RequiredNone
                SchemaName = "cr07a_name"
                FormatName = New-Value "Text"
                MaxLength = 200
            }
        )
        Description = New-Label "Liquidaciones semestrales de prima legal guardadas desde Cotizador Interno."
        DisplayCollectionName = New-Label "Primas"
        DisplayName = New-Label "Prima"
        HasActivities = $false
        HasNotes = $false
        IsActivity = $false
        OwnershipType = "UserOwned"
        SchemaName = $EntitySchemaName
    }

    Invoke-Dataverse -Method Post -Path "EntityDefinitions" -Body $payload | Out-Null
    Write-Host "Creada tabla: $EntityLogicalName"
}

function New-StringAttribute([string]$SchemaName, [string]$Label, [int]$MaxLength = 200) {
    $logicalName = $SchemaName.ToLowerInvariant()
    if (Test-AttributeExists $logicalName) {
        Write-Host "OK columna existente: $logicalName"
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        AttributeType = "String"
        AttributeTypeName = New-Value "StringType"
        SchemaName = $SchemaName
        DisplayName = New-Label $Label
        Description = New-Label $Label
        RequiredLevel = New-RequiredNone
        MaxLength = $MaxLength
        FormatName = New-Value "Text"
    }
    Invoke-Dataverse -Method Post -Path "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes" -Body $payload | Out-Null
    Write-Host "Creada columna: $logicalName"
}

function New-MemoAttribute([string]$SchemaName, [string]$Label, [int]$MaxLength = 100000) {
    $logicalName = $SchemaName.ToLowerInvariant()
    if (Test-AttributeExists $logicalName) {
        Write-Host "OK columna existente: $logicalName"
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
        AttributeType = "Memo"
        AttributeTypeName = New-Value "MemoType"
        SchemaName = $SchemaName
        DisplayName = New-Label $Label
        Description = New-Label $Label
        RequiredLevel = New-RequiredNone
        Format = "TextArea"
        ImeMode = "Disabled"
        IsLocalizable = $false
        MaxLength = $MaxLength
    }
    Invoke-Dataverse -Method Post -Path "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes" -Body $payload | Out-Null
    Write-Host "Creada columna: $logicalName"
}

function New-IntegerAttribute([string]$SchemaName, [string]$Label, [int]$MinValue = 0, [int]$MaxValue = 1000000) {
    $logicalName = $SchemaName.ToLowerInvariant()
    if (Test-AttributeExists $logicalName) {
        Write-Host "OK columna existente: $logicalName"
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
        AttributeType = "Integer"
        AttributeTypeName = New-Value "IntegerType"
        SchemaName = $SchemaName
        DisplayName = New-Label $Label
        Description = New-Label $Label
        RequiredLevel = New-RequiredNone
        Format = "None"
        MinValue = $MinValue
        MaxValue = $MaxValue
    }
    Invoke-Dataverse -Method Post -Path "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes" -Body $payload | Out-Null
    Write-Host "Creada columna: $logicalName"
}

function New-DecimalAttribute([string]$SchemaName, [string]$Label, [decimal]$MinValue = 0, [decimal]$MaxValue = 1000000000, [int]$Precision = 2) {
    $logicalName = $SchemaName.ToLowerInvariant()
    if (Test-AttributeExists $logicalName) {
        Write-Host "OK columna existente: $logicalName"
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.DecimalAttributeMetadata"
        AttributeType = "Decimal"
        AttributeTypeName = New-Value "DecimalType"
        SchemaName = $SchemaName
        DisplayName = New-Label $Label
        Description = New-Label $Label
        RequiredLevel = New-RequiredNone
        MinValue = $MinValue
        MaxValue = $MaxValue
        Precision = $Precision
    }
    Invoke-Dataverse -Method Post -Path "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes" -Body $payload | Out-Null
    Write-Host "Creada columna: $logicalName"
}

function New-MoneyAttribute([string]$SchemaName, [string]$Label) {
    $logicalName = $SchemaName.ToLowerInvariant()
    if (Test-AttributeExists $logicalName) {
        Write-Host "OK columna existente: $logicalName"
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.MoneyAttributeMetadata"
        AttributeType = "Money"
        AttributeTypeName = New-Value "MoneyType"
        SchemaName = $SchemaName
        DisplayName = New-Label $Label
        Description = New-Label $Label
        RequiredLevel = New-RequiredNone
        ImeMode = "Disabled"
        MinValue = 0
        MaxValue = 100000000000
        PrecisionSource = 2
    }
    Invoke-Dataverse -Method Post -Path "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes" -Body $payload | Out-Null
    Write-Host "Creada columna: $logicalName"
}

function New-DateAttribute([string]$SchemaName, [string]$Label) {
    $logicalName = $SchemaName.ToLowerInvariant()
    if (Test-AttributeExists $logicalName) {
        Write-Host "OK columna existente: $logicalName"
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
        AttributeType = "DateTime"
        AttributeTypeName = New-Value "DateTimeType"
        SchemaName = $SchemaName
        DisplayName = New-Label $Label
        Description = New-Label $Label
        RequiredLevel = New-RequiredNone
        DateTimeBehavior = New-Value "DateOnly"
        Format = "DateOnly"
        ImeMode = "Disabled"
    }
    Invoke-Dataverse -Method Post -Path "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes" -Body $payload | Out-Null
    Write-Host "Creada columna: $logicalName"
}

function New-LookupAttribute([string]$SchemaName, [string]$Label, [string]$Target) {
    $logicalName = $SchemaName.ToLowerInvariant()
    if (Test-AttributeExists $logicalName) {
        Write-Host "OK columna existente: $logicalName"
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata"
        SchemaName = "cr07a_empleado_cr07a_prima"
        ReferencedEntity = $Target
        ReferencingEntity = $EntityLogicalName
        AssociatedMenuConfiguration = @{
            Behavior = "UseLabel"
            Group = "Details"
            Label = New-Label "Primas"
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
        Lookup = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.LookupAttributeMetadata"
            AttributeType = "Lookup"
            AttributeTypeName = New-Value "LookupType"
            SchemaName = $SchemaName
            DisplayName = New-Label $Label
            Description = New-Label $Label
            RequiredLevel = New-RequiredNone
        }
    }
    Invoke-Dataverse -Method Post -Path "RelationshipDefinitions" -Body $payload | Out-Null
    Write-Host "Creada columna lookup: $logicalName"
}

New-Entity
New-LookupAttribute "cr07a_Empleado" "Empleado" "cr07a_empleado"
New-IntegerAttribute "cr07a_Anio" "Anio" 2000 2100
New-IntegerAttribute "cr07a_Semestre" "Semestre" 1 2
New-DateAttribute "cr07a_PeriodoInicio" "Periodo inicio"
New-DateAttribute "cr07a_PeriodoFin" "Periodo fin"
New-DateAttribute "cr07a_FechaLiquidacion" "Fecha liquidacion"
New-StringAttribute "cr07a_NombreEmpleado" "Nombre empleado" 200
New-StringAttribute "cr07a_DocumentoEmpleado" "Documento empleado" 80
New-StringAttribute "cr07a_TipoContrato" "Tipo contrato" 100
New-IntegerAttribute "cr07a_MesesCargados" "Meses cargados" 0 6
New-DecimalAttribute "cr07a_DiasBase" "Dias base" 0 180 2
New-MoneyAttribute "cr07a_BasePromedio" "Base promedio"
New-MoneyAttribute "cr07a_PrimaAPagar" "Prima a pagar"
New-DecimalAttribute "cr07a_PorcentajeCloud" "Porcentaje Cloud" 0 100 2
New-DecimalAttribute "cr07a_PorcentajeCopiers" "Porcentaje Copiers" 0 100 2
New-MoneyAttribute "cr07a_ValorCloud" "Valor Cloud"
New-MoneyAttribute "cr07a_ValorCopiers" "Valor Copiers"
New-MemoAttribute "cr07a_DetalleJson" "Detalle JSON" 100000

if (-not $SkipPublish) {
    Invoke-Dataverse -Method Post -Path "PublishXml" -Body @{
        ParameterXml = "<importexportxml><entities><entity>$EntityLogicalName</entity></entities><nodes/><securityroles/><settings/><workflows/></importexportxml>"
    } | Out-Null
}

$metadata = Invoke-Dataverse -Method Get -Path "EntityDefinitions(LogicalName='$EntityLogicalName')?`$select=LogicalName,EntitySetName"
Write-Host "Tabla Primas lista: $($metadata.LogicalName) / $($metadata.EntitySetName)" -ForegroundColor Green
