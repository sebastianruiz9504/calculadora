param(
    [string]$BaseUrl = "",
    [string]$TenantId = "",
    [string]$ClientId = "",
    [string]$ClientSecret = "",
    [string]$WorkbookPath = "",
    [switch]$CreateMissingEmployees,
    [switch]$OnlyEnsureSchema,
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

$LanguageCode = 3082
$EmployeeCedulaField = "cr07a_cedula"

$AdditionalColumns = @(
    @{ LogicalName = "cr07a_identificacionempleadonomina"; SchemaName = "cr07a_IdentificacionEmpleadoNomina"; Label = "Identificacion empleado nomina"; Type = "String"; MaxLength = 50 },
    @{ LogicalName = "cr07a_nocontrato"; SchemaName = "cr07a_NoContrato"; Label = "No contrato"; Type = "String"; MaxLength = 80 },
    @{ LogicalName = "cr07a_periodoinicio"; SchemaName = "cr07a_PeriodoInicio"; Label = "Periodo inicio"; Type = "DateOnly" },
    @{ LogicalName = "cr07a_periodofin"; SchemaName = "cr07a_PeriodoFin"; Label = "Periodo fin"; Type = "DateOnly" },
    @{ LogicalName = "cr07a_incapacidadenfermedadgeneral66"; SchemaName = "cr07a_IncapacidadEnfermedadGeneral66"; Label = "Incapacidad enfermedad general 66"; Type = "Money" },
    @{ LogicalName = "cr07a_vacacionesdisfrutadas"; SchemaName = "cr07a_VacacionesDisfrutadas"; Label = "Vacaciones disfrutadas"; Type = "Money" },
    @{ LogicalName = "cr07a_licenciaporluto"; SchemaName = "cr07a_LicenciaPorLuto"; Label = "Licencia por luto"; Type = "Money" },
    @{ LogicalName = "cr07a_bonificacionesocasionales"; SchemaName = "cr07a_BonificacionesOcasionales"; Label = "Bonificaciones ocasionales"; Type = "Money" },
    @{ LogicalName = "cr07a_interesescesantias"; SchemaName = "cr07a_InteresesCesantias"; Label = "Intereses de cesantias"; Type = "Money" },
    @{ LogicalName = "cr07a_descuentosalarial"; SchemaName = "cr07a_DescuentoSalarial"; Label = "Descuento salarial"; Type = "Money" },
    @{ LogicalName = "cr07a_fondosolidaridadpensional"; SchemaName = "cr07a_FondoSolidaridadPensional"; Label = "Fondo de solidaridad pensional"; Type = "Money" },
    @{ LogicalName = "cr07a_totaldeducciones"; SchemaName = "cr07a_TotalDeducciones"; Label = "Total deducciones"; Type = "Money" },
    @{ LogicalName = "cr07a_archivoorigennomina"; SchemaName = "cr07a_ArchivoOrigenNomina"; Label = "Archivo origen nomina"; Type = "String"; MaxLength = 260 },
    @{ LogicalName = "cr07a_hojaorigen"; SchemaName = "cr07a_HojaOrigen"; Label = "Hoja origen"; Type = "String"; MaxLength = 120 },
    @{ LogicalName = "cr07a_filaorigen"; SchemaName = "cr07a_FilaOrigen"; Label = "Fila origen"; Type = "Integer"; MinValue = 0; MaxValue = 1000000 },
    @{ LogicalName = "cr07a_claveorigen"; SchemaName = "cr07a_ClaveOrigen"; Label = "Clave origen"; Type = "String"; MaxLength = 200 }
)

$TargetSheets = @(
    "nomina enero",
    "nomina febrero",
    "NONIMA MARZO"
)

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
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        return $map
    }

    $lines = & dotnet user-secrets list --project .\CotizadorInterno.Web.csproj 2>$null
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

function ConvertTo-BodyJson($Body) {
    if ($null -eq $Body) { return $null }
    return $Body | ConvertTo-Json -Depth 60
}

function Invoke-Dataverse {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Path,
        [object]$Body = $null,
        [hashtable]$ExtraHeaders = @{},
        [switch]$AllowNotFound
    )

    $uri = if ($Path.StartsWith("http", [System.StringComparison]::OrdinalIgnoreCase)) {
        $Path
    } else {
        "$script:BaseUrl$Path"
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
            throw "Dataverse $Method $Path fallo ($statusText). $($_.Exception.Message)"
        }
    }
}

function Invoke-DataverseJson {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Path,
        [object]$Body = $null,
        [hashtable]$ExtraHeaders = @{},
        [switch]$AllowNotFound
    )

    $response = Invoke-Dataverse -Method $Method -Path $Path -Body $Body -ExtraHeaders $ExtraHeaders -AllowNotFound:$AllowNotFound
    if ($null -eq $response -or [string]::IsNullOrWhiteSpace($response.Content)) {
        return $null
    }

    return $response.Content | ConvertFrom-Json
}

