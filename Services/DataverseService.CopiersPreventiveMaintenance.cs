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

    public async Task<CopiersPreventiveMaintenanceBoardDto> GetCopiersPreventiveMaintenanceBoardAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var dashboard = await GetCopiersDashboardAsync(ct);
        var today = GetBogotaToday();
        var periodStart = new DateOnly(today.Year, today.Month, 1);
        var periodEnd = periodStart.AddMonths(1);
        var periodKey = periodStart.ToString("yyyy-MM", CultureInfo.InvariantCulture);
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
        var latestEquipmentById = await BuildPreventiveLatestCounterEquipmentAsync(
            dashboardEquipment,
            httpContext.User,
            ct);
        var clients = dashboard.Groups
            .GroupBy(group => BuildDashboardGroupKey(group.ClientId, group.ClientName), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var groups = group.ToList();
                var first = groups[0];
                var equipment = groups
                    .SelectMany(static item => item.Equipment)
                    .Where(static item => !string.IsNullOrWhiteSpace(item.RecordId))
                    .GroupBy(static item => item.RecordId, StringComparer.OrdinalIgnoreCase)
                    .Select(static equipmentGroup => equipmentGroup.First())
                    .Select(item =>
                    {
                        var dto = latestEquipmentById.TryGetValue(item.RecordId, out var latest)
                            ? latest
                            : ToPreventiveMaintenanceEquipment(item);
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
                    countersRegistered,
                    schedule is not null);

                return new CopiersPreventiveMaintenanceClientDto
                {
                    ClientKey = group.Key,
                    ClientId = NormalizeOptionalGuid(first.ClientId),
                    ClientName = FirstNonEmpty(first.ClientName, "Sin cliente"),
                    EquipmentCount = equipment.Count,
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
            CounterPeriodLabel = dashboard.CounterPeriodLabel,
            PeriodValue = periodKey,
            RecordsCount = clients.Count,
            Clients = clients
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
        int countersRegistered,
        bool scheduled)
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

        if (maintenanceRegistered > 0 || countersRegistered > 0)
        {
            return new CopiersPreventiveClientState(
                "En proceso",
                "warning",
                "En proceso",
                "warning",
                true);
        }

        if (scheduled)
        {
            return new CopiersPreventiveClientState(
                "Mantenimiento programado",
                "good",
                "Mantenimiento programado",
                "success",
                true);
        }

        return new CopiersPreventiveClientState(
            "Pendiente",
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

    private async Task<IReadOnlyDictionary<string, CopiersPreventiveMaintenanceEquipmentDto>> BuildPreventiveLatestCounterEquipmentAsync(
        IEnumerable<CopiersBillingEquipmentDto> equipmentRows,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var rows = equipmentRows
            .Where(static item => !string.IsNullOrWhiteSpace(item.RecordId))
            .GroupBy(static item => item.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();
        if (rows.Count == 0)
            return new Dictionary<string, CopiersPreventiveMaintenanceEquipmentDto>(StringComparer.OrdinalIgnoreCase);

        var start = new DateOnly(2000, 1, 1);
        var end = GetBogotaToday().AddDays(1);
        var semaphore = new SemaphoreSlim(8);
        var tasks = rows.Select(async row =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var latest = await GetCopiersLastCounterReadingAsync(
                    row.RecordId,
                    row.Serial,
                    start,
                    end,
                    user,
                    ct);
                var dto = ToPreventiveMaintenanceEquipment(row);
                var hasCounter = latest.Date.HasValue;
                var counterDateDisplay = FormatCopiersCounterDateDisplay(latest.Date);
                dto.HasCurrentCounter = hasCounter;
                dto.CounterDateValue = FormatCopiersCounterDateValue(latest.Date);
                dto.CounterDateDisplay = counterDateDisplay;
                dto.CounterCopies = latest.Copies;
                dto.CounterScans = latest.Scans;
                dto.CounterStatusLabel = hasCounter
                    ? $"Ultimo contador {counterDateDisplay}"
                    : "Sin contador registrado";
                dto.CounterStatusTone = hasCounter ? "ok" : "pending";
                return dto;
            }
            finally
            {
                semaphore.Release();
            }
        });

        return (await Task.WhenAll(tasks))
            .ToDictionary(static item => item.RecordId, static item => item, StringComparer.OrdinalIgnoreCase);
    }
}
