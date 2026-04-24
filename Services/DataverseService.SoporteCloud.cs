using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.SoporteCloud;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string SoporteCloudLogicalName = "cr07a_ticket";
    private const string SoporteCloudFallbackEntitySetName = "cr07a_tickets";
    private const string SoporteCloudFallbackIdField = "cr07a_ticketid";
    private const string SoporteCloudFallbackPrimaryNameField = "cr07a_name";
    private const string SoporteCloudTitleField = "cr07a_tituloticket";
    private const string SoporteCloudDescriptionField = "cr07a_descripcion";
    private const string SoporteCloudCreationDateField = "cr07a_fechacreacion";
    private const string SoporteCloudStateField = "cr07a_estado";
    private const string SoporteCloudTypeField = "cr07a_tipo";
    private const string SoporteCloudClientField = "cr07a_cliente";
    private const string SoporteCloudCategoryField = "cr07a_categoria";
    private const string SoporteCloudCreatedByField = "createdby";
    private const string SoporteCloudHoursTakenField = "cr07a_horastomadas";
    private const string SoporteCloudMethodField = "cr07a_metodo";
    private const string SoporteCloudSolutionField = "cr07a_solucion";
    private const string SoporteCloudAttachmentField = "cr07a_adjunto";
    private const string SoporteCloudAttachmentNameField = "cr07a_adjunto_name";
    private const string SoporteCloudModifiedOnField = "modifiedon";
    private const string SoporteCloudCreatedOnFallbackField = "createdon";
    private static readonly CultureInfo SoporteCloudCulture = CultureInfo.GetCultureInfo("es-CO");
    private static readonly HashSet<string> SoporteCloudAllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".jpg",
        ".jpeg",
        ".png",
        ".doc",
        ".docx"
    };
    private readonly ConcurrentDictionary<string, SoporteCloudMetadata> _soporteCloudMetadataCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<SoporteCloudBoardDto> GetSoporteCloudBoardAsync(
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveSoporteCloudMetadataAsync(httpContext.User, ct);
        var allRows = await LoadSoporteCloudRowsAsync(metadata, httpContext.User, ct);
        var (resolvedStartDate, resolvedEndDate) = ResolveSoporteCloudDateRange(startDate, endDate);
        var filteredRows = allRows
            .Where(row => IsSoporteCloudRowInRange(row, resolvedStartDate, resolvedEndDate))
            .ToList();

        var creatorSummaries = filteredRows
            .GroupBy(
                row => BuildDashboardGroupKey(row.CreatorId, row.CreatorName),
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                return new SoporteCloudCreatorSummaryDto
                {
                    CreatorId = first.CreatorId,
                    CreatorName = FirstNonEmpty(first.CreatorName, "Sin creador"),
                    TotalTickets = group.Count(),
                    TotalHours = RoundCurrency(group.Sum(item => item.HoursTaken))
                };
            })
            .OrderByDescending(item => item.TotalTickets)
            .ThenByDescending(item => item.TotalHours)
            .ThenBy(item => item.CreatorName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totalClients = filteredRows
            .Select(row => BuildDashboardGroupKey(row.ClientId, row.ClientName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count(key => !string.Equals(key, "label:empty", StringComparison.OrdinalIgnoreCase));

        return new SoporteCloudBoardDto
        {
            StartDateValue = resolvedStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            EndDateValue = resolvedEndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateRangeLabel = $"{resolvedStartDate:dd/MM/yyyy} - {resolvedEndDate:dd/MM/yyyy}",
            TotalTickets = filteredRows.Count,
            TotalHours = RoundCurrency(filteredRows.Sum(item => item.HoursTaken)),
            TotalCreators = creatorSummaries.Count,
            TotalClients = totalClients,
            Message = filteredRows.Count == 0
                ? "No encontramos tickets de soporte cloud en el rango seleccionado."
                : $"Se cargaron {filteredRows.Count} ticket(s) de soporte cloud.",
            Records = filteredRows,
            CreatorSummaries = creatorSummaries,
            TypeBreakdowns = BuildSoporteCloudBreakdowns(filteredRows, row => row.TypeValue, row => row.TypeLabel, "Sin tipo"),
            MethodBreakdowns = BuildSoporteCloudBreakdowns(filteredRows, row => row.MethodValue, row => row.MethodLabel, "Sin metodo"),
            CategoryBreakdowns = BuildSoporteCloudBreakdowns(filteredRows, row => row.CategoryValue, row => row.CategoryLabel, "Sin categoria"),
            StateOptions = EnsureSoporteCloudOptions(metadata.StateOptions, filteredRows, row => row.StateValue, row => row.StateLabel),
            TypeOptions = EnsureSoporteCloudOptions(metadata.TypeOptions, filteredRows, row => row.TypeValue, row => row.TypeLabel),
            CategoryOptions = EnsureSoporteCloudOptions(metadata.CategoryOptions, filteredRows, row => row.CategoryValue, row => row.CategoryLabel),
            MethodOptions = EnsureSoporteCloudOptions(metadata.MethodOptions, filteredRows, row => row.MethodValue, row => row.MethodLabel)
        };
    }

    public async Task<SoporteCloudSaveResultDto> SaveSoporteCloudTicketAsync(
        SoporteCloudSaveRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveSoporteCloudMetadataAsync(httpContext.User, ct);
        var normalized = NormalizeSoporteCloudWriteModel(request, metadata);
        var normalizedRecordId = NormalizeOptionalGuid(request.RecordId);
        var isCreate = string.IsNullOrWhiteSpace(normalizedRecordId);
        var clientId = FirstNonEmpty(
            NormalizeOptionalGuid(normalized.ClientId),
            string.IsNullOrWhiteSpace(normalized.ClientName)
                ? ""
                : await ResolveSoporteCloudClientIdAsync(normalized.ClientName, ct));

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [SoporteCloudTitleField] = normalized.Title,
            [SoporteCloudDescriptionField] = normalized.Description,
            [SoporteCloudCreationDateField] = normalized.CreationDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            [SoporteCloudStateField] = normalized.StateValue,
            [SoporteCloudTypeField] = normalized.TypeValue,
            [SoporteCloudCategoryField] = normalized.CategoryValue,
            [SoporteCloudHoursTakenField] = normalized.HoursTaken,
            [SoporteCloudMethodField] = normalized.MethodValue,
            [SoporteCloudSolutionField] = normalized.Solution
        };

        if (!string.IsNullOrWhiteSpace(metadata.BaseMetadata.PrimaryNameField)
            && !payload.ContainsKey(metadata.BaseMetadata.PrimaryNameField))
        {
            payload[metadata.BaseMetadata.PrimaryNameField] = normalized.Title;
        }

        if (!string.IsNullOrWhiteSpace(clientId))
        {
            payload[$"{metadata.ClientNavigationProperty}@odata.bind"] =
                $"/{ClientsEntitySetName}({NormalizeGuid(clientId, nameof(request.ClientId))})";
        }
        else if (!isCreate)
        {
            payload[$"{metadata.ClientNavigationProperty}@odata.bind"] = null;
        }

        var relativeUrl = isCreate
            ? $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}"
            : $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}({normalizedRecordId})";

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
            ? ExtractRhRecordId(response, body, metadata.BaseMetadata.PrimaryIdField)
            : normalizedRecordId;
        var record = await ResolveSoporteCloudSavedRecordAsync(metadata, body, recordId, httpContext.User, ct);

        return new SoporteCloudSaveResultDto
        {
            Message = isCreate
                ? "Ticket de soporte cloud creado correctamente."
                : "Ticket de soporte cloud actualizado correctamente.",
            Record = record
        };
    }

    public async Task<SoporteCloudFileUploadResultDto> UploadSoporteCloudAttachmentAsync(
        string recordId,
        string fileName,
        string contentType,
        byte[] content,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveSoporteCloudMetadataAsync(httpContext.User, ct);
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var safeFileName = SanitizeRhFileName(fileName, "soporte-cloud");
        ValidateSoporteCloudAttachmentUpload(safeFileName, content);
        var headerFileName = BuildSoporteCloudUploadHeaderFileName(safeFileName);

        using var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");

        var relativeUrl = $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}({normalizedRecordId})/{SoporteCloudAttachmentField}";
        using var response = await CallRhDataverseResponseAsync(
            relativeUrl,
            "PATCH",
            httpContext.User,
            ct,
            fileContent,
            request =>
            {
                request.Headers.TryAddWithoutValidation("If-Match", "*");
                request.Headers.TryAddWithoutValidation("x-ms-file-name", headerFileName);
            });

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");

        var record = await GetSoporteCloudRecordCoreAsync(metadata, normalizedRecordId, httpContext.User, ct);
        return new SoporteCloudFileUploadResultDto
        {
            Message = "Adjunto cargado correctamente.",
            Record = record
        };
    }

    public async Task<SoporteCloudFileDownloadResult?> DownloadSoporteCloudAttachmentAsync(
        string recordId,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveSoporteCloudMetadataAsync(httpContext.User, ct);
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var relativeUrl = $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}({normalizedRecordId})/{SoporteCloudAttachmentField}/$value";

        using var response = await CallRhDataverseResponseAsync(relativeUrl, "GET", httpContext.User, ct);
        if (response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.NotFound)
            return null;

        var bodyBytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var bodyText = bodyBytes.Length == 0 ? "" : Encoding.UTF8.GetString(bodyBytes);
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {bodyText}");
        }

        return new SoporteCloudFileDownloadResult
        {
            FileName = FirstNonEmpty(
                ReadHeaderValue(response, "x-ms-file-name"),
                ReadHeaderValue(response, "filename"),
                $"SoporteCloud-{normalizedRecordId}.bin"),
            ContentType =
                response.Content.Headers.ContentType?.MediaType
                ?? ReadHeaderValue(response, "mimetype")
                ?? "application/octet-stream",
            Content = bodyBytes
        };
    }

    private async Task<SoporteCloudMetadata> ResolveSoporteCloudMetadataAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        const string cacheKey = SoporteCloudLogicalName;
        if (_soporteCloudMetadataCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var baseMetadata = await ResolveRhEntityMetadataAsync(
            SoporteCloudLogicalName,
            SoporteCloudFallbackEntitySetName,
            SoporteCloudFallbackIdField,
            SoporteCloudFallbackPrimaryNameField,
            user,
            ct);

        string clientNavigationProperty;
        try
        {
            clientNavigationProperty = await ResolveRhLookupNavigationPropertyAsync(
                SoporteCloudLogicalName,
                SoporteCloudClientField,
                SoporteCloudClientField,
                user,
                ct);
        }
        catch (InvalidOperationException)
        {
            clientNavigationProperty = SoporteCloudClientField;
        }

        var statusTask = LoadSoporteCloudOptionsFromMetadataAsync(SoporteCloudStateField, user, ct);
        var typeTask = LoadSoporteCloudOptionsFromMetadataAsync(SoporteCloudTypeField, user, ct);
        var categoryTask = LoadSoporteCloudOptionsFromMetadataAsync(SoporteCloudCategoryField, user, ct);
        var methodTask = LoadSoporteCloudOptionsFromMetadataAsync(SoporteCloudMethodField, user, ct);
        await Task.WhenAll(statusTask, typeTask, categoryTask, methodTask);

        var resolved = new SoporteCloudMetadata
        {
            BaseMetadata = baseMetadata,
            ClientNavigationProperty = clientNavigationProperty,
            StateOptions = statusTask.Result,
            TypeOptions = typeTask.Result,
            CategoryOptions = categoryTask.Result,
            MethodOptions = methodTask.Result
        };

        _soporteCloudMetadataCache[cacheKey] = resolved;
        return resolved;
    }

    private async Task<IReadOnlyList<SoporteCloudTicketRowDto>> LoadSoporteCloudRowsAsync(
        SoporteCloudMetadata metadata,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var relativeUrl =
            $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}?$select={BuildSoporteCloudSelectClause(metadata)}" +
            $"&$orderby={SoporteCloudCreationDateField} desc,{SoporteCloudModifiedOnField} desc";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        return items
            .Select(item => BuildSoporteCloudRowDto(metadata, item))
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderByDescending(item => item.CreationDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildSoporteCloudSelectClause(SoporteCloudMetadata metadata)
    {
        return string.Join(",",
            new[]
            {
                metadata.BaseMetadata.PrimaryIdField,
                metadata.BaseMetadata.PrimaryNameField,
                SoporteCloudTitleField,
                SoporteCloudDescriptionField,
                SoporteCloudCreationDateField,
                SoporteCloudStateField,
                SoporteCloudTypeField,
                BuildDashboardLookupValuePropertyName(SoporteCloudClientField),
                SoporteCloudCategoryField,
                BuildDashboardLookupValuePropertyName(SoporteCloudCreatedByField),
                SoporteCloudHoursTakenField,
                SoporteCloudMethodField,
                SoporteCloudSolutionField,
                SoporteCloudAttachmentField,
                SoporteCloudAttachmentNameField,
                SoporteCloudModifiedOnField,
                SoporteCloudCreatedOnFallbackField
            }
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private SoporteCloudTicketRowDto? BuildSoporteCloudRowDto(SoporteCloudMetadata metadata, JsonElement item)
    {
        var recordId = FirstNonEmpty(
            ReadString(item, metadata.BaseMetadata.PrimaryIdField),
            ReadString(item, SoporteCloudFallbackIdField));
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var creationDate = ResolveSoporteCloudDate(item, SoporteCloudCreationDateField)
            ?? ResolveSoporteCloudDate(item, SoporteCloudCreatedOnFallbackField)
            ?? GetBogotaToday();
        var stateValue = ReadIntFlexible(item, SoporteCloudStateField);
        var typeValue = ReadIntFlexible(item, SoporteCloudTypeField);
        var categoryValue = ReadIntFlexible(item, SoporteCloudCategoryField);
        var methodValue = ReadIntFlexible(item, SoporteCloudMethodField);
        var attachmentToken = ReadString(item, SoporteCloudAttachmentField).Trim();
        var attachmentName = ReadString(item, SoporteCloudAttachmentNameField).Trim();
        var clientLookupProperty = DetectLookupValueProperty(
            item,
            new[]
            {
                BuildDashboardLookupValuePropertyName(SoporteCloudClientField),
                $"_{SoporteCloudClientField}id_value"
            },
            "cliente");
        var createdByLookupProperty = DetectLookupValueProperty(
            item,
            new[]
            {
                BuildDashboardLookupValuePropertyName(SoporteCloudCreatedByField)
            },
            "createdby");
        var modifiedOnDate = ResolveSoporteCloudDate(item, SoporteCloudModifiedOnField);

        return new SoporteCloudTicketRowDto
        {
            RecordId = recordId.Trim(),
            Title = FirstNonEmpty(
                ReadString(item, SoporteCloudTitleField).Trim(),
                ReadString(item, metadata.BaseMetadata.PrimaryNameField).Trim(),
                "Ticket sin titulo"),
            Description = ReadString(item, SoporteCloudDescriptionField).Trim(),
            CreationDateValue = creationDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            CreationDateDisplay = creationDate.ToString("dd/MM/yyyy", SoporteCloudCulture),
            StateValue = stateValue > 0 ? stateValue : null,
            StateLabel = ResolveDashboardOptionLabel(
                item,
                SoporteCloudStateField,
                stateValue,
                metadata.StateLabels,
                "Sin estado"),
            TypeValue = typeValue > 0 ? typeValue : null,
            TypeLabel = ResolveDashboardOptionLabel(
                item,
                SoporteCloudTypeField,
                typeValue,
                metadata.TypeLabels,
                "Sin tipo"),
            ClientId = ReadString(item, clientLookupProperty).Trim(),
            ClientName = FirstNonEmpty(
                ReadLookupFormattedValue(item, clientLookupProperty),
                ReadString(item, $"{SoporteCloudClientField}{FormattedValueAnnotationSuffix}").Trim(),
                "Sin cliente"),
            CategoryValue = categoryValue > 0 ? categoryValue : null,
            CategoryLabel = ResolveDashboardOptionLabel(
                item,
                SoporteCloudCategoryField,
                categoryValue,
                metadata.CategoryLabels,
                "Sin categoria"),
            CreatorId = ReadString(item, createdByLookupProperty).Trim(),
            CreatorName = FirstNonEmpty(
                ReadLookupFormattedValue(item, createdByLookupProperty),
                ReadString(item, $"{SoporteCloudCreatedByField}{FormattedValueAnnotationSuffix}").Trim(),
                "Sin creador"),
            HoursTaken = RoundCurrency(ReadDecimal(item, SoporteCloudHoursTakenField) ?? 0m),
            MethodValue = methodValue > 0 ? methodValue : null,
            MethodLabel = ResolveDashboardOptionLabel(
                item,
                SoporteCloudMethodField,
                methodValue,
                metadata.MethodLabels,
                "Sin metodo"),
            Solution = ReadString(item, SoporteCloudSolutionField).Trim(),
            HasAttachment = !string.IsNullOrWhiteSpace(attachmentToken) || !string.IsNullOrWhiteSpace(attachmentName),
            AttachmentFileName = FirstNonEmpty(
                attachmentName,
                ReadString(item, $"{SoporteCloudAttachmentField}{FormattedValueAnnotationSuffix}").Trim(),
                !string.IsNullOrWhiteSpace(attachmentToken) ? "Adjunto cargado" : ""),
            ModifiedOnDisplay = modifiedOnDate?.ToString("dd/MM/yyyy", SoporteCloudCulture) ?? ""
        };
    }

    private async Task<SoporteCloudTicketRowDto> ResolveSoporteCloudSavedRecordAsync(
        SoporteCloudMetadata metadata,
        string body,
        string recordId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(body))
        {
            using var doc = JsonDocument.Parse(body);
            var inlineRecord = BuildSoporteCloudRowDto(metadata, doc.RootElement);
            if (inlineRecord is not null)
                return inlineRecord;
        }

        if (string.IsNullOrWhiteSpace(recordId))
            throw new InvalidOperationException("No fue posible identificar el ticket guardado.");

        return await GetSoporteCloudRecordCoreAsync(metadata, recordId, user, ct);
    }

    private async Task<SoporteCloudTicketRowDto> GetSoporteCloudRecordCoreAsync(
        SoporteCloudMetadata metadata,
        string recordId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var relativeUrl =
            $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}({NormalizeGuid(recordId, nameof(recordId))})" +
            $"?$select={BuildSoporteCloudSelectClause(metadata)}";
        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        using var doc = JsonDocument.Parse(json);
        return BuildSoporteCloudRowDto(metadata, doc.RootElement)
            ?? throw new InvalidOperationException("No fue posible reconstruir el ticket guardado.");
    }

    private async Task<IReadOnlyList<SoporteCloudOptionDto>> LoadSoporteCloudOptionsFromMetadataAsync(
        string fieldName,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var queries = new[]
        {
            BuildSoporteCloudAttributeMetadataUrl(fieldName, "PicklistAttributeMetadata"),
            BuildSoporteCloudAttributeMetadataUrl(fieldName, "StatusAttributeMetadata"),
            BuildSoporteCloudAttributeMetadataUrl(fieldName, "StateAttributeMetadata"),
            BuildSoporteCloudAttributeMetadataUrl(fieldName, "MultiSelectPicklistAttributeMetadata")
        };

        foreach (var relativeUrl in queries)
        {
            try
            {
                var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct);
                var options = ParseSoporteCloudMetadataOptions(json);
                if (options.Count > 0)
                    return options;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogDebug(ex, "No fue posible leer metadata de opciones para {FieldName} usando {RelativeUrl}", fieldName, relativeUrl);
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "La metadata de opciones para {FieldName} no se pudo interpretar.", fieldName);
            }
        }

        _logger.LogWarning("No fue posible resolver las opciones de metadata para {FieldName} en {EntityLogicalName}.", fieldName, SoporteCloudLogicalName);
        return Array.Empty<SoporteCloudOptionDto>();
    }

    private static string BuildSoporteCloudAttributeMetadataUrl(string fieldName, string attributeMetadataType)
    {
        return
            $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(SoporteCloudLogicalName)}')" +
            $"/Attributes(LogicalName='{EscapeOdataLiteral(fieldName)}')/Microsoft.Dynamics.CRM.{attributeMetadataType}" +
            "?$select=LogicalName&$expand=OptionSet($select=Options),GlobalOptionSet($select=Options)";
    }

    private static IReadOnlyList<SoporteCloudOptionDto> ParseSoporteCloudMetadataOptions(string json)
    {
        using var doc = JsonDocument.Parse(json);
        JsonElement options = default;
        var hasOptions =
            doc.RootElement.TryGetProperty("OptionSet", out var optionSet)
            && optionSet.ValueKind == JsonValueKind.Object
            && optionSet.TryGetProperty("Options", out options)
            && options.ValueKind == JsonValueKind.Array;

        if (!hasOptions)
        {
            hasOptions =
                doc.RootElement.TryGetProperty("GlobalOptionSet", out var globalOptionSet)
                && globalOptionSet.ValueKind == JsonValueKind.Object
                && globalOptionSet.TryGetProperty("Options", out options)
                && options.ValueKind == JsonValueKind.Array;
        }

        if (!hasOptions)
            return Array.Empty<SoporteCloudOptionDto>();

        return options
            .EnumerateArray()
            .Select(option =>
            {
                var value = option.TryGetProperty("Value", out var valueProperty)
                    ? valueProperty.ValueKind switch
                    {
                        JsonValueKind.Number when valueProperty.TryGetInt32(out var intValue) => intValue,
                        JsonValueKind.String when int.TryParse(valueProperty.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue) => parsedValue,
                        _ => 0
                    }
                    : 0;
                var label = ReadSoporteCloudOptionLabel(option);
                return new SoporteCloudOptionDto
                {
                    Value = value,
                    Label = label
                };
            })
            .Where(item => item.Value > 0 && !string.IsNullOrWhiteSpace(item.Label))
            .OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ReadSoporteCloudOptionLabel(JsonElement option)
    {
        if (!option.TryGetProperty("Label", out var labelProperty)
            || labelProperty.ValueKind != JsonValueKind.Object)
        {
            return "";
        }

        if (labelProperty.TryGetProperty("UserLocalizedLabel", out var userLocalized)
            && userLocalized.ValueKind == JsonValueKind.Object)
        {
            var directLabel = ReadString(userLocalized, "Label").Trim();
            if (!string.IsNullOrWhiteSpace(directLabel))
                return directLabel;
        }

        if (labelProperty.TryGetProperty("LocalizedLabels", out var localizedLabels)
            && localizedLabels.ValueKind == JsonValueKind.Array)
        {
            foreach (var localized in localizedLabels.EnumerateArray())
            {
                var label = ReadString(localized, "Label").Trim();
                if (!string.IsNullOrWhiteSpace(label))
                    return label;
            }
        }

        return "";
    }

    private async Task<string> ResolveSoporteCloudClientIdAsync(string clientName, CancellationToken ct)
    {
        var matches = await SearchClientsAsync(clientName, top: 20, ct);
        var normalizedQuery = NormalizeSoporteCloudText(clientName);
        var exactMatch = matches.FirstOrDefault(item =>
            string.Equals(NormalizeSoporteCloudText(item.Name), normalizedQuery, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(exactMatch?.Id))
            return exactMatch.Id;

        var startsWithMatch = matches.FirstOrDefault(item =>
            NormalizeSoporteCloudText(item.Name).StartsWith(normalizedQuery, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(startsWithMatch?.Id))
            return startsWithMatch.Id;

        var firstMatch = matches.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(firstMatch?.Id))
            return firstMatch.Id;

        throw new InvalidOperationException("No fue posible encontrar el cliente seleccionado.");
    }

    private static SoporteCloudWriteModel NormalizeSoporteCloudWriteModel(
        SoporteCloudSaveRequest request,
        SoporteCloudMetadata metadata)
    {
        var title = request.Title?.Trim() ?? "";
        var description = request.Description?.Trim() ?? "";
        var solution = request.Solution?.Trim() ?? "";
        var clientId = request.ClientId?.Trim() ?? "";
        var clientName = request.ClientName?.Trim() ?? "";
        var hoursTaken = RoundCurrency(request.HoursTaken);

        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("El titulo del ticket es obligatorio.");

        if (string.IsNullOrWhiteSpace(description))
            throw new InvalidOperationException("La descripcion del ticket es obligatoria.");

        if (!TryParseDateOnly(request.CreationDateValue, out var creationDate))
            throw new InvalidOperationException("La fecha de creacion debe ser valida.");

        if (string.IsNullOrWhiteSpace(clientId) && string.IsNullOrWhiteSpace(clientName))
            throw new InvalidOperationException("Debes seleccionar un cliente.");

        if (hoursTaken < 0m)
            throw new InvalidOperationException("Las horas tomadas no pueden ser negativas.");

        return new SoporteCloudWriteModel
        {
            Title = title,
            Description = description,
            CreationDate = creationDate,
            StateValue = NormalizeSoporteCloudOptionValue(request.StateValue, metadata.StateLabels, "estado"),
            TypeValue = NormalizeSoporteCloudOptionValue(request.TypeValue, metadata.TypeLabels, "tipo"),
            ClientId = clientId,
            ClientName = clientName,
            CategoryValue = NormalizeSoporteCloudOptionValue(request.CategoryValue, metadata.CategoryLabels, "categoria"),
            HoursTaken = hoursTaken,
            MethodValue = NormalizeSoporteCloudOptionValue(request.MethodValue, metadata.MethodLabels, "metodo"),
            Solution = solution
        };
    }

    private static int NormalizeSoporteCloudOptionValue(
        int? value,
        IReadOnlyDictionary<int, string> knownLabels,
        string fieldLabel)
    {
        if (!value.HasValue || value.Value <= 0)
            throw new InvalidOperationException($"Debes seleccionar un {fieldLabel} valido.");

        if (knownLabels.Count > 0 && !knownLabels.ContainsKey(value.Value))
            throw new InvalidOperationException($"El valor seleccionado para {fieldLabel} no es valido.");

        return value.Value;
    }

    private static IReadOnlyList<SoporteCloudBreakdownDto> BuildSoporteCloudBreakdowns(
        IReadOnlyList<SoporteCloudTicketRowDto> rows,
        Func<SoporteCloudTicketRowDto, int?> valueSelector,
        Func<SoporteCloudTicketRowDto, string> labelSelector,
        string fallbackLabel)
    {
        var totalTickets = rows.Count;
        return rows
            .GroupBy(
                row => BuildDashboardGroupKey(
                    valueSelector(row)?.ToString(CultureInfo.InvariantCulture),
                    labelSelector(row)),
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var label = FirstNonEmpty(labelSelector(first), fallbackLabel);
                return new SoporteCloudBreakdownDto
                {
                    Key = group.Key,
                    Label = label,
                    TotalTickets = group.Count(),
                    TotalHours = RoundCurrency(group.Sum(item => item.HoursTaken)),
                    SharePercent = totalTickets == 0
                        ? 0m
                        : Math.Round((group.Count() * 100m) / totalTickets, 2, MidpointRounding.AwayFromZero)
                };
            })
            .OrderByDescending(item => item.TotalTickets)
            .ThenByDescending(item => item.TotalHours)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<SoporteCloudOptionDto> EnsureSoporteCloudOptions(
        IReadOnlyList<SoporteCloudOptionDto> metadataOptions,
        IReadOnlyList<SoporteCloudTicketRowDto> rows,
        Func<SoporteCloudTicketRowDto, int?> valueSelector,
        Func<SoporteCloudTicketRowDto, string> labelSelector)
    {
        if (metadataOptions.Count > 0)
            return metadataOptions;

        return rows
            .Select(row => new SoporteCloudOptionDto
            {
                Value = valueSelector(row) ?? 0,
                Label = labelSelector(row)
            })
            .Where(item => item.Value > 0 && !string.IsNullOrWhiteSpace(item.Label))
            .GroupBy(item => item.Value)
            .Select(group => group.First())
            .OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static (DateOnly StartDate, DateOnly EndDate) ResolveSoporteCloudDateRange(DateOnly? startDate, DateOnly? endDate)
    {
        var today = GetBogotaToday();
        var resolvedEnd = endDate ?? today;
        var resolvedStart = startDate ?? new DateOnly(resolvedEnd.Year, resolvedEnd.Month, 1);

        if (resolvedStart > resolvedEnd)
            (resolvedStart, resolvedEnd) = (resolvedEnd, resolvedStart);

        return (resolvedStart, resolvedEnd);
    }

    private static bool IsSoporteCloudRowInRange(SoporteCloudTicketRowDto row, DateOnly startDate, DateOnly endDate)
    {
        if (!TryParseDateOnly(row.CreationDateValue, out var rowDate))
            return true;

        return rowDate >= startDate && rowDate <= endDate;
    }

    private static DateOnly? ResolveSoporteCloudDate(JsonElement item, string fieldName)
    {
        return ReadDateOnly(item, fieldName);
    }

    private static string NormalizeSoporteCloudText(string? value)
    {
        return (value ?? "")
            .Normalize(NormalizationForm.FormD)
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .Aggregate(new StringBuilder(), static (builder, character) => builder.Append(character))
            .ToString()
            .Trim()
            .ToLowerInvariant();
    }

    private static void ValidateSoporteCloudAttachmentUpload(string fileName, byte[] content)
    {
        if (content.Length == 0)
            throw new InvalidOperationException("El archivo seleccionado esta vacio.");

        if (content.Length > 128 * 1024 * 1024)
            throw new InvalidOperationException("El archivo supera el limite permitido de 128 MB.");

        var extension = Path.GetExtension(fileName ?? "");
        if (string.IsNullOrWhiteSpace(extension) || !SoporteCloudAllowedExtensions.Contains(extension))
            throw new InvalidOperationException("El adjunto debe ser PDF, JPG/JPEG, PNG, DOC o DOCX.");
    }

    private static string BuildSoporteCloudUploadHeaderFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "soporte-cloud";

        var normalized = fileName.Normalize(NormalizationForm.FormKD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            if (character is >= ' ' and <= '~' and not '"' and not '\\')
                builder.Append(character);
        }

        var headerFileName = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(headerFileName) ? "soporte-cloud" : headerFileName;
    }

    private sealed class SoporteCloudMetadata
    {
        public RhEntityMetadata BaseMetadata { get; init; } = new();
        public string ClientNavigationProperty { get; init; } = SoporteCloudClientField;
        public IReadOnlyList<SoporteCloudOptionDto> StateOptions { get; init; } = Array.Empty<SoporteCloudOptionDto>();
        public IReadOnlyList<SoporteCloudOptionDto> TypeOptions { get; init; } = Array.Empty<SoporteCloudOptionDto>();
        public IReadOnlyList<SoporteCloudOptionDto> CategoryOptions { get; init; } = Array.Empty<SoporteCloudOptionDto>();
        public IReadOnlyList<SoporteCloudOptionDto> MethodOptions { get; init; } = Array.Empty<SoporteCloudOptionDto>();

        public IReadOnlyDictionary<int, string> StateLabels =>
            StateOptions.ToDictionary(item => item.Value, item => item.Label);

        public IReadOnlyDictionary<int, string> TypeLabels =>
            TypeOptions.ToDictionary(item => item.Value, item => item.Label);

        public IReadOnlyDictionary<int, string> CategoryLabels =>
            CategoryOptions.ToDictionary(item => item.Value, item => item.Label);

        public IReadOnlyDictionary<int, string> MethodLabels =>
            MethodOptions.ToDictionary(item => item.Value, item => item.Label);
    }

    private sealed class SoporteCloudWriteModel
    {
        public string Title { get; init; } = "";
        public string Description { get; init; } = "";
        public DateOnly CreationDate { get; init; }
        public int StateValue { get; init; }
        public int TypeValue { get; init; }
        public string ClientId { get; init; } = "";
        public string ClientName { get; init; } = "";
        public int CategoryValue { get; init; }
        public decimal HoursTaken { get; init; }
        public int MethodValue { get; init; }
        public string Solution { get; init; } = "";
    }
}
