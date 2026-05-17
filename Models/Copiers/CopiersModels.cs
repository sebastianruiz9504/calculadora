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
    public IReadOnlyList<CopiersOptionDto> StatusOptions { get; set; } = Array.Empty<CopiersOptionDto>();
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
    public int? MaintenanceStatusValue { get; set; }
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

public sealed class CopiersSupplyQuantityUpdateRequestDto
{
    public string RecordId { get; set; } = "";
    public decimal Quantity { get; set; }
}

public sealed class CopiersSupplyQuantityUpdateResultDto
{
    public string Message { get; set; } = "";
    public CopiersSupplyRowDto Supply { get; set; } = new();
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
    public decimal UnitValueBeforeVat { get; set; }
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
    public string RecordId { get; set; } = "";
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
    public decimal UnitValueBeforeVat { get; set; }
}

public sealed class CopiersSupplierInvoiceBatchCreateResultDto
{
    public string Message { get; set; } = "";
    public IReadOnlyList<CopiersSupplierInvoiceRowDto> Records { get; set; } = Array.Empty<CopiersSupplierInvoiceRowDto>();
}

public sealed class CopiersEquipmentInventoryDto
{
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string ClientContactName { get; set; } = "";
    public string ClientEmail { get; set; } = "";
    public string ClientPhone { get; set; } = "";
    public string ClientAddress { get; set; } = "";
    public string AsOfDateLabel { get; set; } = "";
    public bool HasData { get; set; }
    public int RecordsCount { get; set; }
    public int ContractedEquipmentCount { get; set; }
    public int AssignedToContractCount { get; set; }
    public int BackupEquipmentCount { get; set; }
    public int UnassignedEquipmentCount { get; set; }
    public bool HasInventoryMismatch { get; set; }
    public IReadOnlyList<CopiersEquipmentInventoryMetricDto> Kpis { get; set; } = Array.Empty<CopiersEquipmentInventoryMetricDto>();
    public IReadOnlyList<CopiersEquipmentInventoryContractLineDto> ContractLines { get; set; } = Array.Empty<CopiersEquipmentInventoryContractLineDto>();
    public IReadOnlyList<CopiersEquipmentInventoryIssueDto> Issues { get; set; } = Array.Empty<CopiersEquipmentInventoryIssueDto>();
    public IReadOnlyList<CopiersEquipmentInventoryLocationDto> Locations { get; set; } = Array.Empty<CopiersEquipmentInventoryLocationDto>();
    public IReadOnlyList<CopiersEquipmentInventoryRowDto> Records { get; set; } = Array.Empty<CopiersEquipmentInventoryRowDto>();
    public IReadOnlyList<CopiersEquipmentInventoryMissingColumnDto> MissingColumns { get; set; } = Array.Empty<CopiersEquipmentInventoryMissingColumnDto>();
}

public sealed class CopiersEquipmentInventoryMetricDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal Value { get; set; }
    public string SecondaryLabel { get; set; } = "";
    public string SecondaryValue { get; set; } = "";
}

public sealed class CopiersEquipmentInventoryLocationDto
{
    public string Key { get; set; } = "";
    public string Site { get; set; } = "";
    public string Address { get; set; } = "";
    public string MapUrl { get; set; } = "";
    public string MapEmbedUrl { get; set; } = "";
    public int EquipmentCount { get; set; }
    public IReadOnlyList<string> Areas { get; set; } = Array.Empty<string>();
}

public sealed class CopiersEquipmentInventoryContractLineDto
{
    public string LineId { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int BillingDay { get; set; }
    public string BillingDayDisplay { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal IncludedOperations { get; set; }
    public int ContractedEquipmentCount { get; set; }
    public int AssignedEquipmentCount { get; set; }
    public string AssignmentSummary { get; set; } = "";
    public IReadOnlyList<string> AssignedEquipmentSerials { get; set; } = Array.Empty<string>();
}

public sealed class CopiersEquipmentInventoryIssueDto
{
    public string Code { get; set; } = "";
    public string Severity { get; set; } = "error";
    public string Message { get; set; } = "";
}

public sealed class CopiersEquipmentInventoryRowDto
{
    public int LineNumber { get; set; }
    public string RecordId { get; set; } = "";
    public string Type { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Serial { get; set; } = "";
    public string Company { get; set; } = "";
    public string ClientContactName { get; set; } = "";
    public string ClientEmail { get; set; } = "";
    public string ClientPhone { get; set; } = "";
    public string ClientAddress { get; set; } = "";
    public string Area { get; set; } = "";
    public string Site { get; set; } = "";
    public string Address { get; set; } = "";
    public string MapUrl { get; set; } = "";
    public string MapEmbedUrl { get; set; } = "";
    public string Observations { get; set; } = "";
    public string ContractLineId { get; set; } = "";
    public string ContractLineName { get; set; } = "";
    public string BillingDayDisplay { get; set; } = "";
    public decimal IncludedOperations { get; set; }
    public bool IsBackup { get; set; }
    public string AssignmentStatus { get; set; } = "";
    public int MaintenanceCount { get; set; }
    public string LastMaintenanceDateDisplay { get; set; } = "";
}

public sealed class CopiersEquipmentBackupAssignmentRequestDto
{
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string EquipmentId { get; set; } = "";
    public bool IsBackup { get; set; }
}

public sealed class CopiersEquipmentBackupAssignmentResultDto
{
    public string Message { get; set; } = "";
    public CopiersEquipmentInventoryDto Inventory { get; set; } = new();
}

public sealed class CopiersEquipmentInventoryMissingColumnDto
{
    public string Label { get; set; } = "";
    public string LogicalName { get; set; } = "";
}
