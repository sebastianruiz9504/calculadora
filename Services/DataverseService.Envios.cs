using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.Envios;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string EnvioLogicalName = "cr07a_envio";
    private const string EnvioFallbackEntitySetName = "cr07a_envios";
    private const string EnvioFallbackIdField = "cr07a_envioid";
    private const string EnvioFallbackPrimaryNameField = "cr07a_name";
    private const string EnvioOriginField = "cr07a_origen";
    private const string EnvioDestinationField = "cr07a_destino";
    private const string EnvioClientField = "cr07a_cliente";
    private const string EnvioWhatIsSentField = "cr07a_queseenvia";
    private const string EnvioObservationsField = "cr07a_observaciones";
    private const string EnvioRecipientNameField = "cr07a_quienrecibe";
    private const string EnvioRecipientPhoneField = "cr07a_telefonorecibe";
    private const string EnvioStatusField = "cr07a_estado";
    private const string EnvioScheduledAtField = "cr07a_fechaprogramada";
    private const string EnvioTransporterField = "cr07a_transportador";
    private const string EnvioFreightValueField = "cr07a_valorflete";
    private const string EnvioPickupApprovedField = "cr07a_recogidaaprobada";
    private const string EnvioPickupApprovedAtField = "cr07a_recogidaaprobadaen";
    private const string EnvioPickupApprovedByField = "cr07a_recogidaaprobadapor";
    private const string EnvioDeliveryConfirmedAtField = "cr07a_entregaconfirmadaen";
    private const string EnvioDeliveredByField = "cr07a_entregadapor";
    private const string EnvioReceivedSatisfiedField = "cr07a_recibidosatisfaccion";
    private const string EnvioReceivedSatisfiedAtField = "cr07a_recibidosatisfaccionen";
    private const string EnvioReceivedSatisfiedByField = "cr07a_recibidosatisfaccionpor";
    private const string EnvioDeliveryActField = "cr07a_actaentrega";
    private const string EnvioDeliveryActNameField = "cr07a_actaentrega_name";
    private const string EnvioCreatedOnField = "createdon";
    private const string EnvioModifiedOnField = "modifiedon";
    private const string EnvioCreatedByField = "createdby";
    private const int EnvioStatusOpen = 645250000;
    private const int EnvioStatusScheduled = 645250001;
    private const int EnvioStatusPickupApproved = 645250002;
    private const int EnvioStatusDelivered = 645250003;
    private const int EnvioStatusClosed = 645250004;
    private static readonly CultureInfo EnviosCulture = CultureInfo.GetCultureInfo("es-CO");
    private static readonly IReadOnlyList<EnvioOptionDto> DefaultEnvioStatusOptions = new[]
    {
        new EnvioOptionDto { Value = EnvioStatusOpen, Label = "Solicitud abierta" },
        new EnvioOptionDto { Value = EnvioStatusScheduled, Label = "Agendada por transportador" },
        new EnvioOptionDto { Value = EnvioStatusPickupApproved, Label = "Recogida aprobada" },
        new EnvioOptionDto { Value = EnvioStatusDelivered, Label = "Entrega confirmada" },
        new EnvioOptionDto { Value = EnvioStatusClosed, Label = "Recibido a satisfaccion" }
    };
    private static readonly HashSet<string> EnvioDeliveryActAllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".jpg",
        ".jpeg",
        ".png",
        ".doc",
        ".docx"
    };
    private readonly ConcurrentDictionary<string, EnvioMetadata> _enviosMetadataCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<EnviosBoardDto> GetEnviosBoardAsync(int? year = null, int? month = null, CancellationToken ct = default)
    {
        return await GetEnviosBoardCoreAsync("usuario", year, month, filterForCurrentTransporter: false, ct);
    }

    public async Task<EnviosBoardDto> GetEnviosTransportadorBoardAsync(int? year = null, int? month = null, CancellationToken ct = default)
    {
        return await GetEnviosBoardCoreAsync("transportador", year, month, filterForCurrentTransporter: true, ct);
    }

    public async Task<EnvioSaveResultDto> CreateEnvioSolicitudAsync(EnvioCreateRequest request, CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveEnvioMetadataAsync(httpContext.User, ct);
        var normalized = await NormalizeEnvioCreateRequestAsync(request, ct);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [metadata.BaseMetadata.PrimaryNameField] = BuildEnvioPrimaryName(normalized),
            [EnvioOriginField] = normalized.Origin,
            [EnvioDestinationField] = normalized.Destination,
            [EnvioWhatIsSentField] = normalized.WhatIsSent,
            [EnvioObservationsField] = normalized.Observations,
            [EnvioRecipientNameField] = normalized.RecipientName,
            [EnvioRecipientPhoneField] = normalized.RecipientPhone,
            [EnvioStatusField] = EnvioStatusOpen
        };

        payload[$"{metadata.ClientNavigationProperty}@odata.bind"] =
            $"/{ClientsEntitySetName}({NormalizeGuid(normalized.ClientId, nameof(request.ClientId))})";

        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await CallRhDataverseResponseAsync(
            $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}",
            "POST",
            httpContext.User,
            ct,
            content,
            AddRhReturnRepresentationHeaders);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");

        var recordId = ExtractRhRecordId(response, body, metadata.BaseMetadata.PrimaryIdField);
        var record = await ResolveEnvioSavedRecordAsync(metadata, body, recordId, httpContext.User, ct);
        return new EnvioSaveResultDto
        {
            Message = "Solicitud de envio creada correctamente.",
            Record = record
        };
    }

    public async Task<EnvioSaveResultDto> ScheduleEnvioAsync(EnvioScheduleRequest request, CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveEnvioMetadataAsync(httpContext.User, ct);
        var recordId = NormalizeGuid(request.RecordId, nameof(request.RecordId));
        var current = await GetEnvioRecordCoreAsync(metadata, recordId, httpContext.User, ct);
        if (current.StatusValue is not (EnvioStatusOpen or EnvioStatusScheduled))
            throw new InvalidOperationException("Solo se pueden agendar solicitudes abiertas o ya agendadas.");

        var scheduledAt = ParseEnvioScheduledAt(request.ScheduledAtValue);
        var freightValue = RoundCurrency(request.FreightValue);
        if (freightValue <= 0m)
            throw new InvalidOperationException("El valor del flete debe ser mayor a cero.");

        var currentUser = await GetCurrentUserAsync(ct) ?? new();
        if (string.IsNullOrWhiteSpace(currentUser.SystemUserId))
            throw new InvalidOperationException("No fue posible identificar el transportador actual en Dataverse.");

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [EnvioStatusField] = EnvioStatusScheduled,
            [EnvioScheduledAtField] = FormatEnvioDateTimeForDataverse(scheduledAt),
            [EnvioFreightValueField] = freightValue
        };

        payload[$"{metadata.TransporterNavigationProperty}@odata.bind"] =
            $"/systemusers({NormalizeGuid(currentUser.SystemUserId, nameof(currentUser.SystemUserId))})";

        await CallDataverseSendAsync(
            $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}({recordId})",
            "PATCH",
            payload,
            httpContext.User,
            ct);

        return new EnvioSaveResultDto
        {
            Message = "Solicitud agendada correctamente.",
            Record = await GetEnvioRecordCoreAsync(metadata, recordId, httpContext.User, ct)
        };
    }

    public async Task<EnvioSaveResultDto> ApproveEnvioPickupAsync(string recordId, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveEnvioMetadataAsync(httpContext.User, ct);
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var current = await GetEnvioRecordCoreAsync(metadata, normalizedRecordId, httpContext.User, ct);
        if (current.StatusValue != EnvioStatusScheduled)
            throw new InvalidOperationException("La recogida solo se puede aprobar cuando el envio esta agendado.");

        if (current.FreightValue <= 0m)
            throw new InvalidOperationException("El envio debe tener un valor de flete antes de aprobar la recogida.");

        var currentUser = await GetCurrentUserAsync(ct) ?? new();
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [EnvioStatusField] = EnvioStatusPickupApproved,
            [EnvioPickupApprovedField] = true,
            [EnvioPickupApprovedAtField] = FormatEnvioDateTimeForDataverse(GetBogotaNow())
        };

        if (!string.IsNullOrWhiteSpace(currentUser.SystemUserId))
        {
            payload[$"{metadata.PickupApprovedByNavigationProperty}@odata.bind"] =
                $"/systemusers({NormalizeGuid(currentUser.SystemUserId, nameof(currentUser.SystemUserId))})";
        }

        await CallDataverseSendAsync(
            $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}({normalizedRecordId})",
            "PATCH",
            payload,
            httpContext.User,
            ct);

        return new EnvioSaveResultDto
        {
            Message = "Recogida y valor del flete aprobados correctamente.",
            Record = await GetEnvioRecordCoreAsync(metadata, normalizedRecordId, httpContext.User, ct)
        };
    }

    public async Task<EnvioSaveResultDto> ConfirmEnvioDeliveryAsync(string recordId, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveEnvioMetadataAsync(httpContext.User, ct);
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var current = await GetEnvioRecordCoreAsync(metadata, normalizedRecordId, httpContext.User, ct);
        if (current.StatusValue != EnvioStatusPickupApproved)
            throw new InvalidOperationException("La entrega solo se puede confirmar despues de la aprobacion de recogida.");

        var currentUser = await GetCurrentUserAsync(ct) ?? new();
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [EnvioStatusField] = EnvioStatusDelivered,
            [EnvioDeliveryConfirmedAtField] = FormatEnvioDateTimeForDataverse(GetBogotaNow())
        };

        if (!string.IsNullOrWhiteSpace(currentUser.SystemUserId))
        {
            payload[$"{metadata.DeliveredByNavigationProperty}@odata.bind"] =
                $"/systemusers({NormalizeGuid(currentUser.SystemUserId, nameof(currentUser.SystemUserId))})";
        }

        await CallDataverseSendAsync(
            $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}({normalizedRecordId})",
            "PATCH",
            payload,
            httpContext.User,
            ct);

        return new EnvioSaveResultDto
        {
            Message = "Entrega confirmada correctamente.",
            Record = await GetEnvioRecordCoreAsync(metadata, normalizedRecordId, httpContext.User, ct)
        };
    }

    public async Task<EnvioFileUploadResultDto> ApproveEnvioDeliverySatisfactionAsync(
        string recordId,
        string fileName,
        string contentType,
        byte[] content,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveEnvioMetadataAsync(httpContext.User, ct);
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var current = await GetEnvioRecordCoreAsync(metadata, normalizedRecordId, httpContext.User, ct);
        if (current.StatusValue != EnvioStatusDelivered)
            throw new InvalidOperationException("El recibido a satisfaccion solo se puede registrar despues de que el transportador confirme la entrega.");

        var safeFileName = SanitizeRhFileName(fileName, "acta-entrega");
        ValidateEnvioDeliveryActUpload(safeFileName, content);

        using var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");

        using var fileResponse = await CallRhDataverseResponseAsync(
            $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}({normalizedRecordId})/{EnvioDeliveryActField}",
            "PATCH",
            httpContext.User,
            ct,
            fileContent,
            request =>
            {
                request.Headers.TryAddWithoutValidation("If-Match", "*");
                request.Headers.TryAddWithoutValidation("x-ms-file-name", BuildEnvioFileHeaderName(safeFileName));
            });

        var fileBody = await fileResponse.Content.ReadAsStringAsync(ct);
        if (!fileResponse.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse error {(int)fileResponse.StatusCode} {fileResponse.ReasonPhrase}. Body: {fileBody}");

        var currentUser = await GetCurrentUserAsync(ct) ?? new();
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [EnvioStatusField] = EnvioStatusClosed,
            [EnvioReceivedSatisfiedField] = true,
            [EnvioReceivedSatisfiedAtField] = FormatEnvioDateTimeForDataverse(GetBogotaNow())
        };

        if (!string.IsNullOrWhiteSpace(currentUser.SystemUserId))
        {
            payload[$"{metadata.ReceivedSatisfiedByNavigationProperty}@odata.bind"] =
                $"/systemusers({NormalizeGuid(currentUser.SystemUserId, nameof(currentUser.SystemUserId))})";
        }

        await CallDataverseSendAsync(
            $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}({normalizedRecordId})",
            "PATCH",
            payload,
            httpContext.User,
            ct);

        return new EnvioFileUploadResultDto
        {
            Message = "Recibido a satisfaccion registrado y acta cargada correctamente.",
            Record = await GetEnvioRecordCoreAsync(metadata, normalizedRecordId, httpContext.User, ct)
        };
    }

    public async Task<EnvioFileDownloadResult?> DownloadEnvioDeliveryActAsync(string recordId, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveEnvioMetadataAsync(httpContext.User, ct);
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var relativeUrl = $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}({normalizedRecordId})/{EnvioDeliveryActField}/$value";

        using var response = await CallRhDataverseResponseAsync(relativeUrl, "GET", httpContext.User, ct);
        if (response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.NotFound)
            return null;

        var bodyBytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var bodyText = bodyBytes.Length == 0 ? "" : Encoding.UTF8.GetString(bodyBytes);
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {bodyText}");
        }

        return new EnvioFileDownloadResult
        {
            FileName = FirstNonEmpty(
                ReadHeaderValue(response, "x-ms-file-name"),
                ReadHeaderValue(response, "filename"),
                $"ActaEntrega-{normalizedRecordId}.bin"),
            ContentType =
                response.Content.Headers.ContentType?.MediaType
                ?? ReadHeaderValue(response, "mimetype")
                ?? "application/octet-stream",
            Content = bodyBytes
        };
    }

    private async Task<EnviosBoardDto> GetEnviosBoardCoreAsync(
        string mode,
        int? year,
        int? month,
        bool filterForCurrentTransporter,
        CancellationToken ct)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveEnvioMetadataAsync(httpContext.User, ct);
        var rows = await LoadEnvioRowsAsync(metadata, httpContext.User, ct);
        if (filterForCurrentTransporter)
        {
            var currentUser = await GetCurrentUserAsync(ct) ?? new();
            rows = FilterRowsForTransporter(rows, currentUser.SystemUserId);
        }

        var now = GetBogotaToday();
        var selectedYear = year is >= 2000 and <= 2100 ? year.Value : now.Year;
        var selectedMonth = month is >= 1 and <= 12 ? month.Value : now.Month;
        var displayRows = rows
            .Where(row => ShouldDisplayEnvioRow(row, selectedYear, selectedMonth, filterForCurrentTransporter))
            .OrderBy(row => ResolveEnvioStatusSort(row, filterForCurrentTransporter))
            .ThenBy(row => ResolveEnvioSortDate(row))
            .ThenBy(row => row.ClientName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new EnviosBoardDto
        {
            Mode = mode,
            SelectedYear = selectedYear,
            SelectedMonth = selectedMonth,
            SelectedMonthValue = $"{selectedYear:D4}-{selectedMonth:D2}",
            SelectedMonthLabel = BuildEnvioMonthLabel(selectedYear, selectedMonth),
            CalendarDays = BuildEnvioCalendarDays(rows, selectedYear, selectedMonth),
            Records = displayRows,
            StatusOptions = DefaultEnvioStatusOptions,
            TotalRecords = displayRows.Count,
            OpenCount = displayRows.Count(row => row.StatusValue == EnvioStatusOpen),
            ScheduledCount = displayRows.Count(row => row.StatusValue == EnvioStatusScheduled),
            PickupApprovedCount = displayRows.Count(row => row.StatusValue == EnvioStatusPickupApproved),
            DeliveredCount = displayRows.Count(row => row.StatusValue == EnvioStatusDelivered),
            ClosedCount = displayRows.Count(row => row.StatusValue == EnvioStatusClosed),
            TotalFreightValue = RoundCurrency(displayRows.Sum(row => row.FreightValue)),
            Message = displayRows.Count == 0
                ? "No hay solicitudes de envio para el periodo seleccionado."
                : $"Se cargaron {displayRows.Count} solicitud(es) de envio."
        };
    }

    private async Task<EnvioMetadata> ResolveEnvioMetadataAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        const string cacheKey = EnvioLogicalName;
        if (_enviosMetadataCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var baseMetadata = await ResolveRhEntityMetadataAsync(
            EnvioLogicalName,
            EnvioFallbackEntitySetName,
            EnvioFallbackIdField,
            EnvioFallbackPrimaryNameField,
            user,
            ct);

        var metadata = new EnvioMetadata
        {
            BaseMetadata = baseMetadata,
            ClientNavigationProperty = await ResolveEnvioLookupNavigationPropertyAsync(EnvioClientField, user, ct),
            TransporterNavigationProperty = await ResolveEnvioLookupNavigationPropertyAsync(EnvioTransporterField, user, ct),
            PickupApprovedByNavigationProperty = await ResolveEnvioLookupNavigationPropertyAsync(EnvioPickupApprovedByField, user, ct),
            DeliveredByNavigationProperty = await ResolveEnvioLookupNavigationPropertyAsync(EnvioDeliveredByField, user, ct),
            ReceivedSatisfiedByNavigationProperty = await ResolveEnvioLookupNavigationPropertyAsync(EnvioReceivedSatisfiedByField, user, ct)
        };

        _enviosMetadataCache[cacheKey] = metadata;
        return metadata;
    }

    private async Task<string> ResolveEnvioLookupNavigationPropertyAsync(string lookupField, ClaimsPrincipal user, CancellationToken ct)
    {
        try
        {
            return await ResolveRhLookupNavigationPropertyAsync(
                EnvioLogicalName,
                lookupField,
                lookupField,
                user,
                ct);
        }
        catch (InvalidOperationException)
        {
            return lookupField;
        }
    }

    private async Task<IReadOnlyList<EnvioRowDto>> LoadEnvioRowsAsync(EnvioMetadata metadata, ClaimsPrincipal user, CancellationToken ct)
    {
        var relativeUrl =
            $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}?$select={BuildEnvioSelectClause(metadata)}" +
            $"&$orderby={EnvioCreatedOnField} desc";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        return items
            .Select(item => BuildEnvioRowDto(metadata, item))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();
    }

    private static string BuildEnvioSelectClause(EnvioMetadata metadata)
    {
        return string.Join(",",
            new[]
            {
                metadata.BaseMetadata.PrimaryIdField,
                metadata.BaseMetadata.PrimaryNameField,
                EnvioOriginField,
                EnvioDestinationField,
                BuildDashboardLookupValuePropertyName(EnvioClientField),
                EnvioWhatIsSentField,
                EnvioObservationsField,
                EnvioRecipientNameField,
                EnvioRecipientPhoneField,
                EnvioStatusField,
                EnvioScheduledAtField,
                BuildDashboardLookupValuePropertyName(EnvioTransporterField),
                EnvioFreightValueField,
                EnvioPickupApprovedField,
                EnvioPickupApprovedAtField,
                BuildDashboardLookupValuePropertyName(EnvioPickupApprovedByField),
                EnvioDeliveryConfirmedAtField,
                BuildDashboardLookupValuePropertyName(EnvioDeliveredByField),
                EnvioReceivedSatisfiedField,
                EnvioReceivedSatisfiedAtField,
                BuildDashboardLookupValuePropertyName(EnvioReceivedSatisfiedByField),
                EnvioDeliveryActField,
                EnvioDeliveryActNameField,
                EnvioCreatedOnField,
                EnvioModifiedOnField,
                BuildDashboardLookupValuePropertyName(EnvioCreatedByField)
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private EnvioRowDto? BuildEnvioRowDto(EnvioMetadata metadata, JsonElement item)
    {
        var recordId = FirstNonEmpty(
            ReadString(item, metadata.BaseMetadata.PrimaryIdField),
            ReadString(item, EnvioFallbackIdField));
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var clientLookupProperty = DetectLookupValueProperty(
            item,
            new[] { BuildDashboardLookupValuePropertyName(EnvioClientField), "_cr07a_clienteid_value" },
            "cliente");
        var transporterLookupProperty = DetectLookupValueProperty(
            item,
            new[] { BuildDashboardLookupValuePropertyName(EnvioTransporterField) },
            "transportador");
        var pickupApprovedByLookupProperty = DetectLookupValueProperty(
            item,
            new[] { BuildDashboardLookupValuePropertyName(EnvioPickupApprovedByField) },
            "recogidaaprobadapor");
        var deliveredByLookupProperty = DetectLookupValueProperty(
            item,
            new[] { BuildDashboardLookupValuePropertyName(EnvioDeliveredByField) },
            "entregadapor");
        var receivedSatisfiedByLookupProperty = DetectLookupValueProperty(
            item,
            new[] { BuildDashboardLookupValuePropertyName(EnvioReceivedSatisfiedByField) },
            "recibidosatisfaccionpor");
        var createdByLookupProperty = DetectLookupValueProperty(
            item,
            new[] { BuildDashboardLookupValuePropertyName(EnvioCreatedByField) },
            "createdby");
        var statusValue = ReadIntFlexible(item, EnvioStatusField);
        var createdOn = ReadEnvioDateTime(item, EnvioCreatedOnField);
        var scheduledAt = ReadEnvioDateTime(item, EnvioScheduledAtField);
        var deliveryActToken = ReadString(item, EnvioDeliveryActField).Trim();
        var deliveryActName = ReadString(item, EnvioDeliveryActNameField).Trim();

        return new EnvioRowDto
        {
            RecordId = recordId,
            Name = FirstNonEmpty(ReadString(item, metadata.BaseMetadata.PrimaryNameField), recordId),
            Origin = ReadString(item, EnvioOriginField).Trim(),
            Destination = ReadString(item, EnvioDestinationField).Trim(),
            ClientId = ReadString(item, clientLookupProperty).Trim(),
            ClientName = FirstNonEmpty(
                ReadLookupFormattedValue(item, clientLookupProperty),
                ReadString(item, $"{EnvioClientField}{FormattedValueAnnotationSuffix}"),
                "Sin cliente"),
            WhatIsSent = ReadString(item, EnvioWhatIsSentField).Trim(),
            Observations = ReadString(item, EnvioObservationsField).Trim(),
            RecipientName = ReadString(item, EnvioRecipientNameField).Trim(),
            RecipientPhone = ReadString(item, EnvioRecipientPhoneField).Trim(),
            StatusValue = statusValue,
            StatusLabel = ResolveEnvioStatusLabel(item, statusValue),
            RequestDateValue = FormatEnvioDateTimeValue(createdOn),
            RequestDateDisplay = FormatEnvioDateTimeDisplay(createdOn),
            ScheduledAtValue = FormatEnvioDateTimeValue(scheduledAt),
            ScheduledAtDisplay = FormatEnvioDateTimeDisplay(scheduledAt),
            TransporterId = ReadString(item, transporterLookupProperty).Trim(),
            TransporterName = FirstNonEmpty(ReadLookupFormattedValue(item, transporterLookupProperty), "Sin transportador"),
            FreightValue = RoundCurrency(ReadDecimal(item, EnvioFreightValueField) ?? 0m),
            PickupApproved = ReadBool(item, EnvioPickupApprovedField),
            PickupApprovedAtDisplay = FormatEnvioDateTimeDisplay(ReadEnvioDateTime(item, EnvioPickupApprovedAtField)),
            PickupApprovedByName = FirstNonEmpty(ReadLookupFormattedValue(item, pickupApprovedByLookupProperty), ""),
            DeliveryConfirmedAtDisplay = FormatEnvioDateTimeDisplay(ReadEnvioDateTime(item, EnvioDeliveryConfirmedAtField)),
            DeliveredByName = FirstNonEmpty(ReadLookupFormattedValue(item, deliveredByLookupProperty), ""),
            ReceivedSatisfied = ReadBool(item, EnvioReceivedSatisfiedField),
            ReceivedSatisfiedAtDisplay = FormatEnvioDateTimeDisplay(ReadEnvioDateTime(item, EnvioReceivedSatisfiedAtField)),
            ReceivedSatisfiedByName = FirstNonEmpty(ReadLookupFormattedValue(item, receivedSatisfiedByLookupProperty), ""),
            HasDeliveryAct = !string.IsNullOrWhiteSpace(deliveryActToken) || !string.IsNullOrWhiteSpace(deliveryActName),
            DeliveryActFileName = FirstNonEmpty(deliveryActName, !string.IsNullOrWhiteSpace(deliveryActToken) ? "Acta de entrega" : ""),
            CreatedById = ReadString(item, createdByLookupProperty).Trim(),
            CreatedByName = FirstNonEmpty(ReadLookupFormattedValue(item, createdByLookupProperty), "Sin creador"),
            ModifiedOnDisplay = FormatEnvioDateTimeDisplay(ReadEnvioDateTime(item, EnvioModifiedOnField))
        };
    }

    private async Task<EnvioRowDto> ResolveEnvioSavedRecordAsync(
        EnvioMetadata metadata,
        string body,
        string recordId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(body))
        {
            using var doc = JsonDocument.Parse(body);
            var inlineRecord = BuildEnvioRowDto(metadata, doc.RootElement);
            if (inlineRecord is not null)
                return inlineRecord;
        }

        if (string.IsNullOrWhiteSpace(recordId))
            throw new InvalidOperationException("No fue posible identificar el envio guardado.");

        return await GetEnvioRecordCoreAsync(metadata, recordId, user, ct);
    }

    private async Task<EnvioRowDto> GetEnvioRecordCoreAsync(
        EnvioMetadata metadata,
        string recordId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var relativeUrl =
            $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}({NormalizeGuid(recordId, nameof(recordId))})" +
            $"?$select={BuildEnvioSelectClause(metadata)}";
        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        using var doc = JsonDocument.Parse(json);
        return BuildEnvioRowDto(metadata, doc.RootElement)
            ?? throw new InvalidOperationException("No fue posible reconstruir el envio guardado.");
    }

    private async Task<EnvioCreateWriteModel> NormalizeEnvioCreateRequestAsync(EnvioCreateRequest request, CancellationToken ct)
    {
        var origin = request.Origin?.Trim() ?? "";
        var destination = request.Destination?.Trim() ?? "";
        var clientId = request.ClientId?.Trim() ?? "";
        var clientName = request.ClientName?.Trim() ?? "";
        var whatIsSent = request.WhatIsSent?.Trim() ?? "";
        var observations = request.Observations?.Trim() ?? "";
        var recipientName = request.RecipientName?.Trim() ?? "";
        var recipientPhone = request.RecipientPhone?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(origin))
            throw new InvalidOperationException("El origen es obligatorio.");

        if (string.IsNullOrWhiteSpace(destination))
            throw new InvalidOperationException("El destino es obligatorio.");

        if (string.IsNullOrWhiteSpace(clientId) && string.IsNullOrWhiteSpace(clientName))
            throw new InvalidOperationException("Debes seleccionar un cliente.");

        if (string.IsNullOrWhiteSpace(whatIsSent))
            throw new InvalidOperationException("Debes indicar que se envia.");

        if (string.IsNullOrWhiteSpace(recipientName))
            throw new InvalidOperationException("Debes indicar quien recibe.");

        if (string.IsNullOrWhiteSpace(recipientPhone))
            throw new InvalidOperationException("Debes indicar el telefono de quien recibe.");

        if (string.IsNullOrWhiteSpace(clientId))
            clientId = await ResolveEnvioClientIdAsync(clientName, ct);

        return new EnvioCreateWriteModel
        {
            Origin = origin,
            Destination = destination,
            ClientId = clientId,
            ClientName = clientName,
            WhatIsSent = whatIsSent,
            Observations = observations,
            RecipientName = recipientName,
            RecipientPhone = recipientPhone
        };
    }

    private async Task<string> ResolveEnvioClientIdAsync(string clientName, CancellationToken ct)
    {
        var matches = await SearchClientsAsync(clientName, top: 20, ct);
        var normalizedQuery = NormalizeEnvioText(clientName);
        var exactMatch = matches.FirstOrDefault(item =>
            string.Equals(NormalizeEnvioText(item.Name), normalizedQuery, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(exactMatch?.Id))
            return exactMatch.Id;

        var startsWithMatch = matches.FirstOrDefault(item =>
            NormalizeEnvioText(item.Name).StartsWith(normalizedQuery, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(startsWithMatch?.Id))
            return startsWithMatch.Id;

        var firstMatch = matches.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(firstMatch?.Id))
            return firstMatch.Id;

        throw new InvalidOperationException("No fue posible encontrar el cliente seleccionado.");
    }

    private static IReadOnlyList<EnvioRowDto> FilterRowsForTransporter(IReadOnlyList<EnvioRowDto> rows, string? currentSystemUserId)
    {
        var normalizedUserId = NormalizeOptionalGuid(currentSystemUserId);
        return rows
            .Where(row =>
                row.StatusValue == EnvioStatusOpen
                || (!string.IsNullOrWhiteSpace(normalizedUserId)
                    && string.Equals(NormalizeOptionalGuid(row.TransporterId), normalizedUserId, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private static bool ShouldDisplayEnvioRow(EnvioRowDto row, int selectedYear, int selectedMonth, bool isTransporterMode)
    {
        if (row.StatusValue == EnvioStatusOpen)
            return true;

        if (TryGetEnvioDateFromValue(row.ScheduledAtValue, out var scheduledDate))
            return scheduledDate.Year == selectedYear && scheduledDate.Month == selectedMonth;

        if (isTransporterMode)
            return row.StatusValue is EnvioStatusScheduled or EnvioStatusPickupApproved or EnvioStatusDelivered;

        return row.StatusValue != EnvioStatusClosed;
    }

    private static int ResolveEnvioStatusSort(EnvioRowDto row, bool isTransporterMode)
    {
        if (isTransporterMode)
        {
            return row.StatusValue switch
            {
                EnvioStatusOpen => 0,
                EnvioStatusPickupApproved => 1,
                EnvioStatusScheduled => 2,
                EnvioStatusDelivered => 3,
                EnvioStatusClosed => 4,
                _ => 9
            };
        }

        return row.StatusValue switch
        {
            EnvioStatusDelivered => 0,
            EnvioStatusScheduled => 1,
            EnvioStatusOpen => 2,
            EnvioStatusPickupApproved => 3,
            EnvioStatusClosed => 4,
            _ => 9
        };
    }

    private static DateTime ResolveEnvioSortDate(EnvioRowDto row)
    {
        if (TryGetEnvioDateTimeFromValue(row.ScheduledAtValue, out var scheduledAt))
            return scheduledAt;

        if (TryGetEnvioDateTimeFromValue(row.RequestDateValue, out var requestDate))
            return requestDate;

        return DateTime.MaxValue;
    }

    private static IReadOnlyList<EnvioCalendarDayDto> BuildEnvioCalendarDays(IReadOnlyList<EnvioRowDto> rows, int year, int month)
    {
        var days = DateTime.DaysInMonth(year, month);
        var scheduledCounts = rows
            .Select(row => TryGetEnvioDateFromValue(row.ScheduledAtValue, out var date) ? date : (DateOnly?)null)
            .Where(date => date.HasValue && date.Value.Year == year && date.Value.Month == month)
            .GroupBy(date => date!.Value.Day)
            .ToDictionary(group => group.Key, group => group.Count());

        return Enumerable.Range(1, days)
            .Select(day =>
            {
                var date = new DateOnly(year, month, day);
                scheduledCounts.TryGetValue(day, out var count);
                return new EnvioCalendarDayDto
                {
                    DayNumber = day,
                    DateValue = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    DateDisplay = date.ToString("dd/MM/yyyy", EnviosCulture),
                    ScheduledCount = count
                };
            })
            .ToList();
    }

    private static string ResolveEnvioStatusLabel(JsonElement item, int statusValue)
    {
        var formattedValue = ReadString(item, $"{EnvioStatusField}{FormattedValueAnnotationSuffix}").Trim();
        if (!string.IsNullOrWhiteSpace(formattedValue))
            return formattedValue;

        return DefaultEnvioStatusOptions.FirstOrDefault(option => option.Value == statusValue)?.Label
            ?? "Sin estado";
    }

    private static DateTimeOffset ParseEnvioScheduledAt(string? rawValue)
    {
        var raw = rawValue?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("Debes indicar fecha y hora de agenda.");

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dto))
            return dto;

        if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var localDateTime))
            throw new InvalidOperationException("La fecha y hora de agenda no es valida.");

        if (localDateTime.Kind != DateTimeKind.Unspecified)
            return new DateTimeOffset(localDateTime);

        var timeZone = ResolveBogotaTimeZone();
        return new DateTimeOffset(localDateTime, timeZone.GetUtcOffset(localDateTime));
    }

    private static DateTimeOffset GetBogotaNow()
    {
        return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ResolveBogotaTimeZone());
    }

    private static TimeZoneInfo ResolveBogotaTimeZone()
    {
        foreach (var timeZoneId in new[] { "SA Pacific Standard Time", "America/Bogota" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }

    private static string FormatEnvioDateTimeForDataverse(DateTimeOffset value)
    {
        return value.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ReadEnvioDateTime(JsonElement item, string fieldName)
    {
        var raw = ReadString(item, fieldName).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
            return dto;

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            return new DateTimeOffset(dt);

        return null;
    }

    private static string FormatEnvioDateTimeValue(DateTimeOffset? value)
    {
        if (!value.HasValue)
            return "";

        var local = TimeZoneInfo.ConvertTime(value.Value, ResolveBogotaTimeZone());
        return local.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
    }

    private static string FormatEnvioDateTimeDisplay(DateTimeOffset? value)
    {
        if (!value.HasValue)
            return "";

        var local = TimeZoneInfo.ConvertTime(value.Value, ResolveBogotaTimeZone());
        return local.ToString("dd/MM/yyyy HH:mm", EnviosCulture);
    }

    private static bool TryGetEnvioDateFromValue(string? rawValue, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(rawValue) || rawValue.Length < 10)
            return false;

        return DateOnly.TryParse(rawValue[..10], CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static bool TryGetEnvioDateTimeFromValue(string? rawValue, out DateTime dateTime)
    {
        if (DateTime.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out dateTime))
            return true;

        dateTime = default;
        return false;
    }

    private static string BuildEnvioPrimaryName(EnvioCreateWriteModel request)
    {
        var today = GetBogotaToday();
        return $"Envio {today:yyyyMMdd} - {FirstNonEmpty(request.ClientName, "Cliente")} - {request.Destination}".Trim();
    }

    private static string BuildEnvioMonthLabel(int year, int month)
    {
        var monthName = month is >= 1 and <= 12
            ? EnviosCulture.DateTimeFormat.GetMonthName(month)
            : "Mes";

        return $"{monthName} {year:D4}";
    }

    private static string NormalizeEnvioText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static void ValidateEnvioDeliveryActUpload(string fileName, byte[] content)
    {
        if (content.Length == 0)
            throw new InvalidOperationException("El acta de entrega esta vacia.");

        if (content.Length > 128 * 1024 * 1024)
            throw new InvalidOperationException("El acta de entrega supera el limite permitido de 128 MB.");

        var extension = Path.GetExtension(fileName ?? "");
        if (string.IsNullOrWhiteSpace(extension) || !EnvioDeliveryActAllowedExtensions.Contains(extension))
            throw new InvalidOperationException("El acta debe ser PDF, JPG/JPEG, PNG, DOC o DOCX.");
    }

    private static string BuildEnvioFileHeaderName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "acta-entrega";

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
        return string.IsNullOrWhiteSpace(headerFileName) ? "acta-entrega" : headerFileName;
    }

    private sealed class EnvioMetadata
    {
        public RhEntityMetadata BaseMetadata { get; init; } = new();
        public string ClientNavigationProperty { get; init; } = EnvioClientField;
        public string TransporterNavigationProperty { get; init; } = EnvioTransporterField;
        public string PickupApprovedByNavigationProperty { get; init; } = EnvioPickupApprovedByField;
        public string DeliveredByNavigationProperty { get; init; } = EnvioDeliveredByField;
        public string ReceivedSatisfiedByNavigationProperty { get; init; } = EnvioReceivedSatisfiedByField;
    }

    private sealed class EnvioCreateWriteModel
    {
        public string Origin { get; init; } = "";
        public string Destination { get; init; } = "";
        public string ClientId { get; init; } = "";
        public string ClientName { get; init; } = "";
        public string WhatIsSent { get; init; } = "";
        public string Observations { get; init; } = "";
        public string RecipientName { get; init; } = "";
        public string RecipientPhone { get; init; } = "";
    }
}
