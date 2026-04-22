namespace CotizadorInterno.Web.Models.Dashboard;

public sealed class SiigoCustomerLookupItemDto
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Name { get; set; } = "";
    public string CommercialName { get; set; } = "";
    public string Identification { get; set; } = "";
    public int BranchOffice { get; set; }
    public bool Active { get; set; }
}

public sealed class SiigoInvoiceSearchResultDto
{
    public string CustomerId { get; set; } = "";
    public string CustomerDisplayName { get; set; } = "";
    public string CustomerIdentification { get; set; } = "";
    public int CustomerBranchOffice { get; set; }
    public string StartDateValue { get; set; } = "";
    public string EndDateValue { get; set; } = "";
    public string PeriodLabel { get; set; } = "";
    public bool HasData { get; set; }
    public int RecordsCount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal TotalBalance { get; set; }
    public string EmptyStateTitle { get; set; } = "";
    public string EmptyStateMessage { get; set; } = "";
    public IReadOnlyList<SiigoInvoiceRowDto> Invoices { get; set; } = Array.Empty<SiigoInvoiceRowDto>();
}

public sealed class SiigoInvoiceRowDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Prefix { get; set; } = "";
    public long? Number { get; set; }
    public string DateValue { get; set; } = "";
    public string DateDisplay { get; set; } = "";
    public string CustomerIdentification { get; set; } = "";
    public int CustomerBranchOffice { get; set; }
    public decimal Total { get; set; }
    public decimal Balance { get; set; }
    public string StampStatus { get; set; } = "";
    public bool Annulled { get; set; }
}

public sealed class SiigoInvoiceDownloadRequestDto
{
    public IReadOnlyList<SiigoInvoiceDownloadItemDto> Invoices { get; set; } = Array.Empty<SiigoInvoiceDownloadItemDto>();
}

public sealed class SiigoInvoiceDownloadItemDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class SiigoInvoiceDownloadResult
{
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public byte[] Content { get; set; } = Array.Empty<byte>();
}
