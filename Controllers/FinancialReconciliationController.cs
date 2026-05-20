using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.Dashboard)]
public sealed class FinancialReconciliationController : Controller
{
    private readonly IFinancialReconciliationService _reconciliationService;

    public FinancialReconciliationController(IFinancialReconciliationService reconciliationService)
    {
        _reconciliationService = reconciliationService;
    }

    [HttpGet]
    public async Task<IActionResult> Download([FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
    {
        try
        {
            var period = ResolvePeriod(year, month);
            var report = await _reconciliationService.BuildReportAsync(period.Year, period.Month, ct);
            return File(
                report.ExcelContent,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                report.FileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost]
    public async Task<IActionResult> Send([FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
    {
        try
        {
            var period = ResolvePeriod(year, month);
            var result = await _reconciliationService.RunAndSendAsync(period.Year, period.Month, ct);
            return Json(new
            {
                sent = result.EmailSent,
                status = result.EmailStatus,
                fileName = result.Report.FileName,
                period = result.Report.PeriodLabel,
                billingDifferences = result.Report.Summary.BillingDifferenceCount,
                expenseDifferences = result.Report.Summary.ExpenseDifferenceCount
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private static (int Year, int Month) ResolvePeriod(int? year, int? month)
    {
        if (year.HasValue || month.HasValue)
        {
            if (!year.HasValue || !month.HasValue)
                throw new InvalidOperationException("Periodo invalido. Usa year=YYYY y month=1..12.");

            if (year is < 2000 or > 2100 || month is < 1 or > 12)
                throw new InvalidOperationException("Periodo invalido. Usa year=YYYY y month=1..12.");

            return (year.Value, month.Value);
        }

        var timeZone = MonthlyFinancialReconciliationHostedService.ResolveTimeZone("SA Pacific Standard Time");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone).DateTime);
        var previousMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        return (previousMonth.Year, previousMonth.Month);
    }
}
