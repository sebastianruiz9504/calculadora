using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.RH;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private readonly ConcurrentDictionary<string, RhEntityMetadata> _rhEntityMetadataCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _rhLookupNavigationPropertyCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly CultureInfo RhMoneyCulture = CultureInfo.GetCultureInfo("es-CO");
    private static readonly IReadOnlyDictionary<string, RhTableDefinition> RhTables =
        BuildRhTableDefinitions().ToDictionary(item => item.Key, item => item, StringComparer.OrdinalIgnoreCase);

    public async Task<RhTableDataResultDto> GetRhTableDataAsync(string tableKey, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var table = GetRhTableDefinition(tableKey);
        return await LoadRhTableDataAsync(table, httpContext.User, ct);
    }

    public async Task<RhSaveResultDto> SaveRhRecordAsync(RhSaveRequest request, CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var table = GetRhTableDefinition(request.TableKey);
        var metadata = await ResolveRhEntityMetadataAsync(
            table.LogicalName,
            table.FallbackEntitySetName,
            table.FallbackPrimaryIdField,
            table.FallbackPrimaryNameField,
            httpContext.User,
            ct);

        var normalizedRecordId = NormalizeOptionalGuid(request.RecordId);
        var isCreate = string.IsNullOrWhiteSpace(normalizedRecordId);
        var existingRecord = isCreate
            ? null
            : await GetRhRecordByIdAsync(table, metadata, normalizedRecordId, httpContext.User, ct);
        var employeeNameById = await LoadRhEmployeeNameMapAsync(httpContext.User, ct);
        var payload = await BuildRhPayloadAsync(
            table,
            metadata,
            request.Values,
            existingRecord,
            employeeNameById,
            httpContext.User,
            clearEmptyLookups: !isCreate,
            includeMissingFields: isCreate,
            ct: ct);
        if (!isCreate && payload.Count == 0)
            throw new InvalidOperationException("No hay cambios para guardar en este registro de RH.");

        var relativeUrl = isCreate
            ? $"/api/data/v9.2/{metadata.EntitySetName}"
            : $"/api/data/v9.2/{metadata.EntitySetName}({normalizedRecordId})";

        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await CallRhDataverseResponseAsync(
            relativeUrl,
            isCreate ? "POST" : "PATCH",
            httpContext.User,
            ct,
            content,
            AddRhReturnRepresentationHeaders);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");

        var recordId = isCreate
            ? ExtractRhRecordId(response, body, metadata.PrimaryIdField)
            : normalizedRecordId;

        var record = isCreate
            ? await ResolveRhSavedRecordAsync(table, metadata, response, body, recordId, ct)
            : await GetRhRecordByIdAsync(table, metadata, recordId, httpContext.User, ct);
        return new RhSaveResultDto
        {
            Message = isCreate ? "Registro creado correctamente." : "Registro actualizado correctamente.",
            Record = record
        };
    }

    public async Task<RhFileUploadResultDto> UploadRhFieldFileAsync(
        string tableKey,
        string recordId,
        string fieldName,
        string fileName,
        string contentType,
        byte[] content,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var table = GetRhTableDefinition(tableKey);
        var field = GetRhFileField(table, fieldName);
        var metadata = await ResolveRhEntityMetadataAsync(
            table.LogicalName,
            table.FallbackEntitySetName,
            table.FallbackPrimaryIdField,
            table.FallbackPrimaryNameField,
            httpContext.User,
            ct);

        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var safeFileName = SanitizeRhFileName(fileName, field.EditorType == "image" ? "imagen" : "archivo");
        ValidateRhBinaryUpload(field, safeFileName, contentType, content);

        using var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);

        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}({normalizedRecordId})/{field.LogicalName}/$value";
        using var response = await CallRhDataverseResponseAsync(
            relativeUrl,
            "PATCH",
            httpContext.User,
            ct,
            fileContent,
            request =>
            {
                request.Headers.TryAddWithoutValidation("x-ms-file-name", safeFileName);
                request.Headers.TryAddWithoutValidation("If-Match", "*");
            });

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");

        var record = await GetRhRecordByIdAsync(table, metadata, normalizedRecordId, httpContext.User, ct);
        return new RhFileUploadResultDto
        {
            Message = field.EditorType == "image"
                ? "Imagen cargada correctamente."
                : "Archivo cargado correctamente.",
            Record = record
        };
    }

    public async Task<RhFileDownloadResult?> DownloadRhFieldFileAsync(
        string tableKey,
        string recordId,
        string fieldName,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var table = GetRhTableDefinition(tableKey);
        var field = GetRhFileField(table, fieldName);
        var metadata = await ResolveRhEntityMetadataAsync(
            table.LogicalName,
            table.FallbackEntitySetName,
            table.FallbackPrimaryIdField,
            table.FallbackPrimaryNameField,
            httpContext.User,
            ct);

        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var relativeUrl = field.EditorType == "image"
            ? $"/api/data/v9.2/{metadata.EntitySetName}({normalizedRecordId})/{field.LogicalName}/$value?size=full"
            : $"/api/data/v9.2/{metadata.EntitySetName}({normalizedRecordId})/{field.LogicalName}/$value";

        using var response = await CallRhDataverseResponseAsync(relativeUrl, "GET", httpContext.User, ct);
        if (response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.NotFound)
            return null;

        var bodyBytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var bodyText = bodyBytes.Length == 0 ? "" : Encoding.UTF8.GetString(bodyBytes);
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {bodyText}");
        }

        var fileName = ResolveRhDownloadFileName(response, field, normalizedRecordId);
        var resolvedContentType =
            response.Content.Headers.ContentType?.MediaType
            ?? ReadHeaderValue(response, "mimetype")
            ?? (field.EditorType == "image" ? "image/jpeg" : "application/octet-stream");

        return new RhFileDownloadResult
        {
            FileName = fileName,
            ContentType = resolvedContentType,
            Content = bodyBytes
        };
    }

    private async Task<RhTableDataResultDto> LoadRhTableDataAsync(
        RhTableDefinition table,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var metadata = await ResolveRhEntityMetadataAsync(
            table.LogicalName,
            table.FallbackEntitySetName,
            table.FallbackPrimaryIdField,
            table.FallbackPrimaryNameField,
            user,
            ct);

        var select = BuildRhSelectClause(table, metadata);
        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}";
        if (!string.IsNullOrWhiteSpace(table.OrderBy))
            relativeUrl += $"&$orderby={table.OrderBy}";

        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        var lookupOptionsByField = new Dictionary<string, IReadOnlyList<RhOptionDto>>(StringComparer.OrdinalIgnoreCase);
        foreach (var lookupField in table.Fields.Where(field =>
                     string.Equals(field.EditorType, "lookup", StringComparison.OrdinalIgnoreCase)))
        {
            lookupOptionsByField[lookupField.LogicalName] = await LoadRhLookupOptionsAsync(lookupField, user, ct);
        }

        return new RhTableDataResultDto
        {
            TableKey = table.Key,
            Title = table.Title,
            Subtitle = table.Subtitle,
            Description = table.Description,
            EmptyStateMessage = table.EmptyStateMessage,
            Fields = table.Fields.Select(field => ToRhFieldDto(
                field,
                lookupOptionsByField.TryGetValue(field.LogicalName, out var options)
                    ? options
                    : Array.Empty<RhOptionDto>())).ToList(),
            Records = items
                .Select(item => BuildRhRecordDto(table, metadata, item))
                .Where(item => item is not null)
                .Select(item => item!)
                .ToList()
        };
    }

    private async Task<RhRecordDto> ResolveRhSavedRecordAsync(
        RhTableDefinition table,
        RhEntityMetadata metadata,
        HttpResponseMessage response,
        string body,
        string recordId,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(body))
        {
            using var doc = JsonDocument.Parse(body);
            var inlineRecord = BuildRhRecordDto(table, metadata, doc.RootElement);
            if (inlineRecord is not null)
                return inlineRecord;
        }

        if (string.IsNullOrWhiteSpace(recordId))
            throw new InvalidOperationException("No fue posible identificar el registro guardado en RH.");

        return await GetRhRecordByIdAsync(
            table,
            metadata,
            recordId,
            _httpContextAccessor.HttpContext?.User ?? throw new InvalidOperationException("No HttpContext available."),
            ct);
    }

    private async Task<RhRecordDto> GetRhRecordByIdAsync(
        RhTableDefinition table,
        RhEntityMetadata metadata,
        string recordId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var select = BuildRhSelectClause(table, metadata);
        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}({NormalizeGuid(recordId, nameof(recordId))})?$select={select}";
        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        using var doc = JsonDocument.Parse(json);
        return BuildRhRecordDto(table, metadata, doc.RootElement)
            ?? throw new InvalidOperationException("No fue posible reconstruir el registro guardado en RH.");
    }

    private async Task<Dictionary<string, object?>> BuildRhPayloadAsync(
        RhTableDefinition table,
        RhEntityMetadata metadata,
        IReadOnlyDictionary<string, string?>? values,
        RhRecordDto? existingRecord,
        IReadOnlyDictionary<string, string> employeeNameById,
        ClaimsPrincipal user,
        bool clearEmptyLookups,
        bool includeMissingFields,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var sourceValues = values ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in table.Fields)
        {
            if (string.Equals(field.EditorType, "file", StringComparison.OrdinalIgnoreCase)
                || string.Equals(field.EditorType, "image", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var hasInputValue = sourceValues.ContainsKey(field.LogicalName);
            if (!hasInputValue && !includeMissingFields)
                continue;

            sourceValues.TryGetValue(field.LogicalName, out var rawValue);
            rawValue = rawValue?.Trim();

            if (!includeMissingFields && IsRhFieldValueUnchanged(field, rawValue, existingRecord))
                continue;

            if (field.Required && string.IsNullOrWhiteSpace(rawValue))
                throw new InvalidOperationException($"El campo {field.Label} es obligatorio.");

            if (string.Equals(field.EditorType, "lookup", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(field.LookupTargetLogicalName))
                    throw new InvalidOperationException($"El lookup {field.Label} no tiene configurado su destino.");

                var navigationProperty = await ResolveRhLookupNavigationPropertyAsync(
                    table.LogicalName,
                    field.LogicalName,
                    field.LookupNavigationPropertyFallback,
                    user,
                    ct);

                if (string.IsNullOrWhiteSpace(rawValue))
                {
                    if (clearEmptyLookups)
                        payload[$"{navigationProperty}@odata.bind"] = null;

                    continue;
                }

                var targetMetadata = await ResolveRhEntityMetadataAsync(
                    field.LookupTargetLogicalName,
                    field.LookupTargetFallbackEntitySetName,
                    field.LookupTargetFallbackPrimaryIdField,
                    field.LookupTargetFallbackPrimaryNameField,
                    user,
                    ct);

                payload[$"{navigationProperty}@odata.bind"] =
                    $"/{targetMetadata.EntitySetName}({NormalizeGuid(rawValue, field.LogicalName)})";
                continue;
            }

            payload[field.LogicalName] = ConvertRhFieldValue(field, rawValue);
        }

        if (includeMissingFields && !string.IsNullOrWhiteSpace(metadata.PrimaryNameField))
            payload[metadata.PrimaryNameField] = BuildRhPrimaryName(table, metadata.PrimaryNameField, sourceValues, employeeNameById);

        return payload;
    }

    private static bool IsRhFieldValueUnchanged(RhFieldDefinition field, string? rawValue, RhRecordDto? existingRecord)
    {
        if (existingRecord?.Cells is null
            || !existingRecord.Cells.TryGetValue(field.LogicalName, out var cell))
        {
            return false;
        }

        var currentValue = string.Equals(field.EditorType, "lookup", StringComparison.OrdinalIgnoreCase)
            ? FirstNonEmpty(cell.LookupId, cell.Value)
            : cell.Value;

        return string.Equals(
            NormalizeRhFieldComparisonValue(field, rawValue),
            NormalizeRhFieldComparisonValue(field, currentValue),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRhFieldComparisonValue(RhFieldDefinition field, string? value)
    {
        var trimmed = value?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(trimmed))
            return "";

        if (string.Equals(field.EditorType, "date", StringComparison.OrdinalIgnoreCase)
            && TryParseDateOnly(trimmed, out var parsedDate))
        {
            return parsedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if ((string.Equals(field.EditorType, "number", StringComparison.OrdinalIgnoreCase)
                || string.Equals(field.EditorType, "currency", StringComparison.OrdinalIgnoreCase))
            && decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
        {
            return number.ToString("0.########", CultureInfo.InvariantCulture);
        }

        return trimmed;
    }

    private static object? ConvertRhFieldValue(RhFieldDefinition field, string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return null;

        return field.EditorType switch
        {
            "date" => ParseRhDate(rawValue, field.Label),
            "number" => ParseRhDecimal(rawValue, field.Label),
            "currency" => RoundCurrency(ParseRhDecimal(rawValue, field.Label)),
            "option" => ParseRhInt(rawValue, field.Label),
            _ => rawValue.Trim()
        };
    }

    private async Task<IReadOnlyList<RhOptionDto>> LoadRhEmployeeLookupOptionsAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var metadata = await ResolveRhEntityMetadataAsync(
            "cr07a_empleado",
            "cr07a_empleados",
            "cr07a_empleadoid",
            "cr07a_nombrecompleto",
            user,
            ct);

        var preferredNameField = "cr07a_nombrecompleto";
        var selectFields = new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            preferredNameField
        }
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase);

        var orderField = string.IsNullOrWhiteSpace(preferredNameField) ? metadata.PrimaryNameField : preferredNameField;
        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}?$select={string.Join(",", selectFields)}";
        if (!string.IsNullOrWhiteSpace(orderField))
            relativeUrl += $"&$orderby={orderField} asc";

        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        return items
            .Select(item =>
            {
                var id = ReadString(item, metadata.PrimaryIdField);
                var label = FirstNonEmpty(
                    ReadString(item, preferredNameField),
                    ReadString(item, metadata.PrimaryNameField),
                    id);

                return new RhOptionDto
                {
                    Value = id,
                    Label = label
                };
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<RhOptionDto>> LoadRhSystemUserLookupOptionsAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var metadata = await ResolveRhEntityMetadataAsync(
            "systemuser",
            "systemusers",
            "systemuserid",
            "fullname",
            user,
            ct);

        const string secondaryField = "internalemailaddress";
        const string preferredNameField = "fullname";
        var selectFields = new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            preferredNameField,
            secondaryField
        }
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase);

        var relativeUrl =
            $"/api/data/v9.2/{metadata.EntitySetName}?$select={string.Join(",", selectFields)}&$filter={Uri.EscapeDataString("isdisabled eq false")}&$orderby=fullname asc";

        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        return items
            .Select(item =>
            {
                var id = ReadString(item, metadata.PrimaryIdField);
                var name = FirstNonEmpty(
                    ReadString(item, preferredNameField),
                    ReadString(item, metadata.PrimaryNameField),
                    id);
                var email = ReadString(item, secondaryField);

                return new RhOptionDto
                {
                    Value = id,
                    Label = string.IsNullOrWhiteSpace(email) ? name : $"{name} ({email})"
                };
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<RhOptionDto>> LoadRhLookupOptionsAsync(
        RhFieldDefinition field,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (!string.Equals(field.EditorType, "lookup", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<RhOptionDto>();

        return field.LookupTargetLogicalName switch
        {
            "cr07a_empleado" => await LoadRhEmployeeLookupOptionsAsync(user, ct),
            "systemuser" => await LoadRhSystemUserLookupOptionsAsync(user, ct),
            _ => Array.Empty<RhOptionDto>()
        };
    }

    private async Task<Dictionary<string, string>> LoadRhEmployeeNameMapAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        return (await LoadRhEmployeeLookupOptionsAsync(user, ct))
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .ToDictionary(item => item.Value, item => item.Label, StringComparer.OrdinalIgnoreCase);
    }

    private RhRecordDto? BuildRhRecordDto(RhTableDefinition table, RhEntityMetadata metadata, JsonElement item)
    {
        var recordId = ReadString(item, metadata.PrimaryIdField);
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var cells = new Dictionary<string, RhCellValueDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in table.Fields)
        {
            cells[field.LogicalName] = BuildRhCellValue(field, item);
        }

        return new RhRecordDto
        {
            RecordId = recordId,
            Title = BuildRhRecordTitle(table, metadata, item, cells),
            Cells = cells
        };
    }

    private RhCellValueDto BuildRhCellValue(RhFieldDefinition field, JsonElement item)
    {
        if (string.Equals(field.EditorType, "lookup", StringComparison.OrdinalIgnoreCase))
        {
            var lookupId = ReadDataverseLookupId(item, field.LogicalName, field.LookupFallbackTokens.ToArray());
            var lookupLabel = ReadDataverseDisplayValue(item, field.LogicalName, field.LookupFallbackTokens.ToArray());
            return new RhCellValueDto
            {
                Value = lookupId,
                DisplayValue = string.IsNullOrWhiteSpace(lookupLabel) ? lookupId : lookupLabel,
                LookupId = lookupId,
                LookupLabel = lookupLabel
            };
        }

        if (string.Equals(field.EditorType, "option", StringComparison.OrdinalIgnoreCase))
        {
            var raw = ReadString(item, field.LogicalName);
            return new RhCellValueDto
            {
                Value = raw,
                DisplayValue = FirstNonEmpty(
                    ReadString(item, $"{field.LogicalName}{FormattedValueAnnotationSuffix}"),
                    ResolveRhOptionLabel(field.Options, raw),
                    raw)
            };
        }

        if (string.Equals(field.EditorType, "date", StringComparison.OrdinalIgnoreCase))
        {
            var date = ReadDateOnly(item, field.LogicalName);
            return new RhCellValueDto
            {
                Value = date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                DisplayValue = date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? ""
            };
        }

        if (string.Equals(field.EditorType, "number", StringComparison.OrdinalIgnoreCase)
            || string.Equals(field.EditorType, "currency", StringComparison.OrdinalIgnoreCase))
        {
            var number = ReadDecimal(item, field.LogicalName);
            if (!number.HasValue)
                return new RhCellValueDto();

            var rounded = string.Equals(field.EditorType, "currency", StringComparison.OrdinalIgnoreCase)
                ? RoundCurrency(number.Value)
                : number.Value;

            return new RhCellValueDto
            {
                Value = rounded.ToString("0.##", CultureInfo.InvariantCulture),
                DisplayValue = rounded.ToString("N2", RhMoneyCulture)
            };
        }

        if (string.Equals(field.EditorType, "file", StringComparison.OrdinalIgnoreCase))
        {
            var raw = ReadString(item, field.LogicalName);
            var fileName = ReadString(item, field.FileNameLogicalName);
            var hasContent = !string.IsNullOrWhiteSpace(raw) || !string.IsNullOrWhiteSpace(fileName);
            return new RhCellValueDto
            {
                Value = raw,
                DisplayValue = hasContent ? FirstNonEmpty(fileName, "Archivo cargado") : "",
                HasContent = hasContent,
                FileName = fileName
            };
        }

        if (string.Equals(field.EditorType, "image", StringComparison.OrdinalIgnoreCase))
        {
            var raw = ReadString(item, field.LogicalName);
            var imageUrl = string.IsNullOrWhiteSpace(field.ImageUrlLogicalName)
                ? ""
                : ReadString(item, field.ImageUrlLogicalName);
            var hasContent = !string.IsNullOrWhiteSpace(raw) || !string.IsNullOrWhiteSpace(imageUrl);
            return new RhCellValueDto
            {
                Value = raw,
                DisplayValue = hasContent ? "Foto cargada" : "",
                HasContent = hasContent,
                FileName = hasContent ? "Foto" : ""
            };
        }

        var text = ReadString(item, field.LogicalName);
        return new RhCellValueDto
        {
            Value = text,
            DisplayValue = text
        };
    }

    private string BuildRhRecordTitle(
        RhTableDefinition table,
        RhEntityMetadata metadata,
        JsonElement item,
        IReadOnlyDictionary<string, RhCellValueDto> cells)
    {
        return table.Key switch
        {
            RhModuleKeys.Employees => FirstNonEmpty(
                GetRhCellLabel(cells, "cr07a_nombrecompleto"),
                ReadString(item, metadata.PrimaryNameField),
                "Empleado sin nombre"),
            RhModuleKeys.VacationRequests => BuildRhPeriodTitle("Vacaciones", cells),
            RhModuleKeys.Incapacities => BuildRhPeriodTitle("Incapacidad", cells),
            _ => FirstNonEmpty(ReadString(item, metadata.PrimaryNameField), ReadString(item, metadata.PrimaryIdField), "Registro")
        };
    }

    private static string BuildRhPeriodTitle(string prefix, IReadOnlyDictionary<string, RhCellValueDto> cells)
    {
        var employee = GetRhCellLabel(cells, "cr07a_idempleado");
        var start = GetRhCellLabel(cells, "cr07a_fechainicio");
        var end = GetRhCellLabel(cells, "cr07a_fechafin");

        if (!string.IsNullOrWhiteSpace(employee) && !string.IsNullOrWhiteSpace(start))
            return $"{prefix} - {employee} - {start}{(string.IsNullOrWhiteSpace(end) ? "" : $" a {end}")}";

        return FirstNonEmpty(employee, start, prefix);
    }

    private static string GetRhCellLabel(IReadOnlyDictionary<string, RhCellValueDto> cells, string logicalName)
    {
        return cells.TryGetValue(logicalName, out var cell)
            ? FirstNonEmpty(cell.DisplayValue, cell.LookupLabel, cell.Value)
            : "";
    }

    private RhTableDefinition GetRhTableDefinition(string? tableKey)
    {
        if (!RhTables.TryGetValue(tableKey?.Trim() ?? "", out var table))
            throw new InvalidOperationException("La tabla solicitada de RH no existe.");

        return table;
    }

    private static RhFieldDefinitionDto ToRhFieldDto(RhFieldDefinition field, IReadOnlyList<RhOptionDto> lookupOptions)
    {
        return new RhFieldDefinitionDto
        {
            LogicalName = field.LogicalName,
            Label = field.Label,
            EditorType = field.EditorType,
            Placeholder = field.Placeholder,
            HelpText = field.HelpText,
            Accept = field.Accept,
            Required = field.Required,
            ShowInList = field.ShowInList,
            Options = string.Equals(field.EditorType, "lookup", StringComparison.OrdinalIgnoreCase)
                ? lookupOptions
                : field.Options
        };
    }

    private static string BuildRhSelectClause(RhTableDefinition table, RhEntityMetadata metadata)
    {
        var fields = new List<string>
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField
        };

        foreach (var field in table.Fields)
        {
            if (string.Equals(field.EditorType, "lookup", StringComparison.OrdinalIgnoreCase))
            {
                fields.Add($"_{field.LogicalName}_value");
                continue;
            }

            if (string.Equals(field.EditorType, "file", StringComparison.OrdinalIgnoreCase))
            {
                fields.Add(field.LogicalName);
                fields.Add(field.FileNameLogicalName);
                continue;
            }

            if (string.Equals(field.EditorType, "image", StringComparison.OrdinalIgnoreCase))
            {
                fields.Add(field.LogicalName);

                if (!string.IsNullOrWhiteSpace(field.ImageUrlLogicalName)
                    && !string.Equals(field.ImageUrlLogicalName, field.LogicalName, StringComparison.OrdinalIgnoreCase))
                {
                    fields.Add(field.ImageUrlLogicalName);
                }

                continue;
            }

            fields.Add(field.LogicalName);
        }

        return string.Join(",",
            fields
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private async Task<RhEntityMetadata> ResolveRhEntityMetadataAsync(
        string logicalName,
        string fallbackEntitySetName,
        string fallbackPrimaryIdField,
        string fallbackPrimaryNameField,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var normalizedLogicalName = logicalName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalizedLogicalName))
            throw new InvalidOperationException("La entidad de RH no esta configurada.");

        if (_rhEntityMetadataCache.TryGetValue(normalizedLogicalName, out var cached))
            return cached;

        try
        {
            var relativeUrl =
                $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(normalizedLogicalName)}')" +
                "?$select=LogicalName,EntitySetName,PrimaryIdAttribute,PrimaryNameAttribute";
            var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct);

            using var doc = JsonDocument.Parse(json);
            var metadata = new RhEntityMetadata
            {
                LogicalName = normalizedLogicalName,
                EntitySetName = FirstNonEmpty(ReadString(doc.RootElement, "EntitySetName"), fallbackEntitySetName),
                PrimaryIdField = FirstNonEmpty(ReadString(doc.RootElement, "PrimaryIdAttribute"), fallbackPrimaryIdField),
                PrimaryNameField = FirstNonEmpty(ReadString(doc.RootElement, "PrimaryNameAttribute"), fallbackPrimaryNameField)
            };

            if (string.IsNullOrWhiteSpace(metadata.EntitySetName) || string.IsNullOrWhiteSpace(metadata.PrimaryIdField))
                throw new InvalidOperationException($"No fue posible resolver la metadata base de la entidad {normalizedLogicalName}.");

            _entityPrimaryNameFieldCache[normalizedLogicalName] = metadata.PrimaryNameField;
            _rhEntityMetadataCache[normalizedLogicalName] = metadata;
            return metadata;
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            _logger.LogWarning(ex, "No fue posible resolver la metadata de RH para {LogicalName}. Se usaran valores de respaldo.", normalizedLogicalName);

            if (string.IsNullOrWhiteSpace(fallbackEntitySetName) || string.IsNullOrWhiteSpace(fallbackPrimaryIdField))
                throw;

            var fallback = new RhEntityMetadata
            {
                LogicalName = normalizedLogicalName,
                EntitySetName = fallbackEntitySetName,
                PrimaryIdField = fallbackPrimaryIdField,
                PrimaryNameField = fallbackPrimaryNameField
            };

            _rhEntityMetadataCache[normalizedLogicalName] = fallback;
            return fallback;
        }
    }

    private async Task<string> ResolveRhLookupNavigationPropertyAsync(
        string entityLogicalName,
        string lookupLogicalName,
        string fallbackNavigationProperty,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var cacheKey = $"{entityLogicalName}|{lookupLogicalName}";
        if (_rhLookupNavigationPropertyCache.TryGetValue(cacheKey, out var cached)
            && !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        try
        {
            var relativeUrl =
                $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(entityLogicalName)}')" +
                "?$select=LogicalName" +
                "&$expand=ManyToOneRelationships($select=ReferencingAttribute,ReferencingEntityNavigationPropertyName)";
            var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct);

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("ManyToOneRelationships", out var relationships)
                && relationships.ValueKind == JsonValueKind.Array)
            {
                var navigationProperty = relationships
                    .EnumerateArray()
                    .Where(relationship => string.Equals(
                        ReadString(relationship, "ReferencingAttribute"),
                        lookupLogicalName,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(relationship => ReadString(relationship, "ReferencingEntityNavigationPropertyName"))
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

                if (!string.IsNullOrWhiteSpace(navigationProperty))
                {
                    _rhLookupNavigationPropertyCache[cacheKey] = navigationProperty.Trim();
                    return navigationProperty.Trim();
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            _logger.LogWarning(
                ex,
                "No fue posible resolver la propiedad de navegacion del lookup {LookupLogicalName} para la entidad {EntityLogicalName}.",
                lookupLogicalName,
                entityLogicalName);
        }

        if (string.IsNullOrWhiteSpace(fallbackNavigationProperty))
            throw new InvalidOperationException($"No fue posible resolver el lookup {lookupLogicalName} para la entidad {entityLogicalName}.");

        _rhLookupNavigationPropertyCache[cacheKey] = fallbackNavigationProperty;
        return fallbackNavigationProperty;
    }

    private static void ValidateRhBinaryUpload(RhFieldDefinition field, string fileName, string contentType, byte[] content)
    {
        if (content.Length == 0)
            throw new InvalidOperationException("El archivo seleccionado esta vacio.");

        var maxBytes = string.Equals(field.EditorType, "image", StringComparison.OrdinalIgnoreCase)
            ? 30 * 1024 * 1024
            : 128 * 1024 * 1024;

        if (content.Length > maxBytes)
        {
            var maxMb = string.Equals(field.EditorType, "image", StringComparison.OrdinalIgnoreCase) ? 30 : 128;
            throw new InvalidOperationException($"El archivo supera el limite permitido de {maxMb} MB.");
        }

        if (string.Equals(field.EditorType, "image", StringComparison.OrdinalIgnoreCase)
            && !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("La foto debe ser un archivo de imagen.");
        }

        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidOperationException("El archivo no tiene un nombre valido.");
    }

    private static string SanitizeRhFileName(string? fileName, string fallback)
    {
        var safeName = Path.GetFileName(fileName ?? "").Trim();
        return string.IsNullOrWhiteSpace(safeName) ? fallback : safeName;
    }

    private static void AddRhReturnRepresentationHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation(
            "Prefer",
            $"return=representation, odata.include-annotations=\"{FormattedValueAnnotationSuffix.TrimStart('@')}\"");
    }

    private async Task<HttpResponseMessage> CallRhDataverseResponseAsync(
        string relativeUrl,
        string method,
        ClaimsPrincipal user,
        CancellationToken ct,
        HttpContent? content = null,
        Action<HttpRequestMessage>? customizeRequest = null)
    {
        var result = await _downstreamApi.CallApiForUserAsync(
            serviceName: "Dataverse",
            options =>
            {
                options.RelativePath = relativeUrl;
                options.HttpMethod = method;
                options.CustomizeHttpRequestMessage = customizeRequest;
            },
            user: user,
            content: content,
            cancellationToken: ct);

        if (result is not HttpResponseMessage response)
            throw new InvalidOperationException($"Unexpected downstream response type: {result?.GetType().FullName ?? "null"}");

        return response;
    }

    private static string ExtractRhRecordId(HttpResponseMessage response, string body, string primaryIdField)
    {
        if (!string.IsNullOrWhiteSpace(body))
        {
            using var doc = JsonDocument.Parse(body);
            var inlineId = ReadString(doc.RootElement, primaryIdField);
            if (!string.IsNullOrWhiteSpace(inlineId))
                return inlineId;
        }

        if (response.Headers.TryGetValues("OData-EntityId", out var entityIdValues))
        {
            var entityId = entityIdValues.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(entityId))
            {
                var start = entityId.LastIndexOf('(');
                var end = entityId.LastIndexOf(')');
                if (start >= 0 && end > start)
                    return entityId[(start + 1)..end];
            }
        }

        return "";
    }

    private static string ResolveRhDownloadFileName(HttpResponseMessage response, RhFieldDefinition field, string recordId)
    {
        var headerName = ReadHeaderValue(response, "x-ms-file-name");
        if (!string.IsNullOrWhiteSpace(headerName))
            return headerName.Trim();

        if (string.Equals(field.EditorType, "image", StringComparison.OrdinalIgnoreCase))
            return $"{field.Label}-{recordId}.jpg";

        return $"{field.Label}-{recordId}.bin";
    }

    private static string? ReadHeaderValue(HttpResponseMessage response, string headerName)
    {
        if (response.Headers.TryGetValues(headerName, out var headerValues))
            return headerValues.FirstOrDefault();

        if (response.Content.Headers.TryGetValues(headerName, out var contentHeaderValues))
            return contentHeaderValues.FirstOrDefault();

        return null;
    }

    private RhFieldDefinition GetRhFileField(RhTableDefinition table, string? fieldName)
    {
        var field = table.Fields.FirstOrDefault(item =>
            string.Equals(item.LogicalName, fieldName?.Trim(), StringComparison.OrdinalIgnoreCase));

        if (field is null
            || (!string.Equals(field.EditorType, "file", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(field.EditorType, "image", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("El campo seleccionado no admite archivos.");
        }

        return field;
    }

    private static string BuildRhPrimaryName(
        RhTableDefinition table,
        string primaryNameField,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyDictionary<string, string> employeeNameById)
    {
        var explicitPrimaryName = GetRhInputValue(values, primaryNameField);
        if (!string.IsNullOrWhiteSpace(explicitPrimaryName))
            return explicitPrimaryName;

        return table.Key switch
        {
            RhModuleKeys.Employees => FirstNonEmpty(
                GetRhInputValue(values, "cr07a_nombrecompleto"),
                GetRhInputValue(values, "cr07a_cedula"),
                "Empleado"),
            RhModuleKeys.VacationRequests => BuildRhRequestName("Vacaciones", values, employeeNameById),
            RhModuleKeys.Incapacities => BuildRhRequestName("Incapacidad", values, employeeNameById),
            _ => "Registro RH"
        };
    }

    private static string BuildRhRequestName(
        string prefix,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyDictionary<string, string> employeeNameById)
    {
        var employeeId = GetRhInputValue(values, "cr07a_idempleado");
        var employeeName = !string.IsNullOrWhiteSpace(employeeId) && employeeNameById.TryGetValue(employeeId, out var label)
            ? label
            : employeeId;

        var start = GetRhInputValue(values, "cr07a_fechainicio");
        return FirstNonEmpty(
            $"{prefix} - {FirstNonEmpty(employeeName, "Empleado")} - {FirstNonEmpty(start, "sin fecha")}".Trim(),
            prefix);
    }

    private static string GetRhInputValue(IReadOnlyDictionary<string, string?> values, string logicalName)
    {
        return values.TryGetValue(logicalName, out var value) ? value?.Trim() ?? "" : "";
    }

    private static decimal ParseRhDecimal(string rawValue, string label)
    {
        var normalized = rawValue.Trim();
        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariantValue))
            return invariantValue;

        if (decimal.TryParse(normalized, NumberStyles.Number, RhMoneyCulture, out var localValue))
            return localValue;

        throw new InvalidOperationException($"El valor de {label} debe ser numerico.");
    }

    private static int ParseRhInt(string rawValue, string label)
    {
        if (int.TryParse(rawValue.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;

        throw new InvalidOperationException($"El valor de {label} debe ser un numero entero.");
    }

    private static string ParseRhDate(string rawValue, string label)
    {
        if (!TryParseDateOnly(rawValue, out var parsedDate))
            throw new InvalidOperationException($"El valor de {label} debe ser una fecha valida.");

        return parsedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string ResolveRhOptionLabel(IEnumerable<RhOptionDto> options, string rawValue)
    {
        return options.FirstOrDefault(item => string.Equals(item.Value, rawValue, StringComparison.OrdinalIgnoreCase))?.Label ?? "";
    }

    private static IReadOnlyList<RhTableDefinition> BuildRhTableDefinitions()
    {
        return new[]
        {
            new RhTableDefinition
            {
                Key = RhModuleKeys.Employees,
                Title = "Empleados",
                Subtitle = "cr07a_empleado",
                Description = "Edita la ficha principal de cada colaborador.",
                EmptyStateMessage = "Todavia no hay empleados cargados en esta tabla.",
                LogicalName = "cr07a_empleado",
                FallbackEntitySetName = "cr07a_empleados",
                FallbackPrimaryIdField = "cr07a_empleadoid",
                FallbackPrimaryNameField = "cr07a_nombrecompleto",
                OrderBy = "cr07a_nombrecompleto asc",
                Fields = new[]
                {
                    new RhFieldDefinition { LogicalName = "cr07a_nombrecompleto", Label = "Nombre completo", EditorType = "text", Required = true, Placeholder = "Nombre del empleado" },
                    new RhFieldDefinition { LogicalName = "cr07a_fechadenacimiento", Label = "Fecha de nacimiento", EditorType = "date" },
                    new RhFieldDefinition { LogicalName = "cr07a_fechadeingreso", Label = "Fecha de ingreso", EditorType = "date" },
                    new RhFieldDefinition { LogicalName = "cr07a_fechadesalida", Label = "Fecha de salida", EditorType = "date" },
                    new RhFieldDefinition { LogicalName = "cr07a_sueldomensual", Label = "Sueldo mensual", EditorType = "currency" },
                    new RhFieldDefinition { LogicalName = "cr07a_telefono", Label = "Telefono principal", EditorType = "phone" },
                    new RhFieldDefinition { LogicalName = "cr07a_correo", Label = "Correo", EditorType = "email" },
                    new RhFieldDefinition
                    {
                        LogicalName = "cr07a_usuario",
                        Label = "Usuario",
                        EditorType = "lookup",
                        Placeholder = "Escribe el nombre del usuario",
                        ShowInList = false,
                        HelpText = "Escribe el nombre del usuario y seleccionalo en la lista.",
                        LookupFallbackTokens = new[] { "usuario", "systemuser" },
                        LookupNavigationPropertyFallback = "cr07a_usuario",
                        LookupTargetLogicalName = "systemuser",
                        LookupTargetFallbackEntitySetName = "systemusers",
                        LookupTargetFallbackPrimaryIdField = "systemuserid",
                        LookupTargetFallbackPrimaryNameField = "fullname"
                    },
                    new RhFieldDefinition { LogicalName = "cr07a_cargo", Label = "Cargo", EditorType = "text" },
                    new RhFieldDefinition { LogicalName = "cr07a_diasdevacacionesdisponibles", Label = "Dias de vacaciones disponibles", EditorType = "number" },
                    new RhFieldDefinition { LogicalName = "cr07a_cedula", Label = "Cedula", EditorType = "text" },
                    new RhFieldDefinition { LogicalName = "cr07a_tel", Label = "Telefono alterno", EditorType = "phone" },
                    new RhFieldDefinition
                    {
                        LogicalName = "cr07a_tipocontrato",
                        Label = "Tipo de contrato",
                        EditorType = "option",
                        Options = new[]
                        {
                            new RhOptionDto { Value = "645250000", Label = "Nomina" },
                            new RhOptionDto { Value = "645250001", Label = "Prestacion de servicios" }
                        }
                    },
                    new RhFieldDefinition
                    {
                        LogicalName = "cr07a_foto",
                        Label = "Foto",
                        EditorType = "image",
                        Accept = "image/*",
                        ShowInList = false,
                        HelpText = "Guarda primero el registro para cargar o reemplazar la foto."
                    },
                    new RhFieldDefinition { LogicalName = "cr07a_auxconectividad", Label = "Auxilio de conectividad", EditorType = "currency" },
                    new RhFieldDefinition { LogicalName = "cr07a_topecomisional", Label = "Tope comisional", EditorType = "currency" }
                }
            },
            new RhTableDefinition
            {
                Key = RhModuleKeys.VacationRequests,
                Title = "Vacaciones",
                Subtitle = "cr07a_solicituddevacaciones",
                Description = "Edita solicitudes de vacaciones y su duracion.",
                EmptyStateMessage = "Todavia no hay solicitudes de vacaciones cargadas.",
                LogicalName = "cr07a_solicituddevacaciones",
                FallbackEntitySetName = "cr07a_solicituddevacacioneses",
                FallbackPrimaryIdField = "cr07a_solicituddevacacionesid",
                FallbackPrimaryNameField = "cr07a_name",
                OrderBy = "cr07a_fechainicio desc",
                Fields = new[]
                {
                    new RhFieldDefinition
                    {
                        LogicalName = "cr07a_idempleado",
                        Label = "Empleado",
                        EditorType = "lookup",
                        Required = true,
                        LookupFallbackTokens = new[] { "idempleado", "empleado" },
                        LookupNavigationPropertyFallback = "cr07a_Nomina_cr07a_IDEmpleado_cr07a_Empleado",
                        LookupTargetLogicalName = "cr07a_empleado",
                        LookupTargetFallbackEntitySetName = "cr07a_empleados",
                        LookupTargetFallbackPrimaryIdField = "cr07a_empleadoid",
                        LookupTargetFallbackPrimaryNameField = "cr07a_nombrecompleto"
                    },
                    new RhFieldDefinition { LogicalName = "cr07a_fechainicio", Label = "Fecha inicio", EditorType = "date", Required = true },
                    new RhFieldDefinition { LogicalName = "cr07a_fechafin", Label = "Fecha fin", EditorType = "date", Required = true },
                    new RhFieldDefinition { LogicalName = "cr07a_cantidaddedias", Label = "Cantidad de dias", EditorType = "number", Required = true }
                }
            },
            new RhTableDefinition
            {
                Key = RhModuleKeys.Incapacities,
                Title = "Incapacidades",
                Subtitle = "cr07a_incapacidad",
                Description = "Edita incapacidades y adjunta el soporte correspondiente.",
                EmptyStateMessage = "Todavia no hay incapacidades cargadas.",
                LogicalName = "cr07a_incapacidad",
                FallbackEntitySetName = "cr07a_incapacidads",
                FallbackPrimaryIdField = "cr07a_incapacidadid",
                FallbackPrimaryNameField = "cr07a_name",
                OrderBy = "cr07a_fechainicio desc",
                Fields = new[]
                {
                    new RhFieldDefinition
                    {
                        LogicalName = "cr07a_idempleado",
                        Label = "Empleado",
                        EditorType = "lookup",
                        Required = true,
                        LookupFallbackTokens = new[] { "idempleado", "empleado" },
                        LookupNavigationPropertyFallback = "cr07a_Nomina_cr07a_IDEmpleado_cr07a_Empleado",
                        LookupTargetLogicalName = "cr07a_empleado",
                        LookupTargetFallbackEntitySetName = "cr07a_empleados",
                        LookupTargetFallbackPrimaryIdField = "cr07a_empleadoid",
                        LookupTargetFallbackPrimaryNameField = "cr07a_nombrecompleto"
                    },
                    new RhFieldDefinition { LogicalName = "cr07a_fechainicio", Label = "Fecha inicio", EditorType = "date", Required = true },
                    new RhFieldDefinition { LogicalName = "cr07a_fechafin", Label = "Fecha fin", EditorType = "date", Required = true },
                    new RhFieldDefinition { LogicalName = "cr07a_motivo", Label = "Motivo", EditorType = "text", Required = true, Placeholder = "Describe el motivo" },
                    new RhFieldDefinition
                    {
                        LogicalName = "cr07a_adjuntarincapacidad",
                        Label = "Adjunto incapacidad",
                        EditorType = "file",
                        Accept = "application/pdf,image/*",
                        ShowInList = false,
                        FileNameLogicalName = "cr07a_adjuntarincapacidad_name",
                        HelpText = "Guarda primero el registro para cargar o reemplazar el soporte."
                    }
                }
            }
        };
    }

    private sealed class RhEntityMetadata
    {
        public string LogicalName { get; set; } = "";
        public string EntitySetName { get; set; } = "";
        public string PrimaryIdField { get; set; } = "";
        public string PrimaryNameField { get; set; } = "";
    }

    private sealed class RhTableDefinition
    {
        public string Key { get; init; } = "";
        public string Title { get; init; } = "";
        public string Subtitle { get; init; } = "";
        public string Description { get; init; } = "";
        public string EmptyStateMessage { get; init; } = "";
        public string LogicalName { get; init; } = "";
        public string FallbackEntitySetName { get; init; } = "";
        public string FallbackPrimaryIdField { get; init; } = "";
        public string FallbackPrimaryNameField { get; init; } = "";
        public string OrderBy { get; init; } = "";
        public IReadOnlyList<RhFieldDefinition> Fields { get; init; } = Array.Empty<RhFieldDefinition>();
    }

    private sealed class RhFieldDefinition
    {
        public string LogicalName { get; init; } = "";
        public string Label { get; init; } = "";
        public string EditorType { get; init; } = "text";
        public string Placeholder { get; init; } = "";
        public string HelpText { get; init; } = "";
        public string Accept { get; init; } = "";
        public bool Required { get; init; }
        public bool ShowInList { get; init; } = true;
        public IReadOnlyList<RhOptionDto> Options { get; init; } = Array.Empty<RhOptionDto>();
        public IReadOnlyList<string> LookupFallbackTokens { get; init; } = Array.Empty<string>();
        public string LookupNavigationPropertyFallback { get; init; } = "";
        public string LookupTargetLogicalName { get; init; } = "";
        public string LookupTargetFallbackEntitySetName { get; init; } = "";
        public string LookupTargetFallbackPrimaryIdField { get; init; } = "";
        public string LookupTargetFallbackPrimaryNameField { get; init; } = "";
        public string FileNameLogicalName { get; init; } = "";
        public string ImageUrlLogicalName { get; init; } = "";
    }
}
