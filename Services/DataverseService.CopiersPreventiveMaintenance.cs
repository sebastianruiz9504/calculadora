using System.Globalization;
using System.Security.Claims;
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

    public async Task<CopiersPreventiveMaintenanceBoardDto> GetCopiersPreventiveMaintenanceBoardAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var dashboard = await GetCopiersDashboardAsync(ct);
        var latestEquipmentById = await BuildPreventiveLatestCounterEquipmentAsync(
            dashboard.Groups.SelectMany(static group => group.Equipment),
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
                    .Select(item => latestEquipmentById.TryGetValue(item.RecordId, out var latest) ? latest : ToPreventiveMaintenanceEquipment(item))
                    .OrderBy(static item => item.Serial, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var countersRegistered = equipment.Count(static item => item.HasCurrentCounter);
                var pendingCounters = equipment.Count - countersRegistered;

                return new CopiersPreventiveMaintenanceClientDto
                {
                    ClientKey = group.Key,
                    ClientId = NormalizeOptionalGuid(first.ClientId),
                    ClientName = FirstNonEmpty(first.ClientName, "Sin cliente"),
                    EquipmentCount = equipment.Count,
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
            RecordsCount = clients.Count,
            Clients = clients
        };
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
