using CotizadorInterno.Web.Models;

namespace CotizadorInterno.Web.Models.RegistroPagosClientes;

public sealed class RegistroPagosClientesPageViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
}

public sealed class RegistroPagosClientesBoardDto
{
    public string AsOfDateLabel { get; set; } = "";
    public bool HasData { get; set; }
    public int RecordsCount { get; set; }
    public int PaidCount { get; set; }
    public int OverdueCount { get; set; }
    public int PendingCount { get; set; }
    public decimal TotalInvoiceValue { get; set; }
    public decimal TotalPaidValue { get; set; }
    public decimal TotalPendingValue { get; set; }
    public IReadOnlyList<RegistroPagosClientesInvoiceDto> Invoices { get; set; } = Array.Empty<RegistroPagosClientesInvoiceDto>();
}

public sealed class RegistroPagosClientesInvoiceDto
{
    public string RecordId { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public string EmissionDateValue { get; set; } = "";
    public string EmissionDateDisplay { get; set; } = "";
    public string DueDateValue { get; set; } = "";
    public string DueDateDisplay { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public decimal TotalInvoice { get; set; }
    public string PaymentStatusKey { get; set; } = "";
    public string PaymentStatusLabel { get; set; } = "";
    public string PaymentStatusTone { get; set; } = "";
    public int AgeDays { get; set; }
    public string PaymentDateValue { get; set; } = "";
    public string PaymentDateDisplay { get; set; } = "";
    public decimal PaymentValue { get; set; }
    public decimal VatValue { get; set; }
    public decimal ReteFtePercent { get; set; }
    public decimal ReteIcaPercent { get; set; }
    public decimal RteIvaPercent { get; set; }
    public decimal ReteFteValue { get; set; }
    public decimal ReteIcaValue { get; set; }
    public decimal RteIvaValue { get; set; }
    public decimal DifferenceValue { get; set; }
    public RegistroPagosClientesRetentionSuggestionDto Suggestion { get; set; } = new();
}

public sealed class RegistroPagosClientesRetentionSuggestionDto
{
    public bool HasSuggestion { get; set; }
    public int SourceCount { get; set; }
    public decimal AverageReteFtePercent { get; set; }
    public decimal AverageReteIcaPercent { get; set; }
    public decimal AverageRteIvaPercent { get; set; }
    public RegistroPagosClientesRetentionScenarioDto? LatestScenario { get; set; }
    public IReadOnlyList<RegistroPagosClientesRetentionScenarioDto> Scenarios { get; set; } = Array.Empty<RegistroPagosClientesRetentionScenarioDto>();
}

public sealed class RegistroPagosClientesRetentionScenarioDto
{
    public string InvoiceNumber { get; set; } = "";
    public string PaymentDateDisplay { get; set; } = "";
    public decimal TotalInvoice { get; set; }
    public decimal PaymentValue { get; set; }
    public decimal ReteFtePercent { get; set; }
    public decimal ReteIcaPercent { get; set; }
    public decimal RteIvaPercent { get; set; }
    public decimal DifferenceValue { get; set; }
}

public sealed class RegistroPagosClientesPaymentSaveRequest
{
    public string RecordId { get; set; } = "";
    public decimal PaymentValue { get; set; }
    public string PaymentDateValue { get; set; } = "";
    public decimal ReteFtePercent { get; set; }
    public decimal ReteIcaPercent { get; set; }
    public decimal RteIvaPercent { get; set; }
}

public sealed class RegistroPagosClientesPaymentSaveResult
{
    public string Message { get; set; } = "";
    public RegistroPagosClientesInvoiceDto Invoice { get; set; } = new();
}
