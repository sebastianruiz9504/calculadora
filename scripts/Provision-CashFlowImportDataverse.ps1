param(
    [string]$BaseUrl = "",
    [string]$TenantId = "",
    [string]$ClientId = "",
    [string]$ClientSecret = ""
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

function New-RequiredNone {
    return @{
        "Value" = "None"
        "CanBeChanged" = $true
        "ManagedPropertyLogicalName" = "canmodifyrequirementlevelsettings"
    }
}

function Invoke-Dataverse {
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
        if ($AllowNotFound -and $response -and [int]$response.StatusCode -eq 404) { return $null }
        throw
    }
}

function Test-EntityExists([string]$LogicalName) {
    $result = Invoke-Dataverse -Method "GET" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$LogicalName')?`$select=LogicalName" -AllowNotFound
    return $null -ne $result
}

function Test-AttributeExists([string]$EntityLogicalName, [string]$AttributeLogicalName) {
    $result = Invoke-Dataverse -Method "GET" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')?`$select=LogicalName" -AllowNotFound
    return $null -ne $result
}

function Get-AttributeMetadata([string]$EntityLogicalName, [string]$AttributeLogicalName) {
    return Invoke-Dataverse `
        -Method "GET" `
        -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')?`$select=LogicalName,AttributeType" `
        -AllowNotFound
}

function Assert-AttributeDefinition(
    [string]$EntityLogicalName,
    [string]$AttributeLogicalName,
    [string[]]$ExpectedTypes,
    [int]$MinimumLength = 0,
    [int]$MinimumPrecision = -1
) {
    $metadata = Get-AttributeMetadata $EntityLogicalName $AttributeLogicalName
    if ($null -eq $metadata) {
        throw "Falta la columna requerida $EntityLogicalName.$AttributeLogicalName."
    }

    $actualType = [string]$metadata.AttributeType
    if ($actualType -notin $ExpectedTypes) {
        throw "La columna $EntityLogicalName.$AttributeLogicalName tiene tipo $actualType; se esperaba $($ExpectedTypes -join ' o ')."
    }

    if ($MinimumLength -gt 0) {
        $castType = switch ($actualType) {
            "String" { "StringAttributeMetadata" }
            "Memo" { "MemoAttributeMetadata" }
            default { "" }
        }
        if ([string]::IsNullOrWhiteSpace($castType)) {
            throw "No se puede validar longitud para $EntityLogicalName.$AttributeLogicalName porque su tipo es $actualType."
        }

        $lengthMetadata = Invoke-Dataverse `
            -Method "GET" `
            -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')/Microsoft.Dynamics.CRM.${castType}?`$select=LogicalName,MaxLength" `
            -AllowNotFound
        $actualLength = if ($null -eq $lengthMetadata) { 0 } else { [int]$lengthMetadata.MaxLength }
        if ($actualLength -lt $MinimumLength) {
            throw "La columna $EntityLogicalName.$AttributeLogicalName admite $actualLength caracteres; se requieren al menos $MinimumLength."
        }
    }

    if ($MinimumPrecision -ge 0) {
        $castType = switch ($actualType) {
            "Decimal" { "DecimalAttributeMetadata" }
            "Money" { "MoneyAttributeMetadata" }
            default { "" }
        }
        if ([string]::IsNullOrWhiteSpace($castType)) {
            throw "No se puede validar precision para $EntityLogicalName.$AttributeLogicalName porque su tipo es $actualType."
        }

        $precisionMetadata = Invoke-Dataverse `
            -Method "GET" `
            -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')/Microsoft.Dynamics.CRM.${castType}?`$select=LogicalName,Precision" `
            -AllowNotFound
        $actualPrecision = if ($null -eq $precisionMetadata) { -1 } else { [int]$precisionMetadata.Precision }
        if ($actualPrecision -lt $MinimumPrecision) {
            throw "La columna $EntityLogicalName.$AttributeLogicalName tiene precision $actualPrecision; se requieren al menos $MinimumPrecision decimales."
        }
    }

    Write-Host "  Contrato OK: $EntityLogicalName.$AttributeLogicalName ($actualType)"
}

function Wait-Entity([string]$LogicalName) {
    for ($i = 0; $i -lt 40; $i++) {
        if (Test-EntityExists $LogicalName) { return }
        Start-Sleep -Seconds 3
    }
    throw "La tabla $LogicalName no estuvo disponible despues de crearla."
}