function Get-AccessToken {
    if (-not [string]::IsNullOrWhiteSpace($script:ClientId) -and -not [string]::IsNullOrWhiteSpace($script:ClientSecret)) {
        $tokenResponse = Invoke-RestMethod -Method "POST" -Uri "https://login.microsoftonline.com/$script:TenantId/oauth2/v2.0/token" -ContentType "application/x-www-form-urlencoded" -Body @{
            client_id = $script:ClientId
            client_secret = $script:ClientSecret
            scope = "$script:BaseUrl/.default"
            grant_type = "client_credentials"
        }
        if (-not [string]::IsNullOrWhiteSpace($tokenResponse.access_token)) {
            return $tokenResponse.access_token
        }
    }

    if (Get-Command az -ErrorAction SilentlyContinue) {
        $token = az account get-access-token --resource $script:BaseUrl --query accessToken -o tsv
        if (-not [string]::IsNullOrWhiteSpace($token)) {
            return $token
        }
    }

    throw "No fue posible obtener token para Dataverse."
}

function Test-AttributeExists([string]$EntityLogicalName, [string]$AttributeLogicalName) {
    $path = "/api/data/v9.2/EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')?`$select=LogicalName"
    $result = Invoke-DataverseJson -Method "GET" -Path $path -AllowNotFound
    return $null -ne $result
}

function New-StringAttributePayload($Column) {
    return @{
        "@odata.type" = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        AttributeType = "String"
        AttributeTypeName = New-Value "StringType"
        SchemaName = $Column.SchemaName
        DisplayName = New-Label $Column.Label
        Description = New-Label $Column.Label
        RequiredLevel = New-RequiredNone
        MaxLength = [int]$Column.MaxLength
        FormatName = New-Value "Text"
    }
}

function New-IntegerAttributePayload($Column) {
    return @{
        "@odata.type" = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
        AttributeType = "Integer"
        AttributeTypeName = New-Value "IntegerType"
        SchemaName = $Column.SchemaName
        DisplayName = New-Label $Column.Label
        Description = New-Label $Column.Label
        RequiredLevel = New-RequiredNone
        MinValue = [int]$Column.MinValue
        MaxValue = [int]$Column.MaxValue
        Format = "None"
        SourceTypeMask = 0
    }
}

function New-DateOnlyAttributePayload($Column) {
    return @{
        "@odata.type" = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
        AttributeType = "DateTime"
        AttributeTypeName = New-Value "DateTimeType"
        SchemaName = $Column.SchemaName
        DisplayName = New-Label $Column.Label
        Description = New-Label $Column.Label
        RequiredLevel = New-RequiredNone
        Format = "DateOnly"
        DateTimeBehavior = New-Value "DateOnly"
    }
}

function New-MoneyAttributePayload($Column) {
    return @{
        "@odata.type" = "Microsoft.Dynamics.CRM.MoneyAttributeMetadata"
        AttributeType = "Money"
        AttributeTypeName = New-Value "MoneyType"
        SchemaName = $Column.SchemaName
        DisplayName = New-Label $Column.Label
        Description = New-Label $Column.Label
        RequiredLevel = New-RequiredNone
        MinValue = -1000000000000
        MaxValue = 1000000000000
        Precision = 2
        PrecisionSource = 1
    }
}

function New-AttributePayload($Column) {
    switch ($Column.Type) {
        "String" { return New-StringAttributePayload $Column }
        "Integer" { return New-IntegerAttributePayload $Column }
        "DateOnly" { return New-DateOnlyAttributePayload $Column }
        "Money" { return New-MoneyAttributePayload $Column }
        default { throw "Tipo de columna no soportado: $($Column.Type)" }
    }
}

function Ensure-AdditionalColumns {
    foreach ($column in $AdditionalColumns) {
        if (Test-AttributeExists $script:PayrollTableName $column.LogicalName) {
            Write-Host "  OK columna existente: $($column.LogicalName)"
            continue
        }

        Write-Host "  Creando columna: $($column.LogicalName)"
        Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$script:PayrollTableName')/Attributes" -Body (New-AttributePayload $column) | Out-Null
        Start-Sleep -Milliseconds 750
    }
}

function Publish-PayrollTable {
    if ($SkipPublish) { return }

    $xml = "<importexportxml><entities><entity>$script:PayrollTableName</entity></entities><nodes/><securityroles/><settings/><workflows/></importexportxml>"
    Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/PublishXml" -Body @{ ParameterXml = $xml } | Out-Null
    Write-Host "Customizations de nomina publicadas."
    Start-Sleep -Seconds 10
}

