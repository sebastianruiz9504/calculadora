param(
    [string]$DataverseUrl = "https://orgc79ca19c.crm2.dynamics.com",
    [switch]$Apply
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw "Azure CLI no esta disponible. Ejecuta az login antes de actualizar Dataverse."
}

$DataverseUrl = $DataverseUrl.TrimEnd("/")
$token = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
if ([string]::IsNullOrWhiteSpace($token)) {
    throw "No fue posible obtener token para $DataverseUrl."
}

$headers = @{
    Authorization = "Bearer $token"
    Accept = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
}

$employeeTakenCalculatedField = "cr07a_diastomadoscalculados"
$employeeAvailableCalculatedField = "cr07a_diasdisponiblescalculados"

function Invoke-DvGetAll([string]$RelativePath) {
    $items = @()
    $url = "$DataverseUrl$RelativePath"

    while (-not [string]::IsNullOrWhiteSpace($url)) {
        $response = Invoke-RestMethod -Method Get -Uri $url -Headers $headers
        $items += @($response.value)
        $url = $response."@odata.nextLink"
    }

    return $items
}

function Invoke-DvPatch([string]$RelativePath, [object]$Body) {
    $uri = "$DataverseUrl$RelativePath"
    $patchHeaders = $headers.Clone()
    $patchHeaders["If-Match"] = "*"
    $json = $Body | ConvertTo-Json -Depth 10
    Invoke-RestMethod -Method Patch -Uri $uri -Headers $patchHeaders -ContentType "application/json; charset=utf-8" -Body $json | Out-Null
}

function Invoke-DvPost([string]$RelativePath, [object]$Body = $null) {
    $uri = "$DataverseUrl$RelativePath"
    if ($null -eq $Body) {
        return Invoke-RestMethod -Method Post -Uri $uri -Headers $headers
    }

    $json = $Body | ConvertTo-Json -Depth 30
    return Invoke-RestMethod -Method Post -Uri $uri -Headers $headers -ContentType "application/json; charset=utf-8" -Body $json
}

function Invoke-DvGetOrNull([string]$RelativePath) {
    $uri = "$DataverseUrl$RelativePath"
    try {
        return Invoke-RestMethod -Method Get -Uri $uri -Headers $headers
    }
    catch {
        $response = $_.Exception.Response
        if ($response -and [int]$response.StatusCode -eq 404) {
            return $null
        }

        throw
    }
}

function New-DvLabel([string]$Text) {
    return @{
        "@odata.type" = "Microsoft.Dynamics.CRM.Label"
        LocalizedLabels = @(
            @{
                "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                Label = $Text
                LanguageCode = 3082
            }
        )
    }
}

function New-DvRequiredLevel {
    return @{
        Value = "None"
        CanBeChanged = $true
        ManagedPropertyLogicalName = "canmodifyrequirementlevelsettings"
    }
}

function Test-DvAttributeExists([string]$LogicalName) {
    $entity = [uri]::EscapeDataString("LogicalName='cr07a_empleado'")
    $attribute = [uri]::EscapeDataString("LogicalName='$LogicalName'")
    $result = Invoke-DvGetOrNull "/api/data/v9.2/EntityDefinitions($entity)/Attributes($attribute)?`$select=LogicalName"
    return $null -ne $result
}

function New-DvDecimalAttribute([string]$LogicalName, [string]$SchemaName, [string]$Label, [string]$Description) {
    if (Test-DvAttributeExists $LogicalName) {
        return $false
    }

    $entity = [uri]::EscapeDataString("LogicalName='cr07a_empleado'")
    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.DecimalAttributeMetadata"
        SchemaName = $SchemaName
        DisplayName = New-DvLabel $Label
        Description = New-DvLabel $Description
        RequiredLevel = New-DvRequiredLevel
        MinValue = -100000000000
        MaxValue = 100000000000
        Precision = 2
    }

    Invoke-DvPost "/api/data/v9.2/EntityDefinitions($entity)/Attributes" $payload | Out-Null
    return $true
}