function Get-EntityKeyMetadata([string]$EntityLogicalName, [string]$SchemaName) {
    $result = Invoke-Dataverse -Method "GET" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$EntityLogicalName')/Keys?`$select=SchemaName,KeyAttributes,EntityKeyIndexStatus"
    return @($result.value | Where-Object { $_.SchemaName -eq $SchemaName }) | Select-Object -First 1
}

function Get-EntityKeyMetadataByAttribute([string]$EntityLogicalName, [string]$AttributeLogicalName) {
    $result = Invoke-Dataverse -Method "GET" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$EntityLogicalName')/Keys?`$select=SchemaName,KeyAttributes,EntityKeyIndexStatus"
    return @(
        $result.value | Where-Object {
            $keyAttributes = @($_.KeyAttributes | ForEach-Object { [string]$_ })
            $keyAttributes.Count -eq 1 -and
                [string]::Equals(
                    $keyAttributes[0],
                    $AttributeLogicalName,
                    [System.StringComparison]::OrdinalIgnoreCase)
        }
    ) | Select-Object -First 1
}

function Wait-EntityKey([string]$EntityLogicalName, [string]$SchemaName) {
    for ($i = 0; $i -lt 60; $i++) {
        $key = Get-EntityKeyMetadata $EntityLogicalName $SchemaName
        if ($null -eq $key) {
            Start-Sleep -Seconds 5
            continue
        }

        $status = [string]$key.EntityKeyIndexStatus
        if ($status -in @("2", "Active")) {
            Write-Host "  OK clave unica activa: $EntityLogicalName.$SchemaName"
            return
        }
        if ($status -in @("3", "Failed")) {
            throw "La clave unica $SchemaName fallo al crear su indice en $EntityLogicalName. Revisa si existen valores duplicados."
        }

        Start-Sleep -Seconds 5
    }

    throw "La clave unica $SchemaName no quedo activa en el tiempo esperado."
}

function Get-EntityAttributeValues([string]$EntityLogicalName, [string]$AttributeLogicalName) {
    $metadata = Invoke-Dataverse -Method "GET" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$EntityLogicalName')?`$select=EntitySetName,PrimaryIdAttribute"
    $path = "/api/data/v9.2/$($metadata.EntitySetName)?`$select=$($metadata.PrimaryIdAttribute),$AttributeLogicalName&`$filter=$AttributeLogicalName ne null&`$top=5000"
    $values = @()

    while (-not [string]::IsNullOrWhiteSpace($path)) {
        $page = Invoke-Dataverse -Method "GET" -Path $path
        foreach ($row in @($page.value)) {
            $property = $row.PSObject.Properties[$AttributeLogicalName]
            if ($null -ne $property -and -not [string]::IsNullOrWhiteSpace([string]$property.Value)) {
                $values += [string]$property.Value
            }
        }
        $path = [string]$page.'@odata.nextLink'
    }

    return $values
}

function Assert-NoDuplicateAttributeValues([string]$EntityLogicalName, [string]$AttributeLogicalName) {
    $values = @(Get-EntityAttributeValues $EntityLogicalName $AttributeLogicalName)
    $duplicates = @($values | Group-Object | Where-Object { $_.Count -gt 1 })
    if ($duplicates.Count -gt 0) {
        $sample = ($duplicates | Select-Object -First 5 | ForEach-Object { $_.Name }) -join ", "
        throw "No se puede crear la clave unica en ${EntityLogicalName}: hay $($duplicates.Count) valor(es) duplicados en $AttributeLogicalName. Ejemplos: $sample"
    }

    Write-Host "  Auditoria OK: $($values.Count) valores sin duplicados en $EntityLogicalName.$AttributeLogicalName"
}

function Normalize-DianSearchText([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return "" }

    $builder = [System.Text.StringBuilder]::new()
    $decomposed = $Value.Normalize([System.Text.NormalizationForm]::FormD)
    foreach ($character in $decomposed.ToCharArray()) {
        if ([System.Globalization.CharUnicodeInfo]::GetUnicodeCategory($character) -ne [System.Globalization.UnicodeCategory]::NonSpacingMark) {
            [void]$builder.Append($character)
        }
    }

    return (($builder.ToString().ToUpperInvariant() -replace '[^A-Z0-9]+', ' ').Trim() -replace '\s+', ' ')
}

function Normalize-DianIdentityPart([string]$Value) {
    $builder = [System.Text.StringBuilder]::new()
    foreach ($character in ([string]$Value).ToCharArray()) {
        if ([char]::IsLetterOrDigit($character)) {
            [void]$builder.Append([char]::ToUpperInvariant($character))
        }
    }
    return $builder.ToString()
}

