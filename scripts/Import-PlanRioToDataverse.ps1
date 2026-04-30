param(
    [string]$DataverseUrl = "https://orgc79ca19c.crm2.dynamics.com",
    [string]$WorkbookPath = "App_Data/plan-rio.xlsx",
    [string]$PreferredSheetName = "plan corregido",
    [string[]]$FallbackSheetNames = @("Plan Rio ajustado", "Plan diario"),
    [switch]$KeepExistingRows
)

$ErrorActionPreference = "Stop"

$TableLogicalName = "cr07a_planrioentreno"
$TableSchemaName = "cr07a_PlanRioEntreno"
$TableFallbackSetName = "cr07a_planrioentrenos"
$TableFallbackIdField = "cr07a_planrioentrenoid"
$PrimaryNameField = "cr07a_name"
$LanguageCode = 3082

function New-Label([string]$Text) {
    @{
        "@odata.type" = "Microsoft.Dynamics.CRM.Label"
        LocalizedLabels = @(
            @{
                "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                Label = $Text
                LanguageCode = $LanguageCode
                IsManaged = $false
            }
        )
    }
}

function New-RequiredLevel {
    @{
        Value = "None"
        CanBeChanged = $true
        ManagedPropertyLogicalName = "canmodifyrequirementlevelsettings"
    }
}

function ConvertTo-BodyJson($Body) {
    if ($null -eq $Body) { return $null }
    return $Body | ConvertTo-Json -Depth 60
}

function Invoke-Dataverse {
    param(
        [string]$Method,
        [string]$RelativePath,
        $Body = $null,
        [hashtable]$ExtraHeaders = @{},
        [switch]$AllowNotFound
    )

    $uri = if ($RelativePath.StartsWith("http", [System.StringComparison]::OrdinalIgnoreCase)) {
        $RelativePath
    } else {
        "$DataverseUrl$RelativePath"
    }

    $headers = @{
        Authorization = "Bearer $script:AccessToken"
        Accept = "application/json"
        "OData-MaxVersion" = "4.0"
        "OData-Version" = "4.0"
    }
    foreach ($key in $ExtraHeaders.Keys) {
        $headers[$key] = $ExtraHeaders[$key]
    }

    $jsonBody = ConvertTo-BodyJson $Body
    for ($attempt = 1; $attempt -le 8; $attempt++) {
        try {
            if ($null -eq $jsonBody) {
                return Invoke-WebRequest -Uri $uri -Method $Method -Headers $headers -UseBasicParsing
            }

            return Invoke-WebRequest -Uri $uri -Method $Method -Headers $headers -ContentType "application/json; charset=utf-8" -Body $jsonBody -UseBasicParsing
        }
        catch {
            $response = $_.Exception.Response
            if ($AllowNotFound -and $response -and [int]$response.StatusCode -eq 404) {
                return $null
            }

            $statusCode = if ($response) { [int]$response.StatusCode } else { 0 }
            if ($attempt -lt 8 -and ($statusCode -eq 429 -or $statusCode -eq 502 -or $statusCode -eq 503 -or $statusCode -eq 504)) {
                $retryAfter = 0
                if ($response -and $response.Headers -and $response.Headers["Retry-After"]) {
                    [void][int]::TryParse([string]$response.Headers["Retry-After"], [ref]$retryAfter)
                }
                $delay = if ($retryAfter -gt 0) { $retryAfter } else { [Math]::Min(60, 8 * $attempt) }
                Write-Host "Dataverse devolvio $statusCode. Reintentando en $delay s..."
                Start-Sleep -Seconds $delay
                continue
            }

            $statusText = if ($response) { "$statusCode $($response.ReasonPhrase)" } else { "sin respuesta HTTP" }
            throw "Dataverse $Method $RelativePath fallo ($statusText). $($_.Exception.Message)"
        }
    }
}

