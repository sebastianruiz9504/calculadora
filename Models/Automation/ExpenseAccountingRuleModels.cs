namespace CotizadorInterno.Web.Models.Automation;

public sealed class ExpenseAccountingRuleApplyResultDto
{
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string MovementType { get; set; } = "";
    public int Reviewed { get; set; }
    public int Updated { get; set; }
    public int AlreadyAssigned { get; set; }
    public int NoRule { get; set; }
    public int InvalidRule { get; set; }
    public IReadOnlyList<ExpenseAccountingRuleAppliedRowDto> Rows { get; set; } = Array.Empty<ExpenseAccountingRuleAppliedRowDto>();
}

public sealed class ExpenseAccountingRuleAppliedRowDto
{
    public string ExpenseId { get; set; } = "";
    public string ExpenseName { get; set; } = "";
    public string ProviderNit { get; set; } = "";
    public string ProviderName { get; set; } = "";
    public string Category { get; set; } = "";
    public string RuleId { get; set; } = "";
    public string RuleName { get; set; } = "";
    public string AccountCode { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string Status { get; set; } = "";
    public string Notes { get; set; } = "";
}

public sealed class ExpenseAccountingTemplateApplyResultDto
{
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string MovementType { get; set; } = "";
    public bool DryRun { get; set; }
    public int Reviewed { get; set; }
    public int Updated { get; set; }
    public int AlreadyHandled { get; set; }
    public int NoTemplate { get; set; }
    public int InvalidTemplate { get; set; }
    public int GeneratedLineCount { get; set; }
    public IReadOnlyList<ExpenseAccountingTemplateAppliedRowDto> Rows { get; set; } = Array.Empty<ExpenseAccountingTemplateAppliedRowDto>();
}

public sealed class ExpenseAccountingTemplateAppliedRowDto
{
    public string ExpenseId { get; set; } = "";
    public string ExpenseName { get; set; } = "";
    public string ProviderNit { get; set; } = "";
    public string ProviderName { get; set; } = "";
    public string Category { get; set; } = "";
    public string TemplateId { get; set; } = "";
    public string TemplateName { get; set; } = "";
    public string Status { get; set; } = "";
    public string Notes { get; set; } = "";
    public decimal DebitTotal { get; set; }
    public decimal CreditTotal { get; set; }
    public IReadOnlyList<ExpenseAccountingTemplateGeneratedLineDto> Lines { get; set; } = Array.Empty<ExpenseAccountingTemplateGeneratedLineDto>();
}

public sealed class ExpenseAccountingTemplateGeneratedLineDto
{
    public int Order { get; set; }
    public string Side { get; set; } = "";
    public string AccountCode { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string Formula { get; set; } = "";
    public decimal Value { get; set; }
    public string Description { get; set; } = "";
}
