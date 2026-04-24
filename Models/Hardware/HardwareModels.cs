namespace CotizadorInterno.Web.Models.Hardware;

public sealed class HardwarePageViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
    public string TableLogicalName { get; set; } = "cr07a_hardware";
    public string TableDisplayName { get; set; } = "Hardware";
}

public sealed class HardwareStateOptionDto
{
    public int Value { get; set; }
    public string Label { get; set; } = "";
    public string Tone { get; set; } = "";
    public string ActionKey { get; set; } = "";
    public string ActionLabel { get; set; } = "";
    public bool HasAction { get; set; }
}

public sealed class HardwareStateSummaryDto
{
    public int Value { get; set; }
    public string Label { get; set; } = "";
    public string Tone { get; set; } = "";
    public int Count { get; set; }
}

public sealed class HardwareInvoiceLookupItemDto
{
    public string RecordId { get; set; } = "";
    public string Number { get; set; } = "";
    public string ClientName { get; set; } = "";
    public decimal PaymentValue { get; set; }
}

public sealed class HardwareBoardDto
{
    public string Message { get; set; } = "";
    public int TotalCount { get; set; }
    public int SyncedRequestsCount { get; set; }
    public int SyncedImportedCount { get; set; }
    public int? SelectedStateValue { get; set; }
    public IReadOnlyList<string> SyncMessages { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public IReadOnlyList<HardwareStateOptionDto> StateOptions { get; set; } = Array.Empty<HardwareStateOptionDto>();
    public IReadOnlyList<HardwareStateSummaryDto> StateSummaries { get; set; } = Array.Empty<HardwareStateSummaryDto>();
    public IReadOnlyList<HardwareBoardRowDto> Rows { get; set; } = Array.Empty<HardwareBoardRowDto>();
}

public sealed class HardwareBoardRowDto
{
    public string RecordId { get; set; } = "";
    public string Name { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public int Quantity { get; set; }
    public decimal SaleUnit { get; set; }
    public decimal TotalSale { get; set; }
    public int StateValue { get; set; }
    public string StateLabel { get; set; } = "";
    public string StateTone { get; set; } = "";
    public string ActionKey { get; set; } = "";
    public string ActionLabel { get; set; } = "";
    public bool HasAction { get; set; }
    public string Provider { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public decimal SupplierUnitCost { get; set; }
    public bool InvoiceHasClientPayment { get; set; }
    public string OdcDateValue { get; set; } = "";
    public string OdcDateDisplay { get; set; } = "";
    public string SupplierPaymentDateValue { get; set; } = "";
    public string SupplierPaymentDateDisplay { get; set; } = "";
    public string DeliveryRecordDateValue { get; set; } = "";
    public string DeliveryRecordDateDisplay { get; set; } = "";
    public bool HasOrderPurchase { get; set; }
    public string OrderPurchaseFileName { get; set; } = "";
    public bool HasProforma { get; set; }
    public string ProformaFileName { get; set; } = "";
    public bool HasSupplierPaymentProof { get; set; }
    public string SupplierPaymentProofFileName { get; set; } = "";
    public bool HasDeliveryRecord { get; set; }
    public string DeliveryRecordFileName { get; set; } = "";
    public string ModifiedOnDisplay { get; set; } = "";
}

public sealed class HardwareStageSaveRequest
{
    public string RecordId { get; set; } = "";
    public string ActionKey { get; set; } = "";
    public string OdcDateValue { get; set; } = "";
    public decimal? SupplierUnitCost { get; set; }
    public string Provider { get; set; } = "";
    public string SupplierPaymentDateValue { get; set; } = "";
    public string DeliveryRecordDateValue { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
}

public sealed class HardwareSaveResultDto
{
    public string Message { get; set; } = "";
    public HardwareBoardRowDto Record { get; set; } = new();
}

public sealed class HardwareFileUploadResultDto
{
    public string Message { get; set; } = "";
    public HardwareBoardRowDto Record { get; set; } = new();
}

public sealed class HardwareFileDownloadResult
{
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Content { get; set; } = Array.Empty<byte>();
}

public sealed class HardwareProvisioningSyncResultDto
{
    public string RequestId { get; set; } = "";
    public ProvisioningHardwareSyncStatus Status { get; set; } = ProvisioningHardwareSyncStatus.Pending;
    public int ImportedCount { get; set; }
    public string Message { get; set; } = "";
}

public sealed class HardwareCsvPreviewResultDto
{
    public string FileName { get; set; } = "";
    public string TableLogicalName { get; set; } = "";
    public string TableDisplayName { get; set; } = "";
    public string DetectedDelimiterLabel { get; set; } = "";
    public int TotalRows { get; set; }
    public int TotalColumns { get; set; }
    public int SystemColumnsCount { get; set; }
    public IReadOnlyList<string> SystemColumns { get; set; } = Array.Empty<string>();
    public IReadOnlyList<HardwareCsvColumnDto> Columns { get; set; } = Array.Empty<HardwareCsvColumnDto>();
    public string Message { get; set; } = "";
}

public sealed class HardwareCsvColumnDto
{
    public int Index { get; set; }
    public string SourceHeader { get; set; } = "";
    public string DisplayLabel { get; set; } = "";
    public string LogicalName { get; set; } = "";
    public string SchemaName { get; set; } = "";
    public string DataverseType { get; set; } = "";
    public string ExampleValue { get; set; } = "";
}

public sealed class HardwareProvisionResultDto
{
    public string Message { get; set; } = "";
    public string TableLogicalName { get; set; } = "";
    public string EntitySetName { get; set; } = "";
    public bool TableCreated { get; set; }
    public int CreatedColumnsCount { get; set; }
    public int ExistingColumnsCount { get; set; }
    public int ImportedCount { get; set; }
    public int SkippedDuplicatesCount { get; set; }
    public IReadOnlyList<string> CreatedColumns { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> ExistingColumns { get; set; } = Array.Empty<string>();
}