function Invoke-DataverseJson {
    param(
        [string]$Method,
        [string]$RelativePath,
        $Body = $null,
        [hashtable]$ExtraHeaders = @{},
        [switch]$AllowNotFound
    )

    $response = Invoke-Dataverse -Method $Method -RelativePath $RelativePath -Body $Body -ExtraHeaders $ExtraHeaders -AllowNotFound:$AllowNotFound
    if ($null -eq $response -or [string]::IsNullOrWhiteSpace($response.Content)) {
        return $null
    }

    return $response.Content | ConvertFrom-Json
}

function Test-EntityExists {
    $result = Invoke-DataverseJson -Method "GET" -RelativePath "/api/data/v9.2/EntityDefinitions(LogicalName='$TableLogicalName')?`$select=LogicalName" -AllowNotFound
    return $null -ne $result
}

function Test-AttributeExists([string]$LogicalName) {
    $result = Invoke-DataverseJson -Method "GET" -RelativePath "/api/data/v9.2/EntityDefinitions(LogicalName='$TableLogicalName')/Attributes(LogicalName='$LogicalName')?`$select=LogicalName" -AllowNotFound
    return $null -ne $result
}

function New-StringAttribute([string]$SchemaName, [string]$Label, [int]$MaxLength = 200) {
    @{
        "@odata.type" = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        AttributeType = "String"
        AttributeTypeName = @{ Value = "StringType" }
        SchemaName = $SchemaName
        DisplayName = New-Label $Label
        Description = New-Label $Label
        RequiredLevel = New-RequiredLevel
        MaxLength = $MaxLength
        FormatName = @{ Value = "Text" }
    }
}

function New-MemoAttribute([string]$SchemaName, [string]$Label, [int]$MaxLength = 4000) {
    @{
        "@odata.type" = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
        AttributeType = "Memo"
        AttributeTypeName = @{ Value = "MemoType" }
        SchemaName = $SchemaName
        DisplayName = New-Label $Label
        Description = New-Label $Label
        RequiredLevel = New-RequiredLevel
        MaxLength = $MaxLength
        Format = "TextArea"
    }
}

function New-IntegerAttribute([string]$SchemaName, [string]$Label, [int]$MinValue = -100000, [int]$MaxValue = 100000) {
    @{
        "@odata.type" = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
        AttributeType = "Integer"
        AttributeTypeName = @{ Value = "IntegerType" }
        SchemaName = $SchemaName
        DisplayName = New-Label $Label
        Description = New-Label $Label
        RequiredLevel = New-RequiredLevel
        MinValue = $MinValue
        MaxValue = $MaxValue
        Format = "None"
    }
}

function New-DecimalAttribute([string]$SchemaName, [string]$Label) {
    @{
        "@odata.type" = "Microsoft.Dynamics.CRM.DecimalAttributeMetadata"
        AttributeType = "Decimal"
        AttributeTypeName = @{ Value = "DecimalType" }
        SchemaName = $SchemaName
        DisplayName = New-Label $Label
        Description = New-Label $Label
        RequiredLevel = New-RequiredLevel
        MinValue = 0.0
        MaxValue = 100.0
        Precision = 2
    }
}

function New-DateOnlyAttribute([string]$SchemaName, [string]$Label) {
    @{
        "@odata.type" = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
        AttributeType = "DateTime"
        AttributeTypeName = @{ Value = "DateTimeType" }
        SchemaName = $SchemaName
        DisplayName = New-Label $Label
        Description = New-Label $Label
        RequiredLevel = New-RequiredLevel
        Format = "DateOnly"
        DateTimeBehavior = @{ Value = "DateOnly" }
    }
}

