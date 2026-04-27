namespace CotizadorInterno.Web.Models.Hardware;

public sealed class HardwarePageViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
    public string TableLogicalName { get; set; } = "cr07a_hardware";
    public string TableDisplayName { get; set; } = "Hardware";
}

public sealed class HardwareWorkspaceViewModel
{
    public string RootId { get; set; } = "hardwareApp";
    public string Mode { get; set; } = "dashboard";
    public string CurrentUserLabel { get; set; } = "";
    public string PreviewUrl { get; set; } = "";
    public string ProvisionUrl { get; set; } = "";
    public string BoardUrl { get; set; } = "";
    public string CreateUrl { get; set; } = "";
    public string SaveUrl { get; set; } = "";
    public string UploadUrl { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string InvoiceSearchUrl { get; set; } = "";
    public string ClientSearchUrl { get; set; } = "";
    public string OwnerSearchUrl { get; set; } = "";
    public string EditUrl { get; set; } = "";
    public string InitialStartDate { get; set; } = "";
    public string InitialEndDate { get; set; } = "";
    public bool ShowHero { get; set; } = true;
    public string HeroKicker { get; set; } = "Operación";
    public string HeroTitle { get; set; } = "Hardware";
    public string HeroSubtitle { get; set; } =
        "Administra el ciclo completo de cada línea de hardware, desde la documentación inicial hasta el cierre por pago del cliente.";
    public string AccessLabel { get; set; } = "Acceso";
    public bool IsCommercialMode =>
        string.Equals(Mode, "commercial", StringComparison.OrdinalIgnoreCase);
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
    public string DateFilterStartValue { get; set; } = "";
    public string DateFilterEndValue { get; set; } = "";
    public string DateFilterLabel { get; set; } = "";
    public int TotalCount { get; set; }
    public int? SelectedStateValue { get; set; }
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public IReadOnlyList<HardwareStateOptionDto> StateOptions { get; set; } = Array.Empty<HardwareStateOptionDto>();
    public IReadOnlyList<HardwareStateSummaryDto> StateSummaries { get; set; } = Array.Empty<HardwareStateSummaryDto>();
    public IReadOnlyList<HardwareBoardRowDto> Rows { get; set; } = Array.Empty<HardwareBoardRowDto>();
}

public sealed class HardwareBoardRowDto
{
    public string RecordId { get; set; } = "";
    public string Name { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string OwnerName { get; set; } = "";
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
    public string PurchaseOrderNumber { get; set; } = "";
    public decimal SupplierUnitCost { get; set; }
    public decimal SupplierTotal { get; set; }
    public decimal FreightValue { get; set; }
    public decimal Utility { get; set; }
    public decimal MarginValue { get; set; }
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
    public List<string> RecordIds { get; set; } = new();
    public string ActionKey { get; set; } = "";
    public string PurchaseOrderNumber { get; set; } = "";
    public decimal? FreightValue { get; set; }
    public string OdcDateValue { get; set; } = "";
    public decimal? SupplierUnitCost { get; set; }
    public string Provider { get; set; } = "";
    public string SupplierPaymentDateValue { get; set; } = "";
    public string DeliveryRecordDateValue { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public List<HardwareDocumentationLineSaveRequest> DocumentationRows { get; set; } = new();
}

public sealed class HardwareDocumentationLineSaveRequest
{
    public string RecordId { get; set; } = "";
    public string OdcDateValue { get; set; } = "";
    public decimal? SupplierUnitCost { get; set; }
    public string Provider { get; set; } = "";
}

public sealed class HardwareBulkEditRequest
{
    public List<string> RecordIds { get; set; } = new();
    public bool OwnerChanged { get; set; }
    public string OwnerId { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public bool ClientChanged { get; set; }
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public bool QuantityChanged { get; set; }
    public int? Quantity { get; set; }
    public bool SaleUnitChanged { get; set; }
    public decimal? SaleUnit { get; set; }
    public bool TotalSaleChanged { get; set; }
    public decimal? TotalSale { get; set; }
    public bool StateChanged { get; set; }
    public int? StateValue { get; set; }
    public bool PurchaseOrderNumberChanged { get; set; }
    public string PurchaseOrderNumber { get; set; } = "";
    public bool OdcDateChanged { get; set; }
    public string OdcDateValue { get; set; } = "";
    public bool SupplierUnitCostChanged { get; set; }
    public decimal? SupplierUnitCost { get; set; }
    public bool SupplierTotalChanged { get; set; }
    public decimal? SupplierTotal { get; set; }
    public bool FreightValueChanged { get; set; }
    public decimal? FreightValue { get; set; }
    public bool UtilityChanged { get; set; }
    public decimal? Utility { get; set; }
    public bool MarginValueChanged { get; set; }
    public decimal? MarginValue { get; set; }
    public bool ProviderChanged { get; set; }
    public string Provider { get; set; } = "";
    public bool SupplierPaymentDateChanged { get; set; }
    public string SupplierPaymentDateValue { get; set; } = "";
    public bool DeliveryRecordDateChanged { get; set; }
    public string DeliveryRecordDateValue { get; set; } = "";
    public bool InvoiceNumberChanged { get; set; }
    public string InvoiceNumber { get; set; } = "";
}

public sealed class HardwareOrderCreateRequest
{
    public string PurchaseOrderNumber { get; set; } = "";
    public string OdcDateValue { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public List<HardwareOrderLineCreateRequest> Lines { get; set; } = new();
}

public sealed class HardwareOrderLineCreateRequest
{
    public string RowKey { get; set; } = "";
    public string Name { get; set; } = "";
    public int? Quantity { get; set; }
    public decimal? SupplierUnitCost { get; set; }
    public decimal? SaleUnit { get; set; }
    public string Provider { get; set; } = "";
}

public sealed class HardwareOrderCreateResultDto
{
    public string Message { get; set; } = "";
    public IReadOnlyList<HardwareBoardRowDto> Records { get; set; } = Array.Empty<HardwareBoardRowDto>();
}

public sealed class HardwareBulkEditResultDto
{
    public string Message { get; set; } = "";
    public IReadOnlyList<HardwareBoardRowDto> Records { get; set; } = Array.Empty<HardwareBoardRowDto>();
}

public sealed class HardwareSaveResultDto
{
    public string Message { get; set; } = "";
    public HardwareBoardRowDto Record { get; set; } = new();
    public IReadOnlyList<HardwareBoardRowDto> Records { get; set; } = Array.Empty<HardwareBoardRowDto>();
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
