using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using CotizadorInterno.Web.Models.Copiers;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.RH;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string CopiersCountersLogicalName = "cr07a_contadores";
    private const string CopiersCountersPrimaryIdField = "cr07a_contadoresid";
    private const string CopiersCountersPrimaryNameField = "cr07a_name";
    private const string CopiersCountersStatePageField = "cr07a_paginadeestado";
    private const int CopiersMaintenanceTypePreventive = 645250001;
    private const string CopiersPreventiveScheduleLogicalName = "cr07a_mantenimientopreventivoagenda";
    private const string CopiersPreventiveScheduleSetName = "cr07a_mantenimientopreventivoagendas";
    private const string CopiersPreventiveSchedulePrimaryIdField = "cr07a_mantenimientopreventivoagendaid";
    private const string CopiersPreventiveSchedulePrimaryNameField = "cr07a_name";
    private const string CopiersPreventiveScheduleClientField = "cr07a_cliente";
    private const string CopiersPreventiveScheduleClientIdTextField = "cr07a_clienteidtexto";
    private const string CopiersPreventiveScheduleClientNameField = "cr07a_nombrecliente";
    private const string CopiersPreventiveSchedulePeriodField = "cr07a_periodo";
    private const string CopiersPreventiveScheduleDateField = "cr07a_fechaprogramada";
    private const string CopiersPreventiveScheduleDurationField = "cr07a_duracionminutos";
    private const string CopiersPreventiveScheduleEventIdField = "cr07a_eventographid";
    private const string CopiersPreventiveScheduleWebLinkField = "cr07a_eventoweblink";
    private const string CopiersPreventivePeriodThisMonth = "this-month";
    private const string CopiersPreventivePeriodPreviousMonth = "previous-month";

    public async Task<CopiersPreventiveMaintenanceBoardDto> GetCopiersPreventiveMaintenanceBoardAsync(
        string? period = null,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var today = GetBogotaToday();
        var periodDefinition = ResolveCopiersPreventivePeriod(period, today);
        var periodStart = periodDefinition.StartInclusive;
        var periodEnd = periodDefinition.EndExclusive;
        var periodKey = periodDefinition.Value;
        var dashboard = await GetCopiersDashboardCoreAsync(
            periodStart,
            periodEnd,
            periodDefinition.Label,
            ct);
        var dashboardEquipment = dashboard.Groups
            .SelectMany(static group => group.Equipment)
            .Where(static item => !string.IsNullOrWhiteSpace(item.RecordId))
            .ToList();
        var monthlyCounterEquipmentIds = dashboardEquipment
            .Where(static item => item.HasCurrentCounter)
            .Select(static item => NormalizeOptionalGuid(item.RecordId))
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var monthlyMaintenanceEquipmentIds = await GetPreventiveMonthlyMaintenanceEquipmentIdsAsync(
            periodStart,
            periodEnd,
            httpContext.User,
            ct);
        var schedulesByClientKey = await GetPreventiveSchedulesByClientKeyAsync(periodKey, httpContext.User, ct);
        var clientSettingsById = await GetPreventiveClientSettingsByIdAsync(
            dashboard.Groups.Select(static group => group.ClientId),
            httpContext.User,
            ct);
        var currentUser = await GetCurrentUserAsync(ct);
        var canEditMaintenanceFrequency = CopiersAccessPolicy.CanEditPreventiveMaintenanceFrequency(currentUser);
        var clients = dashboard.Groups
            .GroupBy(group => BuildDashboardGroupKey(group.ClientId, group.ClientName), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var groups = group.ToList();
                var first = groups[0];
                var clientId = NormalizeOptionalGuid(first.ClientId);
                clientSettingsById.TryGetValue(clientId, out var clientSettings);
                var maintenanceFrequency = ResolvePreventiveMaintenanceFrequencyValue(clientSettings?.MaintenanceFrequency)
                    ?? ResolvePreventiveMaintenanceFrequency(groups);
                var equipment = groups
                    .SelectMany(static item => item.Equipment)
                    .Where(static item => !string.IsNullOrWhiteSpace(item.RecordId))
                    .GroupBy(static item => item.RecordId, StringComparer.OrdinalIgnoreCase)
                    .Select(static equipmentGroup => equipmentGroup.First())
                    .Select(item =>
                    {
                        var dto = ToPreventiveMaintenanceEquipment(item);
                        var equipmentId = NormalizeOptionalGuid(dto.RecordId);
                        dto.HasMonthlyMaintenance = monthlyMaintenanceEquipmentIds.Contains(equipmentId);
                        dto.HasMonthlyCounter = monthlyCounterEquipmentIds.Contains(equipmentId);
                        dto.MaintenanceButtonLabel = dto.HasMonthlyMaintenance
                            ? "Mantenimiento registrado"
                            : "Registrar mantenimiento";
                        dto.MaintenanceButtonTone = dto.HasMonthlyMaintenance ? "success" : "outline-primary";
                        dto.CounterButtonLabel = dto.HasMonthlyCounter
                            ? "Contador registrado"
                            : "Registrar contador";
                        dto.CounterButtonTone = dto.HasMonthlyCounter ? "success" : "outline-secondary";
                        return dto;
                    })
                    .OrderBy(static item => item.Serial, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                schedulesByClientKey.TryGetValue(group.Key, out var schedule);
                var maintenanceRegistered = equipment.Count(static item => item.HasMonthlyMaintenance);
                var pendingMaintenance = equipment.Count - maintenanceRegistered;
                var countersRegistered = equipment.Count(static item => item.HasMonthlyCounter);
                var pendingCounters = equipment.Count - countersRegistered;
                var clientState = ResolvePreventiveClientState(
                    equipment.Count,
                    maintenanceRegistered,
                    countersRegistered);

                return new CopiersPreventiveMaintenanceClientDto
                {
                    ClientKey = group.Key,
                    ClientId = clientId,
                    ClientName = FirstNonEmpty(first.ClientName, "Sin cliente"),
                    ClientCity = clientSettings?.City ?? "",
                    EquipmentCount = equipment.Count,
                    MaintenanceFrequencyKey = maintenanceFrequency.Key,
                    MaintenanceFrequencyLabel = maintenanceFrequency.Label,
                    IsBimonthlyMaintenance = maintenanceFrequency.IsBimonthly,
                    IsScheduledThisMonth = schedule is not null,
                    ScheduledDateDisplay = schedule?.ScheduledDateDisplay ?? "",
                    MonthlyStatusLabel = clientState.StatusLabel,
                    MonthlyStatusTone = clientState.StatusTone,
                    ScheduleButtonLabel = clientState.ButtonLabel,
                    ScheduleButtonTone = clientState.ButtonTone,
                    ScheduleButtonDisabled = clientState.ButtonDisabled,
                    MaintenanceRegisteredCount = maintenanceRegistered,
                    PendingMaintenanceCount = pendingMaintenance,
                    CountersRegisteredCount = countersRegistered,
                    PendingCountersCount = pendingCounters,
                    Equipment = equipment
                };
            })
            .OrderBy(static item => item.ClientName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new CopiersPreventiveMaintenanceBoardDto
        {
            AsOfDateLabel = dashboard.AsOfDateLabel,
            CounterPeriodLabel = periodDefinition.Label,
            PeriodFilter = periodDefinition.Filter,
            PeriodLabel = periodDefinition.Label,
            PeriodValue = periodKey,
            PeriodStartValue = periodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PeriodEndValue = periodEnd.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            CanEditMaintenanceFrequency = canEditMaintenanceFrequency,
            RecordsCount = clients.Count,
            Clients = clients
        };
    }

    public async Task<CopiersPreventiveMaintenanceFrequencyUpdateResultDto> UpdateCopiersPreventiveMaintenanceFrequencyAsync(
        CopiersPreventiveMaintenanceFrequencyUpdateRequestDto request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var currentUser = await GetCurrentUserAsync(ct);
        if (!CopiersAccessPolicy.CanEditPreventiveMaintenanceFrequency(currentUser))
        {
            throw new UnauthorizedAccessException(
                "Solo adaza@digitaltechcolombia.com o sruiz@digitaltechcolombia.com pueden cambiar esta periodicidad.");
        }

        var clientId = NormalizeGuid(request.ClientId, nameof(request.ClientId));
        var frequency = ResolvePreventiveMaintenanceFrequencyValue(request.FrequencyKey)
            ?? throw new InvalidOperationException("Selecciona una periodicidad valida: mensual o bimensual.");
        var clientMetadata = await ResolveRhEntityMetadataAsync(
            "cr07a_cliente",
            ClientsEntitySetName,
            CopiersClientIdField,
            CopiersClientNameField,
            httpContext.User,
            ct);
        var frequencyField = await EnsurePreventiveClientFrequencyFieldAsync(clientMetadata, httpContext.User, ct);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [frequencyField] = frequency.Key
        };

        await CallDataverseSendAsync(
            $"/api/data/v9.2/{clientMetadata.EntitySetName}({clientId})",
            "PATCH",
            payload,
            httpContext.User,
            ct);

        return new CopiersPreventiveMaintenanceFrequencyUpdateResultDto
        {
            Message = $"Periodicidad actualizada a {frequency.Label}.",
            ClientId = clientId,
            ClientName = (request.ClientName ?? "").Trim(),
            MaintenanceFrequencyKey = frequency.Key,
            MaintenanceFrequencyLabel = frequency.Label,
            IsBimonthlyMaintenance = frequency.IsBimonthly
        };
    }

    public async Task SaveCopiersPreventiveMaintenanceScheduleAsync(
        CopiersPreventiveMaintenanceScheduleRequestDto request,
        CopiersPreventiveMaintenanceScheduleResultDto calendarResult,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var clientName = (request.ClientName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clientName))
            throw new InvalidOperationException("Debes indicar el cliente del mantenimiento preventivo.");

        if (!DateOnly.TryParse(request.DateValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new InvalidOperationException("Debes seleccionar una fecha valida.");

        if (!TimeOnly.TryParse(request.TimeValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            throw new InvalidOperationException("Debes seleccionar una hora valida.");

        var scheduleMetadata = await ResolveCopiersPreventiveScheduleMetadataAsync(httpContext.User, ct);
        var clientId = NormalizeOptionalGuid(request.ClientId);
        var periodKey = new DateOnly(date.Year, date.Month, 1).ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var durationMinutes = Math.Clamp(request.DurationMinutes ?? 60, 15, 480);
        var scheduledLocal = date.ToDateTime(time);
        var scheduledUtc = new DateTimeOffset(scheduledLocal, TimeSpan.FromHours(-5)).UtcDateTime;
        var title = $"Mantenimiento preventivo - {clientName} - {periodKey}";
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [scheduleMetadata.PrimaryNameField] = title,
            [CopiersPreventiveScheduleClientIdTextField] = clientId,
            [CopiersPreventiveScheduleClientNameField] = clientName,
            [CopiersPreventiveSchedulePeriodField] = periodKey,
            [CopiersPreventiveScheduleDateField] = scheduledUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            [CopiersPreventiveScheduleDurationField] = durationMinutes,
            [CopiersPreventiveScheduleEventIdField] = calendarResult?.EventId?.Trim(),
            [CopiersPreventiveScheduleWebLinkField] = calendarResult?.WebLink?.Trim()
        };

        if (!string.IsNullOrWhiteSpace(clientId))
        {
            var clientMetadata = await ResolveRhEntityMetadataAsync(
                "cr07a_cliente",
                "cr07a_clientes",
                CopiersClientIdField,
                CopiersClientNameField,
                httpContext.User,
                ct);
            var clientNavigationProperty = await ResolveRhLookupNavigationPropertyAsync(
                scheduleMetadata.LogicalName,
                CopiersPreventiveScheduleClientField,
                CopiersPreventiveScheduleClientField,
                httpContext.User,
                ct);
            payload[$"{clientNavigationProperty}@odata.bind"] = $"/{clientMetadata.EntitySetName}({clientId})";
        }

        var existing = await FindPreventiveScheduleAsync(scheduleMetadata, periodKey, clientId, clientName, httpContext.User, ct);
        var relativeUrl = string.IsNullOrWhiteSpace(existing?.RecordId)
            ? $"/api/data/v9.2/{scheduleMetadata.EntitySetName}"
            : $"/api/data/v9.2/{scheduleMetadata.EntitySetName}({existing.RecordId})";
        var method = string.IsNullOrWhiteSpace(existing?.RecordId) ? "POST" : "PATCH";
        using var response = await SendDataversePayloadWithRepresentationAsync(relativeUrl, method, payload, httpContext.User, ct);
        _ = await response.Content.ReadAsStringAsync(ct);
    }

    public async Task<CopiersCounterSaveResultDto> SaveCopiersCounterAsync(
        CopiersCounterSaveRequestDto request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        if (!request.CopiesCounter.HasValue && !request.ScansCounter.HasValue)
            throw new InvalidOperationException("Debes registrar al menos un contador de impresora o escaner.");

        if (request.CopiesCounter is < 0 || request.ScansCounter is < 0)
            throw new InvalidOperationException("Los contadores no pueden ser negativos.");

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var counterDate = ParseCopiersRequiredDate(request.DateValue, "fecha de toma de contador");
        var equipmentId = NormalizeGuid(request.EquipmentId, nameof(request.EquipmentId));
        var counterMetadata = await ResolveCopiersCounterMetadataAsync(httpContext.User, ct);
        var equipmentMetadata = await ResolveRhEntityMetadataAsync(
            DashboardEquipmentTableLogicalName,
            DashboardEquipmentTableSetName,
            DashboardEquipmentIdField,
            DashboardEquipmentPrimaryNameField,
            httpContext.User,
            ct);
        var equipment = await GetEquipmentRecordByIdAsync(equipmentMetadata, equipmentId, httpContext.User, ct)
            ?? throw new InvalidOperationException("No encontramos el equipo seleccionado para registrar el contador.");
        var equipmentNavigationProperty = await ResolveRhLookupNavigationPropertyAsync(
            counterMetadata.LogicalName,
            CopiersLegacyCountersEquipmentField,
            CopiersLegacyCountersEquipmentField,
            httpContext.User,
            ct);
        var title = $"Contador {FirstNonEmpty(equipment.Serial, equipmentId)} {counterDate:yyyy-MM-dd}";
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [CopiersLegacyCountersDateField] = counterDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            [$"{equipmentNavigationProperty}@odata.bind"] = $"/{equipmentMetadata.EntitySetName}({equipmentId})"
        };

        if (request.CopiesCounter.HasValue)
            payload[CopiersLegacyCountersCopiesField] = request.CopiesCounter.Value;

        if (request.ScansCounter.HasValue)
            payload[CopiersLegacyCountersScansField] = request.ScansCounter.Value;

        if (!string.IsNullOrWhiteSpace(counterMetadata.PrimaryNameField)
            && !string.Equals(counterMetadata.PrimaryNameField, counterMetadata.PrimaryIdField, StringComparison.OrdinalIgnoreCase))
        {
            payload[counterMetadata.PrimaryNameField] = title;
        }

        using var response = await SendDataversePayloadWithRepresentationAsync(
            $"/api/data/v9.2/{counterMetadata.EntitySetName}",
            "POST",
            payload,
            httpContext.User,
            ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var recordId = ExtractRhRecordId(response, body, counterMetadata.PrimaryIdField);

        return new CopiersCounterSaveResultDto
        {
            Message = "Contador registrado correctamente.",
            RecordId = recordId
        };
    }

    public async Task<CopiersCounterSaveResultDto> UploadCopiersCounterAttachmentAsync(
        string counterId,
        string fileName,
        string contentType,
        byte[] content,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveCopiersCounterMetadataAsync(httpContext.User, ct);
        var normalizedCounterId = NormalizeGuid(counterId, nameof(counterId));
        await UploadCopiersFileColumnAsync(
            metadata,
            normalizedCounterId,
            CopiersCountersStatePageField,
            fileName,
            contentType,
            content,
            httpContext.User,
            ct);

        return new CopiersCounterSaveResultDto
        {
            Message = "Pagina de estado adjuntada correctamente.",
            RecordId = normalizedCounterId
        };
    }

    private async Task<HashSet<string>> GetPreventiveMonthlyMaintenanceEquipmentIdsAsync(
        DateOnly periodStart,
        DateOnly periodEnd,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var metadata = await ResolveRhEntityMetadataAsync(
            DashboardMaintenanceTableLogicalName,
            DashboardMaintenanceTableSetName,
            DashboardMaintenanceIdField,
            DashboardMaintenancePrimaryNameField,
            user,
            ct);
        var rows = await GetMaintenanceRecordsAsync(metadata, user, ct);

        return rows
            .Where(row =>
                row.MaintenanceTypeValue == CopiersMaintenanceTypePreventive
                && row.MaintenanceDate.HasValue
                && row.MaintenanceDate.Value >= periodStart
                && row.MaintenanceDate.Value < periodEnd)
            .Select(static row => NormalizeOptionalGuid(row.EquipmentId))
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<IReadOnlyDictionary<string, CopiersPreventiveScheduleRow>> GetPreventiveSchedulesByClientKeyAsync(
        string periodKey,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        try
        {
            var metadata = await ResolveCopiersPreventiveScheduleMetadataAsync(user, ct);
            var filters = new List<string>
            {
                $"{CopiersPreventiveSchedulePeriodField} eq '{EscapeOdataLiteral(periodKey)}'"
            };
            var ownerCondition = await BuildCurrentOwnerConditionAsync(ct);
            if (!string.IsNullOrWhiteSpace(ownerCondition))
                filters.Add(ownerCondition);
            var filter = string.Join(" and ", filters);
            var relativeUrl =
                $"/api/data/v9.2/{metadata.EntitySetName}?$select={BuildPreventiveScheduleSelectClause(metadata)}" +
                $"&$filter={Uri.EscapeDataString(filter)}" +
                $"&$orderby={CopiersPreventiveScheduleDateField} desc";
            var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

            return items
                .Select(item => ParsePreventiveScheduleRow(item, metadata.PrimaryIdField, metadata.PrimaryNameField))
                .Where(static item => item is not null)
                .Cast<CopiersPreventiveScheduleRow>()
                .GroupBy(static item => item.ClientKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        }
        catch (InvalidOperationException ex) when (ShouldTryNextCopiersCounterSource(ex))
        {
            _logger.LogWarning(
                ex,
                "No fue posible leer la tabla de programacion de mantenimientos preventivos. Se mostrara la lista sin estado programado.");
            return new Dictionary<string, CopiersPreventiveScheduleRow>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task<IReadOnlyDictionary<string, CopiersPreventiveClientSettings>> GetPreventiveClientSettingsByIdAsync(
        IEnumerable<string> clientIds,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var normalizedIds = clientIds
            .Select(NormalizeOptionalGuid)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedIds.Count == 0)
            return new Dictionary<string, CopiersPreventiveClientSettings>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var clientMetadata = await ResolveRhEntityMetadataAsync(
                "cr07a_cliente",
                ClientsEntitySetName,
                CopiersClientIdField,
                CopiersClientNameField,
                user,
                ct);
            var attributes = await GetDashboardEntityAttributeNamesAsync(clientMetadata.LogicalName, user, ct);
            var cityField = ResolvePreventiveClientCityField(attributes);
            var frequencyField = ResolvePreventiveClientFrequencyField(attributes);
            if (string.IsNullOrWhiteSpace(cityField) && string.IsNullOrWhiteSpace(frequencyField))
            {
                _logger.LogWarning(
                    "No se encontraron columnas de ciudad o frecuencia de mantenimiento preventivo en la tabla cliente ({ClientEntityLogicalName}).",
                    clientMetadata.LogicalName);
                return new Dictionary<string, CopiersPreventiveClientSettings>(StringComparer.OrdinalIgnoreCase);
            }

            var select = string.Join(",", new List<string?>
            {
                clientMetadata.PrimaryIdField,
                CopiersClientIdField,
                cityField,
                frequencyField
            }
            .Where(static field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase));
            var result = new Dictionary<string, CopiersPreventiveClientSettings>(StringComparer.OrdinalIgnoreCase);

            foreach (var chunk in normalizedIds.Chunk(20))
            {
                var filter = string.Join(
                    " or ",
                    chunk.Select(id => $"{clientMetadata.PrimaryIdField} eq {id}"));
                var relativeUrl =
                    $"/api/data/v9.2/{clientMetadata.EntitySetName}?$select={select}" +
                    $"&$filter={Uri.EscapeDataString(filter)}";
                var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

                foreach (var item in items)
                {
                    var clientId = NormalizeOptionalGuid(FirstNonEmpty(
                        ReadString(item, clientMetadata.PrimaryIdField),
                        ReadString(item, CopiersClientIdField)));
                    if (string.IsNullOrWhiteSpace(clientId))
                        continue;

                    result[clientId] = new CopiersPreventiveClientSettings(
                        clientId,
                        ReadPreventiveClientCity(item, cityField),
                        ReadPreventiveClientFrequency(item, frequencyField));
                }
            }

            return result;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is InvalidOperationException or JsonException)
        {
            _logger.LogWarning(
                ex,
                "No fue posible consultar ciudad/frecuencia de los clientes para mantenimientos preventivos.");
            return new Dictionary<string, CopiersPreventiveClientSettings>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task<string> EnsurePreventiveClientFrequencyFieldAsync(
        RhEntityMetadata clientMetadata,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var attributes = await GetDashboardEntityAttributeNamesAsync(clientMetadata.LogicalName, user, ct);
        var existingField = ResolvePreventiveClientFrequencyField(attributes);
        if (!string.IsNullOrWhiteSpace(existingField))
            return existingField;

        await CallDataverseSendAsync(
            $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(clientMetadata.LogicalName)}')/Attributes",
            "POST",
            BuildPreventiveClientFrequencyAttributePayload(),
            user,
            ct);
        await PublishPreventiveClientEntityAsync(clientMetadata.LogicalName, user, ct);
        await WaitForPreventiveClientFrequencyFieldAsync(clientMetadata.LogicalName, user, ct);
        return CopiersClientPreventiveFrequencyField;
    }

    private async Task WaitForPreventiveClientFrequencyFieldAsync(
        string clientLogicalName,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            _dashboardEntityAttributeNamesCache.TryRemove(clientLogicalName, out _);
            var attributes = await GetDashboardEntityAttributeNamesAsync(clientLogicalName, user, ct);
            if (attributes.Contains(CopiersClientPreventiveFrequencyField))
                return;

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        throw new InvalidOperationException("Dataverse creo la columna de periodicidad, pero aun no la expone. Intenta nuevamente en unos segundos.");
    }

    private async Task PublishPreventiveClientEntityAsync(
        string clientLogicalName,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var publishXml =
            $"<importexportxml><entities><entity>{clientLogicalName}</entity></entities></importexportxml>";
        await CallDataverseSendAsync(
            "/api/data/v9.2/PublishXml",
            "POST",
            new Dictionary<string, object?> { ["ParameterXml"] = publishXml },
            user,
            ct);
    }

    private async Task<CopiersPreventiveScheduleRow?> FindPreventiveScheduleAsync(
        RhEntityMetadata metadata,
        string periodKey,
        string clientId,
        string clientName,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var filters = new List<string>
        {
            $"{CopiersPreventiveSchedulePeriodField} eq '{EscapeOdataLiteral(periodKey)}'"
        };

        if (!string.IsNullOrWhiteSpace(clientId))
        {
            var clientLookupProperty = BuildDashboardLookupValuePropertyName(CopiersPreventiveScheduleClientField);
            filters.Add($"({clientLookupProperty} eq {clientId} or {CopiersPreventiveScheduleClientIdTextField} eq '{EscapeOdataLiteral(clientId)}')");
        }
        else
        {
            filters.Add($"{CopiersPreventiveScheduleClientNameField} eq '{EscapeOdataLiteral(clientName)}'");
        }

        var ownerCondition = await BuildCurrentOwnerConditionAsync(ct);
        if (!string.IsNullOrWhiteSpace(ownerCondition))
            filters.Add(ownerCondition);

        var filter = string.Join(" and ", filters);
        var relativeUrl =
            $"/api/data/v9.2/{metadata.EntitySetName}?$select={BuildPreventiveScheduleSelectClause(metadata)}" +
            $"&$filter={Uri.EscapeDataString(filter)}" +
            $"&$orderby={CopiersPreventiveScheduleDateField} desc&$top=1";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        var first = items.FirstOrDefault();
        return first.ValueKind == JsonValueKind.Undefined
            ? null
            : ParsePreventiveScheduleRow(first, metadata.PrimaryIdField, metadata.PrimaryNameField);
    }

    private async Task<string> BuildCurrentOwnerConditionAsync(CancellationToken ct)
    {
        var currentUser = await GetCurrentUserAsync(ct);
        var ownerId = NormalizeOptionalGuid(currentUser?.SystemUserId);
        return string.IsNullOrWhiteSpace(ownerId)
            ? ""
            : $"{BuildDashboardLookupValuePropertyName(DashboardMaintenanceOwnerField)} eq {ownerId}";
    }

    private async Task<RhEntityMetadata> ResolveCopiersPreventiveScheduleMetadataAsync(
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        return await ResolveRhEntityMetadataAsync(
            CopiersPreventiveScheduleLogicalName,
            CopiersPreventiveScheduleSetName,
            CopiersPreventiveSchedulePrimaryIdField,
            CopiersPreventiveSchedulePrimaryNameField,
            user,
            ct);
    }

    private static string BuildPreventiveScheduleSelectClause(RhEntityMetadata metadata)
    {
        return string.Join(",", new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            BuildDashboardLookupValuePropertyName(CopiersPreventiveScheduleClientField),
            CopiersPreventiveScheduleClientIdTextField,
            CopiersPreventiveScheduleClientNameField,
            CopiersPreventiveSchedulePeriodField,
            CopiersPreventiveScheduleDateField,
            CopiersPreventiveScheduleDurationField,
            CopiersPreventiveScheduleEventIdField,
            CopiersPreventiveScheduleWebLinkField,
            BuildDashboardLookupValuePropertyName(DashboardMaintenanceOwnerField)
        }
        .Where(static field => !string.IsNullOrWhiteSpace(field))
        .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static CopiersPreventiveScheduleRow? ParsePreventiveScheduleRow(
        JsonElement item,
        string primaryIdField,
        string primaryNameField)
    {
        var clientId = NormalizeOptionalGuid(FirstNonEmpty(
            ReadString(item, BuildDashboardLookupValuePropertyName(CopiersPreventiveScheduleClientField)),
            ReadString(item, CopiersPreventiveScheduleClientIdTextField)));
        var clientName = FirstNonEmpty(
            ReadString(item, CopiersPreventiveScheduleClientNameField).Trim(),
            ReadLookupFormattedValue(item, BuildDashboardLookupValuePropertyName(CopiersPreventiveScheduleClientField)),
            "Sin cliente");
        var scheduledDate = ReadDateTimeOffset(item, CopiersPreventiveScheduleDateField);

        return new CopiersPreventiveScheduleRow
        {
            RecordId = FirstNonEmpty(ReadString(item, primaryIdField), ReadString(item, CopiersPreventiveSchedulePrimaryIdField)).Trim(),
            Title = FirstNonEmpty(ReadString(item, primaryNameField), ReadString(item, CopiersPreventiveSchedulePrimaryNameField)).Trim(),
            ClientId = clientId,
            ClientName = clientName,
            ClientKey = BuildDashboardGroupKey(clientId, clientName),
            PeriodValue = ReadString(item, CopiersPreventiveSchedulePeriodField).Trim(),
            ScheduledDateDisplay = scheduledDate.HasValue
                ? scheduledDate.Value.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
                : ReadString(item, CopiersPreventiveScheduleDateField).Trim(),
            EventId = ReadString(item, CopiersPreventiveScheduleEventIdField).Trim(),
            WebLink = ReadString(item, CopiersPreventiveScheduleWebLinkField).Trim()
        };
    }

    private static CopiersPreventiveClientState ResolvePreventiveClientState(
        int equipmentCount,
        int maintenanceRegistered,
        int countersRegistered)
    {
        if (equipmentCount > 0 && maintenanceRegistered >= equipmentCount && countersRegistered >= equipmentCount)
        {
            return new CopiersPreventiveClientState(
                "Realizado",
                "good",
                "Realizado",
                "success",
                true);
        }

        return new CopiersPreventiveClientState(
            "Programar mantenimiento",
            "pending",
            "Programar mantenimiento",
            "primary",
            false);
    }

    private async Task<RhEntityMetadata> ResolveCopiersCounterMetadataAsync(
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        return await ResolveRhEntityMetadataAsync(
            CopiersCountersLogicalName,
            CopiersLegacyCountersTableSetName,
            CopiersCountersPrimaryIdField,
            CopiersCountersPrimaryNameField,
            user,
            ct);
    }

    private static CopiersPreventiveMaintenanceEquipmentDto ToPreventiveMaintenanceEquipment(CopiersBillingEquipmentDto equipment)
    {
        return new CopiersPreventiveMaintenanceEquipmentDto
        {
            RecordId = equipment.RecordId,
            Serial = equipment.Serial,
            ClientId = equipment.ClientId,
            ClientName = equipment.ClientName,
            CategoryLabel = equipment.CategoryLabel,
            Reference = equipment.Reference,
            Area = equipment.Area,
            Site = equipment.Site,
            HasCurrentCounter = equipment.HasCurrentCounter,
            CounterDateValue = equipment.CounterDateValue,
            CounterDateDisplay = equipment.CounterDateDisplay,
            CounterCopies = equipment.CounterCopies,
            CounterScans = equipment.CounterScans,
            CounterStatusLabel = equipment.CounterStatusLabel,
            CounterStatusTone = equipment.CounterStatusTone
        };
    }

    private static CopiersPreventivePeriodDefinition ResolveCopiersPreventivePeriod(string? rawPeriod, DateOnly today)
    {
        var currentMonthStart = new DateOnly(today.Year, today.Month, 1);
        var normalized = NormalizeCopiersComparableValue(rawPeriod)
            .Replace("_", "-", StringComparison.Ordinal)
            .Replace(" ", "-", StringComparison.Ordinal);
        var start = normalized is "previous-month" or "previousmonth" or "mes-pasado" or "mes-anterior"
            ? currentMonthStart.AddMonths(-1)
            : currentMonthStart;
        var filter = start == currentMonthStart
            ? CopiersPreventivePeriodThisMonth
            : CopiersPreventivePeriodPreviousMonth;
        var label = ToTitleCase(start.ToString("MMMM yyyy", DashboardCulture));

        return new CopiersPreventivePeriodDefinition(
            filter,
            start.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            label,
            start,
            start.AddMonths(1));
    }

    private static CopiersPreventiveFrequency ResolvePreventiveMaintenanceFrequency(
        IReadOnlyList<CopiersBillingGroupDto> groups)
    {
        var text = string.Join(
            " ",
            groups
                .SelectMany(static group => group.Lines)
                .Select(static line => line.ProductName)
                .Concat(groups.Select(static group => group.ClientName)));
        var normalized = NormalizeCopiersComparableValue(text);
        var isBimonthly = normalized.Contains("bimensual", StringComparison.Ordinal)
            || normalized.Contains("bi mensual", StringComparison.Ordinal)
            || normalized.Contains("bimestral", StringComparison.Ordinal)
            || normalized.Contains("cada 2", StringComparison.Ordinal)
            || normalized.Contains("cada dos", StringComparison.Ordinal);

        return isBimonthly
            ? new CopiersPreventiveFrequency("bimonthly", "Bimensual", true)
            : new CopiersPreventiveFrequency("monthly", "Mensual", false);
    }

    private static CopiersPreventiveFrequency? ResolvePreventiveMaintenanceFrequencyValue(string? value)
    {
        var normalized = NormalizeCopiersComparableValue(value)
            .Replace("_", "-", StringComparison.Ordinal)
            .Replace(" ", "-", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        if (normalized is "bimonthly" or "bi-monthly" or "bimensual" or "bi-mensual" or "bimestral"
            || normalized.Contains("bimensual", StringComparison.Ordinal)
            || normalized.Contains("bimestral", StringComparison.Ordinal)
            || normalized.Contains("cada-2", StringComparison.Ordinal)
            || normalized.Contains("cada-dos", StringComparison.Ordinal))
        {
            return new CopiersPreventiveFrequency("bimonthly", "Bimensual", true);
        }

        if (normalized is "monthly" or "mensual"
            || normalized.Contains("mensual", StringComparison.Ordinal))
        {
            return new CopiersPreventiveFrequency("monthly", "Mensual", false);
        }

        return null;
    }

    private static string ResolvePreventiveClientCityField(IReadOnlySet<string> attributes)
    {
        if (attributes.Count == 0)
            return CopiersClientCityField;

        if (attributes.Contains(CopiersClientCityField))
            return CopiersClientCityField;

        return attributes.FirstOrDefault(static field =>
            string.Equals(field, "ciudad", StringComparison.OrdinalIgnoreCase)
            || field.EndsWith("_ciudad", StringComparison.OrdinalIgnoreCase)
            || field.EndsWith("ciudad", StringComparison.OrdinalIgnoreCase)) ?? "";
    }

    private static string ReadPreventiveClientCity(JsonElement item, string cityField)
    {
        if (string.IsNullOrWhiteSpace(cityField))
            return "";

        return FirstNonEmpty(
            ReadString(item, $"{cityField}{FormattedValueAnnotationSuffix}").Trim(),
            ReadString(item, cityField).Trim());
    }

    private static string ResolvePreventiveClientFrequencyField(IReadOnlySet<string> attributes)
    {
        if (attributes.Count == 0)
            return "";

        var candidates = new[]
        {
            CopiersClientPreventiveFrequencyField,
            "cr07a_periodicidadmantenimientopreventivo",
            "cr07a_periodicidadmantenimientocopiers",
            "cr07a_frecuenciamantenimientocopiers",
            "cr07a_periodicidadcopiers"
        };
        var candidate = candidates.FirstOrDefault(attributes.Contains);
        if (!string.IsNullOrWhiteSpace(candidate))
            return candidate;

        return attributes.FirstOrDefault(static field =>
        {
            var normalized = NormalizeCopiersComparableValue(field);
            return (normalized.Contains("frecuencia", StringComparison.Ordinal)
                    || normalized.Contains("periodicidad", StringComparison.Ordinal))
                && (normalized.Contains("mantenimiento", StringComparison.Ordinal)
                    || normalized.Contains("preventivo", StringComparison.Ordinal)
                    || normalized.Contains("copiers", StringComparison.Ordinal));
        }) ?? "";
    }

    private static string ReadPreventiveClientFrequency(JsonElement item, string frequencyField)
    {
        if (string.IsNullOrWhiteSpace(frequencyField))
            return "";

        return FirstNonEmpty(
            ReadString(item, $"{frequencyField}{FormattedValueAnnotationSuffix}").Trim(),
            ReadString(item, frequencyField).Trim());
    }

    private static object BuildPreventiveClientFrequencyAttributePayload()
    {
        return new Dictionary<string, object?>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.StringAttributeMetadata",
            ["AttributeType"] = "String",
            ["AttributeTypeName"] = CreateHardwareValuePayload("StringType"),
            ["Description"] = CreateHardwareLabelPayload("Periodicidad del mantenimiento preventivo Copiers del cliente."),
            ["DisplayName"] = CreateHardwareLabelPayload("Frecuencia mantenimiento preventivo"),
            ["RequiredLevel"] = CreateRequiredLevelNonePayload(),
            ["SchemaName"] = CopiersClientPreventiveFrequencySchemaName,
            ["FormatName"] = CreateHardwareValuePayload("Text"),
            ["MaxLength"] = 40
        };
    }

    private sealed record CopiersPreventivePeriodDefinition(
        string Filter,
        string Value,
        string Label,
        DateOnly StartInclusive,
        DateOnly EndExclusive);

    private sealed record CopiersPreventiveFrequency(
        string Key,
        string Label,
        bool IsBimonthly);

    private sealed record CopiersPreventiveClientSettings(
        string ClientId,
        string City,
        string MaintenanceFrequency);

    private sealed record CopiersPreventiveClientState(
        string StatusLabel,
        string StatusTone,
        string ButtonLabel,
        string ButtonTone,
        bool ButtonDisabled);

    private sealed class CopiersPreventiveScheduleRow
    {
        public string RecordId { get; init; } = "";
        public string Title { get; init; } = "";
        public string ClientId { get; init; } = "";
        public string ClientName { get; init; } = "";
        public string ClientKey { get; init; } = "";
        public string PeriodValue { get; init; } = "";
        public string ScheduledDateDisplay { get; init; } = "";
        public string EventId { get; init; } = "";
        public string WebLink { get; init; } = "";
    }

}