function Normalize-DianFolio([string]$Value) {
    $digits = ([string]$Value) -replace '\D', ''
    if ([string]::IsNullOrWhiteSpace($digits)) {
        return Normalize-DianIdentityPart $Value
    }

    $normalized = $digits.TrimStart('0')
    return $(if ([string]::IsNullOrWhiteSpace($normalized)) { "0" } else { $normalized })
}

function Get-ColombianNitCheckDigit([string]$Identification) {
    $digits = $Identification -replace '\D', ''
    $weights = @(71, 67, 59, 53, 47, 43, 41, 37, 29, 23, 19, 17, 13, 7, 3)
    $offset = [Math]::Max(0, $weights.Count - $digits.Length)
    $sum = 0
    for ($index = 0; $index -lt $digits.Length -and ($index + $offset) -lt $weights.Count; $index++) {
        $sum += ([int][string]$digits[$index]) * $weights[$index + $offset]
    }

    $remainder = $sum % 11
    return $(if ($remainder -gt 1) { 11 - $remainder } else { $remainder })
}

function Get-DianCanonicalSupplierTaxId([string]$SupplierNit) {
    $digits = $SupplierNit -replace '\D', ''
    if ($digits.Length -ne 10) { return $digits }

    $baseNit = $digits.Substring(0, 9)
    $checkDigit = [int][string]$digits[9]
    return $(if ((Get-ColombianNitCheckDigit $baseNit) -eq $checkDigit) { $baseNit } else { $digits })
}

function Get-DianSiigoBusinessKey(
    [string]$SupplierNit,
    [string]$Prefix,
    [string]$Folio
) {
    $supplier = Get-DianCanonicalSupplierTaxId $SupplierNit
    $normalizedPrefix = Normalize-DianIdentityPart $Prefix
    $normalizedFolio = Normalize-DianFolio $Folio
    if ([string]::IsNullOrWhiteSpace($supplier) -or
        [string]::IsNullOrWhiteSpace($normalizedPrefix) -or
        [string]::IsNullOrWhiteSpace($normalizedFolio)) {
        return ""
    }

    $canonical = "$supplier|$normalizedPrefix|$normalizedFolio"
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($canonical))
        return "dian-siigo:$([System.BitConverter]::ToString($hash).Replace('-', '').ToLowerInvariant())"
    }
    finally {
        $sha.Dispose()
    }
}

function Backfill-DianSiigoBusinessKeys([string]$EntityLogicalName) {
    $metadata = Invoke-Dataverse -Method "GET" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$EntityLogicalName')?`$select=EntitySetName,PrimaryIdAttribute"
    $fields = @(
        $metadata.PrimaryIdAttribute,
        "cr07a_tipodocumento",
        "cr07a_grupodian",
        "cr07a_cufecude",
        "cr07a_prefijo",
        "cr07a_folio",
        "cr07a_nitemisor",
        "cr07a_siigobusinesskey"
    ) -join ','
    $path = "/api/data/v9.2/$($metadata.EntitySetName)?`$select=$fields&`$filter=cr07a_cufecude ne null&`$top=5000"
    $candidates = @()

    while (-not [string]::IsNullOrWhiteSpace($path)) {
        $page = Invoke-Dataverse -Method "GET" -Path $path
        foreach ($row in @($page.value)) {
            $type = Normalize-DianSearchText ([string]$row.cr07a_tipodocumento)
            $group = Normalize-DianSearchText ([string]$row.cr07a_grupodian)
            if (-not $type.Contains("FACTURA ELECTRONICA") -or
                $type.Contains("NOTA") -or
                $type.Contains("APPLICATION RESPONSE") -or
                -not $group.Contains("RECIBID") -or
                $group.Contains("EMITID")) {
                continue
            }

            $idProperty = $row.PSObject.Properties[[string]$metadata.PrimaryIdAttribute]
            if ($null -eq $idProperty -or [string]::IsNullOrWhiteSpace([string]$idProperty.Value)) { continue }

            $keyParameters = @{
                SupplierNit = [string]$row.cr07a_nitemisor
                Prefix = [string]$row.cr07a_prefijo
                Folio = [string]$row.cr07a_folio
            }
            $key = Get-DianSiigoBusinessKey @keyParameters
            if ([string]::IsNullOrWhiteSpace($key)) {
                Write-Warning "No se pudo derivar SiigoBusinessKey para $($idProperty.Value); la fila quedara bloqueada individualmente hasta reimportarla/corregirla."
                continue
            }

            $candidates += [pscustomobject]@{
                Id = [string]$idProperty.Value
                Cufe = [string]$row.cr07a_cufecude
                Key = $key
                CurrentKey = [string]$row.cr07a_siigobusinesskey
            }
        }
        $path = [string]$page.'@odata.nextLink'
    }

    $collisions = @($candidates | Group-Object Key | Where-Object { $_.Count -gt 1 })
    if ($collisions.Count -gt 0) {
        $sample = ($collisions | Select-Object -First 5 | ForEach-Object {
            "$($_.Name) => $(($_.Group | ForEach-Object { $_.Cufe }) -join ', ')"
        }) -join '; '
        throw "No se puede completar el backfill de SiigoBusinessKey: hay $($collisions.Count) identidad(es) de factura duplicadas. Corrige los CUFE en conflicto. Ejemplos: $sample"
    }

    $updated = 0
    foreach ($candidate in $candidates) {
        if ($candidate.CurrentKey -eq $candidate.Key) { continue }
        Invoke-Dataverse -Method "PATCH" -Path "/api/data/v9.2/$($metadata.EntitySetName)($($candidate.Id))" -Body @{
            "cr07a_siigobusinesskey" = $candidate.Key
        } | Out-Null
        $updated++
    }

    Write-Host "  Backfill SiigoBusinessKey: $updated actualizada(s), $($candidates.Count) factura(s) elegible(s) auditada(s)."
}

