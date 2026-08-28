namespace CotizadorInterno.Web.Models.Nomina;

public sealed class NominaPaymentHistoryDto
{
    public int Year { get; set; }
    public string PeriodLabel { get; set; } = "";
    public bool HasData { get; set; }
    public int RecordsCount { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalPayroll { get; set; }
    public decimal TotalCuentaCobro { get; set; }
    public IReadOnlyList<NominaEmployeePaymentSummaryDto> EmployeeSummaries { get; set; } = Array.Empty<NominaEmployeePaymentSummaryDto>();
    public IReadOnlyList<NominaPaymentRecordDto> Records { get; set; } = Array.Empty<NominaPaymentRecordDto>();
}

public sealed class NominaEmployeePaymentSummaryDto
{
    public string EmployeeId { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public int RecordsCount { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalPayroll { get; set; }
    public decimal TotalCuentaCobro { get; set; }
    public decimal TotalCopiers { get; set; }
    public decimal TotalCloud { get; set; }
}

public sealed class NominaPaymentRecordDto
{
    public string RecordId { get; set; } = "";
    public string RecordName { get; set; } = "";
    public string EmployeeId { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public string PeriodKey { get; set; } = "";
    public string PaymentDateValue { get; set; } = "";
    public string PaymentDateDisplay { get; set; } = "";
    public decimal NetPayroll { get; set; }
    public decimal NetCuentaDeCobro { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal GrossSalary { get; set; }
    public decimal CuentaDeCobro { get; set; }
    public decimal Commissions { get; set; }
    public decimal SalaryBase { get; set; }
    public decimal TotalCopiers { get; set; }
    public decimal TotalCloud { get; set; }
}