function Ensure-Table {
    if (Test-EntityExists) {
        Write-Host "Tabla $TableLogicalName ya existe."
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.EntityMetadata"
        SchemaName = $TableSchemaName
        DisplayName = New-Label "Plan Rio Entreno"
        DisplayCollectionName = New-Label "Plan Rio Entrenos"
        Description = New-Label "Entrenos del plan Ironman Rio cargados desde el archivo fuente."
        OwnershipType = "UserOwned"
        IsActivity = $false
        HasActivities = $false
        HasNotes = $false
        PrimaryNameAttribute = $PrimaryNameField
        Attributes = @(
            @{
                "@odata.type" = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
                AttributeType = "String"
                AttributeTypeName = @{ Value = "StringType" }
                SchemaName = $PrimaryNameField
                DisplayName = New-Label "Nombre"
                Description = New-Label "Nombre del entreno"
                RequiredLevel = New-RequiredLevel
                MaxLength = 200
                FormatName = @{ Value = "Text" }
                IsPrimaryName = $true
            }
        )
    }

    Invoke-Dataverse -Method "POST" -RelativePath "/api/data/v9.2/EntityDefinitions" -Body $payload | Out-Null
    Write-Host "Tabla $TableLogicalName creada."
    Start-Sleep -Seconds 8
}

function Ensure-Attribute([string]$LogicalName, [hashtable]$Payload) {
    if (Test-AttributeExists $LogicalName) {
        return
    }

    Invoke-Dataverse -Method "POST" -RelativePath "/api/data/v9.2/EntityDefinitions(LogicalName='$TableLogicalName')/Attributes" -Body $Payload | Out-Null
    Write-Host "Columna $LogicalName creada."
    Start-Sleep -Milliseconds 500
}

function Ensure-Columns {
    Ensure-Attribute "cr07a_fecha" (New-DateOnlyAttribute "cr07a_Fecha" "Fecha")
    Ensure-Attribute "cr07a_dia" (New-StringAttribute "cr07a_Dia" "Dia" 40)
    Ensure-Attribute "cr07a_semanaplan" (New-IntegerAttribute "cr07a_SemanaPlan" "Semana plan" 0 1000)
    Ensure-Attribute "cr07a_iniciodesemana" (New-DateOnlyAttribute "cr07a_InicioDeSemana" "Inicio de semana")
    Ensure-Attribute "cr07a_fase" (New-StringAttribute "cr07a_Fase" "Fase" 250)
    Ensure-Attribute "cr07a_disciplina" (New-StringAttribute "cr07a_Disciplina" "Disciplina" 100)
    Ensure-Attribute "cr07a_sesion" (New-StringAttribute "cr07a_Sesion" "Sesion" 250)
    Ensure-Attribute "cr07a_min" (New-IntegerAttribute "cr07a_Min" "Minutos" 0 3000)
    Ensure-Attribute "cr07a_horas" (New-DecimalAttribute "cr07a_Horas" "Horas")
    Ensure-Attribute "cr07a_volumenobjetivo" (New-StringAttribute "cr07a_VolumenObjetivo" "Volumen objetivo" 250)
    Ensure-Attribute "cr07a_intensidadzona" (New-StringAttribute "cr07a_IntensidadZona" "Intensidad/Zona" 180)
    Ensure-Attribute "cr07a_detalle" (New-MemoAttribute "cr07a_Detalle" "Detalle" 4000)
    Ensure-Attribute "cr07a_nutricionhidratacion" (New-MemoAttribute "cr07a_NutricionHidratacion" "Nutricion/Hidratacion" 4000)
    Ensure-Attribute "cr07a_objetivo" (New-MemoAttribute "cr07a_Objetivo" "Objetivo" 2000)
    Ensure-Attribute "cr07a_estado" (New-StringAttribute "cr07a_Estado" "Estado" 80)
    Ensure-Attribute "cr07a_duracionreal" (New-IntegerAttribute "cr07a_DuracionReal" "Duracion real" 0 3000)
    Ensure-Attribute "cr07a_notas" (New-MemoAttribute "cr07a_Notas" "Notas" 4000)
    Ensure-Attribute "cr07a_origenhoja" (New-StringAttribute "cr07a_OrigenHoja" "Origen hoja" 120)
    Ensure-Attribute "cr07a_filaorigen" (New-IntegerAttribute "cr07a_FilaOrigen" "Fila origen" 0 1000000)
    Ensure-Attribute "cr07a_claveorigen" (New-StringAttribute "cr07a_ClaveOrigen" "Clave origen" 160)
}