function Get-TableMetadata([string]$LogicalName, [string]$FallbackSetName, [string]$FallbackIdField, [string]$FallbackNameField) {
    $metadata = Invoke-DataverseJson -Method "GET" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$LogicalName')?`$select=LogicalName,EntitySetName,PrimaryIdAttribute,PrimaryNameAttribute"
    if ($null -eq $metadata) {
        return [pscustomobject]@{
            EntitySetName = $FallbackSetName
            PrimaryIdAttribute = $FallbackIdField
            PrimaryNameAttribute = $FallbackNameField
        }
    }

    return $metadata
}

function Limit-Text([string]$Text, [int]$MaxLength) {
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    $clean = $Text.Trim()
    if ($clean.Length -le $MaxLength) { return $clean }
    return $clean.Substring(0, $MaxLength)
}

function Add-Value([hashtable]$Payload, [string]$Field, $Value, [int]$MaxLength = 0) {
    if ($null -eq $Value) { return }
    if ($Value -is [string]) {
        $text = if ($MaxLength -gt 0) { Limit-Text $Value $MaxLength } else { $Value.Trim() }
        if ([string]::IsNullOrWhiteSpace($text)) { return }
        $Payload[$Field] = $text
        return
    }

    $Payload[$Field] = $Value
}

function Add-Money([hashtable]$Payload, [string]$Field, [decimal]$Value) {
    $Payload[$Field] = [Math]::Round($Value, 2)
}

function Normalize-Key([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return "" }
    $normalized = $Value.Trim().Normalize([Text.NormalizationForm]::FormD)
    $builder = [System.Text.StringBuilder]::new()
    foreach ($ch in $normalized.ToCharArray()) {
        if ([Globalization.CharUnicodeInfo]::GetUnicodeCategory($ch) -ne [Globalization.UnicodeCategory]::NonSpacingMark) {
            [void]$builder.Append($ch)
        }
    }
    return (($builder.ToString().ToLowerInvariant() -replace "[^a-z0-9]+", " ").Trim() -replace "\s+", " ")
}

function Normalize-Document([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return "" }
    return ($Value -replace "\D+", "")
}

function Escape-ODataLiteral([string]$Value) {
    return ($Value ?? "").Replace("'", "''")
}

function Get-RelativeDataverseUrl([string]$Url) {
    if ([string]::IsNullOrWhiteSpace($Url)) { return $null }
    $uri = [Uri]$Url
    return "$($uri.AbsolutePath)$($uri.Query)"
}

function Get-AllDataverseRows([string]$RelativePath) {
    $rows = New-Object System.Collections.Generic.List[object]
    $next = $RelativePath
    while (-not [string]::IsNullOrWhiteSpace($next)) {
        $page = Invoke-DataverseJson -Method "GET" -Path $next
        foreach ($item in @($page.value)) {
            [void]$rows.Add($item)
        }

        $next = Get-RelativeDataverseUrl $page."@odata.nextLink"
    }

    return $rows.ToArray()
}

