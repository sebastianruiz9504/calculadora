using System.Security.Claims;
using CotizadorInterno.Web.Models;

namespace CotizadorInterno.Web.Models.Puntajes;

public enum ScorePeriodFilter
{
    ThisMonth = 0,
    PreviousMonth = 1,
    NextMonth = 2,
    ThisYear = 3
}

public static class ScorePeriodFilterExtensions
{
    public static ScorePeriodFilter ParseOrDefault(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ScorePeriodFilter.ThisMonth;

        return value.Trim().ToLowerInvariant() switch
        {
            "thismonth" or "this-month" or "este-mes" => ScorePeriodFilter.ThisMonth,
            "previousmonth" or "previous-month" or "mes-pasado" => ScorePeriodFilter.PreviousMonth,
            "nextmonth" or "next-month" or "mes-siguiente" => ScorePeriodFilter.NextMonth,
            "thisyear" or "this-year" or "este-ano" or "este-año" => ScorePeriodFilter.ThisYear,
            _ => ScorePeriodFilter.ThisMonth
        };
    }

    public static string ToKey(this ScorePeriodFilter value) => value switch
    {
        ScorePeriodFilter.ThisMonth => "this-month",
        ScorePeriodFilter.PreviousMonth => "previous-month",
        ScorePeriodFilter.NextMonth => "next-month",
        ScorePeriodFilter.ThisYear => "this-year",
        _ => "this-month"
    };

    public static string ToLabel(this ScorePeriodFilter value) => value switch
    {
        ScorePeriodFilter.ThisMonth => "Este mes",
        ScorePeriodFilter.PreviousMonth => "Mes pasado",
        ScorePeriodFilter.NextMonth => "Mes siguiente",
        ScorePeriodFilter.ThisYear => "Este año",
        _ => "Este mes"
    };
}

public sealed class PuntajesPageViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
    public ScorePeriodFilter InitialFilter { get; set; } = ScorePeriodFilter.ThisMonth;
    public IReadOnlyList<ScoreOptionItem> DealTypeOptions { get; set; } = Array.Empty<ScoreOptionItem>();
    public IReadOnlyList<ScoreOptionItem> FirstContractOptions { get; set; } = Array.Empty<ScoreOptionItem>();
    public IReadOnlyList<ScoreOptionItem> LineOptions { get; set; } = Array.Empty<ScoreOptionItem>();
    public IReadOnlyList<ScoreOptionItem> VerticalOptions { get; set; } = Array.Empty<ScoreOptionItem>();
    public IReadOnlyList<ScoreOptionItem> HasVatOptions { get; set; } = Array.Empty<ScoreOptionItem>();
    public IReadOnlyList<ScoreOptionItem> AutoBillOptions { get; set; } = Array.Empty<ScoreOptionItem>();
    public IReadOnlyList<ScoreOptionItem> ProductLineOptions { get; set; } = Array.Empty<ScoreOptionItem>();
    public IReadOnlyList<ScoreOptionItem> ContractTypeOptions { get; set; } = Array.Empty<ScoreOptionItem>();
}

public sealed class ScoreBoardDto
{
    public string Filter { get; set; } = ScorePeriodFilter.ThisMonth.ToKey();
    public string FilterLabel { get; set; } = ScorePeriodFilter.ThisMonth.ToLabel();
    public int ClientsCount { get; set; }
    public int RecordsCount { get; set; }
    public int ProductLinesCount { get; set; }
    public decimal TotalCommission { get; set; }
    public decimal TotalScore { get; set; }
    public decimal TotalMonthlyValue { get; set; }
    public decimal TotalValue { get; set; }
    public decimal TotalAnnualValue { get; set; }
    public int VerifiedRecordsCount { get; set; }
    public int ClosedRecordsCount { get; set; }
    public bool SupportsMonthClose { get; set; }
    public string MonthClosePeriodKey { get; set; } = "";
    public string MonthClosePeriodLabel { get; set; } = "";
    public IReadOnlyList<ScoreClientGroupDto> Groups { get; set; } = Array.Empty<ScoreClientGroupDto>();
}

public sealed class ScoreClientGroupDto
{
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string SalesPerson { get; set; } = "";
    public bool AllVerified { get; set; }
    public int RecordCount { get; set; }
    public int ProductLinesCount { get; set; }
    public decimal TotalCommission { get; set; }
    public decimal TotalScore { get; set; }
    public decimal TotalMonthlyValue { get; set; }
    public decimal TotalValue { get; set; }
    public decimal TotalAnnualValue { get; set; }
    public IReadOnlyList<ScoreRecordDto> Records { get; set; } = Array.Empty<ScoreRecordDto>();
}