function Publish-PlanRioTable {
    $xml = "<importexportxml><entities><entity>$TableLogicalName</entity></entities><nodes/><securityroles/><settings/><workflows/></importexportxml>"
    Invoke-Dataverse -Method "POST" -RelativePath "/api/data/v9.2/PublishXml" -Body @{ ParameterXml = $xml } | Out-Null
    Write-Host "Customizations publicadas."
    Start-Sleep -Seconds 8
}

function Get-TableMetadata {
    $metadata = Invoke-DataverseJson -Method "GET" -RelativePath "/api/data/v9.2/EntityDefinitions(LogicalName='$TableLogicalName')?`$select=LogicalName,EntitySetName,PrimaryIdAttribute,PrimaryNameAttribute"
    if ($null -eq $metadata) {
        return [pscustomobject]@{
            EntitySetName = $TableFallbackSetName
            PrimaryIdAttribute = $TableFallbackIdField
            PrimaryNameAttribute = $PrimaryNameField
        }
    }

    return $metadata
}

function Convert-ExcelDate($Value) {
    if ($null -eq $Value) { return $null }
    $text = ([string]$Value).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }

    $serial = 0.0
    if ([double]::TryParse($text, [System.Globalization.NumberStyles]::Any, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$serial)) {
        if ($serial -gt 20000 -and $serial -lt 80000) {
            return [DateTime]::FromOADate($serial).ToString("yyyy-MM-dd")
        }
    }

    $date = [DateTime]::MinValue
    if ([DateTime]::TryParse($text, [System.Globalization.CultureInfo]::GetCultureInfo("es-CO"), [System.Globalization.DateTimeStyles]::None, [ref]$date)) {
        return $date.ToString("yyyy-MM-dd")
    }
    if ([DateTime]::TryParse($text, [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::None, [ref]$date)) {
        return $date.ToString("yyyy-MM-dd")
    }

    return $null
}

function Convert-Int($Value) {
    if ($null -eq $Value) { return $null }
    $text = ([string]$Value).Trim()
    if ([string]::IsNullOrWhiteSpace($text) -or $text -eq "—") { return $null }
    $number = 0.0
    if ([double]::TryParse($text.Replace(",", "."), [System.Globalization.NumberStyles]::Any, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$number)) {
        return [int][Math]::Round($number)
    }
    $match = [regex]::Match($text, "\d+")
    if ($match.Success) { return [int]$match.Value }
    return $null
}

function Convert-Decimal($Value) {
    if ($null -eq $Value) { return $null }
    $text = ([string]$Value).Trim()
    if ([string]::IsNullOrWhiteSpace($text) -or $text -eq "—") { return $null }
    $number = 0.0
    if ([double]::TryParse($text.Replace(",", "."), [System.Globalization.NumberStyles]::Any, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$number)) {
        return [Math]::Round($number, 2)
    }
    return $null
}

function Limit-Text([string]$Text, [int]$MaxLength) {
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    $clean = ($Text -replace "\r\n", "`n").Trim()
    if ($MaxLength -le 0) { return $clean }
    if ($clean.Length -le $MaxLength) { return $clean }
    return $clean.Substring(0, $MaxLength)
}

function Load-XmlEntry($Zip, [string]$EntryName) {
    $entry = $Zip.GetEntry($EntryName)
    if ($null -eq $entry) { return $null }
    $doc = New-Object System.Xml.XmlDocument
    $stream = $entry.Open()
    try { $doc.Load($stream) } finally { $stream.Dispose() }
    return $doc
}

function Get-ColumnIndex([string]$Reference) {
    $letters = ([regex]::Match($Reference, "^[A-Z]+")).Value
    $n = 0
    foreach ($ch in $letters.ToCharArray()) {
        $n = ($n * 26) + ([int][char]$ch - [int][char]'A' + 1)
    }
    return $n
}