function New-AlternateKey(
    [string]$EntityLogicalName,
    [string]$SchemaName,
    [string]$Label,
    [string]$AttributeLogicalName
) {
    Assert-NoDuplicateAttributeValues $EntityLogicalName $AttributeLogicalName
    $existing = Get-EntityKeyMetadata $EntityLogicalName $SchemaName
    if ($null -ne $existing) {
        $keyAttributes = @($existing.KeyAttributes | ForEach-Object { [string]$_ })
        if ($keyAttributes.Count -ne 1 -or
            ![string]::Equals($keyAttributes[0], $AttributeLogicalName, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "La clave unica $EntityLogicalName.$SchemaName existe, pero no apunta exclusivamente a $AttributeLogicalName."
        }
        Wait-EntityKey $EntityLogicalName $SchemaName
        return
    }

    $existingByAttribute = Get-EntityKeyMetadataByAttribute $EntityLogicalName $AttributeLogicalName
    if ($null -ne $existingByAttribute) {
        $existingSchemaName = [string]$existingByAttribute.SchemaName
        Write-Host "  Reutilizando clave unica equivalente: $EntityLogicalName.$existingSchemaName"
        Wait-EntityKey $EntityLogicalName $existingSchemaName
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.EntityKeyMetadata"
        "SchemaName" = $SchemaName
        "DisplayName" = New-Label $Label
        "KeyAttributes" = @($AttributeLogicalName)
    }
    Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$EntityLogicalName')/Keys" -Body $payload | Out-Null
    Write-Host "  Creando clave unica: $EntityLogicalName.$SchemaName"
    Wait-EntityKey $EntityLogicalName $SchemaName
}

function New-PrimaryNameAttribute {
    return @{
        "@odata.type" = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "AttributeType" = "String"
        "AttributeTypeName" = New-Value "StringType"
        "SchemaName" = "cr07a_Name"
        "DisplayName" = New-Label "Nombre"
        "Description" = New-Label "Nombre"
        "IsPrimaryName" = $true
        "RequiredLevel" = New-RequiredNone
        "MaxLength" = 200
        "FormatName" = New-Value "Text"
    }
}

function New-Table([string]$SchemaName, [string]$DisplayName, [string]$DisplayCollectionName) {
    $logicalName = $SchemaName.ToLowerInvariant()
    if (Test-EntityExists $logicalName) {
        Write-Host "OK tabla existente: $logicalName"
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.EntityMetadata"
        "Attributes" = @(New-PrimaryNameAttribute)
        "SchemaName" = $SchemaName
        "DisplayName" = New-Label $DisplayName
        "DisplayCollectionName" = New-Label $DisplayCollectionName
        "Description" = New-Label $DisplayName
        "OwnershipType" = "UserOwned"
        "IsActivity" = $false
        "HasActivities" = $false
        "HasNotes" = $true
    }
    Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/EntityDefinitions" -Body $payload | Out-Null
    Write-Host "Creada tabla: $logicalName"
    Wait-Entity $logicalName
}

function New-StringAttribute([string]$Entity, [string]$SchemaName, [string]$Label, [int]$MaxLength = 200) {
    $logicalName = $SchemaName.ToLowerInvariant()
    if (Test-AttributeExists $Entity $logicalName) {
        Write-Host "  OK columna existente: $Entity.$logicalName"
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "AttributeType" = "String"
        "AttributeTypeName" = New-Value "StringType"
        "SchemaName" = $SchemaName
        "DisplayName" = New-Label $Label
        "Description" = New-Label $Label
        "RequiredLevel" = New-RequiredNone
        "MaxLength" = $MaxLength
        "FormatName" = New-Value "Text"
    }
    Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$Entity')/Attributes" -Body $payload | Out-Null
    Write-Host "  Creada columna: $Entity.$logicalName"
}

function New-MemoAttribute([string]$Entity, [string]$SchemaName, [string]$Label, [int]$MaxLength = 4000) {
    $logicalName = $SchemaName.ToLowerInvariant()
    if (Test-AttributeExists $Entity $logicalName) {
        Write-Host "  OK columna existente: $Entity.$logicalName"
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
        "AttributeType" = "Memo"
        "AttributeTypeName" = New-Value "MemoType"
        "SchemaName" = $SchemaName
        "DisplayName" = New-Label $Label
        "Description" = New-Label $Label
        "RequiredLevel" = New-RequiredNone
        "Format" = "TextArea"
        "ImeMode" = "Disabled"
        "IsLocalizable" = $false
        "MaxLength" = $MaxLength
    }
    Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$Entity')/Attributes" -Body $payload | Out-Null
    Write-Host "  Creada columna: $Entity.$logicalName"
}

function New-IntegerAttribute([string]$Entity, [string]$SchemaName, [string]$Label, [int]$MinValue = 0, [int]$MaxValue = 2147483647) {
    $logicalName = $SchemaName.ToLowerInvariant()
    if (Test-AttributeExists $Entity $logicalName) {
        Write-Host "  OK columna existente: $Entity.$logicalName"
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
        "AttributeType" = "Integer"
        "AttributeTypeName" = New-Value "IntegerType"
        "SchemaName" = $SchemaName
        "DisplayName" = New-Label $Label
        "Description" = New-Label $Label
        "RequiredLevel" = New-RequiredNone
        "Format" = "None"
        "MinValue" = $MinValue
        "MaxValue" = $MaxValue
        "SourceTypeMask" = 0
    }
    Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$Entity')/Attributes" -Body $payload | Out-Null
    Write-Host "  Creada columna: $Entity.$logicalName"
}

function New-DecimalAttribute([string]$Entity, [string]$SchemaName, [string]$Label, [decimal]$MinValue = -100000000000, [decimal]$MaxValue = 100000000000, [int]$Precision = 2) {
    $logicalName = $SchemaName.ToLowerInvariant()
    if (Test-AttributeExists $Entity $logicalName) {
        Write-Host "  OK columna existente: $Entity.$logicalName"
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.DecimalAttributeMetadata"
        "AttributeType" = "Decimal"
        "AttributeTypeName" = New-Value "DecimalType"
        "SchemaName" = $SchemaName
        "DisplayName" = New-Label $Label
        "Description" = New-Label $Label
        "RequiredLevel" = New-RequiredNone
        "MinValue" = $MinValue
        "MaxValue" = $MaxValue
        "Precision" = $Precision
    }
    Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$Entity')/Attributes" -Body $payload | Out-Null
    Write-Host "  Creada columna: $Entity.$logicalName"
}

function New-DateAttribute([string]$Entity, [string]$SchemaName, [string]$Label) {
    $logicalName = $SchemaName.ToLowerInvariant()
    if (Test-AttributeExists $Entity $logicalName) {
        Write-Host "  OK columna existente: $Entity.$logicalName"
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
        "AttributeType" = "DateTime"
        "AttributeTypeName" = New-Value "DateTimeType"
        "SchemaName" = $SchemaName
        "DisplayName" = New-Label $Label
        "Description" = New-Label $Label
        "RequiredLevel" = New-RequiredNone
        "Format" = "DateOnly"
        "DateTimeBehavior" = New-Value "DateOnly"
    }
    Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$Entity')/Attributes" -Body $payload | Out-Null
    Write-Host "  Creada columna: $Entity.$logicalName"
}

function New-DateTimeAttribute([string]$Entity, [string]$SchemaName, [string]$Label) {
    $logicalName = $SchemaName.ToLowerInvariant()
    if (Test-AttributeExists $Entity $logicalName) {
        Write-Host "  OK columna existente: $Entity.$logicalName"
        return
    }

    $payload = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
        "AttributeType" = "DateTime"
        "AttributeTypeName" = New-Value "DateTimeType"
        "SchemaName" = $SchemaName
        "DisplayName" = New-Label $Label
        "Description" = New-Label $Label
        "RequiredLevel" = New-RequiredNone
        "Format" = "DateAndTime"
        "DateTimeBehavior" = New-Value "UserLocal"
    }
    Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/EntityDefinitions(LogicalName='$Entity')/Attributes" -Body $payload | Out-Null
    Write-Host "  Creada columna: $Entity.$logicalName"
}

