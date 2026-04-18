using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.RH;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string DashboardEquipmentTableLogicalName = "cr07a_equipo";
    private const string DashboardEquipmentTableSetName = "cr07a_equipos";
    private const string DashboardEquipmentIdField = "cr07a_equipoid";
    private const string DashboardEquipmentPrimaryNameField = "cr07a_nombredelequipo";
    private const string DashboardEquipmentSerialField = "cr07a_nombredelequipo";
    private const string DashboardEquipmentClientField = "cr07a_cliente";
    private const string DashboardEquipmentCategoryField = "cr07a_categoriadeequipo";
    private const string DashboardEquipmentReferenceField = "cr07a_referencia";
    private const string DashboardEquipmentObservationsField = "cr07a_observaciones";

    private const string DashboardMaintenanceTableLogicalName = "cr07a_mantenimiento";
    private const string DashboardMaintenanceTableSetName = "cr07a_mantenimientos";
    private const string DashboardMaintenanceIdField = "cr07a_mantenimientoid";
    private const string DashboardMaintenancePrimaryNameField = "cr07a_mantenimiento1";
    private const string DashboardMaintenanceTitleField = "cr07a_mantenimiento1";
    private const string DashboardMaintenanceEquipmentField = "cr07a_iddeequipo";
    private const string DashboardMaintenanceDateField = "cr07a_fechademantenimiento";
    private const string DashboardMaintenanceDescriptionField = "cr07a_descripciondelmantenimiento";
    private const string DashboardMaintenanceClientField = "cr07a_cliente";
    private const string DashboardMaintenanceAttachmentField = "cr07a_actadeentregadeservicio";
    private const string DashboardMaintenanceExternalIdField = "cr07a_id";
    private const string DashboardMaintenanceTypeField = "cr07a_tipodemantenimiento";
    private const string DashboardMaintenanceOwnerField = "ownerid";

    private static readonly IReadOnlyDictionary<int, string> DashboardEquipmentCategoryLabels =
        new Dictionary<int, string>
        {
            [645250000] = "Impresora",
            [645250001] = "Multifuncional",
            [645250002] = "Escáner",
            [645250003] = "Equipo de computo",
            [645250004] = "Otros"
        };

    private static readonly IReadOnlyDictionary<int, string> DashboardMaintenanceTypeLabels =
        new Dictionary<int, string>
        {
            [645250000] = "Correctivo",
            [645250001] = "Preventivo"
        };

    public async Task<CopiersEquipmentDashboardDto> GetCopiersEquipmentDashboardAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var today = GetBogotaToday();
        var equipmentMetadata = await ResolveRhEntityMetadataAsync(
            DashboardEquipmentTableLogicalName,
            DashboardEquipmentTableSetName,
            DashboardEquipmentIdField,
            DashboardEquipmentPrimaryNameField,
            httpContext.User,
            ct);
        var maintenanceMetadata = await ResolveRhEntityMetadataAsync(
            DashboardMaintenanceTableLogicalName,
            DashboardMaintenanceTableSetName,
            DashboardMaintenanceIdField,
            DashboardMaintenancePrimaryNameField,
            httpContext.User,
            ct);

        var equipmentRows = await GetEquipmentRecordsAsync(equipmentMetadata, httpContext.User, ct);
        var maintenanceRows = await GetMaintenanceRecordsAsync(maintenanceMetadata, httpContext.User, ct);

        return new CopiersEquipmentDashboardDto
        {
            AsOfDateLabel = today.ToString("dd MMM yyyy", DashboardCulture),
            FocusLabel = "Asignacion de equipos, stock disponible y soportes ejecutados",
            HasData = equipmentRows.Count > 0,
            RecordsCount = equipmentRows.Count,
            EmptyStateTitle = "No encontramos equipos registrados.",
            EmptyStateMessage = "Cuando Dataverse tenga filas en cr07a_equipo las veras aqui.",
            Kpis = BuildEquipmentKpis(equipmentRows, maintenanceRows),
            ClientSummaries = BuildEquipmentClientSummaries(equipmentRows),
            EquipmentRows = BuildEquipmentRows(equipmentRows, maintenanceRows),
            StockRows = BuildEquipmentRows(
                equipmentRows.Where(static row => row.InStock).ToList(),
                maintenanceRows),
            MaintenanceChart = BuildMaintenanceChart(maintenanceRows, today)
        };
    }

    public async Task<CopiersEquipmentDetailDto> GetCopiersEquipmentDetailAsync(string equipmentId, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var equipmentMetadata = await ResolveRhEntityMetadataAsync(
            DashboardEquipmentTableLogicalName,
            DashboardEquipmentTableSetName,
            DashboardEquipmentIdField,
            DashboardEquipmentPrimaryNameField,
            httpContext.User,
            ct);
        var maintenanceMetadata = await ResolveRhEntityMetadataAsync(
            DashboardMaintenanceTableLogicalName,
            DashboardMaintenanceTableSetName,
            DashboardMaintenanceIdField,
            DashboardMaintenancePrimaryNameField,
            httpContext.User,
            ct);

        var normalizedEquipmentId = NormalizeGuid(equipmentId, nameof(equipmentId));
        var equipment = await GetEquipmentRecordByIdAsync(equipmentMetadata, normalizedEquipmentId, httpContext.User, ct)
            ?? throw new InvalidOperationException("No encontramos el equipo seleccionado.");
        var maintenanceRows = await GetMaintenanceRecordsAsync(maintenanceMetadata, httpContext.User, ct, normalizedEquipmentId);

        return new CopiersEquipmentDetailDto
        {
            Equipment = BuildEquipmentRows(new[] { equipment }, maintenanceRows).First(),
            MaintenanceRows = BuildMaintenanceRows(maintenanceRows)
        };
    }

    public async Task<CopiersEquipmentAssignmentResultDto> SaveCopiersEquipmentAssignmentAsync(
        CopiersEquipmentAssignmentRequestDto request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveRhEntityMetadataAsync(
            DashboardEquipmentTableLogicalName,
            DashboardEquipmentTableSetName,
            DashboardEquipmentIdField,
            DashboardEquipmentPrimaryNameField,
            httpContext.User,
            ct);

        var normalizedRecordId = NormalizeGuid(request.RecordId, nameof(request.RecordId));
        var current = await GetEquipmentRecordByIdAsync(metadata, normalizedRecordId, httpContext.User, ct)
            ?? throw new InvalidOperationException("No encontramos el equipo que quieres reasignar.");

        var payload = new Dictionary<string, object?>();
        var navigationProperty = await ResolveRhLookupNavigationPropertyAsync(
            DashboardEquipmentTableLogicalName,
            DashboardEquipmentClientField,
            DashboardEquipmentClientField,
            httpContext.User,
            ct);

        if (request.MoveToStock)
        {
            payload[$"{navigationProperty}@odata.bind"] = null;
        }
        else
        {
            var clientName = (request.ClientName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(clientName))
                throw new InvalidOperationException("Debes indicar el cliente al que quieres reasignar el equipo.");

            var requestedClientId = NormalizeOptionalGuid(request.ClientId);
            var resolvedClientId = !string.IsNullOrWhiteSpace(requestedClientId)
                ? requestedClientId
                : string.Equals(
                    NormalizeCopiersComparableValue(clientName),
                    NormalizeCopiersComparableValue(current.ClientName),
                    StringComparison.Ordinal)
                    ? NormalizeOptionalGuid(current.ClientId)
                    : await ResolveCopiersClientIdAsync(clientName, ct);

            if (string.IsNullOrWhiteSpace(resolvedClientId))
                throw new InvalidOperationException("No encontramos un cliente valido para el valor digitado. Selecciona una opcion sugerida.");

            payload[$"{navigationProperty}@odata.bind"] = $"/{ClientsEntitySetName}({resolvedClientId})";
        }

        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}({normalizedRecordId})";
        await CallDataverseSendAsync(relativeUrl, "PATCH", payload, httpContext.User, ct);

        var updated = await GetEquipmentRecordByIdAsync(metadata, normalizedRecordId, httpContext.User, ct)
            ?? throw new InvalidOperationException("El equipo se actualizo pero no pudimos refrescar su informacion.");
        var maintenanceMetadata = await ResolveRhEntityMetadataAsync(
            DashboardMaintenanceTableLogicalName,
            DashboardMaintenanceTableSetName,
            DashboardMaintenanceIdField,
            DashboardMaintenancePrimaryNameField,
            httpContext.User,
            ct);
        var maintenanceRows = await GetMaintenanceRecordsAsync(maintenanceMetadata, httpContext.User, ct, normalizedRecordId);

        return new CopiersEquipmentAssignmentResultDto
        {
            RecordId = normalizedRecordId,
            Message = request.MoveToStock
                ? "El equipo se movio correctamente a stock."
                : "El equipo se reasigno correctamente al cliente seleccionado.",
            Equipment = BuildEquipmentRows(new[] { updated }, maintenanceRows).First()
        };
    }

    public async Task<RhFileDownloadResult?> DownloadCopiersMaintenanceAttachmentAsync(
        string maintenanceId,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveRhEntityMetadataAsync(
            DashboardMaintenanceTableLogicalName,
            DashboardMaintenanceTableSetName,
            DashboardMaintenanceIdField,
            DashboardMaintenancePrimaryNameField,
            httpContext.User,
            ct);

        var normalizedMaintenanceId = NormalizeGuid(maintenanceId, nameof(maintenanceId));
        var relativeUrl =
            $"/api/data/v9.2/{metadata.EntitySetName}({normalizedMaintenanceId})/{DashboardMaintenanceAttachmentField}/$value";

        using var response = await CallRhDataverseResponseAsync(relativeUrl, "GET", httpContext.User, ct);
        if (response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.NotFound)
            return null;

        var content = await response.Content.ReadAsByteArrayAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var bodyText = content.Length == 0 ? "" : System.Text.Encoding.UTF8.GetString(content);
            throw new InvalidOperationException(
                $"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {bodyText}");
        }

        return new RhFileDownloadResult
        {
            FileName = FirstNonEmpty(
                ReadHeaderValue(response, "x-ms-file-name"),
                ReadHeaderValue(response, "filename"),
                $"acta-servicio-{normalizedMaintenanceId}.bin"),
            ContentType =
                response.Content.Headers.ContentType?.MediaType
                ?? ReadHeaderValue(response, "mimetype")
                ?? "application/octet-stream",
            Content = content
        };
    }

    private async Task<List<CopiersEquipmentRecordRow>> GetEquipmentRecordsAsync(
        RhEntityMetadata metadata,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var relativeUrl =
            $"/api/data/v9.2/{metadata.EntitySetName}?$select={BuildEquipmentSelectClause(metadata)}" +
            $"&$orderby={DashboardEquipmentSerialField} asc";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        return items
            .Select(item => ParseEquipmentRecord(item, metadata.PrimaryIdField, metadata.PrimaryNameField))
            .Where(item => item is not null)
            .Cast<CopiersEquipmentRecordRow>()
            .ToList();
    }

    private async Task<CopiersEquipmentRecordRow?> GetEquipmentRecordByIdAsync(
        RhEntityMetadata metadata,
        string equipmentId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var relativeUrl =
            $"/api/data/v9.2/{metadata.EntitySetName}({NormalizeGuid(equipmentId, nameof(equipmentId))})" +
            $"?$select={BuildEquipmentSelectClause(metadata)}";
        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        using var doc = JsonDocument.Parse(json);
        return ParseEquipmentRecord(doc.RootElement, metadata.PrimaryIdField, metadata.PrimaryNameField);
    }

    private string BuildEquipmentSelectClause(RhEntityMetadata metadata)
    {
        return string.Join(",", new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            DashboardEquipmentSerialField,
            BuildDashboardLookupValuePropertyName(DashboardEquipmentClientField),
            DashboardEquipmentCategoryField,
            DashboardEquipmentReferenceField,
            DashboardEquipmentObservationsField
        }
        .Where(static field => !string.IsNullOrWhiteSpace(field))
        .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private CopiersEquipmentRecordRow? ParseEquipmentRecord(JsonElement item, string primaryIdField, string primaryNameField)
    {
        var serial = FirstNonEmpty(
            ReadString(item, DashboardEquipmentSerialField).Trim(),
            ReadString(item, primaryNameField).Trim(),
            "Equipo sin serial");
        var clientName = ReadCopiersFieldDisplayValue(
            item,
            DashboardEquipmentClientField,
            "cliente",
            "Stock");
        var recordId = FirstNonEmpty(
            ReadString(item, primaryIdField),
            ReadString(item, DashboardEquipmentIdField),
            serial);

        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var categoryValue = item.TryGetProperty(DashboardEquipmentCategoryField, out _)
            ? ReadIntFlexible(item, DashboardEquipmentCategoryField)
            : 0;
        var normalizedClientId = ReadCopiersLookupId(item, DashboardEquipmentClientField, "cliente");
        var inStock = string.IsNullOrWhiteSpace(normalizedClientId);

        return new CopiersEquipmentRecordRow
        {
            RecordId = recordId.Trim(),
            Serial = serial,
            ClientId = normalizedClientId,
            ClientName = inStock ? "Stock" : clientName,
            CategoryValue = categoryValue > 0 ? categoryValue : null,
            CategoryLabel = ResolveDashboardOptionLabel(
                item,
                DashboardEquipmentCategoryField,
                categoryValue,
                DashboardEquipmentCategoryLabels,
                "Sin categoria"),
            Reference = ReadString(item, DashboardEquipmentReferenceField).Trim(),
            Observations = ReadString(item, DashboardEquipmentObservationsField).Trim(),
            InStock = inStock
        };
    }

    private async Task<List<CopiersMaintenanceRecordRow>> GetMaintenanceRecordsAsync(
        RhEntityMetadata metadata,
        ClaimsPrincipal user,
        CancellationToken ct,
        string? equipmentId = null)
    {
        var equipmentLookupFieldCandidates = await ResolveCopiersMaintenanceEquipmentLookupFieldCandidatesAsync(user, ct);
        InvalidOperationException? lastLookupException = null;

        foreach (var equipmentLookupField in equipmentLookupFieldCandidates)
        {
            try
            {
                return await GetMaintenanceRecordsCoreAsync(metadata, user, ct, equipmentId, equipmentLookupField);
            }
            catch (InvalidOperationException ex) when (ShouldRetryCopiersMaintenanceLookupQuery(ex, equipmentLookupField))
            {
                lastLookupException = ex;
                _logger.LogWarning(
                    ex,
                    "Fallo la consulta de mantenimientos usando el lookup {LookupField}. Se intentara otra variante del campo.",
                    equipmentLookupField);
            }
        }

        if (lastLookupException is not null)
            throw lastLookupException;

        return await GetMaintenanceRecordsCoreAsync(
            metadata,
            user,
            ct,
            equipmentId,
            DashboardMaintenanceEquipmentField);
    }

    private async Task<List<CopiersMaintenanceRecordRow>> GetMaintenanceRecordsCoreAsync(
        RhEntityMetadata metadata,
        ClaimsPrincipal user,
        CancellationToken ct,
        string? equipmentId,
        string equipmentLookupField)
    {
        var relativeUrl =
            $"/api/data/v9.2/{metadata.EntitySetName}?$select={BuildMaintenanceSelectClause(metadata, equipmentLookupField)}" +
            $"{BuildMaintenanceFilterQuery(equipmentId, equipmentLookupField)}" +
            $"&$orderby={DashboardMaintenanceDateField} desc";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        return items
            .Select(item => ParseMaintenanceRecord(item, metadata.PrimaryIdField, metadata.PrimaryNameField, equipmentLookupField))
            .Where(item => item is not null)
            .Cast<CopiersMaintenanceRecordRow>()
            .ToList();
    }

    private string BuildMaintenanceSelectClause(RhEntityMetadata metadata, string equipmentLookupField)
    {
        return string.Join(",", new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            DashboardMaintenanceTitleField,
            BuildDashboardLookupValuePropertyName(equipmentLookupField),
            DashboardMaintenanceDateField,
            DashboardMaintenanceDescriptionField,
            DashboardMaintenanceClientField,
            BuildDashboardLookupValuePropertyName(DashboardMaintenanceClientField),
            DashboardMaintenanceAttachmentField,
            DashboardMaintenanceExternalIdField,
            DashboardMaintenanceTypeField,
            DashboardMaintenanceOwnerField,
            BuildDashboardLookupValuePropertyName(DashboardMaintenanceOwnerField)
        }
        .Where(static field => !string.IsNullOrWhiteSpace(field))
        .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private string BuildMaintenanceFilterQuery(string? equipmentId, string equipmentLookupField)
    {
        var normalizedEquipmentId = NormalizeOptionalGuid(equipmentId);
        if (string.IsNullOrWhiteSpace(normalizedEquipmentId))
            return "";

        var lookupValueProperty = BuildDashboardLookupValuePropertyName(equipmentLookupField);
        var filter = $"{lookupValueProperty} eq {normalizedEquipmentId}";
        return $"&$filter={Uri.EscapeDataString(filter)}";
    }

    private CopiersMaintenanceRecordRow? ParseMaintenanceRecord(
        JsonElement item,
        string primaryIdField,
        string primaryNameField,
        string equipmentLookupField)
    {
        var title = FirstNonEmpty(
            ReadString(item, DashboardMaintenanceTitleField).Trim(),
            ReadString(item, primaryNameField).Trim(),
            "Mantenimiento");
        var recordId = FirstNonEmpty(
            ReadString(item, primaryIdField),
            ReadString(item, DashboardMaintenanceIdField),
            title);
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var date = ReadDateOnly(item, DashboardMaintenanceDateField);
        var rawDate = ReadString(item, DashboardMaintenanceDateField);
        var typeValue = item.TryGetProperty(DashboardMaintenanceTypeField, out _)
            ? ReadIntFlexible(item, DashboardMaintenanceTypeField)
            : 0;
        var attachmentToken = ReadString(item, DashboardMaintenanceAttachmentField).Trim();
        var ownerLookupProperty = DetectLookupValueProperty(
            item,
            new[]
            {
                BuildDashboardLookupValuePropertyName(DashboardMaintenanceOwnerField)
            },
            "owner");

        return new CopiersMaintenanceRecordRow
        {
            RecordId = recordId.Trim(),
            Title = title,
            InternalId = ReadString(item, DashboardMaintenanceExternalIdField).Trim(),
            EquipmentId = ReadCopiersLookupId(item, equipmentLookupField, "equipo"),
            EquipmentSerial = ReadCopiersFieldDisplayValue(
                item,
                equipmentLookupField,
                "equipo",
                "Equipo sin nombre"),
            MaintenanceDate = date,
            DateValue = date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? rawDate,
            DateDisplay = date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? rawDate,
            Description = ReadString(item, DashboardMaintenanceDescriptionField).Trim(),
            ClientId = ReadCopiersLookupId(item, DashboardMaintenanceClientField, "cliente"),
            ClientName = ReadCopiersFieldDisplayValue(
                item,
                DashboardMaintenanceClientField,
                "cliente",
                "Sin cliente"),
            HasAttachment = !string.IsNullOrWhiteSpace(attachmentToken),
            AttachmentFileName = FirstNonEmpty(
                ReadString(item, $"{DashboardMaintenanceAttachmentField}_name").Trim(),
                ReadString(item, $"{DashboardMaintenanceAttachmentField}{FormattedValueAnnotationSuffix}").Trim(),
                !string.IsNullOrWhiteSpace(attachmentToken) ? "Acta de entrega de servicio" : ""),
            MaintenanceTypeValue = typeValue > 0 ? typeValue : null,
            MaintenanceTypeLabel = ResolveDashboardOptionLabel(
                item,
                DashboardMaintenanceTypeField,
                typeValue,
                DashboardMaintenanceTypeLabels,
                "Sin tipo"),
            TechnicianId = ReadString(item, ownerLookupProperty).Trim(),
            TechnicianName = FirstNonEmpty(
                ReadLookupFormattedValue(item, ownerLookupProperty),
                ReadString(item, $"{DashboardMaintenanceOwnerField}{FormattedValueAnnotationSuffix}").Trim(),
                "Sin tecnico")
        };
    }

    private async Task<IReadOnlyList<string>> ResolveCopiersMaintenanceEquipmentLookupFieldCandidatesAsync(
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var navigationProperty = await ResolveRhLookupNavigationPropertyAsync(
            DashboardMaintenanceTableLogicalName,
            DashboardMaintenanceEquipmentField,
            DashboardMaintenanceEquipmentField,
            user,
            ct);

        return BuildLookupLogicalNameCandidates(
                DashboardMaintenanceEquipmentField,
                navigationProperty)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate => candidate!.Trim())
            .ToList();
    }

    private bool ShouldRetryCopiersMaintenanceLookupQuery(InvalidOperationException exception, string equipmentLookupField)
    {
        var lookupValueProperty = BuildDashboardLookupValuePropertyName(equipmentLookupField);
        return exception.Message.Contains(equipmentLookupField, StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains(lookupValueProperty, StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("Could not find a property", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<PortfolioKpiDto> BuildEquipmentKpis(
        IReadOnlyList<CopiersEquipmentRecordRow> equipmentRows,
        IReadOnlyList<CopiersMaintenanceRecordRow> maintenanceRows)
    {
        var totalEquipment = equipmentRows.Count;
        var assignedEquipment = equipmentRows.Count(row => !row.InStock);
        var stockEquipment = equipmentRows.Count(row => row.InStock);
        var uniqueClients = equipmentRows
            .Where(row => !row.InStock)
            .Select(row => BuildDashboardGroupKey(row.ClientId, row.ClientName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var totalMaintenance = maintenanceRows.Count;

        return new[]
        {
            new PortfolioKpiDto
            {
                Key = "equipment-total",
                Label = "Equipos",
                Hint = "Total de equipos cargados en cr07a_equipo.",
                Value = totalEquipment,
                ValueFormat = "number",
                SecondaryLabel = "Con cliente",
                SecondaryValue = assignedEquipment.ToString("N0", DashboardCulture)
            },
            new PortfolioKpiDto
            {
                Key = "equipment-stock",
                Label = "Stock",
                Hint = "Equipos sin cliente asignado listos para reasignarse.",
                Value = stockEquipment,
                ValueFormat = "number",
                SecondaryLabel = "Clientes activos",
                SecondaryValue = uniqueClients.ToString("N0", DashboardCulture)
            },
            new PortfolioKpiDto
            {
                Key = "equipment-maintenance",
                Label = "Mantenimientos",
                Hint = "Soportes registrados en cr07a_mantenimiento.",
                Value = totalMaintenance,
                ValueFormat = "number",
                SecondaryLabel = "Promedio por equipo",
                SecondaryValue = totalEquipment == 0
                    ? "0"
                    : (totalMaintenance / (decimal)totalEquipment).ToString("N2", DashboardCulture)
            }
        };
    }

    private IReadOnlyList<CopiersEquipmentClientSummaryDto> BuildEquipmentClientSummaries(
        IReadOnlyList<CopiersEquipmentRecordRow> equipmentRows)
    {
        return equipmentRows
            .Where(row => !row.InStock)
            .GroupBy(row => BuildDashboardGroupKey(row.ClientId, row.ClientName), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var items = group.ToList();
                var first = items[0];
                var categoryBreakdown = items
                    .GroupBy(item => item.CategoryLabel, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(item => item.Count())
                    .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(item => $"{item.Key}: {item.Count().ToString("N0", DashboardCulture)}")
                    .ToList();

                return new CopiersEquipmentClientSummaryDto
                {
                    ClientId = first.ClientId,
                    ClientName = first.ClientName,
                    EquipmentCount = items.Count,
                    CategoryBreakdown = string.Join(" · ", categoryBreakdown)
                };
            })
            .OrderByDescending(item => item.EquipmentCount)
            .ThenBy(item => item.ClientName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<CopiersEquipmentRowDto> BuildEquipmentRows(
        IReadOnlyList<CopiersEquipmentRecordRow> equipmentRows,
        IReadOnlyList<CopiersMaintenanceRecordRow> maintenanceRows)
    {
        var maintenanceByEquipment = maintenanceRows
            .Where(row => !string.IsNullOrWhiteSpace(row.EquipmentId))
            .GroupBy(row => row.EquipmentId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => item.MaintenanceDate ?? DateOnly.MinValue)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

        return equipmentRows
            .Select(row =>
            {
                maintenanceByEquipment.TryGetValue(row.RecordId, out var equipmentMaintenance);
                var latestMaintenance = equipmentMaintenance?.FirstOrDefault();

                return new CopiersEquipmentRowDto
                {
                    RecordId = row.RecordId,
                    Serial = row.Serial,
                    ClientId = row.ClientId,
                    ClientName = row.ClientName,
                    CategoryValue = row.CategoryValue,
                    CategoryLabel = row.CategoryLabel,
                    Reference = row.Reference,
                    Observations = row.Observations,
                    InStock = row.InStock,
                    MaintenanceCount = equipmentMaintenance?.Count ?? 0,
                    LastMaintenanceDateDisplay = latestMaintenance?.DateDisplay ?? "Sin mantenimientos"
                };
            })
            .OrderBy(item => item.InStock)
            .ThenBy(item => item.ClientName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Serial, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<CopiersMaintenanceRowDto> BuildMaintenanceRows(
        IReadOnlyList<CopiersMaintenanceRecordRow> maintenanceRows)
    {
        return maintenanceRows
            .OrderByDescending(item => item.MaintenanceDate ?? DateOnly.MinValue)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .Select(item => new CopiersMaintenanceRowDto
            {
                RecordId = item.RecordId,
                Title = item.Title,
                InternalId = item.InternalId,
                EquipmentId = item.EquipmentId,
                EquipmentSerial = item.EquipmentSerial,
                DateValue = item.DateValue,
                DateDisplay = item.DateDisplay,
                Description = item.Description,
                ClientId = item.ClientId,
                ClientName = item.ClientName,
                HasAttachment = item.HasAttachment,
                AttachmentFileName = item.AttachmentFileName,
                MaintenanceTypeValue = item.MaintenanceTypeValue,
                MaintenanceTypeLabel = item.MaintenanceTypeLabel,
                TechnicianId = item.TechnicianId,
                TechnicianName = item.TechnicianName
            })
            .ToList();
    }

    private CopiersMaintenanceChartDto BuildMaintenanceChart(
        IReadOnlyList<CopiersMaintenanceRecordRow> maintenanceRows,
        DateOnly today)
    {
        var anchorMonth = maintenanceRows
            .Where(row => row.MaintenanceDate.HasValue)
            .Select(row => row.MaintenanceDate!.Value)
            .DefaultIfEmpty(today)
            .Max();
        var monthStart = new DateOnly(anchorMonth.Year, anchorMonth.Month, 1);
        var months = Enumerable.Range(0, 12)
            .Select(index => monthStart.AddMonths(index - 11))
            .ToList();
        var labels = months
            .Select(month => month.ToString("MMM yyyy", DashboardCulture))
            .ToList();
        var monthKeys = months
            .Select(month => month.ToString("yyyy-MM", CultureInfo.InvariantCulture))
            .ToList();

        var series = maintenanceRows
            .Where(row => row.MaintenanceDate.HasValue)
            .GroupBy(row => BuildDashboardGroupKey(row.TechnicianId, row.TechnicianName), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var items = group.ToList();
                var first = items[0];
                var countsByMonth = items
                    .GroupBy(item => item.MaintenanceDate!.Value.ToString("yyyy-MM", CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(month => month.Key, month => month.Count(), StringComparer.OrdinalIgnoreCase);
                List<int> values = monthKeys
                    .Select(monthKey => countsByMonth.TryGetValue(monthKey, out var count) ? count : 0)
                    .ToList();

                return new CopiersMaintenanceSeriesDto
                {
                    TechnicianId = first.TechnicianId,
                    TechnicianName = first.TechnicianName,
                    Values = values,
                    Total = values.Sum()
                };
            })
            .Where(item => item.Total > 0)
            .OrderByDescending(item => item.Total)
            .ThenBy(item => item.TechnicianName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new CopiersMaintenanceChartDto
        {
            Labels = labels,
            Series = series
        };
    }

    private static string BuildDashboardGroupKey(string? preferredId, string? fallbackLabel)
    {
        var normalizedId = NormalizeBillingGroupKey(preferredId);
        if (!string.Equals(normalizedId, "empty", StringComparison.OrdinalIgnoreCase))
            return $"id:{normalizedId}";

        return $"label:{NormalizeBillingGroupKey(fallbackLabel)}";
    }

    private static string ResolveDashboardOptionLabel(
        JsonElement item,
        string fieldName,
        int optionValue,
        IReadOnlyDictionary<int, string> labels,
        string fallback)
    {
        var formattedValue = ReadString(item, $"{fieldName}{FormattedValueAnnotationSuffix}").Trim();
        if (!string.IsNullOrWhiteSpace(formattedValue))
            return formattedValue;

        if (optionValue > 0 && labels.TryGetValue(optionValue, out var configuredLabel))
            return configuredLabel;

        return fallback;
    }

    private sealed class CopiersEquipmentRecordRow
    {
        public string RecordId { get; init; } = "";
        public string Serial { get; init; } = "";
        public string ClientId { get; init; } = "";
        public string ClientName { get; init; } = "";
        public int? CategoryValue { get; init; }
        public string CategoryLabel { get; init; } = "";
        public string Reference { get; init; } = "";
        public string Observations { get; init; } = "";
        public bool InStock { get; init; }
    }

    private sealed class CopiersMaintenanceRecordRow
    {
        public string RecordId { get; init; } = "";
        public string Title { get; init; } = "";
        public string InternalId { get; init; } = "";
        public string EquipmentId { get; init; } = "";
        public string EquipmentSerial { get; init; } = "";
        public DateOnly? MaintenanceDate { get; init; }
        public string DateValue { get; init; } = "";
        public string DateDisplay { get; init; } = "";
        public string Description { get; init; } = "";
        public string ClientId { get; init; } = "";
        public string ClientName { get; init; } = "";
        public bool HasAttachment { get; init; }
        public string AttachmentFileName { get; init; } = "";
        public int? MaintenanceTypeValue { get; init; }
        public string MaintenanceTypeLabel { get; init; } = "";
        public string TechnicianId { get; init; } = "";
        public string TechnicianName { get; init; } = "";
    }
}