function Import-SharedStrings($Zip) {
    $sharedDoc = Load-XmlEntry $Zip "xl/sharedStrings.xml"
    if ($null -eq $sharedDoc) { return @() }

    $ns = New-Object System.Xml.XmlNamespaceManager($sharedDoc.NameTable)
    $ns.AddNamespace("m", "http://schemas.openxmlformats.org/spreadsheetml/2006/main")
    return @($sharedDoc.SelectNodes("//m:si", $ns) | ForEach-Object {
        ($_.SelectNodes(".//m:t", $ns) | ForEach-Object { $_."#text" }) -join ""
    })
}

function Get-CellText($Cell, $SharedStrings) {
    $type = $Cell.GetAttribute("t")
    if ($type -eq "s") {
        $idx = 0
        [void][int]::TryParse([string]$Cell.v, [ref]$idx)
        return [string]$SharedStrings[$idx]
    }
    if ($type -eq "inlineStr") {
        return (($Cell.SelectNodes(".//*[local-name()='t']") | ForEach-Object { $_."#text" }) -join "")
    }
    return [string]$Cell.v
}

function Read-PlanRows([string]$Path) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $resolvedPath = Resolve-Path -LiteralPath $Path
    $zip = [System.IO.Compression.ZipFile]::OpenRead($resolvedPath.Path)
    try {
        $sharedStrings = Import-SharedStrings $zip
        $workbook = Load-XmlEntry $zip "xl/workbook.xml"
        $rels = Load-XmlEntry $zip "xl/_rels/workbook.xml.rels"
        $workbookNs = New-Object System.Xml.XmlNamespaceManager($workbook.NameTable)
        $workbookNs.AddNamespace("m", "http://schemas.openxmlformats.org/spreadsheetml/2006/main")

        $sheetNames = @($PreferredSheetName) + $FallbackSheetNames
        $sheetNode = $null
        foreach ($candidate in $sheetNames) {
            $sheetNode = $workbook.SelectNodes("//m:sheets/m:sheet", $workbookNs) | Where-Object {
                $_.name -eq $candidate
            } | Select-Object -First 1
            if ($sheetNode) { break }
        }
        if ($null -eq $sheetNode) {
            $available = ($workbook.SelectNodes("//m:sheets/m:sheet", $workbookNs) | ForEach-Object { $_.name }) -join ", "
            throw "No encontre ninguna hoja compatible. Disponibles: $available"
        }

        $rid = $sheetNode.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships")
        $rel = $rels.DocumentElement.ChildNodes | Where-Object { $_.GetAttribute("Id") -eq $rid } | Select-Object -First 1
        $target = $rel.GetAttribute("Target")
        $sheetPath = if ($target.StartsWith("/")) { $target.TrimStart("/") } else { "xl/" + $target.TrimStart("/") }
        $sheetDoc = Load-XmlEntry $zip $sheetPath
        $sheetNs = New-Object System.Xml.XmlNamespaceManager($sheetDoc.NameTable)
        $sheetNs.AddNamespace("m", "http://schemas.openxmlformats.org/spreadsheetml/2006/main")

        $rows = @($sheetDoc.SelectNodes("//m:sheetData/m:row", $sheetNs) | ForEach-Object {
            $cells = @{}
            foreach ($cell in $_.SelectNodes("m:c", $sheetNs)) {
                $cells[(Get-ColumnIndex $cell.GetAttribute("r"))] = (Get-CellText $cell $sharedStrings)
            }
            [pscustomobject]@{
                RowNumber = [int]$_.GetAttribute("r")
                Cells = $cells
            }
        })

        $headerRow = $rows | Where-Object {
            $values = $_.Cells.Values
            ($values -contains "Fecha") -and (($values -contains "Detalle") -or ($values -contains "Indicaciones"))
        } | Select-Object -First 1
        if ($null -eq $headerRow) {
            throw "No pude identificar la fila de encabezados en la hoja '$($sheetNode.name)'."
        }

        $headers = @{}
        foreach ($idx in $headerRow.Cells.Keys) {
            $name = ([string]$headerRow.Cells[$idx]).Trim()
            if (-not [string]::IsNullOrWhiteSpace($name)) {
                $headers[$name] = $idx
            }
        }

        function Get-ByHeader($Row, [string[]]$Names) {
            foreach ($name in $Names) {
                if ($headers.ContainsKey($name)) {
                    return [string]$Row.Cells[$headers[$name]]
                }
            }
            return ""
        }

        $dataRows = foreach ($row in $rows | Where-Object { $_.RowNumber -gt $headerRow.RowNumber }) {
            $session = Get-ByHeader $row @("Sesión", "Sesion")
            $detail = Get-ByHeader $row @("Detalle", "Indicaciones")
            $date = Get-ByHeader $row @("Fecha")
            if ([string]::IsNullOrWhiteSpace($session) -and [string]::IsNullOrWhiteSpace($detail) -and [string]::IsNullOrWhiteSpace($date)) {
                continue
            }

            $week = Get-ByHeader $row @("Semana plan", "Semana")
            $minutes = Convert-Int (Get-ByHeader $row @("Min", "Duración plan (min)", "Duracion plan (min)"))
            $hours = Convert-Decimal (Get-ByHeader $row @("Horas"))
            if ($null -eq $hours -and $null -ne $minutes) { $hours = [Math]::Round($minutes / 60, 2) }
            $volume = Get-ByHeader $row @("Volumen objetivo", "Distancia plan")
            $unit = Get-ByHeader $row @("Unidad")
            if (-not [string]::IsNullOrWhiteSpace($unit) -and -not [string]::IsNullOrWhiteSpace($volume)) {
                $volume = "$volume $unit"
            }

            [pscustomobject]@{
                RowNumber = $row.RowNumber
                SourceSheet = $sheetNode.name
                Fecha = Convert-ExcelDate $date
                Dia = Get-ByHeader $row @("Día", "Dia")
                Semana = Convert-Int $week
                InicioSemana = Convert-ExcelDate (Get-ByHeader $row @("Inicio semana"))
                Fase = Get-ByHeader $row @("Fase")
                Disciplina = Get-ByHeader $row @("Disciplina")
                Sesion = $session
                Min = $minutes
                Horas = $hours
                VolumenObjetivo = $volume
                IntensidadZona = Get-ByHeader $row @("Intensidad/Zona", "Zona objetivo")
                Detalle = $detail
                NutricionHidratacion = Get-ByHeader $row @("Nutrición/Hidratación", "Nutricion/Hidratacion")
                Objetivo = Get-ByHeader $row @("Objetivo")
                Estado = Get-ByHeader $row @("Estado")
                DuracionReal = Convert-Int (Get-ByHeader $row @("Duración real", "Duracion real", "Duración real (min)", "Duracion real (min)"))
                Notas = Get-ByHeader $row @("Notas", "Comentarios")
            }
        }

        return @($dataRows)
    }
    finally {
        $zip.Dispose()
    }
}