$appsettings = Get-Content -LiteralPath "appsettings.json" -Raw | ConvertFrom-Json
$secrets = Get-UserSecretMap

$BaseUrl = First-NonEmpty $BaseUrl $env:DATAVERSE_BASE_URL $secrets["Dataverse:BaseUrl"] (Get-JsonConfigValue $appsettings "Dataverse:BaseUrl")
$TenantId = First-NonEmpty $TenantId $env:DATAVERSE_TENANT_ID $secrets["Dataverse:TenantId"] (Get-JsonConfigValue $appsettings "Dataverse:TenantId") (Get-JsonConfigValue $appsettings "AzureAd:TenantId")
$ClientId = First-NonEmpty $ClientId $env:DATAVERSE_CLIENT_ID $secrets["Dataverse:ClientId"] (Get-JsonConfigValue $appsettings "Dataverse:ClientId") (Get-JsonConfigValue $appsettings "AzureAd:ClientId")
$ClientSecret = First-NonEmpty $ClientSecret $env:DATAVERSE_CLIENT_SECRET $secrets["Dataverse:ClientSecret"] (Get-JsonConfigValue $appsettings "Dataverse:ClientSecret")

if ([string]::IsNullOrWhiteSpace($BaseUrl) -or [string]::IsNullOrWhiteSpace($TenantId) -or [string]::IsNullOrWhiteSpace($ClientId) -or [string]::IsNullOrWhiteSpace($ClientSecret)) {
    throw "Faltan credenciales Dataverse."
}

