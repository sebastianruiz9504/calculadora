using CotizadorInterno.Web.Models.Automation;

namespace CotizadorInterno.Web.Services;

public interface IExpenseAccountingRuleService
{
    Task<ExpenseAccountingRuleApplyResultDto> ApplyAsync(
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        string? movementType = null,
        bool overwrite = false,
        CancellationToken ct = default);
}
