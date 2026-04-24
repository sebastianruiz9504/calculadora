using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.Hardware;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string HardwareTableLogicalName = "cr07a_hardware";
    private const string HardwarePrimaryNameLogicalName = "cr07a_name";
    private const string HardwareImportKeyLogicalName = "cr07a_importkey";
    private const string HardwareSourceFileNameLogicalName = "cr07a_sourcefilename";
    private const string HardwareSourceRowNumberLogicalName = "cr07a_sourcerownumber";
    private const string HardwareTableDisplayName = "Hardware";
    private const string HardwarePrimaryNameSchemaName = "cr07a_Name";
    private static readonly CultureInfo HardwareCulture = CultureInfo.GetCultureInfo("es-CO");
    private static readonly string[] HardwareDateFormats =
    {
        "dd/MM/yyyy",
        "d/M/yyyy",
        "dd-MM-yyyy",
        "d-M-yyyy",
        "yyyy-MM-dd",
        "yyyy/MM/dd"
    };

    private static readonly IReadOnlyList<HardwareManagedColumnDefinition> HardwareSystemColumns = new[]
    {
        new HardwareManagedColumnDefinition
        {
            DisplayLabel = "Import Key",
            SourceHeader = "Import Key",
            LogicalName = HardwareImportKeyLogicalName,
            SchemaName = "cr07a_ImportKey",
            Kind = HardwareAttributeKind.String,
            MaxLength = 128,
            IsSystemColumn = true
        },
        new HardwareManagedColumnDefinition
        {
            DisplayLabel = "Source File Name",
            SourceHeader = "Source File Name",
            LogicalName = HardwareSourceFileNameLogicalName,
            SchemaName = "cr07a_SourceFileName",
            Kind = HardwareAttributeKind.String,
            MaxLength = 200,
            IsSystemColumn = true
        },
        new HardwareManagedColumnDefinition
        {
            DisplayLabel = "Source Row Number",
            SourceHeader = "Source Row Number",
            LogicalName = HardwareSourceRowNumberLogicalName,
            SchemaName = "cr07a_SourceRowNumber",
            Kind = HardwareAttributeKind.Integer,
            IsSystemColumn = true
        }
    };

    public Task<HardwareCsvPreviewResultDto> PreviewHardwareCsvAsync(
        string fileName,
        byte[] content,
        CancellationToken ct = default)
    {
        var document = ParseHardwareCsv(fileName, content);
        return Task.FromResult(new HardwareCsvPreviewResultDto
        {
            FileName = document.FileName,
            TableLogicalName = HardwareTableLogicalName,
            TableDisplayName = HardwareTableDisplayName,
            DetectedDelimiterLabel = GetHardwareDelimiterLabel(document.Delimiter),
            TotalRows = document.Rows.Count,
            TotalColumns = document.Columns.Count,
            SystemColumnsCount = HardwareSystemColumns.Count,
            SystemColumns = HardwareSystemColumns.Select(static item => item.LogicalName).ToList(),
            Columns = document.Columns
                .Select(column => new HardwareCsvColumnDto
                {
                    Index = column.Index,
                    SourceHeader = column.SourceHeader,
                    DisplayLabel = column.DisplayLabel,
                    LogicalName = column.LogicalName,
                    SchemaName = column.SchemaName,
                    DataverseType = GetHardwareAttributeKindLabel(column.Kind),
                    ExampleValue = column.ExampleValue
                })
                .ToList(),
            Message = document.Rows.Count == 0
                ? "Se detectaron columnas, pero no hay filas con datos para importar."
                : $"Vista previa lista: {document.Rows.Count} fila(s) y {document.Columns.Count} columna(s)."
        });
    }

    public async Task<HardwareProvisionResultDto> ProvisionHardwareCsvAsync(
        string fileName,
        byte[] content,
        CancellationToken ct = default)
    {
        var document = ParseHardwareCsv(fileName, content);
        if (document.Rows.Count == 0)
            throw new InvalidOperationException("El archivo no tiene filas con datos para importar.");

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var user = httpContext.User;
        var tableCreated = false;
        if (await TryResolveHardwareEntityMetadataAsync(user, ct) is null)
        {
            await CreateHardwareEntityAsync(user, ct);
            tableCreated = true;
        }

        var existingAttributes = await LoadHardwareAttributesAsync(user, ct);
        var createdColumns = new List<string>();
        var existingColumns = new List<string>();

        foreach (var column in document.Columns.Concat(HardwareSystemColumns))
        {
            var matchedAttribute = FindMatchingHardwareAttribute(existingAttributes, column);
            if (matchedAttribute is not null)
            {
                column.ResolvedLogicalName = matchedAttribute.LogicalName;
                existingColumns.Add(matchedAttribute.LogicalName);
                continue;
            }

            await CreateHardwareAttributeAsync(column, user, ct);
            createdColumns.Add(column.LogicalName);
        }

        if (tableCreated || createdColumns.Count > 0)
        {
            await PublishHardwareEntityAsync(user, ct);
        }

        var metadata = await ResolveHardwareEntityMetadataAsync(user, ct);
        await ResolveHardwareColumnLogicalNamesAsync(document.Columns.Concat(HardwareSystemColumns).ToList(), user, ct);
        var importedCount = 0;
        var skippedDuplicates = 0;

        foreach (var row in document.Rows)
        {
            var importKey = ComputeHardwareImportKey(row);
            if (await HardwareRecordExistsAsync(metadata.EntitySetName, importKey, user, ct))
            {
                skippedDuplicates++;
                continue;
            }

            var payload = BuildHardwareRecordPayload(document, row, metadata.PrimaryNameField, importKey);
            await CallDataverseSendAsync($"/api/data/v9.2/{metadata.EntitySetName}", "POST", payload, user, ct);
            importedCount++;
        }

        return new HardwareProvisionResultDto
        {
            Message = BuildHardwareProvisionMessage(tableCreated, createdColumns.Count, importedCount, skippedDuplicates),
            TableLogicalName = metadata.LogicalName,
            EntitySetName = metadata.EntitySetName,
            TableCreated = tableCreated,
            CreatedColumnsCount = createdColumns.Count,
            ExistingColumnsCount = existingColumns.Count,
            ImportedCount = importedCount,
            SkippedDuplicatesCount = skippedDuplicates,
            CreatedColumns = createdColumns,
            ExistingColumns = existingColumns
        };
    }

    private static HardwareCsvDocument ParseHardwareCsv(string fileName, byte[] content)
    {
        if (content.Length == 0)
            throw new InvalidOperationException("El archivo seleccionado esta vacio.");

        var extension = Path.GetExtension(fileName ?? "");
        if (!string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El archivo debe estar en formato .csv.");

        var text = DecodeHardwareCsv(content);
        var delimiter = DetectHardwareDelimiter(text);
        var rawRows = ParseHardwareRows(text, delimiter);
        if (rawRows.Count == 0)
            throw new InvalidOperationException("No se encontraron encabezados en el archivo.");

        var headerRow = rawRows[0];
        var dataRows = rawRows
            .Skip(1)
            .Select(static row => row.ToList())
            .Where(row => row.Any(cell => !string.IsNullOrWhiteSpace(NormalizeHardwareCell(cell))))
            .ToList();

        var columnCount = Math.Max(
            headerRow.Count,
            dataRows.Count == 0 ? 0 : dataRows.Max(static row => row.Count));
        if (columnCount == 0)
            throw new InvalidOperationException("No se detectaron columnas validas en el CSV.");

        var columns = BuildHardwareColumnDefinitions(headerRow, dataRows, columnCount);
        if (columns.Count == 0)
            throw new InvalidOperationException("No se pudieron preparar columnas para Dataverse.");

        var rows = dataRows
            .Select((row, index) => new HardwareCsvRow
            {
                SourceRowNumber = index + 2,
                Values = PadHardwareRow(row, columnCount)
            })
            .ToList();

        return new HardwareCsvDocument
        {
            FileName = Path.GetFileName(fileName ?? "hardware.csv"),
            Delimiter = delimiter,
            Columns = columns,
            Rows = rows
        };
    }

    private static List<HardwareManagedColumnDefinition> BuildHardwareColumnDefinitions(
        IReadOnlyList<string> headerRow,
        IReadOnlyList<List<string>> dataRows,
        int columnCount)
    {
        var usedLogicalNames = new HashSet<string>(
            HardwareSystemColumns.Select(static item => item.LogicalName),
            StringComparer.OrdinalIgnoreCase);
        var columns = new List<HardwareManagedColumnDefinition>(columnCount);

        for (var index = 0; index < columnCount; index++)
        {
            var sourceHeader = index < headerRow.Count ? NormalizeHardwareCell(headerRow[index]) : "";
            var displayLabel = SanitizeHardwareHeader(sourceHeader, index + 1);
            var values = dataRows
                .Select(row => index < row.Count ? NormalizeHardwareCell(row[index]) : "")
                .ToList();
            var kind = InferHardwareColumnKind(displayLabel, values);
            var logicalName = CreateUniqueHardwareLogicalName(displayLabel, usedLogicalNames, index + 1);
            var schemaName = CreateHardwareSchemaName(logicalName);

            columns.Add(new HardwareManagedColumnDefinition
            {
                Index = index,
                SourceHeader = string.IsNullOrWhiteSpace(sourceHeader) ? $"Columna {index + 1}" : sourceHeader,
                DisplayLabel = displayLabel,
                LogicalName = logicalName,
                SchemaName = schemaName,
                Kind = kind,
                ExampleValue = values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "",
                MaxLength = DetermineHardwareMaxLength(kind, displayLabel, values)
            });
        }

        return columns;
    }

    private async Task<RhEntityMetadata?> TryResolveHardwareEntityMetadataAsync(
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var relativeUrl =
            $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(HardwareTableLogicalName)}')" +
            "?$select=LogicalName,EntitySetName,PrimaryIdAttribute,PrimaryNameAttribute";
        using var response = await CallRhDataverseResponseAsync(relativeUrl, "GET", user, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");

        using var doc = JsonDocument.Parse(body);
        var metadata = new RhEntityMetadata
        {
            LogicalName = FirstNonEmpty(ReadString(doc.RootElement, "LogicalName"), HardwareTableLogicalName),
            EntitySetName = ReadString(doc.RootElement, "EntitySetName").Trim(),
            PrimaryIdField = ReadString(doc.RootElement, "PrimaryIdAttribute").Trim(),
            PrimaryNameField = FirstNonEmpty(ReadString(doc.RootElement, "PrimaryNameAttribute"), HardwarePrimaryNameLogicalName)
        };

        if (string.IsNullOrWhiteSpace(metadata.EntitySetName) || string.IsNullOrWhiteSpace(metadata.PrimaryIdField))
            throw new InvalidOperationException("No fue posible resolver la metadata base de la tabla Hardware.");

        _rhEntityMetadataCache[HardwareTableLogicalName] = metadata;
        return metadata;
    }

    private async Task<RhEntityMetadata> ResolveHardwareEntityMetadataAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var metadata = await TryResolveHardwareEntityMetadataAsync(user, ct);
        if (metadata is null)
            throw new InvalidOperationException("La tabla Hardware aun no existe en Dataverse.");

        return metadata;
    }

    private async Task<List<HardwareAttributeMetadata>> LoadHardwareAttributesAsync(
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var relativeUrl =
            $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(HardwareTableLogicalName)}')" +
            "?$select=LogicalName&$expand=Attributes($select=LogicalName,SchemaName,AttributeType)";
        using var response = await CallRhDataverseResponseAsync(relativeUrl, "GET", user, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new List<HardwareAttributeMetadata>();

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");

        using var doc = JsonDocument.Parse(body);
        var result = new List<HardwareAttributeMetadata>();
        if (!doc.RootElement.TryGetProperty("Attributes", out var attributes)
            || attributes.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var attribute in attributes.EnumerateArray())
        {
            var logicalName = ReadString(attribute, "LogicalName").Trim();
            if (string.IsNullOrWhiteSpace(logicalName))
                continue;

            result.Add(new HardwareAttributeMetadata
            {
                LogicalName = logicalName,
                SchemaName = ReadString(attribute, "SchemaName").Trim(),
                AttributeType = ReadString(attribute, "AttributeType").Trim()
            });
        }

        return result;
    }

    private async Task ResolveHardwareColumnLogicalNamesAsync(
        IReadOnlyList<HardwareManagedColumnDefinition> columns,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        const int maxAttempts = 8;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var existingAttributes = await LoadHardwareAttributesAsync(user, ct);
            var unresolvedColumns = new List<HardwareManagedColumnDefinition>();

            foreach (var column in columns)
            {
                var matchedAttribute = FindMatchingHardwareAttribute(existingAttributes, column);
                if (matchedAttribute is null)
                {
                    unresolvedColumns.Add(column);
                    continue;
                }

                column.ResolvedLogicalName = matchedAttribute.LogicalName;
            }

            if (unresolvedColumns.Count == 0)
                return;

            if (attempt < maxAttempts)
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        var pendingColumns = columns
            .Where(column => string.IsNullOrWhiteSpace(column.ResolvedLogicalName))
            .Select(column => column.LogicalName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        throw new InvalidOperationException(
            $"Dataverse aun no expone estas columnas para importar: {string.Join(", ", pendingColumns)}. Intenta de nuevo en unos segundos.");
    }

    private static HardwareAttributeMetadata? FindMatchingHardwareAttribute(
        IEnumerable<HardwareAttributeMetadata> attributes,
        HardwareManagedColumnDefinition column)
    {
        var candidates = attributes.ToList();
        var exactLogical = candidates.FirstOrDefault(attribute =>
            string.Equals(attribute.LogicalName, column.LogicalName, StringComparison.OrdinalIgnoreCase));
        if (exactLogical is not null)
            return exactLogical;

        var exactSchema = candidates.FirstOrDefault(attribute =>
            !string.IsNullOrWhiteSpace(attribute.SchemaName)
            && string.Equals(attribute.SchemaName, column.SchemaName, StringComparison.OrdinalIgnoreCase));
        if (exactSchema is not null)
            return exactSchema;

        var normalizedTarget = NormalizeHardwareAttributeAlias(column.LogicalName);
        var normalizedSchema = NormalizeHardwareAttributeAlias(column.SchemaName);
        return candidates.FirstOrDefault(attribute =>
            NormalizeHardwareAttributeAlias(attribute.LogicalName) == normalizedTarget
            || NormalizeHardwareAttributeAlias(attribute.SchemaName) == normalizedTarget
            || NormalizeHardwareAttributeAlias(attribute.LogicalName) == normalizedSchema
            || NormalizeHardwareAttributeAlias(attribute.SchemaName) == normalizedSchema);
    }

    private async Task CreateHardwareEntityAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.EntityMetadata",
            ["Attributes"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["@odata.type"] = "Microsoft.Dynamics.CRM.StringAttributeMetadata",
                    ["AttributeType"] = "String",
                    ["AttributeTypeName"] = CreateHardwareValuePayload("StringType"),
                    ["Description"] = CreateHardwareLabelPayload("Nombre principal del registro de hardware."),
                    ["DisplayName"] = CreateHardwareLabelPayload("Nombre"),
                    ["IsPrimaryName"] = true,
                    ["RequiredLevel"] = CreateRequiredLevelNonePayload(),
                    ["SchemaName"] = HardwarePrimaryNameSchemaName,
                    ["FormatName"] = CreateHardwareValuePayload("Text"),
                    ["MaxLength"] = 200
                }
            },
            ["Description"] = CreateHardwareLabelPayload("Tabla creada desde el modulo Hardware para importar ventas y compras de hardware."),
            ["DisplayCollectionName"] = CreateHardwareLabelPayload(HardwareTableDisplayName),
            ["DisplayName"] = CreateHardwareLabelPayload(HardwareTableDisplayName),
            ["HasActivities"] = false,
            ["HasNotes"] = false,
            ["IsActivity"] = false,
            ["OwnershipType"] = "UserOwned",
            ["SchemaName"] = "cr07a_Hardware"
        };

        await CallDataverseSendAsync("/api/data/v9.2/EntityDefinitions", "POST", payload, user, ct);
    }

    private async Task CreateHardwareAttributeAsync(
        HardwareManagedColumnDefinition column,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        await CallDataverseSendAsync(
            $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(HardwareTableLogicalName)}')/Attributes",
            "POST",
            BuildHardwareAttributePayload(column),
            user,
            ct);
    }

    private async Task PublishHardwareEntityAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var publishXml =
            $"<importexportxml><entities><entity>{HardwareTableLogicalName}</entity></entities></importexportxml>";
        await CallDataverseSendAsync(
            "/api/data/v9.2/PublishXml",
            "POST",
            new Dictionary<string, object?> { ["ParameterXml"] = publishXml },
            user,
            ct);
    }

    private static object BuildHardwareAttributePayload(HardwareManagedColumnDefinition column)
    {
        return column.Kind switch
        {
            HardwareAttributeKind.Date => BuildHardwareDateAttributePayload(column),
            HardwareAttributeKind.Money => BuildHardwareMoneyAttributePayload(column),
            HardwareAttributeKind.Integer => BuildHardwareIntegerAttributePayload(column),
            HardwareAttributeKind.Decimal => BuildHardwareDecimalAttributePayload(column),
            HardwareAttributeKind.Boolean => BuildHardwareBooleanAttributePayload(column),
            HardwareAttributeKind.Memo => BuildHardwareMemoAttributePayload(column),
            _ => BuildHardwareStringAttributePayload(column)
        };
    }

    private static object BuildHardwareStringAttributePayload(HardwareManagedColumnDefinition column)
    {
        return new Dictionary<string, object?>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.StringAttributeMetadata",
            ["AttributeType"] = "String",
            ["AttributeTypeName"] = CreateHardwareValuePayload("StringType"),
            ["Description"] = CreateHardwareLabelPayload(BuildHardwareColumnDescription(column)),
            ["DisplayName"] = CreateHardwareLabelPayload(column.DisplayLabel),
            ["RequiredLevel"] = CreateRequiredLevelNonePayload(),
            ["SchemaName"] = column.SchemaName,
            ["FormatName"] = CreateHardwareValuePayload("Text"),
            ["MaxLength"] = Math.Clamp(column.MaxLength, 50, 4000)
        };
    }

    private static object BuildHardwareMemoAttributePayload(HardwareManagedColumnDefinition column)
    {
        return new Dictionary<string, object?>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.MemoAttributeMetadata",
            ["AttributeType"] = "Memo",
            ["AttributeTypeName"] = CreateHardwareValuePayload("MemoType"),
            ["Format"] = "TextArea",
            ["ImeMode"] = "Disabled",
            ["MaxLength"] = Math.Clamp(column.MaxLength, 200, 4000),
            ["IsLocalizable"] = false,
            ["Description"] = CreateHardwareLabelPayload(BuildHardwareColumnDescription(column)),
            ["DisplayName"] = CreateHardwareLabelPayload(column.DisplayLabel),
            ["RequiredLevel"] = CreateRequiredLevelNonePayload(),
            ["SchemaName"] = column.SchemaName
        };
    }

    private static object BuildHardwareMoneyAttributePayload(HardwareManagedColumnDefinition column)
    {
        return new Dictionary<string, object?>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.MoneyAttributeMetadata",
            ["AttributeType"] = "Money",
            ["AttributeTypeName"] = CreateHardwareValuePayload("MoneyType"),
            ["Description"] = CreateHardwareLabelPayload(BuildHardwareColumnDescription(column)),
            ["DisplayName"] = CreateHardwareLabelPayload(column.DisplayLabel),
            ["RequiredLevel"] = CreateRequiredLevelNonePayload(),
            ["SchemaName"] = column.SchemaName,
            ["PrecisionSource"] = 2
        };
    }

    private static object BuildHardwareDateAttributePayload(HardwareManagedColumnDefinition column)
    {
        return new Dictionary<string, object?>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata",
            ["AttributeType"] = "DateTime",
            ["AttributeTypeName"] = CreateHardwareValuePayload("DateTimeType"),
            ["Description"] = CreateHardwareLabelPayload(BuildHardwareColumnDescription(column)),
            ["DisplayName"] = CreateHardwareLabelPayload(column.DisplayLabel),
            ["RequiredLevel"] = CreateRequiredLevelNonePayload(),
            ["SchemaName"] = column.SchemaName,
            ["Format"] = "DateOnly"
        };
    }

    private static object BuildHardwareIntegerAttributePayload(HardwareManagedColumnDefinition column)
    {
        return new Dictionary<string, object?>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata",
            ["AttributeType"] = "Integer",
            ["AttributeTypeName"] = CreateHardwareValuePayload("IntegerType"),
            ["MaxValue"] = int.MaxValue,
            ["MinValue"] = int.MinValue,
            ["Format"] = "None",
            ["SourceTypeMask"] = 0,
            ["Description"] = CreateHardwareLabelPayload(BuildHardwareColumnDescription(column)),
            ["DisplayName"] = CreateHardwareLabelPayload(column.DisplayLabel),
            ["RequiredLevel"] = CreateRequiredLevelNonePayload(),
            ["SchemaName"] = column.SchemaName
        };
    }

    private static object BuildHardwareDecimalAttributePayload(HardwareManagedColumnDefinition column)
    {
        return new Dictionary<string, object?>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.DecimalAttributeMetadata",
            ["AttributeType"] = "Decimal",
            ["AttributeTypeName"] = CreateHardwareValuePayload("DecimalType"),
            ["MaxValue"] = 1000000000m,
            ["MinValue"] = -1000000000m,
            ["Precision"] = column.Precision,
            ["Description"] = CreateHardwareLabelPayload(BuildHardwareColumnDescription(column)),
            ["DisplayName"] = CreateHardwareLabelPayload(column.DisplayLabel),
            ["RequiredLevel"] = CreateRequiredLevelNonePayload(),
            ["SchemaName"] = column.SchemaName
        };
    }

    private static object BuildHardwareBooleanAttributePayload(HardwareManagedColumnDefinition column)
    {
        return new Dictionary<string, object?>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.BooleanAttributeMetadata",
            ["AttributeType"] = "Boolean",
            ["AttributeTypeName"] = CreateHardwareValuePayload("BooleanType"),
            ["DefaultValue"] = false,
            ["OptionSet"] = new Dictionary<string, object?>
            {
                ["TrueOption"] = new Dictionary<string, object?>
                {
                    ["Value"] = 1,
                    ["Label"] = CreateHardwareLabelPayload("Si")
                },
                ["FalseOption"] = new Dictionary<string, object?>
                {
                    ["Value"] = 0,
                    ["Label"] = CreateHardwareLabelPayload("No")
                },
                ["OptionSetType"] = "Boolean"
            },
            ["Description"] = CreateHardwareLabelPayload(BuildHardwareColumnDescription(column)),
            ["DisplayName"] = CreateHardwareLabelPayload(column.DisplayLabel),
            ["RequiredLevel"] = CreateRequiredLevelNonePayload(),
            ["SchemaName"] = column.SchemaName
        };
    }

    private static Dictionary<string, object?> CreateHardwareLabelPayload(string text)
    {
        return new Dictionary<string, object?>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.Label",
            ["LocalizedLabels"] = new object[]
            {
                CreateHardwareLocalizedLabel(text, 3082),
                CreateHardwareLocalizedLabel(text, 1033)
            }
        };
    }

    private static Dictionary<string, object?> CreateHardwareLocalizedLabel(string text, int languageCode)
    {
        return new Dictionary<string, object?>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.LocalizedLabel",
            ["Label"] = text,
            ["LanguageCode"] = languageCode
        };
    }

    private static Dictionary<string, object?> CreateRequiredLevelNonePayload()
    {
        return new Dictionary<string, object?>
        {
            ["Value"] = "None",
            ["CanBeChanged"] = true,
            ["ManagedPropertyLogicalName"] = "canmodifyrequirementlevelsettings"
        };
    }

    private static Dictionary<string, object?> CreateHardwareValuePayload(string value)
    {
        return new Dictionary<string, object?>
        {
            ["Value"] = value
        };
    }

    private async Task<bool> HardwareRecordExistsAsync(
        string entitySetName,
        string importKey,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var filter = $"{HardwareImportKeyLogicalName} eq '{EscapeOdataLiteral(importKey)}'";
        var relativeUrl =
            $"/api/data/v9.2/{entitySetName}?$select={HardwareImportKeyLogicalName}&$filter={Uri.EscapeDataString(filter)}&$top=1";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct);
        return items.Count > 0;
    }

    private static Dictionary<string, object?> BuildHardwareRecordPayload(
        HardwareCsvDocument document,
        HardwareCsvRow row,
        string primaryNameField,
        string importKey)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [string.IsNullOrWhiteSpace(primaryNameField) ? HardwarePrimaryNameLogicalName : primaryNameField] =
                BuildHardwareRecordName(document, row),
            [HardwareImportKeyLogicalName] = importKey,
            [HardwareSourceFileNameLogicalName] = document.FileName,
            [HardwareSourceRowNumberLogicalName] = row.SourceRowNumber
        };

        for (var index = 0; index < document.Columns.Count; index++)
        {
            var column = document.Columns[index];
            var rawValue = index < row.Values.Count ? row.Values[index] : "";
            var convertedValue = ConvertHardwareColumnValue(column, rawValue);
            if (convertedValue is null)
                continue;

            payload[FirstNonEmpty(column.ResolvedLogicalName, column.LogicalName)] = convertedValue;
        }

        return payload;
    }

    private static object? ConvertHardwareColumnValue(HardwareManagedColumnDefinition column, string? rawValue)
    {
        var normalized = NormalizeHardwareCell(rawValue);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        return column.Kind switch
        {
            HardwareAttributeKind.Date => ParseHardwareDate(normalized, column.DisplayLabel).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            HardwareAttributeKind.Money => RoundCurrency(ParseHardwareDecimal(normalized, column.DisplayLabel)),
            HardwareAttributeKind.Integer => ParseHardwareInteger(normalized, column.DisplayLabel),
            HardwareAttributeKind.Decimal => Math.Round(ParseHardwareDecimal(normalized, column.DisplayLabel), column.Precision, MidpointRounding.AwayFromZero),
            HardwareAttributeKind.Boolean => ParseHardwareBoolean(normalized, column.DisplayLabel),
            _ => normalized
        };
    }

    private static string BuildHardwareRecordName(HardwareCsvDocument document, HardwareCsvRow row)
    {
        var description = GetHardwareRowValue(document, row, "descripcion", "producto");
        var dateValue = GetHardwareRowValue(document, row, "fecha");
        if (TryParseHardwareDate(dateValue, out var parsedDate))
            dateValue = parsedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var identifier = GetHardwareRowValue(document, row, "orden", "factura", "remision");
        var parts = new[]
        {
            FirstNonEmpty(description, "Hardware"),
            string.IsNullOrWhiteSpace(dateValue) ? "" : dateValue,
            string.IsNullOrWhiteSpace(identifier) ? $"Fila {row.SourceRowNumber}" : identifier
        }
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .ToList();

        var value = string.Join(" - ", parts);
        return value.Length <= 200 ? value : value[..200];
    }

    private static string GetHardwareRowValue(HardwareCsvDocument document, HardwareCsvRow row, params string[] tokens)
    {
        foreach (var token in tokens.Where(token => !string.IsNullOrWhiteSpace(token)))
        {
            for (var index = 0; index < document.Columns.Count; index++)
            {
                var column = document.Columns[index];
                if (!column.DisplayLabel.Contains(token, StringComparison.OrdinalIgnoreCase)
                    && !column.SourceHeader.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = index < row.Values.Count ? NormalizeHardwareCell(row.Values[index]) : "";
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        for (var index = 0; index < document.Columns.Count; index++)
        {
            if (document.Columns[index].Kind is HardwareAttributeKind.String or HardwareAttributeKind.Memo)
            {
                var value = index < row.Values.Count ? NormalizeHardwareCell(row.Values[index]) : "";
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        return "";
    }

    private static string ComputeHardwareImportKey(HardwareCsvRow row)
    {
        var rawKey = string.Join(
            "|",
            new[] { row.SourceRowNumber.ToString(CultureInfo.InvariantCulture) }
                .Concat(row.Values.Select(value => NormalizeHardwareCell(value).ToLowerInvariant())));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string DecodeHardwareCsv(byte[] content)
    {
        try
        {
            return NormalizeHardwareText(new UTF8Encoding(false, true).GetString(content));
        }
        catch (DecoderFallbackException)
        {
            return NormalizeHardwareText(Encoding.Latin1.GetString(content));
        }
    }

    private static string NormalizeHardwareText(string text)
    {
        return (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimStart('\uFEFF');
    }

    private static char DetectHardwareDelimiter(string text)
    {
        var firstLine = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))
            ?? "";

        var semicolonCount = CountHardwareDelimiter(firstLine, ';');
        var commaCount = CountHardwareDelimiter(firstLine, ',');
        return semicolonCount > commaCount ? ';' : ',';
    }

    private static int CountHardwareDelimiter(string line, char delimiter)
    {
        var count = 0;
        var inQuotes = false;
        foreach (var character in line ?? string.Empty)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && character == delimiter)
                count++;
        }

        return count;
    }

    private static List<List<string>> ParseHardwareRows(string text, char delimiter)
    {
        var rows = new List<List<string>>();
        var currentRow = new List<string>();
        var currentValue = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            var next = index + 1 < text.Length ? text[index + 1] : '\0';

            if (character == '"')
            {
                if (inQuotes && next == '"')
                {
                    currentValue.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (!inQuotes && character == delimiter)
            {
                currentRow.Add(currentValue.ToString());
                currentValue.Clear();
                continue;
            }

            if (!inQuotes && character == '\n')
            {
                currentRow.Add(currentValue.ToString());
                rows.Add(currentRow);
                currentRow = new List<string>();
                currentValue.Clear();
                continue;
            }

            currentValue.Append(character);
        }

        if (currentValue.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(currentValue.ToString());
            rows.Add(currentRow);
        }

        return rows;
    }

    private static List<string> PadHardwareRow(IReadOnlyList<string> row, int columnCount)
    {
        var result = new List<string>(columnCount);
        for (var index = 0; index < columnCount; index++)
            result.Add(index < row.Count ? NormalizeHardwareCell(row[index]) : "");

        return result;
    }

    private static string NormalizeHardwareCell(string? value)
    {
        return (value ?? string.Empty)
            .Replace('\u00A0', ' ')
            .Replace('\uFFFD', ' ')
            .Trim();
    }

    private static string SanitizeHardwareHeader(string rawHeader, int position)
    {
        var normalized = NormalizeHardwareCell(rawHeader);
        normalized = string.Join(
            " ",
            normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return string.IsNullOrWhiteSpace(normalized) ? $"Columna {position}" : normalized;
    }

    private static HardwareAttributeKind InferHardwareColumnKind(string header, IReadOnlyList<string> values)
    {
        var nonEmptyValues = values
            .Select(NormalizeHardwareCell)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        var normalizedHeader = RemoveHardwareDiacritics(header).ToLowerInvariant();

        if (LooksLikeHardwareIdentifierField(normalizedHeader))
            return HardwareAttributeKind.String;

        if (LooksLikeHardwareDateField(normalizedHeader)
            && (nonEmptyValues.Count == 0 || nonEmptyValues.All(TryParseHardwareDate)))
        {
            return HardwareAttributeKind.Date;
        }

        if (LooksLikeHardwareMoneyField(normalizedHeader)
            && (nonEmptyValues.Count == 0 || nonEmptyValues.All(TryParseHardwareDecimalValue)))
        {
            return HardwareAttributeKind.Money;
        }

        if (LooksLikeHardwarePercentField(normalizedHeader)
            && (nonEmptyValues.Count == 0 || nonEmptyValues.All(TryParseHardwareDecimalValue)))
        {
            return HardwareAttributeKind.Decimal;
        }

        if (LooksLikeHardwareLongTextField(normalizedHeader))
            return HardwareAttributeKind.Memo;

        if (nonEmptyValues.Count > 0 && nonEmptyValues.All(TryParseHardwareBoolean))
            return HardwareAttributeKind.Boolean;

        if (nonEmptyValues.Count > 0 && nonEmptyValues.All(TryParseHardwareDate))
            return HardwareAttributeKind.Date;

        if (nonEmptyValues.Count > 0 && nonEmptyValues.All(TryParseHardwareInteger))
            return LooksLikeHardwareQuantityField(normalizedHeader)
                ? HardwareAttributeKind.Integer
                : HardwareAttributeKind.Decimal;

        if (nonEmptyValues.Count > 0 && nonEmptyValues.All(TryParseHardwareDecimalValue))
            return HardwareAttributeKind.Decimal;

        if (nonEmptyValues.Any(static value => value.Length > 120))
            return HardwareAttributeKind.Memo;

        return HardwareAttributeKind.String;
    }

    private static bool LooksLikeHardwareIdentifierField(string normalizedHeader)
    {
        return normalizedHeader.StartsWith("no ", StringComparison.Ordinal)
            || normalizedHeader.StartsWith("no.", StringComparison.Ordinal)
            || normalizedHeader.StartsWith("#", StringComparison.Ordinal)
            || normalizedHeader.StartsWith("numero ", StringComparison.Ordinal)
            || normalizedHeader.StartsWith("nro ", StringComparison.Ordinal)
            || normalizedHeader.StartsWith("num ", StringComparison.Ordinal);
    }

    private static bool LooksLikeHardwareDateField(string normalizedHeader) =>
        normalizedHeader.Contains("fecha", StringComparison.Ordinal);

    private static bool LooksLikeHardwareMoneyField(string normalizedHeader) =>
        normalizedHeader.Contains("valor", StringComparison.Ordinal)
        || normalizedHeader.Contains("costo", StringComparison.Ordinal)
        || normalizedHeader.Contains("precio", StringComparison.Ordinal)
        || normalizedHeader.Contains("total", StringComparison.Ordinal);

    private static bool LooksLikeHardwarePercentField(string normalizedHeader) =>
        normalizedHeader.Contains("%", StringComparison.Ordinal)
        || normalizedHeader.Contains("porcentaje", StringComparison.Ordinal)
        || normalizedHeader.Contains("utilidad", StringComparison.Ordinal);

    private static bool LooksLikeHardwareQuantityField(string normalizedHeader) =>
        normalizedHeader.Contains("cant", StringComparison.Ordinal)
        || normalizedHeader.Contains("cantidad", StringComparison.Ordinal);

    private static bool LooksLikeHardwareLongTextField(string normalizedHeader) =>
        normalizedHeader.Contains("descripcion", StringComparison.Ordinal)
        || normalizedHeader.Contains("detalle", StringComparison.Ordinal)
        || normalizedHeader.Contains("observ", StringComparison.Ordinal)
        || normalizedHeader.Contains("link", StringComparison.Ordinal);

    private static string CreateUniqueHardwareLogicalName(string label, ISet<string> usedNames, int position)
    {
        var baseName = BuildHardwareLogicalBase(label);
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = $"columna_{position}";

        var candidate = TruncateHardwareLogicalName($"cr07a_{baseName}");
        if (usedNames.Add(candidate))
            return candidate;

        var suffix = 2;
        while (true)
        {
            var withSuffix = TruncateHardwareLogicalName($"cr07a_{baseName}_{suffix}");
            if (usedNames.Add(withSuffix))
                return withSuffix;

            suffix++;
        }
    }

    private static string BuildHardwareLogicalBase(string label)
    {
        var normalized = RemoveHardwareDiacritics(label)
            .ToLowerInvariant();
        var builder = new StringBuilder(normalized.Length);
        var previousUnderscore = false;

        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (builder.Length == 0 && char.IsDigit(character))
                    builder.Append("col_");

                builder.Append(character);
                previousUnderscore = false;
                continue;
            }

            if (previousUnderscore)
                continue;

            builder.Append('_');
            previousUnderscore = true;
        }

        return builder
            .ToString()
            .Trim('_');
    }

    private static string TruncateHardwareLogicalName(string value)
    {
        const int maxLength = 48;
        if (value.Length <= maxLength)
            return value;

        return value[..maxLength].TrimEnd('_');
    }

    private static string CreateHardwareSchemaName(string logicalName)
    {
        var baseName = logicalName.StartsWith("cr07a_", StringComparison.OrdinalIgnoreCase)
            ? logicalName["cr07a_".Length..]
            : logicalName;
        var pascal = string.Join(
            "_",
            baseName
                .Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => char.ToUpperInvariant(segment[0]) + segment[1..]));
        var schemaName = $"cr07a_{pascal}";
        return schemaName.Length <= 50 ? schemaName : schemaName[..50];
    }

    private static string NormalizeHardwareAttributeAlias(string? value)
    {
        return string.Join(
            "",
            (value ?? string.Empty)
                .Where(char.IsLetterOrDigit))
            .ToLowerInvariant();
    }

    private static int DetermineHardwareMaxLength(
        HardwareAttributeKind kind,
        string header,
        IReadOnlyList<string> values)
    {
        if (kind == HardwareAttributeKind.Memo)
            return 4000;

        if (kind != HardwareAttributeKind.String)
            return 0;

        var observedMax = values.Count == 0 ? 0 : values.Max(static value => value.Length);
        if (header.Contains("link", StringComparison.OrdinalIgnoreCase))
            return Math.Clamp(Math.Max(observedMax + 40, 250), 250, 1000);

        return Math.Clamp(Math.Max(observedMax + 20, 100), 100, 4000);
    }

    private static string BuildHardwareColumnDescription(HardwareManagedColumnDefinition column)
    {
        return column.IsSystemColumn
            ? "Campo tecnico generado por el modulo Hardware."
            : $"Columna importada desde el CSV de Hardware: {column.SourceHeader}.";
    }

    private static string GetHardwareDelimiterLabel(char delimiter) =>
        delimiter == ';' ? "Punto y coma (;)" : "Coma (,)";

    private static string GetHardwareAttributeKindLabel(HardwareAttributeKind kind) =>
        kind switch
        {
            HardwareAttributeKind.Date => "Fecha",
            HardwareAttributeKind.Money => "Moneda",
            HardwareAttributeKind.Integer => "Numero entero",
            HardwareAttributeKind.Decimal => "Decimal",
            HardwareAttributeKind.Boolean => "Si/No",
            HardwareAttributeKind.Memo => "Texto largo",
            _ => "Texto"
        };

    private static bool TryParseHardwareDate(string? rawValue) =>
        TryParseHardwareDate(rawValue, out _);

    private static bool TryParseHardwareDate(string? rawValue, out DateOnly date)
    {
        var normalized = NormalizeHardwareCell(rawValue);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            date = default;
            return false;
        }

        if (DateOnly.TryParseExact(normalized, HardwareDateFormats, HardwareCulture, DateTimeStyles.AllowWhiteSpaces, out date))
            return true;

        if (DateOnly.TryParse(normalized, HardwareCulture, DateTimeStyles.AllowWhiteSpaces, out date))
            return true;

        return TryParseDateOnly(normalized, out date);
    }

    private static DateOnly ParseHardwareDate(string rawValue, string label)
    {
        if (TryParseHardwareDate(rawValue, out var date))
            return date;

        throw new InvalidOperationException($"El valor de {label} debe ser una fecha valida.");
    }

    private static bool TryParseHardwareDecimalValue(string? rawValue) =>
        TryParseHardwareDecimalValue(rawValue, out _);

    private static bool TryParseHardwareDecimalValue(string? rawValue, out decimal value)
    {
        var normalized = NormalizeHardwareNumericLiteral(rawValue);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            value = 0m;
            return false;
        }

        if (decimal.TryParse(normalized, NumberStyles.Number, HardwareCulture, out value))
            return true;

        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static decimal ParseHardwareDecimal(string rawValue, string label)
    {
        if (TryParseHardwareDecimalValue(rawValue, out var value))
            return value;

        throw new InvalidOperationException($"El valor de {label} debe ser numerico.");
    }

    private static bool TryParseHardwareInteger(string? rawValue)
    {
        var normalized = NormalizeHardwareNumericLiteral(rawValue);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (int.TryParse(normalized, NumberStyles.Integer, HardwareCulture, out _))
            return true;

        if (!TryParseHardwareDecimalValue(rawValue, out var decimalValue))
            return false;

        return decimal.Truncate(decimalValue) == decimalValue;
    }

    private static int ParseHardwareInteger(string rawValue, string label)
    {
        if (int.TryParse(NormalizeHardwareNumericLiteral(rawValue), NumberStyles.Integer, HardwareCulture, out var value))
            return value;

        if (TryParseHardwareDecimalValue(rawValue, out var decimalValue) && decimal.Truncate(decimalValue) == decimalValue)
            return (int)decimalValue;

        throw new InvalidOperationException($"El valor de {label} debe ser un numero entero.");
    }

    private static bool TryParseHardwareBoolean(string? rawValue)
    {
        var normalized = NormalizeHardwareCell(rawValue).ToLowerInvariant();
        return normalized is "si" or "sí" or "no" or "true" or "false" or "1" or "0";
    }

    private static bool ParseHardwareBoolean(string rawValue, string label)
    {
        var normalized = NormalizeHardwareCell(rawValue).ToLowerInvariant();
        return normalized switch
        {
            "si" => true,
            "sí" => true,
            "true" => true,
            "1" => true,
            "no" => false,
            "false" => false,
            "0" => false,
            _ => throw new InvalidOperationException($"El valor de {label} debe ser Si/No.")
        };
    }

    private static string NormalizeHardwareNumericLiteral(string? rawValue)
    {
        var normalized = NormalizeHardwareCell(rawValue);
        if (string.IsNullOrWhiteSpace(normalized))
            return "";

        normalized = normalized
            .Replace("$", "", StringComparison.Ordinal)
            .Replace("%", "", StringComparison.Ordinal)
            .Replace("COP", "", StringComparison.OrdinalIgnoreCase)
            .Replace("USD", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        return string.Join("", normalized.Where(character =>
            char.IsDigit(character)
            || character == '.'
            || character == ','
            || character == '-'));
    }

    private static string RemoveHardwareDiacritics(string value)
    {
        var normalized = (value ?? string.Empty).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string BuildHardwareProvisionMessage(
        bool tableCreated,
        int createdColumnsCount,
        int importedCount,
        int skippedDuplicatesCount)
    {
        var tableMessage = tableCreated ? "Se creo la tabla Hardware" : "Se reutilizo la tabla Hardware";
        var columnsMessage = createdColumnsCount switch
        {
            0 => "sin crear columnas nuevas",
            1 => "creando 1 columna nueva",
            _ => $"creando {createdColumnsCount} columnas nuevas"
        };
        var importMessage = importedCount switch
        {
            0 => "No se importaron filas nuevas",
            1 => "Se importo 1 fila",
            _ => $"Se importaron {importedCount} filas"
        };

        if (skippedDuplicatesCount <= 0)
            return $"{tableMessage}, {columnsMessage}. {importMessage}.";

        var duplicatesMessage = skippedDuplicatesCount == 1
            ? "Se omitio 1 fila duplicada"
            : $"Se omitieron {skippedDuplicatesCount} filas duplicadas";
        return $"{tableMessage}, {columnsMessage}. {importMessage}. {duplicatesMessage}.";
    }

    private sealed class HardwareCsvDocument
    {
        public string FileName { get; init; } = "";
        public char Delimiter { get; init; }
        public IReadOnlyList<HardwareManagedColumnDefinition> Columns { get; init; } = Array.Empty<HardwareManagedColumnDefinition>();
        public IReadOnlyList<HardwareCsvRow> Rows { get; init; } = Array.Empty<HardwareCsvRow>();
    }

    private sealed class HardwareCsvRow
    {
        public int SourceRowNumber { get; init; }
        public IReadOnlyList<string> Values { get; init; } = Array.Empty<string>();
    }

    private sealed class HardwareManagedColumnDefinition
    {
        public int Index { get; init; }
        public string SourceHeader { get; init; } = "";
        public string DisplayLabel { get; init; } = "";
        public string LogicalName { get; init; } = "";
        public string SchemaName { get; init; } = "";
        public string ResolvedLogicalName { get; set; } = "";
        public HardwareAttributeKind Kind { get; init; }
        public string ExampleValue { get; init; } = "";
        public int MaxLength { get; init; }
        public int Precision { get; init; } = 4;
        public bool IsSystemColumn { get; init; }
    }

    private sealed class HardwareAttributeMetadata
    {
        public string LogicalName { get; init; } = "";
        public string SchemaName { get; init; } = "";
        public string AttributeType { get; init; } = "";
    }

    private enum HardwareAttributeKind
    {
        String,
        Memo,
        Date,
        Money,
        Integer,
        Decimal,
        Boolean
    }
}