$script:BaseUrl = $BaseUrl.TrimEnd("/")
$tokenResponse = Invoke-RestMethod -Method "POST" -Uri "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token" -ContentType "application/x-www-form-urlencoded" -Body @{
    client_id = $ClientId
    client_secret = $ClientSecret
    scope = "$script:BaseUrl/.default"
    grant_type = "client_credentials"
}
$script:AccessToken = $tokenResponse.access_token

New-Table "cr07a_TrasladoInternoFlujoCaja" "Traslado interno flujo caja" "Traslados internos flujo caja"

$movement = "cr07a_movimientobancario"
New-StringAttribute $movement "cr07a_OrigenFlujo" "Origen flujo" 50
New-StringAttribute $movement "cr07a_BancoCuentaCodigo" "Banco cuenta codigo" 50
New-StringAttribute $movement "cr07a_BancoCuentaNombre" "Banco cuenta nombre" 250
New-StringAttribute $movement "cr07a_Destinatario" "Destinatario" 250
New-StringAttribute $movement "cr07a_BancoDestino" "Banco destino" 250
New-StringAttribute $movement "cr07a_TipoDocumento" "Tipo documento" 150
New-MemoAttribute $movement "cr07a_Observaciones" "Observaciones" 4000
New-StringAttribute $movement "cr07a_SiigoEstado" "Siigo estado" 100
New-StringAttribute $movement "cr07a_SiigoDocumentName" "Siigo document name" 150
New-StringAttribute $movement "cr07a_CuentaContableCodigo" "Cuenta contable codigo" 50
New-StringAttribute $movement "cr07a_CuentaContableNombre" "Cuenta contable nombre" 250
New-StringAttribute $movement "cr07a_ClaveExterna" "Clave externa" 200
New-StringAttribute $movement "cr07a_ArchivoOrigen" "Archivo origen" 250
New-StringAttribute $movement "cr07a_TablaOrigen" "Tabla origen" 100
New-IntegerAttribute $movement "cr07a_FilaOrigen" "Fila origen" 0 1000000
New-StringAttribute $movement "cr07a_HashOrigen" "Hash origen" 100

