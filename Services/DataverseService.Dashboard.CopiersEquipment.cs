using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using CotizadorInterno.Web.Models.Copiers;
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
    private const string DashboardEquipmentBrandField = "cr07a_marca";
    private const string DashboardEquipmentAreaField = "cr07a_area";
    private const string DashboardEquipmentSiteField = "cr07a_sede";
    private const string DashboardEquipmentAddressField = "cr07a_direccion";
    private const string DashboardEquipmentMapHtmlField = "cr07a_htmlmapa";
    private const string DashboardEquipmentMapUrlField = "cr07a_mapa";
    private const string CopiersClientIdField = "cr07a_clienteid";
    private const string CopiersClientNameField = "cr07a_nombre";
    private const string CopiersClientContactNameField = "cr07a_nombrepersonaacargo";
    private const string CopiersClientEmailField = "cr07a_correoelectronico";
    private const string CopiersClientPhoneField = "cr07a_telefono";
    private const string CopiersClientAddressField = "cr07a_direccion";
    private const string DashboardEquipmentMovementTableLogicalName = "cr07a_movimientosequipos";
    private const string DashboardEquipmentMovementTableSetName = "cr07a_movimientosequiposes";
    private const string DashboardEquipmentMovementIdField = "cr07a_movimientosequiposid";
    private const string DashboardEquipmentMovementPrimaryNameField = "cr07a_name";
    private const string DashboardEquipmentMovementEquipmentField = "cr07a_equipo";
    private const string DashboardEquipmentMovementClientField = "cr07a_cliente";
    private const string DashboardEquipmentMovementDateField = "cr07a_fecha";
    private const string DashboardEquipmentMovementReasonField = "cr07a_motivo";

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
    private const string DashboardMaintenanceStatusField = "cr07a_estadodelmantenimiento";
    private const string DashboardMaintenanceOwnerField = "ownerid";
    private const int DashboardMaintenanceStatusCompleted = 645250000;
    private const int DashboardMaintenanceStatusPending = 645250001;
    private readonly ConcurrentDictionary<string, string[]> _copiersEquipmentAttributeNamesCache = new(StringComparer.OrdinalIgnoreCase);

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

    private static readonly IReadOnlyDictionary<int, string> DashboardMaintenanceStatusLabels =
        new Dictionary<int, string>
        {
            [DashboardMaintenanceStatusCompleted] = "Completado",
            [DashboardMaintenanceStatusPending] = "Pendiente"
        };
    private static readonly IReadOnlyList<CopiersEquipmentInventoryOptionalColumnDefinition> DashboardEquipmentInventoryOptionalColumns =
        new[]
        {
            new CopiersEquipmentInventoryOptionalColumnDefinition(
                "area",
                "Area",
                new[] { DashboardEquipmentAreaField }),
            new CopiersEquipmentInventoryOptionalColumnDefinition(
                "site",
                "Sede",
                new[] { DashboardEquipmentSiteField }),
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
        var clientsById = await GetCopiersClientContactRowsAsync(
            equipmentRows.Select(static row => row.ClientId),
            httpContext.User,
            ct);

        return new CopiersEquipmentDashboardDto
        {
            AsOfDateLabel = today.ToString("dd MMM yyyy", DashboardCulture),
            FocusLabel = "Asignacion de equipos, stock disponible y soportes ejecutados",
            HasData = equipmentRows.Count > 0,
            RecordsCount = equipmentRows.Count,
            EmptyStateTitle = "No encontramos equipos registrados.",
            EmptyStateMessage = "Cuando Dataverse tenga filas en cr07a_equipo las veras aqui.",
            Kpis = BuildEquipmentKpis(equipmentRows, maintenanceRows),
            ClientSummaries = BuildEquipmentClientSummaries(equipmentRows, clientsById),
            EquipmentRows = BuildEquipmentRows(equipmentRows, maintenanceRows),
            StockRows = BuildEquipmentRows(
                equipmentRows.Where(static row => row.InStock).ToList(),
                maintenanceRows),
            MaintenanceRows = BuildMaintenanceRows(maintenanceRows),
            CategoryOptions = BuildEquipmentCategoryOptions(),
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
        var movementMetadata = await ResolveRhEntityMetadataAsync(
            DashboardEquipmentMovementTableLogicalName,
            DashboardEquipmentMovementTableSetName,
            DashboardEquipmentMovementIdField,
            DashboardEquipmentMovementPrimaryNameField,
            httpContext.User,
            ct);

        var normalizedEquipmentId = NormalizeGuid(equipmentId, nameof(equipmentId));
        var equipment = await GetEquipmentRecordByIdAsync(equipmentMetadata, normalizedEquipmentId, httpContext.User, ct)
            ?? throw new InvalidOperationException("No encontramos el equipo seleccionado.");
        var maintenanceRows = await GetMaintenanceRecordsAsync(maintenanceMetadata, httpContext.User, ct, normalizedEquipmentId);
        var movementRows = await GetEquipmentMovementRecordsAsync(movementMetadata, httpContext.User, ct, normalizedEquipmentId);

        return new CopiersEquipmentDetailDto
        {
            Equipment = BuildEquipmentRows(new[] { equipment }, maintenanceRows).First(),
            MaintenanceRows = BuildMaintenanceRows(maintenanceRows),
            MovementRows = BuildEquipmentMovementRows(movementRows),
            CategoryOptions = BuildEquipmentCategoryOptions()
        };
    }

    public async Task<CopiersEquipmentInventoryDto> GetCopiersEquipmentInventoryAsync(
        string? clientId,
        string? clientName,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var normalizedClientId = NormalizeOptionalGuid(clientId);
        var requestedClientName = (clientName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalizedClientId) && !string.IsNullOrWhiteSpace(requestedClientName))
            normalizedClientId = await ResolveCopiersClientIdAsync(requestedClientName, ct);

        if (string.IsNullOrWhiteSpace(normalizedClientId))
            throw new InvalidOperationException("Debes seleccionar un cliente valido para consultar el inventario de equipos.");

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

        var attributeNames = await GetCopiersEquipmentAttributeNamesAsync(httpContext.User, ct);
        var fieldMap = BuildCopiersEquipmentInventoryFieldMap(attributeNames);
        var clientsById = await GetCopiersClientContactRowsAsync(new[] { normalizedClientId }, httpContext.User, ct);
        clientsById.TryGetValue(normalizedClientId, out var clientContact);
        var equipmentRows = await GetEquipmentInventoryRecordsAsync(
            equipmentMetadata,
            fieldMap,
            normalizedClientId,
            httpContext.User,
            ct);
        var maintenanceRows = await GetMaintenanceRecordsAsync(maintenanceMetadata, httpContext.User, ct);
        var records = BuildEquipmentInventoryRows(equipmentRows, maintenanceRows);
        foreach (var record in records)
        {
            record.ClientContactName = clientContact?.ContactName ?? "";
            record.ClientEmail = clientContact?.Email ?? "";
            record.ClientPhone = clientContact?.Phone ?? "";
            record.ClientAddress = clientContact?.Address ?? "";
        }

        var resolvedClientName = FirstNonEmpty(
            requestedClientName,
            clientContact?.ClientName,
            records.Select(static row => row.Company).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            "Cliente");

        return new CopiersEquipmentInventoryDto
        {
            ClientId = normalizedClientId,
            ClientName = resolvedClientName,
            ClientContactName = clientContact?.ContactName ?? "",
            ClientEmail = clientContact?.Email ?? "",
            ClientPhone = clientContact?.Phone ?? "",
            ClientAddress = clientContact?.Address ?? "",
            AsOfDateLabel = today.ToString("dd MMM yyyy", DashboardCulture),
            HasData = records.Count > 0,
            RecordsCount = records.Count,
            Kpis = BuildEquipmentInventoryKpis(records),
            Locations = BuildEquipmentInventoryLocations(records),
            Records = records,
            MissingColumns = fieldMap.MissingColumns
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

    public async Task<CopiersEquipmentSaveResultDto> SaveCopiersEquipmentAsync(
        CopiersEquipmentSaveRequestDto request,
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
        if (await GetEquipmentRecordByIdAsync(metadata, normalizedRecordId, httpContext.User, ct) is null)
            throw new InvalidOperationException("No encontramos el equipo que quieres actualizar.");

        var serial = (request.Serial ?? "").Trim();
        if (string.IsNullOrWhiteSpace(serial))
            throw new InvalidOperationException("Debes indicar el serial del equipo.");

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [metadata.PrimaryNameField] = serial,
            [DashboardEquipmentCategoryField] = request.CategoryValue,
            [DashboardEquipmentReferenceField] = (request.Reference ?? "").Trim(),
            [DashboardEquipmentAreaField] = (request.Area ?? "").Trim(),
            [DashboardEquipmentSiteField] = (request.Site ?? "").Trim(),
            [DashboardEquipmentObservationsField] = (request.Observations ?? "").Trim()
        };
        payload[DashboardEquipmentSerialField] = serial;

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

        return new CopiersEquipmentSaveResultDto
        {
            RecordId = normalizedRecordId,
            Message = "Equipo actualizado correctamente.",
            Equipment = BuildEquipmentRows(new[] { updated }, maintenanceRows).First()
        };
    }

    public async Task<CopiersEquipmentMovementSaveResultDto> RegisterCopiersEquipmentMovementAsync(
        CopiersEquipmentMovementSaveRequestDto request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var equipmentMetadata = await ResolveRhEntityMetadataAsync(
            DashboardEquipmentTableLogicalName,
            DashboardEquipmentTableSetName,
            DashboardEquipmentIdField,
            DashboardEquipmentPrimaryNameField,
            httpContext.User,
            ct);
        var movementMetadata = await ResolveRhEntityMetadataAsync(
            DashboardEquipmentMovementTableLogicalName,
            DashboardEquipmentMovementTableSetName,
            DashboardEquipmentMovementIdField,
            DashboardEquipmentMovementPrimaryNameField,
            httpContext.User,
            ct);

        var normalizedEquipmentId = NormalizeGuid(request.EquipmentId, nameof(request.EquipmentId));
        var current = await GetEquipmentRecordByIdAsync(equipmentMetadata, normalizedEquipmentId, httpContext.User, ct)
            ?? throw new InvalidOperationException("No encontramos el equipo para registrar el movimiento.");

        var clientName = (request.ClientName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clientName))
            throw new InvalidOperationException("Debes indicar el cliente nuevo del movimiento.");

        var requestedClientId = NormalizeOptionalGuid(request.ClientId);
        var resolvedClientId = !string.IsNullOrWhiteSpace(requestedClientId)
            ? requestedClientId
            : await ResolveCopiersClientIdAsync(clientName, ct);
        if (string.IsNullOrWhiteSpace(resolvedClientId))
            throw new InvalidOperationException("No encontramos un cliente valido para el movimiento. Selecciona una opcion sugerida.");

        var reason = (request.Reason ?? "").Trim();
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Debes indicar el motivo del movimiento.");

        if (!TryParseDateOnly(request.DateValue, out var movementDate))
            throw new InvalidOperationException("Debes indicar una fecha de movimiento valida.");

        var movementRecordId = await CreateCopiersEquipmentMovementAsync(
            movementMetadata,
            equipmentMetadata,
            current,
            normalizedEquipmentId,
            resolvedClientId,
            clientName,
            movementDate,
            reason,
            httpContext.User,
            ct);

        var equipmentClientNavigationProperty = await ResolveRhLookupNavigationPropertyAsync(
            DashboardEquipmentTableLogicalName,
            DashboardEquipmentClientField,
            DashboardEquipmentClientField,
            httpContext.User,
            ct);
        await CallDataverseSendAsync(
            $"/api/data/v9.2/{equipmentMetadata.EntitySetName}({normalizedEquipmentId})",
            "PATCH",
            new Dictionary<string, object?>
            {
                [$"{equipmentClientNavigationProperty}@odata.bind"] = $"/{ClientsEntitySetName}({resolvedClientId})"
            },
            httpContext.User,
            ct);

        var updated = await GetEquipmentRecordByIdAsync(equipmentMetadata, normalizedEquipmentId, httpContext.User, ct)
            ?? throw new InvalidOperationException("El movimiento se registro pero no pudimos refrescar la informacion del equipo.");
        var maintenanceMetadata = await ResolveRhEntityMetadataAsync(
            DashboardMaintenanceTableLogicalName,
            DashboardMaintenanceTableSetName,
            DashboardMaintenanceIdField,
            DashboardMaintenancePrimaryNameField,
            httpContext.User,
            ct);
        var maintenanceRows = await GetMaintenanceRecordsAsync(maintenanceMetadata, httpContext.User, ct, normalizedEquipmentId);
        var movementRows = await GetEquipmentMovementRecordsAsync(movementMetadata, httpContext.User, ct, normalizedEquipmentId);

        return new CopiersEquipmentMovementSaveResultDto
        {
            RecordId = movementRecordId,
            Message = "Movimiento registrado y equipo reasignado correctamente.",
            Equipment = BuildEquipmentRows(new[] { updated }, maintenanceRows).First(),
            MovementRows = BuildEquipmentMovementRows(movementRows)
        };
    }

    public async Task<CopiersEquipmentClientSaveResultDto> SaveCopiersEquipmentClientAsync(
        CopiersEquipmentClientSaveRequestDto request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var normalizedClientId = NormalizeGuid(request.ClientId, nameof(request.ClientId));
        var clientName = (request.ClientName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clientName))
            throw new InvalidOperationException("Debes indicar el nombre del cliente.");

        var payload = new Dictionary<string, object?>
        {
            [CopiersClientNameField] = clientName,
            [CopiersClientContactNameField] = (request.ContactName ?? "").Trim(),
            [CopiersClientEmailField] = (request.Email ?? "").Trim(),
            [CopiersClientPhoneField] = (request.Phone ?? "").Trim(),
            [CopiersClientAddressField] = (request.Address ?? "").Trim()
        };

        var relativeUrl = $"/api/data/v9.2/{ClientsEntitySetName}({normalizedClientId})";
        await CallDataverseSendAsync(relativeUrl, "PATCH", payload, httpContext.User, ct);

        var clientsById = await GetCopiersClientContactRowsAsync(new[] { normalizedClientId }, httpContext.User, ct);
        clientsById.TryGetValue(normalizedClientId, out var client);

        return new CopiersEquipmentClientSaveResultDto
        {
            ClientId = normalizedClientId,
            Message = "Cliente actualizado correctamente.",
            Client = new CopiersEquipmentClientSummaryDto
            {
                ClientId = normalizedClientId,
                ClientName = client?.ClientName ?? clientName,
                ContactName = client?.ContactName ?? (request.ContactName ?? "").Trim(),
                Email = client?.Email ?? (request.Email ?? "").Trim(),
                Phone = client?.Phone ?? (request.Phone ?? "").Trim(),
                Address = client?.Address ?? (request.Address ?? "").Trim()
            }
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

    private async Task<string> CreateCopiersEquipmentMovementAsync(
        RhEntityMetadata movementMetadata,
        RhEntityMetadata equipmentMetadata,
        CopiersEquipmentRecordRow equipment,
        string equipmentId,
        string clientId,
        string clientName,
        DateOnly movementDate,
        string reason,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var clientNavigationProperty = await ResolveRhLookupNavigationPropertyAsync(
            DashboardEquipmentMovementTableLogicalName,
            DashboardEquipmentMovementClientField,
            DashboardEquipmentMovementClientField,
            user,
            ct);
        var equipmentLookupCandidates = await ResolveCopiersEquipmentMovementEquipmentLookupFieldCandidatesAsync(user, ct);
        InvalidOperationException? lastLookupException = null;

        foreach (var equipmentLookupField in equipmentLookupCandidates)
        {
            try
            {
                var equipmentNavigationProperty = await ResolveRhLookupNavigationPropertyAsync(
                    DashboardEquipmentMovementTableLogicalName,
                    equipmentLookupField,
                    equipmentLookupField,
                    user,
                    ct);
                var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    [DashboardEquipmentMovementDateField] = movementDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    [DashboardEquipmentMovementReasonField] = reason,
                    [$"{clientNavigationProperty}@odata.bind"] = $"/{ClientsEntitySetName}({clientId})",
                    [$"{equipmentNavigationProperty}@odata.bind"] = $"/{equipmentMetadata.EntitySetName}({equipmentId})"
                };

                if (!string.IsNullOrWhiteSpace(movementMetadata.PrimaryNameField)
                    && !string.Equals(movementMetadata.PrimaryNameField, movementMetadata.PrimaryIdField, StringComparison.OrdinalIgnoreCase))
                {
                    payload[movementMetadata.PrimaryNameField] = BuildCopiersEquipmentMovementName(equipment, clientName, movementDate);
                }

                var body = await CallDataverseSendAsync(
                    $"/api/data/v9.2/{movementMetadata.EntitySetName}",
                    "POST",
                    payload,
                    user,
                    ct);
                var createdId = TryReadCreatedDataverseId(body, movementMetadata.PrimaryIdField);
                return createdId;
            }
            catch (InvalidOperationException ex) when (ShouldRetryCopiersEquipmentMovementLookupQuery(ex, equipmentLookupField))
            {
                lastLookupException = ex;
                _logger.LogWarning(
                    ex,
                    "Fallo el registro de movimiento usando el lookup de equipo {LookupField}. Se intentara otra variante.",
                    equipmentLookupField);
            }
        }

        if (lastLookupException is not null)
            throw lastLookupException;

        throw new InvalidOperationException("No fue posible resolver el lookup del equipo para registrar el movimiento.");
    }

    private async Task<List<CopiersEquipmentMovementRecordRow>> GetEquipmentMovementRecordsAsync(
        RhEntityMetadata metadata,
        ClaimsPrincipal user,
        CancellationToken ct,
        string? equipmentId = null)
    {
        var equipmentLookupFieldCandidates = await ResolveCopiersEquipmentMovementEquipmentLookupFieldCandidatesAsync(user, ct);
        InvalidOperationException? lastLookupException = null;

        foreach (var equipmentLookupField in equipmentLookupFieldCandidates)
        {
            try
            {
                return await GetEquipmentMovementRecordsCoreAsync(metadata, user, ct, equipmentId, equipmentLookupField);
            }
            catch (InvalidOperationException ex) when (ShouldRetryCopiersEquipmentMovementLookupQuery(ex, equipmentLookupField))
            {
                lastLookupException = ex;
                _logger.LogWarning(
                    ex,
                    "Fallo la consulta de movimientos usando el lookup de equipo {LookupField}. Se intentara otra variante.",
                    equipmentLookupField);
            }
        }

        if (lastLookupException is not null)
            throw lastLookupException;

        return await GetEquipmentMovementRecordsCoreAsync(
            metadata,
            user,
            ct,
            equipmentId,
            DashboardEquipmentMovementEquipmentField);
    }

    private async Task<List<CopiersEquipmentMovementRecordRow>> GetEquipmentMovementRecordsCoreAsync(
        RhEntityMetadata metadata,
        ClaimsPrincipal user,
        CancellationToken ct,
        string? equipmentId,
        string equipmentLookupField)
    {
        var relativeUrl =
            $"/api/data/v9.2/{metadata.EntitySetName}?$select={BuildEquipmentMovementSelectClause(metadata, equipmentLookupField)}" +
            $"{BuildEquipmentMovementFilterQuery(equipmentId, equipmentLookupField)}" +
            $"&$orderby={DashboardEquipmentMovementDateField} desc";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        return items
            .Select(item => ParseEquipmentMovementRecord(item, metadata.PrimaryIdField, metadata.PrimaryNameField, equipmentLookupField))
            .Where(item => item is not null)
            .Cast<CopiersEquipmentMovementRecordRow>()
            .ToList();
    }

    private string BuildEquipmentMovementSelectClause(RhEntityMetadata metadata, string equipmentLookupField)
    {
        return string.Join(",", new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            BuildDashboardLookupValuePropertyName(equipmentLookupField),
            BuildDashboardLookupValuePropertyName(DashboardEquipmentMovementClientField),
            DashboardEquipmentMovementDateField,
            DashboardEquipmentMovementReasonField
        }
        .Where(static field => !string.IsNullOrWhiteSpace(field))
        .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string BuildEquipmentMovementFilterQuery(string? equipmentId, string equipmentLookupField)
    {
        var normalizedEquipmentId = NormalizeOptionalGuid(equipmentId);
        if (string.IsNullOrWhiteSpace(normalizedEquipmentId))
            return "";

        var lookupValueProperty = BuildDashboardLookupValuePropertyName(equipmentLookupField);
        var filter = $"{lookupValueProperty} eq {normalizedEquipmentId}";
        return $"&$filter={Uri.EscapeDataString(filter)}";
    }

    private CopiersEquipmentMovementRecordRow? ParseEquipmentMovementRecord(
        JsonElement item,
        string primaryIdField,
        string primaryNameField,
        string equipmentLookupField)
    {
        var recordId = FirstNonEmpty(
            ReadString(item, primaryIdField),
            ReadString(item, DashboardEquipmentMovementIdField),
            ReadString(item, primaryNameField));
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var date = ReadDateOnly(item, DashboardEquipmentMovementDateField);
        var rawDate = ReadString(item, DashboardEquipmentMovementDateField);

        return new CopiersEquipmentMovementRecordRow
        {
            RecordId = recordId.Trim(),
            EquipmentId = ReadCopiersLookupId(item, equipmentLookupField, "equipo"),
            EquipmentSerial = ReadCopiersFieldDisplayValue(
                item,
                equipmentLookupField,
                "equipo",
                "Equipo sin nombre"),
            ClientId = ReadCopiersLookupId(item, DashboardEquipmentMovementClientField, "cliente"),
            ClientName = ReadCopiersFieldDisplayValue(
                item,
                DashboardEquipmentMovementClientField,
                "cliente",
                "Cliente sin nombre"),
            MovementDate = date,
            DateValue = date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? rawDate,
            DateDisplay = date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? rawDate,
            Reason = ReadString(item, DashboardEquipmentMovementReasonField).Trim()
        };
    }

    private async Task<IReadOnlyList<string>> ResolveCopiersEquipmentMovementEquipmentLookupFieldCandidatesAsync(
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var navigationProperty = await ResolveRhLookupNavigationPropertyAsync(
            DashboardEquipmentMovementTableLogicalName,
            DashboardEquipmentMovementEquipmentField,
            DashboardEquipmentMovementEquipmentField,
            user,
            ct);

        return BuildLookupLogicalNameCandidates(
                DashboardEquipmentMovementEquipmentField,
                "cr07a_iddeequipo",
                "cr07a_idequipo",
                navigationProperty)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate => candidate!.Trim())
            .ToList();
    }

    private bool ShouldRetryCopiersEquipmentMovementLookupQuery(InvalidOperationException exception, string equipmentLookupField)
    {
        var lookupValueProperty = BuildDashboardLookupValuePropertyName(equipmentLookupField);
        return exception.Message.Contains(equipmentLookupField, StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains(lookupValueProperty, StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("Could not find a property", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildCopiersEquipmentMovementName(
        CopiersEquipmentRecordRow equipment,
        string clientName,
        DateOnly movementDate)
    {
        var serial = FirstNonEmpty(equipment.Serial, "Equipo");
        var dateLabel = movementDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        return $"Movimiento {serial} - {clientName} - {dateLabel}";
    }

    private static string TryReadCreatedDataverseId(string body, string primaryIdField)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "";

        try
        {
            using var doc = JsonDocument.Parse(body);
            return FirstNonEmpty(ReadString(doc.RootElement, primaryIdField), ReadString(doc.RootElement, "id"));
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private async Task<IReadOnlyDictionary<string, CopiersClientContactRow>> GetCopiersClientContactRowsAsync(
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
            return new Dictionary<string, CopiersClientContactRow>(StringComparer.OrdinalIgnoreCase);

        var select = string.Join(",", new[]
        {
            CopiersClientIdField,
            CopiersClientNameField,
            CopiersClientContactNameField,
            CopiersClientEmailField,
            CopiersClientPhoneField,
            CopiersClientAddressField
        });
        var result = new Dictionary<string, CopiersClientContactRow>(StringComparer.OrdinalIgnoreCase);

        foreach (var clientId in normalizedIds)
        {
            try
            {
                var relativeUrl = $"/api/data/v9.2/{ClientsEntitySetName}({clientId})?$select={select}";
                var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct);
                using var doc = JsonDocument.Parse(json);
                var row = ParseCopiersClientContactRow(doc.RootElement);
                if (!string.IsNullOrWhiteSpace(row.ClientId))
                    result[row.ClientId] = row;
            }
            catch (Exception ex) when (ex is InvalidOperationException or JsonException)
            {
                _logger.LogWarning(
                    ex,
                    "No fue posible consultar los datos de contacto del cliente {ClientId}.",
                    clientId);
            }
        }

        return result;
    }

    private static CopiersClientContactRow ParseCopiersClientContactRow(JsonElement item)
    {
        return new CopiersClientContactRow
        {
            ClientId = NormalizeOptionalGuid(ReadString(item, CopiersClientIdField)),
            ClientName = ReadString(item, CopiersClientNameField).Trim(),
            ContactName = ReadString(item, CopiersClientContactNameField).Trim(),
            Email = ReadString(item, CopiersClientEmailField).Trim(),
            Phone = ReadString(item, CopiersClientPhoneField).Trim(),
            Address = ReadString(item, CopiersClientAddressField).Trim()
        };
    }

    private async Task<HashSet<string>> GetCopiersEquipmentAttributeNamesAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var cacheKey = DashboardEquipmentTableLogicalName;
        if (!_copiersEquipmentAttributeNamesCache.TryGetValue(cacheKey, out var cached))
        {
            try
            {
                var relativeUrl =
                    $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(DashboardEquipmentTableLogicalName)}')" +
                    "/Attributes?$select=LogicalName";
                var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct);
                cached = items
                    .Select(item => ReadString(item, "LogicalName").Trim())
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception ex) when (ex is InvalidOperationException or JsonException)
            {
                _logger.LogWarning(
                    ex,
                    "No fue posible consultar las columnas de {EntityLogicalName}. Se usara el conjunto base de equipos.",
                    DashboardEquipmentTableLogicalName);
                cached = new[]
                {
                    DashboardEquipmentIdField,
                    DashboardEquipmentPrimaryNameField,
                    DashboardEquipmentSerialField,
                    DashboardEquipmentClientField,
                    DashboardEquipmentCategoryField,
                    DashboardEquipmentReferenceField,
                    DashboardEquipmentObservationsField
                };
            }

            _copiersEquipmentAttributeNamesCache[cacheKey] = cached;
        }

        return new HashSet<string>(cached, StringComparer.OrdinalIgnoreCase);
    }

    private static CopiersEquipmentInventoryFieldMap BuildCopiersEquipmentInventoryFieldMap(
        HashSet<string> attributeNames)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in DashboardEquipmentInventoryOptionalColumns)
        {
            var logicalName = definition.CandidateLogicalNames
                .FirstOrDefault(attributeNames.Contains);
            if (!string.IsNullOrWhiteSpace(logicalName))
            {
                fields[definition.Key] = logicalName;
                continue;
            }
        }

        return new CopiersEquipmentInventoryFieldMap(fields, Array.Empty<CopiersEquipmentInventoryMissingColumnDto>());
    }

    private async Task<List<CopiersEquipmentInventoryRecordRow>> GetEquipmentInventoryRecordsAsync(
        RhEntityMetadata metadata,
        CopiersEquipmentInventoryFieldMap fieldMap,
        string clientId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var relativeUrl =
            $"/api/data/v9.2/{metadata.EntitySetName}?$select={BuildEquipmentInventorySelectClause(metadata, fieldMap)}" +
            $"{BuildEquipmentInventoryFilterQuery(clientId)}" +
            $"&$orderby={DashboardEquipmentSerialField} asc";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        return items
            .Select(item => ParseEquipmentInventoryRecord(item, metadata.PrimaryIdField, metadata.PrimaryNameField, fieldMap))
            .Where(item => item is not null)
            .Cast<CopiersEquipmentInventoryRecordRow>()
            .ToList();
    }

    private string BuildEquipmentInventorySelectClause(
        RhEntityMetadata metadata,
        CopiersEquipmentInventoryFieldMap fieldMap)
    {
        return string.Join(",", new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            DashboardEquipmentSerialField,
            BuildDashboardLookupValuePropertyName(DashboardEquipmentClientField),
            DashboardEquipmentCategoryField,
            DashboardEquipmentReferenceField,
            DashboardEquipmentObservationsField,
            fieldMap.Get("area"),
            fieldMap.Get("site")
        }
        .Where(static field => !string.IsNullOrWhiteSpace(field))
        .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string BuildEquipmentInventoryFilterQuery(string clientId)
    {
        var normalizedClientId = NormalizeGuid(clientId, nameof(clientId));
        var filter = $"{BuildDashboardLookupValuePropertyName(DashboardEquipmentClientField)} eq {normalizedClientId}";
        return $"&$filter={Uri.EscapeDataString(filter)}";
    }

    private CopiersEquipmentInventoryRecordRow? ParseEquipmentInventoryRecord(
        JsonElement item,
        string primaryIdField,
        string primaryNameField,
        CopiersEquipmentInventoryFieldMap fieldMap)
    {
        var equipment = ParseEquipmentRecord(item, primaryIdField, primaryNameField);
        if (equipment is null)
            return null;

        return new CopiersEquipmentInventoryRecordRow
        {
            RecordId = equipment.RecordId,
            Serial = equipment.Serial,
            ClientId = equipment.ClientId,
            ClientName = equipment.ClientName,
            Type = equipment.CategoryLabel,
            Area = FirstNonEmpty(ReadString(item, fieldMap.Get("area")).Trim(), equipment.Area),
            Site = FirstNonEmpty(ReadString(item, fieldMap.Get("site")).Trim(), equipment.Site),
            Observations = equipment.Observations
        };
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
            DashboardEquipmentAreaField,
            DashboardEquipmentSiteField,
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
            Area = ReadString(item, DashboardEquipmentAreaField).Trim(),
            Site = ReadString(item, DashboardEquipmentSiteField).Trim(),
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
            BuildDashboardLookupValuePropertyName(DashboardMaintenanceClientField),
            DashboardMaintenanceAttachmentField,
            DashboardMaintenanceExternalIdField,
            DashboardMaintenanceTypeField,
            DashboardMaintenanceStatusField,
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
        var statusValue = item.TryGetProperty(DashboardMaintenanceStatusField, out _)
            ? ReadIntFlexible(item, DashboardMaintenanceStatusField)
            : DashboardMaintenanceStatusPending;
        var normalizedStatusValue = DashboardMaintenanceStatusLabels.ContainsKey(statusValue)
            ? statusValue
            : DashboardMaintenanceStatusPending;
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
            MaintenanceStatusValue = normalizedStatusValue,
            MaintenanceStatusLabel = ResolveDashboardOptionLabel(
                item,
                DashboardMaintenanceStatusField,
                normalizedStatusValue,
                DashboardMaintenanceStatusLabels,
                "Pendiente"),
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

    private static IReadOnlyList<CopiersEquipmentOptionDto> BuildEquipmentCategoryOptions()
    {
        return DashboardEquipmentCategoryLabels
            .OrderBy(item => item.Key)
            .Select(item => new CopiersEquipmentOptionDto
            {
                Value = item.Key,
                Label = item.Value
            })
            .ToList();
    }

    private IReadOnlyList<CopiersEquipmentClientSummaryDto> BuildEquipmentClientSummaries(
        IReadOnlyList<CopiersEquipmentRecordRow> equipmentRows,
        IReadOnlyDictionary<string, CopiersClientContactRow> clientsById)
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
                clientsById.TryGetValue(first.ClientId, out var client);

                return new CopiersEquipmentClientSummaryDto
                {
                    ClientId = first.ClientId,
                    ClientName = FirstNonEmpty(client?.ClientName, first.ClientName),
                    ContactName = client?.ContactName ?? "",
                    Email = client?.Email ?? "",
                    Phone = client?.Phone ?? "",
                    Address = client?.Address ?? "",
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
                    Area = row.Area,
                    Site = row.Site,
                    Observations = row.Observations,
                    InStock = row.InStock,
                    MaintenanceCount = equipmentMaintenance?.Count ?? 0,
                    LastMaintenanceDateValue = latestMaintenance?.DateValue ?? "",
                    LastMaintenanceDateDisplay = latestMaintenance?.DateDisplay ?? "Sin mantenimientos"
                };
            })
            .OrderBy(item => item.InStock)
            .ThenBy(item => item.ClientName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Serial, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<CopiersEquipmentInventoryRowDto> BuildEquipmentInventoryRows(
        IReadOnlyList<CopiersEquipmentInventoryRecordRow> equipmentRows,
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

        var orderedRows = equipmentRows
            .OrderBy(item => FirstNonEmpty(item.Site, "zzzz"), StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => FirstNonEmpty(item.Area, "zzzz"), StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Serial, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return orderedRows
            .Select((row, index) =>
            {
                maintenanceByEquipment.TryGetValue(row.RecordId, out var equipmentMaintenance);
                var latestMaintenance = equipmentMaintenance?.FirstOrDefault();

                return new CopiersEquipmentInventoryRowDto
                {
                    LineNumber = index + 1,
                    RecordId = row.RecordId,
                    Type = row.Type,
                    Brand = row.Brand,
                    Serial = row.Serial,
                    Company = row.ClientName,
                    Area = row.Area,
                    Site = row.Site,
                    Address = row.Address,
                    MapUrl = row.MapUrl,
                    MapEmbedUrl = row.MapEmbedUrl,
                    Observations = row.Observations,
                    MaintenanceCount = equipmentMaintenance?.Count ?? 0,
                    LastMaintenanceDateDisplay = latestMaintenance?.DateDisplay ?? "Sin mantenimientos"
                };
            })
            .ToList();
    }

    private IReadOnlyList<CopiersEquipmentInventoryMetricDto> BuildEquipmentInventoryKpis(
        IReadOnlyList<CopiersEquipmentInventoryRowDto> records)
    {
        var siteCount = CountDistinctInventoryValues(records.Select(static row => row.Site));
        var areaCount = CountDistinctInventoryValues(records.Select(static row => row.Area));

        return new[]
        {
            new CopiersEquipmentInventoryMetricDto
            {
                Key = "equipment",
                Label = "Equipos",
                Value = records.Count,
                SecondaryLabel = "Cliente",
                SecondaryValue = records.Select(static row => row.Company).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? ""
            },
            new CopiersEquipmentInventoryMetricDto
            {
                Key = "sites",
                Label = "Sedes",
                Value = siteCount,
                SecondaryLabel = "Areas",
                SecondaryValue = areaCount.ToString("N0", DashboardCulture)
            },
            new CopiersEquipmentInventoryMetricDto
            {
                Key = "areas",
                Label = "Areas",
                Value = areaCount,
                SecondaryLabel = "Sedes",
                SecondaryValue = siteCount.ToString("N0", DashboardCulture)
            },
            new CopiersEquipmentInventoryMetricDto
            {
                Key = "maintenance",
                Label = "Mantenimientos",
                Value = records.Sum(static row => row.MaintenanceCount),
                SecondaryLabel = "Equipos con historial",
                SecondaryValue = records.Count(static row => row.MaintenanceCount > 0).ToString("N0", DashboardCulture)
            }
        };
    }

    private IReadOnlyList<CopiersEquipmentInventoryLocationDto> BuildEquipmentInventoryLocations(
        IReadOnlyList<CopiersEquipmentInventoryRowDto> records)
    {
        return records
            .GroupBy(row => BuildEquipmentInventoryLocationKey(row), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var items = group.ToList();
                var first = items[0];
                return new CopiersEquipmentInventoryLocationDto
                {
                    Key = group.Key,
                    Site = FirstNonEmpty(first.Site, "Sin sede"),
                    Address = FirstNonEmpty(first.Address, "Sin direccion"),
                    MapUrl = first.MapUrl,
                    MapEmbedUrl = first.MapEmbedUrl,
                    EquipmentCount = items.Count,
                    Areas = items
                        .Select(static row => row.Area)
                        .Where(static value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                };
            })
            .OrderByDescending(item => item.EquipmentCount)
            .ThenBy(item => item.Site, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Address, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int CountDistinctInventoryValues(IEnumerable<string> values) =>
        values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    private static string BuildEquipmentInventoryLocationKey(CopiersEquipmentInventoryRowDto row) =>
        string.Join("|",
            NormalizeCopiersComparableValue(row.Site),
            NormalizeCopiersComparableValue(row.Address),
            NormalizeCopiersComparableValue(row.MapUrl));

    private static string ExtractCopiersMapEmbedUrl(string? rawMapHtml)
    {
        if (string.IsNullOrWhiteSpace(rawMapHtml))
            return "";

        var match = Regex.Match(
            rawMapHtml,
            "src\\s*=\\s*[\"'](?<url>[^\"']+)[\"']",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
            return "";

        var url = WebUtility.HtmlDecode(match.Groups["url"].Value).Trim();
        return IsAllowedCopiersMapUrl(url, requireEmbed: true) ? url : "";
    }

    private static string NormalizeCopiersMapUrl(string? rawMapUrl)
    {
        if (string.IsNullOrWhiteSpace(rawMapUrl))
            return "";

        var url = WebUtility.HtmlDecode(rawMapUrl).Trim();
        return IsAllowedCopiersMapUrl(url, requireEmbed: false) ? url : "";
    }

    private static bool IsAllowedCopiersMapUrl(string value, bool requireEmbed)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme is not "https" and not "http")
            return false;

        var host = uri.Host.ToLowerInvariant();
        var isGoogleHost =
            string.Equals(host, "maps.app.goo.gl", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "google.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "google.com.co", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".google.com.co", StringComparison.OrdinalIgnoreCase);

        if (!isGoogleHost)
            return false;

        return !requireEmbed || uri.AbsolutePath.StartsWith("/maps/embed", StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<CopiersEquipmentMovementRowDto> BuildEquipmentMovementRows(
        IReadOnlyList<CopiersEquipmentMovementRecordRow> movementRows)
    {
        return movementRows
            .OrderByDescending(item => item.MovementDate ?? DateOnly.MinValue)
            .ThenBy(item => item.ClientName, StringComparer.OrdinalIgnoreCase)
            .Select(item => new CopiersEquipmentMovementRowDto
            {
                RecordId = item.RecordId,
                EquipmentId = item.EquipmentId,
                EquipmentSerial = item.EquipmentSerial,
                ClientId = item.ClientId,
                ClientName = item.ClientName,
                DateValue = item.DateValue,
                DateDisplay = item.DateDisplay,
                Reason = item.Reason
            })
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
                MaintenanceStatusValue = item.MaintenanceStatusValue,
                MaintenanceStatusLabel = item.MaintenanceStatusLabel,
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

    private sealed record CopiersEquipmentInventoryOptionalColumnDefinition(
        string Key,
        string Label,
        IReadOnlyList<string> CandidateLogicalNames);

    private sealed class CopiersEquipmentInventoryFieldMap
    {
        private readonly IReadOnlyDictionary<string, string> _fields;

        public CopiersEquipmentInventoryFieldMap(
            IReadOnlyDictionary<string, string> fields,
            IReadOnlyList<CopiersEquipmentInventoryMissingColumnDto> missingColumns)
        {
            _fields = fields;
            MissingColumns = missingColumns;
        }

        public IReadOnlyList<CopiersEquipmentInventoryMissingColumnDto> MissingColumns { get; }

        public string Get(string key) =>
            _fields.TryGetValue(key, out var field) ? field : "";
    }

    private sealed class CopiersEquipmentInventoryRecordRow
    {
        public string RecordId { get; init; } = "";
        public string Serial { get; init; } = "";
        public string ClientId { get; init; } = "";
        public string ClientName { get; init; } = "";
        public string Type { get; init; } = "";
        public string Brand { get; init; } = "";
        public string Area { get; init; } = "";
        public string Site { get; init; } = "";
        public string Address { get; init; } = "";
        public string MapUrl { get; init; } = "";
        public string MapEmbedUrl { get; init; } = "";
        public string Observations { get; init; } = "";
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
        public string Area { get; init; } = "";
        public string Site { get; init; } = "";
        public string Observations { get; init; } = "";
        public bool InStock { get; init; }
    }

    private sealed class CopiersClientContactRow
    {
        public string ClientId { get; init; } = "";
        public string ClientName { get; init; } = "";
        public string ContactName { get; init; } = "";
        public string Email { get; init; } = "";
        public string Phone { get; init; } = "";
        public string Address { get; init; } = "";
    }

    private sealed class CopiersEquipmentMovementRecordRow
    {
        public string RecordId { get; init; } = "";
        public string EquipmentId { get; init; } = "";
        public string EquipmentSerial { get; init; } = "";
        public string ClientId { get; init; } = "";
        public string ClientName { get; init; } = "";
        public DateOnly? MovementDate { get; init; }
        public string DateValue { get; init; } = "";
        public string DateDisplay { get; init; } = "";
        public string Reason { get; init; } = "";
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
        public int? MaintenanceStatusValue { get; init; }
        public string MaintenanceStatusLabel { get; init; } = "";
        public string TechnicianId { get; init; } = "";
        public string TechnicianName { get; init; } = "";
    }
}