function Load-XmlEntry($Zip, [string]$EntryName) {
    $entry = $Zip.GetEntry($EntryName)
    if ($null -eq $entry) { return $null }
    $doc = [System.Xml.XmlDocument]::new()
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

    $ns = [System.Xml.XmlNamespaceManager]::new($sharedDoc.NameTable)
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

function Convert-Decimal($Value) {
    if ($null -eq $Value) { return [decimal]0 }
    $text = ([string]$Value).Trim()
    if ([string]::IsNullOrWhiteSpace($text) -or $text -eq "-") { return [decimal]0 }
    $number = [decimal]0
    if ([decimal]::TryParse($text.Replace(",", "."), [System.Globalization.NumberStyles]::Any, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$number)) {
        return [Math]::Round($number, 2)
    }
    return [decimal]0
}

function Parse-Period([string]$Text) {
    $match = [regex]::Match($Text, "(\d{2})/(\d{2})/(\d{4})\s*-\s*(\d{2})/(\d{2})/(\d{4})")
    if (-not $match.Success) {
        throw "No pude interpretar el periodo: $Text"
    }

    $start = [DateTime]::new([int]$match.Groups[3].Value, [int]$match.Groups[2].Value, [int]$match.Groups[1].Value)
    $end = [DateTime]::new([int]$match.Groups[6].Value, [int]$match.Groups[5].Value, [int]$match.Groups[4].Value)
    return [pscustomobject]@{
        Start = $start
        End = $end
        Key = $start.ToString("yyyy-MM")
        Days = [int](($end.Date - $start.Date).TotalDays + 1)
    }
}

function Read-NominaRows([string]$Path) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $resolvedPath = Resolve-Path -LiteralPath $Path
    $zip = [System.IO.Compression.ZipFile]::OpenRead($resolvedPath.Path)
    try {
        $sharedStrings = Import-SharedStrings $zip
        $workbook = Load-XmlEntry $zip "xl/workbook.xml"
        $rels = Load-XmlEntry $zip "xl/_rels/workbook.xml.rels"
        $workbookNs = [System.Xml.XmlNamespaceManager]::new($workbook.NameTable)
        $workbookNs.AddNamespace("m", "http://schemas.openxmlformats.org/spreadsheetml/2006/main")

        $allRows = New-Object System.Collections.Generic.List[object]
        foreach ($sheetName in $TargetSheets) {
            $sheetNode = $workbook.SelectNodes("//m:sheets/m:sheet", $workbookNs) | Where-Object { $_.name -eq $sheetName } | Select-Object -First 1
            if ($null -eq $sheetNode) {
                throw "No encontre la hoja requerida: $sheetName"
            }

            $rid = $sheetNode.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships")
            $rel = $rels.DocumentElement.ChildNodes | Where-Object { $_.GetAttribute("Id") -eq $rid } | Select-Object -First 1
            $target = $rel.GetAttribute("Target")
            $sheetPath = if ($target.StartsWith("/")) { $target.TrimStart("/") } else { "xl/" + $target.TrimStart("/") }
            $sheetDoc = Load-XmlEntry $zip $sheetPath
            $sheetNs = [System.Xml.XmlNamespaceManager]::new($sheetDoc.NameTable)
            $sheetNs.AddNamespace("m", "http://schemas.openxmlformats.org/spreadsheetml/2006/main")

            $rows = @($sheetDoc.SelectNodes("//m:sheetData/m:row", $sheetNs) | ForEach-Object {
                $cells = @{}
                foreach ($cell in $_.SelectNodes("m:c", $sheetNs)) {
                    $cells[(Get-ColumnIndex $cell.GetAttribute("r"))] = ((Get-CellText $cell $sharedStrings) ?? "").Trim()
                }
                [pscustomobject]@{
                    RowNumber = [int]$_.GetAttribute("r")
                    Cells = $cells
                }
            })

            $periodText = [string](($rows | Where-Object RowNumber -eq 4 | Select-Object -First 1).Cells[1])
            if ([string]::IsNullOrWhiteSpace($periodText)) {
                $periodText = [string](($rows | Where-Object RowNumber -eq 4 | Select-Object -First 1).Cells[0])
            }
            $period = Parse-Period $periodText
            $headerRow = $rows | Where-Object RowNumber -eq 6 | Select-Object -First 1
            if ($null -eq $headerRow) {
                throw "No pude identificar encabezados en '$sheetName'."
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

            foreach ($row in $rows | Where-Object RowNumber -gt 6) {
                $employeeName = Get-ByHeader $row @("Nombre")
                $document = Get-ByHeader $row @("Identificacion", "Identificación")
                if ([string]::IsNullOrWhiteSpace($employeeName) -and [string]::IsNullOrWhiteSpace($document)) {
                    continue
                }

                [void]$allRows.Add([pscustomobject]@{
                    SourceSheet = $sheetName
                    SourceRow = $row.RowNumber
                    PeriodKey = $period.Key
                    PeriodStart = $period.Start.ToString("yyyy-MM-dd")
                    PeriodEnd = $period.End.ToString("yyyy-MM-dd")
                    PeriodDays = $period.Days
                    EmployeeName = $employeeName
                    Document = $document
                    ContractNumber = Get-ByHeader $row @("No contrato")
                    Salary = Convert-Decimal (Get-ByHeader $row @("Sueldo"))
                    ConnectivityAllowance = Convert-Decimal (Get-ByHeader $row @("Aux. de transporte/Aux. de conectividad digital", "Aux. de conectividad - beneficio extralegal"))
                    SickLeave66 = Convert-Decimal (Get-ByHeader $row @("Incapacidad por enfermedad general al 66%"))
                    Vacation = Convert-Decimal (Get-ByHeader $row @("Vacaciones disfrutadas"))
                    BereavementLeave = Convert-Decimal (Get-ByHeader $row @("Licencia por luto"))
                    OccasionalBonuses = Convert-Decimal (Get-ByHeader $row @("Bonificaciones ocasionales"))
                    SeveranceInterest = Convert-Decimal (Get-ByHeader $row @("Intereses de cesantias", "Intereses de cesantías"))
                    SalaryCommission = Convert-Decimal (Get-ByHeader $row @("Comision Salarial", "Comisión Salarial"))
                    ComplianceBonus = Convert-Decimal (Get-ByHeader $row @("Bono cumplimiento"))
                    TotalIncome = Convert-Decimal (Get-ByHeader $row @("Total Ingresos"))
                    Health = Convert-Decimal (Get-ByHeader $row @("Fondo de salud"))
                    Pension = Convert-Decimal (Get-ByHeader $row @("Fondo de pension", "Fondo de pensión"))
                    PensionSolidarity = Convert-Decimal (Get-ByHeader $row @("Fondo de solidaridad pensional"))
                    Withholding = Convert-Decimal (Get-ByHeader $row @("Retefuente"))
                    Loan = Convert-Decimal (Get-ByHeader $row @("Prestamos"))
                    SalaryDiscount = Convert-Decimal (Get-ByHeader $row @("Descuento salarial"))
                    TotalDeductions = Convert-Decimal (Get-ByHeader $row @("Total deducciones"))
                    NetPay = Convert-Decimal (Get-ByHeader $row @("Neto a Pagar"))
                })
            }
        }

        return $allRows.ToArray()
    }
    finally {
        $zip.Dispose()
    }
}

function Get-EmployeeMaps($Metadata) {
    $select = "$script:EmployeeIdField,$script:EmployeeNameField,$EmployeeCedulaField"
    $rows = Get-AllDataverseRows "/api/data/v9.2/$($Metadata.EntitySetName)?`$select=$select"
    $byDocument = @{}
    $byName = @{}
    foreach ($row in $rows) {
        $id = [string]$row.($script:EmployeeIdField)
        if ([string]::IsNullOrWhiteSpace($id)) { continue }

        $document = Normalize-Document ([string]$row.($EmployeeCedulaField))
        if (-not [string]::IsNullOrWhiteSpace($document) -and -not $byDocument.ContainsKey($document)) {
            $byDocument[$document] = $row
        }

        $nameKey = Normalize-Key ([string]$row.($script:EmployeeNameField))
        if (-not [string]::IsNullOrWhiteSpace($nameKey) -and -not $byName.ContainsKey($nameKey)) {
            $byName[$nameKey] = $row
        }
    }

    return [pscustomobject]@{
        ByDocument = $byDocument
        ByName = $byName
        Count = $rows.Count
    }
}

function Resolve-Employee($Row, $EmployeeMaps) {
    $document = Normalize-Document $Row.Document
    if ($EmployeeMaps.ByDocument.ContainsKey($document)) {
        return $EmployeeMaps.ByDocument[$document]
    }

    $nameKey = Normalize-Key $Row.EmployeeName
    if ($EmployeeMaps.ByName.ContainsKey($nameKey)) {
        return $EmployeeMaps.ByName[$nameKey]
    }

    return $null
}

function Add-EmployeeToMaps($EmployeeMaps, $EmployeeRow) {
    $document = Normalize-Document ([string]$EmployeeRow.($EmployeeCedulaField))
    if (-not [string]::IsNullOrWhiteSpace($document)) {
        $EmployeeMaps.ByDocument[$document] = $EmployeeRow
    }

    $nameKey = Normalize-Key ([string]$EmployeeRow.($script:EmployeeNameField))
    if (-not [string]::IsNullOrWhiteSpace($nameKey)) {
        $EmployeeMaps.ByName[$nameKey] = $EmployeeRow
    }
}

function New-MissingEmployee($EmployeeMetadata, $Row) {
    $payload = @{}
    Add-Value $payload $script:EmployeeNameField $Row.EmployeeName 200
    Add-Value $payload $EmployeeCedulaField (Normalize-Document $Row.Document) 50

    $created = Invoke-DataverseJson `
        -Method "POST" `
        -Path "/api/data/v9.2/$($EmployeeMetadata.EntitySetName)" `
        -Body $payload `
        -ExtraHeaders @{ Prefer = "return=representation" }

    if ($null -ne $created -and -not [string]::IsNullOrWhiteSpace([string]$created.($script:EmployeeIdField))) {
        return $created
    }

    $document = Normalize-Document $Row.Document
    $filter = "$EmployeeCedulaField eq '$(Escape-ODataLiteral $document)'"
    $select = "$script:EmployeeIdField,$script:EmployeeNameField,$EmployeeCedulaField"
    $found = Invoke-DataverseJson -Method "GET" -Path "/api/data/v9.2/$($EmployeeMetadata.EntitySetName)?`$select=$select&`$filter=$([uri]::EscapeDataString($filter))&`$top=1"
    return @($found.value) | Select-Object -First 1
}

function Get-AbsenceReason($Row) {
    $items = @()
    if ($Row.SickLeave66 -gt 0) { $items += "incapacidad" }
    if ($Row.Vacation -gt 0) { $items += "vacaciones" }
    if ($Row.BereavementLeave -gt 0) { $items += "calamidad" }
    if ($items.Count -eq 1) { return $items[0] }
    if ($items.Count -gt 1) { return "mixto" }
    return ""
}

function Find-ExistingPayrollRecord($Metadata, [string]$EmployeeId, [string]$PeriodKey) {
    $employeeLookup = "_$($script:PayrollEmployeeLookupField)_value"
    $filter = "$employeeLookup eq $EmployeeId and contains($script:PayrollNameField,'$(Escape-ODataLiteral $PeriodKey)')"
    $select = "$($Metadata.PrimaryIdAttribute),$script:PayrollNameField,$employeeLookup"
    $path = "/api/data/v9.2/$($Metadata.EntitySetName)?`$select=$select&`$filter=$([uri]::EscapeDataString($filter))&`$top=2"
    $result = Invoke-DataverseJson -Method "GET" -Path $path
    return @($result.value) | Select-Object -First 1
}

function Build-Payload($Row, $EmployeeRow, [string]$WorkbookLeaf) {
    $employeeId = [string]$EmployeeRow.($script:EmployeeIdField)
    $employeeName = First-NonEmpty ([string]$EmployeeRow.($script:EmployeeNameField)) $Row.EmployeeName
    $absencePayment = [Math]::Round(($Row.SickLeave66 + $Row.Vacation + $Row.BereavementLeave), 2)
    $otherDeductions = [Math]::Round(($Row.SalaryDiscount + $Row.PensionSolidarity), 2)
    $recordName = "Nomina $($Row.PeriodKey) - $employeeName"
    if ($recordName.Length -gt 120) {
        $recordName = $recordName.Substring(0, 120)
    }

    $payload = @{}
    Add-Value $payload $script:PayrollNameField $recordName 120
    Add-Value $payload $script:PayrollPaymentDateField $Row.PeriodEnd
    Add-Money $payload $script:PayrollSalaryBaseField $Row.Salary
    Add-Money $payload $script:PayrollConnectivityAllowanceField $Row.ConnectivityAllowance
    Add-Value $payload $script:PayrollPeriodDaysField $Row.PeriodDays
    Add-Money $payload $script:PayrollAbsencePaymentField $absencePayment
    Add-Value $payload $script:PayrollAbsenceReasonField (Get-AbsenceReason $Row) 100
    Add-Money $payload $script:PayrollBonusComplianceField $Row.ComplianceBonus
    Add-Money $payload $script:PayrollCommissionsCopiersField 0
    Add-Money $payload $script:PayrollCommissionsCloudField 0
    Add-Money $payload $script:PayrollCommissionsField $Row.SalaryCommission
    Add-Money $payload $script:PayrollGrossSalaryField $Row.TotalIncome
    Add-Money $payload $script:PayrollHealthField $Row.Health
    Add-Money $payload $script:PayrollPensionField $Row.Pension
    Add-Money $payload $script:PayrollOtherDeductionsField $otherDeductions
    Add-Money $payload $script:PayrollLoanField $Row.Loan
    Add-Money $payload $script:PayrollCuentaDeCobroField 0
    Add-Money $payload $script:PayrollWithholdingField $Row.Withholding
    Add-Money $payload $script:PayrollExternalWithholdingField 0
    Add-Money $payload $script:PayrollNetAmountField $Row.NetPay
    Add-Money $payload $script:PayrollNetCuentaDeCobroField 0

    Add-Value $payload "cr07a_identificacionempleadonomina" (Normalize-Document $Row.Document) 50
    Add-Value $payload "cr07a_nocontrato" $Row.ContractNumber 80
    Add-Value $payload "cr07a_periodoinicio" $Row.PeriodStart
    Add-Value $payload "cr07a_periodofin" $Row.PeriodEnd
    Add-Money $payload "cr07a_incapacidadenfermedadgeneral66" $Row.SickLeave66
    Add-Money $payload "cr07a_vacacionesdisfrutadas" $Row.Vacation
    Add-Money $payload "cr07a_licenciaporluto" $Row.BereavementLeave
    Add-Money $payload "cr07a_bonificacionesocasionales" $Row.OccasionalBonuses
    Add-Money $payload "cr07a_interesescesantias" $Row.SeveranceInterest
    Add-Money $payload "cr07a_descuentosalarial" $Row.SalaryDiscount
    Add-Money $payload "cr07a_fondosolidaridadpensional" $Row.PensionSolidarity
    Add-Money $payload "cr07a_totaldeducciones" $Row.TotalDeductions
    Add-Value $payload "cr07a_archivoorigennomina" $WorkbookLeaf 260
    Add-Value $payload "cr07a_hojaorigen" $Row.SourceSheet 120
    Add-Value $payload "cr07a_filaorigen" $Row.SourceRow
    Add-Value $payload "cr07a_claveorigen" "$($Row.SourceSheet):$($Row.SourceRow):$(Normalize-Document $Row.Document)" 200
    Add-Value $payload "$($script:PayrollEmployeeLookupNavigationProperty)@odata.bind" "/$($script:EmployeeTableSetName)($employeeId)"

    return $payload
}

function Import-PayrollRows($PayrollMetadata, $EmployeeMetadata, [object[]]$Rows, [string]$WorkbookLeaf) {
    $employeeMaps = Get-EmployeeMaps $EmployeeMetadata
    Write-Host "Empleados disponibles en Dataverse: $($employeeMaps.Count)."

    $created = 0
    $updated = 0
    $employeesCreated = 0
    $errors = New-Object System.Collections.Generic.List[string]

    foreach ($row in $Rows) {
        $employee = Resolve-Employee $row $employeeMaps
        if ($null -eq $employee -and $CreateMissingEmployees) {
            Write-Host "  Creando empleado faltante: $($row.EmployeeName) / $($row.Document)"
            $employee = New-MissingEmployee $EmployeeMetadata $row
            if ($null -ne $employee) {
                Add-EmployeeToMaps $employeeMaps $employee
                $employeesCreated++
            }
        }

        if ($null -eq $employee) {
            [void]$errors.Add("Sin empleado: $($row.SourceSheet) fila $($row.SourceRow) - $($row.EmployeeName) / $($row.Document)")
            continue
        }

        $employeeId = [string]$employee.($script:EmployeeIdField)
        $payload = Build-Payload $row $employee $WorkbookLeaf
        $existing = Find-ExistingPayrollRecord $PayrollMetadata $employeeId $row.PeriodKey
        if ($null -eq $existing) {
            Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/$($PayrollMetadata.EntitySetName)" -Body $payload | Out-Null
            $created++
        } else {
            $recordId = [string]$existing.($PayrollMetadata.PrimaryIdAttribute)
            Invoke-Dataverse -Method "PATCH" -Path "/api/data/v9.2/$($PayrollMetadata.EntitySetName)($recordId)" -Body $payload | Out-Null
            $updated++
        }
    }

    return [pscustomobject]@{
        Created = $created
        Updated = $updated
        EmployeesCreated = $employeesCreated
        Errors = @($errors)
    }
}

function Get-ImportedSummary($PayrollMetadata) {
    $select = "$($PayrollMetadata.PrimaryIdAttribute),$script:PayrollNameField,$script:PayrollNetAmountField,cr07a_totaldeducciones,cr07a_hojaorigen"
    $filter = "cr07a_hojaorigen eq 'nomina enero' or cr07a_hojaorigen eq 'nomina febrero' or cr07a_hojaorigen eq 'NONIMA MARZO'"
    $rows = Get-AllDataverseRows "/api/data/v9.2/$($PayrollMetadata.EntitySetName)?`$select=$select&`$filter=$([uri]::EscapeDataString($filter))"
    $rows |
        Group-Object cr07a_hojaorigen |
        Sort-Object Name |
        ForEach-Object {
            [pscustomobject]@{
                Hoja = $_.Name
                Registros = $_.Count
                Neto = [Math]::Round((($_.Group | Measure-Object $script:PayrollNetAmountField -Sum).Sum), 2)
                Deducciones = [Math]::Round((($_.Group | Measure-Object cr07a_totaldeducciones -Sum).Sum), 2)
            }
        }
}

if ([string]::IsNullOrWhiteSpace($WorkbookPath)) {
    $WorkbookPath = Join-Path $env:USERPROFILE "Downloads\Reporte Neto a pagar-20260528121030790 (1).xlsx"
}

if (-not (Test-Path -LiteralPath "appsettings.json")) {
    throw "Ejecuta este script desde la raiz de CotizadorInterno.Web."
}

$appsettings = Get-Content -LiteralPath "appsettings.json" -Raw | ConvertFrom-Json
$secrets = Get-UserSecretMap

$BaseUrl = First-NonEmpty $BaseUrl $env:DATAVERSE_BASE_URL $secrets["Dataverse:BaseUrl"] (Get-JsonConfigValue $appsettings "Dataverse:BaseUrl")
$TenantId = First-NonEmpty $TenantId $env:DATAVERSE_TENANT_ID $secrets["Dataverse:TenantId"] (Get-JsonConfigValue $appsettings "Dataverse:TenantId") (Get-JsonConfigValue $appsettings "AzureAd:TenantId")
$ClientId = First-NonEmpty $ClientId $env:DATAVERSE_CLIENT_ID $secrets["Dataverse:ClientId"] (Get-JsonConfigValue $appsettings "Dataverse:ClientId") (Get-JsonConfigValue $appsettings "AzureAd:ClientId")
$ClientSecret = First-NonEmpty $ClientSecret $env:DATAVERSE_CLIENT_SECRET $secrets["Dataverse:ClientSecret"]

$script:BaseUrl = $BaseUrl.TrimEnd("/")
$script:TenantId = $TenantId
$script:ClientId = $ClientId
$script:ClientSecret = $ClientSecret
$script:AccessToken = Get-AccessToken

$script:EmployeeTableSetName = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:EmployeeTableSetName") "cr07a_empleados"
$script:EmployeeTableName = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:EmployeeTableName") "cr07a_empleado"
$script:EmployeeIdField = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:EmployeeIdField") "cr07a_empleadoid"
$script:EmployeeNameField = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:EmployeeNameField") "cr07a_nombrecompleto"
$script:PayrollTableSetName = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:PayrollTableSetName") "cr07a_nominas"
$script:PayrollTableName = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:PayrollTableName") "cr07a_nomina"
$script:PayrollIdField = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:PayrollIdField") "cr07a_nominaid"
$script:PayrollNameField = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:PayrollNameField") "cr07a_numerodenomina"
$script:PayrollEmployeeLookupField = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:PayrollEmployeeLookupField") "cr07a_idempleado"
$script:PayrollEmployeeLookupNavigationProperty = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:PayrollEmployeeLookupNavigationProperty") "cr07a_IDEmpleado"
$script:PayrollPaymentDateField = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:PayrollPaymentDateField") "cr07a_fechapago"
$script:PayrollSalaryBaseField = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:PayrollSalaryBaseField") "cr07a_sueldobase"
$script:PayrollConnectivityAllowanceField = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:PayrollConnectivityAllowanceField") "cr07a_auxilio"
$script:PayrollPeriodDaysField = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:PayrollPeriodDaysField") "cr07a_diasdelmes"
$script:PayrollAbsencePaymentField = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:PayrollAbsencePaymentField") "cr07a_valordiasnotrabajados"
$script:PayrollAbsenceReasonField = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:PayrollAbsenceReasonField") "cr07a_motivodiasnotrabajados"
$script:PayrollBonusComplianceField = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:PayrollBonusComplianceField") "cr07a_bonocumplimiento"
$script:PayrollCommissionsCopiersField = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:PayrollCommissionsCopiersField") "cr07a_comisionescopiers"
$script:PayrollCommissionsCloudField = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:PayrollCommissionsCloudField") "cr07a_comisionescloud"
$script:PayrollCommissionsField = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:PayrollCommissionsField") "cr07a_comisiones"
$script:PayrollGrossSalaryField = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:PayrollGrossSalaryField") "cr07a_sueldobruto"
$script:PayrollHealthField = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:PayrollHealthField") "cr07a_salud"
$script:PayrollPensionField = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:PayrollPensionField") "cr07a_pension"
$script:PayrollOtherDeductionsField = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:PayrollOtherDeductionsField") "cr07a_otrasdeducciones"
$script:PayrollLoanField = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:PayrollLoanField") "cr07a_prestamo"
$script:PayrollCuentaDeCobroField = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:PayrollCuentaDeCobroField") "cr07a_cuentadecobro"
$script:PayrollWithholdingField = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:PayrollWithholdingField") "cr07a_retencionenlafuentenomina"
$script:PayrollExternalWithholdingField = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:PayrollExternalWithholdingField") "cr07a_retencionenlafuenteexterno"
$script:PayrollNetAmountField = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:PayrollNetAmountField") "cr07a_montopagado"
$script:PayrollNetCuentaDeCobroField = First-NonEmpty (Get-JsonConfigValue $appsettings "Nomina:PayrollNetCuentaDeCobroField") "cr07a_montopagadocuentadecobro"

Write-Host "Ambiente Dataverse: $script:BaseUrl"
Write-Host "Tabla destino: $script:PayrollTableName"
Write-Host "Columnas adicionales"
Ensure-AdditionalColumns
Publish-PayrollTable

$payrollMetadata = Get-TableMetadata $script:PayrollTableName $script:PayrollTableSetName $script:PayrollIdField $script:PayrollNameField
$employeeMetadata = Get-TableMetadata $script:EmployeeTableName $script:EmployeeTableSetName $script:EmployeeIdField $script:EmployeeNameField

if ($OnlyEnsureSchema) {
    Write-Host "Esquema verificado. Tabla: $($payrollMetadata.EntitySetName)."
    return
}

if (-not (Test-Path -LiteralPath $WorkbookPath)) {
    throw "No existe el archivo: $WorkbookPath"
}

Write-Host "Leyendo workbook: $WorkbookPath"
$rows = Read-NominaRows $WorkbookPath
Write-Host "Filas historicas detectadas: $($rows.Count)."

$workbookLeaf = Split-Path -Leaf $WorkbookPath
$importResult = Import-PayrollRows $payrollMetadata $employeeMetadata $rows $workbookLeaf

Write-Host "Registros creados: $($importResult.Created)."
Write-Host "Registros actualizados: $($importResult.Updated)."
Write-Host "Empleados creados: $($importResult.EmployeesCreated)."
if ($importResult.Errors.Count -gt 0) {
    Write-Host "Errores de cruce:"
    $importResult.Errors | ForEach-Object { Write-Host "  $_" }
    throw "La importacion termino con empleados sin cruce."
}

Write-Host "Resumen importado:"
Get-ImportedSummary $payrollMetadata | Format-Table -AutoSize
