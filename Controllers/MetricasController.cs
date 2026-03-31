using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Metricas;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ServiceFilter(typeof(MetricasAccessFilter))]
public sealed class MetricasController : Controller
{
    private readonly IDataverseService _dataverse;
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";

    public MetricasController(IDataverseService dataverse)
    {
        _dataverse = dataverse;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var model = new MetricasPageViewModel
        {
            CurrentUser = await GetCurrentUserAsync(ct),
            InitialFilter = MetricsRangeFilter.ThisYear,
            InitialView = MetricsViewMode.Global
        };

        return View(model);
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Charts([FromQuery] string? filter, [FromQuery] string? view, [FromQuery] string? seller, CancellationToken ct)
    {
        try
        {
            var parsedFilter = MetricsRangeFilterExtensions.ParseOrDefault(filter);
            var parsedView = MetricsViewModeExtensions.ParseOrDefault(view);
            var dashboard = await _dataverse.GetMetricsDashboardAsync(parsedFilter, parsedView, seller, ct);
            return Json(dashboard);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "No fue posible cargar las metricas.");
        }
    }

    private async Task<CurrentUserInfo> GetCurrentUserAsync(CancellationToken ct)
    {
        if (HttpContext.Items.TryGetValue(MetricasAccessFilter.CurrentUserItemKey, out var cachedUser)
            && cachedUser is CurrentUserInfo currentUser)
        {
            return currentUser;
        }

        return await _dataverse.GetCurrentUserAsync(ct) ?? new CurrentUserInfo();
    }
}
