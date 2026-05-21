using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.Dashboard)]
[Route("automation")]
public sealed class AutomationController : Controller
{
    private readonly ISiigoAccountCatalogSyncService _accountCatalogSyncService;
    private readonly IExpenseAccountingRuleService _expenseAccountingRuleService;
    private readonly IExpenseAccountingTemplateService _expenseAccountingTemplateService;
    private readonly ICashFlowImportService _cashFlowImportService;
    private readonly ICashFlowMatchingService _cashFlowMatchingService;

    public AutomationController(
        ISiigoAccountCatalogSyncService accountCatalogSyncService,
        IExpenseAccountingRuleService expenseAccountingRuleService,
        IExpenseAccountingTemplateService expenseAccountingTemplateService,
        ICashFlowImportService cashFlowImportService,
        ICashFlowMatchingService cashFlowMatchingService)
    {
        _accountCatalogSyncService = accountCatalogSyncService;
        _expenseAccountingRuleService = expenseAccountingRuleService;
        _expenseAccountingTemplateService = expenseAccountingTemplateService;
        _cashFlowImportService = cashFlowImportService;
        _cashFlowMatchingService = cashFlowMatchingService;
    }

    [HttpPost("siigo-account-catalog/sync")]
    public async Task<IActionResult> SyncSiigoAccountCatalog(
        [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate,
        CancellationToken ct)
    {
        try
        {
            var result = await _accountCatalogSyncService.SyncAsync(startDate, endDate, ct);
            return Json(new
            {
                startDate = result.StartDate.ToString("yyyy-MM-dd"),
                endDate = result.EndDate.ToString("yyyy-MM-dd"),
                observedAccounts = result.ObservedAccounts,
                created = result.Created,
                updated = result.Updated,
                unchanged = result.Unchanged
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("expense-accounting-rules/apply")]
    public async Task<IActionResult> ApplyExpenseAccountingRules(
        [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate,
        [FromQuery] string? movementType,
        [FromQuery] bool overwrite = false,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _expenseAccountingRuleService.ApplyAsync(startDate, endDate, movementType, overwrite, ct);
            return Json(new
            {
                startDate = result.StartDate.ToString("yyyy-MM-dd"),
                endDate = result.EndDate.ToString("yyyy-MM-dd"),
                movementType = result.MovementType,
                reviewed = result.Reviewed,
                updated = result.Updated,
                alreadyAssigned = result.AlreadyAssigned,
                noRule = result.NoRule,
                invalidRule = result.InvalidRule,
                rows = result.Rows.Take(250)
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("expense-accounting-templates/apply")]
    public async Task<IActionResult> ApplyExpenseAccountingTemplates(
        [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate,
        [FromQuery] string? movementType,
        [FromQuery] bool overwrite = false,
        [FromQuery] bool dryRun = false,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _expenseAccountingTemplateService.ApplyAsync(startDate, endDate, movementType, overwrite, dryRun, ct);
            return Json(new
            {
                startDate = result.StartDate.ToString("yyyy-MM-dd"),
                endDate = result.EndDate.ToString("yyyy-MM-dd"),
                movementType = result.MovementType,
                dryRun = result.DryRun,
                reviewed = result.Reviewed,
                updated = result.Updated,
                alreadyHandled = result.AlreadyHandled,
                noTemplate = result.NoTemplate,
                invalidTemplate = result.InvalidTemplate,
                generatedLineCount = result.GeneratedLineCount,
                rows = result.Rows.Take(250)
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("cash-flow/import")]
    public async Task<IActionResult> ImportCashFlow(
        [FromQuery] bool dryRun = true,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _cashFlowImportService.ImportAsync(dryRun, ct);
            return Json(new
            {
                dryRun = result.DryRun,
                rowsRead = result.RowsRead,
                movementsRead = result.MovementsRead,
                transfersRead = result.TransfersRead,
                skipped = result.Skipped,
                futureRowsSkipped = result.FutureRowsSkipped,
                created = result.Created,
                updated = result.Updated,
                unchanged = result.Unchanged,
                totalEntries = result.TotalEntries,
                totalExits = result.TotalExits,
                transferValue = result.TransferValue,
                flowSummaries = result.FlowSummaries,
                sampleRows = result.SampleRows.Take(50)
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("cash-flow/match-client-payments")]
    public async Task<IActionResult> MatchCashFlowClientPayments(
        [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate,
        [FromQuery] bool dryRun = true,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _cashFlowMatchingService.MatchClientPaymentsAsync(startDate, endDate, dryRun, ct);
            return Json(new
            {
                dryRun = result.DryRun,
                startDate = result.StartDate.ToString("yyyy-MM-dd"),
                endDate = result.EndDate.ToString("yyyy-MM-dd"),
                reviewedMovements = result.ReviewedMovements,
                candidateMovements = result.CandidateMovements,
                suggested = result.Suggested,
                pendingReview = result.PendingReview,
                noInvoiceToken = result.NoInvoiceToken,
                noInvoiceMatch = result.NoInvoiceMatch,
                ambiguousInvoice = result.AmbiguousInvoice,
                differenceOutOfTolerance = result.DifferenceOutOfTolerance,
                created = result.Created,
                updated = result.Updated,
                unchanged = result.Unchanged,
                skipped = result.Skipped,
                totalEntries = result.TotalEntries,
                suggestedEntries = result.SuggestedEntries,
                pendingReviewEntries = result.PendingReviewEntries,
                rows = result.Rows.Take(250)
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