public sealed class ScoreRecordDto
{
    public string RecordId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string ContractStartDateValue { get; set; } = "";
    public string ContractStartDateDisplay { get; set; } = "";
    public decimal Score { get; set; }
    public decimal Commission { get; set; }
    public string SalesPerson { get; set; } = "";
    public string Offer { get; set; } = "";
    public string OfferFileName { get; set; } = "";
    public bool HasOffer { get; set; }
    public bool IsVerified { get; set; }
    public int FirstContractOptionValue { get; set; }
    public int LineOptionValue { get; set; }
    public int VerticalOptionValue { get; set; }
    public string DescriptionClientName { get; set; } = "";
    public string ProvisioningDateValue { get; set; } = "";
    public string ProvisioningDateDisplay { get; set; } = "";
    public string ContractType { get; set; } = "";
    public string BusinessId { get; set; } = "";
    public string ProrationText { get; set; } = "";
    public int ProrationDays { get; set; }
    public decimal ProrationFactor { get; set; }
    public string RawDescription { get; set; } = "";
    public int ProductLinesCount { get; set; }
    public decimal MonthlyValue { get; set; }
    public decimal TotalValue { get; set; }
    public decimal AnnualValue { get; set; }
    public int BillingDay { get; set; }
    public string RenewalDateValue { get; set; } = "";
    public string RenewalDateDisplay { get; set; } = "";
    public string AlignmentDateValue { get; set; } = "";
    public string AlignmentDateDisplay { get; set; } = "";
    public int HasVatOptionValue { get; set; }
    public int AutoBillOptionValue { get; set; }
    public int ProductLineOptionValue { get; set; }
    public int ContractTypeOptionValue { get; set; }
    public int DealTypeValue { get; set; }
    public bool RequiresProration { get; set; }
    public string ScenarioStartDateValue { get; set; } = "";
    public string ScenarioEndDateValue { get; set; } = "";
    public bool IsClosedForActivePeriod { get; set; }
    public string ActivePeriodKey { get; set; } = "";
    public string LastVerifiedAtDisplay { get; set; } = "";
    public string LastVerifiedBy { get; set; } = "";
    public string LastClosedAtDisplay { get; set; } = "";
    public string LastClosedBy { get; set; } = "";
    public IReadOnlyList<ScoreProductLineDto> ProductLines { get; set; } = Array.Empty<ScoreProductLineDto>();
}

public sealed class ScoreProductLineDto
{
    public string LineId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string LineType { get; set; } = "";
    public int LineOptionValue { get; set; }
    public bool HasVat { get; set; }
    public int HasVatOptionValue { get; set; }
    public int Quantity { get; set; }
    public decimal CostUnit { get; set; }
    public decimal MarginPercent { get; set; }
    public int ContractMonths { get; set; }
    public decimal MonthlyUnitValue { get; set; }
    public decimal MonthlyValue { get; set; }
    public decimal TotalValue { get; set; }
    public decimal AnnualValue { get; set; }
}

public class ScoreVerificationRequest
{
    public string RecordId { get; set; } = "";
    public string BusinessId { get; set; } = "";
    public int DealTypeValue { get; set; }
    public bool RequiresProration { get; set; }
    public string ScenarioStartDateValue { get; set; } = "";
    public string ScenarioEndDateValue { get; set; } = "";
    public int FirstContractOptionValue { get; set; }
    public int LineOptionValue { get; set; }
    public int VerticalOptionValue { get; set; }
    public int BillingDay { get; set; }
    public string RenewalDateValue { get; set; } = "";
    public string AlignmentDateValue { get; set; } = "";
    public int HasVatOptionValue { get; set; }
    public int AutoBillOptionValue { get; set; }
    public int ProductLineOptionValue { get; set; }
    public int ContractTypeOptionValue { get; set; }
    public List<ScoreVerificationLineInput> Lines { get; set; } = new();
}

public sealed class ScoreVerificationLineInput
{
    public string LineId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string LineType { get; set; } = "";
    public int LineOptionValue { get; set; }
    public bool HasVat { get; set; }
    public int HasVatOptionValue { get; set; }
    public decimal CostUnit { get; set; }
    public decimal MarginPercent { get; set; }
    public int ContractMonths { get; set; } = 12;
    public int Quantity { get; set; } = 1;
    public decimal SuggestedRetailPrice { get; set; }
    public decimal Acelerador { get; set; }
    public decimal SaleUnit { get; set; }
    public decimal MonthlyValue { get; set; }
    public decimal TotalValue { get; set; }
}

public sealed class ScoreVerificationComputedResultDto
{
    public decimal Points { get; set; }
    public decimal Commission { get; set; }
    public int ProrationDays { get; set; }
    public decimal ProrationFactor { get; set; }
    public string ProrationText { get; set; } = "";
    public decimal TotalMonthlySale { get; set; }
    public decimal TotalSale { get; set; }
}