function Ensure-EmployeeVacationCalculatedColumns {
    $createdTaken = New-DvDecimalAttribute `
        -LogicalName $employeeTakenCalculatedField `
        -SchemaName "cr07a_DiasTomadosCalculados" `
        -Label "Dias tomados calculados" `
        -Description "Suma calculada de dias registrados en solicitudes de vacaciones."

    $createdAvailable = New-DvDecimalAttribute `
        -LogicalName $employeeAvailableCalculatedField `
        -SchemaName "cr07a_DiasDisponiblesCalculados" `
        -Label "Dias disponibles calculados" `
        -Description "Saldo calculado como dias acumulados/base menos dias tomados calculados."

    if ($createdTaken -or $createdAvailable) {
        Invoke-DvPost "/api/data/v9.2/PublishAllXml" @{} | Out-Null
        Start-Sleep -Seconds 8
    }
}

function Get-EasterSunday([int]$Year) {
    $a = $Year % 19
    $b = [math]::Floor($Year / 100)
    $c = $Year % 100
    $d = [math]::Floor($b / 4)
    $e = $b % 4
    $f = [math]::Floor(($b + 8) / 25)
    $g = [math]::Floor(($b - $f + 1) / 3)
    $h = (19 * $a + $b - $d - $g + 15) % 30
    $i = [math]::Floor($c / 4)
    $k = $c % 4
    $l = (32 + 2 * $e + 2 * $i - $h - $k) % 7
    $m = [math]::Floor(($a + 11 * $h + 22 * $l) / 451)
    $month = [math]::Floor(($h + $l - 7 * $m + 114) / 31)
    $day = (($h + $l - 7 * $m + 114) % 31) + 1

    return [datetime]::new($Year, $month, $day)
}

function Move-HolidayToMonday([datetime]$Date) {
    $offset = ([int][DayOfWeek]::Monday - [int]$Date.DayOfWeek + 7) % 7
    return $Date.AddDays($offset).Date
}

function Get-ColombiaHolidays([int]$StartYear, [int]$EndYear) {
    $set = New-Object "System.Collections.Generic.HashSet[string]"

    for ($year = $StartYear; $year -le $EndYear; $year++) {
        $easter = Get-EasterSunday $year
        $dates = @(
            [datetime]::new($year, 1, 1),
            (Move-HolidayToMonday ([datetime]::new($year, 1, 6))),
            (Move-HolidayToMonday ([datetime]::new($year, 3, 19))),
            $easter.AddDays(-3),
            $easter.AddDays(-2),
            [datetime]::new($year, 5, 1),
            (Move-HolidayToMonday $easter.AddDays(39)),
            (Move-HolidayToMonday $easter.AddDays(60)),
            (Move-HolidayToMonday $easter.AddDays(68)),
            (Move-HolidayToMonday ([datetime]::new($year, 6, 29))),
            [datetime]::new($year, 7, 20),
            [datetime]::new($year, 8, 7),
            (Move-HolidayToMonday ([datetime]::new($year, 8, 15))),
            (Move-HolidayToMonday ([datetime]::new($year, 10, 12))),
            (Move-HolidayToMonday ([datetime]::new($year, 11, 1))),
            (Move-HolidayToMonday ([datetime]::new($year, 11, 11))),
            [datetime]::new($year, 12, 8),
            [datetime]::new($year, 12, 25)
        )

        foreach ($date in $dates) {
            [void]$set.Add($date.Date.ToString("yyyy-MM-dd"))
        }
    }

    return $set
}

function Count-VacationBusinessDays([datetime]$StartDate, [datetime]$EndDate) {
    if ($EndDate.Date -lt $StartDate.Date) {
        return $null
    }

    $holidays = Get-ColombiaHolidays $StartDate.Year $EndDate.Year
    $days = 0
    for ($current = $StartDate.Date; $current -le $EndDate.Date; $current = $current.AddDays(1)) {
        if ($current.DayOfWeek -eq [DayOfWeek]::Saturday -or $current.DayOfWeek -eq [DayOfWeek]::Sunday) {
            continue
        }

        if ($holidays.Contains($current.ToString("yyyy-MM-dd"))) {
            continue
        }

        $days++
    }

    return $days
}

