using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.Dashboard)]
public sealed class DashboardController : Controller
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    private readonly IDataverseService _dataverse;

    public DashboardController(IDataverseService dataverse)
    {
        _dataverse = dataverse;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var today = ResolveBogotaToday();
        var model = new DashboardPageViewModel
        {
            CurrentUser = await _dataverse.GetCurrentUserAsync(ct) ?? new CurrentUserInfo(),
            InitialYear = today.Year,
            InitialPeriodKind = BillingPeriodKind.Month,
            InitialPeriodValue = today.Month
        };

        return View(model);
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Billing([FromQuery] int? year, [FromQuery] string? period, [FromQuery] int? value, CancellationToken ct)
    {
        try
        {
            var today = ResolveBogotaToday();
            var dashboard = await _dataverse.GetBillingDashboardAsync(
                year ?? today.Year,
                BillingPeriodKindExtensions.ParseOrDefault(period),
                value,
                ct);

            return Json(dashboard);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar el dashboard de facturacion.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Portfolio(CancellationToken ct)
    {
        try
        {
            var dashboard = await _dataverse.GetPortfolioDashboardAsync(ct);
            return Json(dashboard);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar el dashboard de cartera.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Taxes([FromQuery] int? year, [FromQuery] string? period, [FromQuery] int? value, CancellationToken ct)
    {
        try
        {
            var today = ResolveBogotaToday();
            var dashboard = await _dataverse.GetTaxesDashboardAsync(
                year ?? today.Year,
                BillingPeriodKindExtensions.ParseOrDefault(period),
                value,
                ct);

            return Json(dashboard);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar el dashboard de impuestos.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Pnl([FromQuery] int? year, [FromQuery] int? month, [FromQuery] string? vertical, CancellationToken ct)
    {
        try
        {
            var today = ResolveBogotaToday();
            var dashboard = await _dataverse.GetPnlDashboardAsync(
                year ?? today.Year,
                month,
                vertical,
                ct);

            return Json(dashboard);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar el dashboard P&L.");
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> PnlDetail(
        [FromQuery] int? year,
        [FromQuery(Name = "cutoffMonth")] int? monthCutoff,
        [FromQuery] string? vertical,
        [FromQuery] string? rowKey,
        [FromQuery] int? cellMonth,
        CancellationToken ct)
    {
        try
        {
            var today = ResolveBogotaToday();
            var detail = await _dataverse.GetPnlCellDetailAsync(
                year ?? today.Year,
                monthCutoff,
                vertical,
                rowKey ?? "",
                cellMonth,
                ct);

            return Json(detail);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar el detalle de la celda P&L.");
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> PnlDetailRecord([FromBody] PnlDetailRecordUpdateRequestDto request, CancellationToken ct)
    {
        try
        {
            var result = await _dataverse.UpdatePnlDetailRecordAsync(request, ct);
            return Json(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible actualizar el registro del detalle P&L.");
        }
    }

    private static DateOnly ResolveBogotaToday()
    {
        var utcNow = DateTimeOffset.UtcNow;
        foreach (var timeZoneId in new[] { "SA Pacific Standard Time", "America/Bogota" })
        {
            try
            {
                var timezone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, timezone).DateTime);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return DateOnly.FromDateTime(utcNow.UtcDateTime);
    }
}
