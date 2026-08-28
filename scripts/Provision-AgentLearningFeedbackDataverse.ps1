param(
    [string]$BaseUrl = "https://orgc79ca19c.crm2.dynamics.com"
)

$ErrorActionPreference = "Stop"

function New-DvLabel {
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

function New-DvValue {
    param([string]$Value)
    return @{ "Value" = $Value }
}

function New-DvRequiredNone {
    return @{
        "Value" = "None"
        "CanBeChanged" = $true
        "ManagedPropertyLogicalName" = "canmodifyrequirementlevelsettings"
    }
}

function Invoke-Dv {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body = $null,
        [switch]$AllowNotFound
    )

    $uri = if ($Path.StartsWith("http", [System.StringComparison]::OrdinalIgnoreCase)) { $Path } else { "$script:BaseUrl$Path" }
    $headers = @{
        "Authorization" = "Bearer $script:AccessToken"
        "Accept" = "application/json"
        "OData-MaxVersion" = "4.0"
        "OData-Version" = "4.0"
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

function Test-DvEntity {
    param([string]$LogicalName)
    $result = Invoke-Dv -Method "GET" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$LogicalName')?`$select=LogicalName" -AllowNotFound
    return $null -ne $result
}

function Test-DvAttribute {
    param([string]$Entity, [string]$LogicalName)
    $result = Invoke-Dv -Method "GET" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$Entity')/Attributes(LogicalName='$LogicalName')?`$select=LogicalName" -AllowNotFound
    return $null -ne $result
}

function Wait-DvEntity {
    param([string]$LogicalName)
    for ($i = 0; $i -lt 40; $i++) {
        if (Test-DvEntity $LogicalName) { return }
        Start-Sleep -Seconds 3
    }
    throw "La tabla $LogicalName no estuvo disponible despues de crearla."
}

function New-DvPrimaryName {
    return @{
        "@odata.type" = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "AttributeType" = "String"
        "AttributeTypeName" = New-DvValue "StringType"
        "SchemaName" = "cr07a_Name"
        "DisplayName" = New-DvLabel "Nombre"
        "Description" = New-DvLabel "Nombre"
        "IsPrimaryName" = $true
        "RequiredLevel" = New-DvRequiredNone
        "MaxLength" = 200
        "FormatName" = New-DvValue "Text"
    }
}

function Ensure-DvTable {
    param([string]$SchemaName, [string]$DisplayName, [string]$CollectionName)
    $logicalName = $SchemaName.ToLowerInvariant()
    if (Test-DvEntity $logicalName) {
        Write-Host "OK tabla existente: $logicalName"
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.EntityMetadata"
        "Attributes" = @(New-DvPrimaryName)
        "SchemaName" = $SchemaName
        "DisplayName" = New-DvLabel $DisplayName
        "DisplayCollectionName" = New-DvLabel $CollectionName
        "Description" = New-DvLabel $DisplayName
        "OwnershipType" = "UserOwned"
        "IsActivity" = $false
        "HasActivities" = $false
        "HasNotes" = $true
    }

    Invoke-Dv -Method "POST" -Path "/api/data/v9.2/EntityDefinitions" -Body $payload | Out-Null
    Write-Host "Creada tabla: $logicalName"
    Wait-DvEntity $logicalName
}

function Ensure-DvString {
    param([string]$Entity, [string]$SchemaName, [string]$Label, [int]$MaxLength = 200)
    $logicalName = $SchemaName.ToLowerInvariant()
    if (Test-DvAttribute $Entity $logicalName) {
        Write-Host "  OK columna existente: $Entity.$logicalName"
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "AttributeType" = "String"
        "AttributeTypeName" = New-DvValue "StringType"
        "SchemaName" = $SchemaName
        "DisplayName" = New-DvLabel $Label
        "Description" = New-DvLabel $Label
        "RequiredLevel" = New-DvRequiredNone
        "MaxLength" = $MaxLength
        "FormatName" = New-DvValue "Text"
    }
    Invoke-Dv -Method "POST" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$Entity')/Attributes" -Body $payload | Out-Null
    Write-Host "  Creada columna: $Entity.$logicalName"
}

function Ensure-DvMemo {
    param([string]$Entity, [string]$SchemaName, [string]$Label, [int]$MaxLength = 4000)
    $logicalName = $SchemaName.ToLowerInvariant()
    if (Test-DvAttribute $Entity $logicalName) {
        Write-Host "  OK columna existente: $Entity.$logicalName"
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
        "AttributeType" = "Memo"
        "AttributeTypeName" = New-DvValue "MemoType"
        "SchemaName" = $SchemaName
        "DisplayName" = New-DvLabel $Label
        "Description" = New-DvLabel $Label
        "RequiredLevel" = New-DvRequiredNone
        "Format" = "TextArea"
        "ImeMode" = "Disabled"
        "IsLocalizable" = $false
        "MaxLength" = $MaxLength
    }
    Invoke-Dv -Method "POST" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$Entity')/Attributes" -Body $payload | Out-Null
    Write-Host "  Creada columna: $Entity.$logicalName"
}

$script:BaseUrl = $BaseUrl.TrimEnd("/")
$script:AccessToken = az account get-access-token --resource $script:BaseUrl --query accessToken -o tsv
if ([string]::IsNullOrWhiteSpace($script:AccessToken)) {
    throw "No fue posible obtener token de Dataverse con Azure CLI."
}

$entity = "cr07a_agentlearningfeedback"
Ensure-DvTable "cr07a_AgentLearningFeedback" "Agent learning feedback" "Agent learning feedback"

Ensure-DvMemo $entity "cr07a_Question" "Pregunta" 4000
Ensure-DvMemo $entity "cr07a_Answer" "Respuesta" 4000
Ensure-DvString $entity "cr07a_Category" "Categoria" 100
Ensure-DvMemo $entity "cr07a_ExpectedAnswer" "Respuesta esperada" 4000
Ensure-DvMemo $entity "cr07a_Notes" "Notas" 4000
Ensure-DvString $entity "cr07a_Status" "Estado" 100
Ensure-DvMemo $entity "cr07a_ReviewNotes" "Notas de revision" 4000
Ensure-DvMemo $entity "cr07a_SourcesJson" "Fuentes JSON" 100000
Ensure-DvMemo $entity "cr07a_ContextJson" "Contexto JSON" 100000
Ensure-DvString $entity "cr07a_CreatedByName" "Creado por nombre" 240
Ensure-DvString $entity "cr07a_CreatedByEmail" "Creado por email" 240
Ensure-DvString $entity "cr07a_CreatedById" "Creado por id" 80

$metadata = Invoke-Dv -Method "GET" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$entity')?`$select=LogicalName,EntitySetName,PrimaryIdAttribute,PrimaryNameAttribute"
$metadata | ConvertTo-Json -Depth 5
