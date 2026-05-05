using CotizadorInterno.Web.Models;

namespace CotizadorInterno.Web.Models.RebatesInversiones;

public sealed class RebatesInversionesPageViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
    public int InitialYear { get; set; }
}

public sealed class RebatesInversionesBoardDto
{
    public int SelectedYear { get; set; }
    public IReadOnlyList<int> AvailableYears { get; set; } = Array.Empty<int>();
    public IReadOnlyList<RebatesInversionesMonthSummaryDto> Months { get; set; } = Array.Empty<RebatesInversionesMonthSummaryDto>();
    public IReadOnlyList<RebatesInversionesRecordDto> Rebates { get; set; } = Array.Empty<RebatesInversionesRecordDto>();
    public IReadOnlyList<RebatesInversionesRecordDto> FinancialIncome { get; set; } = Array.Empty<RebatesInversionesRecordDto>();
    public decimal RebatesTotal { get; set; }
    public decimal FinancialIncomeTotal { get; set; }
    public int TotalCount { get; set; }
    public string Message { get; set; } = "";
}

public sealed class RebatesInversionesMonthSummaryDto
{
    public int Month { get; set; }
    public string Label { get; set; } = "";
    public decimal RebatesTotal { get; set; }
    public decimal FinancialIncomeTotal { get; set; }
    public int RebatesCount { get; set; }
    public int FinancialIncomeCount { get; set; }
}

public sealed class RebatesInversionesRecordDto
{
    public string RecordId { get; set; } = "";
    public string TypeKey { get; set; } = "";
    public string TypeLabel { get; set; } = "";
    public string DateValue { get; set; } = "";
    public string DateDisplay { get; set; } = "";
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthLabel { get; set; } = "";
    public decimal Value { get; set; }
    public string CreatedOnDisplay { get; set; } = "";
    public string ModifiedOnDisplay { get; set; } = "";
}

public sealed class RebatesInversionesSaveRequest
{
    public string RecordId { get; set; } = "";
    public string TypeKey { get; set; } = "";
    public string DateValue { get; set; } = "";
    public decimal Value { get; set; }
}

public sealed class RebatesInversionesSaveResultDto
{
    public string Message { get; set; } = "";
    public RebatesInversionesRecordDto Record { get; set; } = new();
}

public sealed class RebatesInversionesDeleteRequest
{
    public string RecordId { get; set; } = "";
}

public sealed class RebatesInversionesDeleteResultDto
{
    public string Message { get; set; } = "";
    public string RecordId { get; set; } = "";
}