public sealed class ScoreVerificationDetailDto : ScoreVerificationRequest
{
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string SalesPerson { get; set; } = "";
    public string Offer { get; set; } = "";
    public string OfferFileName { get; set; } = "";
    public bool HasOffer { get; set; }
    public bool IsVerified { get; set; }
    public string ContractStartDateValue { get; set; } = "";
    public string ContractStartDateDisplay { get; set; } = "";
    public string ProvisioningDateValue { get; set; } = "";
    public string ProvisioningDateDisplay { get; set; } = "";
    public string ContractTypeLabel { get; set; } = "";
    public string ProrationSummary { get; set; } = "";
    public bool IsClosedForActivePeriod { get; set; }
    public string ActivePeriodKey { get; set; } = "";
    public string LastVerifiedAtDisplay { get; set; } = "";
    public string LastVerifiedBy { get; set; } = "";
    public string LastClosedAtDisplay { get; set; } = "";
    public string LastClosedBy { get; set; } = "";
    public string WarningMessage { get; set; } = "";
    public string RenewalMode { get; set; } = "";
    public string RenewalHint { get; set; } = "";
    public ScoreVerificationComputedResultDto? Result { get; set; }
}

public sealed class ScoreVerificationSaveResultDto
{
    public bool Ok { get; set; }
    public string Message { get; set; } = "";
    public ScoreVerificationComputedResultDto Result { get; set; } = new();
}

public sealed class ScoreMonthCloseRequest
{
    public string Filter { get; set; } = "";
}

public sealed class ScoreMonthCloseLogEntryDto
{
    public string Level { get; set; } = "info";
    public string RecordId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Message { get; set; } = "";
}

public sealed class ScoreMonthCloseResultDto
{
    public bool HasErrors { get; set; }
    public string Message { get; set; } = "";
    public string PeriodKey { get; set; } = "";
    public string PeriodLabel { get; set; } = "";
    public int ProcessedRecordsCount { get; set; }
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
    public int ErrorCount { get; set; }
    public IReadOnlyList<ScoreMonthCloseLogEntryDto> Logs { get; set; } = Array.Empty<ScoreMonthCloseLogEntryDto>();
}

public sealed class ScoreOptionItem
{
    public int Value { get; set; }
    public string Label { get; set; } = "";
}

public sealed class ScoreOfferDownloadResult
{
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public string RedirectUrl { get; set; } = "";
}

public static class PuntajesOptionCatalog
{
    public static IReadOnlyList<ScoreOptionItem> DealTypeOptions { get; } = new[]
    {
        new ScoreOptionItem { Value = 0, Label = "ClienteNuevo" },
        new ScoreOptionItem { Value = 1, Label = "CrossSale" },
        new ScoreOptionItem { Value = 2, Label = "Renovacion 1 vez" },
        new ScoreOptionItem { Value = 3, Label = "Renovacion 2 veces" },
        new ScoreOptionItem { Value = 4, Label = "Renovacion 3 veces o mas" }
    };

    public static IReadOnlyList<ScoreOptionItem> FirstContractOptions { get; } = new[]
    {
        new ScoreOptionItem { Value = 1, Label = "Si" },
        new ScoreOptionItem { Value = 2, Label = "No" }
    };

    public static IReadOnlyList<ScoreOptionItem> LineOptions { get; } = new[]
    {
        new ScoreOptionItem { Value = 645250000, Label = "ModernWork" },
        new ScoreOptionItem { Value = 645250001, Label = "Acronis" },
        new ScoreOptionItem { Value = 645250002, Label = "Azure" },
        new ScoreOptionItem { Value = 645250003, Label = "Copiers" },
        new ScoreOptionItem { Value = 645250005, Label = "Security" },
        new ScoreOptionItem { Value = 645250006, Label = "Servicios Profesionales" },
        new ScoreOptionItem { Value = 645250007, Label = "Perpetual" },
        new ScoreOptionItem { Value = 645250004, Label = "Otro" }
    };

    public static IReadOnlyList<ScoreOptionItem> VerticalOptions { get; } = new[]
    {
        new ScoreOptionItem { Value = 645250000, Label = "Cloud" },
        new ScoreOptionItem { Value = 645250001, Label = "Copiers" }
    };

    public static IReadOnlyList<ScoreOptionItem> HasVatOptions { get; } = new[]
    {
        new ScoreOptionItem { Value = 0, Label = "No" },
        new ScoreOptionItem { Value = 1, Label = "Si" }
    };

    public static IReadOnlyList<ScoreOptionItem> AutoBillOptions { get; } = new[]
    {
        new ScoreOptionItem { Value = 0, Label = "No" },
        new ScoreOptionItem { Value = 1, Label = "Si" }
    };

    public static IReadOnlyList<ScoreOptionItem> ProductLineOptions { get; } = new[]
    {
        new ScoreOptionItem { Value = 0, Label = "ModernWork" },
        new ScoreOptionItem { Value = 1, Label = "OutSourcing" },
        new ScoreOptionItem { Value = 2, Label = "Azure" },
        new ScoreOptionItem { Value = 3, Label = "Acronis" }
    };

    public static IReadOnlyList<ScoreOptionItem> ContractTypeOptions { get; } = new[]
    {
        new ScoreOptionItem { Value = 0, Label = "Monthly" },
        new ScoreOptionItem { Value = 1, Label = "Annual" }
    };
}

public static class PuntajesAccessPolicy
{
    private static readonly HashSet<string> AllowedEmails = new(StringComparer.OrdinalIgnoreCase)
    {
        "sruiz@digitaltechcolombia.com",
        "adaza@digitaltechcolombia.com"
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
