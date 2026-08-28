namespace CotizadorInterno.Web.Models.Calculator;

public sealed class CalculatorProposalViewModel
{
    public string GroupId { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string EconomicHash { get; set; } = "";
    public string LatestConfigurationJson { get; set; } = "";
    public string ScenarioId { get; set; } = "";
    public string CrmDealId { get; set; } = "";
    public string ScenarioName { get; set; } = "";
    public string PreparedByName { get; set; } = "";
    public string PreparedByEmail { get; set; } = "";
    public decimal TotalMonthlySale { get; set; }
    public decimal TotalContractSale { get; set; }
    public decimal TotalMonthlyVat { get; set; }
    public decimal TotalContractVat { get; set; }
    public IReadOnlyList<CalculatorProposalLineViewModel> Lines { get; set; } = [];
    public IReadOnlyList<CalculatorProposalPossibilityViewModel> Possibilities { get; set; } = [];
    public IReadOnlyList<ProposalExportHistoryItemDto> ExportHistory { get; set; } = [];
}

public sealed class CalculatorProposalPossibilityViewModel
{
    public string ScenarioId { get; set; } = "";
    public string Title { get; set; } = "";
    public int Order { get; set; }
    public bool IsRecommended { get; set; }
    public decimal TotalMonthlySale { get; set; }
    public decimal TotalContractSale { get; set; }
    public decimal TotalMonthlyVat { get; set; }
    public decimal TotalContractVat { get; set; }
    public IReadOnlyList<CalculatorProposalLineViewModel> Lines { get; set; } = [];
}

public sealed class CalculatorProposalLineViewModel
{
    public string Front { get; set; } = "";
    public string Description { get; set; } = "";
    public int Quantity { get; set; }
    public int ContractMonths { get; set; }
    public decimal UnitSale { get; set; }
    public decimal MonthlySale { get; set; }
    public decimal ContractSale { get; set; }
    public bool HasVat { get; set; }
    public decimal MonthlyVat { get; set; }
    public decimal ContractVat { get; set; }
    public decimal MonthlyTotalWithVat { get; set; }
    public decimal ContractTotalWithVat { get; set; }
}
