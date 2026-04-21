using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.Copiers;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.RH;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string CopiersSupplyLogicalName = "cr07a_suministro";
    private const string CopiersSupplyFallbackEntitySetName = "cr07a_suministros";
    private const string CopiersSupplyFallbackIdField = "cr07a_suministroid";
    private const string CopiersSupplyFallbackPrimaryNameField = "cr07a_nombredelsuministro";
    private const string CopiersSupplyNameField = "cr07a_nombredelsuministro";
    private const string CopiersSupplyQuantityField = "cr07a_cantidad";
    private const string CopiersSupplyLastPurchaseDateField = "cr07a_fechadecompra";
    private const string CopiersSupplyStatusField = "cr07a_estadodelsuministro";
    private const int CopiersSupplyStatusAvailable = 645250000;
    private const int CopiersSupplyStatusExhausted = 645250001;

    private const string CopiersSupplierInvoiceLogicalName = "cr07a_facturasproveedorescopiers";
    private const string CopiersSupplierInvoiceFallbackEntitySetName = "cr07a_facturasproveedorescopiers";
    private const string CopiersSupplierInvoiceFallbackIdField = "cr07a_facturasproveedorescopiersid";
    private const string CopiersSupplierInvoiceFallbackPrimaryNameField = "cr07a_name";
    private const string CopiersSupplierInvoiceNumberField = "cr07a_name";
    private const string CopiersSupplierInvoiceSupplyField = "cr07a_suministro";
    private const string CopiersSupplierInvoiceQuantityField = "cr07a_cantidad";
    private const string CopiersSupplierInvoiceUnitValueBeforeVatField = "cr07a_valorunitarioantesdeiva";
    private const string CopiersSupplierInvoiceApprovedField = "cr07a_aprobadoeingresado";
    private const int CopiersSupplierInvoiceApprovedNo = 0;
    private const int CopiersSupplierInvoiceApprovedYes = 1;

    private const string CopiersDeliveryLogicalName = "cr07a_entrega";
    private const string CopiersDeliveryFallbackEntitySetName = "cr07a_entregas";
    private const string CopiersDeliveryFallbackIdField = "cr07a_entregaid";
    private const string CopiersDeliveryFallbackPrimaryNameField = "cr07a_name";
    private const string CopiersDeliveryClientField = "cr07a_iddecliente";
    private const string CopiersDeliverySupplyField = "cr07a_iddesuministro";
    private const string CopiersDeliveryDateField = "cr07a_fechadeentrega";
    private const string CopiersDeliveryQuantityField = "cr07a_cantidadentregada";
    private const string CopiersDeliveryStatusField = "cr07a_estadodeentrega";
    private const string CopiersDeliveryAttachmentField = "cr07a_comprobantedeentrega";
    private const string CopiersDeliveryAttachmentNameField = "cr07a_comprobantedeentrega_name";
    private const int CopiersDeliveryStatusCompleted = 645250000;
    private const int CopiersDeliveryStatusPending = 645250001;

    private static readonly CultureInfo CopiersCulture = CultureInfo.GetCultureInfo("es-CO");
    private static readonly IReadOnlyList<CopiersOptionDto> CopiersSupplyStatusOptions = new[]
    {
        new CopiersOptionDto { Value = CopiersSupplyStatusAvailable, Label = "Disponible" },
        new CopiersOptionDto { Value = CopiersSupplyStatusExhausted, Label = "Agotado" }
    };
    private static readonly IReadOnlyList<CopiersOptionDto> CopiersDeliveryStatusOptions = new[]
    {
        new CopiersOptionDto { Value = CopiersDeliveryStatusCompleted, Label = "Completada" },
        new CopiersOptionDto { Value = CopiersDeliveryStatusPending, Label = "Pendiente" }
    };
    private static readonly HashSet<string> CopiersAllowedAttachmentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".jpg",
        ".jpeg",
        ".png",
        ".doc",
        ".docx"
    };

    public async Task<CopiersMaintenanceBoardDto> GetCopiersMaintenanceBoardAsync(CancellationToken ct = default)
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

        var rows = await GetCopiersMaintenanceRowsForCurrentOwnerAsync(metadata, httpContext.User, ct);
        return new CopiersMaintenanceBoardDto
        {
            Records = BuildMaintenanceRows(rows),
            TypeOptions = DashboardMaintenanceTypeLabels
                .Select(item => new CopiersOptionDto { Value = item.Key, Label = item.Value })
                .OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    public async Task<CopiersMaintenanceSaveResultDto> SaveCopiersMaintenanceAsync(
        CopiersMaintenanceSaveRequestDto request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveRhEntityMetadataAsync(
            DashboardMaintenanceTableLogicalName,
            DashboardMaintenanceTableSetName,
            DashboardMaintenanceIdField,
            DashboardMaintenancePrimaryNameField,
            httpContext.User,
            ct);
        var equipmentMetadata = await ResolveRhEntityMetadataAsync(
            DashboardEquipmentTableLogicalName,
            DashboardEquipmentTableSetName,
            DashboardEquipmentIdField,
            DashboardEquipmentPrimaryNameField,
            httpContext.User,
            ct);

        var normalizedRecordId = NormalizeOptionalGuid(request.RecordId);
        var isCreate = string.IsNullOrWhiteSpace(normalizedRecordId);
        var currentUser = await GetCurrentUserAsync(ct);
        if (!isCreate)
        {
            var current = await GetCopiersMaintenanceRowByIdAsync(metadata, normalizedRecordId, httpContext.User, ct);
            if (!string.Equals(
                NormalizeOptionalGuid(current.TechnicianId),
                NormalizeOptionalGuid(currentUser?.SystemUserId),
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("El mantenimiento seleccionado no pertenece al owner autenticado.");
            }
        }

        var maintenanceDate = ParseCopiersRequiredDate(request.DateValue, "fecha de mantenimiento");
        var equipmentId = NormalizeGuid(request.EquipmentId, nameof(request.EquipmentId));
        var clientId = NormalizeOptionalGuid(request.ClientId);
        if (string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(request.ClientName))
            clientId = await ResolveCopiersClientIdAsync(request.ClientName.Trim(), ct);

        var title = FirstNonEmpty(
            request.Title?.Trim(),
            $"Mantenimiento {maintenanceDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}");

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [DashboardMaintenanceTitleField] = title,
            [DashboardMaintenanceDateField] = maintenanceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            [DashboardMaintenanceDescriptionField] = request.Description?.Trim(),
            [DashboardMaintenanceExternalIdField] = string.IsNullOrWhiteSpace(request.InternalId) ? null : request.InternalId.Trim(),
            [DashboardMaintenanceTypeField] = NormalizeCopiersMaintenanceType(request.MaintenanceTypeValue)
        };

        if (!string.IsNullOrWhiteSpace(metadata.PrimaryNameField)
            && !payload.ContainsKey(metadata.PrimaryNameField))
        {
            payload[metadata.PrimaryNameField] = title;
        }

        var equipmentNavigationProperty = await ResolveRhLookupNavigationPropertyAsync(
            DashboardMaintenanceTableLogicalName,
            DashboardMaintenanceEquipmentField,
            DashboardMaintenanceEquipmentField,
            httpContext.User,
            ct);
        payload[$"{equipmentNavigationProperty}@odata.bind"] = $"/{equipmentMetadata.EntitySetName}({equipmentId})";

        if (!string.IsNullOrWhiteSpace(clientId))
        {
            var clientNavigationProperty = await ResolveRhLookupNavigationPropertyAsync(
                DashboardMaintenanceTableLogicalName,
                DashboardMaintenanceClientField,
                DashboardMaintenanceClientField,
                httpContext.User,
                ct);
            payload[$"{clientNavigationProperty}@odata.bind"] = $"/{ClientsEntitySetName}({clientId})";
        }

        var relativeUrl = isCreate
            ? $"/api/data/v9.2/{metadata.EntitySetName}"
            : $"/api/data/v9.2/{metadata.EntitySetName}({normalizedRecordId})";

        using var response = await SendDataversePayloadWithRepresentationAsync(
            relativeUrl,
            isCreate ? "POST" : "PATCH",
            payload,
            httpContext.User,
            ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var recordId = isCreate
            ? ExtractRhRecordId(response, body, metadata.PrimaryIdField)
            : normalizedRecordId;

        var record = await GetCopiersMaintenanceRowByIdAsync(metadata, recordId, httpContext.User, ct);
        return new CopiersMaintenanceSaveResultDto
        {
            Message = isCreate
                ? "Mantenimiento creado correctamente."
                : "Mantenimiento actualizado correctamente.",
            Record = BuildMaintenanceRows(new[] { record }).First()
        };
    }

    public async Task<CopiersMaintenanceSaveResultDto> UploadCopiersMaintenanceAttachmentAsync(
        string maintenanceId,
        string fileName,
        string contentType,
        byte[] content,
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
        var currentUser = await GetCurrentUserAsync(ct);
        var current = await GetCopiersMaintenanceRowByIdAsync(metadata, normalizedMaintenanceId, httpContext.User, ct);
        if (!string.Equals(
            NormalizeOptionalGuid(current.TechnicianId),
            NormalizeOptionalGuid(currentUser?.SystemUserId),
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("El mantenimiento seleccionado no pertenece al owner autenticado.");
        }

        await UploadCopiersFileColumnAsync(
            metadata,
            normalizedMaintenanceId,
            DashboardMaintenanceAttachmentField,
            fileName,
            contentType,
            content,
            httpContext.User,
            ct);

        var record = await GetCopiersMaintenanceRowByIdAsync(metadata, normalizedMaintenanceId, httpContext.User, ct);
        return new CopiersMaintenanceSaveResultDto
        {
            Message = "Reporte adjuntado correctamente.",
            Record = BuildMaintenanceRows(new[] { record }).First()
        };
    }

    public async Task<CopiersSupplyInventoryDto> GetCopiersSupplyInventoryAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveCopiersSupplyMetadataAsync(httpContext.User, ct);
        var rows = await LoadCopiersSupplyRowsAsync(metadata, httpContext.User, ct);
        rows = await SyncExhaustedSupplyStatusesAsync(metadata, rows, httpContext.User, ct);

        return new CopiersSupplyInventoryDto
        {
            Records = rows,
            StatusOptions = CopiersSupplyStatusOptions
        };
    }

    public async Task<CopiersSupplyQuantityUpdateResultDto> UpdateCopiersSupplyQuantityAsync(
        CopiersSupplyQuantityUpdateRequestDto request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveCopiersSupplyMetadataAsync(httpContext.User, ct);
        var supplyId = NormalizeGuid(request.RecordId, nameof(request.RecordId));
        var quantity = RoundCurrency(request.Quantity);
        if (quantity < 0m)
            throw new InvalidOperationException("La cantidad del suministro no puede ser negativa.");

        await UpdateCopiersSupplyInventoryAsync(
            metadata,
            supplyId,
            quantity,
            updateLastPurchaseDate: false,
            httpContext.User,
            ct);

        var supply = await GetCopiersSupplyByIdAsync(metadata, supplyId, httpContext.User, ct);
        return new CopiersSupplyQuantityUpdateResultDto
        {
            Message = "Cantidad del suministro actualizada correctamente.",
            Supply = supply
        };
    }

    public async Task<IReadOnlyList<CopiersLookupItemDto>> GetCopiersSupplyLookupAsync(
        string? query = null,
        int top = 100,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveCopiersSupplyMetadataAsync(httpContext.User, ct);
        var rows = await LoadCopiersSupplyRowsAsync(metadata, httpContext.User, ct);
        var normalizedQuery = NormalizeCopiersComparableValue(query);

        return rows
            .Where(row => string.IsNullOrWhiteSpace(normalizedQuery)
                || NormalizeCopiersComparableValue(row.Name).Contains(normalizedQuery, StringComparison.Ordinal))
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(top, 1, 500))
            .Select(row => new CopiersLookupItemDto
            {
                Id = row.RecordId,
                Label = row.Name,
                SecondaryLabel = $"{row.Quantity.ToString("N2", CopiersCulture)} disponibles",
                Quantity = row.Quantity,
                StatusValue = row.StatusValue,
                StatusLabel = row.StatusLabel
            })
            .ToList();
    }

    public async Task<CopiersSupplierInvoiceBoardDto> GetCopiersPendingSupplierInvoicesAsync(CancellationToken ct = default)
    {
        var board = await GetCopiersSupplierInvoicesAsync(ct);
        return new CopiersSupplierInvoiceBoardDto
        {
            Records = board.Records
                .Where(static row => row.ApprovedValue == CopiersSupplierInvoiceApprovedNo)
                .ToList(),
            SupplyOptions = board.SupplyOptions
        };
    }

    public async Task<CopiersApproveSupplierInvoiceResultDto> ApproveCopiersSupplierInvoiceAsync(
        string invoiceId,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var invoiceMetadata = await ResolveCopiersSupplierInvoiceMetadataAsync(httpContext.User, ct);
        var supplyMetadata = await ResolveCopiersSupplyMetadataAsync(httpContext.User, ct);
        var normalizedInvoiceId = NormalizeGuid(invoiceId, nameof(invoiceId));
        var invoice = await GetCopiersSupplierInvoiceByIdAsync(invoiceMetadata, normalizedInvoiceId, httpContext.User, ct);

        if (invoice.ApprovedValue == CopiersSupplierInvoiceApprovedYes)
            throw new InvalidOperationException("Este ingreso ya fue aprobado.");

        if (string.IsNullOrWhiteSpace(invoice.SupplyId))
            throw new InvalidOperationException("La factura seleccionada no tiene suministro relacionado.");

        var supply = await GetCopiersSupplyByIdAsync(supplyMetadata, invoice.SupplyId, httpContext.User, ct);
        var newQuantity = RoundCurrency(supply.Quantity + invoice.Quantity);
        await UpdateCopiersSupplyInventoryAsync(
            supplyMetadata,
            supply.RecordId,
            newQuantity,
            updateLastPurchaseDate: true,
            httpContext.User,
            ct);

        await CallDataverseSendAsync(
            $"/api/data/v9.2/{invoiceMetadata.EntitySetName}({normalizedInvoiceId})",
            "PATCH",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                [CopiersSupplierInvoiceApprovedField] = CopiersSupplierInvoiceApprovedYes
            },
            httpContext.User,
            ct);

        var updatedSupply = await GetCopiersSupplyByIdAsync(supplyMetadata, supply.RecordId, httpContext.User, ct);
        var updatedInvoice = await GetCopiersSupplierInvoiceByIdAsync(invoiceMetadata, normalizedInvoiceId, httpContext.User, ct);

        return new CopiersApproveSupplierInvoiceResultDto
        {
            Message = "Ingreso aprobado y sumado al inventario.",
            Supply = updatedSupply,
            Invoice = updatedInvoice
        };
    }

    public async Task<CopiersDeliveryBoardDto> GetCopiersDeliveriesAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var currentUser = await GetCurrentUserAsync(ct);

        if (string.IsNullOrWhiteSpace(currentUser?.SystemUserId))
            throw new InvalidOperationException("No fue posible identificar el owner autenticado.");

        var metadata = await ResolveCopiersDeliveryMetadataAsync(httpContext.User, ct);
        return new CopiersDeliveryBoardDto
        {
            Records = await LoadCopiersDeliveryRowsAsync(metadata, currentUser.SystemUserId, httpContext.User, ct),
            StatusOptions = CopiersDeliveryStatusOptions
        };
    }

    public async Task<CopiersDeliverySaveResultDto> SaveCopiersDeliveryAsync(
        CopiersDeliverySaveRequestDto request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var deliveryMetadata = await ResolveCopiersDeliveryMetadataAsync(httpContext.User, ct);
        var supplyMetadata = await ResolveCopiersSupplyMetadataAsync(httpContext.User, ct);
        var normalizedRecordId = NormalizeOptionalGuid(request.RecordId);
        var isCreate = string.IsNullOrWhiteSpace(normalizedRecordId);
        var currentUser = await GetCurrentUserAsync(ct);
        CopiersDeliveryRowDto? currentDelivery = null;
        if (!isCreate)
        {
            if (string.IsNullOrWhiteSpace(currentUser?.SystemUserId))
                throw new InvalidOperationException("No fue posible identificar el owner autenticado.");

            currentDelivery = await GetCopiersDeliveryByIdAsync(
                deliveryMetadata,
                normalizedRecordId,
                currentUser.SystemUserId,
                httpContext.User,
                ct);
        }

        var deliveryDate = ParseCopiersRequiredDate(request.DeliveryDateValue, "fecha de entrega");
        var quantity = RoundCurrency(request.QuantityDelivered);
        if (quantity <= 0m)
            throw new InvalidOperationException("La cantidad entregada debe ser mayor a cero.");

        var supplyId = NormalizeGuid(request.SupplyId, nameof(request.SupplyId));
        var supply = await GetCopiersSupplyByIdAsync(supplyMetadata, supplyId, httpContext.User, ct);
        var previousQuantity = currentDelivery is null ? 0m : RoundCurrency(currentDelivery.QuantityDelivered);
        var previousSupplyId = NormalizeOptionalGuid(currentDelivery?.SupplyId);
        var sameSupply = !isCreate
            && string.Equals(previousSupplyId, supplyId, StringComparison.OrdinalIgnoreCase);
        CopiersSupplyRowDto? previousSupply = null;

        if (isCreate)
        {
            if (supply.Quantity < quantity)
                throw new InvalidOperationException($"No hay inventario suficiente. Disponible actual: {supply.Quantity.ToString("N2", CopiersCulture)}.");
        }
        else if (sameSupply)
        {
            var additionalQuantity = RoundCurrency(quantity - previousQuantity);
            if (additionalQuantity > 0m && supply.Quantity < additionalQuantity)
                throw new InvalidOperationException($"No hay inventario suficiente. Disponible actual: {supply.Quantity.ToString("N2", CopiersCulture)}.");
        }
        else
        {
            if (supply.Quantity < quantity)
                throw new InvalidOperationException($"No hay inventario suficiente. Disponible actual: {supply.Quantity.ToString("N2", CopiersCulture)}.");

            if (!string.IsNullOrWhiteSpace(previousSupplyId))
                previousSupply = await GetCopiersSupplyByIdAsync(supplyMetadata, previousSupplyId, httpContext.User, ct);
        }

        var clientId = NormalizeOptionalGuid(request.ClientId);
        if (string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(request.ClientName))
            clientId = await ResolveCopiersClientIdAsync(request.ClientName.Trim(), ct);
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("Debes seleccionar un cliente valido.");

        var clientName = FirstNonEmpty(request.ClientName?.Trim(), "Cliente");
        var status = NormalizeCopiersDeliveryStatus(request.StatusValue);
        var title = $"Entrega - {clientName} - {supply.Name} - {deliveryDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}";

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [CopiersDeliveryDateField] = deliveryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            [CopiersDeliveryQuantityField] = quantity,
            [CopiersDeliveryStatusField] = status
        };
        if (!string.IsNullOrWhiteSpace(deliveryMetadata.PrimaryNameField))
            payload[deliveryMetadata.PrimaryNameField] = title;

        var clientNavigationProperty = await ResolveRhLookupNavigationPropertyAsync(
            CopiersDeliveryLogicalName,
            CopiersDeliveryClientField,
            CopiersDeliveryClientField,
            httpContext.User,
            ct);
        var supplyNavigationProperty = await ResolveRhLookupNavigationPropertyAsync(
            CopiersDeliveryLogicalName,
            CopiersDeliverySupplyField,
            CopiersDeliverySupplyField,
            httpContext.User,
            ct);

        payload[$"{clientNavigationProperty}@odata.bind"] = $"/{ClientsEntitySetName}({clientId})";
        payload[$"{supplyNavigationProperty}@odata.bind"] = $"/{supplyMetadata.EntitySetName}({supplyId})";

        var relativeUrl = isCreate
            ? $"/api/data/v9.2/{deliveryMetadata.EntitySetName}"
            : $"/api/data/v9.2/{deliveryMetadata.EntitySetName}({normalizedRecordId})";

        using var response = await SendDataversePayloadWithRepresentationAsync(
            relativeUrl,
            isCreate ? "POST" : "PATCH",
            payload,
            httpContext.User,
            ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var recordId = isCreate
            ? ExtractRhRecordId(response, body, deliveryMetadata.PrimaryIdField)
            : normalizedRecordId;

        if (isCreate)
        {
            await UpdateCopiersSupplyInventoryAsync(
                supplyMetadata,
                supply.RecordId,
                RoundCurrency(supply.Quantity - quantity),
                updateLastPurchaseDate: false,
                httpContext.User,
                ct);
        }
        else if (sameSupply)
        {
            await UpdateCopiersSupplyInventoryAsync(
                supplyMetadata,
                supply.RecordId,
                RoundCurrency(supply.Quantity + previousQuantity - quantity),
                updateLastPurchaseDate: false,
                httpContext.User,
                ct);
        }
        else
        {
            if (previousSupply is not null)
            {
                await UpdateCopiersSupplyInventoryAsync(
                    supplyMetadata,
                    previousSupply.RecordId,
                    RoundCurrency(previousSupply.Quantity + previousQuantity),
                    updateLastPurchaseDate: false,
                    httpContext.User,
                    ct);
            }

            await UpdateCopiersSupplyInventoryAsync(
                supplyMetadata,
                supply.RecordId,
                RoundCurrency(supply.Quantity - quantity),
                updateLastPurchaseDate: false,
                httpContext.User,
                ct);
        }

        var delivery = await GetCopiersDeliveryByIdAsync(
            deliveryMetadata,
            recordId,
            currentUser?.SystemUserId,
            httpContext.User,
            ct);
        var updatedSupply = await GetCopiersSupplyByIdAsync(supplyMetadata, supply.RecordId, httpContext.User, ct);

        return new CopiersDeliverySaveResultDto
        {
            Message = isCreate
                ? "Entrega registrada y descontada del inventario."
                : "Entrega actualizada e inventario ajustado correctamente.",
            Record = delivery,
            Supply = updatedSupply
        };
    }

    public async Task<CopiersDeliverySaveResultDto> UploadCopiersDeliveryAttachmentAsync(
        string deliveryId,
        string fileName,
        string contentType,
        byte[] content,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveCopiersDeliveryMetadataAsync(httpContext.User, ct);
        var normalizedDeliveryId = NormalizeGuid(deliveryId, nameof(deliveryId));
        var currentUser = await GetCurrentUserAsync(ct);
        _ = await GetCopiersDeliveryByIdAsync(
            metadata,
            normalizedDeliveryId,
            currentUser?.SystemUserId,
            httpContext.User,
            ct);

        await UploadCopiersFileColumnAsync(
            metadata,
            normalizedDeliveryId,
            CopiersDeliveryAttachmentField,
            fileName,
            contentType,
            content,
            httpContext.User,
            ct);

        var record = await GetCopiersDeliveryByIdAsync(
            metadata,
            normalizedDeliveryId,
            currentUser?.SystemUserId,
            httpContext.User,
            ct);
        return new CopiersDeliverySaveResultDto
        {
            Message = "Comprobante adjuntado correctamente.",
            Record = record
        };
    }

    public async Task<RhFileDownloadResult?> DownloadCopiersDeliveryAttachmentAsync(string deliveryId, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveCopiersDeliveryMetadataAsync(httpContext.User, ct);
        var currentUser = await GetCurrentUserAsync(ct);
        _ = await GetCopiersDeliveryByIdAsync(
            metadata,
            deliveryId,
            currentUser?.SystemUserId,
            httpContext.User,
            ct);

        return await DownloadCopiersFileColumnAsync(
            metadata,
            NormalizeGuid(deliveryId, nameof(deliveryId)),
            CopiersDeliveryAttachmentField,
            "comprobante-entrega",
            httpContext.User,
            ct);
    }

    public async Task<CopiersSupplierInvoiceBoardDto> GetCopiersSupplierInvoicesAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var invoiceMetadata = await ResolveCopiersSupplierInvoiceMetadataAsync(httpContext.User, ct);
        var rows = await LoadCopiersSupplierInvoiceRowsAsync(invoiceMetadata, httpContext.User, ct);
        return new CopiersSupplierInvoiceBoardDto
        {
            Records = rows,
            SupplyOptions = await GetCopiersSupplyLookupAsync(top: 500, ct: ct)
        };
    }

    public async Task<CopiersSupplierInvoiceBatchCreateResultDto> CreateCopiersSupplierInvoicesAsync(
        CopiersSupplierInvoiceBatchCreateRequestDto request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var invoiceNumber = request.InvoiceNumber?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(invoiceNumber))
            throw new InvalidOperationException("Debes indicar el numero de factura.");

        var lines = request.Lines
            .Where(static line => !string.IsNullOrWhiteSpace(line.SupplyId))
            .ToList();
        if (lines.Count == 0)
            throw new InvalidOperationException("Debes agregar al menos una linea de suministro.");

        var invoiceMetadata = await ResolveCopiersSupplierInvoiceMetadataAsync(httpContext.User, ct);
        var supplyMetadata = await ResolveCopiersSupplyMetadataAsync(httpContext.User, ct);
        var supplyNavigationProperty = await ResolveRhLookupNavigationPropertyAsync(
            CopiersSupplierInvoiceLogicalName,
            CopiersSupplierInvoiceSupplyField,
            CopiersSupplierInvoiceSupplyField,
            httpContext.User,
            ct);

        var created = new List<CopiersSupplierInvoiceRowDto>();
        foreach (var line in lines)
        {
            var supplyId = NormalizeGuid(line.SupplyId, nameof(line.SupplyId));
            var quantity = RoundCurrency(line.Quantity);
            if (quantity <= 0m)
                throw new InvalidOperationException("Todas las cantidades deben ser mayores a cero.");

            var unitValueBeforeVat = RoundCurrency(line.UnitValueBeforeVat);
            if (unitValueBeforeVat <= 0m)
                throw new InvalidOperationException("Todos los valores unitarios deben ser mayores a cero.");

            _ = await GetCopiersSupplyByIdAsync(supplyMetadata, supplyId, httpContext.User, ct);
            var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                [CopiersSupplierInvoiceNumberField] = invoiceNumber,
                [CopiersSupplierInvoiceQuantityField] = quantity,
                [CopiersSupplierInvoiceUnitValueBeforeVatField] = unitValueBeforeVat,
                [CopiersSupplierInvoiceApprovedField] = CopiersSupplierInvoiceApprovedNo,
                [$"{supplyNavigationProperty}@odata.bind"] = $"/{supplyMetadata.EntitySetName}({supplyId})"
            };
            if (!string.IsNullOrWhiteSpace(invoiceMetadata.PrimaryNameField)
                && !payload.ContainsKey(invoiceMetadata.PrimaryNameField))
            {
                payload[invoiceMetadata.PrimaryNameField] = invoiceNumber;
            }

            using var response = await SendDataversePayloadWithRepresentationAsync(
                $"/api/data/v9.2/{invoiceMetadata.EntitySetName}",
                "POST",
                payload,
                httpContext.User,
                ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var createdId = ExtractRhRecordId(response, body, invoiceMetadata.PrimaryIdField);
            created.Add(await GetCopiersSupplierInvoiceByIdAsync(invoiceMetadata, createdId, httpContext.User, ct));
        }

        return new CopiersSupplierInvoiceBatchCreateResultDto
        {
            Message = created.Count == 1
                ? "Se registro 1 linea de factura."
                : $"Se registraron {created.Count} lineas de factura.",
            Records = created
        };
    }

    private async Task<List<CopiersMaintenanceRecordRow>> GetCopiersMaintenanceRowsForCurrentOwnerAsync(
        RhEntityMetadata metadata,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var currentUser = await GetCurrentUserAsync(ct);
        if (string.IsNullOrWhiteSpace(currentUser?.SystemUserId))
            throw new InvalidOperationException("No fue posible identificar el owner autenticado.");

        var equipmentLookupFieldCandidates = await ResolveCopiersMaintenanceEquipmentLookupFieldCandidatesAsync(user, ct);
        InvalidOperationException? lastLookupException = null;

        foreach (var equipmentLookupField in equipmentLookupFieldCandidates)
        {
            try
            {
                return await GetCopiersMaintenanceRowsForCurrentOwnerCoreAsync(
                    metadata,
                    currentUser.SystemUserId,
                    equipmentLookupField,
                    user,
                    ct);
            }
            catch (InvalidOperationException ex) when (ShouldRetryCopiersMaintenanceLookupQuery(ex, equipmentLookupField))
            {
                lastLookupException = ex;
                _logger.LogWarning(
                    ex,
                    "Fallo la consulta de mis mantenimientos usando el lookup {LookupField}.",
                    equipmentLookupField);
            }
        }

        if (lastLookupException is not null)
            throw lastLookupException;

        return await GetCopiersMaintenanceRowsForCurrentOwnerCoreAsync(
            metadata,
            currentUser.SystemUserId,
            DashboardMaintenanceEquipmentField,
            user,
            ct);
    }

    private async Task<List<CopiersMaintenanceRecordRow>> GetCopiersMaintenanceRowsForCurrentOwnerCoreAsync(
        RhEntityMetadata metadata,
        string ownerId,
        string equipmentLookupField,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var filter = $"{BuildDashboardLookupValuePropertyName(DashboardMaintenanceOwnerField)} eq {NormalizeGuid(ownerId, nameof(ownerId))}";
        var relativeUrl =
            $"/api/data/v9.2/{metadata.EntitySetName}?$select={BuildMaintenanceSelectClause(metadata, equipmentLookupField)}" +
            $"&$filter={Uri.EscapeDataString(filter)}" +
            $"&$orderby={DashboardMaintenanceDateField} desc";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        return items
            .Select(item => ParseMaintenanceRecord(item, metadata.PrimaryIdField, metadata.PrimaryNameField, equipmentLookupField))
            .Where(item => item is not null)
            .Cast<CopiersMaintenanceRecordRow>()
            .ToList();
    }

    private async Task<CopiersMaintenanceRecordRow> GetCopiersMaintenanceRowByIdAsync(
        RhEntityMetadata metadata,
        string recordId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var equipmentLookupFieldCandidates = await ResolveCopiersMaintenanceEquipmentLookupFieldCandidatesAsync(user, ct);
        InvalidOperationException? lastLookupException = null;

        foreach (var equipmentLookupField in equipmentLookupFieldCandidates)
        {
            try
            {
                var relativeUrl =
                    $"/api/data/v9.2/{metadata.EntitySetName}({normalizedRecordId})" +
                    $"?$select={BuildMaintenanceSelectClause(metadata, equipmentLookupField)}";
                var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
                using var doc = JsonDocument.Parse(json);
                var row = ParseMaintenanceRecord(doc.RootElement, metadata.PrimaryIdField, metadata.PrimaryNameField, equipmentLookupField);
                if (row is not null)
                    return row;
            }
            catch (InvalidOperationException ex) when (ShouldRetryCopiersMaintenanceLookupQuery(ex, equipmentLookupField))
            {
                lastLookupException = ex;
            }
        }

        throw new InvalidOperationException("No fue posible reconstruir el mantenimiento guardado.", lastLookupException);
    }

    private static int? NormalizeCopiersMaintenanceType(int? value)
    {
        if (!value.HasValue)
            return null;

        if (!DashboardMaintenanceTypeLabels.ContainsKey(value.Value))
            throw new InvalidOperationException("El tipo de mantenimiento seleccionado no es valido.");

        return value.Value;
    }

    private async Task<RhEntityMetadata> ResolveCopiersSupplyMetadataAsync(ClaimsPrincipal user, CancellationToken ct) =>
        await ResolveRhEntityMetadataAsync(
            CopiersSupplyLogicalName,
            CopiersSupplyFallbackEntitySetName,
            CopiersSupplyFallbackIdField,
            CopiersSupplyFallbackPrimaryNameField,
            user,
            ct);

    private async Task<RhEntityMetadata> ResolveCopiersSupplierInvoiceMetadataAsync(ClaimsPrincipal user, CancellationToken ct) =>
        await ResolveRhEntityMetadataAsync(
            CopiersSupplierInvoiceLogicalName,
            CopiersSupplierInvoiceFallbackEntitySetName,
            CopiersSupplierInvoiceFallbackIdField,
            CopiersSupplierInvoiceFallbackPrimaryNameField,
            user,
            ct);

    private async Task<RhEntityMetadata> ResolveCopiersDeliveryMetadataAsync(ClaimsPrincipal user, CancellationToken ct) =>
        await ResolveRhEntityMetadataAsync(
            CopiersDeliveryLogicalName,
            CopiersDeliveryFallbackEntitySetName,
            CopiersDeliveryFallbackIdField,
            CopiersDeliveryFallbackPrimaryNameField,
            user,
            ct);

    private async Task<List<CopiersSupplyRowDto>> LoadCopiersSupplyRowsAsync(
        RhEntityMetadata metadata,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var relativeUrl =
            $"/api/data/v9.2/{metadata.EntitySetName}?$select={BuildCopiersSupplySelectClause(metadata)}" +
            $"&$orderby={CopiersSupplyNameField} asc";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        return items
            .Select(item => BuildCopiersSupplyRow(metadata, item))
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildCopiersSupplySelectClause(RhEntityMetadata metadata)
    {
        return string.Join(",",
            new[]
            {
                metadata.PrimaryIdField,
                metadata.PrimaryNameField,
                CopiersSupplyNameField,
                CopiersSupplyQuantityField,
                CopiersSupplyLastPurchaseDateField,
                CopiersSupplyStatusField
            }
            .Where(static field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private CopiersSupplyRowDto? BuildCopiersSupplyRow(RhEntityMetadata metadata, JsonElement item)
    {
        var recordId = FirstNonEmpty(
            ReadString(item, metadata.PrimaryIdField),
            ReadString(item, CopiersSupplyFallbackIdField));
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var date = ReadDateOnly(item, CopiersSupplyLastPurchaseDateField);
        var rawDate = ReadString(item, CopiersSupplyLastPurchaseDateField);
        var statusValue = ReadIntFlexible(item, CopiersSupplyStatusField);

        return new CopiersSupplyRowDto
        {
            RecordId = recordId.Trim(),
            Name = FirstNonEmpty(
                ReadString(item, CopiersSupplyNameField).Trim(),
                ReadString(item, metadata.PrimaryNameField).Trim(),
                "Suministro sin nombre"),
            Quantity = RoundCurrency(ReadDecimal(item, CopiersSupplyQuantityField) ?? 0m),
            LastPurchaseDateValue = date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? rawDate,
            LastPurchaseDateDisplay = date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? rawDate,
            StatusValue = statusValue > 0 ? statusValue : null,
            StatusLabel = ResolveDashboardOptionLabel(
                item,
                CopiersSupplyStatusField,
                statusValue,
                CopiersSupplyStatusOptions.ToDictionary(option => option.Value, option => option.Label),
                statusValue == CopiersSupplyStatusExhausted ? "Agotado" : "Disponible")
        };
    }

    private async Task<List<CopiersSupplyRowDto>> SyncExhaustedSupplyStatusesAsync(
        RhEntityMetadata metadata,
        List<CopiersSupplyRowDto> rows,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var result = new List<CopiersSupplyRowDto>(rows.Count);
        foreach (var row in rows)
        {
            if (row.Quantity == 0m && row.StatusValue != CopiersSupplyStatusExhausted)
            {
                await CallDataverseSendAsync(
                    $"/api/data/v9.2/{metadata.EntitySetName}({NormalizeGuid(row.RecordId, nameof(row.RecordId))})",
                    "PATCH",
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        [CopiersSupplyStatusField] = CopiersSupplyStatusExhausted
                    },
                    user,
                    ct);

                row.StatusValue = CopiersSupplyStatusExhausted;
                row.StatusLabel = "Agotado";
            }

            result.Add(row);
        }

        return result;
    }

    private async Task<CopiersSupplyRowDto> GetCopiersSupplyByIdAsync(
        RhEntityMetadata metadata,
        string supplyId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var normalizedSupplyId = NormalizeGuid(supplyId, nameof(supplyId));
        var relativeUrl =
            $"/api/data/v9.2/{metadata.EntitySetName}({normalizedSupplyId})" +
            $"?$select={BuildCopiersSupplySelectClause(metadata)}";
        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        using var doc = JsonDocument.Parse(json);
        return BuildCopiersSupplyRow(metadata, doc.RootElement)
            ?? throw new InvalidOperationException("No fue posible leer el suministro seleccionado.");
    }

    private async Task UpdateCopiersSupplyInventoryAsync(
        RhEntityMetadata metadata,
        string supplyId,
        decimal quantity,
        bool updateLastPurchaseDate,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        quantity = RoundCurrency(quantity);
        if (quantity < 0m)
            throw new InvalidOperationException("La cantidad del suministro no puede quedar negativa.");

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [CopiersSupplyQuantityField] = quantity,
            [CopiersSupplyStatusField] = quantity == 0m
                ? CopiersSupplyStatusExhausted
                : CopiersSupplyStatusAvailable
        };

        if (updateLastPurchaseDate)
            payload[CopiersSupplyLastPurchaseDateField] = GetBogotaToday().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        await CallDataverseSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({NormalizeGuid(supplyId, nameof(supplyId))})",
            "PATCH",
            payload,
            user,
            ct);
    }

    private async Task<List<CopiersSupplierInvoiceRowDto>> LoadCopiersSupplierInvoiceRowsAsync(
        RhEntityMetadata metadata,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var relativeUrl =
            $"/api/data/v9.2/{metadata.EntitySetName}?$select={BuildCopiersSupplierInvoiceSelectClause(metadata)}" +
            $"&$orderby={CopiersSupplierInvoiceNumberField} asc";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        return items
            .Select(item => BuildCopiersSupplierInvoiceRow(metadata, item))
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderBy(item => item.ApprovedValue)
            .ThenBy(item => item.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SupplyName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildCopiersSupplierInvoiceSelectClause(RhEntityMetadata metadata)
    {
        return string.Join(",",
            new[]
            {
                metadata.PrimaryIdField,
                metadata.PrimaryNameField,
                CopiersSupplierInvoiceNumberField,
                BuildDashboardLookupValuePropertyName(CopiersSupplierInvoiceSupplyField),
                CopiersSupplierInvoiceQuantityField,
                CopiersSupplierInvoiceUnitValueBeforeVatField,
                CopiersSupplierInvoiceApprovedField
            }
            .Where(static field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private CopiersSupplierInvoiceRowDto? BuildCopiersSupplierInvoiceRow(RhEntityMetadata metadata, JsonElement item)
    {
        var recordId = FirstNonEmpty(
            ReadString(item, metadata.PrimaryIdField),
            ReadString(item, CopiersSupplierInvoiceFallbackIdField));
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var approvedValue = ReadCopiersYesNoValue(item, CopiersSupplierInvoiceApprovedField);
        return new CopiersSupplierInvoiceRowDto
        {
            RecordId = recordId.Trim(),
            InvoiceNumber = FirstNonEmpty(
                ReadString(item, CopiersSupplierInvoiceNumberField).Trim(),
                ReadString(item, metadata.PrimaryNameField).Trim(),
                "Sin numero"),
            SupplyId = ReadCopiersLookupId(item, CopiersSupplierInvoiceSupplyField, "suministro"),
            SupplyName = ReadCopiersFieldDisplayValue(
                item,
                CopiersSupplierInvoiceSupplyField,
                "suministro",
                "Suministro sin nombre"),
            Quantity = RoundCurrency(ReadDecimal(item, CopiersSupplierInvoiceQuantityField) ?? 0m),
            UnitValueBeforeVat = RoundCurrency(ReadDecimal(item, CopiersSupplierInvoiceUnitValueBeforeVatField) ?? 0m),
            ApprovedValue = approvedValue,
            ApprovedLabel = FirstNonEmpty(
                ReadString(item, $"{CopiersSupplierInvoiceApprovedField}{FormattedValueAnnotationSuffix}"),
                approvedValue == CopiersSupplierInvoiceApprovedYes ? "Si" : "No")
        };
    }

    private async Task<CopiersSupplierInvoiceRowDto> GetCopiersSupplierInvoiceByIdAsync(
        RhEntityMetadata metadata,
        string invoiceId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var normalizedInvoiceId = NormalizeGuid(invoiceId, nameof(invoiceId));
        var relativeUrl =
            $"/api/data/v9.2/{metadata.EntitySetName}({normalizedInvoiceId})" +
            $"?$select={BuildCopiersSupplierInvoiceSelectClause(metadata)}";
        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        using var doc = JsonDocument.Parse(json);
        return BuildCopiersSupplierInvoiceRow(metadata, doc.RootElement)
            ?? throw new InvalidOperationException("No fue posible leer la factura seleccionada.");
    }

    private async Task<List<CopiersDeliveryRowDto>> LoadCopiersDeliveryRowsAsync(
        RhEntityMetadata metadata,
        string ownerId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var filter = $"{BuildDashboardLookupValuePropertyName(DashboardMaintenanceOwnerField)} eq {NormalizeGuid(ownerId, nameof(ownerId))}";
        var relativeUrl =
            $"/api/data/v9.2/{metadata.EntitySetName}?$select={BuildCopiersDeliverySelectClause(metadata)}" +
            $"&$filter={Uri.EscapeDataString(filter)}" +
            $"&$orderby={CopiersDeliveryDateField} desc";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        return items
            .Select(item => BuildCopiersDeliveryRow(metadata, item))
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderByDescending(item => item.DeliveryDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ClientName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildCopiersDeliverySelectClause(RhEntityMetadata metadata)
    {
        return string.Join(",",
            new[]
            {
                metadata.PrimaryIdField,
                metadata.PrimaryNameField,
                BuildDashboardLookupValuePropertyName(CopiersDeliveryClientField),
                BuildDashboardLookupValuePropertyName(CopiersDeliverySupplyField),
                CopiersDeliveryDateField,
                CopiersDeliveryQuantityField,
                CopiersDeliveryStatusField,
                CopiersDeliveryAttachmentField,
                CopiersDeliveryAttachmentNameField,
                BuildDashboardLookupValuePropertyName(DashboardMaintenanceOwnerField)
            }
            .Where(static field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private CopiersDeliveryRowDto? BuildCopiersDeliveryRow(RhEntityMetadata metadata, JsonElement item)
    {
        var recordId = FirstNonEmpty(
            ReadString(item, metadata.PrimaryIdField),
            ReadString(item, CopiersDeliveryFallbackIdField));
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var date = ReadDateOnly(item, CopiersDeliveryDateField);
        var rawDate = ReadString(item, CopiersDeliveryDateField);
        var statusValue = ReadIntFlexible(item, CopiersDeliveryStatusField);
        var attachmentToken = ReadString(item, CopiersDeliveryAttachmentField);
        var attachmentName = ReadString(item, CopiersDeliveryAttachmentNameField);
        var ownerLookupProperty = BuildDashboardLookupValuePropertyName(DashboardMaintenanceOwnerField);

        return new CopiersDeliveryRowDto
        {
            RecordId = recordId.Trim(),
            ClientId = ReadCopiersLookupId(item, CopiersDeliveryClientField, "cliente"),
            ClientName = ReadCopiersFieldDisplayValue(item, CopiersDeliveryClientField, "cliente", "Cliente sin nombre"),
            SupplyId = ReadCopiersLookupId(item, CopiersDeliverySupplyField, "suministro"),
            SupplyName = ReadCopiersFieldDisplayValue(item, CopiersDeliverySupplyField, "suministro", "Suministro sin nombre"),
            DeliveryDateValue = date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? rawDate,
            DeliveryDateDisplay = date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? rawDate,
            QuantityDelivered = RoundCurrency(ReadDecimal(item, CopiersDeliveryQuantityField) ?? 0m),
            StatusValue = statusValue > 0 ? statusValue : null,
            StatusLabel = ResolveDashboardOptionLabel(
                item,
                CopiersDeliveryStatusField,
                statusValue,
                CopiersDeliveryStatusOptions.ToDictionary(option => option.Value, option => option.Label),
                "Pendiente"),
            HasAttachment = !string.IsNullOrWhiteSpace(attachmentToken) || !string.IsNullOrWhiteSpace(attachmentName),
            AttachmentFileName = FirstNonEmpty(attachmentName, "Comprobante de entrega"),
            OwnerId = ReadString(item, ownerLookupProperty).Trim(),
            OwnerName = FirstNonEmpty(ReadLookupFormattedValue(item, ownerLookupProperty), "Sin owner")
        };
    }

    private async Task<CopiersDeliveryRowDto> GetCopiersDeliveryByIdAsync(
        RhEntityMetadata metadata,
        string deliveryId,
        string? ownerId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var normalizedDeliveryId = NormalizeGuid(deliveryId, nameof(deliveryId));
        var relativeUrl =
            $"/api/data/v9.2/{metadata.EntitySetName}({normalizedDeliveryId})" +
            $"?$select={BuildCopiersDeliverySelectClause(metadata)}";
        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        using var doc = JsonDocument.Parse(json);
        var row = BuildCopiersDeliveryRow(metadata, doc.RootElement)
            ?? throw new InvalidOperationException("No fue posible reconstruir la entrega guardada.");

        if (!string.IsNullOrWhiteSpace(ownerId)
            && !string.Equals(NormalizeOptionalGuid(row.OwnerId), NormalizeOptionalGuid(ownerId), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("La entrega seleccionada no pertenece al owner autenticado.");
        }

        return row;
    }

    private static int NormalizeCopiersDeliveryStatus(int? value)
    {
        if (!value.HasValue)
            return CopiersDeliveryStatusCompleted;

        if (value.Value != CopiersDeliveryStatusCompleted && value.Value != CopiersDeliveryStatusPending)
            throw new InvalidOperationException("El estado de entrega seleccionado no es valido.");

        return value.Value;
    }

    private static DateOnly ParseCopiersRequiredDate(string? rawValue, string label)
    {
        if (!TryParseDateOnly(rawValue, out var parsed))
            throw new InvalidOperationException($"El valor de {label} debe ser una fecha valida.");

        return parsed;
    }

    private static int ReadCopiersYesNoValue(JsonElement item, string fieldName)
    {
        if (!item.TryGetProperty(fieldName, out var property))
            return CopiersSupplierInvoiceApprovedNo;

        return property.ValueKind switch
        {
            JsonValueKind.True => CopiersSupplierInvoiceApprovedYes,
            JsonValueKind.False => CopiersSupplierInvoiceApprovedNo,
            JsonValueKind.Number when property.TryGetInt32(out var numericValue) => numericValue,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var boolValue) => boolValue ? CopiersSupplierInvoiceApprovedYes : CopiersSupplierInvoiceApprovedNo,
            JsonValueKind.String when int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue) => intValue,
            _ => CopiersSupplierInvoiceApprovedNo
        };
    }

    private async Task<HttpResponseMessage> SendDataversePayloadWithRepresentationAsync(
        string relativeUrl,
        string method,
        object payload,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        var response = await CallRhDataverseResponseAsync(
            relativeUrl,
            method,
            user,
            ct,
            content,
            AddRhReturnRepresentationHeaders);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");

        response.Content = new StringContent(body, Encoding.UTF8, response.Content.Headers.ContentType?.MediaType ?? "application/json");
        return response;
    }

    private async Task UploadCopiersFileColumnAsync(
        RhEntityMetadata metadata,
        string recordId,
        string fieldName,
        string fileName,
        string contentType,
        byte[] content,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var safeFileName = SanitizeRhFileName(fileName, "archivo");
        ValidateCopiersAttachmentUpload(safeFileName, content);
        var headerFileName = BuildCopiersUploadHeaderFileName(safeFileName);

        using var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");

        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}({NormalizeGuid(recordId, nameof(recordId))})/{fieldName}";
        using var response = await CallRhDataverseResponseAsync(
            relativeUrl,
            "PATCH",
            user,
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
    }

    private async Task<RhFileDownloadResult?> DownloadCopiersFileColumnAsync(
        RhEntityMetadata metadata,
        string recordId,
        string fieldName,
        string fallbackFileName,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}({NormalizeGuid(recordId, nameof(recordId))})/{fieldName}/$value";
        using var response = await CallRhDataverseResponseAsync(relativeUrl, "GET", user, ct);
        if (response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.NotFound)
            return null;

        var bodyBytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var bodyText = bodyBytes.Length == 0 ? "" : Encoding.UTF8.GetString(bodyBytes);
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {bodyText}");
        }

        return new RhFileDownloadResult
        {
            FileName = FirstNonEmpty(
                ReadHeaderValue(response, "x-ms-file-name"),
                ReadHeaderValue(response, "filename"),
                $"{fallbackFileName}-{recordId}.bin"),
            ContentType =
                response.Content.Headers.ContentType?.MediaType
                ?? ReadHeaderValue(response, "mimetype")
                ?? "application/octet-stream",
            Content = bodyBytes
        };
    }

    private static void ValidateCopiersAttachmentUpload(string fileName, byte[] content)
    {
        if (content.Length == 0)
            throw new InvalidOperationException("El archivo seleccionado esta vacio.");

        if (content.Length > 128 * 1024 * 1024)
            throw new InvalidOperationException("El archivo supera el limite permitido de 128 MB.");

        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidOperationException("El archivo no tiene un nombre valido.");

        var extension = Path.GetExtension(fileName);
        if (!string.IsNullOrWhiteSpace(extension) && !CopiersAllowedAttachmentExtensions.Contains(extension))
            throw new InvalidOperationException("El tipo de archivo no esta permitido para este adjunto.");
    }

    private static string BuildCopiersUploadHeaderFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "archivo";

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
        return string.IsNullOrWhiteSpace(headerFileName) ? "archivo" : headerFileName;
    }

}
