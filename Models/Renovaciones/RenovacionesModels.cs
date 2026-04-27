using CotizadorInterno.Web.Models;

namespace CotizadorInterno.Web.Models.Renovaciones;

public enum RenewalPeriodFilter
{
    ThisMonth = 0,
    PreviousMonth = 1,
    NextMonth = 2,
    All = 3,
    AllPast = 4
}

public static class RenewalPeriodFilterExtensions
{
    public static RenewalPeriodFilter ParseOrDefault(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return RenewalPeriodFilter.ThisMonth;

        return value.Trim().ToLowerInvariant() switch
        {
            "thismonth" or "this-month" or "este-mes" => RenewalPeriodFilter.ThisMonth,
            "previousmonth" or "previous-month" or "mes-anterior" => RenewalPeriodFilter.PreviousMonth,
            "nextmonth" or "next-month" or "mes-siguiente" => RenewalPeriodFilter.NextMonth,
            "all" or "todo" => RenewalPeriodFilter.All,
            "allpast" or "all-past" or "todas-las-pasadas" => RenewalPeriodFilter.AllPast,
            _ => RenewalPeriodFilter.ThisMonth
        };
    }

    public static string ToKey(this RenewalPeriodFilter value) => value switch
    {
        RenewalPeriodFilter.ThisMonth => "this-month",
        RenewalPeriodFilter.PreviousMonth => "previous-month",
        RenewalPeriodFilter.NextMonth => "next-month",
        RenewalPeriodFilter.All => "all",
        RenewalPeriodFilter.AllPast => "all-past",
        _ => "this-month"
    };

    public static string ToLabel(this RenewalPeriodFilter value) => value switch
    {
        RenewalPeriodFilter.ThisMonth => "Este mes",
        RenewalPeriodFilter.PreviousMonth => "Mes anterior",
        RenewalPeriodFilter.NextMonth => "Mes siguiente",
        RenewalPeriodFilter.All => "Todo",
        RenewalPeriodFilter.AllPast => "Todas las pasadas",
        _ => "Este mes"
    };
}

public sealed class RenewalBoardDto
{
    public string Filter { get; set; } = RenewalPeriodFilter.ThisMonth.ToKey();
    public string FilterLabel { get; set; } = RenewalPeriodFilter.ThisMonth.ToLabel();
    public int ClientsCount { get; set; }
    public int RecordsCount { get; set; }
    public decimal TotalContractValue { get; set; }
    public IReadOnlyList<RenewalClientGroupDto> Groups { get; set; } = Array.Empty<RenewalClientGroupDto>();
}

public sealed class RenewalClientGroupDto
{
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public int RecordCount { get; set; }
    public decimal ContractValue { get; set; }
    public IReadOnlyList<RenewalRecordDto> Records { get; set; } = Array.Empty<RenewalRecordDto>();
}

public sealed class RenewalRecordDto
{
    public string RecordId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int ProductLineOptionValue { get; set; }
    public int BusinessType { get; set; }
    public int Quantity { get; set; }
    public decimal UnitSaleUsd { get; set; }
    public bool HasVat { get; set; }
    public string RenewalDateValue { get; set; } = "";
    public string RenewalDateDisplay { get; set; } = "";
    public decimal ContractValue { get; set; }
    public string ClientLookupLogicalName { get; set; } = "";
    public string ProductLookupLogicalName { get; set; } = "";
}

public sealed class RenewalBatchUpdateRequest
{
    public List<RenewalRecordUpdateItem> Items { get; set; } = new();
}

public sealed class RenewalRecordUpdateItem
{
    public string RecordId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string OriginalClientId { get; set; } = "";
    public string OriginalProductId { get; set; } = "";
    public int ProductLineOptionValue { get; set; }
    public int BusinessType { get; set; }
    public int Quantity { get; set; }
    public decimal UnitSaleUsd { get; set; }
    public bool HasVat { get; set; }
    public string RenewalDateValue { get; set; } = "";
    public string ClientLookupLogicalName { get; set; } = "";
    public string ProductLookupLogicalName { get; set; } = "";
}

public sealed class RenewalScenarioCreateResultDto
{
    public string ScenarioId { get; set; } = "";
    public string ScenarioName { get; set; } = "";
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}

public sealed class RenovacionesPageViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
    public RenewalPeriodFilter InitialFilter { get; set; } = RenewalPeriodFilter.ThisMonth;
}