function Add-Value([hashtable]$Payload, [string]$Field, $Value, [int]$MaxLength = 0) {
    if ($null -eq $Value) { return }
    if ($Value -is [string]) {
        $text = Limit-Text $Value $MaxLength
        if ([string]::IsNullOrWhiteSpace($text)) { return }
        $Payload[$Field] = $text
        return
    }

    $Payload[$Field] = $Value
}

function Remove-ExistingRows($Metadata) {
    if ($KeepExistingRows) { return 0 }

    $deleted = 0
    $next = "/api/data/v9.2/$($Metadata.EntitySetName)?`$select=$($Metadata.PrimaryIdAttribute)"
    while (-not [string]::IsNullOrWhiteSpace($next)) {
        $page = Invoke-DataverseJson -Method "GET" -RelativePath $next
        foreach ($item in @($page.value)) {
            $id = $item.($Metadata.PrimaryIdAttribute)
            if (-not [string]::IsNullOrWhiteSpace($id)) {
                Invoke-Dataverse -Method "DELETE" -RelativePath "/api/data/v9.2/$($Metadata.EntitySetName)($id)" | Out-Null
                $deleted++
            }
        }

        $nextLink = $page."@odata.nextLink"
        if ([string]::IsNullOrWhiteSpace($nextLink)) {
            $next = $null
        } else {
            $uri = [Uri]$nextLink
            $next = "$($uri.AbsolutePath)$($uri.Query)"
        }
    }

    return $deleted
}