function ConvertTo-VacationDate([string]$RawValue) {
    if ([string]::IsNullOrWhiteSpace($RawValue)) {
        return $null
    }

    if ($RawValue.Length -ge 10) {
        return [datetime]::ParseExact($RawValue.Substring(0, 10), "yyyy-MM-dd", [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return [datetime]::Parse($RawValue, [System.Globalization.CultureInfo]::InvariantCulture).Date
}

$requestPath = "/api/data/v9.2/cr07a_solicituddevacacioneses?`$select=cr07a_solicituddevacacionesid,cr07a_fechainicio,cr07a_fechafin,cr07a_cantidaddedias"
$rows = @(Invoke-DvGetAll $requestPath)
$results = @()

foreach ($row in $rows) {
    $id = [string]$row.cr07a_solicituddevacacionesid
    $startRaw = [string]$row.cr07a_fechainicio
    $endRaw = [string]$row.cr07a_fechafin
    $current = if ($null -eq $row.cr07a_cantidaddedias) { $null } else { [decimal]$row.cr07a_cantidaddedias }
    $computed = $null
    $status = "ok"

    if ([string]::IsNullOrWhiteSpace($startRaw) -or [string]::IsNullOrWhiteSpace($endRaw)) {
        $status = "missing-date"
    }
    else {
        $computed = Count-VacationBusinessDays (ConvertTo-VacationDate $startRaw) (ConvertTo-VacationDate $endRaw)
        if ($null -eq $computed) {
            $status = "invalid-range"
        }
    }

    $needsChange = $status -eq "ok" -and ($null -eq $current -or $current -ne [decimal]$computed)

    $results += [pscustomobject]@{
        Id = $id
        Start = if ([string]::IsNullOrWhiteSpace($startRaw)) { "" } else { (ConvertTo-VacationDate $startRaw).ToString("yyyy-MM-dd") }
        End = if ([string]::IsNullOrWhiteSpace($endRaw)) { "" } else { (ConvertTo-VacationDate $endRaw).ToString("yyyy-MM-dd") }
        Current = $current
        Computed = $computed
        Status = $status
        NeedsChange = $needsChange
    }
}

$badRows = @($results | Where-Object { $_.Status -ne "ok" })
if ($badRows.Count -gt 0) {
    $badRows | Format-Table -AutoSize
    throw "Hay solicitudes sin fechas validas. No se actualizo Dataverse."
}

if ($Apply) {
    foreach ($item in $results) {
        Invoke-DvPatch "/api/data/v9.2/cr07a_solicituddevacacioneses($($item.Id))" @{
            cr07a_cantidaddedias = [decimal]$item.Computed
        }
    }
}

$verificationRows = if ($Apply) {
    @(Invoke-DvGetAll $requestPath)
} else {
    $rows
}

$verified = @()
foreach ($row in $verificationRows) {
    $startRaw = [string]$row.cr07a_fechainicio
    $endRaw = [string]$row.cr07a_fechafin
    $computed = Count-VacationBusinessDays (ConvertTo-VacationDate $startRaw) (ConvertTo-VacationDate $endRaw)
    $current = if ($null -eq $row.cr07a_cantidaddedias) { $null } else { [decimal]$row.cr07a_cantidaddedias }
    $verified += [pscustomobject]@{
        Id = [string]$row.cr07a_solicituddevacacionesid
        Current = $current
        Computed = $computed
        Matches = $current -eq [decimal]$computed
    }
}

if ($Apply) {
    Ensure-EmployeeVacationCalculatedColumns
}

$employeeRequestPath = "/api/data/v9.2/cr07a_solicituddevacacioneses?`$select=_cr07a_idempleado_value,cr07a_cantidaddedias"
$employeePath = "/api/data/v9.2/cr07a_empleados?`$select=cr07a_empleadoid,cr07a_nombrecompleto,cr07a_diasdevacacionesdisponibles,$employeeTakenCalculatedField,$employeeAvailableCalculatedField"
$requestRowsForEmployees = @(Invoke-DvGetAll $employeeRequestPath)
$employeeRows = @(Invoke-DvGetAll $employeePath)
$takenByEmployee = @{}

foreach ($requestRow in $requestRowsForEmployees) {
    $employeeId = [string]$requestRow."_cr07a_idempleado_value"
    if ([string]::IsNullOrWhiteSpace($employeeId)) {
        continue
    }

    $days = if ($null -eq $requestRow.cr07a_cantidaddedias) { [decimal]0 } else { [decimal]$requestRow.cr07a_cantidaddedias }
    if (-not $takenByEmployee.ContainsKey($employeeId)) {
        $takenByEmployee[$employeeId] = [decimal]0
    }

    $takenByEmployee[$employeeId] = [decimal]$takenByEmployee[$employeeId] + $days
}

$employeeResults = @()
foreach ($employeeRow in $employeeRows) {
    $employeeId = [string]$employeeRow.cr07a_empleadoid
    $baseDays = if ($null -eq $employeeRow.cr07a_diasdevacacionesdisponibles) { [decimal]0 } else { [decimal]$employeeRow.cr07a_diasdevacacionesdisponibles }
    $rawCurrentTaken = $employeeRow.PSObject.Properties[$employeeTakenCalculatedField].Value
    $rawCurrentAvailable = $employeeRow.PSObject.Properties[$employeeAvailableCalculatedField].Value
    $currentTaken = if ($null -eq $rawCurrentTaken) { $null } else { [decimal]$rawCurrentTaken }
    $currentAvailable = if ($null -eq $rawCurrentAvailable) { $null } else { [decimal]$rawCurrentAvailable }
    $computedTaken = if ($takenByEmployee.ContainsKey($employeeId)) { [decimal]$takenByEmployee[$employeeId] } else { [decimal]0 }
    $available = $baseDays - $computedTaken
    $needsTakenChange = $null -eq $currentTaken -or $currentTaken -ne $computedTaken -or $null -eq $currentAvailable -or $currentAvailable -ne $available

    if ($Apply) {
        $employeePayload = @{}
        $employeePayload[$employeeTakenCalculatedField] = $computedTaken
        $employeePayload[$employeeAvailableCalculatedField] = $available
        Invoke-DvPatch "/api/data/v9.2/cr07a_empleados($employeeId)" $employeePayload
    }

    $employeeResults += [pscustomobject]@{
        Id = $employeeId
        Employee = [string]$employeeRow.cr07a_nombrecompleto
        BaseDays = $baseDays
        CurrentTaken = $currentTaken
        ComputedTaken = $computedTaken
        Available = $available
        CurrentAvailable = $currentAvailable
        NeedsTakenChange = $needsTakenChange
    }
}

$verifiedEmployees = if ($Apply) {
    @(Invoke-DvGetAll $employeePath)
} else {
    $employeeRows
}

$employeeVerification = @()
foreach ($employeeRow in $verifiedEmployees) {
    $employeeId = [string]$employeeRow.cr07a_empleadoid
    $baseDays = if ($null -eq $employeeRow.cr07a_diasdevacacionesdisponibles) { [decimal]0 } else { [decimal]$employeeRow.cr07a_diasdevacacionesdisponibles }
    $rawCurrentTaken = $employeeRow.PSObject.Properties[$employeeTakenCalculatedField].Value
    $rawCurrentAvailable = $employeeRow.PSObject.Properties[$employeeAvailableCalculatedField].Value
    $currentTaken = if ($null -eq $rawCurrentTaken) { $null } else { [decimal]$rawCurrentTaken }
    $currentAvailable = if ($null -eq $rawCurrentAvailable) { $null } else { [decimal]$rawCurrentAvailable }
    $computedTaken = if ($takenByEmployee.ContainsKey($employeeId)) { [decimal]$takenByEmployee[$employeeId] } else { [decimal]0 }
    $computedAvailable = $baseDays - $computedTaken
    $employeeVerification += [pscustomobject]@{
        Id = $employeeId
        CurrentTaken = $currentTaken
        ComputedTaken = $computedTaken
        CurrentAvailable = $currentAvailable
        ComputedAvailable = $computedAvailable
        Matches = $currentTaken -eq $computedTaken -and $currentAvailable -eq $computedAvailable
    }
}

[pscustomobject]@{
    Mode = if ($Apply) { "Apply" } else { "Preview" }
    Total = $results.Count
    Computable = @($results | Where-Object { $_.Status -eq "ok" }).Count
    ChangedOrWouldChange = @($results | Where-Object { $_.NeedsChange }).Count
    VerifiedMatches = @($verified | Where-Object { $_.Matches }).Count
    VerifiedMismatches = @($verified | Where-Object { -not $_.Matches }).Count
    EmployeesTotal = $employeeResults.Count
    EmployeesChangedOrWouldChange = @($employeeResults | Where-Object { $_.NeedsTakenChange }).Count
    EmployeesVerifiedMatches = @($employeeVerification | Where-Object { $_.Matches }).Count
    EmployeesVerifiedMismatches = @($employeeVerification | Where-Object { -not $_.Matches }).Count
} | Format-List

$results |
    Sort-Object @{ Expression = "NeedsChange"; Descending = $true }, Start |
    Select-Object -First 15 |
    Format-Table -AutoSize

$employeeResults |
    Sort-Object @{ Expression = "NeedsTakenChange"; Descending = $true }, Employee |
    Select-Object -First 15 |
    Format-Table -AutoSize
