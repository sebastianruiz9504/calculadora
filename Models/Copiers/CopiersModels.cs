using CotizadorInterno.Web.Models.Dashboard;

namespace CotizadorInterno.Web.Models.Copiers;

public sealed class CopiersPageViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
}

public sealed class CopiersInventoryPageViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
}

public sealed class CopiersOptionDto
{
    public int Value { get; set; }
    public string Label { get; set; } = "";
}

public sealed class CopiersLookupItemDto
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string SecondaryLabel { get; set; } = "";
    public decimal Quantity { get; set; }
    public int? StatusValue { get; set; }
    public string StatusLabel { get; set; } = "";
}

public sealed class CopiersMaintenanceBoardDto
{
    public IReadOnlyList<CopiersMaintenanceRowDto> Records { get; set; } = Array.Empty<CopiersMaintenanceRowDto>();
    public IReadOnlyList<CopiersOptionDto> TypeOptions { get; set; } = Array.Empty<CopiersOptionDto>();
}

public sealed class CopiersMaintenanceSaveRequestDto
{
    public string RecordId { get; set; } = "";
    public string Title { get; set; } = "";
    public string InternalId { get; set; } = "";
    public string EquipmentId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string DateValue { get; set; } = "";
    public string Description { get; set; } = "";
    public int? MaintenanceTypeValue { get; set; }
}

public sealed class CopiersMaintenanceSaveResultDto
{
    public string Message { get; set; } = "";
    public CopiersMaintenanceRowDto Record { get; set; } = new();
}

public sealed class CopiersSupplyInventoryDto
{
    public IReadOnlyList<CopiersSupplyRowDto> Records { get; set; } = Array.Empty<CopiersSupplyRowDto>();
    public IReadOnlyList<CopiersOptionDto> StatusOptions { get; set; } = Array.Empty<CopiersOptionDto>();
}

public sealed class CopiersSupplyRowDto
{
    public string RecordId { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Quantity { get; set; }
    public string LastPurchaseDateValue { get; set; } = "";
    public string LastPurchaseDateDisplay { get; set; } = "";
    public int? StatusValue { get; set; }
    public string StatusLabel { get; set; } = "";
}

public sealed class CopiersSupplierInvoiceBoardDto
{
    public IReadOnlyList<CopiersSupplierInvoiceRowDto> Records { get; set; } = Array.Empty<CopiersSupplierInvoiceRowDto>();
    public IReadOnlyList<CopiersLookupItemDto> SupplyOptions { get; set; } = Array.Empty<CopiersLookupItemDto>();
}

public sealed class CopiersSupplierInvoiceRowDto
{
    public string RecordId { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public string SupplyId { get; set; } = "";
    public string SupplyName { get; set; } = "";
    public decimal Quantity { get; set; }
    public int ApprovedValue { get; set; }
    public string ApprovedLabel { get; set; } = "";
}

public sealed class CopiersApproveSupplierInvoiceRequestDto
{
    public string InvoiceId { get; set; } = "";
}

public sealed class CopiersApproveSupplierInvoiceResultDto
{
    public string Message { get; set; } = "";
    public CopiersSupplyRowDto Supply { get; set; } = new();
    public CopiersSupplierInvoiceRowDto Invoice { get; set; } = new();
}

public sealed class CopiersDeliveryBoardDto
{
    public IReadOnlyList<CopiersDeliveryRowDto> Records { get; set; } = Array.Empty<CopiersDeliveryRowDto>();
    public IReadOnlyList<CopiersOptionDto> StatusOptions { get; set; } = Array.Empty<CopiersOptionDto>();
}

public sealed class CopiersDeliveryRowDto
{
    public string RecordId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string SupplyId { get; set; } = "";
    public string SupplyName { get; set; } = "";
    public string DeliveryDateValue { get; set; } = "";
    public string DeliveryDateDisplay { get; set; } = "";
    public decimal QuantityDelivered { get; set; }
    public int? StatusValue { get; set; }
    public string StatusLabel { get; set; } = "";
    public bool HasAttachment { get; set; }
    public string AttachmentFileName { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string OwnerName { get; set; } = "";
}

public sealed class CopiersDeliverySaveRequestDto
{
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string SupplyId { get; set; } = "";
    public string DeliveryDateValue { get; set; } = "";
    public decimal QuantityDelivered { get; set; }
    public int? StatusValue { get; set; }
}

public sealed class CopiersDeliverySaveResultDto
{
    public string Message { get; set; } = "";
    public CopiersDeliveryRowDto Record { get; set; } = new();
    public CopiersSupplyRowDto Supply { get; set; } = new();
}

public sealed class CopiersSupplierInvoiceBatchCreateRequestDto
{
    public string InvoiceNumber { get; set; } = "";
    public List<CopiersSupplierInvoiceLineInputDto> Lines { get; set; } = new();
}

public sealed class CopiersSupplierInvoiceLineInputDto
{
    public string SupplyId { get; set; } = "";
    public decimal Quantity { get; set; }
}

public sealed class CopiersSupplierInvoiceBatchCreateResultDto
{
    public string Message { get; set; } = "";
    public IReadOnlyList<CopiersSupplierInvoiceRowDto> Records { get; set; } = Array.Empty<CopiersSupplierInvoiceRowDto>();
}