function Import-Rows($Metadata, [object[]]$Rows) {
    $created = 0
    foreach ($row in $Rows) {
        $titleParts = @(
            if ($null -ne $row.Semana) { "Semana $($row.Semana)" }
            $row.Dia
            $row.Disciplina
            $row.Sesion
        ) | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }
        $title = ($titleParts -join " - ")
        if ([string]::IsNullOrWhiteSpace($title)) {
            $title = "Plan Rio fila $($row.RowNumber)"
        }

        $payload = @{}
        Add-Value $payload $PrimaryNameField $title 200
        Add-Value $payload "cr07a_fecha" $row.Fecha
        Add-Value $payload "cr07a_dia" $row.Dia 40
        Add-Value $payload "cr07a_semanaplan" $row.Semana
        Add-Value $payload "cr07a_iniciodesemana" $row.InicioSemana
        Add-Value $payload "cr07a_fase" $row.Fase 250
        Add-Value $payload "cr07a_disciplina" $row.Disciplina 100
        Add-Value $payload "cr07a_sesion" $row.Sesion 250
        Add-Value $payload "cr07a_min" $row.Min
        Add-Value $payload "cr07a_horas" $row.Horas
        Add-Value $payload "cr07a_volumenobjetivo" $row.VolumenObjetivo 250
        Add-Value $payload "cr07a_intensidadzona" $row.IntensidadZona 180
        Add-Value $payload "cr07a_detalle" $row.Detalle 4000
        Add-Value $payload "cr07a_nutricionhidratacion" $row.NutricionHidratacion 4000
        Add-Value $payload "cr07a_objetivo" $row.Objetivo 2000
        Add-Value $payload "cr07a_estado" $row.Estado 80
        Add-Value $payload "cr07a_duracionreal" $row.DuracionReal
        Add-Value $payload "cr07a_notas" $row.Notas 4000
        Add-Value $payload "cr07a_origenhoja" $row.SourceSheet 120
        Add-Value $payload "cr07a_filaorigen" $row.RowNumber
        Add-Value $payload "cr07a_claveorigen" "$($row.SourceSheet):$($row.RowNumber)" 160

        Invoke-Dataverse -Method "POST" -RelativePath "/api/data/v9.2/$($Metadata.EntitySetName)" -Body $payload | Out-Null
        $created++
    }

    return $created
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw "Azure CLI no esta disponible para obtener token de Dataverse."
}

if (-not (Test-Path -LiteralPath $WorkbookPath)) {
    throw "No existe el archivo $WorkbookPath."
}

$script:AccessToken = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
if ([string]::IsNullOrWhiteSpace($script:AccessToken)) {
    throw "No se pudo obtener token para $DataverseUrl."
}

Write-Host "Leyendo $WorkbookPath..."
$rows = Read-PlanRows $WorkbookPath
if ($rows.Count -eq 0) {
    throw "El archivo no contiene filas de entrenamiento para importar."
}
Write-Host "Filas detectadas: $($rows.Count). Hoja: $($rows[0].SourceSheet)."

Ensure-Table
Ensure-Columns
Publish-PlanRioTable
$metadata = Get-TableMetadata

$deleted = Remove-ExistingRows $metadata
if (-not $KeepExistingRows) {
    Write-Host "Registros anteriores eliminados: $deleted."
}

$created = Import-Rows $metadata $rows
Write-Host "Registros importados: $created."
Write-Host "Tabla: $($metadata.EntitySetName) ($TableLogicalName)."
