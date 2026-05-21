using CotizadorInterno.Web.Models.Automation;

namespace CotizadorInterno.Web.Services;

public interface IExpenseAccountingTemplateService
{
    Task<ExpenseAccountingTemplateApplyResultDto> ApplyAsync(
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        string? movementType = null,
        bool overwrite = false,
        bool dryRun = false,
        CancellationToken ct = default);
}
