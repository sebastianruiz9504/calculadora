using System.Security.Claims;
using CotizadorInterno.Web.Models;

namespace CotizadorInterno.Web.Models.Nomina;

public sealed class NominaPageViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
    public string InitialPeriodKey { get; set; } = "";
    public string SuggestedPaymentDateValue { get; set; } = "";
}

public class NominaPreviewRequest
{
    public string PeriodKey { get; set; } = "";
    public string PaymentDateValue { get; set; } = "";
    public List<NominaAdjustmentInput> Adjustments { get; set; } = new();
}

public sealed class NominaConfirmRequest : NominaPreviewRequest
{
    public bool Confirmed { get; set; }
}

public sealed class NominaAdjustmentInput
{
    public string EmployeeId { get; set; } = "";
    public decimal BonusCompliance { get; set; }
    public decimal OtherDeductions { get; set; }
    public decimal Loan { get; set; }
    public decimal PayrollWithholding { get; set; }
    public decimal ExternalWithholding { get; set; }
}

public sealed class NominaRowDto
{
    public string EmployeeId { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public string PeriodKey { get; set; } = "";
    public string PeriodLabel { get; set; } = "";
    public string PaymentDateValue { get; set; } = "";
    public string PaymentDateDisplay { get; set; } = "";
    public string Operation { get; set; } = "";
    public string ExistingPayrollRecordId { get; set; } = "";
    public int ExistingPayrollRecordCount { get; set; }
    public decimal SalaryBase { get; set; }
    public decimal Auxilio { get; set; }
    public decimal BonusCompliance { get; set; }
    public decimal CommissionsCopiers { get; set; }
    public decimal CommissionsCloud { get; set; }
    public decimal Commissions { get; set; }
    public decimal CommissionCap { get; set; }
    public decimal AppliedCommissionBase { get; set; }
    public decimal ContributionBase { get; set; }
    public decimal HealthRate { get; set; }
    public decimal PensionRate { get; set; }
    public decimal Health { get; set; }
    public decimal Pension { get; set; }
    public decimal OtherDeductions { get; set; }
    public decimal Loan { get; set; }
    public decimal PayrollWithholding { get; set; }
    public decimal CuentaDeCobro { get; set; }
    public decimal ExternalWithholding { get; set; }
    public decimal GrossSalary { get; set; }
    public decimal NetPayroll { get; set; }
    public decimal NetCuentaDeCobro { get; set; }
    public decimal FactorCopiers { get; set; }
    public decimal FactorCloud { get; set; }
    public decimal TotalCopiers { get; set; }
    public decimal TotalCloud { get; set; }
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}

public sealed class NominaVerticalSummaryDto
{
    public string VerticalKey { get; set; } = "";
    public string VerticalLabel { get; set; } = "";
    public decimal TotalAmount { get; set; }
}

public class NominaPreviewResultDto
{
    public string Message { get; set; } = "";
    public string PeriodKey { get; set; } = "";
    public string PeriodLabel { get; set; } = "";
    public string PaymentDateValue { get; set; } = "";
    public string PaymentDateDisplay { get; set; } = "";
    public int EmployeesCount { get; set; }
    public bool HasWarnings { get; set; }
    public decimal TotalPayrollAmount { get; set; }
    public decimal TotalCuentaCobroAmount { get; set; }
    public decimal TotalDisbursementAmount { get; set; }
    public decimal TotalCopiers { get; set; }
    public decimal TotalCloud { get; set; }
    public IReadOnlyList<NominaVerticalSummaryDto> VerticalSummaries { get; set; } = Array.Empty<NominaVerticalSummaryDto>();
    public IReadOnlyList<NominaRowDto> Rows { get; set; } = Array.Empty<NominaRowDto>();
    public IReadOnlyList<NominaProcessLogEntryDto> Logs { get; set; } = Array.Empty<NominaProcessLogEntryDto>();
}

public sealed class NominaConfirmResultDto : NominaPreviewResultDto
{
    public bool HasErrors { get; set; }
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
}

public sealed class NominaProcessLogEntryDto
{
    public string Level { get; set; } = "info";
    public string EmployeeId { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public string Operation { get; set; } = "";
    public string TableName { get; set; } = "";
    public string FieldName { get; set; } = "";
    public string RecordId { get; set; } = "";
    public string Message { get; set; } = "";
    public string Detail { get; set; } = "";
    public string OffendingValue { get; set; } = "";
    public string Suggestion { get; set; } = "";
}

public static class NominaAccessPolicy
{
    private static readonly HashSet<string> AllowedEmails = new(StringComparer.OrdinalIgnoreCase)
    {
        "msuarez@digitaltechcolombia.com",
        "adaza@digitaltechcolombia.com",
        "sruiz@digitaltechcolombia.com"
    };

    public static bool HasAccess(string? email) =>
        !string.IsNullOrWhiteSpace(email) && AllowedEmails.Contains(email.Trim());

    public static bool HasAccess(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return false;

        var candidateEmails = new[]
        {
            user.Identity?.Name,
            user.FindFirstValue("preferred_username"),
            user.FindFirstValue("upn"),
            user.FindFirstValue(ClaimTypes.Upn),
            user.FindFirstValue(ClaimTypes.Email),
            user.FindFirstValue("email")
        };

        return candidateEmails.Any(HasAccess);
    }
}