$transfer = "cr07a_trasladointernoflujocaja"
New-DateAttribute $transfer "cr07a_Fecha" "Fecha"
New-StringAttribute $transfer "cr07a_OrigenFlujo" "Origen flujo" 50
New-StringAttribute $transfer "cr07a_FlujoDesde" "Flujo desde" 50
New-StringAttribute $transfer "cr07a_FlujoHacia" "Flujo hacia" 50
New-DecimalAttribute $transfer "cr07a_Entrada" "Entrada"
New-DecimalAttribute $transfer "cr07a_Salida" "Salida"
New-DecimalAttribute $transfer "cr07a_Valor" "Valor"
New-MemoAttribute $transfer "cr07a_Descripcion" "Descripcion" 4000
New-StringAttribute $transfer "cr07a_Destinatario" "Destinatario" 250
New-StringAttribute $transfer "cr07a_BancoDestino" "Banco destino" 250
New-StringAttribute $transfer "cr07a_TipoDocumento" "Tipo documento" 150
New-MemoAttribute $transfer "cr07a_Observaciones" "Observaciones" 4000
New-StringAttribute $transfer "cr07a_Estado" "Estado" 100
New-StringAttribute $transfer "cr07a_ClaveExterna" "Clave externa" 200
New-StringAttribute $transfer "cr07a_ArchivoOrigen" "Archivo origen" 250
New-StringAttribute $transfer "cr07a_TablaOrigen" "Tabla origen" 100
New-IntegerAttribute $transfer "cr07a_FilaOrigen" "Fila origen" 0 1000000
New-StringAttribute $transfer "cr07a_HashOrigen" "Hash origen" 100

Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/PublishAllXml" -Body @{} | Out-Null
New-AlternateKey $movement "cr07a_MovimientoBancoClaveExternaKey" "Movimiento bancario por clave externa" "cr07a_claveexterna"
New-AlternateKey $transfer "cr07a_TrasladoFlujoClaveExternaKey" "Traslado por clave externa" "cr07a_claveexterna"
$dianSupplierExpense = "cr07a_gastodelaempresa"
if (Test-EntityExists $dianSupplierExpense) {
    New-StringAttribute $dianSupplierExpense "cr07a_ExcelKey" "ExcelKey DIAN" 200
    New-DateTimeAttribute $dianSupplierExpense "cr07a_FechaRecepcion" "Fecha recepcion DIAN"
    New-StringAttribute $dianSupplierExpense "cr07a_CuentaContableCodigo" "Cuenta contable codigo" 50
    New-StringAttribute $dianSupplierExpense "cr07a_CuentaContableNombre" "Cuenta contable nombre" 250
    New-StringAttribute $dianSupplierExpense "cr07a_EstadoAutomatizacion" "Estado automatizacion" 100
    New-MemoAttribute $dianSupplierExpense "cr07a_MotivoRevision" "Motivo revision" 4000
    New-MemoAttribute $dianSupplierExpense "cr07a_RetencionesJson" "Detalle de retenciones" 100000
    New-DecimalAttribute $dianSupplierExpense "cr07a_Iva" "IVA"
    New-StringAttribute $dianSupplierExpense "cr07a_SiigoDocumentId" "Siigo document id" 150
    New-StringAttribute $dianSupplierExpense "cr07a_SiigoDocumentName" "Siigo document name" 150
    New-StringAttribute $dianSupplierExpense "cr07a_SiigoPaymentId" "Siigo payment id" 150
    New-StringAttribute $dianSupplierExpense "cr07a_SiigoPaymentName" "Siigo payment name" 150
    New-MemoAttribute $dianSupplierExpense "cr07a_SiigoRespuesta" "Respuesta documento Siigo" 100000
    New-MemoAttribute $dianSupplierExpense "cr07a_SiigoPaymentResponse" "Respuesta pago Siigo" 100000
    New-StringAttribute $dianSupplierExpense "cr07a_SiigoBusinessKey" "Identidad unica factura Siigo" 150
    New-StringAttribute $dianSupplierExpense "cr07a_CufeCude" "CUFE/CUDE" 200
    New-StringAttribute $dianSupplierExpense "cr07a_FuenteAutomatizacion" "Fuente automatizacion" 100
    New-StringAttribute $dianSupplierExpense "cr07a_SiigoProveedorId" "Siigo proveedor id" 150
    New-StringAttribute $dianSupplierExpense "cr07a_SiigoProveedorNombre" "Siigo proveedor nombre" 250

    Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/PublishAllXml" -Body @{} | Out-Null

    $requiredDianFields = @(
        "cr07a_excelkey",
        "cr07a_fecharecepcion",
        "cr07a_cuentacontablecodigo",
        "cr07a_cuentacontablenombre",
        "cr07a_estadoautomatizacion",
        "cr07a_motivorevision",
        "cr07a_retencionesjson",
        "cr07a_iva",
        "cr07a_siigodocumentid",
        "cr07a_siigodocumentname",
        "cr07a_siigopaymentid",
        "cr07a_siigopaymentname",
        "cr07a_siigorespuesta",
        "cr07a_siigopaymentresponse",
        "cr07a_siigobusinesskey",
        "cr07a_cufecude",
        "cr07a_fuenteautomatizacion",
        "cr07a_siigoproveedorid"
    )
    $missingDianFields = @($requiredDianFields | Where-Object { -not (Test-AttributeExists $dianSupplierExpense $_) })
    if ($missingDianFields.Count -gt 0) {
        throw "Faltan columnas durables en ${dianSupplierExpense}: $($missingDianFields -join ', '). La importacion DIAN/Siigo queda bloqueada."
    }

    $cuentaCobroExpenseFieldContracts = @(
        @{ LogicalName = "cr07a_excelkey"; ExpectedTypes = @("String"); MinimumLength = 200 },
        @{ LogicalName = "cr07a_cuentacontablecodigo"; ExpectedTypes = @("String"); MinimumLength = 50 },
        @{ LogicalName = "cr07a_cuentacontablenombre"; ExpectedTypes = @("String"); MinimumLength = 250 },
        @{ LogicalName = "cr07a_estadoautomatizacion"; ExpectedTypes = @("String"); MinimumLength = 100 },
        @{ LogicalName = "cr07a_motivorevision"; ExpectedTypes = @("Memo"); MinimumLength = 4000 },
        @{ LogicalName = "cr07a_retencionesjson"; ExpectedTypes = @("Memo"); MinimumLength = 100000 },
        @{ LogicalName = "cr07a_iva"; ExpectedTypes = @("Decimal", "Money"); MinimumLength = 0; MinimumPrecision = 2 },
        @{ LogicalName = "cr07a_siigodocumentid"; ExpectedTypes = @("String"); MinimumLength = 150 },
        @{ LogicalName = "cr07a_siigodocumentname"; ExpectedTypes = @("String"); MinimumLength = 150 },
        @{ LogicalName = "cr07a_siigopaymentid"; ExpectedTypes = @("String"); MinimumLength = 150 },
        @{ LogicalName = "cr07a_siigopaymentname"; ExpectedTypes = @("String"); MinimumLength = 150 },
        @{ LogicalName = "cr07a_siigorespuesta"; ExpectedTypes = @("Memo"); MinimumLength = 100000 },
        @{ LogicalName = "cr07a_siigopaymentresponse"; ExpectedTypes = @("Memo"); MinimumLength = 100000 }
    )
    foreach ($fieldContract in $cuentaCobroExpenseFieldContracts) {
        Assert-AttributeDefinition `
            -EntityLogicalName $dianSupplierExpense `
            -AttributeLogicalName $fieldContract.LogicalName `
            -ExpectedTypes $fieldContract.ExpectedTypes `
            -MinimumLength $fieldContract.MinimumLength `
            -MinimumPrecision $(if ($null -eq $fieldContract.MinimumPrecision) { -1 } else { $fieldContract.MinimumPrecision })
    }

    Backfill-DianSiigoBusinessKeys $dianSupplierExpense
    Assert-NoDuplicateAttributeValues $dianSupplierExpense "cr07a_cufecude"

    New-AlternateKey $dianSupplierExpense "cr07a_GastoEmpresaDianExcelKey" "Documento DIAN por CUFE" "cr07a_excelkey"
    New-AlternateKey $dianSupplierExpense "cr07a_GastoEmpresaSiigoBusinessKey" "Factura DIAN por identidad Siigo" "cr07a_siigobusinesskey"
    New-AlternateKey $dianSupplierExpense "cr07a_GastoEmpresaSiigoDocumentIdKey" "Documento DIAN por compra Siigo" "cr07a_siigodocumentid"
}
else {
    throw "No existe la tabla $dianSupplierExpense requerida por la importacion DIAN."
}
Invoke-Dataverse -Method "POST" -Path "/api/data/v9.2/PublishAllXml" -Body @{} | Out-Null
Write-Host "Listo: esquema de flujo de caja publicado."
