namespace CotizadorInterno.Web.Models.Licenciamiento;

public sealed class LicenciamientoPageViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
}

public sealed class LicenciamientoBoardDto
{
    public IReadOnlyList<LicenciamientoRecordDto> Records { get; set; } = Array.Empty<LicenciamientoRecordDto>();
    public IReadOnlyList<LicenciamientoFacturaOptionDto> FacturaOptions { get; set; } = Array.Empty<LicenciamientoFacturaOptionDto>();
    public IReadOnlyList<LicenciamientoContractTypeOptionDto> ContractTypeOptions { get; set; } = Array.Empty<LicenciamientoContractTypeOptionDto>();
    public int TotalCount { get; set; }
    public decimal TotalUsd { get; set; }
    public decimal TotalCop { get; set; }
    public string Message { get; set; } = "";
}

public sealed class LicenciamientoRecordDto
{
    public string RecordId { get; set; } = "";
    public string CompanyAccountId { get; set; } = "";
    public string CompanyAccountDisplay { get; set; } = "";
    public string NombreCliente { get; set; } = "";
    public string Vendor { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string ProductDisplay { get; set; } = "";
    public int Days { get; set; }
    public string BillingInterval { get; set; } = "";
    public string FacturaValue { get; set; } = "";
    public string FacturaDisplay { get; set; } = "";
    public decimal ValorTotalUsd { get; set; }
    public decimal UnidadUsd { get; set; }
    public decimal Cantidad { get; set; }
    public decimal Trm { get; set; }
    public decimal PesosTotal { get; set; }
    public int ContractTypeValue { get; set; }
    public string ContractTypeLabel { get; set; } = "";
    public bool HasAccountLookup { get; set; }
    public bool HasProductLookup { get; set; }
}

public sealed class LicenciamientoFacturaOptionDto
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
    public int Count { get; set; }
}

public sealed class LicenciamientoContractTypeOptionDto
{
    public int Value { get; set; }
    public string Label { get; set; } = "";
}

public sealed class LicenciamientoPreviewResultDto
{
    public string FileName { get; set; } = "";
    public IReadOnlyList<LicenciamientoPreviewRowDto> Rows { get; set; } = Array.Empty<LicenciamientoPreviewRowDto>();
    public IReadOnlyList<LicenciamientoContractTypeOptionDto> ContractTypeOptions { get; set; } = Array.Empty<LicenciamientoContractTypeOptionDto>();
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int WarningRows { get; set; }
    public decimal TotalUsd { get; set; }
    public string Message { get; set; } = "";
}

public sealed class LicenciamientoPreviewRowDto
{
    public int SourceRowNumber { get; set; }
    public string CompanyAccountId { get; set; } = "";
    public string CompanyAccountLookupId { get; set; } = "";
    public string CompanyAccountLookupLabel { get; set; } = "";
    public bool CompanyAccountLookupFound { get; set; }
    public bool CompanyAccountLookupRequired { get; set; }
    public string CompanyAccountLookupFailureReason { get; set; } = "";
    public string NombreCliente { get; set; } = "";
    public string Vendor { get; set; } = "";
    public string ProductDescription { get; set; } = "";
    public string ProductLookupId { get; set; } = "";
    public string ProductLookupLabel { get; set; } = "";
    public bool ProductLookupFound { get; set; }
    public bool ProductLookupRequired { get; set; }
    public string ProductLookupFailureReason { get; set; } = "";
    public int Days { get; set; }
    public string BillingInterval { get; set; } = "";
    public string FacturaValue { get; set; } = "";
    public string FacturaDisplay { get; set; } = "";
    public decimal ValorTotalUsd { get; set; }
    public decimal UnidadUsd { get; set; }
    public decimal Cantidad { get; set; }
    public int ContractTypeValue { get; set; }
    public string ContractTypeLabel { get; set; } = "";
    public bool IsValid { get; set; } = true;
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public sealed class LicenciamientoImportRequestDto
{
    public List<LicenciamientoPreviewRowDto> Rows { get; set; } = new();
}

public sealed class LicenciamientoImportResultDto
{
    public string Message { get; set; } = "";
    public int CreatedCount { get; set; }
    public int SkippedCount { get; set; }
}

public sealed class LicenciamientoLookupItemDto
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string SearchField { get; set; } = "";
    public string MatchedValue { get; set; } = "";
}

public sealed class LicenciamientoAdjustTrmRequestDto
{
    public string FacturaValue { get; set; } = "";
    public decimal Trm { get; set; }
}

public sealed class LicenciamientoAdjustTrmResultDto
{
    public string Message { get; set; } = "";
    public int UpdatedCount { get; set; }
    public decimal Trm { get; set; }
    public decimal TotalUsd { get; set; }
    public decimal TotalCop { get; set; }
}

public sealed class LicenciamientoUpdateContractTypeRequestDto
{
    public List<string> RecordIds { get; set; } = new();
    public int ContractTypeValue { get; set; }
}

public sealed class LicenciamientoUpdateContractTypeResultDto
{
    public string Message { get; set; } = "";
    public int UpdatedCount { get; set; }
    public int ContractTypeValue { get; set; }
    public string ContractTypeLabel { get; set; } = "";
}
